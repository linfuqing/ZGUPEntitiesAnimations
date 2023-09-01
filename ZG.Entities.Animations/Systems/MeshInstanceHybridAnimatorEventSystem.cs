using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Animation;
using UnityEngine;

namespace ZG
{
    public interface IMeshInstanceHybridAnimatorEventHandler
    {
        void Dispatch(Transform root, float weight, int state);
    }

    public abstract class MeshInstanceHybridAnimatorEventConfigBase : ScriptableObject
    {
        public abstract string typeName { get; }

        public abstract int GetState(AnimationEvent animationEvent);
    }

#if UNITY_EDITOR
    public abstract class MeshInstanceHybridAnimatorEventConfig<T> : MeshInstanceHybridAnimatorEventConfigBase where T : IMeshInstanceHybridAnimatorEventHandler
    {
        public override string typeName => typeof(T).AssemblyQualifiedName;
    }

    public class MeshInstanceHybridAnimatorEventFactory : IMeshInstanceClipEventFactory
    {
        public bool Create(AnimationEvent input, out MeshInstanceClipEvent output)
        {
            var handler = input.objectReferenceParameter as MeshInstanceHybridAnimatorEventConfigBase;
            if (handler == null)
            {
                output = default;

                return false;
            }

            output.type = handler.typeName;
            output.state = handler.GetState(input);

            return true;
        }
    }
#endif

    [BurstCompile, UpdateBefore(typeof(AnimatorControllerSystem))]
    public partial struct MeshInstanceHybridAnimatorEventCollectSystem : ISystem
    {
        private struct Count
        {
            [ReadOnly]
            public BufferLookup<AnimatorControllerEvent> events;

            [ReadOnly]
            public BufferAccessor<MeshInstanceRig> rigs;

            [ReadOnly]
            public NativeArray<MeshInstanceAnimatorData> instances;

            public NativeCounter.Concurrent counter;

            public void Execute(int index)
            {
                var rigs = this.rigs[index];
                ref var definition = ref instances[index].definition.Value;
                Entity entity;
                int numRigs = definition.rigs.Length;
                for (int i = 0; i < numRigs; ++i)
                {
                    entity = rigs[definition.rigs[i].index].entity;
                    if (events.HasBuffer(entity))
                        counter.Add(events[entity].Length);
                }
            }
        }


        [BurstCompile]
        private struct CountEx : IJobChunk
        {
            [ReadOnly]
            public BufferLookup<AnimatorControllerEvent> events;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> rigType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceAnimatorData> instanceType;

            public NativeCounter.Concurrent counter;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Count count;
                count.events = events;
                count.rigs = chunk.GetBufferAccessor(ref rigType);
                count.instances = chunk.GetNativeArray(ref instanceType);
                count.counter = counter;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    count.Execute(i);
            }
        }

        [BurstCompile]
        private struct Init : IJob
        {
            public NativeCounter counter;

            public SharedMultiHashMap<EntityObject<Transform>, AnimatorControllerEvent>.Writer events;

            public void Execute()
            {
                events.Clear();
                events.capacity = Unity.Mathematics.math.max(events.capacity, counter.count);

                counter.count = 0;
            }
        }

        private struct Collect
        {
            [ReadOnly]
            public BufferLookup<AnimatorControllerEvent> events;

            [ReadOnly]
            public BufferAccessor<MeshInstanceRig> rigs;

            [ReadOnly]
            public NativeArray<MeshInstanceAnimatorData> instances;

            [ReadOnly]
            public NativeArray<EntityObject<Transform>> transforms;

            public SharedMultiHashMap<EntityObject<Transform>, AnimatorControllerEvent>.ParallelWriter results;

            public void Execute(int index)
            {
                var rigs = this.rigs[index];
                var transform = transforms[index];
                ref var definition = ref instances[index].definition.Value;
                DynamicBuffer<AnimatorControllerEvent> results;
                Entity entity;
                int numRigs = definition.rigs.Length, numResults, i, j;
                for (i = 0; i < numRigs; ++i)
                {
                    entity = rigs[definition.rigs[i].index].entity;
                    if (events.HasBuffer(entity))
                    {
                        results = events[entity];

                        numResults = results.Length;
                        for (j = 0; j < numResults; ++j)
                            this.results.Add(transform, results[j]);
                    }
                }
            }
        }

        [BurstCompile]
        private struct CollectEx : IJobChunk
        {
            [ReadOnly]
            public BufferLookup<AnimatorControllerEvent> events;

            [ReadOnly]
            public BufferTypeHandle<MeshInstanceRig> rigType;

            [ReadOnly]
            public ComponentTypeHandle<MeshInstanceAnimatorData> instanceType;

            [ReadOnly]
            public ComponentTypeHandle<EntityObject<Transform>> transformType;

