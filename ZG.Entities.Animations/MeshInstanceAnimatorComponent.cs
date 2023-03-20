using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Animation;
using UnityEngine;

namespace ZG
{
    [EntityComponent(typeof(MeshInstanceAnimatorData))]
    [EntityComponent(typeof(MeshInstanceAnimatorParameterCommand))]
    public class MeshInstanceAnimatorComponent : EntityProxyComponent, IEntityComponent, IAnimatorController
    {
        //public const int RIG_PARAMTER_SHIFT = 8;
        //public const int RIG_PARAMTER_MASK = (1 << RIG_PARAMTER_SHIFT) - 1;

        [SerializeField]
        internal MeshInstanceAnimatorDatabase _database;

        public Component instance => this;

        public static void SetInteger(
            int id,
            int value, 
            ref DynamicBuffer<MeshInstanceAnimatorParameterCommand> commands)
        {
            MeshInstanceAnimatorParameterCommand command;
            command.name.Id = (uint)id;
            command.value = value;
            commands.Add(command);

            /*AnimatorControllerParameterCommand command;
            MeshInstanceRig rig;
            int numRigs = rigs.Length;
            bool result = false;
            for (int i = 0; i < numRigs; ++i)
            {
                command.index = id & RIG_PARAMTER_MASK;
                if (command.index > 0)
                {
                    rig = rigs[i];
                    if (parameterCommands.HasBuffer(rig.entity))
                    {
                        --command.index;

                        command.value = value;

                        parameterCommands[rig.entity].Add(command);

                        result = true;
                    }
                }

                id >>= RIG_PARAMTER_SHIFT;
            }

            return result;*/
        }

        public static void SetFloat(
            int id,
            float value,
            ref DynamicBuffer<MeshInstanceAnimatorParameterCommand> commands) => SetInteger(id, math.asint(value), ref commands);

        public static void SetBool(
            int id,
            bool value,
            ref DynamicBuffer<MeshInstanceAnimatorParameterCommand> commands) => SetInteger(id, value ? 1 : 0, ref commands);

        public static void SetTrigger(
            int id,
            ref DynamicBuffer<MeshInstanceAnimatorParameterCommand> commands) => SetInteger(id, 1, ref commands);

        public static void ResetTrigger(
            int id,
            ref DynamicBuffer<MeshInstanceAnimatorParameterCommand> commands) => SetInteger(id, 0, ref commands);

        public int GetParameterID(string name)
        {
            /*StringHash parameterName = name;

            ref var definition = ref _database.definition.Value;
            int id = 0, numRigs = definition.rigs.Length, paramterIndex;
            for(int i = 0; i < numRigs; ++i)
            {
                ref var rig = ref definition.rigs[i];
                ref var controller = ref _database.GetControllerDefinition(rig.controllerIndex).Value;
                paramterIndex = AnimatorControllerDefinition.Parameter.IndexOf(parameterName, ref controller.parameters);
                if(paramterIndex >= 0 && paramterIndex < RIG_PARAMTER_MASK)
                    id |= (paramterIndex + 1) << (RIG_PARAMTER_SHIFT * rig.index);
            }

            return id;*/

            return (int)((StringHash)name).Id;
        }

        public int GetInteger(int id)
        {
            if (!gameObjectEntity.isCreated)
                return 0;

            StringHash name;
            name.Id = (uint)id;

            if (this.TryGetBuffer(_database.definition.Value.rigs[0].index, out MeshInstanceRig rig) &&
                this.TryGetComponentData(rig.entity, out AnimatorControllerData instance))
            {
                var index = AnimatorControllerDefinition.Parameter.IndexOf(name, ref instance.definition.Value.parameters);
                if(index != -1 && this.TryGetBuffer(rig.entity, index, out AnimatorControllerParameter paramter))
                    return paramter.value;
            }

            return 0;
        }

        public float GetFloat(int id)
        {
            return math.asfloat(GetInteger(id));
        }

        public void SetInteger(int id, int value)
        {
            var gameObjectEntity = base.gameObjectEntity;
            if (!gameObjectEntity.isCreated)
                return;

            MeshInstanceAnimatorParameterCommand command;
            command.name.Id = (uint)id;
            command.value = value;
            gameObjectEntity.AppendBuffer(command);

            gameObjectEntity.SetComponentEnabled<MeshInstanceAnimatorParameterCommand>(true);

            /*AnimatorControllerParameterCommand command;
            ref var definition = ref _database.definition.Value;
            int numRigs = definition.rigs.Length, rigIndex;
            for (int i = 0; i < numRigs; ++i)
            {
                rigIndex = _database.definition.Value.rigs[i].index;
                command.index = (id >> (rigIndex * RIG_PARAMTER_SHIFT)) & RIG_PARAMTER_MASK;
                if (command.index > 0)
                {
                    if(this.TryGetBuffer(rigIndex, out MeshInstanceRig rig))
                    {
                        --command.index;

                        command.value = value;

                        this.AppendBuffer(rig.entity, command);
                    }
                }
            }*/
        }

        public void SetFloat(int id, float value)
        {
            SetInteger(id, math.asint(value));
        }

        public void SetBool(int id, bool value)
        {
            SetInteger(id, value ? 1 : 0);
        }

        public void SetTrigger(int id)
        {
            SetInteger(id, 1);
        }

        public void ResetTrigger(int id)
        {
            SetInteger(id, 0);
        }

        public void SetTrigger(string name)
        {
            SetTrigger(GetParameterID(name));
        }

        private static List<MotionClipLayer> __layers = new List<MotionClipLayer>();

        public void SetLayerWeight(int layerIndex, float weight)
        {
            WriteOnlyListWrapper<MotionClipLayer, List<MotionClipLayer>> listWrapper;
            MotionClipLayer layer;
            ref var definition = ref _database.definition.Value;
            int numRigs = definition.rigs.Length, rigIndex;
            for (int i = 0; i < numRigs; ++i)
            {
                rigIndex = _database.definition.Value.rigs[i].index;
                if (this.TryGetBuffer(rigIndex, out MeshInstanceRig rig))
                {
                    __layers.Clear();

                    if (this.TryGetBuffer<MotionClipLayer, List<MotionClipLayer>, WriteOnlyListWrapper<MotionClipLayer, List<MotionClipLayer>>>(rig.entity, ref __layers, ref listWrapper) &&
                        __layers.Count > layerIndex)
                    {
                        layer = __layers[layerIndex];
                        layer.weight = weight;
                        __layers[layerIndex] = layer;

                        this.SetBuffer<MotionClipLayer, List<MotionClipLayer>>(rig.entity, __layers);
                    }
                }
            }
        }

        public void RestoreValues()
        {

        }

        public void PlaybackValues()
        {

        }

/*#if UNITY_EDITOR
        private static bool __isTest = true;
        public void Start()
        {
            //if(__isTest && transform.root.name.Contains("RZ"))
            {
                Tests.TestRemap(ref _database.definition.Value, GetComponentInParent<MeshInstanceRigComponent>().database.definition.Value.instanceID);

                __isTest = false;
            }
        }
#endif*/

        void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
        {
            MeshInstanceAnimatorData instance;
            instance.definition = _database.definition;

            assigner.SetComponentData(entity, instance);
        }
    }
}