using System;
using System.IO;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Collections;
using Unity.Animation;
using Unity.Animation.Hybrid;
using UnityEngine;

#if UNITY_EDITOR
using System.Reflection;
using System.Text;
using UnityEditor;
#endif

namespace ZG
{
#if UNITY_EDITOR
    [ComponentBindingProcessor(typeof(MeshRenderer))]
    internal class MeshRendererBindingProcessor : ComponentBindingProcessor<MeshRenderer>
    {
        protected override ChannelBindType Process(EditorCurveBinding binding)
        {
            return ChannelBindType.Float;
        }
    }

    public struct MeshInstanceClipEvent
    {
        public string type;
        public int state;
    }

    public interface IMeshInstanceClipEventFactory
    {
        bool Create(AnimationEvent input, out MeshInstanceClipEvent output);
    }

    public static class MeshInstanceClipUtility
    {
        private class SynchronizationTag : MonoBehaviour, ISynchronizationTag
        {
            public MeshInstanceClipEvent clipEvent;

            public StringHash Type => clipEvent.type;

            public int State 
            {
                get => clipEvent.state;
                set => clipEvent.state = value;
            }
        }

        public static IMeshInstanceClipEventFactory[] __eventFactories;

        public static IMeshInstanceClipEventFactory[] eventFactories
        {
            get
            {
                if(__eventFactories == null)
                {
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    Type[] types;
                    List<IMeshInstanceClipEventFactory> eventFactories = null;
                    IMeshInstanceClipEventFactory eventFactory;
                    foreach (var assembly in assemblies)
                    {
                        types = assembly.GetTypes();
                        foreach(var type in types)
                        {
                            if(!type.IsAbstract && !type.IsGenericType && Array.IndexOf(type.GetInterfaces(), typeof(IMeshInstanceClipEventFactory)) != -1)
                            {
                                try
                                {
                                    eventFactory = (IMeshInstanceClipEventFactory)Activator.CreateInstance(type);
                                    if (eventFactory != null)
                                    {
                                        if (eventFactories == null)
                                            eventFactories = new List<IMeshInstanceClipEventFactory>();

                                        eventFactories.Add(eventFactory);
                                    }
                                }
                                catch(Exception e)
                                {
                                    UnityEngine.Debug.LogException(e.InnerException ?? e);
                                }
                            }
                        }
                    }

                    if(eventFactories != null)
                        __eventFactories = eventFactories.ToArray();
                }

                return __eventFactories;
            }
        }

        /*public static AnimationClip BakeAnimator(this AnimationClip animationClip)
        {
            AnimationClip result = null;
            AnimationCurve animationCurve;
            EditorCurveBinding curveBinding;
            string propertyName;
            var curveBindings = AnimationUtility.GetCurveBindings(animationClip);
            int numCurveBindings = curveBindings == null ? 0 : curveBindings.Length;
            for(int i = 0; i < numCurveBindings; ++i)
            {
                curveBinding = curveBindings[i];
                if (curveBinding.type == typeof(Animator))
                {
                    propertyName = null;
                    switch (curveBinding.propertyName)
                    {
                        case "RootT.x":
                            propertyName = "m_LocalPosition.x";
                            break;
                        case "RootT.y":
                            propertyName = "m_LocalPosition.y";
                            break;
                        case "RootT.z":
                            propertyName = "m_LocalPosition.z";
                            break;
                        case "RootQ.x":
                            propertyName = "m_LocalRotation.x";
                            break;
                        case "RootQ.y":
                            propertyName = "m_LocalRotation.y";
                            break;
                        case "RootQ.z":
                            propertyName = "m_LocalRotation.z";
                            break;
                        case "RootQ.w":
                            propertyName = "m_LocalRotation.w";
                            break;
                    }

                    if(propertyName != null)
                    {
                        if (result == null)
                            result = AnimationClip.Instantiate(animationClip);

                        animationCurve = AnimationUtility.GetEditorCurve(result, curveBinding);
                        AnimationUtility.SetEditorCurve(result, curveBinding, null);

                        curveBinding.path = string.Empty;
                        curveBinding.propertyName = propertyName;
                        curveBinding.type = typeof(Transform);

                        AnimationUtility.SetEditorCurve(result, curveBinding, animationCurve);
                    }
                }
            }

            return result == null ? animationClip : result;
        }*/

        public static BlobAssetReference<Clip> ToDenseClipWithEvents(this AnimationClip animationClip, BindingHashGenerator hasher = default)
        {
            var temp = animationClip;// BakeAnimator(animationClip);

            BlobAssetReference<Clip> result;
            var animationEvents = AnimationUtility.GetAnimationEvents(temp);
            if (animationEvents == null)
                result = temp.ToDenseClip(hasher);
            else
            {
                MeshInstanceClipEvent clipEvent;
                GameObject gameObject;
                List<AnimationEvent> results = null;
                List<GameObject> gameObjects = null;
                var eventFactories = MeshInstanceClipUtility.eventFactories;
                foreach (var animationEvent in animationEvents)
                {
                    foreach (var eventFactory in eventFactories)
                    {
                        if (eventFactory.Create(animationEvent, out clipEvent))
                        {
                            gameObject = __CreateSynchronizationTag(clipEvent);

                            if (gameObjects == null)
                                gameObjects = new List<GameObject>();

                            gameObjects.Add(gameObject);

                            animationEvent.objectReferenceParameter = gameObject;

                            if (results == null)
                                results = new List<AnimationEvent>();

                            results.Add(animationEvent);
                        }
                    }
                }

                if (results == null)
                    result = temp.ToDenseClip(hasher);
                else
                {
                    temp = temp == animationClip ? UnityEngine.Object.Instantiate(animationClip) : temp;

                    AnimationUtility.SetAnimationEvents(temp, results.ToArray());

                    result = temp.ToDenseClip(hasher);

                    UnityEngine.Object.DestroyImmediate(temp);
                }

                if (gameObjects != null)
                {
                    foreach (var gameObjectToDestroy in gameObjects)
                        UnityEngine.Object.DestroyImmediate(gameObjectToDestroy);
                }
            }

            return result;
        }

        private static GameObject __CreateSynchronizationTag(in MeshInstanceClipEvent clipEvent)
        {
            var gameObject = new GameObject();
            gameObject.AddComponent<SynchronizationTag>().clipEvent = clipEvent;

            return gameObject;
        }
    }
#endif

    [CreateAssetMenu(fileName = "Mesh Instance Clip Database", menuName = "ZG/Mesh Instance/Clip Database")]
    public class MeshInstanceClipDatabase : MeshInstanceDatabase<MeshInstanceClipDatabase>, ISerializationCallbackReceiver
    {
        private enum MirrorAxis
        {
            None,
            X,
            Y,
            Z
        }

        [Flags]
        public enum AnimationFlag
        {
            Baked = 0x01, 
            InPlace = 0x04,
            Mirror = 0x08
        }

        [Flags]
        public enum ClipFlag
        {
            Looping = 0x01, 
            Mirror = 0x02, 
            InPlace = 0x04
        }

        [Flags]
        private enum InitType
        {
            Clip = 0x01, 
            RigRemapTable = 0x02
        }

        private class Asset<T> where T : UnityEngine.Object
        {
            private string __path;

            private string __name;

            private T __value;

#if UNITY_EDITOR
            public static implicit operator Asset<T>(T value)
            {
                if (value == null)
                    return null;

                Asset<T> asset = new Asset<T>();
                asset.__value = value;
                asset.__path = AssetDatabase.GetAssetPath(value);
                asset.__name = value.name;

                return asset;
            }

            public static implicit operator T(Asset<T> value)
            {
                if (value == null)
                    return null;

                if(value.__value == null)
                {
                    var assets = AssetDatabase.LoadAllAssetsAtPath(value.__path);
                    foreach(var asset in assets)
                    {
                        if(asset is T && asset.name == value.__name)
                        {
                            value.__value = (T)asset;

                            break;
                        }
                    }
                }

                return value.__value;
            }
#endif
        }

        private struct AvatarClip
        {
            public int remapIndex;

            public int animationIndex;

            public Asset<UnityEngine.Avatar> avatar;

            public Asset<AnimationClip> animationClip;
        }

        private struct RigName : IEquatable<RigName>
        {
            public int rigIndex;
            public string name;

            public bool Equals(RigName other)
            {
                return rigIndex == other.rigIndex && name == other.name;
            }

            public override int GetHashCode()
            {
                return rigIndex ^ name.GetHashCode();
            }

            public override string ToString()
            {
                return name;
            }
        }

        /*private struct RigAnimation : IEquatable<RigAnimation>
        {
            public int rigIndex;

            public int animtionIndex;

            public bool Equals(RigAnimation other)
            {
                return rigIndex == other.rigIndex && animtionIndex == other.animtionIndex;
            }

            public override int GetHashCode()
            {
                return rigIndex ^ animtionIndex;
            }
        }*/

