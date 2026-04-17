using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityExtension.EditorLayout
{
    public struct GridLayout
    {
        Rect GridArea;

        int columns;
        int row;

        float[] column_occupation;
        float[] row_occupation;

        int _Column;
        public int Column
        {
            get => _Column;
            set
            {
                if (value != _Column)
                {
                    value = _Column;

                }
            }   
        }

        public int column_space;
        public int row_space;
    }
}

