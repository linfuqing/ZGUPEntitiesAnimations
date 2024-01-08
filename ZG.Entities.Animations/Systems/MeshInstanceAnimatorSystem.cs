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

    [BurstCompile, 
        UpdateInGroup(typeof(MeshInstanceSystemGroup)), 
        UpdateAfter(typeof(MeshInstanceRigSystem))]
    public partial struct MeshInstanceAnimatorSystem : ISystem
    {
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
            public ComponentLookup<MotionClipData> clips;

            [ReadOnly]
            public ComponentLookup<MeshInstanceRigID> rigIDMap;

            [ReadOnly]
            public BufferLookup<MeshInstanceRig> rigMap;

            [ReadOnly]
            public BufferAccessor<MeshInstanceRig> rigs;

            [ReadOnly]
            public NativeArray<MeshInstanceRigID> rigIDs;

            [ReadOnly]
            public NativeArray<MeshInstanceAnimatorData> instances;

            [ReadOnly]
            public BufferAccessor<EntityParent> entityParents;

            public NativeList<Entity> weightMaskEntities;

            public NativeList<Result> results;

            public int Execute(int index)
            {
                Result result;
                result.definition = instances[index].definition;

                ref var definition = ref result.definition.Value;

                DynamicBuffer<MeshInstanceRig> rigs;
                if (index < this.rigs.Length)
                {
                    rigs = this.rigs[index];

                    result.rigInstanceID = rigIDs[index].value;
                }
                else
                {
                    Entity parentEntity = EntityParent.Get(entityParents[index], rigMap);
                    if (parentEntity == Entity.Null)
                        return definition.instanceID;

                    rigs = this.rigMap[parentEntity];
                    result.rigInstanceID = rigIDMap[parentEntity].value;
                }

                int numRigs = definition.rigs.Length, numResults, i, j;
                for (i = 0; i < numRigs; ++i)
                {
                    ref var rig = ref definition.rigs[i];
                    result.entity = rigs[rig.index].entity;
                    if (clips.HasComponent(result.entity))
                        continue;

                    numResults = results.Length;
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
            public ComponentLookup<MotionClipData> clips;

            [ReadOnly]
            public ComponentLookup<MeshInstanceRigID> rigIDs;

            [ReadOnly]
            public BufferLookup<MeshInstanceRig> rigs;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> rigType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceRigID> rigIDType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceAnimatorData> instanceType;

            [ReadOnly]
            public BufferTypeHandle<EntityParent> entityParentType;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<int> baseEntityIndexArray;

            public NativeArray<MeshInstanceAnimatorID> ids;

            public NativeList<Entity> weightMaskEntities;

            //public SharedHashMap<int, Prefab>.Writer prefabs;

            public NativeList<Result> results;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Collect collect;
                collect.clips = clips;
                collect.rigIDMap = rigIDs;
                collect.rigMap = rigs;
                collect.rigs = chunk.GetBufferAccessor(ref rigType);
                collect.rigIDs = chunk.GetNativeArray(ref rigIDType);
                collect.instances = chunk.GetNativeArray(ref instanceType);
                collect.entityParents = chunk.GetBufferAccessor(ref entityParentType);
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
            public NativeArray<Result> results;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<AnimatorControllerDefinition>>.Reader controllers;

            [ReadOnly]
            public SingletonAssetContainer<BlobAssetReference<MotionClipWeightMaskDefinition>>.Reader weightMasks;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<MotionClipData> clips;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<AnimatorControllerData> instances;

            [NativeDisableParallelForRestriction]
            public BufferLookup<MotionClipLayerWeightMask> layerWeightMasks;

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

        public static readonly int InnerloopBatchCount = 1;

        private ComponentTypeSet __rigComponentTypes;

        private EntityQuery __groupToDestroy;
        private EntityQuery __groupToCreate;

        private ComponentLookup<MotionClipData> __clips;

        private ComponentLookup<MeshInstanceRigID> __rigIDs;

        private BufferLookup<MeshInstanceRig> __rigs;

        private BufferTypeHandle<MeshInstanceRig> __rigType;

        private ComponentTypeHandle<MeshInstanceRigID> __rigIDType;

        private ComponentTypeHandle<MeshInstanceAnimatorData> __instanceType;

        private BufferTypeHandle<EntityParent> __entityParentType;

        private BufferLookup<MotionClipLayerWeightMask> __layerWeightMasks;

        private BufferLookup<MotionClipLayer> __layers;

        private ComponentLookup<AnimatorControllerData> __instances;

        private SingletonAssetContainer<BlobAssetReference<AnimatorControllerDefinition>> __controllers;
        private SingletonAssetContainer<BlobAssetReference<MotionClipWeightMaskDefinition>> __weightMasks;

        /*public SharedHashMap<int, Prefab> prefabs
        {
            get;

            private set;
        }*/

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __rigComponentTypes = new ComponentTypeSet(new FixedList128Bytes<ComponentType>()
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

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToDestroy = builder
                    .WithAll<MeshInstanceAnimatorID>()
                    .WithNone<MeshInstanceRig>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToCreate = builder
                        .WithAll<MeshInstanceAnimatorData, MeshInstanceRig>()
                        .WithNone<MeshInstanceAnimatorID>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);

            __clips = state.GetComponentLookup<MotionClipData>();

            __rigIDs = state.GetComponentLookup<MeshInstanceRigID>(true);
            __rigs = state.GetBufferLookup<MeshInstanceRig>(true);
            __rigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
            __rigIDType = state.GetComponentTypeHandle<MeshInstanceRigID>(true);
            __instanceType = state.GetComponentTypeHandle<MeshInstanceAnimatorData>(true);
            __rigIDType = state.GetComponentTypeHandle<MeshInstanceRigID>(true);
            __entityParentType = state.GetBufferTypeHandle<EntityParent>(true);
            __layerWeightMasks = state.GetBufferLookup<MotionClipLayerWeightMask>();
            __layers = state.GetBufferLookup<MotionClipLayer>();
            __instances = state.GetComponentLookup<AnimatorControllerData>();

            __controllers = SingletonAssetContainer<BlobAssetReference<AnimatorControllerDefinition>>.Retain();
            __weightMasks = SingletonAssetContainer<BlobAssetReference<MotionClipWeightMaskDefinition>>.Retain();

            //prefabs = new SharedHashMap<int, Prefab>(Allocator.Persistent);
        }

        //[BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __controllers.Release();
            __weightMasks.Release();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //var writer = prefabs.writer;

            var entityManager = state.EntityManager;

            if (!__groupToDestroy.IsEmptyIgnoreFilter)
                entityManager.RemoveComponent<MeshInstanceAnimatorID>(__groupToDestroy);

            int entityCount = __groupToCreate.CalculateEntityCount();
            if (entityCount > 0)
            {
                var results = new NativeList<Result>(Allocator.TempJob);

                using (var ids = new NativeArray<MeshInstanceAnimatorID>(entityCount, Allocator.TempJob))
                using(var weightMaskEntities = new NativeList<Entity>(Allocator.TempJob))
                {
                    state.CompleteDependency();

                    CollectEx collect;
                    collect.clips = __clips.UpdateAsRef(ref state);
                    collect.rigIDs = __rigIDs.UpdateAsRef(ref state);
                    collect.rigs = __rigs.UpdateAsRef(ref state);
                    collect.rigType = __rigType.UpdateAsRef(ref state);
                    collect.rigIDType = __rigIDType.UpdateAsRef(ref state);
                    collect.instanceType = __instanceType.UpdateAsRef(ref state);
                    collect.entityParentType = __entityParentType.UpdateAsRef(ref state);
                    collect.baseEntityIndexArray = __groupToCreate.CalculateBaseEntityIndexArray(Allocator.TempJob);
                    collect.ids = ids;
                    collect.weightMaskEntities = weightMaskEntities;
                    //collect.prefabs = writer;
                    collect.results = results;

                    collect.RunByRef(__groupToCreate);

                    entityManager.AddComponentData(__groupToCreate, ids);
                    entityManager.AddComponent<MotionClipLayerWeightMask>(weightMaskEntities.AsArray());
                }

                int numResults = results.Length;
                for (int i = 0; i < numResults; ++i)
                    entityManager.AddComponent(results.ElementAt(i).entity, __rigComponentTypes);

                Init init;
                init.results = results.AsArray();
                init.controllers = __controllers.reader;
                init.weightMasks = __weightMasks.reader;
                init.clips = __clips.UpdateAsRef(ref state);
                init.instances = __instances.UpdateAsRef(ref state);
                init.layerWeightMasks = __layerWeightMasks.UpdateAsRef(ref state);
                init.layers = __layers.UpdateAsRef(ref state);

                var jobHandle = init.ScheduleByRef(results.Length, InnerloopBatchCount, state.Dependency);

                int systemID = state.GetSystemID();
                __controllers.AddDependency(systemID, jobHandle);
                __weightMasks.AddDependency(systemID, jobHandle);

                state.Dependency = results.Dispose(jobHandle);
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
                StringHash name;
                int parameterIndex, paramterLength, i, j;
                bool isContains;
                for(i = commands.Length - 1; i >= 0; --i)
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
                        name = command.name;

                        commands.RemoveAt(i);

                        for (j = i - 1; j >= 0; --j)
                        {
                            if (commands.ElementAt(j).name == name)
                            {
                                commands.RemoveAt(j);

                                --i;
                            }
                        }
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

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAll<MeshInstanceRig>()
                        .WithAllRW<MeshInstanceAnimatorParameterCommand>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);

            __instances = state.GetComponentLookup<AnimatorControllerData>(true);
            __rigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
            __commandType = state.GetBufferTypeHandle<MeshInstanceAnimatorParameterCommand>();
            __parameters = state.GetBufferLookup<AnimatorControllerParameter>();
        }

        [BurstCompile]
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