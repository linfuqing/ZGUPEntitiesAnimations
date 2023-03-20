using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Unity.Animation.Hybrid
{
    /// <summary>
    /// Represents a hash generator for animation bindings.
    /// This is used to convert authoring bindings to RigDefinition unique IDs.
    /// </summary>
    public struct BindingHashGenerator
    {
        /// <summary>
        /// Delegate to compute the unique id of a TransformBindingID.
        /// </summary>
        /// <param name="id">TransformBindingID value.</param>
        /// <returns>Unique uint hash.</returns>
        public delegate uint TransformBindingHashDelegate(TransformBindingID id);

        /// <summary>
        /// Delegate to compute the unique id of a GenericBindingID
        /// </summary>
        /// <param name="id">GenericBindingID value.</param>
        /// <returns>Unique uint hash.</returns>
        public delegate uint GenericBindingHashDelegate(GenericBindingID id);

        TransformBindingHashDelegate m_TransformBindingHasher;
        GenericBindingHashDelegate m_GenericBindingHasher;

        /// <summary>
        /// Converts a TransformBindingID to a unique uint hash.
        /// </summary>
        /// <param name="id">TransformBindingID value.</param>
        /// <returns>Unique uint hash.</returns>
        /// <exception cref="System.NullReferenceException">TransformBindingHashDelegate must be valid.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ToHash(TransformBindingID id)
        {
            Validate(m_TransformBindingHasher);
            return m_TransformBindingHasher(id);
        }

        /// <summary>
        /// Converts a GenericBindingID to a unique uint hash.
        /// </summary>
        /// <param name="id">GenericBindingID value.</param>
        /// <returns>Unique uint hash.</returns>
        /// <exception cref="System.NullReferenceException">GenericBindingHashDelegate must be valid.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ToHash(GenericBindingID id)
        {
            Validate(m_GenericBindingHasher);
            return m_GenericBindingHasher(id);
        }

        /// <summary>
        /// Set the TransformBindingID hash function.
        /// </summary>
        /// <exception cref="System.NullReferenceException"/>TransformBindingHashDelegate must be valid.</exception>
        public TransformBindingHashDelegate TransformBindingHashFunction
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set { Validate(value); m_TransformBindingHasher = value; }
        }

        /// <summary>
        /// Set the GenericBindingID hash function.
        /// </summary>
        /// <exception cref="System.NullReferenceException"/>GenericBindingHashDelegate must be valid.</exception>
        public GenericBindingHashDelegate GenericBindingHashFunction
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set { Validate(value); m_GenericBindingHasher = value; }
        }

        /// <summary>
        /// Returns true if BindingHashGenerator is valid.
        /// </summary>
        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (m_TransformBindingHasher != null && m_GenericBindingHasher != null);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void Validate(TransformBindingHashDelegate function)
        {
            if (function == null)
                throw new System.NullReferenceException("TransformBindingHashDelegate is null.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void Validate(GenericBindingHashDelegate function)
        {
            if (function == null)
                throw new System.NullReferenceException("GenericBindingHashDelegate is null.");
        }
    }
}
