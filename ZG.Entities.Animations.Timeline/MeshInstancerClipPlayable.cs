using Unity.Entities;
using UnityEngine.Playables;

namespace ZG
{
    public class MeshInstanceClipPlayable : PlayableBehaviour
    {
        internal int _clipIndex;
        internal int _rigIndex;

        internal BlobAssetReference<MeshInstanceClipFactoryDefinition> _factory;
        internal BlobAssetReference<MeshInstanceClipDefinition> _definition;

        public MeshInstanceClipTrack GetTrack(Playable playable, float weight)
        {
            MeshInstanceClipTrack track;
            track.clipIndex = _clipIndex;
            track.rigIndex = _rigIndex;
            track.weight = weight;
            track.time = (float)playable.GetTime();
            track.factory = _factory;
            track.definition = _definition;

            return track;
        }
    }
}