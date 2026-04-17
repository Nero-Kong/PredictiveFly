using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Coadaptation.Drones
{
    public static class DroneUtils
    {
        public static Vector3 ModuloV(Vector3 target, Vector3 modulo)
        {
            float x = target.x - (Mathf.Floor(target.x / modulo.x) * modulo.x);
            float y = target.y - (Mathf.Floor(target.y / modulo.y) * modulo.y);
            float z = target.z - (Mathf.Floor(target.z / modulo.z) * modulo.z);

            return new Vector3(x, y, z);
        }

        public static Vector3 ModuloV(Vector3 target, float modulo)
        {
            float x = target.x - (Mathf.Floor(target.x / modulo) * modulo);
            float y = target.y - (Mathf.Floor(target.y / modulo) * modulo);
            float z = target.z - (Mathf.Floor(target.z / modulo) * modulo);

            return new Vector3(x, y, z);
        }
        public static Vector3 ClampV(Vector3 target, Vector3 limits)
        {
            float x = Mathf.Clamp(target.x, -limits.x, limits.x);
            float y = Mathf.Clamp(target.y, -limits.y, limits.y);
            float z = Mathf.Clamp(target.z, -limits.z, limits.z);

            return new Vector3(x, y, z);
        }
        public static Vector3 ClampV(Vector3 target, float limit)
        {
            float x = Mathf.Clamp(target.x, -limit, limit);
            float y = Mathf.Clamp(target.y, -limit, limit);
            float z = Mathf.Clamp(target.z, -limit, limit);

            return new Vector3(x, y, z);
        }

        public static Vector3Int ClampV(Vector3Int target, int limit)
        {
            int x = Mathf.Clamp(target.x, -limit, limit);
            int y = Mathf.Clamp(target.y, -limit, limit);
            int z = Mathf.Clamp(target.z, -limit, limit);

            return new Vector3Int(x, y, z);
        }
    }

}
