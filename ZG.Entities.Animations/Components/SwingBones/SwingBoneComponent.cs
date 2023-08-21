using Unity.Entities;
using UnityEngine;
#if SWING_BONE_V1
using System;
using System.Collections.Generic;
using UnityEngine.Jobs;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Math = ZG.Mathematics.Math;
#endif

namespace ZG
{
#if SWING_BONE_V1
    [Serializable]
    public struct SwingBoneData : ICleanupComponentData
    {
        public float windDelta;
        public float sourceDelta;
        public float destinationDelta;
    }

    [Serializable]
    public struct SwingBoneInfo : IComponentData
    {
        public RigidTransform local;
        public RigidTransform world;
    }

    [InternalBufferCapacity(4)]
    public struct SwingBoneChild : ICleanupBufferElementData
    {
        public Entity entity;

        public static implicit operator SwingBoneChild(Entity entity)
        {
            SwingBoneChild child;
            child.entity = entity;

            return child;
        }
    }

    [EntityComponent(typeof(Transform))]
    public class SwingBoneComponent : SystemStateComponentDataProxy<SwingBoneData>
    {
    }

    [UpdateInGroup(typeof(TimeSystemGroup))]
    public partial class SwingBoneFactorySystem : SystemBase
    {
        private struct Fill
        {
            public NativeList<Entity> parents;
            public NativeList<Entity> leaves;
            [ReadOnly]
            public BufferLookup<SwingBoneChild> childMap;
            [ReadOnly]
            public BufferAccessor<SwingBoneChild> children;

            public void Execute(in DynamicBuffer<SwingBoneChild> children)
            {
                int numChildren = children.Length;
                SwingBoneChild child;
                for (int i = 0; i < numChildren; ++i)
                {
                    child = children[i];
                    if (childMap.HasBuffer(child.entity))
                    {
                        parents.Add(child.entity);

                        Execute(childMap[child.entity]);
                    }
                    else
                        leaves.Add(child.entity);
                }
            }

            public void Execute(int index)
            {
                //int numChildren = children.Length;
                //for (int i = 0; i < numChildren; ++i)
                    //Execute(children[i]);

                Execute(children[index]);
            }
        }

        [BurstCompile]
        private struct FillEx : IJobChunk
        {
            public NativeList<Entity> parents;
            public NativeList<Entity> leaves;

            [ReadOnly]
            public BufferLookup<SwingBoneChild> children;
            [ReadOnly]
            public BufferTypeHandle<SwingBoneChild> childType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Fill fill;
                fill.parents = parents;
                fill.leaves = leaves;
                fill.childMap = children;
                fill.children = chunk.GetBufferAccessor(ref childType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    fill.Execute(i);
            }
        }

        private EntityArchetype __parentArchetype;
        private EntityArchetype __leafArchetype;

        private EntityQuery __groupToCreate;
        private EntityQuery __groupToDestroy;
        private EntityQuery __groupToDestroyImmediate;

        private static List<Transform> __transforms = null;
        private static Dictionary<Transform, Entity> __entities = null;

        protected override void OnCreate()
        {
            base.OnCreate();

            EntityManager entityManager = EntityManager;
            __parentArchetype = entityManager.CreateArchetype(ComponentType.ReadWrite<SwingBoneChild>(), ComponentType.ReadWrite<SwingBoneInfo>(), ComponentType.ReadOnly<EntityObjects>(), TransformAccessArrayEx.componentType);
            __leafArchetype = entityManager.CreateArchetype(ComponentType.ReadWrite<SwingBoneInfo>(), ComponentType.ReadOnly<EntityObjects>(), TransformAccessArrayEx.componentType);

            __groupToCreate = GetEntityQuery(ComponentType.ReadOnly<SwingBoneData>(), ComponentType.Exclude<SwingBoneChild>(), TransformAccessArrayEx.componentType);
            __groupToDestroy = GetEntityQuery(ComponentType.ReadOnly<SwingBoneData>(), ComponentType.ReadOnly<SwingBoneChild>(), ComponentType.Exclude<EntityObjects>());
            __groupToDestroyImmediate = GetEntityQuery(ComponentType.ReadOnly<SwingBoneData>(), ComponentType.Exclude<SwingBoneChild>(), ComponentType.Exclude<EntityObjects>());
        }

