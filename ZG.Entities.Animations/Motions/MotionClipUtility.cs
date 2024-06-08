using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Animation;
using Assert = UnityEngine.Assertions.Assert;
using Math = ZG.Mathematics.Math;

namespace ZG
{
    public interface IMotionClipContext
    {
        NativeArray<AnimatedData> ResolveAnimation(int layer);

        NativeArray<WeightData> ResolveWeightMask(int layer);
    }

    public struct MotionClipStreamStack<T> where T : unmanaged
    {
        public readonly NativeArray<T> Buffer;

        public int offset;

        public MotionClipStreamStack(in NativeArray<T> buffer)
        {
            Buffer = buffer;

            offset = 0;
        }
    }

    public struct MotionClipStream<T> where T : unmanaged
    {
        public static readonly MotionClipStream<T> Null = default;

        public readonly int Length;

        public unsafe readonly int Offset;

        public unsafe readonly T* Ptr;

        public unsafe bool isCreated => Ptr != null;

        public unsafe static implicit operator NativeArray<T>(in MotionClipStream<T> stream)
        {
            var result = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<T>(stream.Ptr, math.max(0, stream.Offset) + stream.Length, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref result, AtomicSafetyHandle.GetTempMemoryHandle());
#endif

            if (stream.Offset > 0)
                return result.GetSubArray(stream.Offset, stream.Length);

            return result;
        }

        public unsafe MotionClipStream(int length, ref MotionClipStreamStack<T> stack)
        {
            Length = length;

            int offset = stack.offset + length;
            if (offset > stack.Buffer.Length)
            {
                Offset = -1;

                Ptr = AllocatorManager.Allocate<T>(Allocator.Temp, length);
            }
            else
            {
                Offset = stack.offset;

                Ptr = (T*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(stack.Buffer);

                stack.offset = offset;
            }
        }

        public unsafe void Release(ref MotionClipStreamStack<T> stack)
        {
            if (Offset < 0)
                AllocatorManager.Free(Allocator.Temp, Ptr, Length);
            else if (Offset + Length == stack.offset)
                stack.offset = Offset;
            else
                __StackError(stack.offset);
        }

        public unsafe ref T Peek()
        {
            __CheckBounds(0);

            return ref UnsafeUtility.ArrayElementAsRef<T>(Ptr, math.max(0, Offset));
        }

        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __StackError(int offset)
        {
            throw new InvalidOperationException();
        }

        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void __CheckBounds(int index)
        {
            if (index < 0 || index >= Length)
                throw new IndexOutOfRangeException();
        }
    }

    public struct MotionClipMixer : IBufferElementData
    {
        public int depth;
        public float weight;
        public Core.MixerState? state;
        public AnimationStream outputStream;
        public MotionClipStream<AnimatedData> stream;
        public MotionClipStream<MotionClipMixer> parent;
    }

