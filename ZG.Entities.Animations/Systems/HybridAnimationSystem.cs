using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using Unity.Animation;
using Unity.Animation.Hybrid;

namespace ZG
{
    public enum HybridAnimationFieldType : byte
    {
        Float = 0, 
        Int = 1, 
        Shift = 1, 
        Mask = (1 << Shift) - 1, 

        Float1 = (1 << Shift) | Float,
        Float2 = (2 << Shift) | Float,
        Float3 = (3 << Shift) | Float,
        Float4 = (4 << Shift) | Float,

        Int1 = (1 << Shift) | Int,
        Int2 = (2 << Shift) | Int,
        Int3 = (3 << Shift) | Int,
        Int4 = (4 << Shift) | Int
    }

    public interface IHybridAnimationCallback
    {
        Type componentType { get; }

        void Execute(
            in Entity entity, 
            ref EntityManager entityManager,
            ref SharedMultiHashMap<HybridAnimationComponent, HybridAnimationField>.Enumerator enumerator);
    }

    public class HybridAnimationCallback<T> : IHybridAnimationCallback
    {
        private static object[] __parameters = new object[1];

        private Dictionary<StringHash, FieldInfo> __fields;
        private Dictionary<StringHash, MethodInfo> __methods;

        public Type componentType => typeof(T);

        public HybridAnimationCallback(
            IDictionary<StringHash, FieldInfo> fields,
            IDictionary<StringHash, MethodInfo> methods)
        {
            __fields = new Dictionary<StringHash, FieldInfo>(fields);
            __methods = new Dictionary<StringHash, MethodInfo>(methods);
        }

        public HybridAnimationCallback()
        {
            Type type = typeof(T);

            __fields = new Dictionary<StringHash, FieldInfo>();
            type.CollectCallbackFields(__fields);

            __methods = new Dictionary<StringHash, MethodInfo>();
            type.CollectCallbackProperties(__methods);
            type.CollectCallbackMethods(__methods);
        }

