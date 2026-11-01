using System;

namespace InFalsusChartLoader
{
    /// <summary>The game's outstanding-read sets, kept current after this mod serves a read.</summary>
    internal static unsafe class PendingReads
    {
        /// <summary>`_hF._vSA` — reads handed to the native side and not finished yet.</summary>
        private static int FieldDispatched = Offsets.Unresolved;

        /// <summary>`_hF._VSA` — reads the dispatcher has decided to skip.</summary>
        private static int FieldCancelled = Offsets.Unresolved;

        /// <summary>`_hF._tSA`, the manager's state word.</summary>
        private static int FieldState = Offsets.Unresolved;

        private static bool _fieldsResolved;

        private static bool _fieldsMissing;

        /// <summary>Resolves the three read-bookkeeping fields.</summary>
        private static bool ResolveFields()
        {
            if (_fieldsResolved) return true;
            if (_fieldsMissing) return false;

            FieldDispatched = FieldResolver.Field("_hF", "_vSA");
            FieldCancelled = FieldResolver.Field("_hF", "_VSA");
            FieldState = FieldResolver.Field("_hF", "_tSA");

            if (FieldDispatched < 0 || FieldCancelled < 0 || FieldState < 0)
            {
                _fieldsMissing = true;
                Diagnostics.Error("the read bookkeeping's fields could not be read by name in this build; " +
                                  "a served read will not release its block");
                return false;
            }

            _fieldsResolved = true;
            return true;
        }

#if DEBUG
        /// <summary>The value of that word while the manager is not taking reads.</summary>
        private const int NotReading = 2;

        /// <summary>Reads the game had already marked to be skipped.</summary>
        internal static long Skipped;

        /// <summary>Reads that arrived while the manager was not taking reads.</summary>
        internal static long NotTaken;

        /// <summary>Reads this mod served and then released.</summary>
        internal static long Released;
#endif

        private static IntPtr _statics;
        private static IntPtr _remove, _removeInfo;
        private static IntPtr _contains, _containsInfo;
        private static bool _ready;

        /// <summary>Releases a served read from the game's outstanding and cancelled sets.</summary>
        internal static void Finished(IntPtr record)
        {
            if (!Ready()) return;

            IntPtr info = Memory.Ptr(record);
            if (info == IntPtr.Zero) return;

            Call(_remove, _removeInfo, Memory.Ptr(_statics + FieldDispatched), info);
            Call(_remove, _removeInfo, Memory.Ptr(_statics + FieldCancelled), info);

#if DEBUG
            Released++;
#endif
        }

#if DEBUG
        /// <summary>Counts a read that is about to be skipped, without changing the sets.</summary>
        internal static void Observe(IntPtr record)
        {
            if (!Ready()) return;

            if (Memory.I32(_statics + FieldState) == NotReading) { NotTaken++; return; }

            IntPtr info = Memory.Ptr(record);
            if (info == IntPtr.Zero) return;

            if (Call(_contains, _containsInfo, Memory.Ptr(_statics + FieldCancelled), info) != 0)
                Skipped++;
        }

        /// <summary>How many reads are outstanding, for the probe; -1 when the sets are not reached.</summary>
        internal static int Outstanding()
        {
            if (!Ready()) return -1;
            IntPtr dispatched = Memory.Ptr(_statics + FieldDispatched);
            return Memory.LooksLikeObject(dispatched)
                ? Memory.I32(dispatched + Offsets.Runtime.HashSetCount)
                : -1;
        }

#endif
        /// <summary>Reaches the sets and the methods used on them, once.</summary>
        private static bool Ready()
        {
            if (_ready) return true;

            try
            {
                if (_statics == IntPtr.Zero)
                {
                    IntPtr klass = FieldResolver.ClassPointer("_hF");
                    if (klass == IntPtr.Zero) return false;

                    _statics = FieldResolver.Statics(klass);
                    if (_statics == IntPtr.Zero) return false;
                }

                if (!ResolveFields()) return false;

                IntPtr sample = Memory.Ptr(_statics + FieldDispatched);
                if (!Memory.LooksLikeObject(sample)) return false;

                IntPtr setClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(sample);
                if (setClass == IntPtr.Zero) return false;

                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(setClass);

                _remove = MethodOn(setClass, "Remove", 1, out _removeInfo);
                _contains = MethodOn(setClass, "Contains", 1, out _containsInfo);
                if (_remove == IntPtr.Zero || _contains == IntPtr.Zero)
                {
#if DEBUG
                    Report("the outstanding-read sets could not be read; a served read will not " +
                           "release its block");
#endif
                    return false;
                }

                _ready = true;
#if DEBUG
                Diagnostics.Info($"read bookkeeping reached: Remove 0x{_remove.ToInt64():X}, " +
                                 $"Contains 0x{_contains.ToInt64():X}, " +
                                 $"outstanding now {Outstanding()}");
#endif
                return true;
            }
            catch (Exception e)
            {
#if DEBUG
                Report($"the outstanding-read sets could not be reached: {Diagnostics.Describe(e)}");
#else
                _ = e;
#endif
                return false;
            }
        }

#if DEBUG
        /// <summary>Logs why the sets could not be reached, once.</summary>
        private static void Report(string why)
        {
            if (_warned) return;
            _warned = true;
            Diagnostics.Warn(why);
        }

        private static bool _warned;
#endif

        /// <summary>A method by name and parameter count, with its MethodInfo.</summary>
        private static IntPtr MethodOn(IntPtr klass, string name, int parameters, out IntPtr methodInfo)
        {
            methodInfo = IntPtr.Zero;

            IntPtr iter = IntPtr.Zero;
            IntPtr method;
            while ((method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
            {
                if (Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name_(method) != name) continue;
                if ((int)Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(method) != parameters) continue;

                IntPtr code = *(IntPtr*)method;
                if (code == IntPtr.Zero) continue;

                methodInfo = method;
                return code;
            }

            return IntPtr.Zero;
        }

        /// <summary>Calls one of those methods.</summary>
        private static byte Call(IntPtr code, IntPtr methodInfo, IntPtr set, IntPtr value) =>
            ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>)code)(set, value, methodInfo);
    }
}
