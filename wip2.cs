using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System;

namespace VRChatAvatarTools
{
    public class MeshColorEditorWindow : EditorWindow
    {
        // Language settings
        private enum Language { English, Japanese }
        private Language currentLanguage = Language.Japanese;
        
        // Target mesh
        private GameObject targetAvatar;
        private SkinnedMeshRenderer targetMeshRenderer;
        private Mesh targetMesh;
        private MeshCollider tempCollider;
        
        // Available SkinnedMeshRenderers
        private SkinnedMeshRenderer[] availableRenderers;
        private int selectedRendererIndex = -1;
        private Dictionary<SkinnedMeshRenderer, bool> originalRendererStates = new Dictionary<SkinnedMeshRenderer, bool>();
        
        // Selection
        private bool isSelectionMode = false;
        private bool isMultiSelectionMode = false;
        private HashSet<int> selectedVertices = new HashSet<int>();
        private List<int> selectedTriangles = new List<int>();
        
        // Selection Settings
        private bool limitToXAxis = false;
        private float xAxisThreshold = 0.0f;
        
        // Multiple selection support
        private List<MeshSelection> meshSelections = new List<MeshSelection>();
        private int activeSelectionIndex = -1;
        private Vector2 selectionScrollPos;
        
        // Editing
        private Color blendColor = new Color(1f, 0.5f, 0.5f, 1f);
        private float blendStrength = 1f;
        private enum BlendMode { Additive, Multiply, Color, Overlay }
        private BlendMode currentBlendMode = BlendMode.Color;
        
        // Original material and texture
        private Material originalMaterial;
        private Texture2D originalTexture;
        
        // Multiple materials support
        private Material[] availableMaterials;
        private Material[] originalMaterials; // 元のマテリアル配列全体を保存（変更されない）
        private Material[] workingMaterials; // 現在作業中のマテリアル配列
        private int selectedMaterialIndex = -1;
        
        // Safety component
        private MeshColorMaterialSafety currentSafety;
        private string windowGUID;
        
        // Preview
        private Material previewMaterial;
        private bool showPreview = true;
        private Texture2D previewTexture;
        private bool needsPreviewUpdate = false;
        
        // Debug information
        private string debugInfo = "";
        private Vector3 lastRaycastPoint;
        private bool lastRaycastHit = false;
        private bool showDebugInfo = false;
        
        [MenuItem("Tools/Mesh Color Editor")]
        public static void ShowWindow()
        {
            MeshColorEditorWindow window = GetWindow<MeshColorEditorWindow>();
            window.titleContent = new GUIContent("Mesh Color Editor");
            window.minSize = new Vector2(400, 600);
        }
        
        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            windowGUID = System.Guid.NewGuid().ToString();
        }
        
        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            
            // Restore original materials if needed
            if (targetMeshRenderer != null && originalMaterials != null)
            {
                if (originalMaterials.Length == 1)
                {
                    targetMeshRenderer.sharedMaterial = originalMaterials[0];
                    debugInfo += "[OnDisable] Restored single material\n";
                }
                else
                {
                    targetMeshRenderer.sharedMaterials = originalMaterials;
                    debugInfo += "[OnDisable] Restored multiple materials\n";
                }
            }
            