        private struct RigRemap : IEquatable<RigRemap>
        {
            public bool isMirror;

            public int rigIndex;

            public Remap value;

            public bool Equals(RigRemap other)
            {
                return isMirror == other.isMirror && rigIndex == other.rigIndex && value.rigIndex == other.value.rigIndex && value.avatarIndex == other.value.avatarIndex;
            }

            public override int GetHashCode()
            {
                return (isMirror ? -1 : 1) * rigIndex ^ value.rigIndex;
            }
        }

        /*public struct RigClip : IEquatable<RigClip>
        {
            public int rigIndex;

            public int clipIndex;

            public bool Equals(RigClip other)
            {
                return rigIndex == other.rigIndex && clipIndex == other.clipIndex;
            }

            public override int GetHashCode()
            {
                return rigIndex ^ clipIndex;
            }
        }*/

        [Serializable]
        public struct Remap
        {
            public int rigIndex;

            public int avatarIndex;

            public Remap(int rigIndex, int avatarIndex)
            {
                this.rigIndex = rigIndex;
                this.avatarIndex = avatarIndex;
            }
        }

        [Serializable]
        public struct Rig
        {
            [UnityEngine.Serialization.FormerlySerializedAs("motionRootPath")]
            public string rootBonePath;

            public int index;

            public int defaultClipIndex;

            public int[] clipIndices;
        }

        [Serializable]
        public struct Clip
        {
            public string name;

            public ClipFlag flag;

            public int animationIndex;

            public int remapIndex;
        }

        [Serializable]
        public struct AnimationData : IEquatable<AnimationData>
        {
            public AnimationFlag flag;

            public AnimationClip clip;

            public bool Equals(AnimationData other)
            {
                return flag == other.flag && clip == other.clip;
            }
        }

        [Serializable]
        public struct RigData
        {
            public int index;

            public int defaultAnimationIndex;

            [UnityEngine.Serialization.FormerlySerializedAs("motionRootName")]
            public string rootBoneName;

            public AnimationData[] animations;
        }

        [Serializable]
        public struct Data
        {
#if UNITY_EDITOR
            public static bool isShowProgressBar = true;
#endif

            public Rig[] rigs;
            public Remap[] remaps;
            public Clip[] clips;
            public UnityEngine.Avatar[] avatars;

