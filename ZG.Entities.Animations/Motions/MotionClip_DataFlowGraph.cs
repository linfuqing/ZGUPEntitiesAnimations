#if USE_MOTION_CLIP_DFG
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Animation;
using Unity.DataFlowGraph;
using Unity.DataFlowGraph.Attributes;

namespace ZG
{
    public struct MotionClipState : ICleanupComponentData
    {
        public int layerCount;
        public GraphHandle graph;
        public NodeHandle<ComponentNode> entityNode;
        public NodeHandle<MotionClipMixerNode> mixerNode;
        public NodeHandle<LayerMixerNode> layerMixerNode;
        public NodeHandle<KernelPassThroughNodeBufferFloat> passThroughNode;
    }

    public class MotionClipMixerNode :
        SimulationKernelNodeDefinition<MotionClipMixerNode.SimPorts, MotionClipMixerNode.KernelDefs>, IRigContextHandler<MotionClipMixerNode.Data>
    {
        private struct Data : INodeData, IMsgHandler<Rig>, IMsgHandler<StringHash>, IMsgHandler<MotionClipTransform>, IMsgHandler<ushort>
        {
            private KernelData __kernelData;

            public void HandleMessage(MessageContext context, in Rig rig)
            {
                __kernelData.rigDefinition = rig;

                context.UpdateKernelData(__kernelData);

                if (rig.Value.IsCreated)
                {
                    var handle = context.Handle;

                    var set = context.Set;
                    int layerCount = math.max(__kernelData.layerCount, 1);
                    if (layerCount > 1)
                    {
                        int weightDataSize = Core.WeightDataSize(rig.Value);
                        for (int i = 0; i < layerCount; ++i)
                            set.SetBufferSize(
                                handle,
                                (OutputPortID)KernelPorts.layerWeightMaskOutput,
                                i,
                                Buffer<WeightData>.SizeRequest(weightDataSize));
                    }

                    int streamSize = rig.Value.Value.Bindings.StreamSize;

                    for (int i = 0; i < layerCount; ++i)
                        set.SetBufferSize(
                            handle,
                            (OutputPortID)KernelPorts.output,
                            i,
                            Buffer<AnimatedData>.SizeRequest(streamSize));

                    __kernelData.HandleMessage(set, handle);
                }
            }

            public void HandleMessage(MessageContext context, in StringHash rootID)
            {
                __kernelData.rootIndex = Core.FindBindingIndex(ref __kernelData.rigDefinition.Value.Skeleton.Ids, rootID);

                context.UpdateKernelData(__kernelData);
            }

            public void HandleMessage(MessageContext context, in MotionClipTransform rootTransform)
            {
                __kernelData.rootTransform = rootTransform;

                context.UpdateKernelData(__kernelData);
            }

            public void HandleMessage(MessageContext context, in ushort layerCount)
            {
                int numLayers = math.max(layerCount, 1);
                if (numLayers != __kernelData.layerCount)
                {
                    context.Set.SetPortArraySize(context.Handle, (OutputPortID)KernelPorts.output, numLayers);
                    context.Set.SetPortArraySize(context.Handle, (OutputPortID)KernelPorts.layerWeightMaskOutput, numLayers > 1 ? numLayers : 0);

                    var set = context.Set;
                    var handle = context.Handle;
                    if (__kernelData.rigDefinition.IsCreated)
                    {
                        int streamSize = __kernelData.rigDefinition.Value.Bindings.StreamSize;
                        for (int i = __kernelData.layerCount; i < numLayers; ++i)
                            set.SetBufferSize(
                                handle,
                                (OutputPortID)KernelPorts.output,
                                i,
                                Buffer<AnimatedData>.SizeRequest(streamSize));

                        if (numLayers > 1)
                        {
                            int weightDataSize = Core.WeightDataSize(__kernelData.rigDefinition);
                            for (int i = __kernelData.layerCount; i < numLayers; ++i)
                                set.SetBufferSize(
                                    handle,
                                    (OutputPortID)KernelPorts.layerWeightMaskOutput,
                                    i,
                                    Buffer<WeightData>.SizeRequest(weightDataSize));
                        }
                    }

                    __kernelData.layerCount = numLayers;

                    __kernelData.HandleMessage(set, handle);

                    context.UpdateKernelData(__kernelData);
                }
            }
        }

        public struct SimPorts : ISimulationPortDefinition
        {
            [PortDefinition(guid: "a4a8fea063a9494a9b6f5a63f5625060", isHidden: true)]
            public MessageInput<MotionClipMixerNode, Rig> rig;

            [PortDefinition(guid: "a4a8fea063a9494a9b6f5a63f5625061", isHidden: true)]
            public MessageInput<MotionClipMixerNode, StringHash> rootID;

            [PortDefinition(guid: "a4a8fea063a9494a9b6f5a63f5625061", isHidden: true)]
            public MessageInput<MotionClipMixerNode, MotionClipTransform> rootTransform;

            [PortDefinition(guid: "61f0790526654efd839f0f59bc5bf623", isHidden: true)]
            public MessageInput<MotionClipMixerNode, ushort> layerCount;
        }

        public struct KernelDefs : IKernelPortDefinition
        {
            public DataInput<MotionClipMixerNode, Buffer<MotionClipInstance>> clipInstanceInput;
            public DataInput<MotionClipMixerNode, Buffer<MotionClipTime>> clipTimeInput;
            public DataInput<MotionClipMixerNode, Buffer<MotionClipWeight>> clipWeightInput;

            public DataInput<MotionClipMixerNode, Buffer<MotionClipLayerWeightMask>> layerWeightMaskInput;

            [PortDefinition(guid: "3bb0eda4c6fd432bb9547765dceafacc", displayName: "Default Pose", description: "Override default animation stream values when sum of weights is less than 1")]
            public DataInput<MotionClipMixerNode, Buffer<AnimatedData>> defaultPoseInput;

            [PortDefinition(guid: "1d6bfa5b22004f918dd21120bae997aa", description: "Resulting animation stream")]
            public PortArray<DataOutput<MotionClipMixerNode, Buffer<AnimatedData>>> output;

            [PortDefinition(guid: "396b45c9cedc14f1a64e5d37f025713f", description: "Resulting layer wegiths")]
            public PortArray<DataOutput<MotionClipMixerNode, Buffer<WeightData>>> layerWeightMaskOutput;

            internal DataOutput<MotionClipMixerNode, Buffer<AnimatedData>> _streamStack;
            internal DataOutput<MotionClipMixerNode, Buffer<MotionClipUtility.MixerNode>> _mixerStack;
        }

        public struct KernelData : IKernelData
        {
            public int layerCount;
            public int rootIndex;
            public MotionClipTransform rootTransform;
            public BlobAssetReference<RigDefinition> rigDefinition;

            public void HandleMessage(NodeSetAPI set, NodeHandle handle)
            {
                if (rigDefinition.IsCreated)
                {
                    int streamSize = rigDefinition.Value.Bindings.StreamSize;
                    set.SetBufferSize(
                        handle,
                        (OutputPortID)KernelPorts._streamStack,
                        Buffer<AnimatedData>.SizeRequest(streamSize * (layerCount + 1)));

                    set.SetBufferSize(
                        handle,
                        (OutputPortID)KernelPorts._mixerStack,
                        Buffer<MotionClipUtility.MixerNode>.SizeRequest(layerCount));
                }
            }
        }

        [BurstCompile]
        public struct Kernel : IGraphKernel<KernelData, KernelDefs>
        {
            private struct Context : IMotionClipcContext
            {
                public RenderContext value;

                public NativeSlice<Buffer<AnimatedData>> animations;
                public NativeSlice<Buffer<WeightData>> weightMasks;

                public NativeArray<AnimatedData> ResolveAnimation(int layer) => animations[layer].ToNative(value);

                public NativeArray<WeightData> ResolveWeightMask(int layer) => weightMasks[layer].ToNative(value);
            }

            public void Execute(RenderContext context, in KernelData data, ref KernelDefs ports)
            {
                Context wrapper;
                wrapper.value = context;
                wrapper.animations = context.Resolve(ref ports.output);
                wrapper.weightMasks = context.Resolve(ref ports.layerWeightMaskOutput);
                var streamStack = new MotionClipUtility.StreamStack<AnimatedData>(context.Resolve(ref ports._streamStack));
                var mixerStack = new MotionClipUtility.StreamStack<MotionClipUtility.MixerNode>(context.Resolve(ref ports._mixerStack));
                var defaultPoseInputStream = AnimationStream.CreateReadOnly(data.rigDefinition, context.Resolve(ports.defaultPoseInput));
                wrapper.Execute(
                    wrapper.animations.Length,
                    data.rootIndex,
                    data.rootTransform,
                    data.rigDefinition,
                    context.Resolve(ports.clipInstanceInput),
                    context.Resolve(ports.clipTimeInput),
                    context.Resolve(ports.clipWeightInput),
                    context.Resolve(ports.layerWeightMaskInput),
                    ref streamStack,
                    ref mixerStack, 
                    ref defaultPoseInputStream);
            }
        }

        InputPortID ITaskPort<IRigContextHandler>.GetPort(NodeHandle handle) =>
            (InputPortID)SimulationPorts.rig;
    }

