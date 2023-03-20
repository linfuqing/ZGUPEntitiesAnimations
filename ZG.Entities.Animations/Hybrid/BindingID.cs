using System;
using System.Diagnostics;
using Unity.Animation.Authoring;
using UnityEngine;

namespace Unity.Animation.Hybrid
{
    /// <summary>
    /// Interface to cover all channel bindings.
    /// </summary>
    public interface IBindingID
    {
        string ID { get; }
    }

    /// <summary>
    /// Binding for transform channels.
    /// Translation, Rotation and Scale channels only need a path
    /// as unique identifier.
    /// Q. Should we instead commit to use a generic binding like other channels?
    /// See also <seealso cref="SkeletonBoneReference"/>, <seealso cref="SkeletonReferenceAttribute"/> and <seealso cref="ShowFullPathAttribute"/>
    /// </summary>
    [Serializable]
    [DebuggerDisplay("id = {ID}")]
    public partial struct TransformBindingID : IBindingID, IEquatable<TransformBindingID>, IComparable<TransformBindingID>
    {
        /// <summary>
        /// Path as it's set in the hierarchy.
        /// </summary>
        public string Path;
        /// <summary>
        /// Retrieves a unique ID for the transform channel.
        /// </summary>
        public string ID { get => Path; }

        /// <summary>
        /// Parent TransformBindingID if it can extracted from path. Invalid TransformBindingID otherwise.
        /// </summary>
        public TransformBindingID GetParent()
        {
            if (this.Equals(Root))
                return Invalid;

            int index = Path.LastIndexOf(Authoring.Skeleton.k_PathSeparator);
            if (index == -1)
                return Root;

            return new TransformBindingID
            {
                Path = Path.Substring(0, index)
            };
        }

        /// <summary>
        /// Invalid TransformBindingID.
        /// </summary>
        public static readonly TransformBindingID Invalid = new TransformBindingID { Path = null };

        /// <summary>
        /// TransformBindingID describing the root node of the animated hierarchy.
        /// </summary>
        public static readonly TransformBindingID Root = new TransformBindingID { Path = "" };

        /// <summary>
        /// Name of the node described by the TransformBindingID.
        /// </summary>
        public string Name
        {
            get => System.IO.Path.GetFileName(Path);
        }

        /// <inheritdoc/>
        public bool Equals(TransformBindingID other)
        {
            return Path == other.Path;
        }

        /// <inheritdoc/>
        public int CompareTo(TransformBindingID other)
        {
            return ID.CompareTo(other.ID);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is TransformBindingID other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return (Path != null ? Path.GetHashCode() : 0);
        }

        public override string ToString() => Path;

        public static bool operator==(TransformBindingID left, TransformBindingID right) => left.Equals(right);
        public static bool operator!=(TransformBindingID left, TransformBindingID right) => !left.Equals(right);

        /// <summary>
        /// Returns true if the TransformBindingID is a descendant of the given TransformBindingID
        /// </summary>
        public bool IsDescendantOf(TransformBindingID ancestor)
        {
            if (Path == null || ancestor.Path == null)
                return false;
            if (ancestor.Path.Length == 0)
                return true;
            return Path.StartsWith(ancestor.Path);
        }

        /// <summary>
        /// Returns the runtime channel index that is identified by the specified id.
        /// </summary>
        /// <returns>Returns the index if a channel matching the id was found. -1 otherwise.</returns>
        public int ConvertToIndex(Authoring.Skeleton skeleton)
        {
            return skeleton.QueryTransformIndex(this);
        }
    }

    /// <summary>
    /// Binding for generic channels.
    /// </summary>
    [Serializable]
    public struct GenericBindingID : IBindingID, IEquatable<GenericBindingID>, IComparable<GenericBindingID>
    {
        /// <summary>
        /// Path as it's set in the hierarchy.
        /// </summary>
        public string Path;
        /// <summary>
        /// Name of the attribute.
        /// </summary>
        public string AttributeName;
        /// <summary>
        /// Type of value.
        /// </summary>
        public Authoring.GenericPropertyType ValueType;
        /// <summary>
        /// Type of channel.
        /// </summary>
        public Authoring.GenericChannelType ChannelType
        {
            get => ValueType.GetGenericChannelType();
        }

        /// <summary>
        /// Type of component.
        /// </summary>
        [SerializeField]
        private string m_ComponentName;

        private static readonly string[] k_Suffixes = new[] {"x", "y", "z", "w"};

        /// <summary>
        /// Type of component.
        /// </summary>
        public Type ComponentType
        {
            get => m_ComponentName != null ? Type.GetType(m_ComponentName) : null;
            set => m_ComponentName = value != null ? value.AssemblyQualifiedName : null;
        }

        /// <summary>
        /// Retrieves a sub-id in a GenericBindingID that contain multiple channels.
        /// </summary>
        /// <param name="index">Index of the sub-channel.</param>
        /// <exception cref="ArgumentException">Index must be between [0...3]</exception>
        public GenericBindingID this[int index]
        {
            get
            {
                if ((uint)index >= 4)
                    throw new System.ArgumentException("index must be between[0...3]");

                return new GenericBindingID
                {
                    Path = Path,
                    AttributeName = $"{AttributeName}.{k_Suffixes[index]}",
                    ComponentType = ComponentType,
                    ValueType = (GenericPropertyType)ChannelType
                };
            }
        }

        /// <summary>
        /// Retrieves x channel sub-id.
        /// </summary>
        public GenericBindingID x { get => this[0]; }
        /// <summary>
        /// Retrieves y channel sub-id.
        /// </summary>
        public GenericBindingID y { get => this[1]; }
        /// <summary>
        /// Retrieves z channel sub-id.
        /// </summary>
        public GenericBindingID z { get => this[2]; }
        /// <summary>
        /// Retrieves w channel sub-id.
        /// </summary>
        public GenericBindingID w { get => this[3]; }

        /// <summary>
        /// Retrieves r channel sub-id. This is the same as <see cref="x"/>.
        /// </summary>
        public GenericBindingID r { get => this[0]; }
        /// <summary>
        /// Retrieves r channel sub-id. This is the same as <see cref="y"/>.
        /// </summary>
        public GenericBindingID g { get => this[1]; }
        /// <summary>
        /// Retrieves r channel sub-id. This is the same as <see cref="z"/>.
        /// </summary>
        public GenericBindingID b { get => this[2]; }
        /// <summary>
        /// Retrieves r channel sub-id. This is the same as <see cref="w"/>.
        /// </summary>
        public GenericBindingID a { get => this[3]; }

        /// <summary>
        /// Invalid GenericBindingID.
        /// </summary>
        public readonly static GenericBindingID Invalid = new GenericBindingID {AttributeName = null, Path = null, ComponentType = null};

        /// <summary>
        /// Retrieves the unique ID for the generic property channel.
        /// </summary>
        public string ID
        {
            get => this.Equals(Invalid) ? null : $"{Path}:{AttributeName}:{m_ComponentName}";
        }

        /// <inheritdoc/>
        public bool Equals(GenericBindingID other)
        {
            return Path == other.Path && AttributeName == other.AttributeName && Equals(m_ComponentName, other.m_ComponentName);
        }

        /// <inheritdoc/>
        public int CompareTo(GenericBindingID other)
        {
            var result = Path.CompareTo(other.Path);
            if (result == 0)
            {
                return AttributeName.CompareTo(other.AttributeName);
            }

            return result;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is GenericBindingID other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Path != null ? Path.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (AttributeName != null ? AttributeName.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (m_ComponentName != null ? m_ComponentName.GetHashCode() : 0);
                return hashCode;
            }
        }
    }
}
