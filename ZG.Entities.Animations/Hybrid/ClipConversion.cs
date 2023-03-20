#if UNITY_EDITOR

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEditor;

namespace Unity.Animation.Hybrid
{
    public static partial class ClipConversion
    {
        /// <summary>
        /// Converts a UnityEngine AnimationClip to a DOTS dense clip.
        ///
        /// NOTE: This extension is not supported in the Player.
        /// </summary>
        /// <param name="sourceClip">The UnityEngine.AnimationClip to convert to a DOTS dense clip format.</param>
        /// <param name="hasher">An optional BindingHashGenerator can be specified to compute unique animation binding IDs. When not specified the <see cref="BindingHashGlobals.DefaultHashGenerator"/> is used.</param>
        /// <returns>Returns a dense clip BlobAssetReference</returns>
        public static BlobAssetReference<Clip> ToDenseClip(this AnimationClip sourceClip, BindingHashGenerator hasher = default)
        {
            if (sourceClip == null)
                return default;

            if (!hasher.IsValid)
                hasher = BindingHashGlobals.DefaultHashGenerator;

            var srcBindings = AnimationUtility.GetCurveBindings(sourceClip);

            var translationBindings = new List<EditorCurveBinding>();
            var rotationBindings = new List<EditorCurveBinding>();
            var scaleBindings = new List<EditorCurveBinding>();
            var floatBindings = new List<EditorCurveBinding>();
            var intBindings = new List<EditorCurveBinding>();

            foreach (var binding in srcBindings)
            {
                switch (BindingProcessor.Instance.Execute(binding))
                {
                    case ChannelBindType.Translation:
                        translationBindings.Add(binding);
                        break;
                    case ChannelBindType.Rotation:
                        rotationBindings.Add(binding);
                        break;
                    case ChannelBindType.Scale:
                        scaleBindings.Add(binding);
                        break;
                    case ChannelBindType.Float:
                        floatBindings.Add(binding);
                        break;
                    case ChannelBindType.Integer:
                        intBindings.Add(binding);
                        break;
                    case ChannelBindType.Discard:
                        break;
                    case ChannelBindType.Unknown:
                    default:
                        UnityEngine.Debug.LogWarning($"Unsupported binding type {binding.type.ToString()} : path = {binding.path}, propertyName = {binding.propertyName}");
                        break;
                }
            }

            var syncTags = new NativeList<SynchronizationTag>(Allocator.Temp);
            sourceClip.ExtractSynchronizationTag(syncTags);

            var blobBuilder = new BlobBuilder(Allocator.Temp);
            ref var clip = ref blobBuilder.ConstructRoot<Clip>();

            CreateBindings(translationBindings, rotationBindings, scaleBindings, floatBindings, intBindings, ref blobBuilder, ref clip, hasher);
            FillCurves(sourceClip, translationBindings, rotationBindings, scaleBindings, floatBindings, intBindings, ref blobBuilder, ref clip);
            FillSynchronizationTag(syncTags, ref blobBuilder, ref clip);

            var outputClip = blobBuilder.CreateBlobAssetReference<Clip>(Allocator.Persistent);

            blobBuilder.Dispose();
            syncTags.Dispose();

            outputClip.Value.m_HashCode = (int)HashUtils.ComputeHash(ref outputClip);

            return outputClip;
        }

        internal static void ExtractSynchronizationTag(this AnimationClip sourceClip, NativeList<SynchronizationTag> syncTags)
        {
            Core.ValidateArgumentIsCreated(syncTags);

            var duration = sourceClip.length != 0.0f ? sourceClip.length : 1.0f;

            var animationEvents = AnimationUtility.GetAnimationEvents(sourceClip);
            for (int i = 0; i < animationEvents.Length; i++)
            {
                var go = animationEvents[i].objectReferenceParameter as GameObject;
                if (go != null)
                {
                    var syncTag = go.GetComponent(typeof(ISynchronizationTag)) as ISynchronizationTag;
                    if (syncTag != null)
                    {
                        syncTags.Add(new SynchronizationTag { NormalizedTime = animationEvents[i].time / duration, Type = syncTag.Type, State = syncTag.State});
                    }
                }
            }
        }

