using System;
using System.IO;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    /// <summary>The custom songs' results, kept in <c>IFCL.sav</c>.</summary>
    internal static unsafe class CustomResults
    {
        /// <summary>`_NH._DEb`, the container holding the game's own save.</summary>
        private static int SaveContainer = Offsets.Unresolved;

        private static int FieldFullPath = Offsets.Unresolved;      // `_NH._OH<T>._JEb` — the container's file
        private static int FieldGameResults = Offsets.Unresolved;   // `SingleFileSaveDataV2.GameResults`
        private static int FieldWrittenBytes = Offsets.Unresolved;  // `_NH._OH<T>._LEb` — bytes last written
        private static int FieldWrittenLength = Offsets.Unresolved; // `_NH._OH<T>._mEb` — and how many

        private const string FileName = "IFCL.sav";
        private const string TempFileName = "IFCL_temp.sav";

        /// <summary>The names the container calls its files by. Not paths: the paths are built here.</summary>
        private const string ShortName = "IFCL";
        private const string ShortTempName = "IFCL_temp";

        private static bool _resolved;

        /// <summary>The custom songs' results, a `GameResultsV4` of this mod's own.</summary>
        internal static IntPtr Table;

        /// <summary>Our save container — the object that owns `IFCL.sav`. Zero until prepared.</summary>
        internal static IntPtr Container;

        /// <summary>Where the file lives. For the log and the probes.</summary>
        internal static string FilePath;

        /// <summary>Counters for the probe.</summary>
        internal static long Reads, Served, Updates, Merged, Saves;

        /// <summary>Writes that landed while the save call raised.</summary>
        internal static long Late;

        internal static long EncounterWrites, EncounterDropped;

        private static bool _writeFailed;

        private static IntPtr _containerHandle;
        private static IntPtr _objectHandle;
        private static IntPtr _tableHandle;

        /// <summary>Whether the custom table exists and can be answered from.</summary>
        internal static bool Ready => Table != IntPtr.Zero;

        /// <summary>Whether a song record is one of this mod's.</summary>
        internal static bool IsOurs(IntPtr songInfo)
        {
            if (songInfo == IntPtr.Zero) return false;

            if (!Offsets.Ready) return false;
            return SongCatalog.CustomSongIds.Contains(Memory.U16(songInfo + Offsets.Song.Id));
        }

        /// <summary>Opens <c>IFCL.sav</c> and prepares the result table.</summary>
        internal static void Prepare()
        {
            if (Ready) return;
            try
            {
                IntPtr save = PlayerSave();
                int containerField = FieldResolver.Field("_NH", "_DEb");
                if (containerField < 0) { PrepareFailed("no field"); return; }
                SaveContainer = containerField;
                IntPtr official = save == IntPtr.Zero ? IntPtr.Zero : Memory.Ptr(save + SaveContainer);
                if (!Memory.LooksLikeObject(official))
                {
                    Diagnostics.Error("the game's save could not be reached; the custom songs' scores " +
                                      "will not be read or saved");
                    return;
                }

                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(official);
                if (!Resolve(klass)) { PrepareFailed("no field"); return; }

                string officialPath = Memory.Text(Memory.Ptr(official + FieldFullPath), 512);
                string folder = officialPath == null ? null : Path.GetDirectoryName(officialPath);
                if (string.IsNullOrEmpty(folder))
                {
                    Diagnostics.Error("the save folder could not be read; the custom songs' scores " +
                                      "will not be read or saved");
                    return;
                }

                string path = Path.Combine(folder, FileName);
                string tempPath = Path.Combine(folder, TempFileName);

                bool exists = File.Exists(path);
                byte[] known = null;
                IntPtr obj = exists ? Load(path, out known) : IntPtr.Zero;
                if (exists && obj == IntPtr.Zero)
                {
                    Diagnostics.Error("the custom results file exists but could not be read, so it was " +
                                      "left alone; the custom songs' scores will not be read or saved");
                    return;
                }

                if (obj == IntPtr.Zero)
                {
                    obj = CreateDefault();
                    if (obj == IntPtr.Zero) { PrepareFailed("no save object"); return; }
                }

                IntPtr table = Memory.Ptr(obj + FieldGameResults);
                if (!Memory.LooksLikeObject(table))
                {
                    table = NewResults();
                    if (table == IntPtr.Zero) { PrepareFailed("no table"); return; }
                    Memory.WritePtr(obj + FieldGameResults, table);
                }

                IntPtr container = NewContainer(klass, obj, path, tempPath, known);
                if (container == IntPtr.Zero) { PrepareFailed("no container"); return; }

                Root(ref _containerHandle, container);
                Root(ref _objectHandle, obj);
                Root(ref _tableHandle, table);

                string failed = SelfCheck(table);
                if (failed != null) { PrepareFailed(failed); return; }

                Container = container;
                Table = table;
                FilePath = path;

                Diagnostics.Load("custom results: " + (exists ? "opened " : "prepared ") + path);

                ResultKeys.Migrate(table);
                if (ResultKeys.Refiled > 0)
                {
                    Diagnostics.Load($"custom results: {ResultKeys.Refiled} record(s) filed under this build's song ids");
                    Save();
                }
            }
            catch (Exception e)
            {
                PrepareFailed(Diagnostics.Describe(e));
            }
        }

        /// <summary>Moves the stored records onto the rebuilt song ids.</summary>
        internal static void MigrateAndSave()
        {
            if (Table == IntPtr.Zero) return;

            long before = ResultKeys.Refiled;
            ResultKeys.Migrate(Table);
            if (ResultKeys.Refiled > before) Save();
        }

        /// <summary>Reports that the table could not be prepared.</summary>
        private static void PrepareFailed(string why) =>
            Diagnostics.Error("the custom results could not be prepared; the custom songs' scores " +
                              "will not be read or saved: " + why);

        /// <summary>The player's save object.</summary>
        private static IntPtr PlayerSave()
        {
            IntPtr klass = FieldResolver.ClassPointer("_NH");
            if (klass == IntPtr.Zero) return IntPtr.Zero;

            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return IntPtr.Zero;

            IntPtr save = Memory.Ptr(statics);
            return Memory.LooksLikeObject(save) ? save : IntPtr.Zero;
        }

        /// <summary>Keeps one of this mod's objects alive for the session.</summary>
        private static void Root(ref IntPtr handle, IntPtr obj)
        {
            if (handle != IntPtr.Zero || obj == IntPtr.Zero) return;

            try
            {
                handle = Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_new(obj, false);
                IntPtr back = Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_get_target(handle);
                if (back != obj)
                    Diagnostics.Warn($"a gc handle answered with 0x{back.ToInt64():X} for an object at " +
                                     $"0x{obj.ToInt64():X}; the custom results are not safe this session");
            }
            catch (Exception e)
            {
                Diagnostics.Warn("an object could not be rooted: " + Diagnostics.Describe(e));
            }
        }

        /// <summary>Reads one record from the table before it is published.</summary>
        private static string SelfCheck(IntPtr table)
        {
            IntPtr klass = FieldResolver.ClassPointer("GameResultsV4");
            IntPtr method = klass == IntPtr.Zero ? IntPtr.Zero : MethodOn(klass, "TryGetResult", 3);
            if (method == IntPtr.Zero) return "no self-test (the record reader could not be found)";

            try
            {
                byte* song = stackalloc byte[Offsets.Song.Size];
                for (int i = 0; i < Offsets.Song.Size; i++) song[i] = 0;
                *(IntPtr*)(song + Offsets.Song.BaseName) = Str("IFCL self test");

                byte difficulty = 1;

                int record = FieldResolver.ValueSize("GameResultV4");
                if (record <= 0) return "no self-test (the result record's size could not be asked of the game)";

                byte* result = stackalloc byte[record];

                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)song;
                args[1] = (IntPtr)(&difficulty);
                args[2] = (IntPtr)result;

                IntPtr raised = IntPtr.Zero;
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, table, (void**)args, ref raised);
                if (raised != IntPtr.Zero) return "the table failed its self-test: " + Raised.Text(raised);
                return null;
            }
            catch (Exception e)
            {
                return "the self-test could not be run: " + Diagnostics.Describe(e);
            }
        }

        /// <summary>Writes the table to <c>IFCL.sav</c>.</summary>
        internal static void Save()
        {
            if (Container == IntPtr.Zero) return;

            try
            {
                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(Container);
                IntPtr method = klass == IntPtr.Zero ? IntPtr.Zero : MethodOn(klass, "_JOA", 0);
                if (method == IntPtr.Zero)
                {
                    SaveFailed("the save method is not on the container in this build");
                    return;
                }

                long before = Stamp(FilePath);

                IntPtr raised = IntPtr.Zero;
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, Container, null, ref raised);
                if (raised == IntPtr.Zero)
                {
                    Saves++;
                    return;
                }

                if (Stamp(FilePath) != before)
                {
                    Saves++;
                    Late++;
                    return;
                }

                SaveFailed(Raised.Text(raised));
            }
            catch (Exception e)
            {
                SaveFailed(Diagnostics.Describe(e));
            }
        }

        /// <summary>A fingerprint of the save file: write time and length.</summary>
        private static long Stamp(string path)
        {
            try
            {
                var file = new FileInfo(path);
                return file.LastWriteTimeUtc.Ticks ^ file.Length;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>Reports that the file could not be written, once.</summary>
        private static void SaveFailed(string why)
        {
            if (_writeFailed) return;
            _writeFailed = true;
            Diagnostics.Error("the custom results file could not be written; this session's custom " +
                              "scores are not saved: " + why);
        }

        // ---------------------------------------------------------------- the file

        /// <summary>The save object read from <c>IFCL.sav</c>.</summary>
        private static IntPtr Load(string path, out byte[] raw)
        {
            raw = null;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"reading {FileName} failed: {Diagnostics.Describe(e)}");
                return IntPtr.Zero;
            }

            if (bytes.Length == 0)
            {
                Diagnostics.Warn($"{FileName} is empty");
                return IntPtr.Zero;
            }

            raw = bytes;
            return Deserialize(bytes);
        }

        /// <summary>Deserializes the file with the game's serializer.</summary>
        private static IntPtr Deserialize(byte[] bytes)
        {
            IntPtr saveClass = FieldResolver.ClassPointer("SingleFileSaveDataV2");
            if (saveClass == IntPtr.Zero)
            {
                Diagnostics.Warn("SingleFileSaveDataV2 could not be found; the custom results file " +
                                 "cannot be read");
                return IntPtr.Zero;
            }

            IntPtr serializer = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass("MemoryPack.dll", "MemoryPack",
                                                                            "MemoryPackSerializer");
            if (serializer == IntPtr.Zero)
            {
                Diagnostics.Error("MemoryPack.MemoryPackSerializer: no class to ask in this build; the " +
                                  "custom results file cannot be read");
                return IntPtr.Zero;
            }

            IntPtr definition = MethodOn(serializer, "Deserialize", 3, genericOnly: true);
            if (definition == IntPtr.Zero)
            {
                Diagnostics.Warn("MemoryPackSerializer.Deserialize could not be found; the custom " +
                                 "results file cannot be read");
                return IntPtr.Zero;
            }

            IntPtr method = MethodResolver.InflatedMethod(serializer, definition, "MemoryPackSerializer",
                                                          "Deserialize", saveClass);
            if (method == IntPtr.Zero)
            {
                Diagnostics.Warn("MemoryPackSerializer.Deserialize could not be instantiated for the " +
                                 "save type; the custom results file cannot be read");
                return IntPtr.Zero;
            }

            IntPtr value = IntPtr.Zero;
            fixed (byte* data = bytes)
            {
                byte* span = stackalloc byte[Offsets.Runtime.SpanSize];
                *(IntPtr*)span = (IntPtr)data;
                *(int*)(span + Offsets.Runtime.SpanLength) = bytes.Length;

                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)span;
                args[1] = (IntPtr)(&value);
                args[2] = SerializerOptions();

                IntPtr raised = IntPtr.Zero;
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, IntPtr.Zero, (void**)args, ref raised);
                if (raised != IntPtr.Zero)
                {
                    Diagnostics.Warn($"reading {FileName} raised: {Raised.Text(raised)}");
                    return IntPtr.Zero;
                }
            }

            if (value == IntPtr.Zero) Diagnostics.Warn($"reading {FileName} answered with nothing");
            return value;
        }

        /// <summary>`_NH._dEb` — the serializer options.</summary>
        private static IntPtr SerializerOptions()
        {
            IntPtr klass = FieldResolver.ClassPointer("_NH");
            if (klass == IntPtr.Zero) return IntPtr.Zero;

            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return IntPtr.Zero;

            int at = FieldResolver.Field("_NH", "_dEb");
            return at < 0 ? IntPtr.Zero : Memory.Ptr(statics + at);
        }

        /// <summary>`SingleFileSaveDataV2.CreateDefault()` — the game's own empty save.</summary>
        private static IntPtr CreateDefault()
        {
            IntPtr klass = FieldResolver.ClassPointer("SingleFileSaveDataV2");
            if (klass == IntPtr.Zero)
            {
                Diagnostics.Warn("SingleFileSaveDataV2 could not be found, so an empty save cannot be made");
                return IntPtr.Zero;
            }

            IntPtr method = MethodOn(klass, "CreateDefault", 0);
            if (method == IntPtr.Zero)
            {
                Diagnostics.Warn("SingleFileSaveDataV2.CreateDefault could not be found");
                return IntPtr.Zero;
            }

            IntPtr raised = IntPtr.Zero;
            IntPtr obj = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, IntPtr.Zero, null, ref raised);
            if (raised != IntPtr.Zero)
            {
                Diagnostics.Warn($"making an empty save raised: {Raised.Text(raised)}");
                return IntPtr.Zero;
            }
            return obj;
        }

        /// <summary>`GameResultsV4.Create()` — an empty result table, for a save that has none.</summary>
        private static IntPtr NewResults()
        {
            IntPtr klass = FieldResolver.ClassPointer("GameResultsV4");
            if (klass == IntPtr.Zero)
            {
                Diagnostics.Warn("GameResultsV4 could not be found, so an empty table cannot be made");
                return IntPtr.Zero;
            }

            IntPtr method = MethodOn(klass, "Create", 0);
            if (method == IntPtr.Zero)
            {
                Diagnostics.Warn("GameResultsV4.Create could not be found");
                return IntPtr.Zero;
            }

            IntPtr raised = IntPtr.Zero;
            IntPtr table = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, IntPtr.Zero, null, ref raised);
            if (raised != IntPtr.Zero)
            {
                Diagnostics.Warn($"making an empty result table raised: {Raised.Text(raised)}");
                return IntPtr.Zero;
            }
            return table;
        }

        /// <summary>The container built with this file's paths.</summary>
        private static IntPtr NewContainer(IntPtr klass, IntPtr saveObject, string path, string tempPath,
                                           byte[] known)
        {
            IntPtr ctor = MethodOn(klass, ".ctor", 7);
            if (ctor == IntPtr.Zero)
            {
                Diagnostics.Warn("the save container's constructor could not be found");
                return IntPtr.Zero;
            }

            IntPtr instance = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(klass);
            if (instance == IntPtr.Zero)
            {
                Diagnostics.Warn("the save container could not be allocated");
                return IntPtr.Zero;
            }

            IntPtr* args = stackalloc IntPtr[7];
            args[0] = saveObject;
            args[1] = Str(ShortName);
            args[2] = Str(ShortTempName);
            args[3] = Str(path);
            args[4] = Str(tempPath);
            args[5] = IntPtr.Zero;
            args[6] = IntPtr.Zero;

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(ctor, instance, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
            {
                Diagnostics.Warn($"building the save container raised: {Raised.Text(raised)}");
                return IntPtr.Zero;
            }

            SeedWrittenCache(instance, known);
            return instance;
        }

        /// <summary>Seeds the container's write cache.</summary>
        private static void SeedWrittenCache(IntPtr container, byte[] known)
        {
            if (container == IntPtr.Zero) return;

            try
            {
                int length = known == null ? 0 : known.Length;

                IntPtr bytes = ByteArray(length);
                if (bytes == IntPtr.Zero)
                {
                    Diagnostics.Warn("the container's write cache could not be seeded; the first save " +
                                     "will take the game's retry path and raise an exception the file " +
                                     "will have to be judged against");
                    return;
                }

                if (length > 0) Marshal.Copy(known, 0, bytes + Offsets.Runtime.ArrayDataOffset, length);

                Memory.WritePtr(container + FieldWrittenBytes, bytes);
                Memory.WriteI32(container + FieldWrittenLength, length);
                Diagnostics.Info($"the container's write cache starts with {length} byte(s)");
            }
            catch (Exception e)
            {
                Diagnostics.Warn("the container's write cache could not be seeded: " + Diagnostics.Describe(e));
            }
        }

        /// <summary>A <c>byte[]</c> of this game's heap.</summary>
        private static IntPtr ByteArray(int count)
        {
            IntPtr arrayClass = BorrowedByteArrayClass();
            if (arrayClass == IntPtr.Zero)
            {
                IntPtr byteClass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass("mscorlib.dll", "System", "Byte");
                if (byteClass == IntPtr.Zero) return IntPtr.Zero;

                arrayClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_class_get(byteClass, 1);
            }
            if (arrayClass == IntPtr.Zero) return IntPtr.Zero;

            return Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(arrayClass, (ulong)count);
        }

        /// <summary>The class of the game container's write-cache array.</summary>
        private static IntPtr BorrowedByteArrayClass()
        {
            try
            {
                if (FieldWrittenBytes <= 0) return IntPtr.Zero;

                IntPtr save = PlayerSave();
                IntPtr official = save == IntPtr.Zero ? IntPtr.Zero : Memory.Ptr(save + SaveContainer);
                if (!Memory.LooksLikeObject(official)) return IntPtr.Zero;

                IntPtr sample = Memory.Ptr(official + FieldWrittenBytes);
                if (!Memory.LooksLikeObject(sample)) return IntPtr.Zero;

                return Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(sample);
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }

        // ---------------------------------------------------------------- resolution

        private static bool _fieldsMissing;

        /// <summary>Resolves the four field offsets.</summary>
        private static bool Resolve(IntPtr containerClass)
        {
            if (_fieldsMissing) return false;
            if (_resolved) return true;
            _resolved = true;

            FieldFullPath = FieldOn(containerClass, "_JEb");
            FieldWrittenBytes = FieldOn(containerClass, "_LEb");
            FieldWrittenLength = FieldOn(containerClass, "_mEb");
            FieldGameResults = FieldOn(FieldResolver.ClassPointer("SingleFileSaveDataV2"), "GameResults");

            if (FieldFullPath < 0 || FieldWrittenBytes < 0 || FieldWrittenLength < 0 ||
                FieldGameResults < 0)
            {
                _fieldsMissing = true;
                return false;
            }

            return true;
        }

        /// <summary>A field's offset on one class.</summary>
        private static int FieldOn(IntPtr klass, string name)
        {
            if (klass == IntPtr.Zero) return -1;

            try
            {
                IntPtr field = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, name);
                if (field == IntPtr.Zero)
                {
                    Diagnostics.Error($"{name}: no usable field by that name in this build");
                    return -1;
                }

                int offset = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field);
                if (offset <= 0)
                {
                    Diagnostics.Error($"{name}: no usable field by that name in this build");
                    return -1;
                }

                return offset;
            }
            catch (Exception)
            {
                Diagnostics.Error($"{name}: no usable field by that name in this build");
                return -1;
            }
        }

        /// <summary>A method by name and parameter count.</summary>
        internal static IntPtr MethodOn(IntPtr klass, string name, int parameters, bool genericOnly = false)
        {
            if (klass == IntPtr.Zero) return IntPtr.Zero;

            IntPtr iter = IntPtr.Zero;
            IntPtr method;
            while ((method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
            {
                if (Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name_(method) != name) continue;
                if ((int)Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(method) != parameters) continue;
                if (genericOnly && !Il2CppInterop.Runtime.IL2CPP.il2cpp_method_is_generic(method)) continue;
                return method;
            }
            return IntPtr.Zero;
        }

        private static IntPtr Str(string text) =>
            Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(text);
    }
}
