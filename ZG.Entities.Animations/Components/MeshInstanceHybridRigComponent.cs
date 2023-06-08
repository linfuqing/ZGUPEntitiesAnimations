using UnityEngine;

namespace ZG
{
    [EntityComponent(typeof(Transform))]
    [EntityComponent(typeof(MeshInstanceHybridRigData))]
    public class MeshInstanceHybridRigComponent : EntityProxyComponent, IEntityComponent
    {
        [SerializeField]
        internal MeshInstanceHybridRigDatabase _database;

        void IEntityComponent.Init(in Unity.Entities.Entity entity, EntityComponentAssigner assigner)
        {
            string transformPathPrefix = transform.GetPath(gameObjectEntity.transform);
            transformPathPrefix = string.IsNullOrEmpty(transformPathPrefix) ? string.Empty : transformPathPrefix + '/';

            MeshInstanceHybridRigData instance;
            instance.transformPathPrefix = transformPathPrefix;
            instance.definition = _database.definition;

            assigner.SetComponentData(entity, instance);
        }
    }
}