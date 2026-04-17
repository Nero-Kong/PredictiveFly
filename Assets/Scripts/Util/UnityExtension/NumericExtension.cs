using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace NumericExtension
{
    public static class FloatExtension
    {
        static readonly float Epsilon = 0.001f;

        public static bool AlmostEqual(this float number, float other)
        {
            return (Mathf.Sign(number) == Mathf.Sign(other))
                && (Mathf.Abs(number) + Epsilon >= other)
                && (Mathf.Abs(number) - Epsilon <= other);
        }

        public static bool InfAlmostEqual(this float number, float other)
        {
            return number < other
                || number.AlmostEqual(other);
        }

        public static bool SupAlmostEqual(this float number, float other)
        {
            return number > other
                || number.AlmostEqual(other);
        }
    }
}
