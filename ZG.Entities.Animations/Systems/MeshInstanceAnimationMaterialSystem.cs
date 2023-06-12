using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Animation;
using Unity.Burst;
using Unity.Burst.Intrinsics;

namespace ZG
{
    public struct MeshInstanceAnimationMaterialDefinition
    {
        public struct MaterialProperty
        {
            public int typeIndex;
            public BlobArray<int> channelIndices;
        }

        public struct Renderer
        {
            public int rigIndex;

            public int startIndex;
            public int count;

            public BlobArray<MaterialProperty> materialProperties;
        }

        public int instanceID;
        public int materialPropertyCount;
        public int materialPropertySzie;
        public BlobArray<Renderer> renderers;

        public void Apply(
            ref EntityComponentAssigner.ParallelWriter entityManager,
            in SingletonAssetContainer<TypeIndex>.Reader typeIndices,
            in BufferLookup<AnimatedData> animations,
            in ComponentLookup<Rig> rigs, 
            in NativeArray<Entity> instanceRigs, 
            in NativeArray<Entity> instanceRenderers)
        {
            SingletonAssetContainerHandle handle;
            handle.instanceID = instanceID;

            AnimationStream animationStream;
            Entity rigEntity, rendererEntity;
            int i, j, k, numMaterialProperties, numRenderers = this.renderers.Length;
            for (i = 0; i < numRenderers; ++i)
            {
                ref var renderer = ref renderers[i];

                rigEntity = instanceRigs[renderer.rigIndex];

                animationStream = AnimationStream.CreateReadOnly(rigs[rigEntity], animations[rigEntity].AsNativeArray());

                for (j = 0; j < renderer.count; ++j)
                {
                    rendererEntity = instanceRenderers[renderer.startIndex + j];
                    numMaterialProperties = renderer.materialProperties.Length;
                    for (k = 0; k < numMaterialProperties; ++k)
                    {
                        ref var materialProperty = ref renderer.materialProperties[k];

                        handle.index = materialProperty.typeIndex;

                        switch (materialProperty.channelIndices.Length)
                        {
                            case 1:
                                entityManager.SetComponentData(
                                    typeIndices[handle],
                                    rendererEntity,
                                    animationStream.GetFloat(materialProperty.channelIndices[0]));
                                break;
                            case 2:
                                entityManager.SetComponentData(
                                    typeIndices[handle],
                                    rendererEntity,
                                    math.float2(animationStream.GetFloat(materialProperty.channelIndices[0]), animationStream.GetFloat(materialProperty.channelIndices[1])));
                                break;
                            case 3:
                                entityManager.SetComponentData(
                                    typeIndices[handle],
                                    rendererEntity,
                                    math.float3(
                                        animationStream.GetFloat(materialProperty.channelIndices[0]),
                                        animationStream.GetFloat(materialProperty.channelIndices[1]),
                                        animationStream.GetFloat(materialProperty.channelIndices[2])));
                                break;
                            case 4:
                                entityManager.SetComponentData(
                                    typeIndices[handle],
                                    rendererEntity,
                                    math.float4(
                                        animationStream.GetFloat(materialProperty.channelIndices[0]),
                                        animationStream.GetFloat(materialProperty.channelIndices[1]),
                                        animationStream.GetFloat(materialProperty.channelIndices[2]),
                                        animationStream.GetFloat(materialProperty.channelIndices[3])));
                                break;
                        }
                    }
                }
            }
        }
    }

    public struct MeshInstanceAnimationMaterialData : IComponentData
    {
        public BlobAssetReference<MeshInstanceAnimationMaterialDefinition> definition;
    }

    [BurstCompile, 
        CreateAfter(typeof(MeshInstanceRendererSystem)), 
        CreateAfter(typeof(MeshInstanceRigStructChangeSystem)), 
        UpdateInGroup(typeof(AnimationSystemGroup), OrderLast = true)]
    public partial struct MeshInstanceAnimationMaterialSystem : ISystem
    {
        private struct Count
        {
            [ReadOnly]
            public NativeArray<MeshInstanceAnimationMaterialData> instances;