            public void ToAsset(
                int instanceID, 
                MeshInstanceRigDatabase.Rig[] dataRigs,
                ref NativeList<BlobAssetReference<RigRemapTable>> rigRemapTables,
                out BlobAssetReference<MeshInstanceClipFactoryDefinition> factroy,
                out BlobAssetReference<MeshInstanceClipDefinition> definition)
            {
                int i, j, numSkeletonNodes, numRigs = this.rigs.Length;
                RigName rigName;
                var skeletonNodeIndices = new Dictionary<RigName, int>();
                for (i = 0; i < numRigs; ++i)
                {
                    ref readonly var rig = ref rigs[i];
                    ref readonly var dataRig = ref dataRigs[rig.index];

                    rigName.rigIndex = rig.index;

                    numSkeletonNodes = dataRig.skeletonNodes.Length;
                    for (j = 1; j < numSkeletonNodes; ++j)
                    {
                        rigName.name = __QuerySkeletonBoneName(dataRig.skeletonNodes[j].path);

                        skeletonNodeIndices[rigName] = j;
                    }
                }

                using (var translationChannels = new NativeList<ChannelMap>(Allocator.Temp))
                //using (var scaleChannels = new NativeList<ChannelMap>(Allocator.Temp))
                using (var translationOffsets = new NativeList<RigTranslationOffset>(Allocator.Temp))
                using (var definitionBuilder = new BlobBuilder(Allocator.Temp))
                {
                    ref var definitionRoot = ref definitionBuilder.ConstructRoot<MeshInstanceClipDefinition>();

                    var rigDefinitions = new NativeParallelHashMap<int, BlobAssetReference<RigDefinition>>(1, Allocator.Temp);

                    var remapResults = new NativeList<MeshInstanceClipDefinition.Remap>(Allocator.Temp);

                    var rigRemapIndices = new NativeParallelHashMap<RigRemap, int>(1, Allocator.Temp);

                    var remapIndices = new NativeList<int>(Allocator.Temp);

                    var rotationOffsets = new NativeList<RigRotationOffset>(Allocator.Temp);
                    var rotationChannels = new NativeList<ChannelMap>(Allocator.Temp);

                    bool isMirror;
                    MirrorAxis mirrorAxis;
                    int numClips = this.clips.Length, 
                        numRemaps, 
                        remapIndex, 
                        destinationSkeletonNodeIndex,
                        destinationTargetSkeletonNodeIndex,
                        sourceSkeletonNodeIndex, 
                        sourceTargetSkeletonNodeIndex, 
                        k;//, numRotationChannels;
                    MeshInstanceRigDatabase.Rig destinationDataRig;
                    MeshInstanceClipDefinition.Remap remapResult;
                    Remap remap;
                    RigRemap rigRemap;
                    BlobAssetReference<RigDefinition> sourceRigDefinition, destinationRigDefinition;
                    BlobBuilderArray<int> clipRemapIndices;
                    string mirrorSkeletonBoneName;
                    var rigRemapQuery = new RigRemapQuery();
                    HumanBone[] human;
                    var originSkeletonNodeIndices = new Dictionary<string, int>();
                    var clips = definitionBuilder.Allocate(ref definitionRoot.clips, numClips);
                    for (i = 0; i < numClips; ++i)
                    {
                        ref readonly var sourceClip = ref this.clips[i];
                        ref var destinationClip = ref clips[i];

#if UNITY_EDITOR
                        if (isShowProgressBar &&
                            EditorUtility.DisplayCancelableProgressBar("Mesh Instance Clip To Asset", $"{i}/{numClips}", i * 1.0f / numClips))
                            break;
#endif

                        remapIndices.Clear();

                        isMirror = (sourceClip.flag & ClipFlag.Mirror) == ClipFlag.Mirror;
                        if (sourceClip.remapIndex != -1 || isMirror)
                        {
                            mirrorAxis = isMirror ? MirrorAxis.X : MirrorAxis.None;
                            for (j = 0; j < numRigs; ++j)
                            {
                                ref readonly var rig = ref this.rigs[j];
                                if (Array.IndexOf(rig.clipIndices, i) == -1)
                                    continue;

                                remap = sourceClip.remapIndex == -1 ? new Remap(rig.index, i) : this.remaps[sourceClip.remapIndex];

                                rigRemap.isMirror = isMirror;
                                rigRemap.rigIndex = rig.index;
                                rigRemap.value = remap;
                                if (!rigRemapIndices.TryGetValue(rigRemap, out remapIndex))
                                {
                                    remapIndex = remapResults.Length;

                                    remapResult.sourceRigIndex = remap.rigIndex;
                                    remapResult.destinationRigIndex = rig.index;

                                    remapResults.Add(remapResult);

                                    rigRemapIndices[rigRemap] = remapIndex;

                                    ref readonly var sourceDataRig = ref dataRigs[remap.rigIndex];

                                    originSkeletonNodeIndices.Clear();
                                    numSkeletonNodes = sourceDataRig.skeletonNodes.Length;
                                    for (k = 1; k < numSkeletonNodes; ++k)
                                        originSkeletonNodeIndices[__QuerySkeletonBoneName(sourceDataRig.skeletonNodes[k].path)] = k;

                                    destinationDataRig = dataRigs[rig.index];

                                    destinationDataRig.skeletonNodes = (MeshInstanceRigDatabase.SkeletonNode[])destinationDataRig.skeletonNodes.Clone();

                                    sourceDataRig.RemapHumanPoseTo(ref destinationDataRig);

                                    translationChannels.Clear();
                                    rotationChannels.Clear();
                                    //scaleChannels.Clear();
                                    translationOffsets.Clear();
                                    rotationOffsets.Clear();

                                    translationOffsets.Add(default);
                                    rotationOffsets.Add(default);

                                    __QueryRemap(
                                        false,
                                        mirrorAxis, 
                                        0, 
                                        0,
                                        0,
                                        0, 
                                        sourceDataRig,
                                        destinationDataRig,
                                        translationChannels,
                                        rotationChannels,
                                        //scaleChannels,
                                        translationOffsets,
                                        rotationOffsets);

                                    rigName.rigIndex = rig.index;

                                    human = sourceClip.remapIndex == -1 ? null : avatars[remap.avatarIndex].humanDescription.human;
                                    if (human != null)
                                    {
                                        foreach (var humanBone in human)
                                        {
                                            rigName.name = humanBone.humanName;

                                            if (!skeletonNodeIndices.TryGetValue(rigName, out destinationSkeletonNodeIndex))
                                                continue;

                                            if (isMirror)
                                            {
                                                rigName.name = __MirrorSkeletonBoneName(rigName.name);
                                                if (!skeletonNodeIndices.TryGetValue(rigName, out destinationTargetSkeletonNodeIndex))
                                                {
                                                    UnityEngine.Debug.LogError($"[REMAP]Missing The Bone {humanBone.boneName}=>{rigName.name} Of Origin Skeleton {sourceDataRig.name}.", avatars[remap.avatarIndex]);

                                                    destinationTargetSkeletonNodeIndex = destinationSkeletonNodeIndex;
                                                }
                                            }
                                            else
                                                destinationTargetSkeletonNodeIndex = destinationSkeletonNodeIndex;

                                            mirrorSkeletonBoneName = humanBone.boneName;

                                            if (!originSkeletonNodeIndices.TryGetValue(mirrorSkeletonBoneName, out sourceSkeletonNodeIndex))
                                            {
                                                UnityEngine.Debug.LogError($"[REMAP]Missing The Bone {humanBone.boneName}=>{rigName.name} Of Origin Skeleton {sourceDataRig.name}.", avatars[remap.avatarIndex]);

                                                continue;
                                            }

                                            if (isMirror)
                                            {
                                                mirrorSkeletonBoneName = __MirrorSkeletonBoneName(mirrorSkeletonBoneName);
                                                if (!originSkeletonNodeIndices.TryGetValue(mirrorSkeletonBoneName, out sourceTargetSkeletonNodeIndex))
                                                {
                                                    UnityEngine.Debug.LogError($"The mirror skeleton node {mirrorSkeletonBoneName} has not been found!");

                                                    sourceTargetSkeletonNodeIndex = sourceSkeletonNodeIndex;
                                                }
                                            }
                                            else
                                                sourceTargetSkeletonNodeIndex = sourceSkeletonNodeIndex;

                                            __QueryRemap(
                                                //isRoot,
                                                humanBone.humanName == "Hips",
                                                mirrorAxis,
                                                sourceTargetSkeletonNodeIndex,
                                                destinationTargetSkeletonNodeIndex,
                                                sourceSkeletonNodeIndex,
                                                destinationSkeletonNodeIndex,
                                                sourceDataRig,
                                                destinationDataRig,
                                                translationChannels,
                                                rotationChannels,
                                                //scaleChannels,
                                                translationOffsets,
                                                rotationOffsets);

                                            //originSkeletonNodeIndices.Remove(humanBone.boneName);
                                            //UnityEngine.Debug.Log($"Remap From {originPath} To {path}");

                                            //isRoot = false;
                                        }

                                        foreach (var humanBone in human)
                                            originSkeletonNodeIndices.Remove(humanBone.boneName);
                                    }

                                    foreach (var pair in originSkeletonNodeIndices)
                                    {
                                        rigName.name = pair.Key;

                                        if (!skeletonNodeIndices.TryGetValue(rigName, out destinationSkeletonNodeIndex))
                                        {
                                            UnityEngine.Debug.LogWarning($"[REMAP]Missing SkeletonNode {rigName.name}");

                                            continue;
                                        }

                                        mirrorSkeletonBoneName = __MirrorSkeletonBoneName(rigName.name);
                                        if (isMirror)
                                        {
                                            if (!skeletonNodeIndices.TryGetValue(rigName, out destinationTargetSkeletonNodeIndex))
                                            {
                                                UnityEngine.Debug.LogWarning($"[REMAP]Missing The Bone {rigName.name} Of Origin Skeleton {sourceDataRig.name}.", avatars[remap.avatarIndex]);

                                                destinationTargetSkeletonNodeIndex = destinationSkeletonNodeIndex;
                                            }
                                        }
                                        else
                                            destinationTargetSkeletonNodeIndex = destinationSkeletonNodeIndex;

                                        sourceSkeletonNodeIndex = pair.Value;

                                        if (isMirror)
                                        {
                                            if (!originSkeletonNodeIndices.TryGetValue(mirrorSkeletonBoneName, out sourceTargetSkeletonNodeIndex))
                                            {
                                                UnityEngine.Debug.LogError($"The mirror skeleton node {mirrorSkeletonBoneName} has not been found!");

                                                sourceTargetSkeletonNodeIndex = sourceSkeletonNodeIndex;
                                            }
                                        }
                                        else
                                            sourceTargetSkeletonNodeIndex = sourceSkeletonNodeIndex;

                                        __QueryRemap(
                                            false,
                                            mirrorAxis,
                                            sourceTargetSkeletonNodeIndex,
                                            destinationTargetSkeletonNodeIndex,
                                            sourceSkeletonNodeIndex,
                                            destinationSkeletonNodeIndex,
                                            sourceDataRig,
                                            destinationDataRig,
                                            translationChannels,
                                            rotationChannels,
                                            //scaleChannels,
                                            translationOffsets,
                                            rotationOffsets);
                                    }

                                    /*foreach (var skeletonNode in this.rigDatabase.rigs[sourceRig.index].skeletonNodes)
                                    {
                                        bool isContains = false;
                                        var humanName = MeshInstanceRigDatabase.SkeletonNode.PathToName(skeletonNode.path);
                                        foreach (var humanBone in human)
                                        {
                                            if (humanBone.humanName == humanName)
                                            {
                                                isContains = true;

                                                break;
                                            }
                                        }

                                        if (!isContains)
                                            UnityEngine.Debug.LogError(skeletonNode.path);
                                    }*/

                                    /*if (isMirror)
                                    {
                                        int numRotationChannels = rotationChannels.Length;
                                        for (k = 1; k < numRotationChannels; ++k)
                                        {
                                            ref var rotationChannel = ref rotationChannels.ElementAt(k);

                                            numSkeletonNodes = destinationDataRig.skeletonNodes.Length;
                                            for (int l = 1; l < numSkeletonNodes; ++l)
                                            {
                                                ref readonly var skeletonNode = ref destinationDataRig.skeletonNodes[l];
                                                if (skeletonNode.path != rotationChannel.DestinationId)
                                                    continue;

                                                rigName.name = __QuerySkeletonBoneName(skeletonNode.path);
                                                rigName.name = __MirrorSkeletonBoneName(rigName.name);
                                                skeletonNodeIndex = skeletonNodeIndices[rigName];
                                                if (skeletonNodeIndex != l)
                                                {
                                                    rotationChannel.DestinationId = destinationDataRig.skeletonNodes[skeletonNodeIndex].path;

                                                    //rotationChannels[l] = channelMap;
                                                }

                                                break;
                                            }
                                        }
                                    }*/

                                    rigRemapQuery.TranslationChannels = translationChannels.AsArray().ToArray();
                                    rigRemapQuery.RotationChannels = rotationChannels.AsArray().ToArray();
                                    //rigRemapQuery.ScaleChannels = scaleChannels.ToArray();
                                    rigRemapQuery.TranslationOffsets = translationOffsets.AsArray().ToArray();
                                    rigRemapQuery.RotationOffsets = rotationOffsets.AsArray().ToArray();

                                    if (!rigDefinitions.TryGetValue(remap.rigIndex, out sourceRigDefinition))
                                    {
                                        sourceRigDefinition = sourceDataRig.ToAsset(false);

                                        rigDefinitions[remap.rigIndex] = sourceRigDefinition;
                                    }

                                    if (!rigDefinitions.TryGetValue(rig.index, out destinationRigDefinition))
                                    {
                                        destinationRigDefinition = destinationDataRig.ToAsset(false);

                                        rigDefinitions[rig.index] = destinationRigDefinition;
                                    }

                                    rigRemapTables.Add(rigRemapQuery.ToRigRemapTable(sourceRigDefinition, destinationRigDefinition));
                                }

                                remapIndices.Add(remapIndex);
                            }
                        }

                        numRemaps = remapIndices.Length;

                        clipRemapIndices = definitionBuilder.Allocate(ref destinationClip.remapIndices, numRemaps);
                        for (k = 0; k < numRemaps; ++k)
                            clipRemapIndices[k] = remapIndices[k];

                        destinationClip.animationIndex = sourceClip.animationIndex;

                        destinationClip.flag = 0;
                        if ((sourceClip.flag & ClipFlag.Looping) == ClipFlag.Looping)
                            destinationClip.flag |= MeshInstanceClipFlag.Looping;

                        if ((sourceClip.flag & ClipFlag.Mirror) == ClipFlag.Mirror)
                            destinationClip.flag |= MeshInstanceClipFlag.Mirror;

                        if ((sourceClip.flag & ClipFlag.InPlace) == ClipFlag.InPlace)
                            destinationClip.flag |= MeshInstanceClipFlag.InPlace;
                    }

                    rotationOffsets.Dispose();
                    rotationChannels.Dispose();

                    remapIndices.Dispose();

                    rigRemapIndices.Dispose();

                    var enumerator = rigDefinitions.GetEnumerator();
                    while (enumerator.MoveNext())
                        enumerator.Current.Value.Dispose();

                    rigDefinitions.Dispose();

                    numRemaps = remapResults.Length;
                    var remaps = definitionBuilder.Allocate(ref definitionRoot.remaps, numRemaps);
                    for (i = 0; i < numRemaps; ++i)
                        remaps[i] = remapResults[i];

                    remapResults.Dispose();

                    definition = definitionBuilder.CreateBlobAssetReference<MeshInstanceClipDefinition>(Allocator.Persistent);
                }

                using (var factoryBuilder = new BlobBuilder(Allocator.Temp))
                {
                    ref var factoryRoot = ref factoryBuilder.ConstructRoot<MeshInstanceClipFactoryDefinition>();
                    factoryRoot.instanceID = instanceID;

                    var rigs = factoryBuilder.Allocate(ref factoryRoot.rigs, numRigs);

                    int numClipIndices;
                    BlobBuilderArray<int> rigClipIndices;
                    for (i = 0; i < numRigs; ++i)
                    {
                        ref readonly var sourceRig = ref this.rigs[i];
                        ref var destinationRig = ref rigs[i];

                        destinationRig.index = sourceRig.index;

                        destinationRig.defaultClipIndex = sourceRig.defaultClipIndex;

                        destinationRig.rootID = sourceRig.rootBonePath;

                        numClipIndices = sourceRig.clipIndices == null ? 0 : sourceRig.clipIndices.Length;
                        rigClipIndices = factoryBuilder.Allocate(ref destinationRig.clipIndices, numClipIndices);
                        for (j = 0; j < numClipIndices; ++j)
                            rigClipIndices[j] = sourceRig.clipIndices[j];
                    }

                    factroy = factoryBuilder.CreateBlobAssetReference<MeshInstanceClipFactoryDefinition>(Allocator.Persistent);

#if UNITY_EDITOR
                    if (isShowProgressBar)
                        EditorUtility.ClearProgressBar();
#endif
                }
            }