    public static class MotionClipUtility
    {
        public static void RigRemapper(
            in BlobAssetReference<RigRemapTable> remapTable,
            ref AnimationStream destinationStream,
            ref AnimationStream sourceStream)
        {
            destinationStream.ValidateIsNotNull();
            sourceStream.ValidateIsNotNull();

            Core.ValidateArgumentIsCreated(remapTable);

            ref var remap = ref remapTable.Value;

            var mask = sourceStream.PassMask;

            for (int i = 0, count = remap.LocalToParentTRCount.x; i < count; i++)
            {
                ref var mapping = ref remap.TranslationMappings[i];

                if (!mask.IsTranslationSet(mapping.SourceIndex))
                    continue;

                var value = sourceStream.GetLocalToParentTranslation(mapping.SourceIndex);

                if (mapping.OffsetIndex > 0)
                {
                    var translationOffset = remap.TranslationOffsets[mapping.OffsetIndex];
                    value = math.mul(translationOffset.Rotation, value * translationOffset.Scale);
                }

                destinationStream.SetLocalToParentTranslation(mapping.DestinationIndex, value);
            }

            for (int i = 0, count = remap.LocalToParentTRCount.y; i < count; i++)
            {
                ref var mapping = ref remap.RotationMappings[i];

                if (!mask.IsRotationSet(mapping.SourceIndex))
                    continue;

                var value = sourceStream.GetLocalToParentRotation(mapping.SourceIndex);

                if (mapping.OffsetIndex > 0)
                {
                    var rotationOffset = remap.RotationOffsets[mapping.OffsetIndex];
                    value = math.mul(math.mul(rotationOffset.PreRotation, value), rotationOffset.PostRotation);
                }

                destinationStream.SetLocalToParentRotation(mapping.DestinationIndex, value);
            }

            for (int i = 0; i < remap.ScaleMappings.Length; i++)
            {
                ref var mapping = ref remap.ScaleMappings[i];

                if (!mask.IsScaleSet(mapping.SourceIndex))
                    continue;

                var value = sourceStream.GetLocalToParentScale(mapping.SourceIndex);
                destinationStream.SetLocalToParentScale(mapping.DestinationIndex, value);
            }

            for (int i = 0; i < remap.FloatMappings.Length; i++)
            {
                ref var mapping = ref remap.FloatMappings[i];

                if (!mask.IsFloatSet(mapping.SourceIndex))
                    continue;

                var value = sourceStream.GetFloat(mapping.SourceIndex);
                destinationStream.SetFloat(mapping.DestinationIndex, value);
            }

            for (int i = 0; i < remap.IntMappings.Length; i++)
            {
                ref var mapping = ref remap.IntMappings[i];

                if (!mask.IsIntSet(mapping.SourceIndex))
                    continue;

                var value = sourceStream.GetInt(mapping.SourceIndex);
                destinationStream.SetInt(mapping.DestinationIndex, value);
            }

            for (int i = 0; i < remap.SortedLocalToRootTREntries.Length; i++)
            {
                ref var localToRootTREntry = ref remap.SortedLocalToRootTREntries[i];
                if (localToRootTREntry.x != -1 && localToRootTREntry.y != -1)
                {
                    ref var translationMapping = ref remap.TranslationMappings[localToRootTREntry.x];
                    ref var rotationMapping = ref remap.RotationMappings[localToRootTREntry.y];

                    if (!mask.IsTranslationSet(translationMapping.SourceIndex) &&
                        !mask.IsRotationSet(rotationMapping.SourceIndex))
                        continue;

                    //ValidateAreEqual(translationMapping.DestinationIndex, rotationMapping.DestinationIndex);

                    float3 tValue;
                    quaternion rValue;
                    if (translationMapping.SourceIndex == rotationMapping.SourceIndex)
                    {
                        sourceStream.GetLocalToRootTR(translationMapping.SourceIndex, out tValue, out rValue);
                    }
                    else
                    {
                        tValue = sourceStream.GetLocalToRootTranslation(translationMapping.SourceIndex);
                        rValue = sourceStream.GetLocalToRootRotation(rotationMapping.SourceIndex);
                    }

                    if (translationMapping.OffsetIndex > 0)
                    {
                        var offset = remap.TranslationOffsets[translationMapping.OffsetIndex];
                        tValue = math.mul(offset.Rotation, tValue * offset.Scale);
                    }

                    if (rotationMapping.OffsetIndex > 0)
                    {
                        var offset = remap.RotationOffsets[rotationMapping.OffsetIndex];
                        rValue = math.mul(math.mul(offset.PreRotation, rValue), offset.PostRotation);
                    }

                    destinationStream.SetLocalToRootTR(translationMapping.DestinationIndex, tValue, rValue);
                }
                else if (localToRootTREntry.y == -1)
                {
                    ref var mapping = ref remap.TranslationMappings[localToRootTREntry.x];
                    if (!mask.IsTranslationSet(mapping.SourceIndex))
                        continue;

                    var value = sourceStream.GetLocalToRootTranslation(mapping.SourceIndex);

                    if (mapping.OffsetIndex > 0)
                    {
                        var offset = remap.TranslationOffsets[mapping.OffsetIndex];
                        value = math.mul(offset.Rotation, value * offset.Scale);
                    }

                    destinationStream.SetLocalToRootTranslation(mapping.DestinationIndex, value);
                }
                else
                {
                    ref var mapping = ref remap.RotationMappings[localToRootTREntry.y];
                    if (!mask.IsRotationSet(mapping.SourceIndex))
                        continue;

                    var value = sourceStream.GetLocalToRootRotation(mapping.SourceIndex);

                    if (mapping.OffsetIndex > 0)
                    {
                        var offset = remap.RotationOffsets[mapping.OffsetIndex];
                        value = math.mul(math.mul(offset.PreRotation, value), offset.PostRotation);
                    }

                    destinationStream.SetLocalToRootRotation(mapping.DestinationIndex, value);
                }
            }
        }

