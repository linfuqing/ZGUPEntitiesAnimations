using UnityEngine;

namespace ZG
{
    public class AnimationWriteTransformComponent : MonoBehaviour, IExposeTransform
    {
        public System.Type componentType => typeof(AnimatedWriteTransformHandle);

        /*#if UNITY_EDITOR
                [UnityEditor.MenuItem("Assets/ZG/Replace Animation Components")]
                public static void Replace()
                {
                    string[] guids = UnityEditor.AssetDatabase.FindAssets("t:prefab");
                    string path;
                    GameObject gameObject;
                    Component component;
                    Unity.Animation.Hybrid.IExposeTransform[] animationComponents;
                    int numGuids = guids == null ? 0 : guids.Length;
                    for (int i = 0; i < numGuids; ++i)
                    {
                        if (UnityEditor.EditorUtility.DisplayCancelableProgressBar("Update Prefab", i.ToString() + "/" + numGuids, i * 1.0f / numGuids))
                            break;

                        path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                        gameObject = UnityEditor.PrefabUtility.LoadPrefabContents(path);
                        animationComponents = gameObject.GetComponentsInChildren<Unity.Animation.Hybrid.IExposeTransform>();
                        if (animationComponents != null && animationComponents.Length > 0)
                        {
                            foreach(var animationComponent in animationComponents)
                            {
                                component = (Component)animationComponent;
                                component.gameObject.AddComponent<AnimationWriteTransformComponent>();

                                Debug.Log($"Replace {animationComponent.GetType()} to {nameof(AnimationWriteTransformComponent)} in: path");

                                DestroyImmediate(component);
                            }

                            UnityEditor.PrefabUtility.SaveAsPrefabAsset(gameObject, path);
                        }

                        UnityEditor.PrefabUtility.UnloadPrefabContents(gameObject);
                    }

                    UnityEditor.EditorUtility.ClearProgressBar();
                }
        #endif*/
    }
}