        protected override void OnUpdate()
        {
            __Create();

            __Destroy();
        }

        private void __Create()
        {
            if (__groupToCreate.IsEmptyIgnoreFilter)
                return;

            //TODO: 
            __groupToCreate.CompleteDependency();

            int parentCount = 0, leafCount = 0, count;
            Transform value;
            using (var transforms = __groupToCreate.ToComponentDataArray<EntityObject<Transform>>(Allocator.TempJob))
            {
                foreach (var transform in transforms)
                {
                    value = transform.value;
                    count = value.GetLeafCount();
                    leafCount += count;
                    parentCount += value.GetNodeCount() - count - 1;
                }

                EntityManager entityManager = EntityManager;
                if (parentCount > 0 && leafCount > 0)
                {
                    using (var parents = entityManager.CreateEntity(__parentArchetype, parentCount, Allocator.Temp))
                    using (var leaves = entityManager.CreateEntity(__leafArchetype, leafCount, Allocator.Temp))
                    using (var entityArray = __groupToCreate.ToEntityArray(Allocator.TempJob))
                    {
                        entityManager.AddComponent<SwingBoneChild>(entityArray);

                        BufferLookup<SwingBoneChild> children = GetBufferLookup<SwingBoneChild>();
                        ComponentLookup<SwingBoneInfo> infos = GetComponentLookup<SwingBoneInfo>();
                        SwingBoneInfo info;
                        Transform root, parent;
                        Entity child;
                        int length = entityArray.Length;
                        for (int i = 0; i < length; ++i)
                        {
                            root = transforms[i].value;

                            if (__transforms == null)
                                __transforms = new List<Transform>();

                            root.GetComponentsInChildren(__transforms);

                            if (__entities == null)
                                __entities = new Dictionary<Transform, Entity>();
                            else
                                __entities.Clear();

                            __entities.Add(root, entityArray[i]);

                            foreach (Transform transform in __transforms)
                            {
                                if (transform == null || transform == root)
                                    continue;

                                if (transform.childCount > 0)
                                    child = parents[--parentCount];
                                else
                                    child = leaves[--leafCount];

#if UNITY_EDITOR
                                entityManager.SetName(child, transform.name);
#endif

                                __entities.Add(transform, child);

                                info.local.pos = transform.localPosition;
                                info.local.rot = transform.localRotation;
                                info.world.pos = transform.position;
                                info.world.rot = transform.rotation;

                                infos[child] = info;

                                new EntityObject<Transform>(transform).SetTo(child, entityManager);
                            }

                            foreach (Transform transform in __transforms)
                            {
                                if (transform == null || transform == root)
                                    continue;

                                parent = transform.parent;
                                if (parent != null)
                                    children[__entities[parent]].Add(__entities[transform]);
                            }
                        }
                    }
                }
            }
        }

        private void __Destroy()
        {
            if (__groupToDestroy.IsEmptyIgnoreFilter)
                return;

			__groupToDestroy.CompleteDependency();

            NativeList<Entity> parents = new NativeList<Entity>(Allocator.TempJob), leaves = new NativeList<Entity>(Allocator.TempJob);
            FillEx fill;
            fill.parents = parents;
            fill.leaves = leaves;
            fill.children = GetBufferLookup<SwingBoneChild>(true);
            fill.childType = GetBufferTypeHandle<SwingBoneChild>(true);
            fill.Run(__groupToDestroy);
            //Entities.With(__groupToDestroy).ForEach((DynamicBuffer<SwingBoneChild> children) => __Set(children, parents, leaves));

            EntityManager entityManager = EntityManager;
            var entityArray = __groupToDestroy.ToEntityArray(Allocator.TempJob);
            entityManager.RemoveComponent<SwingBoneData>(entityArray);
            entityManager.RemoveComponent<SwingBoneChild>(entityArray);
            entityManager.RemoveComponent<SwingBoneChild>(parents.AsArray());
            entityManager.DestroyEntity(parents.AsArray());
            entityManager.DestroyEntity(leaves.AsArray());
            entityManager.DestroyEntity(entityArray);

            entityArray.Dispose();
            parents.Dispose();
            leaves.Dispose();

            entityManager.RemoveComponent<SwingBoneData>(__groupToDestroyImmediate);
        }

