using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Playables;

namespace ZG
{
    public class MeshInstanceClipPlayable : PlayableBehaviour
    {
        internal int _clipIndex;
        internal int _rigIndex;

        internal float _duration;

        internal float _speed;

        internal float4x4 _matrix;

        internal BlobAssetReference<MeshInstanceClipFactoryDefinition> _factory;
        internal BlobAssetReference<MeshInstanceClipDefinition> _definition;

        public MeshInstanceClipTrack GetTrack(Playable playable, float4x4 transform, float weight)
        {
            MeshInstanceClipTrack track;
            track.clipIndex = _clipIndex;
            track.rigIndex = _rigIndex;
            track.weight = weight;

            track.time = (float)playable.GetTime() * _speed;
            if ((_definition.Value.clips[_clipIndex].flag & MeshInstanceClipFlag.Looping) == MeshInstanceClipFlag.Looping)
                track.time = Mathf.Repeat(track.time, _duration);
            else
            {
                track.time = Mathf.Clamp(track.time, -_duration, _duration);
                if (track.time < 0.0f)
                    track.time = _duration + track.time;
            }

            track.matrix = math.mul(transform, _matrix);
            track.factory = _factory;
            track.definition = _definition;

            return track;
        }
    }
}