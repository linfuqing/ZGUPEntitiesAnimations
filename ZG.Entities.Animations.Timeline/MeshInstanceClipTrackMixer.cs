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
        private static Dictionary<object, ulong> __frameIDs;

        private GameObjectEntity __player;

        internal UnityEngine.Transform _parent;

        ~MeshInstanceClipTrackMixer()
        {
            Dispose();
        }

        public void Dispose()
        {
            object playerObject = __player;
            if (playerObject != null)
                __frameIDs.Remove(playerObject);

            if (__player != null)
            {
                __player.RemoveComponent<MeshInstanceClipTrack>();

                __player = null;
            }
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            Dispose();
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
            /*if (numInputs < 1)
                return;*/

            if (__tracks == null)
                __tracks = new List<MeshInstanceClipTrack>();
            else
                __tracks.Clear();

            Playable input;
            float4x4 localToWorld = _parent == null ? float4x4.identity : _parent.localToWorldMatrix;
            for (int i = 0; i < numInputs; ++i)
            {
                input = playable.GetInput(i);
                var clip = ((ScriptPlayable<MeshInstanceClipPlayable>)input).GetBehaviour();
                if (clip != null/* && playable.GetPlayState() == PlayState.Playing*/)
                    __tracks.Add(clip.GetTrack(input, localToWorld, playable.GetInputWeight(i)));
            }

            /*Translation translation;
            translation.Value = matrix.c3.xyz;
            __player.SetComponentData(translation);

            Rotation rotation;
            rotation.Value = matrix.c3.xyz;
            __player.SetComponentData(translation);*/

            if (__frameIDs == null)
                __frameIDs = new Dictionary<object, ulong>();

            if (!__frameIDs.TryGetValue(__player, out ulong sourceFrameID))
                sourceFrameID = 0;

            ulong destinationFrameID = info.frameId;
            if (destinationFrameID == sourceFrameID)
                __player.AppendBuffer<MeshInstanceClipTrack, List<MeshInstanceClipTrack>>(__tracks);
            else
            {
                __player.SetBuffer<MeshInstanceClipTrack, List<MeshInstanceClipTrack>>(__tracks);

                __frameIDs[__player] = destinationFrameID;
            }
            //__player.SetComponentEnabled<MeshInstanceAnimatorControllerClipCommand>(true);
        }
    }
}