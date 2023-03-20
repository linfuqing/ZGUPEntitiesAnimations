using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;

namespace ZG
{
    public static class AnimatorExtensions
    {
        public static void ExtractBoneTransforms(this Animator animatorComponent, List<RigIndexToBone> bones)
        {
            bones.Clear();

            Transform[] transforms;
            if (animatorComponent.avatar == null || !animatorComponent.avatar.isValid || !animatorComponent.avatar.isHuman)
            {
                transforms =  animatorComponent.gameObject.GetComponentsInChildren<Transform>();
            }
            else
            {
                var skeleton = AnimatorUtils.FilterNonExsistantBones(animatorComponent.transform, animatorComponent.avatar.humanDescription.skeleton);

                if (skeleton.Length == 0)
                {
                    transforms = new Transform[0];
                }
                else
                {
                    transforms = AnimatorUtils.GetTransformsFromSkeleton(animatorComponent.transform, skeleton);
                }
            }

            for (int i = 0; i < transforms.Length; ++i)
            {
                bones.Add(new RigIndexToBone {Index = i, Bone = transforms[i]});
            }
        }
    }
}
