#if UNITY_EDITOR

using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using Unity.Assertions;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;


namespace Unity.Animation.Hybrid
{
    /// <summary>
    /// Static class grouping help functions for ClipBuilder
    /// </summary>
    public static partial class ClipBuilderUtils
    {
        static string GetBasePropertyName(this EditorCurveBinding binding)
        {
            // Remove the ".x" at the end.
            return binding.propertyName.Remove(binding.propertyName.Length - 2);
        }

        static UnityEngine.AnimationCurve GetEditorComponentCurve(this UnityEngine.AnimationClip clip, EditorCurveBinding binding, string basePropertyName, char componentSuffix)
        {
            binding.propertyName = $"{basePropertyName}.{componentSuffix}";
            var curve = AnimationUtility.GetEditorCurve(clip, binding);

            Assert.IsTrue(curve != null);

            return curve;
        }

        /// <summary>
        /// Convert UnityEngine AnimationClip to ClipBuilder. All animation curves from <paramref name="sourceClip"/> will be re-sampled at the sourceClip frame rate
        /// and result curves will be stored inside ClipBuilder. Also all SynchronizationTags from the AnimationClip will be copied and stored inside the ClipBuilder.
        /// </summary>
        /// <param name="allocator">Allocator policy for the curves inside the ClipBuilder</param>
        /// <param name="hasher">An optional BindingHashGenerator can be specified to compute unique animation binding IDs. When not specified the <see cref="BindingHashGlobals.DefaultHashGenerator"/> is used.</param>
        /// <returns></returns>
        public static ClipBuilder ToClipBuilder(this UnityEngine.AnimationClip sourceClip, Allocator allocator, BindingHashGenerator hasher = default)
        {
            if (sourceClip == null)
                return default;

            if (!hasher.IsValid)
                hasher = BindingHashGlobals.DefaultHashGenerator;

            var clipBuilder = new ClipBuilder(sourceClip.length, sourceClip.frameRate, allocator);
            sourceClip.ExtractSynchronizationTag(clipBuilder.m_SynchronizationTags);

            var srcBindings = AnimationUtility.GetCurveBindings(sourceClip);
            foreach (var binding in srcBindings)
            {
                switch (BindingProcessor.Instance.Execute(binding))
                {
                    case ChannelBindType.Translation:
                        AddTranslationCurveFromClip(clipBuilder, sourceClip, binding, hasher, allocator);
                        break;
                    case ChannelBindType.Rotation:
                        AddQuaternionCurveFromClip(clipBuilder, sourceClip, binding, hasher, allocator);
                        break;
                    case ChannelBindType.Scale:
                        AddScaleCurveFromClip(clipBuilder, sourceClip, binding, hasher, allocator);
                        break;
                    case ChannelBindType.Float:
                        AddFloatCurveFromClip(clipBuilder, sourceClip, binding, hasher, allocator);
                        break;
                    case ChannelBindType.Integer:
                        AddIntCurveFromClip(clipBuilder, sourceClip, binding, hasher, allocator);
                        break;
                    case ChannelBindType.Discard:
                        break;
                    case ChannelBindType.Unknown:
                    default:
                        UnityEngine.Debug.LogWarning($"Unsupported binding type {binding.type.ToString()} : path = {binding.path}, propertyName = {binding.propertyName}");
                        break;
                }
            }

            return clipBuilder;
        }

        static UnsafeList<float> ResampleEditorFloatCurve(this UnityEngine.AnimationClip clip, EditorCurveBinding binding, ClipBuilder clipBuilder, Allocator allocator)
        {
            var editorCurve = AnimationUtility.GetEditorCurve(clip, binding);

            var resampledCurve = new UnsafeList<float>(clipBuilder.SampleCount, allocator);
            resampledCurve.Length = clipBuilder.SampleCount;

            var value = 0.0f;

            for (var frameIter = 0; frameIter < clipBuilder.FrameCount; frameIter++)
            {
                float time = frameIter / clipBuilder.SampleRate;

                value = editorCurve.Evaluate(time);
                resampledCurve[frameIter] = value;
            }

            // adjust last frame value to match value at duration
            var valueAtDuration = editorCurve.Evaluate(clipBuilder.Duration);
            resampledCurve[clipBuilder.FrameCount] = Core.AdjustLastFrameValue(value, valueAtDuration, clipBuilder.m_LastFrameError);

            return resampledCurve;
        }