        public static int Evaluate<T>(
            this ref T context, 
            int layerCount, 
            int rootIndex, 
            in MotionClipTransform rootTransform, 
            in BlobAssetReference<RigDefinition> rigDefinition, 
            in NativeArray<MotionClipInstance> instances,
            in NativeArray<MotionClipTime> times,
            in NativeArray<MotionClipWeight> weights,
            in NativeArray<MotionClipLayerWeightMask> layerWeightMasks, 
            ref MotionClipStreamStack<AnimatedData> streamStack,
            ref MotionClipStreamStack<MotionClipMixer> mixerStack,
            ref AnimationStream defaultPoseInputStream) where T : struct, IMotionClipContext
        {
            int length = math.min(instances.Length, math.min(times.Length, weights.Length));
            if (length < 1)
                return 0;

            int streamSize = rigDefinition.Value.Bindings.StreamSize;

            var stream = new MotionClipStream<AnimatedData>(streamSize, ref streamStack);
            AnimationStream baseLayerStream = AnimationStream.Create(rigDefinition, context.ResolveAnimation(0)),
                tempStream = AnimationStream.Create(rigDefinition, stream),
                resultStream;

            if (defaultPoseInputStream.IsNull)
                defaultPoseInputStream = AnimationStream.FromDefaultValues(rigDefinition);

            int layerMask = 0, layerIndex = 0;
            float weight;
            MotionClipInstance instance;
            MotionClipMixer mixer = default;
            for (int i = 0; i < length; ++i)
            {
                instance = instances[i];

                if ((layerMask & (1 << instance.layerIndex)) == 0)
                {
                    __ResetMixerStack(
                        -1,
                        ref mixer,
                        ref mixerStack,
                        ref streamStack,
                        ref defaultPoseInputStream);

                    Assert.IsFalse(mixer.stream.isCreated);

                    if (instance.layerIndex == 0)
                        mixer.outputStream = baseLayerStream;
                    else
                        mixer.outputStream = instance.layerIndex < layerCount ?
                            AnimationStream.Create(rigDefinition, context.ResolveAnimation(instance.layerIndex)) :
                            AnimationStream.Null;

                    if (mixer.outputStream.IsNull)
                        continue;

                    mixer.depth = instance.depth;

                    layerIndex = instance.layerIndex;
                    layerMask |= 1 << layerIndex;
                }
                else
                {
                    if (instance.layerIndex != layerIndex)
                    {
                        __ResetMixerStack(
                            -1,
                            ref mixer,
                            ref mixerStack,
                            ref streamStack,
                            ref defaultPoseInputStream);

                        Assert.IsFalse(mixer.stream.isCreated);

                        if (instance.layerIndex == 0)
                            mixer.outputStream = baseLayerStream;
                        else
                            mixer.outputStream = instance.layerIndex < layerCount ?
                                   AnimationStream.Create(rigDefinition, context.ResolveAnimation(instance.layerIndex)) :
                                   AnimationStream.Null;
                        if (mixer.outputStream.IsNull)
                            continue;

                        layerIndex = instance.layerIndex;
                    }
                    else
                        __ResetMixerStack(
                            instance.depth,
                            ref mixer,
                            ref mixerStack,
                            ref streamStack,
                            ref defaultPoseInputStream);
                }

                weight = weights[i].value;
                if (weight > math.FLT_MIN_NORMAL && mixer.weight < 1.0f)
                {
                    mixer.weight += weight;

                    if (instance.value.IsCreated)
                    {
                        if (mixer.outputStream.IsNull)
                        {
                            if (mixer.stream.isCreated && mixer.stream.Length != streamSize)
                                mixer.stream.Release(ref streamStack);

                            if (!mixer.stream.isCreated)
                                mixer.stream = new MotionClipStream<AnimatedData>(streamSize, ref streamStack);

                            mixer.outputStream = AnimationStream.Create(rigDefinition, mixer.stream);
                        }

                        if (weight < 1.0f || mixer.state != null)
                            resultStream = tempStream;
                        else
                            resultStream = mixer.outputStream;

                        __Evaluate(
                            rootIndex,
                            times[i].value,
                            rootTransform,
                            instance,
                            ref baseLayerStream,
                            ref resultStream,
                            ref streamStack);

                        if (weight < 1.0f)
                        {
                            if (mixer.state == null)
                            {
                                mixer.outputStream.ClearMasks();

                                mixer.state = Core.MixerBegin(ref mixer.outputStream);
                            }

                            mixer.state = Core.MixerAdd(ref mixer.outputStream, ref tempStream, weight, mixer.state.Value);
                        }
                    }
                }
            }

            __ResetMixerStack(
                -1,
                ref mixer,
                ref mixerStack,
                ref streamStack,
                ref defaultPoseInputStream);

            if (stream.isCreated)
                stream.Release(ref streamStack);

            if (layerMask == 0)
                Debug.LogError($"[MotionClip]Layer Mask is invailed.");
            else
            {
                BlobAssetReference<MotionClipWeightMaskDefinition> layerWeightMaskDefinition;
                NativeArray<WeightData> weightMask;
                int j, bindingCount = rigDefinition.Value.Bindings.BindingCount,
                    numLayerWeightMasks = layerWeightMasks.IsCreated ? layerWeightMasks.Length : 0,
                    numLayers = Math.GetHighestBit(layerMask),
                    weightDataSize = Core.WeightDataSize(rigDefinition);
                for (int i = Math.GetLowerstBit(layerMask) - 1; i < numLayers; ++i)
                {
                    if ((layerMask & (1 << i)) == 0)
                        continue;

                    weightMask = context.ResolveWeightMask(i);
                    if (!weightMask.IsCreated || weightMask.Length != weightDataSize)
                        continue;

                    resultStream = AnimationStream.CreateReadOnly(rigDefinition, context.ResolveAnimation(i));

                    if (numLayerWeightMasks > i && (layerWeightMaskDefinition = layerWeightMasks[i].definition).IsCreated)
                        layerWeightMaskDefinition.Value.Apply(resultStream.PassMask, rigDefinition, ref weightMask);
                    else
                    {
                        //TODO: 
                        for (j = 0; j < bindingCount; ++j)
                            Core.SetWeightValueFromChannelIndex(rigDefinition, resultStream.PassMask.IsSet(j) ? 1.0f : 0.0f, j, weightMask);
                    }
                }
            }

            return layerMask;
        }

