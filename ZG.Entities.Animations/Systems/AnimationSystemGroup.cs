using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Transforms;
using Unity.Deformations;
using Unity.Animation;
using ZG;

[assembly: RegisterGenericJobType(typeof(SortReadTransformComponentJob<AnimatedReadTransformHandle>))]
[assembly: RegisterGenericJobType(typeof(ReadTransformComponentJob<AnimatedReadTransformHandle>))]
[assembly: RegisterGenericJobType(typeof(ReadRootTransformJob<AnimatedRootMotion>))]
[assembly: RegisterGenericJobType(typeof(UpdateRootRemapMatrixJob<AnimatedRootMotion>))]
[assembly: RegisterGenericJobType(typeof(WriteRootTransformJob<AnimatedRootMotion>))]
[assembly: RegisterGenericJobType(typeof(AccumulateRootTransformJob<AnimatedRootMotion>))]

namespace ZG
{
    public struct AnimatedReadTransformHandle : IReadTransformHandle
    {
        public Entity Entity { get; set; }
        public int Index { get; set; }
    }

    public struct AnimatedWriteTransformHandle : IWriteTransformHandle
    {
        public Entity Entity { get; set; }
        public int Index { get; set; }
    }

    public struct AnimatedRootMotion : IAnimatedRootMotion
    {
        public RigidTransform Delta { get; set; }
    }

