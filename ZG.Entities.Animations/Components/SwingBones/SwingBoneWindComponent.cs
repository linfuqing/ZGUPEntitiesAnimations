using System;
using Unity.Entities;
using Unity.Mathematics;

namespace ZG
{
    [Serializable]
    public struct SwingBoneWind : IComponentData
    {
        public float speed;
        public float minDelta;
        public float maxDelta;
        public float3 direction;

        public static readonly SwingBoneWind DefaultValue = new SwingBoneWind()
        {
            speed = 1.0f,
            minDelta = 0.2f,
            maxDelta = 0.5f,
            direction = new float3(1.0f, 0.0f, 0.0f)
        };
    }

    public sealed class SwingBoneWindComponent : EntityProxyComponent
    {
        public float speed;
        public float minDelta;
        public float maxDelta;

        public SwingBoneWind value
        {
            get
            {
                SwingBoneWind result;
                result.speed = speed;
                result.minDelta = minDelta;
                result.maxDelta = maxDelta;
                result.direction = transform.forward;
                return result;
            }
        }

        void OnEnable()
        {
            this.AddComponentData(value);
        }

        void OnDisable()
        {
            this.RemoveComponent<SwingBoneWind>();
        }

        void Update()
        {
            this.SetComponentData(value);
        }
    }
}