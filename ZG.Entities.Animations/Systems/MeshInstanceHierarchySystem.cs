using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using Unity.Mathematics;

namespace ZG
{
    public struct MeshInstanceHierarchyDefinition
    {
        public struct Renderer
        {
            public int index;

            public float4x4 matrix;
        }

        public struct LOD
        {
            public int index;

            public float4x4 matrix;
        }

        public struct Transform
        {
            public int rigIndex;

            public BlobArray<Renderer> renderers;
            public BlobArray<LOD> lods;
        }

        public int instanceID;
        public BlobArray<Transform> transforms;
    }

    public struct MeshInstanceHierarchyData : IComponentData
    {
        public BlobAssetReference<MeshInstanceHierarchyDefinition> definition;
    }

    public struct MeshInstanceHierarchyID : ICleanupComponentData
    {
        public int value;
    }

    public struct MeshInstanceHierarchyDisabled : IComponentData
    {

    }

    [BurstCompile, UpdateInGroup(typeof(MeshInstanceSystemGroup)), UpdateAfter(typeof(MeshInstanceRigSystem))]
    public partial struct MeshInstanceHierarchySystem : ISystem
    {
        private struct Collect
        {
            public bool hasRig;

            [ReadOnly]
            public SharedHashMap<Entity, MeshInstanceRendererBuilder>.Reader rendererBuilders;

            [ReadOnly]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeArray<MeshInstanceHierarchyData> instances;

            [ReadOnly]
            public BufferAccessor<EntityParent> entityParents;

            [ReadOnly]
            public BufferAccessor<MeshInstanceNode> renderers;

            [ReadOnly]
            public BufferAccessor<MeshInstanceObject> lods;

            [ReadOnly]
            public BufferLookup<MeshInstanceObject> lodMap;

            [ReadOnly]
            public BufferLookup<MeshInstanceNode> rendererMap;

            [ReadOnly]
            public BufferLookup<MeshInstanceRig> rigMap;

            [ReadOnly]
            public ComponentLookup<Parent> parents;

            public UnsafeListEx<Entity> entitiesToCreate;

            public NativeList<Entity> staticEntities;

            public void Execute(int index)
            {
                Entity parentEntity = Entity.Null;
                if (!hasRig)
                {
                    parentEntity = index < entityParents.Length ? EntityParent.Get(entityParents[index], rigMap) : Entity.Null;
                    if(parentEntity == Entity.Null)
                        return;
                }

                Entity entity = entityArray[index];

                DynamicBuffer<MeshInstanceNode> renderers;
                if (index < this.renderers.Length)
                {
                    if (rendererBuilders.ContainsKey(entity))
                        return;

                    renderers = this.renderers[index];
                }
                else
                {
                    if (index < entityParents.Length)
                        parentEntity = EntityParent.Get(entityParents[index], rendererMap);
                    else
                        return;

                    if (parentEntity == Entity.Null || rendererBuilders.ContainsKey(parentEntity))
                        return;

                    renderers = rendererMap[parentEntity];
                }

                DynamicBuffer<MeshInstanceObject> lods;
                if (index < this.lods.Length)
                    lods = this.lods[index];
                else
                {
                    parentEntity = index < entityParents.Length ? EntityParent.Get(entityParents[index], lodMap) : Entity.Null;

                    lods = lodMap.HasBuffer(parentEntity) ? lodMap[parentEntity] : default;
                }

                ref var definition = ref instances[index].definition.Value;

                Entity rendererEntity;
                int numTransforms = definition.transforms.Length, numRenderers, numLODs, i, j;
                for(i = 0; i < numTransforms; ++i)
                {
                    ref var transform = ref definition.transforms[i];

                    numRenderers = transform.renderers.Length;
                    for(j = 0; j < numRenderers; ++j)
                    {
                        ref var renderer = ref transform.renderers[j];

                        rendererEntity = renderers[renderer.index].entity;

                        if (parents.HasComponent(rendererEntity))
                            continue;

                        staticEntities.Add(rendererEntity);
                    }

                    numLODs = transform.lods.Length;
                    for (j = 0; j < numLODs; ++j)
                    {
                        ref var lod = ref transform.lods[j];

                        rendererEntity = lods[lod.index].entity;

                        if (parents.HasComponent(rendererEntity))
                            continue;

                        staticEntities.Add(rendererEntity);
                    }
                }

                entitiesToCreate.Add(entity);
            }
        }