        private static void __Evaluate(
            int rootIndex,
            float time,
            in MotionClipTransform rootTransform,
            in MotionClipInstance instance,
            ref AnimationStream baseLayerStream,
            ref AnimationStream resultStream,
            ref MotionClipStreamStack<AnimatedData> streamStack)
        {
            if (instance.remapDefinition.IsCreated && instance.remapTable.IsCreated)
            {
                ref var remapDefinition = ref instance.remapDefinition.Value;
                Assert.AreEqual(remapDefinition.GetHashCode(), instance.value.Value.RigHashCode);

                var temp = new MotionClipStream<AnimatedData>(remapDefinition.Bindings.StreamSize, ref streamStack);
                var remapStream = AnimationStream.Create(instance.remapDefinition, temp);

                Core.EvaluateClip(instance.value, time, ref remapStream, 0);

                resultStream.ClearMasks();

                if (instance.layerIndex > 0)
                {
                    Assert.AreEqual(baseLayerStream.Rig, resultStream.Rig);

                    resultStream.CopyFrom(ref baseLayerStream);

                    RigRemapper(instance.remapTable, ref resultStream, ref remapStream);
                }
                else
                {
                    resultStream.ResetToDefaultValues();

                    Core.RigRemapper(instance.remapTable, ref resultStream, ref remapStream);
                }

                if (instance.remapTable.Value.TranslationMappings.Length > 0)
                {
                    var translationMapping = instance.remapTable.Value.TranslationMappings[0];
                    if (translationMapping.SourceIndex > 0 || translationMapping.DestinationIndex > 0)
                    {
                        float3 parentPosition = translationMapping.SourceIndex > 0 ?
                            remapStream.GetLocalToRootTranslation(instance.remapDefinition.Value.Skeleton.ParentIndexes[translationMapping.SourceIndex]) : float3.zero;
                        if (translationMapping.DestinationIndex > 0)
                        {
                            var matrix = resultStream.GetLocalToRootInverseMatrix(resultStream.Rig.Value.Skeleton.ParentIndexes[translationMapping.DestinationIndex]);

                            parentPosition = mathex.mul(matrix, parentPosition);

                            /*if ((instance.flag & MotionClipFlag.InPlace) == MotionClipFlag.InPlace)
                                up = math.mul(math.quaternion(matrix.rs), up);*/
                        }

                        var position = resultStream.GetLocalToParentTranslation(translationMapping.DestinationIndex);
                        /*if ((instance.flag & MotionClipFlag.InPlace) == MotionClipFlag.InPlace)
                            position = Math.Project(position, up);*/

                        position += parentPosition;

                        resultStream.SetLocalToParentTranslation(translationMapping.DestinationIndex, position);
                    }
                }

                temp.Release(ref streamStack);
            }
            else
            {
                Assert.AreEqual(resultStream.Rig.Value.GetHashCode(), instance.value.Value.RigHashCode);

                Core.EvaluateClip(instance.value, time, ref resultStream, 0);
            }

            if (rootIndex < 1)
                rootIndex = 0;
            else if (instance.flag != 0)
            {
                //quaternion rotation;
                var rigDefinition = resultStream.Rig;
                /*var defaultStream = AnimationStream.FromDefaultValues(rigDefinition);
                if ((instance.flag & MotionClipFlag.Mirror) == MotionClipFlag.Mirror)
                {
                    //https://stackoverflow.com/questions/32438252/efficient-way-to-apply-mirror-effect-on-quaternion-rotation
                    //https://gamedev.net/forums/topic/349626-mirroring-a-quaternion/3287942/
                    float3 normal = math.mul(defaultStream.GetLocalToParentRotation(rootIndex), math.right());
                    var mirror = math.quaternion(math.float4(normal, 0.0f));
                    int rotationCount = resultStream.RotationCount;
                    for (int i = 0; i < rotationCount; ++i)
                    {
                        if (!resultStream.PassMask.IsRotationSet(i))
                            continue;

                        rotation = resultStream.GetLocalToParentRotation(i);

                        rotation = math.mul(math.mul(mirror, rotation), mirror);

                        //rotation = quaternion.LookRotation(math.forward(rotation), -math.mul(rotation, math.up()));

                        resultStream.SetLocalToParentRotation(i, rotation);
                    }
                }*/

                /*if ((instance.flag & MotionClipFlag.InPlace) == MotionClipFlag.InPlace)
                    __InPlace(ref resultStream, AnimationStream.FromDefaultValues(rigDefinition).GetLocalToRootRotation(rootIndex), rootIndex);*/

                //�����Ǿ�����ԭ�أ�Ϊ�˱�֤�����������Ļ�ϲ�����
                /*{
                    resultStream.GetLocalToRootTRS(rootIndex, out var translation, out rotation, out var scale);

                    translation = math.transform(rootTransform.matrix, translation);
                    rotation = math.mul(rootTransform.rotation, rotation);
                    scale = rootTransform.scale * scale;

                    resultStream.SetLocalToParentTRS(rootIndex, translation, rotation, scale);

                    ref var parentIndexes = ref rigDefinition.Value.Skeleton.ParentIndexes;

                    int parentIndex = parentIndexes[rootIndex];
                    while (parentIndex != -1)
                    {
                        resultStream.SetLocalToParentTR(parentIndex, float3.zero, quaternion.identity);

                        parentIndex = parentIndexes[parentIndex];
                    }
                }*/
            }

            if (resultStream.PassMask.IsTranslationSet(rootIndex))
                resultStream.SetLocalToParentTranslation(rootIndex, math.transform(rootTransform.matrix, resultStream.GetLocalToParentTranslation(rootIndex)));

            if (resultStream.PassMask.IsRotationSet(rootIndex))
                resultStream.SetLocalToParentRotation(rootIndex, math.mul(rootTransform.rotation, resultStream.GetLocalToParentRotation(rootIndex)));

            if (resultStream.PassMask.IsScaleSet(rootIndex))
                resultStream.SetLocalToParentScale(rootIndex, rootTransform.scale * resultStream.GetLocalToParentScale(rootIndex));

            /*if ((clipInstance.flag & MotionClipFlag.Mirror) == MotionClipFlag.Mirror)
            {
                scale = tempStream.GetLocalToParentScale(0);
                scale.x = -scale.x;
                tempStream.SetLocalToParentScale(0, scale);
            }
            else// if(tempStream.Rig.Value.Bindings.ScaleBindings.Length < 1)
            {
                scale = tempStream.GetLocalToParentScale(0);
                //scale.x = math.abs(scale.x);
                tempStream.SetLocalToParentScale(0, scale);
            }*/
        }

