using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Entities;
using Unity.Animation;

namespace ZG
{
    public struct MeshInstanceClipCommand : IBufferElementData, IEnableableComponent
    {
        public int rigIndex;
        public int clipIndex;
        public float speed;
        public float blendTime;
    }

    [BurstCompile, UpdateBefore(typeof(AnimationSystemGroup))]
    public partial struct MeshInstanceClipCommandSystem : ISystem
    {
        private struct Command
        {
            public double time;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<Clip>>.Reader clips;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables;

            [ReadOnly]
            public NativeArray<MeshInstanceClipData> instances;

            [ReadOnly]
            public NativeArray<MeshInstanceMotionClipFactoryData> factories;

            [ReadOnly]
            public NativeArray<MeshInstanceRigID> rigIDs;

            [ReadOnly]
            public BufferAccessor<MeshInstanceRig> rigs;

            public BufferAccessor<MeshInstanceClipCommand> commands;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<MotionClipSafeTime> safeTimes;

            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClip> motionClips;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipTime> motionClipTimes;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipWeight> motionClipWeights;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipWeightStep> motionClipWeightSteps;

            public void Execute(int index)
            {
                var commands = this.commands[index];
                
                int rigInstanceID = rigIDs[index].value;
                var rigs = this.rigs[index];
                ref var definition = ref instances[index].definition.Value;
                ref var factory = ref factories[index].definition.Value;
                foreach (var command in commands)
                {
                    if (command.rigIndex == -1)
                    {
                        int numRigs = factory.rigs.Length;
                        for (int i = 0; i < numRigs; ++i)
                            Execute(
                                rigInstanceID,
                                command.clipIndex,
                                i,
                                command.speed,
                                command.blendTime,
                                rigs,
                                ref factory,
                                ref definition);
                    }
                    else
                        Execute(
                            rigInstanceID,
                            command.clipIndex,
                            command.rigIndex,
                            command.speed,
                            command.blendTime,
                            rigs,
                            ref factory,
                            ref definition);
                }

                commands.Clear();
            }

            public void Execute(
                int rigInstanceID, 
                int clipIndex, 
                int rigIndex, 
                float speed, 
                float blendTime, 
                in DynamicBuffer<MeshInstanceRig> rigs, 
                ref MeshInstanceClipFactoryDefinition factory, 
                ref MeshInstanceClipDefinition definition)
            {
                ref var rig = ref factory.rigs[rigIndex];

                Entity rigEntity = rigs[rig.index].entity;

                var motionClips = this.motionClips[rigEntity];
                var motionClipTimes = this.motionClipTimes[rigEntity];
                var motionClipWeights = this.motionClipWeights[rigEntity];
                var motionClipWeightSteps = this.motionClipWeightSteps[rigEntity];

                if (clipIndex == -1)
                    MotionClipBlendSystem.Pause(
                        ref motionClips,
                        ref motionClipTimes,
                        ref motionClipWeights,
                        ref motionClipWeightSteps);
                else
                {
                    var motionClip = definition.ToMotionClip(
                                rig.clipIndices[clipIndex],
                                rig.index, 
                                factory.instanceID,
                                rigInstanceID,
                                speed, 
                                clips,
                                rigDefinitions,
                                rigRemapTables);

                    MotionClipBlendSystem.Play(
                        blendTime,
                        0.0f,
                        time,
                        motionClip,
                        ref motionClips,
                        ref motionClipTimes,
                        ref motionClipWeights,
                        ref motionClipWeightSteps);

                    if(safeTimes.HasComponent(rigEntity))
                    {
                        MotionClipSafeTime safeTime;
                        safeTime.value = Unity.Mathematics.math.max(safeTimes[rigEntity].value, time + blendTime);
                        safeTimes[rigEntity] = safeTime;
                    }
                }
            }
        }

        [BurstCompile]
        private struct CommandEx : IJobChunk
        {
            public double time;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<Clip>>.Reader clips;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceClipData> instanceType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceMotionClipFactoryData> factoryType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceRigID> rigIDType;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> rigType;