        /*private void __Set(DynamicBuffer<SwingBoneChild> children, NativeList<Entity> parents, NativeList<Entity> leaves)
        {
            EntityManager entityManager = EntityManager;
            foreach (var child in children)
            {
                if (entityManager.HasComponent<SwingBoneChild>(child.entity))
                {
                    parents.Add(child.entity);

                    __Set(entityManager.GetBuffer<SwingBoneChild>(child.entity), parents, leaves);
                }
                else
                    leaves.Add(child.entity);
            }
        }*/
    }
    
    //[UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(SwingBoneFactorySystem))]
    public partial class SwingBoneReadSystem : SystemBase
    {
        private struct Wind
        {
            public float time;
            public float delta;
            public float3 direction;
        }

        [BurstCompile]
        private struct UpdateWind : IJob
        {
            public float deltaTime;
            public Entity singleton;
            [ReadOnly]
            public ComponentLookup<SwingBoneWind> source;
            public NativeReference<Wind> destination;

            public void Execute()
            {
                var wind = source.HasComponent(singleton) ? source[singleton] : SwingBoneWind.DefaultValue;
                var value = destination.Value;
                value.time += deltaTime * wind.speed;

                value.delta = (math.sin(value.time) + 1.0f) * 0.5f * (wind.maxDelta - wind.minDelta) + wind.minDelta;
                value.direction = wind.direction;

                destination.Value = value;
            }
        }

        [BurstCompile]
        private struct UpdateBones : IJobParallelForTransform
        {
            public float deltaTime;

            [ReadOnly]
            public NativeReference<Wind> wind;

            [ReadOnly]
            public NativeList<Entity> entityArray;
            [ReadOnly]
            public NativeList<SwingBoneData> instances;

            [ReadOnly]
            public BufferLookup<SwingBoneChild> children;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<SwingBoneInfo> infos;

            public void Update(
                in SwingBoneData instance,
                in RigidTransform parent,
                in float3 windDirection, 
                in Entity entity)
            {
                SwingBoneInfo info = infos[entity];

                float3 source = info.world.pos - parent.pos, destination = math.mul(parent.rot, info.local.pos);
                info.world.pos = parent.pos + destination;

                info.world.rot = math.slerp(info.world.rot, math.mul(parent.rot, info.local.rot), instance.sourceDelta);

                source = math.normalizesafe(source, float3.zero);
                if (!source.Equals(float3.zero))
                {
                    destination = math.normalizesafe(destination, float3.zero);
                    if (!destination.Equals(float3.zero) && !source.Equals(destination))
                    {
                        if (instance.windDelta > 0.0f && !source.Equals(windDirection))
                            source = math.mul(math.slerp(quaternion.identity, Math.FromToRotation(source, windDirection), instance.windDelta), source);

                        info.world.rot = math.mul(//Mathematics.Math.FromToRotation(destination, math.normalizesafe(math.lerp(source, destination, instance.destinationDelta), destination)),
                            math.slerp(Math.FromToRotation(destination, source), quaternion.identity, instance.destinationDelta),
                            info.world.rot);
                    }
                }

                //info.local.rot = math.mul(math.inverse(parent.rot), info.world.rot);

                if (this.children.HasBuffer(entity))
                {
                    var children = this.children[entity];
                    for (int i = 0; i < children.Length; ++i)
                        Update(instance, info.world, windDirection, children[i].entity);
                }

                infos[entity] = info;
            }

            public void Execute(int index, TransformAccess transform)
            {
                if (!transform.isValid)
                    return;

                Entity entity = entityArray[index];
                if (!this.children.HasBuffer(entity))
                    return;

                var wind = this.wind.Value;

                SwingBoneData instance = instances[index];
                instance.windDelta = math.clamp(wind.delta * instance.windDelta * deltaTime, 0.0f, 1.0f);
                instance.sourceDelta = math.clamp(instance.sourceDelta * deltaTime, 0.0f, 1.0f);
                instance.destinationDelta = math.clamp(instance.destinationDelta * deltaTime, 0.0f, 1.0f);

                RigidTransform parent;
                parent.rot = transform.rotation;
                parent.pos = transform.position;

                DynamicBuffer<SwingBoneChild> children = this.children[entity];
                for (int i = 0; i < children.Length; ++i)
                    Update(instance, parent, wind.direction, children[i].entity);
            }
        }

