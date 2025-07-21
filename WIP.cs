using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System;

namespace VRChatAvatarTools
{
    public class HairMeshEditorWindow : EditorWindow
    {
        // Target mesh
        private GameObject targetAvatar;
        private SkinnedMeshRenderer targetMeshRenderer;
        private Mesh targetMesh;
        private MeshCollider tempCollider;
        
        // Selection
        private bool isSelectionMode = false;
        private HashSet<int> selectedVertices = new HashSet<int>();
        private List<int> selectedTriangles = new List<int>();
        
        // Multiple selection support
        private List<HairStrandSelection> hairStrandSelections = new List<HairStrandSelection>();
        private int activeSelectionIndex = -1;
        private bool addToSelection = false; // Shift key to add to selection
        private Vector2 selectionScrollPos;
        
        // Editing
        private Color blendColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private float blendStrength = 0.5f;
        private enum BlendMode { Additive, Multiply, Color, Overlay }
        private BlendMode currentBlendMode = BlendMode.Color;
        
        // History
        private Material originalMaterial;
        private Texture2D originalTexture;
        private List<EditHistory> editHistories = new List<EditHistory>();
        
        // Preview
        private Material previewMaterial;
        private bool showPreview = true;
        private Texture2D previewTexture;
        private bool needsPreviewUpdate = false;
        
        // Debug information
        private string debugInfo = "";
        private Vector3 lastRaycastPoint;
        private bool lastRaycastHit = false;
        