            public BufferTypeHandle<MeshInstanceClipCommand> commandType;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<MotionClipSafeTime> safeTimes;

            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClip> motionClips;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipTime> motionClipTimes;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipWeight> motionClipWeights;
            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipWeightStep> motionClipWeightSteps;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Command command;
                command.time = time;
                command.clips = clips;
                command.rigDefinitions = rigDefinitions;
                command.rigRemapTables = rigRemapTables;
                command.instances = chunk.GetNativeArray(ref instanceType);
                command.factories = chunk.GetNativeArray(ref factoryType);
                command.rigIDs = chunk.GetNativeArray(ref rigIDType);
                command.rigs = chunk.GetBufferAccessor(ref rigType);
                command.commands = chunk.GetBufferAccessor(ref commandType);
                command.safeTimes = safeTimes;
                command.motionClips = motionClips;
                command.motionClipTimes = motionClipTimes;
                command.motionClipWeights = motionClipWeights;
                command.motionClipWeightSteps = motionClipWeightSteps;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    command.Execute(i);

                    chunk.SetComponentEnabled(ref commandType, i, false);
                }
            }
        }

        private EntityQuery __group;

        private ComponentTypeHandle<MeshInstanceClipData> __instanceType;

        private ComponentTypeHandle<MeshInstanceMotionClipFactoryData> __factoryType;

        private ComponentTypeHandle<MeshInstanceRigID> __rigIDType;

        private BufferTypeHandle<MeshInstanceRig> __rigType;
        private BufferTypeHandle<MeshInstanceClipCommand> __commandType;

        private ComponentLookup<MotionClipSafeTime> __safeTimes;

        private BufferLookup<MotionClip> __motionClips;
        private BufferLookup<MotionClipTime> __motionClipTimes;
        private BufferLookup<MotionClipWeight> __motionClipWeights;
        private BufferLookup<MotionClipWeightStep> __motionClipWeightSteps;

        private SingletonAssetContainer<BlobAssetReference<Clip>> __clips;
        private SingletonAssetContainer<BlobAssetReference<RigDefinition>> __rigDefinitions;
        private SingletonAssetContainer<BlobAssetReference<RigRemapTable>> __rigRemapTables;

        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAll<MeshInstanceRig, MeshInstanceRigID, MeshInstanceClipData, MeshInstanceMotionClipID, MeshInstanceMotionClipFactoryData>()
                        .WithAllRW<MeshInstanceClipCommand>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);

            __group.SetChangedVersionFilter(ComponentType.ReadWrite<MeshInstanceClipCommand>());

            __instanceType = state.GetComponentTypeHandle<MeshInstanceClipData>(true);
            __factoryType = state.GetComponentTypeHandle<MeshInstanceMotionClipFactoryData>(true);
            __rigIDType = state.GetComponentTypeHandle<MeshInstanceRigID>(true);
            __rigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
            __commandType = state.GetBufferTypeHandle<MeshInstanceClipCommand>();
            __safeTimes = state.GetComponentLookup<MotionClipSafeTime>();
            __motionClips = state.GetBufferLookup<MotionClip>();
            __motionClipTimes = state.GetBufferLookup<MotionClipTime>();
            __motionClipWeights = state.GetBufferLookup<MotionClipWeight>();
            __motionClipWeightSteps = state.GetBufferLookup<MotionClipWeightStep>();

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
            CommandEx command;
            command.time = state.WorldUnmanaged.Time.ElapsedTime;
            command.clips = __clips.reader;
            command.rigDefinitions = __rigDefinitions.reader;
            command.rigRemapTables = __rigRemapTables.reader;
            command.instanceType = __instanceType.UpdateAsRef(ref state);
            command.factoryType = __factoryType.UpdateAsRef(ref state);
            command.rigIDType = __rigIDType.UpdateAsRef(ref state);
            command.rigType = __rigType.UpdateAsRef(ref state);
            command.commandType = __commandType.UpdateAsRef(ref state);
            command.safeTimes = __safeTimes.UpdateAsRef(ref state);
            command.motionClips = __motionClips.UpdateAsRef(ref state);
            command.motionClipTimes = __motionClipTimes.UpdateAsRef(ref state);
            command.motionClipWeights = __motionClipWeights.UpdateAsRef(ref state);
            command.motionClipWeightSteps = __motionClipWeightSteps.UpdateAsRef(ref state);

            var jobHandle = command.ScheduleParallelByRef(__group, state.Dependency);

            int systemID = state.GetSystemID();

            __clips.AddDependency(systemID, jobHandle);
            __rigDefinitions.AddDependency(systemID, jobHandle);
            __rigRemapTables.AddDependency(systemID, jobHandle);

            state.Dependency = jobHandle;
        }
    }
}
