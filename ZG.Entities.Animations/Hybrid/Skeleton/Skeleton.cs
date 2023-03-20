using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Animation.Hybrid;
using UnityEngine;

namespace Unity.Animation.Authoring
{
    /// <summary>
    /// This enum represents the current state of a <see cref="TransformChannel"/>
    /// </summary>
    public enum TransformChannelState
    {
        DoesNotExist,
        Active,
        Inactive
    }

    [Flags]
    public enum TransformChannelSearchMode
    {
        /// <summary>The default search mode. It will return all active transforms, which are always descendants of the root.</summary>
        Default = ActiveRootDescendants,
        /// <summary>All inactive transforms which are also descendants of the root</summary>
        InactiveRootDescendants = 1,
        /// <summary>All inactive transforms, including those that are not descendants of the root</summary>
        InactiveAll = 3,
        /// <summary>All active transforms which are also descendants of the root</summary>
        ActiveRootDescendants = 4,
        /// <summary>All transforms that are descendants of the root</summary>
        ActiveAndInactiveRootDescendants = ActiveRootDescendants | InactiveRootDescendants,
        /// <summary>All transforms, including those that are not descendants of the root</summary>
        ActiveAndInactiveAll = ActiveRootDescendants | InactiveAll,
    }

    /// <summary>
    /// Interface that describes an animated channel on the Skeleton asset.
    /// </summary>
    public interface IChannel
    {
        /// <summary>
        /// Unique ID.
        /// </summary>
        IBindingID ID { get; }
    }

    /// <summary>
    /// Interface that describes an animated generic channel on the Skeleton asset.
    /// </summary>
    public interface IGenericChannel : IChannel
    {
        /// <summary>
        /// Default Value for this channel.
        /// </summary>
        GenericPropertyVariant DefaultValue { get; }
    }

    /// <summary>
    /// Properties of a transform channel defined in a <see cref="Skeleton"/>.
    /// </summary>
    [Serializable]
    public struct TransformChannelProperties
    {
        /// <summary>
        /// Default transform channel properties.
        /// </summary>
        public static readonly TransformChannelProperties Default = new TransformChannelProperties
        {
            DefaultTranslationValue = float3.zero,
            DefaultRotationValue = quaternion.identity,
            DefaultScaleValue = new float3(1f)
        };

        /// <summary>
        /// Default translation value.
        /// </summary>
        public float3 DefaultTranslationValue;

        /// <summary>
        /// Default rotation value.
        /// </summary>
        public quaternion DefaultRotationValue;

        /// <summary>
        /// Default scale value.
        /// </summary>
        public float3 DefaultScaleValue;

        public override string ToString() =>
            $"{{ TRS=({DefaultTranslationValue}, {DefaultRotationValue}, {DefaultScaleValue}) }}";
    }

    /// <summary>
    /// A key-value pair of a <see cref="TransformBindingID"/> and <see cref="TransformChannelProperties"/>.
    /// </summary>
    [Serializable]
    [DebuggerDisplay("id = {ID.ID}, properties = {Properties}")]
    public struct TransformChannel : IChannel, IComparable<TransformChannel>
    {
        /// <summary>
        /// Unique ID for the Transform channel.
        /// </summary>
        public TransformBindingID ID;

        /// <summary>
        /// Properties of the transform channel.
        /// </summary>
        public TransformChannelProperties Properties;

        /// <inheritdoc/>
        IBindingID IChannel.ID
        {
            get => ID;
        }

        /// <summary>
        /// Invalid Transform channel.
        /// </summary>
        public static TransformChannel Invalid
        {
            get => new TransformChannel { ID = TransformBindingID.Invalid };
        }

        /// <inheritdoc/>
        public int CompareTo(TransformChannel other)
        {
            return ID.CompareTo(other.ID);
        }

        public override string ToString() => $"{{ {nameof(ID)}={ID}, {nameof(Properties)}={Properties} }}";
    }

    /// <summary>
    /// Animated Generic channel.
    /// This channel describes generic properties that are not part of the Transform component.
    /// </summary>
    /// Q. The GenericChannel also covers standalone Quaternion properties in generic
    /// component. This allows to greatly simplify the TransformChannel logic, but is still up
    /// for debate.
    [Serializable]
    public struct GenericChannel<T> : IGenericChannel, IComparable<GenericChannel<T>>
        where T : struct
    {
        /// <summary>
        /// Unique ID for the generic property channel.
        /// </summary>
        public GenericBindingID ID;
        /// <summary>
        /// Default value.
        /// </summary>
        public T DefaultValue;

        /// <inheritdoc/>
        IBindingID IChannel.ID { get => ID; }

        /// <inheritdoc/>
        GenericPropertyVariant IGenericChannel.DefaultValue
        {
            get => new GenericPropertyVariant {Object = DefaultValue};
        }

        /// <summary>
        /// Invalid generic channel.
        /// </summary>
        public static GenericChannel<T> Invalid
        {
            get => new GenericChannel<T> {ID = GenericBindingID.Invalid};
        }

        /// <inheritdoc/>
        public int CompareTo(GenericChannel<T> other)
        {
            return ID.CompareTo(other.ID);
        }
    }

    /// <summary>
    /// A reference to a specific bone in a skeleton
    /// See also <seealso cref="TransformBindingID"/>, <seealso cref="SkeletonReferenceAttribute"/> and <seealso cref="ShowFullPathAttribute"/>
    /// </summary>
    [Serializable]
    public struct SkeletonBoneReference
    {
        internal const string nameOfID = nameof(m_ID);
        internal const string nameOfSkeleton = nameof(m_Skeleton);

        [SerializeField] Skeleton m_Skeleton;
        public Skeleton Skeleton { get => m_Skeleton; internal set => m_Skeleton = value; }
        [SerializeField] TransformBindingID m_ID;
        public TransformBindingID ID { get => m_ID; }

        public SkeletonBoneReference(Skeleton skeleton, TransformBindingID id)
        {
            m_Skeleton = skeleton;
            m_ID = id;
        }

        public bool IsValid()
        {
            if (m_Skeleton == null)
                return false;
            return m_Skeleton.GetTransformChannelState(ID) == TransformChannelState.Active;
        }

        /// <summary>
        /// Returns the runtime channel index that is identified by the specified id.
        /// </summary>
        /// <returns>Returns the index if a channel matching the id was found. -1 otherwise.</returns>
        public int ConvertToIndex()
        {
            return m_Skeleton.QueryTransformIndex(m_ID);
        }
    }


    /// <summary>
    /// Skeleton Authoring Asset.
    /// </summary>
    //[CreateAssetMenu(fileName = "Skeleton", menuName = "DOTS/Animation/Skeleton", order = 1)]
    public class Skeleton : ScriptableObject
    {
        internal const char k_PathSeparator = '/';

