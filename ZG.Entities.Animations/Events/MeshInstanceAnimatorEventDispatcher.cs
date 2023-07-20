using System;
using UnityEngine;
using UnityEngine.Events;

namespace ZG
{
    public class MeshInstanceAnimatorEventDispatcher : MonoBehaviour
    {
        [Serializable]
        public struct AnimatorEvent
        {
            public string name;

            public UnityEvent callback;

            public void Invoke()
            {
                if (callback != null)
                    callback.Invoke();
            }
        }

        public AnimatorEvent[] events;
    }
}