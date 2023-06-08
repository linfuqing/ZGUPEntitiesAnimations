using Unity.Entities;

namespace ZG
{
    [EntityComponent(typeof(MeshInstanceClipCommand))]
    public class MeshInstanceClipCommandComponent : EntityProxyComponent
    {
        public void Play(int clipIndex, float speed, float blendTime = 0.3f)
        {
            MeshInstanceClipCommand command;
            command.rigIndex = -1;
            command.clipIndex = clipIndex;
            command.speed = speed;
            command.blendTime = blendTime;
            this.AppendBuffer(command);
            this.SetComponentEnabled<MeshInstanceClipCommand>(true);
        }

        public void Play(int clipIndex)
        {
            Play(clipIndex, 1.0f);
        }

        public void Pause()
        {
            MeshInstanceClipCommand command;
            command.rigIndex = -1;
            command.clipIndex = -1;
            command.blendTime = 0.0f;
            command.speed = 0.0f;
            this.AppendBuffer(command);
            this.SetComponentEnabled<MeshInstanceClipCommand>(true);
        }
    }
}