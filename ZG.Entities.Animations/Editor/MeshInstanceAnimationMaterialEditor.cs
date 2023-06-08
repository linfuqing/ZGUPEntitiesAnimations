using UnityEditor;
using UnityEngine;

namespace ZG
{
    [CustomEditor(typeof(MeshInstanceAnimationMaterialDatabase))]
    public class MeshInstanceAnimationMaterialEditor : Editor
    {
        [MenuItem("Assets/ZG/MeshInstance/Rebuild All Animation Materials")]
        [CommandEditor("MeshInstance", 1)]
        public static void RebuildAllAnimationMaterials()
        {
            MeshInstanceAnimationMaterialDatabase target;
            var guids = AssetDatabase.FindAssets("t:MeshInstanceAnimationMaterialDatabase");
            string path;
            int numGUIDs = guids.Length;
            for (int i = 0; i < numGUIDs; ++i)
            {
                path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (EditorUtility.DisplayCancelableProgressBar("Rebuild All Rigs", path, i * 1.0f / numGUIDs))
                    break;

                target = AssetDatabase.LoadAssetAtPath<MeshInstanceAnimationMaterialDatabase>(path);
                if (target == null)
                    continue;

                if (target.root == null)
                {
                    Debug.LogError($"{target.name} missing root", target.root);

                    continue;
                }

                target.Create();

                target.EditorMaskDirty();
            }

            EditorUtility.ClearProgressBar();
        }

        public override void OnInspectorGUI()
        {
            var target = (MeshInstanceAnimationMaterialDatabase)base.target;

            EditorGUI.BeginChangeCheck();
            target.root = EditorGUILayout.ObjectField("Root", target.root, typeof(Transform), true) as Transform;
            if (EditorGUI.EndChangeCheck() || GUILayout.Button("Rebuild"))
            {
                if (target.root != null)
                    target.Create();

                if (PrefabUtility.GetPrefabInstanceStatus(target.root) == PrefabInstanceStatus.Connected)
                {
                    /*if (PrefabUtility.GetPrefabAssetType(target.root) == PrefabAssetType.Model)
                        target.root = (Transform)PrefabUtility.GetPrefabInstanceHandle(target.root);
                    else*/
                        target.root = PrefabUtility.GetCorrespondingObjectFromSource(target.root);
                }

                target.EditorMaskDirty();
            }

            base.OnInspectorGUI();
        }
    }
}