        internal static void FillSynchronizationTag(NativeList<SynchronizationTag> syncTags, ref BlobBuilder blobBuilder, ref Clip clip)
        {
            Core.ValidateArgumentIsCreated(syncTags);

            if (syncTags.Length == 0)
                return;

            var arrayBuilder = blobBuilder.Allocate(ref clip.SynchronizationTags, syncTags.Length);
            for (var i = 0; i != syncTags.Length; ++i)
            {
                arrayBuilder[i] = syncTags[i];
            }
        }

        private static void CreateBindings(IReadOnlyList<EditorCurveBinding> translationBindings,
            IReadOnlyList<EditorCurveBinding> rotationBindings, IReadOnlyList<EditorCurveBinding> scaleBindings,
            IReadOnlyList<EditorCurveBinding> floatBindings, IReadOnlyList<EditorCurveBinding> intBindings,
            ref BlobBuilder blobBuilder, ref Clip clip, BindingHashGenerator hasher)
        {
            clip.Bindings = clip.CreateBindingSet(translationBindings.Count, rotationBindings.Count, scaleBindings.Count, floatBindings.Count, intBindings.Count);

            FillBlobTransformBindingBuffer(translationBindings, ref blobBuilder, ref clip.Bindings.TranslationBindings, hasher);
            FillBlobTransformBindingBuffer(rotationBindings, ref blobBuilder, ref clip.Bindings.RotationBindings, hasher);
            FillBlobTransformBindingBuffer(scaleBindings, ref blobBuilder, ref clip.Bindings.ScaleBindings, hasher);
            FillBlobBindingBuffer(floatBindings, ref blobBuilder, ref clip.Bindings.FloatBindings, hasher);
            FillBlobBindingBuffer(intBindings, ref blobBuilder, ref clip.Bindings.IntBindings, hasher);
        }

        private static void FillBlobTransformBindingBuffer(IReadOnlyList<EditorCurveBinding> bindings, ref BlobBuilder blobBuilder, ref BlobArray<StringHash> blobBuffer, BindingHashGenerator hasher)
        {
            if (bindings == null || bindings.Count == 0)
                return;

            var arrayBuilder = blobBuilder.Allocate(ref blobBuffer, bindings.Count);
            for (var i = 0; i != bindings.Count; ++i)
                arrayBuilder[i] = hasher.ToHash(ClipBuilderUtils.ToTransformBindingID(bindings[i]));
        }

        private static void FillBlobBindingBuffer(IReadOnlyList<EditorCurveBinding> bindings, ref BlobBuilder blobBuilder, ref BlobArray<StringHash> blobBuffer, BindingHashGenerator hasher)
        {
            if (bindings == null || bindings.Count == 0)
                return;

            var arrayBuilder = blobBuilder.Allocate(ref blobBuffer, bindings.Count);
            for (var i = 0; i != bindings.Count; ++i)
                arrayBuilder[i] = hasher.ToHash(ClipBuilderUtils.ToGenericBindingID(bindings[i]));
        }

