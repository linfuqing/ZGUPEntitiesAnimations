using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Animation;
using Math = ZG.Mathematics.Math;

namespace ZG
{
    [BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup)), UpdateBefore(typeof(AnimationInitializeSystem))]
    public partial struct MotionClipAnimationInitSystem : ISystem
    {
        private EntityQuery __groupToCreateWeightMasks;
        private EntityQuery __groupToCreateMixers;

        public void OnCreate(ref SystemState state)
        {
            __groupToCreateWeightMasks = state.GetEntityQuery(ComponentType.ReadOnly<AnimatedData>(), ComponentType.ReadOnly<MotionClipLayer>(), ComponentType.Exclude<WeightData>());
            __groupToCreateMixers = state.GetEntityQuery(ComponentType.ReadOnly<AnimatedData>(), ComponentType.Exclude<MotionClipMixer>());
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var entityManager = state.EntityManager;
            entityManager.AddComponent<WeightData>(__groupToCreateWeightMasks);
            entityManager.AddComponent<MotionClipMixer>(__groupToCreateMixers);
        }
    }

    [BurstCompile, UpdateInGroup(typeof(AnimationSystemGroup))/*, UpdateBefore(typeof(AnimationWriteTransformSystem)), UpdateAfter(typeof(AnimationReadTransformSystem))*/]
    public partial struct MotionClipAnimationSystem : ISystem
    {
        private struct Context : IMotionClipContext
        {
            public int streamSize;

            public int weightDataSize;

            public NativeArray<AnimatedData> animationBuffer;

            public NativeArray<WeightData> weightMaskBuffer;

            public NativeArray<AnimatedData> ResolveAnimation(int layer) => animationBuffer.GetSubArray(layer * streamSize, streamSize);

            public NativeArray<WeightData> ResolveWeightMask(int layer) => weightMaskBuffer.IsCreated ? weightMaskBuffer.GetSubArray(layer * weightDataSize, weightDataSize) : default;
        }

        private struct Evaluate
        {
            [ReadOnly]
            public NativeArray<Rig> rigs;
            [ReadOnly]
            public NativeArray<MotionClipData> instances;
            [ReadOnly]
            public BufferAccessor<MotionClipInstance> clipInstances;
            [ReadOnly]
            public BufferAccessor<MotionClipTime> clipTimes;
            [ReadOnly]
            public BufferAccessor<MotionClipWeight> clipWeights;

            [ReadOnly]
            public BufferAccessor<MotionClipLayer> layers;
            [ReadOnly]
            public BufferAccessor<MotionClipLayerWeightMask> layerWeightMasks;

            public BufferAccessor<MotionClipMixer> mixers;

            public BufferAccessor<AnimatedData> animationBuffers;

            public BufferAccessor<WeightData> weightMaskBuffers;

            public void Execute(int index)
            {
                var rig = rigs[index];
                int streamSize = rig.Value.Value.Bindings.StreamSize,
                    weightDataSize = Core.WeightDataSize(rig);

                var layers = index < this.layers.Length ? this.layers[index] : default;
                int numLayers = layers.IsCreated ? layers.Length : 1;

                var mixers = this.mixers[index];
                mixers.ResizeUninitialized(numLayers);

                var animationBuffer = animationBuffers[index];
                animationBuffer.ResizeUninitialized(streamSize * ((numLayers << 1) + 1));

                var weightMaskBuffer = index < weightMaskBuffers.Length ? weightMaskBuffers[index] : default;
                if(weightMaskBuffer.IsCreated)
                    weightMaskBuffer.ResizeUninitialized(numLayers * weightDataSize);

                Context context;
                context.streamSize = streamSize;
                context.weightDataSize = weightDataSize;
                context.animationBuffer = animationBuffer.AsNativeArray();
                context.weightMaskBuffer = weightMaskBuffer.IsCreated ? weightMaskBuffer.AsNativeArray() : default;

                var instance = instances[index];

                int startIndex = numLayers * streamSize;
                var streamStack = new MotionClipStreamStack<AnimatedData>(context.animationBuffer.GetSubArray(startIndex, context.animationBuffer.Length - startIndex));
                var mixerStack = new MotionClipStreamStack<MotionClipMixer>(mixers.AsNativeArray());

                AnimationStream defaultPoseInputStream = default;
                int layerMask = context.Evaluate(
                    numLayers,
                    Core.FindBindingIndex(ref rig.Value.Value.Skeleton.Ids, instance.rootID),
                    instance.rootTransform,
                    rig.Value,
                    clipInstances[index].AsNativeArray(),
                    clipTimes[index].AsNativeArray(),
                    clipWeights[index].AsNativeArray(),
                    index < layerWeightMasks.Length ? layerWeightMasks[index].AsNativeArray() : default,
                    ref streamStack,
                    ref mixerStack,
                    ref defaultPoseInputStream);

                if (layerMask != 0 && numLayers > 1)
                {
                    numLayers = Math.GetHighestBit(layerMask);

                    AnimationStream outputStream = AnimationStream.Create(rig.Value, context.ResolveAnimation(0)), inputStream;
                    MotionClipLayer layer;
                    for (int i = math.max(Math.GetLowerstBit(layerMask) - 1, 1); i < numLayers; ++i)
                    {
                        if ((layerMask & (1 << i)) == 0)
                            continue;

                        layer = layers[i];
                        inputStream = AnimationStream.CreateReadOnly(rig.Value, context.ResolveAnimation(i));
                        if (layer.weight > 0.0f && !inputStream.IsNull)
                        {
                            switch (layer.blendingMode)
                            {
                                case MotionClipBlendingMode.Override:
                                    var weightMask = context.ResolveWeightMask(i);
                                    if(weightMask.IsCreated)
                                        Core.BlendOverrideLayer(ref outputStream, ref inputStream, layer.weight, weightMask);
                                    else
                                        Core.BlendOverrideLayer(ref outputStream, ref inputStream, layer.weight);
                                    break;
                                case MotionClipBlendingMode.Additive:
                                    weightMask = context.ResolveWeightMask(i);
                                    if (weightMask.IsCreated)
                                        Core.BlendAdditiveLayer(ref outputStream, ref inputStream, layer.weight, weightMask);
                                    else
                                        Core.BlendAdditiveLayer(ref outputStream, ref inputStream, layer.weight);
                                    break;
                            }
                        }
                    }
                }

                animationBuffer.ResizeUninitialized(streamSize);
            }
        }

        [BurstCompile]
        private struct EvaluateEx : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<Rig> rigType;
            [ReadOnly]
            public ComponentTypeHandle<MotionClipData> instanceType;
            [ReadOnly]
            public BufferTypeHandle<MotionClipInstance> clipInstanceType;
            [ReadOnly]
            public BufferTypeHandle<MotionClipTime> clipTimeType;
            [ReadOnly]
            public BufferTypeHandle<MotionClipWeight> clipWeightType;

            [ReadOnly]
            public BufferTypeHandle<MotionClipLayer> layerType;
            [ReadOnly]
            public BufferTypeHandle<MotionClipLayerWeightMask> layerWeightMaskType;

            public BufferTypeHandle<MotionClipMixer> mixerType;

            public BufferTypeHandle<AnimatedData> animationBufferType;

            public BufferTypeHandle<WeightData> weightMaskBufferType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Evaluate evaluate;
                evaluate.rigs = chunk.GetNativeArray(ref rigType);
                evaluate.instances = chunk.GetNativeArray(ref instanceType);
                evaluate.clipInstances = chunk.GetBufferAccessor(ref clipInstanceType);
                evaluate.clipTimes = chunk.GetBufferAccessor(ref clipTimeType);
                evaluate.clipWeights = chunk.GetBufferAccessor(ref clipWeightType);
                evaluate.layers = chunk.GetBufferAccessor(ref layerType);
                evaluate.layerWeightMasks = chunk.GetBufferAccessor(ref layerWeightMaskType);
                evaluate.mixers = chunk.GetBufferAccessor(ref mixerType);
                evaluate.animationBuffers = chunk.GetBufferAccessor(ref animationBufferType);
                evaluate.weightMaskBuffers = chunk.GetBufferAccessor(ref weightMaskBufferType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    evaluate.Execute(i);
            }
        }

        private EntityQuery __group;

        private ComponentTypeHandle<Rig> __rigType;
        private ComponentTypeHandle<MotionClipData> __instanceType;
        private BufferTypeHandle<MotionClipInstance> __clipInstanceType;
        private BufferTypeHandle<MotionClipTime> __clipTimeType;
        private BufferTypeHandle<MotionClipWeight> __clipWeightType;

        private BufferTypeHandle<MotionClipLayer> __layerType;
        private BufferTypeHandle<MotionClipLayerWeightMask> __layerWeightMaskType;

        private BufferTypeHandle<MotionClipMixer> __mixerType;
        private BufferTypeHandle<AnimatedData> __animationBufferType;
        private BufferTypeHandle<WeightData> __weightMaskBufferType;

        public void OnCreate(ref SystemState state)
        {
            __group = state.GetEntityQuery(
                ComponentType.ReadOnly<Rig>(),
                ComponentType.ReadOnly<MotionClipData>(),
                ComponentType.ReadOnly<MotionClipInstance>(),
                ComponentType.ReadOnly<MotionClipTime>(),
                /*ComponentType.ReadOnly<MotionClipLayer>(),
                ComponentType.ReadOnly<MotionClipLayerWeightMask>(),*/
                ComponentType.ReadWrite<MotionClipMixer>(),
                ComponentType.ReadWrite<AnimatedData>()/*,
                ComponentType.ReadWrite<WeightData>()*/);

            __rigType = state.GetComponentTypeHandle<Rig>(true);
            __instanceType = state.GetComponentTypeHandle<MotionClipData>(true);
            __clipInstanceType = state.GetBufferTypeHandle<MotionClipInstance>(true);
            __clipTimeType = state.GetBufferTypeHandle<MotionClipTime>(true);
            __clipWeightType = state.GetBufferTypeHandle<MotionClipWeight>(true);

            __layerType = state.GetBufferTypeHandle<MotionClipLayer>(true);
            __layerWeightMaskType = state.GetBufferTypeHandle<MotionClipLayerWeightMask>(true);

            __mixerType = state.GetBufferTypeHandle<MotionClipMixer>();
            __animationBufferType = state.GetBufferTypeHandle<AnimatedData>();
            __weightMaskBufferType = state.GetBufferTypeHandle<WeightData>();
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EvaluateEx evaluate;
            evaluate.rigType = __rigType.UpdateAsRef(ref state);
            evaluate.instanceType = __instanceType.UpdateAsRef(ref state);
            evaluate.clipInstanceType = __clipInstanceType.UpdateAsRef(ref state);
            evaluate.clipTimeType = __clipTimeType.UpdateAsRef(ref state);
            evaluate.clipWeightType = __clipWeightType.UpdateAsRef(ref state);
            evaluate.layerType = __layerType.UpdateAsRef(ref state);
            evaluate.layerWeightMaskType = __layerWeightMaskType.UpdateAsRef(ref state);
            evaluate.mixerType = __mixerType.UpdateAsRef(ref state);
            evaluate.animationBufferType = __animationBufferType.UpdateAsRef(ref state);
            evaluate.weightMaskBufferType = __weightMaskBufferType.UpdateAsRef(ref state);

            state.Dependency = evaluate.ScheduleParallelByRef(__group, state.Dependency);
        }
    }
}