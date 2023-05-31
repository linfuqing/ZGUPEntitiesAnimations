using System;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Deformations;
using Unity.Rendering;
using Unity.Animation;

namespace ZG
{
    public enum MeshInstanceSkeletonFlag
    {
        BlendShapeWeightsContiguous = 0x01
    }

    public struct MeshInstanceSkeletonPrefab
    {
        public int instanceCount;
        public BlobArray<Entity> entities;
    }

    public struct MeshInstanceSkeletonDefinition
    {
        public struct Bone
        {
            public int rigIndex;
            public int skinMeshIndex;
            public float4x4 bindPose;
            public float4x4 matrix;
        }

        public struct IndirectBone
        {
            public Bone value;

            public RigidTransform offset;
        }

        public struct BlendShape
        {
            public int index;
            public int rigIndex;
        }

        public struct Skeleton
        {
            public MeshInstanceSkeletonFlag flag;
            public int rootBoneIndex;
            public BlobArray<Bone> bones;
            public BlobArray<IndirectBone> indirectBones;
            public BlobArray<BlendShape> blendShapes;
            public BlobArray<float> blendShapeWeights;
        }

        public struct Instance
        {
            public int skeletionIndex;
            public int rigIndex;
            public float4x4 localToRoot;
            public BlobArray<int> rendererIndices;
        }

        public int instanceID;
        public BlobArray<Skeleton> skeletons;
        public BlobArray<Instance> instances;
    }

    public struct MeshInstanceSkeletonData : IComponentData
    {
        public BlobAssetReference<MeshInstanceSkeletonDefinition> definition;
    }

    public struct MeshInstanceSkeletonID : ICleanupComponentData
    {
        public int value;
    }

    public struct MeshInstanceSkeleton : ICleanupBufferElementData
    {
        public Entity entity;
    }

    /*public struct MeshInstanceSkeletonDisabled : IComponentData
    {
    }*/

    [BurstCompile]
    internal struct MeshInstanceSkeletonResizeAndInitAnimations : IJob
    {
        public UnsafeParallelHashMap<Entity, int> rigCounts;

        public BufferLookup<AnimatedLocalToRoot> animatedLocalToRoots;

        public BufferLookup<AnimatedData> animatedDatas;

        [ReadOnly]
        public ComponentLookup<Rig> rigs;

        public void Execute()
        {
            int i, boneCount;
            Entity entity;
            Rig rig;
            AnimationStream animationStream;
            AnimatedLocalToRoot animatedLocalToRoot;
            DynamicBuffer<AnimatedLocalToRoot> animatedLocalToRoots;
            DynamicBuffer<AnimatedData> animatedDatas;
            KeyValue<Entity, int> pair;
            var enumerator = rigCounts.GetEnumerator();
            while (enumerator.MoveNext())
            {
                pair = enumerator.Current;
                entity = pair.Key;

                if (rigs.HasComponent(entity) && this.animatedDatas.HasBuffer(entity))
                {
                    rig = rigs[entity];
                    animatedDatas =  this.animatedDatas[entity];
                    //animatedDatas.ResizeUninitialized(rig.Value.Value.Bindings.StreamSize);
                    animationStream = AnimationStream.Create(rig, animatedDatas.AsNativeArray());
                    animationStream.ResetToDefaultValues();

                    boneCount = animationStream.Rig.Value.Skeleton.BoneCount;

                    animatedLocalToRoots = this.animatedLocalToRoots[entity];
                    animatedLocalToRoots.ResizeUninitialized(boneCount);

                    for (i = 0; i < boneCount; ++i)
                    {
                        animatedLocalToRoot.Value = animationStream.GetLocalToRootMatrix(i);

                        animatedLocalToRoots[i] = animatedLocalToRoot;
                    }
                }
            }

            rigCounts.Dispose();
        }
    }

    [BurstCompile, UpdateInGroup(typeof(MeshInstanceSystemGroup), OrderFirst = true), UpdateAfter(typeof(MeshInstanceRigFactorySystem))]
    public partial struct MeshInstanceSkeletonFactorySystem : ISystem
    {
        [Flags]
        private enum ResultType
        {
            Skinning = 0x01, 
            BlendShape = 0x02,
            BlendShapeWeightsContiguous = 0x04,
            BlendShapeWeights = 0x08
        }

        private struct Result
        {
            public ResultType type;
            public int index;
            public int instanceID;
            public Entity entity;
        }

        private struct CollectToDestroy
        {
            [ReadOnly]
            public NativeArray<MeshInstanceSkeletonID> ids;

            public SharedHashMap<int, BlobAssetReference<MeshInstanceSkeletonPrefab>>.Writer prefabs;

            public NativeList<Entity> results;

            public void Execute(int index)
            {
                int id = ids[index].value;
                var prefabAsset = prefabs[id];
                ref var prefab = ref prefabAsset.Value;
                if (--prefab.instanceCount > 0)
                    return;

                int numEntities = prefab.entities.Length;
                for (int i = 0; i < numEntities; ++i)
                    results.Add(prefab.entities[i]);

                prefabAsset.Dispose();

                prefabs.Remove(id);
            }
        }

        [BurstCompile]
        private struct CollectToDestroyEx : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceSkeletonID> idType;

            public SharedHashMap<int, BlobAssetReference<MeshInstanceSkeletonPrefab>>.Writer prefabs;