        public void Execute(
            in Entity entity,
            ref EntityManager entityManager,
            ref SharedMultiHashMap<HybridAnimationComponent, HybridAnimationField>.Enumerator enumerator)
        {
            object obj = entityManager.GetComponentData<EntityObject<T>>(entity).value;
            FieldInfo fieldInfo;
            MethodInfo methodInfo;
            HybridAnimationField field;
            while (enumerator.MoveNext())
            {
                field = enumerator.Current;

                if (__fields.TryGetValue(field.path, out fieldInfo))
                    fieldInfo.SetValue(obj, field.Pack());
                else if (__methods.TryGetValue(field.path, out methodInfo))
                {
                    __parameters[0] = field.Pack();
                    methodInfo.Invoke(obj, __parameters);
                }
            }
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class HybridAnimationCallbackAttribute : Attribute
    {
        public string path;
        public Type type;

        public HybridAnimationCallbackAttribute(string path)
        {
            this.path = path;
        }
    }

    public struct HybridAnimationComponent : IEquatable<HybridAnimationComponent>
    {
        public int typeIndex;
        public Entity entity;

        public bool Equals(HybridAnimationComponent other)
        {
            return typeIndex == other.typeIndex && entity == other.entity;
        }

        public override int GetHashCode()
        {
            return typeIndex ^ entity.GetHashCode();
        }
    }

    public struct HybridAnimationField
    {
        public HybridAnimationFieldType type;
        public float4 value;
        public StringHash path;

        public object Pack()
        {
            switch(type)
            {
                case HybridAnimationFieldType.Float1:
                    return value.x;
                case HybridAnimationFieldType.Float2:
                    return value.xy;
                case HybridAnimationFieldType.Float3:
                    return value.xyz;
                case HybridAnimationFieldType.Float4:
                    return value;
                case HybridAnimationFieldType.Int1:
                    return math.asint(value.x);
                case HybridAnimationFieldType.Int2:
                    return math.asint(value.xy);
                case HybridAnimationFieldType.Int3:
                    return math.asint(value.xyz);
                case HybridAnimationFieldType.Int4:
                    return math.asint(value);
            }

            throw new InvalidCastException();
        }
    }

    public struct HybridAnimationDefinition
    {
        public struct Field
        {
            public HybridAnimationFieldType type;

            public int streamIndex;

            public int objectIndex;

            public int typeIndex;

            public StringHash path;
        }

        public int instanceID;
        public BlobArray<Field> fields;
    }

    public struct HybridAnimationData : IComponentData
    {
        public BlobAssetReference<HybridAnimationDefinition> definition;
    }

    public struct HybridAnimationRoot : IComponentData
    {
        public Entity entity;
    }

    [Serializable]
    public struct HybridAnimationObject : IBufferElementData
    {
        public Entity entity;
    }

    public struct OldAnimatedData : IBufferElementData
    {
        public float value;
    }

    [BurstCompile, UpdateAfter(typeof(AnimationSystemGroup))]
    public partial struct HybridAnimationSystem : ISystem
    {
        [BurstCompile]
        private struct Count : IJobChunk
        {
            public NativeCounter.Concurrent counter;

            [ReadOnly]
            public ComponentTypeHandle<HybridAnimationData> instanceType;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var instances = chunk.GetNativeArray(ref instanceType);

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    counter.Add(instances[i].definition.Value.fields.Length);
            }
        }

        [BurstCompile]
        private struct Clear : IJob
        {
            public NativeCounter counter;
            public SharedMultiHashMap<HybridAnimationComponent, HybridAnimationField>.Writer componentFields;

            public void Execute()
            {
                componentFields.Clear();
                componentFields.capacity = math.max(componentFields.capacity, counter.count);
            }
        }

        private struct Playback
        {
            [ReadOnly]
            public NativeArray<Rig> rigs;

            [ReadOnly]
            public NativeArray<HybridAnimationData> instances;

            [ReadOnly]
            public NativeArray<HybridAnimationRoot> roots;

            [ReadOnly]
            public BufferLookup<HybridAnimationObject> animationObjects;

            [ReadOnly]
            public BufferAccessor<AnimatedData> animatedDatas;

            public BufferAccessor<OldAnimatedData> oldAnimatedDatas;

            public SharedMultiHashMap<HybridAnimationComponent, HybridAnimationField>.ParallelWriter componentFields;

            public SingletonAssetContainer<int>.Reader typeIndices;

            public void Execute(int index)
            {
                ref var definition = ref instances[index].definition.Value;

                var rig = rigs[index];

                DynamicBuffer<AnimatedData> animatedDatas = this.animatedDatas[index], oldAnimatedDatas = this.oldAnimatedDatas[index].Reinterpret<AnimatedData>();
                oldAnimatedDatas.ResizeUninitialized(animatedDatas.Length);

                AnimationStream source = AnimationStream.CreateReadOnly(rig, animatedDatas.AsNativeArray()),
                    destination = AnimationStream.Create(rig, oldAnimatedDatas.AsNativeArray());

                var animationObjects = this.animationObjects[roots[index].entity];
                HybridAnimationComponent component;
                HybridAnimationField result;
                SingletonAssetContainerHandle handle;
                float4 sourceValue, destinationValue;
                float value;
                HybridAnimationFieldType fieldType;
                int i, j, streamCount, numFields = definition.fields.Length;
                for (i = 0; i < numFields; ++i)
                {
                    ref var field = ref definition.fields[i];

                    component.entity = animationObjects[field.objectIndex].entity;
                    if (component.entity == Entity.Null)
                        continue;

                    sourceValue = float4.zero;
                    destinationValue = float4.zero;

                    streamCount = (int)field.type >> (int)HybridAnimationFieldType.Shift;

                    fieldType = field.type & HybridAnimationFieldType.Mask;
                    switch (fieldType)
                    {
                        case HybridAnimationFieldType.Float:
                            for (j = 0; j < streamCount; ++j)
                                sourceValue[j] = source.GetFloat(j + field.streamIndex);

                            for (j = 0; j < streamCount; ++j)
                                destinationValue[j] = destination.GetFloat(j + field.streamIndex);
                            break;
                        case HybridAnimationFieldType.Int:
                            for (j = 0; j < streamCount; ++j)
                                sourceValue[j] = math.asfloat(source.GetInt(j + field.streamIndex));

                            for (j = 0; j < streamCount; ++j)
                                destinationValue[j] = math.asfloat(destination.GetInt(j + field.streamIndex));
                            break;
                    }

                    if (sourceValue.Equals(destinationValue))
                        continue;

                    switch (fieldType)
                    {
                        case HybridAnimationFieldType.Float:
                            for (j = 0; j < streamCount; ++j)
                            {
                                value = sourceValue[j];
                                if (value == destinationValue[j])
                                    continue;

                                destination.SetFloat(j + field.streamIndex, value);
                            }
                            break;
                        case HybridAnimationFieldType.Int:
                            for (j = 0; j < streamCount; ++j)
                            {
                                value = sourceValue[j];
                                if (value == destinationValue[j])
                                    continue;

                                destination.SetInt(j + field.streamIndex, math.asint(value));
                            }
                            break;
                    }

                    handle.instanceID = definition.instanceID;
                    handle.index = field.typeIndex;

                    component.typeIndex = typeIndices[handle];

                    result.type = field.type;
                    result.path = field.path;
                    result.value = sourceValue;

                    componentFields.Add(component, result);
                }
            }
        }

        [BurstCompile]
        private struct PlaybackEx : IJobChunk
        {
            [ReadOnly]
            public ComponentTypeHandle<Rig> rigType;

            [ReadOnly]
            public ComponentTypeHandle<HybridAnimationData> instanceType;

            [ReadOnly]
            public ComponentTypeHandle<HybridAnimationRoot> rootType;

            [ReadOnly]
            public BufferLookup<HybridAnimationObject> animationObjects;

            [ReadOnly]
            public BufferTypeHandle<AnimatedData> animatedDataType;

            public BufferTypeHandle<OldAnimatedData> oldAnimatedDataType;

            public SharedMultiHashMap<HybridAnimationComponent, HybridAnimationField>.ParallelWriter componentFields;

            public SingletonAssetContainer<int>.Reader typeIndices;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Playback playback;
                playback.rigs = chunk.GetNativeArray(ref rigType);
                playback.instances = chunk.GetNativeArray(ref instanceType);
                playback.roots = chunk.GetNativeArray(ref rootType);
                playback.animationObjects = animationObjects;
                playback.animatedDatas = chunk.GetBufferAccessor(ref animatedDataType);
                playback.oldAnimatedDatas = chunk.GetBufferAccessor(ref oldAnimatedDataType);
                playback.componentFields = componentFields;
                playback.typeIndices = typeIndices;

                var iterator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
                while (iterator.NextEntityIndex(out int i))
                    playback.Execute(i);
            }
        }

