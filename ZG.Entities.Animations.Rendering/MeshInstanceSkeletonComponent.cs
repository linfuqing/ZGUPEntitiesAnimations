using System;
using Unity.Entities;
using UnityEngine;

namespace ZG
{
    [EntityComponent(typeof(MeshInstanceSkeletonData))]
    public class MeshInstanceSkeletonComponent : EntityProxyComponent, IEntityComponent
    {
        [SerializeField]
        internal MeshInstanceSkeletonDatabase _database;

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

        public MeshInstanceSkeletonDatabase database => _database;

        /*[EntityComponents]
        public Type[] entityComponentTypesEx
        {
            get
            {
                if (!isActive)
                    return new Type[] { typeof(MeshInstanceSkeletonDisabled) };

                return null;
            }
        }

        protected void OnEnable()
        {
            if (gameObjectEntity.isCreated)
                this.RemoveComponent<MeshInstanceSkeletonDisabled>();
        }

        protected void OnDisable()
        {
            if (gameObjectEntity.isCreated && gameObjectEntity.world.IsCreated)
                this.AddComponent<MeshInstanceSkeletonDisabled>();
        }*/

        void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
        {
            MeshInstanceSkeletonData instance;
            instance.definition = _database.definition;

            assigner.SetComponentData(entity, instance);
        }
    }
}