        private static void FillCurves(AnimationClip sourceClip, IReadOnlyList<EditorCurveBinding> translationBindings,
            IReadOnlyList<EditorCurveBinding> rotationBindings, IReadOnlyList<EditorCurveBinding> scaleBindings,
            IReadOnlyList<EditorCurveBinding> floatBindings, IReadOnlyList<EditorCurveBinding> intBindings,
            ref BlobBuilder blobBuilder, ref Clip clip)
        {
            clip.Duration = sourceClip.length;
            clip.SampleRate = sourceClip.frameRate;

            var sampleCount = clip.Bindings.CurveCount * (clip.FrameCount + 1);
            if (sampleCount > 0)
            {
                var arrayBuilder = blobBuilder.Allocate(ref clip.Samples, sampleCount);

                // Translation curves
                for (var i = 0; i != translationBindings.Count; ++i)
                    AddVector3Curve(ref clip, ref arrayBuilder, sourceClip, translationBindings[i], "m_LocalPosition", clip.Bindings.TranslationSamplesOffset + i * BindingSet.TranslationKeyFloatCount, clip.Bindings.CurveCount);

                // Scale curves
                for (var i = 0; i != scaleBindings.Count; ++i)
                    AddVector3Curve(ref clip, ref arrayBuilder, sourceClip, scaleBindings[i], "m_LocalScale", clip.Bindings.ScaleSamplesOffset + i * BindingSet.ScaleKeyFloatCount, clip.Bindings.CurveCount);

                // Float curves
                for (var i = 0; i != floatBindings.Count; ++i)
                    AddFloatCurve(ref clip, ref arrayBuilder, sourceClip, floatBindings[i], clip.Bindings.FloatSamplesOffset + i * BindingSet.FloatKeyFloatCount, clip.Bindings.CurveCount);

                // Int curves
                for (var i = 0; i != intBindings.Count; ++i)
                    AddFloatCurve(ref clip, ref arrayBuilder, sourceClip, intBindings[i], clip.Bindings.IntSamplesOffset + i * BindingSet.IntKeyFloatCount, clip.Bindings.CurveCount);

                // Rotation curves
                for (var i = 0; i != rotationBindings.Count; ++i)
                {
                    // Remove the ".x" at the end.
                    var propertyName = rotationBindings[i].propertyName.Remove(rotationBindings[i].propertyName.Length - 2);
                    if (propertyName.Contains("Euler"))
                    {
                        AddEulerCurve(ref clip, ref arrayBuilder, sourceClip, rotationBindings[i], propertyName, clip.Bindings.RotationSamplesOffset + i * BindingSet.RotationKeyFloatCount, clip.Bindings.CurveCount);
                    }
                    else
                    {
                        AddQuaternionCurve(ref clip, ref arrayBuilder, sourceClip, rotationBindings[i], propertyName, clip.Bindings.RotationSamplesOffset + i * BindingSet.RotationKeyFloatCount, clip.Bindings.CurveCount);
                    }
                }
            }
        }

        private static void AddVector3Curve(ref Clip clip, ref BlobBuilderArray<float> samples, AnimationClip sourceClip, EditorCurveBinding binding,
            string property, int curveIndex, in int curveCount)
        {
            binding.propertyName = $"{property}.x";
            ConvertCurve(ref clip, ref samples, AnimationUtility.GetEditorCurve(sourceClip, binding), curveIndex++, curveCount);

            binding.propertyName = $"{property}.y";
            ConvertCurve(ref clip, ref samples, AnimationUtility.GetEditorCurve(sourceClip, binding), curveIndex++, curveCount);

            binding.propertyName = $"{property}.z";
            ConvertCurve(ref clip, ref samples, AnimationUtility.GetEditorCurve(sourceClip, binding), curveIndex++, curveCount);
        }

        private static void AddQuaternionCurve(ref Clip clip, ref BlobBuilderArray<float> samples, AnimationClip sourceClip, EditorCurveBinding binding,
            string property, int curveIndex, in int curveCount)
        {
            binding.propertyName = $"{property}.x";
            ConvertCurve(ref clip, ref samples, AnimationUtility.GetEditorCurve(sourceClip, binding), curveIndex++, curveCount);
            binding.propertyName = $"{property}.y";
            ConvertCurve(ref clip, ref samples, AnimationUtility.GetEditorCurve(sourceClip, binding), curveIndex++, curveCount);
            binding.propertyName = $"{property}.z";
            ConvertCurve(ref clip, ref samples, AnimationUtility.GetEditorCurve(sourceClip, binding), curveIndex++, curveCount);
            binding.propertyName = $"{property}.w";
            ConvertCurve(ref clip, ref samples, AnimationUtility.GetEditorCurve(sourceClip, binding), curveIndex++, curveCount);
        }