        private EntityQuery __group;

        private ComponentTypeHandle<Rig> __rigType;

        private ComponentTypeHandle<HybridAnimationData> __instanceType;

        private ComponentTypeHandle<HybridAnimationRoot> __rootType;

        private BufferLookup<HybridAnimationObject> __animationObjects;

        private BufferTypeHandle<AnimatedData> __animatedDataType;

        private BufferTypeHandle<OldAnimatedData> __oldAnimatedDataType;

        private SingletonAssetContainer<int> __typeIndices;

        public SharedMultiHashMap<HybridAnimationComponent, HybridAnimationField> componentFields
        {
            get;

            private set;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                        .WithAll<Rig, HybridAnimationData, HybridAnimationRoot, AnimatedData>()
                        .WithAllRW<OldAnimatedData>()
                        .Build(ref state);

            __rigType = state.GetComponentTypeHandle<Rig>(true);
            __instanceType = state.GetComponentTypeHandle<HybridAnimationData>(true);
            __rootType = state.GetComponentTypeHandle<HybridAnimationRoot>(true);
            __animationObjects = state.GetBufferLookup<HybridAnimationObject>(true);
            __animatedDataType = state.GetBufferTypeHandle<AnimatedData>(true);
            __oldAnimatedDataType = state.GetBufferTypeHandle<OldAnimatedData>();

            __typeIndices = SingletonAssetContainer<int>.Retain();

            componentFields = new SharedMultiHashMap<HybridAnimationComponent, HybridAnimationField>(Allocator.Persistent);
        }

        //[BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __typeIndices.Release();

            componentFields.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //lookupJobManager.CompleteReadWriteDependency();

            var counter = new NativeCounter(Allocator.TempJob);

            var instanceType = __instanceType.UpdateAsRef(ref state);

            Count count;
            count.instanceType = instanceType;
            count.counter = counter;
            var jobHandle = count.ScheduleParallelByRef(__group, state.Dependency);

            var componentFields = this.componentFields;
            ref var componentFieldsJobManager = ref componentFields.lookupJobManager;

            Clear clear;
            clear.counter = counter;
            clear.componentFields = componentFields.writer;
            jobHandle = clear.ScheduleByRef(JobHandle.CombineDependencies(componentFieldsJobManager.readWriteJobHandle, jobHandle));

