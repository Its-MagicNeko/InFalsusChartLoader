using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`_Dg._hiA` and `_Dg._KiA` — the scene's music pausing and resuming.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MusicPauseFn(IntPtr self, double at, IntPtr methodInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MusicResumeFn(IntPtr self, float volume, double time, IntPtr methodInfo);

        private static NativeHook<MusicPauseFn> _musicPause;
        private static MusicPauseFn _musicPauseTramp;
        private static NativeHook<MusicResumeFn> _musicResume;
        private static MusicResumeFn _musicResumeTramp;

        private static bool InstallPause()
        {
            IntPtr pause = MethodResolver.ByName("_Dg", "_hiA");
            if (pause == IntPtr.Zero) return false;

            IntPtr resume = MethodResolver.ByName("_Dg", "_KiA");
            if (resume == IntPtr.Zero) return false;

            byte[] pausePrologue = Prologue(pause);
            _musicPause = new NativeHook<MusicPauseFn>
            {
                Target = pause,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, double, IntPtr, void>)&MusicPauseDetour,
            };
            _musicPause.Attach();
            _musicPauseTramp = _musicPause.Trampoline;
            Diagnostics.Info("_Dg._hiA hooked");

            byte[] resumePrologue = Prologue(resume);
            _musicResume = new NativeHook<MusicResumeFn>
            {
                Target = resume,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, float, double, IntPtr, void>)&MusicResumeDetour,
            };
            _musicResume.Attach();
            _musicResumeTramp = _musicResume.Trampoline;
            Diagnostics.Info("_Dg._KiA hooked");
            return Landed(Hook.MusicPause, pause, pausePrologue) & Landed(Hook.MusicResume, resume, resumePrologue);
        }

        private static void DetachPause()
        {
            _musicResume?.Detach();
            _musicResume = null;
            _musicResumeTramp = null;

            _musicPause?.Detach();
            _musicPause = null;
            _musicPauseTramp = null;
        }

        /// <summary>Holds this mod's clip when the game pauses its music.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void MusicPauseDetour(IntPtr self, double at, IntPtr methodInfo)
        {
#if DEBUG
            MusicPauses++;
#endif
            _musicPauseTramp(self, at, methodInfo);

            if (!Faulted)
            {
                try
                {
                    VideoBackground.Hold(at);
                }
                catch (Exception e)
                {
                    Fault(Hook.MusicPause, e);
                }
            }
        }

        /// <summary>Resumes this mod's clip when the game resumes its music.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void MusicResumeDetour(IntPtr self, float volume, double time, IntPtr methodInfo)
        {
#if DEBUG
            MusicResumes++;
#endif
            _musicResumeTramp(self, volume, time, methodInfo);

            if (!Faulted)
            {
                try
                {
                    VideoBackground.Resume(atMusicReturn: true);
                }
                catch (Exception e)
                {
                    Fault(Hook.MusicResume, e);
                }
            }
        }
    }
}
