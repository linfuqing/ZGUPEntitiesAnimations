#if USE_MOTION_CLIP_DFG
using Unity.Jobs;
using Unity.Collections;
using Unity.Entities;
using Unity.Animation;
using Unity.DataFlowGraph;

namespace ZG
{
    public struct GraphHandle : System.IEquatable<GraphHandle>
    {
        // Intentionally not using SystemID for equality checks to minimize amount of process in NativeParallelMultiHashMap.
        // The SystemID is only used to validate if the GraphHandle can be employed with a specific AnimationSystemBase.
        internal readonly ushort SystemID;
        internal readonly ushort GraphID;

        internal GraphHandle(ushort systemId, ushort id)
        {
            SystemID = systemId;
            GraphID = id;
        }

        public bool Equals(GraphHandle other) =>
            this == other;

        public static bool operator ==(GraphHandle lhs, GraphHandle rhs) =>
            lhs.GraphID == rhs.GraphID;

        public static bool operator !=(GraphHandle lhs, GraphHandle rhs) =>
            lhs.GraphID != rhs.GraphID;

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            return obj is GraphHandle handle && Equals(handle);
        }

        public override int GetHashCode() => GraphID;

        internal bool IsValid(ushort systemID)
        {
            ValidateIdCompatibility(systemID);

            return GraphID != 0 && SystemID == systemID;
        }

        [System.Diagnostics.Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal void ValidateIdCompatibility(ushort systemID)
        {
            if (GraphID == 0)
                throw new System.ArgumentException("GraphHandle is invalid, use AnimationSystemBase.CreateManagedGraph() to create a valid handle");
            if (SystemID != systemID)
                throw new System.ArgumentException($"GraphHandle [SystemId: {SystemID}] is incompatible with this system [Id: {systemID}]");
        }
    }

    [UpdateInGroup(typeof(AnimationSystemGroup)), UpdateBefore(typeof(AnimationWriteTransformSystem)), UpdateAfter(typeof(AnimationReadTransformSystem))]
    public partial class AnimationGraphSystem : SystemBase
    {
        EntityQuery m_EvaluateGraphQuery;

        ushort m_GraphCounter;

        NativeParallelMultiHashMap<GraphHandle, NodeHandle> m_ManagedNodes;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EvaluateGraphQuery = GetEntityQuery(ComponentType.ReadOnly<Rig>(), ComponentType.ReadOnly<AnimatedData>());
        }

        protected override void OnUpdate()
        {
            Dependency = ScheduleGraphEvaluationJobs(Dependency);
        }

        public NodeSet Set { get; private set; }

        public int RefCount { get; private set; }

        public ushort systemID => (ushort)this.GetState().GetSystemID();

        public void AddRef()
        {
            if (RefCount++ == 0)
            {
                Set = new NodeSet(this);
                m_ManagedNodes = new NativeParallelMultiHashMap<GraphHandle, NodeHandle>(64, Allocator.Persistent);
            }
        }

        public void RemoveRef()
        {
            if (RefCount == 0)
                return;

            if (--RefCount == 0)
            {
                var nodes = m_ManagedNodes.GetValueArray(Allocator.Temp);
                for (int i = 0; i < nodes.Length; ++i)
                    Set.Destroy(nodes[i]);
                m_ManagedNodes.Dispose();

                Set.Dispose();
            }
        }

        /// <summary>
        /// Create a new GraphHandle in order to establish a logical grouping of nodes in a NodeSet.
        /// Note that by using <see cref="CreateNode(GraphHandle)"/> or <see cref="CreateNode(GraphHandle, Entity)"/>,
        /// nodes will either be automatically released when disposing the NodeSet
        /// or when you explicitly call <see cref="Dispose(GraphHandle)"/>.
        /// </summary>
        /// <returns>A unique handle to a graph part of this animation system</returns>
        public GraphHandle CreateGraph() =>
            new GraphHandle(systemID, ++m_GraphCounter);

        /// <summary>
        /// Creates a node associated with a GraphHandle. If either the GraphHandle or the animation system NodeSet is disposed, this node
        /// will be automatically released.
        /// </summary>
        /// <typeparam name="T">A known NodeDefinition</typeparam>
        /// <param name="handle">GraphHandle for this animation system created using <see cref="CreateGraph()"/></param>
        /// <returns>The node handle</returns>
        public NodeHandle<T> CreateNode<T>(GraphHandle handle)
            where T : NodeDefinition, new()
        {
            if (!handle.IsValid(systemID))
                return default;

            var node = Set.Create<T>();
            m_ManagedNodes.Add(handle, node);
            return node;
        }

        /// <summary>
        /// Creates a component node associated with a GraphHandle. If either the GraphHandle or the animation system NodeSet is disposed, this node
        /// will be automatically released.
        /// </summary>
        /// <param name="handle">GraphHandle for this animation system created using <see cref="CreateGraph()"/></param>
        /// <param name="entity">Entity</param>
        /// <returns>The component node handle</returns>
        public NodeHandle<ComponentNode> CreateNode(GraphHandle handle, Entity entity)
        {
            if (!handle.IsValid(systemID))
                return default;

            var node = Set.CreateComponentNode(entity);
            m_ManagedNodes.Add(handle, node);
            return node;
        }

        /// <summary>
        /// Disposes all nodes created using <see cref="CreateNode(GraphHandle)"/> or <see cref="CreateNode(GraphHandle, Entity)"/> that
        /// are associated with a GraphHandle.
        /// </summary>
        /// <param name="handle">GraphHandle for this animation system created using <see cref="CreateGraph()"/></param>
        /// <param name="entity">Entity</param>
        public void Dispose(GraphHandle handle)
        {
            if (Set == null || !handle.IsValid(systemID))
                return;

            if (!m_ManagedNodes.ContainsKey(handle))
                return;

            var values = m_ManagedNodes.GetValuesForKey(handle);
            while (values.MoveNext())
                Set.Destroy(values.Current);

            m_ManagedNodes.Remove(handle);
        }

        protected JobHandle ScheduleGraphEvaluationJobs(JobHandle inputDeps)
        {
            if (Set == null || m_EvaluateGraphQuery.CalculateEntityCount() == 0)
                return inputDeps;

            return Set.Update(inputDeps);
        }
    }
}
#endif