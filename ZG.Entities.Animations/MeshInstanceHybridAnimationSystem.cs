using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Animation;

namespace ZG
{
    public struct MeshInstaceHybridAnimationDefinition
    {
        public struct Rig
        {
            public int index;

            //public int fieldCount;
        }

        public int instanceID;

        public int objectCount;

        public BlobArray<Rig> rigs;
    }

    public struct MeshInstanceHybridAnimationData : IComponentData
    {
        public BlobAssetReference<MeshInstaceHybridAnimationDefinition> definition;
    }

    public struct MeshInstanceHybridAnimationID : ICleanupComponentData
    {
        public int value;
    }

    [Serializable]
    public struct MeshInstanceHybridAnimationObjectData : IComponentData
    {
        public int index;
    }

    [Serializable]
    public struct MeshInstanceHybridAnimationObjectInfo : ICleanupComponentData
    {
        public Entity parentEntity;
        public int parentChildIndex;
    }

    public struct MeshInstanceHybridAnimationDisabled : IComponentData
    {

    }

    [BurstCompile, UpdateInGroup(typeof(MeshInstanceSystemGroup), OrderFirst = true), UpdateAfter(typeof(MeshInstanceRigFactorySystem))]
    public partial struct MeshInstanceHybridAnimationFactorySystem : ISystem
    {
        private struct Collect
        {
            [ReadOnly]
            public NativeArray<MeshInstanceHybridAnimationData> instances;

            [ReadOnly]
            public NativeArray<MeshInstanceRigID> rigIDs;

            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Reader rigPrefabs;

            public NativeList<Entity> entities;

            public void Execute(int index)
            {
                ref var definition = ref instances[index].definition.Value;

                ref var rigPrefab = ref rigPrefabs[rigIDs[index].value].Value;
                Entity entity;
                int numRigs = definition.rigs.Length, numEntities, i, j;
                for (i = 0; i < numRigs; ++i)
                {
                    entity = rigPrefab.rigs[definition.rigs[i].index].entity;
                    numEntities = entities.Length;
                    for(j = 0; j < numEntities; ++j)
                    {
                        if (entities.ElementAt(j) == entity)
                            break;
                    }

                    if(j == numEntities)
                        entities.Add(entity);
                }
            }
        }

        [BurstCompile]
        private struct CollectEx : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceHybridAnimationData> instanceType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceRigID> rigIDType;

            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Reader rigPrefabs;

