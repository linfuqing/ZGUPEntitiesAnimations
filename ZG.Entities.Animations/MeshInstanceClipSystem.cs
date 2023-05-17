using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Animation;

namespace ZG
{
    [System.Flags]
    public enum MeshInstanceClipFlag
    {
        Looping = 0x01,
        Mirror = 0x02, 
        InPlace = 0x04
    }

    public struct MeshInstanceClipFactoryDefinition
    {
        public struct Rig
        {
            public int index;

            public int defaultClipIndex;

            public StringHash rootID;

            public BlobArray<int> clipIndices;
        }

        public int instanceID;
        public BlobArray<Rig> rigs;
    }

    public struct MeshInstanceClipDefinition
    {
        public struct Clip
        {
            public MeshInstanceClipFlag flag;

            public int animationIndex;

            public BlobArray<int> remapIndices;

            //public float speed;

            //public float blendTime;
        }

        public struct Remap
        {
            public int sourceRigIndex;
            public int destinationRigIndex;
        }

        public BlobArray<Clip> clips;
        public BlobArray<Remap> remaps;
    }

    public struct MeshInstanceClipData : IComponentData
    {
        public BlobAssetReference<MeshInstanceClipDefinition> definition;
    }

    public struct MeshInstanceMotionClipFactoryData : IComponentData
    {
        public BlobAssetReference<MeshInstanceClipFactoryDefinition> definition;
    }

    public struct MeshInstanceMotionClipID : IComponentData
    {
        public int value;
        //public int rigInstanceID;
    }

    [BurstCompile, UpdateInGroup(typeof(MeshInstanceSystemGroup), OrderFirst = true), UpdateAfter(typeof(MeshInstanceRigFactorySystem))]
    public partial struct MeshInstanceMotionClipFactorySystem : ISystem
    {
        /*public struct Prefab
        {
            public int count;
        }*/

        private struct DefaultClip
        {
            public MotionClip value;

            public Entity entity;
        }

        /*private struct CollectToDestroy
        {
            [ReadOnly]
            public NativeArray<MeshInstanceMotionClipID> ids;

            public SharedHashMap<int, Prefab>.Writer prefabs;

            public void Execute(int index)
            {
                int id = ids[index].value;
                if (prefabs.TryGetValue(id, out var prefab))
                {
                    if (--prefab.count < 1)
                        prefabs.Remove(id);
                    else
                        prefabs[id] = prefab;
                }
            }
        }

        [BurstCompile]
        private struct CollectToDestroyEx : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceMotionClipID> idType;

            public SharedHashMap<int, Prefab>.Writer prefabs;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                CollectToDestroy collect;
                collect.ids = batchInChunk.GetNativeArray(idType);
                collect.prefabs = prefabs;

                int count = batchInChunk.Count;
                for (int i = 0; i < count; ++i)
                    collect.Execute(i);
            }
        }*/

        private struct Collect//ToCreate
        {
            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Reader rigPrefabs;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<Clip>>.Reader clips;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables;

            [ReadOnly]
            public ComponentLookup<MeshInstanceRigID> rigIDMap;

            [ReadOnly]
            public ComponentLookup<MotionClipData> clipDatas;

            [ReadOnly]
            public NativeArray<MeshInstanceMotionClipFactoryData> factories;

            [ReadOnly]
            public NativeArray<MeshInstanceClipData> instances;

            [ReadOnly]
            public NativeArray<MeshInstanceRigID> rigIDs;

            [ReadOnly]
            public BufferAccessor<EntityParent> entityParents;

            public UnsafeListEx<Entity> rigEntities;

            public UnsafeListEx<MotionClipData> motionClips;

            public UnsafeListEx<DefaultClip> defaultClips;

            //public SharedHashMap<int, Prefab>.Writer prefabs;