            private static Quaternion __MirrorRotation(MirrorAxis axis)
            {
                // Given an axis V and an angle A, the corresponding unmirrored quaternion Q = { Q.XYZ, Q.W } is:
                //
                //		Q = { V * sin(A/2), cos(A/2) }
                //
                //  mirror both the axis of rotation and the angle of rotation around that axis.
                // Therefore, the mirrored quaternion Q' for the axis V and angle A is:
                //
                //		Q' = { MirrorVector(V) * sin(-A/2), cos(-A/2) }
                //		Q' = { -MirrorVector(V) * sin(A/2), cos(A/2) }
                //		Q' = { -MirrorVector(V * sin(A/2)), cos(A/2) }
                //		Q' = { -MirrorVector(Q.XYZ), Q.W }
                //
                switch (axis)
                {
                    case MirrorAxis.X:
                        //rotation.y = -rotation.y;
                        //rotation.z = -rotation.z;

                        return new Quaternion(1.0f, 0.0f, 0.0f, 0.0f);
                    case MirrorAxis.Y:
                        //rotation.x = -rotation.x;
                        //rotation.z = -rotation.z;

                        return new Quaternion(0.0f, 1.0f, 0.0f, 0.0f);
                    case MirrorAxis.Z:
                        //rotation.x = -rotation.x;
                        //rotation.y = -rotation.y;

                        return new Quaternion(0.0f, 0.0f, 1.0f, 0.0f);
                    default:
                        return Quaternion.identity;
                }
            }

            /*private static Quaternion __Mirror(MirrorAxis axis, Quaternion rotation)
            {
                __Mirror(axis, out var prevRotation, out var postRotation);
                
                var result =  prevRotation * rotation * postRotation;
                result.x *= -1.0f;
                result.y *= -1.0f;
                result.z *= -1.0f;
                result.w *= -1.0f;

#if DEBUG
                switch (axis)
                {
                    case MirrorAxis.X:
                        UnityEngine.Assertions.Assert.AreEqual(rotation.x, result.x);
                        UnityEngine.Assertions.Assert.AreEqual(-rotation.y, result.y);
                        UnityEngine.Assertions.Assert.AreEqual(-rotation.z, result.z);
                        UnityEngine.Assertions.Assert.AreEqual(rotation.w, result.w);
                        break;
                    case MirrorAxis.Y:
                        UnityEngine.Assertions.Assert.AreEqual(-rotation.x, result.x);
                        UnityEngine.Assertions.Assert.AreEqual(rotation.y, result.y);
                        UnityEngine.Assertions.Assert.AreEqual(-rotation.z, result.z);
                        UnityEngine.Assertions.Assert.AreEqual(rotation.w, result.w);
                        break;
                    case MirrorAxis.Z:
                        UnityEngine.Assertions.Assert.AreEqual(-rotation.x, result.x);
                        UnityEngine.Assertions.Assert.AreEqual(-rotation.y, result.y);
                        UnityEngine.Assertions.Assert.AreEqual(rotation.z, result.z);
                        UnityEngine.Assertions.Assert.AreEqual(rotation.w, result.w);
                        break;
                }
#endif

                return result;
            }*/

            private static string __MirrorSkeletonBoneName(string skeletonBoneName)
            {
                string newSkeletonBoneName = skeletonBoneName.Replace("Right", "Left");
                if (newSkeletonBoneName.Length == skeletonBoneName.Length)
                {
                    newSkeletonBoneName = skeletonBoneName.Replace("Left", "Right");
                    if (newSkeletonBoneName.Length == skeletonBoneName.Length)
                    {
                        newSkeletonBoneName = skeletonBoneName.Replace(" L ", " R ");
                        if (newSkeletonBoneName == skeletonBoneName)
                        {
                            newSkeletonBoneName = skeletonBoneName.Replace(" R ", " L ");

                            if (newSkeletonBoneName == skeletonBoneName)
                            {
                                newSkeletonBoneName = skeletonBoneName.Replace(".R", ".L");
                                if (newSkeletonBoneName == skeletonBoneName)
                                    newSkeletonBoneName = skeletonBoneName.Replace(".L", ".R");
                            }
                        }
                    }
                }

                if (skeletonBoneName != newSkeletonBoneName)
                {
                    UnityEngine.Debug.Log($"Mirror {skeletonBoneName} To {newSkeletonBoneName}");

                    skeletonBoneName = newSkeletonBoneName;
                }

                return skeletonBoneName;
            }

            private static string __QuerySkeletonBoneName(string path)
            {
                return MeshInstanceRigDatabase.SkeletonNode.PathToName(path);
            }

