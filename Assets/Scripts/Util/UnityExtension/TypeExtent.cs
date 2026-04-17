using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace UnityExtension
{
    public static class TypeExtent
    {

        public static void SetLocalPose(this Transform transform, Pose pose)
        {
            transform.localPosition = pose.position;
            transform.localRotation = pose.rotation;
        }

        public static Pose GetLocalPose(this Transform transform)
        {
            return new Pose()
            {
                position = transform.localPosition,
                rotation = transform.localRotation
            };
        }

        public static void SetPose(this Transform transform, Pose pose)
        {
            transform.position = pose.position;
            transform.rotation = pose.rotation;
        }

        public static Pose GetPose(this Transform transform)
        {
            return new Pose()
            {
                position = transform.position,
                rotation = transform.rotation
            };
        }

        public static float QuatCompare(this Quaternion origin, Quaternion target)
        {
            Quaternion error = origin * Quaternion.Inverse(target);

            error.ToAngleAxis(out float angle, out Vector3 axis);
            return angle;
        }


        public static Vector4 ToVector4(this Quaternion quaternion)
        {
            return new Vector4()
            {
                x = quaternion.x,
                y = quaternion.y,
                z = quaternion.z,
                w = quaternion.w
            };
        }

        public static Quaternion ToQuaternion(this Vector4 vector4)
        {
            return new Quaternion()
            {
                x = vector4.x,
                y = vector4.y,
                z = vector4.z,
                w = vector4.w
            };
        }

        public static string ToTimeString(this float second_time, bool expand = true)
        {
            int hours = Mathf.FloorToInt(second_time / 3600);
            second_time -= hours * 3600;
            int minutes = Mathf.FloorToInt(second_time / 60);
            second_time -= minutes * 60;
            int seconds = Mathf.FloorToInt(second_time);
            second_time -= seconds;
            int milli = Mathf.RoundToInt(second_time * 1000);

            if (expand)
                return string.Format("{0:00}h : {1:00}m : {2:00}s : {3:000}ms", hours, minutes, seconds, milli);
            else
            {
                if (hours > 0)
                    return string.Format("{0:00}h : {1:00}m : {2:00}s : {3:000}ms", hours, minutes, seconds, milli);

                else if (minutes > 0)
                    return string.Format("{0:00}m : {1:00}s : {2:000}ms", minutes, seconds, milli);

                else
                    return string.Format("{0:00}s : {1:000}ms", seconds, milli);
            }
        }

        public static string ToString<GenericType>(this IEnumerable<GenericType> source, string separator = ", ") => String.Join(separator, source.Select(item => item.ToString()).ToArray());
    }
}