            var disposeJobHandle = counter.Dispose(jobHandle);

            PlaybackEx playback;
            playback.typeIndices = __typeIndices.reader;
            playback.rigType = __rigType.UpdateAsRef(ref state);
            playback.instanceType = instanceType;
            playback.rootType = __rootType.UpdateAsRef(ref state);
            playback.animationObjects = __animationObjects.UpdateAsRef(ref state);
            playback.animatedDataType = __animatedDataType.UpdateAsRef(ref state);
            playback.oldAnimatedDataType = __oldAnimatedDataType.UpdateAsRef(ref state);
            playback.componentFields = componentFields.parallelWriter;
            jobHandle = playback.ScheduleParallelByRef(__group, jobHandle);

            __typeIndices.AddDependency(state.GetSystemID(), jobHandle);

            componentFieldsJobManager.readWriteJobHandle = jobHandle;

            state.Dependency = JobHandle.CombineDependencies(jobHandle, disposeJobHandle);
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class HybridAnimationCallbackSystem : SystemBase
    {
        private IReadOnlyDictionary<int, IHybridAnimationCallback> __callbacks;
        private SharedMultiHashMap<HybridAnimationComponent, HybridAnimationField> __componentFields;

        protected override void OnCreate()
        {
            base.OnCreate();

            __callbacks = HybridAnimationUtility.callbacks;

            __componentFields = World.GetOrCreateSystemUnmanaged<HybridAnimationSystem>().componentFields;
        }

        protected override void OnUpdate()
        {
            var entityManager = EntityManager;

            __componentFields.lookupJobManager.CompleteReadOnlyDependency();
            var reader = __componentFields.reader;

            IHybridAnimationCallback callback;
            SharedMultiHashMap<HybridAnimationComponent, HybridAnimationField>.Enumerator enumerator;
            using (var keys = reader.GetKeys(Allocator.Temp))
            {
                foreach(var key in keys)
                {
                    if (!__callbacks.TryGetValue(key.typeIndex, out callback))
                        continue;

                    enumerator = reader.GetValuesForKey(key);

                    callback.Execute(key.entity, ref entityManager, ref enumerator);
                }
            }
        }
    }

    public static class HybridAnimationUtility
    {
        public struct Field
        {
            public HybridAnimationFieldType fieldType;

            public Type componentType;

            public string path;
        }

        private static Dictionary<Type, Field[]> __callbackFields;
        private static Dictionary<int, IHybridAnimationCallback> __callbacks;

        public static IReadOnlyDictionary<Type, Field[]> callbackFields
        {
            get
            {
                if (__callbackFields == null)
                {
                    __callbackFields = new Dictionary<Type, Field[]>();

                    ParameterInfo parameterInfo;
                    ParameterInfo[] parameterInfos;
                    List<Field> fields = new List<Field>();
                    Field field;
                    const BindingFlags BINDING_FLAGS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            fields.Clear();

                            foreach (var fieldInfo in type.GetFields(BINDING_FLAGS))
                            {
                                if (__TryParse(
                                    fieldInfo.GetCustomAttribute<HybridAnimationCallbackAttribute>(),
                                    type,
                                    fieldInfo.FieldType,
                                    out field))
                                    fields.Add(field);
                            }

                            foreach (var property in type.GetProperties(BINDING_FLAGS))
                            {
                                if (!property.CanWrite)
                                    continue;

                                if (__TryParse(
                                    property.GetCustomAttribute<HybridAnimationCallbackAttribute>(),
                                    type,
                                    property.PropertyType,
                                    out field))
                                {
                                    fields.Add(field);

                                    continue;
                                }

                                if (__TryParse(
                                    property.SetMethod.GetCustomAttribute<HybridAnimationCallbackAttribute>(),
                                    type,
                                    property.PropertyType,
                                    out field))
                                    fields.Add(field);
                            }

                            foreach (var methodInfo in type.GetMethods(BINDING_FLAGS))
                            {
                                parameterInfos = methodInfo.GetParameters();
                                if (parameterInfos == null || parameterInfos.Length != 1)
                                    continue;

                                parameterInfo = parameterInfos[0];
                                if (parameterInfo.IsIn && 
                                    __TryParse(
                                    methodInfo.GetCustomAttribute<HybridAnimationCallbackAttribute>(),
                                    type, 
                                    parameterInfo.ParameterType,
                                    out field))
                                    fields.Add(field);
                            }

                            if (fields.Count < 1)
                                continue;

                            __callbackFields[type] = fields.ToArray();
                        }
                    }
                }

