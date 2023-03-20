#if UNITY_EDITOR

using UnityEditor;

namespace Unity.Animation.Hybrid
{
    [ComponentBindingProcessor(typeof(UnityEngine.Transform))]
    class TransformBindingProcessor : ComponentBindingProcessor<UnityEngine.Transform>
    {
        static readonly StringHash k_LocalPosition       = "m_LocalPosition.x";
        static readonly StringHash k_LocalRotation       = "m_LocalRotation.x";
        static readonly StringHash k_LocalEulerAngles    = "localEulerAngles.x";
        static readonly StringHash k_LocalEulerAnglesRaw = "localEulerAnglesRaw.x";
        static readonly StringHash k_LocalScale          = "m_LocalScale.x";

        protected override ChannelBindType Process(EditorCurveBinding binding)
        {
            // TODO : Account for missing T, R, S curves

            StringHash strHash = binding.propertyName;
            if (strHash == k_LocalPosition)
                return ChannelBindType.Translation;

            if (strHash == k_LocalRotation ||
                strHash == k_LocalEulerAngles ||
                strHash == k_LocalEulerAnglesRaw)
                return ChannelBindType.Rotation;

            if (strHash == k_LocalScale)
                return ChannelBindType.Scale;

            // Discard other ".y" and ".z" types
            return ChannelBindType.Discard;
        }
    }
}

#endif
