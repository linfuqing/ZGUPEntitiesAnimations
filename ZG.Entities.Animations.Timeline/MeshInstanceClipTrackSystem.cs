using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Entities;
using Unity.Animation;
using Unity.Mathematics;
using Unity.Transforms;

namespace ZG
{
    public struct MeshInstanceClipTrack : IBufferElementData, IEnableableComponent
    {
        public int rigIndex;
        public int clipIndex;
        public float weight;
        public float time;
        public float4x4 matrix;
        public BlobAssetReference<MeshInstanceClipFactoryDefinition> factory;
        public BlobAssetReference<MeshInstanceClipDefinition> definition;
    }

    [BurstCompile, 
        UpdateBefore(typeof(AnimationSystemGroup)),
        UpdateAfter(typeof(TransformSystemGroup)), 
        UpdateAfter(typeof(AnimatorControllerSystem)), 
        UpdateAfter(typeof(MeshInstanceClipCommandSystem))]
    public partial struct MeshInstanceClipTrackSystem : ISystem
    {
        private struct Evaluate
        {
            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<Clip>>.Reader clips;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables;

            [ReadOnly]
            public NativeArray<MeshInstanceRigID> rigIDs;

            [ReadOnly]
            public BufferAccessor<MeshInstanceRig> rigs;

            [ReadOnly]
            public BufferAccessor<MeshInstanceClipTrack> tracks;

            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClip> motionClips;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipTime> motionClipTimes;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipWeight> motionClipWeights;

            public NativeArray<LocalToWorld> localToWorlds;

            public void Execute(int index)
            {
                Entity entity;
                var rigs = this.rigs[index];
                foreach (var rig in rigs)
                {
                    entity = rigs[index].entity;

                    motionClips[entity].Clear();

                    motionClipTimes[entity].Clear();

                    motionClipWeights[entity].Clear();
                }

                LocalToWorld localToWorld;
                localToWorld.Value = float4x4.zero;

                int rigInstanceID = rigIDs[index].value;
                var tracks = this.tracks[index];
                foreach (var track in tracks)
                {
                    localToWorld.Value += math.float4x4(track.matrix) * track.weight;

                    Execute(
                        rigInstanceID,
                        track.clipIndex,
                        track.rigIndex,
                        track.weight,
                        track.time,
                        rigs,
                        ref track.factory.Value,
                        ref track.definition.Value);
                }

                if(index < localToWorlds.Length)
                    localToWorlds[index] = localToWorld;
            }

            public void Execute(
                int rigInstanceID,
                int clipIndex,
                int rigIndex,
                float weight, 
                float time, 
                in DynamicBuffer<MeshInstanceRig> rigs,
                ref MeshInstanceClipFactoryDefinition factory,
                ref MeshInstanceClipDefinition definition)
            {
                ref var rig = ref factory.rigs[rigIndex];

                Entity rigEntity = rigs[rig.index].entity;

                var motionClips = this.motionClips[rigEntity];

                var motionClip = definition.ToMotionClip(
                                rig.clipIndices[clipIndex],
                                rig.index,
                                factory.instanceID,
                                rigInstanceID,
                                0.0f,
                                clips,
                                rigDefinitions,
                                rigRemapTables);

                motionClip.wrapMode = MotionClipWrapMode.Managed;

                motionClips.Add(motionClip);

                var motionClipTimes = this.motionClipTimes[rigEntity];
                MotionClipTime motionClipTime;
                motionClipTime.value = time;
                motionClipTimes.Add(motionClipTime);

                var motionClipWeights = this.motionClipWeights[rigEntity];
                MotionClipWeight motionClipWeight;
                motionClipWeight.value = weight;
                motionClipWeights.Add(motionClipWeight);
            }
        }

        [BurstCompile]
        private struct EvaluateEx : IJobChunk
        {
            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<Clip>>.Reader clips;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceRigID> rigIDType;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> rigType;

            public BufferTypeHandle<MeshInstanceClipTrack> trackType;

            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClip> motionClips;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipTime> motionClipTimes;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipWeight> motionClipWeights;

            public ComponentTypeHandle<LocalToWorld> localToWorldType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Evaluate evaluate;
                evaluate.clips = clips;
                evaluate.rigDefinitions = rigDefinitions;
                evaluate.rigRemapTables = rigRemapTables;
                evaluate.rigIDs = chunk.GetNativeArray(ref rigIDType);
                evaluate.rigs = chunk.GetBufferAccessor(ref rigType);
                evaluate.tracks = chunk.GetBufferAccessor(ref trackType);
                evaluate.motionClips = motionClips;
                evaluate.motionClipTimes = motionClipTimes;
                evaluate.motionClipWeights = motionClipWeights;
                evaluate.localToWorlds = chunk.GetNativeArray(ref localToWorldType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    evaluate.Execute(i);
            }
        }

        private EntityQuery __group;

        private ComponentTypeHandle<MeshInstanceRigID> __rigIDType;

        private BufferTypeHandle<MeshInstanceRig> __rigType;
        private BufferTypeHandle<MeshInstanceClipTrack> __trackType;

        private BufferLookup<MotionClip> __motionClips;
        private BufferLookup<MotionClipTime> __motionClipTimes;
        private BufferLookup<MotionClipWeight> __motionClipWeights;

        private ComponentTypeHandle<LocalToWorld> __localToWorldType;

        private SingletonAssetContainer<BlobAssetReference<Clip>> __clips;
        private SingletonAssetContainer<BlobAssetReference<RigDefinition>> __rigDefinitions;
        private SingletonAssetContainer<BlobAssetReference<RigRemapTable>> __rigRemapTables;

        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAll<MeshInstanceRig, MeshInstanceRigID, MeshInstanceClipTrack>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);

            __rigIDType = state.GetComponentTypeHandle<MeshInstanceRigID>(true);
            __rigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
            __trackType = state.GetBufferTypeHandle<MeshInstanceClipTrack>(true);
            __motionClips = state.GetBufferLookup<MotionClip>();
            __motionClipTimes = state.GetBufferLookup<MotionClipTime>();
            __motionClipWeights = state.GetBufferLookup<MotionClipWeight>();
            __localToWorldType = state.GetComponentTypeHandle<LocalToWorld>();

            __clips = SingletonAssetContainer<BlobAssetReference<Clip>>.instance;
            __rigDefinitions = SingletonAssetContainer<BlobAssetReference<RigDefinition>>.instance;
            __rigRemapTables = SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.instance;
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EvaluateEx evaluate;
            evaluate.clips = __clips.reader;
            evaluate.rigDefinitions = __rigDefinitions.reader;
            evaluate.rigRemapTables = __rigRemapTables.reader;
            evaluate.rigIDType = __rigIDType.UpdateAsRef(ref state);
            evaluate.rigType = __rigType.UpdateAsRef(ref state);
            evaluate.trackType = __trackType.UpdateAsRef(ref state);
            evaluate.motionClips = __motionClips.UpdateAsRef(ref state);
            evaluate.motionClipTimes = __motionClipTimes.UpdateAsRef(ref state);
            evaluate.motionClipWeights = __motionClipWeights.UpdateAsRef(ref state);
            evaluate.localToWorldType = __localToWorldType.UpdateAsRef(ref state);

            var jobHandle = evaluate.ScheduleParallelByRef(__group, state.Dependency);

            int systemID = state.GetSystemID();

            __clips.AddDependency(systemID, jobHandle);
            __rigDefinitions.AddDependency(systemID, jobHandle);
            __rigRemapTables.AddDependency(systemID, jobHandle);

            state.Dependency = jobHandle;
        }
    }
}