            [NativeDisableParallelForRestriction]
            public NativeArray<int> typeCountAndBufferSize;

            public void Execute(int index)
            {
                ref var defintion = ref instances[index].definition.Value;
                typeCountAndBufferSize.Add(0, defintion.materialPropertyCount);
                typeCountAndBufferSize.Add(1, defintion.materialPropertySzie);
            }
        }

        [BurstCompile]
        private struct CountEx : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceAnimationMaterialData> instanceType;

            [NativeDisableParallelForRestriction]
            public NativeArray<int> typeCountAndBufferSize;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Count count;
                count.instances = chunk.GetNativeArray(ref instanceType);
                count.typeCountAndBufferSize = typeCountAndBufferSize;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    count.Execute(i);
            }
        }

        private struct Apply
        {
            [ReadOnly]
            public SingletonAssetContainer<TypeIndex>.Reader typeIndices;

            [ReadOnly]
            public BufferLookup<AnimatedData> animations;

            [ReadOnly]
            public BufferLookup<MeshInstanceRig> instanceRigMap;

            [ReadOnly]
            public ComponentLookup<Rig> rigs;

            [ReadOnly]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeArray<MeshInstanceAnimationMaterialData> instances;

            [ReadOnly]
            public BufferAccessor<MeshInstanceRig> instanceRigs;

            [ReadOnly]
            public BufferAccessor<MeshInstanceNode> instanceRenderers;

            [ReadOnly]
            public BufferAccessor<EntityParent> entityParents;

            [ReadOnly]
            public SharedHashMap<Entity, MeshInstanceRendererBuilder>.Reader rendererBuilders;

            public EntityComponentAssigner.ParallelWriter entityManager;

            public void Execute(int index)
            {
                if (rendererBuilders.ContainsKey(entityArray[index]))
                    return;

                DynamicBuffer<MeshInstanceRig> instanceRigs;
                if (index < this.instanceRigs.Length)
                    instanceRigs = this.instanceRigs[index];
                else
                {
                    Entity rigEntity = EntityParent.Get(entityParents[index], instanceRigMap);
                    if (rigEntity == Entity.Null)
                        return;

                    instanceRigs = instanceRigMap[rigEntity];
                }

                instances[index].definition.Value.Apply(
                    ref entityManager,
                    typeIndices,
                    animations,
                    rigs,
                    instanceRigs.Reinterpret<Entity>().AsNativeArray(),
                    instanceRenderers[index].Reinterpret<Entity>().AsNativeArray());
            }
        }

        [BurstCompile]
        public struct ApplyEx : IJobChunk
        {
            [ReadOnly]
            public SingletonAssetContainer<TypeIndex>.Reader typeIndices;

            [ReadOnly]
            public BufferLookup<AnimatedData> animations;

            [ReadOnly]
            public BufferLookup<MeshInstanceRig> instanceRigs;

            [ReadOnly]
            public ComponentLookup<Rig> rigs;

            [ReadOnly]
            public EntityTypeHandle entityType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceAnimationMaterialData> instanceType;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> instanceRigType;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceNode> instanceRendererType;

            [ReadOnly]
            public BufferTypeHandle<EntityParent> entityParentType;

            [ReadOnly]
            public SharedHashMap<Entity, MeshInstanceRendererBuilder>.Reader rendererBuilders;

            public EntityComponentAssigner.ParallelWriter entityManager;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Apply apply;
                apply.typeIndices = typeIndices;
                apply.animations = animations;
                apply.instanceRigMap = instanceRigs;
                apply.rigs = rigs;
                apply.entityArray = chunk.GetNativeArray(entityType);
                apply.instances = chunk.GetNativeArray(ref instanceType);
                apply.instanceRigs = chunk.GetBufferAccessor(ref instanceRigType);
                apply.instanceRenderers = chunk.GetBufferAccessor(ref instanceRendererType);
                apply.entityParents = chunk.GetBufferAccessor(ref entityParentType);
                apply.rendererBuilders = rendererBuilders;
                apply.entityManager = entityManager;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    apply.Execute(i);
            }
        }

        private EntityQuery __group;

        private BufferLookup<AnimatedData> __animations;

        private BufferLookup<MeshInstanceRig> __instanceRigs;

        private ComponentLookup<Rig> __rigs;

        private EntityTypeHandle __entityType;

        private ComponentTypeHandle<MeshInstanceAnimationMaterialData> __instanceType;

        private BufferTypeHandle<MeshInstanceRig> __instanceRigType;

        private BufferTypeHandle<MeshInstanceNode> __instanceRendererType;

        private BufferTypeHandle<EntityParent> __entityParentType;

        private EntityComponentAssigner __assigner;

        private SharedHashMap<Entity, MeshInstanceRendererBuilder> __rendererBuilders;

        private SingletonAssetContainer<TypeIndex> __typeIndcies;
        private NativeArray<int> __typeCountAndBufferSize;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAll<MeshInstanceAnimationMaterialData, MeshInstanceRig, MeshInstanceNode>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            __animations = state.GetBufferLookup<AnimatedData>(true);
            __instanceRigs = state.GetBufferLookup<MeshInstanceRig>(true);
            __rigs = state.GetComponentLookup<Rig>(true);
            __instanceType = state.GetComponentTypeHandle<MeshInstanceAnimationMaterialData>(true);
            __instanceRigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
            __instanceRendererType = state.GetBufferTypeHandle<MeshInstanceNode>(true);
            __entityParentType = state.GetBufferTypeHandle<EntityParent>(true);

            __assigner = state.WorldUnmanaged.GetExistingSystemUnmanaged<MeshInstanceRigStructChangeSystem>().assigner;

            __rendererBuilders = state.WorldUnmanaged.GetExistingSystemUnmanaged<MeshInstanceRendererSystem>().builders;

            __typeIndcies = SingletonAssetContainer<TypeIndex>.Retain();

            __typeCountAndBufferSize = new NativeArray<int>(2, Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __typeCountAndBufferSize.Dispose();

            __typeIndcies.Release();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var instanceType = __instanceType.UpdateAsRef(ref state);
            __typeCountAndBufferSize.MemClear();

            CountEx count;
            count.instanceType = __instanceType;
            count.typeCountAndBufferSize = __typeCountAndBufferSize;
            var jobHandle = count.ScheduleParallelByRef(__group, state.Dependency);

            ApplyEx apply;
            apply.typeIndices = __typeIndcies.reader;
            apply.entityType = __entityType.UpdateAsRef(ref state);
            apply.animations = __animations.UpdateAsRef(ref state);
            apply.instanceRigs = __instanceRigs.UpdateAsRef(ref state);
            apply.rigs = __rigs.UpdateAsRef(ref state);
            apply.instanceType = instanceType;
            apply.instanceRigType = __instanceRigType.UpdateAsRef(ref state);
            apply.instanceRendererType = __instanceRendererType.UpdateAsRef(ref state);
            apply.entityParentType = __entityParentType.UpdateAsRef(ref state);
            apply.rendererBuilders = __rendererBuilders.reader;
            apply.entityManager = __assigner.AsParallelWriter(__typeCountAndBufferSize, ref jobHandle);

            ref var lookupJobManager = ref __rendererBuilders.lookupJobManager;

            jobHandle = apply.ScheduleParallelByRef(__group, Unity.Jobs.JobHandle.CombineDependencies(lookupJobManager.readOnlyJobHandle, jobHandle));

            __typeIndcies.AddDependency(state.GetSystemID(), jobHandle);

            lookupJobManager.AddReadOnlyDependency(jobHandle);

            state.Dependency = jobHandle;
        }
    }
}