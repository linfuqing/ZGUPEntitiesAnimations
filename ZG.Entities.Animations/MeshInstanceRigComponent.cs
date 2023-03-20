using System;
using Unity.Entities;
using UnityEngine;

namespace ZG
{
    [EntityComponent(typeof(MeshInstanceRigData))]
    public class MeshInstanceRigComponent : EntityProxyComponent, IEntityComponent, IEntityRuntimeComponentDefinition
    {
        [SerializeField]
        internal MeshInstanceRigDatabase _database;

        public MeshInstanceRigDatabase database => _database;

        public bool isActive
        {
            get
            {
                if (enabled)
                {
                    var transform = base.transform;
                    while (transform.gameObject.activeSelf)
                    {
                        transform = transform.parent;
                        if (transform == null)
                            return true;
                    }
                }

                return false;
            }
        }

        /*[EntityComponents]
        public Type[] entityComponentTypesEx
        {
            get
            {
                if (!isActive)
                    return new Type[] { typeof(MeshInstanceRigDisabled) };

                return null;
            }
        }*/

        public ComponentType[] runtimeComponentTypes
        {
            get
            {
                if (!isActive)
                    return new ComponentType[] { ComponentType.ReadOnly<MeshInstanceRigDisabled>() };

                return null;
            }
        }

        protected void OnEnable()
        {
            if (gameObjectEntity.isCreated)
                this.RemoveComponent<MeshInstanceRigDisabled>();
        }

        protected void OnDisable()
        {
            if (gameObjectEntity.isAssigned)
                this.AddComponent<MeshInstanceRigDisabled>();
        }

        void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
        {
            MeshInstanceRigData instance;
            instance.definition = _database.definition;
            assigner.SetComponentData(entity, instance);
        }
    }
}