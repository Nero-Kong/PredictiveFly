using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UnityExtension
{
    public static class HandleExtent
    {
        public static float PosEditionThreshold = .1f;
        public static float AngleEditionThreshold = .1f;

        public static Vector3 Vector3MagnitudeRotationEdit(Vector3 vector, Vector3 origin, Color color = default)
        {
#if UNITY_EDITOR
            if (vector == Vector3.zero) return vector;

            if (color != default)
            {
                Handles.color = color;
            }

            float magnitude = Handles.ScaleSlider(vector.magnitude, origin, vector.normalized, Quaternion.identity, vector.magnitude, PosEditionThreshold);
            Quaternion rotation = Handles.Disc(Quaternion.FromToRotation(Vector3.forward, vector), origin, Vector3.up, vector.magnitude, true, AngleEditionThreshold);

            return rotation * (Vector3.forward * magnitude);
#else
            return vector;
#endif
        }

        public static Vector3 Vector3PlanePlaceHandle(Transform ReferenceFrame)
        {
#if UNITY_EDITOR
            Vector3 mouse_target = Vector3.zero;
            Plane scanPlane = new Plane(ReferenceFrame.up, ReferenceFrame.position);
            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            float center;

            if (scanPlane.Raycast(ray, out center))
            {
                mouse_target = ray.origin + ray.direction * center;
                Handles.DrawWireDisc(mouse_target, ReferenceFrame.up, 1);
            }

            return mouse_target;
#else
            return Vector3.zero;
#endif
        }
    }
}