        static UnsafeList<float3> ResampleEditorFloat3Curve(this UnityEngine.AnimationClip clip, EditorCurveBinding binding, ClipBuilder clipBuilder, Allocator allocator)
        {
            var property = binding.GetBasePropertyName();

            var xCurve = clip.GetEditorComponentCurve(binding, property, 'x');
            var yCurve = clip.GetEditorComponentCurve(binding, property, 'y');
            var zCurve = clip.GetEditorComponentCurve(binding, property, 'z');

            var resampledCurve = new UnsafeList<float3>(clipBuilder.SampleCount, allocator);
            resampledCurve.Length = clipBuilder.SampleCount;

            var xValue = 0.0f;
            var yValue = 0.0f;
            var zValue = 0.0f;

            for (var frameIter = 0; frameIter < clipBuilder.FrameCount; frameIter++)
            {
                float time = frameIter / clipBuilder.SampleRate;

                xValue = xCurve.Evaluate(time);
                yValue = yCurve.Evaluate(time);
                zValue = zCurve.Evaluate(time);

                resampledCurve[frameIter] = new float3(xValue, yValue, zValue);
            }

            // adjust last frame value to match value at duration
            var xValueAtDuration = xCurve.Evaluate(clipBuilder.Duration);
            var yValueAtDuration = yCurve.Evaluate(clipBuilder.Duration);
            var zValueAtDuration = zCurve.Evaluate(clipBuilder.Duration);

            var t = new float3();
            t.x = Core.AdjustLastFrameValue(xValue, xValueAtDuration, clipBuilder.m_LastFrameError);
            t.y = Core.AdjustLastFrameValue(yValue, yValueAtDuration, clipBuilder.m_LastFrameError);
            t.z = Core.AdjustLastFrameValue(zValue, zValueAtDuration, clipBuilder.m_LastFrameError);
            resampledCurve[clipBuilder.FrameCount] = t;

            return resampledCurve;
        }

        static UnsafeList<quaternion> ResampleEditorQuaternionCurve(this UnityEngine.AnimationClip clip, EditorCurveBinding binding, ClipBuilder clipBuilder, Allocator allocator)
        {
            var property = binding.GetBasePropertyName();

            var resampledCurve = new UnsafeList<quaternion>(clipBuilder.SampleCount, allocator);
            resampledCurve.Length = clipBuilder.SampleCount;

            if (property.Contains("Euler"))
            {
                quaternion EulerToQuaternion(float x, float y, float z)
                {
                    return quaternion.Euler(math.radians(x), math.radians(y), math.radians(z));
                }

                var xEulerCurve = clip.GetEditorComponentCurve(binding, property, 'x');
                var yEulerCurve = clip.GetEditorComponentCurve(binding, property, 'y');
                var zEulerCurve = clip.GetEditorComponentCurve(binding, property, 'z');

                var xEulerValue = 0.0f;
                var yEulerValue = 0.0f;
                var zEulerValue = 0.0f;

                for (var frameIter = 0; frameIter < clipBuilder.FrameCount; frameIter++)
                {
                    float time = frameIter / clipBuilder.SampleRate;

                    xEulerValue = xEulerCurve.Evaluate(time);
                    yEulerValue = yEulerCurve.Evaluate(time);
                    zEulerValue = zEulerCurve.Evaluate(time);

                    resampledCurve[frameIter] = EulerToQuaternion(xEulerValue, yEulerValue, zEulerValue);
                }

                // adjust last frame value to match value at duration
                var xValueAtDuration = xEulerCurve.Evaluate(clipBuilder.Duration);
                var yValueAtDuration = yEulerCurve.Evaluate(clipBuilder.Duration);
                var zValueAtDuration = zEulerCurve.Evaluate(clipBuilder.Duration);

                xEulerValue = Core.AdjustLastFrameValue(xEulerValue, xValueAtDuration, clipBuilder.m_LastFrameError);
                yEulerValue = Core.AdjustLastFrameValue(yEulerValue, yValueAtDuration, clipBuilder.m_LastFrameError);
                zEulerValue = Core.AdjustLastFrameValue(zEulerValue, zValueAtDuration, clipBuilder.m_LastFrameError);

                resampledCurve[clipBuilder.FrameCount] = EulerToQuaternion(xEulerValue, yEulerValue, zEulerValue);
            }
            else
            {
                var xCurve = clip.GetEditorComponentCurve(binding, property, 'x');
                var yCurve = clip.GetEditorComponentCurve(binding, property, 'y');
                var zCurve = clip.GetEditorComponentCurve(binding, property, 'z');
                var wCurve = clip.GetEditorComponentCurve(binding, property, 'w');

                var xValue = 0.0f;
                var yValue = 0.0f;
                var zValue = 0.0f;
                var wValue = 0.0f;

                for (var frameIter = 0; frameIter < clipBuilder.FrameCount; frameIter++)
                {
                    var time = frameIter / clipBuilder.SampleRate;

                    xValue = xCurve.Evaluate(time);
                    yValue = yCurve.Evaluate(time);
                    zValue = zCurve.Evaluate(time);
                    wValue = wCurve.Evaluate(time);

                    quaternion qValue = new quaternion(xValue, yValue, zValue, wValue);
                    qValue = math.normalize(qValue);

                    resampledCurve[frameIter] = qValue;
                }

                // adjust last frame value to match value at duration
                var xValueAtDuration = xCurve.Evaluate(clipBuilder.Duration);
                var yValueAtDuration = yCurve.Evaluate(clipBuilder.Duration);
                var zValueAtDuration = zCurve.Evaluate(clipBuilder.Duration);
                var wValueAtDuration = wCurve.Evaluate(clipBuilder.Duration);

                var q = new quaternion();
                q.value.x = Core.AdjustLastFrameValue(xValue, xValueAtDuration, clipBuilder.m_LastFrameError);
                q.value.y = Core.AdjustLastFrameValue(yValue, yValueAtDuration, clipBuilder.m_LastFrameError);
                q.value.z = Core.AdjustLastFrameValue(zValue, zValueAtDuration, clipBuilder.m_LastFrameError);
                q.value.w = Core.AdjustLastFrameValue(wValue, wValueAtDuration, clipBuilder.m_LastFrameError);
                resampledCurve[clipBuilder.FrameCount] = q;
            }

            return resampledCurve;
        }

