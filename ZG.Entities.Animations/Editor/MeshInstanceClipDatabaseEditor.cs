using UnityEngine;
using UnityEditor;

namespace ZG
{
    [CustomEditor(typeof(MeshInstanceClipDatabase))]
    public class MeshInstanceClipDatabaseEditor : Editor
    {
        [MenuItem("Assets/ZG/MeshInstance/Rebuild All Clips")]
        public static void RebuildAllRenderers()
        {
            MeshInstanceClipDatabase target;
            var guids = AssetDatabase.FindAssets("t:MeshInstanceClipDatabase");
            string path;
            int numGUIDs = guids.Length;
            for (int i = 0; i < numGUIDs; ++i)
            {
                path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (EditorUtility.DisplayCancelableProgressBar("Rebuild All Clips", path, i * 1.0f / numGUIDs))
                    break;

                target = AssetDatabase.LoadAssetAtPath<MeshInstanceClipDatabase>(path);
                if (target == null)
                    continue;

                if (target.root == null)
                {
                    Debug.LogError($"{target.name} missing root", target.root);

                    continue;
                }

                MeshInstanceClipDatabase.Data.isShowProgressBar = false;

                target.Create();

                target.EditorMaskDirty();
            }

            EditorUtility.ClearProgressBar();
        }

        public override void OnInspectorGUI()
        {
            var target = (MeshInstanceClipDatabase)base.target;

            bool isRebuild = false;

            EditorGUI.BeginChangeCheck();
            target.root = EditorGUILayout.ObjectField("Root", target.root, typeof(Transform), true) as Transform;
            if (EditorGUI.EndChangeCheck() || GUILayout.Button("Rebuild"))
            {
                if (target.root != null)
                    target.Create();

                if (PrefabUtility.GetPrefabInstanceStatus(target.root) == PrefabInstanceStatus.Connected)
                    target.root = PrefabUtility.GetCorrespondingObjectFromSource(target.root);

                isRebuild = true;
            }

            isRebuild = GUILayout.Button("Reset") || isRebuild;
            if(isRebuild)
                target.EditorMaskDirty();

            base.OnInspectorGUI();
        }
    }
}