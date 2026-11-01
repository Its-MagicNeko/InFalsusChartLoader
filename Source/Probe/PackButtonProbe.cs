#if DEBUG
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    /// <summary>Tracks the pack button's transitions across chart reloads.</summary>
    internal static unsafe class PackButtonProbe
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StepFn(IntPtr self, IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CompleteFn(IntPtr self, IntPtr tween, IntPtr methodInfo);

        private static NativeHook<CompleteFn> _completeHook;
        private static CompleteFn _complete;
        private static NativeHook<StepFn> _updateHook, _cancelHook, _endHook, _addHook;
        private static StepFn _update, _cancel, _end, _add;
        private static int _current, _target, _transition, _busy, _assets, _table;
        private static int _component, _id, _time, _duration, _bits;
        private static int _calculated, _material;
        private static readonly string[] VisualNames =
        {
            "currentNonHoverButton", "currentHoverButton",
            "targetNonHoverButton", "targetHoverButton",
            "currentContainer", "targetContainer",
        };
        private static readonly int[] VisualFields = new int[VisualNames.Length];
        private static readonly HashSet<IntPtr> Containers = new HashSet<IntPtr>();
        private static readonly HashSet<IntPtr> Tweens = new HashSet<IntPtr>();
        private static IntPtr _button;
        private static string _last;
        private static long _sequence;
        private static bool _ready, _failed;

        /// <summary>Installs observation hooks for the button and its animations.</summary>
        internal static void Install()
        {
            try
            {
                _current = FieldResolver.Field("PackSelectButtonContainer", "_Ar");
                _target = FieldResolver.Field("PackSelectButtonContainer", "_br");
                _transition = FieldResolver.Field("PackSelectButtonContainer", "_Br");
                _busy = FieldResolver.Field("PackSelectButtonContainer", "_cr");
                _assets = FieldResolver.Field("PackSelectButtonContainer", "packSelectSceneAssets");
                _table = FieldResolver.Field("PackSelectSceneAssets", "packToAssets");
                _component = FieldResolver.Field("TweenInstanceUnsafe", "Component");
                _id = FieldResolver.Field("TweenInstanceUnsafe", "Id");
                _time = FieldResolver.Field("TweenInstanceUnsafe", "Time");
                _duration = FieldResolver.Field("TweenInstanceUnsafe", "Duration");
                _bits = FieldResolver.Field("TweenInstanceUnsafe", "bits");
                _calculated = FieldResolver.Field("Constrained2D", "Calculated");
                _material = FieldResolver.Field("_ti", "_OHb");
                foreach (int field in new[] { _current, _target, _transition, _busy, _assets, _table,
                                             _component, _id, _time, _duration, _bits, _calculated, _material })
                    if (field < 0) return;

                for (int i = 0; i < VisualFields.Length; i++)
                {
                    VisualFields[i] = FieldResolver.Field("PackSelectButtonContainer", VisualNames[i]);
                    if (VisualFields[i] < 0) return;
                }

                _ready = true;
                Attach("PackSelectButtonContainer", "Update",
                    (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&Update,
                    out _updateHook, out _update);
                IntPtr complete = MethodResolver.ByRuntime("PackSelectButtonContainer", "_fTA");
                if (complete != IntPtr.Zero)
                {
                    _completeHook = new NativeHook<CompleteFn>
                    {
                        Target = complete,
                        Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&Complete,
                    };
                    _completeHook.Attach();
                    _complete = _completeHook.Trampoline;
                }
                else Diagnostics.Error("probe: pack button cannot observe its completion callback");

                Attach("TweenInstanceUnsafe", "OnAdd",
                    (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&Add,
                    out _addHook, out _add);
                Attach("TweenInstanceUnsafe", "Cancel",
                    (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&Cancel,
                    out _cancelHook, out _cancel);
                Attach("TweenInstanceUnsafe", "OnEnd",
                    (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&End,
                    out _endHook, out _end);
            }
            catch (Exception e) { Failed(e); }
        }

        private static void Attach(string type, string name, IntPtr detour,
                                   out NativeHook<StepFn> hook, out StepFn trampoline)
        {
            hook = null;
            trampoline = null;
            IntPtr target = MethodResolver.ByRuntime(type, name);
            if (target == IntPtr.Zero)
            {
                Diagnostics.Error($"probe: pack button cannot observe {type}.{name}");
                return;
            }
            hook = new NativeHook<StepFn> { Target = target, Detour = detour };
            hook.Attach();
            trampoline = hook.Trampoline;
            Diagnostics.Info($"probe: pack button observing {type}.{name} at 0x{target.ToInt64():X}");
        }

        /// <summary>Removes the observation hooks.</summary>
        internal static void Uninstall()
        {
            _updateHook?.Detach();
            _completeHook?.Detach();
            _addHook?.Detach();
            _cancelHook?.Detach();
            _endHook?.Detach();
            _ready = false;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Update(IntPtr self, IntPtr methodInfo)
        {
            Observe(self, "update before");
            _update(self, methodInfo);
            Observe(self, "update after");
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Complete(IntPtr self, IntPtr tween, IntPtr methodInfo)
        {
            Observe(self, "completion before");
            _complete(self, tween, methodInfo);
            Observe(self, "completion after");
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Add(IntPtr self, IntPtr methodInfo)
        {
            Tween(self, "add before");
            _add(self, methodInfo);
            Tween(self, "add after");
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void Cancel(IntPtr self, IntPtr methodInfo)
        {
            Tween(self, "cancel before");
            _cancel(self, methodInfo);
            Tween(self, "cancel after");
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void End(IntPtr self, IntPtr methodInfo)
        {
            Tween(self, "end before");
            _end(self, methodInfo);
            Tween(self, "end after");
        }

        private static void Observe(IntPtr button, string when)
        {
            if (!_ready || _failed) return;
            try
            {
                if (_button != button)
                {
                    _button = button;
                    _last = null;
                    Containers.Clear();
                    Tweens.Clear();
                }
                foreach (int field in VisualFields)
                {
                    IntPtr visual = Memory.Ptr(button + field);
                    if (Memory.LooksLikeObject(visual)) Containers.Add(visual);
                }
                string state = State();
                if (state == _last) return;
                _last = state;
                Report(when, state);
            }
            catch (Exception e) { Failed(e); }
        }

        /// <summary>Records the button at a reload boundary.</summary>
        internal static void Snapshot(string when)
        {
            if (!_ready || _failed) return;
            try
            {
                IntPtr scene = SongSelectRefresh.Scene();
                int field = FieldResolver.Field("SongSelectScene", "packSelectButtonContainer");
                if (field < 0) return;
                IntPtr button = Memory.LooksLikeObject(scene) ? Memory.Ptr(scene + field) : IntPtr.Zero;
                if (Memory.LooksLikeObject(button)) Observe(button, when);
                Report(when, button == _button && button != IntPtr.Zero ? State() : "no live button");
            }
            catch (Exception e) { Failed(e); }
        }

        private static void Tween(IntPtr tween, string when)
        {
            if (!_ready || _failed || _button == IntPtr.Zero) return;
            try
            {
                IntPtr component = Memory.Ptr(tween + _component);
                bool ours = Containers.Contains(component);
                if (when == "add before")
                {
                    Tweens.Remove(tween);
                    if (ours) Tweens.Add(tween);
                }
                if (!ours && !Tweens.Contains(tween)) return;
                Report(when, $"tween=0x{tween.ToInt64():X} id={Memory.I64(tween + _id)} " +
                             $"component=0x{component.ToInt64():X} " +
                             $"time={Memory.F32(tween + _time):R}/{Memory.F32(tween + _duration):R} " +
                             $"bits=0x{Memory.I64(tween + _bits):X} {State()}");
            }
            catch (Exception e) { Failed(e); }
        }

        private static string State()
        {
            string state = $"button=0x{_button.ToInt64():X} " +
                           $"current={Memory.U16(_button + _current)} target={Memory.U16(_button + _target)} " +
                           $"transition={Memory.U16(_button + _transition)} busy={Memory.U8(_button + _busy)}";
            for (int i = 0; i < VisualFields.Length; i++)
            {
                IntPtr visual = Memory.Ptr(_button + VisualFields[i]);
                IntPtr material = Memory.LooksLikeObject(visual)
                    ? Memory.Ptr(visual + _calculated + _material) : IntPtr.Zero;
                state += $" {VisualNames[i]}=0x{visual.ToInt64():X}/0x{material.ToInt64():X}";
            }
            IntPtr assets = Memory.Ptr(_button + _assets);
            IntPtr table = Memory.LooksLikeObject(assets) ? Memory.Ptr(assets + _table) : IntPtr.Zero;
            state += $" assets=0x{assets.ToInt64():X} table=0x{table.ToInt64():X}";
            if (Memory.LooksLikeObject(table)) state += $" rows={Memory.I32(table + Offsets.Runtime.ArrayLength)}";
            return state;
        }

        private static void Report(string when, string state) =>
            Diagnostics.Info($"probe: pack button #{++_sequence} {when}: {state}");

        private static void Failed(Exception e)
        {
            if (_failed) return;
            _failed = true;
            Diagnostics.Error("probe: pack button observation stopped: " + Diagnostics.Describe(e));
        }
    }
}
#endif
