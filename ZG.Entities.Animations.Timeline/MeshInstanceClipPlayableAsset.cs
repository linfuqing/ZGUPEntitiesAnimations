using UnityEngine;
using UnityEngine.Playables;

namespace ZG
{
    public class MeshInstanceClipPlayableAsset : PlayableAsset
    {
        public int clipIndex;

        public int rigIndex;

        public MeshInstanceClipDatabase database;

        public Vector3 scale = Vector3.one;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;

        public override double duration
        {
            get
            {
                if (database == null)
                    return base.duration;

                return database.GetClip(database.factory.Value.rigs[rigIndex].clipIndices[clipIndex]).Value.Duration;
            }
        }

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<MeshInstanceClipPlayable>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour._clipIndex = clipIndex;
            behaviour._rigIndex = rigIndex;
            behaviour._matrix = Matrix4x4.TRS(position, rotation, scale);
            behaviour._factory = database.factory;
            behaviour._definition = database.definition;//.Resolve(graph.GetResolver()).GetControllerDefinition(animatorControllerIndex);

            //Debug.LogError(behaviour._definition.Value.clips.Length);
            return playable;
        }

    }
}