    [AlwaysUpdateSystem, UpdateInGroup(typeof(InitializationSystemGroup)), UpdateBefore(typeof(InitializeAnimation)), UpdateAfter(typeof(EntityObjectSystemGroup))]
    public partial class MotionClipGraphSystem : SystemBase
    {
        [BurstCompile]
        private struct Init : IJobParallelFor
        {
            public double time;

            [ReadOnly]
            public NativeArray<Entity> entityArray;
            [ReadOnly]
            public NativeArray<MotionClipState> sources;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<MotionClipState> destinations;
            [NativeDisableParallelForRestriction]
            public ComponentLookup<MotionClipSafeTime> safeTimes;

            public void Execute(int index)
            {
                Entity entity = entityArray[index];
                destinations[entity] = sources[index];

                var safeTime = safeTimes[entity];
                safeTime.value = math.max(safeTime.value, time);
                safeTimes[entity] = safeTime;
            }
        }

        public struct DestroyCommand
        {
            public Entity entity;
        }

        public struct CreateCommand
        {
            public Entity entity;
            public Rig rig;
            public StringHash rootID;
            public MotionClipTransform rootTransform;
        }

        public int innerloopBatchCount = 1;

        private AnimationGraphSystem __graphSystem;

        private EntityQuery __groupToCreate;
        private EntityQuery __groupToDestroy;