        [MenuItem("VRChat Avatar Tools/Hair Mesh Editor")]
        public static void ShowWindow()
        {
            HairMeshEditorWindow window = GetWindow<HairMeshEditorWindow>();
            window.titleContent = new GUIContent("Hair Mesh Editor");
            window.minSize = new Vector2(400, 600);
        }
        
        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }
        
        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            CleanupPreview();
            RemoveTempCollider();
        }
        
        private void OnGUI()
        {
            EditorGUILayout.Space();
            
            // Header
            EditorGUILayout.LabelField("VRChat Hair Mesh Editor", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // Target Selection
            DrawTargetSelection();
            
            if (targetMeshRenderer == null)
            {
                EditorGUILayout.HelpBox("Please select an avatar with SkinnedMeshRenderer", MessageType.Info);
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
            DrawHistory();
            
            EditorGUILayout.Space();
            DrawDebugInfo();
            
            // Update preview if needed
            if (needsPreviewUpdate && showPreview && hairStrandSelections.Count > 0)
            {
                UpdatePreview();
                needsPreviewUpdate = false;
            }
        }
        
        private void DrawTargetSelection()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Target Mesh", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            targetAvatar = EditorGUILayout.ObjectField("Avatar", targetAvatar, typeof(GameObject), true) as GameObject;
            
            if (EditorGUI.EndChangeCheck() && targetAvatar != null)
            {
                // Find SkinnedMeshRenderer
                targetMeshRenderer = targetAvatar.GetComponentInChildren<SkinnedMeshRenderer>();
                if (targetMeshRenderer != null)
                {
                    targetMesh = targetMeshRenderer.sharedMesh;
                    originalMaterial = targetMeshRenderer.sharedMaterial;
                    
                    if (originalMaterial != null && originalMaterial.mainTexture != null)
                    {
                        originalTexture = originalMaterial.mainTexture as Texture2D;
                        
                        // Check if texture is readable
                        if (!IsTextureReadable(originalTexture))
                        {
                            debugInfo += "Original texture is not readable. Will create a copy when needed.\n";
                        }
                    }
                    
                    // Setup temporary collider for raycasting
                    SetupTempCollider();
                }
                
                ClearAllSelections();
            }
            
            if (targetMeshRenderer != null)
            {
                EditorGUILayout.LabelField("Mesh: " + targetMesh.name);
                EditorGUILayout.LabelField("Vertices: " + targetMesh.vertexCount);
                EditorGUILayout.LabelField("Material: " + (originalMaterial != null ? originalMaterial.name : "None"));
                
                if (originalTexture != null)
                {
                    bool isReadable = IsTextureReadable(originalTexture);
                    EditorGUILayout.LabelField("Texture Readable: " + (isReadable ? "Yes" : "No (will copy)"), 
                        isReadable ? EditorStyles.miniLabel : EditorStyles.miniBoldLabel);
                }
                
                if (tempCollider != null)
                {
                    EditorGUILayout.LabelField("Status: Collider Ready", EditorStyles.miniLabel);
                }
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawSelectionMode()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Hair Strand Selection", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            isSelectionMode = EditorGUILayout.Toggle("Selection Mode", isSelectionMode);
            
            if (EditorGUI.EndChangeCheck())
            {
                // Force scene view to update when toggling selection mode
                SceneView.RepaintAll();
                
                // Lock/unlock scene view selection
                if (isSelectionMode)
                {
                    Tools.current = Tool.None;
                    // Ensure collider is set up when entering selection mode
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
            
            if (isSelectionMode)
            {
                EditorGUILayout.HelpBox("Click: Select new strand | Shift+Click: Add to selection", MessageType.Info);
            }
            
            EditorGUILayout.LabelField("Total Selected Vertices: " + GetTotalSelectedVertices());
            
            if (GUILayout.Button("Clear All Selections"))
            {
                ClearAllSelections();
            }
            
            // Debug button to manually create collider
            if (targetMeshRenderer != null && tempCollider == null)
            {
                if (GUILayout.Button("Setup Collider (Debug)"))
                {
                    SetupTempCollider();
                }
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawSelectionList()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Hair Strand Selections", EditorStyles.boldLabel);
            
            if (hairStrandSelections.Count == 0)
            {
                EditorGUILayout.LabelField("No strands selected", EditorStyles.miniLabel);
            }
            else
            {
                selectionScrollPos = EditorGUILayout.BeginScrollView(selectionScrollPos, GUILayout.Height(150));
                
                for (int i = 0; i < hairStrandSelections.Count; i++)
                {
                    var selection = hairStrandSelections[i];
                    
                    EditorGUILayout.BeginHorizontal();
                    
                    // Active selection highlight
                    bool isActive = (i == activeSelectionIndex);
                    GUI.backgroundColor = isActive ? Color.cyan : Color.white;
                    
                    // Selection button
                    if (GUILayout.Button($"Strand {i + 1} ({selection.vertices.Count} verts)", 
                        isActive ? EditorStyles.miniButtonMid : EditorStyles.miniButton))
                    {
                        SetActiveSelection(i);
                    }
                    
                    GUI.backgroundColor = Color.white;
                    
                    // Individual color override
                    selection.useCustomColor = EditorGUILayout.Toggle(selection.useCustomColor, GUILayout.Width(20));
                    if (selection.useCustomColor)
                    {
                        selection.customColor = EditorGUILayout.ColorField(selection.customColor, GUILayout.Width(50));
                    }
                    
                    // Remove button
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
                
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Merge Selected"))
                {
                    MergeSelections();
                }
                if (GUILayout.Button("Invert Active"))
                {
                    InvertActiveSelection();
                }
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawColorSettings()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Color Settings", EditorStyles.boldLabel);
            
            EditorGUI.BeginChangeCheck();
            blendColor = EditorGUILayout.ColorField("Blend Color", blendColor);
            blendStrength = EditorGUILayout.Slider("Strength", blendStrength, 0f, 1f);
            currentBlendMode = (BlendMode)EditorGUILayout.EnumPopup("Blend Mode", currentBlendMode);
            
            if (EditorGUI.EndChangeCheck())
            {
                needsPreviewUpdate = true;
            }
            
            EditorGUI.BeginChangeCheck();
            showPreview = EditorGUILayout.Toggle("Show Preview", showPreview);
            
            if (EditorGUI.EndChangeCheck())
            {
                if (showPreview && hairStrandSelections.Count > 0)
                {
                    needsPreviewUpdate = true;
                }
                else if (!showPreview)
                {
                    RemovePreview();
                }
            }
            
            // Explanation for blend modes
            EditorGUILayout.HelpBox(GetBlendModeDescription(), MessageType.Info);
            
            EditorGUILayout.EndVertical();
        }
        
        private string GetBlendModeDescription()
        {
            switch (currentBlendMode)
            {
                case BlendMode.Additive:
                    return "Adds color values (brightens)";
                case BlendMode.Multiply:
                    return "Multiplies color values (darkens)";
                case BlendMode.Color:
                    return "Applies hue and saturation while preserving luminance (Photoshop Color mode)";
                case BlendMode.Overlay:
                    return "Combines multiply and screen based on base color";
                default:
                    return "";
            }
        }
        
        private void DrawActions()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            
            GUI.enabled = hairStrandSelections.Count > 0 && originalTexture != null;
            
            if (GUILayout.Button("Apply Color", GUILayout.Height(30)))
            {
                ApplyColorToSelection();
            }
            
            GUI.enabled = true;
            
            if (GUILayout.Button("Reset to Original"))
            {
                ResetToOriginal();
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawHistory()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Edit History", EditorStyles.boldLabel);
            
            if (editHistories.Count == 0)
            {
                EditorGUILayout.LabelField("No edits yet");
            }
            else
            {
                for (int i = editHistories.Count - 1; i >= 0; i--)
                {
                    var history = editHistories[i];
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"{i + 1}. {history.timestamp}");
                    
                    if (GUILayout.Button("Revert", GUILayout.Width(60)))
                    {
                        RevertToHistory(history);
                    }
                    
                    EditorGUILayout.EndHorizontal();
                }
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawDebugInfo()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Debug Information", EditorStyles.boldLabel);
            
            EditorGUILayout.TextArea(debugInfo, GUILayout.Height(100));
            
            if (GUILayout.Button("Clear Debug"))
            {
                debugInfo = "";
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void OnSceneGUI(SceneView sceneView)
        {
            if (!isSelectionMode || targetMeshRenderer == null) return;
            
            Event e = Event.current;
            
            // Prevent GameObject selection in selection mode
            if (isSelectionMode)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                
                // Change cursor to indicate selection mode
                EditorGUIUtility.AddCursorRect(new Rect(0, 0, sceneView.position.width, sceneView.position.height), MouseCursor.CustomCursor);
            }
            
            // Debug: Show mouse position
            if (e.type == EventType.MouseMove && isSelectionMode)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                debugInfo = $"Mouse Ray Origin: {ray.origin}\n";
                debugInfo += $"Mouse Ray Direction: {ray.direction}\n";
                if (this != null) Repaint();
            }
            
            if (e.type == EventType.MouseDown && e.button == 0 && isSelectionMode)
            {
                // Check if shift key is held for adding to selection
                addToSelection = e.shift;
                
                debugInfo += "\n=== CLICK EVENT ===\n";
                debugInfo += $"Shift key: {addToSelection}\n";
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                debugInfo += $"Click Position: {e.mousePosition}\n";
                debugInfo += $"Ray: {ray.origin} -> {ray.direction}\n";
                
                // Update the baked mesh for accurate raycasting
                if (tempCollider != null && targetMeshRenderer != null)
                {
                    Mesh bakedMesh = new Mesh();
                    targetMeshRenderer.BakeMesh(bakedMesh);
                    tempCollider.sharedMesh = bakedMesh;
                    debugInfo += "Mesh baked for current pose\n";
                }
                
                // Try multiple raycast methods
                RaycastHit hit;
                bool hitSuccess = false;
                
                // Method 1: Standard Physics.Raycast
                if (Physics.Raycast(ray, out hit))
                {
                    hitSuccess = true;
                    debugInfo += $"Physics.Raycast HIT: {hit.collider.gameObject.name}\n";
                    debugInfo += $"Hit Point: {hit.point}\n";
                    debugInfo += $"Hit Distance: {hit.distance}\n";
                    
                    // Check if we hit our temp collider
                    if (hit.collider == tempCollider)
                    {
                        debugInfo += "HIT TEMP COLLIDER! Selecting strand...\n";
                        SelectHairStrand(hit.point);
                        lastRaycastPoint = hit.point;
                        lastRaycastHit = true;
                    }
                    else if (hit.collider.gameObject == targetMeshRenderer.gameObject)
                    {
                        debugInfo += "HIT TARGET MESH! Selecting strand...\n";
                        SelectHairStrand(hit.point);
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
                    
                    // Method 2: Try with longer distance
                    if (Physics.Raycast(ray, out hit, 1000f))
                    {
                        debugInfo += $"Long distance raycast HIT: {hit.collider.gameObject.name}\n";
                    }
                }
                
                // Method 3: Check collider status
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
            
            // Also handle mouse up to prevent selection
            if (e.type == EventType.MouseUp && e.button == 0 && isSelectionMode)
            {
                e.Use();
            }
            
            // Draw all selections with different colors
            if (showPreview)
            {
                DrawAllSelections();
            }
            
            // Draw debug visualization
            if (lastRaycastHit)
            {
                Handles.color = Color.red;
                Handles.DrawWireCube(lastRaycastPoint, Vector3.one * 0.05f);
            }
        }
        
        private void SelectHairStrand(Vector3 hitPoint)
        {
            if (targetMesh == null) return;
            
            debugInfo += "\n--- SelectHairStrand CALLED ---\n";
            debugInfo += $"Hit Point: {hitPoint}\n";
            
            // Find nearest vertex
            Vector3[] vertices = targetMesh.vertices;
            Transform meshTransform = targetMeshRenderer.transform;
            
            debugInfo += $"Total vertices: {vertices.Length}\n";
            
            int nearestVertex = -1;
            float nearestDistance = float.MaxValue;
            
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPos = meshTransform.TransformPoint(vertices[i]);
                float distance = Vector3.Distance(worldPos, hitPoint);
                
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestVertex = i;
                }
            }
            
            debugInfo += $"Nearest vertex: {nearestVertex} (distance: {nearestDistance})\n";
            
            if (nearestVertex >= 0)
            {
                // Find connected vertices (hair strand)
                selectedVertices.Clear();
                selectedTriangles.Clear();
                
                HashSet<int> visited = new HashSet<int>();
                Queue<int> queue = new Queue<int>();
                
                queue.Enqueue(nearestVertex);
                visited.Add(nearestVertex);
                
                int[] triangles = targetMesh.triangles;
                debugInfo += $"Total triangles: {triangles.Length / 3}\n";
                
                int processedCount = 0;
                while (queue.Count > 0 && processedCount < 1000) // Limit to prevent infinite loop
                {
                    processedCount++;
                    int currentVertex = queue.Dequeue();
                    selectedVertices.Add(currentVertex);
                    
                    // Find all triangles containing this vertex
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
                            // Add triangle indices
                            selectedTriangles.Add(i / 3);
                            
                            // Add connected vertices
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
                
                debugInfo += $"Selected vertices: {selectedVertices.Count}\n";
                debugInfo += $"Selected triangles: {selectedTriangles.Count}\n";
                debugInfo += $"Processed iterations: {processedCount}\n";
                
                // Create or update selection
                if (!addToSelection || hairStrandSelections.Count == 0)
                {
                    // Create new selection (replace current)
                    hairStrandSelections.Clear();
                }
                
                // Add new selection
                var newSelection = new HairStrandSelection
                {
                    vertices = new HashSet<int>(selectedVertices),
                    triangles = new List<int>(selectedTriangles)
                };
                
                hairStrandSelections.Add(newSelection);
                activeSelectionIndex = hairStrandSelections.Count - 1;
                
                debugInfo += $"Total selections: {hairStrandSelections.Count}\n";
                
                // Update preview
                if (showPreview)
                {
                    needsPreviewUpdate = true;
                }
                
                // Schedule repaint instead of immediate
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
            
            // Draw all selections with different colors
            for (int i = 0; i < hairStrandSelections.Count; i++)
            {
                var selection = hairStrandSelections[i];
                bool isActive = (i == activeSelectionIndex);
                
                // Choose color based on selection index and active state
                Color baseColor = Color.HSVToRGB((float)i / Mathf.Max(hairStrandSelections.Count, 1f), 0.8f, 1f);
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
            if (originalTexture == null || hairStrandSelections.Count == 0) return;
            
            // Remove preview first
            RemovePreview();
            
            // Create new texture
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string newTexturePath = $"Assets/GeneratedTextures/{originalTexture.name}_edited_{timestamp}.png";
            
            // Ensure directory exists
            if (!AssetDatabase.IsValidFolder("Assets/GeneratedTextures"))
            {
                AssetDatabase.CreateFolder("Assets", "GeneratedTextures");
            }
            
            // Copy and modify texture with all selections
            Texture2D newTexture = CreateModifiedTextureWithAllSelections();
            
            if (newTexture == null)
            {
                EditorUtility.DisplayDialog("Error", "Failed to create texture. Please check if the original texture has Read/Write enabled.", "OK");
                return;
            }
            
            // Save texture
            byte[] pngData = newTexture.EncodeToPNG();
            System.IO.File.WriteAllBytes(newTexturePath, pngData);
            AssetDatabase.Refresh();
            
            // Load saved texture with proper import settings
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
            
            // Create new material
            string newMaterialPath = $"Assets/GeneratedMaterials/{originalMaterial.name}_edited_{timestamp}.mat";
            
            if (!AssetDatabase.IsValidFolder("Assets/GeneratedMaterials"))
            {
                AssetDatabase.CreateFolder("Assets", "GeneratedMaterials");
            }
            
            Material newMaterial = new Material(originalMaterial);
            newMaterial.mainTexture = savedTexture;
            
            AssetDatabase.CreateAsset(newMaterial, newMaterialPath);
            AssetDatabase.SaveAssets();
            
            // Apply to renderer
            targetMeshRenderer.sharedMaterial = newMaterial;
            
            // Add to history
            EditHistory history = new EditHistory
            {
                timestamp = timestamp,
                material = newMaterial,
                texture = savedTexture,
                selectedVertices = new HashSet<int>(selectedVertices)
            };
            
            editHistories.Add(history);
            
            // Clear selection
            ClearAllSelections();
            
            debugInfo += $"\nTexture saved: {newTexturePath}\n";
            debugInfo += $"Material saved: {newMaterialPath}\n";
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
            // Create a temporary RenderTexture
            RenderTexture tmp = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB
            );
            
            // Copy the source texture to the RenderTexture
            Graphics.Blit(source, tmp);
            
            // Set the RenderTexture as active
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = tmp;
            
            // Create a new readable Texture2D and read the pixels
            Texture2D readableTexture = new Texture2D(source.width, source.height, TextureFormat.ARGB32, false);
            readableTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
            readableTexture.Apply();
            
            // Reset active RenderTexture
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tmp);
            
            return readableTexture;
        }
        
        private Texture2D CreateModifiedTextureWithAllSelections()
        {
            Texture2D workingTexture;
            
            // Check if original texture is readable
            if (IsTextureReadable(originalTexture))
            {
                debugInfo += "Using original texture (readable)\n";
                // Create a copy of the original texture
                workingTexture = new Texture2D(originalTexture.width, originalTexture.height, TextureFormat.ARGB32, false);
                workingTexture.SetPixels(originalTexture.GetPixels());
                workingTexture.Apply();
            }
            else
            {
                debugInfo += "Creating readable copy of texture\n";
                // Create readable copy using RenderTexture
                workingTexture = GetReadableTexture(originalTexture);
            }
            
            // Get UV coordinates
            Vector2[] uvs = targetMesh.uv;
            
            // Apply color for each selection
            foreach (var selection in hairStrandSelections)
            {
                HashSet<Vector2Int> paintedPixels = new HashSet<Vector2Int>();
                
                // Determine color to use
                Color colorToUse = selection.useCustomColor ? selection.customColor : blendColor;
                float strengthToUse = selection.useCustomColor ? selection.customStrength : blendStrength;
                
                // Paint triangles for this selection
                foreach (int triangleIndex in selection.triangles)
                {
                    int baseIndex = triangleIndex * 3;
                    int[] triangles = targetMesh.triangles;
                    
                    if (baseIndex + 2 < triangles.Length)
                    {
                        Vector2 uv0 = uvs[triangles[baseIndex]];
                        Vector2 uv1 = uvs[triangles[baseIndex + 1]];
                        Vector2 uv2 = uvs[triangles[baseIndex + 2]];
                        
                        PaintTriangleOnTextureWithColor(workingTexture, uv0, uv1, uv2, paintedPixels, colorToUse, strengthToUse);
                    }
                }
            }
            
            workingTexture.Apply();
            return workingTexture;
        }
        
        private void PaintTriangleOnTexture(Texture2D texture, Vector2 uv0, Vector2 uv1, Vector2 uv2, HashSet<Vector2Int> paintedPixels)
        {
            PaintTriangleOnTextureWithColor(texture, uv0, uv1, uv2, paintedPixels, blendColor, blendStrength);
        }
        
        private void PaintTriangleOnTextureWithColor(Texture2D texture, Vector2 uv0, Vector2 uv1, Vector2 uv2, 
            HashSet<Vector2Int> paintedPixels, Color color, float strength)
        {
            // Convert UV to pixel coordinates
            int x0 = Mathf.RoundToInt(uv0.x * (texture.width - 1));
            int y0 = Mathf.RoundToInt(uv0.y * (texture.height - 1));
            int x1 = Mathf.RoundToInt(uv1.x * (texture.width - 1));
            int y1 = Mathf.RoundToInt(uv1.y * (texture.height - 1));
            int x2 = Mathf.RoundToInt(uv2.x * (texture.width - 1));
            int y2 = Mathf.RoundToInt(uv2.y * (texture.height - 1));
            
            // Get bounding box
            int minX = Mathf.Max(0, Mathf.Min(x0, Mathf.Min(x1, x2)));
            int maxX = Mathf.Min(texture.width - 1, Mathf.Max(x0, Mathf.Max(x1, x2)));
            int minY = Mathf.Max(0, Mathf.Min(y0, Mathf.Min(y1, y2)));
            int maxY = Mathf.Min(texture.height - 1, Mathf.Max(y0, Mathf.Max(y1, y2)));
            
            // Paint pixels inside triangle
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Vector2Int pixelCoord = new Vector2Int(x, y);
                    
                    // Only paint if we haven't painted this pixel yet and it's inside the triangle
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
                    // Photoshop Color mode: Apply hue and saturation while preserving luminance
                    float baseLuminance = GetLuminance(baseColor);
                    Color colorized = SetLuminance(blendColor, baseLuminance);
                    result = Color.Lerp(baseColor, colorized, strength);
                    break;
                    
                case BlendMode.Overlay:
                    // Overlay: multiply dark colors, screen light colors
                    result = new Color(
                        OverlayChannel(baseColor.r, blendColor.r, strength),
                        OverlayChannel(baseColor.g, blendColor.g, strength),
                        OverlayChannel(baseColor.b, blendColor.b, strength),
                        baseColor.a
                    );
                    break;
            }
            
            // Clamp values
            result.r = Mathf.Clamp01(result.r);
            result.g = Mathf.Clamp01(result.g);
            result.b = Mathf.Clamp01(result.b);
            result.a = baseColor.a; // Preserve original alpha
            
            return result;
        }
        
        private float GetLuminance(Color color)
        {
            // Use standard luminance calculation
            return 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
        }
        
        private Color SetLuminance(Color color, float targetLuminance)
        {
            float currentLuminance = GetLuminance(color);
            
            if (currentLuminance <= 0.0001f)
            {
                // If the color is black, return gray with target luminance
                return new Color(targetLuminance, targetLuminance, targetLuminance, color.a);
            }
            
            // Scale the color to match target luminance
            float scale = targetLuminance / currentLuminance;
            
            Color result = new Color(
                color.r * scale,
                color.g * scale,
                color.b * scale,
                color.a
            );
            
            // Handle cases where scaling causes values to exceed 1.0
            float maxComponent = Mathf.Max(result.r, Mathf.Max(result.g, result.b));
            if (maxComponent > 1.0f)
            {
                // Desaturate towards target luminance
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
        
        private void ResetToOriginal()
        {
            if (originalMaterial != null && targetMeshRenderer != null)
            {
                targetMeshRenderer.sharedMaterial = originalMaterial;
                RemovePreview();
            }
        }
        
        private void RevertToHistory(EditHistory history)
        {
            if (targetMeshRenderer != null && history.material != null)
            {
                targetMeshRenderer.sharedMaterial = history.material;
                selectedVertices = new HashSet<int>(history.selectedVertices);
                SceneView.RepaintAll();
            }
        }
        
        private void SetupTempCollider()
        {
            if (targetMeshRenderer == null || targetMesh == null) return;
            
            RemoveTempCollider();
            
            debugInfo += "\n=== SETUP TEMP COLLIDER ===\n";
            
            // Create a temporary GameObject with MeshCollider
            GameObject tempObject = new GameObject("TempHairCollider");
            tempObject.transform.SetParent(targetMeshRenderer.transform, false);
            tempObject.layer = targetMeshRenderer.gameObject.layer;
            
            debugInfo += $"Created temp object: {tempObject.name}\n";
            
            // Add MeshCollider
            tempCollider = tempObject.AddComponent<MeshCollider>();
            
            // For SkinnedMeshRenderer, we need to bake the mesh
            Mesh bakedMesh = new Mesh();
            targetMeshRenderer.BakeMesh(bakedMesh);
            tempCollider.sharedMesh = bakedMesh;
            
            debugInfo += $"Baked mesh vertices: {bakedMesh.vertexCount}\n";
            debugInfo += $"Collider enabled: {tempCollider.enabled}\n";
            
            // Hide the temporary object in hierarchy
            tempObject.hideFlags = HideFlags.HideAndDontSave;
            
            debugInfo += "Temporary collider created for raycasting\n";
            
            // Schedule repaint instead of immediate
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
            
            // Create or update preview material
            if (previewMaterial == null)
            {
                previewMaterial = new Material(originalMaterial);
                previewMaterial.name = "Hair Editor Preview";
            }
            
            // Create preview texture
            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
            }
            
            // Use the same method that creates the final texture
            previewTexture = CreateModifiedTextureWithAllSelections();
            previewMaterial.mainTexture = previewTexture;
            
            // Apply preview material
            targetMeshRenderer.sharedMaterial = previewMaterial;
            
            debugInfo += "Preview updated\n";
        }
        
        private void RemovePreview()
        {
            if (targetMeshRenderer != null && originalMaterial != null)
            {
                targetMeshRenderer.sharedMaterial = originalMaterial;
            }
            
            if (previewTexture != null)
            {
                DestroyImmediate(previewTexture);
                previewTexture = null;
            }
        }
        
        private void ClearSelection()
        {
            selectedVertices.Clear();
            selectedTriangles.Clear();
            SceneView.RepaintAll();
        }
        
        private void ClearAllSelections()
        {
            hairStrandSelections.Clear();
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
            foreach (var selection in hairStrandSelections)
            {
                allVertices.UnionWith(selection.vertices);
            }
            return allVertices.Count;
        }
        
        private void SetActiveSelection(int index)
        {
            if (index >= 0 && index < hairStrandSelections.Count)
            {
                activeSelectionIndex = index;
                var selection = hairStrandSelections[index];
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
            if (index >= 0 && index < hairStrandSelections.Count)
            {
                hairStrandSelections.RemoveAt(index);
                
                // Update active selection index
                if (activeSelectionIndex >= hairStrandSelections.Count)
                {
                    activeSelectionIndex = hairStrandSelections.Count - 1;
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
        
        private void MergeSelections()
        {
            if (hairStrandSelections.Count <= 1) return;
            
            HashSet<int> mergedVertices = new HashSet<int>();
            HashSet<int> mergedTriangles = new HashSet<int>();
            
            foreach (var selection in hairStrandSelections)
            {
                mergedVertices.UnionWith(selection.vertices);
                mergedTriangles.UnionWith(selection.triangles);
            }
            
            hairStrandSelections.Clear();
            hairStrandSelections.Add(new HairStrandSelection
            {
                vertices = mergedVertices,
                triangles = new List<int>(mergedTriangles)
            });
            
            activeSelectionIndex = 0;
            SetActiveSelection(0);
        }
        
        private void InvertActiveSelection()
        {
            if (activeSelectionIndex < 0 || targetMesh == null) return;
            
            HashSet<int> allVertices = new HashSet<int>();
            for (int i = 0; i < targetMesh.vertexCount; i++)
            {
                allVertices.Add(i);
            }
            
            allVertices.ExceptWith(selectedVertices);
            
            // Create new selection with inverted vertices
            var invertedSelection = new HairStrandSelection
            {
                vertices = allVertices,
                triangles = new List<int>()
            };
            
            // Find triangles for inverted selection
            int[] triangles = targetMesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                bool hasSelectedVertex = false;
                for (int j = 0; j < 3; j++)
                {
                    if (allVertices.Contains(triangles[i + j]))
                    {
                        hasSelectedVertex = true;
                        break;
                    }
                }
                
                if (hasSelectedVertex)
                {
                    invertedSelection.triangles.Add(i / 3);
                }
            }
            
            hairStrandSelections.Add(invertedSelection);
            activeSelectionIndex = hairStrandSelections.Count - 1;
            SetActiveSelection(activeSelectionIndex);
        }
        
        [System.Serializable]
        private class EditHistory
        {
            public string timestamp;
            public Material material;
            public Texture2D texture;
            public HashSet<int> selectedVertices;
        }
        
        [System.Serializable]
        private class HairStrandSelection
        {
            public HashSet<int> vertices = new HashSet<int>();
            public List<int> triangles = new List<int>();
            public bool useCustomColor = false;
            public Color customColor = Color.white;
            public float customStrength = 0.5f;
        }
    }
}
