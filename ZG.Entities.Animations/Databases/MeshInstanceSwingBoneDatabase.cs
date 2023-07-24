using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEditor;
using UnityEngine;

namespace ZG
{
    [CreateAssetMenu(fileName = "Mesh Instance Swing Bone Database", menuName = "ZG/Mesh Instance/Swing Bone Database")]
    public class MeshInstanceSwingBoneDatabase : ScriptableObject, ISerializationCallbackReceiver
    {
        [Serializable]
        public struct Data
        {
            [Serializable]
            public struct Bone
            {
                public string bonePath;

                public float windDelta;
                public float sourceDelta;
                public float destinationDelta;
            }

            [Serializable]
            public struct Rig
            {
                public string name;

                public int index;

                public Bone[] bones;
            }

            public Rig[] rigs;

            public BlobAssetReference<MeshInstanceSwingBoneDefinition> ToAsset()
            {
                using (var blobBuilder = new BlobBuilder(Allocator.Temp))
                {
                    ref var root = ref blobBuilder.ConstructRoot<MeshInstanceSwingBoneDefinition>();

                    int i, j, numBones, numRigs = this.rigs.Length;
                    var rigs = blobBuilder.Allocate(ref root.rigs, numRigs);
                    BlobBuilderArray<MeshInstanceSwingBoneDefinition.Bone> bones;
                    for (i = 0; i < numRigs; ++i)
                    {
                        ref readonly var sourceRig = ref this.rigs[i];
                        ref var destinationRig = ref rigs[i];

                        destinationRig.index = sourceRig.index;

                        numBones = sourceRig.bones.Length;
                        bones = blobBuilder.Allocate(ref destinationRig.bones, numBones);
                        for (j = 0; j < numBones; ++j)
                        {
                            ref readonly var sourceBone = ref sourceRig.bones[j];
                            ref var destinationBone = ref bones[j];

                            destinationBone.boneID = sourceBone.bonePath;
                            destinationBone.windDelta = sourceBone.windDelta;
                            destinationBone.sourceDelta = sourceBone.sourceDelta;
                            destinationBone.destinationDelta = sourceBone.destinationDelta;
                        }
                    }

                    return blobBuilder.CreateBlobAssetReference<MeshInstanceSwingBoneDefinition>(Allocator.Persistent);
                }
            }
        }

        [SerializeField, HideInInspector]
        private byte[] __bytes;

        private BlobAssetReference<MeshInstanceSwingBoneDefinition> __definition;

        public BlobAssetReference<MeshInstanceSwingBoneDefinition> definition
        {
            get
            {
                return __definition;
            }
        }

        ~MeshInstanceSwingBoneDatabase()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (__definition.IsCreated)
            {
                __definition.Dispose();

                __definition = BlobAssetReference<MeshInstanceSwingBoneDefinition>.Null;
            }
        }

        protected void OnDestroy()
        {
            Dispose();
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
                            __definition = reader.Read<MeshInstanceSwingBoneDefinition>();
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


#if UNITY_EDITOR
        public Data data;

        protected void OnValidate()
        {
            if(!EditorApplication.isPlayingOrWillChangePlaymode)
                Rebuild();
        }

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