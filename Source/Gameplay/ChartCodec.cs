using System;

namespace InFalsusChartLoader
{
    /// <summary>Decodes a chart with the game's own decoder.</summary>
    internal static unsafe class ChartCodec
    {
        /// <summary>The game's chart decoder.</summary>
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr> _gab;

        /// <summary>The game's empty-chart constructor.</summary>
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr> _hab;

        /// <summary>The byte stride of one note record.</summary>
        private static int _noteSize;

        /// <summary>The upper bound on a note time, in milliseconds.</summary>
        private const int MaxChartMs = 3_600_000;

        /// <summary>Finds the decoder and the empty-chart constructor.</summary>
        internal static bool Resolve()
        {
            IntPtr p = MethodResolver.ByName("_S", "_Gab");
            if (p == IntPtr.Zero) return false;

            _gab = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)p;

#if DEBUG
            IntPtr reference = MethodResolver.MethodInfoOf("_S", "_Gab");
            if (reference != IntPtr.Zero)
                Diagnostics.Info($"reference MethodInfo _S._Gab at 0x{reference.ToInt64():X}");
            Diagnostics.Info($"reference _S._Gab words: {JacketFactory.Words(reference, 12)}");
#endif

            IntPtr hab = MethodResolver.ByName("_S", "_hab");
            if (hab == IntPtr.Zero) return false;

            _hab = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr>)hab;
            return true;
        }

        /// <summary>Fills the tuple with the game's empty chart.</summary>
        internal static bool Empty(IntPtr tuple32)
        {
            if (_hab == null)
            {
                Diagnostics.Error("the game's empty-chart constructor is not in this build; a chart " +
                                  "that fails to decode cannot be served");
                return false;
            }

            _hab(tuple32, IntPtr.Zero);
            return true;
        }

        /// <summary>The range of note planes.</summary>
        private const int SideMin = 1;
        private const int SideMax = 4;

        /// <summary>Decodes a chart into the game's 32-byte tuple.</summary>
        internal static bool TryDecode(byte[] bytes, string seed, IntPtr tuple32, out string reason)
        {
            reason = null;

            if (_gab == null)
            {
#if DEBUG
                reason = "the chart decoder could not be reached";
#endif
                return false;
            }

#if IF_SPC_API
            if (!StartsWithIcp1(bytes))
            {
                if_spc_api.Conversion conversion = if_spc_api.Converter.Convert(bytes, seed);
                if (conversion.Ok) bytes = conversion.Output;
            }
#endif

            IntPtr name = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(seed);
            if (name == IntPtr.Zero)
            {
#if DEBUG
                reason = $"the name '{seed}' could not be passed to the decoder";
#endif
                return false;
            }

            byte* span = stackalloc byte[Offsets.Runtime.SpanSize];
            fixed (byte* data = bytes)
            {
                *(IntPtr*)span = (IntPtr)data;
                *(int*)(span + Offsets.Runtime.SpanLength) = bytes.Length;
                _gab(tuple32, (IntPtr)span, name, IntPtr.Zero);
            }

            return Inspect(tuple32, seed, out reason);
        }

        /// <summary>Checks the decoded notes.</summary>
        private static bool Inspect(IntPtr tuple32, string seed, out string reason)
        {
            reason = null;

            IntPtr notes = Memory.Ptr(tuple32 + Offsets.Runtime.ChartTupleNotes);
            if (!Memory.TryList(notes, out IntPtr items, out int count))
            {
#if DEBUG
                reason = "the decoder returned no note list";
#endif
                return false;
            }
            if (count == 0)
            {
#if DEBUG
                reason = "the decoder found no notes (bad container, or encoded under another name)";
#endif
                return false;
            }

            if (!MeasureNoteSize(items, count))
            {
#if DEBUG
                reason = "the note records could not be read this build";
#endif
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                IntPtr note = items + i * _noteSize;

                int side = Memory.U8(note + Offsets.Note.Side);
                int type = Memory.U8(note + Offsets.Note.Type);
                int start = Memory.I32(note + Offsets.Note.StartMs);
                int end = Memory.I32(note + Offsets.Note.EndMs);

                if (side < SideMin || side > SideMax)
                {
#if DEBUG
                    reason = $"note {i} is on plane {side} under the name '{seed}'; " +
                             "the file is probably not encoded under that name";
                    Dump(items, count);
#endif
                    return false;
                }
                if (!IsNoteType(type))
                {
#if DEBUG
                    reason = $"note {i} has type {type} under the name '{seed}'; " +
                             "the file is probably not encoded under that name";
                    Dump(items, count);
#endif
                    return false;
                }
                if (start < 0 || end < start || end > MaxChartMs)
                {
#if DEBUG
                    reason = $"note {i} runs {start}..{end}ms under the name '{seed}'; " +
                             "the file is probably not encoded under that name";
                    Dump(items, count);
#endif
                    return false;
                }
            }

            Diagnostics.Info($"decoded '{seed}': {count} notes, {_noteSize}-byte records");
            return true;
        }

        /// <summary>Logs the first note records when a chart is refused.</summary>
        private static void Dump(IntPtr items, int count)
        {
            int show = count < 4 ? count : 4;
            for (int i = 0; i < show; i++)
            {
                IntPtr note = items + i * _noteSize;

                var text = new System.Text.StringBuilder(_noteSize * 2);
                for (int b = 0; b < _noteSize; b++)
                    text.Append(Memory.U8(note + b).ToString("X2"));

                Diagnostics.Info($"  note[{i}] {text}");
            }
        }

        /// <summary>The four note types the shipped charts contain: tap, hold, flick, sky area.</summary>
        private static bool IsNoteType(int type) => type == 1 || type == 2 || type == 4 || type == 5;

