using UnityEngine;
using UnityEditor;

namespace ZG
{
    [CustomEditor(typeof(MeshInstanceRigDatabase))]
    public class MeshInstanceRigDatabaseEditor : Editor
    {
        [MenuItem("Assets/ZG/MeshInstance/Rebuild All Rigs")]
        [CommandEditor("MeshInstance", 0)]
        public static void RebuildAllRigs()
        {
            MeshInstanceRigDatabase target;
            var guids = AssetDatabase.FindAssets("t:MeshInstanceRigDatabase");
            string path;
            int numGUIDs = guids.Length;
            for (int i = 0; i < numGUIDs; ++i)
            {
                path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (EditorUtility.DisplayCancelableProgressBar("Rebuild All Rigs", path, i * 1.0f / numGUIDs))
                    break;

                target = AssetDatabase.LoadAssetAtPath<MeshInstanceRigDatabase>(path);
                if (target == null)
                    continue;

                if (target.root == null)
                {
                    Debug.LogError($"{target.name} missing root", target.root);

                    continue;
                }


                try
                {
                    target.Create();

                    target.EditorMaskDirty();
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e.InnerException ?? e);
                }
            }

            EditorUtility.ClearProgressBar();
        }

        public override void OnInspectorGUI()
        {
            var target = (MeshInstanceRigDatabase)base.target;

            EditorGUI.BeginChangeCheck();
            target.root = EditorGUILayout.ObjectField("Root", target.root, typeof(Transform), true) as Transform;
            if (EditorGUI.EndChangeCheck() || GUILayout.Button("Rebuild"))
            {
                if(target.root != null)
                    target.Create();

                target.EditorMaskDirty();
            }

            base.OnInspectorGUI();
        }
    }
}