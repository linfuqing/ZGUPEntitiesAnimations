#if !SWING_BONE_V1
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Animation;
using Math = ZG.Mathematics.Math;

namespace ZG
{
    public struct SwingBone : IBufferElementData
    {
        public int index;

        public float windDelta;
        public float sourceDelta;
        public float destinationDelta;
    }

    [UpdateInGroup(typeof(AnimationSystemGroup), OrderLast = true),
        UpdateBefore(typeof(AnimationComputeDeformationDataSystem)), 
        UpdateAfter(typeof(AnimationComputeRigMatrixSystem))]
    public partial struct SwingBoneSystem : ISystem
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

        private struct UpdateBones
        {
            public float deltaTime;

            [ReadOnly]
            public NativeReference<Wind> wind;

            [ReadOnly]
            public NativeArray<Rig> rigs;

            [ReadOnly]
            public BufferAccessor<AnimatedData> animatedDatas;

            [ReadOnly]
            public BufferAccessor<SwingBone> bones;

            public BufferAccessor<AnimatedLocalToWorld> localToWorlds;

            public static void Update(
                ref NativeArray<AnimatedLocalToWorld> localToWorlds,
                in NativeArray<SwingBone> bones, 
                in AnimationStream animationStream, 
                in float3 windDirection, 
                float deltaTime, 
                int index)
            {
                var bone = bones[index];
                var localToWorld = localToWorlds[bone.index];
                var world = math.RigidTransform(localToWorld.Value);
                var local = math.RigidTransform(animationStream.GetLocalToParentRotation(bone.index), animationStream.GetLocalToParentTranslation(bone.index));
                int parentIndex = animationStream.Rig.Value.Skeleton.ParentIndexes[bone.index];
                var parent = parentIndex >= 0 && parentIndex < localToWorlds.Length ? math.RigidTransform(localToWorlds[parentIndex].Value) : RigidTransform.identity;

                float3 source = world.pos - parent.pos, destination = math.mul(parent.rot, local.pos);
                //localToWorld.pos = parentToWorld.pos + destination;

                //localToWorld.rot = math.slerp(localToWorld.rot, math.mul(parentToWorld.rot, localToParent.rot), bone.sourceDelta * deltaTime);

                bool isRotation = false;
                var rotation = quaternion.identity;
                source = math.normalizesafe(source, float3.zero);
                if (!source.Equals(float3.zero))
                {
                    destination = math.normalizesafe(destination, float3.zero);
                    if (!destination.Equals(float3.zero) && !source.Equals(destination))
                    {
                        if (bone.windDelta > math.FLT_MIN_NORMAL && !source.Equals(windDirection))
                            source = math.mul(math.slerp(quaternion.identity, Math.FromToRotation(source, windDirection), bone.windDelta * deltaTime), source);

                        /*localToWorld.rot = math.mul(//Mathematics.Math.FromToRotation(destination, math.normalizesafe(math.lerp(source, destination, instance.destinationDelta), destination)),
                            math.slerp(Math.FromToRotation(destination, source), quaternion.identity, bone.destinationDelta * deltaTime),
                            localToWorld.rot);*/

                        rotation = math.slerp(Math.FromToRotation(destination, source), quaternion.identity, bone.destinationDelta * deltaTime);

                        isRotation = true;
                    }
                }

                if(!isRotation)
                    rotation = math.slerp(quaternion.identity, math.mul(parent.rot, math.inverse(world.rot)), bone.sourceDelta * deltaTime);

                //info.local.rot = math.mul(math.inverse(parent.rot), info.world.rot);

                /*if (this.children.HasBuffer(entity))
                {
                    var children = this.children[entity];
                    for (int i = 0; i < children.Length; ++i)
                        Update(instance, info.world, windDirection, children[i].entity);
                }*/

                localToWorld.Value = math.mul(math.float4x4(math.RigidTransform(rotation, destination)), localToWorld.Value);

                localToWorlds[bone.index] = localToWorld;
            }

