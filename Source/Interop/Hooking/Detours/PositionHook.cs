using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`_NH._aoA` — the pack the song select opens on.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ushort CurrentPackFn(IntPtr self, IntPtr methodInfo);

        /// <summary>`_NH._hoA` — the pack moved to.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SelectPackFn(IntPtr self, ushort pack, IntPtr methodInfo);

        /// <summary>`_NH._zNA` — the song a pack remembers.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate ushort SavedSongFn(IntPtr self, ushort pack, IntPtr methodInfo);

        /// <summary>`_NH._ZNA` — the song a pack stopped at.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SaveSongFn(IntPtr self, ushort pack, ushort song, IntPtr methodInfo);

        /// <summary>The game's formatter for the selection slot, `Dictionary&lt;PackId, SongId&gt;` — writing or reading it.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SlotFn(IntPtr formatter, IntPtr stream, IntPtr value, IntPtr methodInfo);

        /// <summary>`_NH._SoA` — the game saving its own save.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SaveRequestFn(IntPtr self, IntPtr callback, IntPtr methodInfo);

        private static NativeHook<CurrentPackFn> _currentPack;
        private static CurrentPackFn _currentPackTramp;
        private static NativeHook<SelectPackFn> _selectPack;
        private static SelectPackFn _selectPackTramp;
        private static NativeHook<SavedSongFn> _savedSong;
        private static SavedSongFn _savedSongTramp;
        private static NativeHook<SaveSongFn> _saveSong;
        private static SaveSongFn _saveSongTramp;
        private static NativeHook<SlotFn> _slotWrite;
        private static SlotFn _slotWriteTramp;
        private static NativeHook<SlotFn> _slotRead;
        private static SlotFn _slotReadTramp;
        private static NativeHook<SaveRequestFn> _saveRequest;
        private static SaveRequestFn _saveRequestTramp;

        /// <summary>`SongId.get_Invalid`, the game's own "no song".</summary>
        private static delegate* unmanaged[Cdecl]<IntPtr, ushort> _noSong;

        private const string PositionsUnavailable =
            "the song select's position methods are not in this build; the custom packs and songs will not be remembered";

        private static bool InstallPositions()
        {
            IntPtr current = MethodResolver.ByRuntime("_NH", "_aoA");
            IntPtr select = MethodResolver.ByRuntime("_NH", "_hoA");
            IntPtr saved = MethodResolver.ByRuntime("_NH", "_zNA");
            IntPtr save = MethodResolver.ByRuntime("_NH", "_ZNA");
            IntPtr noSong = MethodResolver.ByRuntime("SongId", "get_Invalid");
            if (current == IntPtr.Zero || select == IntPtr.Zero || saved == IntPtr.Zero || save == IntPtr.Zero ||
                noSong == IntPtr.Zero)
            {
                Diagnostics.Error(PositionsUnavailable);
                return false;
            }
            _noSong = (delegate* unmanaged[Cdecl]<IntPtr, ushort>)noSong;

            byte[] currentPrologue = Prologue(current);
            _currentPack = new NativeHook<CurrentPackFn>
            {
                Target = current,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, ushort>)&CurrentPackDetour,
            };
            _currentPack.Attach();
            _currentPackTramp = _currentPack.Trampoline;

            byte[] selectPrologue = Prologue(select);
            _selectPack = new NativeHook<SelectPackFn>
            {
                Target = select,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, ushort, IntPtr, void>)&SelectPackDetour,
            };
            _selectPack.Attach();
            _selectPackTramp = _selectPack.Trampoline;

            byte[] savedPrologue = Prologue(saved);
            _savedSong = new NativeHook<SavedSongFn>
            {
                Target = saved,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, ushort, IntPtr, ushort>)&SavedSongDetour,
            };
            _savedSong.Attach();
            _savedSongTramp = _savedSong.Trampoline;

            byte[] savePrologue = Prologue(save);
            _saveSong = new NativeHook<SaveSongFn>
            {
                Target = save,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, ushort, ushort, IntPtr, void>)&SaveSongDetour,
            };
            _saveSong.Attach();
            _saveSongTramp = _saveSong.Trampoline;

            Diagnostics.Info("_NH._aoA/_hoA/_zNA/_ZNA hooked");
            return Landed(Hook.CurrentPack, current, currentPrologue)
                 & Landed(Hook.SelectPack, select, selectPrologue)
                 & Landed(Hook.SavedSong, saved, savedPrologue)
                 & Landed(Hook.SaveSong, save, savePrologue);
        }

        /// <summary>Keeps the positions in <c>IFCL.sav</c>'s selection slot, and saves it whenever the game saves its own.</summary>
        internal static bool InstallSaveFollow()
        {
            if (_saveRequest != null) return true;

            IntPtr formatterClass = SlotFormatterClass();
            IntPtr write = formatterClass == IntPtr.Zero
                ? IntPtr.Zero
                : Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(formatterClass, "Serialize", 2);
            IntPtr read = formatterClass == IntPtr.Zero
                ? IntPtr.Zero
                : Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(formatterClass, "Deserialize", 2);
            IntPtr writeCode = write == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)write;
            IntPtr readCode = read == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)read;
            IntPtr request = MethodResolver.ByRuntime("_NH", "_SoA");
            if (writeCode == IntPtr.Zero || readCode == IntPtr.Zero || request == IntPtr.Zero) return false;

            byte[] writePrologue = Prologue(writeCode);
            _slotWrite = new NativeHook<SlotFn>
            {
                Target = writeCode,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&SlotWriteDetour,
            };
            _slotWrite.Attach();
            _slotWriteTramp = _slotWrite.Trampoline;

            byte[] readPrologue = Prologue(readCode);
            _slotRead = new NativeHook<SlotFn>
            {
                Target = readCode,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&SlotReadDetour,
            };
            _slotRead.Attach();
            _slotReadTramp = _slotRead.Trampoline;

            byte[] requestPrologue = Prologue(request);
            _saveRequest = new NativeHook<SaveRequestFn>
            {
                Target = request,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&SaveRequestDetour,
            };
            _saveRequest.Attach();
            _saveRequestTramp = _saveRequest.Trampoline;

            Diagnostics.Info("the selection slot's formatter and _NH._SoA hooked");
            return Landed(Hook.SlotWrite, writeCode, writePrologue)
                 & Landed(Hook.SlotRead, readCode, readPrologue)
                 & Landed(Hook.SaveRequest, request, requestPrologue);
        }

        /// <summary>The class of the formatter the game writes `GeneralSaveStateV5.PackIdToSelectedSongId` with.</summary>
        private static IntPtr SlotFormatterClass()
        {
            IntPtr stateClass = FieldResolver.ClassPointer("GeneralSaveStateV5");
            IntPtr field = stateClass == IntPtr.Zero
                ? IntPtr.Zero
                : Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(stateClass, "PackIdToSelectedSongId");
            IntPtr slotClass = field == IntPtr.Zero
                ? IntPtr.Zero
                : Il2CppInterop.Runtime.IL2CPP.il2cpp_class_from_type(Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_type(field));

            IntPtr formatter = Positions.FormatterOf(slotClass);
            return formatter == IntPtr.Zero ? IntPtr.Zero : Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(formatter);
        }

        private static void DetachPositions()
        {
            _saveRequest?.Detach();
            _saveRequest = null;
            _saveRequestTramp = null;

            _slotRead?.Detach();
            _slotRead = null;
            _slotReadTramp = null;

            _slotWrite?.Detach();
            _slotWrite = null;
            _slotWriteTramp = null;

            _saveSong?.Detach();
            _saveSong = null;
            _saveSongTramp = null;

            _savedSong?.Detach();
            _savedSong = null;
            _savedSongTramp = null;

            _selectPack?.Detach();
            _selectPack = null;
            _selectPackTramp = null;

            _currentPack?.Detach();
            _currentPack = null;
            _currentPackTramp = null;
        }

        /// <summary>Answers the custom pack last stopped at, and a game pack from the game's save.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static ushort CurrentPackDetour(IntPtr self, IntPtr methodInfo)
        {
            if (!Faulted && Positions.Ready)
            {
                try
                {
                    if (Positions.LastPack(out ushort last)) return last;
                }
                catch (Exception e)
                {
                    Fault(Hook.CurrentPack, e);
                }
            }

            return _currentPackTramp(self, methodInfo);
        }

        /// <summary>Records a custom pack here and a game pack in the game's save.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void SelectPackDetour(IntPtr self, ushort pack, IntPtr methodInfo)
        {
            bool ours = false;
            if (!Faulted && Positions.Ready)
            {
                try { ours = Positions.SelectPack(pack); }
                catch (Exception e) { Fault(Hook.SelectPack, e); }
            }

            if (!ours) _selectPackTramp(self, pack, methodInfo);
#if DEBUG
            SelectionRestoreProbe.PackSet(pack);
#endif
        }

        /// <summary>Answers a custom song from its <c>if</c> id, and the game's song from the game's save.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static ushort SavedSongDetour(IntPtr self, ushort pack, IntPtr methodInfo)
        {
            ushort song = 0;
            bool ours = false;
            if (!Faulted && Positions.Ready)
            {
                try { ours = Positions.SavedSong(pack, _noSong(IntPtr.Zero), out song); }
                catch (Exception e) { Fault(Hook.SavedSong, e); }
            }

            if (!ours) song = _savedSongTramp(self, pack, methodInfo);
#if DEBUG
            SelectionRestoreProbe.SongRead(pack, song);
#endif
            return song;
        }

        /// <summary>Records a custom song here and a game song of a game pack in the game's save.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void SaveSongDetour(IntPtr self, ushort pack, ushort song, IntPtr methodInfo)
        {
            bool ours = false;
            if (!Faulted && Positions.Ready)
            {
                try { ours = Positions.SaveSong(pack, song); }
                catch (Exception e) { Fault(Hook.SaveSong, e); }
            }

            if (!ours) _saveSongTramp(self, pack, song, methodInfo);
#if DEBUG
            SelectionRestoreProbe.SongWritten(pack, song);
#endif
        }

        /// <summary>Writes the positions into <c>IFCL.sav</c>'s selection slot; every other save is the game's.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void SlotWriteDetour(IntPtr formatter, IntPtr writer, IntPtr value, IntPtr methodInfo)
        {
            if (Faulted || !Positions.Created || !CustomResults.Writing)
            {
                _slotWriteTramp(formatter, writer, value, methodInfo);
                return;
            }

            try { Positions.Write(writer); }
            catch (Exception e) { Fault(Hook.SlotWrite, e); }
        }

        /// <summary>Reads the positions from <c>IFCL.sav</c>'s selection slot; every other save is the game's.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void SlotReadDetour(IntPtr formatter, IntPtr reader, IntPtr value, IntPtr methodInfo)
        {
            if (Faulted || !Positions.Created || !CustomResults.Reading)
            {
                _slotReadTramp(formatter, reader, value, methodInfo);
                return;
            }

            try { Positions.Read(reader); }
            catch (Exception e) { Fault(Hook.SlotRead, e); }
        }

        /// <summary>Saves <c>IFCL.sav</c> after the game saved its own.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void SaveRequestDetour(IntPtr self, IntPtr callback, IntPtr methodInfo)
        {
            _saveRequestTramp(self, callback, methodInfo);

            if (Faulted || !Positions.Ready) return;
            try
            {
                CustomResults.Save();
            }
            catch (Exception e)
            {
                Fault(Hook.SaveRequest, e);
            }
        }
    }
}