            public int Execute(int index)
            {
                ref var factory = ref factories[index].definition.Value;

                int rigInstanceID;
                if (index < rigIDs.Length)
                    rigInstanceID = rigIDs[index].value;
                else
                {
                    Entity parentEntity = EntityParent.Get(entityParents[index], rigIDMap);
                    if (parentEntity == Entity.Null)
                        return factory.instanceID;

                    rigInstanceID = rigIDMap[parentEntity].value;
                }

                /*if (prefabs.TryGetValue(factory.instanceID, out var prefab))
                {
                    ++prefab.count;

                    prefabs[factory.instanceID] = prefab;
                }
                else
                {
                    prefab.count = 1;
                    prefabs.Add(factory.instanceID, prefab);
                }*/

                //��Rig���ر��ٴ�ʱ��Ҳ�����ã����Բ���ʹ��Prefab

                MotionClipData motionClip;
                motionClip.rootTransform = MotionClipTransform.Identity;

                ref var definition = ref instances[index].definition.Value;
                ref var rigPrefab = ref rigPrefabs[rigInstanceID].Value;
                DefaultClip defaultClip;
                Entity entity;
                int numRigs = factory.rigs.Length, numRigEntities/*, numDefaultClips*/, i, j;
                for (i = 0; i < numRigs; ++i)
                {
                    ref var rig = ref factory.rigs[i];

                    entity = rigPrefab.rigs[rig.index].entity;

                    if (clipDatas.HasComponent(entity))
                        continue;

                    numRigEntities = rigEntities.length;
                    for (j = 0; j < numRigEntities; ++j)
                    {
                        if (rigEntities.ElementAt(j) == entity)
                            break;
                    }

                    if (j == numRigEntities)
                    {
                        rigEntities.Add(entity);

                        motionClip.rootID = rig.rootID;

                        motionClips.Add(motionClip);
                    
                        if (rig.defaultClipIndex != -1)
                    /*{
                        numDefaultClips = defaultClips.length;
                        for (j = 0; j < numDefaultClips; ++j)
                        {
                            if (defaultClips.ElementAt(j).entity == entity)
                                break;
                        }

                        if (j == numDefaultClips)*/
                        {
                            defaultClip.value = definition.ToMotionClip(
                                rig.clipIndices[rig.defaultClipIndex],
                                rig.index, 
                                factory.instanceID,
                                rigInstanceID,
                                1.0f,
                                clips,
                                rigDefinitions,
                                rigRemapTables);

                            defaultClip.entity = entity;

                            defaultClips.Add(defaultClip);
                        }
                    }
                }

                return factory.instanceID;
            }
        }

        [BurstCompile]
        private struct CollectEx : IJobChunk
        {
            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Reader rigPrefabs;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<Clip>>.Reader clips;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables;

            [ReadOnly]
            public ComponentLookup<MeshInstanceRigID> rigIDs;

            [ReadOnly]
            public ComponentLookup<MotionClipData> clipDatas;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceMotionClipFactoryData> factoryType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceClipData> instanceType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceRigID> rigIDType;

            [ReadOnly]
            public BufferTypeHandle<EntityParent> entityParentType;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<int> baseEntityIndexArray;

            public NativeArray<MeshInstanceMotionClipID> ids;

            public UnsafeListEx<Entity> rigEntities;

            public UnsafeListEx<MotionClipData> motionClips;

            public UnsafeListEx<DefaultClip> defaultClips;

            //public SharedHashMap<int, Prefab>.Writer prefabs;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Collect collect;
                collect.rigPrefabs = rigPrefabs;
                collect.clips = clips;
                collect.rigDefinitions = rigDefinitions;
                collect.rigRemapTables = rigRemapTables;
                collect.rigIDMap = rigIDs;
                collect.clipDatas = clipDatas;
                collect.factories = chunk.GetNativeArray(ref factoryType);
                collect.instances = chunk.GetNativeArray(ref instanceType);
                collect.rigIDs = chunk.GetNativeArray(ref rigIDType);
                collect.entityParents = chunk.GetBufferAccessor(ref entityParentType);
                collect.rigEntities = rigEntities;
                collect.motionClips = motionClips;
                collect.defaultClips = defaultClips;
                //collect.prefabs = prefabs;

                MeshInstanceMotionClipID id;
                int index = baseEntityIndexArray[unfilteredChunkIndex];
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    id.value = collect.Execute(i);

