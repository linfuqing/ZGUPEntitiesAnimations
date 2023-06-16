using UnityEngine;
using UnityEditor;

namespace ZG
{
    [CustomEditor(typeof(MeshInstanceAnimatorDatabase))]
    public class MeshInstanceAnimatorDatabaseEditor : Editor
    {
        private bool __foldout;

        [MenuItem("Assets/ZG/MeshInstance/Rebuild All Animators")]
        [CommandEditor("MeshInstance", 2)]
        public static void RebuildAllAnimators()
        {
            MeshInstanceAnimatorDatabase target;
            var guids = AssetDatabase.FindAssets("t:MeshInstanceAnimatorDatabase");
            string path;
            int numGUIDs = guids.Length;
            for (int i = 0; i < numGUIDs; ++i)
            {
                path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (EditorUtility.DisplayCancelableProgressBar("Rebuild All Animators", path, i * 1.0f / numGUIDs))
                    break;

                target = AssetDatabase.LoadAssetAtPath<MeshInstanceAnimatorDatabase>(path);
                if (target == null)
                    continue;

                if (target.root == null)
                {
                    Debug.LogError($"{target.name} missing root", target.root);

                    continue;
                }

                try
                {

                    //MeshInstanceAnimatorDatabase.Data.isShowProgressBar = false;

                    target.Create();

                    target.EditorMaskDirty();
                }
                catch(System.Exception e)
                {
                    Debug.LogException(e.InnerException ?? e);
                }
            }

            EditorUtility.ClearProgressBar();
        }

        /*public const string NAME_SPACE_ROOT_BONE_NAME = "MeshInstanceAnimatorDatabaseEditorRootBoneName";

        private Dictionary<string, (string[], int[])> __rigRootBones;
        private Dictionary<int, string> __rigRootBoneNames;

        public void RootBoneGUI(int rigIndex, in MeshInstanceRigDatabase.Rig rig)
        {
            if (__rigRootBones == null)
                __rigRootBones = new Dictionary<string, (string[], int[])>();

            if(!__rigRootBones.TryGetValue(rig.name, out var value))
            {

            }

            if (__rigRootBoneNames == null)
                __rigRootBoneNames = new Dictionary<int, string>();

            if(!__rigRootBoneNames.TryGetValue(rig.name, out string name))
            {

            }

            EditorGUI.BeginChangeCheck();
            index = EditorGUILayout.IntPopup($"{rig.name} Root Bone", index, value.Item1, value.Item2);
            if (EditorGUI.EndChangeCheck())
            {
                __rigRootBoneIndices[rig.name] = index;

                EditorPrefs.SetString(NAME_SPACE_ROOT_BONE_NAME + rig.name, value.Item1[index]);
            }
        }*/

        public override void OnInspectorGUI()
        {
            var target = (MeshInstanceAnimatorDatabase)base.target;

            /*var rigDatabase = target == null ? null : target.rigDatabase;
            if(rigDatabase != null && rigDatabase.data.rigs != null)
            {
                foreach(var rig in rigDatabase.data.rigs)
                {
                    RootBoneGUI(rig);
                }
            }*/

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

            __foldout = EditorGUILayout.Foldout(__foldout, "Data");
            if (__foldout)
            {
                ++EditorGUI.indentLevel;
                base.OnInspectorGUI();
                --EditorGUI.indentLevel;
            }
            else
                target.rigDatabase = EditorGUILayout.ObjectField(target.rigDatabase, typeof(MeshInstanceRigDatabase), false) as MeshInstanceRigDatabase;
        }
    }
}