using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine.Playables;
using Unity.Transforms;
using UnityEngine;

namespace ZG
{
    public class MeshInstanceClipTrackMixer : PlayableBehaviour
    {
        private static List<MeshInstanceClipTrack> __tracks;
        private static Dictionary<object, ulong> __frameIDs;

        private GameObjectEntity __player;

        internal Transform _parent;

        ~MeshInstanceClipTrackMixer()
        {
            Dispose();
        }

        public void Dispose()
        {
            object playerObject = __player;
            bool isPlaying = playerObject != null && __frameIDs.Remove(playerObject);

            if (__player != null)
            {
                if(isPlaying)
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
                Dispose();

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

            float weight;
            Playable input;
            float4x4 localToWorld = _parent == null ? float4x4.identity : _parent.localToWorldMatrix;
            for (int i = 0; i < numInputs; ++i)
            {
                weight = playable.GetInputWeight(i);
                if (weight > Mathf.Epsilon)
                {
                    input = playable.GetInput(i);
                    var clip = ((ScriptPlayable<MeshInstanceClipPlayable>)input).GetBehaviour();
                    if (clip != null /* && playable.GetPlayState() == PlayState.Playing*/)
                        __tracks.Add(clip.GetTrack(input, localToWorld, weight));
                }
            }

            if (__tracks.Count > 0)
            {
                if (__frameIDs == null)
                    __frameIDs = new Dictionary<object, ulong>();

                if (!__frameIDs.TryGetValue(__player, out ulong sourceFrameID))
                    sourceFrameID = 0;

                ulong destinationFrameID = info.frameId;
                if (destinationFrameID == sourceFrameID || sourceFrameID == 0)
                    __player.AppendBuffer<MeshInstanceClipTrack, List<MeshInstanceClipTrack>>(__tracks);
                else
                {
                    __player.SetBuffer<MeshInstanceClipTrack, List<MeshInstanceClipTrack>>(__tracks);

                    __frameIDs[__player] = destinationFrameID;
                }
            }
            else 
            {
                if(__frameIDs != null && __frameIDs.TryGetValue(__player, out ulong sourceFrameID) && sourceFrameID != info.frameId)
                {
                    __frameIDs.Remove(sourceFrameID);
                    
                    __player.RemoveComponent<MeshInstanceClipTrack>();
                }
            }
        }
    }
}