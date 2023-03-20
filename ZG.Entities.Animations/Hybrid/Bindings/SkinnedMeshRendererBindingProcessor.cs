#if UNITY_EDITOR

using UnityEditor;

namespace Unity.Animation.Hybrid
{
    [ComponentBindingProcessor(typeof(UnityEngine.SkinnedMeshRenderer))]
    class SkinnedMeshRendererBindingProcessor : ComponentBindingProcessor<UnityEngine.SkinnedMeshRenderer>
    {
        static readonly StringHash k_DirtyAABB            = "m_DirtyAABB";
        static readonly StringHash k_Enabled              = "m_Enabled";
        static readonly StringHash k_SkinnedMotionVectors = "m_SkinnedMotionVectors";
        static readonly StringHash k_UpdateWhenOffScreen  = "m_UpdateWhenOffscreen";
        static readonly StringHash k_ReceiveShadows       = "m_ReceiveShadows";

        protected override ChannelBindType Process(EditorCurveBinding binding)
        {
            StringHash strHash = binding.propertyName;
            if (strHash == k_DirtyAABB ||
                strHash == k_Enabled ||
                strHash == k_SkinnedMotionVectors ||
                strHash == k_UpdateWhenOffScreen ||
                strHash == k_ReceiveShadows)
                return ChannelBindType.Integer;

            // All other animatable types are floats
            return ChannelBindType.Float;
        }
    }
}

#endif