        // Channels are kept separately in similar data structures to RigComponent
        // to ensure a tight conversion loop.
        // Quaternion needs to remain as interpolation is done separately.
        [SerializeField] private List<TransformChannel> m_TransformChannels = new List<TransformChannel>();
        [SerializeField] private List<TransformChannel> m_InactiveTransformChannels = new List<TransformChannel>();
        [SerializeField] private List<GenericChannel<int>> m_IntChannels = new List<GenericChannel<int>>();
        [SerializeField] private List<GenericChannel<float>> m_FloatChannels = new List<GenericChannel<float>>();
        [SerializeField] private List<GenericChannel<quaternion>> m_QuaternionChannels = new List<GenericChannel<quaternion>>();

        [SerializeField] private TransformBindingID m_Root = TransformBindingID.Invalid;
        static readonly RangeInt s_InvalidRange = new RangeInt(-1, -1);
        private RangeInt m_RootRange = s_InvalidRange;

        /// <summary>
        /// Delegate function that is called whenever a new transform channel is added.
        /// </summary>
        internal event Action<TransformBindingID> BoneAdded;
        /// <summary>
        /// Delegate function that is called whenever a transform channel values are changed.
        /// </summary>
        internal event Action<TransformBindingID> BoneModified;
        /// <summary>
        /// Delegate function that is called whenever a transform channel is removed.
        /// </summary>
        internal event Action<TransformBindingID> BoneRemoved;
        /// <summary>
        /// Delegate function that is called whenever a new generic channel is added.
        /// </summary>
        internal event Action<GenericBindingID> GenericPropertyAdded;
        /// <summary>
        /// Delegate function that is called whenever a generic channel value is changed.
        /// </summary>
        internal event Action<GenericBindingID> GenericPropertyModified;
        /// <summary>
        /// Delegate function that is called whenever a generic channel is removed.
        /// </summary>
        internal event Action<GenericBindingID> GenericPropertyRemoved;

        /// <summary>
        /// The root node of this skeleton.
        /// </summary>
        public TransformBindingID Root { get { return m_Root; } set { m_Root = value; InvalidateActiveTransformChannelsRange(); } }

        /// <summary>
        /// Retrieves all the Transform channels in this skeleton, both inactive and active, including those outside of the root.
        /// The transform channels will be returned sorted by path
        /// </summary>
        /// <param name="destination">The list the transform channels will be copied into</param>
        /// <param name="searchMode">Flags that determine which transforms to return.</param>
        public void GetAllTransforms(List<TransformChannel> destination, TransformChannelSearchMode searchMode = TransformChannelSearchMode.Default)
        {
            destination.Clear();

            bool invertActive = false;
            RangeInt activeRange = default;
            RangeInt inactiveRange = default;
            int count;

            // We have two lists, the "active list" and the "inactive list".
            // We also have a root that's set in the skeleton, which is one of its bones.
            // The "inactive list" is always _completely_ inactive.
            // However, outside the skeleton, we only treat the part of the "active list" that's a descendant of the root, as active.
            // So the part of the "active list" that is NOT a descendant of the root, can actually be considered inactive.
            // (We retain explicit active/inactive state in this way to preserve history in the Inspector-based workflow)

            if ((searchMode & TransformChannelSearchMode.InactiveAll) != 0)
            {
                // Add the complete contents of the inactive list
                inactiveRange = new RangeInt(0, m_InactiveTransformChannels.Count);
                count = inactiveRange.length;

                // We also need to add the part of the active list that's outside the root (we treat it as inactive)
                if ((searchMode & TransformChannelSearchMode.ActiveRootDescendants) == 0)
                {
                    // If we don't want to add the active part, we get the range of the part of the active list
                    // that's a descendant of the root (the only part we _actually_ treat as active) and
                    // invert the output (only add what's NOT in this range)
                    activeRange = GetActiveTransformChannelsRange();
                    count += m_TransformChannels.Count - activeRange.length;
                    invertActive = true;
                }
                else
                {
                    // If we also add the active part that's a descendent of the root, we essentially add the entire "active" list
                    activeRange = new RangeInt(0, m_TransformChannels.Count);
                    count += activeRange.length;
                }
            }
            else if ((searchMode & TransformChannelSearchMode.InactiveRootDescendants) != 0)
            {
                // Add the part of the inactive list which is a descendant of the root
                inactiveRange = GetImplicitlyInactiveTransformChannelsRange();
                count = inactiveRange.length;
                if ((searchMode & TransformChannelSearchMode.ActiveRootDescendants) != 0)
                {
                    // Add the part of the active list which is a descendant of the root
                    activeRange = GetActiveTransformChannelsRange();
                    count += activeRange.length;
                }
            }
            else if ((searchMode & TransformChannelSearchMode.ActiveRootDescendants) != 0)
            {
                // Only add the part of the active list which is a descendant of the root
                activeRange = GetActiveTransformChannelsRange();
                count = activeRange.length;
            }
            else
                // none of the flags has been set, so we've been asked to return ... nothing
                return;

            if (destination.Capacity < count)
                destination.Capacity = count;

            for (int i = inactiveRange.start; i < inactiveRange.end; i++)
                destination.Add(m_InactiveTransformChannels[i]);

            if (invertActive)
            {
                for (int i = 0; i < activeRange.start; i++) destination.Add(m_TransformChannels[i]);
                for (int i = activeRange.end; i < m_TransformChannels.Count; i++) destination.Add(m_TransformChannels[i]);
            }
            else
            {
                for (int i = activeRange.start; i < activeRange.end; i++) destination.Add(m_TransformChannels[i]);
            }
            destination.Sort();
        }

        /// <summary>
        /// Returns the number of active transform channels
        /// </summary>
        public int ActiveTransformChannelCount
        {
            get
            {
                return GetActiveTransformChannelsRange().length;
            }
        }

        /// <summary>
        /// Returns the number of inactive transform channels
        /// </summary>
        public int InactiveTransformChannelCount
        {
            get
            {
                return (m_TransformChannels.Count + m_InactiveTransformChannels.Count) - ActiveTransformChannelCount;
            }
        }

        /// <summary>
        /// Retrieves the Integer channels.
        /// </summary>
        public IReadOnlyList<GenericChannel<int>> IntChannels => m_IntChannels;

        /// <summary>
        /// Retrieves the Float channels.
        /// </summary>
        public IReadOnlyList<GenericChannel<float>> FloatChannels => m_FloatChannels;

        /// <summary>
        /// Retrieves the Quaternion channels.
        /// </summary>
        public IReadOnlyList<GenericChannel<quaternion>> QuaternionChannels => m_QuaternionChannels;


        /// <summary>
        /// Retrieves all generic channels.
        /// Generic channels are comprised of float, integer and quaternion channels.
        /// </summary>
        public void GetGenericChannels(List<IGenericChannel> channels)
        {
            var totalSize = m_FloatChannels.Count + m_IntChannels.Count + m_QuaternionChannels.Count;
            channels.Capacity = totalSize;

            for (int i = 0; i < m_FloatChannels.Count; ++i)
                channels.Add(m_FloatChannels[i]);

            for (int i = 0; i < m_IntChannels.Count; ++i)
                channels.Add(m_IntChannels[i]);

            for (int i = 0; i < m_QuaternionChannels.Count; ++i)
                channels.Add(m_QuaternionChannels[i]);
        }

