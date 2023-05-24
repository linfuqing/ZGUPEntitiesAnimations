using Unity.Entities;
using UnityEngine.Playables;

namespace ZG
{
    public class MeshInstanceAnimatorControllerClipPlayable : PlayableBehaviour
    {
        internal int _clipIndex;
        internal int _rigIndex;

        internal BlobAssetReference<AnimatorControllerDefinition> _definition;

        public MeshInstanceAnimatorControllerClipTrack GetTrack(Playable playable, float weight)
        {
            MeshInstanceAnimatorControllerClipTrack track;
            track.clipIndex = _clipIndex;
            track.rigIndex = _rigIndex;
            track.weight = weight;
            track.previousTime = (float)playable.GetPreviousTime();
            track.currentTime = (float)playable.GetTime();
            track.definition = _definition;

            return track;
        }
    }
}