            CleanupPreview();
            RemoveTempCollider();
            RestoreAllMeshes();
            RemoveSafetyComponent();
        }
        
        private void ClearAvatarSelection()
        {
            // Restore original materials if needed
            if (targetMeshRenderer != null && originalMaterials != null)
            {
                debugInfo += "[Clear] === CLEAR AVATAR SELECTION DEBUG ===\n";
                debugInfo += "[Clear] Current materials before restore:\n";
                Material[] currentMats = targetMeshRenderer.sharedMaterials;
                for (int i = 0; i < currentMats.Length; i++)
                {
                    debugInfo += $"  Current[{i}] {(currentMats[i] != null ? currentMats[i].name : "null")} (HashCode: {(currentMats[i] != null ? currentMats[i].GetHashCode().ToString() : "null")})\n";
                }
                
                debugInfo += "[Clear] Original materials array contents:\n";
                for (int i = 0; i < originalMaterials.Length; i++)
                {
                    debugInfo += $"  Original[{i}] {(originalMaterials[i] != null ? originalMaterials[i].name : "null")} (HashCode: {(originalMaterials[i] != null ? originalMaterials[i].GetHashCode().ToString() : "null")})\n";
                }
                
                debugInfo += "[Clear] Working materials array contents:\n";
                if (workingMaterials != null)
                {
                    for (int i = 0; i < workingMaterials.Length; i++)
                    {
                        debugInfo += $"  Working[{i}] {(workingMaterials[i] != null ? workingMaterials[i].name : "null")} (HashCode: {(workingMaterials[i] != null ? workingMaterials[i].GetHashCode().ToString() : "null")})\n";
                    }
                }
                else
                {
                    debugInfo += "  Working materials array is NULL\n";
                }
                
                debugInfo += "[Clear] Available materials array contents:\n";
                if (availableMaterials != null)
                {
                    for (int i = 0; i < availableMaterials.Length; i++)
                    {
                        debugInfo += $"  Available[{i}] {(availableMaterials[i] != null ? availableMaterials[i].name : "null")} (HashCode: {(availableMaterials[i] != null ? availableMaterials[i].GetHashCode().ToString() : "null")})\n";
                    }
                }
                else
                {
                    debugInfo += "  Available materials array is NULL\n";
                }
                
                // Create a new array to avoid reference issues, copying from originalMaterials
                Material[] materialsToRestore = new Material[originalMaterials.Length];
                for (int i = 0; i < originalMaterials.Length; i++)
                {
                    materialsToRestore[i] = originalMaterials[i];
                    debugInfo += $"[Clear] Copying material {i}: {(originalMaterials[i] != null ? originalMaterials[i].name : "null")} -> {(materialsToRestore[i] != null ? materialsToRestore[i].name : "null")}\n";
                }
                
                debugInfo += "[Clear] About to restore materials array:\n";
                for (int i = 0; i < materialsToRestore.Length; i++)
                {
                    debugInfo += $"  ToRestore[{i}] {(materialsToRestore[i] != null ? materialsToRestore[i].name : "null")}\n";
                }
                
                // Use different restoration method based on material count
                if (materialsToRestore.Length == 1)
                {
                    debugInfo += "[Clear] Single material - using sharedMaterial\n";
                    targetMeshRenderer.sharedMaterial = materialsToRestore[0];
                }
                else
                {
                    debugInfo += "[Clear] Multiple materials - using sharedMaterials\n";
                    targetMeshRenderer.sharedMaterials = materialsToRestore;
                }
                
                debugInfo += "[Clear] Materials after restore:\n";
                Material[] restoredMats = targetMeshRenderer.sharedMaterials;
                for (int i = 0; i < restoredMats.Length; i++)
                {
                    debugInfo += $"  Restored[{i}] {(restoredMats[i] != null ? restoredMats[i].name : "null")} (HashCode: {(restoredMats[i] != null ? restoredMats[i].GetHashCode().ToString() : "null")})\n";
                }
                debugInfo += "[Clear] === END CLEAR DEBUG ===\n";
            }
            
            // Clean up everything
            CleanupPreview();
            RemoveTempCollider();
            RestoreAllMeshes();
            RemoveSafetyComponent();
            ClearAllSelections();
            
            // Clear references
            targetAvatar = null;
            targetMeshRenderer = null;
            targetMesh = null;
            originalMaterial = null;
            originalTexture = null;
            availableRenderers = null;
            selectedRendererIndex = -1;
            availableMaterials = null;
            originalMaterials = null;
            workingMaterials = null;
            selectedMaterialIndex = -1;
            originalRendererStates.Clear();
            debugInfo = "";
            
            // Reset selection mode
            isSelectionMode = false;
            Tools.current = Tool.Move;
        }
        
        private Vector2 mainScrollPosition;
        
        private void OnGUI()
        {
            EditorGUILayout.Space();
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(GetLocalizedText("title"), EditorStyles.boldLabel);
            
            GUILayout.FlexibleSpace();
            
            // Language tabs
            GUI.backgroundColor = currentLanguage == Language.English ? Color.cyan : Color.white;
            if (GUILayout.Button("EN", GUILayout.Width(30)))
            {
                currentLanguage = Language.English;
            }
            GUI.backgroundColor = currentLanguage == Language.Japanese ? Color.cyan : Color.white;
            if (GUILayout.Button("JP", GUILayout.Width(30)))
            {
                currentLanguage = Language.Japanese;
            }
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
            
            mainScrollPosition = EditorGUILayout.BeginScrollView(mainScrollPosition);
            
            DrawTargetSelection();
            
            if (targetMeshRenderer == null)
            {
                EditorGUILayout.EndScrollView();
                EditorGUILayout.HelpBox(GetLocalizedText("noRenderer"), MessageType.Info);
                return;
            }
            
            EditorGUILayout.Space();
            DrawSelectionMode();
            
            EditorGUILayout.Space();
            DrawSelectionList();
            
            EditorGUILayout.Space();
            DrawColorSettings();
            
            EditorGUILayout.Space();
            DrawActions();
            
            
            EditorGUILayout.Space();
            DrawDebugInfo();
            
            EditorGUILayout.EndScrollView();
            
            if (needsPreviewUpdate && showPreview && meshSelections.Count > 0)
            {
                UpdatePreview();
                needsPreviewUpdate = false;
            }
        }
        
        private void DrawTargetSelection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(GetLocalizedText("targetMesh"), EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            targetAvatar = EditorGUILayout.ObjectField(GetLocalizedText("avatar"), targetAvatar, typeof(GameObject), true) as GameObject;
            
            if (targetAvatar != null)
            {
                if (GUILayout.Button(GetLocalizedText("clear"), GUILayout.Width(50)))
                {
                    ClearAvatarSelection();
                }
            }
            EditorGUILayout.EndHorizontal();
            
            if (EditorGUI.EndChangeCheck() && targetAvatar != null)
            {
                availableRenderers = targetAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();
                
                originalRendererStates.Clear();
                foreach (var renderer in availableRenderers)
                {
                    originalRendererStates[renderer] = renderer.enabled;
                }
                
                if (availableRenderers.Length > 0)
                {
                    selectedRendererIndex = -1; // No default selection
                    targetMeshRenderer = null;
                    targetMesh = null;
                    originalMaterial = null;
                    originalTexture = null;
                }
                else
                {
                    targetMeshRenderer = null;
                    targetMesh = null;
                    originalMaterial = null;
                    originalTexture = null;
                    EditorUtility.DisplayDialog(GetLocalizedText("noSkinnedMeshRenderer"), 
                        GetLocalizedText("noSkinnedMeshRendererMsg"), GetLocalizedText("ok"));
                }
                
                ClearAllSelections();
            }
            
            if (targetAvatar != null && availableRenderers != null && availableRenderers.Length > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(GetLocalizedText("selectMesh"), EditorStyles.boldLabel);
                
                string[] rendererNames = new string[availableRenderers.Length + 1];
                rendererNames[0] = GetLocalizedText("selectMeshPrompt");
                for (int i = 0; i < availableRenderers.Length; i++)
                {
                    rendererNames[i + 1] = $"{i}: {availableRenderers[i].name}";
                    if (availableRenderers[i].sharedMesh != null)
                    {
                        rendererNames[i + 1] += $" ({availableRenderers[i].sharedMesh.name})";
                    }
                }
                
                EditorGUI.BeginChangeCheck();
                int displayIndex = selectedRendererIndex + 1; // +1 because of the prompt at index 0
                displayIndex = EditorGUILayout.Popup("Mesh Renderer", displayIndex, rendererNames);
                selectedRendererIndex = displayIndex - 1; // Convert back to actual index
                
                if (EditorGUI.EndChangeCheck() && selectedRendererIndex >= 0)
                {
                    SelectMeshRenderer(availableRenderers[selectedRendererIndex]);
                    ClearAllSelections();
                }
                
                EditorGUILayout.Space();
                bool hideOthers = EditorGUILayout.Toggle(GetLocalizedText("hideOtherMeshes"), IsOtherMeshesHidden());
                
                if (hideOthers)
                {
                    HideOtherMeshes();
                }
                else
                {
                    RestoreAllMeshes();
                }
            }
            
            if (targetMeshRenderer != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(GetLocalizedText("meshInfo"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(GetLocalizedText("mesh") + targetMesh.name);
                EditorGUILayout.LabelField(GetLocalizedText("vertices") + targetMesh.vertexCount);
                
                // Material selection if multiple materials exist
                if (availableMaterials != null && availableMaterials.Length > 1)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(GetLocalizedText("selectMaterial"), EditorStyles.boldLabel);
                    
                    string[] materialNames = new string[availableMaterials.Length + 1];
                    materialNames[0] = GetLocalizedText("selectMaterialPrompt");
                    for (int i = 0; i < availableMaterials.Length; i++)
                    {
                        materialNames[i + 1] = $"{i}: {(availableMaterials[i] != null ? availableMaterials[i].name : "None")}";
                    }
                    
                    EditorGUI.BeginChangeCheck();
                    int displayIndex = selectedMaterialIndex + 1; // +1 because of the prompt at index 0
                    displayIndex = EditorGUILayout.Popup(GetLocalizedText("material"), displayIndex, materialNames);
                    selectedMaterialIndex = displayIndex - 1; // Convert back to actual index
                    
                    if (EditorGUI.EndChangeCheck() && selectedMaterialIndex >= 0)
                    {
                        SelectMaterial(selectedMaterialIndex);
                        ClearAllSelections();
                    }
                }
                else
                {
                    EditorGUILayout.LabelField(GetLocalizedText("material") + (originalMaterial != null ? originalMaterial.name : "None"));
                }

                // if (originalTexture != null)
                // {
                //     // 絶対コピーするので出さない
                //     // bool isReadable = IsTextureReadable(originalTexture);
                //     // EditorGUILayout.LabelField(GetLocalizedText("textureReadable") + (isReadable ? GetLocalizedText("yes") : GetLocalizedText("no")), 
                //     //     isReadable ? EditorStyles.miniLabel : EditorStyles.miniBoldLabel);
                // }
                
                // if (tempCollider != null)
                // {
                //     EditorGUILayout.LabelField(GetLocalizedText("statusColliderReady"), EditorStyles.miniLabel);
                // }
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void SelectMeshRenderer(SkinnedMeshRenderer renderer)
        {
            // Restore the previous mesh's materials before switching
            if (targetMeshRenderer != null && workingMaterials != null)
            {
                if (workingMaterials.Length == 1)
                {
                    targetMeshRenderer.sharedMaterial = workingMaterials[0];
                    debugInfo += "[SelectRenderer] Restored previous single working material\n";
                }
                else
                {
                    targetMeshRenderer.sharedMaterials = workingMaterials;
                    debugInfo += "[SelectRenderer] Restored previous multiple working materials\n";
                }
                RemovePreview();
            }
            
            RemoveSafetyComponent();
            
            targetMeshRenderer = renderer;
            
            if (targetMeshRenderer != null)
            {
                targetMesh = targetMeshRenderer.sharedMesh;
                
                // Save original materials array (make a copy) - these NEVER change
                Material[] currentMaterials = targetMeshRenderer.sharedMaterials;
                
                debugInfo += $"[SelectRenderer] Current renderer materials:\n";
                for (int i = 0; i < currentMaterials.Length; i++)
                {
                    debugInfo += $"  Current[{i}] {(currentMaterials[i] != null ? currentMaterials[i].name : "null")} (HashCode: {(currentMaterials[i] != null ? currentMaterials[i].GetHashCode().ToString() : "null")})\n";
                }
                
                // IMPORTANT: Create completely separate arrays to avoid reference sharing
                originalMaterials = new Material[currentMaterials.Length];
                for (int i = 0; i < currentMaterials.Length; i++)
                {
                    originalMaterials[i] = currentMaterials[i];
                }
                
                // Create working materials array (this is what gets modified)
                workingMaterials = new Material[currentMaterials.Length];
                for (int i = 0; i < currentMaterials.Length; i++)
                {
                    workingMaterials[i] = currentMaterials[i];
                }
                
                // Create separate available materials array (no reference sharing)
                availableMaterials = new Material[currentMaterials.Length];
                for (int i = 0; i < currentMaterials.Length; i++)
                {
                    availableMaterials[i] = currentMaterials[i];
                }
                selectedMaterialIndex = -1; // Reset selection
                originalMaterial = null;
                originalTexture = null;
                
                debugInfo += $"[SelectRenderer] Saved original materials:\n";
                for (int i = 0; i < originalMaterials.Length; i++)
                {
                    debugInfo += $"  Original[{i}] {(originalMaterials[i] != null ? originalMaterials[i].name : "null")} (HashCode: {(originalMaterials[i] != null ? originalMaterials[i].GetHashCode().ToString() : "null")})\n";
                }
                
                debugInfo += $"[SelectRenderer] Working materials:\n";
                for (int i = 0; i < workingMaterials.Length; i++)
                {
                    debugInfo += $"  Working[{i}] {(workingMaterials[i] != null ? workingMaterials[i].name : "null")} (HashCode: {(workingMaterials[i] != null ? workingMaterials[i].GetHashCode().ToString() : "null")})\n";
                }
                
                // If only one material, select it automatically
                if (availableMaterials.Length == 1)
                {
                    SelectMaterial(0);
                }
                
                SetupTempCollider();
            }
        }
        
        private void SelectMaterial(int materialIndex)
        {
            if (availableMaterials == null || materialIndex < 0 || materialIndex >= availableMaterials.Length)
                return;
                
            // Restore the previous material if any (from working materials array)
            if (selectedMaterialIndex >= 0 && workingMaterials != null && selectedMaterialIndex < workingMaterials.Length)
            {
                Material[] materials = targetMeshRenderer.sharedMaterials;
                materials[selectedMaterialIndex] = workingMaterials[selectedMaterialIndex];
                
                if (materials.Length == 1)
                {
                    targetMeshRenderer.sharedMaterial = materials[0];
                }
                else
                {
                    targetMeshRenderer.sharedMaterials = materials;
                }
                debugInfo += $"[SelectMaterial] Restored working material at index {selectedMaterialIndex}\n";
                RemovePreview();
            }
            
            RemoveSafetyComponent();
            
            selectedMaterialIndex = materialIndex;
            originalMaterial = originalMaterials[materialIndex]; // Use original materials array for texture reference
            
            if (originalMaterial != null && originalMaterial.mainTexture != null)
            {
                originalTexture = originalMaterial.mainTexture as Texture2D;
                
                if (!IsTextureReadable(originalTexture))
                {
                    debugInfo += "Original texture is not readable. Will create a copy when needed.\n";
                }
            }
            else
            {
                originalTexture = null;
            }
            
            debugInfo += $"[SelectMaterial] Selected material {materialIndex}: {(originalMaterial != null ? originalMaterial.name : "null")}\n";
            
            SetupSafetyComponent();
        }
        
        private bool IsOtherMeshesHidden()
        {
            if (availableRenderers == null || targetMeshRenderer == null) return false;
            
            foreach (var renderer in availableRenderers)
            {
                if (renderer != targetMeshRenderer && renderer.enabled)
                {
                    return false;
                }
            }
            
            return true;
        }
        
        private void HideOtherMeshes()
        {
            if (availableRenderers == null || targetMeshRenderer == null) return;
            
            foreach (var renderer in availableRenderers)
            {
                if (renderer != targetMeshRenderer)
                {
                    renderer.enabled = false;
                }
                else
                {
                    renderer.enabled = true;
                }
            }
            
            SceneView.RepaintAll();
        }
        
        private void RestoreAllMeshes()
        {
            if (availableRenderers == null) return;
            
            foreach (var renderer in availableRenderers)
            {
                if (originalRendererStates.ContainsKey(renderer))
                {
                    renderer.enabled = originalRendererStates[renderer];
                }
                else
                {
                    renderer.enabled = true;
                }
            }
            
            SceneView.RepaintAll();
        }
        
        private void DrawSelectionMode()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(GetLocalizedText("meshSelection"), EditorStyles.boldLabel);
            
            // Selection mode button
            GUI.backgroundColor = isSelectionMode ? new Color(0.3f, 1f, 0.3f) : Color.white;
            if (GUILayout.Button(isSelectionMode ? GetLocalizedText("selectionModeOn") : GetLocalizedText("selectionModeOff"), GUILayout.Height(30)))
            {
                isSelectionMode = !isSelectionMode;
                SceneView.RepaintAll();
                
                if (isSelectionMode)
                {
                    Tools.current = Tool.None;
                    if (targetMeshRenderer != null && tempCollider == null)
                    {
                        SetupTempCollider();
                    }
                }
                else
                {
                    Tools.current = Tool.Move;
                }
            }
            GUI.backgroundColor = Color.white;
            
            if (isSelectionMode)
            {
                EditorGUILayout.HelpBox(GetLocalizedText("sceneViewHint"), MessageType.Info);
                
                EditorGUILayout.Space();
                isMultiSelectionMode = EditorGUILayout.Toggle(GetLocalizedText("multiSelectionMode"), isMultiSelectionMode);
                
                if (isMultiSelectionMode)
                {
                    EditorGUILayout.HelpBox(GetLocalizedText("clickAdd"), MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox(GetLocalizedText("clickNew"), MessageType.None);
                }
                
                // Selection Settings
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(GetLocalizedText("selectionSettings"), EditorStyles.miniBoldLabel);
                
                limitToXAxis = EditorGUILayout.Toggle(GetLocalizedText("limitToXAxis"), limitToXAxis);
                
                if (limitToXAxis)
                {
                    EditorGUI.indentLevel++;
                    xAxisThreshold = EditorGUILayout.FloatField(GetLocalizedText("xAxisCenter"), xAxisThreshold);
                    EditorGUILayout.HelpBox(string.Format(GetLocalizedText("xAxisHelp"), xAxisThreshold), MessageType.Info);
                    EditorGUI.indentLevel--;
                }
            }
            
            EditorGUILayout.LabelField(GetLocalizedText("totalSelectedVertices") + GetTotalSelectedVertices());
            
            if (GUILayout.Button(GetLocalizedText("clearAllSelections")))
            {
                ClearAllSelections();
            }
            
            // if (targetMeshRenderer != null && tempCollider == null)
            // {
            //     if (GUILayout.Button(GetLocalizedText("setupCollider")))
            //     {
            //         SetupTempCollider();
            //     }
            // }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawSelectionList()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(GetLocalizedText("meshSelections"), EditorStyles.boldLabel);
            
            if (meshSelections.Count == 0)
            {
                EditorGUILayout.LabelField(GetLocalizedText("noAreasSelected"), EditorStyles.miniLabel);
            }
            else
            {
                selectionScrollPos = EditorGUILayout.BeginScrollView(selectionScrollPos, GUILayout.Height(150));
                
                for (int i = 0; i < meshSelections.Count; i++)
                {
                    var selection = meshSelections[i];
                    
                    EditorGUILayout.BeginHorizontal();
                    
                    bool isActive = (i == activeSelectionIndex);
                    GUI.backgroundColor = isActive ? Color.cyan : Color.white;
                    
                    if (GUILayout.Button($"{GetLocalizedText("area")} {i + 1} ({selection.vertices.Count} {GetLocalizedText("verts")})", 
                        isActive ? EditorStyles.miniButtonMid : EditorStyles.miniButton))
                    {
                        SetActiveSelection(i);
                    }
                    
                    GUI.backgroundColor = Color.white;
                    
                    selection.isEnabled = EditorGUILayout.Toggle(selection.isEnabled, GUILayout.Width(20));
                    
                    GUI.backgroundColor = Color.red;
                    if (GUILayout.Button("X", GUILayout.Width(20)))
                    {
                        RemoveSelection(i);
                    }
                    GUI.backgroundColor = Color.white;
                    
                    EditorGUILayout.EndHorizontal();
                }
                
                EditorGUILayout.EndScrollView();
                EditorGUILayout.Space();
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawColorSettings()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(GetLocalizedText("colorSettings"), EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            blendColor = EditorGUILayout.ColorField(GetLocalizedText("blendColor"), blendColor);
            blendStrength = EditorGUILayout.Slider(GetLocalizedText("strength"), blendStrength, 0f, 1f);
            currentBlendMode = (BlendMode)EditorGUILayout.EnumPopup(GetLocalizedText("blendMode"), currentBlendMode);
            
            if (EditorGUI.EndChangeCheck())
            {
                needsPreviewUpdate = true;
            }
            
            EditorGUI.BeginChangeCheck();
            showPreview = EditorGUILayout.Toggle(GetLocalizedText("showPreview"), showPreview);
            
            if (EditorGUI.EndChangeCheck())
            {
                if (showPreview && meshSelections.Count > 0)
                {
                    needsPreviewUpdate = true;
                }
                else if (!showPreview)
                {
                    RemovePreview();
                }
            }
            
            EditorGUILayout.HelpBox(GetBlendModeDescription(), MessageType.Info);
            
            EditorGUILayout.EndVertical();
        }
        
        private string GetBlendModeDescription()
        {
            switch (currentBlendMode)
            {
                case BlendMode.Additive:
                    return GetLocalizedText("additiveDesc");
                case BlendMode.Multiply:
                    return GetLocalizedText("multiplyDesc");
                case BlendMode.Color:
                    return GetLocalizedText("colorDesc");
                case BlendMode.Overlay:
                    return GetLocalizedText("overlayDesc");
                default:
                    return "";
            }
        }
        
        private void DrawActions()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(GetLocalizedText("actions"), EditorStyles.boldLabel);
            
            GUI.enabled = meshSelections.Count > 0 && originalTexture != null;
            
            if (GUILayout.Button(GetLocalizedText("applyColor"), GUILayout.Height(30)))
            {
                ApplyColorToSelection();
            }
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(GetLocalizedText("exportMaskTexture"), GUILayout.Height(30)))
            {
                ExportMaskTexture();
            }
            
            if (GUILayout.Button(GetLocalizedText("exportTexture"), GUILayout.Height(30)))
            {
                ExportTexture();
            }
            EditorGUILayout.EndHorizontal();
            
            GUI.enabled = true;
            
            if (GUILayout.Button(GetLocalizedText("resetToOriginal")))
            {
                ResetToOriginal();
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(GetLocalizedText("materialSafetyHint"), MessageType.Info);
            
            EditorGUILayout.EndVertical();
        }
        
        
        private void DrawDebugInfo()
        {
            EditorGUILayout.BeginVertical("box");
            
            showDebugInfo = EditorGUILayout.Foldout(showDebugInfo, GetLocalizedText("debugInformation"), true, EditorStyles.foldoutHeader);
            
            if (showDebugInfo)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.TextArea(debugInfo, GUILayout.Height(100));
                
                if (GUILayout.Button(GetLocalizedText("clearDebug")))
                {
                    debugInfo = "";
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void OnSceneGUI(SceneView sceneView)
        {
            if (!isSelectionMode || targetMeshRenderer == null) return;
            
            Event e = Event.current;
            
            // Check if Alt key is held for camera navigation
            bool isAltHeld = e.alt;
            
            // Only take control if Alt is not pressed
            if (isSelectionMode && !isAltHeld)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                EditorGUIUtility.AddCursorRect(new Rect(0, 0, sceneView.position.width, sceneView.position.height), MouseCursor.CustomCursor);
            }
            
            if (e.type == EventType.MouseMove && isSelectionMode && !isAltHeld)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                debugInfo = $"Mouse Ray Origin: {ray.origin}\n";
                debugInfo += $"Mouse Ray Direction: {ray.direction}\n";
                if (this != null) Repaint();
            }
            
            // Only process LEFT clicks when Alt is not held (to allow camera rotation)
            // button == 0: left click, button == 1: right click, button == 2: middle click
            if (e.type == EventType.MouseDown && e.button == 0 && isSelectionMode && !isAltHeld)
            {
                bool isCtrlHeld = e.control;
                
                debugInfo += "\n=== CLICK EVENT ===\n";
                debugInfo += $"Multi Selection Mode: {isMultiSelectionMode}\n";
                debugInfo += $"Ctrl key: {isCtrlHeld}\n";
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                debugInfo += $"Click Position: {e.mousePosition}\n";
                debugInfo += $"Ray: {ray.origin} -> {ray.direction}\n";
                
                if (tempCollider != null && targetMeshRenderer != null)
                {
                    Mesh bakedMesh = new Mesh();
                    targetMeshRenderer.BakeMesh(bakedMesh);
                    tempCollider.sharedMesh = bakedMesh;
                    debugInfo += "Mesh baked for current pose\n";
                }
                
                RaycastHit hit;
                
                if (Physics.Raycast(ray, out hit))
                {
                    debugInfo += $"Physics.Raycast HIT: {hit.collider.gameObject.name}\n";
                    debugInfo += $"Hit Point: {hit.point}\n";
                    debugInfo += $"Hit Distance: {hit.distance}\n";
                    
                    if (hit.collider == tempCollider)
                    {
                        debugInfo += "HIT TEMP COLLIDER! Selecting area...\n";
                        SelectMeshArea(hit.point);
                        lastRaycastPoint = hit.point;
                        lastRaycastHit = true;
                    }
                    else if (hit.collider.gameObject == targetMeshRenderer.gameObject)
                    {
                        debugInfo += "HIT TARGET MESH! Selecting area...\n";
                        SelectMeshArea(hit.point);
                        lastRaycastPoint = hit.point;
                        lastRaycastHit = true;
                    }
                    else
                    {
                        debugInfo += $"Hit wrong object. Expected: {targetMeshRenderer.gameObject.name}\n";
                    }
                }
                else
                {
                    debugInfo += "Physics.Raycast MISSED\n";
                    
                    if (Physics.Raycast(ray, out hit, 1000f))
                    {
                        debugInfo += $"Long distance raycast HIT: {hit.collider.gameObject.name}\n";
                    }
                }
                
                if (tempCollider == null)
                {
                    debugInfo += "WARNING: Temp collider is NULL!\n";
                }
                else
                {
                    debugInfo += $"Temp collider exists: {tempCollider.gameObject.name}\n";
                    debugInfo += $"Collider enabled: {tempCollider.enabled}\n";
                    debugInfo += $"Mesh assigned: {tempCollider.sharedMesh != null}\n";
                }
                
                if (this != null) Repaint();
                e.Use();
            }
            
            // Only consume mouse up event when Alt is not held
            if (e.type == EventType.MouseUp && e.button == 0 && isSelectionMode && !isAltHeld)
            {
                e.Use();
            }
            
            if (showPreview)
            {
                DrawAllSelections();
            }
            
            if (lastRaycastHit)
            {
                Handles.color = Color.red;
                Handles.DrawWireCube(lastRaycastPoint, Vector3.one * 0.05f);
            }
        }
        
        private void SelectMeshArea(Vector3 hitPoint)
        {
            if (targetMesh == null) return;
            
            debugInfo += "\n--- SelectMeshArea CALLED ---\n";
            debugInfo += $"Hit Point: {hitPoint}\n";
            
            Event currentEvent = Event.current;
            bool isCtrlHeld = currentEvent != null ? currentEvent.control : false;
            
            Camera sceneCamera = SceneView.lastActiveSceneView?.camera;
            if (sceneCamera == null) return;
            
            Vector3 cameraPosition = sceneCamera.transform.position;
            Vector3 cameraForward = sceneCamera.transform.forward;
            
            Vector3[] vertices = targetMesh.vertices;
            Vector3[] normals = targetMesh.normals;
            Transform meshTransform = targetMeshRenderer.transform;
            
            debugInfo += $"Total vertices: {vertices.Length}\n";
            debugInfo += $"Camera position: {cameraPosition}\n";
            
            List<VertexCandidate> candidates = new List<VertexCandidate>();
            float threshold = 0.001f;
            
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPos = meshTransform.TransformPoint(vertices[i]);
                float distance = Vector3.Distance(worldPos, hitPoint);
                
                candidates.Add(new VertexCandidate
                {
                    index = i,
                    worldPosition = worldPos,
                    distance = distance
                });
            }
            
            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
            
            if (candidates.Count == 0) return;
            
            float minDistance = candidates[0].distance;
            List<VertexCandidate> closestVertices = new List<VertexCandidate>();
            
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].distance <= minDistance + threshold)
                {
                    closestVertices.Add(candidates[i]);
                }
                else
                {
                    break;
                }
            }
            
            debugInfo += $"Found {closestVertices.Count} vertices within threshold\n";
            
            int selectedVertex = -1;
            float bestDot = -1f;
            
            for (int i = 0; i < closestVertices.Count; i++)
            {
                int vertexIndex = closestVertices[i].index;
                Vector3 worldNormal = meshTransform.TransformDirection(normals[vertexIndex]).normalized;
                Vector3 toCameraDirection = (cameraPosition - closestVertices[i].worldPosition).normalized;
                
                float dot = Vector3.Dot(worldNormal, toCameraDirection);
                
                debugInfo += $"Vertex {vertexIndex}: dot = {dot:F3}\n";
                
                if (dot > bestDot)
                {
                    bestDot = dot;
                    selectedVertex = vertexIndex;
                }
            }
            
            debugInfo += $"Selected vertex: {selectedVertex} (dot: {bestDot:F3})\n";
            
            if (selectedVertex >= 0)
            {
                selectedVertices.Clear();
                selectedTriangles.Clear();
                
                // Determine which side of X-axis was clicked if limiting is enabled
                bool selectPositiveSide = true;
                if (limitToXAxis)
                {
                    Vector3 clickWorldPos = meshTransform.TransformPoint(vertices[selectedVertex]);
                    selectPositiveSide = clickWorldPos.x > xAxisThreshold;
                    debugInfo += $"Limiting to X-axis: clicked on {(selectPositiveSide ? "positive" : "negative")} side (X = {clickWorldPos.x:F3})\n";
                }
                
                HashSet<int> visited = new HashSet<int>();
                Queue<int> queue = new Queue<int>();
                
                queue.Enqueue(selectedVertex);
                visited.Add(selectedVertex);
                
                int[] triangles = targetMesh.triangles;
                debugInfo += $"Total triangles: {triangles.Length / 3}\n";
                
                int processedCount = 0;
                bool isFirstVertex = true;
                
                while (queue.Count > 0 && processedCount < 1000)
                {
                    processedCount++;
                    int currentVertex = queue.Dequeue();
                    
                    // Apply X-axis filtering if enabled
                    if (limitToXAxis)
                    {
                        Vector3 worldPos = meshTransform.TransformPoint(vertices[currentVertex]);
                        if (selectPositiveSide && worldPos.x <= xAxisThreshold)
                        {
                            continue;
                        }
                        else if (!selectPositiveSide && worldPos.x >= xAxisThreshold)
                        {
                            continue;
                        }
                    }
                    
                    selectedVertices.Add(currentVertex);
                    
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        bool containsVertex = false;
                        
                        for (int j = 0; j < 3; j++)
                        {
                            if (triangles[i + j] == currentVertex)
                            {
                                containsVertex = true;
                                break;
                            }
                        }
                        
                        if (containsVertex)
                        {
                            bool shouldIncludeTriangle = true;
                            
                            if (isFirstVertex)
                            {
                                Vector3 v0 = meshTransform.TransformPoint(vertices[triangles[i]]);
                                Vector3 v1 = meshTransform.TransformPoint(vertices[triangles[i + 1]]);
                                Vector3 v2 = meshTransform.TransformPoint(vertices[triangles[i + 2]]);
                                
                                Vector3 triangleNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                                Vector3 triangleCenter = (v0 + v1 + v2) / 3f;
                                Vector3 toCameraFromTriangle = (cameraPosition - triangleCenter).normalized;
                                
                                float triangleDot = Vector3.Dot(triangleNormal, toCameraFromTriangle);
                                
                                shouldIncludeTriangle = triangleDot > 0.1f;
                            }
                            
                            // Apply X-axis filtering to triangles as well
                            if (shouldIncludeTriangle && limitToXAxis)
                            {
                                bool allVerticesOnCorrectSide = true;
                                for (int j = 0; j < 3; j++)
                                {
                                    Vector3 vertexWorldPos = meshTransform.TransformPoint(vertices[triangles[i + j]]);
                                    if (selectPositiveSide && vertexWorldPos.x <= xAxisThreshold)
                                    {
                                        allVerticesOnCorrectSide = false;
                                        break;
                                    }
                                    else if (!selectPositiveSide && vertexWorldPos.x >= xAxisThreshold)
                                    {
                                        allVerticesOnCorrectSide = false;
                                        break;
                                    }
                                }
                                shouldIncludeTriangle = allVerticesOnCorrectSide;
                            }
                            
                            if (shouldIncludeTriangle)
                            {
                                selectedTriangles.Add(i / 3);
                                
                                for (int j = 0; j < 3; j++)
                                {
                                    int vertexIndex = triangles[i + j];
                                    if (!visited.Contains(vertexIndex))
                                    {
                                        visited.Add(vertexIndex);
                                        queue.Enqueue(vertexIndex);
                                    }
                                }
                            }
                        }
                    }
                    
                    isFirstVertex = false;
                }
                
                debugInfo += $"Selected vertices: {selectedVertices.Count}\n";
                debugInfo += $"Selected triangles: {selectedTriangles.Count}\n";
                debugInfo += $"Processed iterations: {processedCount}\n";
                
                if (isMultiSelectionMode)
                {
                    if (isCtrlHeld)
                    {
                        debugInfo += "Multi Mode: Removing from selection\n";
                        RemoveVerticesFromSelections(selectedVertices);
                    }
                    else
                    {
                        int overlappingSelectionIndex = FindOverlappingSelection(selectedVertices);
                        
                        if (overlappingSelectionIndex >= 0)
                        {
                            debugInfo += $"Multi Mode: Removing overlapping selection {overlappingSelectionIndex}\n";
                            RemoveSelection(overlappingSelectionIndex);
                        }
                        else
                        {
                            debugInfo += "Multi Mode: Adding to selection\n";
                            var newSelection = new MeshSelection
                            {
                                vertices = new HashSet<int>(selectedVertices),
                                triangles = new List<int>(selectedTriangles)
                            };
                            
                            meshSelections.Add(newSelection);
                            activeSelectionIndex = meshSelections.Count - 1;
                        }
                    }
                }
                else
                {
                    debugInfo += "Normal Mode: Replacing selection\n";
                    meshSelections.Clear();
                    
                    var newSelection = new MeshSelection
                    {
                        vertices = new HashSet<int>(selectedVertices),
                        triangles = new List<int>(selectedTriangles)
                    };
                    
                    meshSelections.Add(newSelection);
                    activeSelectionIndex = meshSelections.Count - 1;
                }
                
                debugInfo += $"Total selections: {meshSelections.Count}\n";
                
                if (showPreview)
                {
                    needsPreviewUpdate = true;
                }
                
                EditorApplication.delayCall += () => {
                    if (this != null) Repaint();
                };
                SceneView.RepaintAll();
            }
        }
        
        private void DrawAllSelections()
        {
            if (targetMeshRenderer == null) return;
            
            Transform meshTransform = targetMeshRenderer.transform;
            Vector3[] vertices = targetMesh.vertices;
            
            for (int i = 0; i < meshSelections.Count; i++)
            {
                var selection = meshSelections[i];
                bool isActive = (i == activeSelectionIndex);
                
                Color baseColor = Color.HSVToRGB((float)i / Mathf.Max(meshSelections.Count, 1f), 0.8f, 1f);
                baseColor.a = isActive ? 0.8f : 0.4f;
                Handles.color = baseColor;
                
                foreach (int vertexIndex in selection.vertices)
                {
                    if (vertexIndex < vertices.Length)
                    {
                        Vector3 worldPos = meshTransform.TransformPoint(vertices[vertexIndex]);
                        float size = isActive ? 0.004f : 0.002f;
                        Handles.SphereHandleCap(0, worldPos, Quaternion.identity, size, EventType.Repaint);
                    }
                }
            }
        }
        
        private void ApplyColorToSelection()
        {
            if (originalTexture == null || meshSelections.Count == 0) return;
            
            RemovePreview();
            
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string newTexturePath = $"Assets/kokoa/GeneratedTextures/{originalTexture.name}_edited_{timestamp}.png";
            
            if (!AssetDatabase.IsValidFolder("Assets/kokoa/GeneratedTextures"))
            {
                AssetDatabase.CreateFolder("Assets/kokoa", "GeneratedTextures");
            }
            
            Texture2D newTexture = CreateModifiedTextureWithAllSelections();
            
            if (newTexture == null)
            {
                EditorUtility.DisplayDialog(GetLocalizedText("error"), GetLocalizedText("textureCreateError"), GetLocalizedText("ok"));
                return;
            }
            
            byte[] pngData = newTexture.EncodeToPNG();
            System.IO.File.WriteAllBytes(newTexturePath, pngData);
            AssetDatabase.Refresh();
            
            TextureImporter importer = AssetImporter.GetAtPath(newTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = Mathf.Max(originalTexture.width, originalTexture.height);
                importer.SaveAndReimport();
            }
            
            Texture2D savedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(newTexturePath);
            
            string newMaterialPath = $"Assets/kokoa/GeneratedMaterials/{originalMaterial.name}_edited_{timestamp}.mat";
            
            if (!AssetDatabase.IsValidFolder("Assets/kokoa/GeneratedMaterials"))
            {
                AssetDatabase.CreateFolder("Assets/kokoa", "GeneratedMaterials");
            }
            
            Material newMaterial = new Material(originalMaterial);
            newMaterial.mainTexture = savedTexture;
            
            AssetDatabase.CreateAsset(newMaterial, newMaterialPath);
            AssetDatabase.SaveAssets();
            
            // Remove safety component temporarily to allow material change
            RemoveSafetyComponent();
            
            // Apply the new material to the correct slot
            if (selectedMaterialIndex >= 0 && availableMaterials != null && selectedMaterialIndex < availableMaterials.Length)
            {
                Material[] materials = targetMeshRenderer.sharedMaterials;
                materials[selectedMaterialIndex] = newMaterial;
                
                if (materials.Length == 1)
                {
                    targetMeshRenderer.sharedMaterial = materials[0];
                    debugInfo += $"[Apply] Applied new single material: {newMaterial.name}\n";
                }
                else
                {
                    targetMeshRenderer.sharedMaterials = materials;
                    debugInfo += $"[Apply] Applied new material to slot {selectedMaterialIndex}: {newMaterial.name}\n";
                }
                
                // Update working materials and original materials with the new applied material
                workingMaterials[selectedMaterialIndex] = newMaterial;
                availableMaterials[selectedMaterialIndex] = newMaterial;
                // IMPORTANT: Update originalMaterials so Safety component uses the new material as "original"
                originalMaterials[selectedMaterialIndex] = newMaterial;
            }
            else
            {
                targetMeshRenderer.sharedMaterial = newMaterial;
                // Update originalMaterials for single material case too
                if (originalMaterials != null && originalMaterials.Length > 0)
                {
                    originalMaterials[0] = newMaterial;
                }
            }
            
            // Update the original material reference to the new material
            originalMaterial = newMaterial;
            
            debugInfo += $"[Apply] Updated originalMaterials array with new applied material\n";
            debugInfo += $"[Apply] Updated originalMaterials contents:\n";
            for (int i = 0; i < originalMaterials.Length; i++)
            {
                debugInfo += $"[Apply]   originalMaterials[{i}]: {(originalMaterials[i] != null ? originalMaterials[i].name : "null")}\n";
            }
            
            // Recreate safety component with the new material as the "original"
            SetupSafetyComponent();
            
            ClearAllSelections();
            
            // Turn off selection mode after applying color
            isSelectionMode = false;
            Tools.current = Tool.Move;
            
            debugInfo += $"\nTexture saved: {newTexturePath}\n";
            debugInfo += $"Material saved: {newMaterialPath}\n";
            debugInfo += $"Material applied and Safety component updated\n";
            debugInfo += $"Selection mode turned OFF\n";
            
            // Repaint to update UI
            SceneView.RepaintAll();
        }
        
        private bool IsTextureReadable(Texture2D texture)
        {
            try
            {
                texture.GetPixel(0, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        private Texture2D GetReadableTexture(Texture2D source)
        {
            RenderTexture tmp = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            );
            
            Graphics.Blit(source, tmp);
            
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = tmp;
            
            Texture2D readableTexture = new Texture2D(source.width, source.height, TextureFormat.ARGB32, false);
            readableTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
            readableTexture.Apply();
            
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tmp);
            
            return readableTexture;
        }
        
        private Texture2D CreateModifiedTextureWithAllSelections()
        {
            Texture2D workingTexture;
            
            if (IsTextureReadable(originalTexture))
            {
                debugInfo += "Using original texture (readable)\n";
                workingTexture = new Texture2D(originalTexture.width, originalTexture.height, TextureFormat.ARGB32, false);
                workingTexture.SetPixels(originalTexture.GetPixels());
                workingTexture.Apply();
            }
            else
            {
                debugInfo += "Creating readable copy of texture\n";
                workingTexture = GetReadableTexture(originalTexture);
            }
            
            Vector2[] uvs = targetMesh.uv;
            
            foreach (var selection in meshSelections)
            {
                if (!selection.isEnabled) continue;
                
                HashSet<Vector2Int> paintedPixels = new HashSet<Vector2Int>();
                
                foreach (int triangleIndex in selection.triangles)
                {
                    int baseIndex = triangleIndex * 3;
                    int[] triangles = targetMesh.triangles;
                    
                    if (baseIndex + 2 < triangles.Length)
                    {
                        Vector2 uv0 = uvs[triangles[baseIndex]];
                        Vector2 uv1 = uvs[triangles[baseIndex + 1]];
                        Vector2 uv2 = uvs[triangles[baseIndex + 2]];
                        
                        PaintTriangleOnTextureWithColor(workingTexture, uv0, uv1, uv2, paintedPixels, blendColor, blendStrength);
                    }
                }
            }
            
            workingTexture.Apply();
            return workingTexture;
        }
        
        private void PaintTriangleOnTextureWithColor(Texture2D texture, Vector2 uv0, Vector2 uv1, Vector2 uv2, 
            HashSet<Vector2Int> paintedPixels, Color color, float strength)
        {
            int x0 = Mathf.RoundToInt(uv0.x * (texture.width - 1));
            int y0 = Mathf.RoundToInt(uv0.y * (texture.height - 1));
            int x1 = Mathf.RoundToInt(uv1.x * (texture.width - 1));
            int y1 = Mathf.RoundToInt(uv1.y * (texture.height - 1));
            int x2 = Mathf.RoundToInt(uv2.x * (texture.width - 1));
            int y2 = Mathf.RoundToInt(uv2.y * (texture.height - 1));
            
            int minX = Mathf.Max(0, Mathf.Min(x0, Mathf.Min(x1, x2)));
            int maxX = Mathf.Min(texture.width - 1, Mathf.Max(x0, Mathf.Max(x1, x2)));
            int minY = Mathf.Max(0, Mathf.Min(y0, Mathf.Min(y1, y2)));
            int maxY = Mathf.Min(texture.height - 1, Mathf.Max(y0, Mathf.Max(y1, y2)));
            
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Vector2Int pixelCoord = new Vector2Int(x, y);
                    
                    if (!paintedPixels.Contains(pixelCoord) && IsPointInTriangle(x, y, x0, y0, x1, y1, x2, y2))
                    {
                        paintedPixels.Add(pixelCoord);
                        Color originalColor = texture.GetPixel(x, y);
                        Color blendedColor = ApplyBlendMode(originalColor, color, currentBlendMode, strength);
                        texture.SetPixel(x, y, blendedColor);
                    }
                }
            }
        }
        
        private Color ApplyBlendMode(Color baseColor, Color blendColor, BlendMode mode, float strength)
        {
            Color result = baseColor;
            
            switch (mode)
            {
                case BlendMode.Additive:
                    result = baseColor + blendColor * strength;
                    break;
                    
                case BlendMode.Multiply:
                    result = Color.Lerp(baseColor, baseColor * blendColor, strength);
                    break;
                    
                case BlendMode.Color:
                    float baseLuminance = GetLuminance(baseColor);
                    Color colorized = SetLuminance(blendColor, baseLuminance);
                    result = Color.Lerp(baseColor, colorized, strength);
                    break;
                    
                case BlendMode.Overlay:
                    result = new Color(
                        OverlayChannel(baseColor.r, blendColor.r, strength),
                        OverlayChannel(baseColor.g, blendColor.g, strength),
                        OverlayChannel(baseColor.b, blendColor.b, strength),
                        baseColor.a
                    );
                    break;
            }
            
            result.r = Mathf.Clamp01(result.r);
            result.g = Mathf.Clamp01(result.g);
            result.b = Mathf.Clamp01(result.b);
            result.a = baseColor.a;
            
            return result;
        }
        
        private float GetLuminance(Color color)
        {
            return 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
        }
        
        private Color SetLuminance(Color color, float targetLuminance)
        {
            float currentLuminance = GetLuminance(color);
            
            if (currentLuminance <= 0.0001f)
            {
                return new Color(targetLuminance, targetLuminance, targetLuminance, color.a);
            }
            
            float scale = targetLuminance / currentLuminance;
            
            Color result = new Color(
                color.r * scale,
                color.g * scale,
                color.b * scale,
                color.a
            );
            
            float maxComponent = Mathf.Max(result.r, Mathf.Max(result.g, result.b));
            if (maxComponent > 1.0f)
            {
                float desaturation = (maxComponent - 1.0f) / (maxComponent - targetLuminance);
                result = Color.Lerp(result, new Color(targetLuminance, targetLuminance, targetLuminance, color.a), desaturation);
            }
            
            return result;
        }
        
        private float OverlayChannel(float baseValue, float blendValue, float strength)
        {
            float result;
            if (baseValue < 0.5f)
            {
                result = 2.0f * baseValue * blendValue;
            }
            else
            {
                result = 1.0f - 2.0f * (1.0f - baseValue) * (1.0f - blendValue);
            }
            return Mathf.Lerp(baseValue, result, strength);
        }
        
        private bool IsPointInTriangle(int px, int py, int x0, int y0, int x1, int y1, int x2, int y2)
        {
            float area = 0.5f * (-y1 * x2 + y0 * (-x1 + x2) + x0 * (y1 - y2) + x1 * y2);
            float s = 1 / (2 * area) * (y0 * x2 - x0 * y2 + (y2 - y0) * px + (x0 - x2) * py);
            float t = 1 / (2 * area) * (x0 * y1 - y0 * x1 + (y0 - y1) * px + (x1 - x0) * py);
            
            return s >= 0 && t >= 0 && (s + t) <= 1;
        }
        
        private void ExportMaskTexture()
        {
            if (originalTexture == null || meshSelections.Count == 0) return;
            
            RemovePreview();
            
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string maskTexturePath = $"Assets/kokoa/GeneratedTextures/{originalTexture.name}_mask_{timestamp}.png";
            
            if (!AssetDatabase.IsValidFolder("Assets/kokoa/GeneratedTextures"))
            {
                AssetDatabase.CreateFolder("Assets/kokoa", "GeneratedTextures");
            }
            
            Texture2D maskTexture = CreateMaskTexture();
            
            if (maskTexture == null)
            {
                EditorUtility.DisplayDialog(GetLocalizedText("error"), GetLocalizedText("textureCreateError"), GetLocalizedText("ok"));
                return;
            }
            
            byte[] pngData = maskTexture.EncodeToPNG();
            System.IO.File.WriteAllBytes(maskTexturePath, pngData);
            AssetDatabase.Refresh();
            
            TextureImporter importer = AssetImporter.GetAtPath(maskTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = Mathf.Max(originalTexture.width, originalTexture.height);
                importer.SaveAndReimport();
            }
            
            debugInfo += $"\nMask texture saved: {maskTexturePath}\n";
            
            EditorUtility.DisplayDialog(GetLocalizedText("maskExportComplete"), 
                string.Format(GetLocalizedText("maskExportMsg"), maskTexturePath), 
                GetLocalizedText("ok"));
        }
        
        private void ExportTexture()
        {
            if (originalTexture == null || meshSelections.Count == 0) return;
            
            RemovePreview();
            
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string texturePath = $"Assets/kokoa/GeneratedTextures/{originalTexture.name}_exported_{timestamp}.png";
            
            if (!AssetDatabase.IsValidFolder("Assets/kokoa/GeneratedTextures"))
            {
                AssetDatabase.CreateFolder("Assets/kokoa", "GeneratedTextures");
            }
            
            Texture2D exportTexture = CreateModifiedTextureWithAllSelections();
            
            if (exportTexture == null)
            {
                EditorUtility.DisplayDialog(GetLocalizedText("error"), GetLocalizedText("textureCreateError"), GetLocalizedText("ok"));
                return;
            }
            
            byte[] pngData = exportTexture.EncodeToPNG();
            System.IO.File.WriteAllBytes(texturePath, pngData);
            AssetDatabase.Refresh();
            
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = Mathf.Max(originalTexture.width, originalTexture.height);
                importer.SaveAndReimport();
            }
            
            debugInfo += $"\nTexture exported: {texturePath}\n";
            
            EditorUtility.DisplayDialog(GetLocalizedText("textureExportComplete"), 
                string.Format(GetLocalizedText("textureExportMsg"), texturePath), 
                GetLocalizedText("ok"));
        }
        
        private Texture2D CreateMaskTexture()
        {
            Texture2D workingTexture;
            
            if (IsTextureReadable(originalTexture))
            {
                debugInfo += "Creating mask texture based on original dimensions\n";
                workingTexture = new Texture2D(originalTexture.width, originalTexture.height, TextureFormat.ARGB32, false);
                
                Color[] blackPixels = new Color[originalTexture.width * originalTexture.height];
                for (int i = 0; i < blackPixels.Length; i++)
                {
                    blackPixels[i] = Color.black;
                }
                workingTexture.SetPixels(blackPixels);
            }
            else
            {
                debugInfo += "Creating mask texture using readable copy\n";
                Texture2D readableOriginal = GetReadableTexture(originalTexture);
                workingTexture = new Texture2D(readableOriginal.width, readableOriginal.height, TextureFormat.ARGB32, false);
                
                Color[] blackPixels = new Color[readableOriginal.width * readableOriginal.height];
                for (int i = 0; i < blackPixels.Length; i++)
                {
                    blackPixels[i] = Color.black;
                }
                workingTexture.SetPixels(blackPixels);
                
                DestroyImmediate(readableOriginal);
            }
            
            Vector2[] uvs = targetMesh.uv;
            
            foreach (var selection in meshSelections)
            {
                if (!selection.isEnabled) continue;
                
                HashSet<Vector2Int> paintedPixels = new HashSet<Vector2Int>();
                
                foreach (int triangleIndex in selection.triangles)
                {
                    int baseIndex = triangleIndex * 3;
                    int[] triangles = targetMesh.triangles;
                    
                    if (baseIndex + 2 < triangles.Length)
                    {
                        Vector2 uv0 = uvs[triangles[baseIndex]];
                        Vector2 uv1 = uvs[triangles[baseIndex + 1]];
                        Vector2 uv2 = uvs[triangles[baseIndex + 2]];
                        
                        PaintTriangleOnMask(workingTexture, uv0, uv1, uv2, paintedPixels);
                    }
                }
            }
            
            workingTexture.Apply();
            return workingTexture;
        }
        
        private void PaintTriangleOnMask(Texture2D texture, Vector2 uv0, Vector2 uv1, Vector2 uv2, HashSet<Vector2Int> paintedPixels)
        {
            int x0 = Mathf.RoundToInt(uv0.x * (texture.width - 1));
            int y0 = Mathf.RoundToInt(uv0.y * (texture.height - 1));
            int x1 = Mathf.RoundToInt(uv1.x * (texture.width - 1));
            int y1 = Mathf.RoundToInt(uv1.y * (texture.height - 1));
            int x2 = Mathf.RoundToInt(uv2.x * (texture.width - 1));
            int y2 = Mathf.RoundToInt(uv2.y * (texture.height - 1));
            
            int minX = Mathf.Max(0, Mathf.Min(x0, Mathf.Min(x1, x2)));
            int maxX = Mathf.Min(texture.width - 1, Mathf.Max(x0, Mathf.Max(x1, x2)));
            int minY = Mathf.Max(0, Mathf.Min(y0, Mathf.Min(y1, y2)));
            int maxY = Mathf.Min(texture.height - 1, Mathf.Max(y0, Mathf.Max(y1, y2)));
            
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Vector2Int pixelCoord = new Vector2Int(x, y);
                    
                    if (!paintedPixels.Contains(pixelCoord) && IsPointInTriangle(x, y, x0, y0, x1, y1, x2, y2))
                    {
                        paintedPixels.Add(pixelCoord);
                        texture.SetPixel(x, y, Color.white);
                    }
                }
            }
        }
        
        private void ResetToOriginal()
        {
            if (originalMaterials != null && targetMeshRenderer != null)
            {
                debugInfo += "[Reset] Resetting to original materials:\n";
                for (int i = 0; i < originalMaterials.Length; i++)
                {
                    debugInfo += $"  [{i}] {(originalMaterials[i] != null ? originalMaterials[i].name : "null")}\n";
                }
                
                if (originalMaterials.Length == 1)
                {
                    targetMeshRenderer.sharedMaterial = originalMaterials[0];
                }
                else
                {
                    targetMeshRenderer.sharedMaterials = originalMaterials;
                }
                
                // Reset working materials to original state
                workingMaterials = new Material[originalMaterials.Length];
                for (int i = 0; i < originalMaterials.Length; i++)
                {
                    workingMaterials[i] = originalMaterials[i];
                }
                availableMaterials = workingMaterials;
                
                // Clear selections and reset material selection
                ClearAllSelections();
                selectedMaterialIndex = -1;
                originalMaterial = null;
                originalTexture = null;
                
                RemovePreview();
                debugInfo += "[Reset] Reset to original materials complete\n";
            }
        }
        
        
        private void SetupTempCollider()
        {
            if (targetMeshRenderer == null || targetMesh == null) return;
            
            RemoveTempCollider();
            
            debugInfo += "\n=== SETUP TEMP COLLIDER ===\n";
            
            GameObject tempObject = new GameObject("TempMeshCollider");
            tempObject.transform.SetParent(targetMeshRenderer.transform, false);
            tempObject.layer = targetMeshRenderer.gameObject.layer;
            
            debugInfo += $"Created temp object: {tempObject.name}\n";
            
            tempCollider = tempObject.AddComponent<MeshCollider>();
            
            Mesh bakedMesh = new Mesh();
            targetMeshRenderer.BakeMesh(bakedMesh);
            tempCollider.sharedMesh = bakedMesh;
            
            debugInfo += $"Baked mesh vertices: {bakedMesh.vertexCount}\n";
            debugInfo += $"Collider enabled: {tempCollider.enabled}\n";
            
            tempObject.hideFlags = HideFlags.HideAndDontSave;
            
            debugInfo += "Temporary collider created for raycasting\n";
            
            EditorApplication.delayCall += () => {
                if (this != null) Repaint();
            };
        }
        
        private void RemoveTempCollider()
        {
            if (tempCollider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(tempCollider.gameObject);
                }
                else
                {
                    DestroyImmediate(tempCollider.gameObject);
                }
                tempCollider = null;
            }
        }
        
        private void UpdatePreview()
        {
            if (targetMeshRenderer == null || originalTexture == null) return;
            
            if (previewMaterial == null)
            {
                previewMaterial = new Material(originalMaterial);
                previewMaterial.name = "Mesh Color Editor Preview";
            }
            
            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
            }
            
            previewTexture = CreateModifiedTextureWithAllSelections();
            previewMaterial.mainTexture = previewTexture;
            
            // Apply preview to the correct material slot
            if (selectedMaterialIndex >= 0 && availableMaterials != null && selectedMaterialIndex < availableMaterials.Length)
            {
                Material[] materials = targetMeshRenderer.sharedMaterials;
                materials[selectedMaterialIndex] = previewMaterial;
                
                if (materials.Length == 1)
                {
                    targetMeshRenderer.sharedMaterial = materials[0];
                }
                else
                {
                    targetMeshRenderer.sharedMaterials = materials;
                }
            }
            else
            {
                targetMeshRenderer.sharedMaterial = previewMaterial;
            }
            
            debugInfo += "Preview updated\n";
        }
        
        private void RemovePreview()
        {
            if (targetMeshRenderer != null && workingMaterials != null && selectedMaterialIndex >= 0)
            {
                Material[] materials = targetMeshRenderer.sharedMaterials;
                if (selectedMaterialIndex < workingMaterials.Length)
                {
                    materials[selectedMaterialIndex] = workingMaterials[selectedMaterialIndex];
                    
                    if (materials.Length == 1)
                    {
                        targetMeshRenderer.sharedMaterial = materials[0];
                    }
                    else
                    {
                        targetMeshRenderer.sharedMaterials = materials;
                    }
                    debugInfo += $"[RemovePreview] Restored working material at index {selectedMaterialIndex}\n";
                }
            }
            
            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
                previewTexture = null;
            }
        }
        
        private void ClearAllSelections()
        {
            meshSelections.Clear();
            activeSelectionIndex = -1;
            selectedVertices.Clear();
            selectedTriangles.Clear();
            RemovePreview();
            SceneView.RepaintAll();
        }
        
        private void CleanupPreview()
        {
            RemovePreview();
            
            if (previewMaterial != null)
            {
                DestroyImmediate(previewMaterial);
                previewMaterial = null;
            }
        }
        
        private int GetTotalSelectedVertices()
        {
            HashSet<int> allVertices = new HashSet<int>();
            foreach (var selection in meshSelections)
            {
                allVertices.UnionWith(selection.vertices);
            }
            return allVertices.Count;
        }
        
        private void SetActiveSelection(int index)
        {
            if (index >= 0 && index < meshSelections.Count)
            {
                activeSelectionIndex = index;
                var selection = meshSelections[index];
                selectedVertices = new HashSet<int>(selection.vertices);
                selectedTriangles = new List<int>(selection.triangles);
                
                if (showPreview)
                {
                    needsPreviewUpdate = true;
                }
                
                SceneView.RepaintAll();
            }
        }
        
        private void RemoveSelection(int index)
        {
            if (index >= 0 && index < meshSelections.Count)
            {
                meshSelections.RemoveAt(index);
                
                if (activeSelectionIndex >= meshSelections.Count)
                {
                    activeSelectionIndex = meshSelections.Count - 1;
                }
                
                if (activeSelectionIndex >= 0)
                {
                    SetActiveSelection(activeSelectionIndex);
                }
                else
                {
                    selectedVertices.Clear();
                    selectedTriangles.Clear();
                    RemovePreview();
                }
                
                SceneView.RepaintAll();
            }
        }
        
        private int FindOverlappingSelection(HashSet<int> currentVertices)
        {
            float overlapThreshold = 0.7f;
            
            for (int i = 0; i < meshSelections.Count; i++)
            {
                var existingSelection = meshSelections[i];
                
                HashSet<int> intersection = new HashSet<int>(existingSelection.vertices);
                intersection.IntersectWith(currentVertices);
                
                float overlapWithExisting = (float)intersection.Count / existingSelection.vertices.Count;
                float overlapWithCurrent = (float)intersection.Count / currentVertices.Count;
                
                if (overlapWithExisting >= overlapThreshold || overlapWithCurrent >= overlapThreshold)
                {
                    debugInfo += $"Found overlapping selection {i}: existing={overlapWithExisting:F2}, current={overlapWithCurrent:F2}\n";
                    return i;
                }
            }
            
            return -1;
        }
        
        private void RemoveVerticesFromSelections(HashSet<int> verticesToRemove)
        {
            debugInfo += $"Removing {verticesToRemove.Count} vertices from selections\n";
            
            for (int i = meshSelections.Count - 1; i >= 0; i--)
            {
                var selection = meshSelections[i];
                
                selection.vertices.ExceptWith(verticesToRemove);
                
                int[] triangles = targetMesh.triangles;
                for (int j = selection.triangles.Count - 1; j >= 0; j--)
                {
                    int triangleIndex = selection.triangles[j];
                    int baseIndex = triangleIndex * 3;
                    
                    if (baseIndex + 2 < triangles.Length)
                    {
                        bool containsRemovedVertex = false;
                        for (int k = 0; k < 3; k++)
                        {
                            if (verticesToRemove.Contains(triangles[baseIndex + k]))
                            {
                                containsRemovedVertex = true;
                                break;
                            }
                        }
                        
                        if (containsRemovedVertex)
                        {
                            selection.triangles.RemoveAt(j);
                        }
                    }
                }
                
                if (selection.vertices.Count == 0)
                {
                    meshSelections.RemoveAt(i);
                    if (activeSelectionIndex == i)
                    {
                        activeSelectionIndex = -1;
                    }
                    else if (activeSelectionIndex > i)
                    {
                        activeSelectionIndex--;
                    }
                }
            }
            
            if (activeSelectionIndex >= meshSelections.Count)
            {
                activeSelectionIndex = meshSelections.Count - 1;
            }
            
            if (activeSelectionIndex >= 0)
            {
                SetActiveSelection(activeSelectionIndex);
            }
            else
            {
                selectedVertices.Clear();
                selectedTriangles.Clear();
                RemovePreview();
            }
            
            debugInfo += $"Remaining selections: {meshSelections.Count}\n";
        }
        
        private void SetupSafetyComponent()
        {
            debugInfo += $"[Safety] SetupSafetyComponent called\n";
            debugInfo += $"[Safety] targetMeshRenderer: {(targetMeshRenderer != null ? targetMeshRenderer.name : "null")}\n";
            debugInfo += $"[Safety] originalMaterials: {(originalMaterials != null ? originalMaterials.Length.ToString() : "null")}\n";
            debugInfo += $"[Safety] windowGUID: {windowGUID}\n";
            
            if (targetMeshRenderer != null && originalMaterials != null && originalMaterials.Length > 0)
            {
                debugInfo += $"[Safety] Creating safety with {originalMaterials.Length} materials:\n";
                for (int i = 0; i < originalMaterials.Length; i++)
                {
                    debugInfo += $"[Safety]   Material[{i}]: {(originalMaterials[i] != null ? originalMaterials[i].name : "null")}\n";
                }
                
                currentSafety = MeshColorMaterialSafety.CreateSafety(targetMeshRenderer, originalMaterials, windowGUID);
                if (currentSafety != null)
                {
                    debugInfo += $"[Safety] Material safety component created successfully on {targetMeshRenderer.gameObject.name}\n";
                    
                    var components = targetMeshRenderer.gameObject.GetComponents<MeshColorMaterialSafety>();
                    debugInfo += $"[Safety] Found {components.Length} MeshColorMaterialSafety components on GameObject\n";
                }
                else
                {
                    debugInfo += "[Safety] Failed to create safety component (returned null)\n";
                }
            }
            else
            {
                debugInfo += "[Safety] Cannot create safety component - missing renderer or materials\n";
            }
        }
        
        private void RemoveSafetyComponent()
        {
            debugInfo += "[Safety] RemoveSafetyComponent called\n";
            
            if (targetMeshRenderer != null)
            {
                var components = targetMeshRenderer.gameObject.GetComponents<MeshColorMaterialSafety>();
                debugInfo += $"[Safety] Found {components.Length} safety components before removal\n";
                
                MeshColorMaterialSafety.RemoveSafety(targetMeshRenderer);
                
                components = targetMeshRenderer.gameObject.GetComponents<MeshColorMaterialSafety>();
                debugInfo += $"[Safety] Found {components.Length} safety components after removal\n";
            }
            
            currentSafety = null;
            debugInfo += "[Safety] Material safety component removed\n";
        }
        
        
        [System.Serializable]
        private class MeshSelection
        {
            public HashSet<int> vertices = new HashSet<int>();
            public List<int> triangles = new List<int>();
            public bool isEnabled = true;
        }
        
        private class VertexCandidate
        {
            public int index;
            public Vector3 worldPosition;
            public float distance;
        }
        
        private string GetLocalizedText(string key)
        {
            if (currentLanguage == Language.Japanese)
            {
                switch (key)
                {
                    // Main window
                    case "title": return "Nesh Color Editor";
                    case "noRenderer": return "アバターを選択してください";
                    
                    // Target selection
                    case "targetMesh": return "1.アバターの選択(GameObject)";
                    case "avatar": return "アバターを選択";
                    case "clear": return "クリア";
                    case "selectMesh": return "メッシュを選択:";
                    case "selectMeshPrompt": return "メッシュを選択してください";
                    case "hideOtherMeshes": return "他のメッシュを隠す";
                    case "meshInfo": return "メッシュ情報:";
                    case "mesh": return "メッシュ: ";
                    case "vertices": return "頂点数: ";
                    case "material": return "マテリアル: ";
                    case "selectMaterial": return "マテリアルを選択:";
                    case "selectMaterialPrompt": return "マテリアルを選択してください";
                    case "textureReadable": return "テクスチャ読み取り可能: ";
                    case "yes": return "はい";
                    case "no": return "いいえ (コピーします)";
                    case "statusColliderReady": return "ステータス: コライダー準備完了";
                    
                    // Selection mode
                    case "meshSelection": return "2.色を変えるメッシュ選択";
                    case "selectionMode": return "選択モード";
                    case "selectionModeOn": return "選択モード ON (クリックで無効)";
                    case "selectionModeOff": return "選択モード OFF (クリックで有効)";
                    case "sceneViewHint": return "💡 Sceneビューでメッシュをクリックして選択してください";
                    case "multiSelectionMode": return "複数選択モード";
                    case "clickAdd": return "クリック: 選択に追加 | Ctrl+クリック: 選択から削除";
                    case "clickNew": return "クリック: 新しいエリアを選択";
                    case "selectionSettings": return "選択設定";
                    case "limitToXAxis": return "X軸の片側に制限";
                    case "xAxisCenter": return "X軸中心";
                    case "xAxisHelp": return "選択はX = {0}を越えません\nクリックした側のみが選択されます";
                    case "totalSelectedVertices": return "選択された頂点の総数: ";
                    case "clearAllSelections": return "すべての選択をクリア";
                    case "setupCollider": return "コライダーをセットアップ (デバッグ)";
                    
                    // Selection list
                    case "meshSelections": return "選択されたメッシュ";
                    case "noAreasSelected": return "選択されたエリアはありません";
                    case "area": return "エリア";
                    case "verts": return "頂点";
                    
                    // Color settings
                    case "colorSettings": return "3.色設定";
                    case "blendColor": return "色";
                    case "strength": return "強度";
                    case "blendMode": return "ブレンドモード";
                    case "showPreview": return "プレビューを表示";
                    
                    // Blend modes
                    case "additiveDesc": return "色値を加算（明るくする）";
                    case "multiplyDesc": return "色値を乗算（暗くする）";
                    case "colorDesc": return "輝度を保持しながら色相と彩度を適用（Photoshopのカラーモード）";
                    case "overlayDesc": return "ベース色に基づいて乗算とスクリーンを組み合わせる";
                    
                    // Actions
                    case "actions": return "アクション";
                    case "applyColor": return "色をマテリアルに適用";
                    case "exportMaskTexture": return "マスクをエクスポート";
                    case "exportTexture": return "テクスチャをエクスポート";
                    case "resetToOriginal": return "オリジナルにリセット";
                    case "materialSafetyHint": return "💡 オリジナルのマテリアルは上書きされません。複製されたファイルは kokoa/GeneratedMaterials と kokoa/GeneratedTextures に保存されます。";
                    
                    
                    // Debug
                    case "debugInformation": return "デバッグ情報";
                    case "clearDebug": return "デバッグをクリア";
                    
                    // Dialog messages
                    case "noSkinnedMeshRenderer": return "SkinnedMeshRendererなし";
                    case "noSkinnedMeshRendererMsg": return "選択されたアバターにはSkinnedMeshRendererコンポーネントがありません。";
                    case "error": return "エラー";
                    case "textureCreateError": return "テクスチャの作成に失敗しました。元のテクスチャで読み取り/書き込みが有効になっているか確認してください。";
                    case "maskExportComplete": return "マスクエクスポート完了";
                    case "maskExportMsg": return "マスクテクスチャがエクスポートされました:\n{0}\n\n白い領域は選択された領域、黒い領域は未選択を表します。";
                    case "textureExportComplete": return "テクスチャエクスポート完了";
                    case "textureExportMsg": return "テクスチャがエクスポートされました:\n{0}";
                    case "ok": return "OK";
                    
                    default: return key;
                }
            }
            else // English
            {
                switch (key)
                {
                    // Main window
                    case "title": return "Mesh Color Editor";
                    case "noRenderer": return "Please select an avatar";
                    
                    // Target selection
                    case "targetMesh": return "1. Avatar Selection";
                    case "avatar": return "Select Avatar or GameObject";
                    case "clear": return "Clear";
                    case "selectMesh": return "Select Mesh:";
                    case "selectMeshPrompt": return "Please select a mesh";
                    case "hideOtherMeshes": return "Hide Other Meshes";
                    case "meshInfo": return "Mesh Info:";
                    case "mesh": return "Mesh: ";
                    case "vertices": return "Vertices: ";
                    case "material": return "Material: ";
                    case "selectMaterial": return "Select Material:";
                    case "selectMaterialPrompt": return "Please select a material";
                    case "textureReadable": return "Texture Readable: ";
                    case "yes": return "Yes";
                    case "no": return "No (will copy)";
                    case "statusColliderReady": return "Status: Collider Ready";
                    
                    // Selection mode
                    case "meshSelection": return "2. Mesh Selection for Color Change";
                    case "selectionMode": return "Selection Mode";
                    case "selectionModeOn": return "Selection Mode ON (Click to disable)";
                    case "selectionModeOff": return "Selection Mode OFF (Click to enable)";
                    case "sceneViewHint": return "💡 Click on mesh in Scene view to select";
                    case "multiSelectionMode": return "Multi Selection Mode";
                    case "clickAdd": return "Click: Add to selection | Ctrl+Click: Remove from selection";
                    case "clickNew": return "Click: Select new area";
                    case "selectionSettings": return "Selection Settings";
                    case "limitToXAxis": return "Limit to X-Axis Side";
                    case "xAxisCenter": return "X-Axis Center";
                    case "xAxisHelp": return "Selection will not cross X = {0}\nClick on either side to select that side only";
                    case "totalSelectedVertices": return "Total Selected Vertices: ";
                    case "clearAllSelections": return "Clear All Selections";
                    case "setupCollider": return "Setup Collider (Debug)";
                    
                    // Selection list
                    case "meshSelections": return "Selected Meshes";
                    case "noAreasSelected": return "No areas selected";
                    case "area": return "Area";
                    case "verts": return "vertices";
                    
                    // Color settings
                    case "colorSettings": return "3. Color Settings";
                    case "blendColor": return "Color";
                    case "strength": return "Strength";
                    case "blendMode": return "Blend Mode";
                    case "showPreview": return "Show Preview";
                    
                    // Blend modes
                    case "additiveDesc": return "Adds color values (brightens)";
                    case "multiplyDesc": return "Multiplies color values (darkens)";
                    case "colorDesc": return "Applies hue and saturation while preserving luminance (Photoshop Color mode)";
                    case "overlayDesc": return "Combines multiply and screen based on base color";
                    
                    // Actions
                    case "actions": return "Actions";
                    case "applyColor": return "Apply Color to Material";
                    case "exportMaskTexture": return "Export Mask";
                    case "exportTexture": return "Export Texture";
                    case "resetToOriginal": return "Reset to Original";
                    case "materialSafetyHint": return "💡 Original materials are not overwritten. Duplicated files are saved to kokoa/GeneratedMaterials and kokoa/GeneratedTextures.";
                    
                    
                    // Debug
                    case "debugInformation": return "Debug Information";
                    case "clearDebug": return "Clear Debug";
                    
                    // Dialog messages
                    case "noSkinnedMeshRenderer": return "No SkinnedMeshRenderer";
                    case "noSkinnedMeshRendererMsg": return "The selected avatar doesn't have any SkinnedMeshRenderer components.";
                    case "error": return "Error";
                    case "textureCreateError": return "Failed to create texture. Please check if the original texture has Read/Write enabled.";
                    case "maskExportComplete": return "Mask Export Complete";
                    case "maskExportMsg": return "Mask texture has been exported to:\n{0}\n\nWhite areas represent selected regions, black areas are unselected.";
                    case "textureExportComplete": return "Texture Export Complete";
                    case "textureExportMsg": return "Texture has been exported to:\n{0}";
                    case "ok": return "OK";
                    
                    default: return key;
                }
            }
        }
    }
}