            public SharedMultiHashMap<EntityObject<Transform>, AnimatorControllerEvent>.ParallelWriter results;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Collect collect;
                collect.events = events;
                collect.rigs = chunk.GetBufferAccessor(ref rigType);
                collect.instances = chunk.GetNativeArray(ref instanceType);
                collect.transforms = chunk.GetNativeArray(ref transformType);
                collect.results = results;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    collect.Execute(i);
            }
        }

        private EntityQuery __group;
        private BufferLookup<AnimatorControllerEvent> __events;
        private BufferTypeHandle<MeshInstanceRig> __rigType;
        private ComponentTypeHandle<MeshInstanceAnimatorData> __instanceType;
        private ComponentTypeHandle<EntityObject<Transform>> __transformType;
        private NativeCounter __counter;

        public SharedMultiHashMap<EntityObject<Transform>, AnimatorControllerEvent> events
        {
            get;

            private set;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAll<MeshInstanceRig, MeshInstanceAnimatorData, EntityObject<Transform>>()
                        .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                        .Build(ref state);
            __events = state.GetBufferLookup<AnimatorControllerEvent>(true);
            __rigType = state.GetBufferTypeHandle<MeshInstanceRig>(true);
            __instanceType = state.GetComponentTypeHandle<MeshInstanceAnimatorData>(true);
            __transformType = state.GetComponentTypeHandle<EntityObject<Transform>>(true);

            __counter = new NativeCounter(Allocator.Persistent);

            events = new SharedMultiHashMap<EntityObject<Transform>, AnimatorControllerEvent>(Allocator.Persistent);
        }

        //[BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __counter.Dispose();

            events.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var events = __events.UpdateAsRef(ref state);
            var rigType = __rigType.UpdateAsRef(ref state);
            var instanceType = __instanceType.UpdateAsRef(ref state);

            CountEx count;
            count.events = events;
            count.rigType = rigType;
            count.instanceType = instanceType;
            count.counter = __counter;

            var jobHandle = count.ScheduleParallelByRef(__group, state.Dependency);

            var results = this.events;
            ref var lookupJobManager = ref results.lookupJobManager;
            lookupJobManager.CompleteReadWriteDependency();

            Init init;
            init.counter = __counter;
            init.events = results.writer;
            jobHandle = init.ScheduleByRef(jobHandle);

            CollectEx collect;
            collect.events = events;
            collect.rigType = rigType;
            collect.instanceType = instanceType;
            collect.transformType = __transformType.UpdateAsRef(ref state);
            collect.results = results.parallelWriter;

            jobHandle = collect.ScheduleParallelByRef(__group, jobHandle);

            lookupJobManager.readWriteJobHandle = jobHandle;

            state.Dependency = jobHandle;
        }
    }

    [CreateAfter(typeof(MeshInstanceHybridAnimatorEventCollectSystem)), UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class MeshInstanceHybridAnimatorEventDispatchSystem : SystemBase
    {
        private SharedMultiHashMap<EntityObject<Transform>, AnimatorControllerEvent> __events;
        private Dictionary<StringHash, IMeshInstanceHybridAnimatorEventHandler> __handlers;

        protected override void OnCreate()
        {
            base.OnCreate();

            __events = World.GetExistingSystemUnmanaged<MeshInstanceHybridAnimatorEventCollectSystem>().events;
            __handlers = new Dictionary<StringHash, IMeshInstanceHybridAnimatorEventHandler>();

            IMeshInstanceHybridAnimatorEventHandler handler;
            Type[] types;
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                types = assembly.GetTypes();
                foreach (var type in types)
                {
                    if (!type.IsAbstract && !type.IsGenericType && Array.IndexOf(type.GetInterfaces(), typeof(IMeshInstanceHybridAnimatorEventHandler)) != -1)
                    {
                        try
                        {
                            handler = (IMeshInstanceHybridAnimatorEventHandler)Activator.CreateInstance(type);
                            if (handler != null)
                                __handlers.Add(type.AssemblyQualifiedName, handler);
                        }
                        catch (Exception e)
                        {
                            UnityEngine.Debug.LogException(e.InnerException ?? e);
                        }
                    }
                }
            }
        }

        protected override void OnUpdate()
        {
            __events.lookupJobManager.CompleteReadOnlyDependency();

            IMeshInstanceHybridAnimatorEventHandler handler;
            Transform transform;
            AnimatorControllerEvent result;
            Unity.Collections.LowLevel.Unsafe.KeyValue<EntityObject<Transform>, AnimatorControllerEvent> keyValue;
            var enumerator = __events.GetEnumerator();
            while (enumerator.MoveNext())
            {
                keyValue = enumerator.Current;

                transform = keyValue.Key.value;
                if (transform == null)
                    continue;

                result = keyValue.Value;
                if (!__handlers.TryGetValue(result.type, out handler) || handler == null)
                    continue;

                handler.Dispatch(transform, result.weight, result.state);
            }
        }
    }
}