                    ids[index++] = id;
                }
            }
        }

        private struct Play : IJobParallelFor
        {
            public double time;

            [ReadOnly]
            public UnsafeListEx<DefaultClip> defaultClips;

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
                var defaultClip = defaultClips[index];

                var motionClips = this.motionClips[defaultClip.entity];
                var motionClipTimes = this.motionClipTimes[defaultClip.entity];
                var motionClipWeights = this.motionClipWeights[defaultClip.entity];
                var motionClipWeightSteps = this.motionClipWeightSteps[defaultClip.entity];

                MotionClipBlendSystem.Play(
                    0.0f,
                    0.0f,
                    time,
                    defaultClip.value,
                    ref motionClips,
                    ref motionClipTimes,
                    ref motionClipWeights,
                    ref motionClipWeightSteps);
            }
        }

        [BurstCompile]
        private struct CopyClips : IJobParallelFor
        {
            [ReadOnly]
            public UnsafeListEx<Entity> entityArray;

            [ReadOnly]
            public UnsafeListEx<MotionClipData> source;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<MotionClipData> destination;

            public void Execute(int index)
            {
                destination[entityArray[index]] = source[index];
            }
        }

        [BurstCompile]
        private struct DisposeClips : IJob
        {
            public UnsafeListEx<Entity> entities;

            public UnsafeListEx<MotionClipData> values;

            public void Execute()
            {
                entities.Dispose();

                values.Dispose();
            }
        }

        [BurstCompile]
        private struct DisposeAll : IJob
        {
            public UnsafeListEx<DefaultClip> defaultClips;

            public void Execute()
            {
                defaultClips.Dispose();
            }
        }

        public static readonly int InnerloopBatchCount = 1;

        private ComponentTypeSet __rigComponentTypes;
        private EntityQuery __groupToDestroy;
        private EntityQuery __groupToCreate;
        private SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>> __rigPrefabs;
        private SingletonAssetContainer<BlobAssetReference<Clip>> __clips;
        private SingletonAssetContainer<BlobAssetReference<RigDefinition>> __rigDefinitions;
        private SingletonAssetContainer<BlobAssetReference<RigRemapTable>> __rigRemapTables;

        /*public SharedHashMap<int, Prefab> prefabs
        {
            get;

            private set;
        }*/

        public void OnCreate(ref SystemState state)
        {
            BurstUtility.InitializeJobParallelFor<Play>();
            BurstUtility.InitializeJobParallelFor<CopyClips>();
            BurstUtility.InitializeJob<DisposeClips>();
            BurstUtility.InitializeJob<DisposeAll>();

            __rigComponentTypes = new ComponentTypeSet(
                ComponentType.ReadOnly<MotionClipData>(), 
                ComponentType.ReadWrite<MotionClip>(),
                ComponentType.ReadWrite<MotionClipTime>(),
                ComponentType.ReadWrite<MotionClipWeight>(),
                ComponentType.ReadWrite<MotionClipWeightStep>());

            __groupToDestroy = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceMotionClipID>()
                    },

                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceMotionClipFactoryData)
                    }
                }, 
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceMotionClipID>(),
                        ComponentType.ReadOnly<MeshInstanceMotionClipFactoryData>()
                    }, 
                    Any = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceRigDisabled>()
                    }, 
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __groupToCreate = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceMotionClipFactoryData>()
                    }, 
                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceMotionClipID), 
                        typeof(MeshInstanceRigDisabled)
                    }, 
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __rigPrefabs = state.World.GetOrCreateSystemUnmanaged<MeshInstanceRigFactorySystem>().prefabs;
            __clips = SingletonAssetContainer<BlobAssetReference<Clip>>.instance;
            __rigDefinitions = SingletonAssetContainer<BlobAssetReference<RigDefinition>>.instance;
            __rigRemapTables = SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.instance;

            //prefabs = new SharedHashMap<int, Prefab>(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            //prefabs.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //var writer = prefabs.writer;

            var entityManager = state.EntityManager;

            //if (!__groupToDestroy.IsEmptyIgnoreFilter)
            {
                /*state.CompleteDependency();
                prefabs.lookupJobManager.CompleteReadWriteDependency();

                CollectToDestroyEx collect;
                collect.idType = state.GetComponentTypeHandle<MeshInstanceMotionClipID>(true);
                collect.prefabs = writer;
                collect.RunBurstCompatible(__groupToDestroy);*/

                entityManager.RemoveComponent<MeshInstanceMotionClipID>(__groupToDestroy);
            }

            int entityCount = __groupToCreate.CalculateEntityCount();
            if (entityCount > 0)
            {
                JobHandle inputDeps = state.Dependency;

                var defaultClips = new UnsafeListEx<DefaultClip>(Allocator.TempJob);
                var motionClips = new UnsafeListEx<MotionClipData>(Allocator.TempJob);
                var rigEntities = new UnsafeListEx<Entity>(Allocator.TempJob);

                using (var ids = new NativeArray<MeshInstanceMotionClipID>(entityCount, Allocator.TempJob))
                {
                    state.CompleteDependency();
                    __rigPrefabs.lookupJobManager.CompleteReadOnlyDependency();
                    //prefabs.lookupJobManager.CompleteReadWriteDependency();

                    CollectEx collect;
                    collect.rigPrefabs = __rigPrefabs.reader;
                    collect.clips = __clips.reader;
                    collect.rigDefinitions = __rigDefinitions.reader;
                    collect.rigRemapTables = __rigRemapTables.reader;
                    collect.rigIDs = state.GetComponentLookup<MeshInstanceRigID>(true);
                    collect.clipDatas = state.GetComponentLookup<MotionClipData>(true);
                    collect.factoryType = state.GetComponentTypeHandle<MeshInstanceMotionClipFactoryData>(true);
                    collect.instanceType = state.GetComponentTypeHandle<MeshInstanceClipData>(true);
                    collect.rigIDType = state.GetComponentTypeHandle<MeshInstanceRigID>(true);
                    collect.entityParentType = state.GetBufferTypeHandle<EntityParent>(true);
                    collect.baseEntityIndexArray = __groupToCreate.CalculateBaseEntityIndexArray(Allocator.TempJob);
                    collect.ids = ids;
                    collect.rigEntities = rigEntities;
                    collect.motionClips = motionClips;
                    collect.defaultClips = defaultClips;
                    //collect.prefabs = writer;

                    collect.Run(__groupToCreate);

                    entityManager.AddComponentDataBurstCompatible(__groupToCreate, ids);
                }

                int numRigEntities = rigEntities.length;
                for (int i = 0; i < numRigEntities; ++i)
                    entityManager.AddComponent(rigEntities[i], __rigComponentTypes);

                CopyClips copyClips;
                copyClips.entityArray = rigEntities;
                copyClips.source = motionClips;
                copyClips.destination = state.GetComponentLookup<MotionClipData>();
                var jobHandle = copyClips.Schedule(rigEntities.length, InnerloopBatchCount, inputDeps);

                DisposeClips disposeClips;
                disposeClips.entities = rigEntities;
                disposeClips.values = motionClips;
                jobHandle = disposeClips.Schedule(jobHandle);

                Play play;
                play.time = state.WorldUnmanaged.Time.ElapsedTime;
                play.defaultClips = defaultClips;
                play.motionClips = state.GetBufferLookup<MotionClip>();
                play.motionClipTimes = state.GetBufferLookup<MotionClipTime>();
                play.motionClipWeights = state.GetBufferLookup<MotionClipWeight>();
                play.motionClipWeightSteps = state.GetBufferLookup<MotionClipWeightStep>();

                jobHandle = JobHandle.CombineDependencies(jobHandle, play.Schedule(defaultClips.length, InnerloopBatchCount, inputDeps));

                DisposeAll disposeAll;
                disposeAll.defaultClips = defaultClips;

                state.Dependency = disposeAll.Schedule(jobHandle);
            }
        }
    }

    public static class MeshInstanceMotionClipUtility
    {
        public static MotionClip ToMotionClip(
            this ref MeshInstanceClipDefinition definition,
            int clipIndex,
            int rigIndex, 
            int factoryInstanceID, 
            int rigInstanceID, 
            float speed, 
            in SingletonAssetContainer<BlobAssetReference<Clip>>.Reader clips,
            in SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions,
            in SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.Reader rigRemapTables)
        {
            ref var clip = ref definition.clips[clipIndex];

            MotionClip motionClip;

            motionClip.flag = 0;

            if ((clip.flag & MeshInstanceClipFlag.Mirror) == MeshInstanceClipFlag.Mirror)
                motionClip.flag |= MotionClipFlag.Mirror;

            if ((clip.flag & MeshInstanceClipFlag.InPlace) == MeshInstanceClipFlag.InPlace)
                motionClip.flag |= MotionClipFlag.InPlace;

            motionClip.wrapMode = (clip.flag & MeshInstanceClipFlag.Looping) == MeshInstanceClipFlag.Looping ? MotionClipWrapMode.Loop : MotionClipWrapMode.Normal;
            motionClip.layerIndex = 0;
            motionClip.depth = 0;

            motionClip.speed = speed;
            motionClip.value = clips[new SingletonAssetContainerHandle(factoryInstanceID, clip.animationIndex)];

            motionClip.remapTable = default;
            motionClip.remapDefinition = default;

            int numRemapIndices = clip.remapIndices.Length, remapIndex;
            for (int i = 0; i < numRemapIndices; ++i)
            {
                remapIndex = clip.remapIndices[i];
                ref var remap = ref definition.remaps[remapIndex];
                if (remap.destinationRigIndex == rigIndex)
                {
                    motionClip.remapTable = rigRemapTables[new SingletonAssetContainerHandle(factoryInstanceID, remapIndex)];
                    motionClip.remapDefinition = rigDefinitions[new SingletonAssetContainerHandle(rigInstanceID, remap.sourceRigIndex)];

                    break;
                }
            }

            return motionClip;
        }
    }
}