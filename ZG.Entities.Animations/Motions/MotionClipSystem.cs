using System;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Animation;
using Math = ZG.Mathematics.Math;

namespace ZG
{
    [Flags]
    public enum MotionClipFlag
    {
        Mirror = 0x01,
        InPlace = 0x02
    }

    public enum MotionClipWrapMode
    {
        Pause,
        Normal,
        Loop, 
        Managed
    }

    public enum MotionClipBlendingMode
    {
        //
        // 摘要:
        //     Animations overrides to the previous layers.
        Override,
        //
        // 摘要:
        //     Animations are added to the previous layers.
        Additive
    }

    public struct MotionClipWeightMaskDefinition
    {
        public struct Weight
        {
            public int index;

            public float value;
        }

        public BlobArray<Weight> translationWeights;
        public BlobArray<Weight> rotationWeights;
        public BlobArray<Weight> scaleWeights;
        public BlobArray<Weight> floatWeights;
        public BlobArray<Weight> intWeights;

        public void Apply(in ChannelMask mask, in BlobAssetReference<RigDefinition> rigDefinition, ref NativeArray<WeightData> values)
        {
            values.MemClear();

            ref var bindings = ref rigDefinition.Value.Bindings;

            WeightData value;

            int numTranslationWeights = translationWeights.Length;
            for (int i = 0; i < numTranslationWeights; ++i)
            {
                ref var weight = ref translationWeights[i];
                if (!mask.IsTranslationSet(weight.index))
                    continue;

                value.Value = weight.value;

                values[weight.index + bindings.TranslationSamplesOffset] = value;
            }

            int numRotationWeights = rotationWeights.Length;
            for (int i = 0; i < numRotationWeights; ++i)
            {
                ref var weight = ref rotationWeights[i];
                if (!mask.IsRotationSet(weight.index))
                    continue;

                value.Value = weight.value;

                values[weight.index + bindings.RotationSamplesOffset] = value;
            }

            int numScaleWeights = scaleWeights.Length;
            for (int i = 0; i < numScaleWeights; ++i)
            {
                ref var weight = ref scaleWeights[i];
                if (!mask.IsScaleSet(weight.index))
                    continue;

                value.Value = weight.value;

                values[weight.index + bindings.ScaleSamplesOffset] = value;
            }

            int numFloatWeights = floatWeights.Length;
            for (int i = 0; i < numFloatWeights; ++i)
            {
                ref var weight = ref floatWeights[i];
                if (!mask.IsFloatSet(weight.index))
                    continue;

                value.Value = weight.value;

                values[weight.index + bindings.FloatSamplesOffset] = value;
            }

            int numIntWeights = intWeights.Length;
            for (int i = 0; i < numIntWeights; ++i)
            {
                ref var weight = ref intWeights[i];
                if (!mask.IsIntSet(weight.index))
                    continue;

                value.Value = weight.value;

                values[weight.index + bindings.IntSamplesOffset] = value;
            }
        }
    }

    public struct MotionClipTransform
    {
        public static readonly MotionClipTransform Identity = new MotionClipTransform()
        {
            translation = float3.zero,
            rotation = quaternion.identity,
            scale = 1.0f
        };

        public float3 translation;
        public quaternion rotation;
        public float3 scale;

        public float4x4 matrix => float4x4.TRS(translation, rotation, scale);
    }

    public struct MotionClipFactory : IComponentData
    {
        public struct DestroyCommand
        {
            public Entity entity;
        }

        public struct CreateCommand
        {
            public Entity entity;
            public Rig rig;
            public StringHash rootID;
            public MotionClipTransform rootTransform;
        }

        private EntityCommandPool<DestroyCommand>.Context __destroyCommander;
        private EntityCommandPool<CreateCommand>.Context __createCommander;

        public EntityCommandPool<CreateCommand> createCommander => __createCommander.pool;
        public EntityCommandPool<DestroyCommand> destroyCommander => __destroyCommander.pool;

        public bool isCreated => __createCommander.isCreated && __destroyCommander.isCreated;

