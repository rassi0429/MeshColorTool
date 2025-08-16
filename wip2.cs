using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;

namespace VRChatAvatarTools
{
    /// <summary>
    /// MeshColorToolのフェイルセーフコンポーネント
    /// 編集中にツールがクラッシュしても元のマテリアルに戻すことを保証する
    /// </summary>
    [ExecuteInEditMode]
    public class MeshColorMaterialSafety : MonoBehaviour
    {
        [SerializeField, HideInInspector]
        private Material originalMaterial;
        
        [SerializeField, HideInInspector]
        private SkinnedMeshRenderer targetRenderer;
        
        [SerializeField, HideInInspector]
        private bool isToolActive = false;
        
        [SerializeField, HideInInspector]
        private string toolWindowGUID;
        
        private static Dictionary<SkinnedMeshRenderer, MeshColorMaterialSafety> activeSafeties = new Dictionary<SkinnedMeshRenderer, MeshColorMaterialSafety>();
        
#if UNITY_EDITOR
        /// <summary>
        /// 編集開始時にセーフティコンポーネントを作成
        /// </summary>
        public static MeshColorMaterialSafety CreateSafety(SkinnedMeshRenderer renderer, Material originalMat, string windowGUID)
        {
            if (renderer == null || originalMat == null) 
            {
                Debug.LogWarning("[MeshColorSafety] Cannot create safety: renderer or material is null");
                return null;
            }
            
            // 既存のセーフティがあれば削除
            RemoveSafety(renderer);
            
            // 新しいセーフティコンポーネントを追加
            MeshColorMaterialSafety safety = renderer.gameObject.AddComponent<MeshColorMaterialSafety>();
            safety.originalMaterial = originalMat;
            safety.targetRenderer = renderer;
            safety.isToolActive = true;
            safety.toolWindowGUID = windowGUID;
            
            // 辞書に登録
            activeSafeties[renderer] = safety;
            
            // コンポーネントを隠す（インスペクターに表示しない）
            // デバッグのため一時的にコメントアウト
            // safety.hideFlags = HideFlags.HideInInspector;
            
            Debug.Log($"[MeshColorSafety] Safety component created for {renderer.name} on GameObject: {renderer.gameObject.name}");
            Debug.Log($"[MeshColorSafety] Original material: {originalMat.name}");
            Debug.Log($"[MeshColorSafety] Component count on object: {renderer.gameObject.GetComponents<Component>().Length}");
            
            return safety;
        }
        
        /// <summary>
        /// セーフティコンポーネントを削除
        /// </summary>
        public static void RemoveSafety(SkinnedMeshRenderer renderer)
        {
            if (renderer == null) 
            {
                Debug.Log("[MeshColorSafety] RemoveSafety called with null renderer");
                return;
            }
            
            // GameObjectに直接アタッチされているコンポーネントも確認
            MeshColorMaterialSafety[] existingComponents = renderer.gameObject.GetComponents<MeshColorMaterialSafety>();
            if (existingComponents.Length > 0)
            {
                Debug.Log($"[MeshColorSafety] Found {existingComponents.Length} safety components on {renderer.name}");
                foreach (var comp in existingComponents)
                {
                    if (comp != null)
                    {
                        comp.RestoreOriginalMaterial();
                        if (Application.isPlaying)
                        {
                            Destroy(comp);
                        }
                        else
                        {
                            DestroyImmediate(comp);
                        }
                    }
                }
            }
            
            if (activeSafeties.ContainsKey(renderer))
            {
                activeSafeties.Remove(renderer);
                Debug.Log($"[MeshColorSafety] Safety removed from dictionary for {renderer.name}");
            }
        }
        
        /// <summary>
        /// ツールがアクティブかチェック
        /// </summary>
        public void CheckToolStatus()
        {
            // MeshColorEditorWindowが存在するかチェック
            bool windowExists = false;
            
            EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var window in windows)
            {
                if (window != null && window.GetType().Name == "MeshColorEditorWindow")
                {
                    // ウィンドウのGUIDをチェック（将来的な拡張用）
                    windowExists = true;
                    break;
                }
            }
            
            if (!windowExists && isToolActive)
            {
                Debug.Log($"[MeshColorSafety] Tool window not found, restoring material for {targetRenderer.name}");
                RestoreAndRemove();
            }
        }
#endif
        
        /// <summary>
        /// 元のマテリアルに戻す
        /// </summary>
        private void RestoreOriginalMaterial()
        {
            if (targetRenderer != null && originalMaterial != null)
            {
                targetRenderer.sharedMaterial = originalMaterial;
                Debug.Log($"[MeshColorSafety] Material restored for {targetRenderer.name}");
            }
        }
        
        /// <summary>
        /// マテリアルを戻してコンポーネントを削除
        /// </summary>
        private void RestoreAndRemove()
        {
            RestoreOriginalMaterial();
            isToolActive = false;
            
            if (targetRenderer != null && activeSafeties.ContainsKey(targetRenderer))
            {
                activeSafeties.Remove(targetRenderer);
            }
            
            // 自身を削除
            if (Application.isPlaying)
            {
                Destroy(this);
            }
            else
            {
                #if UNITY_EDITOR
                DestroyImmediate(this);
                #else
                Destroy(this);
                #endif
            }
        }
        
        private void Awake()
        {
            // エディタでのみ動作
            if (Application.isPlaying)
            {
                Destroy(this);
                return;
            }
        }
        
#if UNITY_EDITOR
        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                EditorApplication.update += OnEditorUpdate;
            }
        }
        
        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                EditorApplication.update -= OnEditorUpdate;
            }
        }
        
        private float lastCheckTime = 0f;
        private const float CHECK_INTERVAL = 1f; // 1秒ごとにチェック
        
        private void OnEditorUpdate()
        {
            if (!isToolActive) return;
            
            // 定期的にツールの状態をチェック
            if (Time.realtimeSinceStartup - lastCheckTime > CHECK_INTERVAL)
            {
                lastCheckTime = Time.realtimeSinceStartup;
                CheckToolStatus();
            }
        }
        
        /// <summary>
        /// 全てのセーフティをクリーンアップ（エディタ終了時など）
        /// </summary>
        [InitializeOnLoadMethod]
        private static void InitializeCleanup()
        {
            EditorApplication.quitting += CleanupAllSafeties;
            AssemblyReloadEvents.beforeAssemblyReload += CleanupAllSafeties;
        }
        
        private static void CleanupAllSafeties()
        {
            foreach (var kvp in activeSafeties)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.RestoreOriginalMaterial();
                    if (Application.isPlaying)
                    {
                        Destroy(kvp.Value);
                    }
                    else
                    {
                        DestroyImmediate(kvp.Value);
                    }
                }
            }
            activeSafeties.Clear();
        }
#endif
        
        private void OnDestroy()
        {
            // コンポーネントが削除される時、元のマテリアルに戻す
            if (!Application.isPlaying && isToolActive)
            {
                RestoreOriginalMaterial();
                
                if (targetRenderer != null && activeSafeties.ContainsKey(targetRenderer))
                {
                    activeSafeties.Remove(targetRenderer);
                }
            }
        }
    }
}
