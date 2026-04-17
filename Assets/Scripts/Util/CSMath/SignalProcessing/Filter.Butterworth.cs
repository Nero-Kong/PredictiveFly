using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CSMath.SignalProcessing
{
    public partial class Filter
    {
        public static Filter Butterworth(int order, float pulse_ratio, FilterType type = FilterType.LowPass)
        {
            Polynomial_Single template = GetButterPol(order);
            Polynomial_Single numerator, denominator;

            switch (type)
            {

                case FilterType.HighPass:
                    ToHighPass(template, out numerator, out denominator);
                    break;

                default:
                    numerator = Polynomial_Single.One;
                    denominator = template;
                    break;
            }

            // warp frequency
            float factor = 1 / Mathf.Tan(Mathf.PI * pulse_ratio); //ZWarpFactor(cutoff, sampling) / (2 * Mathf.PI * cutoff);

            numerator.ChangeVariable(factor);
            denominator.ChangeVariable(factor);

            BilinearZTransform(ref numerator, ref denominator);

            float[] forwardCoefficient = numerator.Coefficient;
            float[] backwardCoefficient = denominator.Coefficient;

            // z^i => z^-i
            Array.Reverse(forwardCoefficient);
            Array.Reverse(backwardCoefficient);

            //normalize
            for (int i = forwardCoefficient.Length - 1; i >= 0; i--)
            {
                forwardCoefficient[i] /= backwardCoefficient[0];
            }

            for (int i = backwardCoefficient.Length - 1; i >= 0; i--)
            {
                backwardCoefficient[i] /= backwardCoefficient[0];
            }

            return new Filter(order, pulse_ratio)
            {
                _ForwardCoefficient = forwardCoefficient,
                _BackwardCoefficient = backwardCoefficient
            };
        }

        public static Filter Butterworth(int order, float cutoff, float sampling, FilterType type = FilterType.LowPass)
        {
            return Butterworth(order, cutoff / sampling, type);

        }

        private static Polynomial_Single GetButterPol(int order)
        {
            Polynomial_Single butterPol = (order % 2 == 0) ? Polynomial_Single.One : new Polynomial_Single(new float[] { 1, 1 });
            Polynomial_Single temp = new Polynomial_Single(new float[] { 1, 1, 1 });

            for (int i = 1; i <= order / 2; i++)
            {
                temp[1] = -2f * Mathf.Cos(Mathf.PI * ((2f * i + (float)order - 1f) / (2f * order)));
                butterPol *= temp;
            }

            return butterPol;
        }

        private static void BilinearZTransform(ref Polynomial_Single numerator, ref Polynomial_Single denominator) //Missing form factor 2fe !!! Positive power of z
        {
            Polynomial_Single numZ = 0f;
            Polynomial_Single denZ = 0f;

            Polynomial_Single zM = new Polynomial_Single(new float[] { -1, 1 }); // (z - 1)
            Polynomial_Single zP = new Polynomial_Single(new float[] { 1, 1 });  // (z + 1)

            for (int i = 0; i <= numerator.Order; i++)
            {
                numZ += numerator[i] * Polynomial_Single.Pow(zM, i) * Polynomial_Single.Pow(zP, numerator.Order - i);
            }

            for (int j = 0; j <= denominator.Order; j++)
            {
                denZ += denominator[j] * Polynomial_Single.Pow(zM, j) * Polynomial_Single.Pow(zP, denominator.Order - j);
            }

            numZ *= Polynomial_Single.Pow(zP, denominator.Order - denominator.Order);

            numerator = numZ;
            denominator = denZ;
        }

    }
}
