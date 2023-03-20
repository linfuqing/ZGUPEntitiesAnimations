using Unity.Mathematics;
using Unity.Animation.Hybrid;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[assembly: ZG.RegisterEntityObject(typeof(ZG.RectTransformAnimationComponent))]

namespace ZG
{
#if UNITY_EDITOR
    [ComponentBindingProcessor(typeof(RectTransform))]
    internal class RectTransformBindingProcessor : ComponentBindingProcessor<RectTransform>
    {
        protected override ChannelBindType Process(EditorCurveBinding binding)
        {
            return ChannelBindType.Float;
        }
    }
#endif

    public class RectTransformAnimationCallback : HybridAnimationCallback<RectTransformAnimationComponent>
    {

    }

    [EntityComponent]
    public class RectTransformAnimationComponent : MeshInstanceHybridAnimationObjectComponent
    {
        [HybridAnimationCallback("m_AnchoredPosition", type = typeof(RectTransform))]
        public float2 anchoredPosition
        {
            set
            {
                ((RectTransform)transform).anchoredPosition = value;
            }
        }
    }
}