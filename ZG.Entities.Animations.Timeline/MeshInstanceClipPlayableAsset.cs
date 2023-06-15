using UnityEngine;
using UnityEngine.Playables;

namespace ZG
{
    public class MeshInstanceClipPlayableAsset : PlayableAsset
    {
        public bool useOriginTransform;

        public int clipIndex;

        public int rigIndex;

        public float speed = 1.0f;

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
            behaviour._duration = (float)duration;
            behaviour._speed = speed;
            behaviour._matrix = useOriginTransform ? default : Matrix4x4.TRS(position, rotation, scale);
            behaviour._factory = database.factory;
            behaviour._definition = database.definition;//.Resolve(graph.GetResolver()).GetControllerDefinition(animatorControllerIndex);

            //Debug.LogError(behaviour._definition.Value.clips.Length);
            return playable;
        }

    }
}