        private EntityCommandPool<DestroyCommand>.Context __destroyCommander;
        private EntityCommandPool<CreateCommand>.Context __createCommander;

        public EntityCommandPool<CreateCommand> createCommander => __createCommander.pool;
        public EntityCommandPool<DestroyCommand> destroyCommander => __destroyCommander.pool;

        public static MotionClipState CreateGraph(
            in Entity entity,
            in Rig rig,
            AnimationGraphSystem graphSystem)
        {
            MotionClipState state;

            state.layerCount = -1;

            state.graph = graphSystem.CreateGraph();
            state.entityNode = graphSystem.CreateNode(state.graph, entity);
            state.mixerNode = graphSystem.CreateNode<MotionClipMixerNode>(state.graph);
            state.layerMixerNode = default;// graphSystem.CreateNode<LayerMixerNode>(state.graph);
            state.passThroughNode = default;// graphSystem.CreateNode<KernelPassThroughNodeBufferFloat>(state.graph);

            var set = graphSystem.Set;

            set.Connect(state.entityNode, state.mixerNode, MotionClipMixerNode.KernelPorts.clipInstanceInput);
            set.Connect(state.entityNode, state.mixerNode, MotionClipMixerNode.KernelPorts.clipTimeInput);
            set.Connect(state.entityNode, state.mixerNode, MotionClipMixerNode.KernelPorts.clipWeightInput);
            set.Connect(state.entityNode, state.mixerNode, MotionClipMixerNode.KernelPorts.layerWeightMaskInput);

            //set.Connect(state.entityNode, state.mixerNode, MotionClipMixerNode.KernelPorts.defaultPoseInput);

            set.SendMessage(state.mixerNode, MotionClipMixerNode.SimulationPorts.rig, rig);

            return state;
        }

