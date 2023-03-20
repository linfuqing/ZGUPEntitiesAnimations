using UnityEngine;
using UnityEditor;
using Unity.Animation.Hybrid;

namespace ZG
{
    [CustomEditor(typeof(MeshInstanceHierarchyDatabase))]
    public class MeshInstanceHierarchyDatabaseEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var target = (MeshInstanceHierarchyDatabase)base.target;
            EditorGUI.BeginChangeCheck();
            target.rendererRoot = EditorGUILayout.ObjectField("Renderer Root", target.rendererRoot, typeof(Transform), true) as Transform;
            if (EditorGUI.EndChangeCheck() || GUILayout.Button("Rebuild"))
            {
                if (target.rendererRoot != null)
                {
                    target.Create(target.rendererRoot);

                    if(PrefabUtility.GetPrefabInstanceStatus(target.rendererRoot) ==  PrefabInstanceStatus.Connected)
                        target.rendererRoot = PrefabUtility.GetCorrespondingObjectFromSource(target.rendererRoot);
                }

                target.EditorMaskDirty();
            }

            base.OnInspectorGUI();
        }
    }
}