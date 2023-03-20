using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Unity.Animation.Authoring
{
    /// <summary>
    /// This enum defines animatable generic property types.
    /// </summary>
    public enum GenericPropertyType : byte
    {
        Invalid,
        Float,
        Float2,
        Float3,
        Float4,
        Int,
        Int2,
        Int3,
        Int4,
        Quaternion
    }

    /// <summary>
    /// This enum defines animatable generic channel types.
    /// </summary>
    public enum GenericChannelType : byte
    {
        Invalid      = GenericPropertyType.Invalid,
        Float        = GenericPropertyType.Float,
        Int          = GenericPropertyType.Int,
        Quaternion   = GenericPropertyType.Quaternion
    }

    /// <summary>
    /// Extension methods for GenericPropertyType.
    /// </summary>
    public static class GenericPropertyTypeExtensions
    {
        /// <summary>
        /// Retrieves the number of channels that are required by the specified
        /// generic property type.
        /// </summary>
        /// <param name="type">Type of generic property.</param>
        /// <returns>Number of channels.</returns>
        /// <exception cref="InvalidOperationException">Type is invalid.</exception>
        public static uint GetNumberOfChannels(this GenericPropertyType type)
        {
            switch (type)
            {
                case GenericPropertyType.Float:
                case GenericPropertyType.Int:
                case GenericPropertyType.Quaternion:
                    return 1;
                case GenericPropertyType.Float2:
                case GenericPropertyType.Int2:
                    return 2;
                case GenericPropertyType.Float3:
                case GenericPropertyType.Int3:
                    return 3;
                case GenericPropertyType.Float4:
                case GenericPropertyType.Int4:
                    return 4;
                default:
                    throw new InvalidOperationException($"Invalid Type {type}");
            }
        }

        /// <summary>
        /// Retrieves the concrete channel type for the specified generic property type.
        /// </summary>
        /// <param name="type">Type of generic property.</param>
        /// <returns>Type of channel.</returns>
        /// <exception cref="InvalidOperationException">Type is invalid.</exception>
        public static GenericChannelType GetGenericChannelType(this GenericPropertyType type)
        {
            switch (type)
            {
                case GenericPropertyType.Float:
                case GenericPropertyType.Float2:
                case GenericPropertyType.Float3:
                case GenericPropertyType.Float4:
                    return GenericChannelType.Float;
                case GenericPropertyType.Int:
                case GenericPropertyType.Int2:
                case GenericPropertyType.Int3:
                case GenericPropertyType.Int4:
                    return GenericChannelType.Int;
                case GenericPropertyType.Quaternion:
                    return GenericChannelType.Quaternion;
                default:
                    throw new InvalidOperationException($"Invalid Type {type}");
            }
        }
    }

    /// <summary>
    /// Data structure that can hold values for all animatable types.
    /// </summary>
    [StructLayout(LayoutKind.Explicit), Serializable]
    public struct GenericPropertyVariant : IEquatable<GenericPropertyVariant>
    {
        [FieldOffset(0)] public GenericPropertyType Type;

        [FieldOffset(sizeof(GenericPropertyType))]
        private float4 m_FloatBuffer;

        [FieldOffset(sizeof(GenericPropertyType))]
        private int4 m_IntBuffer;

        [FieldOffset(sizeof(GenericPropertyType))]
        private quaternion m_Quaternion;

        public float Float
        {
            get
            {
                switch (Type)
                {
                    case GenericPropertyType.Float:
                    case GenericPropertyType.Float2:
                    case GenericPropertyType.Float3:
                    case GenericPropertyType.Float4:
                        return m_FloatBuffer.x;
                    case GenericPropertyType.Int:
                    case GenericPropertyType.Int2:
                    case GenericPropertyType.Int3:
                    case GenericPropertyType.Int4:
                        return m_IntBuffer.x;
                    default:
                        throw new InvalidOperationException($"Invalid Type {Type}");
                }
            }
            set
            {
                Type = GenericPropertyType.Float;
                m_FloatBuffer = new float4(value, float3.zero);
            }
        }

        public float2 Float2
        {
            get
            {
                switch (Type)
                {
                    case GenericPropertyType.Float:
                    case GenericPropertyType.Float2:
                    case GenericPropertyType.Float3:
                    case GenericPropertyType.Float4:
                        return m_FloatBuffer.xy;
                    case GenericPropertyType.Int:
                    case GenericPropertyType.Int2:
                    case GenericPropertyType.Int3:
                    case GenericPropertyType.Int4:
                        return m_IntBuffer.xy;
                    default:
                        throw new InvalidOperationException($"Invalid Type {Type}");
                }
            }
            set
            {
                Type = GenericPropertyType.Float2;
                m_FloatBuffer = new float4(value, float2.zero);
            }
        }

        public float3 Float3
        {
            get
            {
                switch (Type)
                {
                    case GenericPropertyType.Float:
                    case GenericPropertyType.Float2:
                    case GenericPropertyType.Float3:
                    case GenericPropertyType.Float4:
                        return m_FloatBuffer.xyz;
                    case GenericPropertyType.Int:
                    case GenericPropertyType.Int2:
                    case GenericPropertyType.Int3:
                    case GenericPropertyType.Int4:
                        return m_IntBuffer.xyz;
                    default:
                        throw new InvalidOperationException($"Invalid Type {Type}");
                }
            }
            set
            {
                Type = GenericPropertyType.Float3;
                m_FloatBuffer = new float4(value, 0f);
            }
        }

        public float4 Float4
        {
            get
            {
                switch (Type)
                {
                    case GenericPropertyType.Float:
                    case GenericPropertyType.Float2:
                    case GenericPropertyType.Float3:
                    case GenericPropertyType.Float4:
                        return m_FloatBuffer;
                    case GenericPropertyType.Int:
                    case GenericPropertyType.Int2:
                    case GenericPropertyType.Int3:
                    case GenericPropertyType.Int4:
                        return m_IntBuffer.xyzw;
                    case GenericPropertyType.Quaternion:
                        return m_Quaternion.value;
                    default:
                        throw new InvalidOperationException($"Invalid Type {Type}");
                }
            }
            set
            {
                Type = GenericPropertyType.Float4;
                m_FloatBuffer = value;
            }
        }

        public int Int
        {
            get
            {
                switch (Type)
                {
                    case GenericPropertyType.Float:
                    case GenericPropertyType.Float2:
                    case GenericPropertyType.Float3:
                    case GenericPropertyType.Float4:
                        return (int)m_FloatBuffer.x;
                    case GenericPropertyType.Int:
                    case GenericPropertyType.Int2:
                    case GenericPropertyType.Int3:
                    case GenericPropertyType.Int4:
                        return m_IntBuffer.x;
                    default:
                        throw new InvalidOperationException($"Invalid Type {Type}");
                }
            }
            set
            {
                Type = GenericPropertyType.Int;
                m_IntBuffer = new int4(value, int3.zero);
            }
        }

        public int2 Int2
        {
            get
            {
                switch (Type)
                {
                    case GenericPropertyType.Float:
                    case GenericPropertyType.Float2:
                    case GenericPropertyType.Float3:
                    case GenericPropertyType.Float4:
                        return (int2)m_FloatBuffer.xy;
                    case GenericPropertyType.Int:
                    case GenericPropertyType.Int2:
                    case GenericPropertyType.Int3:
                    case GenericPropertyType.Int4:
                        return m_IntBuffer.xy;
                    default:
                        throw new InvalidOperationException($"Invalid Type {Type}");
                }
            }
            set
            {
                Type = GenericPropertyType.Int2;
                m_IntBuffer = new int4(value, int2.zero);
            }
        }

        public int3 Int3
        {
            get
            {
                switch (Type)
                {
                    case GenericPropertyType.Float:
                    case GenericPropertyType.Float2:
                    case GenericPropertyType.Float3:
                    case GenericPropertyType.Float4:
                        return (int3)m_FloatBuffer.xyz;
                    case GenericPropertyType.Int:
                    case GenericPropertyType.Int2:
                    case GenericPropertyType.Int3:
                    case GenericPropertyType.Int4:
                        return m_IntBuffer.xyz;
                    default:
                        throw new InvalidOperationException($"Invalid Type {Type}");
                }
            }
            set
            {
                Type = GenericPropertyType.Int3;
                m_IntBuffer = new int4(value, 0);
            }
        }

        public int4 Int4
        {
            get
            {
                switch (Type)
                {
                    case GenericPropertyType.Float:
                    case GenericPropertyType.Float2:
                    case GenericPropertyType.Float3:
                    case GenericPropertyType.Float4:
                        return (int4)m_FloatBuffer.xyzw;
                    case GenericPropertyType.Int:
                    case GenericPropertyType.Int2:
                    case GenericPropertyType.Int3:
                    case GenericPropertyType.Int4:
                        return m_IntBuffer;
                    default:
                        throw new InvalidOperationException($"Invalid Type {Type}");
                }
            }
            set
            {
                Type = GenericPropertyType.Int;
                m_IntBuffer = value;
            }
        }

        public quaternion Quaternion
        {
            get
            {
                switch (Type)
                {
                    case GenericPropertyType.Quaternion:
                        return m_Quaternion;
                    case GenericPropertyType.Float4:
                        return m_FloatBuffer;
                    default:
                        throw new InvalidOperationException($"Invalid Type {Type}");
                }
            }
            set
            {
                Type = GenericPropertyType.Quaternion;
                m_Quaternion = value;
            }
        }

        internal System.Object Object
        {
            get
            {
                switch (Type)
                {
                    case GenericPropertyType.Float:
                        return m_FloatBuffer.x;
                    case GenericPropertyType.Float2:
                        return m_FloatBuffer.xy;
                    case GenericPropertyType.Float3:
                        return m_FloatBuffer.xyz;
                    case GenericPropertyType.Float4:
                        return m_FloatBuffer;
                    case GenericPropertyType.Int:
                        return m_IntBuffer.x;
                    case GenericPropertyType.Int2:
                        return m_IntBuffer.xy;
                    case GenericPropertyType.Int3:
                        return m_IntBuffer.xyz;
                    case GenericPropertyType.Int4:
                        return m_IntBuffer;
                    case GenericPropertyType.Quaternion:
                        return m_Quaternion;
                    default:
                        throw new InvalidOperationException($"Invalid Type {Type}");
                }
            }

            set
            {
                var type = value.GetType();

                if (type == typeof(float))
                    Float = (float)value;
                else if (type == typeof(float2))
                    Float2 = (float2)value;
                else if (type == typeof(float3))
                    Float3 = (float3)value;
                else if (type == typeof(float4))
                    Float4 = (float4)value;
                else if (type == typeof(int))
                    Int = (int)value;
                else if (type == typeof(int2))
                    Int2 = (int2)value;
                else if (type == typeof(int3))
                    Int3 = (int3)value;
                else if (type == typeof(int4))
                    Int4 = (int4)value;
                else if (type == typeof(quaternion))
                    Quaternion = (quaternion)value;
                else
                    throw new InvalidOperationException($"Invalid Type {Type}");
            }
        }


        public GenericPropertyVariant this[int index]
        {
            get
            {
                if (index < 0 || index >= 4)
                    throw new System.ArgumentException("index must be between[0...3]");

                switch (Type)
                {
                    case GenericPropertyType.Float:
                    case GenericPropertyType.Float2:
                    case GenericPropertyType.Float3:
                    case GenericPropertyType.Float4:
                        return new GenericPropertyVariant {Float = m_FloatBuffer[index]};
                    case GenericPropertyType.Int:
                    case GenericPropertyType.Int2:
                    case GenericPropertyType.Int3:
                    case GenericPropertyType.Int4:
                        return new GenericPropertyVariant {Int = m_IntBuffer[index]};
                    case GenericPropertyType.Quaternion:
                        return new GenericPropertyVariant {Float = m_Quaternion.value[index]};
                    default:
                        throw new InvalidOperationException($"Invalid Type {Type}");
                }
            }
        }

        public bool Equals(GenericPropertyVariant other)
        {
            if (Type != other.Type)
                return false;

            switch (Type)
            {
                case GenericPropertyType.Float:
                    return Float == other.Float;
                case GenericPropertyType.Float2:
                    return Float2.Equals(other.Float2);
                case GenericPropertyType.Float3:
                    return Float3.Equals(other.Float3);
                case GenericPropertyType.Float4:
                    return Float4.Equals(other.Float4);
                case GenericPropertyType.Int:
                    return Int == other.Int;
                case GenericPropertyType.Int2:
                    return Int2.Equals(other.Int2);
                case GenericPropertyType.Int3:
                    return Int3.Equals(other.Int3);
                case GenericPropertyType.Int4:
                    return Int4.Equals(other.Int4);
                case GenericPropertyType.Quaternion:
                    return Quaternion.Equals(other.Quaternion);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public override bool Equals(object obj)
        {
            return obj is GenericPropertyVariant other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Tuple.Create((int)Type, m_Quaternion).GetHashCode();
        }
    }
}
