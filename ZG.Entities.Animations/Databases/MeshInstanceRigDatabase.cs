using System;
using System.Text;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Animation;
using Unity.Animation.Hybrid;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ZG
{
    [CreateAssetMenu(fileName = "Mesh Instance Rig Database", menuName = "ZG/Mesh Instance/Rig Database")]
    public partial class MeshInstanceRigDatabase : MeshInstanceDatabase<MeshInstanceRigDatabase>, ISerializationCallbackReceiver
    {
        [Flags]
        public enum InitType
        {
            TypeIndex = 0x01,
            ComponentTypes = 0x02, 
            RigDefinition = 0x04
        }

        [Serializable]
        public struct ComponentTypeWrapper : IEquatable<ComponentTypeWrapper>
        {
            public int[] typeIndices;

            public ComponentTypeWrapper(int[] typeIndices)
            {
                this.typeIndices = typeIndices;
            }

            public ComponentTypeSet ToComponentTypes(TypeIndex[] typeIndices)
            {
                int numTypeIndices = this.typeIndices.Length;
                ComponentType[] componentTypes = new ComponentType[numTypeIndices];
                for (int i = 0; i < numTypeIndices; ++i)
                    componentTypes[i].TypeIndex = typeIndices[this.typeIndices[i]];

                return new ComponentTypeSet(componentTypes);
            }

            public bool Equals(ComponentTypeWrapper other)
            {
                bool isContains;
                foreach(int sourceTypeIndex in typeIndices)
                {
                    isContains = false;
                    foreach (int destinationTypeIndex in other.typeIndices)
                    {
                        if (destinationTypeIndex == sourceTypeIndex)
                        {
                            isContains = true;

                            break;
                        }
                    }

                    if (!isContains)
                        return false;
                }

                return true;
            }
        }

        [Serializable]
        public struct SkeletonNode : IEquatable<SkeletonNode>
        {
            public string path;

            public int parentIndex;
            public int axisIndex;

            public Vector3 localTranslationDefaultValue;
            public Quaternion localRotationDefaultValue;
            public Vector3 localScaleDefaultValue;

            public Vector3 localTranslationPoseValue;
            public Quaternion localRotationPoseValue;
            public Vector3 localScalePoseValue;

            public Matrix4x4 localDefaultMatrix => Matrix4x4.TRS(localTranslationDefaultValue, localRotationDefaultValue, localScaleDefaultValue);

            public Matrix4x4 localPoseMatrix => Matrix4x4.TRS(localTranslationPoseValue, localRotationPoseValue, localScalePoseValue);

            public static Matrix4x4  GetRootDefaultMatrix(SkeletonNode[] skeletonNodes, int index)
            {
                if (index == -1)
                    return Matrix4x4.identity;

                ref var skeletonNode = ref skeletonNodes[index];
                return GetRootDefaultMatrix(skeletonNodes, skeletonNode.parentIndex) * skeletonNode.localDefaultMatrix;
            }

            public static Matrix4x4 GetRootPoseMatrix(SkeletonNode[] skeletonNodes, int index)
            {
                if (index == -1)
                    return Matrix4x4.identity;

                ref var skeletonNode = ref skeletonNodes[index];
                return GetRootPoseMatrix(skeletonNodes, skeletonNode.parentIndex) * skeletonNode.localPoseMatrix;
            }

            public static SkeletonBone[] ToBones(SkeletonNode[] skeletonNodes, bool usePose)
            {
                int numSkeletonNodes = skeletonNodes.Length;
                SkeletonBone[] skeletonBones = new SkeletonBone[numSkeletonNodes];
                if (usePose)
                {
                    for (int i = 0; i < numSkeletonNodes; ++i)
                    {
                        ref readonly var skeletonNode = ref skeletonNodes[i];

                        ref var skeletonBone = ref skeletonBones[i];

                        skeletonBone.name = PathToName(skeletonNode.path);
                        skeletonBone.position = skeletonNode.localTranslationPoseValue;
                        skeletonBone.rotation = skeletonNode.localRotationPoseValue;
                        skeletonBone.scale = skeletonNode.localScalePoseValue;
                    }
                }
                else
                {
                    for (int i = 0; i < numSkeletonNodes; ++i)
                    {
                        ref readonly var skeletonNode = ref skeletonNodes[i];

                        ref var skeletonBone = ref skeletonBones[i];

                        skeletonBone.name = PathToName(skeletonNode.path);
                        skeletonBone.position = skeletonNode.localTranslationDefaultValue;
                        skeletonBone.rotation = skeletonNode.localRotationDefaultValue;
                        skeletonBone.scale = skeletonNode.localScaleDefaultValue;
                    }
                }

                return skeletonBones;
            }

            public static int IndexOf(SkeletonNode[] skeletonNodes, string name)
            {
                int numSkeletonNodes = skeletonNodes.Length;
                for(int i = 0; i < numSkeletonNodes; ++i)
                {
                    if (PathToName(skeletonNodes[i].path) == name)
                        return i;
                }

                return -1;
            }

            public static string PathToName(string path)
            {
                int index = path.LastIndexOf('/');
                return index == -1 ? path : path.Substring(index + 1);
            }

            public void Assert()
            {
                UnityEngine.Assertions.Assert.IsTrue(
                    math.all(math.isfinite(localTranslationDefaultValue)) &&
                    math.all(math.isfinite(((quaternion)localRotationDefaultValue).value)) &&
                    math.all(math.isfinite(localScaleDefaultValue)) &&
                    math.all(math.isfinite(localTranslationPoseValue)) &&
                    math.all(math.isfinite(((quaternion)localRotationPoseValue).value)) &&
                    math.all(math.isfinite(localScalePoseValue)), path);
            }

            public bool Equals(SkeletonNode other)
            {
                return path == other.path &&
                    parentIndex == other.parentIndex &&
                    axisIndex == other.axisIndex &&
                    localScaleDefaultValue == other.localScaleDefaultValue &&
                    localTranslationDefaultValue == other.localTranslationDefaultValue &&
                    localRotationDefaultValue == other.localRotationDefaultValue;
            }
        }

        [Serializable]
        public struct Axis
        {
            public Vector3 scalingOffset;
            public Vector3 scalingPivot;
            public Vector3 rotationOffset;
            public Vector3 rotationPivot;
            public Quaternion preRotation;
            public Quaternion postRotation;

            public bool Equals(Axis other)
            {
                return scalingOffset == other.scalingOffset &&
                    scalingPivot == other.scalingPivot &&
                    rotationOffset == other.rotationOffset &&
                    rotationPivot == other.rotationPivot &&
                    preRotation == other.preRotation &&
                    postRotation == other.postRotation;
            }
        }

        [Serializable]
        public struct Rig : IEquatable<Rig>
        {
            public struct BoneTransform
            {
                public const int SIZE = sizeof(float) * 7;

                public Vector3 translation;
                public Quaternion rotation;
            }

            public string name;

            public bool usePose;

            public UnityEngine.Avatar avatar;

            public SkeletonNode[] skeletonNodes;
            public TranslationChannel[] translationChannels;
            public RotationChannel[] rotationChannels;
            public ScaleChannel[] scaleChannels;
            public FloatChannel[] floatChannels;
            public IntChannel[] intChannels;
            public Axis[] axes;

            public static void ReplacePath(StringBuilder stringBuilder, Dictionary<string, string> names, string path, int startIndex = -1)
            {
                startIndex = startIndex == -1 ? path.Length - 1 : startIndex;
                int index = path.LastIndexOf('/', startIndex);
                string name;
                if (index == -1)
                    name = path.Substring(0, startIndex + 1);
                else
                {
                    name = path.Substring(index + 1, startIndex - index);

                    if (index > 0)
                    {
                        ReplacePath(stringBuilder, names, path, index - 1);

                        stringBuilder.Append('/');
                    }
                }

                if (names.TryGetValue(name, out var newName))
                    name = newName;

                stringBuilder.Append(name);
            }

            public bool isCreated =>
                skeletonNodes != null && skeletonNodes.Length > 0 ||
                translationChannels != null && translationChannels.Length > 0 ||
                rotationChannels != null && rotationChannels.Length > 0 ||
                scaleChannels != null && scaleChannels.Length > 0 ||
                floatChannels != null && floatChannels.Length > 0 ||
                intChannels != null && intChannels.Length > 0;

            public int BoneIndexOf(string path)
            {
                int numSkeletonNodes = skeletonNodes.Length;
                for(int i = 0; i < numSkeletonNodes; ++i)
                {
                    if (skeletonNodes[i].path == path)
                        return i;
                }

                return -1;
            }

            public BlobAssetReference<RigDefinition> ToAsset(bool usePose)
            {
                RigBuilderData rigBuilderData = default;
                Unity.Animation.SkeletonNode skeletonNode;
                Unity.Animation.Axis axis;
                int length = skeletonNodes == null ? 0 : skeletonNodes.Length;
                rigBuilderData.SkeletonNodes = new NativeList<Unity.Animation.SkeletonNode>(length, Allocator.Temp);
                rigBuilderData.SkeletonNodes.ResizeUninitialized(length);
                for (int i = 0; i < length; ++i)
                {
                    ref readonly var origin = ref skeletonNodes[i];

                    skeletonNode.Id = new TransformBindingID { Path = i > 0 ? origin.path : string.Empty }.ID;
                    skeletonNode.ParentIndex = origin.parentIndex;
                    skeletonNode.AxisIndex = origin.axisIndex;
                    if (usePose)
                    {
                        skeletonNode.LocalScaleDefaultValue = origin.localScalePoseValue;
                        skeletonNode.LocalRotationDefaultValue = origin.localRotationPoseValue;
                        skeletonNode.LocalTranslationDefaultValue = origin.localTranslationPoseValue;
                    }
                    else
                    {
                        skeletonNode.LocalScaleDefaultValue = origin.localScaleDefaultValue;
                        skeletonNode.LocalRotationDefaultValue = origin.localRotationDefaultValue;
                        skeletonNode.LocalTranslationDefaultValue = origin.localTranslationDefaultValue;
                    }

                    rigBuilderData.SkeletonNodes[i] = skeletonNode;
                }

                length = translationChannels == null ? 0 : translationChannels.Length;
                rigBuilderData.TranslationChannels = new NativeList<LocalTranslationChannel>(length, Allocator.Temp);
                rigBuilderData.TranslationChannels.ResizeUninitialized(length);
                for (int i = 0; i < length; ++i)
                {
                    ref readonly var origin = ref translationChannels[i];

                    rigBuilderData.TranslationChannels[i] = new LocalTranslationChannel()
                    {
                        Id = origin.Id,
                        DefaultValue = origin.DefaultValue
                    };
                }

                length = rotationChannels == null ? 0 : rotationChannels.Length;
                rigBuilderData.RotationChannels = new NativeList<LocalRotationChannel>(length, Allocator.Temp);
                rigBuilderData.RotationChannels.ResizeUninitialized(length);
                for (int i = 0; i < length; ++i)
                {
                    ref readonly var origin = ref rotationChannels[i];

                    rigBuilderData.RotationChannels[i] = new LocalRotationChannel()
                    {
                        Id = origin.Id,
                        DefaultValue = origin.DefaultValue
                    };
                }

                length = scaleChannels == null ? 0 : scaleChannels.Length;
                rigBuilderData.ScaleChannels = new NativeList<LocalScaleChannel>(length, Allocator.Temp);
                rigBuilderData.ScaleChannels.ResizeUninitialized(length);
                for (int i = 0; i < length; ++i)
                {
                    ref readonly var origin = ref scaleChannels[i];

                    rigBuilderData.ScaleChannels[i] = new LocalScaleChannel()
                    {
                        Id = origin.Id,
                        DefaultValue = origin.DefaultValue
                    };
                }

                length = floatChannels == null ? 0 : floatChannels.Length;
                rigBuilderData.FloatChannels = new NativeList<Unity.Animation.FloatChannel>(length, Allocator.Temp);
                rigBuilderData.FloatChannels.ResizeUninitialized(length);
                for (int i = 0; i < length; ++i)
                {
                    ref readonly var origin = ref floatChannels[i];

                    //UnityEngine.Debug.LogError($"Rig {origin.Id} To {((StringHash)origin.Id).Id}");
                    rigBuilderData.FloatChannels[i] = new Unity.Animation.FloatChannel()
                    {
                        Id = origin.Id,
                        DefaultValue = origin.DefaultValue
                    };
                }

                length = intChannels == null ? 0 : intChannels.Length;
                rigBuilderData.IntChannels = new NativeList<Unity.Animation.IntChannel>(length, Allocator.Temp);
                rigBuilderData.IntChannels.ResizeUninitialized(length);
                for (int i = 0; i < length; ++i)
                {
                    ref readonly var origin = ref intChannels[i];

                    rigBuilderData.IntChannels[i] = new Unity.Animation.IntChannel()
                    {
                        Id = origin.Id,
                        DefaultValue = origin.DefaultValue
                    };
                }

                length = axes == null ? 0 : axes.Length;
                rigBuilderData.Axes = new NativeList<Unity.Animation.Axis>(length, Allocator.Temp);
                rigBuilderData.Axes.ResizeUninitialized(length);
                for (int i = 0; i < length; ++i)
                {
                    ref readonly var origin = ref axes[i];

                    axis.ScalingOffset = origin.scalingOffset;
                    axis.ScalingPivot = origin.scalingPivot;
                    axis.RotationOffset = origin.rotationOffset;
                    axis.RotationPivot = origin.rotationPivot;
                    axis.PreRotation = origin.preRotation;
                    axis.PostRotation = origin.postRotation;

                    rigBuilderData.Axes[i] = axis;
                }

                var rigDefinition = RigBuilder.CreateRigDefinition(rigBuilderData);

                rigBuilderData.Dispose();

                UnityEngine.Assertions.Assert.IsFalse(rigDefinition.Value.Skeleton.BoneCount > rigDefinition.Value.Bindings.TranslationBindings.Length, $"{rigDefinition.Value.Skeleton.BoneCount} > {rigDefinition.Value.Bindings.TranslationBindings.Length}");
                UnityEngine.Assertions.Assert.IsFalse(rigDefinition.Value.Skeleton.BoneCount > rigDefinition.Value.Bindings.RotationBindings.Length);
                UnityEngine.Assertions.Assert.IsFalse(rigDefinition.Value.Skeleton.BoneCount > rigDefinition.Value.Bindings.ScaleBindings.Length);

                return rigDefinition;
            }

            public BlobAssetReference<RigDefinition> ToAsset() => ToAsset(usePose);

            public int[] ToParentIndices()
            {
                int numSkeletonNodes = skeletonNodes.Length;
                var parentIndices = new int[numSkeletonNodes];
                for (int i = 0; i < numSkeletonNodes; ++i)
                    parentIndices[i] = skeletonNodes[i].parentIndex;

                return parentIndices;
            }

            public string[] ToJointPaths()
            {
                int numSkeletonNodes = skeletonNodes.Length;
                var jointPaths = new string[numSkeletonNodes];

                jointPaths[0] = string.Empty;

                if (usePose)
                {
                    for (int i = 1; i < numSkeletonNodes; ++i)
                        jointPaths[i] = skeletonNodes[i].path;
                }
                else
                {
                    var boneNames = new Dictionary<string, string>();

                    var humanBones = avatar.humanDescription.human;
                    foreach (var humanBone in humanBones)
                        boneNames.Add(humanBone.humanName, humanBone.boneName);

                    var stringBuilder = new StringBuilder();
                    for (int i = 1; i < numSkeletonNodes; ++i)
                    {
                        stringBuilder.Clear();

                        ReplacePath(stringBuilder, boneNames, skeletonNodes[i].path);

                        jointPaths[i] = stringBuilder.ToString();
                    }
                }

                return jointPaths;
            }

            /*public HumanPoseHandler ToHumanPoseHandler(bool isInit, out string[] jointPaths)
            {
                jointPaths = ToJointPaths();

                var humanPoseHandler = new HumanPoseHandler(avatar, jointPaths);

                if(isInit)
                {
                    var boneTransforms = new NativeArray<BoneTransform>(skeletonNodes.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                    ToBoneTransforms(ref boneTransforms);

                    humanPoseHandler.SetInternalAvatarPose(boneTransforms.Reinterpret<float>(BoneTransform.SIZE));

                    boneTransforms.Dispose();
                }

                return humanPoseHandler;
            }*/

            public BoneTransform ToBoneTransform(int index, bool usePose)
            {
                ref readonly var skeletonNode = ref skeletonNodes[index];

                BoneTransform boneTransform;
                if (usePose)
                {
                    boneTransform.translation = skeletonNode.localTranslationPoseValue;
                    boneTransform.rotation = skeletonNode.localRotationPoseValue;
                }
                else
                {
                    boneTransform.translation = skeletonNode.localTranslationDefaultValue;
                    boneTransform.rotation = skeletonNode.localRotationDefaultValue;
                }

                return boneTransform;
            }

            public void ToBoneTransforms(ref NativeArray<BoneTransform> boneTransforms, int startIndex = 0, bool usePose = true)
            {
                BoneTransform boneTransform;
                int length = Mathf.Min(skeletonNodes.Length, boneTransforms.Length);
                if (usePose)
                {
                    for (int i = 0; i < length; ++i)
                    {
                        ref readonly var skeletonNode = ref skeletonNodes[i + startIndex];

                        boneTransform.translation = skeletonNode.localTranslationPoseValue;
                        boneTransform.rotation = skeletonNode.localRotationPoseValue;

                        boneTransforms[i] = boneTransform;
                    }
                }
                else
                {
                    for (int i = 0; i < length; ++i)
                    {
                        ref readonly var skeletonNode = ref skeletonNodes[i + startIndex];

                        boneTransform.translation = skeletonNode.localTranslationDefaultValue;
                        boneTransform.rotation = skeletonNode.localRotationDefaultValue;

                        boneTransforms[i] = boneTransform;
                    }
                }
            }

            public NativeArray<BoneTransform> ToBoneTransforms(Allocator allocator, bool usePose = true)
            {
                var boneTransforms = new NativeArray<BoneTransform>(skeletonNodes.Length, allocator, NativeArrayOptions.UninitializedMemory);

                ToBoneTransforms(ref boneTransforms, 0, usePose);

                return boneTransforms;
            }

            public void ApplyBoneTransforms(in NativeArray<BoneTransform> boneTransforms, int startIndex = 0)
            {
                BoneTransform boneTransform;
                int length = Mathf.Min(skeletonNodes.Length, boneTransforms.Length);
                for (int i = 0; i < length; ++i)
                {
                    boneTransform = boneTransforms[i];

                    ref var skeletonNode = ref skeletonNodes[i + startIndex];

                     skeletonNode.localTranslationDefaultValue = boneTransform.translation;
                     skeletonNode.localRotationDefaultValue = boneTransform.rotation;
                }
            }

            public void RemapHumanPoseTo(ref Rig rig)
            {
                /*var unhumanJointIndices = new Dictionary<string, int>();

                var jointPaths = ToJointPaths();
                int numJointPaths = jointPaths.Length;
                for (int i = 0; i < numJointPaths; ++i)
                    unhumanJointIndices[SkeletonNode.PathToName(jointPaths[i])] = i;

                var humanBones = avatar.humanDescription.human;
                foreach (var humanBone in humanBones)
                {
                    unhumanJointIndices.Remove(humanBone.humanName);
                    unhumanJointIndices.Remove(humanBone.boneName); 
                }*/

                var sourceHumanPoseHandler = new HumanPoseHandler(avatar, ToJointPaths());// ToHumanPoseHandler(true, out _);

                var boneTransforms = ToBoneTransforms(Allocator.Temp, false);
                sourceHumanPoseHandler.SetInternalAvatarPose(boneTransforms.Reinterpret<float>(BoneTransform.SIZE));
                boneTransforms.Dispose();

                var humanPose = new HumanPose();

                sourceHumanPoseHandler.GetInternalHumanPose(ref humanPose);

                sourceHumanPoseHandler.Dispose();

                var jointPaths = rig.ToJointPaths();

                var destinationHumanPoseHandler = new HumanPoseHandler(rig.avatar, jointPaths);// rig.ToHumanPoseHandler(false, out _);

                //实际上这里会获得TPose数据
                destinationHumanPoseHandler.SetInternalHumanPose(ref humanPose);

                //boneTransforms = new NativeArray<BoneTransform>(rig.skeletonNodes.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                boneTransforms = new NativeArray<BoneTransform>(rig.skeletonNodes.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);// rig.ToBoneTransforms(Allocator.Temp, false);

                destinationHumanPoseHandler.GetInternalAvatarPose(boneTransforms.Reinterpret<float>(BoneTransform.SIZE));

                destinationHumanPoseHandler.Dispose();

                var humanBones = rig.avatar.humanDescription.human;
                string jointName;
                int numJointPaths = jointPaths.Length;
                bool isHuman;
                for(int i = 0; i < numJointPaths; ++i)
                {
                    jointName = SkeletonNode.PathToName(jointPaths[i]);

                    isHuman = false;
                    foreach (var humanBone in humanBones)
                    {
                        if(humanBone.boneName == jointName)
                        {
                            isHuman = true;
                            break;
                        }
                    }

                    if (!isHuman)
                        boneTransforms[i] = rig.ToBoneTransform(i, false);
                }

                rig.ApplyBoneTransforms(boneTransforms);

                boneTransforms.Dispose();
            }

#if UNITY_EDITOR
            public AnimationClip ResampleToPose(AnimationClip animationClip)
            {
                var humanBoneNames = new Dictionary<string, string>();

                var humanBones = avatar.humanDescription.human;
                foreach (var humanBone in humanBones)
                    humanBoneNames.Add(humanBone.boneName, humanBone.humanName);

                var jointPaths = ToJointPaths();
                var humanPoseHandler = new HumanPoseHandler(avatar, jointPaths);

                using (var boneTransforms = ToBoneTransforms(Allocator.Temp, false))
                    humanPoseHandler.SetInternalAvatarPose(boneTransforms.Reinterpret<float>(BoneTransform.SIZE));

                return animationClip.ResampleToPose(
                    humanPoseHandler, //ToHumanPoseHandler(true, out var jointPaths), 
                    jointPaths,
                    ToParentIndices(), 
                    AnimationHumanUtility.CreateSampleIndices(jointPaths, humanBoneNames));
            }
#endif
            public bool Equals(Rig other)
            {
                int length = skeletonNodes == null ? 0 : skeletonNodes.Length;
                if (length != (other.skeletonNodes == null ? 0 : other.skeletonNodes.Length))
                    return false;

                if(length > 0)
                {
                    for(int i = 0; i < length; ++i)
                    {
                        if (!skeletonNodes[i].Equals(other.skeletonNodes[i]))
                            return false;
                    }
                }

                length = translationChannels == null ? 0 : translationChannels.Length;
                if (length != (other.translationChannels == null ? 0 : other.translationChannels.Length))
                    return false;

                if (length > 0)
                {
                    for (int i = 0; i < length; ++i)
                    {
                        ref readonly var source = ref translationChannels[i];
                        ref readonly var destination = ref other.translationChannels[i];
                        if (source.Id != destination.Id || source.DefaultValue != destination.DefaultValue)
                            return false;
                    }
                }

                length = rotationChannels == null ? 0 : rotationChannels.Length;
                if (length != (other.rotationChannels == null ? 0 : other.rotationChannels.Length))
                    return false;

                if (length > 0)
                {
                    for (int i = 0; i < length; ++i)
                    {
                        ref readonly var source = ref rotationChannels[i];
                        ref readonly var destination = ref other.rotationChannels[i];
                        if (source.Id != destination.Id || source.DefaultValue != destination.DefaultValue)
                            return false;
                    }
                }

                length = scaleChannels == null ? 0 : scaleChannels.Length;
                if (length != (other.scaleChannels == null ? 0 : other.scaleChannels.Length))
                    return false;

                if (length > 0)
                {
                    for (int i = 0; i < length; ++i)
                    {
                        ref readonly var source = ref scaleChannels[i];
                        ref readonly var destination = ref other.scaleChannels[i];
                        if (source.Id != destination.Id || source.DefaultValue != destination.DefaultValue)
                            return false;
                    }
                }

                length = floatChannels == null ? 0 : floatChannels.Length;
                if (length != (other.floatChannels == null ? 0 : other.floatChannels.Length))
                    return false;

                if (length > 0)
                {
                    for (int i = 0; i < length; ++i)
                    {
                        ref readonly var source = ref floatChannels[i];
                        ref readonly var destination = ref other.floatChannels[i];
                        if (source.Id != destination.Id || source.DefaultValue != destination.DefaultValue)
                            return false;
                    }
                }

                length = intChannels == null ? 0 : intChannels.Length;
                if (length != (other.intChannels == null ? 0 : other.intChannels.Length))
                    return false;

                if (length > 0)
                {
                    for (int i = 0; i < length; ++i)
                    {
                        ref readonly var source = ref intChannels[i];
                        ref readonly var destination = ref other.intChannels[i];
                        if (source.Id != destination.Id || source.DefaultValue != destination.DefaultValue)
                            return false;
                    }
                }

                length = axes == null ? 0 : axes.Length;
                if (length != (other.axes == null ? 0 : other.axes.Length))
                    return false;

                if (length > 0)
                {
                    for (int i = 0; i < length; ++i)
                    {
                        if (!axes[i].Equals(other.axes[i]))
                            return false;
                    }
                }


                return true;
            }
        }

        [Serializable]
        public struct Node
        {
            public string path;

            public int[] typeIndices;
        }

        [Serializable]
        public struct Instance
        {
            [Serializable]
            public struct Node
            {
                public int boneIndex;
                public int nodeIndex;
            }

            public int rigIndex;
            public Matrix4x4 matrix;
            [UnityEngine.Serialization.FormerlySerializedAs("transforms")]
            public Node[] nodes;
            public int[] typeIndices;
        }

        [Serializable]
        public struct Data
        {
            public Rig[] rigs;
            public Instance[] instances;
            public Node[] nodes;

            public BlobAssetReference<MeshInstanceRigDefinition> ToAsset(int instanceID, out ComponentTypeWrapper[] componentTypeWrappers)
            {
                ComponentTypeWrapper componentTypeWrapper;
                var componentTypeWrappersResults = new List<ComponentTypeWrapper>();
                var componentTypeWrapperIndices = new Dictionary<ComponentTypeWrapper, int>();
                using (var blobBuilder = new BlobBuilder(Allocator.Temp))
                {
                    ref var root = ref blobBuilder.ConstructRoot<MeshInstanceRigDefinition>();
                    root.instanceID = instanceID;

                    {
                        int numInstances = this.instances.Length, numNodes, i, j;
                        BlobBuilderArray<MeshInstanceRigDefinition.Rig.Node> nodes;
                        var instances = blobBuilder.Allocate(ref root.rigs, numInstances);
                        for (i = 0; i < numInstances; ++i)
                        {
                            ref readonly var sourceInstance = ref this.instances[i];
                            ref readonly var skeletonNode = ref rigs[sourceInstance.rigIndex].skeletonNodes[0];

                            ref var destinationInstance = ref instances[i];

                            destinationInstance.index = sourceInstance.rigIndex;
                            destinationInstance.transform = sourceInstance.matrix;

                            destinationInstance.rootRotation = skeletonNode.localRotationDefaultValue;
                            destinationInstance.rootScale = skeletonNode.localScaleDefaultValue;
                            destinationInstance.rootTranslation = skeletonNode.localTranslationDefaultValue;

                            if (sourceInstance.typeIndices != null && sourceInstance.typeIndices.Length > 0)
                            {
                                componentTypeWrapper = new ComponentTypeWrapper(sourceInstance.typeIndices);
                                if (!componentTypeWrapperIndices.TryGetValue(componentTypeWrapper, out destinationInstance.componentTypesIndex))
                                {
                                    destinationInstance.componentTypesIndex = componentTypeWrappersResults.Count;

                                    componentTypeWrappersResults.Add(componentTypeWrapper);
                                }
                            }
                            else
                                destinationInstance.componentTypesIndex = -1;

                            numNodes = sourceInstance.nodes == null ? 0 : sourceInstance.nodes.Length;
                            nodes = blobBuilder.Allocate(ref destinationInstance.nodes, numNodes);
                            for (j = 0; j < numNodes; ++j)
                            {
                                ref readonly var sourceNode = ref sourceInstance.nodes[j];
                                ref var destinationNode = ref nodes[j];
                                destinationNode.index = sourceNode.boneIndex;
                                destinationNode.nodeIndex = sourceNode.nodeIndex;
                            }
                        }
                    }

                    {
                        int i, j, numNodes = this.nodes.Length, numTypeIndices;
                        var nodes = blobBuilder.Allocate(ref root.nodes, numNodes);
                        BlobBuilderArray<int> typeIndices;
                        for (i = 0; i < numNodes; ++i)
                        {
                            ref readonly var sourceNode = ref this.nodes[i];
                            ref var destinationNode = ref nodes[i];

                            numTypeIndices = sourceNode.typeIndices == null ? 0 : sourceNode.typeIndices.Length;

                            typeIndices = blobBuilder.Allocate(ref destinationNode.typeIndices, numTypeIndices);
                            for (j = 0; j < numTypeIndices; ++j)
                                typeIndices[j] = sourceNode.typeIndices[j];
                        }
                    }

                    componentTypeWrappers = componentTypeWrappersResults.ToArray();

                    return blobBuilder.CreateBlobAssetReference<MeshInstanceRigDefinition>(Allocator.Persistent);
                }
            }

            public void Create(
                bool usePose, 
                Transform root, 
                IList<Type> types, 
                IDictionary<Component, int> outComponentRigIndices = null, 
                MeshInstanceRendererDatabase.MaterialPropertyOverride materialPropertyOverride = null)
            {
                if(outComponentRigIndices == null)
                    outComponentRigIndices = new Dictionary<Component, int>();

                var rigIndices = new Dictionary<Rig, int>();
                rigs = CreateRigs(usePose, root.gameObject, rigIndices, outComponentRigIndices, materialPropertyOverride);

                var bones = new List<RigIndexToBone>();
                var results = new List<Instance.Node>();
                var instances = new List<Instance>();
                var nodes = new List<Node>();
                var nodeIndices = new Dictionary<Transform, int>();
                var typeIndices = new Dictionary<Type, int>();
                var typeIndexSet = new HashSet<int>();
                Component component;
                Transform transform;
                Instance instance;
                int numTypeIndices;
                foreach (var pair in outComponentRigIndices)
                {
                    instance.rigIndex = pair.Value;

                    component = pair.Key;
                    transform = component.transform;
                    instance.matrix = transform.localToWorldMatrix;

                    __ExtractBoneTransforms(component, bones);

                    typeIndexSet.Clear();

                    __ExposeTransforms(
                        root,
                        results, 
                        bones,
                        nodes,
                        types,
                        typeIndexSet, 
                        typeIndices, 
                        nodeIndices);

                    instance.nodes = results.ToArray();

                    numTypeIndices = typeIndexSet.Count;
                    if (numTypeIndices > 0)
                    {
                        instance.typeIndices = new int[numTypeIndices];

                        typeIndexSet.CopyTo(instance.typeIndices);
                    }
                    else
                        instance.typeIndices = null;

                    instances.Add(instance);
                }

                this.instances = instances.ToArray();
                this.nodes = nodes.ToArray();
            }

            private static void __ExtractBoneTransforms(Component component, List<RigIndexToBone> rigIndexToBones)
            {
                var rigAuthoring = component as IRigAuthoring;
                if (rigAuthoring == null)
                {
                    ((Animator)component).ExtractBoneTransforms(rigIndexToBones);

                    CollectBones(component.transform, x =>
                    {
                        RigIndexToBone rigIndexToBone;
                        rigIndexToBone.Bone = x.transform;
                        rigIndexToBone.Index = rigIndexToBones.Count;
                        rigIndexToBones.Add(rigIndexToBone);
                    });
                }
                else
                    rigAuthoring.GetBones(rigIndexToBones);
            }

            private static void __ExposeTransforms(
                Transform root,
                List<Instance.Node> results,
                List<RigIndexToBone> bones,
                List<Node> nodes,
                IList<Type> types, 
                HashSet<int> typeIndexSet, 
                IDictionary<Type, int> typeIndices, 
                IDictionary<Transform, int> nodeIndices)
            {
                results.Clear();

                int typeIndex, numBones = bones.Count;
                Instance.Node result;
                Node node;
                RigIndexToBone bone;
                Type type;
                IExposeTransform[] exposeTransforms;
                List<int> typeIndiceList = null;
                for (int i = 0; i < numBones; ++i)
                {
                    bone = bones[i];
                    exposeTransforms = bone.Bone == null ? null : bone.Bone.GetComponents<IExposeTransform>();
                    if (exposeTransforms == null || exposeTransforms.Length < 1)
                        continue;

                    if (nodeIndices.TryGetValue(bone.Bone, out result.nodeIndex))
                        node = nodes[result.nodeIndex];
                    else
                    {
                        result.nodeIndex = nodes.Count;

                        nodeIndices[bone.Bone] = result.nodeIndex;

                        node.path = RigGenerator.ComputeRelativePath(bone.Bone, root);

                        if (typeIndiceList == null)
                            typeIndiceList = new List<int>();
                        else
                            typeIndiceList.Clear();

                        foreach (var exposeTransform in exposeTransforms)
                        {
                            type = exposeTransform.componentType;
                            /*type = exposeTransform.GetType();
                            while (type != null && !type.IsGenericType)
                                type = type.BaseType;

                            type = type == null ? null : type.GenericTypeArguments[0];*/

                            if (!typeIndices.TryGetValue(type, out typeIndex))
                            {
                                typeIndex = types.Count;
                                types.Add(type);

                                typeIndices[type] = typeIndex;
                            }

                            typeIndiceList.Add(typeIndex);
                        }

                        node.typeIndices = typeIndiceList.ToArray();

                        nodes.Add(node);
                    }

                    if (node.typeIndices != null)
                    {
                        foreach(int typeIndexToSet in node.typeIndices)
                            typeIndexSet.Add(typeIndexToSet);
                    }

                    result.boneIndex = bone.Index;

                    results.Add(result);
                }
            }
        }

        [SerializeField]
        internal string[] _types;

        [SerializeField]
        internal ComponentTypeWrapper[] _componentTypeWrappers;

        [SerializeField, HideInInspector]
        [UnityEngine.Serialization.FormerlySerializedAs("_bytes")]
        private byte[] __bytes;

        [SerializeField, HideInInspector]
        private int __rigCount;

        private InitType __initType;

        private TypeIndex[] __typeIndices;

        private BlobAssetReference<RigDefinition>[] __rigs;

        private BlobAssetReference<MeshInstanceRigDefinition> __definition;

        public override int instanceID => __definition.IsCreated ? __definition.Value.instanceID : 0;

        public BlobAssetReference<MeshInstanceRigDefinition> definition
        {
            get
            {
                Init();

                return __definition;
            }
        }

        protected override void _Dispose()
        {
            if (__definition.IsCreated)
            {
                __definition.Dispose();

                __definition = BlobAssetReference<MeshInstanceRigDefinition>.Null;
            }

            if(__rigs != null)
            {
                foreach (var rig in __rigs)
                    rig.Dispose();

                __rigs = null;
            }
        }

        protected override void _Destroy()
        {
            SingletonAssetContainerHandle handle;
            handle.instanceID = __definition.Value.instanceID;

            if ((__initType & InitType.TypeIndex) == InitType.TypeIndex)
            {
                int length = __typeIndices == null ? 0 : __typeIndices.Length;
                if (length > 0)
                {
                    var container = SingletonAssetContainer<int>.instance;

                    for (int i = 0; i < length; ++i)
                    {
                        handle.index = i;

                        container.Delete(handle);
                    }
                }

                __typeIndices = null;
            }

            if ((__initType & InitType.ComponentTypes) == InitType.ComponentTypes)
            {
                int length = _componentTypeWrappers == null ? 0 : _componentTypeWrappers.Length;
                if (length > 0)
                {
                    var container = SingletonAssetContainer<ComponentTypeSet>.instance;

                    for (int i = 0; i < length; ++i)
                    {
                        handle.index = i;

                        container.Delete(handle);
                    }
                }
            }

            if ((__initType & InitType.RigDefinition) == InitType.RigDefinition)
            {
                var container = SingletonAssetContainer<BlobAssetReference<RigDefinition>>.instance;

                for (int i = 0; i < __rigCount; ++i)
                {
                    handle.index = i;

                    container.Delete(handle);
                }
            }

            __initType = 0;
        }

        protected override void _Init()
        {
            __InitTypeIndices();
            __InitComponentTypes();
            __InitRigDefinitions();
        }

        private void __InitTypeIndices()
        {
            if ((__initType & InitType.TypeIndex) != InitType.TypeIndex)
            {
                __initType |= InitType.TypeIndex;

                int numTypes = _types == null ? 0 : _types.Length;
                if (numTypes > 0)
                {
                    var instance = SingletonAssetContainer<int>.instance;

                    SingletonAssetContainerHandle handle;
                    handle.instanceID = __definition.Value.instanceID;

                    TypeIndex typeIndex;
                    __typeIndices = new TypeIndex[numTypes];
                    for (int i = 0; i < numTypes; ++i)
                    {
                        typeIndex = TypeManager.GetTypeIndex(Type.GetType(_types[i]));

                        __typeIndices[i] = typeIndex;

                        handle.index = i;

                        instance[handle] = typeIndex;
                    }
                }
            }
        }

        private void __InitComponentTypes()
        {
            if ((__initType & InitType.ComponentTypes) != InitType.ComponentTypes)
            {
                __initType |= InitType.ComponentTypes;

                int numComponentTypeWrappers = _componentTypeWrappers == null ? 0 : _componentTypeWrappers.Length;
                if (numComponentTypeWrappers > 0)
                {
                    var instance = SingletonAssetContainer<ComponentTypeSet>.instance;

                    SingletonAssetContainerHandle handle;
                    handle.instanceID = __definition.Value.instanceID;

                    for (int i = 0; i < numComponentTypeWrappers; ++i)
                    {
                        handle.index = i;

                        instance[handle] = _componentTypeWrappers[i].ToComponentTypes(__typeIndices);
                    }
                }
            }
        }

        private void __InitRigDefinitions()
        {
            if ((__initType & InitType.RigDefinition) == InitType.RigDefinition)
                return;

            __initType |= InitType.RigDefinition;

            var instance = SingletonAssetContainer<BlobAssetReference<RigDefinition>>.instance;

            SingletonAssetContainerHandle handle;
            handle.instanceID = __definition.Value.instanceID;

            for (int i = 0; i < __rigCount; ++i)
            {
                handle.index = i;

                instance[handle] = __rigs[i];
            }
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (__bytes != null && __bytes.Length > 0)
            {
                if (__definition.IsCreated)
                    __definition.Dispose();

                if (__rigs != null)
                {
                    foreach (var rig in __rigs)
                    {
                        if (rig.IsCreated)
                            rig.Dispose();
                    }
                }

                unsafe
                {
                    fixed (byte* ptr = __bytes)
                    {
                        using (var reader = new MemoryBinaryReader(ptr, __bytes.LongLength))
                        {
                            __definition = reader.Read<MeshInstanceRigDefinition>();

                            __rigs = new BlobAssetReference<RigDefinition>[__rigCount];
                            for (int i = 0; i < __rigCount; ++i)
                                __rigs[i] = reader.Read<RigDefinition>();
                        }
                    }
                }

                __bytes = null;
            }

            __initType = 0;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            if (__definition.IsCreated)
            {
                using (var writer = new MemoryBinaryWriter())
                {
                    writer.Write(__definition);

                    __rigCount = __rigs == null ? 0 : __rigs.Length;
                    for (int i = 0; i < __rigCount; ++i)
                        writer.Write(__rigs[i]);

                    __bytes = writer.GetContentAsNativeArray().ToArray();
                }
            }
        }

#if UNITY_EDITOR
        public MeshInstanceMaterialPropertySettings materialPropertySettings;

        [HideInInspector]
        public Transform root;

        public Data data;

        public Type[] types
        {
            set
            {
                int numTypes = value.Length;

                _types = new string[numTypes];
                for (int i = 0; i < numTypes; ++i)
                    _types[i] = value[i].AssemblyQualifiedName;
            }
        }

        public void Create(
            bool usePose, 
            Transform root,
            IDictionary<Component, int> outComponentRigIndices = null)
        {
            var types = new List<Type>();

            data.Create(usePose, root, types, outComponentRigIndices, materialPropertySettings == null ? null : materialPropertySettings.Override);

            this.types = types.ToArray();
        }

        public void Create(bool usePose = false, IDictionary<Component, int> outComponentRigIndices = null)
        {
            var types = new List<Type>();

            data.Create(usePose, root, types, outComponentRigIndices, materialPropertySettings == null ? null : materialPropertySettings.Override);

            this.types = types.ToArray();
        }

        public void Rebuild()
        {
            Dispose();

            int instanceID = GetInstanceID();

            __definition = data.ToAsset(instanceID, out _componentTypeWrappers);

            __rigCount = data.rigs.Length;

            __rigs = new BlobAssetReference<RigDefinition>[__rigCount];
            for (int i = 0; i < __rigCount; ++i)
                __rigs[i] = data.rigs[i].ToAsset();

            ((ISerializationCallbackReceiver)this).OnBeforeSerialize();
        }

        public void EditorMaskDirty()
        {
            Rebuild();

            EditorUtility.SetDirty(this);
        }
#endif
    }
}