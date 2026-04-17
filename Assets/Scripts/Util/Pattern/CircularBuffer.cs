using System;
using System.Text;
using UnityEngine;
public class CircularBuffer<GenericType>
{
    int _BufferLength;
    GenericType[] _Buffer;
    int wptr;

    public CircularBuffer(int length)
    {
        _BufferLength = length;
        _Buffer = new GenericType[length];
    }

    public int BufferLength
    {
        get => _BufferLength;
        set
        {
            if (value != _BufferLength)
            {
                GenericType[] temp = new GenericType[value];
                Array.Copy(_Buffer, temp, Mathf.Min(value, _BufferLength));
                _BufferLength = value;
                _Buffer = temp;
            }
        }
    }

    public GenericType[] Read(ref int ptr, int length)
    {
        GenericType[] output = new GenericType[length];

        Array.Copy(_Buffer, ptr, output, 0, Mathf.Min(_BufferLength - ptr, length));
        if (length - (_BufferLength - ptr) > 0) Array.Copy(_Buffer, 0, output, _BufferLength - ptr, length - (_BufferLength - ptr));

        ptr = (ptr + length) % _BufferLength;
        return output;
    }

    public GenericType[] Read(int ptr, int length)
    {
        GenericType[] output = new GenericType[length];

        Array.Copy(_Buffer, ptr, output, 0, Mathf.Min(_BufferLength - ptr, length));
        if (length - (_BufferLength - ptr) > 0) Array.Copy(_Buffer, 0, output, _BufferLength - ptr, length - (_BufferLength - ptr));

        return output;
    }

    public GenericType[] ReadAll()
    {
        return Read(wptr, _BufferLength);
    }

    public int Write(GenericType input)
    {
        _Buffer[wptr] = input;
        wptr = (wptr + 1) % _BufferLength;

        return wptr;
    }

    public int Write(GenericType[] input)
    {
        if (input.Length > _BufferLength) throw new System.OverflowException("Input array is larger than buffer capacity.");
        Array.Copy(input, 0, _Buffer, wptr, Mathf.Min(_BufferLength - wptr, input.Length));
        if (input.Length - (_BufferLength - wptr) > 0) Array.Copy(input, _BufferLength - wptr, _Buffer, 0, input.Length - (_BufferLength - wptr));

        wptr = (wptr + input.Length) % _BufferLength;

        return wptr;
    }

    public int Write(int ptr, GenericType[] input)
    {
        wptr = ptr;
        return Write(input);
    }

    public void Reset()
    {
        wptr = 0;
        for (int i = 0; i < _BufferLength; i++)
        {
            _Buffer[i] = default;
        }
    }

    public void Log()
    {
        int idx = 0;
        StringBuilder stringBuilder = new StringBuilder("Content: \n");
        foreach (GenericType val in _Buffer)
        {
            stringBuilder.Append(idx++.ToString() + " : " + val.ToString() + "\n");
        }
        Debug.Log(stringBuilder.ToString());
        Debug.Log("wptr = " + wptr.ToString());
    }

}
