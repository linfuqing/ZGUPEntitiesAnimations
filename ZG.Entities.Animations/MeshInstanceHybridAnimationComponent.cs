using Unity.Entities;
using UnityEngine;

namespace ZG
{
    [EntityComponent(typeof(MeshInstanceHybridAnimationData))]
    public class MeshInstanceHybridAnimationComponent : EntityProxyComponent, IEntityComponent
    {
        [SerializeField]
        internal MeshInstanceHybridAnimationDatabase _database;

        void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
        {
            MeshInstanceHybridAnimationData instance;
            instance.definition = _database.definition;
            assigner.SetComponentData(entity, instance);
        }
    }
}