        protected Skeleton()
        {
        }

        private void OnValidate()
        {
            // This ensures the range, which is not serialized, is updated on Undo
            InvalidateActiveTransformChannelsRange();
        }

        /// <summary>
        /// Clears all channels and labels.
        /// </summary>
        public void Clear()
        {
            m_RootRange = s_InvalidRange;

            m_TransformChannels.Clear();
            m_InactiveTransformChannels.Clear();
            m_IntChannels.Clear();
            m_FloatChannels.Clear();
            m_QuaternionChannels.Clear();

            InvalidateActiveTransformChannelsRange();
        }

        /// <summary>
        /// Adds or updates a transform channel with the specified ID.
        /// If its ancestors do not yet exist, they are also added with default <see cref="TransformChannelProperties"/>.
        /// </summary>
        /// <param name="id">A transform channel identifier.</param>
        public TransformChannelProperties this[TransformBindingID id]
        {
            get
            {
                var index = m_TransformChannels.FindIndex(c => c.ID.Equals(id));
                if (index >= 0)
                    return m_TransformChannels[index].Properties;
                index = m_InactiveTransformChannels.FindIndex(c => c.ID.Equals(id));
                if (index >= 0)
                    return m_InactiveTransformChannels[index].Properties;
                throw new KeyNotFoundException($"No transform channel defined for {id}");
            }
            set => AddOrSetTransformChannel(new TransformChannel { ID = id, Properties = value });
        }

        readonly List<TransformBindingID> m_ModifiedChannels = new List<TransformBindingID>(16);

        /// <summary>
        /// Adds or sets Transform bone channel in the current skeleton definition.
        /// Updates the current channel values if it already exist in skeleton definition.
        /// </summary>
        /// <param name="channel">Transform channel</param>
        /// <returns>Returns True if bone was added to channels.</returns>
        /// <exception cref="ArgumentException">channel.ID is invalid</exception>
        internal void AddOrSetTransformChannel(TransformChannel channel)
        {
            if (channel.ID.Equals(TransformBindingID.Invalid))
                throw new ArgumentException($"The Argument {nameof(channel.ID)} is not valid");

            int activeIndex = m_TransformChannels.FindIndex(c => c.ID.Equals(channel.ID));
            if (activeIndex == -1)
            {
                int inactiveIndex = m_InactiveTransformChannels.FindIndex(c => c.ID.Equals(channel.ID));
                if (inactiveIndex == -1)
                {
                    m_ModifiedChannels.Clear();

                    TransformBindingID parentID;
                    do
                    {
                        parentID = channel.ID.GetParent();
                        var comparableParentChannel = new TransformChannel { ID = parentID };

                        int insertIndex = -1;
                        List<TransformChannel> listToInsertInto = m_TransformChannels;

                        // if parent exists in active list, insert as normal
                        if (m_TransformChannels.BinarySearch(comparableParentChannel) >= 0)
                        {
                            insertIndex = m_TransformChannels.BinarySearch(channel);
                            InvalidateActiveTransformChannelsRange();
                        }
                        // otherwise, see if parent exists in inactive list
                        else if (m_InactiveTransformChannels.BinarySearch(comparableParentChannel) >= 0)
                        {
                            insertIndex = m_InactiveTransformChannels.BinarySearch(channel);
                            listToInsertInto = m_InactiveTransformChannels;
                        }

                        if (insertIndex < 0)
                            insertIndex = ~insertIndex;

                        listToInsertInto.Insert(insertIndex, channel);

                        m_ModifiedChannels.Add(channel.ID);

                        channel.ID = parentID;
                        channel.Properties = TransformChannelProperties.Default;
                    }
                    while (parentID != TransformBindingID.Invalid && !Contains(parentID, TransformChannelSearchMode.ActiveAndInactiveAll));

                    foreach (var ch in m_ModifiedChannels)
                        BoneAdded?.Invoke(ch);

                    InvalidateActiveTransformChannelsRange();
                }
                else
                {
                    m_InactiveTransformChannels[activeIndex] = channel;

                    BoneModified?.Invoke(channel.ID);
                }
            }
            else
            {
                m_TransformChannels[activeIndex] = channel;
                InvalidateActiveTransformChannelsRange();

                BoneModified?.Invoke(channel.ID);
            }
        }

        bool RemoveTransformChannelAndDescendants(List<TransformChannel> channels, TransformBindingID id, List<TransformBindingID> modifiedChannels)
        {
            // Remove transform channel associated to binding id.
            int index = channels.FindIndex(channel => channel.ID.Path.StartsWith(id.Path));
            var removedChannel = index != -1 && channels[index].ID.Equals(id);

            if (index == -1)
                return removedChannel;

            do
            {
                modifiedChannels.Add(channels[index].ID);
                channels.RemoveAt(index);
            }
            while (index < channels.Count && channels[index].ID.Path.StartsWith(id.Path));

            return removedChannel;
        }

        /// <summary>
        /// Remove a transform channel and all its descendants from the current skeleton definition.
        /// </summary>
        /// <param name="id">The Transform bone binding ID.</param>
        /// <returns>Returns True if bone with binding ID was removed from channels.</returns>
        /// <exception cref="ArgumentException">channel.ID is invalid</exception>
        public bool RemoveTransformChannelAndDescendants(TransformBindingID id)
        {
            if (id.Equals(TransformBindingID.Invalid))
                throw new ArgumentException($"The Argument {nameof(id)} is not valid");

            // Remove transform channel associated with binding id and descendants
            m_ModifiedChannels.Clear();
            bool hasRemovedBone = RemoveTransformChannelAndDescendants(m_TransformChannels, id, m_ModifiedChannels);
            if (hasRemovedBone)
                InvalidateActiveTransformChannelsRange();

            hasRemovedBone |= RemoveTransformChannelAndDescendants(m_InactiveTransformChannels, id, m_ModifiedChannels);

            foreach (var ch in m_ModifiedChannels)
                BoneRemoved?.Invoke(ch);

            InvalidateActiveTransformChannelsRange();

            return hasRemovedBone;
        }

