using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Animation;
//using Unity.Animation.Hybrid;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ZG
{
    [CreateAssetMenu(fileName = "Mesh Instance Skeleton Database", menuName = "ZG/Mesh Instance/Skeleton Database")]
    public class MeshInstanceSkeletonDatabase : ScriptableObject, ISerializationCallbackReceiver
    {
        [Serializable]
        public struct Bone
        {
            public int rigIndex;
            public int skinMeshIndex;
            public Matrix4x4 bindPose;
            public Matrix4x4 matrix;
        }

        [Serializable]
        public struct DirectBone
        {
            public string name;

            public Bone value;
        }

        [Serializable]
        public struct IndirectBone
        {
            public string name;

            public Bone value;

            public Matrix4x4 offset;
        }

        [Serializable]
        public struct BlendShape
        {
            public string name;

            public int index;
            public int rigIndex;
        }

        [Serializable]
        public struct Skeleton
        {
            public string name;

            public MeshInstanceSkeletonFlag flag;
            public int rootBoneIndex;
            public DirectBone[] bones;
            public IndirectBone[] indirectBones;
            public BlendShape[] blendShapes;
            public float[] blendShapeWeights;

            public Mesh Bake(Mesh skinnedMesh)
            {
                int numDirectBones = bones.Length, numIndirectBones = indirectBones.Length;
                Matrix4x4 skinMatrix;
                Matrix4x4[] skinMatrices = new Matrix4x4[numDirectBones + numIndirectBones];

                for (int i = 0; i < numDirectBones; ++i)
                {
                    ref readonly var bone = ref bones[i].value;

                    skinMatrix = bone.matrix * bone.bindPose;
                    skinMatrices[bone.skinMeshIndex] = skinMatrix;
                }

                for (int i = 0; i < numIndirectBones; ++i)
                {
                    ref var bone = ref indirectBones[i].value;

                    skinMatrix = bone.matrix * bone.bindPose;
                    skinMatrices[bone.skinMeshIndex] = skinMatrix;
                }

                var boneWeights = skinnedMesh.boneWeights;
                var originVertices = skinnedMesh.vertices;
                var originNormals = skinnedMesh.normals;
                var originTangents = skinnedMesh.tangents;

                int numVertices = originVertices.Length;
                var skinnedVertices = new Vector3[numVertices];
                var skinnedNormals = new Vector3[numVertices];
                var skinnedTangents = new Vector4[numVertices];

                for (int i = 0; i < numVertices; ++i)
                {
                    ref readonly var boneWeight = ref boneWeights[i];

                    ref readonly var originVertex = ref originVertices[i];
                    skinnedVertices[i] = __Skin(new Vector4(originVertex.x, originVertex.y, originVertex.z, 1.0f), boneWeight, skinMatrices);

                    ref readonly var originNormal = ref originNormals[i];
                    skinnedNormals[i] = __Skin(new Vector4(originNormal.x, originNormal.y, originNormal.z, 0.0f), boneWeight, skinMatrices);

                    ref readonly var originTangent = ref originTangents[i];
                    skinnedTangents[i] = __Skin(new Vector4(originTangent.x, originTangent.y, originTangent.z, 0.0f), boneWeight, skinMatrices);
                }

                var mesh = Instantiate(skinnedMesh);
                mesh.boneWeights = null;
                mesh.vertices = skinnedVertices;
                mesh.normals = skinnedNormals;
                mesh.tangents = skinnedTangents;

                return mesh;
            }

            private Vector3 __Skin(Vector4 origin, in BoneWeight boneWeight, Matrix4x4[] skinMatrices)
            {
                var result = Vector3.zero;

                ref readonly var skinMatrix0 = ref skinMatrices[boneWeight.boneIndex0];

                result += (Vector3)(skinMatrix0 * origin) * boneWeight.weight0;

                ref readonly var skinMatrix1 = ref skinMatrices[boneWeight.boneIndex1];

                result += (Vector3)(skinMatrix1 * origin) * boneWeight.weight1;

                ref readonly var skinMatrix2 = ref skinMatrices[boneWeight.boneIndex2];

                result += (Vector3)(skinMatrix2 * origin) * boneWeight.weight2;

                ref readonly var skinMatrix3 = ref skinMatrices[boneWeight.boneIndex3];

                result += (Vector3)(skinMatrix3 * origin) * boneWeight.weight3;

                return result;
            }
        }

        [Serializable]
        public struct Instance
        {
            public string name;

            public int skeletionIndex;
            public int rigIndex;
            public Matrix4x4 localToRoot;
            public int[] rendererIndices;
        }

        [Serializable]
        public struct Data
        {
#if UNITY_EDITOR
            public static bool isShowProgressBar = true;
#endif

            public Skeleton[] skeletons;
            public Instance[] instances;

            public Transform Bake(Transform renderRoot)
            {
                int skeletonIndex = 0;
                Mesh skinnedMesh;
                var bakedMeshes = new Mesh[skeletons.Length];
                Dictionary<Mesh, int> meshIndices = new Dictionary<Mesh, int>();
                var renderers = renderRoot.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (var renderer in renderers)
                {
                    skinnedMesh = renderer.sharedMesh;
                    if (!meshIndices.ContainsKey(skinnedMesh))
                    {
                        meshIndices[skinnedMesh] = skeletonIndex;

                        bakedMeshes[skeletonIndex] = skeletons[skeletonIndex].Bake(skinnedMesh);

                        ++skeletonIndex;
                    }
                }

                int numInstances = instances.Length;
                Transform root = new GameObject(renderRoot.name).transform, child, parent, rootBone;
                Dictionary<Transform, Transform> parents = new Dictionary<Transform, Transform>();
                for (int i = 0; i < numInstances; ++i)
                {
                    ref readonly var instance = ref instances[i];

                    child = new GameObject(instance.name, typeof(MeshFilter), typeof(MeshRenderer)).transform;

                    child.GetComponent<MeshFilter>().sharedMesh = bakedMeshes[instance.skeletionIndex];

                    rootBone = renderers[i].rootBone;
                    if (rootBone == null)
                        parent = root;
                    else if (!parents.TryGetValue(rootBone, out parent))
                    {
                        parent = new GameObject(rootBone.name).transform;

                        parent.position = rootBone.position;
                        parent.rotation = rootBone.rotation;
                        parent.localScale = rootBone.lossyScale;

                        parent.SetParent(root, true);
                    }

                    child.SetParent(parent, false);
                }

                return root;
            }

            public BlobAssetReference<MeshInstanceSkeletonDefinition> ToAsset(int instanceID)
            {
                using (var blobBuilder = new BlobBuilder(Allocator.Temp))
                {
                    ref var root = ref blobBuilder.ConstructRoot<MeshInstanceSkeletonDefinition>();
                    root.instanceID = instanceID;

                    int numSkeletons = this.skeletons.Length, numBones, numIndirectBones, numBlendShapes, numBlendShapeWeights, i, j;
                    BlobBuilderArray<MeshInstanceSkeletonDefinition.Bone> bones;
                    BlobBuilderArray<MeshInstanceSkeletonDefinition.IndirectBone> indirectBones;
                    BlobBuilderArray<MeshInstanceSkeletonDefinition.BlendShape> blendShapes;
                    BlobBuilderArray<float> blendShapeWeights;
                    var skeletons = blobBuilder.Allocate(ref root.skeletons, numSkeletons);
                    for (i = 0; i < numSkeletons; ++i)
                    {
                        ref readonly var sourceSkeleton = ref this.skeletons[i];

                        ref var destinationSkeleton = ref skeletons[i];

                        destinationSkeleton.flag = sourceSkeleton.flag;
                        destinationSkeleton.rootBoneIndex = sourceSkeleton.rootBoneIndex;

                        numBones = sourceSkeleton.bones == null ? 0 : sourceSkeleton.bones.Length;

                        bones = blobBuilder.Allocate(ref destinationSkeleton.bones, numBones);
                        for (j = 0; j < numBones; ++j)
                        {
                            ref readonly var sourceBone = ref sourceSkeleton.bones[j];

                            ref var destinationBone = ref bones[j];

                            destinationBone.rigIndex = sourceBone.value.rigIndex;
                            destinationBone.skinMeshIndex = sourceBone.value.skinMeshIndex;
                            destinationBone.bindPose = sourceBone.value.bindPose;
                            destinationBone.matrix = sourceBone.value.matrix;
                        }

                        numIndirectBones = sourceSkeleton.indirectBones == null ? 0 : sourceSkeleton.indirectBones.Length;

                        indirectBones = blobBuilder.Allocate(ref destinationSkeleton.indirectBones, numIndirectBones);
                        for (j = 0; j < numIndirectBones; ++j)
                        {
                            ref readonly var sourceIndirectBone = ref sourceSkeleton.indirectBones[j];

                            ref var destinationIndirectBone = ref indirectBones[j];

                            destinationIndirectBone.value.rigIndex = sourceIndirectBone.value.rigIndex;
                            destinationIndirectBone.value.skinMeshIndex = sourceIndirectBone.value.skinMeshIndex;
                            destinationIndirectBone.value.bindPose = sourceIndirectBone.value.bindPose;
                            destinationIndirectBone.value.matrix = sourceIndirectBone.value.matrix;
                            destinationIndirectBone.offset = Unity.Mathematics.math.RigidTransform(sourceIndirectBone.offset);
                        }

                        numBlendShapes = sourceSkeleton.blendShapes == null ? 0 : sourceSkeleton.blendShapes.Length;

                        blendShapes = blobBuilder.Allocate(ref destinationSkeleton.blendShapes, numBlendShapes);
                        for (j = 0; j < numBlendShapes; ++j)
                        {
                            ref readonly var sourceBlendShape = ref sourceSkeleton.blendShapes[j];

                            ref var destinationBlendShape = ref blendShapes[j];

                            destinationBlendShape.index = sourceBlendShape.index;
                            destinationBlendShape.rigIndex = sourceBlendShape.rigIndex;
                        }

                        numBlendShapeWeights = sourceSkeleton.blendShapeWeights == null ? 0 : sourceSkeleton.blendShapeWeights.Length;

                        blendShapeWeights = blobBuilder.Allocate(ref destinationSkeleton.blendShapeWeights, numBlendShapeWeights);
                        for (j = 0; j < numBlendShapeWeights; ++j)
                            blendShapeWeights[j] = sourceSkeleton.blendShapeWeights[j];
                    }

                    int numInstances = this.instances.Length, numRendererIndices;
                    BlobBuilderArray<int> rendererIndices;
                    var instances = blobBuilder.Allocate(ref root.instances, numInstances);
                    for (i = 0; i < numInstances; ++i)
                    {
                        ref readonly var sourceInstance = ref this.instances[i];

                        ref var destinationInstance = ref instances[i];

                        destinationInstance.skeletionIndex = sourceInstance.skeletionIndex;
                        destinationInstance.rigIndex = sourceInstance.rigIndex;
                        destinationInstance.localToRoot = sourceInstance.localToRoot;

                        numRendererIndices = sourceInstance.rendererIndices == null ? 0 : sourceInstance.rendererIndices.Length;

                        rendererIndices = blobBuilder.Allocate(ref destinationInstance.rendererIndices, numRendererIndices);
                        for (j = 0; j < numRendererIndices; ++j)
                            rendererIndices[j] = sourceInstance.rendererIndices[j];
                    }

                    return blobBuilder.CreateBlobAssetReference<MeshInstanceSkeletonDefinition>(Allocator.Persistent);
                }
            }

            public void Create(bool isRoot, Transform rendererRoot, in MeshInstanceRigDatabase.Data rig/*, out Type[] types*/)
            {
                var renderers = rendererRoot.GetComponentsInChildren<Renderer>();

                var root = rendererRoot.root;

                var rigIndices = MeshInstanceRigDatabase.CreateComponentRigIndices(root.gameObject);

                /*var typeResults = new List<Type>(); 
                var rigIndices = new Dictionary<Component, int>();
                rig.Create(root, typeResults, rigIndices);

                types = typeResults.ToArray();*/

                var rendererLODCounts = new Dictionary<Renderer, int>();

                var lodGroups = rendererRoot.GetComponentsInChildren<LODGroup>();
                if (lodGroups != null)
                    MeshInstanceRendererDatabase.Data.Build(lodGroups, rendererLODCounts);

                Create(
                    isRoot,
                    root,
                    renderers,
                    rig.rigs,
                    rig.nodes,
                    rendererLODCounts,
                    rigIndices);
            }

            public void Create(
                bool isRoot,
                Transform root,
                Renderer[] renderers,
                MeshInstanceRigDatabase.Rig[] rigs,
                MeshInstanceRigDatabase.Node[] nodes,
                IDictionary<Renderer, int> rendererLODCounts,
                IDictionary<Component, int> rigIndices)
            {
                int numRenderers = renderers == null ? 0 : renderers.Length;
                if (numRenderers < 1)
                    return;

#if UNITY_EDITOR
                int index = 0;
#endif

                bool isSkinedMesh;
                int rendererIndex = 0, rendererCount, rendererLODCount, materialCount, numMaterials, skeletonIndex, i;
                Skeleton skeleton;
                Instance instance;
                Mesh mesh;
                MeshFilter meshFilter;
                MeshRenderer meshRenderer;
                SkinnedMeshRenderer skinnedMeshRenderer;
                Component rigRoot;
                Transform transform;
                GameObject target;
                Material[] materials;
                List<Instance> instances = null;
                List<Skeleton> skeletons = null;
                Dictionary<Mesh, int> skeletonIndices = null;
                foreach (var renderer in renderers)
                {
                    target = renderer == null ? null : renderer.gameObject;
                    if (target == null)
                        continue;

                    meshRenderer = renderer as MeshRenderer;
                    if (meshRenderer == null)
                    {
                        skinnedMeshRenderer = renderer as SkinnedMeshRenderer;
                        if (skinnedMeshRenderer == null)
                            continue;

                        mesh = skinnedMeshRenderer.sharedMesh;
                        isSkinedMesh = true;
                    }
                    else
                    {
                        skinnedMeshRenderer = null;

                        meshFilter = renderer.GetComponent<MeshFilter>();
                        mesh = meshFilter == null ? null : meshFilter.sharedMesh;
                        isSkinedMesh = false;
                    }

                    if (mesh == null)
                        continue;

                    transform = renderer.transform;
                    if (transform == null)
                        continue;

#if UNITY_EDITOR
                    if (isShowProgressBar)
                        EditorUtility.DisplayProgressBar("Building Renderers..", renderer.name, (index++ * 1.0f) / numRenderers);
#endif

                    materials = renderer.sharedMaterials;
                    numMaterials = materials == null ? 0 : materials.Length;
                    if (numMaterials > 0)
                    {
                        materialCount = 0;
                        for (i = 0; i < numMaterials; ++i)
                        {
                            if (materials[i] == null)
                                UnityEngine.Debug.LogError(renderer.name + " Lost Material Index: " + i);
                            else
                                ++materialCount;
                        }

                        if (!rendererLODCounts.TryGetValue(renderer, out rendererLODCount))
                            rendererLODCount = 1;

                        rendererCount = materialCount * rendererLODCount;

                        if (isSkinedMesh)
                        {
                            if (skeletonIndices == null)
                                skeletonIndices = new Dictionary<Mesh, int>();

                            if (skeletonIndices.TryGetValue(mesh, out skeletonIndex))
                            {
                                rigRoot = __GetRigRoot(renderer.gameObject, out _/*, null*/);
                                if (rigRoot == null || !rigIndices.TryGetValue(rigRoot.transform, out instance.rigIndex))
                                {
                                    UnityEngine.Debug.LogError("Create Skeleton Fail.", renderer);

                                    break;
                                }

                                skeleton = skeletons[skeletonIndex];
                            }
                            else
                            {
                                instance.rigIndex = __CreateSkeleton(
                                    root,
                                    skinnedMeshRenderer,
                                    rigs,
                                    nodes,
                                    rigIndices,
                                    out rigRoot,
                                    out skeleton);
                                if (instance.rigIndex == -1)
                                {
                                    UnityEngine.Debug.LogError("Create Skeleton Fail.", renderer);

                                    break;
                                }

                                if (skeletons == null)
                                    skeletons = new List<Skeleton>();

                                skeletonIndex = skeletons.Count;

                                skeletons.Add(skeleton);
                                skeletonIndices[mesh] = skeletonIndex;

                                var rootBone = skinnedMeshRenderer.rootBone;
                                if (rootBone != null)
                                    rigRoot = rootBone;
                            }

                            instance.skeletionIndex = skeletonIndex;

                            instance.localToRoot = isRoot ? Matrix4x4.identity : Matrix4x4.Inverse(rigRoot.transform.localToWorldMatrix) * renderer.transform.parent.localToWorldMatrix;
                            /*instance.localToRoot = Matrix4x4.Inverse(instance.localToRoot) * 
                                MeshInstanceRigDatabase.SkeletonNode.GetRootDefaultMatrix(
                                    rigs[instance.rigIndex].skeletonNodes, 
                                    skeleton.rootBoneIndex == -1 ? 0 : skeleton.rootBoneIndex);*/

                            instance.rendererIndices = new int[rendererCount];
                            for (i = 0; i < rendererCount; ++i)
                                instance.rendererIndices[i] = rendererIndex++;

                            instance.name = renderer.name;

                            if (instances == null)
                                instances = new List<Instance>();

                            instances.Add(instance);
                        }
                        else
                            rendererIndex += rendererCount;
                    }
                }

                this.instances = instances.ToArray();
                this.skeletons = skeletons.ToArray();

#if UNITY_EDITOR
                if (isShowProgressBar)
                    EditorUtility.ClearProgressBar();
#endif
            }

            private static int __CreateSkeleton(
                Transform root,
                SkinnedMeshRenderer skinnedMeshRenderer,
                MeshInstanceRigDatabase.Rig[] rigs,
                MeshInstanceRigDatabase.Node[] nodes,
                IDictionary<Component, int> rigIndices,
                out Component rigRoot,
                out Skeleton skeleton)
            {
                skeleton = default;

                //var bones = new List<RigIndexToBone>();
                rigRoot = __GetRigRoot(skinnedMeshRenderer.gameObject/*, bones*/, out var humanBones);
                /*int numBones = bones.Count;
                if (numBones < 1)
                    return -1;*/

                int rigIndex;
                if (!rigIndices.TryGetValue(rigRoot, out rigIndex))
                    return -1;

                /*var skeletonNodes = rigs[rigIndex].skeletonNodes;

                var animator = rigRoot as Animator;
                var avatar = animator == null ? null : animator.avatar;
                var humanBones = avatar == null || !avatar.isHuman ? null : avatar.humanDescription.human;
                if(humanBones != null)
                {
                    string boneName;
                    RigIndexToBone bone;
                    for (int i = 0; i < numBones; ++i)
                    {
                        bone = bones[i];
                        if (bone.Index == 0)
                            continue;

                        bone.Index = -1;

                        boneName = bone.Bone.name;
                        foreach (var humanBone in humanBones)
                        {
                            if(humanBone.boneName == boneName)
                            {
                                bone.Index = MeshInstanceRigDatabase.SkeletonNode.IndexOf(skeletonNodes, humanBone.humanName);

                                break;
                            }
                        }

                        if(bone.Index == -1)
                            bone.Index = MeshInstanceRigDatabase.SkeletonNode.IndexOf(skeletonNodes, boneName);

                        if (bone.Index == -1)
                        {
                            //UnityEngine.Debug.LogError($"The bone: {boneName} has not been found!");

                            bones.RemoveAtSwapBack(i--);

                            --numBones;
                        }
                        else
                            bones[i] = bone;
                    }
                }*/

                using (var smrMappings = new NativeList<SkinnedMeshToRigIndexMapping>(Allocator.Temp))
                using (var smrIndirectMappings = new NativeList<SkinnedMeshToRigIndexIndirectMapping>(Allocator.Temp))
                {
                    var boneMatchCount = __ExtractMatchingBoneBindings(
                        skinnedMeshRenderer,
                        humanBones,
                        rigs[rigIndex].skeletonNodes,
                        smrMappings,
                        smrIndirectMappings
                    );

                    if (boneMatchCount > 0)
                    {
                        var smrRootBone = skinnedMeshRenderer.rootBone;
                        smrRootBone = smrRootBone == null ? skinnedMeshRenderer.transform : smrRootBone;

                        skeleton.rootBoneIndex = -1;

                        string path = __ComputeRelativePath(smrRootBone, root);

                        int numNodes = nodes.Length;
                        for (int i = 0; i < numNodes; ++i)
                        {
                            if (nodes[i].path == path)
                            {
                                skeleton.rootBoneIndex = i;

                                break;
                            }
                        }

                        Matrix4x4 invRoot = smrRootBone.localToWorldMatrix.inverse;

                        var skinBones = skinnedMeshRenderer.bones;
                        var bindposes = skinnedMeshRenderer.sharedMesh.bindposes;

                        int numSmrMappings = smrMappings.Length;
                        skeleton.bones = new DirectBone[numSmrMappings];

                        DirectBone directBone;
                        SkinnedMeshToRigIndexMapping smrMapping;
                        for (int i = 0; i < numSmrMappings; ++i)
                        {
                            smrMapping = smrMappings[i];

                            directBone.name = skinBones[smrMapping.SkinMeshIndex].name;

                            directBone.value.rigIndex = smrMapping.RigIndex;
                            directBone.value.skinMeshIndex = smrMapping.SkinMeshIndex;

                            directBone.value.matrix = invRoot * skinBones[smrMapping.SkinMeshIndex].localToWorldMatrix;
                            directBone.value.bindPose = bindposes[smrMapping.SkinMeshIndex];

                            skeleton.bones[i] = directBone;
                        }

                        int numSmrIndirectMappings = smrIndirectMappings.Length;
                        skeleton.indirectBones = new IndirectBone[numSmrIndirectMappings];

                        IndirectBone indirectBone;
                        SkinnedMeshToRigIndexIndirectMapping smrIndirectMapping;
                        for (int i = 0; i < numSmrIndirectMappings; ++i)
                        {
                            smrIndirectMapping = smrIndirectMappings[i];

                            indirectBone.name = skinBones[smrIndirectMapping.SkinMeshIndex].name;

                            indirectBone.value.rigIndex = smrIndirectMapping.RigIndex;
                            indirectBone.value.skinMeshIndex = smrIndirectMapping.SkinMeshIndex;

                            indirectBone.value.matrix = invRoot * skinBones[smrIndirectMapping.SkinMeshIndex].localToWorldMatrix;
                            indirectBone.value.bindPose = bindposes[smrIndirectMapping.SkinMeshIndex];

                            indirectBone.offset = (Unity.Mathematics.float4x4)smrIndirectMapping.Offset;

                            skeleton.indirectBones[i] = indirectBone;
                        }
                    }
                }

                using (var blendShapeToRigIndexMappings = new NativeList<BlendShapeToRigIndexMapping>(Allocator.Temp))
                {
                    int matchCount = __ExtractMatchingBlendShapeBindings(
                        skinnedMeshRenderer,
                        rigRoot.transform,
                        rigs[rigIndex].floatChannels,
                        blendShapeToRigIndexMappings);

                    var sharedMesh = skinnedMeshRenderer.sharedMesh;
                    if (matchCount > 0)
                    {
                        /*#if !ENABLE_COMPUTE_DEFORMATIONS
                                                UnityEngine.Debug.LogError("DOTS SkinnedMeshRenderer blendshapes are only supported via compute shaders in hybrid renderer. Make sure to add 'ENABLE_COMPUTE_DEFORMATIONS' to your scripting defines in Player settings.");
                        #endif*/

                        if (__AreBlendShapeWeightsContiguous(sharedMesh, blendShapeToRigIndexMappings))
                            skeleton.flag |= MeshInstanceSkeletonFlag.BlendShapeWeightsContiguous;

                        int numBlendShapeToRigIndexMappings = blendShapeToRigIndexMappings.Length;
                        BlendShapeToRigIndexMapping blendShapeToRigIndexMapping;
                        BlendShape blendShape;
                        skeleton.blendShapes = new BlendShape[numBlendShapeToRigIndexMappings];
                        for (int i = 0; i < numBlendShapeToRigIndexMappings; ++i)
                        {
                            blendShapeToRigIndexMapping = blendShapeToRigIndexMappings[i];

                            blendShape.name = sharedMesh.GetBlendShapeName(blendShapeToRigIndexMapping.BlendShapeIndex);

                            blendShape.index = blendShapeToRigIndexMapping.BlendShapeIndex;
                            blendShape.rigIndex = blendShapeToRigIndexMapping.RigIndex;

                            skeleton.blendShapes[i] = blendShape;
                        }
                    }

                    int numBlendShapeWeights = sharedMesh.blendShapeCount;
                    skeleton.blendShapeWeights = new float[numBlendShapeWeights];
                    for (int i = 0; i < numBlendShapeWeights; ++i)
                        skeleton.blendShapeWeights[i] = skinnedMeshRenderer.GetBlendShapeWeight(i);
                }

                skeleton.name = skinnedMeshRenderer.name;

                return rigIndex;
            }

            private static Component __GetRigRoot(GameObject gameObject/*, List<RigIndexToBone> bones*/, out HumanBone[] humanBones)
            {
                var rigAuthoring = gameObject.GetComponent<IRigAuthoring>();
                if (rigAuthoring != null)
                {
                    /*if(bones != null)
                        rigAuthoring.GetBones(bones);*/

                    humanBones = null;

                    return (Component)rigAuthoring;
                }

                var animator = gameObject.GetComponent<Animator>();
                if (animator != null)
                {
                    /*if (bones != null)
                    {
                        animator.ExtractBoneTransforms(bones);

                        MeshInstanceRigDatabase.CollectBones(gameObject.transform, x =>
                        {
                            RigIndexToBone rigIndexToBone;
                            rigIndexToBone.Bone = x.transform;
                            rigIndexToBone.Index = bones.Count;
                            bones.Add(rigIndexToBone);
                        });
                    }*/

                    var avatar = animator.avatar;
                    humanBones = avatar == null || !avatar.isHuman ? null : avatar.humanDescription.human;

                    return animator;
                }

                var parent = gameObject.transform.parent;
                if (parent == null)
                {
                    humanBones = null;

                    return null;
                }

                return __GetRigRoot(parent.gameObject/*, bones*/, out humanBones);
            }

            private static bool __AreBlendShapeWeightsContiguous(Mesh mesh, NativeList<BlendShapeToRigIndexMapping> mappings)
            {
                if (mesh.blendShapeCount != mappings.Length)
                    return false;

                var testIndices = mappings[0];
                for (int i = 1; i < mappings.Length; ++i)
                {
                    testIndices.BlendShapeIndex++;
                    testIndices.RigIndex++;

                    if (mappings[i].RigIndex != testIndices.RigIndex ||
                        mappings[i].BlendShapeIndex != testIndices.BlendShapeIndex)
                        return false;
                }

                return true;
            }

            public static string __ComputeRelativePath(Transform target, Transform root)
            {
                var stack = new List<Transform>(10);
                var cur = target;
                while (cur != root && cur != null)
                {
                    stack.Add(cur);
                    cur = cur.parent;
                }

                var res = "";
                if (stack.Count > 0)
                {
                    for (var i = stack.Count - 1; i > 0; --i)
                        res += stack[i].name + "/";
                    res += stack[0].name;
                }

                return res;
            }


            private static int __ExtractMatchingBoneBindings(
                SkinnedMeshRenderer skinnedMeshRenderer,
                //List<RigIndexToBone> skeletonBones,
                HumanBone[] humanBones,
                MeshInstanceRigDatabase.SkeletonNode[] skeletonNodes,
                NativeList<SkinnedMeshToRigIndexMapping> outSMRMappings,
                NativeList<SkinnedMeshToRigIndexIndirectMapping> outSMRIndirectMappings)
            {
                outSMRMappings.Clear();
                outSMRIndirectMappings.Clear();

                if (skinnedMeshRenderer == null)
                    throw new ArgumentNullException("Invalid SkinnedMeshRenderer.");
                if (!outSMRMappings.IsCreated)
                    throw new ArgumentException($"Invalid ${nameof(outSMRMappings)}");
                if (!outSMRIndirectMappings.IsCreated)
                    throw new ArgumentException($"Invalid ${nameof(outSMRIndirectMappings)}");

                var skinBones = skinnedMeshRenderer.bones;
                if (skinBones == null)
                    return 0;

                int matchCount = 0;

                int boneIdx;
                for (int i = 0; i < skinBones.Length; ++i)
                {
                    var smrBone = skinBones[i];

                    boneIdx = __FindBoneIndex(smrBone.name, humanBones, skeletonNodes);
                    if (boneIdx == -1)
                    {
                        // Immediate SMR to skeleton mapping not found, walk the hierarchy to find possible parent
                        // and compute static offset
                        var parent = smrBone.parent;
                        while (parent != null)
                        {
                            boneIdx = __FindBoneIndex(parent.name, humanBones, skeletonNodes);
                            if (boneIdx == -1)
                                parent = parent.parent;
                            else
                            {
                                outSMRIndirectMappings.Add(new SkinnedMeshToRigIndexIndirectMapping
                                {
                                    Offset = mathex.AffineTransform(parent.worldToLocalMatrix * smrBone.localToWorldMatrix),
                                    RigIndex = boneIdx,
                                    SkinMeshIndex = i
                                });

                                matchCount++;
                                break;
                            }
                        }

                        if (parent == null)
                            UnityEngine.Debug.LogWarning($"{skinnedMeshRenderer} references bone '{skinBones[i].name}' that cannot be found.");
                    }
                    else
                    {
                        outSMRMappings.Add(new SkinnedMeshToRigIndexMapping
                        {
                            SkinMeshIndex = i,
                            RigIndex = boneIdx
                        });

                        matchCount++;
                    }
                }

                return matchCount;
            }

            private static int __FindBoneIndex(
                string boneName,
                HumanBone[] humanBones,
                MeshInstanceRigDatabase.SkeletonNode[] skeletonNodes)
            {
                if (humanBones != null)
                {
                    int boneIndex;
                    foreach (var humanBone in humanBones)
                    {
                        if (humanBone.boneName == boneName)
                        {
                            boneIndex = MeshInstanceRigDatabase.SkeletonNode.IndexOf(skeletonNodes, humanBone.humanName);
                            if (boneIndex != -1)
                                return boneIndex;

                            break;
                        }
                    }
                }

                return MeshInstanceRigDatabase.SkeletonNode.IndexOf(skeletonNodes, boneName);
            }

            private static int __ExtractMatchingBlendShapeBindings(
                SkinnedMeshRenderer skinnedMeshRenderer,
                Transform root,
                FloatChannel[] floatChannels,
                NativeList<BlendShapeToRigIndexMapping> outBlendShapeToRigIndexMapping)
            {
                if (skinnedMeshRenderer == null)
                    throw new ArgumentNullException("Invalid SkinnedMeshRenderer.");
                if (root == null)
                    throw new ArgumentNullException($"Invalid root transform {nameof(root)}");
                if (!outBlendShapeToRigIndexMapping.IsCreated)
                    throw new ArgumentNullException($"Invalid {nameof(outBlendShapeToRigIndexMapping)}");

                var sharedMesh = skinnedMeshRenderer.sharedMesh;
                if (sharedMesh == null)
                    throw new ArgumentNullException("SkinnedMeshRenderer contains a null SharedMesh.");

                int count = sharedMesh.blendShapeCount;
                if (count == 0)
                    return 0;

                var relativePath = __ComputeRelativePath(skinnedMeshRenderer.transform, root);

                int matchCount = 0, i, j;
                string id;
                for (i = 0; i < count; ++i)
                {
                    id = __BuildPath(relativePath, $"blendShape.{sharedMesh.GetBlendShapeName(i)}");

                    int length = floatChannels == null ? 0 : floatChannels.Length;
                    for (j = 0; j < length;  ++j)
                    {
                        if (floatChannels[j].Id == id)
                            break;
                    }

                    if (j < floatChannels.Length)
                    {
                        outBlendShapeToRigIndexMapping.Add(new BlendShapeToRigIndexMapping
                        {
                            BlendShapeIndex = i,
                            RigIndex = j
                        });

                        matchCount++;
                    }
                }

                return matchCount;
            }

            private static string __BuildPath(string path, string property)
            {
                bool nullPath = string.IsNullOrEmpty(path);
                bool nullProperty = string.IsNullOrEmpty(property);

                if (nullPath && nullProperty)
                    return string.Empty;
                if (nullPath)
                    return property;
                if (nullProperty)
                    return path;

                return path + "/" + property;
            }

        }

        [SerializeField, HideInInspector]
        [UnityEngine.Serialization.FormerlySerializedAs("_bytes")]
        private byte[] __bytes;

        private BlobAssetReference<MeshInstanceSkeletonDefinition> __definition;

        public BlobAssetReference<MeshInstanceSkeletonDefinition> definition => __definition;

        ~MeshInstanceSkeletonDatabase()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (__definition.IsCreated)
                __definition.Dispose();
        }

        unsafe void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (__bytes != null && __bytes.Length > 0)
            {
                if (__definition.IsCreated)
                    __definition.Dispose();

                fixed (byte* ptr = __bytes)
                {
                    using (var reader = new MemoryBinaryReader(ptr, __bytes.LongLength))
                    {
                        __definition = reader.Read<MeshInstanceSkeletonDefinition>();
                    }
                }

                __bytes = null;
            }
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            if (!__definition.IsCreated)
                return;

            using (var writer = new MemoryBinaryWriter())
            {
                writer.Write(__definition);

                __bytes = writer.GetContentAsNativeArray().ToArray();
            }
        }

        void OnDestroy()
        {
            Dispose();
        }

#if UNITY_EDITOR
        public static bool isShowProgressBar = true;

        public bool isRoot = true;

        [HideInInspector]
        public Transform rendererRoot;

        [UnityEngine.Serialization.FormerlySerializedAs("rig")]
        public MeshInstanceRigDatabase rigDatabase;

        public Data data;

        public void Create(Transform root)
        {
            data.Create(isRoot, root, rigDatabase.data/*, out var types*/);

            //rigDatabase.types = types;
        }

        public void Rebuild()
        {
            Dispose();

            __definition = data.ToAsset(GetInstanceID());

            ((ISerializationCallbackReceiver)this).OnBeforeSerialize();
        }

        public void EditorMaskDirty()
        {
            Rebuild();

            rigDatabase.EditorMaskDirty();

            EditorUtility.SetDirty(this);
        }
#endif
    }
}