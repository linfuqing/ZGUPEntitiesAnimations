using UnityEngine;
using UnityEditor;

namespace ZG
{
    [CustomEditor(typeof(MeshInstanceSkeletonDatabase))]
    public class MeshInstanceSkeletonDatabaseEditor : Editor
    {
        [MenuItem("Assets/ZG/MeshInstance/Check All Skeletons")]
        public static void CheckAllSkeletons()
        {
            MeshInstanceRendererDatabase renderer;
            MeshInstanceSkeletonDatabase skeleton; 
            var rendererGuids = AssetDatabase.FindAssets("t:MeshInstanceRendererDatabase");
            var skeletonGuids = AssetDatabase.FindAssets("t:MeshInstanceSkeletonDatabase");
            string skeletonPath, rendererPath;
            int numGUIDs = skeletonGuids.Length;
            bool isFind;
            for (int i = 0; i < numGUIDs; ++i)
            {
                skeletonPath = AssetDatabase.GUIDToAssetPath(skeletonGuids[i]);
                if (EditorUtility.DisplayCancelableProgressBar("Check All Skeletons", skeletonPath, i * 1.0f / numGUIDs))
                    break;

                skeleton = AssetDatabase.LoadAssetAtPath<MeshInstanceSkeletonDatabase>(skeletonPath);
                if (skeleton == null)
                    continue;

                isFind = false;
                foreach (var rendererGuid in rendererGuids)
                {
                    rendererPath = AssetDatabase.GUIDToAssetPath(rendererGuid);

                    renderer = AssetDatabase.LoadAssetAtPath<MeshInstanceRendererDatabase>(rendererPath);
                    if (renderer == null)
                        continue;

                    if (renderer.root == skeleton.rendererRoot)
                    {
                        isFind = true;

                        break;
                    }
                }

                if(!isFind)
                    Debug.LogError(skeletonPath, skeleton);
            }

            var guids = AssetDatabase.FindAssets("t:prefab");
            var skeletonComponents = new System.Collections.Generic.List<MeshInstanceSkeletonComponent>();
            string path;
            GameObject gameObject;
            MeshInstanceRigComponent rigComponent;
            numGUIDs = guids.Length;
            for (int i = 0; i < numGUIDs; ++i)
            {
                path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (EditorUtility.DisplayCancelableProgressBar("Check All Skeleton Prefab", path, i * 1.0f / numGUIDs))
                    break;

                gameObject = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (gameObject == null)
                    continue;

                gameObject.GetComponentsInChildren(true, skeletonComponents);
                if (skeletonComponents.Count > 0)
                {
                    foreach (var skeletonComponent in skeletonComponents)
                    {
                        if (skeletonComponent.GetComponent<MeshInstanceRendererComponent>().database.root != skeletonComponent.database.rendererRoot)
                            Debug.LogError(path, skeletonComponent.database);

                        rigComponent = skeletonComponent.transform.GetComponentInParent<MeshInstanceRigComponent>(true);
                        if (rigComponent == null)
                        {
                            if (/*skeletonComponent.transform.GetComponentInParent<Avatar.Part>(true) == null && */skeletonComponent.transform.parent != null)
                                Debug.LogWarning(path, skeletonComponent.database);
                        }
                        else if (!skeletonComponent.database.rendererRoot.ContainsInParent(rigComponent.database.root))
                            Debug.LogError(path, skeletonComponent.database);
                    }
                }
            }

            EditorUtility.ClearProgressBar();
        }

        [MenuItem("Assets/ZG/MeshInstance/Rebuild All Skeletons")]
        public static void RebuildAllSkeletons()
        {
            MeshInstanceSkeletonDatabase target;
            var guids = AssetDatabase.FindAssets("t:MeshInstanceSkeletonDatabase");
            string path;
            int numGUIDs = guids.Length;
            for (int i = 0; i < numGUIDs; ++i)
            {
                path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (EditorUtility.DisplayCancelableProgressBar("Rebuild All Skeletons", path, i * 1.0f / numGUIDs))
                    break;

                target = AssetDatabase.LoadAssetAtPath<MeshInstanceSkeletonDatabase>(path);
                if (target == null)
                    continue;

                if (target.rendererRoot == null)
                {
                    Debug.LogError($"{target.name} missing root", target.rendererRoot);

                    continue;
                }

                MeshInstanceSkeletonDatabase.Data.isShowProgressBar = false;
                target.Create(target.rendererRoot);

                target.EditorMaskDirty();
            }

            EditorUtility.ClearProgressBar();
        }

        public override void OnInspectorGUI()
        {
            var target = (MeshInstanceSkeletonDatabase)base.target;
            EditorGUI.BeginChangeCheck();
            target.rendererRoot = EditorGUILayout.ObjectField("Renderer Root", target.rendererRoot, typeof(Transform), true) as Transform;
            if (EditorGUI.EndChangeCheck() || GUILayout.Button("Rebuild"))
            {
                if (target.rendererRoot != null)
                {
                    target.Create(target.rendererRoot);

                    if (PrefabUtility.GetPrefabInstanceStatus(target.rendererRoot) == PrefabInstanceStatus.Connected)
                        target.rendererRoot = PrefabUtility.GetCorrespondingObjectFromSource(target.rendererRoot);
                }

                target.EditorMaskDirty();
            }

            if(GUILayout.Button("Bake"))
            {
                target.data.Bake(target.rendererRoot);
            }

            if (GUILayout.Button("Test"))
            {
                var matrix = MeshInstanceRigDatabase.SkeletonNode.GetRootDefaultMatrix(target.rigDatabase.data.rigs[0].skeletonNodes, 47);
                Debug.Log(matrix);
            }

            base.OnInspectorGUI();
        }
    }
}