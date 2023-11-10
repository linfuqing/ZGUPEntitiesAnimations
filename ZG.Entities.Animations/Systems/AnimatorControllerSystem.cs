using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Animation;
using Math = ZG.Mathematics.Math;
using Unity.Collections.LowLevel.Unsafe;

namespace ZG
{
    public enum AnimatorControllerParameterType
    {
        Float = 1,
        Int = 2,
        Bool = 3,
        Trigger = 4
    }

    public enum AnimatorControllerMotionType
    {
        None,
        Simple1D,
        SimpleDirectional2D,
        FreeformDirectional2D, 
        FreeformCartesian2D, 
    }

    public enum AnimatorControllerConditionOpcode
    {
        If = 1,
        IfNot = 2,
        Greater = 3,
        Less = 4,
        Equals = 6,
        NotEqual = 7
    }

    [Flags]
    public enum AnimatorControllerTransitionFlag
    {
        OrderedInterruption = 0x01,
        HasFixedDuration = 0x02,
        CanTransitionToSelf = 0x04
    }

    public enum AnimatorControllerInterruptionSource
    {
        None,
        Source,
        Destination,
        SourceThenDestination,
        DestinationThenSource
    }

    public struct AnimatorControllerState
    {
        public enum Flag
        {
            None = 0,
            Initialized = (1 << 0),
            Updated = (1 << 1), 
            IsInTransition = (1 << 2),
            IsActiveTransitionGlobal = (1 << 3),
            IsActiveTransitionFromDestinationState = (1 << 4),

            TransitionFlags = IsInTransition | IsActiveTransitionGlobal | IsActiveTransitionFromDestinationState
        }

        public Flag flag;

        public int activeTransitionIndex;
        public int sourceStateIndex;
        public int destinationStateIndex;
        public float transitionTime;
        public float sourceStateTime;
        public float destinationStateTime;
        public float preSourceStateTime;
        public float preDestinationStateTime;

        public bool isInTransition => (flag & Flag.IsInTransition) == Flag.IsInTransition;

        public bool isActiveTransitionGlobal => (flag & Flag.IsActiveTransitionGlobal) == Flag.IsActiveTransitionGlobal;

        public bool isActiveTransitionFromDestinationState => (flag & Flag.IsActiveTransitionFromDestinationState) == Flag.IsActiveTransitionFromDestinationState;

        public bool isActiveTransitionFromSourceState => (flag & Flag.TransitionFlags) == Flag.IsInTransition;

        public void Clear()
        {
            flag &= ~Flag.Updated;
        }

        public bool InitializeOrUpdate(int initialStateIndex)
        {
            if ((flag & Flag.Initialized) == Flag.Initialized)
            {
                preDestinationStateTime = destinationStateTime;

                if ((flag & Flag.Updated) == Flag.Updated)
                    return false;
            }
            else
            {
                if (initialStateIndex == -1)
                    return false;

                var unsetFlags = Flag.TransitionFlags;

                sourceStateIndex = initialStateIndex;
                sourceStateTime = 0;
                flag = (flag & ~unsetFlags) | Flag.Initialized;
            }

            flag |= Flag.Updated;

            preSourceStateTime = sourceStateTime;

            return true;
        }

        public void StartTransition(int transitionIndex, int destinationStateIndex, float cycleOffset)
        {
            this.destinationStateIndex = destinationStateIndex;
            destinationStateTime = cycleOffset;
            preDestinationStateTime = cycleOffset;
            transitionTime = 0.0f;
            activeTransitionIndex = transitionIndex;

            flag &= ~Flag.TransitionFlags;
            flag |= Flag.IsInTransition;// | Flag.HasDestinationStateChanged;
        }

        public void EndTransition()
        {
            sourceStateIndex = destinationStateIndex;
            sourceStateTime = destinationStateTime;
            preSourceStateTime = preDestinationStateTime;

            //flag |= Flag.HasSourceStateChanged;
            flag &= ~Flag.TransitionFlags;
        }

        public void AbortTransition(float cycleOffset)
        {
            sourceStateTime = cycleOffset;
            preSourceStateTime = cycleOffset;

            //flag |= Flag.HasSourceStateChanged;
            flag &= ~Flag.TransitionFlags;
        }
    }

    public struct AnimatorControllerBlendTree1DSimple
    {
        public StringHash blendParameter;

        public BlobArray<float> motionSpeeds;
        public BlobArray<float> motionThresholds;

        public static float WeightForIndex(in NativeArray<float> motionThresholds, int length, int index, float blend)
        {
            if (blend >= motionThresholds[index])
            {
                if (index + 1 == length)
                {
                    return 1.0f;
                }
                else if (motionThresholds[index + 1] < blend)
                {
                    return 0.0f;
                }
                else
                {
                    if (motionThresholds[index] - motionThresholds[index + 1] != 0)
                    {
                        return (blend - motionThresholds[index + 1]) / (motionThresholds[index] - motionThresholds[index + 1]);
                    }
                    else
                    {
                        return 1.0f;
                    }
                }
            }
            else
            {
                if (index == 0)
                {
                    return 1.0f;
                }
                else if (motionThresholds[index - 1] > blend)
                {
                    return 0.0f;
                }
                else
                {
                    if ((motionThresholds[index] - motionThresholds[index - 1]) != 0)
                    {
                        return (blend - motionThresholds[index - 1]) / (motionThresholds[index] - motionThresholds[index - 1]);
                    }
                    else
                    {
                        return 1.0f;
                    }
                }
            }
        }

        public static void ComputeWeights(
            in NativeArray<float> motionThresholds, 
            float blendParameter, 
            ref NativeArray<float> outWeights)
        {
            var length = motionThresholds.Length;

            var blend = math.clamp(blendParameter, motionThresholds[0], motionThresholds[length - 1]);
            for (int i = 0; i < length; i++)
                outWeights[i] = WeightForIndex(motionThresholds, length, i, blend);
        }
    }

    public struct AnimatorControllerBlendTree2DSimpleDirectional
    {
        public StringHash blendParameterX;
        public StringHash blendParameterY;

        public BlobArray<float> motionSpeeds;
        public BlobArray<float2> motionPositions;

