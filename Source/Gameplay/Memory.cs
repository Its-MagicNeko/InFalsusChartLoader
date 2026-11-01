using System;

namespace InFalsusChartLoader
{
    /// <summary>Unchecked reads and writes of game memory, and the object-pointer check.</summary>
    internal static unsafe class Memory
    {
        internal static int I32(IntPtr p) => *(int*)p;
        internal static long I64(IntPtr p) => *(long*)p;
        internal static ushort U16(IntPtr p) => *(ushort*)p;
        internal static float F32(IntPtr p) => *(float*)p;
        internal static double F64(IntPtr p) => *(double*)p;
        internal static byte U8(IntPtr p) => *(byte*)p;
        internal static IntPtr Ptr(IntPtr p) => *(IntPtr*)p;
        internal static void WriteI32(IntPtr p, int v) => *(int*)p = v;
        internal static void WriteF32(IntPtr p, float v) => *(float*)p = v;
        internal static void WriteI64(IntPtr p, long v) => *(long*)p = v;
        internal static void WritePtr(IntPtr p, IntPtr v) => *(IntPtr*)p = v;
        internal static void WriteU8(IntPtr p, byte v) => *(byte*)p = v;
        internal static void WriteU16(IntPtr p, ushort v) => *(ushort*)p = v;

        /// <summary>Whether a pointer can be dereferenced as an IL2CPP object.</summary>
        internal static bool LooksLikeObject(IntPtr p)
        {
            if (p == IntPtr.Zero) return false;
            long v = p.ToInt64();
            if ((v & 7) != 0) return false;

            return v > 0x10000 && v < 0x7FFFFFFFFFFF;
        }

        /// <summary>An IL2CPP string's text, or null when it is not a string.</summary>
        internal static string Text(IntPtr s, int limit = 256)
        {
            if (!LooksLikeObject(s)) return null;

            try
            {
                int length = Il2CppInterop.Runtime.IL2CPP.il2cpp_string_length(s);
                if (length <= 0 || length > limit) return null;
                return Il2CppInterop.Runtime.IL2CPP.Il2CppStringToManaged(s);
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"string at 0x{s.ToInt64():X} could not be read: {Diagnostics.Describe(e)}");
                return null;
            }
        }

        /// <summary>An IL2CPP array's total byte length, or 0 when it cannot be read.</summary>
        internal static long ArrayBytes(IntPtr array)
        {
            if (!LooksLikeObject(array)) return 0;

            try
            {
                return Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(array);
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"an array's byte length could not be read: {Diagnostics.Describe(e)}");
                return 0;
            }
        }

        /// <summary>A `List&lt;T&gt;`'s backing array and count.</summary>
        internal static bool TryList(IntPtr list, out IntPtr items, out int count)
        {
            items = IntPtr.Zero;
            count = 0;

            if (!LooksLikeObject(list)) return false;

            IntPtr array = Ptr(list + Offsets.Runtime.ListItems);
            int size = I32(list + Offsets.Runtime.ListSize);
            if (size < 0 || !LooksLikeObject(array)) return false;

            int length = I32(array + Offsets.Runtime.ArrayLength);
            if (size > length) return false;

            items = array + Offsets.Runtime.ArrayDataOffset;
            count = size;
            return true;
        }
    }
}