            private static void __QueryRemap(
                bool isRoot,
                MirrorAxis mirrorAxis, 
                int sourceTargetSkeletonNodeIndex,
                int destinationTargetSkeletonNodeIndex,
                int sourceSkeletonNodeIndex,
                int destinationSkeletonNodeIndex,
                in MeshInstanceRigDatabase.Rig sourceRig,
                in MeshInstanceRigDatabase.Rig destinationRig,
                NativeList<ChannelMap> translationChannels,
                NativeList<ChannelMap> rotationChannels,
                //NativeList<ChannelMap> scaleChannels,
                NativeList<RigTranslationOffset> translationOffsets,
                NativeList<RigRotationOffset> rotationOffsets)
            {
                //ref readonly var targetSourceSkeletonNode = ref sourceRig.skeletonNodes[targetSourceSkeletonNodeIndex];
                ref readonly var sourceSkeletonNode = ref sourceRig.skeletonNodes[sourceSkeletonNodeIndex];
                ref readonly var destinationSkeletonNode = ref destinationRig.skeletonNodes[destinationTargetSkeletonNodeIndex];

                //UnityEngine.Debug.LogError($"{sourceSkeletonNode.path}=>{targetSourceSkeletonNode.path}:{destinationSkeletonNode.path}");

                ChannelMap channelMap;
                channelMap.SourceId = sourceSkeletonNode.path;
                channelMap.DestinationId = destinationSkeletonNode.path;

                __CalculateRemap(
                    mirrorAxis,
                    false, 
                    false, 
                    sourceSkeletonNodeIndex,
                    destinationTargetSkeletonNodeIndex,
                    sourceRig.skeletonNodes,
                    destinationRig.skeletonNodes,
                    out var preRotation,
                    out var postRotation, 
                    out float scale);

                /*if (mirrorAxis != MirrorAxis.None)
                {
                    __CalculateRemap(
                        mirrorAxis,
                        false,
                        false,
                        destinationSkeletonNodeIndex,
                        destinationTargetSkeletonNodeIndex,
                        destinationRig.skeletonNodes,
                        destinationRig.skeletonNodes,
                        out var mirrorPreRotation,
                        out var mirrorPostRotation,
                        out float mirrorScale);

                    preRotation = mirrorPreRotation * preRotation;
                    postRotation = postRotation * mirrorPostRotation;

                    scale *= mirrorScale;
                }*/

                /*__CalculateRemap(
                    MirrorAxis.None,
                    false,
                    false,
                    sourceSkeletonNodeIndex,
                    destinationSkeletonNodeIndex,
                    sourceRig.skeletonNodes,
                    destinationRig.skeletonNodes,
                    out var targetPreRotation,
                    out var targetPostRotation,
                    out float targetScale);

                preRotation = targetPreRotation * preRotation;
                postRotation = postRotation * targetPostRotation;

                scale *= targetScale;*/

                if (isRoot)
                {
                    RigTranslationOffset translationOffset;
                    translationOffset.Scale = scale;
                    translationOffset.Rotation = preRotation;
                    if (translationOffset.Scale == 1.0f && preRotation == Quaternion.identity)
                        channelMap.OffsetIndex = -1;
                    else
                    {
                        channelMap.OffsetIndex = translationOffsets.Length;

                        /*ref readonly var sourceRootSkeletonNode = ref sourceSkeletonNodes[0];
                        ref readonly var destinationRootSkeletonNode = ref destinationSkeletonNodes[0];

                        Vector3 sourcePosition = Matrix4x4.TRS(
                            sourceRootSkeletonNode.localTranslationDefaultValue,
                            sourceRootSkeletonNode.localRotationDefaultValue,
                            sourceRootSkeletonNode.localScaleDefaultValue).MultiplyPoint(new Vector3(sourceMatrix.m03, sourceMatrix.m13, sourceMatrix.m23)), 
                            destinationPosition = Matrix4x4.TRS( 
                                destinationRootSkeletonNode.localTranslationDefaultValue,
                                destinationRootSkeletonNode.localRotationDefaultValue,
                                destinationRootSkeletonNode.localScaleDefaultValue).MultiplyPoint(new Vector3(destinationMatrix.m03, destinationMatrix.m13, destinationMatrix.m23));*/

                        translationOffset.Space = RigRemapSpace.LocalToParent;

                        translationOffsets.Add(translationOffset);
                    }

                    translationChannels.Add(channelMap);
                }

                if (preRotation == Quaternion.identity && postRotation == Quaternion.identity)
                    channelMap.OffsetIndex = -1;
                else
                {
                    channelMap.OffsetIndex = rotationOffsets.Length;

                    RigRotationOffset rotationOffset;
                    rotationOffset.PreRotation = preRotation;
                    rotationOffset.PostRotation = postRotation;

                    rotationOffset.Space = RigRemapSpace.LocalToParent;
                    rotationOffsets.Add(rotationOffset);
                }

                rotationChannels.Add(channelMap);

                /*if (!isRoot)
                {
                    channelMap.OffsetIndex = -1;
                    scaleChannels.Add(channelMap);
                }*/
            }

            private static void __CalculateRemap(
                MirrorAxis mirrorAxis, 
                bool sourceSkeletonUsePose,
                bool destinationSkeletonUsePose,
                int sourceSkeletonNodeIndex,
                int destinationSkeletonNodeIndex,
                MeshInstanceRigDatabase.SkeletonNode[] sourceSkeletonNodes,
                MeshInstanceRigDatabase.SkeletonNode[] destinationSkeletonNodes,
                out Quaternion preRotation, 
                out Quaternion postRotation, 
                out float scale)
            {
                ref readonly var sourceSkeletonNode = ref sourceSkeletonNodes[sourceSkeletonNodeIndex];
                ref readonly var destinationSkeletonNode = ref destinationSkeletonNodes[destinationSkeletonNodeIndex];

                Matrix4x4 sourceMatrix, destinationMatrix;
                Quaternion sourceParentRotation = __CalculateLocalToRoot(
                    sourceSkeletonUsePose,
                    sourceSkeletonNode.parentIndex,
                    sourceSkeletonNodes,
                    out sourceMatrix) ? sourceMatrix.rotation : Quaternion.identity,
                destinationParentRotation = __CalculateLocalToRoot(
                    destinationSkeletonUsePose,
                    destinationSkeletonNode.parentIndex,
                    destinationSkeletonNodes,
                    out destinationMatrix) ? destinationMatrix.rotation : Quaternion.identity;

                //__Mirror(mirrorAixs, out var mirrorPreRotation, out var mirrorPostRotation);

                var mirrorPreRotation = __MirrorRotation(mirrorAxis);

                sourceParentRotation = mirrorPreRotation * sourceParentRotation;// __Mirror(sourceParentRotation, mirrorAixs);

                preRotation = Quaternion.Inverse(destinationParentRotation) * sourceParentRotation;

                //Vector3 sourceParentPosition = sourceMatrix.GetColumn(3), destinationParentPosition = destinationMatrix.GetColumn(3);

                if (sourceSkeletonUsePose)
                    sourceMatrix *= Matrix4x4.TRS(
                        sourceSkeletonNode.localTranslationPoseValue,
                        sourceSkeletonNode.localRotationPoseValue,
                        sourceSkeletonNode.localScalePoseValue);
                else
                    sourceMatrix *= Matrix4x4.TRS(
                        sourceSkeletonNode.localTranslationDefaultValue,
                        sourceSkeletonNode.localRotationDefaultValue,
                        sourceSkeletonNode.localScaleDefaultValue);

                if(destinationSkeletonUsePose)
                    destinationMatrix *= Matrix4x4.TRS(
                        destinationSkeletonNode.localTranslationPoseValue,
                        destinationSkeletonNode.localRotationPoseValue,
                        destinationSkeletonNode.localScalePoseValue);
                else
                    destinationMatrix *= Matrix4x4.TRS(
                        destinationSkeletonNode.localTranslationDefaultValue,
                        destinationSkeletonNode.localRotationDefaultValue,
                        destinationSkeletonNode.localScaleDefaultValue);

                /*float sourceDistance = Vector3.Distance(sourceMatrix.GetColumn(3), sourceParentPosition), 
                    destinationDistance = Vector3.Distance(destinationMatrix.GetColumn(3), destinationParentPosition);*/

                scale = Mathf.Abs(sourceMatrix.m13) > Mathf.Epsilon ? destinationMatrix.m13 / sourceMatrix.m13 : 1.0f;

                //var mirrorPostRotation = mirrorPreRotation;

                var sourceRotation = sourceMatrix.rotation;
                sourceRotation = mirrorPreRotation * sourceRotation;// * mirrorPostRotation;

                postRotation = Quaternion.Inverse(sourceRotation) * destinationMatrix.rotation;

                //postRotation = mirrorPostRotation * postRotation;
            }

            private static bool __CalculateLocalToRoot(
                bool usePose, 
                int skeletonNodeIndex,
                MeshInstanceRigDatabase.SkeletonNode[] skeletonNodes,
                out Matrix4x4 matrix)
            {
                matrix = Matrix4x4.identity;

                MeshInstanceRigDatabase.SkeletonNode skeletonNode;
                switch (skeletonNodeIndex)
                {
                    case -1:
                        return false;
                    default:
                        skeletonNode = skeletonNodes[skeletonNodeIndex];

                        if(usePose)
                            matrix = Matrix4x4.TRS(
                                    skeletonNode.localTranslationPoseValue,
                                    skeletonNode.localRotationPoseValue,
                                    skeletonNode.localScalePoseValue);
                        else
                            matrix = Matrix4x4.TRS(
                                    skeletonNode.localTranslationDefaultValue,
                                    skeletonNode.localRotationDefaultValue,
                                    skeletonNode.localScaleDefaultValue);

                        break;
                }

                if (__CalculateLocalToRoot(usePose, skeletonNode.parentIndex, skeletonNodes, out var temp))
                    matrix = temp * matrix;

                return true;
            }
        }

        [SerializeField, HideInInspector]
        internal byte[] _bytes;
        [SerializeField, HideInInspector]
        internal int _clipCount;
        [SerializeField, HideInInspector]
        internal int _rigRemapTableCount;

        private InitType __initType;

        private BlobAssetReference<Unity.Animation.Clip>[] __clips;
        private BlobAssetReference<RigRemapTable>[] __rigRemapTables;

        private BlobAssetReference<MeshInstanceClipFactoryDefinition> __factory;
        private BlobAssetReference<MeshInstanceClipDefinition> __definition;

        public override int instanceID => __factory.IsCreated ? __factory.Value.instanceID : 0;

        public BlobAssetReference<MeshInstanceClipFactoryDefinition> factory
        {
            get
            {
                Init();

                return __factory;
            }
        }

        public BlobAssetReference<MeshInstanceClipDefinition> definition
        {
            get
            {
                Init();

                return __definition;
            }
        }