        /// <summary>
        /// Adds or sets a generic property channel in the current skeleton definition.
        /// </summary>
        /// <param name="id">The unique binding ID describing the generic property.</param>
        /// <param name="defaultValue">The default value for the generic property.</param>
        /// <returns>Returns True if property has been added to channels. False otherwise.</returns>
        /// <exception cref="InvalidOperationException">Type must be a recognized animatable type.</exception>
        /// <exception cref="ArgumentException">id is invalid</exception>
        public void AddOrSetGenericProperty(GenericBindingID id, GenericPropertyVariant defaultValue)
        {
            if (id.Equals(GenericBindingID.Invalid))
                throw new ArgumentException($"The Argument {nameof(id)} is not valid");

            switch (defaultValue.Type)
            {
                case GenericPropertyType.Float:
                    AddOrSetGenericProperty(id, m_FloatChannels, defaultValue.Float);
                    break;
                case GenericPropertyType.Float2:
                    AddOrSetGenericTupleProperty(id, m_FloatChannels, new[] {defaultValue.Float2.x, defaultValue.Float2.y});
                    break;
                case GenericPropertyType.Float3:
                    AddOrSetGenericTupleProperty(id, m_FloatChannels, new[] {defaultValue.Float3.x, defaultValue.Float3.y, defaultValue.Float3.z});
                    break;
                case GenericPropertyType.Int:
                    AddOrSetGenericProperty(id, m_IntChannels, defaultValue.Int);
                    break;
                case GenericPropertyType.Quaternion:
                    AddOrSetGenericProperty(id, m_QuaternionChannels, defaultValue.Quaternion);
                    break;
                default:
                    throw new InvalidOperationException($"Invalid Type {defaultValue.Type}");
            }
        }

        private void AddOrSetGenericTupleProperty<T>(GenericBindingID id, List<GenericChannel<T>> channels, T[] defaultValues)
            where T : struct
        {
            for (int i = 0; i < defaultValues.Length; ++i)
            {
                AddOrSetGenericProperty(id[i], channels, defaultValues[i]);
            }
        }

        private void AddOrSetGenericProperty<T>(GenericBindingID id, List<GenericChannel<T>> channels, T defaultValue)
            where T : struct
        {
            var newChannel = new GenericChannel<T>
            {
                ID = id,
                DefaultValue = defaultValue
            };

            int index = channels.FindIndex(channel => channel.ID.Equals(id));
            if (index == -1)
            {
                var insertIndex = channels.BinarySearch(newChannel);
                if (insertIndex < 0) insertIndex = ~insertIndex;

                channels.Insert(insertIndex, newChannel);

                GenericPropertyAdded?.Invoke(newChannel.ID);
            }
            else
            {
                channels[index] = newChannel;

                GenericPropertyModified?.Invoke(newChannel.ID);
            }
        }

        /// <summary>
        /// Removes a generic property channel from the current skeleton definition.
        /// </summary>
        /// <param name="id">The generic property binding ID.</param>
        /// <returns>Returns True if property with binding ID was removed from channels.</returns>
        /// <exception cref="InvalidOperationException">Type must be a recognized animatable type.</exception>
        /// <exception cref="ArgumentException">id is invalid</exception>
        public bool RemoveGenericProperty(GenericBindingID id)
        {
            if (id.Equals(GenericBindingID.Invalid))
                throw new ArgumentException($"The Argument {nameof(id)} is not valid");

            switch (id.ValueType)
            {
                case GenericPropertyType.Float:
                    return RemoveGenericProperty(id, m_FloatChannels);
                case GenericPropertyType.Float2:
                    return RemoveGenericTupleProperty(id, m_FloatChannels);
                case GenericPropertyType.Float3:
                    return RemoveGenericTupleProperty(id, m_FloatChannels);
                case GenericPropertyType.Int:
                    return RemoveGenericProperty(id, m_IntChannels);
                case GenericPropertyType.Quaternion:
                    return RemoveGenericProperty(id, m_QuaternionChannels);
                default:
                    throw new InvalidOperationException($"Invalid Type {id.ValueType}");
            }
        }

        private bool RemoveGenericTupleProperty<T>(GenericBindingID id, List<GenericChannel<T>> channels)
            where T : struct
        {
            bool hasRemovedProperty = false;
            for (int i = 0; i < id.ValueType.GetNumberOfChannels(); ++i)
            {
                hasRemovedProperty |= RemoveGenericProperty(id[i], channels);
            }

            return hasRemovedProperty;
        }

        private bool RemoveGenericProperty<T>(GenericBindingID id, List<GenericChannel<T>> channels)
            where T : struct
        {
            bool hasRemovedProperty = false;

            int index = channels.FindIndex(channel => channel.ID.Equals(id));
            if (index != -1)
            {
                channels.RemoveAt(index);
                GenericPropertyRemoved?.Invoke(id);

                hasRemovedProperty = true;
            }

            return hasRemovedProperty;
        }

        /// <summary>
        /// Invalidates the range of the active transform channels.
        /// </summary>
        void InvalidateActiveTransformChannelsRange()
        {
            m_RootRange = s_InvalidRange;
        }

        void UpdateActiveTransformChannelsRange()
        {
            if (m_Root.Equals(TransformBindingID.Invalid))
            {
                m_RootRange = new RangeInt(0, m_TransformChannels.Count);
                return;
            }

            int index = m_TransformChannels.FindIndex((channel) => channel.ID.Equals(m_Root));
            if (index == -1)
            {
                m_RootRange = new RangeInt(0, m_TransformChannels.Count);
                return;
            }

            int lastIndex = m_TransformChannels.FindLastIndex((channel) => channel.ID.Path.StartsWith(m_Root.Path));
            m_RootRange = new RangeInt(index, lastIndex - index + 1);
        }

        /// <summary>
        /// Retrieves the range in the active transform channels based on the skeleton root node if set.
        /// </summary>
        /// <returns>Returns the range based on the skeleton root node.</returns>
        RangeInt GetActiveTransformChannelsRange()
        {
            if (m_RootRange.start == s_InvalidRange.start &&
                m_RootRange.length == s_InvalidRange.length)
            {
                UpdateActiveTransformChannelsRange();
            }

            return m_RootRange;
        }

        /// <summary>
        /// Retrieves the range in the inactive transform channels based on the skeleton root node if set.
        /// </summary>
        /// <returns>Returns the range based on the skeleton root node.</returns>
        RangeInt GetImplicitlyInactiveTransformChannelsRange()
        {
            if (m_Root.Equals(TransformBindingID.Invalid))
                return new RangeInt(0, m_InactiveTransformChannels.Count);

            int index = m_InactiveTransformChannels.FindIndex((channel) => channel.ID.Equals(m_Root));
            if (index == -1)
                return new RangeInt(0, m_InactiveTransformChannels.Count);

            int lastIndex = m_InactiveTransformChannels.FindLastIndex((channel) => channel.ID.Path.StartsWith(m_Root.Path));

            return new RangeInt(index, lastIndex - index + 1);
        }

        /// <summary>
        /// Queries the channel index that is identified by the specified id.
        /// Optionally, continuous channels can be queried by specifying a size.
        /// </summary>
        /// <param name="id">The transform binding id.</param>
        /// <param name="size">The continuous size of channels to query.</param>
        /// <returns>Returns the index if channels matching the query were found. -1 otherwise.</returns>
        /// <exception cref="ArgumentException">id is invalid</exception>
        internal int QueryTransformIndex(TransformBindingID id, uint size = 1)
        {
            if (id.Equals(TransformBindingID.Invalid))
                throw new ArgumentException($"The Argument {nameof(id)} is not valid");

            var range = GetActiveTransformChannelsRange();
            var index = m_TransformChannels.FindIndex(range.start, range.length, (channel) => channel.ID.Equals(id));
            if (index != -1)
                return ((index + size) <= range.end) ? index - range.start : -1;

            return -1;
        }

