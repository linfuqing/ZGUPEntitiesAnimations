using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Entities.Serialization;
using Unity.Animation;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

namespace ZG
{
    public static class MeshInstanceAnimatorUtility
    {
        public static BlobAssetReference<MotionClipWeightMaskDefinition> ToAsset(this AvatarMask avatarMask, in MeshInstanceRigDatabase.Rig rig)
        {
            int i, numSkeletonNodes = rig.skeletonNodes.Length;
            string path, name;
            var humanBones = rig.avatar.humanDescription.human;
            var boneIndices = new Dictionary<string, int>();
            for (i = 1; i < numSkeletonNodes; ++i)
            {
                path = rig.skeletonNodes[i].path;

                boneIndices.Add(path, i);

                name = MeshInstanceRigDatabase.SkeletonNode.PathToName(path);

                foreach (var humanBone in humanBones)
                {
                    if (name == humanBone.humanName || name == humanBone.boneName)
                    {
                        boneIndices[humanBone.humanName] = i;

                        break;
                    }
                }
            }

            int maskIndex;
            var maskIndices = new HashSet<int>();
            var humanNames = HumanTrait.BoneName;
            for (AvatarMaskBodyPart avatarMaskBodyPart = 0; avatarMaskBodyPart < AvatarMaskBodyPart.LastBodyPart; ++avatarMaskBodyPart)
            {
                if (avatarMask.GetHumanoidBodyPartActive(avatarMaskBodyPart))
                    continue;

                switch(avatarMaskBodyPart)
                {
                    case AvatarMaskBodyPart.Root:
                        maskIndices.Add(0);
                        break;
                    case AvatarMaskBodyPart.Body:
                        if(boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.Hips], out maskIndex))
                            maskIndices.Add(maskIndex);
                        break;
                    case AvatarMaskBodyPart.Head:
                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.Head], out maskIndex))
                            maskIndices.Add(maskIndex);
                        break;
                    case AvatarMaskBodyPart.LeftLeg:
                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftUpperLeg], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftLowerLeg], out maskIndex))
                            maskIndices.Add(maskIndex);
                        break;
                    case AvatarMaskBodyPart.RightLeg:
                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightUpperLeg], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightLowerLeg], out maskIndex))
                            maskIndices.Add(maskIndex);
                        break;
                    case AvatarMaskBodyPart.LeftArm:
                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftUpperArm], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftLowerArm], out maskIndex))
                            maskIndices.Add(maskIndex);
                        break;
                    case AvatarMaskBodyPart.RightArm:
                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightUpperArm], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightLowerArm], out maskIndex))
                            maskIndices.Add(maskIndex);
                        break;
                    case AvatarMaskBodyPart.LeftFingers:
                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftThumbProximal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftThumbIntermediate], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftThumbDistal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftIndexProximal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftIndexIntermediate], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftIndexDistal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftMiddleProximal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftMiddleIntermediate], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftMiddleDistal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftRingProximal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftRingIntermediate], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftRingDistal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftLittleProximal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftLittleIntermediate], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.LeftLittleDistal], out maskIndex))
                            maskIndices.Add(maskIndex);
                        break;
                    case AvatarMaskBodyPart.RightFingers:
                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightThumbProximal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightThumbIntermediate], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightThumbDistal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightIndexProximal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightIndexIntermediate], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightIndexDistal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightMiddleProximal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightMiddleIntermediate], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightMiddleDistal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightRingProximal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightRingIntermediate], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightRingDistal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightLittleProximal], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightLittleIntermediate], out maskIndex))
                            maskIndices.Add(maskIndex);

                        if (boneIndices.TryGetValue(humanNames[(int)HumanBodyBones.RightLittleDistal], out maskIndex))
                            maskIndices.Add(maskIndex);
                        break;
                }
            }

            int transformCount = avatarMask.transformCount;
            for(i = 0; i < transformCount; ++i)
            {
                if (avatarMask.GetTransformActive(i))
                    continue;

                if (boneIndices.TryGetValue(avatarMask.GetTransformPath(i), out maskIndex))
                    maskIndices.Add(maskIndex);
            }

            using (var blobBuilder = new BlobBuilder(Allocator.Temp))
            {
                ref var definition = ref blobBuilder.ConstructRoot<MotionClipWeightMaskDefinition>();

                int channelIndex, numWeights = numSkeletonNodes - maskIndices.Count;

                channelIndex = 0;
                var translationWeights = blobBuilder.Allocate(ref definition.translationWeights, numWeights);
                for (i = 0; i < numWeights; ++i)
                {
                    ref var weight = ref translationWeights[i];

                    while (maskIndices.Contains(channelIndex))
                        ++channelIndex;

                    weight.index = channelIndex++;
                    weight.value = 1.0f;
                }

                channelIndex = 0;
                var rotationWeights = blobBuilder.Allocate(ref definition.rotationWeights, numWeights);
                for (i = 0; i < numWeights; ++i)
                {
                    ref var weight = ref rotationWeights[i];

                    while (maskIndices.Contains(channelIndex))
                        ++channelIndex;

                    weight.index = channelIndex++;
                    weight.value = 1.0f;
                }

                channelIndex = 0;
                var scaleWeights = blobBuilder.Allocate(ref definition.scaleWeights, numWeights);
                for (i = 0; i < numWeights; ++i)
                {
                    ref var weight = ref scaleWeights[i];

                    while (maskIndices.Contains(channelIndex))
                        ++channelIndex;

                    weight.index = channelIndex++;
                    weight.value = 1.0f;
                }

                blobBuilder.Allocate(ref definition.floatWeights, 0);
                blobBuilder.Allocate(ref definition.intWeights, 0);

                return blobBuilder.CreateBlobAssetReference<MotionClipWeightMaskDefinition>(Allocator.Persistent);
            }
        }
    }

    [CreateAssetMenu(fileName = "Mesh Instance Animator Database", menuName = "ZG/Mesh Instance/Animator Database")]
    public class MeshInstanceAnimatorDatabase : MeshInstanceDatabase<MeshInstanceAnimatorDatabase>, ISerializationCallbackReceiver
    {
        [Flags]
        private enum InitType
        {
            BlendTree = 0x01, 
            Clip = 0x02,
            RigRemapTable = 0x04,
            ControllerDefinition = 0x08,
            WeightMaskDefinition = 0x10
        }

        [Serializable]
        public struct Remap
        {
            public int index;
            public int sourceRigIndex;
            public int destinationRigIndex;

            public readonly void ToAsset(ref AnimatorControllerDefinition.Remap remap)
            {
                remap.index = index;
                remap.sourceRigIndex = sourceRigIndex;
                remap.destinationRigIndex = destinationRigIndex;
            }
        }

        [Serializable]
        public struct Clip
        {
            public string name;

            public MotionClipFlag flag;

            public MotionClipWrapMode wrapMode;

            public int animationIndex;

            public int[] remapIndices;

            public readonly void ToAsset(ref BlobBuilder blobBuilder, ref AnimatorControllerDefinition.Clip clip)
            {
                clip.flag = flag;
                clip.wrapMode = wrapMode;
                clip.index = animationIndex;

                int numRemapIndices = this.remapIndices == null ? 0 : this.remapIndices.Length;

                var remapIndices = blobBuilder.Allocate(ref clip.remapIndices, numRemapIndices);

                for (int i = 0; i < numRemapIndices; ++i)
                    remapIndices[i] = this.remapIndices[i];
            }
        }

        [Serializable]
        public struct Motion
        {
            public string name;

            public AnimatorControllerMotionType type;

            public int index;

            public int[] childIndices;

            public readonly void ToAsset(ref BlobBuilder blobBuilder, ref AnimatorControllerDefinition.Motion motion)
            {
                motion.type = type;
                motion.index = index;

                int numChildren = this.childIndices == null ? 0 : this.childIndices.Length;

                var childIndices = blobBuilder.Allocate(ref motion.childIndices, numChildren);
                for (int i = 0; i < numChildren; ++i)
                    childIndices[i] = this.childIndices[i];
            }
        }

        [Serializable]
        public struct Parameter
        {
            public string name;

            public AnimatorControllerParameterType type;
            public int defaultValue;

            public readonly void ToAsset(ref AnimatorControllerDefinition.Parameter parameter)
            {
                parameter.type = type;
                parameter.name = name;
                parameter.defaultValue = defaultValue;
            }
        }

        [Serializable]
        public struct Condition
        {
            public AnimatorControllerConditionOpcode opcode;
            public int parameterIndex;
            public int threshold;

            public readonly void ToAsset(ref AnimatorControllerDefinition.Condition condition)
            {
                condition.opcode = opcode;
                condition.parameterIndex = parameterIndex;
                condition.threshold = threshold;
            }
        }

        [Serializable]
        public struct Transition
        {
            public string name;

            public AnimatorControllerTransitionFlag flag;
            public AnimatorControllerInterruptionSource interruptionSource;
            public int destinationStateIndex;

            public float offset;
            public float duration;
            public float exitTime;

            public Condition[] conditions;

            public static void ToAsset(ref BlobBuilder blobBuilder, ref BlobArray<AnimatorControllerDefinition.Transition> destinations, Transition[] sources)
            {
                int i, j, numConditions, numTransitions = sources.Length;
                var transitions = blobBuilder.Allocate(ref destinations, numTransitions);
                BlobBuilderArray<AnimatorControllerDefinition.Condition> conditions;
                for (i = 0; i < numTransitions; ++i)
                {
                    ref readonly var sourceTransition = ref sources[i];

                    ref var destinationTransition = ref transitions[i];

                    destinationTransition.flag = sourceTransition.flag;
                    destinationTransition.interruptionSource = sourceTransition.interruptionSource;
                    destinationTransition.destinationStateIndex = sourceTransition.destinationStateIndex;
                    destinationTransition.offset = sourceTransition.offset;
                    destinationTransition.duration = sourceTransition.duration;
                    destinationTransition.exitTime = sourceTransition.exitTime;

                    numConditions = sourceTransition.conditions == null ? 0 : sourceTransition.conditions.Length;

                    conditions = blobBuilder.Allocate(ref destinationTransition.conditions, numConditions);
                    for (j = 0; j < numConditions; ++j)
                        sourceTransition.conditions[j].ToAsset(ref conditions[j]);
                }
            }
        }

        [Serializable]
        public struct State
        {
            public string name;

            public MotionClipWrapMode wrapMode;
            public int speedMultiplierParameterIndex;
            public float speed;
            public float motionAverageLength;
            public Transition[] transitions;

            public readonly void ToAsset(ref BlobBuilder blobBuilder, ref AnimatorControllerDefinition.State state)
            {
                state.wrapMode = wrapMode;
                state.speedMultiplierParameterIndex = speedMultiplierParameterIndex;
                state.speed = speed;
                state.motionAverageLength = motionAverageLength;

                Transition.ToAsset(ref blobBuilder, ref state.transitions, transitions);
            }
        }

        [Serializable]
        public struct StateMachine
        {
            public string name;

            public int initStateIndex;
            public State[] states;
            public Transition[] globalTransitions;

            public readonly void ToAsset(ref BlobBuilder blobBuilder, ref AnimatorControllerDefinition.StateMachine stateMachine)
            {
                stateMachine.initStateIndex = initStateIndex;

                int numStates = this.states.Length;

                var states = blobBuilder.Allocate(ref stateMachine.states, numStates);

                for (int i = 0; i < numStates; ++i)
                    this.states[i].ToAsset(ref blobBuilder, ref states[i]);

                Transition.ToAsset(ref blobBuilder, ref stateMachine.globalTransitions, globalTransitions);
            }
        }

        [Serializable]
        public struct Layer
        {
            public string name;

            public MotionClipBlendingMode blendingMode;
            public float defaultWeight;
            public AvatarMask avatarMask;

            public int stateMachineIndex;
            public int[] stateMotionIndices;

            public readonly void ToAsset(ref MeshInstanceAnimatorDefinition.Layer layer)
            {
                layer.blendingMode = blendingMode;
                layer.weight = defaultWeight;
            }

            public readonly void ToAsset(ref BlobBuilder blobBuilder, ref AnimatorControllerDefinition.Layer layer)
            {
                layer.stateMachineIndex = stateMachineIndex;

                int numStateMotionIndices = this.stateMotionIndices == null ? 0 : this.stateMotionIndices.Length;

                var stateMotionIndices = blobBuilder.Allocate(ref layer.stateMotionIndices, numStateMotionIndices);
                for (int i = 0; i < numStateMotionIndices; ++i)
                    stateMotionIndices[i] = this.stateMotionIndices[i];
            }
        }

        [Serializable]
        public struct Controller
        {
            public string name;

            public Remap[] remaps;
            public Clip[] clips;
            public Motion[] motions;
            public StateMachine[] stateMachines;
            public Layer[] layers;
            public Parameter[] parameters;

            public readonly BlobAssetReference<AnimatorControllerDefinition> ToAsset(int instanceID)
            {
                var blobBuilder = new BlobBuilder(Allocator.Temp);

                ref var root = ref blobBuilder.ConstructRoot<AnimatorControllerDefinition>();

                root.instanceID = instanceID;

                int i, numRemaps = this.remaps.Length;
                var remaps = blobBuilder.Allocate(ref root.remaps, numRemaps);
                for (i = 0; i < numRemaps; ++i)
                    this.remaps[i].ToAsset(ref remaps[i]);

                int numClips = this.clips.Length;
                var clips = blobBuilder.Allocate(ref root.clips, numClips);
                for (i = 0; i < numClips; ++i)
                    this.clips[i].ToAsset(ref blobBuilder, ref clips[i]);

                int numMotions = this.motions.Length;
                var motions = blobBuilder.Allocate(ref root.motions, numMotions);
                for (i = 0; i < numMotions; ++i)
                    this.motions[i].ToAsset(ref blobBuilder, ref motions[i]);

                int numStateMachines = this.stateMachines.Length;
                var stateMachines = blobBuilder.Allocate(ref root.stateMachines, numStateMachines);
                for (i = 0; i < numStateMachines; ++i)
                    this.stateMachines[i].ToAsset(ref blobBuilder, ref stateMachines[i]);

                int numLayers = this.layers.Length;
                var layers = blobBuilder.Allocate(ref root.layers, numLayers);
                for (i = 0; i < numLayers; ++i)
                    this.layers[i].ToAsset(ref blobBuilder, ref layers[i]);

                int numParameters = this.parameters.Length;
                var parameters = blobBuilder.Allocate(ref root.parameters, numParameters);
                for (i = 0; i < numParameters; ++i)
                    this.parameters[i].ToAsset(ref parameters[i]);

                var result = blobBuilder.CreateBlobAssetReference<AnimatorControllerDefinition>(Allocator.Persistent);
                blobBuilder.Dispose();

                return result;
            }
        }

        [Serializable]
        public struct Rig
        {
            public string name;

            [UnityEngine.Serialization.FormerlySerializedAs("motionRootName")]
            public string rootBonePath;

            public int index;
            public int controllerIndex;

            public readonly void ToAsset(
                ref BlobBuilder blobBuilder, 
                Controller[] controllers, 
                MeshInstanceRigDatabase.Rig[] rigs, 
                ref MeshInstanceAnimatorDefinition.Rig rig, 
                ref NativeList<BlobAssetReference<MotionClipWeightMaskDefinition>> weightMaskDefinitions, 
                Dictionary<AvatarMask, int> weightMaskDefinitionIndices)
            {
                rig.index = index;
                rig.controllerIndex = controllerIndex;
                rig.rootID = rootBonePath;

                ref readonly var dataRig = ref rigs[index];
                ref readonly var controller = ref controllers[controllerIndex];
                MeshInstanceAnimatorDefinition.WeightMask weightMask;
                NativeList<MeshInstanceAnimatorDefinition.WeightMask> weightMaskResults = default;
                int numLayers = controller.layers.Length;
                for(int i = 0; i < numLayers; ++i)
                {
                    ref var layer = ref controller.layers[i];
                    if (layer.avatarMask == null)
                        continue;

                    if (!weightMaskDefinitionIndices.TryGetValue(layer.avatarMask, out weightMask.index))
                    {
                        weightMask.index = weightMaskDefinitions.Length;

                        weightMaskDefinitions.Add(layer.avatarMask.ToAsset(dataRig));

                        weightMaskDefinitionIndices[layer.avatarMask] = weightMask.index;
                    }

                    weightMask.layerIndex = i;

                    if (!weightMaskResults.IsCreated)
                        weightMaskResults = new NativeList<MeshInstanceAnimatorDefinition.WeightMask>(Allocator.Temp);

                    weightMaskResults.Add(weightMask);
                }

                if (weightMaskResults.IsCreated)
                {
                    int numWeightMasks = weightMaskResults.Length;
                    var weightMasks = blobBuilder.Allocate(ref rig.weightMasks, numWeightMasks);
                    for (int i = 0; i < numWeightMasks; ++i)
                        weightMasks[i] = weightMaskResults[i];

                    weightMaskResults.Dispose();
                }
                else
                    blobBuilder.Allocate(ref rig.weightMasks, 0);
            }
        }

        [Serializable]
        public struct Data
        {
            public Controller[] controllers;
            public Rig[] rigs;

            public readonly BlobAssetReference<MeshInstanceAnimatorDefinition> ToAsset(
                int instanceID,
                MeshInstanceRigDatabase.Rig[] dataRigs,
                out BlobAssetReference<AnimatorControllerDefinition>[] controllerDefinitions,
                out BlobAssetReference<MotionClipWeightMaskDefinition>[] weightMaskDefinitions)
            {
                int i, numControllers = this.controllers.Length;

                controllerDefinitions = new BlobAssetReference<AnimatorControllerDefinition>[numControllers];
                for (i = 0; i < numControllers; ++i)
                    controllerDefinitions[i] = this.controllers[i].ToAsset(instanceID);

                var blobBuilder = new BlobBuilder(Allocator.Temp);

                ref var root = ref blobBuilder.ConstructRoot<MeshInstanceAnimatorDefinition>();

                root.instanceID = instanceID;

                var controllers = blobBuilder.Allocate(ref root.controllers, numControllers);
                BlobBuilderArray<MeshInstanceAnimatorDefinition.Layer> layers;
                int numLayers, j;
                for (i = 0; i < numControllers; ++i)
                {
                    ref readonly var sourceController = ref this.controllers[i];
                    ref var destinationController = ref controllers[i];

                    numLayers = sourceController.layers.Length;

                    layers = blobBuilder.Allocate(ref destinationController.layers, numLayers);
                    for (j = 0; j < numLayers; ++j)
                        sourceController.layers[j].ToAsset(ref layers[j]);
                }

                var weightMaskDefinitionIndices = new Dictionary<AvatarMask, int>();
                var weightMaskDefinitionResults = new NativeList<BlobAssetReference<MotionClipWeightMaskDefinition>>(Allocator.Temp);
                int numRigs = this.rigs.Length;
                var rigs = blobBuilder.Allocate(ref root.rigs, numRigs);
                for (i = 0; i < numRigs; ++i)
                    this.rigs[i].ToAsset(ref blobBuilder, this.controllers, dataRigs, ref rigs[i], ref weightMaskDefinitionResults, weightMaskDefinitionIndices);

                weightMaskDefinitions = weightMaskDefinitionResults.Length > 0 ? weightMaskDefinitionResults.AsArray().ToArray() : Array.Empty<BlobAssetReference<MotionClipWeightMaskDefinition>>();

                var result = blobBuilder.CreateBlobAssetReference<MeshInstanceAnimatorDefinition>(Allocator.Persistent);

                blobBuilder.Dispose();

                return result;
            }
        }

        [SerializeField, HideInInspector]
        internal byte[] _bytes;
        [SerializeField, HideInInspector]
        internal int _blendTreeCount;
        [SerializeField, HideInInspector]
        internal int _clipCount;
        [SerializeField, HideInInspector]
        internal int _rigRemapTableCount;
        [SerializeField, HideInInspector]
        internal int _controllerDefinitionCount;
        [SerializeField, HideInInspector]
        internal int _weightMaskDefinitionCount;

        private InitType __initType;

        private UnsafeUntypedBlobAssetReference[] __blendTrees;
        private BlobAssetReference<Unity.Animation.Clip>[] __clips;
        private BlobAssetReference<RigRemapTable>[] __rigRemapTables;
        private BlobAssetReference<AnimatorControllerDefinition>[] __controllerDefinitions;
        private BlobAssetReference<MotionClipWeightMaskDefinition>[] __weightMaskDefinitions;
        private BlobAssetReference<MeshInstanceAnimatorDefinition> __definition;

        public override int instanceID => __definition.IsCreated ? __definition.Value.instanceID : 0;

        public BlobAssetReference<MeshInstanceAnimatorDefinition> definition
        {
            get
            {
                Init();

                return __definition;
            }
        }

        public BlobAssetReference<AnimatorControllerDefinition> GetControllerDefinition(int index)
        {
            __InitControllerDefinitions();

            return __controllerDefinitions[index];
        }

        public BlobAssetReference<Unity.Animation.Clip> GetClip(int index)
        {
            __InitClips();

            return __clips[index];
        }

        protected override void _Dispose()
        {
            if (__definition.IsCreated)
            { 
                __definition.Dispose();

                __definition = BlobAssetReference<MeshInstanceAnimatorDefinition>.Null;
            }

            if (__weightMaskDefinitions != null)
            {
                foreach (var weightMaskDefinition in __weightMaskDefinitions)
                {
                    if (weightMaskDefinition.IsCreated)
                        weightMaskDefinition.Dispose();
                }

                __weightMaskDefinitions = null;
            }

            if (__controllerDefinitions != null)
            {
                foreach (var controllerDefinition in __controllerDefinitions)
                {
                    if (controllerDefinition.IsCreated)
                        controllerDefinition.Dispose();
                }

                __controllerDefinitions = null;
            }

            if (__rigRemapTables != null)
            {
                foreach (var rigRemapTable in __rigRemapTables)
                {
                    if (rigRemapTable.IsCreated)
                        rigRemapTable.Dispose();
                }

                __rigRemapTables = null;
            }

            if (__clips != null)
            {
                foreach (var clip in __clips)
                {
                    if (clip.IsCreated)
                        clip.Dispose();
                }

                __clips = null;
            }

            if (__blendTrees != null)
            {
                foreach (var blendTree in __blendTrees)
                    blendTree.Dispose();

                __blendTrees = null;
            }
        }

        protected override void _Destroy()
        {
            SingletonAssetContainerHandle handle;
            handle.instanceID = __definition.Value.instanceID;

            if ((__initType & InitType.BlendTree) == InitType.BlendTree)
            {
                var blendTrees = SingletonAssetContainer<UnsafeUntypedBlobAssetReference>.instance;
                for (int i = 0; i < _clipCount; ++i)
                {
                    handle.index = i;

                    blendTrees.Delete(handle);
                }
            }

            if ((__initType & InitType.Clip) == InitType.Clip)
            {
                var clips = SingletonAssetContainer<BlobAssetReference<Unity.Animation.Clip>>.instance;
                for (int i = 0; i < _clipCount; ++i)
                {
                    handle.index = i;

                    clips.Delete(handle);
                }
            }


            if ((__initType & InitType.RigRemapTable) == InitType.RigRemapTable)
            {
                var rigRemapTables = SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.instance;
                for (int i = 0; i < _rigRemapTableCount; ++i)
                {
                    handle.index = i;

                    rigRemapTables.Delete(handle);
                }
            }

            if ((__initType & InitType.ControllerDefinition) == InitType.ControllerDefinition)
            {
                var controllerDefinitions = SingletonAssetContainer<BlobAssetReference<AnimatorControllerDefinition>>.instance;
                for (int i = 0; i < _controllerDefinitionCount; ++i)
                {
                    handle.index = i;

                    controllerDefinitions.Delete(handle);
                }
            }


            if ((__initType & InitType.WeightMaskDefinition) == InitType.WeightMaskDefinition)
            {
                var weightMaskDefinitions = SingletonAssetContainer<BlobAssetReference<MotionClipWeightMaskDefinition>>.instance;
                for (int i = 0; i < _weightMaskDefinitionCount; ++i)
                {
                    handle.index = i;

                    weightMaskDefinitions.Delete(handle);
                }
            }

            __initType = 0;
        }

        protected override void _Init()
        {
            __InitBlendTrees();
            __InitClips();
            __InitRigRemapTables();
            __InitControllerDefinitions();
            __InitWeightMaskDefinitions();
        }

        private bool __InitBlendTrees()
        {
            if ((__initType & InitType.BlendTree) == InitType.BlendTree)
                return false;

            __initType |= InitType.BlendTree;

            var instance = SingletonAssetContainer<UnsafeUntypedBlobAssetReference>.instance;

            SingletonAssetContainerHandle handle;
            handle.instanceID = __definition.Value.instanceID;

            int numBlendTrees = __blendTrees.Length;
            for (int i = 0; i < numBlendTrees; ++i)
            {
                handle.index = i;

                instance[handle] = __blendTrees[i];
            }

            return true;
        }

        private bool __InitClips()
        {
            if ((__initType & InitType.Clip) == InitType.Clip)
                return false;

            __initType |= InitType.Clip;

            var instance = SingletonAssetContainer<BlobAssetReference<Unity.Animation.Clip>>.instance;

            SingletonAssetContainerHandle handle;
            handle.instanceID = __definition.Value.instanceID;

            int numClips = __clips.Length;
            for (int i = 0; i < numClips; ++i)
            {
                handle.index = i;

                instance[handle] = __clips[i];
            }

            return true;
        }

        private bool __InitRigRemapTables()
        {
            if ((__initType & InitType.RigRemapTable) == InitType.RigRemapTable)
                return false;

            __initType |= InitType.RigRemapTable;

            var instance = SingletonAssetContainer<BlobAssetReference<RigRemapTable>>.instance;

            SingletonAssetContainerHandle handle;
            handle.instanceID = __definition.Value.instanceID;

            int numRigRemapTables = __rigRemapTables.Length;
            for (int i = 0; i < numRigRemapTables; ++i)
            {
                handle.index = i;

                instance[handle] = __rigRemapTables[i];
            }

            return true;
        }

        private bool __InitControllerDefinitions()
        {
            if ((__initType & InitType.ControllerDefinition) == InitType.ControllerDefinition)
                return false;

            __initType |= InitType.ControllerDefinition;

            var instance = SingletonAssetContainer<BlobAssetReference<AnimatorControllerDefinition>>.instance;

            SingletonAssetContainerHandle handle;
            handle.instanceID = __definition.Value.instanceID;

            int numControllerDefinitions = __controllerDefinitions.Length;
            for (int i = 0; i < numControllerDefinitions; ++i)
            {
                handle.index = i;

                instance[handle] = __controllerDefinitions[i];
            }

            return true;
        }

        private bool __InitWeightMaskDefinitions()
        {
            if ((__initType & InitType.WeightMaskDefinition) == InitType.WeightMaskDefinition)
                return false;

            __initType |= InitType.WeightMaskDefinition;

            var instance = SingletonAssetContainer<BlobAssetReference<MotionClipWeightMaskDefinition>>.instance;

            SingletonAssetContainerHandle handle;
            handle.instanceID = __definition.Value.instanceID;

            int numWeightMaskDefinitions = __weightMaskDefinitions.Length;
            for (int i = 0; i < numWeightMaskDefinitions; ++i)
            {
                handle.index = i;

                instance[handle] = __weightMaskDefinitions[i];
            }

            return true;
        }

        unsafe void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (_bytes != null && _bytes.Length > 0)
            {
                if (__definition.IsCreated)
                    __definition.Dispose();

                if (__weightMaskDefinitions != null)
                {
                    foreach (var weightMaskDefinition in __weightMaskDefinitions)
                    {
                        if (weightMaskDefinition.IsCreated)
                            weightMaskDefinition.Dispose();
                    }
                }

                if (__controllerDefinitions != null)
                {
                    foreach(var controllerDefinition in __controllerDefinitions)
                    {
                        if (controllerDefinition.IsCreated)
                            controllerDefinition.Dispose();
                    }
                }

                if (__rigRemapTables != null)
                {
                    foreach (var rigRemapTable in __rigRemapTables)
                    {
                        if (rigRemapTable.IsCreated)
                            rigRemapTable.Dispose();
                    }
                }

                if (__clips != null)
                {
                    foreach (var clip in __clips)
                    {
                        if (clip.IsCreated)
                            clip.Dispose();
                    }
                }

                if (__blendTrees != null)
                {
                    foreach (var blendTree in __blendTrees)
                        blendTree.Dispose();
                }

                fixed (byte* ptr = _bytes)
                {
                    using (var reader = new MemoryBinaryReader(ptr, _bytes.LongLength))
                    {
                        int instanceID = __definition.GetHashCode();

                        __definition = reader.Read<MeshInstanceAnimatorDefinition>();
                        __definition.Value.instanceID = instanceID;

                        __weightMaskDefinitions = new BlobAssetReference<MotionClipWeightMaskDefinition>[_weightMaskDefinitionCount];
                        for (int i = 0; i < _weightMaskDefinitionCount; ++i)
                            __weightMaskDefinitions[i] = reader.Read<MotionClipWeightMaskDefinition>();

                        __controllerDefinitions = new BlobAssetReference<AnimatorControllerDefinition>[_controllerDefinitionCount];
                        for (int i = 0; i < _controllerDefinitionCount; ++i)
                        {
                            __controllerDefinitions[i] = reader.Read<AnimatorControllerDefinition>();

                            __controllerDefinitions[i].Value.instanceID = instanceID;
                        }

                        __rigRemapTables = new BlobAssetReference<RigRemapTable>[_rigRemapTableCount];
                        for (int i = 0; i < _rigRemapTableCount; ++i)
                            __rigRemapTables[i] = reader.Read<RigRemapTable>();

                        __clips = new BlobAssetReference<Unity.Animation.Clip>[_clipCount];
                        for (int i = 0; i < _clipCount; ++i)
                            __clips[i] = reader.Read<Unity.Animation.Clip>();

                        __blendTrees = new UnsafeUntypedBlobAssetReference[_blendTreeCount];
                        for (int i = 0; i < _blendTreeCount; ++i)
                            __blendTrees[i] = reader.ReadUnsafeUntypedBlobAssetReference();
                    }
                }

                _bytes = null;
            }

            __initType = 0;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            if (_bytes != null && _bytes.Length > 0)
                return;

            if (!__definition.IsCreated)
                return;

            using (var writer = new MemoryBinaryWriter())
            {
                writer.Write(__definition);

                _weightMaskDefinitionCount = __weightMaskDefinitions == null ? 0 : __weightMaskDefinitions.Length;
                for (int i = 0; i < _weightMaskDefinitionCount; ++i)
                    writer.Write(__weightMaskDefinitions[i]);

                _controllerDefinitionCount = __controllerDefinitions == null ? 0 : __controllerDefinitions.Length;
                for (int i = 0; i < _controllerDefinitionCount; ++i)
                    writer.Write(__controllerDefinitions[i]);

                _rigRemapTableCount = __rigRemapTables == null ? 0 : __rigRemapTables.Length;
                for (int i = 0; i < _rigRemapTableCount; ++i)
                    writer.Write(__rigRemapTables[i]);

                _clipCount = __clips == null ? 0 : __clips.Length;
                for (int i = 0; i < _clipCount; ++i)
                    writer.Write(__clips[i]);

                _blendTreeCount = __blendTrees == null ? 0 : __blendTrees.Length;
                for (int i = 0; i < _blendTreeCount; ++i)
                    writer.Write(__blendTrees[i]);

                _bytes = writer.GetContentAsNativeArray().ToArray();
            }
        }


