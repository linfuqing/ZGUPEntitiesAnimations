using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Animation;

namespace ZG
{
    [System.Serializable]
    public struct MotionClipWeightStep : IBufferElementData
    {
        public float from;
        public float to;

        public float duration;

        public double time;

        public float Evaluate(double time)
        {
            //return 0.5f;
            return math.lerp(from, to, (float)math.smoothstep(this.time, this.time + duration, time));
        }
    }

    [BurstCompile, UpdateBefore(typeof(AnimationSystemGroup))]
    public partial struct MotionClipBlendSystem : ISystem
    {
        [BurstCompile]
        private struct Apply : IJobChunk
        {
            public double time;

            public BufferTypeHandle<MotionClipWeightStep> stepType;

            public BufferTypeHandle<MotionClipWeight> weightType;

            public BufferTypeHandle<MotionClipTime> timeType;

            public BufferTypeHandle<MotionClip> clipType;

            public BufferTypeHandle<MotionClipInstance> clipInstanceType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var stepAccessor = chunk.GetBufferAccessor(ref stepType);
                var weightAccessor = chunk.GetBufferAccessor(ref weightType);
                BufferAccessor<MotionClipTime> timeAccessor = default;
                BufferAccessor<MotionClip> clipAccessor = default;
                BufferAccessor<MotionClipInstance> clipInstanceAccessor = default;
                DynamicBuffer<MotionClipInstance> clipInstances = default;
                DynamicBuffer<MotionClip> clips = default;
                DynamicBuffer<MotionClipTime> times = default;
                DynamicBuffer<MotionClipWeight> weights;
                DynamicBuffer<MotionClipWeightStep> steps;
                MotionClipWeight weight;
                MotionClipInstance clipInstance;
                int i, j, index, end, length;
                bool isInitAccessor = false, isInit;
                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out i))
                {
                    isInit = false;

                    steps = stepAccessor[i];
                    weights = weightAccessor[i];

                    length = weights.Length;
                    index = steps.Length;
                    if (index > length)
                        steps.ResizeUninitialized(length);
                    else
                        length = index;

                    for (j = 0; j < length; ++j)
                    {
                        weight = weights[j];
                        weight.value = steps[j].Evaluate(this.time);
                        if (weight.value > math.FLT_MIN_NORMAL)
                        {
                            weight.value = math.min(weight.value, 1.0f);
                            weights[j] = weight;
                        }
                        else
                        {
                            if (!isInitAccessor)
                            {
                                isInitAccessor = true;

                                timeAccessor = chunk.GetBufferAccessor(ref timeType);
                                clipAccessor = chunk.GetBufferAccessor(ref clipType);
                                clipInstanceAccessor = chunk.GetBufferAccessor(ref clipInstanceType);
                            }

                            if (!isInit)
                            {
                                isInit = true;

                                times = timeAccessor[i];
                                clips = clipAccessor[i];
                                clipInstances = clipInstanceAccessor[i];
                            }

                            steps.RemoveAtSwapBack(j);
                            weights.RemoveAtSwapBack(j);
                            times.RemoveAtSwapBack(j);
                            clips.RemoveAtSwapBack(j);

                            /*if(times.Length < 1)
                            {
                                Debug.LogError($"{clips.Length}");
                            }*/

                            if (clipInstances.Length < length--)
                            {
                                index = --j;

                                end = length - 1;
                            }
                            else
                            {
                                index = j--;

                                end = length;
                            }

                            if (end != index)
                            {
                                clipInstance = clipInstances[end];
                                clipInstances[end] = clipInstances[index];
                                clipInstances[index] = clipInstance;
                            }
                        }
                    }
                }
            }
        }

        private EntityQuery __group;

        public static void Play(
            float blendTime,
            float playTime,
            double elpasedTime,
            in MotionClip clip,
            ref DynamicBuffer<MotionClip> clips,
            ref DynamicBuffer<MotionClipTime> times,
            ref DynamicBuffer<MotionClipWeight> weights,
            ref DynamicBuffer<MotionClipWeightStep> steps)
        {
            /*{
                //clips.Clear();
                //times.Clear();
                //weights.Clear();
                //steps.Clear();

                clips.Add(clip);

                MotionClipTime time;
                time.value = playTime;
                times.Add(time);

                MotionClipWeight weight;
                weight.value = 0.5f;
                weights.Add(weight);

                MotionClipWeightStep step;
                step.from = 1.0f;
                step.to = 1.0f;
                step.duration = blendTime;
                step.time = elpasedTime;
                steps.Add(step);

                return;
            }*/

            {
                int i, numClips = clips.Length, numWeights = weights.Length;
                weights.ResizeUninitialized(numClips);

                MotionClipWeight weight;
                //weight.version = MotionClipSystem.Version.Data;
                weight.value = 0.0f;
                for (i = numWeights; i < numClips; ++i)
                    weights[i] = weight;

                if (numClips > 0)
                {
                    for (i = 0; i < numClips; ++i)
                    {
                        if (clips[i].value == clip.value)
                        {
                            weight.value = 1.0f - weights[i].value;

                            break;
                        }

                        weight.value += weights[i].value;
                    }

                    weight.value = 1.0f - weight.value;
                }
                else
                {
                    i = 0;

                    weight.value = 1.0f;
                }

                MotionClipWeightStep step;
                step.to = 1.0f;
                step.duration = blendTime;
                step.time = elpasedTime;

                MotionClipTime time;
                time.value = playTime;

                steps.ResizeUninitialized(numClips);

                if (i < numClips)
                {
                    step.from = weights[i].value;

                    clips[i] = clip;

                    //if (math.abs(times[i].value - playTime) > blendTime)
                        times[i] = time;

                    weights[i] = weight;

                    steps[i] = step;
                }
                else
                {
                    i = 0;

                    step.from = numClips++ > 0 ? 0.0f : 1.0f;

                    clips.Insert(0, clip);

                    times.Insert(0, time);

                    weights.Insert(0, weight);

                    steps.Insert(0, step);
                }

                if (steps.Length != clips.Length || 
                    clips.Length != times.Length ||
                    times.Length != weights.Length ||
                    weights.Length != steps.Length)
                    UnityEngine.Debug.Log($"Blend {steps.Length} : {clips.Length} : {times.Length} : {weights.Length}");

                step.to = 0.0f;

                for (int j = 0; j < numClips; ++j)
                {
                    if (j == i)
                        continue;

                    step.from = weights[j].value;
                    steps[j] = step;
                }
            }
        }

        public static void Pause(
            ref DynamicBuffer<MotionClip> clips,
            ref DynamicBuffer<MotionClipTime> times,
            ref DynamicBuffer<MotionClipWeight> weights,
            ref DynamicBuffer<MotionClipWeightStep> steps)
        {
            clips.Clear();
            times.Clear();
            weights.Clear();
            steps.Clear();
        }

        public void OnCreate(ref SystemState state)
        {
            __group = state.GetEntityQuery(
                new EntityQueryDesc()
                {
                    All = new ComponentType[]
                    {
                        ComponentType.ReadWrite<MotionClipWeightStep>(),
                        ComponentType.ReadWrite<MotionClipWeight>(),
                        ComponentType.ReadWrite<MotionClipTime>(),
                        ComponentType.ReadWrite<MotionClip>(),
                        ComponentType.ReadWrite<MotionClipInstance>()
                    },
                    //Options = EntityQueryOptions.FilterWriteGroup
                });
        }

        public void OnDestroy(ref SystemState state)
        {

        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            Apply apply;
            apply.time = state.WorldUnmanaged.Time.ElapsedTime;
            apply.stepType = state.GetBufferTypeHandle<MotionClipWeightStep>();
            apply.weightType = state.GetBufferTypeHandle<MotionClipWeight>();
            apply.timeType = state.GetBufferTypeHandle<MotionClipTime>();
            apply.clipType = state.GetBufferTypeHandle<MotionClip>();
            apply.clipInstanceType = state.GetBufferTypeHandle<MotionClipInstance>();

            state.Dependency = apply.ScheduleParallel(__group, state.Dependency);
        }
    }
}
