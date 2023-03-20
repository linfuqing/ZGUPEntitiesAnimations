using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Animation;
using UnityEngine;
using System.Security.Principal;

namespace ZG
{
    public struct MeshInstanceAnimatorDefinition
    {
        public struct Layer
        {
            public MotionClipBlendingMode blendingMode;
            public float weight;
        }

        public struct Controller
        {
            public BlobArray<Layer> layers;
        }

        public struct WeightMask
        {
            public int index;
            public int layerIndex;
        }
        public struct Rig
        {
            public int index;

            public int controllerIndex;

            public StringHash rootID;

            public BlobArray<WeightMask> weightMasks;
        }

        public int instanceID;
        public BlobArray<Controller> controllers;
        public BlobArray<Rig> rigs;
    }

    public struct MeshInstanceAnimatorData :IComponentData
    {
        public BlobAssetReference<MeshInstanceAnimatorDefinition> definition;
    }

    public struct MeshInstanceAnimatorID : ICleanupComponentData
    {
        public int value;
    }

    public struct MeshInstanceAnimatorParameterCommand : IBufferElementData, IEnableableComponent
    {
        public StringHash name;
        public int value;
    }

    [BurstCompile, UpdateInGroup(typeof(MeshInstanceSystemGroup), OrderFirst = true), UpdateAfter(typeof(MeshInstanceRigFactorySystem))]
    public partial struct MeshInstanceAnimatorFactorySystem : ISystem
    {
        /*public struct Prefab
        {
            public int count;
        }*/

        private struct Result
        {
            public int rigIndex;
            public int rigInstanceID;
            public Entity entity;
            public BlobAssetReference<MeshInstanceAnimatorDefinition> definition;
        }

        /*private struct CollectToDestroy
        {
            [ReadOnly]
            public NativeArray<MeshInstanceAnimatorID> ids;

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
            public ComponentTypeHandle<MeshInstanceAnimatorID> idType;

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

        private struct Collect
        {
            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Reader rigPrefabs;

            [ReadOnly]
            public ComponentLookup<MotionClipData> clips;

            [ReadOnly]
            public ComponentLookup<MeshInstanceRigID> rigIDMap;

            [ReadOnly]
            public NativeArray<MeshInstanceRigID> rigIDs;

            [ReadOnly]
            public NativeArray<EntityParent> entityParents;

            [ReadOnly]
            public NativeArray<MeshInstanceAnimatorData> instances;

            public UnsafeListEx<Entity> weightMaskEntities;

            public UnsafeListEx<Result> results;

            public int Execute(int index)
            {
                Result result;
                result.definition = instances[index].definition;

                ref var definition = ref result.definition.Value;

                /*if (prefabs.TryGetValue(definition.instanceID, out var prefab))
                {
                    ++prefab.count;

                    prefabs[definition.instanceID] = prefab;
                }
                else
                {
                    prefab.count = 1;
                    prefabs.Add(definition.instanceID, prefab);

                }*/

                //��Rig���ر��ٴ�ʱ��Ҳ�����ã����Բ���ʹ��Prefab

                result.rigInstanceID = index < rigIDs.Length ? rigIDs[index].value : rigIDMap[entityParents[index].entity].value;

                ref var rigPrefab = ref rigPrefabs[result.rigInstanceID].Value;

                int numRigs = definition.rigs.Length, numResults, i, j;
                for (i = 0; i < numRigs; ++i)
                {
                    ref var rig = ref definition.rigs[i];
                    result.entity = rigPrefab.rigs[rig.index].entity;
                    if (clips.HasComponent(result.entity))
                        continue;

                    numResults = results.length;
                    for(j = 0; j < numResults; ++j)
                    {
                        if (results.ElementAt(j).entity == result.entity)
                            break;
                    }

                    if (j == numResults)
                    {
                        if (rig.weightMasks.Length > 0)
                            weightMaskEntities.Add(result.entity);

                        result.rigIndex = i;
                        results.Add(result);
                    }
                }