        private static void __InPlace(
            ref AnimationStream animationStream,
            quaternion defaultRotation,
            int motionRootIndex)
        {
            //var defaultStream = AnimationStream.FromDefaultValues(animationStream.Rig);
            /*RigidTransform motionTransform = math.RigidTransform(animationStream.GetLocalToRootRotation(rotationIndex), animationStream.GetLocalToRootTranslation(translationIndex)),
                defaultTransform = math.RigidTransform(defaultStream.GetLocalToRootRotation(rotationIndex), defaultStream.GetLocalToRootTranslation(translationIndex));

            motionTransform = math.mul(defaultTransform, math.inverse(motionTransform));

            motionTransform.rot = math.normalize(math.quaternion(0f, motionTransform.rot.value.y / motionTransform.rot.value.w, 0f, 1f));

            animationStream.SetLocalToParentTR(0, motionTransform.pos, motionTransform.rot);*/

            var motionTranslation = animationStream.GetLocalToRootTranslation(motionRootIndex);
            var motionRotation = animationStream.GetLocalToRootRotation(motionRootIndex);

            //var defaultRotation = defaultStream.GetLocalToRootRotation(motionRootIndex);
            defaultRotation = mathex.mul(motionRotation, math.conjugate(defaultRotation));
            defaultRotation = mathex.select(quaternion.identity, defaultRotation, math.dot(defaultRotation, defaultRotation) > math.FLT_MIN_NORMAL);

            __ProjectMotionNode(motionTranslation, defaultRotation, out float3 motionProjTranslation, out quaternion motionProjRotation, false);

            animationStream.SetLocalToParentTR(0, motionProjTranslation, motionProjRotation);

            animationStream.SetLocalToRootTR(motionRootIndex, motionTranslation, motionRotation);

            animationStream.SetLocalToParentTR(0, float3.zero, quaternion.identity);

            //animationStream.SetLocalToRootTranslation(0, float3.zero);
            //animationStream.SetLocalToParentTR(0, float3.zero, quaternion.identity);
        }