        static void AddTranslationCurveFromClip(this ClipBuilder clipBuilder, UnityEngine.AnimationClip clip, EditorCurveBinding binding, BindingHashGenerator hasher, Allocator allocator)
        {
            var translation = clip.ResampleEditorFloat3Curve(binding, clipBuilder, allocator);
            clipBuilder.AddCurve(translation, hasher.ToHash(ToTransformBindingID(binding)), ref clipBuilder.m_TranslationCurves);
        }

        static void AddScaleCurveFromClip(ClipBuilder clipBuilder, UnityEngine.AnimationClip clip, EditorCurveBinding binding, BindingHashGenerator hasher, Allocator allocator)
        {
            var scale = clip.ResampleEditorFloat3Curve(binding, clipBuilder, allocator);
            clipBuilder.AddCurve(scale, hasher.ToHash(ToTransformBindingID(binding)), ref clipBuilder.m_ScaleCurves);
        }

        static void AddQuaternionCurveFromClip(ClipBuilder clipBuilder, UnityEngine.AnimationClip clip, EditorCurveBinding binding, BindingHashGenerator hasher, Allocator allocator)
        {
            var quaternionCurve = clip.ResampleEditorQuaternionCurve(binding, clipBuilder, allocator);
            clipBuilder.AddCurve(quaternionCurve, hasher.ToHash(ToTransformBindingID(binding)), ref clipBuilder.m_QuaternionCurves);
        }

        static void AddFloatCurveFromClip(ClipBuilder clipBuilder, UnityEngine.AnimationClip clip, EditorCurveBinding binding, BindingHashGenerator hasher, Allocator allocator)
        {
            var floatCurve = clip.ResampleEditorFloatCurve(binding, clipBuilder, allocator);
            clipBuilder.AddCurve(floatCurve, hasher.ToHash(ToGenericBindingID(binding)), ref clipBuilder.m_FloatCurves);
        }

        static void AddIntCurveFromClip(ClipBuilder clipBuilder, UnityEngine.AnimationClip clip, EditorCurveBinding binding, BindingHashGenerator hasher, Allocator allocator)
        {
            var intCurve = clip.ResampleEditorFloatCurve(binding, clipBuilder, allocator);
            clipBuilder.AddCurve(intCurve, hasher.ToHash(ToGenericBindingID(binding)), ref clipBuilder.m_IntCurves);
        }

        static void CopyFloatCurveToClipSamples(UnsafeList<float> curve, int curveIndex, in int curveCount, ref BlobBuilderArray<float> samples)
        {
            for (var frameIter = 0; frameIter < curve.Length; frameIter++)
            {
                samples[curveIndex + frameIter * curveCount] = curve[frameIter];
            }
        }

        static void CopyFloat3CurveToClipSamples(UnsafeList<float3> curve, int curveIndex, in int curveCount, ref BlobBuilderArray<float> samples)
        {
            for (var frameIter = 0; frameIter < curve.Length; frameIter++)
            {
                var sampleIndex = curveIndex + frameIter * curveCount;
                var sample = curve[frameIter];
                samples[sampleIndex] = sample.x;
                samples[sampleIndex + 1] = sample.y;
                samples[sampleIndex + 2] = sample.z;
            }
        }