            public void Execute(int index)
            {
                var wind = this.wind.Value;

                var localToWorlds = this.localToWorlds[index].AsNativeArray();

                var animationStream = AnimationStream.CreateReadOnly(rigs[index].Value, animatedDatas[index].AsNativeArray());

                var bones = this.bones[index].AsNativeArray();
                int numBones = bones.Length;
                for (int i = 0; i < numBones; ++i)
                    Update(
                        ref localToWorlds, 
                        bones, 
                        animationStream, 
                        wind.direction,
                        deltaTime, 
                        i);
            }
        }

        [BurstCompile]
        private struct UpdateBonesEx : IJobChunk
        {
            public float deltaTime;

            [ReadOnly]
            public NativeReference<Wind> wind;

            [ReadOnly]
            public ComponentTypeHandle<Rig> rigType;

            [ReadOnly]
            public BufferTypeHandle<AnimatedData> animatedDataType;

            [ReadOnly]
            public BufferTypeHandle<SwingBone> boneType;

            public BufferTypeHandle<AnimatedLocalToWorld> localToWorldType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                UpdateBones updateBones;
                updateBones.deltaTime = deltaTime;
                updateBones.wind = wind;
                updateBones.rigs = chunk.GetNativeArray(ref rigType);
                updateBones.animatedDatas = chunk.GetBufferAccessor(ref animatedDataType);
                updateBones.bones = chunk.GetBufferAccessor(ref boneType);
                updateBones.localToWorlds = chunk.GetBufferAccessor(ref localToWorldType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    updateBones.Execute(i);
            }
        }

        private EntityQuery __group;
        private EntityQuery __windGroup;

        private ComponentLookup<SwingBoneWind> __winds;

        private ComponentTypeHandle<Rig> __rigType;

        private BufferTypeHandle<AnimatedData> __animatedDataType;

        private BufferTypeHandle<SwingBone> __boneType;

        private BufferTypeHandle<AnimatedLocalToWorld> __localToWorldType;

        private NativeReference<Wind> __wind;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAll<SwingBone, Rig, AnimatedData>()
                        .WithAllRW<AnimatedLocalToWorld>()
                        .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __windGroup = builder
                    .WithAll<SwingBoneWind>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludeSystems)
                    .Build(ref state);

            __winds = state.GetComponentLookup<SwingBoneWind>(true);

            __rigType = state.GetComponentTypeHandle<Rig>(true);

            __animatedDataType = state.GetBufferTypeHandle<AnimatedData>(true);

            __boneType = state.GetBufferTypeHandle<SwingBone>(true);

            __localToWorldType = state.GetBufferTypeHandle<AnimatedLocalToWorld>();

            __wind = new NativeReference<Wind>(Allocator.Persistent);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __wind.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = state.WorldUnmanaged.Time.DeltaTime;

            UpdateWind updateWind;
            updateWind.deltaTime = deltaTime;
            updateWind.singleton = __windGroup.HasSingleton<SwingBoneWind>() ? __windGroup.GetSingletonEntity() : Entity.Null;
            updateWind.source = __winds.UpdateAsRef(ref state);
            updateWind.destination = __wind;

            var jobHandle = updateWind.ScheduleByRef(state.Dependency);

            UpdateBonesEx updateBones;
            updateBones.deltaTime = deltaTime;
            updateBones.wind = __wind;
            updateBones.rigType = __rigType.UpdateAsRef(ref state);
            updateBones.animatedDataType = __animatedDataType.UpdateAsRef(ref state);
            updateBones.boneType = __boneType.UpdateAsRef(ref state);
            updateBones.localToWorldType = __localToWorldType.UpdateAsRef(ref state);
            state.Dependency = updateBones.ScheduleParallelByRef(__group, jobHandle);
        }
    }
}
#endif