using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CSMath
{

    public static class Function
    {
        public static float Factorial(int n)
        {
            if (n == 0) { return 1f; }


            float res = 1;
            for (int k = 2; k <= n; k++)
            {
                res *= k;
            }
            return res;
        }

        public static float Factorial(int m, int n)
        {
            if (n == 0) { return 1f; }

            float res = 1;
            for (int k = m; k <= n; k++)
            {
                res *= k;
            }
            return res;
        }
    }

}