        public static void ComputeWeights(in NativeArray<float2> motionPositions, in float2 blendParameter, ref NativeArray<float> outWeights)
        {
            var length = motionPositions.Length;

            outWeights.MemClear();

            // Handle fallback
            if (length < 2)
            {
                if (length == 1)
                    outWeights[0] = 1.0f;

                return;
            }

            // Handle special case when sampled exactly in the middle
            if (math.all(blendParameter == float2.zero))
            {
                // If we have a center motion, give that one all the weight
                for (int i = 0; i < length; i++)
                {
                    if (math.all(motionPositions[i] == float2.zero))
                    {
                        outWeights[i] = 1;
                        return;
                    }
                }

                // Otherwise divide weight evenly
                float sharedWeight = 1.0f / length;
                for (int i = 0; i < length; i++)
                    outWeights[i] = sharedWeight;
                return;
            }

            int indexA = -1;
            int indexB = -1;
            int indexCenter = -1;
            float maxDotForNegCross = -100000.0f;
            float maxDotForPosCross = -100000.0f;
            for (int i = 0; i < length; i++)
            {
                float2 position = motionPositions[i];
                if (math.all(position == float2.zero))
                {
                    if (indexCenter >= 0)
                        return;

                    indexCenter = i;
                    continue;
                }
                var posNormalized = math.normalize(position);
                float dot = math.dot(posNormalized, blendParameter);
                float det = posNormalized.x * blendParameter.y - posNormalized.y * blendParameter.x;
                if (det > 0)
                {
                    if (dot > maxDotForPosCross)
                    {
                        maxDotForPosCross = dot;
                        indexA = i;
                    }
                }
                else
                {
                    if (dot > maxDotForNegCross)
                    {
                        maxDotForNegCross = dot;
                        indexB = i;
                    }
                }
            }

            float centerWeight = 0.0F;

            if (indexA < 0 || indexB < 0)
            {
                // Fallback if sampling point is not inside a triangle
                centerWeight = 1;
            }
            else
            {
                var a = motionPositions[indexA];
                var b = motionPositions[indexB];

                // Calculate weights using barycentric coordinates
                // (formulas from http://en.wikipedia.org/wiki/Barycentric_coordinate_system_%28mathematics%29 )
                float det = b.y * a.x - b.x * a.y;        // Simplified from: (b.y-0)*(a.x-0) + (0-b.x)*(a.y-0);
                float wA = (b.y * blendParameter.x - b.x * blendParameter.y) / det; // Simplified from: ((b.y-0)*(l.x-0) + (0-b.x)*(l.y-0)) / det;
                float wB = (a.x * blendParameter.y - a.y * blendParameter.x) / det; // Simplified from: ((0-a.y)*(l.x-0) + (a.x-0)*(l.y-0)) / det;
                centerWeight = 1 - wA - wB;

                // Clamp to be inside triangle
                if (centerWeight < 0)
                {
                    centerWeight = 0;
                    float sum = wA + wB;
                    wA /= sum;
                    wB /= sum;
                }
                else if (centerWeight > 1)
                {
                    centerWeight = 1;
                    wA = 0;
                    wB = 0;
                }

                // Give weight to the two vertices on the periphery that are closest
                outWeights[indexA] = wA;
                outWeights[indexB] = wB;
            }

            if (indexCenter >= 0)
                outWeights[indexCenter] = centerWeight;
            else
            {
                // Give weight to all children when input is in the center
                float sharedWeight = 1.0f / length;
                for (int i = 0; i < length; i++)
                    outWeights[i] += sharedWeight * centerWeight;
            }
        }
    }

    public struct AnimatorControllerBlendTree2DFreeformCartesian
    {
        public StringHash blendParameterX;
        public StringHash blendParameterY;

        public BlobArray<float> motionSpeeds;
        public BlobArray<float2> motionPositions;

        public static void ComputeWeights(in NativeArray<float2> motionPositions, in float2 blendParameter, ref NativeArray<float> outWeights)
        {
            float2 motionPositionX, motionPositionY, vecXS, vecXY;
            float influence, weight, totalWeight = 0.0f;
            int i, j, numMotionPositions = motionPositions.Length;
            // Calculate impact values for each animation node
            for(i = 0; i < numMotionPositions; ++i)
            {
                weight = 1.0f;

                motionPositionX = motionPositions[i];

                // Calculate the impact value of the current node for each other node, and take the smallest one
                for (j = 0; j < numMotionPositions; ++j)
                {
                    if (j == i) 
                        continue;

                    motionPositionY = motionPositions[j];

                    vecXS = blendParameter - motionPositionX;
                    vecXY = motionPositionY - motionPositionX;

                    // The influence value corresponds to the normalization of the projection of 0S on 01.
                    influence = math.saturate(1 - math.dot(vecXS, vecXY) / math.lengthsq(vecXY));
                    // Retain the smallest of the impact values
                    weight = math.min(weight, influence);
                }

                outWeights[i] = weight;

                totalWeight += weight;
            }

            if (totalWeight > math.FLT_MIN_NORMAL)
            {
                totalWeight = 1.0f / totalWeight;
                for (i = 0; i < numMotionPositions; ++i)
                    outWeights[i] *= totalWeight;
            }
        }
    }

    public struct AnimatorControllerDefinition
    {
        public struct Parameter
        {
            public AnimatorControllerParameterType type;
            public StringHash name;
            public int defaultValue;

            public int GetInt(int index, in DynamicBuffer<AnimatorControllerParameter> values)
            {
                if (index < values.Length)
                    return values[index].value;

                return defaultValue;
            }

            public float GetFloat(int index, in DynamicBuffer<AnimatorControllerParameter> values)
            {
                return math.asfloat(GetInt(index, values));
            }

            public static int IndexOf(StringHash name, ref BlobArray<Parameter> parameters)
            {
                int numParameters = parameters.Length;
                for(int i = 0; i < numParameters; ++i)
                {
                    if (parameters[i].name == name)
                        return i;
                }

                return -1;
            }
        }

        public struct Remap
        {
            public int index;
            public int sourceRigIndex;
            public int destinationRigIndex;
        }

        public struct Clip
        {
            public MotionClipFlag flag;

            public MotionClipWrapMode wrapMode;

            public int index;

            public BlobArray<int> remapIndices;

            public void Evaluate(
                int rigIndex,
                int rigInstanceID,
                int instanceID,
                int layerIndex,
                int depth,
                float weight,
                float speed,
                float previousTime,
                float currentTime,
                in SingletonAssetContainer<BlobAssetReference<Unity.Animation.Clip>>.Reader clips,
                in SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions,
                in SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables,
                ref BlobArray<Remap> remaps,
                ref DynamicBuffer<MotionClip> outClips,
                ref DynamicBuffer<MotionClipWeight> outWeights,
                ref DynamicBuffer<MotionClipTime> outTimes,
                ref DynamicBuffer<AnimatorControllerEvent> outEvents)
            {
                var clipInstance = clips[new SingletonAssetContainerHandle(instanceID, index)];
                if (!clipInstance.IsCreated)
                {
                    Debug.LogError("clipInstance has not been created");

                    return;
                }

                ref var clipValue = ref clipInstance.Value;
                float time;
                MotionClipWrapMode wrapMode;
                switch (this.wrapMode)
                {
                    case MotionClipWrapMode.Normal:
                        if (math.abs(currentTime) > clipValue.Duration)
                        {
                            wrapMode = MotionClipWrapMode.Normal;

                            time = currentTime < 0.0f ? 0.0f : clipValue.Duration;
                        }
                        else
                        {
                            wrapMode = MotionClipWrapMode.Managed;

                            time = currentTime < 0.0f ? currentTime + clipValue.Duration : currentTime;
                        }
                        break;
                    case MotionClipWrapMode.Loop:
                        wrapMode = MotionClipWrapMode.Managed;

                        time = Math.Repeat(/*speed < 0.0f ? motion.averageLength - currentTime : */currentTime, clipValue.Duration);
                        break;
                    default:
                        wrapMode = MotionClipWrapMode.Normal;

                        time = currentTime;
                        break;
                }

                int remapIndex = -1, clipRemapIndex, numClipRemapIndices = remapIndices.Length;
                for (int i = 0; i < numClipRemapIndices; ++i)
                {
                    clipRemapIndex = remapIndices[i];
                    ref var remap = ref remaps[clipRemapIndex];
                    if (remap.destinationRigIndex == rigIndex)
                    {
                        remapIndex = clipRemapIndex;

                        break;
                    }
                }

                Evaluate(
                    flag,
                    wrapMode,
                    instanceID,
                    rigInstanceID,
                    remapIndex,
                    layerIndex,
                    depth,
                    speed,
                    time,
                    weight,
                    clipInstance,
                    rigDefinitions,
                    rigRemapTables,
                    ref outClips,
                    ref outWeights,
                    ref outTimes,
                    ref remaps);

                ref var synchronizationTags = ref clipValue.SynchronizationTags;
                int numSynchronizationTags = synchronizationTags.Length;
                if (numSynchronizationTags > 0)
                {
                    float normalizedPreviousTime = previousTime / clipValue.Duration,
                        normalizedCurrentTime = currentTime / clipValue.Duration;
                    switch (wrapMode)
                    {
                        case MotionClipWrapMode.Normal:
                            normalizedPreviousTime = math.clamp(normalizedPreviousTime, -1.0f, 1.0f);
                            normalizedCurrentTime = math.clamp(normalizedCurrentTime, -1.0f, 1.0f);

                            Repeat(ref normalizedPreviousTime, ref normalizedCurrentTime);
                            break;
                        case MotionClipWrapMode.Loop:
                            Repeat(ref normalizedPreviousTime, ref normalizedCurrentTime);

                            break;
                    }

                    if (normalizedPreviousTime != normalizedCurrentTime)
                    {
                        AnimatorControllerEvent outEvent;
                        for (int i = 0; i < numSynchronizationTags; ++i)
                        {
                            ref var synchronizationTag = ref clipValue.SynchronizationTags[i];
                            if (Overlaps(normalizedPreviousTime, normalizedCurrentTime, synchronizationTag.NormalizedTime))
                            {
                                outEvent.type = synchronizationTag.Type;
                                outEvent.state = synchronizationTag.State;
                                outEvent.weight = weight;

                                outEvents.Add(outEvent);
                            }
                        }
                    }
                }

            }

