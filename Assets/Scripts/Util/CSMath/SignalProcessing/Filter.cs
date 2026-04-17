using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CSMath.SignalProcessing
{
    public partial class Filter
    {
        public enum FilterClass { Butterworth };
        public FilterClass Class { get; private set; }

        public enum FilterType { LowPass, HighPass, BandPass, BandStop }
        public FilterType Type { get; private set; }


        public int Order { get; private set; }
        public float PulseRatio { get; private set; }

        float[] _InputMemory;
        float[] _OutputMemory;

        float[] _ForwardCoefficient;
        float[] _BackwardCoefficient;

        protected Filter(int order, float pulseRatio)
        {
            Order = order;
            PulseRatio = pulseRatio;
        }

        public float Process(float sample)
        {
            Array.Copy(_InputMemory, 0, _InputMemory, 1, _InputMemory.Length - 1);
            _InputMemory[0] = sample;

            float result = 0f;

            for (int i = 0; i < _ForwardCoefficient.Length; i++)
            {
                result += _ForwardCoefficient[i] * _InputMemory[i];
            }

            for (int i = 0; i < _BackwardCoefficient.Length; i++)
            {
                result += _BackwardCoefficient[i] * _OutputMemory[i];
            }

            Array.Copy(_OutputMemory, 0, _OutputMemory, 1, _OutputMemory.Length - 1);
            _OutputMemory[0] = result;

            return result;
        }

        public float[] Process(float[] signal)
        {
            float[] process = new float[signal.Length];
            for (int i = 0; i < process.Length; i++)
            {
                process[i] = Process(signal[i]);
            }
            return process;
        }

        //public void ResetMemory()
        //{
        //    if (_InputMemory != null)
        //    {
        //        foreach (float[] channelMemory in _InputMemory)
        //        {
        //            Array.Clear(channelMemory, 0, channelMemory.Length);
        //        }
        //    }

        //    if (_OutputMemory != null)
        //    {
        //        foreach (float[] channelMemory in _OutputMemory)
        //        {
        //            Array.Clear(channelMemory, 0, channelMemory.Length);
        //        }
        //    }
        //}





        //public Quaternion Process(Quaternion quaternion)
        //{
        //    return new Quaternion()
        //    {
        //        x = Process(Quaternion.x);
        //    }
        //}





        private static void ToHighPass(Polynomial_Single denLP, out Polynomial_Single numHP, out Polynomial_Single denHP)
        {

            float[] temp = denLP.Coefficient;
            Array.Reverse(temp);

            numHP = (1 / denLP[denLP.Order]) * new Polynomial_Single(denLP.Order); // = X^n
            denHP = (1 / denLP[denLP.Order]) * new Polynomial_Single(temp); // P(x) => P(1/x) <=> Ci => Cn-i (*1/x^n)

        }

        private static void ToBandPass(Polynomial_Single denLP, out Polynomial_Single numBP, out Polynomial_Single denBP)
        {
            numBP = Polynomial_Single.One;
            denBP = Polynomial_Single.One;

            throw new System.NotImplementedException("BandPass filter conversion is yet to be done !");
        }

        private static void ToBandStop(Polynomial_Single denLP, out Polynomial_Single numBS, out Polynomial_Single denBS)
        {
            numBS = Polynomial_Single.One;
            denBS = Polynomial_Single.One;

            throw new System.NotImplementedException("BandStop filter conversion is yet to be done !");
        }


        public override string ToString()
        {
            StringBuilder builder = new StringBuilder("Filter data: \n");
            builder.Append("Order: " + Order.ToString() + "\n");
            builder.Append("PulseRatio: " + PulseRatio.ToString() + "\n");
            builder.Append("Forward coefficient: " + string.Concat(_ForwardCoefficient.Select(c => string.Format("{0:0.000} ", c))) + "\n");
            builder.Append("Backward coefficient: " + string.Concat(_BackwardCoefficient.Select(c => string.Format("{0:0.000} ", c))) + "\n");
            return builder.ToString();
        }
    }


}