        static void CopyQuaternionCurveToClipSamples(UnsafeList<quaternion> curve, int curveIndex, in int curveCount, ref BlobBuilderArray<float> samples)
        {
            for (var frameIter = 0; frameIter < curve.Length; frameIter++)
            {
                var sampleIndex = curveIndex + frameIter * curveCount;
                var sample = curve[frameIter].value;
                samples[sampleIndex] = sample.x;
                samples[sampleIndex + 1] = sample.y;
                samples[sampleIndex + 2] = sample.z;
                samples[sampleIndex + 3] = sample.w;
            }
        }

        /// <summary>
        /// Convert ClipBuilder to Clip by copying directly all keyframes from ClipBuilder to Clip Samples interleaved array. All SynchronizationTags from the ClipBuilder
        /// will be copied and stored inside the out Clip as well.
        /// </summary>
        /// <returns></returns>
        public static BlobAssetReference<Clip> ToDenseClip(this ClipBuilder clipBuilder)
        {
            var blobBuilder = new BlobBuilder(Allocator.Temp);
            ref var clip = ref blobBuilder.ConstructRoot<Clip>();

            clip.Duration = clipBuilder.Duration;
            clip.SampleRate = clipBuilder.SampleRate;

            clip.Bindings = clip.CreateBindingSet(clipBuilder.m_TranslationCurves.Count(), clipBuilder.m_QuaternionCurves.Count(), clipBuilder.m_ScaleCurves.Count(), clipBuilder.m_FloatCurves.Count(), clipBuilder.m_IntCurves.Count());

            var curveCount = clip.Bindings.CurveCount;
            var sampleCount = clip.Bindings.CurveCount * (clip.FrameCount + 1);
            var samples = blobBuilder.Allocate(ref clip.Samples, sampleCount);

            var translationBindings = blobBuilder.Allocate(ref clip.Bindings.TranslationBindings, clipBuilder.m_TranslationCurves.Count());
            var index = 0;
            foreach (var pair in clipBuilder.m_TranslationCurves)
            {
                var curveIndex = clip.Bindings.TranslationSamplesOffset + index * BindingSet.TranslationKeyFloatCount;
                CopyFloat3CurveToClipSamples(pair.Value, curveIndex, curveCount, ref samples);
                translationBindings[index++] = pair.Key;
            }

            var rotationBindings = blobBuilder.Allocate(ref clip.Bindings.RotationBindings, clipBuilder.m_QuaternionCurves.Count());
            index = 0;
            foreach (var pair in clipBuilder.m_QuaternionCurves)
            {
                var curveIndex = clip.Bindings.RotationSamplesOffset + index * BindingSet.RotationKeyFloatCount;
                CopyQuaternionCurveToClipSamples(pair.Value, curveIndex, curveCount, ref samples);
                rotationBindings[index++] = pair.Key;
            }

            var scaleBindings = blobBuilder.Allocate(ref clip.Bindings.ScaleBindings, clipBuilder.m_ScaleCurves.Count());
            index = 0;
            foreach (var pair in clipBuilder.m_ScaleCurves)
            {
                var curveIndex = clip.Bindings.ScaleSamplesOffset + index * BindingSet.ScaleKeyFloatCount;
                CopyFloat3CurveToClipSamples(pair.Value, curveIndex, curveCount, ref samples);
                scaleBindings[index++] = pair.Key;
            }

            var floatBindings = blobBuilder.Allocate(ref clip.Bindings.FloatBindings, clipBuilder.m_FloatCurves.Count());
            index = 0;
            foreach (var pair in clipBuilder.m_FloatCurves)
            {
                var curveIndex = clip.Bindings.FloatSamplesOffset + index * BindingSet.FloatKeyFloatCount;
                CopyFloatCurveToClipSamples(pair.Value, curveIndex, curveCount, ref samples);
                floatBindings[index++] = pair.Key;
            }

            var intBindings = blobBuilder.Allocate(ref clip.Bindings.IntBindings, clipBuilder.m_IntCurves.Count());
            index = 0;
            foreach (var pair in clipBuilder.m_IntCurves)
            {
                var curveIndex = clip.Bindings.IntSamplesOffset + index * BindingSet.IntKeyFloatCount;
                CopyFloatCurveToClipSamples(pair.Value, curveIndex, curveCount, ref samples);
                intBindings[index++] = pair.Key;
            }

            ClipConversion.FillSynchronizationTag(clipBuilder.m_SynchronizationTags, ref blobBuilder, ref clip);

            var outputClip = blobBuilder.CreateBlobAssetReference<Clip>(Allocator.Persistent);

            blobBuilder.Dispose();

            outputClip.Value.m_HashCode = (int)HashUtils.ComputeHash(ref outputClip);

            return outputClip;
        }

