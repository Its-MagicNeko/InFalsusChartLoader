using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`SongData._apA` — the song's jacket reference.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ApAFn(IntPtr songData, IntPtr songInfo, byte large, IntPtr methodInfo);

        /// <summary>`_MIA<T>` — the non-blocking loader.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MiaFn(IntPtr owner, IntPtr releaser, IntPtr reference, byte forceReload,
                                      IntPtr methodInfo);

        private static NativeHook<ApAFn> _apa;
        private static ApAFn _apaTramp;
        private static NativeHook<MiaFn> _mia;
        private static MiaFn _miaTramp;

        /// <summary>`SongData._ZOA` — the chart's jacket reference.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr ZoaFn(IntPtr songData, IntPtr chartInfo, byte large, IntPtr methodInfo);

        /// <summary>`_LIA<T>` — the blocking loader.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr LiaFn(IntPtr owner, IntPtr reference, IntPtr methodInfo);

        private static NativeHook<ZoaFn> _zoa;
        private static ZoaFn _zoaTramp;
        private static NativeHook<LiaFn> _lia;
        private static LiaFn _liaTramp;

        /// <summary>`GameplayBackgrounds._UmA` — the in-play background reference.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr UmaFn(IntPtr backgrounds, int background, IntPtr methodInfo);

        private static NativeHook<UmaFn> _uma;
        private static UmaFn _umaTramp;

        private static bool InstallJacket()
        {
            IntPtr reference = MethodResolver.ByName("SongData", "_apA");
            if (reference == IntPtr.Zero) return false;

            byte[] referencePrologue = Prologue(reference);
            _apa = new NativeHook<ApAFn>
            {
                Target = reference,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, IntPtr>)&ApADetour,
            };
            _apa.Attach();
            _apaTramp = _apa.Trampoline;
            Diagnostics.Info("SongData._apA hooked");

            bool ok = Landed(Hook.JacketReference, reference, referencePrologue);

            IntPtr load = MethodResolver.ByRuntime("AddressableHandleAutoReleaser", "_MIA");
            if (load == IntPtr.Zero)
            {
                Diagnostics.Error("AddressableHandleAutoReleaser._MIA could not be found; custom " +
                                  "jackets will not be substituted onto the song list");
            }
            else
            {
#if DEBUG
                Diagnostics.Info($"  the method's first words: {JacketFactory.Words(load, 4)}");
#endif

                byte[] loadPrologue = Prologue(load);
                _mia = new NativeHook<MiaFn>
                {
                    Target = load,
                    Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte, IntPtr, IntPtr>)&MiaDetour,
                };
                _mia.Attach();
                _miaTramp = _mia.Trampoline;
                Diagnostics.Info("AddressableHandleAutoReleaser._MIA hooked");
                ok &= Landed(Hook.JacketMaterial, load, loadPrologue);
            }

            ok &= InstallChartReference();
            ok &= InstallChartLoader();

            ok &= InstallBackground();
            return ok;
        }

        /// <summary>Installs the in-play background hook.</summary>
        private static bool InstallBackground()
        {
            IntPtr target = MethodResolver.ByName("GameplayBackgrounds", "_UmA");
            if (target == IntPtr.Zero)
            {
                Diagnostics.Warn("GameplayBackgrounds._UmA could not be found; a custom song will play " +
                                 "over the game's own background");
                return false;
            }

            byte[] prologue = Prologue(target);
            _uma = new NativeHook<UmaFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, IntPtr, IntPtr>)&UmaDetour,
            };
            _uma.Attach();
            _umaTramp = _uma.Trampoline;
            Diagnostics.Info("GameplayBackgrounds._UmA hooked");
            return Landed(Hook.Background, target, prologue);
        }

        /// <summary>Installs the `_ZOA` hook.</summary>
        private static bool InstallChartReference()
        {
            IntPtr target = MethodResolver.ByName("SongData", "_ZOA");
            if (target == IntPtr.Zero)
            {
                Diagnostics.Warn("SongData._ZOA could not be found; the loading screen and the " +
                                 "results screen will keep the game's fallback jacket");
                return false;
            }

            byte[] prologue = Prologue(target);
            _zoa = new NativeHook<ZoaFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, byte, IntPtr, IntPtr>)&ZoaDetour,
            };
            _zoa.Attach();
            _zoaTramp = _zoa.Trampoline;
            Diagnostics.Info("SongData._ZOA hooked");
            return Landed(Hook.ChartReference, target, prologue);
        }

        /// <summary>Installs the `_LIA` hook.</summary>
        private static bool InstallChartLoader()
        {
            IntPtr target = MethodResolver.ByRuntime("AddressableHandleAutoReleaser", "_LIA");
            if (target == IntPtr.Zero)
            {
                Diagnostics.Warn("AddressableHandleAutoReleaser._LIA could not be found; anything " +
                                 "that loads a jacket through it will draw the game's fallback");
                return false;
            }

            byte[] prologue = Prologue(target);
            _lia = new NativeHook<LiaFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr>)&LiaDetour,
            };
            _lia.Attach();
            _liaTramp = _lia.Trampoline;
            Diagnostics.Info("AddressableHandleAutoReleaser._LIA hooked");
            return Landed(Hook.ChartMaterial, target, prologue);
        }

        private static void DetachJacket()
        {
            _uma?.Detach();
            _uma = null;
            _umaTramp = null;

            _lia?.Detach();
            _lia = null;
            _liaTramp = null;

            _zoa?.Detach();
            _zoa = null;
            _zoaTramp = null;

            _mia?.Detach();
            _mia = null;
            _miaTramp = null;

            _apa?.Detach();
            _apa = null;
            _apaTramp = null;
        }

        /// <summary>Answers a jacket request and arms the material for the load.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr ApADetour(IntPtr songData, IntPtr songInfo, byte large, IntPtr methodInfo)
        {
            IntPtr fallback = _apaTramp(songData, songInfo, large, methodInfo);
#if DEBUG
            ApACalls++;
#endif

            if (!Faulted)
            {
#if DEBUG
                long t0 = Now;
#endif
                try
                {
                    if (JacketCatalog.Find(songInfo, out IntPtr material))
                    {
                        JacketCatalog.Arm(fallback, material, false);
#if DEBUG
                        ApAArmed++;
#endif
                    }
                    else JacketCatalog.Disarm();
                }
                catch (Exception e)
                {
                    JacketCatalog.Disarm();
                    Fault(Hook.JacketReference, e);
                }
#if DEBUG
                TicksApa += Now - t0;
#endif
            }

            return fallback;
        }

        /// <summary>Substitutes the armed material on its load.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr MiaDetour(IntPtr owner, IntPtr releaser, IntPtr reference, byte forceReload,
                                        IntPtr methodInfo)
        {
#if DEBUG
            MiaCalls++;
#endif

            if (!Faulted)
            {
#if DEBUG
                long t0 = Now;
#endif
                try
                {
                    if (JacketCatalog.TryClaim(reference, out IntPtr material, out bool _))
                    {
#if DEBUG
                        MiaClaimed++;
                        TicksMia += Now - t0;
#endif
                        return material;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.JacketMaterial, e);
                }
#if DEBUG
                TicksMia += Now - t0;
#endif
            }

            return _miaTramp(owner, releaser, reference, forceReload, methodInfo);
        }

        /// <summary>Answers a chart's jacket request and arms the material for the load.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr ZoaDetour(IntPtr songData, IntPtr chartInfo, byte large, IntPtr methodInfo)
        {
            IntPtr fallback = _zoaTramp(songData, chartInfo, large, methodInfo);
#if DEBUG
            ZoaCalls++;
#endif

            if (!Faulted)
            {
                try
                {
                    if (JacketCatalog.FindByChart(chartInfo, out IntPtr material))
                    {
                        JacketCatalog.Arm(fallback, material, false);
#if DEBUG
                        ZoaArmed++;
#endif
                    }
                    else JacketCatalog.Disarm();
                }
                catch (Exception e)
                {
                    JacketCatalog.Disarm();
                    Fault(Hook.ChartReference, e);
                }
            }

            return fallback;
        }

        /// <summary>Substitutes the armed material on its blocking load.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr LiaDetour(IntPtr owner, IntPtr reference, IntPtr methodInfo)
        {
#if DEBUG
            LiaCalls++;
#endif

            if (!Faulted)
            {
                try
                {
                    if (JacketCatalog.TryClaim(reference, out IntPtr material, out bool background))
                    {
#if DEBUG
                        LiaClaimed++;
#endif
                        return background
                            ? CloneBackground(owner, reference, material, methodInfo)
                            : material;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.ChartMaterial, e);
                }
            }

            return _liaTramp(owner, reference, methodInfo);
        }

        /// <summary>Returns a clone of the game's background material carrying this mod's picture.</summary>
        private static IntPtr CloneBackground(IntPtr owner, IntPtr reference, IntPtr ours, IntPtr methodInfo)
        {
            IntPtr game = _liaTramp(owner, reference, methodInfo);

            if (_backgroundCloneFailed) return game;

            string reason = null;
            if (!Memory.LooksLikeObject(game)) reason = "the game's own material did not load";
            IntPtr clone = reason == null ? BackgroundClone.Build(game, ours, out reason) : IntPtr.Zero;

            if (clone != IntPtr.Zero)
            {
                Diagnostics.Info("the background material is a clone of the game's own");
                return clone;
            }

            _backgroundCloneFailed = true;
            Diagnostics.Error($"the background material could not be cloned from the game's own: " +
                              $"{reason}; this mod's background substitution is off for this session");
            return game;
        }

        /// <summary>Whether the background clone failed for this session.</summary>
        private static bool _backgroundCloneFailed;

        /// <summary>Answers a background request when the played song names one.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr UmaDetour(IntPtr backgrounds, int background, IntPtr methodInfo)
        {
            IntPtr fallback = _umaTramp(backgrounds, background, methodInfo);
#if DEBUG
            UmaCalls++;
#endif

            if (!Faulted)
            {
                try
                {
                    string why = null;
                    var entry = default(BackgroundCatalog.Entry);
                    bool armed = Selection.TryReadSong(out IntPtr song) &&
                                 BackgroundCatalog.Find(song, Selection.Difficulty(),
                                                        out entry, out why);

                    if (armed)
                    {
                        JacketCatalog.Arm(fallback, entry.Material, true);
#if DEBUG
                        UmaArmed++;
#endif
                    }
                    else
                    {
                        JacketCatalog.Disarm();

                        VideoBackground.Stop();

                        if (why != null)
                        {
#if DEBUG
                            UmaUnset++;
#endif
                            Diagnostics.Warn($"the in-play background was not replaced: {why}");
                        }
                    }
                }
                catch (Exception e)
                {
                    JacketCatalog.Disarm();
                    Fault(Hook.Background, e);
                }
            }

            return fallback;
        }
    }
}