        private EntityQuery __group;
        private EntityQuery __windGroup;
        private NativeReference<Wind> __wind;
        private TransformAccessArrayEx __transformAccessArray;

        protected override void OnCreate()
        {
            base.OnCreate();

            __group = GetEntityQuery(ComponentType.ReadOnly<SwingBoneData>(), TransformAccessArrayEx.componentType);
            __windGroup = GetEntityQuery(new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<SwingBoneWind>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludeSystems
            });
            __wind = new NativeReference<Wind>(Allocator.Persistent);
            __transformAccessArray = new TransformAccessArrayEx(__group);
        }

        protected override void OnDestroy()
        {
            __wind.Dispose();
            __transformAccessArray.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            float deltaTime = World.Time.DeltaTime;

            UpdateWind updateWind;
            updateWind.deltaTime = deltaTime;
            updateWind.singleton = __windGroup.HasSingleton<SwingBoneWind>() ? __windGroup.GetSingletonEntity() : Entity.Null;
            updateWind.source = GetComponentLookup<SwingBoneWind>(true);
            updateWind.destination = __wind;

            var jobHandle = updateWind.ScheduleByRef(Dependency);

            var transformAccessArray = __transformAccessArray.Convert(this);

            UpdateBones updateBones;
            updateBones.deltaTime = deltaTime;
            updateBones.wind = __wind;
            updateBones.entityArray = __group.ToEntityListAsync(WorldUpdateAllocator, out JobHandle entityJobHandle);
            updateBones.instances = __group.ToComponentDataListAsync<SwingBoneData>(WorldUpdateAllocator, out JobHandle instanceJobHandle);
            updateBones.children = GetBufferLookup<SwingBoneChild>(true);
            updateBones.infos = GetComponentLookup<SwingBoneInfo>();
            Dependency = updateBones.Schedule(transformAccessArray, JobHandle.CombineDependencies(jobHandle, entityJobHandle, instanceJobHandle));
        }
    }

    [UpdateInGroup(typeof(TimeSystemGroup)), UpdateAfter(typeof(SwingBoneReadSystem))]
    public partial class SwingBoneWriteSystem : SystemBase
    {
        [BurstCompile]
        private struct UpdateTransform : IJobParallelForTransform
        {
            [ReadOnly]
            public NativeList<SwingBoneInfo> infos;

            public void Execute(int index, TransformAccess transform)
            {
                if (!transform.isValid)
                    return;

                SwingBoneInfo info = infos[index];

                //transformAccess.localPosition = info.local.pos;
                //transformAccess.localRotation = info.local.rot;

                //transformAccess.position = info.world.pos;
                transform.rotation = info.world.rot;
            }
        }
        
        private EntityQuery __group;
        private TransformAccessArrayEx __transformAccessArray;

        protected override void OnCreate()
        {
            base.OnCreate();

            __group = GetEntityQuery(ComponentType.ReadOnly<SwingBoneInfo>(), TransformAccessArrayEx.componentType);
            __transformAccessArray = new TransformAccessArrayEx(__group);
        }

        protected override void OnDestroy()
        {
            __transformAccessArray.Dispose();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            var transformAccessArray = __transformAccessArray.Convert(this);

            UpdateTransform updateTransform;
            updateTransform.infos = __group.ToComponentDataListAsync<SwingBoneInfo>(WorldUpdateAllocator, out JobHandle infoJobHandle);
            Dependency = updateTransform.Schedule(transformAccessArray, JobHandle.CombineDependencies(Dependency, infoJobHandle));
        }
    }
#else
    [EntityComponent(typeof(MeshInstanceSwingBoneData))]
    [EntityComponent(typeof(MeshInstanceSwingBoneDirty))]
    public class SwingBoneComponent : MonoBehaviour, IEntityComponent
    {
        [SerializeField]
        internal MeshInstanceSwingBoneDatabase _database;

        void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
        {
            MeshInstanceSwingBoneData instance;
            instance.definition = _database.definition;
            assigner.SetComponentData(entity, instance);
        }
    }
#endif
}