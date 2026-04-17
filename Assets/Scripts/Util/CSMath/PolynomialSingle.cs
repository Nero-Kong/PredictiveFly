using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Text;

namespace CSMath
{
    [System.Serializable]
    public class Polynomial_Single : IEquatable<Polynomial_Single>
    {
        public int Order { get; private set; }
        public float[] Coefficient { get; private set; }

        public static readonly Polynomial_Single One = new Polynomial_Single(new float[] { 1 });

        #region Constructor

        public Polynomial_Single() // P(x) = 0;
        {
            Order = 0;
            Coefficient = new float[1] { 0f };
        }

        public Polynomial_Single(int order) // P(x) = x^order
        {
            Order = order;
            Coefficient = new float[order + 1];
            Coefficient[order] = 1f;
        }

        public Polynomial_Single(float[] coefficient) // P(x) = c0 + c1*x + ... + cn*x^n 
        {
            Order = GetOrder(ref coefficient);
            Coefficient = new float[Order + 1];
            Array.Copy(coefficient, Coefficient, Coefficient.Length);
        }

        private Polynomial_Single(int order, ref float[] coefficient)
        {
            Order = order;
            Coefficient = coefficient;
        }

        #endregion

        #region Mathematics

        public Polynomial_Single Derivative(int n)
        {
            if (n > Order || this == 0f) { return new Polynomial_Single(); }

            int nOrder = Order - n;
            float[] coef = new float[nOrder + 1];
            Array.Copy(Coefficient, n, coef, 0, coef.Length);
            for (int c = 0; c < coef.Length; c++)
            {
                coef[c] *= Function.Factorial(c, n);
            }
            return new Polynomial_Single(nOrder, ref coef);
        }

        public Polynomial_Single Integral(int n)
        {
            if (this == 0f) { return new Polynomial_Single(); }

            int nOrder = Order + n;
            float[] coef = new float[nOrder + 1];
            Array.Copy(Coefficient, 0, coef, n, Order + 1);
            for (int c = n; c <= nOrder; c++)
            {
                coef[c] /= Function.Factorial(c + 1, c + n);
            }

            return new Polynomial_Single(coef);

        }


        public float[] Evaluate(float[] x_array)
        {
            float[] res = new float[x_array.Length];
            Array.Copy(x_array, res, x_array.Length);
            Evaluate(ref res);
            return res;
        }

        public void Evaluate(ref float[] x_array)
        {
            for (int i = 0; i < x_array.Length; i++)
            {
                x_array[i] = Evaluate(x_array[i]);
            }
        }

        public float Evaluate(float x)
        {
            float res = 0;
            for (int i = 0; i <= Order; i++)
            {
                res += Coefficient[i] * Mathf.Pow(x, i);
            }
            return res;
        }

        public void ChangeVariable(float factor) // P(x) => P(factor * x)
        {
            for (int i = 0; i < Coefficient.Length; i++)
            {
                Coefficient[i] *= Mathf.Pow(factor, i);
            }
        }

        public static Polynomial_Single Pow(Polynomial_Single polynomial, int k)
        {
            if (k == 0) return Polynomial_Single.One;

            Polynomial_Single result = polynomial;
            for (int i = 2; i <= k; i++)
            {
                result *= polynomial;
            }

            return result;
        }

        #endregion

        #region Operator

        public float this[int i]
        {
            get
            {
                if (i > Order)
                {
                    return 0f;
                }
                else
                {
                    return Coefficient[i];
                }
            }

            set
            {
                if (i > Order)
                {
                    float[] coef = new float[i + 1]; coef[i] = value;
                    Array.Copy(Coefficient, coef, Order + 1);

                    Order = i;
                    Coefficient = coef;
                }
                else
                {
                    Coefficient[i] = value;
                }
            }
        }

        public static Polynomial_Single operator +(Polynomial_Single self) => self;
        public static Polynomial_Single operator -(Polynomial_Single self)
        {
            for (int c = 0; c <= self.Order; c++)
            {
                self.Coefficient[c] = -self.Coefficient[c];
            }

            return self;
        }

        public static Polynomial_Single operator +(Polynomial_Single p1, Polynomial_Single p2)
        {
            float[] coef;

            int nOrder = Mathf.Max(p1.Order, p2.Order);

            if (p1.Order > p2.Order)
            {
                coef = p1.Coefficient;
                for (int i = 0; i <= p2.Order; i++)
                {
                    coef[i] += p2[i];
                }
            }

            else if (p1.Order < p2.Order)
            {
                coef = p2.Coefficient;
                for (int i = 0; i <= p1.Order; i++)
                {
                    coef[i] += p1[i];
                }
            }

            else
            {
                while (p1.Coefficient[nOrder] == (-p2.Coefficient[nOrder])
                    && nOrder != 0)
                {
                    nOrder--;
                }

                coef = new float[nOrder + 1];

                for (int i = 0; i <= nOrder; i++)
                {
                    coef[i] = p1.Coefficient[i] + p2.Coefficient[i];
                }
            }



            return new Polynomial_Single(nOrder, ref coef);
        }
        public static Polynomial_Single operator +(Polynomial_Single p, float value)
        {
            float[] coef = new float[p.Order + 1];
            Array.Copy(p.Coefficient, coef, p.Order + 1);
            coef[0] += value;

            return new Polynomial_Single(p.Order, ref coef);
        }
        public static Polynomial_Single operator +(float value, Polynomial_Single p) => p + value;