        public static bool SetLayers(
            ref MotionClipState state,
            in Rig rig,
            in NativeArray<MotionClipLayer> layers,
            AnimationGraphSystem graphSystem)
        {
            int numLayers = layers.Length;
            if (numLayers == 0)
                return SetLayer(ref state, rig, graphSystem);

            var set = graphSystem.Set;
            if (state.layerCount < 1)
            {
                if (state.layerCount == 0)
                {
                    set.Disconnect(state.passThroughNode, KernelPassThroughNodeBufferFloat.KernelPorts.Output, state.entityNode, ComponentNode.Input<AnimatedData>());
                    set.Destroy(state.passThroughNode);
                }

                state.layerMixerNode = graphSystem.CreateNode<LayerMixerNode>(state.graph);

                set.SendMessage(state.layerMixerNode, LayerMixerNode.SimulationPorts.Rig, rig);

                set.Connect(state.layerMixerNode, LayerMixerNode.KernelPorts.Output, state.entityNode, NodeSet.ConnectionType.Feedback);
            }

            if (numLayers < state.layerCount)
            {
                for (int i = numLayers; i < state.layerCount; ++i)
                {
                    set.Disconnect(state.mixerNode, MotionClipMixerNode.KernelPorts.output, i, state.layerMixerNode, LayerMixerNode.KernelPorts.Inputs, i);
                    set.Disconnect(state.mixerNode, MotionClipMixerNode.KernelPorts.layerWeightMaskOutput, i, state.layerMixerNode, LayerMixerNode.KernelPorts.WeightMasks, i);
                }
            }

            set.SendMessage(state.mixerNode, MotionClipMixerNode.SimulationPorts.layerCount, (ushort)numLayers);
            set.SendMessage(state.layerMixerNode, LayerMixerNode.SimulationPorts.LayerCount, (ushort)numLayers);

            MotionClipLayer layer;
            for (int i = 0; i < numLayers; ++i)
            {
                layer = layers[i];

                if (state.layerCount <= i)
                {
                    set.Connect(state.mixerNode, MotionClipMixerNode.KernelPorts.output, i, state.layerMixerNode, LayerMixerNode.KernelPorts.Inputs, i);

                    if (numLayers > 1)
                        set.Connect(state.mixerNode, MotionClipMixerNode.KernelPorts.layerWeightMaskOutput, i, state.layerMixerNode, LayerMixerNode.KernelPorts.WeightMasks, i);
                }

                set.SetData(state.layerMixerNode, LayerMixerNode.KernelPorts.BlendingModes, i, layer.blendingMode);
                set.SetData(state.layerMixerNode, LayerMixerNode.KernelPorts.Weights, i, layer.weight);
            }

            state.layerCount = numLayers;

            return true;
        }

        public static bool SetLayer(
            ref MotionClipState state,
            in Rig rig,
            AnimationGraphSystem graphSystem)
        {
            if (state.layerCount == 0)
                return false;

            var set = graphSystem.Set;
            if (state.layerCount > 0)
            {
                for (int i = 0; i < state.layerCount; ++i)
                {
                    set.Disconnect(state.mixerNode, MotionClipMixerNode.KernelPorts.output, i, state.layerMixerNode, LayerMixerNode.KernelPorts.Inputs, i);

                    if (state.layerCount > 1)
                        set.Disconnect(state.mixerNode, MotionClipMixerNode.KernelPorts.layerWeightMaskOutput, i, state.layerMixerNode, LayerMixerNode.KernelPorts.WeightMasks, i);
                }

                set.Disconnect(state.layerMixerNode, LayerMixerNode.KernelPorts.Output, state.entityNode, ComponentNode.Input<AnimatedData>());

                set.Destroy(state.layerMixerNode);
            }

            state.layerCount = 0;

            set.SendMessage(state.mixerNode, MotionClipMixerNode.SimulationPorts.layerCount, (ushort)1);

            state.passThroughNode = graphSystem.CreateNode<KernelPassThroughNodeBufferFloat>(state.graph);

            set.SendMessage(state.passThroughNode, KernelPassThroughNodeBufferFloat.SimulationPorts.BufferSize, rig.Value.Value.Bindings.StreamSize);

            set.Connect(state.mixerNode, MotionClipMixerNode.KernelPorts.output, 0, state.passThroughNode, KernelPassThroughNodeBufferFloat.KernelPorts.Input);
            set.Connect(state.passThroughNode, KernelPassThroughNodeBufferFloat.KernelPorts.Output, state.entityNode, NodeSet.ConnectionType.Feedback);

            /*set.SendMessage(state.layerMixerNode, LayerMixerNode.SimulationPorts.LayerCount, (ushort)1);

            set.SetData(state.layerMixerNode, LayerMixerNode.KernelPorts.BlendingModes, 0, BlendingMode.Override);
            set.SetData(state.layerMixerNode, LayerMixerNode.KernelPorts.Weights, 0, 1.0f);

            set.Connect(state.layerMixerNode, LayerMixerNode.KernelPorts.Output, state.entityNode, NodeSet.ConnectionType.Feedback);*/

            return true;
        }

