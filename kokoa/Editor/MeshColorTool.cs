using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

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
        
        // Highlight functionality
        private SkinnedMeshRenderer highlightedRenderer;
        private Material highlightMaterial;
        private Material[] originalHighlightMaterials;
        
        // Mesh thumbnail cache
        private Dictionary<Mesh, Texture2D> meshThumbnailCache = new Dictionary<Mesh, Texture2D>();
        private const int thumbnailSize = 64;
        private Vector2 meshRendererScrollPosition = Vector2.zero;
        private bool showMeshRendererList = false;
        
        // Selection
        private bool isSelectionMode = false;
        private bool isMultiSelectionMode = false;
        private HashSet<int> selectedVertices = new HashSet<int>();
        private List<int> selectedTriangles = new List<int>();
        
        // Selection Settings
        private bool limitToXAxis = false;
        private float xAxisThreshold = 0.0f;
        private int maxVertexCount = 1000;
        
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
                }
                else
                {
                    targetMeshRenderer.sharedMaterials = originalMaterials;
                }
            }
            
            CleanupPreview();
            RemoveTempCollider();
            RestoreAllMeshes();
            RemoveHighlight();
            RemoveSafetyComponent();
            ClearMeshThumbnailCache();
            
            // ハイライトマテリアルのクリーンアップ
            if (highlightMaterial != null)
            {
                DestroyImmediate(highlightMaterial);
                highlightMaterial = null;
            }
        }
        
        private void ClearAvatarSelection()
        {
            // Restore original materials if needed
            if (targetMeshRenderer != null && originalMaterials != null)
            {
                
                // Create a new array to avoid reference issues, copying from originalMaterials
                Material[] materialsToRestore = new Material[originalMaterials.Length];
                for (int i = 0; i < originalMaterials.Length; i++)
                {
                    materialsToRestore[i] = originalMaterials[i];
                }
                
                // Use different restoration method based on material count
                if (materialsToRestore.Length == 1)
                {
                    targetMeshRenderer.sharedMaterial = materialsToRestore[0];
                }
                else
                {
                    targetMeshRenderer.sharedMaterials = materialsToRestore;
                }
                
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
            
            // Disable the object field if an avatar is already selected
            EditorGUI.BeginDisabledGroup(targetAvatar != null);
            EditorGUI.BeginChangeCheck();
            targetAvatar = EditorGUILayout.ObjectField(GetLocalizedText("avatar"), targetAvatar, typeof(GameObject), true) as GameObject;
            bool avatarChanged = EditorGUI.EndChangeCheck();
            EditorGUI.EndDisabledGroup();
            
            if (targetAvatar != null)
            {
                if (GUILayout.Button(GetLocalizedText("clear"), GUILayout.Width(50)))
                {
                    ClearAvatarSelection();
                }
            }
            EditorGUILayout.EndHorizontal();
            
            // Show hint when avatar is already selected
            if (targetAvatar != null)
            {
                EditorGUILayout.HelpBox(GetLocalizedText("avatarLockedHint"), MessageType.Info);
            }
            
            if (avatarChanged && targetAvatar != null)
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
                
                // 新しいサムネイル付きメッシュレンダラー選択UI
                DrawMeshRendererWithThumbnails();
                
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
                }
                else
                {
                    targetMeshRenderer.sharedMaterials = workingMaterials;
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
                RemovePreview();
            }
            
            RemoveSafetyComponent();
            
            selectedMaterialIndex = materialIndex;
            originalMaterial = originalMaterials[materialIndex]; // Use original materials array for texture reference
            
            if (originalMaterial != null && originalMaterial.mainTexture != null)
            {
                originalTexture = originalMaterial.mainTexture as Texture2D;
            }
            else
            {
                originalTexture = null;
            }
            
            
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
        
        private void HighlightMeshRenderer(SkinnedMeshRenderer renderer)
        {
            // 既存のハイライトを削除
            RemoveHighlight();
            
            if (renderer == null) return;
            
            highlightedRenderer = renderer;
            originalHighlightMaterials = renderer.sharedMaterials;
            
            // ハイライト用マテリアルを作成（奥にあっても見えるように設定）
            if (highlightMaterial == null)
            {
                // Unlitシェーダーを使用してライティングに影響されないようにする
                highlightMaterial = new Material(Shader.Find("Unlit/Color"));
                highlightMaterial.color = new Color(1f, 1f, 0f, 0.8f); // 半透明の黄色
                
                // 奥にあっても見えるようにZTestを変更
                highlightMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                highlightMaterial.SetOverrideTag("RenderType", "Transparent");
                highlightMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                highlightMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                highlightMaterial.SetInt("_ZWrite", 0);
                highlightMaterial.renderQueue = 5000; // より高い優先度で描画
                
                // シェーダーキーワードの設定
                highlightMaterial.DisableKeyword("_ALPHATEST_ON");
                highlightMaterial.EnableKeyword("_ALPHABLEND_ON");
                highlightMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }
            
            // ハイライトマテリアルを適用
            Material[] highlightMaterials = new Material[originalHighlightMaterials.Length];
            for (int i = 0; i < highlightMaterials.Length; i++)
            {
                highlightMaterials[i] = highlightMaterial;
            }
            renderer.sharedMaterials = highlightMaterials;
            
            // 3秒後にハイライトを削除（少し長めに）
            EditorApplication.delayCall += () => {
                EditorApplication.delayCall += () => {
                    EditorApplication.delayCall += () => {
                        EditorApplication.delayCall += RemoveHighlight;
                    };
                };
            };
            
            SceneView.RepaintAll();
        }
        
        private void RemoveHighlight()
        {
            if (highlightedRenderer != null && originalHighlightMaterials != null)
            {
                highlightedRenderer.sharedMaterials = originalHighlightMaterials;
                highlightedRenderer = null;
                originalHighlightMaterials = null;
                SceneView.RepaintAll();
            }
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
                
                maxVertexCount = EditorGUILayout.IntSlider(GetLocalizedText("maxVertexCount"), maxVertexCount, 100, 10000);
                
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
            
            if (GUILayout.Button(GetLocalizedText("clearAllSelections")))
            {
                ClearAllSelections();
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
            EditorGUILayout.LabelField(GetLocalizedText("apply"), EditorStyles.boldLabel);
            
            GUI.enabled = meshSelections.Count > 0 && originalTexture != null;
            
            if (GUILayout.Button(GetLocalizedText("applyColor"), GUILayout.Height(30)))
            {
                ApplyColorToSelection();
            }
            
            GUI.enabled = true;
            
            if (GUILayout.Button(GetLocalizedText("resetToOriginal")))
            {
                ResetToOriginal();
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(GetLocalizedText("textureOutput"), EditorStyles.boldLabel);
            
            GUI.enabled = meshSelections.Count > 0 && originalTexture != null;
            
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
            
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(GetLocalizedText("materialSafetyHint"), MessageType.Info);
            
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
                if (this != null) Repaint();
            }
            
            // Only process LEFT clicks when Alt is not held (to allow camera rotation)
            // button == 0: left click, button == 1: right click, button == 2: middle click
            if (e.type == EventType.MouseDown && e.button == 0 && isSelectionMode && !isAltHeld)
            {
                bool isCtrlHeld = e.control;
                
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                
                if (tempCollider != null && targetMeshRenderer != null)
                {
                    Mesh bakedMesh = new Mesh();
                    targetMeshRenderer.BakeMesh(bakedMesh);
                    tempCollider.sharedMesh = bakedMesh;
                }
                
                RaycastHit hit;
                
                if (Physics.Raycast(ray, out hit))
                {
                    
                    if (hit.collider == tempCollider)
                    {
                        SelectMeshArea(hit.point);
                    }
                    else if (hit.collider.gameObject == targetMeshRenderer.gameObject)
                    {
                        SelectMeshArea(hit.point);
                    }
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
        }
        
        private void SelectMeshArea(Vector3 hitPoint)
        {
            if (targetMesh == null) return;
            
            
            Event currentEvent = Event.current;
            bool isCtrlHeld = currentEvent != null ? currentEvent.control : false;
            
            Camera sceneCamera = SceneView.lastActiveSceneView?.camera;
            if (sceneCamera == null) return;
            
            Vector3 cameraPosition = sceneCamera.transform.position;
            Vector3 cameraForward = sceneCamera.transform.forward;
            
            Vector3[] vertices = targetMesh.vertices;
            Vector3[] normals = targetMesh.normals;
            Transform meshTransform = targetMeshRenderer.transform;
            
            
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
            
            
            int selectedVertex = -1;
            float bestDot = -1f;
            
            for (int i = 0; i < closestVertices.Count; i++)
            {
                int vertexIndex = closestVertices[i].index;
                Vector3 worldNormal = meshTransform.TransformDirection(normals[vertexIndex]).normalized;
                Vector3 toCameraDirection = (cameraPosition - closestVertices[i].worldPosition).normalized;
                
                float dot = Vector3.Dot(worldNormal, toCameraDirection);
                
                
                if (dot > bestDot)
                {
                    bestDot = dot;
                    selectedVertex = vertexIndex;
                }
            }
            
            
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
                }
                
                HashSet<int> visited = new HashSet<int>();
                Queue<int> queue = new Queue<int>();
                
                queue.Enqueue(selectedVertex);
                visited.Add(selectedVertex);
                
                int[] triangles = targetMesh.triangles;
                
                int processedCount = 0;
                bool isFirstVertex = true;
                
                while (queue.Count > 0 && processedCount < maxVertexCount)
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
                
                
                if (isMultiSelectionMode)
                {
                    if (isCtrlHeld)
                    {
                        RemoveVerticesFromSelections(selectedVertices);
                    }
                    else
                    {
                        int overlappingSelectionIndex = FindOverlappingSelection(selectedVertices);
                        
                        if (overlappingSelectionIndex >= 0)
                        {
                            RemoveSelection(overlappingSelectionIndex);
                        }
                        else
                        {
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
                    meshSelections.Clear();
                    
                    var newSelection = new MeshSelection
                    {
                        vertices = new HashSet<int>(selectedVertices),
                        triangles = new List<int>(selectedTriangles)
                    };
                    
                    meshSelections.Add(newSelection);
                    activeSelectionIndex = meshSelections.Count - 1;
                }
                
                
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
            
            // ブレンドシェイプが適用された後の頂点位置を取得
            Mesh bakedMesh = new Mesh();
            targetMeshRenderer.BakeMesh(bakedMesh);
            Vector3[] vertices = bakedMesh.vertices;
            
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
                }
                else
                {
                    targetMeshRenderer.sharedMaterials = materials;
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

            // Recreate safety component with the new material as the "original"
            SetupSafetyComponent();
            
            ClearAllSelections();
            
            // Turn off selection mode after applying color
            isSelectionMode = false;
            Tools.current = Tool.Move;
            
            
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
        
        private Texture2D GetMeshThumbnail(Mesh mesh, SkinnedMeshRenderer renderer = null)
        {
            if (mesh == null) return null;
            
            if (meshThumbnailCache.ContainsKey(mesh))
            {
                return meshThumbnailCache[mesh];
            }
            
            // カラーサムネイルを生成
            Texture2D colorThumbnail = CreateColorMeshPreview(mesh, renderer);
            
            if (colorThumbnail != null)
            {
                meshThumbnailCache[mesh] = colorThumbnail;
                return colorThumbnail;
            }
            
            // フォールバック: AssetPreviewを使用
            Texture2D thumbnail = AssetPreview.GetAssetPreview(mesh);
            if (thumbnail == null)
            {
                thumbnail = EditorGUIUtility.FindTexture("d_Mesh Icon");
            }
            
            if (thumbnail != null)
            {
                Texture2D resizedThumbnail = new Texture2D(thumbnailSize, thumbnailSize, TextureFormat.ARGB32, false);
                RenderTexture tmp = RenderTexture.GetTemporary(thumbnailSize, thumbnailSize, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = tmp;
                
                Graphics.Blit(thumbnail, tmp);
                resizedThumbnail.ReadPixels(new Rect(0, 0, thumbnailSize, thumbnailSize), 0, 0);
                resizedThumbnail.Apply();
                
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(tmp);
                
                meshThumbnailCache[mesh] = resizedThumbnail;
                return resizedThumbnail;
            }
            
            return null;
        }
        
        private Texture2D CreateColorMeshPreview(Mesh mesh, SkinnedMeshRenderer renderer = null)
        {
            if (mesh == null) return null;
            
            // プレビュー用の独立したレイヤーを使用
            int previewLayer = 31; // 通常使われないレイヤー
            
            // プレビュー用のレンダーテクスチャを作成
            RenderTexture renderTexture = RenderTexture.GetTemporary(thumbnailSize * 2, thumbnailSize * 2, 24, RenderTextureFormat.ARGB32);
            renderTexture.antiAliasing = 4;
            
            // プレビュー用のカメラを作成
            GameObject cameraGO = new GameObject("PreviewCamera");
            cameraGO.hideFlags = HideFlags.HideAndDontSave;
            Camera previewCamera = cameraGO.AddComponent<Camera>();
            previewCamera.targetTexture = renderTexture;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0f);
            float fieldOfView = 30f;
            previewCamera.fieldOfView = fieldOfView;
            previewCamera.cullingMask = 1 << previewLayer; // プレビューレイヤーのみ表示
            
            // プレビュー用のメッシュオブジェクトを作成
            GameObject meshGO = new GameObject("PreviewMesh");
            meshGO.hideFlags = HideFlags.HideAndDontSave;
            meshGO.layer = previewLayer;
            
            // SkinnedMeshRendererの場合の処理
            if (renderer != null)
            {
                // SkinnedMeshRendererから直接メッシュを取得してMeshRendererとして作成
                MeshFilter meshFilter = meshGO.AddComponent<MeshFilter>();
                MeshRenderer meshRenderer = meshGO.AddComponent<MeshRenderer>();
                
                // ベイクされたメッシュを作成
                Mesh bakedMesh = new Mesh();
                renderer.BakeMesh(bakedMesh);
                meshFilter.sharedMesh = bakedMesh;
                
                // マテリアルを設定
                if (renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0)
                {
                    meshRenderer.sharedMaterials = renderer.sharedMaterials;
                }
                else if (renderer.sharedMaterial != null)
                {
                    meshRenderer.sharedMaterial = renderer.sharedMaterial;
                }
                else
                {
                    Material defaultMat = new Material(Shader.Find("Standard"));
                    defaultMat.color = Color.white;
                    meshRenderer.sharedMaterial = defaultMat;
                }
                
                // 境界ボックスを取得
                Bounds bounds = bakedMesh.bounds;
                
                // メッシュの種類を推測して最適な角度を設定
                Vector3 cameraDirection = GetOptimalCameraAngle(renderer.name, bounds);
                
                // カメラの距離を計算（全体が映るように）
                float distance = CalculateOptimalCameraDistance(bounds, fieldOfView, cameraDirection);
                
                // カメラの位置を調整
                cameraGO.transform.position = bounds.center + cameraDirection.normalized * distance;
                cameraGO.transform.LookAt(bounds.center);
                
                // 後でクリーンアップ用に保持
                mesh = bakedMesh;
            }
            else
            {
                // 通常のメッシュの場合
                MeshFilter meshFilter = meshGO.AddComponent<MeshFilter>();
                MeshRenderer meshRenderer = meshGO.AddComponent<MeshRenderer>();
                meshFilter.sharedMesh = mesh;
                
                // デフォルトマテリアル
                Material defaultMat = new Material(Shader.Find("Standard"));
                defaultMat.color = Color.white;
                meshRenderer.sharedMaterial = defaultMat;
                
                // 境界ボックスを取得
                Bounds bounds = mesh.bounds;
                
                // デフォルトの角度
                Vector3 cameraDirection = new Vector3(1.0f, 0.6f, -1.8f);
                
                // カメラの距離を計算（全体が映るように）
                float distance = CalculateOptimalCameraDistance(bounds, fieldOfView, cameraDirection);
                
                // カメラの位置を調整
                cameraGO.transform.position = bounds.center + cameraDirection.normalized * distance;
                cameraGO.transform.LookAt(bounds.center);
            }
            
            // ライトを追加
            GameObject lightGO = new GameObject("PreviewLight");
            lightGO.hideFlags = HideFlags.HideAndDontSave;
            lightGO.layer = previewLayer;
            Light light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
            light.color = Color.white;
            lightGO.transform.rotation = Quaternion.Euler(30, -30, 0);
            
            // アンビエントライトを追加
            GameObject ambientLightGO = new GameObject("AmbientLight");
            ambientLightGO.hideFlags = HideFlags.HideAndDontSave;
            ambientLightGO.layer = previewLayer;
            Light ambientLight = ambientLightGO.AddComponent<Light>();
            ambientLight.type = LightType.Directional;
            ambientLight.intensity = 0.5f;
            ambientLight.color = Color.white;
            ambientLightGO.transform.rotation = Quaternion.Euler(-30, 30, 0);
            
            // レンダリング
            previewCamera.Render();
            
            // レンダーテクスチャから読み取り
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            
            Texture2D thumbnail = new Texture2D(thumbnailSize, thumbnailSize, TextureFormat.ARGB32, false);
            thumbnail.ReadPixels(new Rect((thumbnailSize * 2 - thumbnailSize) / 2, (thumbnailSize * 2 - thumbnailSize) / 2, thumbnailSize, thumbnailSize), 0, 0);
            thumbnail.Apply();
            
            RenderTexture.active = previous;
            
            // クリーンアップ
            DestroyImmediate(cameraGO);
            DestroyImmediate(meshGO);
            DestroyImmediate(lightGO);
            DestroyImmediate(ambientLightGO);
            
            // ベイクされたメッシュをクリーンアップ
            if (renderer != null && mesh != null)
            {
                DestroyImmediate(mesh);
            }
            
            RenderTexture.ReleaseTemporary(renderTexture);
            
            return thumbnail;
        }
        
        private float CalculateOptimalCameraDistance(Bounds bounds, float fieldOfView, Vector3 cameraDirection)
        {
            // カメラの向きを正規化
            Vector3 normalizedDir = cameraDirection.normalized;
            
            // バウンディングボックスの8つの頂点を取得
            Vector3[] corners = new Vector3[8];
            corners[0] = bounds.min;
            corners[1] = new Vector3(bounds.min.x, bounds.min.y, bounds.max.z);
            corners[2] = new Vector3(bounds.min.x, bounds.max.y, bounds.min.z);
            corners[3] = new Vector3(bounds.max.x, bounds.min.y, bounds.min.z);
            corners[4] = new Vector3(bounds.max.x, bounds.max.y, bounds.min.z);
            corners[5] = new Vector3(bounds.max.x, bounds.min.y, bounds.max.z);
            corners[6] = new Vector3(bounds.min.x, bounds.max.y, bounds.max.z);
            corners[7] = bounds.max;
            
            // 各頂点から必要な距離を計算
            float maxDistance = 0f;
            float halfFOV = fieldOfView * 0.5f * Mathf.Deg2Rad;
            float tanHalfFOV = Mathf.Tan(halfFOV);
            
            foreach (Vector3 corner in corners)
            {
                // 中心から頂点へのベクトル
                Vector3 toCorner = corner - bounds.center;
                
                // カメラ方向に垂直な平面への投影距離
                float perpDistance = Vector3.Cross(toCorner, normalizedDir).magnitude;
                
                // カメラ方向への投影距離
                float parallelDistance = Vector3.Dot(toCorner, -normalizedDir);
                
                // FOVを考慮した必要距離
                float requiredDistance = perpDistance / tanHalfFOV + parallelDistance;
                
                maxDistance = Mathf.Max(maxDistance, requiredDistance);
            }
            
            // パディングを追加（10%の余白）
            return maxDistance * 1.1f;
        }
        
        private Vector3 GetOptimalCameraAngle(string meshName, Bounds bounds)
        {
            string lowerName = meshName.ToLower();
            
            // アスペクト比を計算（縦長か横長か）
            float heightRatio = bounds.size.y / Mathf.Max(bounds.size.x, bounds.size.z);
            bool isTall = heightRatio > 1.5f; // 縦長のメッシュ
            
            // 頭部・髪のメッシュ
            if (lowerName.Contains("head") || lowerName.Contains("hair") || 
                lowerName.Contains("face") || lowerName.Contains("頭") || 
                lowerName.Contains("髪"))
            {
                // 顔が見えるように少し上から斜め前方
                return new Vector3(0.7f, 0.5f, 1.8f);
            }
            // 体・ボディメッシュ
            else if (lowerName.Contains("body") || lowerName.Contains("身体") || 
                     lowerName.Contains("胴") || isTall)
            {
                // 全身が見えるように斜め前方から
                return new Vector3(1.2f, 0.8f, 2.0f);
            }
            // 服・衣装メッシュ
            else if (lowerName.Contains("cloth") || lowerName.Contains("wear") || 
                     lowerName.Contains("shirt") || lowerName.Contains("dress") ||
                     lowerName.Contains("服") || lowerName.Contains("衣装"))
            {
                // 服のデザインが見えるように正面寄り
                return new Vector3(0.5f, 0.3f, 2.2f);
            }
            // アクセサリー・小物
            else if (lowerName.Contains("accessory") || lowerName.Contains("jewel") ||
                     lowerName.Contains("ring") || lowerName.Contains("アクセ"))
            {
                // 詳細が見えるように近めで斜め
                return new Vector3(0.8f, 0.8f, 1.5f);
            }
            // 靴・足元
            else if (lowerName.Contains("shoe") || lowerName.Contains("boot") ||
                     lowerName.Contains("foot") || lowerName.Contains("靴"))
            {
                // 横から少し上
                return new Vector3(1.5f, 0.3f, 1.5f);
            }
            // 手・腕
            else if (lowerName.Contains("hand") || lowerName.Contains("arm") ||
                     lowerName.Contains("手") || lowerName.Contains("腕"))
            {
                // 手の形が見えるように
                return new Vector3(1.0f, 0.5f, 1.8f);
            }
            // デフォルト（3/4ビュー）
            else
            {
                // 汎用的な斜め前方からの角度
                return new Vector3(1.0f, 0.6f, 1.8f);
            }
        }
        
        private void DrawMeshRendererWithThumbnails()
        {
            if (availableRenderers == null || availableRenderers.Length == 0) return;
            
            EditorGUILayout.LabelField("Mesh Renderer", EditorStyles.boldLabel);
            
            // 選択されたレンダラーの情報を表示
            EditorGUILayout.BeginHorizontal();
            
            if (selectedRendererIndex >= 0 && selectedRendererIndex < availableRenderers.Length)
            {
                var selectedRenderer = availableRenderers[selectedRendererIndex];
                Texture2D thumbnail = GetMeshThumbnail(selectedRenderer.sharedMesh, selectedRenderer);
                
                if (thumbnail != null)
                {
                    GUILayout.Label(thumbnail, GUILayout.Width(thumbnailSize), GUILayout.Height(thumbnailSize));
                }
                
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField($"選択中: {selectedRenderer.name}");
                if (selectedRenderer.sharedMesh != null)
                {
                    EditorGUILayout.LabelField($"メッシュ: {selectedRenderer.sharedMesh.name}");
                    EditorGUILayout.LabelField($"頂点数: {selectedRenderer.sharedMesh.vertexCount}");
                }
                EditorGUILayout.EndVertical();
            }
            else
            {
                EditorGUILayout.LabelField("メッシュレンダラーを選択してください");
            }
            
            GUILayout.FlexibleSpace();
            
            // 変更ボタン
            if (GUILayout.Button("変更", GUILayout.Width(60)))
            {
                showMeshRendererList = !showMeshRendererList;
            }
            
            EditorGUILayout.EndHorizontal();
            
            // リストの表示・非表示
            if (showMeshRendererList)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("利用可能なメッシュレンダラー:", EditorStyles.boldLabel);
                
                meshRendererScrollPosition = EditorGUILayout.BeginScrollView(meshRendererScrollPosition, GUILayout.Height(200));
                
                for (int i = 0; i < availableRenderers.Length; i++)
                {
                    var renderer = availableRenderers[i];
                    Texture2D thumbnail = GetMeshThumbnail(renderer.sharedMesh, renderer);
                    
                    EditorGUILayout.BeginHorizontal();
                    
                    // サムネイル表示
                    if (thumbnail != null)
                    {
                        GUILayout.Label(thumbnail, GUILayout.Width(thumbnailSize), GUILayout.Height(thumbnailSize));
                    }
                    else
                    {
                        GUILayout.Label("", GUILayout.Width(thumbnailSize), GUILayout.Height(thumbnailSize));
                    }
                    
                    // メッシュ情報
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField($"{i}: {renderer.name}");
                    if (renderer.sharedMesh != null)
                    {
                        EditorGUILayout.LabelField($"メッシュ: {renderer.sharedMesh.name}");
                        EditorGUILayout.LabelField($"頂点数: {renderer.sharedMesh.vertexCount}");
                    }
                    EditorGUILayout.EndVertical();
                    
                    // 右側にボタンを配置
                    GUILayout.FlexibleSpace();
                    
                    EditorGUILayout.BeginVertical(GUILayout.Width(80));
                    
                    // 選択ボタン
                    bool isSelected = (selectedRendererIndex == i);
                    Color originalColor = GUI.backgroundColor;
                    if (isSelected)
                    {
                        GUI.backgroundColor = Color.cyan;
                    }
                    
                    if (GUILayout.Button("選択", GUILayout.Width(80)))
                    {
                        selectedRendererIndex = i;
                        SelectMeshRenderer(availableRenderers[selectedRendererIndex]);
                        ClearAllSelections();
                        showMeshRendererList = false; // 選択後にリストを閉じる
                    }
                    
                    GUI.backgroundColor = Color.yellow;
                    
                    GUIContent highlightContent = EditorGUIUtility.IconContent("Lighting");
                    highlightContent.text = " 光らせる";
                    highlightContent.tooltip = "メッシュを一時的にハイライト表示します";
                    
                    if (GUILayout.Button(highlightContent, GUILayout.Width(80)))
                    {
                        HighlightMeshRenderer(renderer);
                    }
                    
                    GUI.backgroundColor = originalColor;
                    
                    EditorGUILayout.EndVertical();
                    
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.Space(5);
                }
                
                EditorGUILayout.EndScrollView();
            }
        }
        
        private void ClearMeshThumbnailCache()
        {
            foreach (var thumbnail in meshThumbnailCache.Values)
            {
                if (thumbnail != null)
                {
                    DestroyImmediate(thumbnail);
                }
            }
            meshThumbnailCache.Clear();
        }
        
        private Texture2D CreateModifiedTextureWithAllSelections()
        {
            Texture2D workingTexture;
            
            if (IsTextureReadable(originalTexture))
            {
                workingTexture = new Texture2D(originalTexture.width, originalTexture.height, TextureFormat.ARGB32, false);
                workingTexture.SetPixels(originalTexture.GetPixels());
                workingTexture.Apply();
            }
            else
            {
                workingTexture = GetReadableTexture(originalTexture);
            }
            
            Vector2[] uvs = targetMesh.uv;
            
            
            // Get entire texture pixels once using Color32 for better Job performance
            Color32[] allPixels = workingTexture.GetPixels32();
            
            // Collect all triangles that need to be painted
            List<Vector2> allTriangleUVs = new List<Vector2>();
            int[] triangles = targetMesh.triangles;
            
            foreach (var selection in meshSelections)
            {
                if (!selection.isEnabled) continue;
                
                foreach (int triangleIndex in selection.triangles)
                {
                    int baseIndex = triangleIndex * 3;
                    if (baseIndex + 2 < triangles.Length)
                    {
                        allTriangleUVs.Add(uvs[triangles[baseIndex]]);
                        allTriangleUVs.Add(uvs[triangles[baseIndex + 1]]);
                        allTriangleUVs.Add(uvs[triangles[baseIndex + 2]]);
                    }
                }
            }
            
            if (allTriangleUVs.Count > 0)
            {
                // Create NativeArrays for Job System
                NativeArray<Color32> pixelArray = new NativeArray<Color32>(allPixels, Allocator.TempJob);
                NativeArray<Vector2> triangleUVArray = new NativeArray<Vector2>(allTriangleUVs.ToArray(), Allocator.TempJob);

                
                // Convert blend mode enum to int
                int blendModeInt = 0;
                switch (currentBlendMode)
                {
                    case BlendMode.Multiply: blendModeInt = 1; break;
                    case BlendMode.Additive: blendModeInt = 2; break;
                    case BlendMode.Overlay: blendModeInt = 3; break;
                    case BlendMode.Color: 
                    default: blendModeInt = 0; break;
                }
                
                // Create and schedule the job
                TrianglePaintJob paintJob = new TrianglePaintJob
                {
                    pixels = pixelArray,
                    triangleUVs = triangleUVArray,
                    textureWidth = workingTexture.width,
                    textureHeight = workingTexture.height,
                    paintColor = new Color32((byte)(blendColor.r * 255), (byte)(blendColor.g * 255), (byte)(blendColor.b * 255), 255),
                    strength = blendStrength,
                    blendMode = blendModeInt
                };
                
                int triangleCount = allTriangleUVs.Count / 3;
                JobHandle jobHandle = paintJob.Schedule(); // Single job processes all triangles
                jobHandle.Complete();
                
                // Copy results back
                pixelArray.CopyTo(allPixels);
                
                // Clean up
                pixelArray.Dispose();
                triangleUVArray.Dispose();
            }
            
            // Set entire texture pixels once
            workingTexture.SetPixels32(allPixels);
            
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
            
            // Expand bounds by 2 pixels for mipmap support
            int minX = Mathf.Max(0, Mathf.Min(x0, Mathf.Min(x1, x2)) - 2);
            int maxX = Mathf.Min(texture.width - 1, Mathf.Max(x0, Mathf.Max(x1, x2)) + 2);
            int minY = Mathf.Max(0, Mathf.Min(y0, Mathf.Min(y1, y2)) - 2);
            int maxY = Mathf.Min(texture.height - 1, Mathf.Max(y0, Mathf.Max(y1, y2)) + 2);
            
            // Get pixels in batch for the region
            int width = maxX - minX + 1;
            int height = maxY - minY + 1;
            
            Color[] regionPixels = texture.GetPixels(minX, minY, width, height);
            
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Vector2Int pixelCoord = new Vector2Int(x, y);
                    
                    if (!paintedPixels.Contains(pixelCoord))
                    {
                        bool isInTriangle = IsPointInTriangle(x, y, x0, y0, x1, y1, x2, y2);
                        float distanceToTriangle = 0f;
                        
                        if (!isInTriangle)
                        {
                            // Calculate distance to triangle for pixels outside triangle
                            distanceToTriangle = DistanceToTriangle(x, y, x0, y0, x1, y1, x2, y2);
                        }
                        
                        // Paint if inside triangle or within 2 pixels of triangle
                        if (isInTriangle || distanceToTriangle <= 2f)
                        {
                            paintedPixels.Add(pixelCoord);
                            
                            // Calculate array index for the region
                            int localX = x - minX;
                            int localY = y - minY;
                            int pixelIndex = localY * width + localX;
                            
                            Color originalColor = regionPixels[pixelIndex];
                            Color blendedColor = ApplyBlendMode(originalColor, color, currentBlendMode, strength);
                            regionPixels[pixelIndex] = blendedColor;
                        }
                    }
                }
            }
            
            // Set all modified pixels back to texture in one batch
            texture.SetPixels(minX, minY, width, height, regionPixels);
        }
        
        private void PaintTriangleOnPixelArray(Color[] pixels, int textureWidth, int textureHeight,
            Vector2 uv0, Vector2 uv1, Vector2 uv2, HashSet<Vector2Int> paintedPixels, Color color, float strength)
        {
            int x0 = Mathf.RoundToInt(uv0.x * (textureWidth - 1));
            int y0 = Mathf.RoundToInt(uv0.y * (textureHeight - 1));
            int x1 = Mathf.RoundToInt(uv1.x * (textureWidth - 1));
            int y1 = Mathf.RoundToInt(uv1.y * (textureHeight - 1));
            int x2 = Mathf.RoundToInt(uv2.x * (textureWidth - 1));
            int y2 = Mathf.RoundToInt(uv2.y * (textureHeight - 1));
            
            // Expand bounds by 2 pixels for mipmap support
            int minX = Mathf.Max(0, Mathf.Min(x0, Mathf.Min(x1, x2)) - 2);
            int maxX = Mathf.Min(textureWidth - 1, Mathf.Max(x0, Mathf.Max(x1, x2)) + 2);
            int minY = Mathf.Max(0, Mathf.Min(y0, Mathf.Min(y1, y2)) - 2);
            int maxY = Mathf.Min(textureHeight - 1, Mathf.Max(y0, Mathf.Max(y1, y2)) + 2);
            
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Vector2Int pixelCoord = new Vector2Int(x, y);
                    
                    if (!paintedPixels.Contains(pixelCoord))
                    {
                        bool isInTriangle = IsPointInTriangle(x, y, x0, y0, x1, y1, x2, y2);
                        float distanceToTriangle = 0f;
                        
                        if (!isInTriangle)
                        {
                            // Calculate distance to triangle for pixels outside triangle
                            distanceToTriangle = DistanceToTriangle(x, y, x0, y0, x1, y1, x2, y2);
                        }
                        
                        // Paint if inside triangle or within 2 pixels of triangle
                        if (isInTriangle || distanceToTriangle <= 2f)
                        {
                            paintedPixels.Add(pixelCoord);
                            
                            // Calculate array index for the pixel
                            int pixelIndex = y * textureWidth + x;
                            
                            if (pixelIndex >= 0 && pixelIndex < pixels.Length)
                            {
                                Color originalColor = pixels[pixelIndex];
                                Color blendedColor = ApplyBlendMode(originalColor, color, currentBlendMode, strength);
                                pixels[pixelIndex] = blendedColor;
                            }
                        }
                    }
                }
            }
        }
        
        private float DistanceToTriangle(float px, float py, float x0, float y0, float x1, float y1, float x2, float y2)
        {
            // Calculate distance from point to each edge of the triangle
            float distToEdge1 = DistanceToLineSegment(px, py, x0, y0, x1, y1);
            float distToEdge2 = DistanceToLineSegment(px, py, x1, y1, x2, y2);
            float distToEdge3 = DistanceToLineSegment(px, py, x2, y2, x0, y0);
            
            return Mathf.Min(distToEdge1, Mathf.Min(distToEdge2, distToEdge3));
        }
        
        private float DistanceToLineSegment(float px, float py, float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            float length2 = dx * dx + dy * dy;
            
            if (length2 == 0)
            {
                // Line segment is a point
                return Mathf.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));
            }
            
            // Calculate parameter t for projection of point onto line
            float t = Mathf.Max(0, Mathf.Min(1, ((px - x1) * dx + (py - y1) * dy) / length2));
            
            // Calculate projection point
            float projX = x1 + t * dx;
            float projY = y1 + t * dy;
            
            // Return distance from point to projection
            return Mathf.Sqrt((px - projX) * (px - projX) + (py - projY) * (py - projY));
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
                    // Photoshop-style Color blend mode: applies hue and saturation of blend color while preserving luminance of base
                    Vector3 baseHSL = RGBToHSL(baseColor);
                    Vector3 blendHSL = RGBToHSL(blendColor);
                    
                    // Keep base luminance, use blend hue and saturation
                    Vector3 resultHSL = new Vector3(blendHSL.x, blendHSL.y, baseHSL.z);
                    Color hslResult = HSLToRGB(resultHSL);
                    hslResult.a = baseColor.a;
                    
                    result = Color.Lerp(baseColor, hslResult, strength);
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
        
        // RGB to HSL conversion
        public static Vector3 RGBToHSL(Color color)
        {
            float r = color.r;
            float g = color.g;
            float b = color.b;
            
            float max = Mathf.Max(r, Mathf.Max(g, b));
            float min = Mathf.Min(r, Mathf.Min(g, b));
            float h, s, l;
            
            l = (max + min) / 2f;
            
            if (max == min)
            {
                h = s = 0; // achromatic
            }
            else
            {
                float d = max - min;
                s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
                
                if (max == r)
                {
                    h = (g - b) / d + (g < b ? 6f : 0f);
                }
                else if (max == g)
                {
                    h = (b - r) / d + 2f;
                }
                else
                {
                    h = (r - g) / d + 4f;
                }
                
                h /= 6f;
            }
            
            return new Vector3(h, s, l);
        }
        
        // HSL to RGB conversion
        public static Color HSLToRGB(Vector3 hsl)
        {
            float h = hsl.x;
            float s = hsl.y;
            float l = hsl.z;
            
            float r, g, b;
            
            if (s == 0)
            {
                r = g = b = l; // achromatic
            }
            else
            {
                float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
                float p = 2f * l - q;
                r = HueToRGB(p, q, h + 1f/3f);
                g = HueToRGB(p, q, h);
                b = HueToRGB(p, q, h - 1f/3f);
            }
            
            return new Color(r, g, b, 1f);
        }
        
        private static float HueToRGB(float p, float q, float t)
        {
            if (t < 0f) t += 1f;
            if (t > 1f) t -= 1f;
            if (t < 1f/6f) return p + (q - p) * 6f * t;
            if (t < 1f/2f) return q;
            if (t < 2f/3f) return p + (q - p) * (2f/3f - t) * 6f;
            return p;
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
            
            
            EditorUtility.DisplayDialog(GetLocalizedText("maskExportComplete"), 
                string.Format(GetLocalizedText("maskExportMsg"), maskTexturePath), 
                GetLocalizedText("ok"));
        }
        
        private void ExportTexture()
        {
            if (originalTexture == null || meshSelections.Count == 0) return;
            
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
            
            
            EditorUtility.DisplayDialog(GetLocalizedText("textureExportComplete"), 
                string.Format(GetLocalizedText("textureExportMsg"), texturePath), 
                GetLocalizedText("ok"));
        }
        
        private Texture2D CreateMaskTexture()
        {
            Texture2D workingTexture;
            
            if (IsTextureReadable(originalTexture))
            {
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
            
            // Expand bounds by 2 pixels for mipmap support
            int minX = Mathf.Max(0, Mathf.Min(x0, Mathf.Min(x1, x2)) - 2);
            int maxX = Mathf.Min(texture.width - 1, Mathf.Max(x0, Mathf.Max(x1, x2)) + 2);
            int minY = Mathf.Max(0, Mathf.Min(y0, Mathf.Min(y1, y2)) - 2);
            int maxY = Mathf.Min(texture.height - 1, Mathf.Max(y0, Mathf.Max(y1, y2)) + 2);
            
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Vector2Int pixelCoord = new Vector2Int(x, y);
                    
                    if (!paintedPixels.Contains(pixelCoord))
                    {
                        bool isInTriangle = IsPointInTriangle(x, y, x0, y0, x1, y1, x2, y2);
                        float distanceToTriangle = 0f;
                        
                        if (!isInTriangle)
                        {
                            // Calculate distance to triangle for pixels outside triangle
                            distanceToTriangle = DistanceToTriangle(x, y, x0, y0, x1, y1, x2, y2);
                        }
                        
                        // Paint if inside triangle or within 2 pixels of triangle
                        if (isInTriangle || distanceToTriangle <= 2f)
                        {
                            paintedPixels.Add(pixelCoord);
                            texture.SetPixel(x, y, Color.white);
                        }
                    }
                }
            }
        }
        
        private void ResetToOriginal()
        {
            if (originalMaterials != null && targetMeshRenderer != null)
            {
                for (int i = 0; i < originalMaterials.Length; i++)
                {
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
                
                // Clear selections but keep material selection
                ClearAllSelections();
                
                // Restore material selection if it was previously selected
                if (selectedMaterialIndex >= 0 && selectedMaterialIndex < originalMaterials.Length)
                {
                    originalMaterial = originalMaterials[selectedMaterialIndex];
                    if (originalMaterial != null && originalMaterial.mainTexture != null)
                    {
                        originalTexture = originalMaterial.mainTexture as Texture2D;
                    }
                }
                
                RemovePreview();
            }
        }
        
        
        private void SetupTempCollider()
        {
            if (targetMeshRenderer == null || targetMesh == null) return;
            
            RemoveTempCollider();
            
            
            GameObject tempObject = new GameObject("TempMeshCollider");
            tempObject.transform.SetParent(targetMeshRenderer.transform, false);
            tempObject.layer = targetMeshRenderer.gameObject.layer;
            
            
            tempCollider = tempObject.AddComponent<MeshCollider>();
            
            Mesh bakedMesh = new Mesh();
            targetMeshRenderer.BakeMesh(bakedMesh);
            tempCollider.sharedMesh = bakedMesh;
            
            
            tempObject.hideFlags = HideFlags.HideAndDontSave;
            
            
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
                    return i;
                }
            }
            
            return -1;
        }
        
        private void RemoveVerticesFromSelections(HashSet<int> verticesToRemove)
        {
            
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
            
        }
        
        private void SetupSafetyComponent()
        {
            
            if (targetMeshRenderer != null && originalMaterials != null && originalMaterials.Length > 0)
            {
                for (int i = 0; i < originalMaterials.Length; i++)
                {
                }
                
                currentSafety = MeshColorMaterialSafety.CreateSafety(targetMeshRenderer, originalMaterials, windowGUID);
                if (currentSafety != null)
                {
                    
                    var components = targetMeshRenderer.gameObject.GetComponents<MeshColorMaterialSafety>();
                }
                else
                {
                }
            }
            else
            {
            }
        }
        
        private void RemoveSafetyComponent()
        {
            
            if (targetMeshRenderer != null)
            {
                var components = targetMeshRenderer.gameObject.GetComponents<MeshColorMaterialSafety>();
                
                MeshColorMaterialSafety.RemoveSafety(targetMeshRenderer);
                
                components = targetMeshRenderer.gameObject.GetComponents<MeshColorMaterialSafety>();
            }
            
            currentSafety = null;
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
                    case "avatarLockedHint": return "💡 アバターを変更するには「クリア」ボタンを押してください";
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
                    case "maxVertexCount": return "選択頂点数上限";
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
                    case "apply": return "アクション";
                    case "textureOutput": return "テクスチャ出力";
                    case "applyColor": return "適用";
                    case "exportMaskTexture": return "マスクをエクスポート";
                    case "exportTexture": return "テクスチャをエクスポート";
                    case "resetToOriginal": return "オリジナルにリセット";
                    case "materialSafetyHint": return "💡 オリジナルのマテリアルは上書きされません。複製されたファイルは kokoa/GeneratedMaterials と kokoa/GeneratedTextures に保存されます。";
                    
                    
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
                    case "avatarLockedHint": return "💡 To change avatar, press the \"Clear\" button";
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
                    case "maxVertexCount": return "Max Vertex Count";
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
                    case "apply": return "Apply";
                    case "textureOutput": return "Texture Output";
                    case "applyColor": return "Apply";
                    case "exportMaskTexture": return "Export Mask";
                    case "exportTexture": return "Export Texture";
                    case "resetToOriginal": return "Reset to Original";
                    case "materialSafetyHint": return "💡 Original materials are not overwritten. Duplicated files are saved to kokoa/GeneratedMaterials and kokoa/GeneratedTextures.";
                    
                    
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

    public struct TrianglePaintJob : IJob
    {
        public NativeArray<Color32> pixels;
        [ReadOnly] public NativeArray<Vector2> triangleUVs;
        [ReadOnly] public int textureWidth;
        [ReadOnly] public int textureHeight;
        [ReadOnly] public Color32 paintColor;
        [ReadOnly] public float strength;
        [ReadOnly] public int blendMode;
        
        public void Execute()
        {
            int triangleCount = triangleUVs.Length / 3;
            
            // Create a hashset equivalent using NativeArray for processed pixels
            NativeArray<bool> processedPixels = new NativeArray<bool>(textureWidth * textureHeight, Allocator.Temp);
            
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                int baseIndex = triangleIndex * 3;
                if (baseIndex + 2 >= triangleUVs.Length) continue;
                
                Vector2 uv0 = triangleUVs[baseIndex];
                Vector2 uv1 = triangleUVs[baseIndex + 1];
                Vector2 uv2 = triangleUVs[baseIndex + 2];
                
                // Skip invalid UVs
                if (uv0.x < 0 || uv0.x > 1 || uv0.y < 0 || uv0.y > 1 ||
                    uv1.x < 0 || uv1.x > 1 || uv1.y < 0 || uv1.y > 1 ||
                    uv2.x < 0 || uv2.x > 1 || uv2.y < 0 || uv2.y > 1) continue;
                
                int x0 = (int)(uv0.x * (textureWidth - 1));
                int y0 = (int)(uv0.y * (textureHeight - 1));
                int x1 = (int)(uv1.x * (textureWidth - 1));
                int y1 = (int)(uv1.y * (textureHeight - 1));
                int x2 = (int)(uv2.x * (textureWidth - 1));
                int y2 = (int)(uv2.y * (textureHeight - 1));
                
                // Expand bounds by 2 pixels for mipmap support and edge smoothing
                int minX = math.max(0, math.min(x0, math.min(x1, x2)) - 2);
                int maxX = math.min(textureWidth - 1, math.max(x0, math.max(x1, x2)) + 2);
                int minY = math.max(0, math.min(y0, math.min(y1, y2)) - 2);
                int maxY = math.min(textureHeight - 1, math.max(y0, math.max(y1, y2)) + 2);
                
                // Skip degenerate triangles
                if (maxX <= minX || maxY <= minY) continue;
                
                // Calculate triangle area for early culling (more generous limit)
                int area = (maxX - minX) * (maxY - minY);
                if (area > 50000) continue; // Skip extremely large triangles only
                
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        bool isInTriangle = IsPointInTriangleFast(x, y, x0, y0, x1, y1, x2, y2);
                        float distanceToTriangle = 0f;
                        
                        if (!isInTriangle)
                        {
                            // Fast distance calculation for edge smoothing
                            distanceToTriangle = DistanceToTriangleFast(x, y, x0, y0, x1, y1, x2, y2);
                        }
                        
                        // Paint if inside triangle or within 2 pixels of triangle (for smooth edges)
                        if (isInTriangle || distanceToTriangle <= 2f)
                        {
                            int pixelIndex = y * textureWidth + x;
                            
                            if (pixelIndex >= 0 && pixelIndex < pixels.Length && !processedPixels[pixelIndex])
                            {
                                processedPixels[pixelIndex] = true;
                                Color32 originalColor = pixels[pixelIndex];
                                Color32 blendedColor = ApplyBlendModeFast(originalColor, paintColor, blendMode, strength);
                                pixels[pixelIndex] = blendedColor;
                            }
                        }
                    }
                }
            }
            
            processedPixels.Dispose();
        }
        
        private bool IsPointInTriangleFast(int px, int py, int x0, int y0, int x1, int y1, int x2, int y2)
        {
            // Integer-based barycentric coordinates for speed
            int denom = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
            if (denom == 0) return false;
            
            int a = (y1 - y2) * (px - x2) + (x2 - x1) * (py - y2);
            int b = (y2 - y0) * (px - x2) + (x0 - x2) * (py - y2);
            
            if (denom > 0)
            {
                return a >= 0 && b >= 0 && (a + b) <= denom;
            }
            else
            {
                return a <= 0 && b <= 0 && (a + b) >= denom;
            }
        }
        
        private float DistanceToTriangleFast(int px, int py, int x0, int y0, int x1, int y1, int x2, int y2)
        {
            // Fast approximation of distance to triangle edges
            float dist1 = DistanceToLineSegmentFast(px, py, x0, y0, x1, y1);
            float dist2 = DistanceToLineSegmentFast(px, py, x1, y1, x2, y2);
            float dist3 = DistanceToLineSegmentFast(px, py, x2, y2, x0, y0);
            
            return math.min(dist1, math.min(dist2, dist3));
        }
        
        private float DistanceToLineSegmentFast(int px, int py, int x1, int y1, int x2, int y2)
        {
            int dx = x2 - x1;
            int dy = y2 - y1;
            int lengthSquared = dx * dx + dy * dy;
            
            if (lengthSquared == 0)
            {
                dx = px - x1;
                dy = py - y1;
                return math.sqrt(dx * dx + dy * dy);
            }
            
            float t = math.clamp((float)((px - x1) * dx + (py - y1) * dy) / lengthSquared, 0f, 1f);
            float projX = x1 + t * dx;
            float projY = y1 + t * dy;
            
            dx = (int)(px - projX);
            dy = (int)(py - projY);
            return math.sqrt(dx * dx + dy * dy);
        }
        
        private Color32 ApplyBlendModeFast(Color32 original, Color32 paint, int blendMode, float strength)
        {
            // Simplified but correct color blending
            if (strength <= 0) return original;
            
            Color originalColor = new Color(original.r / 255f, original.g / 255f, original.b / 255f, original.a / 255f);
            Color paintColor = new Color(paint.r / 255f, paint.g / 255f, paint.b / 255f, paint.a / 255f);
            Color result;
            
            switch (blendMode)
            {
                case 1: // Multiply
                    result = originalColor * paintColor;
                    break;
                case 2: // Additive
                    result = originalColor + paintColor;
                    break;
                case 3: // Overlay
                    result = new Color(
                        originalColor.r < 0.5f ? 2f * originalColor.r * paintColor.r : 1f - 2f * (1f - originalColor.r) * (1f - paintColor.r),
                        originalColor.g < 0.5f ? 2f * originalColor.g * paintColor.g : 1f - 2f * (1f - originalColor.g) * (1f - paintColor.g),
                        originalColor.b < 0.5f ? 2f * originalColor.b * paintColor.b : 1f - 2f * (1f - originalColor.b) * (1f - paintColor.b),
                        originalColor.a
                    );
                    break;
                default: // Color (0) - Photoshop-style
                    // Apply hue and saturation while preserving luminance
                    Vector3 baseHSL = MeshColorEditorWindow.RGBToHSL(originalColor);
                    Vector3 blendHSL = MeshColorEditorWindow.RGBToHSL(paintColor);
                    Vector3 resultHSL = new Vector3(blendHSL.x, blendHSL.y, baseHSL.z);
                    result = MeshColorEditorWindow.HSLToRGB(resultHSL);
                    result.a = originalColor.a;
                    break;
            }
            
            // Lerp for strength
            result = Color.Lerp(originalColor, result, strength);
            
            return new Color32(
                (byte)(math.clamp(result.r, 0f, 1f) * 255f),
                (byte)(math.clamp(result.g, 0f, 1f) * 255f),
                (byte)(math.clamp(result.b, 0f, 1f) * 255f),
                255
            );
        }
    }
}