        public BlobAssetReference<Unity.Animation.Clip> GetClip(int index)
        {
            __InitClips();

            return __clips[index];
        }

        protected override void _Dispose()
        {
            if (__factory.IsCreated)
            {
                __factory.Dispose();

                __factory = BlobAssetReference<MeshInstanceClipFactoryDefinition>.Null;
            }

            if (__definition.IsCreated)
            {
                __definition.Dispose();

                __definition = BlobAssetReference<MeshInstanceClipDefinition>.Null;
            }
        }

        protected override void _Destroy()
        {
            SingletonAssetContainerHandle handle;
            handle.instanceID = __factory.Value.instanceID;

            if ((__initType & InitType.Clip) == InitType.Clip)
            {
                var clips = SingletonAssetContainer<BlobAssetReference<Unity.Animation.Clip>>.instance;
                for (int i = 0; i < _clipCount; ++i)
                {
                    handle.index = i;

                    clips.Delete(handle);
                }
            }

            if ((__initType & InitType.RigRemapTable) == InitType.RigRemapTable)
            {
                var rigRemapTables = SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.instance;
                for (int i = 0; i < _rigRemapTableCount; ++i)
                {
                    handle.index = i;

                    rigRemapTables.Delete(handle);
                }
            }

            __initType = 0;
        }

        protected override void _Init()
        {
            __InitClips();
            __InitRigRemapTables();
        }

        public bool __InitClips()
        {
            if ((__initType & InitType.Clip) == InitType.Clip)
                return false;

            __initType |= InitType.Clip;

            var instance = SingletonAssetContainer<BlobAssetReference<Unity.Animation.Clip>>.instance;

            SingletonAssetContainerHandle handle;
            handle.instanceID = __factory.Value.instanceID;

            int numClips = __clips.Length;
            for (int i = 0; i < numClips; ++i)
            {
                handle.index = i;

                instance[handle] = __clips[i];
            }

            return true;
        }

        public bool __InitRigRemapTables()
        {
            if ((__initType & InitType.RigRemapTable) == InitType.RigRemapTable)
                return false;

            __initType |= InitType.RigRemapTable;

            var instance = SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.instance;

            SingletonAssetContainerHandle handle;
            handle.instanceID = __factory.Value.instanceID;

            int numRigRemapTables = __rigRemapTables.Length;
            for (int i = 0; i < numRigRemapTables; ++i)
            {
                handle.index = i;

                instance[handle] = __rigRemapTables[i];
            }

            return true;
        }

        unsafe void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (_bytes != null && _bytes.Length > 0)
            {
                if (__factory.IsCreated)
                    __factory.Dispose();

                if (__definition.IsCreated)
                    __definition.Dispose();

                if (__rigRemapTables != null)
                {
                    foreach (var rigRemapTable in __rigRemapTables)
                    {
                        if (rigRemapTable.IsCreated)
                            rigRemapTable.Dispose();
                    }
                }

                if (__clips != null)
                {
                    foreach (var clip in __clips)
                    {
                        if (clip.IsCreated)
                            clip.Dispose();
                    }
                }

                fixed (byte* ptr = _bytes)
                {
                    using (var reader = new MemoryBinaryReader(ptr, _bytes.LongLength))
                    {
                        __factory = reader.Read<MeshInstanceClipFactoryDefinition>();
                        __definition = reader.Read<MeshInstanceClipDefinition>();

                        __rigRemapTables = new BlobAssetReference<RigRemapTable>[_rigRemapTableCount];
                        for (int i = 0; i < _rigRemapTableCount; ++i)
                            __rigRemapTables[i] = reader.Read<RigRemapTable>();

                        __clips = new BlobAssetReference<Unity.Animation.Clip>[_clipCount];
                        for (int i = 0; i < _clipCount; ++i)
                            __clips[i] = reader.Read<Unity.Animation.Clip>();
                    }
                }

                _bytes = null;
            }

            __initType = 0;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            if (_bytes != null && _bytes.Length > 0)
                return;

            if (!__factory.IsCreated)
                return;

            using (var writer = new MemoryBinaryWriter())
            {
                writer.Write(__factory);

                writer.Write(__definition);

                _rigRemapTableCount = __rigRemapTables.Length;
                for (int i = 0; i < _rigRemapTableCount; ++i)
                    writer.Write(__rigRemapTables[i]);

                _clipCount = __clips.Length;
                for (int i = 0; i < _clipCount; ++i)
                    writer.Write(__clips[i]);

                _bytes = writer.GetContentAsNativeArray().ToArray();
            }

        }

        public static bool Check(in MeshInstanceRigDatabase.Rig rig, ref BindingSet bindingSet)
        {
            bool isFound;
            int length = bindingSet.TranslationBindings.Length;
            for (int i = 0; i < length; ++i)
            {
                ref var binding = ref bindingSet.TranslationBindings[i];

                isFound = false;
                if (rig.skeletonNodes != null)
                {
                    foreach(var skeletonNode in rig.skeletonNodes)
                    {
                        if (new TransformBindingID { Path = skeletonNode.path }.ID == binding)
                        {
                            isFound = true;

                            break;
                        }
                    }
                }

                if (!isFound)
                    return false;
            }

            length = bindingSet.RotationBindings.Length;
            for (int i = 0; i < length; ++i)
            {
                ref var binding = ref bindingSet.RotationBindings[i];

                isFound = false;
                if (rig.skeletonNodes != null)
                {
                    foreach (var skeletonNode in rig.skeletonNodes)
                    {
                        if (new TransformBindingID { Path = skeletonNode.path }.ID == binding)
                        {
                            isFound = true;

                            break;
                        }
                    }
                }

                if (!isFound)
                    return false;
            }

            length = bindingSet.ScaleBindings.Length;
            for (int i = 0; i < length; ++i)
            {
                ref var binding = ref bindingSet.ScaleBindings[i];

                isFound = false;
                if (rig.skeletonNodes != null)
                {
                    foreach (var skeletonNode in rig.skeletonNodes)
                    {
                        if (new TransformBindingID { Path = skeletonNode.path }.ID == binding)
                        {
                            isFound = true;

                            break;
                        }
                    }
                }

                if (!isFound)
                    return false;
            }

            length = bindingSet.FloatBindings.Length;
            for (int i = 0; i < length; ++i)
            {
                ref var binding = ref bindingSet.FloatBindings[i];

                isFound = false;
                if (rig.floatChannels != null)
                {
                    foreach (var floatChannel in rig.floatChannels)
                    {
                        if (StringHash.Hash(floatChannel.Id) == binding)
                        {
                            isFound = true;

                            break;
                        }
                    }
                }

                if (!isFound)
                    return false;
            }

            length = bindingSet.IntBindings.Length;
            for (int i = 0; i < length; ++i)
            {
                ref var binding = ref bindingSet.IntBindings[i];

                isFound = false;
                if (rig.floatChannels != null)
                {
                    foreach (var intChannel in rig.intChannels)
                    {
                        if (StringHash.Hash(intChannel.Id) == binding)
                        {
                            isFound = true;

                            break;
                        }
                    }
                }

                if (!isFound)
                    return false;
            }

            return true;
        }

#if UNITY_EDITOR
        [HideInInspector]
        public Transform root;

        public MeshInstanceRigDatabase rigDatabase;

        public Data data;

        public static uint GenericBindingHashFullPath(GenericBindingID id)
        {
            return StringHash.Hash(id.ID);
        }