    [BurstCompile, UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct AnimationInitializeSystem : ISystem
    {
        private EntityQuery __group;

        public void OnCreate(ref SystemState state)
        {
            __group = state.GetEntityQuery(ComponentType.ReadOnly<Rig>(), ComponentType.ReadWrite<AnimatedData>());
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new ClearMasksJob
            {
                RigType = state.GetComponentTypeHandle<Rig>(true),
                AnimatedDataType = state.GetBufferTypeHandle<AnimatedData>()
            }.ScheduleParallel(__group, state.Dependency);
        }
    }

    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial class AnimationSystemGroup : ComponentSystemGroup
    {
    }

    [BurstCompile, UpdateInGroup(typeof(AnimationSystemGroup), OrderFirst = true)]
    public partial struct AnimationReadTransformSystem : ISystem
    {
        private EntityQuery __readRootTransformQuery;
        private EntityQuery __sortReadComponentDataQuery;
        private EntityQuery __readComponentDataQuery;
        private EntityQuery __updateRootRemapJobDataQuery;
        private EntityQuery __clearPassMaskQuery;

        public void OnCreate(ref SystemState state)
        {
            __sortReadComponentDataQuery = state.GetEntityQuery(SortReadTransformComponentJob<AnimatedReadTransformHandle>.QueryDesc);
            __readComponentDataQuery = state.GetEntityQuery(ReadTransformComponentJob<AnimatedReadTransformHandle>.QueryDesc);
            __readRootTransformQuery = state.GetEntityQuery(ReadRootTransformJob<AnimatedRootMotion>.QueryDesc);
            __clearPassMaskQuery = state.GetEntityQuery(ComponentType.ReadOnly<Rig>(), ComponentType.ReadWrite<AnimatedData>());
            __updateRootRemapJobDataQuery = state.GetEntityQuery(UpdateRootRemapMatrixJob<AnimatedRootMotion>.QueryDesc);
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var sortJob = new SortReadTransformComponentJob<AnimatedReadTransformHandle>
            {
                ReadTransforms = state.GetBufferTypeHandle<AnimatedReadTransformHandle>(),
                LastSystemVersion = state.LastSystemVersion
            }.ScheduleParallel(__sortReadComponentDataQuery, state.Dependency);

            var readJob = new ReadTransformComponentJob<AnimatedReadTransformHandle>
            {
                Rigs = state.GetComponentTypeHandle<Rig>(true),
                RigRoots = state.GetComponentTypeHandle<RigRootEntity>(true),
                ReadTransforms = state.GetBufferTypeHandle<AnimatedReadTransformHandle>(true),
                EntityLocalToWorld = state.GetComponentLookup<LocalToWorld>(true),
                AnimatedData = state.GetBufferTypeHandle<AnimatedData>()
            }.ScheduleParallel(__readComponentDataQuery, sortJob);

            var readRootJob = new ReadRootTransformJob<AnimatedRootMotion>
            {
                EntityTranslation = state.GetComponentLookup<Translation>(true),
                EntityRotation = state.GetComponentLookup<Rotation>(true),
                EntityScale = state.GetComponentLookup<Scale>(true),
                EntityNonUniformScale = state.GetComponentLookup<NonUniformScale>(true),
                RigType = state.GetComponentTypeHandle<Rig>(true),
                RigRootEntityType = state.GetComponentTypeHandle<RigRootEntity>(true),
                AnimatedDataType = state.GetBufferTypeHandle<AnimatedData>()
            }.ScheduleParallel(__readRootTransformQuery, readJob);

            var clearPassMaskJob = new ClearPassMaskJob
            {
                RigType = state.GetComponentTypeHandle<Rig>(true),
                AnimatedDataType = state.GetBufferTypeHandle<AnimatedData>()
            }.ScheduleParallel(__clearPassMaskQuery, readRootJob);

            var updateRigRemapJob = new UpdateRootRemapMatrixJob<AnimatedRootMotion>
            {
                EntityLocalToWorld = state.GetComponentLookup<LocalToWorld>(true),
                Parent = state.GetComponentLookup<Parent>(true),
                DisableRootTransformType = state.GetComponentTypeHandle<DisableRootTransformReadWriteTag>(true),
                AnimatedRootMotionType = state.GetComponentTypeHandle<AnimatedRootMotion>(true),
                RigRootEntityType = state.GetComponentTypeHandle<RigRootEntity>(),
            }.ScheduleParallel(__updateRootRemapJobDataQuery, readRootJob);

            state.Dependency = JobHandle.CombineDependencies(updateRigRemapJob, clearPassMaskJob);
        }
    }

    [BurstCompile, UpdateInGroup(typeof(AnimationSystemGroup), OrderLast = true)]
    public partial struct AnimationWriteTransformSystem : ISystem
    {
        private EntityQuery __writeRootTransformQuery;
        private EntityQuery __accumulateRootTransformQuery;

        public void OnCreate(ref SystemState state)
        {
            __writeRootTransformQuery = state.GetEntityQuery(WriteRootTransformJob<AnimatedRootMotion>.QueryDesc);
            __accumulateRootTransformQuery = state.GetEntityQuery(AccumulateRootTransformJob<AnimatedRootMotion>.QueryDesc);
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var writeRootJob = new WriteRootTransformJob<AnimatedRootMotion>
            {
                EntityLocalToWorld = state.GetComponentLookup<LocalToWorld>(),
                EntityLocalToParent = state.GetComponentLookup<LocalToParent>(),
                EntityTranslation = state.GetComponentLookup<Translation>(),
                EntityRotation = state.GetComponentLookup<Rotation>(),
                EntityScale = state.GetComponentLookup<Scale>(),
                EntityNonUniformScale = state.GetComponentLookup<NonUniformScale>(),
                RigType = state.GetComponentTypeHandle<Rig>(true),
                RigRootEntityType = state.GetComponentTypeHandle<RigRootEntity>(true),
                AnimatedDataType = state.GetBufferTypeHandle<AnimatedData>(),
            }.ScheduleParallel(__writeRootTransformQuery, state.Dependency);

            var accumulateRootJob = new AccumulateRootTransformJob<AnimatedRootMotion>
            {
                EntityLocalToWorld = state.GetComponentLookup<LocalToWorld>(),
                EntityLocalToParent = state.GetComponentLookup<LocalToParent>(),
                EntityTranslation = state.GetComponentLookup<Translation>(),
                EntityRotation = state.GetComponentLookup<Rotation>(),
                EntityScale = state.GetComponentLookup<Scale>(),
                EntityNonUniformScale = state.GetComponentLookup<NonUniformScale>(),
                RootMotionOffsetType = state.GetComponentTypeHandle<RootMotionOffset>(),
                RootMotionType = state.GetComponentTypeHandle<AnimatedRootMotion>(),
                RigType = state.GetComponentTypeHandle<Rig>(true),
                RigRootEntityType = state.GetComponentTypeHandle<RigRootEntity>(true),
                AnimatedDataType = state.GetBufferTypeHandle<AnimatedData>()
            }.ScheduleParallel(__accumulateRootTransformQuery, writeRootJob);

            state.Dependency = accumulateRootJob;
        }
    }

    [BurstCompile, UpdateInGroup(typeof(AnimationSystemGroup), OrderLast = true), UpdateAfter(typeof(AnimationWriteTransformSystem))]
    public partial struct AnimationComputeRigMatrixSystem : ISystem
    {

        [BurstCompile /*(FloatMode = FloatMode.Fast)*/]
        private struct ComputeWorldSpaceJob : IJobChunk
        {
            static public EntityQueryDesc QueryDesc => new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Rig>(),
                    ComponentType.ReadOnly<RigRootEntity>(),
                    ComponentType.ReadOnly<AnimatedData>(),
                    ComponentType.ReadWrite<AnimatedLocalToWorld>()
                },
                None = new ComponentType[]
                {
                    typeof(AnimatedLocalToRoot)
                }
            };

            [ReadOnly] public ComponentTypeHandle<Rig> Rig;
            [ReadOnly] public ComponentTypeHandle<RigRootEntity> RigRootEntity;
            [ReadOnly] public BufferTypeHandle<AnimatedData> AnimatedData;
            [ReadOnly] public ComponentLookup<LocalToWorld> EntityLocalToWorld;

            public BufferTypeHandle<AnimatedLocalToWorld> AnimatedLocalToWorld;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var rigs = chunk.GetNativeArray(ref Rig);
                var rigRoots = chunk.GetNativeArray(ref RigRootEntity);
                var animatedDataAccessor = chunk.GetBufferAccessor(ref AnimatedData);
                var animatedLocalToWorldAccessor = chunk.GetBufferAccessor(ref AnimatedLocalToWorld);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    var rootLocalToWorld = EntityLocalToWorld[rigRoots[i].Value].Value;
                    var stream = AnimationStream.CreateReadOnly(rigs[i], animatedDataAccessor[i].AsNativeArray());
                    Core.ComputeLocalToRoot(ref stream, rootLocalToWorld, animatedLocalToWorldAccessor[i].Reinterpret<float4x4>().AsNativeArray());
                }
            }
        }

        [BurstCompile /*(FloatMode = FloatMode.Fast)*/]
        private struct ComputeRootSpaceJob : IJobChunk
        {
            static public EntityQueryDesc QueryDesc => new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Rig>(),
                    ComponentType.ReadOnly<AnimatedData>(),
                    ComponentType.ReadWrite<AnimatedLocalToRoot>()
                },
                None = new ComponentType[]
                {
                    typeof(AnimatedLocalToWorld)
                }
            };

            [ReadOnly] public ComponentTypeHandle<Rig> Rigs;
            [ReadOnly] public BufferTypeHandle<AnimatedData> AnimatedData;

            public BufferTypeHandle<AnimatedLocalToRoot> AnimatedLocalToRoot;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var rigs = chunk.GetNativeArray(ref Rigs);
                var data = chunk.GetBufferAccessor(ref AnimatedData);
                var animatedLocalToRoot = chunk.GetBufferAccessor(ref AnimatedLocalToRoot);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    var stream = AnimationStream.CreateReadOnly(rigs[i], data[i].AsNativeArray());
                    Core.ComputeLocalToRoot(ref stream, float4x4.identity, animatedLocalToRoot[i].Reinterpret<float4x4>().AsNativeArray());
                }
            }
        }

        [BurstCompile /*(FloatMode = FloatMode.Fast)*/]
        private struct ComputeWorldAndRootSpaceJob : IJobChunk
        {
            static public EntityQueryDesc QueryDesc => new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Rig>(),
                    ComponentType.ReadOnly<RigRootEntity>(),
                    ComponentType.ReadOnly<AnimatedData>(),
                    ComponentType.ReadWrite<AnimatedLocalToWorld>(),
                    ComponentType.ReadWrite<AnimatedLocalToRoot>()
                }
            };

            [ReadOnly] public ComponentTypeHandle<Rig> Rig;
            [ReadOnly] public ComponentTypeHandle<RigRootEntity> RigRootEntity;
            [ReadOnly] public BufferTypeHandle<AnimatedData> AnimatedData;

            [ReadOnly] public ComponentLookup<LocalToWorld> EntityLocalToWorld;

            public BufferTypeHandle<AnimatedLocalToWorld> AnimatedLocalToWorld;
            public BufferTypeHandle<AnimatedLocalToRoot> AnimatedLocalToRoot;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var rigs = chunk.GetNativeArray(ref Rig);
                var rigRoots = chunk.GetNativeArray(ref RigRootEntity);
                var animatedLocalToRootAccessor = chunk.GetBufferAccessor(ref AnimatedLocalToRoot);

                var animatedDataAccessor = chunk.GetBufferAccessor(ref AnimatedData);
                var animatedLocalToWorldAccessor = chunk.GetBufferAccessor(ref AnimatedLocalToWorld);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    var rootLocalToWorld = EntityLocalToWorld[rigRoots[i].Value].Value;
                    var stream = AnimationStream.CreateReadOnly(rigs[i].Value, animatedDataAccessor[i].AsNativeArray());
                    Core.ComputeLocalToRoot(
                        ref stream,
                        float4x4.identity,
                        animatedLocalToRootAccessor[i].Reinterpret<float4x4>().AsNativeArray(),
                        rootLocalToWorld,
                        animatedLocalToWorldAccessor[i].Reinterpret<float4x4>().AsNativeArray()
                    );
                }
            }
        }

        [BurstCompile /*(FloatMode = FloatMode.Fast)*/]
        private struct WriteTransformComponents : IJobChunk
        {
            static public EntityQueryDesc QueryDesc => new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<Rig>(),
                    //ComponentType.ReadOnly<RigRootEntity>(),
                    ComponentType.ReadOnly<AnimatedData>(),
                    ComponentType.ReadOnly<AnimatedWriteTransformHandle>(),
                    ComponentType.ReadOnly<AnimatedLocalToWorld>()
                }
            };

            [ReadOnly]
            public ComponentTypeHandle<Rig> rigType;
            [ReadOnly]
            public BufferTypeHandle<AnimatedData> animatedDataType;
            [ReadOnly]
            public BufferTypeHandle<AnimatedLocalToWorld> animatedLocalToWorldType;
            [ReadOnly]
            public BufferTypeHandle<AnimatedWriteTransformHandle> writeTransformType;

            [ReadOnly]
            public BufferLookup<Child> children;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<LocalToWorld> localToWorlds;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<LocalToParent> localToParents;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<Translation> translations;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<Rotation> rotations;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<Scale> scales;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<NonUniformScale> nonUniformScales;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var rigs = chunk.GetNativeArray(ref rigType);
                var animatedDataAccessor = chunk.GetBufferAccessor(ref animatedDataType);
                var writeTransformAccessor = chunk.GetBufferAccessor(ref writeTransformType);
                var animatedLocalToWorldAccessor = chunk.GetBufferAccessor(ref animatedLocalToWorldType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                {
                    var animatedLocalToWorlds = animatedLocalToWorldAccessor[i].Reinterpret<float4x4>().AsNativeArray();
                    var stream = AnimationStream.CreateReadOnly(rigs[i], animatedDataAccessor[i].AsNativeArray());

                    var writeTransforms = writeTransformAccessor[i].AsNativeArray();
                    for (int j = 0; j < writeTransforms.Length; ++j)
                    {
                        var localToWorld = animatedLocalToWorlds[writeTransforms[j].Index];

                        if (localToWorlds.HasComponent(writeTransforms[j].Entity))
                        {
                            localToWorlds[writeTransforms[j].Entity] = new LocalToWorld
                            {
                                Value = localToWorld
                            };
                        }

                        if (localToParents.HasComponent(writeTransforms[j].Entity))
                        {
                            localToParents[writeTransforms[j].Entity] = new LocalToParent
                            {
                                Value = mathex.float4x4(stream.GetLocalToParentMatrix(writeTransforms[j].Index))
                            };
                        }

                        if (translations.HasComponent(writeTransforms[j].Entity))
                        {
                            translations[writeTransforms[j].Entity] = new Translation
                            {
                                Value = stream.GetLocalToParentTranslation(writeTransforms[j].Index)
                            };
                        }

                        if (rotations.HasComponent(writeTransforms[j].Entity))
                        {
                            rotations[writeTransforms[j].Entity] = new Rotation
                            {
                                Value = stream.GetLocalToParentRotation(writeTransforms[j].Index)
                            };
                        }

                        if (scales.HasComponent(writeTransforms[j].Entity))
                        {
                            scales[writeTransforms[j].Entity] = new Scale
                            {
                                Value = stream.GetLocalToParentScale(writeTransforms[j].Index).x
                            };
                        }
                        else if (nonUniformScales.HasComponent(writeTransforms[j].Entity))
                        {
                            nonUniformScales[writeTransforms[j].Entity] = new NonUniformScale
                            {
                                Value = stream.GetLocalToParentScale(writeTransforms[j].Index)
                            };
                        }

                        __UpdateChildren(writeTransforms[j].Entity, localToWorld);
                    }
                }
            }

            private void __UpdateChildren(in Entity entity, in float4x4 value)
            {
                if (!this.children.HasBuffer(entity))
                    return;

                var children = this.children[entity];
                Entity child;
                float3 scale, translation;
                quaternion rotation;
                LocalToWorld localToWorld;
                int numChildren = children.Length;
                for (int i = 0; i < numChildren; ++i)
                {
                    child = children[i].Value;
                    if (!localToWorlds.HasComponent(child))
                        continue;

                    if (localToParents.HasComponent(child))
                        localToWorld.Value = math.mul(value, localToParents[child].Value);
                    else
                    {
                        translation = translations.HasComponent(entity) ? translations[entity].Value : float3.zero;
                        rotation = rotations.HasComponent(entity) ? rotations[entity].Value : quaternion.identity;

                        if (scales.HasComponent(entity))
                            scale = scales[entity].Value;
                        else if (nonUniformScales.HasComponent(entity))
                            scale = nonUniformScales[entity].Value;
                        else
                            scale = 1.0f;

                        localToWorld.Value = float4x4.TRS(translation, rotation, scale);
                    }

                    localToWorlds[child] = localToWorld;

                    __UpdateChildren(child, localToWorld.Value);
                }
            }

        }

        private EntityQuery __worldSpaceOnlyQuery;
        private EntityQuery __rootSpaceOnlyQuery;
        private EntityQuery __worldAndRootSpaceQuery;
        private EntityQuery __writeComponentDataQuery;

        private ComponentTypeHandle<Rig> __rigTypeRO;
        private ComponentTypeHandle<RigRootEntity> __rigRootEntityTypeRO;
        private BufferTypeHandle<AnimatedData> __animatedDataTypeRO;
        private ComponentLookup<LocalToWorld> __entityLocalToWorldRO;

        private BufferTypeHandle<AnimatedLocalToRoot> __animatedLocalToRootType;
        private BufferTypeHandle<AnimatedLocalToWorld> __animatedLocalToWorldType;

        private BufferTypeHandle<AnimatedWriteTransformHandle> __writeTransformTypeRO;
        private BufferLookup<Child> __childrenRO;
        private ComponentLookup<LocalToWorld> __localToWorlds;
        private ComponentLookup<LocalToParent> __localToParents;
        private ComponentLookup<Translation> __translations;
        private ComponentLookup<Rotation> __rotations;
        private ComponentLookup<Scale> __scales;
        private ComponentLookup<NonUniformScale> __nonUniformScales;

        public void OnCreate(ref SystemState state)
        {
            __worldSpaceOnlyQuery = state.GetEntityQuery(ComputeWorldSpaceJob.QueryDesc);
            __rootSpaceOnlyQuery = state.GetEntityQuery(ComputeRootSpaceJob.QueryDesc);
            __worldAndRootSpaceQuery = state.GetEntityQuery(ComputeWorldAndRootSpaceJob.QueryDesc);
            __writeComponentDataQuery = state.GetEntityQuery(WriteTransformComponents.QueryDesc);

            __rigTypeRO = state.GetComponentTypeHandle<Rig>(true);
            __rigRootEntityTypeRO = state.GetComponentTypeHandle<RigRootEntity>(true);
            __animatedDataTypeRO = state.GetBufferTypeHandle<AnimatedData>(true);
            __entityLocalToWorldRO = state.GetComponentLookup<LocalToWorld>(true);

            __animatedLocalToRootType = state.GetBufferTypeHandle<AnimatedLocalToRoot>();
            __animatedLocalToWorldType = state.GetBufferTypeHandle<AnimatedLocalToWorld>();

            __writeTransformTypeRO = state.GetBufferTypeHandle<AnimatedWriteTransformHandle>(true);
            __childrenRO = state.GetBufferLookup<Child>(true);
            __localToWorlds = state.GetComponentLookup<LocalToWorld>();
            __localToParents = state.GetComponentLookup<LocalToParent>();
            __translations = state.GetComponentLookup<Translation>();
            __rotations = state.GetComponentLookup<Rotation>();
            __scales = state.GetComponentLookup<Scale>();
            __nonUniformScales = state.GetComponentLookup<NonUniformScale>();
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var rigTypeRO = __rigTypeRO.UpdateAsRef(ref state);
            var rigRootEntityTypeRO = __rigRootEntityTypeRO.UpdateAsRef(ref state);
            var animatedDataTypeRO = __animatedDataTypeRO.UpdateAsRef(ref state);
            var entityLocalToWorldRO = __entityLocalToWorldRO.UpdateAsRef(ref state);

            var animatedLocalToRootType = __animatedLocalToRootType.UpdateAsRef(ref state);
            var animatedLocalToWorldType = __animatedLocalToWorldType.UpdateAsRef(ref state);

            JobHandle worldSpaceOnlyHandle = state.Dependency;
            if (!__worldSpaceOnlyQuery.IsEmpty)
            {
                worldSpaceOnlyHandle = new ComputeWorldSpaceJob
                {
                    Rig = rigTypeRO,
                    RigRootEntity = rigRootEntityTypeRO,
                    EntityLocalToWorld = entityLocalToWorldRO,
                    AnimatedData = animatedDataTypeRO,
                    AnimatedLocalToWorld = animatedLocalToWorldType,
                }.ScheduleParallel(__worldSpaceOnlyQuery, worldSpaceOnlyHandle);
            }

            JobHandle rootSpaceOnlyHandle = state.Dependency;
            if (!__rootSpaceOnlyQuery.IsEmpty)
            {
                rootSpaceOnlyHandle = new ComputeRootSpaceJob
                {
                    Rigs = rigTypeRO,
                    AnimatedData = animatedDataTypeRO,
                    AnimatedLocalToRoot = animatedLocalToRootType,
                }.ScheduleParallel(__rootSpaceOnlyQuery, rootSpaceOnlyHandle);
            }

            // TODO : These jobs should ideally all run in parallel since the queries are mutually exclusive.
            //        For now, in order to prevent the safety system from throwing errors schedule
            //        WorldAndRootSpaceJob with a dependency on the two others.
            var jobHandle = JobHandle.CombineDependencies(worldSpaceOnlyHandle, rootSpaceOnlyHandle);
            if (!__worldAndRootSpaceQuery.IsEmpty)
            {
                jobHandle = new ComputeWorldAndRootSpaceJob
                {
                    Rig = rigTypeRO,
                    RigRootEntity = rigRootEntityTypeRO,
                    AnimatedData = animatedDataTypeRO,
                    EntityLocalToWorld = entityLocalToWorldRO,
                    AnimatedLocalToWorld = animatedLocalToWorldType,
                    AnimatedLocalToRoot = animatedLocalToRootType,
                }.ScheduleParallel(__worldAndRootSpaceQuery, jobHandle);
            }

            jobHandle = new WriteTransformComponents
            {
                rigType = rigTypeRO,
                animatedDataType = animatedDataTypeRO,
                animatedLocalToWorldType = animatedLocalToWorldType,
                writeTransformType = __writeTransformTypeRO.UpdateAsRef(ref state),
                children = __childrenRO.UpdateAsRef(ref state),
                localToWorlds = __localToWorlds.UpdateAsRef(ref state),
                localToParents = __localToParents.UpdateAsRef(ref state),
                translations = __translations.UpdateAsRef(ref state),
                rotations = __rotations.UpdateAsRef(ref state),
                scales = __scales.UpdateAsRef(ref state),
                nonUniformScales = __nonUniformScales.UpdateAsRef(ref state)
            }.ScheduleParallel(__writeComponentDataQuery, jobHandle);

            state.Dependency = jobHandle;
        }
    }

    [BurstCompile, UpdateInGroup(typeof(AnimationSystemGroup), OrderLast = true), UpdateAfter(typeof(AnimationComputeRigMatrixSystem))]
    public partial struct AnimationComputeDeformationDataSystem : ISystem
    {
        [BurstCompile /*(FloatMode = FloatMode.Fast)*/]
        private struct ComputeSkinMatrixJob : IJobChunk
        {
            [ReadOnly] public BufferLookup<AnimatedLocalToRoot> EntityAnimatedLocalToRoot;
            [ReadOnly] public ComponentLookup<RigRootEntity> EntityRigRootBone;
            [ReadOnly] public ComponentLookup<LocalToWorld> EntityLocalToWorld;

            [ReadOnly] public ComponentTypeHandle<RigEntity> RigEntityType;
            [ReadOnly] public ComponentTypeHandle<SkinnedMeshRootEntity> SkinnedMeshRootEntityType;
            [ReadOnly] public BufferTypeHandle<SkinnedMeshToRigIndexMapping> SkinnedMeshToRigIndexMappingType;
            [ReadOnly] public BufferTypeHandle<SkinnedMeshToRigIndexIndirectMapping> SkinnedMeshToRigIndexIndirectMappingType;
            [ReadOnly] public BufferTypeHandle<BindPose> BindPoseType;

            public BufferTypeHandle<SkinMatrix> SkinMatriceType;

            static public EntityQueryDesc QueryDesc => new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<RigEntity>(),
                    ComponentType.ReadOnly<SkinnedMeshRootEntity>(),
                    ComponentType.ReadOnly<SkinnedMeshToRigIndexMapping>(),
                    ComponentType.ReadOnly<SkinnedMeshToRigIndexIndirectMapping>(),
                    ComponentType.ReadOnly<BindPose>(),
                    ComponentType.ReadWrite<SkinMatrix>()
                }
            };

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var rigEntities = chunk.GetNativeArray(ref RigEntityType);
                var skinnedMeshRootEntities = chunk.GetNativeArray(ref SkinnedMeshRootEntityType);
                var skinnedMeshToRigIndexMappings = chunk.GetBufferAccessor(ref SkinnedMeshToRigIndexMappingType);
                var skinnedMeshToRigIndexIndirectMappings = chunk.GetBufferAccessor(ref SkinnedMeshToRigIndexIndirectMappingType);
                var bindPoses = chunk.GetBufferAccessor(ref BindPoseType);
                var outSkinMatrices = chunk.GetBufferAccessor(ref SkinMatriceType);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var rig = rigEntities[i].Value;
                    if (EntityAnimatedLocalToRoot.HasBuffer(rig) && EntityRigRootBone.HasComponent(rig))
                    {
                        var animatedLocalToRootMatrices = EntityAnimatedLocalToRoot[rig];
                        var rigRootL2W = EntityLocalToWorld[EntityRigRootBone[rig].Value].Value;
                        var smrRootL2W = EntityLocalToWorld[skinnedMeshRootEntities[i].Value].Value;

                        ComputeSkinMatrices(
                            math.mul(math.inverse(smrRootL2W), rigRootL2W),
                            skinnedMeshToRigIndexMappings[i],
                            skinnedMeshToRigIndexIndirectMappings[i],
                            bindPoses[i],
                            animatedLocalToRootMatrices,
                            outSkinMatrices[i]
                        );
                    }
                }
            }

            static void ComputeSkinMatrices(
                float4x4 smrToRigRootOffset,
                [ReadOnly] DynamicBuffer<SkinnedMeshToRigIndexMapping> smrToRigMappings,
                [ReadOnly] DynamicBuffer<SkinnedMeshToRigIndexIndirectMapping> smrToRigIndirectMappings,
                [ReadOnly] DynamicBuffer<BindPose> bindPoses,
                [ReadOnly] DynamicBuffer<AnimatedLocalToRoot> animatedLocalToRootMatrices,
                DynamicBuffer<SkinMatrix> outSkinMatrices
            )
            {
                for (int i = 0; i != smrToRigMappings.Length; ++i)
                {
                    var mapping = smrToRigMappings[i];

                    var skinMat = math.mul(math.mul(smrToRigRootOffset, animatedLocalToRootMatrices[mapping.RigIndex].Value), bindPoses[mapping.SkinMeshIndex].Value);
                    outSkinMatrices[mapping.SkinMeshIndex] = new SkinMatrix
                    {
                        Value = new float3x4(skinMat.c0.xyz, skinMat.c1.xyz, skinMat.c2.xyz, skinMat.c3.xyz)
                    };
                }

                for (int i = 0; i != smrToRigIndirectMappings.Length; ++i)
                {
                    var mapping = smrToRigIndirectMappings[i];

                    var skinMat = math.mul(math.mul(smrToRigRootOffset, math.mul(animatedLocalToRootMatrices[mapping.RigIndex].Value, mapping.Offset)), bindPoses[mapping.SkinMeshIndex].Value);
                    outSkinMatrices[mapping.SkinMeshIndex] = new SkinMatrix
                    {
                        Value = new float3x4(skinMat.c0.xyz, skinMat.c1.xyz, skinMat.c2.xyz, skinMat.c3.xyz)
                    };
                }
            }
        }

        [BurstCompile /*(FloatMode = FloatMode.Fast)*/]
        private struct CopyContiguousBlendShapeWeightJob : IJobChunk
        {
            [ReadOnly] public ComponentLookup<Rig> Rigs;
            [ReadOnly] public BufferLookup<AnimatedData> AnimatedData;

            [ReadOnly] public ComponentTypeHandle<RigEntity> RigEntityType;
            [ReadOnly] public ComponentTypeHandle<BlendShapeChunkMapping> BlendShapeChunkMappingType;

            public BufferTypeHandle<BlendShapeWeight> BlendShapeWeightType;

            static public EntityQueryDesc QueryDesc => new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<RigEntity>(),
                    ComponentType.ReadOnly<BlendShapeChunkMapping>(),
                    ComponentType.ReadWrite<BlendShapeWeight>()
                }
            };

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var rigEntities = chunk.GetNativeArray(ref RigEntityType);
                var blendShapeChunkMappings = chunk.GetNativeArray(ref BlendShapeChunkMappingType);
                var blendShapeWeightAccessor = chunk.GetBufferAccessor(ref BlendShapeWeightType);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var rigEntity = rigEntities[i].Value;
                    if (Rigs.HasComponent(rigEntity) && AnimatedData.HasBuffer(rigEntity))
                    {
                        var mapping = blendShapeChunkMappings[i];
                        var stream = AnimationStream.CreateReadOnly(Rigs[rigEntity], AnimatedData[rigEntity].AsNativeArray());
                        var blendShapeWeights = blendShapeWeightAccessor[i].AsNativeArray();

                        UnsafeUtility.MemCpy(
                            blendShapeWeights.GetUnsafePtr(),
                            (float*)stream.GetUnsafePtr() + stream.Rig.Value.Bindings.FloatSamplesOffset + mapping.RigIndex,
                            mapping.Size * UnsafeUtility.SizeOf<float>()
                        );
                    }
                }
            }
        }

        [BurstCompile /*(FloatMode = FloatMode.Fast)*/]
        private struct CopySparseBlendShapeWeightJob : IJobChunk
        {
            [ReadOnly] public ComponentLookup<Rig> Rigs;
            [ReadOnly] public BufferLookup<AnimatedData> AnimatedData;

            [ReadOnly] public ComponentTypeHandle<RigEntity> RigEntityType;
            [ReadOnly] public BufferTypeHandle<BlendShapeToRigIndexMapping> BlendShapeToRigIndexMappingType;

            public BufferTypeHandle<BlendShapeWeight> BlendShapeWeightType;

            static public EntityQueryDesc QueryDesc => new EntityQueryDesc()
            {
                All = new ComponentType[]
                {
                    ComponentType.ReadOnly<RigEntity>(),
                    ComponentType.ReadOnly<BlendShapeToRigIndexMapping>(),
                    ComponentType.ReadWrite<BlendShapeWeight>()
                }
            };

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var rigEntities = chunk.GetNativeArray(ref RigEntityType);
                var blendShapeToRigIndexMappings = chunk.GetBufferAccessor(ref BlendShapeToRigIndexMappingType);
                var blendShapeWeightAccessor = chunk.GetBufferAccessor(ref BlendShapeWeightType);

                var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (enumerator.NextEntityIndex(out int i))
                {
                    var rigEntity = rigEntities[i].Value;
                    if (Rigs.HasComponent(rigEntity) && AnimatedData.HasBuffer(rigEntity))
                    {
                        var mappings = blendShapeToRigIndexMappings[i];
                        var stream = AnimationStream.CreateReadOnly(Rigs[rigEntity], AnimatedData[rigEntity].AsNativeArray());
                        var blendShapeWeights = blendShapeWeightAccessor[i].AsNativeArray();

                        for (int j = 0; j != mappings.Length; ++j)
                        {
                            blendShapeWeights[mappings[j].BlendShapeIndex] = new BlendShapeWeight
                            {
                                Value = stream.GetFloat(mappings[j].RigIndex)
                            };
                        }
                    }
                }
            }
        }

        private EntityQuery __computeSkinMatrixQuery;
        private EntityQuery __copySparseBlendShapeWeightQuery;
        private EntityQuery __copyContiguousBlendShapeWeightQuery;

        public void OnCreate(ref SystemState state)
        {
            __computeSkinMatrixQuery = state.GetEntityQuery(ComputeSkinMatrixJob.QueryDesc);
            __copySparseBlendShapeWeightQuery = state.GetEntityQuery(CopySparseBlendShapeWeightJob.QueryDesc);
            __copyContiguousBlendShapeWeightQuery = state.GetEntityQuery(CopyContiguousBlendShapeWeightJob.QueryDesc);
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var inputDeps = state.Dependency;

            var computeSkinMatrixJob = new ComputeSkinMatrixJob
            {
                EntityAnimatedLocalToRoot = state.GetBufferLookup<AnimatedLocalToRoot>(true),
                EntityRigRootBone = state.GetComponentLookup<RigRootEntity>(true),
                EntityLocalToWorld = state.GetComponentLookup<LocalToWorld>(true),
                RigEntityType = state.GetComponentTypeHandle<RigEntity>(true),
                SkinnedMeshRootEntityType = state.GetComponentTypeHandle<SkinnedMeshRootEntity>(true),
                SkinnedMeshToRigIndexMappingType = state.GetBufferTypeHandle<SkinnedMeshToRigIndexMapping>(true),
                SkinnedMeshToRigIndexIndirectMappingType = state.GetBufferTypeHandle<SkinnedMeshToRigIndexIndirectMapping>(true),
                BindPoseType = state.GetBufferTypeHandle<BindPose>(true),
                SkinMatriceType = state.GetBufferTypeHandle<SkinMatrix>()
            }.ScheduleParallel(__computeSkinMatrixQuery, inputDeps);

            var copySparseBlendShapeJob = new CopySparseBlendShapeWeightJob
            {
                Rigs = state.GetComponentLookup<Rig>(true),
                AnimatedData = state.GetBufferLookup<AnimatedData>(true),
                RigEntityType = state.GetComponentTypeHandle<RigEntity>(true),
                BlendShapeToRigIndexMappingType = state.GetBufferTypeHandle<BlendShapeToRigIndexMapping>(true),
                BlendShapeWeightType = state.GetBufferTypeHandle<BlendShapeWeight>()
            }.ScheduleParallel(__copySparseBlendShapeWeightQuery, inputDeps);

            var copyContiguousBlendShapeJob = new CopyContiguousBlendShapeWeightJob
            {
                Rigs = state.GetComponentLookup<Rig>(true),
                AnimatedData = state.GetBufferLookup<AnimatedData>(true),
                RigEntityType = state.GetComponentTypeHandle<RigEntity>(true),
                BlendShapeChunkMappingType = state.GetComponentTypeHandle<BlendShapeChunkMapping>(true),
                BlendShapeWeightType = state.GetBufferTypeHandle<BlendShapeWeight>()
            }.ScheduleParallel(__copyContiguousBlendShapeWeightQuery, copySparseBlendShapeJob);

            state.Dependency = JobHandle.CombineDependencies(computeSkinMatrixJob, copyContiguousBlendShapeJob);
        }
    }
}