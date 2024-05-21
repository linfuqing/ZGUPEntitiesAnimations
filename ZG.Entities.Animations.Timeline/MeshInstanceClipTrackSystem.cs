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

            public void Execute(int index)
            {
                var rigs = this.rigs[index];
                foreach (var rig in rigs)
                {
                    motionClips[rig.entity].Clear();

                    motionClipTimes[rig.entity].Clear();

                    motionClipWeights[rig.entity].Clear();
                }

                int rigInstanceID = rigIDs[index].value;
                var tracks = this.tracks[index];
                foreach (var track in tracks)
                {
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

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceClipTrack> trackType;

            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClip> motionClips;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipTime> motionClipTimes;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipWeight> motionClipWeights;

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

        private SingletonAssetContainer<BlobAssetReference<Clip>> __clips;
        private SingletonAssetContainer<BlobAssetReference<RigDefinition>> __rigDefinitions;
        private SingletonAssetContainer<BlobAssetReference<RigRemapTable>> __rigRemapTables;

        [BurstCompile]
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

            __clips = SingletonAssetContainer<BlobAssetReference<Clip>>.Retain();
            __rigDefinitions = SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Retain();
            __rigRemapTables = SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Retain();
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
            evaluate.motionClips = __motionClips.UpdateAsRef(ref state);
            evaluate.motionClipTimes = __motionClipTimes.UpdateAsRef(ref state);
            evaluate.motionClipWeights = __motionClipWeights.UpdateAsRef(ref state);

            var jobHandle = evaluate.ScheduleParallelByRef(__group, state.Dependency);

            int systemID = state.GetSystemID();

            __clips.AddDependency(systemID, jobHandle);
            __rigDefinitions.AddDependency(systemID, jobHandle);
            __rigRemapTables.AddDependency(systemID, jobHandle);

            state.Dependency = jobHandle;
        }
    }

    [BurstCompile, 
        UpdateInGroup(typeof(AnimationSystemGroup), OrderLast = true), 
        UpdateBefore(typeof(AnimationComputeRigMatrixSystem)), 
        UpdateAfter(typeof(AnimationWriteTransformSystem))]
    public partial struct MeshInstanceClipTrackWriteSystem : ISystem
    {
        private struct Evaluate
        {
            [ReadOnly]
            public BufferAccessor<MeshInstanceRig> rigs;

            [ReadOnly]
            public BufferAccessor<MeshInstanceClipTrack> tracks;

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
                int numRigs = rigs.Length;
                float3 translation, scale;
                quaternion rotation;
                float4x4 matrix;
                LocalToWorld localToWorld;
                MeshInstanceRig rig;
                var tracks = this.tracks[index];
                for(int i = 0; i < numRigs; ++i)
                {
                    rig = rigs[i];

                    if (!localToWorlds.HasComponent(rig.entity))
                        continue;

                    localToWorld = localToWorlds[rig.entity];

                    matrix = float4x4.zero;
                    foreach (var track in tracks)
                    {
                        if (track.rigIndex != i)
                            continue;

                        matrix += (math.determinant(track.matrix) > math.FLT_MIN_NORMAL ? track.matrix : localToWorld.Value) * track.weight;
                    }

                    if (localToParents.HasComponent(rig.entity))
                        localToWorld.Value = localToParents[rig.entity].Value;
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
        }

        [BurstCompile]
        private struct EvaluateEx : IJobChunk
        {
            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> rigType;

            public BufferTypeHandle<MeshInstanceClipTrack> trackType;

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
                evaluate.rigs = chunk.GetBufferAccessor(ref rigType);
                evaluate.tracks = chunk.GetBufferAccessor(ref trackType);
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

        private EntityQuery __group;

        private BufferTypeHandle<MeshInstanceRig> __rigType;
        private BufferTypeHandle<MeshInstanceClipTrack> __trackType;

        private ComponentLookup<LocalToWorld> __localToWorlds;

        private ComponentLookup<LocalToParent> __localToParents;
        private ComponentLookup<Translation> __translations;
        private ComponentLookup<Rotation> __rotations;
        private ComponentLookup<Scale> __scales;
        private ComponentLookup<NonUniformScale> __nonUniformScales;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAll<MeshInstanceRig, MeshInstanceRigID, MeshInstanceClipTrack>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);

            __rigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
            __trackType = state.GetBufferTypeHandle<MeshInstanceClipTrack>(true);
            __localToWorlds = state.GetComponentLookup<LocalToWorld>();
            __localToParents = state.GetComponentLookup<LocalToParent>(true);
            __translations = state.GetComponentLookup<Translation>(true);
            __rotations = state.GetComponentLookup<Rotation>(true);
            __scales = state.GetComponentLookup<Scale>(true);
            __nonUniformScales = state.GetComponentLookup<NonUniformScale>(true);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EvaluateEx evaluate;
            evaluate.rigType = __rigType.UpdateAsRef(ref state);
            evaluate.trackType = __trackType.UpdateAsRef(ref state);
            evaluate.localToWorlds = __localToWorlds.UpdateAsRef(ref state);
            evaluate.localToParents = __localToParents.UpdateAsRef(ref state);
            evaluate.translations = __translations.UpdateAsRef(ref state);
            evaluate.rotations = __rotations.UpdateAsRef(ref state);
            evaluate.scales = __scales.UpdateAsRef(ref state);
            evaluate.nonUniformScales = __nonUniformScales.UpdateAsRef(ref state);

            state.Dependency = evaluate.ScheduleParallelByRef(__group, state.Dependency);
        }
    }
}
