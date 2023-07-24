using UnityEngine;

namespace ZG
{
    public class MeshInstanceAnimatorEventDispatcherHandler : IMeshInstanceHybridAnimatorEventHandler
    {
        public void Dispatch(Transform root, float weight, int state)
        {
            if (state < 0)
                return;

            var component = root.GetComponentInChildren<AnimationEventDispatcher>();
            if (component == null || component.events == null || component.events.Length <= state)
                return;

            component.events[state].Invoke();
        }
    }


#if UNITY_EDITOR
    [CreateAssetMenu(menuName = "ZG/Mesh Instance/Animation Events/Dispather Config", fileName = "Mesh Instance Animator Event Dispatcher Config")]
    public class MeshInstanceAnimatorEventDispatcherConfig : MeshInstanceHybridAnimatorEventConfig<MeshInstanceAnimatorEventDispatcherHandler>
    {
        public override int GetState(AnimationEvent animationEvent) => animationEvent.intParameter;
    }
#endif
}