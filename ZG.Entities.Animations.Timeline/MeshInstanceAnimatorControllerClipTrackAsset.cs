using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace ZG
{
    [TrackClipType(typeof(MeshInstanceAnimatorControllerClipPlayableAsset))]
    [TrackBindingType(typeof(GameObjectEntity), TrackBindingFlags.None)]
    public class MeshInstanceAnimatorControllerClipTrackAsset : TrackAsset
    {
        public override Playable CreateTrackMixer(
            PlayableGraph graph, GameObject go, int inputCount)
        {
            var mixer = ScriptPlayable<MeshInstanceAnimatorControllerClipTrackMixer>.Create(graph);
            mixer.SetInputCount(inputCount);
            return mixer;
        }
    }
}