        public static Polynomial_Single operator -(Polynomial_Single p1, Polynomial_Single p2)
        {
            int nOrder = Mathf.Max(p1.Order, p2.Order);
            if (p1.Order == p2.Order)
            {
                while (p1.Coefficient[nOrder] == p2.Coefficient[nOrder])
                {
                    nOrder--;
                }
            }
            float[] coef = new float[nOrder + 1];

            for (int i = 0; i <= nOrder; i++)
            {
                coef[i] = p1.Coefficient[i] - p2.Coefficient[i];
            }

            return new Polynomial_Single(nOrder, ref coef);
        }
        public static Polynomial_Single operator -(Polynomial_Single p, float value)
        {
            float[] coef = new float[p.Order + 1];
            Array.Copy(p.Coefficient, coef, p.Order + 1);
            coef[0] -= value;

            return new Polynomial_Single(p.Order, ref coef);
        }
        public static Polynomial_Single operator -(float value, Polynomial_Single p) => value + (-p);

        public static Polynomial_Single operator *(Polynomial_Single p1, Polynomial_Single p2)
        {
            if (p1 == 0f || p2 == 0f)
            {
                return new Polynomial_Single();
            }

            int nOrder = p1.Order + p2.Order;
            float[] coef = new float[nOrder + 1];

            for (int cp1 = 0; cp1 <= p1.Order; cp1++)
            {
                for (int cp2 = 0; cp2 <= p2.Order; cp2++)
                {
                    coef[cp1 + cp2] += p1.Coefficient[cp1] * p2.Coefficient[cp2];
                }
            }

            return new Polynomial_Single(nOrder, ref coef);
        }
        public static Polynomial_Single operator *(float value, Polynomial_Single p)
        {
            if (value == 0f)
            {
                return new Polynomial_Single();
            }
            else
            {
                int nOrder = p.Order;
                float[] coef = new float[p.Order + 1];
                for (int c = 0; c <= nOrder; c++)
                {
                    coef[c] = value * p.Coefficient[c];
                }

                return new Polynomial_Single(nOrder, ref coef);
            }
        }
        public static Polynomial_Single operator *(Polynomial_Single p, float value) => value * p;

        public static bool operator ==(Polynomial_Single p1, Polynomial_Single p2)
        {
            if (p1.Order == p2.Order)
            {
                for (int i = 0; i <= p1.Order; i++)
                {
                    if (p1.Coefficient[i] != p2.Coefficient[i]) { return false; }
                }
                return true;
            }
            return false;
        }
        public static bool operator ==(Polynomial_Single p, float k)
        {
            return (p.Order == 0) && (p.Coefficient[0] == k);
        }
        public static bool operator ==(float k, Polynomial_Single p) => p == k;

        public static bool operator !=(Polynomial_Single p1, Polynomial_Single p2)
        {
            if (p1.Order != p2.Order)
            {
                for (int i = 0; i <= p1.Order; i++)
                {
                    if (p1.Coefficient[i] != p2.Coefficient[i]) { return true; }
                }
                return false;
            }
            return true;
        }
        public static bool operator !=(Polynomial_Single p, float k)
        {
            return !(p == k);
        }
        public static bool operator !=(float k, Polynomial_Single p) => p != k;

        public static implicit operator Polynomial_Single(float[] value)
        {
            return new Polynomial_Single(value);
        }

        public static implicit operator Polynomial_Single(float value)
        {
            float[] coef = new float[1] { value };
            return new Polynomial_Single(0, ref coef);
        }

        #endregion

        #region C# Object Methods

        public override bool Equals(object obj)
        {
            return this.Equals(obj as Polynomial_Single);
        }

        public bool Equals(Polynomial_Single other)
        {
            return other != null &&
                   Order == other.Order &&
                   EqualityComparer<float[]>.Default.Equals(Coefficient, other.Coefficient);
        }

        public override int GetHashCode()
        {
            int hashCode = -356224191;
            hashCode = hashCode * -1521134295 + Order.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<float[]>.Default.GetHashCode(Coefficient);
            return hashCode;
        }

        public string ToString(char variable = 'x')
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Order; i++)
            {
                sb.Append(String.Format("{0}*" + variable + "{1} + ", Coefficient[i], i));
            }
            sb.Append(String.Format("{0}*" + variable + "{1}", Coefficient[Order], Order));
            return sb.ToString();
        }

        #endregion

        #region Utility

        int GetOrder(ref float[] coef_array)
        {
            for (int order = coef_array.Length - 1; order >= 0; order--)
            {
                if (coef_array[order] != 0) { return order; }
            }
            return 0;
        }



        #endregion

    }
}

