using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public struct ColumnLayout
{

    Vector2 offset;

    int num_row, num_col;
    public int[] heights;
    public int[] widths;

    static int defaultCRSpace = 3;
    static int defaultCellWidth = 70;
    static int defaultCellHeight = 18;

    public ColumnLayout(int row, int col, Vector2 offset = default)
    {
        num_row = row;
        num_col = col;

        this.offset = offset;

        heights = new int[num_row];
        widths = new int[num_col];

        for (int i = 0; i < num_row; i++)
        {
            heights[i] = defaultCellHeight;
        }

        for (int i = 0; i < num_col; i++)
        {
            widths[i] = defaultCellWidth;
        }
    }


    public Rect GetRect(int i, int j)
    {
        Rect rect = new Rect()
        {
            position = offset
        };

        for (int idx = 0; (idx < i) && (idx < num_row); idx++)
        {
            rect.y += heights[idx] + defaultCRSpace;
        }

        for (int idx = 0; (idx < j) && (idx < num_col); idx++)
        {
            rect.x += widths[idx];
        }

        rect.height = heights[i];
        rect.width = widths[j];
        return rect;
    }



}