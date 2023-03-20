using System.Collections.Generic;
using UnityEngine;

namespace ZG
{
    public struct RigIndexToBone
    {
        public int Index;
        public Transform Bone;
    }

    /// <summary>
    /// Interfaces that describe a rig authoring component generating
    /// a RigDefinition at conversion.
    /// </summary>
    public interface IRigAuthoring
    {
        void GetBones(List<RigIndexToBone> bones);
    }
}