                return definition.instanceID;
            }
        }

        [BurstCompile]
        private struct CollectEx : IJobChunk
        {
            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Reader rigPrefabs;

            [ReadOnly]
            public ComponentLookup<MotionClipData> clips;

            [ReadOnly]
            public ComponentLookup<MeshInstanceRigID> rigIDs;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceRigID> rigIDType;

            [ReadOnly]
            public ComponentTypeHandle<EntityParent> entityParentType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceAnimatorData> instanceType;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<int> baseEntityIndexArray;

            public NativeArray<MeshInstanceAnimatorID> ids;

            public UnsafeListEx<Entity> weightMaskEntities;

            //public SharedHashMap<int, Prefab>.Writer prefabs;

            public UnsafeListEx<Result> results;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Collect collect;
                collect.clips = clips;
                collect.rigPrefabs = rigPrefabs;
                collect.rigIDMap = rigIDs;
                collect.rigIDs = chunk.GetNativeArray(ref rigIDType);
                collect.entityParents = chunk.GetNativeArray(ref entityParentType);
                collect.instances = chunk.GetNativeArray(ref instanceType);
                collect.weightMaskEntities = weightMaskEntities;
                //collect.prefabs = prefabs;
                collect.results = results;

                MeshInstanceAnimatorID id;

                int index = baseEntityIndexArray[unfilteredChunkIndex];
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    id.value = collect.Execute(i);

                    ids[index++] = id;
                }
            }
        }

        [BurstCompile]
        private struct Init : IJobParallelFor
        {
            [ReadOnly]
            public UnsafeListEx<Result> results;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<AnimatorControllerDefinition>>.Reader controllers;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<MotionClipWeightMaskDefinition>>.Reader weightMasks;

            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipLayerWeightMask> layerWeightMasks;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<AnimatorControllerData> instances;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<MotionClipData> clips;

            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipLayer> layers;

            public void Execute(int index)
            {
                var result = results[index];
                ref var definition = ref result.definition.Value;
                ref var rig = ref definition.rigs[result.rigIndex];

                AnimatorControllerData instance;
                instance.rigIndex = result.rigIndex;
                instance.rigInstanceID = result.rigInstanceID;
                instance.definition = controllers[new SingletonAssetContainerHandle(definition.instanceID, rig.controllerIndex)];
                instances[result.entity] = instance;

                MotionClipData clip;
                clip.rootID = rig.rootID;
                clip.rootTransform = MotionClipTransform.Identity;
                clips[result.entity] = clip;

                ref var controller = ref definition.controllers[rig.controllerIndex];
                int numLayers = controller.layers.Length;

                var layers = this.layers[result.entity];
                layers.ResizeUninitialized(numLayers);

                for(int i = 0; i < numLayers; ++i)
                {
                    ref var sourceLayer = ref controller.layers[i];
                    ref var destinationLayer = ref layers.ElementAt(i);

                    destinationLayer.blendingMode = sourceLayer.blendingMode;
                    destinationLayer.weight = i == 0 ? 1.0f : sourceLayer.weight;
                }

                int numWeightMasks = rig.weightMasks.Length;
                if (numWeightMasks > 0)
                {
                    var layerWeightMasks = this.layerWeightMasks[result.entity];
                    layerWeightMasks.ResizeUninitialized(numLayers);
                    layerWeightMasks.AsNativeArray().MemClear();

                    MotionClipLayerWeightMask layerWeightMask;
                    for (int i = 0; i < numWeightMasks; ++i)
                    {
                        ref var weightMask = ref rig.weightMasks[i];

                        layerWeightMask.definition = weightMasks[new SingletonAssetContainerHandle(definition.instanceID, weightMask.index)];

                        layerWeightMasks[weightMask.layerIndex] = layerWeightMask;
                    }
                }

            }
        }

        [BurstCompile]
        private struct DisposeAll : IJob
        {
            public UnsafeListEx<Result> results;

            public void Execute()
            {
                results.Dispose();
            }
        }

        public static readonly int InnerloopBatchCount = 1;

        private ComponentTypeSet __rigComponentTypes;

        private EntityQuery __groupToDestroy;
        private EntityQuery __groupToCreate;

        private SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>> __rigPrefabs;
        private SingletonAssetContainer<BlobAssetReference<AnimatorControllerDefinition>> __controllers;
        private SingletonAssetContainer<BlobAssetReference<MotionClipWeightMaskDefinition>> __weightMasks;

        /*public SharedHashMap<int, Prefab> prefabs
        {
            get;

            private set;
        }*/

        public void OnCreate(ref SystemState state)
        {
            BurstUtility.InitializeJobParallelFor<Init>();
            BurstUtility.InitializeJob<DisposeAll>();

            __rigComponentTypes = new ComponentTypeSet(
                new ComponentType[]
                {
                    ComponentType.ReadOnly<EntityObjects>(), 
                    ComponentType.ReadOnly<AnimatorControllerData>(),
                    ComponentType.ReadOnly<MotionClipData>(),
                    ComponentType.ReadWrite<MotionClip>(),
                    ComponentType.ReadWrite<MotionClipTime>(),
                    ComponentType.ReadWrite<MotionClipWeight>(),
                    ComponentType.ReadWrite<MotionClipLayer>(),
                    //ComponentType.ReadWrite<AnimatorControllerParameterCommand>(),
                    ComponentType.ReadWrite<AnimatorControllerParameter>(),
                    ComponentType.ReadWrite<AnimatorControllerEvent>(),
                    ComponentType.ReadWrite<AnimatorControllerStateMachine>()
                });

            __groupToDestroy = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceAnimatorID>()
                    },

                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceAnimatorData)
                    }
                },
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceAnimatorID>(),
                        ComponentType.ReadOnly<MeshInstanceAnimatorData>()
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
                        ComponentType.ReadOnly<MeshInstanceAnimatorData>()
                    },
                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceAnimatorID),
                        typeof(MeshInstanceRigDisabled)
                    },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __rigPrefabs = state.World.GetOrCreateSystemUnmanaged<MeshInstanceRigFactorySystem>().prefabs;
            __controllers = SingletonAssetContainer<BlobAssetReference<AnimatorControllerDefinition>>.instance;
            __weightMasks = SingletonAssetContainer<BlobAssetReference<MotionClipWeightMaskDefinition>>.instance;

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

            if (!__groupToDestroy.IsEmptyIgnoreFilter)
            {
                /*state.CompleteDependency();
                prefabs.lookupJobManager.CompleteReadWriteDependency();

                CollectToDestroyEx collect;
                collect.idType = state.GetComponentTypeHandle<MeshInstanceAnimatorID>(true);
                collect.prefabs = writer;
                collect.RunBurstCompatible(__groupToDestroy);*/

                entityManager.RemoveComponent<MeshInstanceAnimatorID>(__groupToDestroy);
            }

            int entityCount = __groupToCreate.CalculateEntityCount();
            if (entityCount > 0)
            {
                var results = new UnsafeListEx<Result>(Allocator.TempJob);

                using (var ids = new NativeArray<MeshInstanceAnimatorID>(entityCount, Allocator.TempJob))
                using(var weightMaskEntities = new UnsafeListEx<Entity>(Allocator.TempJob))
                {
                    state.CompleteDependency();
                    __rigPrefabs.lookupJobManager.CompleteReadOnlyDependency();
                    //prefabs.lookupJobManager.CompleteReadWriteDependency();

                    CollectEx collect;
                    collect.rigPrefabs = __rigPrefabs.reader;
                    collect.rigIDs = state.GetComponentLookup<MeshInstanceRigID>(true);
                    collect.clips = state.GetComponentLookup<MotionClipData>(true);
                    collect.entityParentType = state.GetComponentTypeHandle<EntityParent>(true);
                    collect.instanceType = state.GetComponentTypeHandle<MeshInstanceAnimatorData>(true);
                    collect.rigIDType = state.GetComponentTypeHandle<MeshInstanceRigID>(true); 
                    collect.baseEntityIndexArray = __groupToCreate.CalculateBaseEntityIndexArray(Allocator.TempJob);
                    collect.ids = ids;
                    collect.weightMaskEntities = weightMaskEntities;
                    //collect.prefabs = writer;
                    collect.results = results;

                    collect.Run(__groupToCreate);

                    entityManager.AddComponentDataBurstCompatible(__groupToCreate, ids);
                    entityManager.AddComponentBurstCompatible<MotionClipLayerWeightMask>(weightMaskEntities.AsArray());
                }

                int numResults = results.length;
                for (int i = 0; i < numResults; ++i)
                    entityManager.AddComponent(results.ElementAt(i).entity, __rigComponentTypes);

                Init init;
                init.results = results;
                init.controllers = __controllers.reader;
                init.weightMasks = __weightMasks.reader;
                init.layerWeightMasks = state.GetBufferLookup<MotionClipLayerWeightMask>();
                init.instances = state.GetComponentLookup<AnimatorControllerData>();
                init.clips = state.GetComponentLookup<MotionClipData>();
                init.layers = state.GetBufferLookup<MotionClipLayer>();

                var jobHandle = init.Schedule(results.length, InnerloopBatchCount, state.Dependency);

                int systemID = state.GetSystemID();
                __controllers.AddDependency(systemID, jobHandle);
                __weightMasks.AddDependency(systemID, jobHandle);

                DisposeAll disposeAll;
                disposeAll.results = results;

                state.Dependency = disposeAll.Schedule(jobHandle);
            }
        }
    }

    [BurstCompile, UpdateBefore(typeof(AnimatorControllerSystem))]
    public partial struct MeshInstanceAnimatorParameterSystem : ISystem
    {
        private struct Apply
        {
            [ReadOnly]
            public ComponentLookup<AnimatorControllerData> instances;

            [ReadOnly]
            public BufferAccessor<MeshInstanceRig> rigs;

            public BufferAccessor<MeshInstanceAnimatorParameterCommand> commands;

            [NativeDisableParallelForRestriction]
            public BufferLookup<AnimatorControllerParameter> parameters;

            public void Execute(int index)
            {
                var commands = this.commands[index];
                var rigs = this.rigs[index];
                DynamicBuffer<AnimatorControllerParameter> parameters;
                int numCommands = commands.Length, parameterIndex, paramterLength, i, j;
                bool isContains;
                for(i = 0; i < numCommands; ++i)
                {
                    ref readonly var command = ref commands.ElementAt(i);

                    isContains = false;
                    foreach (var rig in rigs)
                    {
                        if(this.parameters.HasBuffer(rig.entity) && instances.HasComponent(rig.entity))
                        {
                            ref var parametersDefinition = ref instances[rig.entity].definition.Value.parameters;
                            parameterIndex = AnimatorControllerDefinition.Parameter.IndexOf(command.name, ref parametersDefinition);
                            if (parameterIndex != -1)
                            {
                                parameters = this.parameters[rig.entity];
                                paramterLength = parameters.Length;
                                if (paramterLength <= parameterIndex)
                                {
                                    parameters.ResizeUninitialized(parameterIndex + 1);

                                    for (j = paramterLength; j < parameterIndex; ++j)
                                        parameters.ElementAt(j).value = parametersDefinition[j].defaultValue;
                                }

                                parameters.ElementAt(parameterIndex).value = command.value;
                            }

                            isContains = true;
                        }
                    }

                    if (isContains)
                    {
                        commands.RemoveAtSwapBack(i--);
                        --numCommands;
                    }
                }
            }
        }

        [BurstCompile]
        private struct ApplyEx : IJobChunk
        {
            [ReadOnly]
            public ComponentLookup<AnimatorControllerData> instances;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> rigType;

            public BufferTypeHandle<MeshInstanceAnimatorParameterCommand> commandType;

            [NativeDisableParallelForRestriction]
            public BufferLookup<AnimatorControllerParameter> parameters;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Apply apply;
                apply.rigs = chunk.GetBufferAccessor(ref rigType);
                apply.commands = chunk.GetBufferAccessor(ref commandType);
                apply.parameters = parameters;
                apply.instances = instances;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while(iterator.NextEntityIndex(out int i))
                {
                    apply.Execute(i);

                    if (apply.commands.Length < 1)
                        chunk.SetComponentEnabled(ref commandType, i, false);
                }
            }
        }

        private EntityQuery __group;
        private ComponentLookup<AnimatorControllerData> __instances;
        private BufferTypeHandle<MeshInstanceRig> __rigType;
        private BufferTypeHandle<MeshInstanceAnimatorParameterCommand> __commandType;
        private BufferLookup<AnimatorControllerParameter> __parameters;

        public void OnCreate(ref SystemState state)
        {
            __group = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceRig>(),
                        ComponentType.ReadWrite<MeshInstanceAnimatorParameterCommand>()
                    },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __instances = state.GetComponentLookup<AnimatorControllerData>(true);
            __rigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
            __commandType = state.GetBufferTypeHandle<MeshInstanceAnimatorParameterCommand>();
            __parameters = state.GetBufferLookup<AnimatorControllerParameter>();
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            ApplyEx apply;
            apply.instances = __instances.UpdateAsRef(ref state);
            apply.rigType = __rigType.UpdateAsRef(ref state);
            apply.commandType = __commandType.UpdateAsRef(ref state);
            apply.parameters = __parameters.UpdateAsRef(ref state);

            state.Dependency = apply.ScheduleParallelByRef(__group, state.Dependency);
        }
    }
}