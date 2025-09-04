using System;
using System.Runtime.CompilerServices;

namespace SharpScript.Common.NT_Structs
{
    [InlineArray(4)]
    [CollectionBuilder(typeof(InlineArrayBuilder), nameof(InlineArrayBuilder.Object4Creator))]
    internal struct Object4<T>
    {
        private T value;
    }

    [InlineArray(8)]
    [CollectionBuilder(typeof(InlineArrayBuilder), nameof(InlineArrayBuilder.Object8Creator))]
    internal struct Object8<T>
    {
        private T value;
    }

    [InlineArray(10)]
    [CollectionBuilder(typeof(InlineArrayBuilder), nameof(InlineArrayBuilder.Object10Creator))]
    internal struct Object10<T>
    {
        private T value;
    }

    [InlineArray(16)]
    [CollectionBuilder(typeof(InlineArrayBuilder), nameof(InlineArrayBuilder.Object16Creator))]
    internal struct Object16<T>
    {
        private T value;
    }

    internal static class InlineArrayBuilder
    {
        public static Object4<T> Object4Creator<T>(params ReadOnlySpan<T> values)
        {
            if (values.Length != 4) { throw new ArgumentException("Invalid number of elements", nameof(values)); }
            Object4<T> result = new();
            values.CopyTo(result);
            return result;
        }

        public static Object8<T> Object8Creator<T>(params ReadOnlySpan<T> values)
        {
            if (values.Length != 8) { throw new ArgumentException("Invalid number of elements", nameof(values)); }
            Object8<T> result = new();
            values.CopyTo(result);
            return result;
        }

        public static Object10<T> Object10Creator<T>(params ReadOnlySpan<T> values)
        {
            if (values.Length != 10) { throw new ArgumentException("Invalid number of elements", nameof(values)); }
            Object10<T> result = new();
            values.CopyTo(result);
            return result;
        }

        public static Object16<T> Object16Creator<T>(params ReadOnlySpan<T> values)
        {
            if (values.Length != 16) { throw new ArgumentException("Invalid number of elements", nameof(values)); }
            Object16<T> result = new();
            values.CopyTo(result);
            return result;
        }
    }
}
