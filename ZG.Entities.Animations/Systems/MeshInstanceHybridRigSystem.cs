using Unity.Collections;
using Unity.Entities;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Animation;
using UnityEngine;

namespace ZG
{
    public struct MeshInstanceHybridRigDefinition
    {
        public struct Node
        {
            public HybridRigNodeTransformType transformType;

            public StringHash boneID;
            public BlobString transformPath;
        }

        public struct Rig
        {
            public int index;
            public BlobArray<Node> nodes;
        }

        public BlobArray<Rig> rigs;
    }

    public struct MeshInstanceHybridRigData : IComponentData
    {
        public FixedString128Bytes transformPathPrefix;
        public BlobAssetReference<MeshInstanceHybridRigDefinition> definition;
    }

    public struct MeshInstanceHybridRigNode : ICleanupBufferElementData
    {
        public Entity entity;
    }

    [UpdateInGroup(typeof(MeshInstanceSystemGroup)), UpdateAfter(typeof(MeshInstanceRigSystem))]
    public partial struct MeshInstanceHybridRigSystem : ISystem
    {
        private struct Result
        {
            public int nodeOffset;
            public int nodeCount;
            public Entity entity;
            public FixedString128Bytes transformPathPrefix;
            public BlobAssetReference<MeshInstanceHybridRigDefinition> definition;
            public EntityObject<Transform> root;
        }

        private struct CollectToDestroy
        {
            [ReadOnly]
            public BufferAccessor<MeshInstanceHybridRigNode> nodes;

            /*[ReadOnly]
            public ComponentLookup<EntityObject<Transform>> transforms;*/

            public NativeList<Entity> entities;

            public void Exeucte(int index)
            {
                var entityArray = nodes[index].Reinterpret<Entity>().AsNativeArray();

                entities.AddRange(entityArray);

                /*Entity entity;
                int length = entityArray.Length;
                for(int i = 0; i < length; ++i)
                {
                    entity = entityArray[i];
                    if(transforms.HasComponent(entity))
                        results.Add(transforms[entity]);
                }*/
            }
        }

        [BurstCompile]
        private struct CollectToDestroyEx : IJobChunk
        {
            [ReadOnly]
            public BufferTypeHandle<MeshInstanceHybridRigNode> nodeType;

            /*[ReadOnly]
            public ComponentLookup<EntityObject<Transform>> transforms;

            public NativeList<EntityObject<Transform>> results;*/

            public NativeList<Entity> entities;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                CollectToDestroy collectToDestroy;
                collectToDestroy.nodes = chunk.GetBufferAccessor(ref nodeType);
                /*collectToDestroy.transforms = transforms;
                collectToDestroy.results = results;*/
                collectToDestroy.entities = entities;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    collectToDestroy.Exeucte(i);
            }
        }

        private struct CollectToCreate
        {
            [ReadOnly]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeArray<MeshInstanceHybridRigData> instances;

            [ReadOnly]
            public NativeArray<EntityObject<Transform>> transforms;

            public NativeArray<int> counter;

            public Result Execute(int index)
            {
                var instance = instances[index];

                Result result;
                result.entity = entityArray[index];
                result.root = transforms[index];
                result.transformPathPrefix = instance.transformPathPrefix;
                result.definition = instance.definition;
                ref var definition = ref result.definition.Value;

                result.nodeOffset = counter[0];
                result.nodeCount = 0;

                int numRigs = definition.rigs.Length;
                for(int i = 0; i < numRigs; ++i)
                {
                    ref var rig = ref definition.rigs[i];

                    result.nodeCount += rig.nodes.Length;
                }

                counter[0] = result.nodeOffset + result.nodeCount;

                return result;
            }
        }

        [BurstCompile]
        private struct CollectToCreateEx : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle entityType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceHybridRigData> instanceType;

            [ReadOnly]
            public ComponentTypeHandle<EntityObject<Transform>> transformType;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<int> baseEntityIndexArray;

            public NativeArray<int> counter;

            public NativeArray<Result> results;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                CollectToCreate collectToCreate;
                collectToCreate.entityArray = chunk.GetNativeArray(entityType);
                collectToCreate.transforms = chunk.GetNativeArray(ref transformType);
                collectToCreate.instances = chunk.GetNativeArray(ref instanceType);
                collectToCreate.counter = counter;

