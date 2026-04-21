#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class TitanRigRuntimePrefabBaker
{
    private const string TitanPrefabPath = "Assets/Resources/Prefabs/Titan.prefab";

    [MenuItem("Tools/TrashMan/Titan/Bake TitanRigRuntime Bone References")]
    public static void BakeTitanPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(TitanPrefabPath);
        if (root == null)
        {
            Debug.LogError($"[TitanRigRuntimeBaker] Failed to load prefab contents: {TitanPrefabPath}");
            return;
        }

        try
        {
            TitanRigRuntime rig = root.GetComponent<TitanRigRuntime>();
            if (rig == null)
            {
                Debug.LogError($"[TitanRigRuntimeBaker] TitanRigRuntime not found on prefab root: {TitanPrefabPath}");
                return;
            }

            bool ok = rig.BakeBoneReferencesInEditor();
            PrefabUtility.SaveAsPrefabAsset(root, TitanPrefabPath);

            Debug.Log($"[TitanRigRuntimeBaker] Bake complete. ok={ok}. Saved: {TitanPrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
