using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Animation;

namespace ZG
{
    public struct MeshInstanceAnimatorControllerClipTrack : IBufferElementData
    {
        public int clipIndex;
        public int rigIndex;
        public float weight;
        public float previousTime;
        public float currentTime;
        public BlobAssetReference<AnimatorControllerDefinition> definition;
    }

    [BurstCompile, UpdateBefore(typeof(AnimationSystemGroup)), UpdateAfter(typeof(AnimatorControllerSystem)), UpdateAfter(typeof(MeshInstanceClipCommandSystem))]
    public partial struct MeshInstanceAnimatorControllerClipTrackSystem : ISystem
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
            public BufferAccessor<MeshInstanceAnimatorControllerClipTrack> tracks;

            [NativeDisableParallelForRestriction]
            public BufferLookup<AnimatorControllerEvent> events;

            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClip> clipValues;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipWeight> clipWeights;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipTime> clipTimes;

            public void Execute(int index)
            {
                Entity entity;
                var rigs = this.rigs[index];
                foreach(var rig in rigs)
                {
                    entity = rigs[index].entity;

                    events[entity].Clear();

                    clipValues[entity].Clear();

                    clipWeights[entity].Clear();

                    if (clipTimes.HasBuffer(entity))
                        clipTimes[entity].Clear();
                }

                int rigInstanceID = rigIDs[index].value;
                var tracks = this.tracks[index];
                foreach (var track in tracks)
                {
                    entity = rigs[index].entity;

                    var events = this.events[entity];
                    var clipValues = this.clipValues[entity];
                    var clipWeights = this.clipWeights[entity];
                    var clipTimes = this.clipTimes.HasBuffer(entity) ? this.clipTimes[entity] : default;

                    ref var definition = ref track.definition.Value;

                    definition.clips[track.clipIndex].Evaluate(
                        track.rigIndex,
                        rigInstanceID,
                        track.definition.Value.instanceID,
                        0, 
                        0,
                        track.weight,
                        0.0f,
                        track.previousTime,
                        track.currentTime,
                        clips,
                        rigDefinitions,
                        rigRemapTables,
                        ref definition.remaps, 
                        ref clipValues,
                        ref clipWeights,
                        ref clipTimes,
                        ref events);
                }

                //commands.Clear();
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

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceAnimatorControllerClipTrack> trackType;

            [NativeDisableParallelForRestriction]
            public BufferLookup<AnimatorControllerEvent> events;

            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClip> clipValues;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipWeight> clipWeights;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipTime> clipTimes;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Evaluate evaluate;
                evaluate.clips = clips;
                evaluate.rigDefinitions = rigDefinitions;
                evaluate.rigRemapTables = rigRemapTables;
                evaluate.rigIDs = chunk.GetNativeArray(ref rigIDType);
                evaluate.rigs = chunk.GetBufferAccessor(ref rigType);
                evaluate.tracks = chunk.GetBufferAccessor(ref trackType);
                evaluate.events = events;
                evaluate.clipValues = clipValues;
                evaluate.clipWeights = clipWeights;
                evaluate.clipTimes = clipTimes;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    evaluate.Execute(i);

                    //chunk.SetComponentEnabled(ref commandType, i, false);
                }
            }
        }

        private EntityQuery __group;

        private SingletonAssetContainer<BlobAssetReference<Clip>> __clips;
        private SingletonAssetContainer<BlobAssetReference<RigDefinition>> __rigDefinitions;
        private SingletonAssetContainer<BlobAssetReference<RigRemapTable>> __rigRemapTables;

        private ComponentTypeHandle<MeshInstanceRigID> __rigIDType;

        private BufferTypeHandle<MeshInstanceRig> __rigType;

        private BufferTypeHandle<MeshInstanceAnimatorControllerClipTrack> __trackType;

        private BufferLookup<AnimatorControllerEvent> __eventType;
        private BufferLookup<MotionClip> __clipType;
        private BufferLookup<MotionClipTime> __clipTimeType;
        private BufferLookup<MotionClipWeight> __clipWeightType;

        //[BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAll<MeshInstanceRig, MeshInstanceRigID, MeshInstanceAnimatorControllerClipTrack>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);

            __clips = SingletonAssetContainer<BlobAssetReference<Clip>>.Retain();
            __rigDefinitions = SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Retain();
            __rigRemapTables = SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Retain();

            __rigIDType = state.GetComponentTypeHandle<MeshInstanceRigID>(true);

            __rigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);

            __trackType = state.GetBufferTypeHandle<MeshInstanceAnimatorControllerClipTrack>(true);

            __eventType = state.GetBufferLookup<AnimatorControllerEvent>();
            __clipType = state.GetBufferLookup<MotionClip>();
            __clipTimeType = state.GetBufferLookup<MotionClipTime>();
            __clipWeightType = state.GetBufferLookup<MotionClipWeight>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __clips.Release();
            __rigDefinitions.Release();
            __rigRemapTables.Release();
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
            evaluate.events = __eventType.UpdateAsRef(ref state);
            evaluate.clipValues = __clipType.UpdateAsRef(ref state);
            evaluate.clipTimes = __clipTimeType.UpdateAsRef(ref state);
            evaluate.clipWeights = __clipWeightType.UpdateAsRef(ref state);

            var jobHandle = evaluate.ScheduleParallelByRef(__group, state.Dependency);

            int id = state.GetSystemID();
            __clips.AddDependency(id, jobHandle);
            __rigDefinitions.AddDependency(id, jobHandle);
            __rigRemapTables.AddDependency(id, jobHandle);

            state.Dependency = jobHandle;
        }
    }
}