#if IF_SPC_API
        /// <summary>Whether the file starts with the game's release-chart marker.</summary>
        private static bool StartsWithIcp1(byte[] bytes) =>
            bytes != null && bytes.Length >= 4 &&
            bytes[0] == (byte)'I' && bytes[1] == (byte)'C' &&
            bytes[2] == (byte)'P' && bytes[3] == (byte)'1';
#endif

        /// <summary>Measures the note stride from the live array.</summary>
        private static bool MeasureNoteSize(IntPtr items, int count)
        {
            if (!ResolveNoteFields()) return false;
            if (_strideMissing) return false;

            try
            {
                IntPtr array = items - Offsets.Runtime.ArrayDataOffset;
                long byteLength = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(array);
                int stride = (int)(byteLength / count);

                if (stride > 0)
                {
                    if (_noteSize > 0 && stride != _noteSize)
                        Diagnostics.Warn($"note records measure {stride} bytes, not the {_noteSize} measured before");

                    _noteSize = stride;
                    return true;
                }
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"could not measure the note stride: {Diagnostics.Describe(e)}");
            }

            _strideMissing = true;
            Diagnostics.Error("the note records' stride could not be measured in this build; " +
                              "no chart can be checked, so none will load");
            return false;
        }

        private static bool _offsetsResolved;

        private static bool _noteFieldsMissing;

        private static bool _strideMissing;

        /// <summary>Resolves the four note field offsets.</summary>
        private static bool ResolveNoteFields()
        {
            if (_offsetsResolved) return true;
            if (_noteFieldsMissing) return false;

            if (FieldResolver.ClassPointer("_fA") == IntPtr.Zero) return false;

            Offsets.Note.Side = FieldResolver.Field("_fA", "_Ae");
            Offsets.Note.Type = FieldResolver.Field("_fA", "_be");
            Offsets.Note.StartMs = FieldResolver.Field("_fA", "_Be");
            Offsets.Note.EndMs = FieldResolver.Field("_fA", "_ce");

            if (Offsets.Note.Side < 0 || Offsets.Note.Type < 0 ||
                Offsets.Note.StartMs < 0 || Offsets.Note.EndMs < 0)
            {
                _noteFieldsMissing = true;
                Diagnostics.Error("the note record's fields could not be read by name in this build; " +
                                  "no chart can be checked, so none will load");
                return false;
            }

            _offsetsResolved = true;
            return true;
        }
    }
}