            public NativeList<Entity> results;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                CollectToDestroy collectToDestroy;
                collectToDestroy.ids = chunk.GetNativeArray(ref idType);
                collectToDestroy.prefabs = prefabs;
                collectToDestroy.results = results;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    collectToDestroy.Execute(i);
            }
        }

        private struct CollectToCreate
        {
            [ReadOnly]
            public BufferLookup<AnimatedLocalToRoot> animatedLocalToRoots;

            [ReadOnly]
            public BufferLookup<MeshInstanceRig> rigInstances;

            [ReadOnly]
            public ComponentLookup<DeformedEntity> deformedEntities;

            /*[ReadOnly]
            public ComponentLookup<SkinningTag> skinningTags;

            [ReadOnly]
            public ComponentLookup<BlendShapeTag> blendShapeTags;*/

            [ReadOnly]
            public ComponentLookup<MeshInstanceRigID> rigIDMap;

            [ReadOnly]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeArray<MeshInstanceSkeletonData> instances;

            [ReadOnly]
            public NativeArray<MeshInstanceRigID> rigIDs;

            [ReadOnly]
            public NativeArray<MeshInstanceRendererID> rendererIDs;

            [ReadOnly]
            public BufferAccessor<MeshInstanceNode> rendererInstances;

            [ReadOnly]
            public BufferAccessor<EntityParent> entityParents;

            [ReadOnly]
            public SharedHashMap<Entity, MeshInstanceRendererBuilder>.Reader rendererBuilders;

            [ReadOnly]
            public SharedHashMap<int, MeshInstanceRendererPrefabBuilder>.Reader rendererPrefabBuilders;

            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRendererPrefab>>.Reader renderers;

            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Reader rigs;

            public SharedHashMap<int, BlobAssetReference<MeshInstanceSkeletonPrefab>>.Writer prefabs;

            public NativeArray<int> entityCount;

            public NativeArray<Entity> entities;

            public NativeList<Result> results;

            //public NativeParallelHashMap<Entity, int> skinningEntities;

            //public NativeParallelHashMap<Entity, int> blendShapeEntities;

            public NativeParallelHashMap<Entity, int> rendererEntities;

            public UnsafeParallelHashMap<Entity, int> rigCounts;

            public void Execute(int index)
            {
                int rendererInstanceID = rendererIDs[index].value;
                if (rendererPrefabBuilders.ContainsKey(rendererInstanceID))
                    return;

                Entity entity = entityArray[index];
                if (rendererBuilders.ContainsKey(entity))
                    return;

                int rigInstanceID;
                DynamicBuffer<MeshInstanceRig> rigInstances;
                if (index < rigIDs.Length)
                {
                    rigInstanceID = rigIDs[index].value;

                    rigInstances = this.rigInstances.HasBuffer(entity) ? this.rigInstances[entity] : default;
                }
                else if (index < entityParents.Length)
                {
                    Entity entityParent = EntityParent.Get(entityParents[index], rigIDMap);
                    if (entityParent == Entity.Null)
                        return;

                    rigInstanceID = rigIDMap[entityParent].value;

                    rigInstances = this.rigInstances.HasBuffer(entityParent) ? this.rigInstances[entityParent] : default;
                }
                else
                    return;

                ref var definition = ref instances[index].definition.Value;
                int numInstances = definition.instances.Length;
                bool isCreated;
                if (prefabs.TryGetValue(definition.instanceID, out var prefab))
                {
                    ++prefab.Value.instanceCount;

                    isCreated = false;
                }
                else
                {
                    using (var blobBuilder = new BlobBuilder(Allocator.Temp))
                    {
                        ref var root = ref blobBuilder.ConstructRoot<MeshInstanceSkeletonPrefab>();
                        root.instanceCount = 1;
                        blobBuilder.Allocate(ref root.entities, numInstances);

                        prefab = blobBuilder.CreateBlobAssetReference<MeshInstanceSkeletonPrefab>(Allocator.Persistent);
                    }

                    prefabs[definition.instanceID] = prefab;

                    isCreated = true;
                }

                Result result;
                result.entity = entity;
                result.instanceID = definition.instanceID;

                var rendererInstances = index < this.rendererInstances.Length ? this.rendererInstances[index] : default;

                int rendererCount = rendererInstances.IsCreated ? rendererInstances.Length : 0, 
                    rendererIndex, rigCount;
                ref var renderer = ref renderers[rendererInstanceID].Value;
                ref var rig = ref rigs[rigInstanceID].Value;
                Entity entityTemp;
                int numRendererIndices, i, j;
                for (i = 0; i < numInstances; ++i)
                {
                    ref var temp = ref definition.instances[i];

                    entityTemp = rig.rigs[temp.rigIndex].entity;
                    if (!animatedLocalToRoots.HasBuffer(entityTemp))
                    {
                        if (rigCounts.TryGetValue(entityTemp, out rigCount))
                            ++rigCount;
                        else
                            rigCount = 1;

                        rigCounts[entityTemp] = rigCount;
                    }

                    if(rigInstances.IsCreated)
                    {
                        entityTemp = rigInstances[temp.rigIndex].entity;
                        if (!animatedLocalToRoots.HasBuffer(entityTemp))
                        {
                            if (rigCounts.TryGetValue(entityTemp, out rigCount))
                                ++rigCount;
                            else
                                rigCount = 1;

                            rigCounts[entityTemp] = rigCount;
                        }
                    }

                    result.index = i;
                    result.type = 0;

                    ref var skeleton = ref definition.skeletons[temp.skeletionIndex];

                    if (skeleton.bones.Length > 0 || skeleton.indirectBones.Length > 0)
                    {
                        result.type |= ResultType.Skinning;

                        numRendererIndices = temp.rendererIndices.Length;
                        for (j = 0; j < numRendererIndices; ++j)
                        {
                            rendererIndex = temp.rendererIndices[j];

                            ref var rendererEntity = ref renderer.nodes[rendererIndex];
                            //if (!skinningTags.HasComponent(rendererEntity))
                            {
                                //skinningEntities.TryAdd(rendererEntity, rendererIndex);

                                if (!deformedEntities.HasComponent(rendererEntity))
                                    rendererEntities.TryAdd(rendererEntity, rendererIndex);
                            }

                            if(rendererInstances.IsCreated && rendererIndex < rendererCount)
                            {
                                entityTemp = rendererInstances[rendererIndex].entity;
                                //if (!skinningTags.HasComponent(entityTemp))
                                {
                                    //skinningEntities.TryAdd(entityTemp, rendererIndex);

                                    if (!deformedEntities.HasComponent(entityTemp))
                                        rendererEntities.TryAdd(entityTemp, rendererIndex);
                                }
                            }
                        }
                    }

                    if (skeleton.blendShapes.Length > 0)
                    {
                        if ((definition.skeletons[temp.skeletionIndex].flag & MeshInstanceSkeletonFlag.BlendShapeWeightsContiguous) == MeshInstanceSkeletonFlag.BlendShapeWeightsContiguous)
                            result.type |= ResultType.BlendShapeWeightsContiguous;
                        else
                            result.type |= ResultType.BlendShape;

                        numRendererIndices = temp.rendererIndices.Length;
                        for (j = 0; j < numRendererIndices; ++j)
                        {
                            rendererIndex = temp.rendererIndices[j];

                            ref var rendererEntity = ref renderer.nodes[rendererIndex];

                            //if (!blendShapeTags.HasComponent(rendererEntity))
                            {
                                //blendShapeEntities.TryAdd(rendererEntity, rendererIndex);

                                if (!deformedEntities.HasComponent(rendererEntity))
                                    rendererEntities.TryAdd(rendererEntity, rendererIndex);
                            }

                            if (rendererInstances.IsCreated && rendererIndex < rendererCount)
                            {
                                entityTemp = rendererInstances[rendererIndex].entity;
                                //if (!blendShapeTags.HasComponent(entityTemp))
                                {
                                    //blendShapeEntities.TryAdd(entityTemp, rendererIndex);

                                    if (!deformedEntities.HasComponent(entityTemp))
                                        rendererEntities.TryAdd(entityTemp, rendererIndex);
                                }
                            }
                        }
                    }

                    if (skeleton.blendShapeWeights.Length > 0)
                        result.type |= ResultType.BlendShapeWeights;

                    if(isCreated)
                        results.Add(result);
                }

                int entityCount = this.entityCount[0];

                entities[entityCount++] = entity;

                this.entityCount[0] = entityCount;
            }
        }

        [BurstCompile]
        private struct CollectToCreateEx : IJobChunk
        {
            [ReadOnly]
            public BufferLookup<AnimatedLocalToRoot> animatedLocalToRoots;

            [ReadOnly]
            public BufferLookup<MeshInstanceRig> rigInstances;

            [ReadOnly]
            public ComponentLookup<DeformedEntity> deformedEntities;

            /*[ReadOnly]
            public ComponentLookup<SkinningTag> skinningTags;

            [ReadOnly]
            public ComponentLookup<BlendShapeTag> blendShapeTags;*/

            [ReadOnly]
            public ComponentLookup<MeshInstanceRigID> rigIDs;

            [ReadOnly]
            public EntityTypeHandle entityType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceSkeletonData> instanceType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceRigID> rigIDType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceRendererID> rendererIDType;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceNode> rendererInstanceType;

            [ReadOnly]
            public BufferTypeHandle<EntityParent> entityParentType;

            [ReadOnly]
            public SharedHashMap<Entity, MeshInstanceRendererBuilder>.Reader rendererBuilders;

            [ReadOnly]
            public SharedHashMap<int, MeshInstanceRendererPrefabBuilder>.Reader rendererPrefabBuilders;

            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRendererPrefab>>.Reader renderers;

            [ReadOnly]
            public SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>>.Reader rigs;

            public SharedHashMap<int, BlobAssetReference<MeshInstanceSkeletonPrefab>>.Writer prefabs;

            public NativeArray<int> entityCount;

            public NativeArray<Entity> entities;

            public NativeList<Result> results;

            //public NativeParallelHashMap<Entity, int> skinningEntities;

            //public NativeParallelHashMap<Entity, int> blendShapeEntities;

            public NativeParallelHashMap<Entity, int> rendererEntities;

            public UnsafeParallelHashMap<Entity, int> rigCounts;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                CollectToCreate collectToCreate;
                collectToCreate.animatedLocalToRoots = animatedLocalToRoots;
                collectToCreate.rigInstances = rigInstances;
                collectToCreate.deformedEntities = deformedEntities;
                //collectToCreate.skinningTags = skinningTags;
                //collectToCreate.blendShapeTags = blendShapeTags;
                collectToCreate.rigIDMap = rigIDs;
                collectToCreate.entityArray = chunk.GetNativeArray(entityType);
                collectToCreate.instances = chunk.GetNativeArray(ref instanceType);
                collectToCreate.rigIDs = chunk.GetNativeArray(ref rigIDType);
                collectToCreate.rendererIDs = chunk.GetNativeArray(ref rendererIDType);
                collectToCreate.rendererInstances = chunk.GetBufferAccessor(ref rendererInstanceType);
                collectToCreate.entityParents = chunk.GetBufferAccessor(ref entityParentType);
                collectToCreate.rendererBuilders = rendererBuilders; 
                collectToCreate.rendererPrefabBuilders = rendererPrefabBuilders;
                collectToCreate.renderers = renderers;
                collectToCreate.rigs = rigs;
                collectToCreate.prefabs = prefabs;
                collectToCreate.entityCount = entityCount;
                collectToCreate.entities = entities;
                collectToCreate.results = results;
                //collectToCreate.skinningEntities = skinningEntities;
                //collectToCreate.blendShapeEntities = blendShapeEntities;
                collectToCreate.rendererEntities = rendererEntities;
                collectToCreate.rigCounts = rigCounts;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    collectToCreate.Execute(i);
            }
        }

        [BurstCompile]
        private struct CopyIDs : IJobParallelFor
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<int> entityCount;

            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public ComponentLookup<MeshInstanceSkeletonData> instances;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<MeshInstanceSkeletonID> ids;

            public void Execute(int index)
            {
                Entity entity = entityArray[index];

                MeshInstanceSkeletonID id;
                id.value = instances[entity].definition.Value.instanceID;
                ids[entity] = id;
            }
        }

        [BurstCompile]
        private struct Init : IJobParallelFor
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeList<Result> results;

            [ReadOnly]
            public ComponentLookup<MeshInstanceSkeletonData> instances;

            [NativeDisableParallelForRestriction]
            public BufferLookup<SkinnedMeshToRigIndexMapping> skinnedMeshToRigIndexMappings;
            [NativeDisableParallelForRestriction]
            public BufferLookup<SkinnedMeshToRigIndexIndirectMapping> skinnedMeshToRigIndexIndirectMappings;
            [NativeDisableParallelForRestriction]
            public BufferLookup<BindPose> bindPoses;
            [NativeDisableParallelForRestriction]
            public BufferLookup<SkinMatrix> skinMatrices;

            [NativeDisableParallelForRestriction]
            public BufferLookup<BlendShapeWeight> blendShapeWeights;

            [NativeDisableParallelForRestriction]
            public BufferLookup<BlendShapeToRigIndexMapping> blendShapeToRigIndexMappings;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<BlendShapeChunkMapping> blendShapeChunkMappings;

            public void Execute(int index)
            {
                Entity entity = entityArray[index];
                var result = results[index];
                ref var definition = ref instances[result.entity].definition.Value;

                ref var temp = ref definition.instances[result.index];

                ref var skeleton = ref definition.skeletons[temp.skeletionIndex];

                int numDirectBones = skeleton.bones.Length;
                int numIndirectBones = skeleton.indirectBones.Length;

                int numBones = numDirectBones + numIndirectBones;

                DynamicBuffer<BindPose> bindPoses;
                DynamicBuffer<SkinMatrix> skinMatrices;
                if (numBones > 0)
                {
                    bindPoses = this.bindPoses[entity];
                    bindPoses.ResizeUninitialized(numBones);

                    skinMatrices = this.skinMatrices[entity];
                    skinMatrices.ResizeUninitialized(numBones);
                }
                else
                {
                    bindPoses = default;
                    skinMatrices = default;
                }

                if (numDirectBones > 0)
                {
                    var skinnedMeshToRigIndexMappings = this.skinnedMeshToRigIndexMappings[entity];
                    skinnedMeshToRigIndexMappings.ResizeUninitialized(numDirectBones);

                    SkinnedMeshToRigIndexMapping skinnedMeshToRigIndexMapping;
                    for (int i = 0; i < numDirectBones; ++i)
                    {
                        ref var bone = ref skeleton.bones[i];

                        skinnedMeshToRigIndexMapping.RigIndex = bone.rigIndex;
                        skinnedMeshToRigIndexMapping.SkinMeshIndex = bone.skinMeshIndex;
                        skinnedMeshToRigIndexMappings[i] = skinnedMeshToRigIndexMapping;
                    }
                }

                if (numIndirectBones > 0)
                {
                    var skinnedMeshToRigIndexIndirectMappings = this.skinnedMeshToRigIndexIndirectMappings[entity];
                    skinnedMeshToRigIndexIndirectMappings.ResizeUninitialized(numIndirectBones);

                    SkinnedMeshToRigIndexIndirectMapping skinnedMeshToRigIndexIndirectMapping;
                    for (int i = 0; i < numIndirectBones; ++i)
                    {
                        ref var bone = ref skeleton.indirectBones[i];

                        skinnedMeshToRigIndexIndirectMapping.RigIndex = bone.value.rigIndex;
                        skinnedMeshToRigIndexIndirectMapping.SkinMeshIndex = bone.value.skinMeshIndex;
                        skinnedMeshToRigIndexIndirectMapping.Offset.rs = math.float3x3(bone.offset.rot);
                        skinnedMeshToRigIndexIndirectMapping.Offset.t = bone.offset.pos;
                        skinnedMeshToRigIndexIndirectMappings[i] = skinnedMeshToRigIndexIndirectMapping;
                    }
                }

                BindPose bindPose;
                SkinMatrix skinMatrix;
                float4x4 matrix;
                for (int i = 0; i < numDirectBones; ++i)
                {
                    ref var bone = ref skeleton.bones[i];

                    bindPose.Value = bone.bindPose;
                    bindPoses[bone.skinMeshIndex] = bindPose;

                    matrix = math.mul(bone.matrix, bone.bindPose);
                    skinMatrix.Value = new float3x4(matrix.c0.xyz, matrix.c1.xyz, matrix.c2.xyz, matrix.c3.xyz);
                    skinMatrices[bone.skinMeshIndex] = skinMatrix;
                }

                for (int i = 0; i < numIndirectBones; ++i)
                {
                    ref var bone = ref skeleton.indirectBones[i];

                    bindPose.Value = bone.value.bindPose;
                    bindPoses[bone.value.skinMeshIndex] = bindPose;

                    matrix = math.mul(bone.value.matrix, bone.value.bindPose);
                    skinMatrix.Value = new float3x4(matrix.c0.xyz, matrix.c1.xyz, matrix.c2.xyz, matrix.c3.xyz);
                    skinMatrices[bone.value.skinMeshIndex] = skinMatrix;
                }

                int numBlendShapeWeights = skeleton.blendShapeWeights.Length;
                if (numBlendShapeWeights > 0)
                {
                    var blendShapeWeights = this.blendShapeWeights[entity];
                    blendShapeWeights.ResizeUninitialized(numBlendShapeWeights);

                    BlendShapeWeight blendShapeWeight;
                    for (int i = 0; i < numBlendShapeWeights; ++i)
                    {
                        blendShapeWeight.Value = skeleton.blendShapeWeights[i];
                        blendShapeWeights[i] = blendShapeWeight;
                    }
                }

                int numBlendShapes = skeleton.blendShapes.Length;
                if (numBlendShapes > 0)
                {
                    if ((skeleton.flag & MeshInstanceSkeletonFlag.BlendShapeWeightsContiguous) == MeshInstanceSkeletonFlag.BlendShapeWeightsContiguous)
                    {
                        BlendShapeChunkMapping blendShapeChunkMapping;
                        blendShapeChunkMapping.RigIndex = skeleton.blendShapes[0].rigIndex;
                        blendShapeChunkMapping.Size = skeleton.blendShapes.Length;

                        blendShapeChunkMappings[entity] = blendShapeChunkMapping;
                    }
                    else
                    {
                        var blendShapeToRigIndexMappings = this.blendShapeToRigIndexMappings[entity];
                        blendShapeToRigIndexMappings.ResizeUninitialized(numBlendShapes);

                        BlendShapeToRigIndexMapping blendShapeToRigIndexMapping;
                        for (int i = 0; i < numBlendShapes; ++i)
                        {
                            ref readonly var blendShape = ref skeleton.blendShapes[i];
                            blendShapeToRigIndexMapping.BlendShapeIndex = blendShape.index;
                            blendShapeToRigIndexMapping.RigIndex = blendShape.rigIndex;
                            blendShapeToRigIndexMappings[i] = blendShapeToRigIndexMapping;
                        }
                    }
                }
            }
        }

        public static readonly int InnerloopBatchCount = 1;

        private EntityArchetype __entityArchetype;
        private ComponentTypeSet __skinningComponentTypes;
        //private ComponentTypeSet __blendShapeComponentTypes;
        //private ComponentTypeSet __blendShapeContiguousComponentTypes;
        private EntityQuery __groupToCreate;
        private EntityQuery __groupToDestroy;

        private SharedHashMap<Entity, MeshInstanceRendererBuilder> __rendererBuilders;
        private SharedHashMap<int, MeshInstanceRendererPrefabBuilder> __rendererPrefabBuilders;
        private SharedHashMap<int, BlobAssetReference<MeshInstanceRendererPrefab>> __renderers;
        private SharedHashMap<int, BlobAssetReference<MeshInstanceRigPrefab>> __rigs;

        public SharedHashMap<int, BlobAssetReference<MeshInstanceSkeletonPrefab>> prefabs
        {
            get;

            private set;
        }

        public void OnCreate(ref SystemState state)
        {
            BurstUtility.InitializeJob<MeshInstanceSkeletonResizeAndInitAnimations>();

            BurstUtility.InitializeJobParallelFor<CopyIDs>();
            BurstUtility.InitializeJobParallelFor<Init>();

            __entityArchetype = state.EntityManager.CreateArchetype(
                ComponentType.ReadOnly<Prefab>(), 
                //ComponentType.ReadOnly<LocalToParent>(),
                //ComponentType.ReadOnly<Parent>(),
                ComponentType.ReadOnly<RigEntity>());

            __skinningComponentTypes = new ComponentTypeSet(
                ComponentType.ReadOnly<SkinnedMeshToRigIndexMapping>(),
                ComponentType.ReadOnly<SkinnedMeshToRigIndexIndirectMapping>(),
                ComponentType.ReadOnly<SkinnedMeshRootEntity>(),
                ComponentType.ReadOnly<BindPose>(),
                ComponentType.ReadOnly<SkinMatrix>());

            /*__blendShapeComponentTypes = new ComponentTypeSet(
                ComponentType.ReadOnly<BlendShapeWeight>(),
                ComponentType.ReadOnly<BlendShapeToRigIndexMapping>());

            __blendShapeContiguousComponentTypes = new ComponentTypeSet(
                ComponentType.ReadOnly<BlendShapeWeight>(),
                ComponentType.ReadOnly<BlendShapeChunkMapping>());*/

            __groupToDestroy = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceSkeletonID>()
                    },

                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceSkeletonData)
                    }
                },
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceSkeletonID>()
                    },

                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceRendererID)
                    }
                },
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceSkeletonID>(),
                        ComponentType.ReadOnly<MeshInstanceSkeletonData>(),
                    },

                    Any = new ComponentType[]
                    {
                        typeof(MeshInstanceRendererDisabled)
                    },

                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __groupToCreate = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceSkeletonData>(), 
                        ComponentType.ReadOnly<MeshInstanceRendererID>()
                    }, 
                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceSkeletonID), 
                        typeof(MeshInstanceRendererDisabled)
                    }, 
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __rendererBuilders = state.World.GetOrCreateSystemUnmanaged<MeshInstanceRendererSystem>().builders;
            __rendererPrefabBuilders = state.World.GetOrCreateSystemUnmanaged<MeshInstanceFactorySystem>().builders;
            __renderers = state.World.GetOrCreateSystemUnmanaged<MeshInstanceFactorySystem>().prefabs;
            __rigs = state.World.GetOrCreateSystemUnmanaged<MeshInstanceRigFactorySystem>().prefabs;

            prefabs = new SharedHashMap<int, BlobAssetReference<MeshInstanceSkeletonPrefab>>(Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            prefabs.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var entityManager = state.EntityManager;

            var prefabs = this.prefabs;

            if (!__groupToDestroy.IsEmpty)
            {
                using (var entities = new NativeList<Entity>(Allocator.TempJob))
                {
                    state.CompleteDependency();
                    prefabs.lookupJobManager.CompleteReadWriteDependency();

                    CollectToDestroyEx collectToDestroy;
                    collectToDestroy.idType = state.GetComponentTypeHandle<MeshInstanceSkeletonID>(true);
                    collectToDestroy.prefabs = prefabs.writer;
                    collectToDestroy.results = entities;

                    collectToDestroy.Run(__groupToDestroy);

                    entityManager.DestroyEntity(entities.AsArray());
                }

                entityManager.RemoveComponent<MeshInstanceSkeletonID>(__groupToDestroy);
            }

            if (!__groupToCreate.IsEmpty)
            {
                NativeArray<Entity> entities;
                var entityCount = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                var entityArray = new NativeArray<Entity>(__groupToCreate.CalculateEntityCount(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var results = new NativeList<Result>(Allocator.TempJob);
                var rigCounts = new UnsafeParallelHashMap<Entity, int>(1, Allocator.TempJob);
                using (var rendererEntities = new NativeParallelHashMap<Entity, int>(1, Allocator.TempJob))
                //using (var skinningEntities = new NativeParallelHashMap<Entity, int>(1, Allocator.TempJob))
                //using (var blendShapeEntities = new NativeParallelHashMap<Entity, int>(1, Allocator.TempJob))
                {
                    state.CompleteDependency();
                    __rigs.lookupJobManager.CompleteReadOnlyDependency();
                    __rendererBuilders.lookupJobManager.CompleteReadOnlyDependency();
                    __rendererPrefabBuilders.lookupJobManager.CompleteReadOnlyDependency();
                    __renderers.lookupJobManager.CompleteReadOnlyDependency();
                    prefabs.lookupJobManager.CompleteReadWriteDependency();

                    var writer = prefabs.writer;

                    CollectToCreateEx collectToCreate;
                    collectToCreate.entityType = state.GetEntityTypeHandle();
                    collectToCreate.animatedLocalToRoots = state.GetBufferLookup<AnimatedLocalToRoot>(true);
                    collectToCreate.rigInstances = state.GetBufferLookup<MeshInstanceRig>(true);
                    collectToCreate.deformedEntities = state.GetComponentLookup<DeformedEntity>(true);
                    //collectToCreate.skinningTags = state.GetComponentLookup<SkinningTag>(true);
                    //collectToCreate.blendShapeTags = state.GetComponentLookup<BlendShapeTag>(true);
                    collectToCreate.rigIDs = state.GetComponentLookup<MeshInstanceRigID>(true);
                    collectToCreate.entityParentType = state.GetBufferTypeHandle<EntityParent>(true);
                    collectToCreate.instanceType = state.GetComponentTypeHandle<MeshInstanceSkeletonData>(true);
                    collectToCreate.rigIDType = state.GetComponentTypeHandle<MeshInstanceRigID>(true);
                    collectToCreate.rendererIDType = state.GetComponentTypeHandle<MeshInstanceRendererID>(true);
                    collectToCreate.rendererInstanceType = state.GetBufferTypeHandle<MeshInstanceNode>(true);
                    collectToCreate.rigs = __rigs.reader;
                    collectToCreate.rendererBuilders = __rendererBuilders.reader;
                    collectToCreate.rendererPrefabBuilders = __rendererPrefabBuilders.reader;
                    collectToCreate.renderers = __renderers.reader;
                    collectToCreate.prefabs = writer;
                    collectToCreate.entityCount = entityCount;
                    collectToCreate.entities = entityArray;
                    collectToCreate.results = results;
                    //collectToCreate.skinningEntities = skinningEntities;
                    //collectToCreate.blendShapeEntities = blendShapeEntities;
                    collectToCreate.rendererEntities = rendererEntities;
                    collectToCreate.rigCounts = rigCounts;

                    collectToCreate.Run(__groupToCreate);

                    int numResults = results.Length;
                    entities = entityManager.CreateEntity(__entityArchetype, numResults, Allocator.TempJob);

                    Result result;
                    Entity entity;
                    NativeList<Entity> blendShapes = default, blendShapeWeightsContiguous = default, blendShapeWeights = default;
                    for(int i = 0; i < numResults; ++i)
                    {
                        result = results[i];

                        entity = entities[i];

                        writer[result.instanceID].Value.entities[result.index] = entity;

                        if ((result.type & ResultType.Skinning) == ResultType.Skinning)
                            entityManager.AddComponent(entity, __skinningComponentTypes);

                        if ((result.type & ResultType.BlendShape) == ResultType.BlendShape)
                        {
                            if (!blendShapes.IsCreated)
                                blendShapes = new NativeList<Entity>(Allocator.Temp);

                            //entityManager.AddComponent(entity, __blendShapeComponentTypes);
                            blendShapes.Add(entity);
                        }
                        else if ((result.type & ResultType.BlendShapeWeightsContiguous) == ResultType.BlendShapeWeightsContiguous)
                        {
                            //entityManager.AddComponent(entity, __blendShapeContiguousComponentTypes);
                            if (!blendShapeWeightsContiguous.IsCreated)
                                blendShapeWeightsContiguous = new NativeList<Entity>(Allocator.Temp);

                            blendShapeWeightsContiguous.Add(entity);
                        }

                        if ((result.type & ResultType.BlendShapeWeights) == ResultType.BlendShapeWeights)
                        {
                            if (!blendShapeWeights.IsCreated)
                                blendShapeWeights = new NativeList<Entity>(Allocator.Temp);

                            blendShapeWeights.Add(entity);
                        }
                    }

                    if (blendShapes.IsCreated)
                    {
                        entityManager.AddComponent<BlendShapeChunkMapping>(blendShapes.AsArray());

                        blendShapes.Dispose();
                    }

                    if (blendShapeWeightsContiguous.IsCreated)
                    {
                        entityManager.AddComponent<BlendShapeToRigIndexMapping>(blendShapeWeightsContiguous.AsArray());

                        blendShapeWeightsContiguous.Dispose();
                    }

                    if (blendShapeWeights.IsCreated)
                    {
                        entityManager.AddComponent<BlendShapeWeight>(blendShapeWeights.AsArray());

                        blendShapeWeights.Dispose();
                    }

                    if (!rendererEntities.IsEmpty)
                    {
                        using (var keys = rendererEntities.GetKeyArray(Allocator.Temp))
                        {
                            entityManager.AddComponentBurstCompatible<DeformedEntity>(keys);

#if ENABLE_COMPUTE_DEFORMATIONS
                            entityManager.AddComponentBurstCompatible<DeformedMeshIndex>(keys);
#endif
                        }
                    }

                    /*if (!skinningEntities.IsEmpty)
                    {
                        using (var keys = skinningEntities.GetKeyArray(Allocator.Temp))
                            entityManager.AddComponentBurstCompatible<SkinningTag>(keys);
                    }

                    if (!blendShapeEntities.IsEmpty)
                    {
                        using (var keys = blendShapeEntities.GetKeyArray(Allocator.Temp))
                            entityManager.AddComponentBurstCompatible<BlendShapeTag>(keys);
                    }*/

                    if (!rigCounts.IsEmpty)
                    {
                        using (var keys = rigCounts.GetKeyArray(Allocator.Temp))
                            entityManager.AddComponentBurstCompatible<AnimatedLocalToRoot>(keys);
                    }
                }

                int entityCountValue = entityCount[0];

                //var entityArray = __groupToCreate.ToEntityArray(Allocator.TempJob);

                entityManager.AddComponentBurstCompatible<MeshInstanceSkeletonID>(entityArray.GetSubArray(0, entityCountValue));

                var inputDeps = state.Dependency;
                var instances = state.GetComponentLookup<MeshInstanceSkeletonData>(true);

                CopyIDs copyIDs;
                copyIDs.entityCount = entityCount;
                copyIDs.entityArray = entityArray;
                copyIDs.ids = state.GetComponentLookup<MeshInstanceSkeletonID>();
                copyIDs.instances = instances;
                var temp = copyIDs.Schedule(entityCountValue, InnerloopBatchCount, inputDeps);

                int numEntities = entities.Length;

                Init init;
                init.entityArray = entities;
                init.results = results;
                init.instances = instances;
                init.skinnedMeshToRigIndexMappings = state.GetBufferLookup<SkinnedMeshToRigIndexMapping>();
                init.skinnedMeshToRigIndexIndirectMappings = state.GetBufferLookup<SkinnedMeshToRigIndexIndirectMapping>();
                init.bindPoses = state.GetBufferLookup<BindPose>();
                init.skinMatrices = state.GetBufferLookup<SkinMatrix>();
                init.blendShapeWeights = state.GetBufferLookup<BlendShapeWeight>();
                init.blendShapeToRigIndexMappings = state.GetBufferLookup<BlendShapeToRigIndexMapping>();
                init.blendShapeChunkMappings = state.GetComponentLookup<BlendShapeChunkMapping>();

                var jobHandle = init.Schedule(numEntities, InnerloopBatchCount, inputDeps);

                jobHandle = results.Dispose(jobHandle);

                MeshInstanceSkeletonResizeAndInitAnimations resizeAndInitAnimations;
                resizeAndInitAnimations.rigCounts = rigCounts;
                resizeAndInitAnimations.animatedLocalToRoots = state.GetBufferLookup<AnimatedLocalToRoot>();
                resizeAndInitAnimations.animatedDatas = state.GetBufferLookup<AnimatedData>();
                resizeAndInitAnimations.rigs = state.GetComponentLookup<Rig>();

                jobHandle = JobHandle.CombineDependencies(jobHandle, resizeAndInitAnimations.Schedule(inputDeps));

                state.Dependency = JobHandle.CombineDependencies(temp, jobHandle);
            }
        }
    }

    [BurstCompile, UpdateInGroup(typeof(MeshInstanceSystemGroup)), UpdateAfter(typeof(MeshInstanceRigSystem))]
    public partial struct MeshInstanceSkeletonSystem : ISystem
    {
        private struct CollectToDestroy
        {
            [ReadOnly]
            public BufferAccessor<MeshInstanceSkeleton> instances;

            public NativeList<Entity> entities;

            public void Execute(int index)
            {
                entities.AddRange(instances[index].Reinterpret<Entity>().AsNativeArray());
            }
        }

        [BurstCompile]
        private struct CollectToDestroyEx : IJobChunk
        {
            [ReadOnly]
            public BufferTypeHandle<MeshInstanceSkeleton> instanceType;

            public NativeList<Entity> entities;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                CollectToDestroy collectToDestroy;
                collectToDestroy.instances = chunk.GetBufferAccessor(ref instanceType);
                collectToDestroy.entities = entities;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    collectToDestroy.Execute(i);
            }
        }

        private struct CollectToCreate
        {
            [ReadOnly]
            public BufferLookup<MeshInstanceRig> rigMap;

            [ReadOnly]
            public BufferLookup<AnimatedLocalToRoot> animatedLocalToRoots;

            [ReadOnly]
            public SharedHashMap<Entity, MeshInstanceRendererBuilder>.Reader rendererBuilders;

            [ReadOnly]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public NativeArray<MeshInstanceSkeletonData> instances;

            [ReadOnly]
            public NativeArray<MeshInstanceSkeletonID> ids;

            [ReadOnly]
            public BufferAccessor<MeshInstanceRig> rigs;

            [ReadOnly]
            public BufferAccessor<EntityParent> entityParents;

            public NativeParallelMultiHashMap<int, Entity> entities;

            public UnsafeParallelHashMap<Entity, int> rigCounts;

            public void Execute(int index)
            {
                Entity entity = entityArray[index];
                if (rendererBuilders.ContainsKey(entity))
                    return;

                DynamicBuffer<MeshInstanceRig> rigs;
                if (index < this.rigs.Length)
                    rigs = this.rigs[index];
                else
                {
                    var parentEntity = EntityParent.Get(entityParents[index], rigMap);
                    if (parentEntity == Entity.Null)
                        return;

                    rigs = rigMap[parentEntity];
                }

                Entity rigEntity;
                ref var definition = ref instances[index].definition.Value;
                int numInstances = definition.instances.Length, rigCount;// rigBoneCount, boneCount;
                for (int i = 0; i < numInstances; ++i)
                {
                    ref var instance = ref definition.instances[i];
                    rigEntity = rigs[instance.rigIndex].entity;

                    if (!animatedLocalToRoots.HasBuffer(rigEntity))
                    {
                        //boneCount = definition.skeletons[instance.skeletionIndex].boneCount;
                        if (rigCounts.TryGetValue(rigEntity, out rigCount))
                            ++rigCount;
                        else
                            rigCount = 1;

                        rigCounts[rigEntity] = rigCount;
                    }
                }

                entities.Add(ids[index].value, entity);
            }
        }

        [BurstCompile]
        private struct CollectToCreateEx : IJobChunk
        {
            [ReadOnly]
            public BufferLookup<MeshInstanceRig> rigMap;

            [ReadOnly]
            public BufferLookup<AnimatedLocalToRoot> animatedLocalToRoots;

            [ReadOnly]
            public SharedHashMap<Entity, MeshInstanceRendererBuilder>.Reader rendererBuilders;

            [ReadOnly]
            public EntityTypeHandle entityType;
            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceSkeletonData> instanceType;
            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceSkeletonID> idType;
            [ReadOnly]
            public BufferTypeHandle<EntityParent> entityParentType;
            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> rigType;

            public NativeParallelMultiHashMap<int, Entity> entities;

            public UnsafeParallelHashMap<Entity, int> rigCounts;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                CollectToCreate collectToCreate;
                collectToCreate.rigMap = rigMap;
                collectToCreate.animatedLocalToRoots = animatedLocalToRoots;
                collectToCreate.rendererBuilders = rendererBuilders;
                collectToCreate.entityArray = chunk.GetNativeArray(entityType);
                collectToCreate.instances = chunk.GetNativeArray(ref instanceType);
                collectToCreate.ids = chunk.GetNativeArray(ref idType);
                collectToCreate.rigs = chunk.GetBufferAccessor(ref rigType);
                collectToCreate.entityParents = chunk.GetBufferAccessor(ref entityParentType);
                collectToCreate.entities = entities;
                collectToCreate.rigCounts = rigCounts;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    collectToCreate.Execute(i);
            }
        }

        [BurstCompile]
        private struct InitEntities : IJobParallelFor
        {
            public int instanceCount;

            [ReadOnly]
            public NativeArray<Entity> instanceEntities;
            [ReadOnly]
            public NativeArray<Entity> prefabEntities;

            [NativeDisableContainerSafetyRestriction]
            public BufferLookup<MeshInstanceSkeleton> instances;

            public void Execute(int index)
            {
                int numPrefabEntities = prefabEntities.Length;
                Entity prefabEntity = prefabEntities[index];
                var entities = instances[prefabEntity].Reinterpret<Entity>();
                entities.ResizeUninitialized(instanceCount);
                for (int i = 0; i < instanceCount; ++i)
                    entities[i] = instanceEntities[numPrefabEntities * i + index];
            }
        }

        [BurstCompile]
        private struct Init : IJobParallelFor
        {
            [ReadOnly, DeallocateOnJobCompletion]
            public NativeArray<Entity> entityArray;

            [ReadOnly]
            public BufferLookup<EntityParent> entityParents;

            //[ReadOnly]
            //public ComponentLookup<RigRootEntity> rigRootEntities;

            [ReadOnly]
            public ComponentLookup<MeshInstanceSkeletonData> instances;

            [ReadOnly]
            public BufferLookup<MeshInstanceSkeleton> skeletons;

            [ReadOnly]
            public BufferLookup<MeshInstanceRig> rigs;

            [ReadOnly]
            public BufferLookup<MeshInstanceRigNode> nodes;

            [ReadOnly]
            public BufferLookup<MeshInstanceNode> renderers;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<Parent> parents;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<LocalToParent> localToParents;

            //[NativeDisableParallelForRestriction]
            //public ComponentLookup<Parent> parents;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<RigEntity> rigEntities;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<SkinnedMeshRootEntity> skinnedMeshRootEntities;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<DeformedEntity> deformedEntities;

            public void Execute(int index)
            {
                Entity entity = entityArray[index], rigRootEntity = EntityParent.Get(entity, entityParents, this.rigs);

                var skeletons = this.skeletons[entity];

                var rigs = this.rigs[rigRootEntity];
                var nodes = this.nodes.HasBuffer(rigRootEntity) ? this.nodes[rigRootEntity] : default;
                var renderers = this.renderers[entity];

                ref var definition = ref instances[entity].definition.Value;

                //localToParent.Value = float4x4.identity;

                Entity rendererEntity;
                Parent parent;
                LocalToParent localToParent;
                DeformedEntity deformedEntity;
                RigEntity rigEntity;
                SkinnedMeshRootEntity skinnedMeshRootEntity;
                int numInstances = definition.instances.Length, i, j;
                for (i = 0; i < numInstances; ++i)
                {
                    ref var temp = ref definition.instances[i];

                    deformedEntity.Value = skeletons[i].entity;

                    rigEntity.Value = rigs[temp.rigIndex].entity;

                    ref var skeleton = ref definition.skeletons[temp.skeletionIndex];

                    parent.Value = skeleton.rootBoneIndex == -1 ? rigEntity.Value : nodes[skeleton.rootBoneIndex].entity;
                    //parents[deformedEntity.Value] = parent;

                    rigEntities[deformedEntity.Value] = rigEntity;

                    if (skeleton.bones.Length > 0 || skeleton.indirectBones.Length > 0 || skeleton.blendShapes.Length > 0)
                    {
                        skinnedMeshRootEntity.Value = parent.Value;
                        skinnedMeshRootEntities[deformedEntity.Value] = skinnedMeshRootEntity;

                        int numRendererIndices = temp.rendererIndices.Length;
                        for (j = 0; j < numRendererIndices; ++j)
                        {
                            rendererEntity = renderers[temp.rendererIndices[j]].entity;

                            if (parents.HasComponent(rendererEntity))
                                parents[rendererEntity] = parent;

                            if (localToParents.HasComponent(rendererEntity))
                            {
                                localToParent.Value = temp.localToRoot;// math.float4x4(math.RigidTransform(quaternion.identity, math.float3(4.46173f, 2.353137f, 0.0f)));
                                localToParents[rendererEntity] = localToParent;
                            }

                            deformedEntities[rendererEntity] = deformedEntity;
                        }
                    }
                }
            }
        }

        [BurstCompile]
        private struct DisposeAll : IJob
        {
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
                public int instanceCount;
                public int prefabEntityCount;
                public int prefabEntityStartIndex;
                public int instanceEntityStartIndex;

                public int instanceEntityCount => prefabEntityCount * instanceCount;

                public Info(
                    int prefabEntityStartIndex,
                    int prefabEntityCount,
                    in NativeArray<Entity> instanceEntities,
                    ref BlobArray<Entity> instances,
                    ref EntityManager entityManager,
                    ref int instanceEntityStartIndex)
                {
                    instanceCount = instances.Length;

                    this.prefabEntityCount = prefabEntityCount;
                    this.prefabEntityStartIndex = prefabEntityStartIndex;
                    this.instanceEntityStartIndex = instanceEntityStartIndex;

                    int instanceEntityCount = this.instanceEntityCount;
                    var entities = instanceEntities.GetSubArray(instanceEntityStartIndex, instanceEntityCount);

                    for (int i = 0; i < instanceCount; ++i)
                        entityManager.Instantiate(instances[i], entities.GetSubArray(i * prefabEntityCount, prefabEntityCount));

                    instanceEntityStartIndex += instanceEntityCount;
                }

                public Result ToResult(
                    in NativeArray<Entity> prefabEntities,
                    in NativeArray<Entity> instanceEntities)
                {
                    Result result;
                    result.numInstances = instanceCount;
                    result.prefabEntities = prefabEntities.GetSubArray(prefabEntityStartIndex, prefabEntityCount);
                    result.instanceEntities = instanceEntities.GetSubArray(instanceEntityStartIndex, instanceEntityCount);

                    return result;
                }
            }

            public int numInstances;
            public NativeArray<Entity> prefabEntities;
            public NativeArray<Entity> instanceEntities;

            public static void Schedule(
                int innerloopBatchCount,
                in NativeArray<Entity> prefabEntities,
                in NativeArray<Entity> instanceEntities,
                in NativeArray<Info> infos,
                ref SystemState systemState)
            {
                var skeletons = systemState.GetBufferLookup<MeshInstanceSkeleton>();

                Result result;
                JobHandle temp, inputDeps = systemState.Dependency;
                JobHandle? jobHandle = null;
                int length = infos.Length;
                for (int i = 0; i < length; ++i)
                {
                    result = infos[i].ToResult(prefabEntities, instanceEntities);

                    temp = result.ScheduleInitEntities(
                        innerloopBatchCount,
                        ref skeletons,
                        inputDeps);

                    jobHandle = jobHandle == null ? temp : JobHandle.CombineDependencies(jobHandle.Value, temp);
                }

                if (jobHandle != null)
                    systemState.Dependency = jobHandle.Value;
            }

            public JobHandle ScheduleInitEntities(
                int innerloopBatchCount,
                ref BufferLookup<MeshInstanceSkeleton> instances,
                in JobHandle inputDeps)
            {
                InitEntities initEntities;
                initEntities.instanceCount = numInstances;
                initEntities.instanceEntities = instanceEntities;
                initEntities.prefabEntities = prefabEntities;
                initEntities.instances = instances;

                return initEntities.Schedule(prefabEntities.Length, innerloopBatchCount, inputDeps);
            }
        }

        public static readonly int InnerloopBatchCount = 1;

        private SharedHashMap<Entity, MeshInstanceRendererBuilder> __rendererBuilders;
        private SharedHashMap<int, BlobAssetReference<MeshInstanceSkeletonPrefab>> __prefabs;
        private EntityQuery __groupToDestroy;
        private EntityQuery __groupToCreate;

        public void OnCreate(ref SystemState state)
        {
            BurstUtility.InitializeJobParallelFor<InitEntities>();
            BurstUtility.InitializeJobParallelFor<Init>();
            BurstUtility.InitializeJob<DisposeAll>();

            __groupToDestroy = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceSkeleton>()
                    },

                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceSkeletonID)
                    },

                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            __groupToCreate = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<MeshInstanceSkeletonData>(),
                        ComponentType.ReadOnly<MeshInstanceSkeletonID>()
                    },
                    None = new ComponentType[]
                    {
                        typeof(MeshInstanceSkeleton)
                    },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });

            var world = state.World;
            __rendererBuilders = world.GetOrCreateSystemUnmanaged<MeshInstanceRendererSystem>().builders;
            __prefabs = world.GetOrCreateSystemUnmanaged<MeshInstanceSkeletonFactorySystem>().prefabs;
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
                using (var entities = new NativeList<Entity>(Allocator.TempJob))
                {
                    CollectToDestroyEx collectToDestroy;
                    collectToDestroy.instanceType = state.GetBufferTypeHandle<MeshInstanceSkeleton>(true);
                    collectToDestroy.entities = entities;

                    state.CompleteDependency();

                    collectToDestroy.Run(__groupToDestroy);

                    entityManager.DestroyEntity(entities.AsArray());
                }

                entityManager.RemoveComponent<MeshInstanceSkeleton>(__groupToDestroy);
            }

            int entityCount = __groupToCreate.CalculateEntityCount();
            if (entityCount > 0)
            {
                using (var entities = new NativeParallelMultiHashMap<int, Entity>(entityCount, Allocator.TempJob))
                {
                    var rigCounts = new UnsafeParallelHashMap<Entity, int>(1, Allocator.TempJob);

                    __rendererBuilders.lookupJobManager.CompleteReadOnlyDependency();
                    state.CompleteDependency();

                    CollectToCreateEx collectToCreate;
                    collectToCreate.rigMap = state.GetBufferLookup<MeshInstanceRig>(true);
                    collectToCreate.animatedLocalToRoots = state.GetBufferLookup<AnimatedLocalToRoot>(true);
                    collectToCreate.rendererBuilders = __rendererBuilders.reader;
                    collectToCreate.entityType = state.GetEntityTypeHandle();
                    collectToCreate.instanceType = state.GetComponentTypeHandle<MeshInstanceSkeletonData>(true);
                    collectToCreate.idType = state.GetComponentTypeHandle<MeshInstanceSkeletonID>(true);
                    collectToCreate.rigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
                    collectToCreate.entityParentType = state.GetBufferTypeHandle<EntityParent>(true);
                    collectToCreate.entities = entities;
                    collectToCreate.rigCounts = rigCounts;

                    collectToCreate.Run(__groupToCreate);

                    if (rigCounts.IsEmpty)
                        rigCounts.Dispose();
                    else
                    {
                        using (var keys = rigCounts.GetKeyArray(Allocator.Temp))
                            entityManager.AddComponentBurstCompatible<AnimatedLocalToRoot>(keys);
                    }

                    JobHandle inputDeps;
                    JobHandle? result;
                    if (entities.IsEmpty)
                    {
                        inputDeps = state.Dependency;

                        result = null;
                    }
                    else
                    {
                        using (var keyValueArray = entities.GetKeyValueArrays(Allocator.Temp))
                        {
                            entityManager.AddComponentBurstCompatible<MeshInstanceSkeleton>(keyValueArray.Values);

                            __prefabs.lookupJobManager.CompleteReadOnlyDependency();

                            var reader = __prefabs.reader;
                            int count = keyValueArray.Keys.ConvertToUniqueArray(), instanceEntityCount = 0, key;
                            for (int i = 0; i < count; ++i)
                            {
                                key = keyValueArray.Keys[i];
                                ref var prefab = ref reader[key].Value;

                                instanceEntityCount += entities.CountValuesForKey(key) * prefab.entities.Length;
                            }

                            var prefabEntities = new NativeArray<Entity>(entityCount, Allocator.TempJob);
                            var instanceEntities = new NativeArray<Entity>(instanceEntityCount, Allocator.TempJob);

                            int instanceEntityStartIndex = 0, prefabEntityStartIndex = 0;
                            var infos = new NativeArray<Result.Info>(count, Allocator.Temp);
                            {
                                int prefabEntityStartCount;
                                NativeParallelMultiHashMap<int, Entity>.Enumerator enumerator;
                                for (int i = 0; i < count; ++i)
                                {
                                    key = keyValueArray.Keys[i];

                                    prefabEntityStartCount = 0;

                                    enumerator = entities.GetValuesForKey(key);
                                    while (enumerator.MoveNext())
                                        prefabEntities[prefabEntityStartIndex + prefabEntityStartCount++] = enumerator.Current;

                                    ref var prefab = ref reader[key].Value;

                                    infos[i] = new Result.Info(
                                        prefabEntityStartIndex,
                                        prefabEntityStartCount,
                                        instanceEntities,
                                        ref prefab.entities,
                                        ref entityManager,
                                        ref instanceEntityStartIndex);

                                    prefabEntityStartIndex += prefabEntityStartCount;
                                }

                                Result.Schedule(InnerloopBatchCount, prefabEntities, instanceEntities, infos, ref state);

                                inputDeps = state.Dependency;

                                var entityArray = entities.GetValueArray(Allocator.TempJob);

                                Init init;
                                init.entityArray = entityArray;
                                init.entityParents = state.GetBufferLookup<EntityParent>(true);
                                init.instances = state.GetComponentLookup<MeshInstanceSkeletonData>(true);
                                init.skeletons = state.GetBufferLookup<MeshInstanceSkeleton>(true);
                                init.rigs = state.GetBufferLookup<MeshInstanceRig>(true);
                                init.nodes = state.GetBufferLookup<MeshInstanceRigNode>(true);
                                init.renderers = state.GetBufferLookup<MeshInstanceNode>(true);
                                init.parents = state.GetComponentLookup<Parent>();
                                init.localToParents = state.GetComponentLookup<LocalToParent>();
                                init.rigEntities = state.GetComponentLookup<RigEntity>();
                                init.skinnedMeshRootEntities = state.GetComponentLookup<SkinnedMeshRootEntity>();
                                init.deformedEntities = state.GetComponentLookup<DeformedEntity>();

                                var jobHandle = init.Schedule(entityArray.Length, InnerloopBatchCount, inputDeps);

                                DisposeAll disposeAll;
                                disposeAll.prefabEntities = prefabEntities;
                                disposeAll.instanceEntities = instanceEntities;
                                jobHandle = JobHandle.CombineDependencies(jobHandle, disposeAll.Schedule(inputDeps));

                                result = jobHandle;

                                infos.Dispose();
                            }
                        }
                    }

                    if(rigCounts.IsCreated)
                    {
                        MeshInstanceSkeletonResizeAndInitAnimations resizeAndInitAnimations;
                        resizeAndInitAnimations.rigCounts = rigCounts;
                        resizeAndInitAnimations.animatedLocalToRoots = state.GetBufferLookup<AnimatedLocalToRoot>();
                        resizeAndInitAnimations.animatedDatas = state.GetBufferLookup<AnimatedData>();
                        resizeAndInitAnimations.rigs = state.GetComponentLookup<Rig>();

                        var jobHandle = resizeAndInitAnimations.Schedule(inputDeps);

                        result = result == null ? jobHandle : JobHandle.CombineDependencies(result.Value, jobHandle);
                    }

                    if (result != null)
                        state.Dependency = result.Value;
                }
            }
        }
    }
}