        /// <summary>
        /// Determines if the channel that is identified by the specified id is part of this skeleton.
        /// </summary>
        /// <param name="id">The transform binding id.</param>
        /// <param name="searchMode">Flags that determine which channels to check.</param>
        /// <returns>Returns the true if the id exists in this skeleton, false otherwise.</returns>
        /// <exception cref="ArgumentException">id is invalid</exception>
        public bool Contains(TransformBindingID id, TransformChannelSearchMode searchMode = TransformChannelSearchMode.Default)
        {
            if (id.Equals(TransformBindingID.Invalid))
                throw new ArgumentException("Invalid identifier", nameof(id));

            if ((searchMode & TransformChannelSearchMode.InactiveAll) != 0)
            {
                // The complete inactive list needs to be checked
                if (m_InactiveTransformChannels.FindIndex(channel => channel.ID.Equals(id)) != -1)
                    return true;

                // We also need to check the part of the active list that's outside the root (we treat it as inactive)
                if ((searchMode & TransformChannelSearchMode.ActiveRootDescendants) == 0)
                {
                    // If we don't want to check the active part, we get the range of the part of the "active list"
                    // that's a descendant of the root (the only part we _actually_ treat as active) and
                    // invert it (only check what's NOT in this range)
                    var activeRange = GetActiveTransformChannelsRange();
                    var index = m_TransformChannels.FindIndex(channel => channel.ID.Equals(id));
                    return (index != -1 &&
                        (index < activeRange.start || index > activeRange.end));
                }
                else
                {
                    // If we also check the active part that's a descendent of the root, we essentially
                    // need to check the entire "active list"
                    return (m_TransformChannels.FindIndex(channel => channel.ID.Equals(id)) != -1);
                }
            }
            else if ((searchMode & TransformChannelSearchMode.InactiveRootDescendants) != 0)
            {
                // Check the part of the inactive list which is a descendant of the root
                var inactiveRange = GetImplicitlyInactiveTransformChannelsRange();
                var index = m_InactiveTransformChannels.FindIndex(channel => channel.ID.Equals(id));
                if (index != -1 &&
                    index >= inactiveRange.start && index <= inactiveRange.end)
                    return true;
            }

            if ((searchMode & TransformChannelSearchMode.ActiveRootDescendants) != 0)
            {
                // Only check the part of the "active list" which is a descendant of the root
                var activeRange = GetActiveTransformChannelsRange();
                var index = m_TransformChannels.FindIndex(channel => channel.ID.Equals(id));
                if (index != -1 &&
                    index >= activeRange.start &&
                    index <= activeRange.end)
                    return true;
            }
            // else: none of the flags has been set, so we've been asked to return ... nothing
            return false;
        }

        /// <summary>
        /// Queries the channel index that is identified by the specified binding id.
        /// </summary>
        /// <param name="id">The generic binding id.</param>
        /// <returns>Returns a tuple combining the channel index and the channel type matching the query if successful. (-1, GenericChannelType.Invalid) otherwise.</returns>
        /// <exception cref="ArgumentException">id is invalid</exception>
        /// <exception cref="InvalidOperationException">Type must be a recognized animatable type.</exception>
        internal (int, GenericChannelType) QueryGenericPropertyIndex(GenericBindingID id)
        {
            return QueryGenericPropertyIndex(id, id.ValueType.GetNumberOfChannels());
        }

        /// <summary>
        /// Queries the channel index that is identified by the specified binding id.
        /// Optionally, continuous channels can be queried by specifying a size.
        /// </summary>
        /// <param name="id">The generic binding id.</param>
        /// <param name="size">The continuous size of channels to query.</param>
        /// <returns>Returns a tuple combining the channel index and the channel type matching the query if successful. (-1, GenericChannelType.Invalid) otherwise.</returns>
        /// <exception cref="ArgumentException">id is invalid</exception>
        /// <exception cref="InvalidOperationException">Type must be a recognized animatable type.</exception>
        internal (int, GenericChannelType) QueryGenericPropertyIndex(GenericBindingID id, uint size = 1)
        {
            if (id.Equals(GenericBindingID.Invalid))
                throw new ArgumentException($"The Argument {nameof(id)} is not valid");

            switch (id.ValueType)
            {
                case GenericPropertyType.Float:
                    return QueryGenericPropertyIndex(m_FloatChannels, id, size);
                case GenericPropertyType.Float2:
                    return QueryGenericPropertyIndex(m_FloatChannels, id.x, size);
                case GenericPropertyType.Float3:
                    return QueryGenericPropertyIndex(m_FloatChannels, id.x, size);
                case GenericPropertyType.Int:
                    return QueryGenericPropertyIndex(m_IntChannels, id, size);
                case GenericPropertyType.Quaternion:
                    // Quaternion indices need to be offset as they are appended to transform channels.
                    return QueryGenericPropertyIndex(m_QuaternionChannels, id, size);
                default:
                    throw new InvalidOperationException($"Invalid Type {id.ValueType}");
            }
        }

        private (int, GenericChannelType) QueryGenericPropertyIndex<T>(List<GenericChannel<T>> channels, GenericBindingID id, uint size)
            where T : struct
        {
            var index = channels.FindIndex(channel => channel.ID.Equals(id));
            if (index != -1)
                return (((index + size) <= channels.Count) ? index : -1, id.ChannelType);

            return (-1, id.ChannelType);
        }