        [BurstCompile]
        private struct CollectEx : IJobChunk
        {
            [ReadOnly]
            public SharedHashMap<Entity, MeshInstanceRendererBuilder>.Reader rendererBuilders;

            [ReadOnly]
            public EntityTypeHandle entityType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceHierarchyData> instanceType;

            [ReadOnly]
            public BufferTypeHandle<EntityParent> entityParentType;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> rigType;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceNode> rendererType;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceObject> lodType;

            [ReadOnly]
            public BufferLookup<MeshInstanceObject> lods;

            [ReadOnly]
            public BufferLookup<MeshInstanceNode> renderers;

            [ReadOnly]
            public BufferLookup<MeshInstanceRig> rigs;

            [ReadOnly]
            public ComponentLookup<Parent> parents;

            public UnsafeListEx<Entity> entitiesToCreate;

            public NativeList<Entity> staticEntities;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Collect collect;
                collect.hasRig = chunk.Has(ref rigType);
                collect.rendererBuilders = rendererBuilders;
                collect.entityArray = chunk.GetNativeArray(entityType);
                collect.instances = chunk.GetNativeArray(ref instanceType);
                collect.entityParents = chunk.GetBufferAccessor(ref entityParentType);
                collect.renderers = chunk.GetBufferAccessor(ref rendererType);
                collect.lods = chunk.GetBufferAccessor(ref lodType);
                collect.lodMap = lods;
                collect.rendererMap = renderers;
                collect.rigMap = rigs;
                collect.parents = parents;
                collect.entitiesToCreate = entitiesToCreate;
                collect.staticEntities = staticEntities;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    collect.Execute(i);
            }
        }

        [BurstCompile]
        private struct Init : IJobParallelFor
        {
            [ReadOnly]
            public UnsafeListEx<Entity> entityArray;

            [ReadOnly]
            public ComponentLookup<MeshInstanceHierarchyData> instances;

            [ReadOnly]
            public BufferLookup<EntityParent> entityParents;

            [ReadOnly]
            public BufferLookup<MeshInstanceNode> renderers;

            [ReadOnly]
            public BufferLookup<MeshInstanceObject> lods;

            [ReadOnly]
            public BufferLookup<MeshInstanceRigNode> nodes;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<MeshInstanceHierarchyID> ids;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<Parent> parents;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<LocalToParent> localToParents;

            public void Execute(int index)
            {
                Entity entity = entityArray[index], parentEntity = EntityParent.Get(entity, entityParents, this.nodes);
                if (parentEntity == Entity.Null)
                    return;

                var nodes = this.nodes[parentEntity];

                parentEntity = EntityParent.Get(entity, entityParents, this.renderers);
                if (parentEntity == Entity.Null)
                    return;

                var renderers = this.renderers[parentEntity];

                var lods = this.lods.HasBuffer(parentEntity) ? this.lods[parentEntity] : default;

                Parent parent;
                LocalToParent localToParent;
                ref var definition = ref instances[entity].definition.Value;
                Entity rendererEntity, lodEntity;
                int numTransforms = definition.transforms.Length, numRenderers, numLODs, i, j;
                for(i = 0; i < numTransforms; ++i)
                {
                    ref var transform = ref definition.transforms[i];

                    parent.Value = nodes[transform.rigIndex].entity;

                    numRenderers = transform.renderers.Length;
                    for(j = 0; j < numRenderers; ++j)
                    {
                        ref var renderer = ref transform.renderers[j];

                        rendererEntity = renderers[renderer.index].entity;

                        localToParent.Value = renderer.matrix;
                        localToParents[rendererEntity] = localToParent;

                        parents[rendererEntity] = parent;
                    }

                    numLODs = transform.lods.Length;
                    for (j = 0; j < numLODs; ++j)
                    {
                        ref var lod = ref transform.lods[j];

                        lodEntity = lods[lod.index].entity;

                        localToParent.Value = lod.matrix;
                        localToParents[lodEntity] = localToParent;

                        parents[lodEntity] = parent;
                    }
                }

                MeshInstanceHierarchyID id;
                id.value = definition.instanceID;
                ids[entity] = id;
            }
        }

