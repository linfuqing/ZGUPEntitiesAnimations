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

    public struct SwingBoneTransform : IBufferElementData
    {
        public float4x4 matrix;
    }

    [BurstCompile, 
        UpdateInGroup(typeof(AnimationSystemGroup), OrderLast = true),
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
            public BufferAccessor<SwingBone> bones;

            [ReadOnly]
            public BufferAccessor<AnimatedData> animatedDatas;

            public BufferAccessor<AnimatedLocalToWorld> localToWorlds;

            public BufferAccessor<AnimatedLocalToRoot> localToRoots;

            public BufferAccessor<SwingBoneTransform> transforms;

            public static void Update(
                ref DynamicBuffer<SwingBoneTransform> transforms,
                ref NativeArray<AnimatedLocalToWorld> localToWorlds,
                in NativeArray<SwingBone> bones,
                in AnimationStream animationStream,
                in Wind wind, 
                float deltaTime, 
                int index)
            {
                int numTransforms = transforms.Length;
                var bone = bones[index];
                //bone.sourceDelta = 1.0f;
                //bone.destinationDelta = 1.0f;

                var localToWorld = index < numTransforms ? transforms[index].matrix : localToWorlds[bone.index].Value;
                var localToParent = animationStream.GetLocalToParentMatrix(bone.index);
                var world = math.RigidTransform(localToWorld);
                var local = math.RigidTransform(localToParent.rs, localToParent.t);

                int parentIndex = animationStream.Rig.Value.Skeleton.ParentIndexes[bone.index];
                var parentToWorld = parentIndex >= 0 && parentIndex < localToWorlds.Length ? localToWorlds[parentIndex].Value : float4x4.identity;
                var parent = math.RigidTransform(parentToWorld);

                float3 source = world.pos - parent.pos, destination = math.rotate(parentToWorld, local.pos), worldPosition = parent.pos + destination;
                //localToWorld.pos = parentToWorld.pos + destination;

                //localToWorld.rot = math.slerp(localToWorld.rot, math.mul(parentToWorld.rot, localToParent.rot), bone.sourceDelta * deltaTime);
                var rotation = math.mul(math.slerp(world.rot, math.mul(parent.rot, local.rot), math.saturate(bone.sourceDelta * deltaTime)), math.inverse(world.rot));

                source = math.normalizesafe(source, float3.zero);
                if (!source.Equals(float3.zero))
                {
                    destination = math.normalizesafe(destination, float3.zero);
                    if (!destination.Equals(float3.zero))
                    {
                        if (bone.windDelta > math.FLT_MIN_NORMAL && !source.Equals(wind.direction))
                            source = math.mul(math.slerp(quaternion.identity, Math.FromToRotation(source, wind.direction), 
                                math.saturate(bone.windDelta * wind.delta * deltaTime)), 
                                source);

                        if (!source.Equals(destination))
                        {
                            /*localToWorld.rot = math.mul(//Mathematics.Math.FromToRotation(destination, math.normalizesafe(math.lerp(source, destination, instance.destinationDelta), destination)),
                                math.slerp(Math.FromToRotation(destination, source), quaternion.identity, bone.destinationDelta * deltaTime),
                                localToWorld.rot);*/

                            rotation = math.mul(math.slerp(Math.FromToRotation(destination, source), quaternion.identity, math.saturate(bone.destinationDelta * deltaTime)), rotation);
                        }
                    }
                }

                //info.local.rot = math.mul(math.inverse(parent.rot), info.world.rot);

                /*if (this.children.HasBuffer(entity))
                {
                    var children = this.children[entity];
                    for (int i = 0; i < children.Length; ++i)
                        Update(instance, info.world, windDirection, children[i].entity);
                }*/

                localToWorld = math.float4x4(math.mul(math.float3x3(rotation), math.float3x3(localToWorld)), worldPosition);

                AnimatedLocalToWorld animatedLocalToWorld;
                animatedLocalToWorld.Value = localToWorld;
                localToWorlds[bone.index] = animatedLocalToWorld;
                
                SwingBoneTransform transform;
                if(numTransforms <= index)
                {
                    int length = index + 1;
                    transforms.ResizeUninitialized(length);

                    for (int i = numTransforms; i < index; ++i)
                    {
                        transform.matrix = localToWorlds[bones[i].index].Value;
                        transforms[i] = transform;
                    }
                }

                transform.matrix = localToWorld;
                transforms[index] = transform;
            }

            public void Execute(int index)
            {
                var wind = this.wind.Value;

                var transforms = this.transforms[index];
                var localToWorlds = this.localToWorlds[index].AsNativeArray();

                var animationStream = AnimationStream.CreateReadOnly(rigs[index].Value, animatedDatas[index].AsNativeArray());

                var bones = this.bones[index].AsNativeArray();
                int numBones = bones.Length;
                for (int i = 0; i < numBones; ++i)
                    Update(
                        ref transforms,
                        ref localToWorlds,
                        bones,
                        animationStream,
                        wind, 
                        deltaTime,
                        i);

                if (index < this.localToRoots.Length)
                {
                    var localToRoots = this.localToRoots[index];

                    var worldToRoot = math.mul(localToRoots[0].Value, math.inverse(localToWorlds[0].Value));

                    int boneIndex;
                    AnimatedLocalToRoot localToRoot;
                    for (int i = 0; i < numBones; ++i)
                    {
                        boneIndex = bones[i].index;

                        localToRoot.Value = math.mul(worldToRoot, localToWorlds[boneIndex].Value);

                        localToRoots[boneIndex] = localToRoot;
                    }
                }
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
            public BufferTypeHandle<SwingBone> boneType;

            [ReadOnly]
            public BufferTypeHandle<AnimatedData> animatedDataType;

            public BufferTypeHandle<AnimatedLocalToWorld> localToWorldType;

            public BufferTypeHandle<AnimatedLocalToRoot> localToRootType;

            public BufferTypeHandle<SwingBoneTransform> transformType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                UpdateBones updateBones;
                updateBones.deltaTime = deltaTime;
                updateBones.wind = wind;
                updateBones.rigs = chunk.GetNativeArray(ref rigType);
                updateBones.bones = chunk.GetBufferAccessor(ref boneType);
                updateBones.animatedDatas = chunk.GetBufferAccessor(ref animatedDataType);
                updateBones.localToWorlds = chunk.GetBufferAccessor(ref localToWorldType);
                updateBones.localToRoots = chunk.GetBufferAccessor(ref localToRootType);
                updateBones.transforms = chunk.GetBufferAccessor(ref transformType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    updateBones.Execute(i);
            }
        }

        private EntityQuery __group;
        private EntityQuery __windGroup;

        private ComponentLookup<SwingBoneWind> __winds;

        private ComponentTypeHandle<Rig> __rigType;

        private BufferTypeHandle<SwingBone> __boneType;

        private BufferTypeHandle<AnimatedData> __animatedDataType;

        private BufferTypeHandle<AnimatedLocalToWorld> __localToWorldType;

        private BufferTypeHandle<AnimatedLocalToRoot> __localToRootType;

        public BufferTypeHandle<SwingBoneTransform> __transformType;

        private NativeReference<Wind> __wind;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAll<SwingBone, SwingBoneTransform, Rig, AnimatedData>()
                        .WithAllRW<AnimatedLocalToWorld>()
                        .Build(ref state);

            state.RequireForUpdate(__group);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __windGroup = builder
                    .WithAll<SwingBoneWind>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludeSystems)
                    .Build(ref state);

            __winds = state.GetComponentLookup<SwingBoneWind>(true);

            __rigType = state.GetComponentTypeHandle<Rig>(true);

            __boneType = state.GetBufferTypeHandle<SwingBone>(true);

            __animatedDataType = state.GetBufferTypeHandle<AnimatedData>(true);

            __localToWorldType = state.GetBufferTypeHandle<AnimatedLocalToWorld>();

            __localToRootType = state.GetBufferTypeHandle<AnimatedLocalToRoot>();

            __transformType = state.GetBufferTypeHandle<SwingBoneTransform>();

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
            updateBones.boneType = __boneType.UpdateAsRef(ref state);
            updateBones.animatedDataType = __animatedDataType.UpdateAsRef(ref state);
            updateBones.localToWorldType = __localToWorldType.UpdateAsRef(ref state);
            updateBones.localToRootType = __localToRootType.UpdateAsRef(ref state);
            updateBones.transformType = __transformType.UpdateAsRef(ref state);
            state.Dependency = updateBones.ScheduleParallelByRef(__group, jobHandle);
        }
    }
}
#endif