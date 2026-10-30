using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`PackVisualMemberLarge._Mf`, the cards of the pack on screen.</summary>
        private static int CardsField = Offsets.Unresolved;

        /// <summary>`PackSongCardMember._if`, the song view model the card was built from.</summary>
        private static int CardViewModel = Offsets.Unresolved;

        /// <summary>`PackVisualMemberLarge.dataAccess`.</summary>
        private static int PackDataAccess = Offsets.Unresolved;

        /// <summary>`_SH._WEb`, the song record — embedded, so this is its address and not a pointer.</summary>
        private static int ViewModelSong = Offsets.Unresolved;

        /// <summary>`SongSelectPackAssets.PackSelectLargeNonCompleted` — the large-card material slot.</summary>
        private static int RowLargeCard = Offsets.Unresolved;

        /// <summary>`SongSelectPackAssets.PackSelectNonCompleted` — the small-card material slot.</summary>
        private static int RowSmallCard = Offsets.Unresolved;

        /// <summary>`SongSelectPackAssets.BackingImageNonCompleted` — the backing material slot.</summary>
        private static int RowBacking = Offsets.Unresolved;

        /// <summary>`SongSelectPackAssets.BackingImageCompleted` — the completed backing slot.</summary>
        private static int RowBackingDone = Offsets.Unresolved;

        private static bool _fieldsResolved;

        private static bool _fieldsMissing;

        /// <summary>Resolves the card and row fields.</summary>
        private static bool ResolveFields()
        {
            if (_fieldsResolved) return true;
            if (_fieldsMissing) return false;

            PackDataAccess = FieldResolver.Field("PackVisualMemberLarge", "dataAccess");
            ViewModelSong = FieldResolver.Field("_SH", "_WEb");
            CardsField = FieldResolver.Field("PackVisualMemberLarge", "_Mf");
            CardViewModel = FieldResolver.Field("PackSongCardMember", "_if");
            ViewModelDifficulty = FieldResolver.Field("_SH", "_xEb");
            RowLargeCard = FieldResolver.Field("SongSelectPackAssets", "PackSelectLargeNonCompleted");
            RowSmallCard = FieldResolver.Field("SongSelectPackAssets", "PackSelectNonCompleted");
            RowBacking = FieldResolver.Field("SongSelectPackAssets", "BackingImageNonCompleted");
            RowBackingDone = FieldResolver.Field("SongSelectPackAssets", "BackingImageCompleted");

            if (PackDataAccess < 0 || ViewModelSong < 0 || CardsField < 0 ||
                CardViewModel < 0 || ViewModelDifficulty < 0 ||
                RowLargeCard < 0 || RowSmallCard < 0 || RowBacking < 0 || RowBackingDone < 0)
            {
                _fieldsMissing = true;
                Diagnostics.Error("the pack's card fields could not be read by name in this build; the " +
                                  "cards will keep the difficulty they were first built with");
                return false;
            }

            _fieldsResolved = true;
            return true;
        }

        /// <summary>`_SH._xEb`, the difficulty the card is showing.</summary>
        private static int ViewModelDifficulty = Offsets.Unresolved;

        /// <summary>The `_vc` delegate.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PackDifficultyFn(IntPtr self, byte difficulty, IntPtr methodInfo);

        private static NativeHook<PackDifficultyFn> _packDifficulty;
        private static PackDifficultyFn _packDifficultyTramp;

        /// <summary>`PackSongCardMember._pc`'s code pointer, or null when it could not be reached.</summary>
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void> _buildCard;

        /// <summary>`_pc`'s `MethodInfo`.</summary>
        private static IntPtr _buildCardInfo;

        /// <summary>Difficulty applies seen, cards rebuilt for one, and pack screens rebuilt after a reload.</summary>
        internal static long PackDifficultyCalls, PackCardsRebuilt, PackVisualRebuilds;

        /// <summary>The shared accessor for `SongSelectPackAssets` rows.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PackRowFn(IntPtr table, IntPtr destination, uint index);

        private static NativeHook<PackRowFn> _packRow;
        private static PackRowFn _packRowTramp;

        /// <summary>The row this mod's pack is answered with; -1 until it has been resolved.</summary>
        private static int _rowSource = -1;

        private static bool InstallPackRow()
        {
            int candidates = 0;
            IntPtr target = RowAccessor(ref candidates);
            if (target == IntPtr.Zero)
            {
                if (candidates < 0)
                {
                    Diagnostics.Error("the game's pack row getter could not be found by name in this " +
                                      "build; the custom pack will wear whatever this build puts in its row");
                    return false;
                }

                Diagnostics.Info($"the pack row accessor: the getter's most-called function is called " +
                                 $"{candidates} time(s); a row is {RowSize()} byte(s)");
                Diagnostics.Error("the pack row accessor could not be located in this build; the custom " +
                                  "pack will wear whatever this build puts in its row");
                return false;
            }

            byte[] prologue = Prologue(target);
            _packRow = new NativeHook<PackRowFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, void>)&PackRowDetour,
            };
            _packRow.Attach();
            _packRowTramp = _packRow.Trampoline;
            Diagnostics.Info($"the pack row accessor is at 0x{target.ToInt64():X}");
            return Landed(Hook.PackRow, target, prologue);
        }

        /// <summary>Answers this mod's pack with the In Falsus row.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PackRowDetour(IntPtr table, IntPtr destination, uint index)
        {
#if DEBUG
            RowCalls++;
            long t0 = Now;
#endif
            Diagnostics.Info($"probe: pack row index={index} " +
                             $"count={(Memory.LooksLikeObject(table) ? Memory.I32(table + Offsets.Runtime.ArrayLength) : -1)} " +
                             $"table=0x{table.ToInt64():X}");
            if (SongCatalog.CustomPackId >= 0 && index == (uint)SongCatalog.CustomPackId)
            {
                if (_rowSource < 0) _rowSource = ResolveSourceRow();
                if (_rowSource >= 0)
                {
                    index = (uint)_rowSource;
#if DEBUG
                    RowRedirects++;
#endif
                }
            }
#if DEBUG
            TicksRow += Now - t0;
#endif

            _packRowTramp(table, destination, index);
        }

        /// <summary>The row this mod's pack is answered with.</summary>
        private static int ResolveSourceRow()
        {
            if (!PackSetup.TryGetVisuals(out IntPtr canonical) || !Memory.LooksLikeObject(canonical))
                return -1;

            int offset = PackSetup.VisualsTableOffset;
            IntPtr table = offset > 0 ? Memory.Ptr(canonical + offset) : IntPtr.Zero;
            int count = Memory.LooksLikeObject(table) ? Memory.I32(table + Offsets.Runtime.ArrayLength) : 0;
            int stride = Stride(table, count);
            int row = stride <= 0 ? -1 : SourceRow(table, count, stride);
            if (row < 0) PackSetup.NoRow();
            return row;
        }

        /// <summary>The helper the game's row getter calls.</summary>
        private static IntPtr RowAccessor(ref int candidates)
        {
            IntPtr getter = MethodResolver.ByName("PackSelectSceneAssets", "_WnA");
            if (getter == IntPtr.Zero)
            {
                candidates = -1;
                return IntPtr.Zero;
            }

            byte* code = (byte*)getter;
            IntPtr best = IntPtr.Zero;
            int bestCount = 0;
            bool tied = false;

            for (int at = 0; at + CallLength <= CallWindow; at++)
            {
                if (code[at] != 0xE8) continue;

                IntPtr target = Target(code + at);
                if (!Executable(target)) continue;

                int count = 1;
                for (int other = at + CallLength; other + CallLength <= CallWindow; other++)
                    if (code[other] == 0xE8 && Target(code + other) == target)
                        count++;

                if (count > bestCount) { bestCount = count; best = target; tied = false; }
                else if (count == bestCount) tied = true;
            }

            candidates = bestCount;
            return bestCount > 1 && !tied ? best : IntPtr.Zero;
        }

        /// <summary>`call rel32` — one opcode byte and four of displacement.</summary>
        private const int CallLength = 5;

        /// <summary>How far into the getter calls are looked for.</summary>
        private const int CallWindow = 96;

        /// <summary>Where an <c>E8</c> calls.</summary>
        private static IntPtr Target(byte* call)
        {
            return (IntPtr)((long)(call + CallLength) + *(int*)(call + 1));
        }

        /// <summary>Whether an address is in an executable section.</summary>
        private static bool Executable(IntPtr address)
        {
            byte* image = (byte*)GameAssembly.Base;
            if (image == null || *(ushort*)image != 0x5A4D) return false;        // "MZ"

            byte* pe = image + *(int*)(image + 0x3C);
            if (*(uint*)pe != 0x00004550) return false;                         // "PE\0\0"

            int sections = *(ushort*)(pe + 6);
            byte* section = pe + 24 + *(ushort*)(pe + 20);
            long at = address.ToInt64();

            for (int s = 0; s < sections; s++, section += 40)
            {
                if ((*(uint*)(section + 36) & 0x20000000) == 0) continue;

                uint size = *(uint*)(section + 8);
                uint start = *(uint*)(section + 12);
                if (size == 0 || start == 0) continue;

                long from = (long)(image + start);
                if (at >= from && at < from + size) return true;
            }

            return false;
        }

        /// <summary>The size of one `SongSelectPackAssets` row.</summary>
        private static int RowSize()
        {
            if (_rowSize >= 0) return _rowSize;

            int size = FieldResolver.ValueSize("SongSelectPackAssets");
            if (size <= 0) return 0;

            return _rowSize = size;
        }

        /// <summary>`RowSize`'s answer, -1 until it has been asked.</summary>
        private static int _rowSize = -1;

        /// <summary>The first shipped row after the head with a pack cover.</summary>
        internal static int SourceRow(IntPtr table, int count, int stride)
        {
            if (stride <= 0 || count <= 0) return -1;

            for (int i = 1; i < count; i++)
                if (SongCatalog.IsOfficialPack((ushort)i) && CoverDrawable(table, count, stride, i)) return i;

            return -1;
        }

        /// <summary>The first shipped pack row with its own large card and a backing picture.</summary>
        internal static int PackVisualRow(IntPtr table, int count, int stride)
        {
            if (!ResolveFields()) return -1;
            if (stride <= 0 || count <= 0) return -1;

            IntPtr head = Row(table, count, stride, 0);
            IntPtr shared = head == IntPtr.Zero ? IntPtr.Zero : Memory.Ptr(head + RowLargeCard);

            for (int i = 1; i < count; i++)
            {
                if (!SongCatalog.IsOfficialPack((ushort)i)) continue;

                IntPtr row = Row(table, count, stride, i);
                if (row == IntPtr.Zero) continue;

                IntPtr large = Memory.Ptr(row + RowLargeCard);
                IntPtr backing = Memory.Ptr(row + RowBacking);
                if (!Memory.LooksLikeObject(large) || !Memory.LooksLikeObject(backing)) continue;
                if (large == shared) continue;

                return i;
            }

            return -1;
        }

        /// <summary>The first shipped row after the head with a backing picture.</summary>
        internal static int BackingRow(IntPtr table, int count, int stride)
        {
            if (stride <= 0 || count <= 0) return -1;

            for (int i = 1; i < count; i++)
            {
                if (!SongCatalog.IsOfficialPack((ushort)i)) continue;

                IntPtr row = Row(table, count, stride, i);
                if (row != IntPtr.Zero && Memory.LooksLikeObject(Memory.Ptr(row + RowBacking))) return i;
            }

            return -1;
        }

        /// <summary>A row's size in bytes.</summary>
        private static int Stride(IntPtr table, int count)
        {
            if (!Memory.LooksLikeObject(table) || count <= 0) return 0;

            try
            {
                return (int)(Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(table) / count);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>Whether a row has the materials a pack cover draws.</summary>
        private static bool CoverDrawable(IntPtr table, int count, int stride, int id)
        {
            if (!ResolveFields()) return false;

            IntPtr row = Row(table, count, stride, id);
            if (row == IntPtr.Zero) return false;

            return Memory.LooksLikeObject(Memory.Ptr(row + RowLargeCard)) ||
                   Memory.LooksLikeObject(Memory.Ptr(row + RowSmallCard));
        }

        /// <summary>The row an id selects — `packToAssets[id]`, and row zero for an id past the end.</summary>
        private static IntPtr Row(IntPtr table, int count, int stride, int id)
        {
            if (stride <= 0 || count <= 0) return IntPtr.Zero;
            if (id < 0 || id >= count) id = 0;

            return table + Offsets.Runtime.ArrayDataOffset + id * stride;
        }

        private static bool InstallPackVisual()
        {
            bool row = InstallPackRow();

            IntPtr info = MethodResolver.MethodInfoByRuntime("PackSongCardMember", "_pc");
            if (info == IntPtr.Zero)
            {
                Diagnostics.Warn("PackSongCardMember._pc could not be found; a difficulty change on the " +
                                 "pack screen will not reach the jackets until the screen is reloaded");
            }
            else
            {
                _buildCardInfo = info;
                _buildCard = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)(*(IntPtr*)info);
            }

            IntPtr target = MethodResolver.ByRuntime("PackVisualMemberLarge", "_vc");
            if (target == IntPtr.Zero)
            {
                Diagnostics.Warn("PackVisualMemberLarge._vc could not be found; the jackets on the pack " +
                                 "screen will keep the difficulty they were first built with");
                return false;
            }

            byte[] prologue = Prologue(target);
            _packDifficulty = new NativeHook<PackDifficultyFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, byte, IntPtr, void>)&PackDifficultyDetour,
            };
            _packDifficulty.Attach();
            _packDifficultyTramp = _packDifficulty.Trampoline;
            Diagnostics.Info("PackVisualMemberLarge._vc hooked");
            return Landed(Hook.PackVisual, target, prologue) & row;
        }

        private static void DetachPackVisual()
        {
            _packRow?.Detach();
            _packRow = null;
            _packRowTramp = null;

            _packDifficulty?.Detach();
            _packDifficulty = null;
            _packDifficultyTramp = null;
            _buildCard = null;
            _buildCardInfo = IntPtr.Zero;
        }

        /// <summary>Records the difficulty and rebuilds cards that showed another.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PackDifficultyDetour(IntPtr self, byte difficulty, IntPtr methodInfo)
        {
            PackDifficultyCalls++;

            bool rebuild = false;
            if (!Faulted)
            {
#if DEBUG
                long t0 = Now;
#endif
                try
                {
                    Selection.ApplyingDifficulty(difficulty);
                    rebuild = CardsForAnotherDifficulty(self, difficulty);
                }
                catch (Exception e)
                {
                    Fault(Hook.PackVisual, e);
                }
#if DEBUG
                TicksVc += Now - t0;
#endif
            }

            _packDifficultyTramp(self, difficulty, methodInfo);

            if (rebuild)
            {
#if DEBUG
                long t1 = Now;
#endif
                RebuildCards(self);
#if DEBUG
                TicksVc += Now - t1;
#endif
            }
        }

        /// <summary>Whether a card on screen showed another difficulty.</summary>
        private static bool CardsForAnotherDifficulty(IntPtr packVisual, byte difficulty)
        {
            if (!ResolveFields()) return false;

            if (!Memory.TryList(Memory.Ptr(packVisual + CardsField), out IntPtr items, out int count))
                return false;

            for (int i = 0; i < count; i++)
            {
                if (!Ours(items, i, out IntPtr _, out IntPtr viewModel)) continue;
                if (Memory.U8(viewModel + ViewModelDifficulty) != difficulty) return true;
            }

            return false;
        }

        /// <summary>Rebuilds each of this mod's cards.</summary>
        private static void RebuildCards(IntPtr packVisual)
        {
            if (_buildCard == null) return;

            if (!ResolveFields()) return;

            IntPtr dataAccess = Memory.Ptr(packVisual + PackDataAccess);
            if (!Memory.LooksLikeObject(dataAccess)) return;

            if (!Memory.TryList(Memory.Ptr(packVisual + CardsField), out IntPtr items, out int count))
                return;

            for (int i = 0; i < count; i++)
            {
                if (!Ours(items, i, out IntPtr card, out IntPtr viewModel)) continue;

                try
                {
                    _buildCard(card, dataAccess, viewModel, _buildCardInfo);
                    PackCardsRebuilt++;
                }
                catch (Exception e)
                {
                    Reported("rebuilding a card on the pack screen raised: " + Diagnostics.Describe(e));
                }
            }
        }

        /// <summary>One card of the list, when it is one of this mod's songs'.</summary>
        private static bool Ours(IntPtr items, int index, out IntPtr card, out IntPtr viewModel)
        {
            card = IntPtr.Zero;
            viewModel = IntPtr.Zero;

            if (!ResolveFields()) return false;

            card = Memory.Ptr(items + index * IntPtr.Size);
            if (!Memory.LooksLikeObject(card)) return false;

            viewModel = Memory.Ptr(card + CardViewModel);
            if (!Memory.LooksLikeObject(viewModel)) return false;

            return JacketCatalog.IsOurs(viewModel + ViewModelSong);
        }

        /// <summary>Says why the rebuild failed, once.</summary>
        [System.Diagnostics.Conditional("DEBUG")]
        private static void Reported(string why)
        {
            if (_rebuildReported) return;
            _rebuildReported = true;
            Diagnostics.Warn(why);
        }

        private static bool _rebuildReported;

        private static int _selectedField = Offsets.Unresolved;
        private static int _backingField = Offsets.Unresolved;
        private static int _calculatedField = Offsets.Unresolved;
        private static int _materialField = Offsets.Unresolved;
        private static bool _maskFieldsAsked;

        private static int _outlineListField = Offsets.Unresolved;
        private static int _outlineVisualField = Offsets.Unresolved;
        private static int _topScrollField = Offsets.Unresolved;
        private static int _bottomScrollField = Offsets.Unresolved;
        private static int _cardListField = Offsets.Unresolved;
        private static int _cardVisualField = Offsets.Unresolved;
        private static bool _listFieldsAsked;
        private static bool _listFieldsReady;

        private static int _buttonField = Offsets.Unresolved;
        private static readonly int[] ButtonFields = new int[7];
        private static bool _buttonFieldsAsked;
        private static bool _buttonFieldsReady;

        private static bool _maskFailureReported;

        /// <summary>The pack visual and the pack scene the screen is using.</summary>
        internal static IntPtr PackVisual, PackScene;

        /// <summary>Turns on the alpha mask of the pack screen's live materials.</summary>
        internal static void ApplyPackMask(IntPtr self)
        {
            if (!EnsureMaskFields()) return;

            ApplyMaskTo(self, _selectedField);
            ApplyMaskTo(self, _backingField);
        }

        private static bool EnsureMaskFields()
        {
            if (!_maskFieldsAsked)
            {
                _maskFieldsAsked = true;
                _selectedField = FieldResolver.Field("PackVisualMemberLarge", "selectedVisual");
                _backingField = FieldResolver.Field("PackVisualMemberLarge", "backingVisual");
                _calculatedField = FieldResolver.Field("Constrained2D", "Calculated");
                _materialField = FieldResolver.Field("_ti", "_OHb");
            }

            return _selectedField >= 0 && _backingField >= 0 &&
                   _calculatedField >= 0 && _materialField >= 0;
        }

        private static bool EnsureListFields()
        {
            if (!_listFieldsAsked)
            {
                _listFieldsAsked = true;
                _outlineListField = FieldResolver.Field("PackSelectScene", "_ff");
                _outlineVisualField = FieldResolver.Field("PackOutlineMember", "outlineVisual");
                _topScrollField = FieldResolver.Field("PackSelectScene", "topScrollContainer");
                _bottomScrollField = FieldResolver.Field("PackSelectScene", "bottomScrollContainer");
                _cardListField = FieldResolver.Field("PackSelectScrollContainer", "_jr");
                _cardVisualField = FieldResolver.Field("PackVisualMemberSmall", "nonSelectedVisual");
                _listFieldsReady = _outlineListField >= 0 && _outlineVisualField >= 0 &&
                                   _topScrollField >= 0 && _bottomScrollField >= 0 &&
                                   _cardListField >= 0 && _cardVisualField >= 0;
            }

            return _listFieldsReady;
        }

        private static bool EnsureButtonFields()
        {
            if (!_buttonFieldsAsked)
            {
                _buttonFieldsAsked = true;
                _buttonField = FieldResolver.Field("SongSelectScene", "packSelectButtonContainer");
                string[] names =
                {
                    "clearTypeBacking",
                    "currentNonHoverButton",
                    "currentHoverButton",
                    "targetNonHoverButton",
                    "targetHoverButton",
                    "currentContainer",
                    "targetContainer",
                };

                bool ready = _buttonField >= 0;
                for (int i = 0; i < names.Length; i++)
                {
                    ButtonFields[i] = FieldResolver.Field("PackSelectButtonContainer", names[i]);
                    ready &= ButtonFields[i] >= 0;
                }

                _buttonFieldsReady = ready;
            }

            return _buttonFieldsReady;
        }

        /// <summary>Keeps the live pack material's alpha mask on after the game's material pass.</summary>
        internal static void TickPackMask()
        {
            if (PackVisual != IntPtr.Zero)
                try { ApplyPackMask(PackVisual); }
                catch (Exception e) { MaskFailed("keeping the pack's alpha mask on raised: " + Diagnostics.Describe(e)); }

            if (PackScene != IntPtr.Zero)
                try { ApplyPackMasks(PackScene); }
                catch (Exception e) { MaskFailed("keeping the pack list's alpha mask on raised: " + Diagnostics.Describe(e)); }

            IntPtr scene = SongSelectRefresh.Scene();
            if (scene != IntPtr.Zero)
                try { ApplyPackButtonMask(scene); }
                catch (Exception e) { MaskFailed("keeping the pack button's alpha mask on raised: " + Diagnostics.Describe(e)); }
        }

        private static void MaskFailed(string why)
        {
            if (_maskFailureReported) return;
            _maskFailureReported = true;
            Diagnostics.Error(why);
        }

        private static void ApplyMaskTo(IntPtr self, int field)
        {
            IntPtr constrained = Memory.Ptr(self + field);
            if (!Memory.LooksLikeObject(constrained)) return;

            IntPtr calculated = constrained + _calculatedField;
            IntPtr material = Memory.Ptr(calculated + _materialField);
            if (!Memory.LooksLikeObject(material)) return;
            if (!JacketFactory.LooksLikeMaterial(material)) return;

            if (!JacketFactory.EnableAlphaMask(material))
                MaskFailed($"the pack material at 0x{material.ToInt64():X} could not have its alpha mask turned on");
        }

        /// <summary>Turns on the alpha mask for every pack list element the row feeds.</summary>
        internal static void ApplyPackMasks(IntPtr layer)
        {
            if (!Memory.LooksLikeObject(layer)) return;
            if (!EnsureMaskFields() || !EnsureListFields()) return;

            IntPtr outlines = Memory.Ptr(layer + _outlineListField);
            if (Memory.TryList(outlines, out IntPtr outlineItems, out int outlineCount))
                for (int i = 0; i < outlineCount; i++)
                    ApplyMaskTo(Memory.Ptr(outlineItems + i * IntPtr.Size), _outlineVisualField);

            ApplyCardMasks(layer, _topScrollField);
            ApplyCardMasks(layer, _bottomScrollField);
        }

        private static void ApplyCardMasks(IntPtr layer, int containerField)
        {
            IntPtr container = Memory.Ptr(layer + containerField);
            if (!Memory.LooksLikeObject(container)) return;

            IntPtr list = Memory.Ptr(container + _cardListField);
            if (!Memory.TryList(list, out IntPtr items, out int count)) return;

            for (int i = 0; i < count; i++)
                ApplyMaskTo(Memory.Ptr(items + i * IntPtr.Size), _cardVisualField);
        }

        /// <summary>Turns on the alpha mask for the pack button's row-fed visuals.</summary>
        internal static void ApplyPackButtonMask(IntPtr scene)
        {
            if (!Memory.LooksLikeObject(scene)) return;
            if (!EnsureMaskFields() || !EnsureButtonFields()) return;

            IntPtr button = Memory.Ptr(scene + _buttonField);
            if (!Memory.LooksLikeObject(button)) return;

            foreach (int field in ButtonFields)
                ApplyMaskTo(button, field);
        }
    }
}
