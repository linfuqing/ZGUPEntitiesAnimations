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

    public struct MeshInstanceHybridAnimationObjectData : IComponentData
    {
        public int index;
    }

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
            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Reader rigPrefabs;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<HybridAnimationDefinition>>.Reader animationDefinitions;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public ComponentLookup<MeshInstanceHybridAnimationData> instances;

            [ReadOnly]
            public ComponentLookup<MeshInstanceRigID> rigIDs;

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

        private ComponentTypeHandle<MeshInstanceHybridAnimationData> __instanceType;

        private ComponentTypeHandle<MeshInstanceRigID> __rigIDType;

        private ComponentLookup<MeshInstanceHybridAnimationData> __instances;

        private ComponentLookup<MeshInstanceRigID> __rigIDs;

        private ComponentLookup<HybridAnimationData> __animations;

        private ComponentLookup<HybridAnimationRoot> __animationRoots;

        private SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>> __rigPrefabs;
        private SingletonAssetContainer<BlobAssetReference<HybridAnimationDefinition>> __animationDefinitions;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            BurstUtility.InitializeJobParallelFor<Init>();

            __animationComponentTypes = new ComponentTypeSet(
                ComponentType.ReadOnly<HybridAnimationData>(),
                ComponentType.ReadOnly<HybridAnimationRoot>(),
                ComponentType.ReadWrite<OldAnimatedData>());

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToDestroy = builder
                        .WithAll<MeshInstanceHybridAnimationID>()
                        .WithNone<MeshInstanceHybridAnimationData>()
                        .AddAdditionalQuery()
                        .WithAll<MeshInstanceHybridAnimationID, MeshInstanceHybridAnimationData>()
                        .WithAny<MeshInstanceHybridAnimationDisabled>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToCreate = builder
                    .WithAll<MeshInstanceHybridAnimationData, MeshInstanceRigID>()
                    .WithNone<MeshInstanceHybridAnimationID>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            __instanceType = state.GetComponentTypeHandle<MeshInstanceHybridAnimationData>(true);
            __rigIDType = state.GetComponentTypeHandle<MeshInstanceRigID>(true);

            __instances = state.GetComponentLookup<MeshInstanceHybridAnimationData>(true);
            __rigIDs = state.GetComponentLookup<MeshInstanceRigID>(true);
            __animations = state.GetComponentLookup<HybridAnimationData>();
            __animationRoots = state.GetComponentLookup<HybridAnimationRoot>();

            __rigPrefabs = state.WorldUnmanaged.GetExistingSystemUnmanaged<MeshInstanceRigFactorySystem>().prefabs;

            __animationDefinitions = SingletonAssetContainer<BlobAssetReference<HybridAnimationDefinition>>.Retain();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __animationDefinitions.Release();
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
                collect.instanceType = __instanceType.UpdateAsRef(ref state);
                collect.rigIDType = __rigIDType.UpdateAsRef(ref state);
                collect.rigPrefabs = __rigPrefabs.reader;
                collect.entities = entities;

                collect.RunByRef(__groupToCreate);

                int numEntities = entities.Length;
                for (int i = 0; i < numEntities; ++i)
                    entityManager.AddComponent(entities[i], __animationComponentTypes);
            }

            var entityArray = __groupToCreate.ToEntityArrayBurstCompatible(state.GetEntityTypeHandle(), Allocator.TempJob);
            entityManager.AddComponent<MeshInstanceHybridAnimationID>(__groupToCreate);

            Init init;
            init.rigPrefabs = __rigPrefabs.reader;
            init.animationDefinitions = __animationDefinitions.reader;
            init.entityArray = entityArray;
            init.instances = __instances.UpdateAsRef(ref state);
            init.rigIDs = __rigIDs.UpdateAsRef(ref state);
            init.animations = __animations.UpdateAsRef(ref state);
            init.animationRoots = __animationRoots.UpdateAsRef(ref state);

            var jobHandle = init.ScheduleByRef(entityArray.Length, InnerloopBatchCount, state.Dependency);

            __animationDefinitions.AddDependency(state.GetSystemID(), jobHandle);

            state.Dependency = jobHandle;
        }
    }

    [BurstCompile, UpdateInGroup(typeof(MeshInstanceSystemGroup)), UpdateAfter(typeof(MeshInstanceRigSystem))]
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
            public BufferAccessor<EntityParent> entityParents;

            public BufferLookup<HybridAnimationObject> animationObjects;

            public void Execute(int index)
            {
                HybridAnimationObject animationObject;
                animationObject.entity = entityArray[index];

                Entity entity = index < entityParents.Length ? EntityParent.Get(entityParents[index], animationObjects) : animationObject.entity;
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
            public BufferTypeHandle<EntityParent> entityParentType;

            public BufferLookup<HybridAnimationObject> animationObjects;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                SetObjects setObjects;
                setObjects.entityArray = chunk.GetNativeArray(entityType);
                setObjects.instances = chunk.GetNativeArray(ref instanceType);
                setObjects.entityParents = chunk.GetBufferAccessor(ref entityParentType);
                setObjects.animationObjects = animationObjects;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    setObjects.Execute(i);
            }
        }

        public static readonly int InnerloopBatchCount = 1;

        private EntityQuery __rootGroup;
        private EntityQuery __animationGroup;

        private EntityTypeHandle __entityType;

        private ComponentTypeHandle<MeshInstanceHybridAnimationObjectData> __instanceType;

        private BufferTypeHandle<EntityParent> __entityParentType;

        private ComponentLookup<MeshInstanceHybridAnimationData> __instances;

        private BufferLookup<MeshInstanceRig> __rigs;

        private ComponentLookup<HybridAnimationRoot> __animationRoots;

        private BufferLookup<HybridAnimationObject> __animationObjects;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __rootGroup = builder
                    .WithAll<MeshInstanceHybridAnimationData, MeshInstanceHybridAnimationID, MeshInstanceRig>()
                    .WithNone<HybridAnimationObject>()
                    .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __animationGroup = builder
                    .WithAll<MeshInstanceHybridAnimationObjectData>()
                    .WithNone<MeshInstanceHybridAnimationObjectInfo>()
                    .Build(ref state);

            __entityType = state.GetEntityTypeHandle();
            __instanceType = state.GetComponentTypeHandle<MeshInstanceHybridAnimationObjectData>(true);
            __entityParentType = state.GetBufferTypeHandle<EntityParent>(true);

            __instances = state.GetComponentLookup<MeshInstanceHybridAnimationData>(true);
            __rigs = state.GetBufferLookup<MeshInstanceRig>(true);
            __animationRoots = state.GetComponentLookup<HybridAnimationRoot>();
            __animationObjects = state.GetBufferLookup<HybridAnimationObject>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var entityArray = __rootGroup.ToEntityArray(Allocator.TempJob);

            state.EntityManager.AddComponent<HybridAnimationObject>(__rootGroup);

            var animationObjects = __animationObjects.UpdateAsRef(ref state);

            Init init;
            init.entityArray = entityArray;
            init.instances = __instances.UpdateAsRef(ref state);
            init.rigs = __rigs.UpdateAsRef(ref state);
            init.animationRoots = __animationRoots.UpdateAsRef(ref state);
            init.animationObjects = animationObjects;
            var jobHandle = init.ScheduleByRef(entityArray.Length, InnerloopBatchCount, state.Dependency);

            SetObjectsEx setObjects;
            setObjects.entityType = __entityType.UpdateAsRef(ref state);
            setObjects.instanceType = __instanceType.UpdateAsRef(ref state);
            setObjects.entityParentType = __entityParentType.UpdateAsRef(ref state);
            setObjects.animationObjects = animationObjects;
            state.Dependency = setObjects.ScheduleByRef(__animationGroup, jobHandle);
        }
    }

    [BurstCompile, UpdateInGroup(typeof(MeshInstanceSystemGroup), OrderLast = true)]
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
            public BufferLookup<EntityParent> entityParents;

            [ReadOnly]
            public ComponentLookup<MeshInstanceRigData> rigs;

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
                info.parentEntity = EntityParent.Get(entity, entityParents, rigs);
                infos[entity] = info;
            }
        }

        public static readonly int InnerloopBatchCount = 1;

        private EntityQuery __groupToDestroy;
        private EntityQuery __groupToCreate;

        private ComponentTypeHandle<MeshInstanceHybridAnimationObjectInfo> __infoType;

        private BufferLookup<HybridAnimationObject> __targets;

        private BufferLookup<EntityParent> __entityParents;

        private ComponentLookup<MeshInstanceRigData> __rigs;

        private ComponentLookup<MeshInstanceHybridAnimationObjectData> __instances;

        private ComponentLookup<MeshInstanceHybridAnimationObjectInfo> __infos;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToDestroy = builder
                    .WithAll<MeshInstanceHybridAnimationObjectInfo>()
                    .WithNone<MeshInstanceHybridAnimationObjectData>()
                    .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToCreate = builder
                    .WithAll<MeshInstanceHybridAnimationObjectData>()
                    .WithNone<MeshInstanceHybridAnimationObjectInfo>()
                    .Build(ref state);

            __infoType = state.GetComponentTypeHandle<MeshInstanceHybridAnimationObjectInfo>(true);
            __targets = state.GetBufferLookup<HybridAnimationObject>();

            __entityParents = state.GetBufferLookup<EntityParent>(true);
            __rigs = state.GetComponentLookup<MeshInstanceRigData>(true);
            __instances = state.GetComponentLookup<MeshInstanceHybridAnimationObjectData>(true);
            __infos = state.GetComponentLookup<MeshInstanceHybridAnimationObjectInfo>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var entityManager = state.EntityManager;
            state.CompleteDependency();

            DestroyEx destroy;
            destroy.infoType = state.GetComponentTypeHandle<MeshInstanceHybridAnimationObjectInfo>(true);
            destroy.targets = state.GetBufferLookup<HybridAnimationObject>();
            destroy.RunByRef(__groupToDestroy);

            entityManager.RemoveComponent<MeshInstanceHybridAnimationObjectInfo>(__groupToDestroy);

            var entityArray = __groupToCreate.ToEntityArray(Allocator.TempJob);
            entityManager.AddComponent<MeshInstanceHybridAnimationObjectInfo>(__groupToCreate);

            Create create;
            create.entityArray = entityArray;
            create.entityParents = __entityParents.UpdateAsRef(ref state);
            create.rigs = __rigs.UpdateAsRef(ref state);
            create.instances = __instances.UpdateAsRef(ref state);
            create.infos = __infos.UpdateAsRef(ref state);

            state.Dependency = create.ScheduleByRef(entityArray.Length, InnerloopBatchCount, state.Dependency);
        }
    }
}
