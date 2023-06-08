using System;
using System.Text;
using System.Collections.Generic;
using Unity.Animation.Hybrid;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ZG
{
    public partial class MeshInstanceRigDatabase
    {
        public struct MaterialProperty
        {
            public Component rigRoot;
            public Renderer renderer;
            public Type componentType;

            public string id;
            public float value;
            public int index;
        }

        public static void BuildRendererMaterialProperties(
            Renderer[] renderers,
            MeshInstanceRendererDatabase.MaterialPropertyOverride materialPropertyOverride,
            IList<MaterialProperty> materialProperties)
        {
            GenericBindingID bindingID = default;

            MaterialProperty materialProperty = default;
            Action<string, Type, ShaderPropertyType, Vector4> callback = (x, y, z, w) =>
            {
                bindingID.AttributeName = $"material.{x}";
                materialProperty.componentType = y;
                switch (z)
                {
                    case ShaderPropertyType.Int:

                    /*intChannel = new IntChannel();
                    intChannel.Id = bindingID.ID;
                    intChannel.DefaultValue = (int)overrideData.value.x;

                    if (intChannels == null)
                        intChannels = new HashSet<IntChannel>();

                    intChannels.Add(intChannel);
                    break;*/
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:

                        //floatChannel = new FloatChannel();
                        //floatChannel.Id = bindingID.ID;
                        //floatChannel.DefaultValue = overrideData.value.x;

                        bindingID.ValueType = Unity.Animation.Authoring.GenericPropertyType.Float;

                        materialProperty.id = bindingID.ID;
                        materialProperty.value = w.x;
                        materialProperty.index = 0;
                        materialProperties.Add(materialProperty);
                        break;
                    case ShaderPropertyType.Color:
                        bindingID.ValueType = Unity.Animation.Authoring.GenericPropertyType.Float4;

                        for (int i = 0; i < 4; ++i)
                        {
                            materialProperty.id = bindingID.GetColor(i).ID;
                            materialProperty.value = w[i];
                            materialProperty.index = i;

                            materialProperties.Add(materialProperty);
                        }
                        break;
                    case ShaderPropertyType.Vector:

                        //floatChannel = new FloatChannel();
                        bindingID.ValueType = Unity.Animation.Authoring.GenericPropertyType.Float4;

                        for (int i = 0; i < 4; ++i)
                        {
                            materialProperty.id = bindingID[i].ID;
                            materialProperty.value = w[i];
                            materialProperty.index = i;

                            materialProperties.Add(materialProperty);
                        }
                        break;
                }
            };

            bool isOverride;
            Transform root;
            Material[] materials;
            MaterialOverride[] materialOverrides;
            foreach (var renderer in renderers)
            {
                materialOverrides = renderer.GetComponents<MaterialOverride>();

                materials = renderer.sharedMaterials;

                bindingID.Path = renderer.transform.GetPath(x => x.GetComponent<Animator>() != null, out root);
                bindingID.ComponentType = renderer.GetType();

                materialProperty.rigRoot = root.GetComponent<Animator>();
                materialProperty.renderer = renderer;
                foreach (var material in materials)
                {
                    isOverride = false;
                    foreach (var materialOverride in materialOverrides)
                    {
                        if (materialOverride == null ||
                            materialOverride.overrideAsset == null ||
                            materialOverride.overrideAsset.material != material ||
                            materialOverride.overrideList == null)
                            continue;

                        isOverride = true;

                        foreach (var overrideData in materialOverride.overrideList)
                            callback(
                                overrideData.name,
                                materialOverride.overrideAsset.GetTypeFromAttrs(overrideData),
                                overrideData.type,
                                overrideData.value);

                        break;
                    }

                    if (!isOverride && materialPropertyOverride != null)
                        materialPropertyOverride(material, callback);
                }
            }
        }

        public static void BuildRigMaterailPropertyIndices(IList<MaterialProperty> materialProperties, IDictionary<Component, List<int>> rigMaterailPropertyIndices)
        {
            int numMaterialProperties = materialProperties.Count;
            MaterialProperty materialProperty;
            List<int> materailPropertyIndices;
            for (int i = 0; i < numMaterialProperties; ++i)
            {
                materialProperty = materialProperties[i];
                if (!rigMaterailPropertyIndices.TryGetValue(materialProperty.rigRoot, out materailPropertyIndices))
                {
                    materailPropertyIndices = new List<int>();
                    rigMaterailPropertyIndices[materialProperty.rigRoot] = materailPropertyIndices;
                }

                materailPropertyIndices.Add(i);
            }
        }

        public static void CollectBones(Transform root, Action<MeshInstanceRigBone> bones)
        {
            root.GetComponentsInChildren(false, bones, typeof(IRigAuthoring), typeof(Animator));
        }

        public static Dictionary<Component, int> CreateComponentRigIndices(GameObject root)
        {
            var componentRigIndices = new Dictionary<Component, int>();

            int rigIndex = 0;
            var animators = root.GetComponentsInChildren<Animator>();
            foreach (var animator in animators)
                componentRigIndices[animator] = rigIndex++;

            /*var rigAuthorings = root.GetComponentsInChildren<RigAuthoring>();
            foreach (var rigAuthoring in rigAuthorings)
                componentRigIndices[rigAuthoring] = rigIndex++;

            var rigComponents = root.GetComponentsInChildren<RigComponent>();
            foreach (var rigComponent in rigComponents)
                componentRigIndices[rigComponent] = rigIndex++;*/

            return componentRigIndices;
        }

        public static Rig[] CreateRigs(
            bool usePose,
            GameObject root,
            IDictionary<Rig, int> outRigIndices,
            IDictionary<Component, int> componentRigIndices,
            MeshInstanceRendererDatabase.MaterialPropertyOverride materialPropertyOverride)
        {
            if (outRigIndices == null)
                outRigIndices = new Dictionary<Rig, int>();

            int rigIndex;
            Rig rig;
            var rigs = new List<Rig>();

            var animators = root.GetComponentsInChildren<Animator>();
            foreach (var animator in animators)
            {
                rig = __ToRig(usePose, animator);

                rigIndex = __SetupRig(rig, rigs, outRigIndices);

                if (componentRigIndices == null)
                    componentRigIndices = new Dictionary<Component, int>();

                componentRigIndices[animator] = rigIndex;
            }

            var results = rigs.ToArray();

            var materialProperties = new List<MaterialProperty>();

            BuildRendererMaterialProperties(root.GetComponentsInChildren<Renderer>(), materialPropertyOverride, materialProperties);

            int numMaterialProperties = materialProperties.Count;
            if (numMaterialProperties > 0)
            {
                var rigMaterailPropertyIndices = new Dictionary<Component, List<int>>();
                BuildRigMaterailPropertyIndices(materialProperties, rigMaterailPropertyIndices);

                int i, originCount, count;
                FloatChannel floatChannel;
                MaterialProperty materialProperty;
                List<int> materailPropertyIndices;
                foreach (var pair in rigMaterailPropertyIndices)
                {
                    rigIndex = componentRigIndices[pair.Key];

                    ref var result = ref results[rigIndex];

                    originCount = result.floatChannels == null ? 0 : result.floatChannels.Length;

                    materailPropertyIndices = pair.Value;
                    count = materailPropertyIndices.Count;
                    Array.Resize(ref result.floatChannels, originCount + count);

                    for (i = 0; i < count; ++i)
                    {
                        materialProperty = materialProperties[materailPropertyIndices[i]];

                        floatChannel = new FloatChannel();
                        floatChannel.Id = materialProperty.id;
                        floatChannel.DefaultValue = materialProperty.value;
                        result.floatChannels[i + originCount] = floatChannel;
                    }
                }
            }

            /*var rigAuthorings = root.GetComponentsInChildren<RigAuthoring>();
            foreach (var rigAuthoring in rigAuthorings)
            {
                rig = __ToRig(rigAuthoring.Skeleton, usePose);

                rigIndex = __SetupRig(rig, rigs, outRigIndices);

                if (componentRigIndices != null)
                    componentRigIndices[rigAuthoring] = rigIndex;
            }

            var rigComponents = root.GetComponentsInChildren<RigComponent>();
            foreach (var rigComponent in rigComponents)
            {
                rig = __ToRig(rigComponent, usePose);

                rigIndex = __SetupRig(rig, rigs, outRigIndices);

                if (componentRigIndices != null)
                    componentRigIndices[rigComponent] = rigIndex;
            }*/

            return results;
        }

        /*private static Rig __ToRig(Unity.Animation.Authoring.Skeleton skeleton, bool usePose)
        {
            Rig rig;
            rig.name = skeleton.name;

            rig.usePose = usePose;
            rig.avatar = null;

            var transformChannels = new List<TransformChannel>();
            skeleton.GetAllTransforms(transformChannels);

            var skeletonNodesCount = transformChannels.Count;

            var floatChannels = skeleton.FloatChannels;
            var intChannels = skeleton.IntChannels;
            var quaternionChannels = skeleton.QuaternionChannels;

            var floatChannelsCount = floatChannels.Count;
            var intChannelsCount = intChannels.Count;
            var quaternionChannelsCount = quaternionChannels.Count;

            int i;
            if (skeletonNodesCount > 0)
            {
                rig.skeletonNodes = new SkeletonNode[skeletonNodesCount];

                int j;
                TransformBindingID parentID;
                TransformChannel transformChannel;
                for (i = 0; i < skeletonNodesCount; ++i)
                {
                    ref var skeletonNode = ref rig.skeletonNodes[i];

                    skeletonNode.axisIndex = -1;

                    transformChannel = transformChannels[i];

                    skeletonNode.path = transformChannel.ID.ID;
                    parentID = transformChannel.ID.GetParent();

                    skeletonNode.parentIndex = -1;
                    for (j = 0; j < i; ++j)
                    {
                        if (transformChannels[j].ID.Equals(parentID))
                        {
                            skeletonNode.parentIndex = j;

                            break;
                        }
                    }

                    skeletonNode.localTranslationDefaultValue = transformChannel.Properties.DefaultTranslationValue;
                    skeletonNode.localRotationDefaultValue = transformChannel.Properties.DefaultRotationValue;
                    skeletonNode.localScaleDefaultValue = transformChannel.Properties.DefaultScaleValue;

                    skeletonNode.localTranslationPoseValue = transformChannel.Properties.DefaultTranslationValue;
                    skeletonNode.localRotationPoseValue = transformChannel.Properties.DefaultRotationValue;
                    skeletonNode.localScalePoseValue = transformChannel.Properties.DefaultScaleValue;
                }
            }
            else
                rig.skeletonNodes = null;

            rig.translationChannels = null;

            if (quaternionChannelsCount > 0)
            {
                rig.rotationChannels = new RotationChannel[quaternionChannelsCount];

                GenericChannel<quaternion> source;
                for (i = 0; i < quaternionChannelsCount; ++i)
                {
                    source = quaternionChannels[i];

                    ref var destination = ref rig.rotationChannels[i];

                    destination.Id = source.ID.ID;
                    destination.DefaultValue = source.DefaultValue;
                }
            }
            else
                rig.rotationChannels = null;

            rig.scaleChannels = null;

            if (floatChannelsCount > 0)
            {
                rig.floatChannels = new FloatChannel[floatChannelsCount];

                GenericChannel<float> source;
                for (i = 0; i < floatChannelsCount; ++i)
                {
                    source = floatChannels[i];

                    ref var destination = ref rig.floatChannels[i];

                    destination.Id = source.ID.ID;
                    destination.DefaultValue = source.DefaultValue;
                }
            }
            else
                rig.floatChannels = null;

            if (intChannelsCount > 0)
            {
                rig.intChannels = new IntChannel[intChannelsCount];

                GenericChannel<int> source;
                for (i = 0; i < intChannelsCount; i++)
                {
                    source = intChannels[i];

                    ref var destination = ref rig.intChannels[i];

                    destination.Id = source.ID.ID;
                    destination.DefaultValue = source.DefaultValue;
                }
            }
            else
                rig.intChannels = null;

            rig.axes = null;

            return rig;
        }

        private static Rig __ToRig(RigComponent rigComponent, bool usePose)
        {
            Rig rig;
            rig.name = rigComponent.name;

            rig.usePose = usePose;
            rig.avatar = null;

            var skeletonNodesCount = rigComponent.Bones.Length;
            rig.skeletonNodes = new SkeletonNode[skeletonNodesCount];

            Transform root = rigComponent.transform, bone;
            for (int i = 0; i < skeletonNodesCount; i++)
            {
                ref var skeletonNode = ref rig.skeletonNodes[i];

                bone = rigComponent.Bones[i];

                skeletonNode.path = RigGenerator.ComputeRelativePath(bone, root);
                skeletonNode.axisIndex = -1;
                skeletonNode.parentIndex = RigGenerator.FindTransformIndex(bone.parent, rigComponent.Bones);

                UnityEngine.Assertions.Assert.IsTrue(skeletonNode.parentIndex < i, skeletonNode.path);

                skeletonNode.localTranslationDefaultValue = bone.localPosition;
                skeletonNode.localRotationDefaultValue = bone.localRotation;
                skeletonNode.localScaleDefaultValue = bone.localScale;

                skeletonNode.localTranslationPoseValue = bone.localPosition;
                skeletonNode.localRotationPoseValue = bone.localRotation;
                skeletonNode.localScalePoseValue = bone.localScale;
            }

            rig.translationChannels = (TranslationChannel[])rigComponent.TranslationChannels.Clone();
            rig.rotationChannels = (RotationChannel[])rigComponent.RotationChannels.Clone();
            rig.scaleChannels = (ScaleChannel[])rigComponent.ScaleChannels.Clone();
            rig.floatChannels = (FloatChannel[])rigComponent.FloatChannels.Clone();
            rig.intChannels = (IntChannel[])rigComponent.IntChannels.Clone();
            rig.axes = null;

            return rig;
        }*/

        private static Rig __ToRig(
            bool usePose,
            Animator animator)
        {
            Rig rig;
            rig.name = animator.name;

            rig.usePose = usePose;

            rig.avatar = animator.avatar;

            var root = animator.transform;

            rig.skeletonNodes = __FilterNonExsistantBones(root, rig.avatar);
            rig.translationChannels = null;
            rig.rotationChannels = null;
            rig.scaleChannels = null;
            rig.floatChannels = null;
            rig.intChannels = null;

            rig.axes = null;

            return rig;
        }

        private static int __SetupRig(in Rig rig, IList<Rig> outRigs, IDictionary<Rig, int> outRigIndices)
        {
            if (!outRigIndices.TryGetValue(rig, out int rigIndex))
            {
                rigIndex = outRigs.Count;

                outRigs.Add(rig);

                outRigIndices[rig] = rigIndex;
            }

            return rigIndex;
        }

        private static SkeletonNode[] __FilterNonExsistantBones(Transform root, UnityEngine.Avatar avatar)
        {
            var filteredSkeleton = new List<SkeletonNode>();

            Dictionary<Transform, int> parentIndices;
            SkeletonNode skeletonNode;
            Transform transform;
            string path;
            bool hirearchyExsists, isHuman = avatar != null && avatar.isValid && avatar.isHuman;
            if (isHuman)
            {
                if (!avatar.isValid)
                    throw new ArgumentException($"Avatar ({avatar.ToString()}) is not valid, so no bones could be extracted.");

                var humanDescription = avatar.humanDescription;
                var human = humanDescription.human;
                var skeleton = humanDescription.skeleton;
                if (skeleton.Length == 0)
                    return filteredSkeleton.ToArray();

                var skeletonBone = skeleton[0];

                string name = skeletonBone.name;

                name = __GetSkeletonName(name, human);

                skeletonNode.path = name;
                skeletonNode.axisIndex = -1;
                skeletonNode.parentIndex = -1;

                skeletonNode.localTranslationDefaultValue = root.localPosition;
                skeletonNode.localRotationDefaultValue = root.localRotation;
                skeletonNode.localScaleDefaultValue = root.localScale;

                skeletonNode.localTranslationPoseValue = skeletonBone.position;
                skeletonNode.localRotationPoseValue = skeletonBone.rotation;
                skeletonNode.localScalePoseValue = skeletonBone.scale;

                skeletonNode.Assert();

                filteredSkeleton.Add(skeletonNode);

                parentIndices = new Dictionary<Transform, int>();
                parentIndices[root] = 0;

                int numSkeletonBones = skeleton.Length;
                Transform parent;
                var parentNames = new Dictionary<Transform, string>();
                var stringBuilder = new StringBuilder();
                for (int i = 1; i < numSkeletonBones; ++i)
                {
                    skeletonBone = skeleton[i];

                    name = skeletonBone.name;
                    transform = AnimatorUtils.FindDescendant(root, name);
                    if (transform == null)
                        continue;

                    // Make sure that there are bones until the root.
                    hirearchyExsists = true;
                    parent = transform;
                    while (hirearchyExsists && parent.parent != root)
                    {
                        parent = parent.parent;
                        if (Array.FindIndex(skeleton, (bone) => bone.name == parent.name) == -1)
                            hirearchyExsists = false;
                    }

                    /*if (hirearchyExsists)
                    {
                        hirearchyExsists = true;
                        foreach (var filteredBone in filteredSkeleton)
                        {
                            if (SkeletonNode.PathToName(filteredBone.path) == name)
                            {
                                UnityEngine.Debug.LogError($"SkeletonNode has an invalid name {name}: The SkeletonNode cannot be the same.", root);

                                hirearchyExsists = false;

                                break;
                            }
                        }
                    }*/

                    if (!hirearchyExsists)
                        continue;

                    name = __GetSkeletonName(name, human);

                    parentNames[transform] = name;

                    parentIndices[transform] = filteredSkeleton.Count;

                    parent = transform.parent;

                    stringBuilder.Clear();

                    __GetSkeletonPath(stringBuilder, parent, root, parentNames);

                    stringBuilder.Append(name);

                    skeletonNode.path = stringBuilder.ToString();
                    skeletonNode.axisIndex = -1;
                    skeletonNode.parentIndex = __GetParentIndex(
                        parentIndices,
                        transform,
                        out skeletonNode.localTranslationDefaultValue,
                        out skeletonNode.localRotationDefaultValue,
                        out skeletonNode.localScaleDefaultValue);
                    if (skeletonNode.parentIndex == -1)
                        UnityEngine.Debug.LogError($"Error skeletonNode path {skeletonNode.path}", transform);

                    UnityEngine.Assertions.Assert.IsTrue(skeletonNode.parentIndex < filteredSkeleton.Count, name);

                    skeletonNode.localTranslationPoseValue = skeletonBone.position;
                    skeletonNode.localRotationPoseValue = skeletonBone.rotation;
                    skeletonNode.localScalePoseValue = skeletonBone.scale;

                    skeletonNode.Assert();

                    filteredSkeleton.Add(skeletonNode);
                }

                var bones = new List<MeshInstanceRigBone>();
                CollectBones(root, bones.Add);

                foreach (var bone in bones)
                {
                    transform = bone.transform;

                    name = transform.name;
                    /*hirearchyExsists = true;
                    foreach (var filteredBone in filteredSkeleton)
                    {
                        if(SkeletonNode.PathToName(filteredBone.path) == name)
                        {
                            UnityEngine.Debug.LogError($"SkeletonNode has an invalid name {name}: The SkeletonNode cannot be the same.", root);

                            hirearchyExsists = false;

                            break;
                        }
                    }

                    if (!hirearchyExsists)
                        continue;*/

                    parentIndices[transform] = filteredSkeleton.Count;

                    skeletonNode.path = RigGenerator.ComputeRelativePath(transform, root);
                    skeletonNode.axisIndex = -1;
                    skeletonNode.parentIndex = __GetParentIndex(
                        parentIndices,
                        transform,
                        out skeletonNode.localTranslationDefaultValue,
                        out skeletonNode.localRotationDefaultValue,
                        out skeletonNode.localScaleDefaultValue);
                    if (skeletonNode.parentIndex == -1)
                        UnityEngine.Debug.LogError($"Error skeletonNode path {skeletonNode.path}", transform);

                    UnityEngine.Assertions.Assert.IsTrue(skeletonNode.parentIndex < filteredSkeleton.Count, name);

                    skeletonNode.localTranslationPoseValue = skeletonNode.localTranslationDefaultValue;
                    skeletonNode.localRotationPoseValue = skeletonNode.localRotationDefaultValue;
                    skeletonNode.localScalePoseValue = skeletonNode.localScaleDefaultValue;

                    skeletonNode.Assert();

                    filteredSkeleton.Add(skeletonNode);
                }
            }
            else
            {
                var transforms = root.GetComponentsInChildren<Transform>();
                int numTransforms = transforms.Length;
                filteredSkeleton.Capacity = numTransforms;
                for (int i = 0; i < numTransforms; i++)
                {
                    transform = transforms[i];

                    path = RigGenerator.ComputeRelativePath(transform, root);

                    // Make sure that there are bones until the root.
                    // TODO: figure out what to do when two transforms have the same name.
                    /*hirearchyExsists = true;
                    foreach (var filteredBone in filteredSkeleton)
                    {
                        if (filteredBone.path == path)
                        {
                            UnityEngine.Debug.LogError($"SkeletonNode has an invalid path {path}: The SkeletonNode path cannot be the same.", root);

                            hirearchyExsists = false;

                            break;
                        }
                    }

                    if (!hirearchyExsists)
                    {
                        ArrayUtility.RemoveAt(ref transforms, --i);

                        --numTransforms;

                        continue;
                    }*/

                    skeletonNode.path = i == 0 ? transform.name : RigGenerator.ComputeRelativePath(transform, root);
                    skeletonNode.axisIndex = -1;
                    skeletonNode.parentIndex = RigGenerator.FindTransformIndex(transform.parent, transforms);

                    UnityEngine.Assertions.Assert.IsTrue(skeletonNode.parentIndex < i, transform.name);

                    skeletonNode.localTranslationDefaultValue = transform.localPosition;
                    skeletonNode.localRotationDefaultValue = transform.localRotation;
                    skeletonNode.localScaleDefaultValue = transform.localScale;

                    skeletonNode.localTranslationPoseValue = transform.localPosition;
                    skeletonNode.localRotationPoseValue = transform.localRotation;
                    skeletonNode.localScalePoseValue = transform.localScale;

                    skeletonNode.Assert();

                    filteredSkeleton.Add(skeletonNode);
                }
            }

            return filteredSkeleton.ToArray();
        }

        private static int __GetParentIndex(
            Dictionary<Transform, int> parentIndices,
            Transform transform,
            out Vector3 translation,
            out Quaternion rotation,
            out Vector3 scale)
        {
            translation = transform.localPosition;
            rotation = transform.localRotation;
            scale = transform.localScale;

            var parent = transform.parent;
            if (parent == null)
                return -1;

            if (parentIndices.TryGetValue(parent, out int index))
                return index;

            index = __GetParentIndex(parentIndices, parent, out var parentTranslation, out var parentRotation, out var parentScale);

            var matrix = Matrix4x4.TRS(parentTranslation, parentRotation, parentScale) * Matrix4x4.TRS(translation, rotation, scale);

            translation = matrix.GetColumn(3);
            rotation = matrix.rotation;
            scale = matrix.lossyScale;

            return index;
        }

        private static string __GetSkeletonName(string name, HumanBone[] human)
        {
            foreach (var humanBone in human)
            {
                if (humanBone.boneName == name)
                    return humanBone.humanName;
            }

            return name;
        }

        private static void __GetSkeletonPath(
            StringBuilder stringBuilder,
            Transform transform,
            Transform root,
            Dictionary<Transform, string> parentNames)
        {
            if (transform == null || transform == root)
                return;

            stringBuilder.Insert(0, '/');

            if (parentNames.TryGetValue(transform, out string name))
            {
                stringBuilder.Insert(0, name);

                __GetSkeletonPath(stringBuilder, transform.parent, root, parentNames);
            }
            else
                stringBuilder.Insert(0, RigGenerator.ComputeRelativePath(transform, root));
        }

    }
}