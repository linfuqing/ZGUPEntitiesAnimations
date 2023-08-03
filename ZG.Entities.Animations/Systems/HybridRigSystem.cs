using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Animation;
using UnityEngine.Jobs;
using UnityEngine;

namespace ZG
{
    [System.Flags]
    public enum HybridRigNodeTransformTyoe
    {
        Scale = 0x01,
        Rotation = 0x02,
        Translation = 0x04
    }

    public struct HybridRigNode : IComponentData
    {
        public HybridRigNodeTransformTyoe transformType;

        public int boneIndex;

        public Entity rigEntity;
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class HybridRigSystem : SystemBase
    {
        [BurstCompile]
        private struct Collect : IJobChunk
        {
            [ReadOnly]
            public BufferLookup<AnimatedLocalToRoot> localToRoots;

            [ReadOnly]
            public ComponentTypeHandle<HybridRigNode> rigNodeType;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<int> baseEntityIndexArray;

            [NativeDisableParallelForRestriction]
            public NativeArray<float4x4> results;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                DynamicBuffer<AnimatedLocalToRoot> localToRoots;
                var rigNodes = chunk.GetNativeArray(ref rigNodeType);
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                int index = baseEntityIndexArray[unfilteredChunkIndex];
                while (iterator.NextEntityIndex(out int i))
                {
                    var rigNode = rigNodes[i];
                    if (this.localToRoots.HasBuffer(rigNode.rigEntity))
                    {
                        localToRoots = this.localToRoots[rigNode.rigEntity];

                        results[index++] = rigNode.boneIndex < localToRoots.Length ? localToRoots[rigNode.boneIndex].Value : float4x4.identity;
                    }
                    else
                        results[index++] = float4x4.identity;
                }
            }
        }

        [BurstCompile]
        private struct Apply : IJobParallelForTransform
        {
            [DeallocateOnJobCompletion]
            public NativeArray<float4x4> results;

            public void Execute(int index, TransformAccess transform)
            {
                if (!transform.isValid)
                    return;

                var result = (Matrix4x4)results[index];

                transform.localPosition = result.GetPosition();
                transform.localRotation = result.rotation;
                transform.localScale = result.lossyScale;
            }
        }

        private EntityQuery __group;
        private TransformAccessArrayEx __transformAccessArray;
        private BufferLookup<AnimatedLocalToRoot> __localToRoots;
        private ComponentTypeHandle<HybridRigNode> __rigNodeType;

        protected override void OnCreate()
        {
            base.OnCreate();

            __group = GetEntityQuery(TransformAccessArrayEx.componentType, ComponentType.ReadOnly<HybridRigNode>());

            __transformAccessArray = new TransformAccessArrayEx(__group);

            __localToRoots = GetBufferLookup<AnimatedLocalToRoot>(true);
            __rigNodeType = GetComponentTypeHandle<HybridRigNode>(true);
        }

        protected override void OnDestroy()
        {
            __transformAccessArray.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            var transformAccessArray = __transformAccessArray.Convert(this);
            var results = new NativeArray<float4x4>(transformAccessArray.length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            ref var state = ref this.GetState();

            Collect collect;
            collect.localToRoots = __localToRoots.UpdateAsRef(ref state);
            collect.rigNodeType = __rigNodeType.UpdateAsRef(ref state);
            collect.baseEntityIndexArray = __group.CalculateBaseEntityIndexArrayAsync(Allocator.TempJob, Dependency, out var jobHandle);
            collect.results = results;

            jobHandle = collect.ScheduleParallel(__group, jobHandle);

            Apply apply;
            apply.results = results;
            Dependency = apply.Schedule(transformAccessArray, jobHandle);
        }
    }
}