#if UNITY_EDITOR
        [HideInInspector]
        public Transform root;

        public MeshInstanceRigDatabase rigDatabase;

        public Data data;

        [SerializeField]
        internal MeshInstanceClipDatabase.Data _clipData;

        public static AnimatorController GetAnimatorController(RuntimeAnimatorController runtimeAnimatorController)
        {
            if (runtimeAnimatorController == null)
                return null;

            string animmatorControllerAssetPath = AssetDatabase.GetAssetPath(runtimeAnimatorController);
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(animmatorControllerAssetPath);
        }

        public void Create(MeshInstanceClipDatabase.AnimationFlag animationFlag = MeshInstanceClipDatabase.AnimationFlag.InPlace)
        {
            var rigIndices = MeshInstanceRigDatabase.CreateComponentRigIndices(root.root.gameObject);

            var animators = root.GetComponentsInChildren<Animator>();
            var animations = new List<BlobAssetReference<Unity.Animation.Clip>>();
            var animationClipIndices = new Dictionary<MeshInstanceClipDatabase.AnimationData, int>();

            BlobAssetReference<MeshInstanceClipDefinition> clipDefinition;
            {
                int i, numAnimations;
                MeshInstanceClipDatabase.RigData rig;
                AnimationClip[] animationClips;
                var rigs = new List<MeshInstanceClipDatabase.RigData>();
                foreach (var animator in animators)
                {
                    if (!rigIndices.TryGetValue(animator, out rig.index))
                        continue;

                    animationClips = animator.runtimeAnimatorController.animationClips;

                    numAnimations = animationClips.Length;

                    rig.defaultAnimationIndex = -1;
                    rig.rootBoneName = "Hips";
                    rig.animations = new MeshInstanceClipDatabase.AnimationData[numAnimations];

                    for (i = 0; i < numAnimations; ++i)
                    {
                        ref var animation = ref rig.animations[i];

                        animation.flag = animationFlag;
                        animation.clip = animationClips[i];
                    }

                    rigs.Add(rig);
                }

                MeshInstanceClipDatabase.Create(
                    this, 
                    rigs.ToArray(),
                    rigDatabase.materialPropertySettings == null ? null : rigDatabase.materialPropertySettings.Override,
                    animations, 
                    animationClipIndices, 
                    ref rigDatabase.data.rigs, 
                    ref _clipData);

                var rigRemapTables = new NativeList<BlobAssetReference<RigRemapTable>>(Allocator.Temp);
                _clipData.ToAsset(GetInstanceID(), rigDatabase.data.rigs, ref rigRemapTables, out var factory, out clipDefinition);

                if (factory.IsCreated)
                    factory.Dispose();

                _rigRemapTableCount = rigRemapTables.Length;

                __rigRemapTables = new BlobAssetReference<RigRemapTable>[_rigRemapTableCount];
                for (i = 0; i < _rigRemapTableCount; ++i)
                    __rigRemapTables[i] = rigRemapTables[i];

                rigRemapTables.Dispose();
            }

            __clips = animations.ToArray();

            _clipCount = __clips.Length;

            {
                var rigs = new List<Rig>();
                var motionIndices = new Dictionary<UnityEngine.Motion, int>();
                var clipIndices = new Dictionary<int, int>();
                var remapIndices = new Dictionary<int, int>();
                var remaps = new List<Remap>();
                var clips = new List<Clip>();
                var motions = new List<Motion>();
                var blendTrees = new List<UnsafeUntypedBlobAssetReference>();
                var states = new List<AnimatorState>();
                var authoringStateMachines = new List<AnimatorStateMachine>();
                var layerStateMachines = new List<StateMachine>();
                var layerStateMachineIndices = new Dictionary<AnimatorStateMachine, int>();
                var stateMachines = new Dictionary<AnimatorState, AnimatorStateMachine>();
                var sourceStates = new Dictionary<AnimatorTransitionBase, AnimatorState>();
                var parents = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
                var transitionStack = new Stack<AnimatorTransitionBase>();
                var transitions = new List<Transition>();
                var controllers = new List<Controller>();
                var controllerIndices = new Dictionary<AnimatorController, int>();
                UnityEngine.AnimatorControllerParameter[] parameters;
                AnimatorControllerLayer[] layers;
                AnimatorStateMachine sourceLayerStateMachine, stateMachine;
                AnimatorController animatorController;
                Controller controller;
                StateMachine destinationLayerStateMachine;
                UnityEngine.Motion motion;
                AnimatorState sourceState;
                Rig rig;
                ref var clipDefinitionValue = ref clipDefinition.Value;
                int transitionStackConditionCount = 0, numParameters, numLayers, numStates, i, j;
                foreach (var animator in animators)
                {
                    if (!rigIndices.TryGetValue(animator, out rig.index))
                        continue;

                    rig.name = animator.name;
                    rig.rootBonePath = _clipData.rigs[rig.index].rootBonePath;

                    animatorController = GetAnimatorController(animator.runtimeAnimatorController);
                    if (animatorController == null)
                        rig.controllerIndex = -1;
                    else if(!controllerIndices.TryGetValue(animatorController, out rig.controllerIndex))
                    {
                        controller.name = animatorController.name;

                        parameters = animatorController.parameters;
                        numParameters = parameters.Length;

                        controller.parameters = new Parameter[numParameters];
                        for(i = 0; i < numParameters; ++i)
                        {
                            ref readonly var sourceParameter = ref parameters[i];
                            ref var destinationParamter = ref controller.parameters[i];

                            destinationParamter.name = sourceParameter.name;

                            switch (sourceParameter.type)
                            {
                                case UnityEngine.AnimatorControllerParameterType.Float:
                                    destinationParamter.type = AnimatorControllerParameterType.Float;
                                    destinationParamter.defaultValue = math.asint(sourceParameter.defaultFloat);
                                    break;
                                case UnityEngine.AnimatorControllerParameterType.Int:
                                    destinationParamter.type = AnimatorControllerParameterType.Int;
                                    destinationParamter.defaultValue = sourceParameter.defaultInt;
                                    break;
                                case UnityEngine.AnimatorControllerParameterType.Bool:
                                    destinationParamter.type = AnimatorControllerParameterType.Bool;
                                    destinationParamter.defaultValue = sourceParameter.defaultBool ? 1 : 0;
                                    break;
                                case UnityEngine.AnimatorControllerParameterType.Trigger:
                                    destinationParamter.type = AnimatorControllerParameterType.Trigger;
                                    destinationParamter.defaultValue = sourceParameter.defaultBool ? 1 : 0;
                                    break;
                            }
                        }

                        layers = animatorController.layers;
                        numLayers = layers.Length;
                        controller.layers = new Layer[numLayers];

                        motionIndices.Clear();
                        clipIndices.Clear();
                        remapIndices.Clear();
                        remaps.Clear();
                        clips.Clear();
                        motions.Clear();
                        //blendTrees.Clear();
                        for (i = 0; i < numLayers; ++i)
                        {
                            ref readonly var sourceLayer = ref layers[i];
                            ref var destinationLayer = ref controller.layers[i];

                            destinationLayer.name = sourceLayer.name;

                            destinationLayer.blendingMode = (MotionClipBlendingMode)sourceLayer.blendingMode;
                            destinationLayer.defaultWeight = sourceLayer.defaultWeight;
                            destinationLayer.avatarMask = sourceLayer.avatarMask;

                            sourceLayerStateMachine = sourceLayer.syncedLayerIndex == -1 ? sourceLayer.stateMachine : layers[sourceLayer.syncedLayerIndex].stateMachine;
                            states.Clear();
                            __GetStatesRecursive(states, sourceLayerStateMachine);

                            numStates = states.Count;

                            if (!layerStateMachineIndices.TryGetValue(sourceLayerStateMachine, out destinationLayer.stateMachineIndex))
                            {
                                destinationLayer.stateMachineIndex = layerStateMachines.Count;

                                layerStateMachineIndices[sourceLayerStateMachine] = destinationLayer.stateMachineIndex;

                                destinationLayerStateMachine.name = sourceLayerStateMachine.name;

                                destinationLayerStateMachine.initStateIndex = states.IndexOf(sourceLayerStateMachine.defaultState);
                                /*if (destinationLayer.initStateIndex == -1)
                                    continue;*/

                                //stateMachines.Clear();
                                //sourceStates.Clear();
                                //parents.Clear();
                                __CreateRelationshipsRecursive(
                                    stateMachines,
                                    sourceStates,
                                    parents,
                                    sourceLayerStateMachine);

                                destinationLayerStateMachine.states = new State[numStates];
                                for (j = 0; j < numStates; ++j)
                                {
                                    sourceState = states[j];
                                    ref var destinationState = ref destinationLayerStateMachine.states[j];

                                    destinationState.name = sourceState.name;

                                    motion = sourceState.motion;
                                    if(motion == null)
                                        motion = sourceLayer.GetOverrideMotion(sourceState);

                                    if (motion == null)
                                    {
                                        destinationState.wrapMode = MotionClipWrapMode.Pause;
                                        destinationState.motionAverageLength = 0.0f;
                                    }
                                    else
                                    {
                                        destinationState.wrapMode = motion.isLooping ? MotionClipWrapMode.Loop : MotionClipWrapMode.Normal;
                                        destinationState.motionAverageLength = motion.averageDuration;
                                    }

                                    /*var motion = sourceLayer.syncedLayerIndex == -1 ? sourceState.motion : sourceLayer.GetOverrideMotion(sourceState);

                                    destinationState.motionIndex = __CreateMotionRecursive(
                                        animationFlag,
                                        _clipData,
                                        ref clipDefinitionValue,
                                        motion,
                                        animationClipIndices,
                                        motionIndices,
                                        clipIndices,
                                        remapIndices,
                                        remaps,
                                        clips,
                                        motions,
                                        blendTrees);*/
                                    destinationState.speed = sourceState.speed;

                                    destinationState.speedMultiplierParameterIndex = -1;
                                    if (sourceState.speedParameterActive)
                                    {
                                        for (i = 0; i < numParameters; ++i)
                                        {
                                            ref var parameter = ref parameters[i];
                                            if (parameter.name == sourceState.speedParameter)
                                            {
                                                destinationState.speedMultiplierParameterIndex = i;

                                                break;
                                            }
                                        }
                                    }

                                    stateMachine = stateMachines[sourceState];
                                    //numTransitions = __CalculateTransitionCountRecursive(sourceState.transitions, stateMachines, sourceStates, parents);
                                    transitions.Clear();
                                    foreach (var transition in sourceState.transitions)
                                        __CreateTransitionRecursive(
                                            ref transitionStackConditionCount,
                                            transition,
                                            stateMachine,
                                            transitionStack,
                                            transitions,
                                            states,
                                            stateMachines,
                                            //sourceStates,
                                            parents,
                                            parameters);

                                    destinationState.transitions = transitions.ToArray();
                                }

                                authoringStateMachines.Clear();
                                __GetStateMachinesRecursive(authoringStateMachines, sourceLayerStateMachine);
                                //var authoringAnyStateTransitions = authoringStateMachines.SelectMany(stateMachine => stateMachine.anyStateTransitions);

                                transitions.Clear();
                                foreach (var authoringStateMachine in authoringStateMachines)
                                {
                                    foreach (var authoringAnyStateTransition in authoringStateMachine.anyStateTransitions)
                                        __CreateTransitionRecursive(
                                                ref transitionStackConditionCount,
                                                authoringAnyStateTransition,
                                                authoringStateMachine,
                                                transitionStack,
                                                transitions,
                                                states,
                                                stateMachines,
                                                //sourceStates,
                                                parents,
                                                parameters);
                                }

                                destinationLayerStateMachine.globalTransitions = transitions.ToArray();

                                layerStateMachines.Add(destinationLayerStateMachine);
                            }

                            destinationLayer.stateMotionIndices = new int[numStates];
                            for (j = 0; j < numStates; ++j)
                            {
                                sourceState = states[j];

                                motion = sourceLayer.syncedLayerIndex == -1 ? sourceState.motion : sourceLayer.GetOverrideMotion(sourceState);

                                destinationLayer.stateMotionIndices[j] = __CreateMotionRecursive(
                                    animationFlag,
                                    _clipData,
                                    ref clipDefinitionValue,
                                    motion,
                                    animationClipIndices,
                                    motionIndices,
                                    clipIndices,
                                    remapIndices,
                                    remaps,
                                    clips,
                                    motions,
                                    blendTrees);
                            }
                        }

                        controller.stateMachines = layerStateMachines.ToArray();
                        controller.motions = motions.ToArray();
                        controller.clips = clips.ToArray();
                        controller.remaps = remaps.ToArray();

                        rig.controllerIndex = controllers.Count;

                        controllers.Add(controller);

                        controllerIndices[animatorController] = rig.controllerIndex;
                    }

                    rigs.Add(rig);
                }

                data.controllers = controllers.ToArray();
                data.rigs = rigs.ToArray();

                __blendTrees = blendTrees.ToArray();

                _blendTreeCount = __blendTrees.Length;
            }

            clipDefinition.Dispose();
        }

        public void Rebuild()
        {
            /*int instanceID = GetInstanceID();

            var rigRemapTables = new NativeList<BlobAssetReference<RigRemapTable>>(Allocator.Temp);
            _clipData.ToAsset(instanceID, rigDatabase.data.rigs, ref rigRemapTables, out var factory, out var definition);

            if(factory.IsCreated)
                factory.Dispose();

            if(definition.IsCreated)
                definition.Dispose();

            _rigRemapTableCount = rigRemapTables.Length;

            __rigRemapTables = new BlobAssetReference<RigRemapTable>[_rigRemapTableCount];
            for (int i = 0; i < _rigRemapTableCount; ++i)
                __rigRemapTables[i] = rigRemapTables[i];

            rigRemapTables.Dispose();*/

            if(__controllerDefinitions != null)
            {
                foreach (var controllerDefinition in __controllerDefinitions)
                    controllerDefinition.Dispose();
            }

            if(__weightMaskDefinitions != null)
            {
                foreach (var weightMaskDefinition in __weightMaskDefinitions)
                    weightMaskDefinition.Dispose();
            }

            __definition = data.ToAsset(GetInstanceID(), rigDatabase.data.rigs, out __controllerDefinitions, out __weightMaskDefinitions);

            _controllerDefinitionCount = __controllerDefinitions.Length;
            _weightMaskDefinitionCount = __weightMaskDefinitions.Length;

            _bytes = null;

            ((ISerializationCallbackReceiver)this).OnBeforeSerialize();
        }

        public void EditorMaskDirty()
        {
            rigDatabase.EditorMaskDirty();

            Rebuild();

            EditorUtility.SetDirty(this);
        }

        private static int __CreateMotionRecursive(
            MeshInstanceClipDatabase.AnimationFlag animationFlag,
            in MeshInstanceClipDatabase.Data clipData,
            ref MeshInstanceClipDefinition clipDefinition,
            UnityEngine.Motion value,
            Dictionary<MeshInstanceClipDatabase.AnimationData, int> animationClipIndices,
            Dictionary<UnityEngine.Motion, int> motionIndices,
            Dictionary<int, int> clipIndices,
            Dictionary<int, int> remapIndices,
            List<Remap> outRemaps,
            List<Clip> outClips,
            List<Motion> outMotions,
            List<UnsafeUntypedBlobAssetReference> outBlendTrees)
        {
            if (value == null)
                return -1;

            if (motionIndices.TryGetValue(value, out int index))
                return index;

            Motion motion;
            motion.name = value.name;
            //motion.wrapMode = value.isLooping ? MotionClipWrapMode.Loop : MotionClipWrapMode.Normal;
            //motion.averageLength = value.averageDuration;
            if (value is AnimationClip)
            {
                MeshInstanceClipDatabase.AnimationData animation;
                animation.flag = animationFlag;
                animation.clip = (AnimationClip)value;

                if (animationClipIndices.TryGetValue(animation, out int clipIndex))
                {
                    ref readonly var sourceClipData = ref clipData.clips[clipIndex];

                    ref var sourceClip = ref clipDefinition.clips[clipIndex];

                    if (!clipIndices.TryGetValue(clipIndex, out motion.index))
                    {
                        motion.index = outClips.Count;

                        Clip destinationClip;
                        destinationClip.name = sourceClipData.name;
                        destinationClip.wrapMode = value.isLooping ? MotionClipWrapMode.Loop : MotionClipWrapMode.Normal;
                        destinationClip.animationIndex = sourceClip.animationIndex;
                        destinationClip.flag = 0;

                        if ((sourceClip.flag & MeshInstanceClipFlag.InPlace) == MeshInstanceClipFlag.InPlace)
                            destinationClip.flag |= MotionClipFlag.InPlace;

                        if ((sourceClip.flag & MeshInstanceClipFlag.Mirror) == MeshInstanceClipFlag.Mirror)
                            destinationClip.flag |= MotionClipFlag.Mirror;

                        int sourceRemapIndex, destinationRemapIndex, numRemapIndices = sourceClip.remapIndices.Length;
                        destinationClip.remapIndices = new int[numRemapIndices];
                        for (int i = 0; i < numRemapIndices; ++i)
                        {
                            sourceRemapIndex = sourceClip.remapIndices[i];
                            if (!remapIndices.TryGetValue(sourceRemapIndex, out destinationRemapIndex))
                            {
                                destinationRemapIndex = outRemaps.Count;

                                ref var sourceRemap = ref clipDefinition.remaps[sourceRemapIndex];
                                Remap destinationRemap;
                                destinationRemap.index = sourceRemapIndex;
                                destinationRemap.sourceRigIndex = sourceRemap.sourceRigIndex;
                                destinationRemap.destinationRigIndex = sourceRemap.destinationRigIndex;

                                outRemaps.Add(destinationRemap);

                                remapIndices[sourceRemapIndex] = destinationRemapIndex;
                            }

                            destinationClip.remapIndices[i] = destinationRemapIndex;
                        }

                        outClips.Add(destinationClip);

                        clipIndices[clipIndex] = motion.index;
                    }

                    motion.type = AnimatorControllerMotionType.None;
                    motion.childIndices = null;

                    index = outMotions.Count;

                    outMotions.Add(motion);

                    motionIndices[value] = index;

                    return index;
                }
                else
                    UnityEngine.Debug.LogError($"Animation Clip {value.name} has not been found.", value);
            }
            else if (value is BlendTree)
            {
                motion.index = outBlendTrees.Count;

                var blendTree = (BlendTree)value;
                var children = blendTree.children;
                ChildMotion child;
                int numChildren = children.Length;
                switch (blendTree.blendType)
                {
                    case BlendTreeType.Simple1D:
                        using (var blobBuilder = new BlobBuilder(Allocator.Temp))
                        {
                            ref var root = ref blobBuilder.ConstructRoot<AnimatorControllerBlendTree1DSimple>();

                            root.blendParameter = blendTree.blendParameter;

                            var motionSpeeds = blobBuilder.Allocate(ref root.motionSpeeds, numChildren);
                            var motionThresholds = blobBuilder.Allocate(ref root.motionThresholds, numChildren);

                            for (int i = 0; i < numChildren; ++i)
                            {
                                child = children[i];

                                motionSpeeds[i] = child.timeScale;
                                motionThresholds[i] = child.threshold;
                            }

                            outBlendTrees.Add(UnsafeUntypedBlobAssetReference.Create(blobBuilder.CreateBlobAssetReference<AnimatorControllerBlendTree1DSimple>(Allocator.Persistent)));
                        }

                        motion.childIndices = new int[numChildren];

                        for (int i = 0; i < numChildren; ++i)
                        {
                            child = children[i];

                            motion.childIndices[i] = __CreateMotionRecursive(
                                animationFlag,
                                clipData,
                                ref clipDefinition,
                                child.motion,
                                animationClipIndices,
                                motionIndices,
                                clipIndices,
                                remapIndices,
                                outRemaps,
                                outClips,
                                outMotions,
                                outBlendTrees);
                        }

                        motion.type = AnimatorControllerMotionType.Simple1D;

                        index = outMotions.Count;

                        motionIndices[value] = index;

                        outMotions.Add(motion);

                        return index;
                    case BlendTreeType.SimpleDirectional2D:
                        using (var blobBuilder = new BlobBuilder(Allocator.Temp))
                        {
                            ref var root = ref blobBuilder.ConstructRoot<AnimatorControllerBlendTree2DSimpleDirectional>();

                            root.blendParameterX = blendTree.blendParameter;
                            root.blendParameterY = blendTree.blendParameterY;

                            var motionSpeeds = blobBuilder.Allocate(ref root.motionSpeeds, numChildren);
                            var motionPositions = blobBuilder.Allocate(ref root.motionPositions, numChildren);

                            for (int i = 0; i < numChildren; ++i)
                            {
                                child = children[i];

                                motionSpeeds[i] = child.timeScale;
                                motionPositions[i] = child.position;
                            }

                            outBlendTrees.Add(UnsafeUntypedBlobAssetReference.Create(blobBuilder.CreateBlobAssetReference<AnimatorControllerBlendTree2DSimpleDirectional>(Allocator.Persistent)));
                        }

                        motion.childIndices = new int[numChildren];

                        for (int i = 0; i < numChildren; ++i)
                        {
                            child = children[i];

                            motion.childIndices[i] = __CreateMotionRecursive(
                                animationFlag,
                                clipData,
                                ref clipDefinition,
                                child.motion,
                                animationClipIndices,
                                motionIndices,
                                clipIndices,
                                remapIndices,
                                outRemaps,
                                outClips,
                                outMotions,
                                outBlendTrees);
                        }

                        motion.type = AnimatorControllerMotionType.SimpleDirectional2D;

                        index = outMotions.Count;

                        outMotions.Add(motion);

                        motionIndices[value] = index;

                        return index;
                    case BlendTreeType.FreeformCartesian2D:
                        using (var blobBuilder = new BlobBuilder(Allocator.Temp))
                        {
                            ref var root = ref blobBuilder.ConstructRoot<AnimatorControllerBlendTree2DFreeformCartesian>();

                            root.blendParameterX = blendTree.blendParameter;
                            root.blendParameterY = blendTree.blendParameterY;

                            var motionSpeeds = blobBuilder.Allocate(ref root.motionSpeeds, numChildren);
                            var motionPositions = blobBuilder.Allocate(ref root.motionPositions, numChildren);

                            for (int i = 0; i < numChildren; ++i)
                            {
                                child = children[i];

                                motionSpeeds[i] = child.timeScale;
                                motionPositions[i] = child.position;
                            }

                            outBlendTrees.Add(UnsafeUntypedBlobAssetReference.Create(blobBuilder.CreateBlobAssetReference<AnimatorControllerBlendTree2DFreeformCartesian>(Allocator.Persistent)));
                        }

                        motion.childIndices = new int[numChildren];

                        for (int i = 0; i < numChildren; ++i)
                        {
                            child = children[i];

                            motion.childIndices[i] = __CreateMotionRecursive(
                                animationFlag,
                                clipData,
                                ref clipDefinition,
                                child.motion,
                                animationClipIndices,
                                motionIndices,
                                clipIndices,
                                remapIndices,
                                outRemaps,
                                outClips,
                                outMotions,
                                outBlendTrees);
                        }

                        motion.type = AnimatorControllerMotionType.FreeformCartesian2D;

                        index = outMotions.Count;

                        outMotions.Add(motion);

                        motionIndices[value] = index;

                        return index;
                    default:
                        UnityEngine.Debug.LogError($"Blend Tree({value.name}) type({blendTree.blendType}) is not supported.", value);
                        break;
                }
            }

            return -1;
        }

        private static void __GetStatesRecursive(List<AnimatorState> outStates, AnimatorStateMachine animatorStateMachine)
        {
            foreach (var childState in animatorStateMachine.states)
                outStates.Add(childState.state);

            foreach (var childStateMachine in animatorStateMachine.stateMachines)
                __GetStatesRecursive(outStates, childStateMachine.stateMachine);
        }

        private static void __GetStateMachinesRecursive(List<AnimatorStateMachine> outAnimatorStateMachines, AnimatorStateMachine animatorStateMachine)
        {
            outAnimatorStateMachines.Add(animatorStateMachine);

            foreach (var childStateMachine in animatorStateMachine.stateMachines)
                __GetStateMachinesRecursive(outAnimatorStateMachines, childStateMachine.stateMachine);
        }

        private static void __CreateRelationshipsRecursive(
            Dictionary<AnimatorState, AnimatorStateMachine> outStateMachines,
            Dictionary<AnimatorTransitionBase, AnimatorState> outSourceStates,
            Dictionary<AnimatorStateMachine, AnimatorStateMachine> outParents,
            AnimatorStateMachine animatorStateMachine)
        {
            foreach (var childState in animatorStateMachine.states)
            {
                outStateMachines[childState.state] = animatorStateMachine;

                foreach (var transition in childState.state.transitions)
                    outSourceStates[transition] = childState.state;
            }

            foreach (var childStateMachine in animatorStateMachine.stateMachines)
            {
                outParents[childStateMachine.stateMachine] = animatorStateMachine;

                __CreateRelationshipsRecursive(
                    outStateMachines,
                    outSourceStates,
                    outParents, 
                    childStateMachine.stateMachine);
            }
        }

        /*private int __CalculateTransitionCountRecursive(
            IEnumerable<AnimatorTransitionBase> authoringTransitions,
            Dictionary<AnimatorState, AnimatorStateMachine> stateMachines,
            Dictionary<AnimatorTransitionBase, AnimatorState> sourceStates,
            Dictionary<AnimatorStateMachine, AnimatorStateMachine> parents)
        {
            int stateTransitionCount = 0;
            foreach (var authoringTransition in authoringTransitions)
                stateTransitionCount += __CalculateTransitionCountRecursive(authoringTransition, stateMachines, sourceStates, parents);

            return stateTransitionCount;
        }

        private int __CalculateTransitionCountRecursive(
            AnimatorTransitionBase authoringTransition,
            Dictionary<AnimatorState, AnimatorStateMachine> stateMachines,
            Dictionary<AnimatorTransitionBase, AnimatorState> sourceStates, 
            Dictionary<AnimatorStateMachine, AnimatorStateMachine> parents)
        {
            if (authoringTransition.mute)
                return 0;

            if (authoringTransition.isExit)
            {
                var state = sourceStates[authoringTransition];
                var stateMachine = stateMachines[state];

                int transitionCount = 0;
                foreach (var exitTransition in parents[stateMachine].GetStateMachineTransitions(stateMachine))
                    transitionCount += __CalculateTransitionCountRecursive(exitTransition, stateMachines, sourceStates, parents);

                return transitionCount;
            }
            else if (authoringTransition.destinationStateMachine)
            {
                int transitionCount = 0;
                foreach (var entryTransition in authoringTransition.destinationStateMachine.entryTransitions)
                    transitionCount += __CalculateTransitionCountRecursive(entryTransition, stateMachines, sourceStates, parents);

                if (authoringTransition.destinationStateMachine.defaultState != null)
                    transitionCount++;

                return transitionCount;
            }
            else if (authoringTransition.destinationState)
            {
                return 1;
            }
            return 0;
        }*/

        private void __CreateTransitionRecursive(
            ref int transitionStackConditionCount,
            AnimatorTransitionBase authoringTransition,
            AnimatorStateMachine stateMachine, 
            Stack<AnimatorTransitionBase> transitionStack,
            List<Transition> transitions,
            List<AnimatorState> states,
            Dictionary<AnimatorState, AnimatorStateMachine> stateMachines,
            //Dictionary<AnimatorTransitionBase, AnimatorState> sourceStates,
            Dictionary<AnimatorStateMachine, AnimatorStateMachine> parents,
            UnityEngine.AnimatorControllerParameter[] parameters)
        {
            if (authoringTransition.mute)
                return;

            transitionStack.Push(authoringTransition);
            transitionStackConditionCount += authoringTransition.conditions.Length;

            if (authoringTransition.isExit)
            {
                //var state = sourceStates[authoringTransition];
                //var stateMachine = stateMachines[state];
                if (parents.TryGetValue(stateMachine, out var parent))
                {
                    foreach (var exitTransition in parent.GetStateMachineTransitions(stateMachine))
                        __CreateTransitionRecursive(
                            ref transitionStackConditionCount,
                            exitTransition,
                            parent,
                            transitionStack,
                            transitions,
                            states,
                            stateMachines,
                            //sourceStates,
                            parents,
                            parameters);
                }
                else
                {
                    var transition = __CreateTransition(
                        transitionStackConditionCount,
                        authoringTransition.destinationStateMachine.defaultState,
                        authoringTransition,
                        transitionStack,
                        states,
                        parameters);

                    transitions.Add(transition);
                }
            }
            else if (authoringTransition.destinationStateMachine != null)
            {
                var destinationStateMachine = authoringTransition.destinationStateMachine;
                foreach (var entryTransition in destinationStateMachine.entryTransitions)
                    __CreateTransitionRecursive(
                        ref transitionStackConditionCount,
                        entryTransition,
                        destinationStateMachine, 
                        transitionStack,
                        transitions,
                        states,
                        stateMachines,
                        //sourceStates,
                        parents,
                        parameters);

                var transition = __CreateTransition(
                    transitionStackConditionCount,
                    authoringTransition.destinationStateMachine.defaultState,
                    authoringTransition,
                    transitionStack, 
                    states,
                    parameters);

                transitions.Add(transition);
            }
            else if (authoringTransition.destinationState != null)
            {
                var transition = __CreateTransition(
                    transitionStackConditionCount, 
                    null, 
                    authoringTransition,
                    transitionStack, 
                    states,
                    parameters);

                transitions.Add(transition);
            }

            var poppedTransition = transitionStack.Pop();
            transitionStackConditionCount -= poppedTransition.conditions.Length;
        }

        private Transition __CreateTransition(
            int transitionStackConditionCount,
            AnimatorState overrideDestinationState,
            AnimatorTransitionBase authoringTransition,
            Stack<AnimatorTransitionBase> transitionStack, 
            List<AnimatorState> states, 
            UnityEngine.AnimatorControllerParameter[] parameters)
        {
            Transition transition;
            transition.name = authoringTransition.name;
            transition.flag = 0;
            transition.interruptionSource = AnimatorControllerInterruptionSource.None;
            transition.duration = 0.0f;
            transition.offset = 0.0f;
            transition.exitTime = 0.0f;

            foreach (var stackTransition in transitionStack)
            {
                if (stackTransition is AnimatorStateTransition stateTransition)
                {
                    transition.flag = 0;
                    if (stateTransition.canTransitionToSelf)
                        transition.flag |= AnimatorControllerTransitionFlag.CanTransitionToSelf;

                    if (stateTransition.hasFixedDuration)
                        transition.flag |= AnimatorControllerTransitionFlag.HasFixedDuration;

                    if (stateTransition.orderedInterruption)
                        transition.flag |= AnimatorControllerTransitionFlag.OrderedInterruption;

                    transition.interruptionSource = (AnimatorControllerInterruptionSource)stateTransition.interruptionSource;

                    transition.duration = stateTransition.duration;
                    transition.offset = stateTransition.offset;
                    transition.exitTime = stateTransition.hasExitTime ? stateTransition.exitTime : 0.0f;

                    break;
                }
            }

            var destinationState = overrideDestinationState ? overrideDestinationState : authoringTransition.destinationState;
            if (destinationState == null)
            {
                transition.name = "Exit";
                transition.destinationStateIndex = -1;
            }
            else
            {
                transition.name = destinationState.name;
                transition.destinationStateIndex = states.IndexOf(destinationState);
            }

            transition.conditions = new Condition[transitionStackConditionCount];
            int conditionIndex = 0, numParameters = parameters.Length, i;
            foreach (var stackTransition in transitionStack)
            {
                foreach (var authoringCondition in stackTransition.conditions)
                {
                    ref var condition = ref transition.conditions[conditionIndex++];

                    switch (authoringCondition.mode)
                    {
                        case AnimatorConditionMode.If:
                            condition.opcode = AnimatorControllerConditionOpcode.If;
                            break;
                        case AnimatorConditionMode.IfNot:
                            condition.opcode = AnimatorControllerConditionOpcode.IfNot;
                            break;
                        case AnimatorConditionMode.Greater:
                            condition.opcode = AnimatorControllerConditionOpcode.Greater;
                            break;
                        case AnimatorConditionMode.Less:
                            condition.opcode = AnimatorControllerConditionOpcode.Less;
                            break;
                        case AnimatorConditionMode.Equals:
                            condition.opcode = AnimatorControllerConditionOpcode.Equals;
                            break;
                        case AnimatorConditionMode.NotEqual:
                            condition.opcode = AnimatorControllerConditionOpcode.NotEqual;
                            break;
                    }

                    condition.parameterIndex = -1;
                    for (i = 0; i < numParameters; ++i)
                    {
                        ref var parameter = ref parameters[i];
                        if (parameter.name == authoringCondition.parameter)
                        {
                            condition.parameterIndex = i;

                            condition.threshold = parameter.type == UnityEngine.AnimatorControllerParameterType.Float ? 
                                math.asint(authoringCondition.threshold) : (int)authoringCondition.threshold;

                            break;
                        }
                    }
                }
            }

            return transition;
        }
#endif
    }
}