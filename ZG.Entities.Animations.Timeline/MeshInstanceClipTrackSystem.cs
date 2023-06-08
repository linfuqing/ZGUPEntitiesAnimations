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
    public struct MeshInstanceClipTrackMask : ICleanupComponentData
    {

    }

    public struct MeshInstanceClipTrack : IBufferElementData
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
        [BurstCompile]
        private struct Collect : IJobChunk
        {
            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> rigType;

            public NativeList<Entity> entities;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (!chunk.Has(ref rigType))
                    return;

                var rigs = chunk.GetBufferAccessor(ref rigType);
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    entities.AddRange(rigs[i].AsNativeArray().Reinterpret<Entity>());
            }
        }

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

            [NativeDisableParallelForRestriction]
            public ComponentLookup<LocalToWorld> localToWorlds;

            [ReadOnly]
            public ComponentLookup<LocalToParent> localToParents;

            [ReadOnly]
            public ComponentLookup<Translation> translations;

            [ReadOnly]
            public ComponentLookup<Rotation> rotations;

            [ReadOnly]
            public ComponentLookup<Scale> scales;

            [ReadOnly]
            public ComponentLookup<NonUniformScale> nonUniformScales;

            public void Execute(int index)
            {
                var rigs = this.rigs[index];
                foreach (var rig in rigs)
                {
                    motionClips[rig.entity].Clear();

                    motionClipTimes[rig.entity].Clear();

                    motionClipWeights[rig.entity].Clear();
                }

                float4x4 matrix = float4x4.zero;

                int rigInstanceID = rigIDs[index].value;
                var tracks = this.tracks[index];
                foreach (var track in tracks)
                {
                    matrix += math.float4x4(track.matrix) * track.weight;

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

                float3 translation, scale;
                quaternion rotation;
                LocalToWorld localToWorld;
                foreach (var rig in rigs)
                {
                    if (!localToWorlds.HasComponent(rig.entity))
                        continue;

                    if (localToParents.HasComponent(rig.entity))
                    {
                        localToWorld.Value = localToParents[rig.entity].Value;
                    }
                    else
                    {
                        translation = translations.HasComponent(rig.entity) ? translations[rig.entity].Value : float3.zero;

                        rotation = rotations.HasComponent(rig.entity) ? rotations[rig.entity].Value : quaternion.identity;

                        if (nonUniformScales.HasComponent(rig.entity))
                            scale = nonUniformScales[rig.entity].Value;
                        else if (scales.HasComponent(rig.entity))
                            scale = scales[rig.entity].Value;
                        else
                            scale = 1.0f;

                        localToWorld.Value = float4x4.TRS(translation, rotation, scale);
                    }

                    localToWorld.Value = math.mul(matrix, localToWorld.Value);
                    localToWorlds[rig.entity] = localToWorld;
                }
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

            [NativeDisableParallelForRestriction]
            public ComponentLookup<LocalToWorld> localToWorlds;

            [ReadOnly]
            public ComponentLookup<LocalToParent> localToParents;

            [ReadOnly]
            public ComponentLookup<Translation> translations;

            [ReadOnly]
            public ComponentLookup<Rotation> rotations;

            [ReadOnly]
            public ComponentLookup<Scale> scales;

            [ReadOnly]
            public ComponentLookup<NonUniformScale> nonUniformScales;

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
                evaluate.localToWorlds = localToWorlds;
                evaluate.localToParents = localToParents;
                evaluate.translations = translations;
                evaluate.rotations = rotations;
                evaluate.scales = scales;
                evaluate.nonUniformScales = nonUniformScales;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    evaluate.Execute(i);
            }
        }

        private EntityQuery __groupToCreate;
        private EntityQuery __groupToDestroy;
        private EntityQuery __group;

        private ComponentTypeHandle<MeshInstanceRigID> __rigIDType;

        private BufferTypeHandle<MeshInstanceRig> __rigType;
        private BufferTypeHandle<MeshInstanceClipTrack> __trackType;

        private BufferLookup<MotionClip> __motionClips;
        private BufferLookup<MotionClipTime> __motionClipTimes;
        private BufferLookup<MotionClipWeight> __motionClipWeights;

        private ComponentLookup<LocalToWorld> __localToWorlds;

        private ComponentLookup<LocalToParent> __localToParents;
        private ComponentLookup<Translation> __translations;
        private ComponentLookup<Rotation> __rotations;
        private ComponentLookup<Scale> __scales;
        private ComponentLookup<NonUniformScale> __nonUniformScales;


        private SingletonAssetContainer<BlobAssetReference<Clip>> __clips;
        private SingletonAssetContainer<BlobAssetReference<RigDefinition>> __rigDefinitions;
        private SingletonAssetContainer<BlobAssetReference<RigRemapTable>> __rigRemapTables;

        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToCreate = builder
                        .WithAll<MeshInstanceRig, MeshInstanceRigID, MeshInstanceClipTrack>()
                        .WithNone<MeshInstanceClipTrackMask>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToDestroy = builder
                        .WithAll<MeshInstanceClipTrackMask>()
                        .WithNone<MeshInstanceClipTrack>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);

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
            __localToWorlds = state.GetComponentLookup<LocalToWorld>();
            __localToParents = state.GetComponentLookup<LocalToParent>(true);
            __translations = state.GetComponentLookup<Translation>(true);
            __rotations = state.GetComponentLookup<Rotation>(true);
            __scales = state.GetComponentLookup<Scale>(true);
            __nonUniformScales = state.GetComponentLookup<NonUniformScale>(true);

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
            var entityManager = state.EntityManager;
            if (!__groupToCreate.IsEmpty)
            {
                using (var entities = new NativeList<Entity>(Allocator.TempJob))
                {
                    Collect collect;
                    collect.rigType = __rigType.UpdateAsRef(ref state);
                    collect.entities = entities;

                    collect.RunByRef(__groupToCreate);

                    entityManager.AddComponent<MeshInstanceClipTrackMask>(__groupToCreate);

                    entityManager.AddComponent<DisableRootTransformReadWriteTag>(entities.AsArray());
                }
            }

            if (!__groupToDestroy.IsEmpty)
            {
                using (var entities = new NativeList<Entity>(Allocator.TempJob))
                {
                    Collect collect;
                    collect.rigType = __rigType.UpdateAsRef(ref state);
                    collect.entities = entities;

                    collect.RunByRef(__groupToDestroy);

                    entityManager.RemoveComponent<MeshInstanceClipTrackMask>(__groupToDestroy);

                    entityManager.RemoveComponent<DisableRootTransformReadWriteTag>(entities.AsArray());
                }
            }

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
            evaluate.localToWorlds = __localToWorlds.UpdateAsRef(ref state);
            evaluate.localToParents = __localToParents.UpdateAsRef(ref state);
            evaluate.translations = __translations.UpdateAsRef(ref state);
            evaluate.rotations = __rotations.UpdateAsRef(ref state);
            evaluate.scales = __scales.UpdateAsRef(ref state);
            evaluate.nonUniformScales = __nonUniformScales.UpdateAsRef(ref state);

            var jobHandle = evaluate.ScheduleParallelByRef(__group, state.Dependency);

            int systemID = state.GetSystemID();

            __clips.AddDependency(systemID, jobHandle);
            __rigDefinitions.AddDependency(systemID, jobHandle);
            __rigRemapTables.AddDependency(systemID, jobHandle);

            state.Dependency = jobHandle;
        }
    }
}
