using UnityEngine;

namespace ZG
{
    [EntityComponent(typeof(MeshInstanceClipData))]
    [EntityComponent(typeof(MeshInstanceMotionClipFactoryData))]
    public class MeshInstanceClipComponent : EntityProxyComponent, IEntityComponent
    {
        [SerializeField]
        internal MeshInstanceClipDatabase _database;

        void IEntityComponent.Init(in Unity.Entities.Entity entity, EntityComponentAssigner assigner)
        {
            MeshInstanceClipData instance;
            instance.definition = _database.definition;
            assigner.SetComponentData(entity, instance);

            MeshInstanceMotionClipFactoryData factory;
            factory.definition = _database.factory;
            assigner.SetComponentData(entity, factory);
        }
    }
}