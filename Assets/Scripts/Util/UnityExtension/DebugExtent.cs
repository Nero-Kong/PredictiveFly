//using RosSharp.RosBridgeClient;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityExtension
{
    public static class DebugExtent
    {
        public static void Log(string format, params object[] args) => Debug.Log(string.Format(format, args));

        public static void NullTest(object variable, string name = "variable") => Debug.Log(name + " is " + ( (variable == null) ? "null." : "object.") );
    }
}

