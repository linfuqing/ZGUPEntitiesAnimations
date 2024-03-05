using System;
using System.Collections.Generic;
using Unity.Entities.Serialization;
using Unity.Entities;
using UnityEngine;
using HybridRenderer = UnityEngine.Renderer;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ZG
{
    [CreateAssetMenu(fileName = "Mesh Instance Animation Material Database", menuName = "ZG/Mesh Instance/Animation Material Database")]
    public class MeshInstanceAnimationMaterialDatabase : MeshInstanceDatabase<MeshInstanceAnimationMaterialDatabase>, ISerializationCallbackReceiver
    {
        [Flags]
        private enum InitType
        {
            TypeIndex = 0x01
        }

        [Serializable]
        public struct MaterialProperty
        {
            public string id;

            public int typeIndex;
            public int[] channelIndices;
        }

        [Serializable]
        public struct Renderer
        {
            public string name;

            public int rigIndex;

            public int startIndex;
            public int count;

            public MaterialProperty[] materialProperties;
        }

        [Serializable]
        public struct Data
        {
            public Renderer[] renderers;

            public void Create(
                Transform root,
                MeshInstanceRendererDatabase.MaterialPropertyOverride materialPropertyOverride,
                IList<Type> types = null,
                IDictionary<Type, int> typeIndices = null)
            {
                var rendererLODGroups = new Dictionary<HybridRenderer, LODGroup[]>();
                MeshInstanceRendererDatabase.Build(root.GetComponentsInChildren<LODGroup>(), rendererLODGroups);

                var hybridRenderers = root.GetComponentsInChildren<HybridRenderer>();
                var rendererStartIndices = new Dictionary<HybridRenderer, int>();
                MeshInstanceRendererDatabase.Build(hybridRenderers, rendererLODGroups, rendererStartIndices);

                var rigMaterialProperties = new List<MeshInstanceRigDatabase.MaterialProperty>();

                MeshInstanceRigDatabase.BuildRendererMaterialProperties(hybridRenderers, materialPropertyOverride, rigMaterialProperties);
                int numRigMaterialProperties = rigMaterialProperties.Count;
                if (numRigMaterialProperties < 1)
                    return;

                var rigMaterailPropertyIndices = new Dictionary<Component, List<int>>();
                MeshInstanceRigDatabase.BuildRigMaterailPropertyIndices(rigMaterialProperties, rigMaterailPropertyIndices);

                var componentRigIndices = MeshInstanceRigDatabase.CreateComponentRigIndices(root.root.gameObject);

                var materialPropertyIndices = new Dictionary<(HybridRenderer, Type), int>();
                var renderers = new List<Renderer>();
                var rendererIndices = new Dictionary<HybridRenderer, int>();
                LODGroup[] lodGroups;
                Renderer renderer;
                (HybridRenderer, Type) materialPropertyKey;
                MaterialProperty materialProperty;
                MeshInstanceRigDatabase.MaterialProperty rigMaterialProperty;
                int rendererIndex, materialPropertyIndex;
                for(int i = 0; i < numRigMaterialProperties; ++i)
                {
                    rigMaterialProperty = rigMaterialProperties[i];
                    if (rendererIndices.TryGetValue(rigMaterialProperty.renderer, out rendererIndex))
                        renderer = renderers[rendererIndex];
                    else
                    {
                        if (!rendererStartIndices.TryGetValue(rigMaterialProperty.renderer, out renderer.startIndex))
                            continue;

                        if (rendererLODGroups.TryGetValue(rigMaterialProperty.renderer, out lodGroups))
                            renderer.count = Mathf.Min(lodGroups == null ? 0 : lodGroups.Length, 1);
                        else
                            renderer.count = 1;

                        renderer.rigIndex = componentRigIndices[rigMaterialProperty.rigRoot];
                        renderer.name = rigMaterialProperty.renderer.name;
                        renderer.materialProperties = null;

                        rendererIndex = renderers.Count;

                        renderers.Add(renderer);

                        rendererIndices[rigMaterialProperty.renderer] = rendererIndex;
                    }

                    materialPropertyKey = (rigMaterialProperty.renderer, rigMaterialProperty.componentType);
                    if (materialPropertyIndices.TryGetValue(materialPropertyKey, out materialPropertyIndex))
                        materialProperty = renderer.materialProperties[materialPropertyIndex];
                    else
                    {
                        materialPropertyIndex = renderer.materialProperties == null ? 0 : renderer.materialProperties.Length;

                        materialPropertyIndices[materialPropertyKey] = materialPropertyIndex;

                        Array.Resize(ref renderer.materialProperties, materialPropertyIndex + 1);

                        if (typeIndices == null)
                            typeIndices = new Dictionary<Type, int>();

                        if (!typeIndices.TryGetValue(rigMaterialProperty.componentType, out materialProperty.typeIndex))
                        {
                            if (types == null)
                                types = new List<Type>();

                            materialProperty.typeIndex = types.Count;

                            typeIndices[rigMaterialProperty.componentType] = materialProperty.typeIndex;

                            types.Add(rigMaterialProperty.componentType);
                        }

                        materialProperty.id = rigMaterialProperty.id;
                        materialProperty.channelIndices = null;
                    }

                    if (rigMaterialProperty.index >= (materialProperty.channelIndices == null ? 0 : materialProperty.channelIndices.Length))
                        Array.Resize(ref materialProperty.channelIndices, rigMaterialProperty.index + 1);

                    materialProperty.channelIndices[rigMaterialProperty.index] = rigMaterailPropertyIndices[rigMaterialProperty.rigRoot].IndexOf(i);

                    renderer.materialProperties[materialPropertyIndex] = materialProperty;

                    renderers[rendererIndex] = renderer;
                }

                this.renderers = renderers.ToArray();
            }

            public BlobAssetReference<MeshInstanceAnimationMaterialDefinition> ToAsset(int instanceID)
            {
                using (var builder = new BlobBuilder(Unity.Collections.Allocator.Temp))
                {
                    ref var root = ref builder.ConstructRoot<MeshInstanceAnimationMaterialDefinition>();
                    root.instanceID = instanceID;
                    root.materialPropertyCount = 0;
                    root.materialPropertySize = 0;

                    int i, j, k, numChannelIndices, numMaterialProperties, numRenderers = this.renderers.Length;
                    BlobBuilderArray<int> channelIndices;
                    BlobBuilderArray<MeshInstanceAnimationMaterialDefinition.MaterialProperty> materialProperties;
                    var renderers = builder.Allocate(ref root.renderers, numRenderers);
                    for (i = 0; i < numRenderers; ++i)
                    {
                        ref var sourceRenderer = ref this.renderers[i];
                        ref var destinationRenderer = ref renderers[i];

                        destinationRenderer.rigIndex = sourceRenderer.rigIndex;
                        destinationRenderer.startIndex = sourceRenderer.startIndex;
                        destinationRenderer.count = sourceRenderer.count;

                        numMaterialProperties = sourceRenderer.materialProperties.Length;
                        materialProperties = builder.Allocate(ref destinationRenderer.materialProperties, numMaterialProperties);
                        for (j = 0; j < numMaterialProperties; ++j)
                        {
                            ref var sourceMaterialProperty = ref sourceRenderer.materialProperties[j];
                            ref var destinationMaterialProperty = ref materialProperties[j];

                            destinationMaterialProperty.typeIndex = sourceMaterialProperty.typeIndex;

                            numChannelIndices = sourceMaterialProperty.channelIndices.Length;

                            channelIndices = builder.Allocate(ref destinationMaterialProperty.channelIndices, numChannelIndices);
                            for (k = 0; k < numChannelIndices; ++k)
                                channelIndices[k] = sourceMaterialProperty.channelIndices[k];

                            root.materialPropertySize += numChannelIndices * Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<float>();
                        }

                        root.materialPropertyCount += numMaterialProperties;
                    }

                    return builder.CreateBlobAssetReference<MeshInstanceAnimationMaterialDefinition>(Unity.Collections.Allocator.Persistent);
                }
            }
        }


        [SerializeField]
        internal string[] _types;

        [SerializeField, HideInInspector]
        private byte[] __bytes;

        private InitType __initType;

        private TypeIndex[] __typeIndices;

        private BlobAssetReference<MeshInstanceAnimationMaterialDefinition> __definition;

        public override int instanceID => __definition.IsCreated ? __definition.Value.instanceID : 0;

        public BlobAssetReference<MeshInstanceAnimationMaterialDefinition> definition
        {
            get
            {
                Init();

                return __definition;
            }
        }

        protected override void _Dispose()
        {
            if (__definition.IsCreated)
            {
                __definition.Dispose();

                __definition = BlobAssetReference<MeshInstanceAnimationMaterialDefinition>.Null;
            }
        }

        protected override void _Destroy()
        {
            SingletonAssetContainerHandle handle;
            handle.instanceID = __definition.Value.instanceID;

            if ((__initType & InitType.TypeIndex) == InitType.TypeIndex)
            {
                int length = __typeIndices == null ? 0 : __typeIndices.Length;
                if (length > 0)
                {
                    var container = SingletonAssetContainer<int>.instance;

                    for (int i = 0; i < length; ++i)
                    {
                        handle.index = i;

                        container.Delete(handle);
                    }
                }

                __typeIndices = null;
            }

            __initType = 0;
        }

        protected override void _Init()
        {
            __InitTypeIndices();
        }

        private void __InitTypeIndices()
        {
            if ((__initType & InitType.TypeIndex) != InitType.TypeIndex)
            {
                __initType |= InitType.TypeIndex;

                int numTypes = _types == null ? 0 : _types.Length;
                if (numTypes > 0)
                {
                    var instance = SingletonAssetContainer<TypeIndex>.instance;

                    SingletonAssetContainerHandle handle;
                    handle.instanceID = __definition.Value.instanceID;

                    TypeIndex typeIndex;
                    __typeIndices = new TypeIndex[numTypes];
                    for (int i = 0; i < numTypes; ++i)
                    {
                        typeIndex = TypeManager.GetTypeIndex(Type.GetType(_types[i]));

                        __typeIndices[i] = typeIndex;

                        handle.index = i;

                        instance[handle] = typeIndex;
                    }
                }
            }
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (__bytes != null && __bytes.Length > 0)
            {
                if (__definition.IsCreated)
                    __definition.Dispose();

                unsafe
                {
                    fixed (byte* ptr = __bytes)
                    {
                        using (var reader = new MemoryBinaryReader(ptr, __bytes.LongLength))
                        {
                            __definition = reader.Read<MeshInstanceAnimationMaterialDefinition>();
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

                    __bytes = writer.GetContentAsNativeArray().ToArray();
                }
            }
        }

#if UNITY_EDITOR
        public MeshInstanceMaterialPropertySettings materialPropertySettings;

        [HideInInspector]
        public Transform root;

        public Data data;

        public void Create()
        {
            var types = new List<Type>();

            data.Create(root, materialPropertySettings == null ? null : materialPropertySettings.Override, types);

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

            ((ISerializationCallbackReceiver)this).OnBeforeSerialize();
        }

        public void EditorMaskDirty()
        {
            Rebuild();

            EditorUtility.SetDirty(this);
        }
#endif
    }
}