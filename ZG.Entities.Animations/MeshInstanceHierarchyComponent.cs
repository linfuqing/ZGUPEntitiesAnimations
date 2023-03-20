using System;
using Unity.Entities;
using UnityEngine;

namespace ZG
{
    [EntityComponent(typeof(MeshInstanceHierarchyData))]
    public class MeshInstanceHierarchyComponent : EntityProxyComponent, IEntityComponent, IEntityRuntimeComponentDefinition
    {
        [SerializeField]
        internal MeshInstanceHierarchyDatabase _database;

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
                    return new Type[] { typeof(MeshInstanceHierarchyDisabled) };

                return null;
            }
        }*/

        public ComponentType[] runtimeComponentTypes
        {
            get
            {
                if (!isActive)
                    return new ComponentType[] { ComponentType.ReadOnly<MeshInstanceHierarchyDisabled>() };

                return null;
            }
        }

        protected void OnEnable()
        {
            if (gameObjectEntity.isCreated)
                this.RemoveComponent<MeshInstanceHierarchyDisabled>();
        }

        protected void OnDisable()
        {
            if (gameObjectEntity.isAssigned)
                this.AddComponent<MeshInstanceHierarchyDisabled>();
        }

        void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
        {
            MeshInstanceHierarchyData instance;
            instance.definition = _database.definition;
            assigner.SetComponentData(entity, instance);
        }
    }
}