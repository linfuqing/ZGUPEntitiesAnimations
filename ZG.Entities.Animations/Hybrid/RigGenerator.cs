using System.Collections.Generic;
using UnityEngine;

namespace Unity.Animation.Hybrid
{
    public static partial class RigGenerator
    {
        public static int FindTransformIndex(Transform transform, Transform[] transforms)
        {
            if (transform == null || transforms == null)
                return -1;

            var instanceID = transform.GetInstanceID();
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].GetInstanceID() == instanceID)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Calculates path from a root transform to a target transform.
        /// </summary>
        /// <param name="target">The leaf transform of the path</param>
        /// <param name="root">The root transform of the path.</param>
        /// <returns>Returns a string representing the path in a transform hierarchy from a given root transform down to a given target transform.</returns>
        /// <remarks>
        /// The root transform must be higher in the hierarchy than the target transform.
        /// The target and root may also be the same transform.
        ///
        /// This is the same function as AnimationUtility.CalculateTransformPath, except that it also works at runtime.
        /// </remarks>
        public static string ComputeRelativePath(Transform target, Transform root)
        {
            var stack = new List<Transform>(10);
            var cur = target;
            while (cur != root && cur != null)
            {
                stack.Add(cur);
                cur = cur.parent;
            }

            var res = "";
            if (stack.Count > 0)
            {
                for (var i = stack.Count - 1; i > 0; --i)
                    res += stack[i].name + "/";
                res += stack[0].name;
            }

            return res;
        }

        /// <summary>
        /// Extracts the SkeletonNodes given a root and an array of transforms.
        /// </summary>
        /// <param name="root">The root transform of the hierarchy.</param>
        /// <param name="transforms">The list of transforms part of the hierarchy.</param>
        /// <param name="hasher">An optional BindingHashGenerator can be specified to compute unique animation binding IDs. When not specified the <see cref="BindingHashGlobals.DefaultHashGenerator"/> is used.</param>
        /// <returns>SkeletonNode array</returns>
        public static SkeletonNode[] ExtractSkeletonNodesFromTransforms(Transform root, Transform[] transforms, BindingHashGenerator hasher = default)
        {
            var skeletonNodes = new List<SkeletonNode>();

            if (!hasher.IsValid)
                hasher = BindingHashGlobals.DefaultHashGenerator;

            for (int i = 0; i < transforms.Length; i++)
            {
                var skeletonNode = new SkeletonNode
                {
                    Id = hasher.ToHash(ToTransformBindingID(transforms[i], root)),
                    AxisIndex = -1,
                    LocalTranslationDefaultValue = transforms[i].localPosition,
                    LocalRotationDefaultValue = transforms[i].localRotation,
                    LocalScaleDefaultValue = transforms[i].localScale,
                    ParentIndex = FindTransformIndex(transforms[i].parent, transforms)
                };
                skeletonNodes.Add(skeletonNode);
            }

            return skeletonNodes.ToArray();
        }

        /// <summary>
        /// Extracts the SkeletonNodes of a transform hierarchy given a root GameObject.
        /// </summary>
        /// <param name="root">The root GameObject.</param>
        /// <param name="hasher">An optional BindingHashGenerator can be specified to compute unique animation binding IDs. When not specified the <see cref="BindingHashGlobals.DefaultHashGenerator"/> is used.</param>
        /// <returns>SkeletonNode array</returns>
        public static SkeletonNode[] ExtractSkeletonNodesFromGameObject(GameObject root, BindingHashGenerator hasher = default)
        {
            if (!hasher.IsValid)
                hasher = BindingHashGlobals.DefaultHashGenerator;

            var transforms = root.GetComponentsInChildren<Transform>();

            return ExtractSkeletonNodesFromTransforms(root.transform, transforms, hasher);
        }

        /// <summary>
        /// Returns a component of Type type in the GameObject or parent GameObjects.
        /// </summary>
        /// <param name="gameObject">The GameObject from where to start searching.</param>
        /// <typeparam name="T">The type of Component to retrieve.</typeparam>
        /// <returns>A component matching the specified type. Null if none was found.</returns>
        public static T GetComponentInParent<T>(GameObject gameObject)
        {
            var res = GetComponentInParent(typeof(T), gameObject);
            return (T)(object)res;     // Can be either a Component or an Interface that also implement the Component class...
        }

        /// <summary>
        /// Returns a component of Type type in the GameObject or parent GameObjects.
        /// </summary>
        /// <param name="type">The type of Component to retrieve.</param>
        /// <param name="gameObject">The GameObject from where to start searching.</param>
        /// <returns>A component matching the specified type. Null if none was found.</returns>
        public static Component GetComponentInParent(System.Type type, GameObject gameObject)
        {
            Component queryComponent = null;

            for (var transform = gameObject.transform; queryComponent == null && transform != null; transform = transform.parent)
            {
                transform.TryGetComponent(type, out queryComponent);
            }

            return queryComponent;
        }

        static internal TransformBindingID ToTransformBindingID(Transform target, Transform root) =>
            new TransformBindingID { Path = ComputeRelativePath(target, root) };

        static internal GenericBindingID ToGenericBindingID(string id) =>
            new GenericBindingID { Path = string.IsNullOrEmpty(id) ? string.Empty : System.IO.Path.GetDirectoryName(id), AttributeName = System.IO.Path.GetFileName(id) };
    }
}
