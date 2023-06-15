using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Animation;

namespace ZG
{
    public struct MeshInstanceRigPrefab
    {
        public struct Rig
        {
            public int hash;
            public Entity entity;
        }

        public int instanceCount;
        public BlobArray<Rig> rigs;
        public BlobArray<Entity> nodes;
    }

    public struct MeshInstanceRigDefinition
    {
        /*public struct Renderer
        {
            public int nodeIndex;

            public float4x4 offset;
        }*/

        public struct Node
        {
            public BlobArray<int> typeIndices;
        }

        public struct Rig
        {
            public struct Node
            {
                public int index;

                public int nodeIndex;
            }

            public int index;
            public int componentTypesIndex;
            public float4x4 transform;
            public quaternion rootRotation;
            public float3 rootTranslation;
            public float3 rootScale;
            public BlobArray<Node> nodes;
        }

        public int instanceID;
        public BlobArray<Rig> rigs;
        public BlobArray<Node> nodes;
    }

    public struct MeshInstanceRigData : IComponentData
    {
        public BlobAssetReference<MeshInstanceRigDefinition> definition;
    }

    public struct MeshInstanceRigID : ICleanupComponentData
    {
        public int value;
    }

    [BurstCompile]
    public static class MeshInstanceRigUtility
    {
        private struct TransformHandle : ITransformHandle
        {
            public Entity Entity { get; set; }

            public int Index { get; set; }
        }

        private struct ResultEx
        {
            public int offset;
            public ComponentTypeSet componentTypes;
        }

        public struct Result
        {
            public int rigOffset;
            public int nodeOffset;
            public Entity entity;
            public BlobAssetReference<MeshInstanceRigPrefab> prefab;
        }

        private struct Collect
        {
            [ReadOnly]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeArray<MeshInstanceRendererID> rendererIDs;

            [ReadOnly]
            public NativeArray<MeshInstanceRigData> instances;

            [ReadOnly]
            public SingletonAssetContainer<ComponentTypeSet>.Reader componentTypes;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions;

            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Writer prefabs;

            public UnsafeListEx<Result> results;

            public NativeList<ResultEx> resultsEx;

            public NativeArray<int> counters;

            public int Execute(int index)
            {
                ref var definition = ref instances[index].definition.Value;
                if (prefabs.TryGetValue(definition.instanceID, out var prefab))
                    ++prefab.Value.instanceCount;
                else
                {
                    using (var blobBuilder = new BlobBuilder(Allocator.Temp))
                    {
                        ref var root = ref blobBuilder.ConstructRoot<MeshInstanceRigPrefab>();
                        root.instanceCount = 1;

                        int numRigs = definition.rigs.Length;

                        var rigs = blobBuilder.Allocate(ref root.rigs, numRigs);

                        SingletonAssetContainerHandle handle;
                        handle.instanceID = definition.instanceID;

                        int rigOffset = counters[0], i;
                        ResultEx resultEx;
                        for (i = 0; i < numRigs; ++i)
                        {
                            ref var sourceRig = ref definition.rigs[i];
                            ref var destinationRig = ref rigs[i];

                            handle.index = sourceRig.index;

                            destinationRig.hash = rigDefinitions[handle].Value.GetHashCode();

                            if (sourceRig.componentTypesIndex != -1)
                            {
                                handle.index = sourceRig.componentTypesIndex;

                                resultEx.offset = rigOffset + i;
                                resultEx.componentTypes = componentTypes[handle];

                                resultsEx.Add(resultEx);
                            }
                        }

                        int numNodes = definition.nodes.Length;

                        blobBuilder.Allocate(ref root.nodes, numNodes);

                        Result result;
                        result.rigOffset = rigOffset;
                        result.nodeOffset = counters[1];
                        result.entity = entityArray[index];
                        result.prefab = blobBuilder.CreateBlobAssetReference<MeshInstanceRigPrefab>(Allocator.Persistent);

                        counters[0] = result.rigOffset + numRigs;
                        counters[1] = result.nodeOffset + numNodes;

                        prefabs[definition.instanceID] = result.prefab;

                        results.Add(result);
                    }
                }

                return definition.instanceID;
            }
        }

