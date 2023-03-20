using UnityEngine;

namespace ZG
{
    [System.Serializable]
    public class TranslationChannel
    {
        public string Id;
        public Vector3 DefaultValue;
    }

    [System.Serializable]
    public class RotationChannel
    {
        public string Id;
        public Quaternion DefaultValue;
    }

    [System.Serializable]
    public class ScaleChannel
    {
        public string Id;
        public Vector3 DefaultValue;
    }

    [System.Serializable]
    public class FloatChannel
    {
        public string Id;
        public float DefaultValue;
    }

    [System.Serializable]
    public class IntChannel
    {
        public string Id;
        public int DefaultValue;
    }
}
