using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine.Playables;
using Unity.Transforms;

namespace ZG
{
    public class MeshInstanceClipTrackMixer : PlayableBehaviour
    {
        private static List<MeshInstanceClipTrack> __tracks;

        private GameObjectEntity __player;

        public override void OnPlayableDestroy(Playable playable)
        {
            if (__player != null)
            {
                __player.RemoveComponent<MeshInstanceClipTrack>();

                __player = null;
            }
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            base.ProcessFrame(playable, info, playerData);

            var player = playerData as GameObjectEntity;
            if (player != __player)
            {
                if(__player != null)
                    __player.RemoveComponent<MeshInstanceClipTrack>();

                if (player != null)
                {
                    //player.Awake();
                    player.AddBuffer<MeshInstanceClipTrack>();
                }

                __player = player;
            }

            if (__player == null)
                return;

            int numInputs = playable.GetInputCount();
            if (numInputs < 1)
                return;

            if (__tracks == null)
                __tracks = new List<MeshInstanceClipTrack>();
            else
                __tracks.Clear();

            Playable input;
            for (int i = 0; i < numInputs; ++i)
            {
                input = playable.GetInput(i);
                var clip = ((ScriptPlayable<MeshInstanceClipPlayable>)input).GetBehaviour();
                if (clip != null/* && playable.GetPlayState() == PlayState.Playing*/)
                {
                    float weight = playable.GetInputWeight(i);

                    __tracks.Add(clip.GetTrack(input, weight));
                }
            }

            /*Translation translation;
            translation.Value = matrix.c3.xyz;
            __player.SetComponentData(translation);

            Rotation rotation;
            rotation.Value = matrix.c3.xyz;
            __player.SetComponentData(translation);*/

            __player.SetBuffer<MeshInstanceClipTrack, List<MeshInstanceClipTrack>>(__tracks);
            //__player.SetComponentEnabled<MeshInstanceAnimatorControllerClipCommand>(true);
        }
    }
}