        [BurstCompile]
        private struct CollectEx : IJobChunk
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<int> baseEntityIndexArray;

            [ReadOnly]
            public EntityTypeHandle entityType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceRendererID> rendererIDType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceRigData> instanceType;

            [ReadOnly]
            public SingletonAssetContainer<ComponentTypeSet>.Reader componentTypes;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader rigDefinitions;

            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Writer prefabs;

            public UnsafeListEx<Result> results;

            public NativeList<ResultEx> resultsEx;

            public NativeArray<MeshInstanceRigID> ids;

            public NativeArray<int> counters;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                MeshInstanceRigID id;

                Collect collect;
                collect.entityArray = chunk.GetNativeArray(entityType);
                collect.rendererIDs = chunk.GetNativeArray(ref rendererIDType);
                collect.instances = chunk.GetNativeArray(ref instanceType);
                collect.componentTypes = componentTypes;
                collect.rigDefinitions = rigDefinitions;
                collect.prefabs = prefabs;
                collect.results = results;
                collect.resultsEx = resultsEx;
                collect.counters = counters;

                int index = baseEntityIndexArray[unfilteredChunkIndex];
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    id.value = collect.Execute(i);
                    ids[index++] = id;
                }
            }
        }

        private struct Release
        {
            [ReadOnly]
            public NativeArray<MeshInstanceRigID> ids;

            //[ReadOnly]
            //public ComponentLookup<RigRootEntity> rigRootEntities;

            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Writer prefabs;

            public NativeList<Entity> results;

            public void Execute(int index)
            {
                int id = ids[index].value;
                var prefabAsset = prefabs[id];
                ref var prefab = ref prefabAsset.Value;
                if (--prefab.instanceCount > 0)
                    return;

                int numRigs = prefab.rigs.Length;
                for (int i = 0; i < numRigs; ++i)
                {
                    ref var rig = ref prefab.rigs[i];

                    //results.Add(rigRootEntities[rig.entity].Value);
                    results.Add(rig.entity);
                }

                int numNodes = prefab.nodes.Length;
                for (int i = 0; i < numNodes; ++i)
                    results.Add(prefab.nodes[i]);

                prefabAsset.Dispose();

                prefabs.Remove(id);
            }
        }

        [BurstCompile]
        private struct ReleaseEx : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceRigID> idType;

            //[ReadOnly]
            //public ComponentLookup<RigRootEntity> rigRootEntities;

            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Writer prefabs;

            public NativeList<Entity> results;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Release release;
                release.ids = chunk.GetNativeArray(ref idType);
                //release.rigRootEntities = rigRootEntities;
                release.prefabs = prefabs;
                release.results = results;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while(iterator.NextEntityIndex(out int i))
                    release.Execute(i);
            }
        }

        [BurstCompile]
        private struct InitPrefabs : IJobParallelFor
        {
            [ReadOnly]
            public UnsafeListEx<Result> results;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> rigs;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> nodes;

            [ReadOnly]
            public ComponentLookup<MeshInstanceRigData> instances;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<RigRootEntity> rigRootEntities;

            public void Execute(int index)
            {
                var result = results[index];

                ref var definition = ref instances[result.entity].definition.Value;

                ref var prefab = ref result.prefab.Value;

                RigRootEntity rigRootEntity;
                int numRigs = prefab.rigs.Length;
                for (int i = 0; i < numRigs; ++i)
                {
                    ref var prefabRig = ref prefab.rigs[i];

                    prefabRig.entity = rigs[result.rigOffset + i];

                    ref var rig = ref definition.rigs[i];

                    rigRootEntity.Value = prefabRig.entity;
                    rigRootEntity.RemapToRootMatrix = new AffineTransform(rig.rootTranslation, rig.rootRotation, rig.rootScale);
                    rigRootEntities[prefabRig.entity] = rigRootEntity;
                }

                int numNodes = prefab.nodes.Length;
                for (int i = 0; i < numNodes; ++i)
                    prefab.nodes[i] = nodes[result.nodeOffset + i];
            }
        }

        [BurstCompile]
        private struct Init : IJobParallelFor
        {
            [ReadOnly]
            public UnsafeListEx<Result> results;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<RigDefinition>>.Reader container;

            [ReadOnly]
            public ComponentLookup<RigRootEntity> rigRootEntities;

            [ReadOnly]
            public ComponentLookup<MeshInstanceRigData> instances;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<Translation> translations;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<Rotation> rotations;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<NonUniformScale> scales;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<LocalToWorld> localToWorlds;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<Rig> rigs;

            [NativeDisableParallelForRestriction]
            public BufferLookup<AnimatedData> animations;
            [NativeDisableParallelForRestriction]
            public BufferLookup<AnimatedLocalToWorld> animatedLocalToWorlds;

            public void Execute(int index)
            {
                var result = results[index];
                ref var definition = ref instances[result.entity].definition.Value;
                ref var prefab = ref result.prefab.Value;

                SingletonAssetContainerHandle handle;
                handle.instanceID = definition.instanceID;

                RigEntityBuilder.RigBuffers rigBuffers;
                Rig rig;
                Translation translation;
                NonUniformScale scale;
                Rotation rotation;
                LocalToWorld localToWorld;
                Entity rigRootEntity;
                int numRigs = definition.rigs.Length;
                for (int i = 0; i < numRigs; ++i)
                {
                    ref var prefabRig = ref prefab.rigs[i];

                    rigRootEntity = rigRootEntities[prefabRig.entity].Value;

                    ref var temp = ref definition.rigs[i];

                    translation.Value = temp.rootTranslation;
                    translations[rigRootEntity] = translation;

                    rotation.Value = temp.rootRotation;
                    rotations[rigRootEntity] = rotation;

                    scale.Value = temp.rootScale;
                    scales[rigRootEntity] = scale;

                    localToWorld.Value = math.mul(temp.transform, float4x4.TRS(temp.rootTranslation, temp.rootRotation, temp.rootScale));
                    localToWorlds[rigRootEntity] = localToWorld;

                    handle.index = temp.index;

                    rig.Value = container[handle];
                    rigs[prefabRig.entity] = rig;

                    rigBuffers.Data = animations[prefabRig.entity];
                    rigBuffers.GlobalMatrices = animatedLocalToWorlds[prefabRig.entity];

                    rigBuffers.ResizeBuffers(rig.Value);
                    rigBuffers.InitializeBuffers(rig.Value);

                    /*var animationStream = AnimationStream.CreateReadOnly(rig.Value, rigBuffers.Data.AsNativeArray());

                    int length = rigBuffers.GlobalMatrices.Length;
                    for (int j = 0; j < length; ++j)
                    {
                        AnimatedLocalToWorld animatedLocalToWorld;
                        animatedLocalToWorld.Value = animationStream.GetLocalToRootMatrix(j);

                        rigBuffers.GlobalMatrices[j] = animatedLocalToWorld;
                    }*/
                }
            }
        }

        /*public delegate void InitDelegate(
            int innerloopBatchCount,
            in UnsafeListEx<Result> results,
            in SingletonAssetContainer<BlobAssetReference<RigDefinition>> container,
            ref SystemState systemState);

        public delegate void CreateDelegate(
            in EntityArchetype rigEntityArchetype,
            in EntityArchetype nodeEntityArchetype,
            in EntityQuery group,
            in SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>> prefabs,
            in SingletonAssetContainer<ComponentTypeSet> componentTypes,
            in SingletonAssetContainer<BlobAssetReference<RigDefinition>> rigDefinitions,
            ref UnsafeListEx<Result> results,
            ref SystemState systemState);

        public delegate void DestroyDelegate(
            in EntityQuery group,
            ref SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>> prefabs,
            ref SystemState systemState);

        public static readonly InitDelegate InitFunction = BurstCompiler.CompileFunctionPointer<InitDelegate>(InitResults).Invoke;

        public static readonly DestroyDelegate DestroyFunction = BurstCompiler.CompileFunctionPointer<DestroyDelegate>(Destroy).Invoke;

        public static readonly CreateDelegate CreateFunction = BurstCompiler.CompileFunctionPointer<CreateDelegate>(Create).Invoke;*/

        public static ComponentType[] rigPrefabComponentTypes
        {
            get
            {
                BurstUtility.InitializeJobParallelFor<Init>();

                int numComponentTypes = RigEntityBuilder.RigPrefabComponentTypes.Length;
                var componentTypes = new ComponentType[numComponentTypes + 5];
                componentTypes[0] = ComponentType.ReadOnly<RigRootEntity>();
                componentTypes[1] = ComponentType.ReadOnly<LocalToWorld>();
                componentTypes[2] = ComponentType.ReadOnly<Translation>();
                componentTypes[3] = ComponentType.ReadOnly<NonUniformScale>();
                componentTypes[4] = ComponentType.ReadOnly<Rotation>();
                for (int i = 0; i < numComponentTypes; ++i)
                    componentTypes[i + 5] = RigEntityBuilder.RigPrefabComponentTypes.GetComponentType(i);

                return componentTypes;
            }
        }

        public static ComponentType[] nodePrefabComponentTypes
        {
            get
            {
                return new ComponentType[]
                {
                    ComponentType.ReadOnly<Prefab>(),
                    ComponentType.ReadOnly<LocalToWorld>(),
                    ComponentType.ReadOnly<AnimationLocalToWorldOverride>()
                };
            }
        }

        /*[BurstCompile]
        [AOT.MonoPInvokeCallback(typeof(DestroyDelegate))]*/
        public static void Destroy(
            in EntityQuery group,
            ref SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>> prefabs,
            ref SystemState systemState)
        {
            var entityManager = systemState.EntityManager;
            using (var entities = new NativeList<Entity>(Allocator.TempJob))
            {
                prefabs.lookupJobManager.CompleteReadWriteDependency();

                ReleaseEx release;
                release.idType = systemState.GetComponentTypeHandle<MeshInstanceRigID>(true);
                //release.rigRootEntities = systemState.GetComponentLookup<RigRootEntity>(true);
                release.prefabs = prefabs.writer;
                release.results = entities;

                systemState.CompleteDependency();

                release.Run(group);

                entityManager.DestroyEntity(entities.AsArray());
            }

            entityManager.RemoveComponent<MeshInstanceRigID>(group);
        }

        /*[BurstCompile]
        [AOT.MonoPInvokeCallback(typeof(CreateDelegate))]*/
        public static void Create(
            in EntityArchetype rigEntityArchetype,
            in EntityArchetype nodeEntityArchetype,
            in EntityQuery group,
            in SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>> prefabs,
            in SingletonAssetContainer<ComponentTypeSet> componentTypes,
            in SingletonAssetContainer<BlobAssetReference<RigDefinition>> rigDefinitions,
            ref UnsafeListEx<Result> results,
            ref SystemState systemState)
        {
            results.Clear();

            NativeArray<Entity> rigs, nodes;
            using (var counters = new NativeArray<int>(2, Allocator.TempJob, NativeArrayOptions.ClearMemory))
            using(var ids = new NativeArray<MeshInstanceRigID>(group.CalculateEntityCount(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory))
            using (var resultsEx = new NativeList<ResultEx>(Allocator.TempJob))
            {
                prefabs.lookupJobManager.CompleteReadWriteDependency();

                CollectEx collect;
                collect.baseEntityIndexArray = group.CalculateBaseEntityIndexArray(Allocator.TempJob);
                collect.entityType = systemState.GetEntityTypeHandle();
                collect.rendererIDType = systemState.GetComponentTypeHandle<MeshInstanceRendererID>(true);
                collect.instanceType = systemState.GetComponentTypeHandle<MeshInstanceRigData>(true);
                collect.componentTypes = componentTypes.reader;
                collect.rigDefinitions = rigDefinitions.reader;
                collect.prefabs = prefabs.writer;
                collect.results = results;
                collect.resultsEx = resultsEx;
                collect.ids = ids;
                collect.counters = counters;

                systemState.CompleteDependency();

                collect.Run(group);

                var entityManager = systemState.EntityManager;
                entityManager.AddComponentDataBurstCompatible(group, ids);

                rigs = entityManager.CreateEntity(rigEntityArchetype, counters[0], Allocator.Temp);

                int numResultsEx = resultsEx.Length;
                ResultEx resultEx;
                for (int i = 0; i < numResultsEx; ++i)
                {
                    resultEx = resultsEx[i];

                    entityManager.AddComponent(rigs[resultEx.offset], resultEx.componentTypes);
                }

                nodes = entityManager.CreateEntity(nodeEntityArchetype, counters[1], Allocator.Temp);
            }

            InitPrefabs initPrefabs;
            initPrefabs.results = results;
            initPrefabs.rigs = rigs;
            initPrefabs.nodes = nodes;
            initPrefabs.instances = systemState.GetComponentLookup<MeshInstanceRigData>(true);
            initPrefabs.rigRootEntities = systemState.GetComponentLookup<RigRootEntity>();

            int numResults = results.length;
            for (int i = 0; i < numResults; ++i)
                initPrefabs.Execute(i);

            rigs.Dispose();
            nodes.Dispose();

            InitSharedData(results, systemState.EntityManager);
        }

        public static void InitSharedData(
            in UnsafeListEx<Result> results,
            EntityManager entityManager)
        {
            int numResults = results.length, numRigs, i, j;
            Result result;
            SharedRigHash sharedRigHash;
            for (i = 0; i < numResults; ++i)
            {
                result = results[i];

                ref var prefab = ref result.prefab.Value;

                numRigs = prefab.rigs.Length;
                for (j = 0; j < numRigs; ++j)
                {
                    ref var rig = ref prefab.rigs[j];

                    sharedRigHash.Value = rig.hash;
                    entityManager.SetSharedComponent(rig.entity, sharedRigHash);
                }
            }
        }

        /*[BurstCompile]
        [AOT.MonoPInvokeCallback(typeof(InitDelegate))]*/
        public static void InitResults(
            int innerloopBatchCount, 
            in UnsafeListEx<Result> results,
            in SingletonAssetContainer<BlobAssetReference<RigDefinition>> container,
            ref SystemState systemState)
        {
            Init init;
            init.results = results;
            init.container = container.reader;
            init.rigRootEntities = systemState.GetComponentLookup<RigRootEntity>(true);
            init.instances = systemState.GetComponentLookup<MeshInstanceRigData>(true);
            init.translations = systemState.GetComponentLookup<Translation>();
            init.rotations = systemState.GetComponentLookup<Rotation>();
            init.scales = systemState.GetComponentLookup<NonUniformScale>();
            init.localToWorlds = systemState.GetComponentLookup<LocalToWorld>();
            init.rigs = systemState.GetComponentLookup<Rig>();
            init.animations = systemState.GetBufferLookup<AnimatedData>();
            init.animatedLocalToWorlds = systemState.GetBufferLookup<AnimatedLocalToWorld>();

            var jobHandle = init.Schedule(results.length, innerloopBatchCount, systemState.Dependency);

            container.AddDependency(systemState.GetSystemID(), jobHandle);

            systemState.Dependency = jobHandle;
        }

        public static void Execute(
            int innerloopBatchCount,
            in EntityArchetype rigEntityArchetype,
            in EntityArchetype nodeEntityArchetype,
            in EntityQuery group,
            in SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>> prefabs,
            in SingletonAssetContainer<ComponentTypeSet> componentTypes,
            in SingletonAssetContainer<BlobAssetReference<RigDefinition>> rigDefinitions,
            ref UnsafeListEx<Result> results,
            ref SystemState systemState)
        {
            Create(
                rigEntityArchetype,
                nodeEntityArchetype,
                group,
                prefabs,
                componentTypes,
                rigDefinitions, 
                ref results,
                ref systemState);

            //InitSharedData(results, systemState.EntityManager);

            InitResults(
                innerloopBatchCount, 
                results, 
                rigDefinitions, 
                ref systemState);
        }
    }
}