        public MotionClipGraphSystem()
        {
            __destroyCommander = new EntityCommandPool<DestroyCommand>.Context(Allocator.Persistent);
            __createCommander = new EntityCommandPool<CreateCommand>.Context(Allocator.Persistent);
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            __graphSystem = World.GetOrCreateSystemManaged<AnimationGraphSystem>();

            // Increase the reference count on the graph system so it knows
            // that we want to use it.
            __graphSystem.AddRef();

            __groupToCreate = GetEntityQuery(ComponentType.ReadOnly<MotionClipTime>(), ComponentType.Exclude<MotionClipSafeTime>());
            __groupToDestroy = GetEntityQuery(ComponentType.ReadOnly<MotionClipState>(), ComponentType.Exclude<MotionClip>());
        }

        protected override void OnDestroy()
        {
            __createCommander.Dispose();
            __destroyCommander.Dispose();

            if (__graphSystem != null)
                __graphSystem.RemoveRef();

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            var entityManager = EntityManager;
            entityManager.AddComponent<MotionClipSafeTime>(__groupToCreate);

            if (!__groupToDestroy.IsEmpty)
            {
                CompleteDependency();

                using (var states = __groupToDestroy.ToComponentDataArray<MotionClipState>(Allocator.Temp))
                {
                    int numStates = states.Length;
                    for (int i = 0; i < numStates; ++i)
                        __graphSystem.Dispose(states[i].graph);
                }

                entityManager.RemoveComponent<MotionClipState>(__groupToDestroy);
            }

            if (!__destroyCommander.isEmpty)
            {
                var states = GetComponentLookup<MotionClipState>(true);
                var entitiesToDestroy = new NativeList<Entity>(Allocator.Temp);
                while (__destroyCommander.TryDequeue(out var command))
                {
                    /*if (!states.HasComponent(command.entity))
                        continue;*/

                    __graphSystem.Dispose(states[command.entity].graph);

                    //UnityEngine.Debug.Log($"Destroy {command.entity}");
                    entitiesToDestroy.Add(command.entity);
                }

                entityManager.RemoveComponent<MotionClipState>(entitiesToDestroy.AsArray());
                entitiesToDestroy.Dispose();
            }

            var set = __graphSystem.Set;
            if (!__createCommander.isEmpty)
            {
                var entitiesToCreate = new NativeList<Entity>(Allocator.TempJob);
                var states = new NativeList<MotionClipState>(Allocator.TempJob);
                MotionClipState state;
                while (__createCommander.TryDequeue(out var command))
                {
                    if (//entityManager.HasComponent<MotionClipState>(command.entity) || 
                        !entityManager.HasComponent<MotionClipSafeTime>(command.entity))
                        continue;

                    //UnityEngine.Debug.Log($"Create {command.entity}");
                    entitiesToCreate.Add(command.entity);

                    state = CreateGraph(
                        command.entity,
                        command.rig,
                        __graphSystem);

                    set.SendMessage(
                        state.mixerNode,
                        MotionClipMixerNode.SimulationPorts.rootID,
                        command.rootID);

                    set.SendMessage(
                        state.mixerNode,
                        MotionClipMixerNode.SimulationPorts.rootTransform,
                        command.rootTransform);

                    if (entityManager.HasComponent<MotionClipLayer>(command.entity))
                        SetLayers(ref state, command.rig, entityManager.GetBuffer<MotionClipLayer>(command.entity).AsNativeArray(), __graphSystem);
                    else
                        SetLayer(ref state, command.rig, __graphSystem);

                    states.Add(state);
                }

                var entityArray = entitiesToCreate.AsArray();
                entityManager.AddComponent<MotionClipState>(entityArray);

                Init init;
                init.time = World.Time.ElapsedTime + UnityEngine.Time.maximumDeltaTime;
                init.entityArray = entityArray;
                init.sources = states.AsArray();
                init.destinations = GetComponentLookup<MotionClipState>();
                init.safeTimes = GetComponentLookup<MotionClipSafeTime>();
                var jobHandle = init.Schedule(entitiesToCreate.Length, innerloopBatchCount, Dependency);

                Dependency = JobHandle.CombineDependencies(entitiesToCreate.Dispose(jobHandle), states.Dispose(jobHandle));
            }

            Entities.ForEach((ref MotionClipState state, in Rig rig, in DynamicBuffer<MotionClipLayer> layers) =>
            {
                SetLayers(ref state, rig, layers.AsNativeArray(), __graphSystem);
            })
                .WithChangeFilter<MotionClipLayer>()
                .WithoutBurst()
                .Run();
        }
    }
}
#endif