            public static void Evaluate(
                MotionClipFlag flag,
                MotionClipWrapMode wrapMode,
                int instanceID,
                int rigInstanceID,
                int remapIndex,
                int layerIndex,
                int depth,
                float speed,
                float time,
                float weight,
                in BlobAssetReference<Unity.Animation.Clip> clip,
                in SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions,
                in SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables,
                ref DynamicBuffer<MotionClip> outClips,
                ref DynamicBuffer<MotionClipWeight> outWeights,
                ref DynamicBuffer<MotionClipTime> outTimes,
                ref BlobArray<Remap> remaps)
            {
                MotionClip result;

                result.flag = flag;
                result.wrapMode = wrapMode;
                result.layerIndex = layerIndex;
                result.depth = depth;
                result.speed = speed;

                result.value = clip;

                if (remapIndex == -1)
                {
                    result.remapTable = default;
                    result.remapDefinition = default;
                }
                else
                {
                    ref var remap = ref remaps[remapIndex];

                    result.remapTable = rigRemapTables[new SingletonAssetContainerHandle(instanceID, remap.index)];
                    result.remapDefinition = rigDefinitions[new SingletonAssetContainerHandle(rigInstanceID, remap.sourceRigIndex)];
                }

                outClips.Add(result);

                MotionClipWeight clipWeight;
                //clipWeight.version = MotionClipSystem.Version.Data;
                clipWeight.value = weight;
                outWeights.Add(clipWeight);

                if (outTimes.IsCreated)
                {
                    MotionClipTime clipTime;
                    clipTime.value = time;
                    outTimes.Add(clipTime);
                }

                /*if (outClips.Length != outTimes.Length ||
                    outTimes.Length != outWeights.Length ||
                    outWeights.Length != outClips.Length)
                    UnityEngine.Debug.Log($"Animator {outClips.Length} : {outTimes.Length} : {outWeights.Length}");*/

            }
        }

        public struct Motion
        {
            public AnimatorControllerMotionType type;

            public int index;

            public BlobArray<int> childIndices;

            public static float GetTime(float time, float speed, float duration)
            {
                return Math.Repeat((speed < 0.0f ? duration - time : time) * speed, duration);
            }