                return __callbackFields;
            }
        }

        public static IReadOnlyDictionary<int, IHybridAnimationCallback> callbacks
        {
            get
            {
                if (__callbacks == null)
                {
                    IHybridAnimationCallback callback;
                    __callbacks = new Dictionary<int, IHybridAnimationCallback>();

                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            if (type.IsAbstract || 
                                type.IsGenericTypeDefinition || 
                                type.GetConstructor(Type.EmptyTypes) == null ||
                                Array.IndexOf(type.GetInterfaces(), typeof(IHybridAnimationCallback)) == -1)
                                continue;

                            callback = (IHybridAnimationCallback)Activator.CreateInstance(type);
                            __callbacks[TypeManager.GetTypeIndex(callback.componentType)] = callback;
                        }
                    }
                }

                return __callbacks;
            }
        }

        public static void CollectCallbackFields(this Type type, IDictionary<StringHash, FieldInfo> fields)
        {
            HybridAnimationCallbackAttribute attribute;
            var fieldInfos = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var fieldInfo in fieldInfos)
            {
                attribute = fieldInfo.GetCustomAttribute<HybridAnimationCallbackAttribute>();
                if (attribute == null)
                    continue;

                fields[new GenericBindingID() 
                { 
                    AttributeName = attribute.path, 
                    ComponentType = attribute.type ?? type
                }.ID] = fieldInfo;
            }
        }

        public static void CollectCallbackProperties(this Type type, IDictionary<StringHash, MethodInfo> methods)
        {
            HybridAnimationCallbackAttribute attribute;
            var propertyInfos = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var propertyInfo in propertyInfos)
            {
                if (!propertyInfo.CanWrite)
                    continue;

                attribute = propertyInfo.GetCustomAttribute<HybridAnimationCallbackAttribute>() ?? propertyInfo.SetMethod.GetCustomAttribute<HybridAnimationCallbackAttribute>();
                if (attribute == null)
                    continue;

                methods[new GenericBindingID()
                {
                    AttributeName = attribute.path,
                    ComponentType = attribute.type ?? type
                }.ID] = propertyInfo.SetMethod;
            }
        }

        public static void CollectCallbackMethods(this Type type, IDictionary<StringHash, MethodInfo> methods)
        {
            HybridAnimationCallbackAttribute attribute;
            var methodInfos = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            foreach (var methodInfo in methodInfos)
            {
                attribute = methodInfo.GetCustomAttribute<HybridAnimationCallbackAttribute>();
                if (attribute == null)
                    continue;

                methods[new GenericBindingID()
                {
                    AttributeName = attribute.path,
                    ComponentType = attribute.type ?? type
                }.ID] = methodInfo;
            }
        }

        private static bool __TryParse(
            HybridAnimationCallbackAttribute attribute, 
            Type componentType, 
            Type fieldType, 
            out Field field)
        {
            if (attribute == null)
            {
                field = default;

                return false;
            }

            while (true)
            {
                if (typeof(int) == fieldType)
                {
                    field.fieldType = HybridAnimationFieldType.Int1;

                    break;
                }

                if (typeof(int2) == fieldType)
                {
                    field.fieldType = HybridAnimationFieldType.Int2;

                    break;
                }

                if (typeof(int3) == fieldType)
                {
                    field.fieldType = HybridAnimationFieldType.Int3;

                    break;
                }

                if (typeof(int4) == fieldType)
                {
                    field.fieldType = HybridAnimationFieldType.Int4;

                    break;
                }

                if (typeof(float) == fieldType)
                {
                    field.fieldType = HybridAnimationFieldType.Float1;

                    break;
                }

                if (typeof(float2) == fieldType)
                {
                    field.fieldType = HybridAnimationFieldType.Float2;

                    break;
                }

                if (typeof(float3) == fieldType)
                {
                    field.fieldType = HybridAnimationFieldType.Float3;

                    break;
                }

                if (typeof(float4) == fieldType)
                {
                    field.fieldType = HybridAnimationFieldType.Float4;

                    break;
                }

                field = default;

                return false;
            }

            field.componentType = attribute.type ?? componentType;
            field.path = attribute.path;

            return true;
        }
    }
}