        public MotionClipFactory(Allocator allocator)
        {
            __destroyCommander = new EntityCommandPool<DestroyCommand>.Context(allocator);
            __createCommander = new EntityCommandPool<CreateCommand>.Context(allocator);
        }

        public void Dispsoe()
        {
            __destroyCommander.Dispose();
            __createCommander.Dispose();
        }

        public bool TryDequeueCreateCommand(out CreateCommand value) => __createCommander.TryDequeue(out value);

        public bool TryDequeueDestroyCommand(out DestroyCommand value) => __destroyCommander.TryDequeue(out value);
    }

    public struct MotionClipData : IComponentData
    {
        public StringHash rootID;
        public MotionClipTransform rootTransform;
    }

    public struct MotionClipLayer : IBufferElementData
    {
        public MotionClipBlendingMode blendingMode;
        public float weight;
    }

    public struct MotionClipLayerWeightMask : IBufferElementData
    {
        public BlobAssetReference<MotionClipWeightMaskDefinition> definition;
    }

    public struct MotionClip : IBufferElementData
    {
        public MotionClipFlag flag;
        public MotionClipWrapMode wrapMode;
        public int layerIndex;
        public int depth;
        public float speed;
        public BlobAssetReference<Clip> value;
        public BlobAssetReference<RigDefinition> remapDefinition;
        public BlobAssetReference<RigRemapTable> remapTable;
    }

    public struct MotionClipInstance : ICleanupBufferElementData
    {
        public MotionClipFlag flag;
        public int layerIndex;
        public int depth;
        public BlobAssetReference<ClipInstance> value;
        public BlobAssetReference<RigDefinition> remapDefinition;
        public BlobAssetReference<RigRemapTable> remapTable;
    }

    public struct MotionClipWeight : IBufferElementData
    {
        //public int version;
        public float value;
    }

    public struct MotionClipTime : IBufferElementData, IEnableableComponent
    {
        public float value;
    }

    public struct MotionClipSafeTime : IComponentData
    {
        public double value;
    }

        //LateAnimationSystemGroup For pass parent system
    [BurstCompile, UpdateInGroup(typeof(AnimationSystemGroup), OrderFirst = true)]
    public partial struct MotionClipSystem : ISystem
    {
        public struct Key : IEquatable<Key>
        {
            public int clipHashCode;
            public int rigHashCode;

            public bool Equals(Key other)
            {
                return clipHashCode == other.clipHashCode && rigHashCode == other.rigHashCode;
            }

            public override int GetHashCode()
            {
                return clipHashCode ^ rigHashCode;
            }
        }

        private struct Value
        {
            public int count;

            public BlobAssetReference<ClipInstance> clipInstance;
        }

        private struct Clear
        {
            [ReadOnly]
            public BufferAccessor<MotionClipInstance> clipInstances;

            public NativeParallelHashMap<Key, Value> instances;

            public void Execute(int index)
            {
                var clipInstances = this.clipInstances[index];
                BlobAssetReference<ClipInstance> clipInstance;
                int length = clipInstances.Length;
                for (int i = 0; i < length; ++i)
                {
                    clipInstance = clipInstances[i].value;
                    if (!clipInstance.IsCreated)
                        continue;

                    ref var value = ref clipInstances[i].value.Value;

                    __Release(ref instances, value.ClipHashCode, value.RigHashCode);
                }
            }
        }

        [BurstCompile]
        private struct ClearEx : IJobChunk
        {
            [ReadOnly]
            public BufferTypeHandle<MotionClipInstance> clipInstanceType;

            public NativeParallelHashMap<Key, Value> instances;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Clear clear;
                clear.clipInstances = chunk.GetBufferAccessor(ref clipInstanceType);
                clear.instances = instances;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    clear.Execute(i);
            }
        }

        private struct Sync
        {
            [ReadOnly]
            public NativeArray<Rig> rigs;
            [ReadOnly]
            public BufferAccessor<MotionClip> clips;
            public BufferAccessor<MotionClipInstance> clipInstances;

            public NativeParallelHashMap<Key, Value> instances;

