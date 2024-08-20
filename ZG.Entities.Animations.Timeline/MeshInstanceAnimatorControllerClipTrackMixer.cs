using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

namespace ZG
{
    public class MeshInstanceAnimatorControllerClipTrackMixer : PlayableBehaviour
    {
        private static List<MeshInstanceAnimatorControllerClipTrack> __tracks;

        private GameObjectEntity __player;
        private bool __isPlaying;

        public override void OnPlayableDestroy(Playable playable)
        {
            if (__player != null)
            {
                if(__isPlaying)
                    __player.RemoveComponent<MeshInstanceAnimatorControllerClipTrack>();

                __player = null;
            }

            __isPlaying = false;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            base.ProcessFrame(playable, info, playerData);

            var player = playerData as GameObjectEntity;
            if (player != __player)
            {
                if (__isPlaying)
                {
                    if (__player != null)
                        __player.RemoveComponent<MeshInstanceAnimatorControllerClipTrack>();

                    if (player != null)
                        player.AddBuffer<MeshInstanceAnimatorControllerClipTrack>();
                }

                __player = player;
            }

            if (__player == null)
                return;

            int numInputs = playable.GetInputCount();
            if (numInputs < 1)
            {
                if (__isPlaying)
                {
                    __isPlaying = false;
                    
                    __player.RemoveComponent<MeshInstanceAnimatorControllerClipTrack>();
                }
                
                return;
            }

            if (__tracks == null)
                __tracks = new List<MeshInstanceAnimatorControllerClipTrack>();
            else
                __tracks.Clear();

            float weight;
            Playable input;
            for (int i = 0; i < numInputs; ++i)
            {
                input = playable.GetInput(i);
                weight = playable.GetInputWeight(i);
                if (weight > Mathf.Epsilon)
                {
                    var clip = ((ScriptPlayable<MeshInstanceAnimatorControllerClipPlayable>)input).GetBehaviour();
                    if (clip != null /* && playable.GetPlayState() == PlayState.Playing*/)
                        __tracks.Add(clip.GetTrack(input, weight));
                }
            }

            if (__tracks.Count > 0)
            {
                if (!__isPlaying)
                {
                    __isPlaying = true;
                    
                    __player.AddBuffer<MeshInstanceAnimatorControllerClipTrack>();
                }
                
                __player
                    .SetBuffer<MeshInstanceAnimatorControllerClipTrack, List<MeshInstanceAnimatorControllerClipTrack>>(
                        __tracks);
            }
            else if (__isPlaying)
            {
                __isPlaying = false;
                
                __player.RemoveComponent<MeshInstanceAnimatorControllerClipTrack>();
            }
            //__player.SetComponentEnabled<MeshInstanceAnimatorControllerClipCommand>(true);
        }
    }
}