        public static Data Create(
            UnityEngine.Object asset, 
            RigData[] rigs,
            MeshInstanceRendererDatabase.MaterialPropertyOverride materialPropertyOverride, 
            List<MeshInstanceRigDatabase.Rig> results,
            List<BlobAssetReference<Unity.Animation.Clip>> outAnimations, 
            Dictionary<AnimationData, int> outAnimationClipIndices)
        {
            try
            {
                string targetPath = AssetDatabase.GetAssetPath(asset);

                string targetDirectory = Path.GetDirectoryName(targetPath),
                    targetFileName = Path.GetFileNameWithoutExtension(targetPath),
                    targetFolder = Path.Combine(targetDirectory, targetFileName);
                if (AssetDatabase.IsValidFolder(targetFolder))
                    AssetDatabase.DeleteAsset(targetFolder);

                AssetDatabase.CreateFolder(targetDirectory, targetFileName);

                int numRigs = rigs.Length, animationCount = 0, i;
                for (i = 0; i < numRigs; ++i)
                    animationCount += rigs[i].animations.Length;

                Data data;
                data.rigs = new Rig[numRigs];

                if (outAnimationClipIndices == null)
                    outAnimationClipIndices = new Dictionary<AnimationData, int>();

                var avatars = new List<UnityEngine.Avatar>();
                var clips = new List<Clip>();
                var remaps = new List<Remap>();
                var avatarClips = new Dictionary<AnimationClip, AvatarClip>();
                var avatarIndices = new Dictionary<UnityEngine.Avatar, int>();
                var remapIndices = new Dictionary<string, int>();
                var rigDefinitions = new Dictionary<int, BlobAssetReference<RigDefinition>>();
                var paths = new Dictionary<string, string>();
                var hasher = BindingHashGlobals.DefaultHashGenerator;
                UnityEngine.Object[] sourceAssets, destinationAssets;
                BlobAssetReference<Unity.Animation.Clip> clip;
                Remap remap;
                ModelImporter modelImporter, newModelImporter;
                string assetPath, newAssetPath, newAssetPathWithoutExtension, avatarAssetPath, newAvatarAssetPath;
                int index = 0, numAnimations, clipIndex, j;
                for (i = 0; i < numRigs; ++i)
                {
                    ref readonly var sourceRig = ref rigs[i];
                    ref var destinationRig = ref data.rigs[i];

                    destinationRig.index = sourceRig.index;

                    destinationRig.rootBonePath = sourceRig.rootBoneName;
 
                    foreach (var skeletonNode in results[sourceRig.index].skeletonNodes)
                    {
                        if (MeshInstanceRigDatabase.SkeletonNode.PathToName(skeletonNode.path) == destinationRig.rootBonePath)
                        {
                            destinationRig.rootBonePath = skeletonNode.path;

                            break;
                        }
                    }

                    numAnimations = sourceRig.animations.Length;

                    destinationRig.clipIndices = new int[numAnimations];
                    for (j = 0; j < numAnimations; ++j)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("Create Mesh Instance Clips", index.ToString(), index * 1.0f / animationCount))
                            break;

                        ++index;

                        ref readonly var animation = ref sourceRig.animations[j];
                        if (!outAnimationClipIndices.TryGetValue(animation, out clipIndex))
                        {
                            clipIndex = clips.Count;
                            outAnimationClipIndices[animation] = clipIndex;

                            Clip targetClip;
                            if (avatarClips.TryGetValue(animation.clip, out var avatarClip))
                                targetClip.remapIndex = avatarClip.remapIndex;
                            else
                            {
                                assetPath = AssetDatabase.GetAssetPath(animation.clip);

                                modelImporter = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                                var avatar = modelImporter == null ? null : modelImporter.sourceAvatar;
                                if (avatar == null)
                                    avatar = AssetDatabase.LoadAssetAtPath<UnityEngine.Avatar>(assetPath);

                                if (avatar == null || !avatar.isValid || !avatar.isHuman)
                                {
                                    targetClip.remapIndex = -1;

                                    avatarClip.remapIndex = -1;
                                    avatarClip.animationIndex = -1;
                                    avatarClip.animationClip = animation.clip;
                                    avatarClip.avatar = null;

                                    avatarClips[animation.clip] = avatarClip;
                                }
                                else
                                {
                                    avatarAssetPath = AssetDatabase.GetAssetPath(avatar);
                                    if (modelImporter != null && modelImporter.animationType == ModelImporterAnimationType.Human)
                                    {
                                        if (avatarAssetPath != assetPath)
                                        {
                                            if (!paths.TryGetValue(avatarAssetPath, out newAvatarAssetPath))
                                            {
                                                newAssetPathWithoutExtension = Path.Combine(targetFolder, Path.GetFileNameWithoutExtension(avatarAssetPath));
                                                newAvatarAssetPath = newAssetPathWithoutExtension + Path.GetExtension(avatarAssetPath);
                                                AssetDatabase.CopyAsset(avatarAssetPath, newAvatarAssetPath);

                                                newModelImporter = (ModelImporter)AssetImporter.GetAtPath(newAvatarAssetPath);
                                                newModelImporter.animationType = ModelImporterAnimationType.Generic;
                                                newModelImporter.SaveAndReimport();

                                                paths[avatarAssetPath] = newAvatarAssetPath;
                                            }
                                        }
                                        else
                                            newAvatarAssetPath = null;

                                        if (!paths.TryGetValue(assetPath, out newAssetPath))
                                        {
                                            newAssetPathWithoutExtension = Path.Combine(targetFolder, Path.GetFileNameWithoutExtension(assetPath));
                                            newAssetPath = newAssetPathWithoutExtension + Path.GetExtension(assetPath);
                                            AssetDatabase.CopyAsset(assetPath, newAssetPath);

                                            newModelImporter = (ModelImporter)AssetImporter.GetAtPath(newAssetPath);
                                            newModelImporter.animationType = ModelImporterAnimationType.Generic;

                                            newModelImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                                            newModelImporter.sourceAvatar = null;
                                            newModelImporter.SaveAndReimport();

                                            paths[assetPath] = newAssetPath;
                                        }

                                        if (!remapIndices.TryGetValue(avatarAssetPath, out targetClip.remapIndex))
                                        {
                                            targetClip.remapIndex = remaps.Count;

                                            remapIndices[avatarAssetPath] = targetClip.remapIndex;

                                            if (!avatarIndices.TryGetValue(avatar, out remap.avatarIndex))
                                            {
                                                remap.avatarIndex = avatars.Count;

                                                avatarIndices[avatar] = remap.avatarIndex;

                                                avatars.Add(avatar);
                                            }

                                            remap.rigIndex = results.Count;
                                            //remap.databaseIndex = rigDatabases.Count;
                                            remaps.Add(remap);

                                            var dataRig = MeshInstanceRigDatabase.CreateRigs(
                                                false, 
                                                AssetDatabase.LoadAssetAtPath<GameObject>(string.IsNullOrEmpty(newAvatarAssetPath) ? newAssetPath : newAvatarAssetPath), 
                                                null, 
                                                null,
                                                materialPropertyOverride)[0];

                                            dataRig.avatar = avatar;

                                            results.Add(dataRig);
                                        }
                                    }
                                    else
                                    {
                                        newAssetPath = assetPath;
                                        newAvatarAssetPath = avatarAssetPath;

                                        targetClip.remapIndex = -1;
                                    }

                                    avatarClip.remapIndex = targetClip.remapIndex;
                                    avatarClip.animationIndex = -1;
                                    avatarClip.avatar = avatar;

                                    if(newAvatarAssetPath != avatarAssetPath && newAvatarAssetPath != newAssetPath)
                                    {
                                        sourceAssets = AssetDatabase.LoadAllAssetsAtPath(avatarAssetPath);
                                        destinationAssets = AssetDatabase.LoadAllAssetsAtPath(newAvatarAssetPath);
                                        foreach (var sourceAsset in sourceAssets)
                                        {
                                            if (sourceAsset is AnimationClip)
                                            {
                                                string sourceAssetName = sourceAsset.name;
                                                foreach (var destinationAsset in destinationAssets)
                                                {
                                                    if (destinationAsset is AnimationClip && destinationAsset.name == sourceAssetName)
                                                    {
                                                        avatarClip.animationClip = (AnimationClip)destinationAsset;
                                                        avatarClips[(AnimationClip)sourceAsset] = avatarClip;

                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    sourceAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                                    destinationAssets = AssetDatabase.LoadAllAssetsAtPath(newAssetPath);
                                    foreach (var sourceAsset in sourceAssets)
                                    {
                                        if (sourceAsset is AnimationClip)
                                        {
                                            string sourceAssetName = sourceAsset.name;
                                            foreach (var destinationAsset in destinationAssets)
                                            {
                                                if (destinationAsset is AnimationClip && destinationAsset.name == sourceAssetName)
                                                {
                                                    avatarClip.animationClip = (AnimationClip)destinationAsset;
                                                    avatarClips[(AnimationClip)sourceAsset] = avatarClip;

                                                    break;
                                                }
                                            }
                                        }
                                    }

                                    avatarClip = avatarClips[animation.clip];
                                }
                            }

                            var animationClipSettings = AnimationUtility.GetAnimationClipSettings(animation.clip);
                            AnimationClip animationClip = avatarClip.animationClip;
                            if (avatarClip.animationIndex == -1)
                            {
                                if (outAnimations == null)
                                    outAnimations = new List<BlobAssetReference<Unity.Animation.Clip>>();

                                avatarClip.animationIndex = outAnimations.Count;

                                /*string animationPath = Path.Combine(newAssetPathWithoutExtension, animation.clip.name) + ".bytes",
                                    assetFullPath = Path.GetDirectoryName(Path.GetDirectoryName(Application.streamingAssetsPath));
                                assetFullPath = Path.Combine(assetFullPath, animationPath);

                                if (!AssetDatabase.IsValidFolder(newAssetPathWithoutExtension))
                                    AssetDatabase.CreateFolder(Path.GetDirectoryName(newAssetPathWithoutExtension), MeshInstanceRigDatabase.SkeletonNode.PathToName(newAssetPathWithoutExtension));*/

                                if (avatarClip.remapIndex != -1)
                                {
                                    animationClip = results[remaps[avatarClip.remapIndex].rigIndex].ResampleToPose(animation.clip);

                                    AssetDatabase.CreateAsset(animationClip, Path.Combine(targetFolder, animationClip.name + ".anim"));
                                }

                                clip = animationClip.ToDenseClipWithEvents(new BindingHashGenerator() 
                                { 
                                    TransformBindingHashFunction = BindingHashGlobals.TransformBindingHashFullPath, 
                                    GenericBindingHashFunction = GenericBindingHashFullPath
                                } );

                                /*ClipConfiguration clipConfiguration;
                                clipConfiguration.Mask = 0;// ClipConfigurationMask.CycleRootMotion | ClipConfigurationMask.DeltaRootMotion | ClipConfigurationMask.BankPivot;

                                if (animationClipSettings.loopTime)
                                    clipConfiguration.Mask |= ClipConfigurationMask.LoopTime;

                                if (!animationClipSettings.loopBlend)
                                    clipConfiguration.Mask |= ClipConfigurationMask.LoopValues | ClipConfigurationMask.CycleRootMotion;// ClipConfigurationMask.CycleRootMotion;

                                //if ((animation.flag & AnimationFlag.Baked) == AnimationFlag.Baked)
                                if (clipConfiguration.Mask != 0)
                                {
                                    string rootBoneName = sourceRig.rootBoneName;
                                    UnityEngine.Avatar avatar = avatarClip.avatar;
                                    if (avatar != null && avatar.isValid && avatar.isHuman)
                                    {
                                        foreach (var humanBone in avatar.humanDescription.human)
                                        {
                                            if (humanBone.humanName == sourceRig.rootBoneName)
                                            {
                                                rootBoneName = humanBone.boneName;

                                                break;
                                            }
                                        }
                                    }

                                    int rigIndex = avatarClip.remapIndex == -1 ? sourceRig.index : remaps[avatarClip.remapIndex].rigIndex;

                                    string rootBonePath = rootBoneName;
                                    foreach (var skeletonNode in results[rigIndex].skeletonNodes)
                                    {
                                        if (MeshInstanceRigDatabase.SkeletonNode.PathToName(skeletonNode.path) == rootBoneName)
                                        {
                                            rootBonePath = skeletonNode.path;

                                            break;
                                        }
                                    }

                                    clipConfiguration.MotionID = hasher.ToHash(RigGenerator.ToGenericBindingID(rootBonePath));

                                    if (!rigDefinitions.TryGetValue(rigIndex, out var rigDefinition))
                                    {
                                        rigDefinition = results[rigIndex].ToAsset(true);
                                        rigDefinitions[rigIndex] = rigDefinition;
                                    }

                                    var bakedClip = UberClipNode.Bake(rigDefinition, clip, clipConfiguration, clip.Value.SampleRate);

                                    clip.Dispose();

                                    clip = bakedClip;
                                }*/

                                if ((clip.Value.Bindings.TranslationBindings.Length +
                                        clip.Value.Bindings.RotationBindings.Length +
                                        clip.Value.Bindings.ScaleBindings.Length +
                                        clip.Value.Bindings.FloatBindings.Length +
                                        clip.Value.Bindings.IntBindings.Length) < 1)
                                    UnityEngine.Debug.LogError($"{animationClip} is empty!", animationClip);
                                else if (!Check(results[targetClip.remapIndex == -1 ? sourceRig.index : remaps[targetClip.remapIndex].rigIndex], ref clip.Value.Bindings))
                                    UnityEngine.Debug.LogWarning($"{animationClip} contans some unsupported bindings!", animationClip);

                                //using (var bakedClip = UberClipNode.Bake(rigDefinition, clip, clipConfiguration, clip.Value.SampleRate))

                                outAnimations.Add(clip);

                                avatarClips[animation.clip] = avatarClip;
                            }

                            targetClip.name = animationClip.name;
                            targetClip.flag = 0;

                            if (animationClip.isLooping)
                                targetClip.flag |= ClipFlag.Looping;

                            if (animationClipSettings.mirror)
                            {
                                if ((animation.flag & AnimationFlag.Mirror) != AnimationFlag.Mirror)
                                    targetClip.flag |= ClipFlag.Mirror;
                            }
                            else if((animation.flag & AnimationFlag.Mirror) == AnimationFlag.Mirror)
                                targetClip.flag |= ClipFlag.Mirror;

                            if ((animation.flag & AnimationFlag.InPlace) == AnimationFlag.InPlace)
                                targetClip.flag |= ClipFlag.InPlace;

                            targetClip.animationIndex = avatarClip.animationIndex;
                            targetClip.remapIndex = avatarClip.remapIndex;

                            clips.Add(targetClip);
                        }

                        destinationRig.clipIndices[j] = clipIndex;
                    }

                    destinationRig.defaultClipIndex = sourceRig.defaultAnimationIndex == -1 ? -1 : outAnimationClipIndices[sourceRig.animations[sourceRig.defaultAnimationIndex]];
                }

                foreach (var rigDefinition in rigDefinitions.Values)
                    rigDefinition.Dispose();

                data.remaps = remaps.ToArray();
                data.clips = clips.ToArray();
                data.avatars = avatars.ToArray();

                //__clips = animations.ToArray();

                return data;
            }
            catch(Exception e)
            {
                UnityEngine.Debug.LogException(e.InnerException ?? e, asset);

                return default;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static void Create(
            UnityEngine.Object asset,
            RigData[] dataRigs,
            MeshInstanceRendererDatabase.MaterialPropertyOverride materialPropertyOverride,
            List<BlobAssetReference<Unity.Animation.Clip>> outAnimations,
            Dictionary<AnimationData, int> outAnimationClipIndices,
            ref MeshInstanceRigDatabase.Rig[] rigs, 
            ref Data data)
        {
            var rigIndices = new HashSet<int>();
            if(data.remaps != null)
            {
                foreach(var remap in data.remaps)
                    rigIndices.Add(remap.rigIndex);
            }

            var results = new List<MeshInstanceRigDatabase.Rig>();

            int numRigs = rigs.Length;
            for(int i = 0; i < numRigs; ++i)
            {
                if (rigIndices.Contains(i))
                    continue;

                results.Add(rigs[i]);
            }

            data = Create(
                asset, 
                dataRigs,
                materialPropertyOverride, 
                results, 
                outAnimations, 
                outAnimationClipIndices);

            rigs = results.ToArray();
        }

        public void Create()
        {
            var rigIndices = MeshInstanceRigDatabase.CreateComponentRigIndices(root.root.gameObject);

            int i, numAnimations;
            RigData rig;
            AnimationClip[] animationClips;
            var animators = root.GetComponentsInChildren<Animator>();
            var rigs = new List<RigData>();
            foreach(var animator in animators)
            {
                if (!rigIndices.TryGetValue(animator, out rig.index))
                    continue;

                animationClips = animator.runtimeAnimatorController.animationClips;

                numAnimations = animationClips.Length;

                rig.defaultAnimationIndex = -1;
                rig.rootBoneName = "Hips";
                rig.animations = new AnimationData[numAnimations];

                for(i = 0; i < numAnimations; ++i)
                {
                    ref var animation = ref rig.animations[i];

                    animation.clip = animationClips[i];
                }

                rigs.Add(rig);
            }

            var animations = new List<BlobAssetReference<Unity.Animation.Clip>>();

            Create(
                this, 
                rigs.ToArray(),
                rigDatabase.materialPropertySettings == null ? null : rigDatabase.materialPropertySettings.Override,
                animations, 
                null, 
                ref rigDatabase.data.rigs, 
                ref data);

            __clips = animations.ToArray();
        }

        public void Rebuild()
        {
            if (__factory.IsCreated)
                __factory.Dispose();

            if (__definition.IsCreated)
                __definition.Dispose();

            int instanceID = GetInstanceID();

            var rigRemapTables = new NativeList<BlobAssetReference<RigRemapTable>>(Allocator.Temp);
            data.ToAsset(instanceID, rigDatabase.data.rigs, ref rigRemapTables, out __factory, out __definition);

            SingletonAssetContainerHandle handle;
            handle.instanceID = instanceID;

            _rigRemapTableCount = rigRemapTables.Length;

            __rigRemapTables = new BlobAssetReference<RigRemapTable>[_rigRemapTableCount];
            for (int i = 0; i < _rigRemapTableCount; ++i)
                __rigRemapTables[i] = rigRemapTables[i];

            rigRemapTables.Dispose();

            _bytes = null;

            ((ISerializationCallbackReceiver)this).OnBeforeSerialize();
        }

        public void EditorMaskDirty()
        {
            rigDatabase.EditorMaskDirty();

            Rebuild();

            EditorUtility.SetDirty(this);
        }
#endif
    }
}