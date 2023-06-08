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

//[assembly: RegisterGenericJobType(typeof(ZG.MeshInstanceTransformJob<ZG.MeshInstanceRigTransformSystem.Enumerator, ZG.MeshInstanceRigTransformSystem.Enumerable>))]

namespace ZG
{
    public struct MeshInstanceRigDisabled : IComponentData
    {

    }

    public struct MeshInstanceRig : ICleanupBufferElementData
    {
        public Entity entity;
    }

    public struct MeshInstanceRigNode : ICleanupBufferElementData
    {
        public Entity entity;
    }

    [BurstCompile, UpdateInGroup(typeof(MeshInstanceSystemGroup), OrderLast = true)]
    public partial struct MeshInstanceRigStructChangeSystem : ISystem
    {
        public EntityComponentAssigner assigner
        {
            get;

            private set;
        }

        public void OnCreate(ref SystemState state)
        {
            assigner = new EntityComponentAssigner(Allocator.Persistent);

            /*__group = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<Rig>()
                    },
                    Options = EntityQueryOptions.IncludeDisabled
                });*/
        }

        public void OnDestroy(ref SystemState state)
        {
            assigner.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            assigner.Playback(ref state);
        }
    }

    [BurstCompile, UpdateInGroup(typeof(MeshInstanceSystemGroup), OrderFirst = true), UpdateAfter(typeof(MeshInstanceFactorySystem))]
    public partial struct MeshInstanceRigFactorySystem : ISystem
    {
        public static readonly int InnerloopBatchCount = 1;

        private EntityArchetype __rigEntityArchetype;
        private EntityArchetype __nodeEntityArchetype;
        private EntityQuery __groupToCreate;
        private EntityQuery __groupToDestroy;
        private SingletonAssetContainer<ComponentTypeSet> __componentTypes;
        private SingletonAssetContainer<BlobAssetReference<RigDefinition>> __rigDefinitions;
        private UnsafeListEx<MeshInstanceRigUtility.Result> __results;

        public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>> prefabs
        {
            get;

            private set;
        }

        public void OnCreate(ref SystemState state)
        {
            prefabs = new SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>(Allocator.Persistent);

            var entityManager = state.EntityManager;

            var rigPrefabComponentTypes = MeshInstanceRigUtility.rigPrefabComponentTypes;

            __rigEntityArchetype = entityManager.CreateArchetype(rigPrefabComponentTypes);

            __nodeEntityArchetype = entityManager.CreateArchetype(MeshInstanceRigUtility.nodePrefabComponentTypes);

            __groupToCreate = state.GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<MeshInstanceRigData>()
                },
                None = new ComponentType[]
                {
                    typeof(MeshInstanceRigID), 
                    typeof(MeshInstanceRigDisabled)
                }, 