        /// <summary>
        /// Sample ClipBuilder into <paramref name="stream"/> at given <paramref name="time"/>. All curves from ClipBuilder whose property hash
        /// matches with <paramref name="stream"/> rig bindings will be sampled at <paramref name="time"/> (linearly interpolating between keyframes)
        /// and written to the stream. This result will be identical to converting ClipBuilder to Clip, Clip to ClipInstance, and then calling
        /// EvaluateClip() on ClipInstance.
        /// </summary>
        /// <param name="time">Sampling time in seconds, will be clamped between 0 and ClipBuilder <code>Duration</code></param>
        /// <param name="stream">Output animation stream</param>
        /// <param name="additive">if 0, stream will be initialized with default rig values, otherwise all stream values will be initialized with 0.</param>
        public static void EvaluateClip(ClipBuilder data, float time, ref AnimationStream stream, int additive)
        {
            stream.ValidateIsNotNull();

            if (additive == 0)
            {
                stream.ResetToDefaultValues();
                stream.ClearMasks();
            }
            else
            {
                stream.ResetToZero();
            }

            ref var bindings = ref stream.Rig.Value.Bindings;

            var keyframe = Core.ClipKeyframe.Create(ref data, time);

            // Rotations
            {
                for (var i = 0; i < bindings.RotationBindings.Length; ++i)
                {
                    var propertyHash = bindings.RotationBindings[i];
                    if (data.m_QuaternionCurves.TryGetValue(propertyHash, out var quaternionCurve))
                    {
                        var leftKey = quaternionCurve[keyframe.Left];
                        var rightKey = quaternionCurve[keyframe.Right];

                        stream.SetLocalToParentRotation(i, mathex.lerp(leftKey, rightKey, keyframe.Weight));
                    }
                }
            }

            // Translations
            {
                for (var i = 0; i < bindings.TranslationBindings.Length; ++i)
                {
                    var propertyHash = bindings.TranslationBindings[i];
                    if (data.m_TranslationCurves.TryGetValue(propertyHash, out var translationCurve))
                    {
                        var leftKey = translationCurve[keyframe.Left];
                        var rightKey = translationCurve[keyframe.Right];

                        stream.SetLocalToParentTranslation(i, math.lerp(leftKey, rightKey, keyframe.Weight));
                    }
                }
            }

            // Scales
            {
                for (var i = 0; i < bindings.ScaleBindings.Length; ++i)
                {
                    var propertyHash = bindings.ScaleBindings[i];
                    if (data.m_ScaleCurves.TryGetValue(propertyHash, out var scaleCurve))
                    {
                        var leftKey = scaleCurve[keyframe.Left];
                        var rightKey = scaleCurve[keyframe.Right];

                        stream.SetLocalToParentScale(i, math.lerp(leftKey, rightKey, keyframe.Weight));
                    }
                }
            }

            // Floats
            {
                for (var i = 0; i < bindings.FloatBindings.Length; ++i)
                {
                    var propertyHash = bindings.FloatBindings[i];
                    if (data.m_FloatCurves.TryGetValue(propertyHash, out var floatCurve))
                    {
                        var leftKey = floatCurve[keyframe.Left];
                        var rightKey = floatCurve[keyframe.Right];

                        stream.SetFloat(i, math.lerp(leftKey, rightKey, keyframe.Weight));
                    }
                }
            }

            // Ints
            {
                for (var i = 0; i < bindings.IntBindings.Length; ++i)
                {
                    var propertyHash = bindings.IntBindings[i];
                    if (data.m_IntCurves.TryGetValue(propertyHash, out var intCurve))
                    {
                        var key = math.select(keyframe.Left, keyframe.Right, keyframe.Weight > 0.5f);
                        stream.SetInt(i, (int)intCurve[key]);
                    }
                }
            }
        }

        static internal TransformBindingID ToTransformBindingID(EditorCurveBinding binding) =>
            new TransformBindingID { Path = binding.path };

        static internal GenericBindingID ToGenericBindingID(EditorCurveBinding binding) =>
            new GenericBindingID { Path = binding.path, AttributeName = binding.propertyName, ComponentType = binding.type };
    }
}

#endif
