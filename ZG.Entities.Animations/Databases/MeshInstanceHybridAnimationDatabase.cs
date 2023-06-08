using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Animation.Hybrid;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ZG
{
    [EntityComponent(typeof(MeshInstanceHybridAnimationObjectData))]
    public abstract class MeshInstanceHybridAnimationObjectComponent : EntityProxyComponent, IEntityComponent
    {
        [SerializeField, HideInInspector]
        internal int _objectIndex;

        void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
        {
            MeshInstanceHybridAnimationObjectData instance;
            instance.index = _objectIndex;
            assigner.SetComponentData(entity, instance);
        }
    }

    [CreateAssetMenu(fileName = "Mesh Instance Hybrid Animation Database", menuName = "ZG/Mesh Instance/Hybrid Animation Database")]
    public class MeshInstanceHybridAnimationDatabase : MeshInstanceDatabase<MeshInstanceHybridAnimationDatabase>, ISerializationCallbackReceiver
    {
        private enum InitType
        {
            Type = 0x01, 
            Animation = 0x02
        }

        [Serializable]
        public struct Field
        {
            public HybridAnimationFieldType type;

            public int streamIndex;

            public int objectIndex;

            public int typeIndex;

            public string path;

            public void ToAsset(ref HybridAnimationDefinition.Field definition)
            {
                definition.type = type;
                definition.streamIndex = streamIndex;
                definition.objectIndex = objectIndex;
                definition.typeIndex = typeIndex;
                definition.path = path;
            }
        }

        [Serializable]
        public struct Rig
        {
            public int index;

            public Field[] fields;

            public BlobAssetReference<HybridAnimationDefinition> ToAsset(int instanceID)
            {
                using (var blobBuilder = new BlobBuilder(Allocator.Temp))
                {
                    ref var root = ref blobBuilder.ConstructRoot<HybridAnimationDefinition>();
                    root.instanceID = instanceID;

                    int numFields = this.fields == null ? 0 : this.fields.Length;
                    var fields = blobBuilder.Allocate(ref root.fields, numFields);
                    for(int i = 0; i < numFields; ++i)
                    {
                        ref readonly var sourceField = ref this.fields[i];
                        ref var destinationField = ref fields[i];

                        sourceField.ToAsset(ref destinationField);
                    }

                    return blobBuilder.CreateBlobAssetReference<HybridAnimationDefinition>(Allocator.Persistent);
                }
            }
        }

        [Serializable]
        public struct Data
        {
            public static readonly string[] StreamNames = new string[] { ".x", ".y", ".z", ".w" };

            public int objectCount;

            public Rig[] rigs;

            public BlobAssetReference<MeshInstaceHybridAnimationDefinition> ToAsset(int instanceID)
            {
                using (var blobBuilder = new BlobBuilder(Allocator.Temp))
                {
                    ref var root = ref blobBuilder.ConstructRoot<MeshInstaceHybridAnimationDefinition>();
                    root.instanceID = instanceID;
                    root.objectCount = objectCount;

                    int numRigs = this.rigs == null ? 0 : this.rigs.Length;
                    var rigs = blobBuilder.Allocate(ref root.rigs, numRigs);
                    for(int i = 0; i < numRigs; ++i)
                    {
                        ref readonly var sourceRig = ref this.rigs[i];
                        ref var destinationRig = ref rigs[i];

                        destinationRig.index = sourceRig.index;
                        //destinationRig.fieldCount = sourceRig.fields == null ? 0 : sourceRig.fields.Length;
                    }

                    return blobBuilder.CreateBlobAssetReference<MeshInstaceHybridAnimationDefinition>(Allocator.Persistent);
                }
            }

            public void Create(
                GameObject root, 
                IDictionary<Component, int> rigIndices, 
                MeshInstanceRigDatabase.Rig[] rigs, 
                IList<Type> types, 
                IDictionary<Type, int> typeIndices)
            {
                objectCount = 0;

                int typeIndex;
                Type type;
                HybridAnimationUtility.Field[] fields;
                List<Component> components = new List<Component>();
                var callbackFields = HybridAnimationUtility.callbackFields;
                var rigFields = new Dictionary<int, List<Field>>();
                foreach(var target in root.GetComponentsInChildren<MeshInstanceHybridAnimationObjectComponent>())
                {
                    target._objectIndex = objectCount++;

                    target.GetComponents(components);
                    foreach (var component in components)
                    {
                        type = component.GetType();

                        if (!callbackFields.TryGetValue(type, out fields))
                            continue;

                        if (!typeIndices.TryGetValue(type, out typeIndex))
                            typeIndex = types.Count;

                        if (!__Collect(
                            typeIndex,
                            target._objectIndex,
                            fields,
                            component.transform,
                            component,
                            rigIndices,
                            rigs,
                            rigFields))
                            continue;

                        if (typeIndex == types.Count)
                        {
                            typeIndices[type] = typeIndex;

                            types.Add(type);
                        }
                    }
                }

                int rigIndex = 0;
                Rig rig;
                this.rigs = new Rig[rigs.Length];
                foreach(var pair in rigFields)
                {
                    rig.index = pair.Key;
                    rig.fields = pair.Value.ToArray();

                    this.rigs[rigIndex++] = rig;
                }
            }

            private static bool __Collect(
                int typeIndex,
                int objectIndex,
                HybridAnimationUtility.Field[] fields,
                Transform transform,
                Component component,
                IDictionary<Component, int> rigIndices,
                MeshInstanceRigDatabase.Rig[] rigs,
                Dictionary<int, List<Field>> results)
            {
                bool result = false, rigResult;
                int numChannels, streamCount, rigIndex, i, j;
                string rigID, fieldID;
                GenericBindingID rigBindingID = default, fieldBindingID = default;
                Field fieldResult;
                List<Field> fieldResults;
                var rigComponents = transform.GetComponents<Component>();
                foreach (var rigComponent in rigComponents)
                {
                    if (!rigIndices.TryGetValue(rigComponent, out rigIndex))
                        continue;

                    rigResult = false;

                    if (!results.TryGetValue(rigIndex, out fieldResults))
                        fieldResults = null;

                    ref var rig = ref rigs[rigIndex];

                    if (rigBindingID.Path == null)
                        rigBindingID.Path = RigGenerator.ComputeRelativePath(component.transform, transform);

                    foreach (var field in fields)
                    {
                        streamCount = (int)field.fieldType >> (int)HybridAnimationFieldType.Shift;

                        rigBindingID.AttributeName = streamCount > 1 ? field.path + StreamNames[0] : field.path;
                        rigBindingID.ComponentType = field.componentType;

                        fieldBindingID.AttributeName = field.path;
                        fieldBindingID.ComponentType = field.componentType;

                        rigID = rigBindingID.ID;
                        fieldID = fieldBindingID.ID;

                        switch (field.fieldType & HybridAnimationFieldType.Mask)
                        {
                            case HybridAnimationFieldType.Float:
                                numChannels = rig.floatChannels == null ? 0 : rig.floatChannels.Length;
                                for (i = 0; i < numChannels; ++i)
                                {
                                    if (rig.floatChannels[i].Id == rigID)
                                        break;
                                }

                                if (i == numChannels)
                                {
                                    Array.Resize(ref rig.floatChannels, numChannels + streamCount);

                                    if (streamCount > 1)
                                    {
                                        for (j = 0; j < streamCount; ++j)
                                        {
                                            rigBindingID.AttributeName = field.path + StreamNames[j];

                                            rig.floatChannels[numChannels + j] = new FloatChannel { Id = rigBindingID.ID };
                                        }
                                    }
                                    else
                                        rig.floatChannels[numChannels] = new FloatChannel { Id = rigID };
                                }

                                fieldResult.type = field.fieldType;
                                fieldResult.objectIndex = objectIndex;
                                fieldResult.typeIndex = typeIndex;
                                fieldResult.streamIndex = i;
                                fieldResult.path = fieldID;

                                if (fieldResults == null)
                                    fieldResults = new List<Field>();

                                fieldResults.Add(fieldResult);

                                rigResult = true;

                                break;
                            case HybridAnimationFieldType.Int:
                                numChannels = rig.intChannels == null ? 0 : rig.intChannels.Length;
                                for (i = 0; i < numChannels; ++i)
                                {
                                    if (rig.intChannels[i].Id == rigID)
                                        break;
                                }

                                if (i == numChannels)
                                {
                                    Array.Resize(ref rig.intChannels, numChannels + streamCount);

                                    if (streamCount > 1)
                                    {
                                        for (j = 0; j < streamCount; ++j)
                                        {
                                            rigBindingID.AttributeName = field.path + StreamNames[j];

                                            rig.intChannels[numChannels + j] = new IntChannel { Id = rigBindingID.ID };
                                        }
                                    }
                                    else
                                        rig.intChannels[numChannels] = new IntChannel { Id = rigID };
                                }

                                fieldResult.type = field.fieldType;
                                fieldResult.objectIndex = objectIndex;
                                fieldResult.typeIndex = typeIndex;
                                fieldResult.streamIndex = i;
                                fieldResult.path = fieldID;

                                if (fieldResults == null)
                                    fieldResults = new List<Field>();

                                fieldResults.Add(fieldResult);

                                rigResult = true;

                                break;
                        }
                    }

                    if (rigResult)
                    {
                        result = true;

                        results[rigIndex] = fieldResults;
                    }
                }

                if (result)
                    return true;

                transform = transform.parent;
                if (transform == null)
                    return false;

                return __Collect(
                            typeIndex,
                            objectIndex,
                            fields,
                            transform,
                            component,
                            rigIndices,
                            rigs,
                            results);
            }
        }

#if UNITY_EDITOR
        public MeshInstanceRigDatabase rigDatabase;
        public Data data;
#endif

        [SerializeField]
        internal string[] _types;

        [SerializeField, HideInInspector]
        private byte[] __bytes;

        [SerializeField, HideInInspector]
        private int __animationCount;

        private InitType __initType;

        private BlobAssetReference<MeshInstaceHybridAnimationDefinition> __definition;

        private BlobAssetReference<HybridAnimationDefinition>[] __animations;

        public override int instanceID => __definition.IsCreated ? __definition.Value.instanceID : 0;

        public BlobAssetReference<MeshInstaceHybridAnimationDefinition> definition
        {
            get
            {
                Init();

                return __definition;
            }
        }

#if UNITY_EDITOR
        public void Create(GameObject root)
        {
            var rigIndices = MeshInstanceRigDatabase.CreateComponentRigIndices(root);

            var typeIndices = new Dictionary<Type, int>();

            var types = new List<Type>();

            data.Create(
                root,
                rigIndices,
                rigDatabase.data.rigs,
                types,
                typeIndices);

            int numTypes = types.Count;
            _types = new string[numTypes];
            for (int i = 0; i < numTypes; ++i)
                _types[i] = types[i].AssemblyQualifiedName;
        }

        public void Rebuild()
        {
            Dispose();

            int instanceID = GetInstanceID();

            __definition = data.ToAsset(instanceID);

            __animationCount = data.rigs.Length;
            __animations = new BlobAssetReference<HybridAnimationDefinition>[__animationCount];
            for(int i = 0; i < __animationCount; ++i)
                __animations[i] = data.rigs[i].ToAsset(instanceID);

            ((ISerializationCallbackReceiver)this).OnBeforeSerialize();
        }

        public void EditorMaskDirty()
        {
            Rebuild();

            rigDatabase.EditorMaskDirty();

            EditorUtility.SetDirty(this);
        }
#endif

        protected override void _Dispose()
        {
            if (__definition.IsCreated)
            {
                __definition.Dispose();

                __definition = BlobAssetReference<MeshInstaceHybridAnimationDefinition>.Null;
            }
        }

        protected override void _Destroy()
        {
            int instanceID = __definition.Value.instanceID;

            if ((__initType & InitType.Type) == InitType.Type)
            {
                int length = _types == null ? 0 : _types.Length;
                if (length > 0)
                {
                    var container = SingletonAssetContainer<int>.instance;

                    SingletonAssetContainerHandle handle;
                    handle.instanceID = instanceID;

                    for (int i = 0; i < length; ++i)
                    {
                        handle.index = i;

                        container.Delete(handle);
                    }
                }
            }

            if ((__initType & InitType.Animation) == InitType.Animation)
            {
                var container = SingletonAssetContainer<BlobAssetReference<HybridAnimationDefinition>>.instance;

                SingletonAssetContainerHandle handle;
                handle.instanceID = instanceID;

                for (int i = 0; i < __animationCount; ++i)
                {
                    handle.index = i;

                    container.Delete(handle);
                }
            }

            __initType = 0;
        }

        protected override void _Init()
        {
            __InitTypes();
            __InitAnimations();
        }

        private void __InitTypes()
        {
            if ((__initType & InitType.Type) == InitType.Type)
                return;

            __initType |= InitType.Type;

            int length = _types == null ? 0 : _types.Length;
            if (length > 0)
            {
                var container = SingletonAssetContainer<int>.instance;

                SingletonAssetContainerHandle handle;
                handle.instanceID = GetInstanceID();

                for(int i = 0; i < length; ++i)
                {
                    handle.index = i;

                    container[handle] = TypeManager.GetTypeIndex(Type.GetType(_types[i]));
                }
            }
        }

        private void __InitAnimations()
        {
            if ((__initType & InitType.Animation) == InitType.Animation)
                return;

            __initType |= InitType.Animation;

            if (__animationCount > 0)
            {
                var container = SingletonAssetContainer<BlobAssetReference<HybridAnimationDefinition>>.instance;

                SingletonAssetContainerHandle handle;
                handle.instanceID = GetInstanceID();

                for (int i = 0; i < __animationCount; ++i)
                {
                    handle.index = i;

                    container[handle] = __animations[i];
                }
            }
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (__bytes != null && __bytes.Length > 0)
            {
                if (__definition.IsCreated)
                    __definition.Dispose();

                if (__animations != null)
                {
                    foreach (var animation in __animations)
                    {
                        if (animation.IsCreated)
                            animation.Dispose();
                    }
                }

                unsafe
                {
                    fixed (byte* ptr = __bytes)
                    {
                        using (var reader = new MemoryBinaryReader(ptr, __bytes.LongLength))
                        {
                            __definition = reader.Read<MeshInstaceHybridAnimationDefinition>();

                            __animations = new BlobAssetReference<HybridAnimationDefinition>[__animationCount];
                            for (int i = 0; i < __animationCount; ++i)
                                __animations[i] = reader.Read<HybridAnimationDefinition>();
                        }
                    }
                }

                __bytes = null;
            }

            __initType = 0;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            if (__definition.IsCreated)
            {
                using (var writer = new MemoryBinaryWriter())
                {
                    writer.Write(__definition);

                    __animationCount = __animations == null ? 0 : __animations.Length;
                    for (int i = 0; i < __animationCount; ++i)
                        writer.Write(__animations[i]);

                    __bytes = writer.GetContentAsNativeArray().ToArray();
                }
            }
        }

    }
}