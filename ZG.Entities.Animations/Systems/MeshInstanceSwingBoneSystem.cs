using System.Collections;
using System.Collections.Generic;
using Unity.Animation;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace ZG
{
    public struct MeshInstanceSwingBoneDefinition
    {
        public struct Bone
        {
            public float windDelta;
            public float sourceDelta;
            public float destinationDelta;

            public StringHash boneID;
        }

        public struct Rig
        {
            public int index;
            public BlobArray<Bone> bones;
        }

        public BlobArray<Rig> rigs;
    }

    public struct MeshInstanceSwingBoneData : IComponentData, IEnableableComponent
    {
        public BlobAssetReference<MeshInstanceSwingBoneDefinition> definition;
    }

    public struct MeshInstanceSwingBoneDirty : IComponentData
    {

    }

    [BurstCompile, UpdateInGroup(typeof(MeshInstanceSystemGroup)), UpdateAfter(typeof(MeshInstanceRigSystem))]
    public partial struct MeshInstanceSwingBoneSystem : ISystem
    {
        private struct Collect
        {
            [ReadOnly]
            public NativeArray<MeshInstanceSwingBoneData> instances;

            [ReadOnly]
            public BufferAccessor<MeshInstanceRig> instanceRigs;

            [ReadOnly]
            public BufferLookup<SwingBone> swingBones;

            public NativeList<Entity> rigEntities;

            public bool Execute(int index)
            {
                bool result = false;
                var instanceRigs = this.instanceRigs[index];
                ref var definition = ref instances[index].definition.Value;
                Entity rigEntity;
                int numRigs = definition.rigs.Length;
                for (int i = 0; i < numRigs; ++i)
                {
                    ref var rig = ref definition.rigs[i];

                    rigEntity = instanceRigs[definition.rigs[i].index].entity;
                    if (swingBones.HasBuffer(rigEntity))
                        continue;

                    rigEntities.Add(rigEntity);

                    result = true;
                }

                return result;
            }
        }

        [BurstCompile]
        private struct CollectEx : IJobChunk
        {
            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> instanceRigType;

            [ReadOnly]
            public BufferLookup<SwingBone> swingBones;

            public ComponentTypeHandle<MeshInstanceSwingBoneData> instanceType;

            public NativeList<Entity> rigEntities;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Collect collect;
                collect.instances = chunk.GetNativeArray(ref instanceType);
                collect.instanceRigs = chunk.GetBufferAccessor(ref instanceRigType);
                collect.rigEntities = rigEntities;
                collect.swingBones = swingBones;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    chunk.SetComponentEnabled(ref instanceType, i, collect.Execute(i));
            }
        }

        private struct Init
        {
            [ReadOnly]
            public ComponentLookup<Rig> rigs;

            [ReadOnly]
            public NativeArray<MeshInstanceSwingBoneData> instances;

            [ReadOnly]
            public BufferAccessor<MeshInstanceRig> instanceRigs;

            public BufferLookup<SwingBone> swingBones;

            public void Execute(int index)
            {
                /*DynamicBuffer<MeshInstanceRig> instanceRigs;
                if (index < this.instanceRigs.Length)
                    instanceRigs = this.instanceRigs[index];
                else
                {
                    Entity instanceRigEntity = EntityParent.Get(entityParents[index], instanceRigMap);
                    if (Entity.Null == instanceRigEntity)
                        return;

                    instanceRigs = instanceRigMap[instanceRigEntity];
                }*/

                SwingBone swingBone;
                DynamicBuffer<SwingBone> swingBones;
                var instanceRigs = this.instanceRigs[index];
                ref var definition = ref instances[index].definition.Value;
                Entity rigEntity;
                int i, j, numBones, numRigs = definition.rigs.Length;
                for (i = 0; i < numRigs; ++i)
                {
                    ref var rig = ref definition.rigs[i];

                    rigEntity = instanceRigs[definition.rigs[i].index].entity;

                    swingBones = this.swingBones[rigEntity];
                    if (swingBones.Length > 0)
                        continue;

                    ref var ids = ref rigs[rigEntity].Value.Value.Skeleton.Ids;

                    numBones = rig.bones.Length;
                    for (j = 0; j < numBones; ++j)
                    {
                        ref var bone = ref rig.bones[j];

                        swingBone.index = Core.FindBindingIndex(ref ids, bone.boneID);
                        if (swingBone.index == -1)
                        {
                            UnityEngine.Debug.LogError($"Bone ID:{bone.boneID.Id} can not been found.");

                            continue;
                        }

                        swingBone.windDelta = bone.windDelta;
                        swingBone.sourceDelta = bone.sourceDelta;
                        swingBone.destinationDelta = bone.destinationDelta;

                        swingBones.Add(swingBone);
                    }
                }
            }
        }

        [BurstCompile]
        private struct InitEx : IJobChunk
        {
            [ReadOnly]
            public ComponentLookup<Rig> rigs;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> instanceRigType;

            [NativeDisableParallelForRestriction]
            public BufferLookup<SwingBone> swingBones;

            public ComponentTypeHandle<MeshInstanceSwingBoneData> instanceType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Init init;
                init.instances = chunk.GetNativeArray(ref instanceType);
                init.instanceRigs = chunk.GetBufferAccessor(ref instanceRigType);
                init.swingBones = swingBones;
                init.rigs = rigs;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    init.Execute(i);

                    chunk.SetComponentEnabled(ref instanceType, i, false);
                }
            }
        }

        private EntityQuery __groupToCreate;
        private EntityQuery __groupToUpdate;

        private ComponentLookup<Rig> __rigs;

        private BufferTypeHandle<MeshInstanceRig> __instanceRigType;

        private BufferLookup<SwingBone> __swingBones;

        private ComponentTypeHandle<MeshInstanceSwingBoneData> __instanceType;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using(var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToCreate = builder
                    .WithAll<MeshInstanceSwingBoneData, MeshInstanceRig, MeshInstanceSwingBoneDirty>()
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                    .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __groupToUpdate = builder
                    .WithAll<MeshInstanceSwingBoneData, MeshInstanceRig>()
                    .Build(ref state);

            //__group.AddChangedVersionFilter(ComponentType.ReadOnly<MeshInstanceSwingBoneData>());
            //__group.AddChangedVersionFilter(ComponentType.ReadOnly<MeshInstanceRig>());

            __rigs = state.GetComponentLookup<Rig>(true);
            __instanceRigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
            __swingBones = state.GetBufferLookup<SwingBone>();
            __instanceType = state.GetComponentTypeHandle<MeshInstanceSwingBoneData>();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!__groupToCreate.IsEmptyIgnoreFilter)
            {
                using (var rigEntities = new NativeList<Entity>(Allocator.TempJob))
                {
                    CollectEx collect;
                    collect.instanceRigType = __instanceRigType.UpdateAsRef(ref state);
                    collect.swingBones = __swingBones.UpdateAsRef(ref state);
                    collect.instanceType = __instanceType.UpdateAsRef(ref state);
                    collect.rigEntities = rigEntities;
                    collect.Run(__groupToCreate);

                    if (rigEntities.Length < 1)
                        return;

                    var entityManager = state.EntityManager;
                    entityManager.RemoveComponent<MeshInstanceSwingBoneDirty>(__groupToCreate);
                    entityManager.AddComponent<SwingBone>(rigEntities.AsArray());
                }
            }

            InitEx init;
            init.rigs = __rigs.UpdateAsRef(ref state);
            init.instanceRigType = __instanceRigType.UpdateAsRef(ref state);
            init.swingBones = __swingBones.UpdateAsRef(ref state);
            init.instanceType = __instanceType.UpdateAsRef(ref state);

            state.Dependency = init.ScheduleParallelByRef(__groupToUpdate, state.Dependency);
        }
    }
}