                int index = baseEntityIndexArray[unfilteredChunkIndex];
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    results[index++] = collectToCreate.Execute(i);
            }
        }

        [BurstCompile]
        private struct Init : IJobParallelFor
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> entityArray;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<EntityObject<Transform>> transforms;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Result> results;

            [ReadOnly]
            public ComponentLookup<Rig> rigMap;

            [ReadOnly]
            public BufferLookup<MeshInstanceRig> rigs;

            [NativeDisableParallelForRestriction]
            public BufferLookup<MeshInstanceHybridRigNode> nodes;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<EntityObject<Transform>> transformMap;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<HybridRigNode> instances;

            public void Execute(int index)
            {
                var result = results[index];

                ref var definition = ref result.definition.Value;

                var rigs = this.rigs[result.entity];
                HybridRigNode instance;
                Entity entity;
                int numRigs = definition.rigs.Length, numNodes, nodeOffset = result.nodeOffset, i, j;
                for(i = 0; i < numRigs; ++i)
                {
                    ref var rig = ref definition.rigs[i];

                    instance.rigEntity = rigs[rig.index].entity;

                    ref var boneIDs = ref rigMap[instance.rigEntity].Value.Value.Skeleton.Ids;

                    numNodes = rig.nodes.Length;
                    for (j = 0; j < numNodes; ++j)
                    {
                        ref var node = ref rig.nodes[j];

                        entity = entityArray[nodeOffset];

                        transformMap[entity] = transforms[nodeOffset];

                        instance.transformType = node.transformType;
                        instance.boneIndex = Core.FindBindingIndex(ref boneIDs, node.boneID);
                        if(instance.boneIndex == -1)
                            UnityEngine.Debug.LogError($"Bone ID:{node.boneID.Id} can not been found.");

                        instances[entity] = instance;

                        ++nodeOffset;
                    }
                }

                nodes[result.entity].Reinterpret<Entity>().AddRange(entityArray.GetSubArray(result.nodeOffset, result.nodeCount));
            }
        }

        private struct CollectTransforms : IFunctionWrapper
        {
            public NativeArray<Result> results;
            public NativeArray<EntityObject<Transform>> transforms;
            
            public void Invoke()
            {
                Transform root, temp;
                EntityObject<Transform> transform;
                Result result;
                string transformPath;
                int numResults = results.Length, numRigs, numNodes, nodeIndex, i, j, k;
                for(i = 0; i < numResults; ++i)
                {
                    result = results[i];
                    ref var definition = ref result.definition.Value;

                    nodeIndex = result.nodeOffset;

                    numRigs = definition.rigs.Length;
                    for (j = 0; j < numRigs; ++j)
                    {
                        ref var rig = ref result.definition.Value.rigs[j];

                        root = result.root.value;

                        numNodes = rig.nodes.Length;
                        for (k = 0; k < numNodes; ++k)
                        {
                            ref var node = ref rig.nodes[k];

                            transformPath = result.transformPathPrefix + node.transformPath.ToString();
                            temp = root.Find(transformPath);

                            if (temp == null)
                            {
                                UnityEngine.Debug.LogError($"{root.root} transform Path: {transformPath} can not been found.", root.root);

                                transform = EntityObject<Transform>.Null;
                            }
                            else
                            {
                                transform = new EntityObject<Transform>(temp);

                                //transform.Retain();
                            }

                            transforms[nodeIndex++] = transform;
                        }
                    }
                }
            }
        }

        public static readonly int InnerloopBatchCount = 1;

        private EntityArchetype __entityArchetype;
        private EntityQuery __groupToDestroy;
        private EntityQuery __groupToCreate;
        
        private EntityTypeHandle __entityType;
        private ComponentTypeHandle<EntityObject<Transform>> __transformType;
        private ComponentTypeHandle<MeshInstanceHybridRigData> __instanceType;
        private BufferTypeHandle<MeshInstanceHybridRigNode> __nodeType;

        private BufferLookup<MeshInstanceHybridRigNode> __nodes;

        private BufferLookup<MeshInstanceRig> __instanceRigs;

        private ComponentLookup<Rig> __rigs;
        private ComponentLookup<HybridRigNode> __instances;

        private ComponentLookup<EntityObject<Transform>> __transforms;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using(var componentTypes = new NativeList<ComponentType>(Allocator.Temp)
                  {
                      TransformAccessArrayEx.componentType, 
                      ComponentType.ReadOnly<EntityObjects>(), 
                      ComponentType.ReadOnly<HybridRigNode>()
                  })
                __entityArchetype = state.EntityManager.CreateArchetype(componentTypes.AsArray());

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToDestroy = builder
                    .WithAll<MeshInstanceHybridRigNode>()
                    .WithNone<MeshInstanceHybridRigData>()
                    .AddAdditionalQuery()
                    .WithAll<MeshInstanceHybridRigNode, MeshInstanceHybridRigData>()
                    .WithNone<MeshInstanceRig>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);
            
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToCreate = builder
                    .WithAll<EntityObject<Transform>, MeshInstanceHybridRigData, MeshInstanceRig>()
                    .WithNone<MeshInstanceHybridRigNode>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            __entityType = state.GetEntityTypeHandle();
            __transformType = state.GetComponentTypeHandle<EntityObject<Transform>>(true);
            __instanceType = state.GetComponentTypeHandle<MeshInstanceHybridRigData>(true);
            __nodeType = state.GetBufferTypeHandle<MeshInstanceHybridRigNode>(true);
            
            __nodes = state.GetBufferLookup<MeshInstanceHybridRigNode>();
            __instanceRigs = state.GetBufferLookup<MeshInstanceRig>(true);
            __rigs = state.GetComponentLookup<Rig>(true);
            __instances = state.GetComponentLookup<HybridRigNode>();
            __transforms = state.GetComponentLookup<EntityObject<Transform>>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.CompleteDependency();

            var entityManager = state.EntityManager;
            if (!__groupToDestroy.IsEmptyIgnoreFilter)
            {
                using (var entities = new NativeList<Entity>(Allocator.TempJob))
                {
                    CollectToDestroyEx collect;
                    collect.nodeType = __nodeType.UpdateAsRef(ref state);
                    //collect.transforms = GetComponentLookup<EntityObject<Transform>>(true);
                    collect.entities = entities;

                    collect.RunByRef(__groupToDestroy);

                    entityManager.RemoveComponent<MeshInstanceHybridRigNode>(__groupToDestroy);

                    entityManager.DestroyEntity(entities.AsArray());
                }
            }

            int entityCount = __groupToCreate.CalculateEntityCount();
            if (entityCount > 0)
            {
                var results = new NativeArray<Result>(entityCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                {
                    int count;
                    using (var counter = new NativeArray<int>(1, Allocator.TempJob))
                    {
                        CollectToCreateEx collect;
                        collect.entityType = __entityType.UpdateAsRef(ref state);
                        collect.transformType = __transformType.UpdateAsRef( ref state);
                        collect.instanceType = __instanceType.UpdateAsRef(ref state);
                        collect.baseEntityIndexArray = __groupToCreate.CalculateBaseEntityIndexArray(Allocator.TempJob);
                        collect.counter = counter;
                        collect.results = results;
                        collect.RunByRef(__groupToCreate);

                        count = counter[0];
                    }

                    entityManager.AddComponent<MeshInstanceHybridRigNode>(__groupToCreate);

                    var entityArray = entityManager.CreateEntity(__entityArchetype, count, Allocator.TempJob);
                    var transforms = new NativeArray<EntityObject<Transform>>(count, Allocator.TempJob, NativeArrayOptions.ClearMemory);

                    CollectTransforms collectTransforms;
                    collectTransforms.results = results;
                    collectTransforms.transforms = transforms;
                    collectTransforms.Run();

                    Init init;
                    init.entityArray = entityArray;
                    init.transforms = transforms;
                    init.results = results;
                    init.rigMap = __rigs.UpdateAsRef(ref state);
                    init.rigs = __instanceRigs.UpdateAsRef(ref state);
                    init.nodes = __nodes.UpdateAsRef(ref state);
                    init.transformMap = __transforms.UpdateAsRef(ref state);
                    init.instances = __instances.UpdateAsRef(ref state);

                    state.Dependency = init.ScheduleByRef(results.Length, InnerloopBatchCount, state.Dependency);
                }
            }
        }
    }
}