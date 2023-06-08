using UnityEngine;

namespace ZG
{
    [EntityComponent(typeof(MeshInstanceAnimationMaterialData))]
    public class MeshInstanceAnimationMaterialComponent : MonoBehaviour, IEntityComponent
    {
        [SerializeField]
        internal MeshInstanceAnimationMaterialDatabase _database;

        void IEntityComponent.Init(in Unity.Entities.Entity entity, EntityComponentAssigner assigner)
        {
            MeshInstanceAnimationMaterialData instance;
            instance.definition = _database.definition;
            assigner.SetComponentData(entity, instance);
        }
    }
}