            public void Execute(int index)
            {
                var clips = this.clips[index];
                var clipInstances = this.clipInstances[index];

                int numClips = clips.Length,
                    numClipInstances = clipInstances.Length;

                var rigDefinition = rigs[index].Value;

                MotionClip clip;
                MotionClipInstance clipInstance;
                if (numClipInstances > numClips)
                {
                    for (int i = numClips; i < numClipInstances; ++i)
                    {
                        clipInstance = clipInstances[i];
                        if (clipInstance.value.IsCreated)
                        {
                            ref var temp = ref clipInstances[i].value.Value;

                            __Release(ref instances, temp.ClipHashCode, temp.RigHashCode);
                        }
                    }

                    clipInstances.ResizeUninitialized(numClips);
                }
                else if (numClips > numClipInstances)
                {
                    for (int i = numClipInstances; i < numClips; ++i)
                    {
                        clip = clips[i];
                        clipInstance.flag = clip.flag;
                        clipInstance.layerIndex = clip.layerIndex;
                        clipInstance.depth = clip.depth;
                        clipInstance.value = clip.value.IsCreated ? __Retain(ref instances, clip.value, clip.remapDefinition.IsCreated ? clip.remapDefinition : rigDefinition) : BlobAssetReference<ClipInstance>.Null;
                        clipInstance.remapDefinition = clip.remapDefinition;
                        clipInstance.remapTable = clip.remapTable;
                        clipInstances.Add(clipInstance);
                    }

                    numClips = numClipInstances;
                }

                bool isDirty;
                int rigHashCode = rigDefinition.Value.GetHashCode();
                for (int i = 0; i < numClips; ++i)
                {
                    clip = clips[i];
                    clipInstance = clipInstances[i];

                    isDirty = false;

                    if (clip.flag != clipInstance.flag)
                    {
                        clipInstance.flag = clip.flag;

                        isDirty = true;
                    }

                    if (clip.layerIndex != clipInstance.layerIndex)
                    {
                        clipInstance.layerIndex = clip.layerIndex;

                        isDirty = true;
                    }

                    if (clip.depth != clipInstance.depth)
                    {
                        clipInstance.depth = clip.depth;

                        isDirty = true;
                    }

                    if (clipInstance.value.IsCreated)
                    {
                        ref var temp = ref clipInstance.value.Value;

                        if (clip.value.IsCreated)
                        {
                            if (temp.RigHashCode != (clip.remapDefinition.IsCreated ? clip.remapDefinition.Value.GetHashCode() : rigHashCode) ||
                                temp.ClipHashCode != clip.value.Value.GetHashCode() ||
                                clipInstance.remapDefinition != clip.remapDefinition ||
                                clipInstance.remapTable != clip.remapTable)
                            {
                                __Release(ref instances, temp.ClipHashCode, temp.RigHashCode);

                                clipInstance.value = __Retain(ref instances, clip.value, clip.remapDefinition.IsCreated ? clip.remapDefinition : rigDefinition);
                                clipInstance.remapDefinition = clip.remapDefinition;
                                clipInstance.remapTable = clip.remapTable;

                                isDirty = true;
                            }
                        }
                        else
                        {
                            __Release(ref instances, temp.ClipHashCode, temp.RigHashCode);

                            clipInstance.value = BlobAssetReference<ClipInstance>.Null;
                            clipInstance.remapDefinition = clip.remapDefinition;
                            clipInstance.remapTable = clip.remapTable;

                            isDirty = true;
                        }
                    }
                    else if (clip.value.IsCreated)
                    {
                        clipInstance.value = __Retain(ref instances, clip.value, clip.remapDefinition.IsCreated ? clip.remapDefinition : rigDefinition);
                        clipInstance.remapDefinition = clip.remapDefinition;
                        clipInstance.remapTable = clip.remapTable;

                        isDirty = true;
                    }

                    if (isDirty)
                        clipInstances[i] = clipInstance;
                }
            }
        }

        [BurstCompile]
        private struct SyncEx : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<Rig> rigType;
            [ReadOnly]
            public BufferTypeHandle<MotionClip> clipType;
            public BufferTypeHandle<MotionClipInstance> clipInstanceType;