            public static void Evaluate(
                int index, 
                int rigIndex,
                int rigInstanceID,
                int instanceID,
                int layerIndex,
                int depth, 
                float speed,
                float previousTime,
                float currentTime,
                float weight, 
                in DynamicBuffer<AnimatorControllerParameter> parameterValues, 
                in SingletonAssetContainer<UnsafeUntypedBlobAssetReference>.Reader motions,
                in SingletonAssetContainer<BlobAssetReference<Unity.Animation.Clip>>.Reader clips,
                in SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions,
                in SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables,
                ref BlobArray<Remap> remaps,
                ref BlobArray<Clip> clipKeys,
                ref BlobArray<Motion> motionKeys, 
                ref BlobArray<Parameter> parameterKeys,
                ref DynamicBuffer<MotionClip> outClips,
                ref DynamicBuffer<MotionClipWeight> outWeights,
                ref DynamicBuffer<MotionClipTime> outTimes,
                ref DynamicBuffer<AnimatorControllerEvent> outEvents)
            {
                if (index == -1)
                    return;

                if (weight > math.FLT_MIN_NORMAL)
                {
                    ref var motion = ref motionKeys[index];

                    switch (motion.type)
                    {
                        case AnimatorControllerMotionType.None:
                            ref var clip = ref clipKeys[motion.index];
                            clip.Evaluate(
                                rigIndex,
                                rigInstanceID,
                                instanceID,
                                layerIndex,
                                depth,
                                weight,
                                speed,
                                previousTime,
                                currentTime,
                                clips,
                                rigDefinitions,
                                rigRemapTables,
                                ref remaps,
                                ref outClips, 
                                ref outWeights, 
                                ref outTimes, 
                                ref outEvents);
                            break;
                        case AnimatorControllerMotionType.Simple1D:
                            {
                                Clip.Evaluate(
                                    0,
                                    MotionClipWrapMode.Managed, 
                                    instanceID,
                                    rigInstanceID,
                                    -1,
                                    layerIndex,
                                    depth,
                                    speed,
                                    currentTime,
                                    weight,
                                    BlobAssetReference<Unity.Animation.Clip>.Null,
                                    rigDefinitions,
                                    rigRemapTables,
                                    ref outClips,
                                    ref outWeights,
                                    ref outTimes,
                                    ref remaps);

                                ref var blendTree = ref motions[new SingletonAssetContainerHandle(instanceID, motion.index)].Reinterpret<AnimatorControllerBlendTree1DSimple>().Value;
                                int numWeights = motion.childIndices.Length;
                                var weights = new NativeArray<float>(numWeights, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                                int paramterIndex = Parameter.IndexOf(blendTree.blendParameter, ref parameterKeys);

                                AnimatorControllerBlendTree1DSimple.ComputeWeights(
                                    blendTree.motionThresholds.AsArray(),
                                    paramterIndex == -1 ? 0.0f : parameterKeys[paramterIndex].GetFloat(paramterIndex, parameterValues),
                                    ref weights);

                                ++depth;

                                float motionSpeed;
                                for (int i = 0; i < numWeights; ++i)
                                {
                                    motionSpeed = blendTree.motionSpeeds[i];

                                    Evaluate(
                                        motion.childIndices[i],
                                        rigIndex,
                                        rigInstanceID,
                                        instanceID,
                                        layerIndex,
                                        depth,
                                        speed * motionSpeed,
                                        previousTime * motionSpeed, 
                                        currentTime * motionSpeed,
                                        weights[i], // * weight,
                                        parameterValues,
                                        motions,
                                        clips,
                                        rigDefinitions,
                                        rigRemapTables,
                                        ref remaps,
                                        ref clipKeys,
                                        ref motionKeys,
                                        ref parameterKeys, 
                                        ref outClips,
                                        ref outWeights,
                                        ref outTimes,
                                        ref outEvents);
                                }

                                weights.Dispose();
                            }
                            break;
                        case AnimatorControllerMotionType.SimpleDirectional2D:
                            {
                                Clip.Evaluate(
                                       0,
                                       MotionClipWrapMode.Managed, 
                                       instanceID,
                                       rigInstanceID,
                                       -1,
                                       layerIndex,
                                       depth,
                                       speed,
                                       currentTime,
                                       weight,
                                       BlobAssetReference<Unity.Animation.Clip>.Null,
                                       rigDefinitions,
                                       rigRemapTables,
                                       ref outClips,
                                       ref outWeights,
                                       ref outTimes,
                                       ref remaps);

                                ref var blendTree = ref motions[new SingletonAssetContainerHandle(instanceID, motion.index)].Reinterpret<AnimatorControllerBlendTree2DSimpleDirectional>().Value;
                                int numWeights = blendTree.motionPositions.Length;
                                var weights = new NativeArray<float>(numWeights, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                                int paramterIndexX = Parameter.IndexOf(blendTree.blendParameterX, ref parameterKeys),
                                    paramterIndexY = Parameter.IndexOf(blendTree.blendParameterY, ref parameterKeys);

                                AnimatorControllerBlendTree2DSimpleDirectional.ComputeWeights(
                                    blendTree.motionPositions.AsArray(),
                                    math.float2(
                                        paramterIndexX == -1 ? 0.0f : parameterKeys[paramterIndexX].GetFloat(paramterIndexX, parameterValues),
                                        paramterIndexY == -1 ? 0.0f : parameterKeys[paramterIndexY].GetFloat(paramterIndexY, parameterValues)),
                                    ref weights);

                                ++depth;

                                float motionSpeed;
                                for (int i = 0; i < numWeights; ++i)
                                {
                                    motionSpeed = blendTree.motionSpeeds[i];

                                    Evaluate(
                                        motion.childIndices[i],
                                        rigIndex,
                                        rigInstanceID,
                                        instanceID,
                                        layerIndex,
                                        depth,
                                        speed * motionSpeed,
                                        previousTime * motionSpeed, 
                                        currentTime * motionSpeed,
                                        weights[i], // * weight,
                                        parameterValues,
                                        motions,
                                        clips,
                                        rigDefinitions,
                                        rigRemapTables,
                                        ref remaps,
                                        ref clipKeys,
                                        ref motionKeys,
                                        ref parameterKeys,
                                        ref outClips,
                                        ref outWeights,
                                        ref outTimes,
                                        ref outEvents);
                                }

                                weights.Dispose();
                            }
                            break;
                        case AnimatorControllerMotionType.FreeformCartesian2D:
                            {
                                Clip.Evaluate(
                                       0,
                                       MotionClipWrapMode.Managed, 
                                       instanceID,
                                       rigInstanceID,
                                       -1,
                                       layerIndex,
                                       depth,
                                       speed,
                                       currentTime,
                                       weight,
                                       BlobAssetReference<Unity.Animation.Clip>.Null,
                                       rigDefinitions,
                                       rigRemapTables,
                                       ref outClips,
                                       ref outWeights,
                                       ref outTimes,
                                       ref remaps);

                                ref var blendTree = ref motions[new SingletonAssetContainerHandle(instanceID, motion.index)].Reinterpret<AnimatorControllerBlendTree2DFreeformCartesian>().Value;
                                int numWeights = blendTree.motionPositions.Length;
                                var weights = new NativeArray<float>(numWeights, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                                int paramterIndexX = Parameter.IndexOf(blendTree.blendParameterX, ref parameterKeys),
                                    paramterIndexY = Parameter.IndexOf(blendTree.blendParameterY, ref parameterKeys);

                                AnimatorControllerBlendTree2DFreeformCartesian.ComputeWeights(
                                    blendTree.motionPositions.AsArray(),
                                    math.float2(
                                        paramterIndexX == -1 ? 0.0f : parameterKeys[paramterIndexX].GetFloat(paramterIndexX, parameterValues),
                                        paramterIndexY == -1 ? 0.0f : parameterKeys[paramterIndexY].GetFloat(paramterIndexY, parameterValues)),
                                    ref weights);

                                ++depth;

                                float motionSpeed;
                                for (int i = 0; i < numWeights; ++i)
                                {
                                    motionSpeed = blendTree.motionSpeeds[i];

                                    Evaluate(
                                        motion.childIndices[i],
                                        rigIndex,
                                        rigInstanceID,
                                        instanceID,
                                        layerIndex,
                                        depth,
                                        speed * motionSpeed,
                                        previousTime * motionSpeed,
                                        currentTime * motionSpeed,
                                        weights[i], // * weight,
                                        parameterValues,
                                        motions,
                                        clips,
                                        rigDefinitions,
                                        rigRemapTables,
                                        ref remaps,
                                        ref clipKeys,
                                        ref motionKeys,
                                        ref parameterKeys,
                                        ref outClips,
                                        ref outWeights,
                                        ref outTimes,
                                        ref outEvents);
                                }

                                weights.Dispose();
                            }
                            break;
                    }
                }
            }

            /*public static void __Evaluate(
                MotionClipFlag flag,
                MotionClipWrapMode wrapMode, 
                int instanceID,
                int rigInstanceID,
                int remapIndex, 
                int layerIndex, 
                int depth, 
                float speed,
                float time,
                float weight,
                in BlobAssetReference<Unity.Animation.Clip> clip,
                in SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions,
                in SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables,
                ref DynamicBuffer<MotionClip> outClips,
                ref DynamicBuffer<MotionClipWeight> outWeights,
                ref DynamicBuffer<MotionClipTime> outTimes,
                ref BlobArray<Remap> remaps)
            {
                MotionClip result;

                result.flag = flag;
                result.wrapMode = wrapMode;
                result.layerIndex = layerIndex;
                result.depth = depth;
                result.speed = speed;

                result.value = clip;

                if (remapIndex == -1)
                {
                    result.remapTable = default;
                    result.remapDefinition = default;
                }
                else
                {
                    ref var remap = ref remaps[remapIndex];

                    result.remapTable = rigRemapTables[new SingletonAssetContainerHandle(instanceID, remap.index)];
                    result.remapDefinition = rigDefinitions[new SingletonAssetContainerHandle(rigInstanceID, remap.sourceRigIndex)];
                }

                outClips.Add(result);

                MotionClipWeight clipWeight;
                //clipWeight.version = MotionClipSystem.Version.Data;
                clipWeight.value = weight;
                outWeights.Add(clipWeight);

                if (outTimes.IsCreated)
                {
                    MotionClipTime clipTime;
                    clipTime.value = time;
                    outTimes.Add(clipTime);
                }
            }*/
        }

        public struct Condition
        {
            public AnimatorControllerConditionOpcode opcode;
            public int parameterIndex;
            public int threshold;

            public readonly bool Judge(int destination)
            {
                int source = threshold;
                switch(opcode)
                {
                    case AnimatorControllerConditionOpcode.If:
                        return destination != 0;
                    case AnimatorControllerConditionOpcode.IfNot:
                        return destination == 0;
                    case AnimatorControllerConditionOpcode.Greater:
                        return destination > source;
                    case AnimatorControllerConditionOpcode.Less:
                        return destination < source;
                    case AnimatorControllerConditionOpcode.Equals:
                        return destination == source;
                    case AnimatorControllerConditionOpcode.NotEqual:
                        return destination != source;
                    default:
                        return false;
                }
            }

            public readonly bool Judge(float destination)
            {
                float source = math.asfloat(threshold);
                switch (opcode)
                {
                    case AnimatorControllerConditionOpcode.If:
                        return destination != 0;
                    case AnimatorControllerConditionOpcode.IfNot:
                        return destination == 0;
                    case AnimatorControllerConditionOpcode.Greater:
                        return destination > source;
                    case AnimatorControllerConditionOpcode.Less:
                        return destination < source;
                    case AnimatorControllerConditionOpcode.Equals:
                        return destination == source;
                    case AnimatorControllerConditionOpcode.NotEqual:
                        return destination != source;
                    default:
                        return false;
                }
            }

            public readonly bool Judge(/*in UnsafeHashSet<int> triggerIndices, */in DynamicBuffer<AnimatorControllerParameter> values, ref BlobArray<Parameter> keys)
            {
                if (parameterIndex >= keys.Length)
                    return false;

                ref var key = ref keys[parameterIndex];
                switch(key.type)
                {
                    case AnimatorControllerParameterType.Float:
                        return Judge(key.GetFloat(parameterIndex, values));
                    /*case AnimatorControllerParameterType.Trigger:
                        return Judge(triggerIndices.IsCreated && triggerIndices.Contains(parameterIndex) ? 0 : key.GetInt(parameterIndex, values));*/
                    default:
                        return Judge(key.GetInt(parameterIndex, values));
                }
            }
        }

        public struct Transition
        {
            public AnimatorControllerTransitionFlag flag;
            public AnimatorControllerInterruptionSource interruptionSource;
            public int destinationStateIndex;

            public float offset;
            public float duration;
            public float exitTime;

            public BlobArray<Condition> conditions;

            public readonly bool isOrderedInterruption => (flag & AnimatorControllerTransitionFlag.OrderedInterruption) == AnimatorControllerTransitionFlag.OrderedInterruption;

            public readonly bool hasFixedDuration => (flag & AnimatorControllerTransitionFlag.HasFixedDuration) == AnimatorControllerTransitionFlag.HasFixedDuration;

            public readonly bool canTransitionToSelf => (flag & AnimatorControllerTransitionFlag.CanTransitionToSelf) == AnimatorControllerTransitionFlag.CanTransitionToSelf;

            public bool Judge(
                //in UnsafeHashSet<int> paramterTriggerIndices, 
                in DynamicBuffer<AnimatorControllerParameter> paramterValues,
                ref BlobArray<Parameter> parameterKeys)
            {
                int numConditions = conditions.Length;
                for (int i = 0; i < numConditions; i++)
                {
                    if (!conditions[i].Judge(/*paramterTriggerIndices, */paramterValues, ref parameterKeys))
                        return false;
                }

                return true;
            }

            public bool Judge(
                MotionClipWrapMode wrapMode, 
                float averageMotionLength, 
                float previousTime, 
                float currentTime,
                //in UnsafeHashSet<int> paramterTriggerIndices, 
                in DynamicBuffer<AnimatorControllerParameter> paramterValues,
                ref BlobArray<Parameter> parameterKeys)
            {
                if (!Judge(/*paramterTriggerIndices, */paramterValues, ref parameterKeys))
                    return false;

                if (exitTime > math.FLT_MIN_NORMAL)
                {
                    // https://docs.unity3d.com/Manual/class-Transition
                    float normalizedCurrentTime = averageMotionLength > math.FLT_MIN_NORMAL  ? currentTime / averageMotionLength : currentTime;
                    if (exitTime < 1.0f && wrapMode == MotionClipWrapMode.Loop)
                    {
                        float normalizedPreviousTime = previousTime / averageMotionLength;
                        Repeat(ref normalizedPreviousTime, ref normalizedCurrentTime);

                        return Overlaps(normalizedPreviousTime, normalizedCurrentTime, exitTime);
                    }

                    return math.abs(normalizedCurrentTime) >= exitTime;
                }

                return true;
            }

            public void CollectParameterTriggers(ref BlobArray<Parameter> parameters, ref UnsafeHashSet<int> triggerIndices)
            {
                int numConditions = conditions.Length;
                for (int i = 0; i < numConditions; i++)
                {
                    ref var condition = ref conditions[i];

                    if (parameters[condition.parameterIndex].type == AnimatorControllerParameterType.Trigger)
                    {
                        if (!triggerIndices.IsCreated)
                            triggerIndices = new UnsafeHashSet<int>(1, Allocator.Temp);

                        triggerIndices.Add(condition.parameterIndex);
                    }
                }
            }
        }

        public struct State
        {
            public MotionClipWrapMode wrapMode;
            //public int motionIndex;
            public int speedMultiplierParameterIndex;
            public float speed;
            public float motionAverageLength;
            public BlobArray<Transition> transitions;

            public readonly float GetSpeed(ref BlobArray<Parameter> parameterKeys, in DynamicBuffer<AnimatorControllerParameter> parameterValues)
            {
                if (speedMultiplierParameterIndex == -1)
                    return speed;

                ref var parameterKey = ref parameterKeys[speedMultiplierParameterIndex];

                return parameterKey.GetFloat(speedMultiplierParameterIndex, parameterValues);
            }

            /*public float UpdateTime(
                ref BlobArray<Motion> motions,
                ref BlobArray<Parameter> parameterKeys,
                in DynamicBuffer<AnimatorControllerParameter> parameterValues, 
                float deltaTime, 
                float time)
            {
                float speed = GetSpeed(ref parameterKeys, parameterValues);

                float scaledDeltaTime = deltaTime * speed;
                ref var motion = ref motions[motionIndex];
                if (motion.wrapMode == MotionClipWrapMode.Loop)
                    return time + scaledDeltaTime;

                float unclampedTime = time + scaledDeltaTime;
                return math.clamp(unclampedTime, -motion.averageLength, motion.averageLength);
            }*/
        }

        public struct StateMachine
        {
            public int initStateIndex;
            public BlobArray<State> states;
            public BlobArray<Transition> globalTransitions;
        }

        public struct Layer
        {
            public int stateMachineIndex;
            public BlobArray<int> stateMotionIndices;
        }

        public int instanceID;
        public BlobArray<Remap> remaps;
        public BlobArray<Clip> clips;
        public BlobArray<Motion> motions;
        public BlobArray<StateMachine> stateMachines;
        public BlobArray<Layer> layers;
        public BlobArray<Parameter> parameters;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Overlaps(float previousTime, float currentTime, float t)
        {
            // backward time direction
            if (previousTime > currentTime)
                return t >= currentTime && t < previousTime;

            // forward time direction
            return t <= currentTime && t >= previousTime;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Repeat(ref float normalizedPreviousTime, ref float normalizedCurrentTime)
        {
            bool isLess = normalizedPreviousTime < normalizedCurrentTime;
            normalizedPreviousTime = math.frac(normalizedPreviousTime);
            normalizedCurrentTime = math.frac(normalizedCurrentTime);

            if (isLess != (normalizedPreviousTime < normalizedCurrentTime))
            {
                if (isLess)
                    normalizedCurrentTime += 1.0f;
                else
                    normalizedPreviousTime += 1.0f;
            }
        }

        public static ref Transition GetActiveTranstion(ref AnimatorControllerState state, ref StateMachine stateMachine)
        {
            if (state.isActiveTransitionGlobal)
                return ref stateMachine.globalTransitions[state.activeTransitionIndex];

            if (state.isActiveTransitionFromDestinationState)
            {
                ref var destinationState = ref stateMachine.states[state.destinationStateIndex];
                return ref destinationState.transitions[state.activeTransitionIndex];
            }

            ref var sourceState = ref stateMachine.states[state.sourceStateIndex];
            return ref sourceState.transitions[state.activeTransitionIndex];
        }

        public static bool EvaluateInterruptTransitions(
            MotionClipWrapMode sourceWrapMode, 
            MotionClipWrapMode destinationWrapMode, 
            float sourceStateMotionLength,
            float destinationStateMotionLength,
            float sourcePreviousStateTime,
            float destinationPreviousStateTime,
            in DynamicBuffer<AnimatorControllerParameter> parameterValues,
            ref BlobArray<Parameter> parameterKeys,
            ref UnsafeHashSet<int> parameterTriggerIndices, 
            ref Transition transition,
            ref State sourceState,
            ref State destinationState,
            ref AnimatorControllerState state)
        {
            if (transition.interruptionSource == AnimatorControllerInterruptionSource.None)
                return false;

            if (transition.interruptionSource == AnimatorControllerInterruptionSource.Destination ||
                transition.interruptionSource == AnimatorControllerInterruptionSource.DestinationThenSource)
                goto EvaluateDestinationStateTransitions;

            EvaluateSourceStateTransitions:
            {
                int maxTransitionIndex = transition.isOrderedInterruption ?
                    (state.isActiveTransitionFromSourceState ? state.activeTransitionIndex : 0) : 
                    sourceState.transitions.Length;
                for (int i = 0; i < maxTransitionIndex; ++i)
                {
                    ref var sourceTransition = ref sourceState.transitions[i];
                    if (sourceTransition.Judge(
                        sourceWrapMode, 
                        sourceStateMotionLength,
                        sourcePreviousStateTime,
                        state.sourceStateTime, 
                        //parameterTriggerIndices, 
                        parameterValues,
                        ref parameterKeys))
                    {
                        sourceTransition.CollectParameterTriggers(ref parameterKeys, ref parameterTriggerIndices);

                        state.AbortTransition(transition.offset * sourceStateMotionLength);

                        state.StartTransition(i, sourceTransition.destinationStateIndex, sourceTransition.offset * sourceStateMotionLength);

                        return true;
                    }
                }

                if (transition.interruptionSource != AnimatorControllerInterruptionSource.SourceThenDestination)
                    return false;
            }

            EvaluateDestinationStateTransitions:
            {
                if (!transition.isOrderedInterruption || !state.isActiveTransitionGlobal)
                {
                    int numTransitions = destinationState.transitions.Length;
                    for (int i = 0; i < numTransitions; i++)
                    {
                        ref var destinationTransition = ref destinationState.transitions[i];
                        if (destinationTransition.Judge(
                            destinationWrapMode,
                            destinationStateMotionLength,
                            destinationPreviousStateTime,
                            state.destinationStateTime,
                            //parameterTriggerIndices,
                            parameterValues,
                            ref parameterKeys))
                        {
                            destinationTransition.CollectParameterTriggers(ref parameterKeys, ref parameterTriggerIndices);

                            state.EndTransition();

                            state.StartTransition(i, destinationTransition.destinationStateIndex, destinationTransition.offset * destinationStateMotionLength);

                            //layerState.flag |= AnimatorControllerLayerState.Flag.IsActiveTransitionFromDestinationState;

                            return true;
                        }
                    }

                    if (transition.interruptionSource == AnimatorControllerInterruptionSource.DestinationThenSource)
                        goto EvaluateSourceStateTransitions;
                }

                return false;
            }
        }

        public bool Evaluate(
            float deltaTime,
            in DynamicBuffer<AnimatorControllerParameter> parameters, 
            ref UnsafeHashSet<int> parameterTriggerIndices, 
            ref AnimatorControllerState state, 
            ref StateMachine stateMachine)
        {
            if (!state.InitializeOrUpdate(stateMachine.initStateIndex))
                return false;

            //layerState.flag &= ~AnimatorControllerLayerState.Flag.UpdateFlags;

            //MotionClipWrapMode sourceWrapMode;
            int i, numTransitions, sourceParameterTriggerIndexCount, destinationParameterTriggerIndexCount;
            float averageMotionLength, duration;//, sourceAverageMotionLength;
            do
            {
                ref var sourceState = ref stateMachine.states[state.sourceStateIndex];

                state.sourceStateTime += sourceState.GetSpeed(ref this.parameters, parameters) * deltaTime;

                numTransitions = state.isActiveTransitionGlobal && stateMachine.globalTransitions[state.activeTransitionIndex].isOrderedInterruption ?
                    state.activeTransitionIndex : stateMachine.globalTransitions.Length;
                for (i = 0; i < numTransitions; i++)
                {
                    ref var transition = ref stateMachine.globalTransitions[i];

                    if (!transition.canTransitionToSelf &&
                        (state.isActiveTransitionGlobal ?
                        state.activeTransitionIndex == i :
                        transition.destinationStateIndex == state.sourceStateIndex))
                        continue;

                    if (!transition.Judge(/*parameterTriggerIndices, */parameters, ref this.parameters))
                        continue;

                    transition.CollectParameterTriggers(ref this.parameters, ref parameterTriggerIndices);

                    averageMotionLength = stateMachine.states[transition.destinationStateIndex].motionAverageLength;

                    state.StartTransition(i, transition.destinationStateIndex, transition.offset * averageMotionLength);
                    state.flag |= AnimatorControllerState.Flag.IsActiveTransitionGlobal;

                    break;
                }

                if (!state.isInTransition)
                {
                    numTransitions = sourceState.transitions.Length;
                    for (i = 0; i < numTransitions; i++)
                    {
                        ref var transition = ref sourceState.transitions[i];
                        if (transition.Judge(
                            sourceState.wrapMode,
                            sourceState.motionAverageLength,
                            state.preSourceStateTime,
                            state.sourceStateTime,
                            //parameterTriggerIndices,
                            parameters,
                            ref this.parameters))
                        {
                            transition.CollectParameterTriggers(ref this.parameters, ref parameterTriggerIndices);

                            averageMotionLength = stateMachine.states[transition.destinationStateIndex].motionAverageLength;

                            state.StartTransition(i, transition.destinationStateIndex, transition.offset * averageMotionLength);

                            break;
                        }
                    }
                }

                if (state.isInTransition)
                {
                    ref var activeTransition = ref GetActiveTranstion(ref state, ref stateMachine);

                    ref var destinationState = ref stateMachine.states[activeTransition.destinationStateIndex];

                    state.destinationStateTime += destinationState.GetSpeed(ref this.parameters, parameters) * deltaTime;

                    state.transitionTime += deltaTime;
                    duration = GetDuration(ref activeTransition, ref sourceState, parameters);
                    if (duration > state.transitionTime)
                    {
                        sourceParameterTriggerIndexCount = parameterTriggerIndices.IsCreated ? parameterTriggerIndices.Count : 0;
                        if (EvaluateInterruptTransitions(
                            sourceState.wrapMode,
                            destinationState.wrapMode,
                            sourceState.motionAverageLength,
                            destinationState.motionAverageLength,
                            state.preSourceStateTime,
                            state.preDestinationStateTime,
                            parameters,
                            ref this.parameters,
                            ref parameterTriggerIndices,
                            ref activeTransition,
                            ref sourceState,
                            ref destinationState,
                            ref state))
                        {
                            //state.sourceStateTime = math.max(state.sourceStateTime - deltaTime, state.preSourceStateTime);

                            destinationParameterTriggerIndexCount = parameterTriggerIndices.IsCreated ? parameterTriggerIndices.Count : 0;
                            if(destinationParameterTriggerIndexCount > sourceParameterTriggerIndexCount)
                                continue;
                        }
                    }
                    else
                    {
                        state.EndTransition();

                        if (duration > math.FLT_MIN_NORMAL)
                        {
                            deltaTime = state.transitionTime - duration;
                            if (deltaTime > 0.0f)
                                continue;
                        }
                    }
                }

                break;
            } while (true);

            return true;
        }

        public void Evaluate(
            int rigIndex, 
            int rigInstanceID, 
            float deltaTime,
            in SingletonAssetContainer<UnsafeUntypedBlobAssetReference>.Reader motions,
            in SingletonAssetContainer<BlobAssetReference<Unity.Animation.Clip>>.Reader clips,
            in SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions,
            in SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables,
            in DynamicBuffer<MotionClipLayer> layers, 
            ref DynamicBuffer<AnimatorControllerParameter> parameters,
            ref DynamicBuffer<AnimatorControllerState> states,
            ref DynamicBuffer<AnimatorControllerEvent> outEvents,
            ref DynamicBuffer<MotionClip> outClips,
            ref DynamicBuffer<MotionClipWeight> outWeights,
            ref DynamicBuffer<MotionClipTime> outTimes)
        {
            int numStateMachines = stateMachines.Length, numStates = states.Length;
            states.ResizeUninitialized(numStateMachines);

            for (int i = 0; i < numStates; ++i)
                states.ElementAt(i).Clear();

            for (int i = numStates; i < numStateMachines; ++i)
                states[i] = default;

            outClips.Clear();
            outTimes.Clear();
            outWeights.Clear();

            UnsafeHashSet<int> parameterTriggerIndices = default;
            float transitionNormalizedTime, stateWeight;
            int numStateMotionIndices, numLayers = math.min(layers.IsCreated ? layers.Length : 0, this.layers.Length);
            for (int i = 0; i < numLayers; ++i)
            {
                if (layers[i].weight > math.FLT_MIN_NORMAL)
                {
                    ref var layer = ref this.layers[i];

                    numStateMotionIndices = layer.stateMotionIndices.Length;
                    if (numStateMotionIndices < 1)
                        continue;

                    ref var state = ref states.ElementAt(layer.stateMachineIndex);

                    ref var stateMachine = ref stateMachines[layer.stateMachineIndex];

                    Evaluate(deltaTime, parameters, ref parameterTriggerIndices, ref state, ref stateMachine);

                    ref var sourceState = ref stateMachine.states[state.sourceStateIndex];

                    transitionNormalizedTime = 0.0f;
                    if (state.isInTransition)
                    {
                        ref var activeTransition = ref GetActiveTranstion(ref state, ref stateMachine);

                        transitionNormalizedTime = state.transitionTime / GetDuration(ref activeTransition, ref sourceState, parameters);
                    }

                    stateWeight = 1.0f - transitionNormalizedTime;
                    if (stateWeight > math.FLT_MIN_NORMAL)
                    {
                        Motion.Evaluate(
                            layer.stateMotionIndices[state.sourceStateIndex],
                            rigIndex,
                            rigInstanceID,
                            instanceID,
                            i,
                            0,
                            sourceState.GetSpeed(ref this.parameters, parameters),
                            state.preSourceStateTime,
                            state.sourceStateTime,
                            stateWeight,
                            parameters,
                            motions,
                            clips,
                            rigDefinitions,
                            rigRemapTables,
                            ref remaps,
                            ref this.clips,
                            ref this.motions,
                            ref this.parameters,
                            ref outClips,
                            ref outWeights,
                            ref outTimes,
                            ref outEvents);
                    }

                    stateWeight = transitionNormalizedTime;
                    if (stateWeight > math.FLT_MIN_NORMAL)
                    {
                        ref var destinationState = ref stateMachine.states[state.destinationStateIndex];

                        Motion.Evaluate(
                            layer.stateMotionIndices[state.destinationStateIndex],
                            rigIndex,
                            rigInstanceID,
                            instanceID,
                            i,
                            0,
                            destinationState.GetSpeed(ref this.parameters, parameters),
                            state.preDestinationStateTime,
                            state.destinationStateTime,
                            stateWeight,
                            parameters,
                            motions,
                            clips,
                            rigDefinitions,
                            rigRemapTables,
                            ref remaps,
                            ref this.clips,
                            ref this.motions,
                            ref this.parameters,
                            ref outClips,
                            ref outWeights,
                            ref outTimes,
                            ref outEvents);
                    }
                }
            }

            if(parameterTriggerIndices.IsCreated)
            {
                int triggerParameterIndex = 0;
                var enumerator = parameterTriggerIndices.GetEnumerator();
                while (enumerator.MoveNext())
                    triggerParameterIndex = math.max(triggerParameterIndex, enumerator.Current);

                int numParameters = parameters.Length;
                if (numParameters <= triggerParameterIndex)
                {
                    parameters.ResizeUninitialized(triggerParameterIndex + 1);
                    for (int i = numParameters; i < triggerParameterIndex; ++i)
                        parameters.ElementAt(i).value = this.parameters[i].defaultValue;
                }

                enumerator = parameterTriggerIndices.GetEnumerator();
                while(enumerator.MoveNext())
                {
                    triggerParameterIndex = enumerator.Current;

                    //if(triggerParameterIndex < numParameters)
                        parameters.ElementAt(triggerParameterIndex).value = 0;
                }

                parameterTriggerIndices.Dispose();
            }
        }

        public float GetDuration(ref Transition transition, ref State currentState, in DynamicBuffer<AnimatorControllerParameter> parameters)
        {
            if (transition.hasFixedDuration)
                return transition.duration;

            float speed = currentState.GetSpeed(ref this.parameters, parameters);
            return math.abs(speed) > math.FLT_MIN_NORMAL ? currentState.motionAverageLength * transition.duration / speed : 0.0f;
        }

        /*public float GetMotionAverageLength(int motionIndex)
        {
            if (motionIndex == -1)
                return 0.0f;

            return motions[motionIndex].averageLength;
        }

        public MotionClipWrapMode GetMotionWrapMode(int motionIndex)
        {
            if (motionIndex == -1)
                return MotionClipWrapMode.Pause;

            return motions[motionIndex].wrapMode;
        }*/

    }

    public struct AnimatorControllerData : IComponentData
    {
        public int rigIndex;
        public int rigInstanceID;
        public BlobAssetReference<AnimatorControllerDefinition> definition;
    }

    public struct AnimatorControllerStateMachine : IBufferElementData
    {
        public AnimatorControllerState state;
    }

    public struct AnimatorControllerEvent : IBufferElementData
    {
        public StringHash type;
        public int state;
        public float weight;
    }

    public struct AnimatorControllerParameter : IBufferElementData
    {
        public int value;
    }

    /*public struct AnimatorControllerParameterCommand : IBufferElementData
    {
        public int index;
        public int value;
    }*/

    [BurstCompile, UpdateBefore(typeof(AnimationSystemGroup))]
    public partial struct AnimatorControllerSystem : ISystem
    {
        private struct Evaluate
        {
            public float deltaTime;

            [ReadOnly]
            public SingletonAssetContainer<UnsafeUntypedBlobAssetReference>.Reader motions;
            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<Clip>>.Reader clips;
            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions;
            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables;

            [ReadOnly]
            public NativeArray<AnimatorControllerData> instances;

            [ReadOnly]
            public BufferAccessor<MotionClipLayer> layers;

            public BufferAccessor<AnimatorControllerStateMachine> stateMachines;

            public BufferAccessor<AnimatorControllerEvent> events;

            public BufferAccessor<AnimatorControllerParameter> parameters;

            //public BufferAccessor<AnimatorControllerParameterCommand> parameterCommands;

            public BufferAccessor<MotionClip> clipValues;
            public BufferAccessor<MotionClipWeight> clipWeights;
            public BufferAccessor<MotionClipTime> clipTimes;

            public void Execute(int index)
            {
                var instance = instances[index];
                ref var definition = ref instance.definition.Value;

                var parameters = this.parameters[index];

                /*if (index < this.parameterCommands.Length)
                {
                    var parameterCommands = this.parameterCommands[index];
                    int numParamterCommands = parameterCommands.Length, paramterLength = parameters.Length, i, j;
                    for (i = 0; i < numParamterCommands; ++i)
                    {
                        ref var parameterCommand = ref parameterCommands.ElementAt(i);
                        if (parameterCommand.index >= 0 && parameterCommand.index < definition.parameters.Length)
                        {
                            if (paramterLength <= parameterCommand.index)
                            {
                                parameters.ResizeUninitialized(parameterCommand.index + 1);

                                for (j = paramterLength; j < parameterCommand.index; ++j)
                                    parameters.ElementAt(j).value = definition.parameters[j].defaultValue;
                            }

                            parameters.ElementAt(parameterCommand.index).value = parameterCommand.value;
                        }
                    }

                    parameterCommands.Clear();
                }*/

                var events = this.events[index];
                events.Clear();

                var states = stateMachines[index].Reinterpret<AnimatorControllerState>();

                var clipValues = this.clipValues[index];
                var clipWeights = this.clipWeights[index];
                var clipTimes = index < this.clipTimes.Length ? this.clipTimes[index] : default;

                definition.Evaluate(
                    instance.rigIndex,
                    instance.rigInstanceID, 
                    deltaTime, 
                    motions,
                    clips, 
                    rigDefinitions, 
                    rigRemapTables, 
                    index < layers.Length ? layers[index] : default, 
                    ref parameters, 
                    ref states,
                    ref events, 
                    ref clipValues,
                    ref clipWeights,
                    ref clipTimes);
            }
        }

        [BurstCompile]
        private struct EvaluateEx : IJobChunk
        {
            public float deltaTime;

            [ReadOnly]
            public SingletonAssetContainer<UnsafeUntypedBlobAssetReference>.Reader motions;
            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<Clip>>.Reader clips;
            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions;
            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables;

            [ReadOnly]
            public ComponentTypeHandle<AnimatorControllerData> instanceType;

            [ReadOnly]
            public BufferTypeHandle<MotionClipLayer> layerType;

            public BufferTypeHandle<AnimatorControllerStateMachine> stateMachineType;

            public BufferTypeHandle<AnimatorControllerEvent> eventType;

            public BufferTypeHandle<AnimatorControllerParameter> parameterType;

            //public BufferTypeHandle<AnimatorControllerParameterCommand> parameterCommandType;

            public BufferTypeHandle<MotionClip> clipType;
            public BufferTypeHandle<MotionClipWeight> clipWeightType;
            public BufferTypeHandle<MotionClipTime> clipTimeType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Evaluate evaluate;
                evaluate.deltaTime = deltaTime;
                evaluate.motions = motions;
                evaluate.clips = clips;
                evaluate.rigDefinitions = rigDefinitions;
                evaluate.rigRemapTables = rigRemapTables;
                evaluate.instances = chunk.GetNativeArray(ref instanceType);
                evaluate.layers = chunk.GetBufferAccessor(ref layerType);
                evaluate.stateMachines = chunk.GetBufferAccessor(ref stateMachineType);
                evaluate.events = chunk.GetBufferAccessor(ref eventType);
                evaluate.parameters = chunk.GetBufferAccessor(ref parameterType);
                //evaluate.parameterCommands = chunk.GetBufferAccessor(ref parameterCommandType);
                evaluate.clipValues = chunk.GetBufferAccessor(ref clipType);
                evaluate.clipWeights = chunk.GetBufferAccessor(ref clipWeightType);
                evaluate.clipTimes = chunk.GetBufferAccessor(ref clipTimeType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    evaluate.Execute(i);
            }
        }

        private EntityQuery __group;

        private SingletonAssetContainer<UnsafeUntypedBlobAssetReference> __motions;
        private SingletonAssetContainer<BlobAssetReference<Clip>> __clips;
        private SingletonAssetContainer<BlobAssetReference<RigDefinition>> __rigDefinitions;
        private SingletonAssetContainer<BlobAssetReference<RigRemapTable>> __rigRemapTables;

        private ComponentTypeHandle<AnimatorControllerData> __instanceType;
        private BufferTypeHandle<MotionClipLayer> __layerType;
        private BufferTypeHandle<AnimatorControllerStateMachine> __stateMachineType;
        private BufferTypeHandle<AnimatorControllerEvent> __eventType;
        private BufferTypeHandle<AnimatorControllerParameter> __parameterType;
        //private BufferTypeHandle<AnimatorControllerParameterCommand> __parameterCommandType;

        private BufferTypeHandle<MotionClip> __clipType;
        private BufferTypeHandle<MotionClipTime> __clipTimeType;
        private BufferTypeHandle<MotionClipWeight> __clipWeightType;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAll<AnimatorControllerData>()
                        .WithAllRW<AnimatorControllerStateMachine, AnimatorControllerParameter>()
                        .WithAllRW<MotionClip, MotionClipWeight>()
                        .Build(ref state);
                /*ComponentType.ReadOnly<AnimatorControllerData>(),
                ComponentType.ReadWrite<AnimatorControllerStateMachine>(),
                ComponentType.ReadWrite<AnimatorControllerParameter>(),
                ComponentType.ReadWrite<MotionClip>(),
                //ComponentType.ReadWrite<MotionClipTime>(),
                ComponentType.ReadWrite<MotionClipWeight>());*/

            __motions = SingletonAssetContainer<UnsafeUntypedBlobAssetReference>.Retain();
            __clips = SingletonAssetContainer<BlobAssetReference<Clip>>.instance;
            __rigDefinitions = SingletonAssetContainer<BlobAssetReference<RigDefinition>>.instance;
            __rigRemapTables = SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.instance;

            __instanceType = state.GetComponentTypeHandle<AnimatorControllerData>(true);
            __layerType = state.GetBufferTypeHandle<MotionClipLayer>(true);
            __stateMachineType = state.GetBufferTypeHandle<AnimatorControllerStateMachine>();

            __eventType = state.GetBufferTypeHandle<AnimatorControllerEvent>();

            __parameterType = state.GetBufferTypeHandle<AnimatorControllerParameter>();
            //__parameterCommandType = state.GetBufferTypeHandle<AnimatorControllerParameterCommand>();

            __clipType = state.GetBufferTypeHandle<MotionClip>();
            __clipTimeType = state.GetBufferTypeHandle<MotionClipTime>();
            __clipWeightType = state.GetBufferTypeHandle<MotionClipWeight>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __motions.Release();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EvaluateEx evaluate;
            evaluate.deltaTime = state.WorldUnmanaged.Time.DeltaTime;
            evaluate.motions = __motions.reader;
            evaluate.clips = __clips.reader;
            evaluate.rigDefinitions = __rigDefinitions.reader;
            evaluate.rigRemapTables = __rigRemapTables.reader;
            evaluate.instanceType = __instanceType.UpdateAsRef(ref state);
            evaluate.layerType = __layerType.UpdateAsRef(ref state);
            evaluate.stateMachineType = __stateMachineType.UpdateAsRef(ref state);
            evaluate.eventType = __eventType.UpdateAsRef(ref state);
            evaluate.parameterType = __parameterType.UpdateAsRef(ref state);
            //evaluate.parameterCommandType = __parameterCommandType.UpdateAsRef(ref state);
            evaluate.clipType = __clipType.UpdateAsRef(ref state);
            evaluate.clipTimeType = __clipTimeType.UpdateAsRef(ref state);
            evaluate.clipWeightType = __clipWeightType.UpdateAsRef(ref state);

            var jobHandle = evaluate.ScheduleParallel(__group, state.Dependency);

            int id = state.GetSystemID();
            __motions.AddDependency(id, jobHandle);
            __clips.AddDependency(id, jobHandle);
            __rigDefinitions.AddDependency(id, jobHandle);
            __rigRemapTables.AddDependency(id, jobHandle);

            state.Dependency = jobHandle;
        }
    }
}