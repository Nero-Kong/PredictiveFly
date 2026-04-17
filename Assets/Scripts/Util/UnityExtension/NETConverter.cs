using System.Collections;
using System.Collections.Generic;

namespace NETExtension
{
    public static class NETConverter
    {
        public static System.Numerics.Vector3 ToNET(this UnityEngine.Vector3 self)
        {
            return new System.Numerics.Vector3(self.x, self.y, self.z);
        }

        public static UnityEngine.Vector3 FromNET(this System.Numerics.Vector3 self)
        {
            return new UnityEngine.Vector3(self.X, self.Y, self.Z);
        }
    }
}