            public NativeParallelHashMap<Key, Value> instances;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Sync sync;
                sync.rigs = chunk.GetNativeArray(ref rigType);
                sync.clips = chunk.GetBufferAccessor(ref clipType);
                sync.clipInstances = chunk.GetBufferAccessor(ref clipInstanceType);
                sync.instances = instances;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    sync.Execute(i);
            }
        }

        private EntityQuery __groupToDestroy;
        private EntityQuery __groupToCreate;
        private EntityQuery __groupToUpdate;
        private NativeHashMapLite<Key, Value> __instances;

        public void OnCreate(ref SystemState state)
        {
            __groupToDestroy = state.GetEntityQuery(ComponentType.ReadOnly<MotionClipInstance>(), ComponentType.Exclude<MotionClip>());
            __groupToCreate = state.GetEntityQuery(ComponentType.ReadOnly<MotionClip>(), ComponentType.Exclude<MotionClipInstance>());
            __groupToUpdate = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadOnly<Rig>(),
                        ComponentType.ReadOnly<MotionClip>(),
                        ComponentType.ReadWrite<MotionClipInstance>()
                    },
                    Options = EntityQueryOptions.IncludeDisabledEntities
                });
            __groupToUpdate.SetChangedVersionFilter(new ComponentType[] { typeof(Rig), typeof(MotionClip) });

            __instances = new NativeHashMapLite<Key, Value>(1, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            using (var values = ((NativeParallelHashMap<Key, Value>)__instances).GetValueArray(Allocator.Temp))
            {
                int length = values.Length;
                for (int i = 0; i < length; ++i)
                    values[i].clipInstance.Dispose();
            }

            __instances.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var entityManager = state.EntityManager;
            if (!__groupToDestroy.IsEmptyIgnoreFilter)
            {
                state.CompleteDependency();

                ClearEx clear;
                clear.clipInstanceType = state.GetBufferTypeHandle<MotionClipInstance>(true);
                clear.instances = __instances;
                clear.Run(__groupToDestroy);

                entityManager.RemoveComponent<MotionClipInstance>(__groupToDestroy);
            }

            entityManager.AddComponent<MotionClipInstance>(__groupToCreate);

            if (!__groupToUpdate.IsEmptyIgnoreFilter)
            {
                SyncEx sync;
#if USE_MOTION_CLIP_DFG
                //TODO: Write For Kernel Bug 
                ///<see cref="ProcessAnimationGraphBase.OnUpdate"/>
                ///<see cref="RenderGraph.CopyWorlds"/>
                ///<see cref="RepatchDFGInputsIfNeededJob.PatchDFGInputsFor"/>
                sync.rigType = state.GetComponentTypeHandle<Rig>();
#else
                sync.rigType = state.GetComponentTypeHandle<Rig>(true);
#endif
                sync.clipType = state.GetBufferTypeHandle<MotionClip>(true);
                sync.clipInstanceType = state.GetBufferTypeHandle<MotionClipInstance>();
                sync.instances = __instances;
                state.Dependency = sync.Schedule(__groupToUpdate, state.Dependency);
            }
        }

        private static BlobAssetReference<ClipInstance> __Retain(
            ref NativeParallelHashMap<Key, Value> instances,
            in BlobAssetReference<Clip> clip,
            in BlobAssetReference<RigDefinition> rigDefinition)
        {
            BlobAssetReference<ClipInstance> result;

            Key key;
            key.clipHashCode = clip.Value.GetHashCode();
            key.rigHashCode = rigDefinition.Value.GetHashCode();

            if (instances.TryGetValue(key, out var instance))
            {
                result = instance.clipInstance;

                ++instance.count;
            }
            else
            {
                instance.count = 1;

                ClipInstanceBuilder.CreateClipInstanceJob createClipInstance;
                createClipInstance.RigDefinition = rigDefinition;
                createClipInstance.SourceClip = clip;
                createClipInstance.BlobBuilder = new BlobBuilder(Allocator.Temp);
                createClipInstance.Execute();

                result = createClipInstance.BlobBuilder.CreateBlobAssetReference<ClipInstance>(Allocator.Persistent);
                createClipInstance.BlobBuilder.Dispose();

                instance.clipInstance = result;
            }

            instances[key] = instance;

            return result;
        }

        private static void __Release(ref NativeParallelHashMap<Key, Value> instances, int clipHashCode, int rigHashCode)
        {
            Key key;
            key.clipHashCode = clipHashCode;
            key.rigHashCode = rigHashCode;
            if (instances.TryGetValue(key, out var instance))
            {
                --instance.count;
                /*if (--instance.count < 1)
                {
                    instance.clip.Dispose();

                    instances.Remove(key);
                }
                else*/
                instances[key] = instance;
            }
        }
    }

    [BurstCompile, UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct MotionClipTimeSystem : ISystem
    {
        private struct UpdateFrames
        {
            public float deltaTime;

            [ReadOnly]
            public BufferAccessor<MotionClip> clips;

            public BufferAccessor<MotionClipTime> times;

            public bool Execute(int index)
            {
                var clips = this.clips[index];
                DynamicBuffer<MotionClipTime> times = default;

                MotionClipTime time;
                MotionClip clip;
                int length = clips.Length;
                bool isPlaying = false;
                for (int i = 0; i < length; ++i)
                {
                    clip = clips[i];

                    switch (clip.wrapMode)
                    {
                        case MotionClipWrapMode.Normal:
                            if (!times.IsCreated)
                            {
                                if (index < this.times.Length)
                                    times = this.times[index];
                                else
                                    break;
                            }
                            time = times[i];

                            time.value = time.value + deltaTime * clip.speed;
                            if (clip.value.IsCreated && clip.value.Value.Duration > math.FLT_MIN_NORMAL)
                            {
                                if (math.abs(time.value) > clip.value.Value.Duration)
                                    time.value = time.value < 0.0f ? 0.0f : clip.value.Value.Duration;
                                else
                                {
                                    if (time.value < 0.0f)
                                        time.value += clip.value.Value.Duration;

                                    isPlaying = true;
                                }
                            }

                            times[i] = time;
                            break;
                        case MotionClipWrapMode.Loop:
                            if (clip.value.IsCreated)
                            {
                                if (!times.IsCreated)
                                {
                                    if (index < this.times.Length)
                                        times = this.times[index];
                                    else
                                        break;
                                }

                                time = times[i];
                                time.value += deltaTime * clip.speed;

                                if (clip.value.IsCreated && clip.value.Value.Duration > math.FLT_MIN_NORMAL)
                                {
                                    time.value = Math.Repeat(time.value, clip.value.Value.Duration);

                                    isPlaying = true;
                                }

                                times[i] = time;
                            }
                            break;
                        case MotionClipWrapMode.Managed:
                            isPlaying = true;
                            break;
                    }
                }

                return isPlaying;
            }
        }

        [BurstCompile]
        private struct UpdateFramesEx : IJobChunk, IEntityCommandProducerJob
        {
            public float deltaTime;
            public double elpasedTime;

            [ReadOnly]
            public EntityTypeHandle entityType;

            [ReadOnly]
            public ComponentTypeHandle<Rig> rigType;

            [ReadOnly]
            public ComponentTypeHandle<MotionClipData> instanceType;

            [ReadOnly]
            public ComponentTypeHandle<MotionClipSafeTime> safeTimeType;

            [ReadOnly]
            public BufferTypeHandle<MotionClip> clipType;

            public BufferTypeHandle<MotionClipTime> timeType;

            //public BufferTypeHandle<MotionClipWeight> weightType;

            [NativeDisableContainerSafetyRestriction]
            public EntityCommandQueue<MotionClipFactory.CreateCommand>.ParallelWriter createCommander;
            [NativeDisableContainerSafetyRestriction]
            public EntityCommandQueue<MotionClipFactory.DestroyCommand>.ParallelWriter destroyCommander;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                UpdateFrames updateFrames;
                updateFrames.deltaTime = deltaTime;
                updateFrames.clips = chunk.GetBufferAccessor(ref clipType);
                updateFrames.times = chunk.GetBufferAccessor(ref timeType);

                bool isPlaying;
                Entity entity;
                MotionClipData instance;
                MotionClipFactory.CreateCommand createCommand;
                var entityArray = chunk.GetNativeArray(entityType);
                var instances = chunk.GetNativeArray(ref instanceType);
                var rigs = chunk.GetNativeArray(ref rigType);
                var safeTimes = chunk.GetNativeArray(ref safeTimeType);
                //var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                //while (iterator.NextEntityIndex(out int i))
                int count = chunk.Count;
                for(int i = 0; i < count; ++i)
                {
                    isPlaying = updateFrames.Execute(i);

                    if (isPlaying != chunk.IsComponentEnabled(ref timeType, i))
                    {
                        entity = entityArray[i];
                        if (isPlaying)
                        {
                            if (createCommander.isCreated && i < rigs.Length && i < instances.Length)
                            {
                                instance = instances[i];

                                createCommand.entity = entity;
                                createCommand.rig = rigs[i];
                                createCommand.rootID = instance.rootID;
                                createCommand.rootTransform = instance.rootTransform;

                                createCommander.Enqueue(createCommand);
                            }
                        }

                        ///�����������BUG(entityNode��Ϊ��󷵻�����ȴ����ִ��)�������ӳ�һ֡�ȴ����ݽ������
                        ///<see cref="InternalComponentNode.GraphKernel.Execute"/>
                        ///<see cref="RenderGraph.WorldRenderingScheduleJob.ScheduleJobified"/>
                        ///<see cref="TopologyAPI.CacheAPI.RecursiveDependencySearch"/>
                        else if (destroyCommander.isCreated && (i >= safeTimes.Length || safeTimes[i].value < elpasedTime))
                        {
                            MotionClipFactory.DestroyCommand destroyCommand;
                            destroyCommand.entity = entity;
                            destroyCommander.Enqueue(destroyCommand);
                        }

                        chunk.SetComponentEnabled(ref timeType, i, isPlaying);
                    }
                }
            }
        }

        private EntityQuery __factoryGroup;
        private EntityQuery __group;

        public void OnCreate(ref SystemState state)
        {
            __factoryGroup = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadWrite<MotionClipFactory>()
                    }, 
                    Options = EntityQueryOptions.IncludeSystems
                });

            __group = state.GetEntityQuery(ComponentType.ReadOnly<MotionClip>()/*, ComponentType.ReadWrite<MotionClipTime>()*/);
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var factory = __factoryGroup.IsEmpty ? default : __factoryGroup.GetSingleton<MotionClipFactory>();

            var createCommander = factory.isCreated ? factory.createCommander.Create() : default;
            var destroyCommander = factory.isCreated ? factory.destroyCommander.Create() : default;

            ref readonly var time = ref state.WorldUnmanaged.Time;

            UpdateFramesEx updateFrames;
            updateFrames.deltaTime = time.DeltaTime;
            updateFrames.elpasedTime = time.ElapsedTime;
            updateFrames.entityType = state.GetEntityTypeHandle();
            updateFrames.rigType = state.GetComponentTypeHandle<Rig>(true);
            updateFrames.instanceType = state.GetComponentTypeHandle<MotionClipData>(true);
            updateFrames.safeTimeType = state.GetComponentTypeHandle<MotionClipSafeTime>(true);
            updateFrames.clipType = state.GetBufferTypeHandle<MotionClip>(true);
            updateFrames.timeType = state.GetBufferTypeHandle<MotionClipTime>();
            updateFrames.createCommander = createCommander.isCreated ? createCommander.parallelWriter : default;
            updateFrames.destroyCommander = destroyCommander.isCreated ? destroyCommander.parallelWriter : default;

            var jobHandle = updateFrames.ScheduleParallel(__group, state.Dependency);

            if(createCommander.isCreated)
                createCommander.AddJobHandleForProducer<UpdateFramesEx>(jobHandle);

            if(destroyCommander.isCreated)
                destroyCommander.AddJobHandleForProducer<UpdateFramesEx>(jobHandle);

            state.Dependency = jobHandle;
        }
    }
}