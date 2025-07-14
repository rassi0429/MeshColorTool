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
        
        // Editing
        private Color additiveColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private float additiveStrength = 0.5f;
        
        // History
        private Material originalMaterial;
        private Texture2D originalTexture;
        private List<EditHistory> editHistories = new List<EditHistory>();
        
        // Preview
        private Material previewMaterial;
        private bool showPreview = true;
        
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
            DrawColorSettings();
            
            EditorGUILayout.Space();
            DrawActions();
            
            EditorGUILayout.Space();
            DrawHistory();
            
            EditorGUILayout.Space();
            DrawDebugInfo();
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
                    }
                    
                    // Setup temporary collider for raycasting
                    SetupTempCollider();
                }
                
                ClearSelection();
            }
            
            if (targetMeshRenderer != null)
            {
                EditorGUILayout.LabelField("Mesh: " + targetMesh.name);
                EditorGUILayout.LabelField("Vertices: " + targetMesh.vertexCount);
                EditorGUILayout.LabelField("Material: " + (originalMaterial != null ? originalMaterial.name : "None"));
                
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
                EditorGUILayout.HelpBox("Click on hair strands in Scene View to select. Scene selection is disabled.", MessageType.Info);
            }
            
            EditorGUILayout.LabelField("Selected Vertices: " + selectedVertices.Count);
            
            if (GUILayout.Button("Clear Selection"))
            {
                ClearSelection();
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
        
        private void DrawColorSettings()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Color Settings", EditorStyles.boldLabel);
            
            additiveColor = EditorGUILayout.ColorField("Additive Color", additiveColor);
            additiveStrength = EditorGUILayout.Slider("Strength", additiveStrength, 0f, 1f);
            
            showPreview = EditorGUILayout.Toggle("Show Preview", showPreview);
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawActions()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            
            GUI.enabled = selectedVertices.Count > 0 && originalTexture != null;
            
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
            if (e.type == EventType.MouseMove)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                debugInfo = $"Mouse Ray Origin: {ray.origin}\n";
                debugInfo += $"Mouse Ray Direction: {ray.direction}\n";
                Repaint();
            }
            
            if (e.type == EventType.MouseDown && e.button == 0 && isSelectionMode)
            {
                debugInfo += "\n=== CLICK EVENT ===\n";
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
                
                Repaint();
                e.Use();
            }
            
            // Also handle mouse up to prevent selection
            if (e.type == EventType.MouseUp && e.button == 0 && isSelectionMode)
            {
                e.Use();
            }
            
            // Draw selected vertices
            if (selectedVertices.Count > 0 && showPreview)
            {
                DrawSelectedVertices();
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
                
                Repaint();
                SceneView.RepaintAll();
            }
        }
        
        private void DrawSelectedVertices()
        {
            if (targetMeshRenderer == null || selectedVertices.Count == 0) return;
            
            Handles.color = new Color(1f, 0.5f, 0f, 0.5f);
            Transform meshTransform = targetMeshRenderer.transform;
            Vector3[] vertices = targetMesh.vertices;
            
            foreach (int vertexIndex in selectedVertices)
            {
                if (vertexIndex < vertices.Length)
                {
                    Vector3 worldPos = meshTransform.TransformPoint(vertices[vertexIndex]);
                    Handles.SphereHandleCap(0, worldPos, Quaternion.identity, 0.01f, EventType.Repaint);
                }
            }
        }
        
        private void ApplyColorToSelection()
        {
            if (originalTexture == null || selectedVertices.Count == 0) return;
            
            // Create new texture
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string newTexturePath = $"Assets/GeneratedTextures/{originalTexture.name}_edited_{timestamp}.png";
            
            // Ensure directory exists
            if (!AssetDatabase.IsValidFolder("Assets/GeneratedTextures"))
            {
                AssetDatabase.CreateFolder("Assets", "GeneratedTextures");
            }
            
            // Copy and modify texture
            Texture2D newTexture = CreateModifiedTexture();
            
            // Save texture
            byte[] pngData = newTexture.EncodeToPNG();
            System.IO.File.WriteAllBytes(newTexturePath, pngData);
            AssetDatabase.Refresh();
            
            // Load saved texture
            TextureImporter importer = AssetImporter.GetAtPath(newTexturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
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
            ClearSelection();
        }
        
        private Texture2D CreateModifiedTexture()
        {
            // Create readable copy of original texture
            RenderTexture tmp = RenderTexture.GetTemporary(
                originalTexture.width,
                originalTexture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear
            );
            
            Graphics.Blit(originalTexture, tmp);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = tmp;
            
            Texture2D newTexture = new Texture2D(originalTexture.width, originalTexture.height);
            newTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
            newTexture.Apply();
            
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tmp);
            
            // Get UV coordinates for selected vertices
            Vector2[] uvs = targetMesh.uv;
            
            // Paint on texture
            foreach (int triangleIndex in selectedTriangles)
            {
                int baseIndex = triangleIndex * 3;
                int[] triangles = targetMesh.triangles;
                
                if (baseIndex + 2 < triangles.Length)
                {
                    Vector2 uv0 = uvs[triangles[baseIndex]];
                    Vector2 uv1 = uvs[triangles[baseIndex + 1]];
                    Vector2 uv2 = uvs[triangles[baseIndex + 2]];
                    
                    PaintTriangleOnTexture(newTexture, uv0, uv1, uv2);
                }
            }
            
            newTexture.Apply();
            return newTexture;
        }
        
        private void PaintTriangleOnTexture(Texture2D texture, Vector2 uv0, Vector2 uv1, Vector2 uv2)
        {
            // Convert UV to pixel coordinates
            int x0 = Mathf.RoundToInt(uv0.x * texture.width);
            int y0 = Mathf.RoundToInt(uv0.y * texture.height);
            int x1 = Mathf.RoundToInt(uv1.x * texture.width);
            int y1 = Mathf.RoundToInt(uv1.y * texture.height);
            int x2 = Mathf.RoundToInt(uv2.x * texture.width);
            int y2 = Mathf.RoundToInt(uv2.y * texture.height);
            
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
                    if (IsPointInTriangle(x, y, x0, y0, x1, y1, x2, y2))
                    {
                        Color originalColor = texture.GetPixel(x, y);
                        Color blendedColor = Color.Lerp(originalColor, originalColor + additiveColor, additiveStrength);
                        texture.SetPixel(x, y, blendedColor);
                    }
                }
            }
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
        
        private void ClearSelection()
        {
            selectedVertices.Clear();
            selectedTriangles.Clear();
            SceneView.RepaintAll();
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
            Repaint();
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
        
        private void CleanupPreview()
        {
            if (previewMaterial != null)
            {
                DestroyImmediate(previewMaterial);
            }
        }
        
        [System.Serializable]
        private class EditHistory
        {
            public string timestamp;
            public Material material;
            public Texture2D texture;
            public HashSet<int> selectedVertices;
        }
    }
}
