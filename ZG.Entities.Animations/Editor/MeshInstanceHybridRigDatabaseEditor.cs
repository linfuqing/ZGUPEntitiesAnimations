using UnityEngine;
using UnityEditor;

namespace ZG
{
    [CustomEditor(typeof(MeshInstanceHybridRigDatabase))]
    public class MeshInstanceHybridRigDatabaseEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var target = (MeshInstanceHybridRigDatabase)base.target;
            EditorGUI.BeginChangeCheck();
            if (EditorGUI.EndChangeCheck() || GUILayout.Button("Rebuild"))
                target.EditorMaskDirty();

            base.OnInspectorGUI();
        }
    }
}
