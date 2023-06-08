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
    public static class ComponentUtility
    {
        public static T GetComponentInParentEx<T>(this Component component)
        {
            if (component == null)
                return default;

            var result = component.GetComponent<T>();
            if (result != null)
                return result;

            return GetComponentInParentEx<T>(component.transform.parent);
        }
    }

    [CreateAssetMenu(fileName = "Mesh Instance Hierarchy Database", menuName = "ZG/Mesh Instance/Hierarchy Database")]
    public class MeshInstanceHierarchyDatabase : ScriptableObject, ISerializationCallbackReceiver
    {
        [Serializable]
        public struct Transform
        {
            [Serializable]
            public struct Renderer
            {
                public string name;

                public int index;

                public Matrix4x4 matrix;
            }

            [Serializable]
            public struct LOD
            {
                public string name;

                public int index;

                public Matrix4x4 matrix;
            }

            public string name;

            public int rigNodeIndex;

            public Renderer[] renderers;
            public LOD[] lods;
        }

        [Serializable]
        public struct Data
        {
            public class RenderGroup
            {
                private List<Transform.Renderer> __renderers;
                private List<Transform.LOD> __lods;

                public bool isEmpty => __renderers == null || __renderers.Count < 1;

                public void AddRenderer(in Transform.Renderer renderer)
                {
                    if (__renderers == null)
                        __renderers = new List<Transform.Renderer>();

                    __renderers.Add(renderer);
                }

                public void AddLOD(in Transform.LOD lod)
                {
                    if (__lods == null)
                        __lods = new List<Transform.LOD>();

                    __lods.Add(lod);
                }

                public Transform.Renderer[] ToRenderers()
                {
                    return __renderers.ToArray();
                }

                public Transform.LOD[] ToLODs()
                {
                    return __lods == null ? null : __lods.ToArray();
                }
            }

            public Transform[] transforms;

            public static void Build(
                UnityEngine.Transform root, 
                MeshInstanceRigDatabase.Node[] nodes, 
                IDictionary<UnityEngine.Transform, int> outRigNodeIndices)
            {
                int numNodes = nodes.Length;
                for (int i = 0; i < numNodes; ++i)
                    outRigNodeIndices[root.Find(nodes[i].path)] = i;
            }

            public static void Build(
                IDictionary<Renderer, int> rendererLODCounts, 
                IDictionary<UnityEngine.Transform, RenderGroup> outTransformRenderGroups, 
                params Renderer[] renderers)
            {
                RenderGroup renderGroup;
                UnityEngine.Transform exposeTransform;
                Component component;
                Renderer renderer;
                Transform.Renderer transformRenderer;
                int numRenderers = renderers.Length, rendererIndex = 0, rendererCount, rendererLODCount, i, j;
                for (i = 0; i < numRenderers; ++i)
                {
                    renderer = renderers[i];

                    if (!rendererLODCounts.TryGetValue(renderer, out rendererLODCount))
                        rendererLODCount = 1;

                    rendererCount = renderer.sharedMaterials.Length * rendererLODCount;

                    component = renderer.GetComponentInParentEx<IExposeTransform>() as Component;
                    exposeTransform = component == null ? null : component.transform;
                    if (exposeTransform == null)
                    {
                        rendererIndex += rendererCount;

                        continue;
                    }

                    if (!outTransformRenderGroups.TryGetValue(exposeTransform, out renderGroup))
                    {
                        renderGroup = new RenderGroup();

                        outTransformRenderGroups[exposeTransform] = renderGroup;
                    }

                    transformRenderer.name = renderer.name;
                    transformRenderer.matrix = Matrix4x4.Inverse(exposeTransform.localToWorldMatrix) * renderer.transform.localToWorldMatrix;
                    for (j = 0; j < rendererCount; ++j)
                    {
                        transformRenderer.index = rendererIndex++;
                        renderGroup.AddRenderer(transformRenderer);
                    }
                }
            }

            public static void Build(
                IDictionary<UnityEngine.Transform, RenderGroup> outTransformRenderGroups,
                params LODGroup[] lodGroups)
            {
                RenderGroup renderGroup;
                UnityEngine.Transform exposeTransform;
                Component component;
                LODGroup lodGroup;
                Transform.LOD transformLOD;
                int numLODGroups = lodGroups.Length;
                for (int i = 0; i < numLODGroups; ++i)
                {
                    lodGroup = lodGroups[i];
                    component = lodGroup.GetComponentInParent<IExposeTransform>() as Component;
                    exposeTransform = component == null ? null : component.transform;
                    if (exposeTransform == null)
                        continue;

                    if (!outTransformRenderGroups.TryGetValue(exposeTransform, out renderGroup))
                    {
                        renderGroup = new RenderGroup();

                        outTransformRenderGroups[exposeTransform] = renderGroup;
                    }

                    transformLOD.name = lodGroup.name;
                    transformLOD.matrix = Matrix4x4.Inverse(exposeTransform.localToWorldMatrix) * lodGroup.transform.localToWorldMatrix;

                    transformLOD.index = i;
                    renderGroup.AddLOD(transformLOD);
                }
            }

            public void Create(
                IEnumerable<KeyValuePair<UnityEngine.Transform, int>> rigNodeIndices,
                IDictionary<UnityEngine.Transform, RenderGroup> transformRenderGroups)
            {
                var transforms = new List<Transform>();
                RenderGroup renderGroup;
                Transform transform;
                UnityEngine.Transform key;
                foreach (var pair in rigNodeIndices)
                {
                    key = pair.Key;
                    if (!transformRenderGroups.TryGetValue(key, out renderGroup) || renderGroup == null || renderGroup.isEmpty)
                        continue;

                    transform.name = key.name;
                    transform.rigNodeIndex = pair.Value;
                    transform.renderers = renderGroup.ToRenderers();
                    transform.lods = renderGroup.ToLODs();

                    transforms.Add(transform);
                }

                this.transforms = transforms.ToArray();
            }

            public BlobAssetReference<MeshInstanceHierarchyDefinition> ToAsset(int instanceID)
            {
                using (var blobBuilder = new BlobBuilder(Allocator.Temp))
                {
                    ref var root = ref blobBuilder.ConstructRoot<MeshInstanceHierarchyDefinition>();
                    root.instanceID = instanceID;

                    int numTransformss = this.transforms.Length, numRenderers, numLODs, i, j;
                    var transforms = blobBuilder.Allocate(ref root.transforms, numTransformss);
                    BlobBuilderArray<MeshInstanceHierarchyDefinition.Renderer> renderers;
                    BlobBuilderArray<MeshInstanceHierarchyDefinition.LOD> lods;
                    for (i = 0; i < numTransformss; ++i)
                    {
                        ref readonly var sourceTransform = ref this.transforms[i];
                        ref var destinationTransform = ref transforms[i];

                        destinationTransform.rigIndex = sourceTransform.rigNodeIndex;

                        numRenderers = sourceTransform.renderers.Length;
                        renderers = blobBuilder.Allocate(ref destinationTransform.renderers, numRenderers);
                        for(j = 0; j < numRenderers; ++j)
                        {
                            ref readonly var sourceRenderer = ref sourceTransform.renderers[j];
                            ref var destinationRenderer = ref renderers[j];

                            destinationRenderer.index = sourceRenderer.index;
                            destinationRenderer.matrix = sourceRenderer.matrix;
                        }

                        numLODs = sourceTransform.lods == null ? 0 : sourceTransform.lods.Length;
                        lods = blobBuilder.Allocate(ref destinationTransform.lods, numLODs);
                        for (j = 0; j < numLODs; ++j)
                        {
                            ref readonly var sourceLOD = ref sourceTransform.lods[j];
                            ref var destinationLOD = ref lods[j];

                            destinationLOD.index = sourceLOD.index;
                            destinationLOD.matrix = sourceLOD.matrix;
                        }
                    }

                    return blobBuilder.CreateBlobAssetReference<MeshInstanceHierarchyDefinition>(Allocator.Persistent);
                }
            }

        }

#if UNITY_EDITOR
        [HideInInspector]
        public UnityEngine.Transform rendererRoot;

        public MeshInstanceRigDatabase rigDatabase;

        public Data data;
#endif

        [SerializeField, HideInInspector]
        private byte[] __bytes;

        private BlobAssetReference<MeshInstanceHierarchyDefinition> __definition;

        public BlobAssetReference<MeshInstanceHierarchyDefinition> definition => __definition;

        ~MeshInstanceHierarchyDatabase()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (__definition.IsCreated)
            {
                __definition.Dispose();

                __definition = BlobAssetReference<MeshInstanceHierarchyDefinition>.Null;
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
                            __definition = reader.Read<MeshInstanceHierarchyDefinition>();
                        }
                    }
                }

                __bytes = null;
            }
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

        void OnDestroy()
        {
            Dispose();
        }

#if UNITY_EDITOR
        public void Create(UnityEngine.Transform rendererRoot)
        {
            var rigNodeIndices = new Dictionary<UnityEngine.Transform, int>();

            Data.Build(rendererRoot.root, rigDatabase.data.nodes, rigNodeIndices);

            var rendererLODCounts = new Dictionary<Renderer, int>();
            MeshInstanceRendererDatabase.Build(rendererRoot.GetComponentsInChildren<LODGroup>(), rendererLODCounts);

            var transformRenderGroups = new Dictionary<UnityEngine.Transform, Data.RenderGroup>();
            Data.Build(rendererLODCounts, transformRenderGroups, rendererRoot.GetComponentsInChildren<Renderer>());
            Data.Build(transformRenderGroups, rendererRoot.GetComponentsInChildren<LODGroup>());

            data.Create(rigNodeIndices, transformRenderGroups);
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