                Options = EntityQueryOptions.IncludeDisabledEntities
            });

            __groupToDestroy = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceRigID>()
                    },

                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceRigData)
                    }
                },
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceRigID>(),
                        ComponentType.ReadOnly<MeshInstanceRigData>(),
                    },

                    Any = new ComponentType[]
                    {
                        typeof(MeshInstanceRigDisabled)
                    },

                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __componentTypes = SingletonAssetContainer<ComponentTypeSet>.instance;

            __rigDefinitions = SingletonAssetContainer<BlobAssetReference<RigDefinition>>.instance;

            __results = new UnsafeListEx<MeshInstanceRigUtility.Result>(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            __results.Dispose();

            prefabs.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var prefabs = this.prefabs;

            if (!__groupToDestroy.IsEmpty)
                MeshInstanceRigUtility.Destroy(__groupToDestroy, ref prefabs, ref state);

            if (!__groupToCreate.IsEmpty)
                MeshInstanceRigUtility.Execute(
                    InnerloopBatchCount, 
                    __rigEntityArchetype,
                    __nodeEntityArchetype, 
                    __groupToCreate,
                    prefabs,
                    __componentTypes,
                    __rigDefinitions, 
                    ref __results, 
                    ref state);
        }
    }

    [BurstCompile, UpdateInGroup(typeof(MeshInstanceSystemGroup)), UpdateAfter(typeof(MeshInstanceRendererSystem))]
    public partial struct MeshInstanceRigSystem : ISystem
    {
        private struct Key : IEquatable<Key>
        {
            public bool isStatic;
            public int instanceID;

            public bool Equals(Key other)
            {
                return isStatic == other.isStatic && instanceID == other.instanceID;
            }

            public override int GetHashCode()
            {
                return instanceID;
            }
        }

        private struct TransformHandle : ITransformHandle
        {
            public Entity Entity { get; set; }

            public int Index { get; set; }
        }

        private struct Collect
        {
            [ReadOnly]
            public BufferAccessor<MeshInstanceRig> rigs;

            //[ReadOnly]
            //public ComponentLookup<RigRootEntity> rigRootEntities;

            [ReadOnly]
            public BufferAccessor<MeshInstanceRigNode> nodes;

            public NativeList<Entity> entities;

            public void Execute(int index)
            {
                var rigs = this.rigs[index];
                int numRigs = rigs.Length;
                for (int i = 0; i < numRigs; ++i)
                    entities.Add(rigs[i].entity);
                //entities.Add(rigRootEntities[entity].Value);

                if (index < this.nodes.Length)
                {
                    var transforms = this.nodes[index];
                    int numTransforms = transforms.Length;
                    for (int i = 0; i < numTransforms; ++i)
                        entities.Add(transforms[i].entity);
                }
            }
        }

        [BurstCompile]
        private struct CollectEx : IJobChunk
        {
            [ReadOnly]
            public EntityTypeHandle entityType;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> rigType;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRigNode> nodeType;

            //[ReadOnly]
            //public ComponentLookup<RigRootEntity> rigRootEntities;

            public NativeList<Entity> entitiesToDestroy;

            public NativeList<Entity> entitiesToRemove;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Collect collect;
                collect.rigs = chunk.GetBufferAccessor(ref rigType);
                collect.nodes = chunk.GetBufferAccessor(ref nodeType);
                collect.entities = entitiesToDestroy;
                //collect.rigRootEntities = rigRootEntities;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    collect.Execute(i);

                if (chunk.Has(ref nodeType))
                {
                    var entityArray = chunk.GetNativeArray(entityType);
                    iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                    while (iterator.NextEntityIndex(out int i))
                        entitiesToRemove.Add(entityArray[i]);
                }
            }
        }

        private struct CollectEntities
        {
            public bool isStatic;

            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Reader prefabs;

            [ReadOnly]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeArray<MeshInstanceRigData> instances;

            public NativeParallelMultiHashMap<Key, Entity> entities;

            public NativeList<Entity> transforms;

            public int Execute(int index)
            {
                ref var definition = ref instances[index].definition.Value;

                Entity entity = entityArray[index];

                Key key;
                key.isStatic = isStatic;
                key.instanceID = definition.instanceID;

                entities.Add(key, entity);

                if (prefabs[definition.instanceID].Value.nodes.Length > 0)
                    transforms.Add(entity);

                int numNodes = definition.nodes.Length, typeCount = 0;
                for (int i = 0; i < numNodes; ++i)
                    typeCount += definition.nodes[i].typeIndices.Length;

                return typeCount;
            }
        }

        [BurstCompile]
        private struct CollectEntitiesEx : IJobChunk
        {
            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Reader prefabs;

            [ReadOnly]
            public EntityTypeHandle entityType;
            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceStatic> staticType;
            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceRigData> instanceType;

            public NativeParallelMultiHashMap<Key, Entity> entities;

            public NativeList<Entity> transforms;

            public NativeArray<int> typeCount;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                CollectEntities collectEntities;
                collectEntities.isStatic = chunk.Has(ref staticType);
                collectEntities.prefabs = prefabs;
                collectEntities.entityArray = chunk.GetNativeArray(entityType);
                collectEntities.instances = chunk.GetNativeArray(ref instanceType);
                collectEntities.entities = entities;
                collectEntities.transforms = transforms;

                int typeCount = 0;
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    typeCount += collectEntities.Execute(i);

                this.typeCount[0] += typeCount;
            }
        }

        [BurstCompile]
        private struct InitEntities : IJobParallelFor
        {
            public int rigCount;
            public int nodeCount;

            [ReadOnly]
            public NativeArray<Entity> instanceEntities;
            [ReadOnly]
            public NativeArray<Entity> prefabEntities;

            [NativeDisableContainerSafetyRestriction]
            public BufferLookup<MeshInstanceRig> rigs;

            [NativeDisableContainerSafetyRestriction]
            public BufferLookup<MeshInstanceRigNode> nodes;

            //[NativeDisableContainerSafetyRestriction]
            //public ComponentLookup<RigRootEntity> rigRootEntities;

            public void Execute(int index)
            {
                int numPrefabEntities = prefabEntities.Length;
                Entity prefabEntity = prefabEntities[index];//, instanceEntity;

                //RigRootEntity rigRootEntity;
                var entities = rigs[prefabEntity].Reinterpret<Entity>();
                entities.ResizeUninitialized(rigCount);
                for (int i = 0; i < rigCount; ++i)
                    //instanceEntity = instanceEntities[numPrefabEntities * i + index];
                    //rigRootEntity = rigRootEntities[instanceEntity];
                    //rigRootEntity.Value = instanceEntities[numPrefabEntities * (i + rigCount) + index];
                    //rigRootEntities[instanceEntity] = rigRootEntity;

                    entities[i] = instanceEntities[numPrefabEntities * i + index];// instanceEntity;

                if (nodeCount > 0)
                {
                    var nodes = this.nodes[prefabEntity].Reinterpret<Entity>();
                    nodes.ResizeUninitialized(nodeCount);

                    for (int i = 0; i < nodeCount; ++i)
                        nodes[i] = instanceEntities[numPrefabEntities * (rigCount + i) + index];
                }
            }
        }

        [BurstCompile]
        private struct SetParents : IJobParallelFor
        {
            [ReadOnly]
            public SingletonAssetContainer<int>.Reader typeIndices;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> entityArray;
            [ReadOnly]
            public ComponentLookup<MeshInstanceTransform> transforms;
            [ReadOnly]
            public ComponentLookup<Rig> rigDefinitions;
            [ReadOnly]
            public BufferLookup<MeshInstanceRig> rigs;
            [ReadOnly]
            public BufferLookup<MeshInstanceRigNode> nodes;
            [ReadOnly]
            public ComponentLookup<MeshInstanceRigData> instances;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<RigRootEntity> rigRootEntities;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<Translation> translations;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<Rotation> rotations;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<NonUniformScale> scales;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<LocalToParent> localToParents;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<Parent> parents;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<MotionClipData> motionClips;

            [NativeDisableParallelForRestriction]
            public BufferLookup<AnimatedData> animations;

            public EntityComponentAssigner.ParallelWriter transformHandles;

            public void Execute(int index)
            {
                Entity entity = entityArray[index];
                ref var definition = ref instances[entity].definition.Value;
                SingletonAssetContainerHandle handle;
                handle.instanceID = definition.instanceID;

                quaternion quaternion;
                float3 position, scale;
                if(transforms.HasComponent(entity))
                {
                    var transform = transforms[entity];
                    position = transform.translation;
                    quaternion = transform.rotation;
                    scale = transform.scale;
                }
                else
                {
                    position = float3.zero;
                    quaternion = quaternion.identity;
                    scale = 1.0f;
                }

                var matrix = float4x4.TRS(position, quaternion, scale);

                var rigs = this.rigs[entity];
                var nodes = this.nodes.HasBuffer(entity) ? this.nodes[entity] : default;
                RigRootEntity rigRootEntity;
                Parent parent;
                LocalToParent localToParent;
                Translation translation;
                Rotation rotation;
                NonUniformScale nonUniformScale;
                MotionClipData motionClip;
                AnimationStream animationStream;
                BlobAssetReference<RigDefinition> rigDefinition;
                TransformHandle transformHandle;
                Entity rigEntity;
                int i, j, k, rootBoneIndex, numTypeIndices, numNodes, numRigs = rigs.Length;
                for (i = 0; i < numRigs; ++i)
                {
                    ref var rig = ref definition.rigs[i];

                    rigEntity = rigs[i].entity;

                    /*if (parents.HasComponent(rigEntity))
                    {
                        parent.Value = entity;
                        parents[rigEntity] = parent;
                    }

                    if (localToParents.HasComponent(rigEntity))
                    {
                        localToParent.Value = rig.transform;

                        localToParents[rigEntity] = localToParent;
                    }*/

                    rigRootEntity = rigRootEntities[rigEntity];
                    rigRootEntity.Value = rigEntity;
                    rigRootEntities[rigEntity] = rigRootEntity;

                    if (parents.HasComponent(rigEntity))
                    {
                        parent.Value = entity;

                        parents[rigEntity] = parent;
                    }

                    if (localToParents.HasComponent(rigEntity))
                    {
                        localToParent.Value = math.mul(matrix, float4x4.TRS(rig.rootTranslation, rig.rootRotation, rig.rootScale));

                        localToParents[rigEntity] = localToParent;
                    }

                    if (translations.HasComponent(rigEntity))
                    {
                        translation.Value = math.transform(matrix, rig.rootTranslation);

                        translations[rigEntity] = translation;
                    }

                    if (rotations.HasComponent(rigEntity))
                    {
                        rotation.Value = math.mul(quaternion, rig.rootRotation);

                        rotations[rigEntity] = rotation;
                    }

                    if (scales.HasComponent(rigEntity))
                    {
                        nonUniformScale.Value = scale * rig.rootScale;

                        scales[rigEntity] = nonUniformScale;
                    }

                    rootBoneIndex = 0;
                    if (rigDefinitions.HasComponent(rigEntity))
                    {
                        rigDefinition = rigDefinitions[rigEntity].Value;
                        if (motionClips.HasComponent(rigEntity))
                        {
                            motionClip = motionClips[rigEntity];

                            rootBoneIndex = Core.FindBindingIndex(ref rigDefinition.Value.Skeleton.Ids, motionClip.rootID);
                            if (rootBoneIndex == -1)
                                rootBoneIndex = 0;
                            
                            motionClip.rootTransform.translation = position;
                            motionClip.rootTransform.rotation = quaternion;
                            motionClip.rootTransform.scale = scale;

                            motionClips[rigEntity] = motionClip;
                        }

                        if (animations.HasBuffer(rigEntity))
                        {
                            animationStream = AnimationStream.Create(rigDefinition, animations[rigEntity].AsNativeArray());

                            animationStream.SetLocalToParentTRS(rootBoneIndex,
                                    math.transform(matrix, animationStream.GetLocalToParentTranslation(rootBoneIndex)),
                                    math.mul(quaternion, animationStream.GetLocalToParentRotation(rootBoneIndex)),
                                    scale * animationStream.GetLocalToParentScale(rootBoneIndex));
                        }
                    }

                    numNodes = rig.nodes.Length;
                    for (j = 0; j < numNodes; ++j)
                    {
                        ref var node = ref rig.nodes[j];
                        ref var typeIndices = ref definition.nodes[node.nodeIndex].typeIndices;

                        transformHandle = new TransformHandle()
                        {
                            Index = node.index,
                            Entity = nodes[node.nodeIndex].entity
                        };

                        numTypeIndices = typeIndices.Length;
                        for (k = 0; k < numTypeIndices; ++k)
                        {
                            handle.index = typeIndices[k];

                            transformHandles.AppendBuffer(this.typeIndices[handle], rigEntity, ref transformHandle);
                        }
                    }
                }

                /*numTransforms = definition.transforms.Length;
                if (numTransforms > 0)
                {
                    var nodes = this.nodes[entity];
                    Entity rendererEntity;
                    int numRenderers;
                    for (i = 0; i < numTransforms; ++i)
                    {
                        parent.Value = transforms[i].entity;

                        ref var renderers = ref definition.transforms[i].renderers;

                        numRenderers = renderers.Length;
                        for (j = 0; j < numRenderers; ++j)
                        {
                            ref var renderer = ref renderers[j];

                            rendererEntity = nodes[renderer.nodeIndex].entity;

                            parents[rendererEntity] = parent;

                            localToParent.Value = renderer.offset;
                            localToParents[rendererEntity] = localToParent;
                        }
                    }
                }*/
            }
        }

        [BurstCompile]
        public struct DisposeAll : IJob
        {
            [DeallocateOnJobCompletion]
            public NativeArray<int> typeCounts;
            [DeallocateOnJobCompletion]
            public NativeArray<Entity> prefabEntities;
            [DeallocateOnJobCompletion]
            public NativeArray<Entity> instanceEntities;

            public void Execute()
            {

            }
        }

        private struct Result
        {
            public struct Info
            {
                public int rigCount;
                public int nodeCount;
                public int prefabEntityCount;
                public int prefabEntityStartIndex;
                public int instanceEntityStartIndex;

                public int instanceEntityCount => prefabEntityCount * (rigCount + nodeCount);

                public Info(
                    bool isStatic,
                    int prefabEntityStartIndex,
                    int prefabEntityCount,
                    in NativeArray<Entity> instanceEntities,
                    ref BlobArray<MeshInstanceRigPrefab.Rig> rigs,
                    ref BlobArray<Entity> nodes,
                    ref EntityManager entityManager,
                    ref int instanceEntityStartIndex)
                {
                    rigCount = rigs.Length;
                    nodeCount = nodes.Length;

                    this.prefabEntityCount = prefabEntityCount;
                    this.prefabEntityStartIndex = prefabEntityStartIndex;
                    this.instanceEntityStartIndex = instanceEntityStartIndex;

                    int instanceEntityCount = this.instanceEntityCount;
                    NativeArray<Entity> entities = instanceEntities.GetSubArray(instanceEntityStartIndex, instanceEntityCount), rigRootEntities;
                    for (int i = 0; i < rigCount; ++i)
                    {
                        ref var rig = ref rigs[i];

                        rigRootEntities = entities.GetSubArray(i * prefabEntityCount, prefabEntityCount);

                        entityManager.Instantiate(rig.entity, rigRootEntities);

                        /*if (isStatic)
                            entityManager.AddComponent<Static>(rigRootEntities);
                        else*/
                        if (!isStatic)
                        {
                            entityManager.AddComponentBurstCompatible<LocalToParent>(rigRootEntities);
                            entityManager.AddComponentBurstCompatible<Parent>(rigRootEntities);
                        }
                    }

                    for(int i = 0; i < nodeCount; ++i)
                        entityManager.Instantiate(nodes[i], entities.GetSubArray((rigCount + i) * prefabEntityCount, prefabEntityCount));

                    instanceEntityStartIndex += instanceEntityCount;
                }

                public Result ToResult(
                    in NativeArray<Entity> prefabEntities,
                    in NativeArray<Entity> instanceEntities)
                {
                    Result result;
                    result.rigCount = rigCount;
                    result.nodeCount = nodeCount;
                    result.prefabEntities = prefabEntities.GetSubArray(prefabEntityStartIndex, prefabEntityCount);
                    result.instanceEntities = instanceEntities.GetSubArray(instanceEntityStartIndex, instanceEntityCount);

                    return result;
                }
            }

            public int rigCount;
            public int nodeCount;
            public NativeArray<Entity> prefabEntities;
            public NativeArray<Entity> instanceEntities;

            public static void Schedule(
                int innerloopBatchCount,
                in NativeArray<Entity> prefabEntities,
                in NativeArray<Entity> instanceEntities,
                in NativeArray<Info> infos,
                ref SystemState systemState)
            {
                var rigs = systemState.GetBufferLookup<MeshInstanceRig>();
                var nodes = systemState.GetBufferLookup<MeshInstanceRigNode>();
                //var rigRootEntities = systemState.GetComponentLookup<RigRootEntity>();

                Result result;
                JobHandle temp, inputDeps = systemState.Dependency;
                JobHandle? jobHandle = null;
                int length = infos.Length;
                for (int i = 0; i < length; ++i)
                {
                    result = infos[i].ToResult(prefabEntities, instanceEntities);

                    temp = result.ScheduleInitEntities(
                        innerloopBatchCount,
                        ref rigs, 
                        ref nodes, 
                        inputDeps);

                    jobHandle = jobHandle == null ? temp : JobHandle.CombineDependencies(jobHandle.Value, temp);
                }

                if (jobHandle != null)
                    systemState.Dependency = jobHandle.Value;
            }

            public JobHandle ScheduleInitEntities(
                int innerloopBatchCount,
                ref BufferLookup<MeshInstanceRig> rigs, 
                ref BufferLookup<MeshInstanceRigNode> nodes,
                //ref ComponentLookup<RigRootEntity> rigRootEntities, 
                in JobHandle inputDeps)
            {
                InitEntities initEntities;
                initEntities.rigCount = rigCount;
                initEntities.nodeCount = nodeCount;
                initEntities.instanceEntities = instanceEntities;
                initEntities.prefabEntities = prefabEntities;
                initEntities.rigs = rigs;
                initEntities.nodes = nodes;
                //initEntities.rigRootEntities = rigRootEntities;

                return initEntities.Schedule(prefabEntities.Length, innerloopBatchCount, inputDeps);
            }
        }

        public static readonly int InnerloopBatchCount = 1;

        private EntityQuery __groupToDestroy;
        private EntityQuery __groupToCreate;
        private SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>> __prefabs;
        private EntityComponentAssigner __assigner;
        private SingletonAssetContainer<int> __typeIndices;

        public void OnCreate(ref SystemState state)
        {
            BurstUtility.InitializeJobParallelFor<InitEntities>();
            BurstUtility.InitializeJobParallelFor<SetParents>();
            BurstUtility.InitializeJob<DisposeAll>();

            __groupToDestroy = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceRig>()
                    },

                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceRigID)
                    },

                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __groupToCreate = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceRigData>(),
                        ComponentType.ReadOnly<MeshInstanceRigID>()
                    },
                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceRig)
                    }, 
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __prefabs = state.World.GetOrCreateSystemUnmanaged<MeshInstanceRigFactorySystem>().prefabs;
            __assigner = state.World.GetOrCreateSystemUnmanaged<MeshInstanceRigStructChangeSystem>().assigner;

            __typeIndices = SingletonAssetContainer<int>.instance;
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var entityManager = state.EntityManager;

            if (!__groupToDestroy.IsEmpty)
            {
                using (var entitiesToDestroy = new NativeList<Entity>(Allocator.TempJob))
                using (var entitiesToRemove = new NativeList<Entity>(Allocator.TempJob))
                {
                    state.CompleteDependency();

                    CollectEx collect;
                    collect.entityType = state.GetEntityTypeHandle();
                    collect.rigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
                    collect.nodeType = state.GetBufferTypeHandle<MeshInstanceRigNode>(true);
                    //collect.rigRootEntities = state.GetComponentLookup<RigRootEntity>(true);
                    collect.entitiesToDestroy = entitiesToDestroy;
                    collect.entitiesToRemove = entitiesToRemove;

                    collect.Run(__groupToDestroy);

                    entityManager.RemoveComponent<MeshInstanceRig>(__groupToDestroy);

                    entityManager.RemoveComponent<MeshInstanceRigNode>(entitiesToRemove.AsArray());

                    entityManager.DestroyEntity(entitiesToDestroy.AsArray());
                }
            }

            int entityCount = __groupToCreate.CalculateEntityCount();
            if (entityCount > 0)
            {
                using (var entities = new NativeParallelMultiHashMap<Key, Entity>(entityCount, Allocator.TempJob))
                {
                    __prefabs.lookupJobManager.CompleteReadOnlyDependency();

                    var reader = __prefabs.reader;

                    var typeCounts = new NativeArray<int>(2, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    using (var transforms = new NativeList<Entity>(Allocator.TempJob))
                    {
                        CollectEntitiesEx collectEntities;
                        collectEntities.prefabs = reader;
                        collectEntities.entityType = state.GetEntityTypeHandle();
                        collectEntities.staticType = state.GetComponentTypeHandle<MeshInstanceStatic>(true);
                        collectEntities.instanceType = state.GetComponentTypeHandle<MeshInstanceRigData>(true);
                        collectEntities.entities = entities;
                        collectEntities.transforms = transforms;
                        collectEntities.typeCount = typeCounts;

                        state.CompleteDependency();

                        collectEntities.Run(__groupToCreate);

                        entityManager.AddComponentBurstCompatible<MeshInstanceRigNode>(transforms.AsArray());
                    }

                    entityManager.AddComponent<MeshInstanceRig>(__groupToCreate);

                    if (!entities.IsEmpty)
                    {
                        using (var keys = entities.GetKeyArray(Allocator.Temp))
                        {
                            int count = keys.ConvertToUniqueArray(), instanceCount = 0, numKeys;
                            Key key;
                            for (int i = 0; i < count; ++i)
                            {
                                key = keys[i];
                                ref var prefab = ref reader[key.instanceID].Value;

                                numKeys = entities.CountValuesForKey(key);

                                instanceCount += numKeys * (prefab.rigs.Length + prefab.nodes.Length);
                            }

                            var prefabEntities = new NativeArray<Entity>(entityCount, Allocator.TempJob);
                            var instanceEntities = new NativeArray<Entity>(instanceCount, Allocator.TempJob);

                            int instanceEntityStartIndex = 0, prefabEntityStartIndex = 0;
                            var infos = new NativeArray<Result.Info>(count, Allocator.Temp);
                            {
                                int prefabEntityStartCount;
                                NativeParallelMultiHashMap<Key, Entity>.Enumerator enumerator;
                                for (int i = 0; i < count; ++i)
                                {
                                    key = keys[i];

                                    prefabEntityStartCount = 0;

                                    enumerator = entities.GetValuesForKey(key);
                                    while (enumerator.MoveNext())
                                        prefabEntities[prefabEntityStartIndex + prefabEntityStartCount++] = enumerator.Current;

                                    ref var prefab = ref reader[key.instanceID].Value;

                                    infos[i] = new Result.Info(
                                        key.isStatic,
                                        prefabEntityStartIndex,
                                        prefabEntityStartCount,
                                        instanceEntities,
                                        ref prefab.rigs,
                                        ref prefab.nodes,
                                        ref entityManager,
                                        ref instanceEntityStartIndex);

                                    prefabEntityStartIndex += prefabEntityStartCount;
                                }

                                Result.Schedule(InnerloopBatchCount, prefabEntities, instanceEntities, infos, ref state);

                                var jobHandle = state.Dependency;

                                var entityArray = entities.GetValueArray(Allocator.TempJob);

                                typeCounts[1] = typeCounts[0];

                                SetParents setParents;
                                setParents.typeIndices = __typeIndices.reader;
                                setParents.entityArray = entityArray;
                                setParents.transforms = state.GetComponentLookup<MeshInstanceTransform>(true);
                                setParents.rigDefinitions = state.GetComponentLookup<Rig>(true);
                                setParents.rigs = state.GetBufferLookup<MeshInstanceRig>(true);
                                setParents.nodes = state.GetBufferLookup<MeshInstanceRigNode>(true);
                                setParents.instances = state.GetComponentLookup<MeshInstanceRigData>(true);
                                setParents.rigRootEntities = state.GetComponentLookup<RigRootEntity>();
                                setParents.translations = state.GetComponentLookup<Translation>();
                                setParents.rotations = state.GetComponentLookup<Rotation>();
                                setParents.scales = state.GetComponentLookup<NonUniformScale>();
                                setParents.localToParents = state.GetComponentLookup<LocalToParent>();
                                setParents.parents = state.GetComponentLookup<Parent>();
                                setParents.motionClips = state.GetComponentLookup<MotionClipData>();
                                setParents.animations = state.GetBufferLookup<AnimatedData>();
                                setParents.transformHandles = __assigner.AsBufferParallelWriter<TransformHandle>(typeCounts, ref jobHandle);

                                var result = setParents.Schedule(entityArray.Length, InnerloopBatchCount, jobHandle);

                                __typeIndices.AddDependency(state.GetSystemID(), result);

                                __assigner.jobHandle = result;

                                DisposeAll disposeAll;
                                disposeAll.typeCounts = typeCounts;
                                disposeAll.prefabEntities = prefabEntities;
                                disposeAll.instanceEntities = instanceEntities;

                                state.Dependency = JobHandle.CombineDependencies(
                                    result,
                                    disposeAll.Schedule(jobHandle));

                                infos.Dispose();
                            }
                        }
                    }
                }
            }
        }
    }

    /*[UpdateInGroup(typeof(MeshInstanceSystemGroup)), UpdateAfter(typeof(MeshInstanceRendererSystem))]
    public partial class MeshInstanceRigSystem : SystemBase
    {
        [BurstCompile]
        private static class BurstUtility
        {
            public unsafe delegate void UpdateDelegate(MeshInstanceRigSystemCore* core, ref SystemState state);

            public unsafe static UpdateDelegate UpdateFunction = BurstCompiler.CompileFunctionPointer<UpdateDelegate>(Update).Invoke;

            [BurstCompile]
            [AOT.MonoPInvokeCallback(typeof(UpdateDelegate))]
            public static unsafe void Update(MeshInstanceRigSystemCore* core, ref SystemState state)
            {
                core->OnUpdate(ref state);
            }
        }

        private MeshInstanceRigSystemCore __core;

        protected override void OnCreate()
        {
            base.OnCreate();

            __core.OnCreate(ref this.GetState());
        }

        protected override void OnDestroy()
        {
            __core.OnDestroy(ref this.GetState());

            base.OnDestroy();
        }

        protected override unsafe void OnUpdate()
        {
            BurstUtility.UpdateFunction((MeshInstanceRigSystemCore*)UnsafeUtility.AddressOf(ref __core), ref this.GetState());
        }
    }*/
    /*[BurstCompile, UpdateInGroup(typeof(TransformSystemGroup))]
    public partial struct MeshInstanceRigTransformSystem : ISystem
    {
        public struct Enumerator : IMeshInstanceTransformEnumerator
        {
            public enum Step
            {
                None,
                Rig
            }

            public Step step;

            [ReadOnly]
            public BufferAccessor<MeshInstanceRig> rigs;

            public bool MoveNext(int index, out NativeArray<Entity> entityArray)
            {
                ++step;
                switch (step)
                {
                    case Step.Rig:
                        entityArray = rigs[index].Reinterpret<Entity>().AsNativeArray();
                        return true;
                }

                entityArray = default;

                return false;
            }
        }

        public struct Enumerable : IMeshInstanceTransformEnumerable<Enumerator>
        {
            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> rigType;

            public void Init(ref SystemState state)
            {
                rigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
            }

            public Enumerator GetEnumerator(in ArchetypeChunk chunk)
            {
                Enumerator enumerator;
                enumerator.step = Enumerator.Step.None;
                enumerator.rigs = chunk.GetBufferAccessor(rigType);

                return enumerator;
            }
        }

        private MeshInstanceTransformManager<Enumerator, Enumerable> __manager;

        public void OnCreate(ref SystemState state)
        {
            __manager = new MeshInstanceTransformManager<Enumerator, Enumerable>(ref state, ComponentType.ReadOnly<MeshInstanceRig>());
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            __manager.Update(ref state);
        }
    }
    */
}