        private static void __ProjectMotionNode(float3 t, quaternion q, out float3 projT, out quaternion projQ, bool bankPivot)
        {
            if (bankPivot)
            {
                projT = math.mul(q, new float3(0, 1, 0));
                projT = t - projT * (t.y / projT.y);
            }
            else
            {
                projT = math.float3(t.x, 0f, t.z);
            }

            projQ = math.normalize(math.quaternion(0f, q.value.y / q.value.w, 0f, 1.0f));
        }

        private static bool __ClearMixerStack(
            int depth,
            ref MotionClipStreamStack<AnimatedData> streamStack,
            ref MotionClipStreamStack<MotionClipMixer> mixerStack,
            ref MotionClipStream<MotionClipMixer> parentStream,
            //ref MotionClipMixer child, 
            ref MotionClipStream<AnimatedData> stream,
            ref AnimationStream outputStream,
            ref AnimationStream defaultPoseInputStream)
        {
            //bool result = false;
            MotionClipStream<MotionClipMixer> /*parentStream = child.parent, */childStream;
            while (parentStream.isCreated)
            {
                ref var parent = ref parentStream.Peek();
                if (parent.depth < depth)
                {
                    //break;
                    //child.depth = math.min(child.depth, depth);
                    
                    return false;
                }

                if (parent.weight > math.FLT_MIN_NORMAL && !outputStream.IsNull)
                {
                    if (parent.state == null)
                        parent.state = Core.MixerBegin(ref parent.outputStream);

                    parent.state = Core.MixerAdd(
                        ref parent.outputStream,
                        ref outputStream,
                        parent.weight - parent.state.Value.WeightSum,
                        parent.state.Value);
                    
                    outputStream = AnimationStream.Null;
                }
                
                if (parent.depth == depth)
                    break;

                //result = true;

                if (parent.state != null)
                    Core.MixerEnd(ref parent.outputStream, ref defaultPoseInputStream, parent.state.Value);

                outputStream = parent.outputStream;

                if (stream.isCreated)
                    stream.Release(ref streamStack);

                stream = parent.stream;

                childStream = parentStream;

                parentStream = parent.parent;

                childStream.Release(ref mixerStack);
            }

            /*if (child.stream.isCreated)
                child.stream.Release(ref streamStack);*/

            return true;//result;
        }