        [BurstCompile]
        private struct DisposeAll : IJob
        {
            public UnsafeListEx<Entity> entities;

            public void Execute()
            {
                entities.Dispose();
            }
        }

        public static readonly int InnerloopBatchCount = 1;

        private EntityQuery __groupToDestroy;
        private EntityQuery __groupToCreate;

        private SharedHashMap<Entity, MeshInstanceRendererBuilder> __rendererBuilders;

        public void OnCreate(ref SystemState state)
        {
            BurstUtility.InitializeJobParallelFor<Init>();
            BurstUtility.InitializeJob<DisposeAll>();

            __groupToDestroy = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceHierarchyID>()
                    },

                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceHierarchyData)
                    }
                },
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceHierarchyID>(),
                        ComponentType.ReadOnly<MeshInstanceHierarchyData>(),
                    },

                    Any = new ComponentType[]
                    {
                        typeof(MeshInstanceHierarchyDisabled)
                    },

                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __groupToCreate = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceHierarchyData>(),
                    }, 
                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceHierarchyID), 
                        typeof(MeshInstanceHierarchyDisabled)
                    }, 
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __rendererBuilders = state.World.GetOrCreateSystemUnmanaged<MeshInstanceRendererSystem>().builders;
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var entityManager = state.EntityManager;

            entityManager.RemoveComponent<MeshInstanceHierarchyID>(__groupToDestroy);

            if (!__groupToCreate.IsEmpty)
            {
                var entities = new UnsafeListEx<Entity>(Allocator.TempJob);

                using (var staticEntities = new NativeList<Entity>(Allocator.TempJob))
                {
                    __rendererBuilders.lookupJobManager.CompleteReadOnlyDependency();
                    state.CompleteDependency();

                    CollectEx collect;
                    collect.rendererBuilders = __rendererBuilders.reader;
                    collect.entityType = state.GetEntityTypeHandle();
                    collect.instanceType = state.GetComponentTypeHandle<MeshInstanceHierarchyData>(true);
                    collect.entityParentType = state.GetBufferTypeHandle<EntityParent>(true);
                    collect.rigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
                    collect.rendererType = state.GetBufferTypeHandle<MeshInstanceNode>(true);
                    collect.lodType = state.GetBufferTypeHandle<MeshInstanceObject>(true);
                    collect.lods = state.GetBufferLookup<MeshInstanceObject>(true);
                    collect.renderers = state.GetBufferLookup<MeshInstanceNode>(true);
                    collect.rigs = state.GetBufferLookup<MeshInstanceRig>(true);
                    collect.parents = state.GetComponentLookup<Parent>(true);
                    collect.staticEntities = staticEntities;
                    collect.entitiesToCreate = entities;

                    collect.Run(__groupToCreate);

                    entityManager.RemoveComponent<Static>(staticEntities.AsArray());

                    entityManager.AddComponentBurstCompatible<Parent>(staticEntities.AsArray());
                    entityManager.AddComponentBurstCompatible<LocalToParent>(staticEntities.AsArray());

                    entityManager.AddComponentBurstCompatible<MeshInstanceHierarchyID>(entities.AsArray());
                }

                Init init;
                init.entityArray = entities;
                init.instances = state.GetComponentLookup<MeshInstanceHierarchyData>(true);
                init.entityParents = state.GetBufferLookup<EntityParent>(true);
                init.renderers = state.GetBufferLookup<MeshInstanceNode>(true);
                init.lods = state.GetBufferLookup<MeshInstanceObject>(true);
                init.nodes = state.GetBufferLookup<MeshInstanceRigNode>(true);
                init.ids = state.GetComponentLookup<MeshInstanceHierarchyID>();
                init.parents = state.GetComponentLookup<Parent>();
                init.localToParents = state.GetComponentLookup<LocalToParent>();

                var jobHandle = init.Schedule(entities.length, InnerloopBatchCount, state.Dependency);

                DisposeAll disposeAll;
                disposeAll.entities = entities;

                state.Dependency = disposeAll.Schedule(jobHandle);
            }
        }
    }
}