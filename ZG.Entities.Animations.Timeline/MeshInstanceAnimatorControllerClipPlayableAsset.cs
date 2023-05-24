using UnityEngine;
using UnityEngine.Playables;

namespace ZG
{
    public class MeshInstanceAnimatorControllerClipPlayableAsset : PlayableAsset
    {
        public int clipIndex;

        public int rigIndex;

        public int animatorControllerIndex;

        public MeshInstanceAnimatorDatabase database;

        public override double duration
        {
            get
            {
                if (database == null)
                    return base.duration;

                return database.GetClip(database.GetControllerDefinition(animatorControllerIndex).Value.clips[clipIndex].index).Value.Duration;
            }
        }

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<MeshInstanceAnimatorControllerClipPlayable>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour._clipIndex = clipIndex;
            behaviour._rigIndex = rigIndex;
            behaviour._definition = database.GetControllerDefinition(animatorControllerIndex);//.Resolve(graph.GetResolver()).GetControllerDefinition(animatorControllerIndex);

            //Debug.LogError(behaviour._definition.Value.clips.Length);
            return playable;
        }

    }
}