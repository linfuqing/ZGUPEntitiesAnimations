using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;

namespace ZG
{
    [CreateAssetMenu(fileName = "Mesh Instance Hybrid Rig Database", menuName = "ZG/Mesh Instance/Hybrid Rig Database")]
    public class MeshInstanceHybridRigDatabase : ScriptableObject, ISerializationCallbackReceiver
    {
        [Serializable]
        public struct Data
        {
            [Serializable]
            public struct Node
            {
                public string bonePath;
                public string transformPath;

                [Mask]
                public HybridRigNodeTransformType transformTyoe;
            }

            [Serializable]
            public struct Rig
            {
                public string name;

                public int index;

                public Node[] nodes;
            }

            public Rig[] rigs;

            public BlobAssetReference<MeshInstanceHybridRigDefinition> ToAsset()
            {
                var blobBuilder = new BlobBuilder(Allocator.Temp);
                ref var root = ref blobBuilder.ConstructRoot<MeshInstanceHybridRigDefinition>();

                int i, j, numNodes, numRigs = this.rigs.Length;
                var rigs = blobBuilder.Allocate(ref root.rigs, numRigs);
                BlobBuilderArray<MeshInstanceHybridRigDefinition.Node> nodes;
                for (i = 0; i < numRigs; ++i)
                {
                    ref readonly var sourceRig = ref this.rigs[i];
                    ref var destinationRig = ref rigs[i];

                    destinationRig.index = sourceRig.index;

                    numNodes = sourceRig.nodes.Length;
                    nodes = blobBuilder.Allocate(ref destinationRig.nodes, numNodes);
                    for (j = 0; j < numNodes; ++j)
                    {
                        ref readonly var sourceNode = ref sourceRig.nodes[j];
                        ref var destinationNode = ref nodes[j];

                        destinationNode.transformType = sourceNode.transformTyoe;
                        destinationNode.boneID = sourceNode.bonePath;
                        blobBuilder.AllocateString(ref destinationNode.transformPath, sourceNode.transformPath);
                    }
                }

                var asset = blobBuilder.CreateBlobAssetReference<MeshInstanceHybridRigDefinition>(Allocator.Persistent);

                blobBuilder.Dispose();

                return asset;
            }
        }

        [SerializeField, HideInInspector]
        private byte[] __bytes;

        private BlobAssetReference<MeshInstanceHybridRigDefinition> __definition;

        public BlobAssetReference<MeshInstanceHybridRigDefinition> definition
        {
            get
            {
                return __definition;
            }
        }

        ~MeshInstanceHybridRigDatabase()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (__definition.IsCreated)
            {
                __definition.Dispose();

                __definition = BlobAssetReference<MeshInstanceHybridRigDefinition>.Null;
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
                            __definition = reader.Read<MeshInstanceHybridRigDefinition>();
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
        public Data data;

        public void Rebuild()
        {
            Dispose();

            __definition = data.ToAsset();

            ((ISerializationCallbackReceiver)this).OnBeforeSerialize();
        }

        public void EditorMaskDirty()
        {
            Rebuild();

            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}