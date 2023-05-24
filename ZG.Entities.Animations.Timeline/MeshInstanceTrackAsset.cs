using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ZG
{
    [TrackClipType(typeof(MeshInstanceClipPlayableAsset))]
    [TrackBindingType(typeof(GameObjectEntity), TrackBindingFlags.None)]
    public class MeshInstanceClipTrackAsset : TrackAsset
    {
        public override Playable CreateTrackMixer(
            PlayableGraph graph, GameObject go, int inputCount)
        {
            var mixer = ScriptPlayable<MeshInstanceClipTrackMixer>.Create(graph);
            mixer.SetInputCount(inputCount);
            return mixer;
        }
    }
}