            public NativeList<Entity> entities;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Collect collect;
                collect.instances = chunk.GetNativeArray(ref instanceType);
                collect.rigIDs = chunk.GetNativeArray(ref rigIDType);
                collect.rigPrefabs = rigPrefabs;
                collect.entities = entities;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    collect.Execute(i);
            }
        }

        [BurstCompile]
        private struct Init : IJobParallelFor
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public ComponentLookup<MeshInstanceHybridAnimationData> instances;

            [ReadOnly]
            public ComponentLookup<MeshInstanceRigID> rigIDs;

            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Reader rigPrefabs;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<HybridAnimationDefinition>>.Reader animationDefinitions;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<HybridAnimationData> animations;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<HybridAnimationRoot> animationRoots;

            public void Execute(int index)
            {
                var entity = entityArray[index];
                ref var definition = ref instances[entity].definition.Value;
                ref var rigPrefab = ref rigPrefabs[rigIDs[entity].value].Value;

                HybridAnimationRoot animationRoot;
                animationRoot.entity = entityArray[index];

                SingletonAssetContainerHandle handle;
                handle.instanceID = definition.instanceID;

                int numRigs = definition.rigs.Length;
                Entity rigEntity;
                HybridAnimationData animation;
                for (int i = 0; i < numRigs; ++i)
                {
                    ref var rig = ref definition.rigs[i];

                    rigEntity = rigPrefab.rigs[rig.index].entity;

                    handle.index = i;

                    animation.definition = animationDefinitions[handle];
                    animations[rigEntity] = animation;

                    animationRoots[rigEntity] = animationRoot;
                }
            }
        }

        public static readonly int InnerloopBatchCount = 1;

        private ComponentTypeSet __animationComponentTypes;
        private EntityQuery __groupToDestroy;
        private EntityQuery __groupToCreate;
        private SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>> __rigPrefabs;
        private SingletonAssetContainer<BlobAssetReference<HybridAnimationDefinition>> __animationDefinitions;

        public void OnCreate(ref SystemState state)
        {
            BurstUtility.InitializeJobParallelFor<Init>();

            __animationComponentTypes = new ComponentTypeSet(
                ComponentType.ReadOnly<HybridAnimationData>(),
                ComponentType.ReadOnly<HybridAnimationRoot>(),
                ComponentType.ReadWrite<OldAnimatedData>());

            __groupToDestroy = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceHybridAnimationID>()
                    },

                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceHybridAnimationData)
                    }
                },
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceHybridAnimationID>(),
                        ComponentType.ReadOnly<MeshInstanceHybridAnimationData>(),
                    },

                    Any = new ComponentType[]
                    {
                        typeof(MeshInstanceHybridAnimationDisabled)
                    },

                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __groupToCreate = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceHybridAnimationData>(),
                        ComponentType.ReadOnly<MeshInstanceRigID>()
                    }, 
                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceHybridAnimationID)
                    }, 
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __rigPrefabs = state.World.GetOrCreateSystemUnmanaged<MeshInstanceRigFactorySystem>().prefabs;

            __animationDefinitions = SingletonAssetContainer<BlobAssetReference<HybridAnimationDefinition>>.instance;
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var entityManager = state.EntityManager;
            entityManager.RemoveComponent<MeshInstanceHybridAnimationID>(__groupToDestroy);

            using (var entities = new NativeList<Entity>(Allocator.TempJob))
            {
                __rigPrefabs.lookupJobManager.CompleteReadOnlyDependency();
                state.CompleteDependency();

                CollectEx collect;
                collect.instanceType = state.GetComponentTypeHandle<MeshInstanceHybridAnimationData>(true);
                collect.rigIDType = state.GetComponentTypeHandle<MeshInstanceRigID>(true);
                collect.rigPrefabs = __rigPrefabs.reader;
                collect.entities = entities;

                collect.Run(__groupToCreate);

                int numEntities = entities.Length;
                for (int i = 0; i < numEntities; ++i)
                    entityManager.AddComponent(entities[i], __animationComponentTypes);
            }

            var entityArray = __groupToCreate.ToEntityArrayBurstCompatible(state.GetEntityTypeHandle(), Allocator.TempJob);
            entityManager.AddComponent<MeshInstanceHybridAnimationID>(__groupToCreate);

            Init init;
            init.entityArray = entityArray;
            init.instances = state.GetComponentLookup<MeshInstanceHybridAnimationData>(true);
            init.rigIDs = state.GetComponentLookup<MeshInstanceRigID>(true);
            init.rigPrefabs = __rigPrefabs.reader;
            init.animationDefinitions = __animationDefinitions.reader;
            init.animations = state.GetComponentLookup<HybridAnimationData>();
            init.animationRoots = state.GetComponentLookup<HybridAnimationRoot>();

            var jobHandle = init.Schedule(entityArray.Length, InnerloopBatchCount, state.Dependency);

            __animationDefinitions.AddDependency(state.GetSystemID(), jobHandle);

            state.Dependency = jobHandle;
        }
    }

    [UpdateInGroup(typeof(MeshInstanceSystemGroup)), UpdateAfter(typeof(MeshInstanceRigSystem))]
    public partial struct MeshInstanceHybridAnimationSystem : ISystem
    {
        [BurstCompile]
        private struct Init : IJobParallelFor
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public ComponentLookup<MeshInstanceHybridAnimationData> instances;

            [ReadOnly]
            public BufferLookup<MeshInstanceRig> rigs;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<HybridAnimationRoot> animationRoots;

            [NativeDisableParallelForRestriction]
            public BufferLookup<HybridAnimationObject> animationObjects;

            public void Execute(int index)
            {
                var entity = entityArray[index];
                ref var definition = ref instances[entity].definition.Value;
                var rigs = this.rigs[entity];

                HybridAnimationRoot animationRoot;
                animationRoot.entity = entityArray[index];

                int numRigs = definition.rigs.Length;
                for (int i = 0; i < numRigs; ++i)
                    animationRoots[rigs[definition.rigs[i].index].entity] = animationRoot;

                var animationObjects = this.animationObjects[entity];
                animationObjects.ResizeUninitialized(definition.objectCount);

                //TODO:
                HybridAnimationObject animationObject;
                animationObject.entity = Entity.Null;
                for (int i = 0; i < definition.objectCount; ++i)
                    animationObjects[i] = animationObject;
            }
        }

        private struct SetObjects
        {
            [ReadOnly]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeArray<MeshInstanceHybridAnimationObjectData> instances;

            [ReadOnly]
            public NativeArray<EntityParent> entityParents;

            public BufferLookup<HybridAnimationObject> animationObjects;

            public void Execute(int index)
            {
                HybridAnimationObject animationObject;
                animationObject.entity = entityArray[index];

                Entity entity = index < entityParents.Length ? entityParents[index].entity : animationObject.entity;
                if (this.animationObjects.HasBuffer(entity))
                {
                    var animationObjects = this.animationObjects[entity];
                    animationObjects[instances[index].index] = animationObject;
                }
            }
        }

        [BurstCompile]
        private struct SetObjectsEx : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle entityType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceHybridAnimationObjectData> instanceType;

            [ReadOnly]
            public ComponentTypeHandle<EntityParent> entityParentType;

            public BufferLookup<HybridAnimationObject> animationObjects;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                SetObjects setObjects;
                setObjects.entityArray = chunk.GetNativeArray(entityType);
                setObjects.instances = chunk.GetNativeArray(ref instanceType);
                setObjects.entityParents = chunk.GetNativeArray(ref entityParentType);
                setObjects.animationObjects = animationObjects;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    setObjects.Execute(i);
            }
        }

        public static readonly int InnerloopBatchCount = 1;

        private EntityQuery __rootGroup;
        private EntityQuery __animationGroup;

        public void OnCreate(ref SystemState state)
        {
            __rootGroup = state.GetEntityQuery(
                ComponentType.ReadOnly<MeshInstanceHybridAnimationData>(),
                ComponentType.ReadOnly<MeshInstanceHybridAnimationID>(),
                ComponentType.ReadOnly<MeshInstanceRig>(),
                ComponentType.Exclude<HybridAnimationObject>());

            __animationGroup = state.GetEntityQuery(
                ComponentType.ReadOnly<MeshInstanceHybridAnimationObjectData>(),
                ComponentType.Exclude<MeshInstanceHybridAnimationObjectInfo>());
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        public void OnUpdate(ref SystemState state)
        {
            var entityArray = __rootGroup.ToEntityArray(Allocator.TempJob);

            state.EntityManager.AddComponent<HybridAnimationObject>(__rootGroup);

            Init init;
            init.entityArray = entityArray;
            init.instances = state.GetComponentLookup<MeshInstanceHybridAnimationData>(true);
            init.rigs = state.GetBufferLookup<MeshInstanceRig>(true);
            init.animationRoots = state.GetComponentLookup<HybridAnimationRoot>();
            init.animationObjects = state.GetBufferLookup<HybridAnimationObject>();
            var jobHandle = init.Schedule(entityArray.Length, InnerloopBatchCount, state.Dependency);

            SetObjectsEx setObjects;
            setObjects.entityType = state.GetEntityTypeHandle();
            setObjects.instanceType = state.GetComponentTypeHandle<MeshInstanceHybridAnimationObjectData>(true);
            setObjects.entityParentType = state.GetComponentTypeHandle<EntityParent>(true);
            setObjects.animationObjects = state.GetBufferLookup<HybridAnimationObject>();
            state.Dependency = setObjects.Schedule(__animationGroup, jobHandle);
        }
    }

    [UpdateInGroup(typeof(MeshInstanceSystemGroup), OrderLast = true)]
    public partial struct MeshInstanceHybridAnimationClearSystem : ISystem
    {
        private struct Destroy
        {
            [ReadOnly]
            public NativeArray<MeshInstanceHybridAnimationObjectInfo> infos;

            public BufferLookup<HybridAnimationObject> targets;

            public void Execute(int index)
            {
                var info = infos[index];
                if (!this.targets.HasBuffer(info.parentEntity))
                    return;

                HybridAnimationObject target;
                target.entity = Entity.Null;

                var targets = this.targets[info.parentEntity];
                targets[info.parentChildIndex] = target;
            }
        }

        [BurstCompile]
        private struct DestroyEx : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceHybridAnimationObjectInfo> infoType;

            public BufferLookup<HybridAnimationObject> targets;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Destroy destroy;
                destroy.infos = chunk.GetNativeArray(ref infoType);
                destroy.targets = targets;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    destroy.Execute(i);
            }
        }

        [BurstCompile]
        private struct Create : IJobParallelFor
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public ComponentLookup<EntityParent> entityParents;

            [ReadOnly]
            public ComponentLookup<MeshInstanceHybridAnimationObjectData> instances;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<MeshInstanceHybridAnimationObjectInfo> infos;

            public void Execute(int index)
            {
                Entity entity = entityArray[index];

                var instance = instances[entity];
                MeshInstanceHybridAnimationObjectInfo info;
                info.parentChildIndex = instance.index;
                info.parentEntity = entityParents.HasComponent(entity) ? entityParents[entity].entity : entity;
                infos[entity] = info;
            }
        }

        public static readonly int InnerloopBatchCount = 1;

        private EntityQuery __groupToDestroy;
        private EntityQuery __groupToCreate;

        public void OnCreate(ref SystemState state)
        {
            __groupToDestroy = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceHybridAnimationObjectInfo>()
                    },
                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceHybridAnimationObjectData)
                    },
                    //Options = EntityQueryOptions.IncludeDisabled
                }/*,
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceHybridAnimationObjectInfo>(),
                        ComponentType.ReadOnly<MeshInstanceHybridAnimationObjectData>(),
                    },

                    Any = new ComponentType[]
                    {
                        typeof(MeshInstanceHybridAnimationDisabled)
                    },

                    Options = EntityQueryOptions.IncludeDisabled
                }*/);

            __groupToCreate = state.GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<MeshInstanceHybridAnimationObjectData>()
                },
                None = new ComponentType[]
                {
                    typeof(MeshInstanceHybridAnimationObjectInfo),
                    //typeof(MeshInstanceHybridAnimationDisabled)
                },
                //Options = EntityQueryOptions.IncludeDisabled
            });
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        public void OnUpdate(ref SystemState state)
        {
            var entityManager = state.EntityManager;
            state.CompleteDependency();

            DestroyEx destroy;
            destroy.infoType = state.GetComponentTypeHandle<MeshInstanceHybridAnimationObjectInfo>(true);
            destroy.targets = state.GetBufferLookup<HybridAnimationObject>();
            destroy.Run(__groupToDestroy);

            entityManager.RemoveComponent<MeshInstanceHybridAnimationObjectInfo>(__groupToDestroy);

            var entityArray = __groupToCreate.ToEntityArray(Allocator.TempJob);
            entityManager.AddComponent<MeshInstanceHybridAnimationObjectInfo>(__groupToCreate);

            Create create;
            create.entityArray = entityArray;
            create.entityParents = state.GetComponentLookup<EntityParent>(true);
            create.instances = state.GetComponentLookup<MeshInstanceHybridAnimationObjectData>(true);
            create.infos = state.GetComponentLookup<MeshInstanceHybridAnimationObjectInfo>();

            state.Dependency = create.Schedule(entityArray.Length, InnerloopBatchCount, state.Dependency);
        }
    }
}