        /// <summary>
        /// Queries the channels that are identified by the specified id.
        /// </summary>
        /// <param name="id">Channel ID</param>
        /// <param name="channels">Generic channels matching the binding id</param>
        /// <exception cref="ArgumentException">id is invalid</exception>
        /// <exception cref="InvalidOperationException">Type must be a recognized animatable type.</exception>
        public void QueryGenericPropertyChannels(GenericBindingID id, List<IGenericChannel> channels)
        {
            if (id.Equals(GenericBindingID.Invalid))
                throw new ArgumentException($"The Argument {nameof(id)} is not valid");

            var index = QueryGenericPropertyIndex(id).Item1;
            if (index != -1)
            {
                uint numberOfChannels = id.ValueType.GetNumberOfChannels();

                switch (id.ChannelType)
                {
                    case GenericChannelType.Float:
                        for (int i = 0; i < numberOfChannels; ++i)
                        {
                            channels.Add(m_FloatChannels[index + i]);
                        }
                        break;
                    case GenericChannelType.Int:
                        for (int i = 0; i < numberOfChannels; ++i)
                        {
                            channels.Add(m_IntChannels[index + i]);
                        }
                        break;
                    case GenericChannelType.Quaternion:
                        for (int i = 0; i < numberOfChannels; ++i)
                        {
                            channels.Add(m_QuaternionChannels[index + i]);
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Invalid Type {id.ValueType}");
                }
            }
        }

        /// <summary>
        /// Creates the DOTS representation of a rig.
        /// </summary>
        /// <param name="hasher">
        /// The hash function used to generate the ID of the rig channels.
        /// </param>
        /// <returns>The blob asset reference of a RigDefinition.</returns>
        public BlobAssetReference<RigDefinition> ToRigDefinition(BindingHashGenerator hasher = default)
        {
            BlobAssetReference<RigDefinition> rigDefinition;
            using (var rigBuilderData = ToRigBuilderData(hasher, Allocator.Temp))
            {
                rigDefinition = RigBuilder.CreateRigDefinition(rigBuilderData);
            }

            return rigDefinition;
        }

        /// <summary>
        /// Fills the lists of the RigBuilderData from the bones and custom channels of the RigComponent.
        /// </summary>
        /// <param name="hasher">
        /// The hash function used to generate the ID of the rig channels.
        /// </param>
        /// <param name="allocator">
        /// A member of the [Unity.Collections.Allocator](https://docs.unity3d.com/ScriptReference/Unity.Collections.Allocator.html)
        /// enumeration.
        /// It is used to allocate all the NativeLists inside the RigBuilderData.
        /// </param>
        /// <returns>
        /// The RigBuilderData with all its lists filled with the corresponding rig channels.
        /// </returns>
        /// <remarks>
        /// If you have your own rig representation, you just need to create a function like this one that fills
        /// a <see cref="RigBuilderData"/> and use it with <see cref="RigBuilder.CreateRigDefinition"/>.
        /// </remarks>
        public RigBuilderData ToRigBuilderData(BindingHashGenerator hasher = default, Allocator allocator = Allocator.Persistent)
        {
            if (!hasher.IsValid)
                hasher = BindingHashGlobals.DefaultHashGenerator;

            var transformChannelsRange = GetActiveTransformChannelsRange();

            var skeletonNodesCount = transformChannelsRange.length;
            var floatChannelsCount = m_FloatChannels.Count;
            var intChannelsCount = m_IntChannels.Count;
            var quaternionChannelsCount = m_QuaternionChannels.Count;

            var rigBuilderData = new RigBuilderData(allocator);
            rigBuilderData.SkeletonNodes.Capacity = skeletonNodesCount;
            rigBuilderData.RotationChannels.Capacity = quaternionChannelsCount;
            rigBuilderData.FloatChannels.Capacity = floatChannelsCount;
            rigBuilderData.IntChannels.Capacity = intChannelsCount;

            if (transformChannelsRange.length > 0)
            {
                for (int i = transformChannelsRange.start; i < transformChannelsRange.end; i++)
                {
                    var id = m_TransformChannels[i].ID;
                    var parentID = id.GetParent();

                    var parentIndex = -1;
                    for (int j = transformChannelsRange.start; j < i; ++j)
                    {
                        if (m_TransformChannels[j].ID.Equals(parentID))
                        {
                            parentIndex = j - transformChannelsRange.start;
                            break;
                        }
                    }

                    rigBuilderData.SkeletonNodes.Add(new SkeletonNode
                    {
                        Id = hasher.ToHash(id),
                        AxisIndex = -1,
                        LocalTranslationDefaultValue = m_TransformChannels[i].Properties.DefaultTranslationValue,
                        LocalRotationDefaultValue = m_TransformChannels[i].Properties.DefaultRotationValue,
                        LocalScaleDefaultValue = m_TransformChannels[i].Properties.DefaultScaleValue,
                        ParentIndex = parentIndex
                    });
                }
            }

            for (int i = 0; i < m_QuaternionChannels.Count; i++)
            {
                rigBuilderData.RotationChannels.Add(new LocalRotationChannel
                {
                    Id = hasher.ToHash(m_QuaternionChannels[i].ID),
                    DefaultValue = m_QuaternionChannels[i].DefaultValue
                });
            }

            for (int i = 0; i < m_FloatChannels.Count; i++)
            {
                rigBuilderData.FloatChannels.Add(new Unity.Animation.FloatChannel
                {
                    Id = hasher.ToHash(m_FloatChannels[i].ID),
                    DefaultValue = m_FloatChannels[i].DefaultValue
                });
            }

            for (int i = 0; i < m_IntChannels.Count; i++)
            {
                rigBuilderData.IntChannels.Add(new Unity.Animation.IntChannel
                {
                    Id = hasher.ToHash(m_IntChannels[i].ID),
                    DefaultValue = m_IntChannels[i].DefaultValue
                });
            }

            return rigBuilderData;
        }

        /// <summary>
        /// Sets all the transform channels that are inactive. This is used internally to being able to set all active/inactive bones in a single method.
        /// Beware that this method can be dangerous! It doesn't ensure that all bones form an unbroken chain from the root to every leaf.
        /// </summary>
        /// <param name="ids">A list of ids of bones, which are included in this skeleton, that need to be inactive. All remaining bones in this skeleton will be set as active.</param>
        /// <exception cref="ArgumentException">If any id is invalid or does not point to a bone in this skeleton an ArgumentException will be thrown. The skeleton will not be modified.</exception>
        internal void SetInactiveTransformChannels(IEnumerable<TransformBindingID> ids)
        {
            if (ids == null)
                throw new NullReferenceException(nameof(ids));
            m_TransformChannels.AddRange(m_InactiveTransformChannels);
            m_InactiveTransformChannels.Clear();
            var channelIndices = new List<int>();
            foreach (var id in ids)
            {
                int index = m_TransformChannels.FindIndex(channel => channel.ID.Equals(id));
                if (index == -1)
                    throw new ArgumentException($"{nameof(ids)} contains a {nameof(TransformBindingID)} that is not part of this {nameof(Skeleton)}", nameof(ids));
                channelIndices.Add(index);
            }
            channelIndices.Sort();
            for (int i = channelIndices.Count - 1; i >= 0; i--)
            {
                m_InactiveTransformChannels.Add(m_TransformChannels[i]);
                m_TransformChannels.RemoveAt(i);
            }
            m_TransformChannels.Sort();
            m_InactiveTransformChannels.Sort();
            InvalidateActiveTransformChannelsRange();
        }

        [Flags]
        enum BoneRelationship
        {
            None = 0,
            Self = 1,
            Ancestor = 2,
            Decendant = 4
        }

        static BoneRelationship GetPathRelationship(string path, string otherPath)
        {
            // We check if paths are subsets of each other, in which case they're an Ancestor or Descentant
            // Note: If the length of our path ends with a "/" in the path
            // of the other channel then it could potentially be a subset
            // (this is to prevent finding "Root/Left" in "Root/LeftFoot/")

            if (otherPath.Length > path.Length)
            {
                return (path.Length != 0 && // root is never a descendant
                    otherPath[path.Length] == k_PathSeparator && otherPath.StartsWith(path))
                    ? BoneRelationship.Decendant : BoneRelationship.None;
            }
            else if (otherPath.Length < path.Length)
            {
                return (otherPath.Length == 0 || // root is always an ancestor
                    (path[otherPath.Length] == k_PathSeparator && path.StartsWith(otherPath))) ?
                    BoneRelationship.Ancestor : BoneRelationship.None;
            }

            // Finally, check if the path is identical, in which case it's itself
            return (otherPath == path) ? BoneRelationship.Self : BoneRelationship.None;
        }

        // returns true if the given ids contain the root id
        static bool GetPathsFromIDs(Skeleton skeleton, IEnumerable<TransformBindingID> ids, List<string> foundPaths)
        {
            bool containsRoot = false;
            // Loop through all ids, check them & find their respective channels & paths
            foreach (var id in ids)
            {
                if (id.Equals(TransformBindingID.Invalid))
                    throw new ArgumentException($"The Argument {nameof(ids)} contains an id that is not valid.", nameof(ids));

                // We need to check if our id exists in our skeleton
                if (!skeleton.Contains(id, TransformChannelSearchMode.ActiveAndInactiveAll))
                    throw new ArgumentException($"The Argument {nameof(ids)} contains an id that is not part of this {nameof(Skeleton)}.", nameof(ids));

                var path = id.Path;
                // If our current channel is the root, then ALL channels need to be modified
                if (string.IsNullOrEmpty(path))
                    containsRoot = true;

                // Store our path, we'll use it to find all our descendants
                foundPaths.Add(path);
            }
            return containsRoot;
        }

        // Moves transform channels from one list to another, depending on if the relationship between their paths (an enum), is included in the compareRelationship flags
        static void MoveTransformChannelsBasedOnPathRelationship(List<string> comparePaths, BoneRelationship compareRelationship, List<TransformChannel> fromChannel, List<TransformChannel> toChannel)
        {
            // Since we move channels from one list to another, we don't actually need to check both lists.
            // So we go through all the channels in fromChannel, and see what it's relationship is to our channel.
            for (int i = fromChannel.Count - 1; i >= 0; i--)
            {
                var otherPath = fromChannel[i].ID.Path;
                foreach (var path in comparePaths)
                {
                    // This returns the relationship between the paths as an enum value
                    // (each relationship option has its own bit)
                    var relationship = GetPathRelationship(path, otherPath);

                    // If any of the bits in compareRelationship match ...
                    if ((relationship & compareRelationship) == BoneRelationship.None)
                        continue;

                    // ... we move the channel from fromChannel to toChannel
                    toChannel.Add(fromChannel[i]);
                    fromChannel.RemoveAt(i);

                    // since this value isn't part of fromChannel anymore,
                    // we can break out the inner loop.
                    break;
                }
            }
            toChannel.Sort();
            fromChannel.Sort();
        }

        // Temporaries to avoid GC allocations at runtime/editor time
        static readonly List<string> s_FoundPaths = new List<string>();
        static TransformBindingID[] s_BindingIDArrayOf1 = new TransformBindingID[1];

        /// <summary>
        /// Sets all the descendants and ancestors of all the given bones to active, including the bones themselves.
        /// It'll skip all siblings and siblings of our ancestors.
        /// This method ensures an unbroken chain from the root to all the descendants of the given bones in <paramref name="ids"/>.
        /// </summary>
        /// <param name="ids">The ids of the bones for which we set the descendants/ancestors to active. All the ids must point to bones in this skeleton or an exception will be thrown.</param>
        /// <exception cref="ArgumentException">If an id is invalid or does not point to a bone in this skeleton an ArgumentException will be thrown.</exception>
        /// <exception cref="ArgumentNullException">If <paramref name="ids"/> is null, an ArgumentNullException will be thrown.</exception>
        public void SetTransformChannelDescendantsAndAncestorsToActive(IEnumerable<TransformBindingID> ids)
        {
            if (ids == null)
                throw new ArgumentNullException(nameof(ids));

            // Find all the paths in the given ids, throws exception when we have invalid ids
            s_FoundPaths.Clear();
            bool containsRoot = GetPathsFromIDs(this, ids, s_FoundPaths);

            // Nothing to do ...
            if (s_FoundPaths.Count == 0)
                return;

            InvalidateActiveTransformChannelsRange();

            // If the array contains the root, then ALL channels need to be set to active
            if (containsRoot) { m_TransformChannels.AddRange(m_InactiveTransformChannels); m_InactiveTransformChannels.Clear(); m_TransformChannels.Sort(); return; }

            const BoneRelationship pathRelationship = BoneRelationship.Ancestor | BoneRelationship.Decendant | BoneRelationship.Self;

            // We want to move active transform channels to inactive transform channels, based on their path relationship
            MoveTransformChannelsBasedOnPathRelationship(s_FoundPaths, pathRelationship, m_InactiveTransformChannels, m_TransformChannels);
        }

        /// <summary>
        /// Sets all the descendants and the ancestors of a bone to active, including the bone itself. This method ensures an unbroken chain from the root to all the descendants of the given bone <paramref name="id"/>.
        /// </summary>
        /// <param name="id">The <paramref name="id"/> of the bone for which set the descendants/ancestors to active. This id must point to a bone in this skeleton or an exception will be thrown.</param>
        /// <exception cref="ArgumentException">If <paramref name="id"/> is invalid or does not point to a bone in this skeleton an ArgumentException will be thrown.</exception>
        public void SetTransformChannelDescendantsAndAncestorsToActive(TransformBindingID id)
        {
            s_BindingIDArrayOf1[0] = id;
            SetTransformChannelDescendantsAndAncestorsToActive(s_BindingIDArrayOf1);
        }

        /// <summary>
        /// Sets all the descendants of all the given bones to inactive, optionally including the given bones themselves.
        /// </summary>
        /// <param name="ids">The ids of all the given bones for which we set the descendants to inactive. All the ids must point to bones in this skeleton or an exception will be thrown.</param>
        /// <exception cref="ArgumentException">If an id in <paramref name="ids"/> is invalid or does not point to a bone in this skeleton an ArgumentException will be thrown.</exception>
        /// <exception cref="ArgumentNullException">If <paramref name="ids"/> is null, an ArgumentNullException will be thrown.</exception>
        public void SetTransformChannelDescendantsToInactive(IEnumerable<TransformBindingID> ids, bool includeSelf = true)
        {
            if (ids == null)
                throw new ArgumentNullException(nameof(ids));

            // Find all the paths in the given ids, throws exception when we have invalid ids
            s_FoundPaths.Clear();
            bool containsRoot = GetPathsFromIDs(this, ids, s_FoundPaths);
            if (s_FoundPaths.Count == 0)
                return;

            InvalidateActiveTransformChannelsRange();

            // If the array contains the root, then ALL channels need to be set to inactive
            if (containsRoot) { m_InactiveTransformChannels.AddRange(m_TransformChannels); m_TransformChannels.Clear(); m_InactiveTransformChannels.Sort(); return; }

            var pathRelationship = includeSelf ? BoneRelationship.Decendant | BoneRelationship.Self : BoneRelationship.Decendant;

            // We want to move active transform channels to inactive transform channels, based on their path relationship
            MoveTransformChannelsBasedOnPathRelationship(s_FoundPaths, pathRelationship, m_TransformChannels, m_InactiveTransformChannels);
        }

        /// <summary>
        /// Sets all the descendants of a bone to inactive, optionally including the bone itself.
        /// </summary>
        /// <param name="id">The id of the bone for which we set the descendants to inactive. This id must point to a bone in this skeleton or an exception will be thrown.</param>
        /// <param name="includeSelf">True if <paramref name="id"/> needs to set to inactive, false if it needs to be ignored. Default value is true.</param>
        /// <exception cref="ArgumentException">If the <paramref name="id"/> is invalid or does not point to a bone in this skeleton an ArgumentException will be thrown.</exception>
        public void SetTransformChannelDescendantsToInactive(TransformBindingID id, bool includeSelf = true)
        {
            s_BindingIDArrayOf1[0] = id;
            SetTransformChannelDescendantsToInactive(s_BindingIDArrayOf1, includeSelf);
        }

        /// <summary>
        /// Sets all the ancestors of the given bones to active.
        /// </summary>
        /// <param name="ids">The ids of all the given bones for which we set the ancestors to active. All the ids must point to bones in this skeleton or an exception will be thrown.</param>
        /// <param name="includeSelf">True if the bones included in <paramref name="ids"/> needs to be set to active, false if it needs to be ignored. Default value is true.</param>
        /// <exception cref="ArgumentException">If an id in <paramref name="ids"/> is invalid or does not point to a bone in this skeleton an ArgumentException will be thrown.</exception>
        /// <exception cref="ArgumentNullException">If <paramref name="ids"/> is null, an ArgumentNullException will be thrown.</exception>
        public void SetTransformChannelAncestorsToActive(IEnumerable<TransformBindingID> ids, bool includeSelf = true)
        {
            if (ids == null)
                throw new ArgumentNullException(nameof(ids));

            // Find all the paths in the given ids, throws exception when we have invalid ids
            s_FoundPaths.Clear();
            GetPathsFromIDs(this, ids, s_FoundPaths);
            if (s_FoundPaths.Count == 0)
                return;

            InvalidateActiveTransformChannelsRange();

            var pathRelationship = includeSelf ? (BoneRelationship.Ancestor | BoneRelationship.Self) : BoneRelationship.Ancestor;

            // We want to move active transform channels to inactive transform channels, based on their path relationship
            MoveTransformChannelsBasedOnPathRelationship(s_FoundPaths, pathRelationship, m_InactiveTransformChannels, m_TransformChannels);
        }

        /// <summary>
        /// Sets all the ancestors of a bone to active, optionally including the bone itself.
        /// </summary>
        /// <param name="id">The id of the bone for which set the ancestors to active. This id must point to a bone in this skeleton or an exception will be thrown.</param>
        /// <param name="includeSelf">True if <paramref name="id"/> needs to set to inactive, false if it needs to be ignored. Default value is true.</param>
        /// <exception cref="ArgumentException">If an id in <paramref name="id"/> is invalid or does not point to a bone in this skeleton an ArgumentException will be thrown.</exception>
        public void SetTransformChannelAncestorsToActive(TransformBindingID id, bool includeSelf = true)
        {
            s_BindingIDArrayOf1[0] = id;
            SetTransformChannelAncestorsToActive(s_BindingIDArrayOf1, includeSelf);
        }

        /// <summary>
        /// Queries a given transform channel/bone to see if it's active, inactive or does not exist in this skeleton
        /// </summary>
        public TransformChannelState GetTransformChannelState(TransformBindingID id)
        {
            if (id.Equals(TransformBindingID.Invalid))
                throw new ArgumentException($"The Argument {nameof(id)} is not valid");
            var channelIndex = m_TransformChannels.FindIndex(channel => channel.ID.Equals(id));
            if (channelIndex != -1)
            {
                var range = GetActiveTransformChannelsRange();
                if (channelIndex >= range.start && channelIndex <= range.end)
                    return TransformChannelState.Active;
                return TransformChannelState.Inactive;
            }
            channelIndex = m_InactiveTransformChannels.FindIndex(channel => channel.ID.Equals(id));
            if (channelIndex != -1)
                return TransformChannelState.Inactive;
            return TransformChannelState.DoesNotExist;
        }

        internal TransformBindingID GetPrecedingBindingID(TransformBindingID bindingID)
        {
            var range = GetActiveTransformChannelsRange();
            var channelIndex = m_TransformChannels.FindIndex((channel) => channel.ID.Equals(bindingID)) - 1;
            if (channelIndex >= range.start && channelIndex <= range.end)
                return m_TransformChannels[channelIndex].ID;
            return TransformBindingID.Invalid;
        }

        internal TransformBindingID GetParentBindingID(TransformBindingID bindingID)
        {
            if (bindingID == TransformBindingID.Root)
                return TransformBindingID.Invalid;

            var range = GetActiveTransformChannelsRange();
            var channelIndex = m_TransformChannels.FindIndex((channel) => channel.ID.Equals(bindingID));
            if (channelIndex < range.start || channelIndex > range.end)
                return TransformBindingID.Invalid;

            var desiredChannelPath = bindingID.Path;
            var desiredChannelPathLength = desiredChannelPath.LastIndexOf(k_PathSeparator);
            if (desiredChannelPathLength == -1)
                return TransformBindingID.Root;

            do
            {
                channelIndex--;
                if (channelIndex < range.start)
                    return TransformBindingID.Invalid;

                var channelPath = m_TransformChannels[channelIndex].ID.Path;
                if (channelPath.Length > desiredChannelPathLength)
                    continue;

                if (channelPath.Length < desiredChannelPathLength)
                    return TransformBindingID.Invalid;

                if (string.Compare(desiredChannelPath, 0, channelPath, 0, desiredChannelPathLength) == 0)
                    return m_TransformChannels[channelIndex].ID;
            }
            while (true);
        }

        internal TransformBindingID GetNextBindingID(TransformBindingID bindingID)
        {
            var range = GetActiveTransformChannelsRange();
            var channelIndex = m_TransformChannels.FindIndex((channel) => channel.ID.Equals(bindingID)) + 1;
            if (channelIndex >= range.start && channelIndex <= range.end)
                return m_TransformChannels[channelIndex].ID;
            return TransformBindingID.Invalid;
        }

        internal TransformBindingID GetBindingIDByRuntimeIndex(int runtimeIndex)
        {
            if (runtimeIndex < 0)
                return TransformBindingID.Invalid;
            var range = GetActiveTransformChannelsRange();
            if (runtimeIndex >= range.length)
                return TransformBindingID.Invalid;
            var channelIndex = runtimeIndex + range.start;
            return m_TransformChannels[channelIndex].ID;
        }
    }
}
