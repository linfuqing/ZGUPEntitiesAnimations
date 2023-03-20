using UnityEngine;
using UnityEditor;

namespace ZG
{
    [CustomEditor(typeof(MeshInstanceHybridAnimationDatabase))]
    public class MeshInstanceHybridAnimationDatabaseEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var root = EditorGUILayout.ObjectField("Animation Root", null, typeof(GameObject), true) as GameObject;
            if (root != null)
            {
                var target = (MeshInstanceHybridAnimationDatabase)base.target;
                target.Create(root);

                target.EditorMaskDirty();
            }

            base.OnInspectorGUI();
        }
    }
}
