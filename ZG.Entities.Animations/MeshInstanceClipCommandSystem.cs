using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Entities;
using Unity.Animation;

namespace ZG
{
    [Serializable]
    public struct MeshInstanceMotionClipCommand : IComponentData
    {
        public uint version;
        public int rigIndex;
        public int clipIndex;
        public float speed;
        public float blendTime;
    }

    [Serializable]
    public struct MeshInstanceMotionClipCommandVersion : IComponentData
    {
        public uint value;
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
            public BufferAccessor<MeshInstanceRig> rigs;

            [ReadOnly]
            public NativeArray<MeshInstanceRigID> rigIDs;

            [ReadOnly]
            public NativeArray<MeshInstanceClipData> instances;

            [ReadOnly]
            public NativeArray<MeshInstanceMotionClipFactoryData> factories;

            [ReadOnly]
            public NativeArray<MeshInstanceMotionClipCommand> commands;

            public NativeArray<MeshInstanceMotionClipCommandVersion> commandVersions;

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
                var command = commands[index];
                var commandVersion = commandVersions[index];
                if (command.version != commandVersion.value)
                    return;

                commandVersion.value = command.version + 1;
                commandVersions[index] = commandVersion;

                int rigInstanceID = rigIDs[index].value;
                var rigs = this.rigs[index];
                ref var definition = ref instances[index].definition.Value;
                ref var factory = ref factories[index].definition.Value;
                if (command.rigIndex == -1)
                {
                    int numRigs = factory.rigs.Length;
                    for(int i = 0; i < numRigs; ++i)
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
            public BufferTypeHandle<MeshInstanceRig> rigType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceRigID> rigIDType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceClipData> instanceType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceMotionClipFactoryData> factoryType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceMotionClipCommand> commandType;

            public ComponentTypeHandle<MeshInstanceMotionClipCommandVersion> commandVersionType;

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
                command.rigs = chunk.GetBufferAccessor(ref rigType);
                command.rigIDs = chunk.GetNativeArray(ref rigIDType);
                command.instances = chunk.GetNativeArray(ref instanceType);
                command.factories = chunk.GetNativeArray(ref factoryType);
                command.commands = chunk.GetNativeArray(ref commandType);
                command.commandVersions = chunk.GetNativeArray(ref commandVersionType);
                command.safeTimes = safeTimes;
                command.motionClips = motionClips;
                command.motionClipTimes = motionClipTimes;
                command.motionClipWeights = motionClipWeights;
                command.motionClipWeightSteps = motionClipWeightSteps;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    command.Execute(i);
            }
        }

        private EntityQuery __group;
        private SingletonAssetContainer<BlobAssetReference<Clip>> __clips;
        private SingletonAssetContainer<BlobAssetReference<RigDefinition>> __rigDefinitions;
        private SingletonAssetContainer<BlobAssetReference<RigRemapTable>> __rigRemapTables;

        public void OnCreate(ref SystemState state)
        {
            __group = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceRig>(),
                        ComponentType.ReadOnly<MeshInstanceRigID>(),
                        ComponentType.ReadOnly<MeshInstanceClipData>(),
                        ComponentType.ReadOnly<MeshInstanceMotionClipID>(),
                        ComponentType.ReadOnly<MeshInstanceMotionClipFactoryData>(),
                        ComponentType.ReadOnly<MeshInstanceMotionClipCommand>(),
                        ComponentType.ReadWrite<MeshInstanceMotionClipCommandVersion>()
                    },

                    Options = EntityQueryOptions.IncludeDisabledEntities
                });
            __group.SetChangedVersionFilter(typeof(MeshInstanceMotionClipCommand));

            __clips = SingletonAssetContainer<BlobAssetReference<Clip>>.instance;
            __rigDefinitions = SingletonAssetContainer<BlobAssetReference<RigDefinition>>.instance;
            __rigRemapTables = SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.instance;
        }

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
            command.rigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
            command.rigIDType = state.GetComponentTypeHandle<MeshInstanceRigID>(true);
            command.instanceType = state.GetComponentTypeHandle<MeshInstanceClipData>(true);
            command.factoryType = state.GetComponentTypeHandle<MeshInstanceMotionClipFactoryData>(true);
            command.commandType = state.GetComponentTypeHandle<MeshInstanceMotionClipCommand>(true);
            command.commandVersionType = state.GetComponentTypeHandle<MeshInstanceMotionClipCommandVersion>();
            command.safeTimes = state.GetComponentLookup<MotionClipSafeTime>();
            command.motionClips = state.GetBufferLookup<MotionClip>();
            command.motionClipTimes = state.GetBufferLookup<MotionClipTime>();
            command.motionClipWeights = state.GetBufferLookup<MotionClipWeight>();
            command.motionClipWeightSteps = state.GetBufferLookup<MotionClipWeightStep>();

            var jobHandle = command.ScheduleParallel(__group, state.Dependency);

            int systemID = state.GetSystemID();

            __clips.AddDependency(systemID, jobHandle);
            __rigDefinitions.AddDependency(systemID, jobHandle);
            __rigRemapTables.AddDependency(systemID, jobHandle);

            state.Dependency = jobHandle;
        }
    }
}