        private static void AddEulerCurve(ref Clip clip, ref BlobBuilderArray<float> samples, AnimationClip sourceClip, EditorCurveBinding binding,
            string property, int curveIndex, in int curveCount)
        {
            binding.propertyName = $"{property}.x";
            var xEulerCurve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            binding.propertyName = $"{property}.y";
            var yEulerCurve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            binding.propertyName = $"{property}.z";
            var zEulerCurve = AnimationUtility.GetEditorCurve(sourceClip, binding);

            float xLastValue, yLastValue, zLastValue;
            float xValueAtDuration, yValueAtDuration, zValueAtDuration;
            var qLastValue = new Quaternion();
            var qValueAtDuration = new Quaternion();
            var sampleIndex = curveIndex;

            for (var frameIter = 0; frameIter < clip.FrameCount; frameIter++)
            {
                var frameTime = frameIter / clip.SampleRate;
                xLastValue = xEulerCurve.Evaluate(frameTime);
                yLastValue = yEulerCurve.Evaluate(frameTime);
                zLastValue = zEulerCurve.Evaluate(frameTime);
                qLastValue = Quaternion.Euler(xLastValue, yLastValue, zLastValue);

                samples[sampleIndex + 0] = qLastValue.x;
                samples[sampleIndex + 1] = qLastValue.y;
                samples[sampleIndex + 2] = qLastValue.z;
                samples[sampleIndex + 3] = qLastValue.w;

                sampleIndex += curveCount;
            }

            // adjust last frame value to match value at duration
            xValueAtDuration = xEulerCurve.Evaluate(clip.Duration);
            yValueAtDuration = yEulerCurve.Evaluate(clip.Duration);
            zValueAtDuration = zEulerCurve.Evaluate(clip.Duration);
            qValueAtDuration = Quaternion.Euler(xValueAtDuration, yValueAtDuration, zValueAtDuration);

            sampleIndex = curveIndex + clip.FrameCount * curveCount;
            samples[sampleIndex + 0] = Core.AdjustLastFrameValue(qLastValue.x, qValueAtDuration.x, clip.LastFrameError);
            samples[sampleIndex + 1] = Core.AdjustLastFrameValue(qLastValue.y, qValueAtDuration.y, clip.LastFrameError);
            samples[sampleIndex + 2] = Core.AdjustLastFrameValue(qLastValue.z, qValueAtDuration.z, clip.LastFrameError);
            samples[sampleIndex + 3] = Core.AdjustLastFrameValue(qLastValue.w, qValueAtDuration.w, clip.LastFrameError);
        }

        private static void AddFloatCurve(ref Clip clip, ref BlobBuilderArray<float> samples, AnimationClip sourceClip, EditorCurveBinding binding,
            int curveIndex, in int curveCount)
        {
            ConvertCurve(ref clip, ref samples, AnimationUtility.GetEditorCurve(sourceClip, binding), curveIndex, curveCount);
        }

        private static void ConvertCurve(ref Clip clip, ref BlobBuilderArray<float> samples, UnityEngine.AnimationCurve curve, int curveIndex, in int curveCount)
        {
            var lastValue = 0.0f;

            for (var frameIter = 0; frameIter < clip.FrameCount; frameIter++)
            {
                lastValue = curve.Evaluate(frameIter / clip.SampleRate);
                samples[curveIndex + frameIter * curveCount] = lastValue;
            }

            // adjust last frame value to match value at duration
            var valueAtDuration = curve.Evaluate(clip.Duration);

            samples[curveIndex + clip.FrameCount * curveCount] = Core.AdjustLastFrameValue(lastValue, valueAtDuration, clip.LastFrameError);
        }
    }
}

#endif
