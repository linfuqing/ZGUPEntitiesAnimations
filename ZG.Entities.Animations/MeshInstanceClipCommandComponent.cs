using Unity.Entities;

namespace ZG
{
    [EntityComponent(typeof(MeshInstanceMotionClipCommand))]
    [EntityComponent(typeof(MeshInstanceMotionClipCommandVersion))]
    public class MeshInstanceClipCommandComponent : EntityProxyComponent, IEntityComponent
    {
        public uint version
        {
            get
            {
                return this.GetComponentData<MeshInstanceMotionClipCommandVersion>().value;
            }
        }

        public void Play(int clipIndex, float speed, float blendTime = 0.3f)
        {
            MeshInstanceMotionClipCommand command;
            command.version = version;
            command.rigIndex = -1;
            command.clipIndex = clipIndex;
            command.speed = speed;
            command.blendTime = blendTime;
            this.SetComponentData(command);
        }

        public void Play(int clipIndex)
        {
            Play(clipIndex, 1.0f);
        }

        public void Pause()
        {
            MeshInstanceMotionClipCommand command;
            command.version = version;
            command.rigIndex = -1;
            command.clipIndex = -1;
            command.blendTime = 0.0f;
            command.speed = 0.0f;
            this.SetComponentData(command);
        }

        void IEntityComponent.Init(in Entity entity, EntityComponentAssigner assigner)
        {
            MeshInstanceMotionClipCommandVersion version;
            version.value = 1;
            assigner.SetComponentData(entity, version);
        }
    }
}