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
    public partial class MeshInstanceHybridRigSystem : SystemBase
    {
        public struct Result
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
            public NativeArray<EntityObject<Transform>> trasnforms;

            public NativeArray<int> counter;

            public Result Execute(int index)
            {
                var instance = instances[index];

                Result result;
                result.entity = entityArray[index];
                result.root = trasnforms[index];
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
            public ComponentTypeHandle<EntityObject<Transform>> trasnformType;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<int> baseEntityIndexArray;

            public NativeArray<int> counter;

            public NativeArray<Result> results;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                CollectToCreate collectToCreate;
                collectToCreate.entityArray = chunk.GetNativeArray(entityType);
                collectToCreate.trasnforms = chunk.GetNativeArray(ref trasnformType);
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

        public readonly int InnerloopBatchCount = 1;

        private EntityArchetype __entityArchetype;
        private EntityQuery __groupToDestroy;
        private EntityQuery __groupToCreate;

        protected override void OnCreate()
        {
            base.OnCreate();

            var componentType = TransformAccessArrayEx.componentType;
            __entityArchetype = EntityManager.CreateArchetype(componentType, ComponentType.ReadOnly<EntityObjects>(), ComponentType.ReadOnly<HybridRigNode>());

            __groupToDestroy = GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceHybridRigNode>()
                    },

                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceHybridRigData)
                    }
                },
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceHybridRigNode>(),
                        ComponentType.ReadOnly<MeshInstanceHybridRigData>(),
                    },

                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceRig)
                    },

                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __groupToCreate = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    componentType,
                    ComponentType.ReadOnly<MeshInstanceHybridRigData>(), 
                    ComponentType.ReadOnly<MeshInstanceRig>()
                },
                None = new ComponentType[]
                {
                    typeof(MeshInstanceHybridRigNode)
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });
        }

        protected override void OnUpdate()
        {
            CompleteDependency();

            var entityManager = EntityManager;
            if (!__groupToDestroy.IsEmptyIgnoreFilter)
            {
                using (var entities = new NativeList<Entity>(Allocator.TempJob))
                {
                    CollectToDestroyEx collect;
                    collect.nodeType = GetBufferTypeHandle<MeshInstanceHybridRigNode>(true);
                    //collect.transforms = GetComponentLookup<EntityObject<Transform>>(true);
                    collect.entities = entities;

                    collect.Run(__groupToDestroy);

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
                        collect.entityType = GetEntityTypeHandle();
                        collect.trasnformType = GetComponentTypeHandle<EntityObject<Transform>>(true);
                        collect.instanceType = GetComponentTypeHandle<MeshInstanceHybridRigData>(true);
                        collect.baseEntityIndexArray = __groupToCreate.CalculateBaseEntityIndexArray(Allocator.TempJob);
                        collect.counter = counter;
                        collect.results = results;
                        collect.Run(__groupToCreate);

                        count = counter[0];
                    }

                    entityManager.AddComponent<MeshInstanceHybridRigNode>(__groupToCreate);

                    var entityArray = entityManager.CreateEntity(__entityArchetype, count, Allocator.TempJob);
                    var transforms = new NativeArray<EntityObject<Transform>>(count, Allocator.TempJob, NativeArrayOptions.ClearMemory);

                    Transform root, temp;
                    EntityObject<Transform> transform;
                    Result result;
                    string transformPath;
                    int numRigs, numNodes, nodeIndex, i, j, k;
                    for(i = 0; i < entityCount; ++i)
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

                    Init init;
                    init.entityArray = entityArray;
                    init.transforms = transforms;
                    init.results = results;
                    init.rigMap = GetComponentLookup<Rig>(true);
                    init.rigs = GetBufferLookup<MeshInstanceRig>(true);
                    init.nodes = GetBufferLookup<MeshInstanceHybridRigNode>();
                    init.transformMap = GetComponentLookup<EntityObject<Transform>>();
                    init.instances = GetComponentLookup<HybridRigNode>();

                    Dependency = init.Schedule(results.Length, InnerloopBatchCount, Dependency);
                }
            }
        }
    }
}