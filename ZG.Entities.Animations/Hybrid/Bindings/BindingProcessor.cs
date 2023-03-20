#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace Unity.Animation.Hybrid
{
    internal enum ChannelBindType
    {
        Translation,
        Rotation,
        Scale,
        Float,
        Integer,
        Unknown,
        Discard
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    internal class ComponentBindingProcessorAttribute : Attribute
    {
        public ComponentBindingProcessorAttribute(Type component)
        {
            ComponentType = component;
        }

        public Type ComponentType { get; }
    }

    internal interface IComponentBindingProcessor
    {
        ChannelBindType Execute(EditorCurveBinding binding);
    }

    /// <summary>
    /// Base binding processor class. This can be used to implement a binding processor
    /// for a known or custom Unity component.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal abstract class ComponentBindingProcessor<T> : IComponentBindingProcessor
        where T : Component
    {
        public ChannelBindType Execute(EditorCurveBinding binding)
        {
            if (typeof(T) != binding.type)
                return ChannelBindType.Unknown;

            return Process(binding);
        }

        protected abstract ChannelBindType Process(EditorCurveBinding binding);
    }

    /// <summary>
    /// Singleton binding processor helper. At initialization, it builds a dictionary of known
    /// binding processors it finds from the TypeCache.
    /// When it executes on an EditorCurveBinding, it uses the binding type to find a corresponding processor
    /// in the dictionary. When one isn't found, it uses reflection to reason about component bindings.
    /// </summary>
    internal sealed class BindingProcessor
    {
        const BindingFlags k_PropertyFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static Dictionary<Type, IComponentBindingProcessor> s_ComponentBindingProcessors;

        static BindingProcessor()
        {
            var processorTypes = TypeCache.GetTypesWithAttribute<ComponentBindingProcessorAttribute>();
            s_ComponentBindingProcessors = new Dictionary<Type, IComponentBindingProcessor>(processorTypes.Count);
            foreach (var processorType in processorTypes)
            {
                var attr = processorType.GetCustomAttribute<ComponentBindingProcessorAttribute>();
                if (!typeof(Component).IsAssignableFrom(attr.ComponentType))
                {
                    UnityEngine.Debug.LogError($"ComponentBindingProcessor [{processorType.ToString()}] is targeting an invalid component type [{attr.ComponentType.ToString()}]");
                    continue;
                }

                if (s_ComponentBindingProcessors.TryGetValue(attr.ComponentType, out var existingProcessor))
                {
                    UnityEngine.Debug.LogError($"Duplicate ComponentBindingProcessor [{processorType.ToString()}] found for Component [{attr.ComponentType.ToString()}]! The [{existingProcessor.GetType().ToString()}] ComponentBindingProcessor was already defined.");
                }
                else
                {
                    s_ComponentBindingProcessors.Add(attr.ComponentType, (IComponentBindingProcessor)Activator.CreateInstance(processorType));
                }
            }
        }

        BindingProcessor() {}

        internal static BindingProcessor Instance { get; } = new BindingProcessor();

        internal ChannelBindType Execute(EditorCurveBinding binding)
        {
            if (binding.type == null)
                return ChannelBindType.Unknown;

            if (s_ComponentBindingProcessors.TryGetValue(binding.type, out var componentProcessor))
                return componentProcessor.Execute(binding);

            if (binding.type == typeof(GameObject))
            {
                if (binding.propertyName == "m_IsActive")
                    return ChannelBindType.Integer;
            }

            if (binding.isDiscreteCurve)
                return ChannelBindType.Integer;

            // Extrapolate rig channel type based on propertyName and reflection. This strategy works well
            // for custom components but reflection is limited with Unity component types defined in C++.
            Type extrapolatedType = binding.type;
            foreach (var propertyName in binding.propertyName.Split('.'))
            {
                var fieldInfo = extrapolatedType.GetField(propertyName, k_PropertyFlags);
                if (fieldInfo != null)
                    extrapolatedType = fieldInfo.FieldType;
                else
                {
                    var propertyInfo = extrapolatedType.GetProperty(propertyName, k_PropertyFlags);
                    if (propertyInfo != null)
                        extrapolatedType = propertyInfo.PropertyType;
                }

                if (extrapolatedType != null)
                {
                    if (extrapolatedType == typeof(bool) || extrapolatedType == typeof(int) || extrapolatedType.IsEnum)
                        return ChannelBindType.Integer;
                    else if (extrapolatedType == typeof(float))
                        return ChannelBindType.Float;
                }
            }

            return ChannelBindType.Unknown;
        }
    }
}

#endif
