using System;
using System.Collections.Generic;
using PositionMap = Il2CppSystem.Collections.Generic.Dictionary<string, string>;

namespace InFalsusChartLoader
{
    /// <summary>Where the song select stopped in the custom categories, kept in <c>IFCL.sav</c>'s own selection slot.</summary>
    /// <remarks>The game's own save keeps the game packs and the game songs; this keeps only what is ours.</remarks>
    internal static unsafe class Positions
    {
        private const string LastKey = "LAST";
        private const string RootKey = "IFCL";
        private const string PackPrefix = "PACK-";
        private const string GamePrefix = "GAME-";

        /// <summary>`PackId.Invalid` — all songs.</summary>
        private const ushort AllSongs = ushort.MaxValue;

        private static PositionMap _map;
        private static IntPtr _formatter;
        private static IntPtr _serialize;
        private static IntPtr _deserialize;

        /// <summary>Whether the positions are answered from this mod's dictionary.</summary>
        internal static bool Ready;

        /// <summary>Whether the game's formatter for the dictionary was found.</summary>
        internal static bool Created => _deserialize != IntPtr.Zero;

        /// <summary>Makes an empty dictionary and asks the game for its formatter.</summary>
        internal static bool Create()
        {
            if (Created) return true;

            try
            {
                var map = new PositionMap();
                IntPtr mapClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(map.Pointer);

                IntPtr formatter = FormatterOf(mapClass);
                IntPtr formatterClass = formatter == IntPtr.Zero
                    ? IntPtr.Zero
                    : Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(formatter);
                IntPtr serialize = formatterClass == IntPtr.Zero
                    ? IntPtr.Zero
                    : Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(formatterClass, "Serialize", 2);
                IntPtr deserialize = formatterClass == IntPtr.Zero
                    ? IntPtr.Zero
                    : Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(formatterClass, "Deserialize", 2);
                if (serialize == IntPtr.Zero || deserialize == IntPtr.Zero) return false;

                _map = map;
                _formatter = formatter;
                _serialize = serialize;
                _deserialize = deserialize;
                return true;
            }
            catch (Exception e)
            {
                Diagnostics.Warn("the position dictionary could not be made: " + Diagnostics.Describe(e));
                return false;
            }
        }

        /// <summary>Writes the dictionary with the game's formatter, in place of the slot's own value.</summary>
        internal static void Write(IntPtr writer) => Invoke(_serialize, writer, _map.Pointer, out _);

        /// <summary>Reads the dictionary with the game's formatter from the slot.</summary>
        internal static void Read(IntPtr reader)
        {
            if (Invoke(_deserialize, reader, IntPtr.Zero, out IntPtr map) && map != IntPtr.Zero)
                _map = new PositionMap(map);
        }

        private static bool _failed;

        private static bool Invoke(IntPtr method, IntPtr stream, IntPtr value, out IntPtr result)
        {
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = stream;
            args[1] = (IntPtr)(&value);

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, _formatter, (void**)args, ref raised);
            result = value;
            if (raised == IntPtr.Zero) return true;

            if (!_failed)
            {
                _failed = true;
                Diagnostics.Error("the song select positions could not be read or written with the custom " +
                                  "results file: " + Raised.Text(raised));
            }
            return false;
        }

        /// <summary>This run's id of the custom pack last stopped at, or all songs when it is gone; false when the game's save answers.</summary>
        internal static bool LastPack(out ushort pack)
        {
            pack = AllSongs;
            if (!_map.TryGetValue(LastKey, out string key)) return false;

            var identity = key == RootKey ? (true, string.Empty) : (false, key.Substring(PackPrefix.Length));
            if (!SongCatalog.PackIds.TryGetValue(identity, out pack))
            {
                pack = AllSongs;
                _map.Remove(LastKey);
            }
            return true;
        }

        /// <summary>Records the pack moved to; false for a game pack, which the game records.</summary>
        internal static bool SelectPack(ushort pack)
        {
            if (SongCatalog.LivePacks.TryGetValue(pack, out PackManifest manifest))
            {
                _map[LastKey] = KeyOf(manifest);
                return true;
            }

            _map.Remove(LastKey);
            return false;
        }

        /// <summary>This run's id of the custom song a pack remembers, or no song; false when the game's save answers.</summary>
        internal static bool SavedSong(ushort pack, ushort none, out ushort song)
        {
            song = none;
            if (!_map.TryGetValue(KeyOf(pack), out string name)) return SongCatalog.IsCustomPack(pack);

            foreach (KeyValuePair<ushort, ChartInfo> live in SongCatalog.ById)
            {
                if (live.Value.BaseName != name) continue;
                song = live.Key;
                break;
            }
            return true;
        }

        /// <summary>Records the song a pack stopped at; false for a game song in a game pack, which the game records.</summary>
        internal static bool SaveSong(ushort pack, ushort song)
        {
            string key = KeyOf(pack);

            if (SongCatalog.ById.TryGetValue(song, out ChartInfo info))
            {
                _map[key] = info.BaseName;
                return true;
            }

            _map.Remove(key);
            return SongCatalog.IsCustomPack(pack);
        }

        /// <summary>`MemoryPackFormatterProvider.GetFormatter&lt;T&gt;()` — the formatter the game registered for a type.</summary>
        internal static IntPtr FormatterOf(IntPtr typeClass)
        {
            IntPtr provider = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass("MemoryPack.dll", "MemoryPack",
                                                                          "MemoryPackFormatterProvider");
            IntPtr definition = CustomResults.MethodOn(provider, "GetFormatter", 0, genericOnly: true);
            if (definition == IntPtr.Zero || typeClass == IntPtr.Zero) return IntPtr.Zero;

            IntPtr method = MethodResolver.InflatedMethod(provider, definition, "MemoryPackFormatterProvider",
                                                          "GetFormatter", typeClass);
            if (method == IntPtr.Zero) return IntPtr.Zero;

            IntPtr raised = IntPtr.Zero;
            IntPtr formatter = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, IntPtr.Zero, null, ref raised);
            return raised == IntPtr.Zero && Memory.LooksLikeObject(formatter) ? formatter : IntPtr.Zero;
        }

        /// <summary>The key a pack's song is remembered under.</summary>
        private static string KeyOf(ushort pack) =>
            SongCatalog.LivePacks.TryGetValue(pack, out PackManifest manifest) ? KeyOf(manifest) : GamePrefix + pack;

        /// <summary>The key a custom pack is remembered under.</summary>
        private static string KeyOf(PackManifest pack) => pack.UseGamePictures ? RootKey : PackPrefix + pack.Id;
    }
}