        private static void __ResetMixerStack(
            int depth,
            ref MotionClipMixer mixer,
            ref MotionClipStreamStack<MotionClipMixer> mixerStack,
            ref MotionClipStreamStack<AnimatedData> streamStack,
            ref AnimationStream defaultPoseInputStream)
        {
            if (mixer.depth == depth)
                return;

            if (mixer.depth < depth)
            {
                bool isPeek = !mixer.outputStream.IsNull;
                if (!isPeek)
                {
                    isPeek = mixer.weight < 1.0f;
                    if (isPeek && mixer.weight > math.FLT_MIN_NORMAL)
                    {
                        var rigDefinition = defaultPoseInputStream.Rig;
                        int streamSize = rigDefinition.Value.Bindings.StreamSize;

                        if (mixer.stream.isCreated && mixer.stream.Length != streamSize)
                            mixer.stream.Release(ref streamStack);

                        if (!mixer.stream.isCreated)
                            mixer.stream = new MotionClipStream<AnimatedData>(streamSize, ref streamStack);

                        mixer.outputStream = AnimationStream.Create(rigDefinition, mixer.stream);
                    }
                }
                
                if (isPeek)
                {
                    var parent = new MotionClipStream<MotionClipMixer>(1, ref mixerStack);
                    parent.Peek() = mixer;

                    mixer.parent = parent;
                    mixer.outputStream = AnimationStream.Null;
                    mixer.stream = MotionClipStream<AnimatedData>.Null;
                }

                mixer.depth = depth;
                mixer.weight = 0.0f;
                mixer.state = null;

                return;
            }

            if (mixer.state != null)
                Core.MixerEnd(ref mixer.outputStream, ref defaultPoseInputStream, mixer.state.Value);

            var stream = mixer.parent;
            if (__ClearMixerStack(
                    depth,
                    ref streamStack,
                    ref mixerStack,
                    //ref mixer,
                    ref stream,
                    ref mixer.stream,
                    ref mixer.outputStream,
                    ref defaultPoseInputStream))
            {
                if (mixer.stream.isCreated)
                    mixer.stream.Release(ref streamStack);

                //var stream = mixer.parent;
                mixer.parent = MotionClipStream<MotionClipMixer>.Null;
                if (stream.isCreated)
                {
                    mixer = stream.Peek();
                    if (mixer.depth == depth)
                    {
                        stream.Release(ref mixerStack);

                        return;
                    }

                    mixer.parent = stream;
                }

                mixer.weight = 0.0f;
                mixer.state = null;
                mixer.stream = MotionClipStream<AnimatedData>.Null;
                mixer.outputStream = AnimationStream.Null;
            }
            else
                mixer.state = null;
            
            mixer.depth = depth;
        }
    }
}