using System;
using System.Collections.Generic;

namespace InFalsusChartLoader
{
    /// <summary>Asks the song-select scene to redraw after the tables changed.</summary>
    internal static unsafe class SongSelectRefresh
    {
        private const string SceneType = "SongSelectScene";

        private static IntPtr _sceneClass;
        private static int _selectedField = Offsets.Unresolved;
        private static int _packLayerField = Offsets.Unresolved;
        private static int _ihPackField = Offsets.Unresolved;
        private static int _songsListField = Offsets.Unresolved;
        private static int _viewListField = Offsets.Unresolved;
        private static int _leftCardField = Offsets.Unresolved;
        private static int _rightCardField = Offsets.Unresolved;
        private static int _cardListField = Offsets.Unresolved;
        private static int _cardIndexField = Offsets.Unresolved;
        private static int _selectedIndexField = Offsets.Unresolved;
        private static IntPtr _gcInfo;
        private static IntPtr _aoAInfo;
        private static IntPtr _selectPackInfo;
        private static IntPtr _yoInfo;
        private static IntPtr _ihClass;
        private static IntPtr _ihCtor;
        private static IntPtr _yoAllInfo;
        private static IntPtr _vnInfo;
        private static IntPtr _aoInfo;
        private static IntPtr _onInfo;
        private static IntPtr _snInfo;
        private static int _sortField = Offsets.Unresolved;
        private static int _dataAccessField = Offsets.Unresolved;
        private static int _packVisualField = Offsets.Unresolved;
        private static int _packListField = Offsets.Unresolved;
        private static int _packIndexField = Offsets.Unresolved;
        private static IntPtr _packRebuildInfo;

        private static int _packSceneAccessField = Offsets.Unresolved;
        private static int _packDifficultyField = Offsets.Unresolved;
        private static int _ihSongArrayField = Offsets.Unresolved;
        private static int _shSongField = Offsets.Unresolved;
        private static int _packOutlineListField = Offsets.Unresolved;
        private static int _outlinePackField = Offsets.Unresolved;
        private static int _topPackContainerField = Offsets.Unresolved;
        private static int _bottomPackContainerField = Offsets.Unresolved;
        private static int _packCardListField = Offsets.Unresolved;
        private static int _cardPackField = Offsets.Unresolved;
        private static IntPtr _localizationInfo;
        private static IntPtr _outlineUpdateInfo;
        private static IntPtr _packCardUpdateInfo;
        private static bool _switchReady;
        private static bool _switchMissing;
        private static bool _outlineReady;
        private static bool _outlineMissing;
        private static bool _cardsReady;
        private static bool _cardsMissing;

        private static int _packButtonField = Offsets.Unresolved;
        private static int _packInfoField = Offsets.Unresolved;
        private static IntPtr _packTitleInfo;
        private static bool _titleReady;
        private static bool _titleMissing;
        private static bool _titleFellBack;
        private static string _screenSignature;

        /// <summary>The largest PackInfo a pack switch will copy.</summary>
        private const int PackInfoLimit = 256;

        private static bool _resolved;
        private static bool _missing;

        /// <summary>Rebuilds the song list and keeps the current selection, through the scene's own apply.</summary>
        internal static void Structure()
        {
            IntPtr scene = Scene();
            if (scene == IntPtr.Zero) return;

            if (!Resolve()) return;

            if (!CurrentPackId(out ushort packId))
            {
                Diagnostics.Error("the current pack could not be read; the song list was not rebuilt");
                return;
            }

            if (packId != ushort.MaxValue && !SongCatalog.LivePackExists(packId))
            {
                Diagnostics.Info($"probe: refresh pack id={packId} is gone; showing all songs");
                packId = ushort.MaxValue;
                _titleFellBack = DeselectPack();
            }

            IntPtr fresh = IntPtr.Zero;
            IntPtr layer = IntPtr.Zero;
            int slot = -1;
            bool packEmpty = false;
            Diagnostics.Info($"probe: refresh pack id={packId}");

            if (packId == ushort.MaxValue)
            {
                Diagnostics.Info("probe: refresh pack=all songs");
            }
            else
            {
                layer = Memory.Ptr(scene + _packLayerField);
                if (!Memory.LooksLikeObject(layer))
                {
                    Diagnostics.Error("the pack layer could not be read; the song list was not rebuilt");
                    return;
                }

                IntPtr pack = PackFor(layer, packId);
                if (!Memory.LooksLikeObject(pack))
                {
                    Diagnostics.Error("the current pack could not be read; the song list was not rebuilt");
                    return;
                }

                bool copied = SongCatalog.CopyPackTo(pack + _ihPackField, packId);
                Diagnostics.Info($"probe: refresh pack={packId} copied={copied} ids={IdsText(pack + _ihPackField)} " +
                                 $"all={SongCatalog.AllSongCount()} ffb={SongCatalog.SongListLength()} " +
                                 $"songs={ListCount(scene, _songsListField)} views={ListCount(scene, _viewListField)}");

                fresh = BuildPack(scene, pack + _ihPackField);
                if (fresh == IntPtr.Zero)
                {
                    Diagnostics.Error("the current pack could not be rebuilt; the song list was not refreshed");
                    return;
                }

                slot = ReplacePackView(layer, packId, fresh);

                IntPtr songs = Memory.Ptr(fresh + _ihPackField + Offsets.Pack.Songs);
                packEmpty = ArrayCount(songs) == 0;
                if (packEmpty)
                {
                    Diagnostics.Info($"probe: refresh pack={packId} empty; showing all songs");
                    _titleFellBack = DeselectPack();
                }
            }

            RefreshSongs(scene);
            ApplyInline(scene, packEmpty ? IntPtr.Zero : fresh);

            if (_titleFellBack)
            {
                _titleFellBack = false;
                ShowAllSongsTitle(scene);
            }

            bool rebuildPending = _screenSignature != null &&
                                  SongCatalog.PackScreenSignature() != _screenSignature;

            RefreshCards(scene);
            if (!rebuildPending) RefreshPackScreen(layer, fresh, slot);

            Diagnostics.Info($"probe: refresh after songs={ListCount(scene, _songsListField)} " +
                             $"views={ListCount(scene, _viewListField)} qr={SongIdsText(scene, _songsListField)}");
            LogCards(scene, _leftCardField, "left");
            LogCards(scene, _rightCardField, "right");

            if (rebuildPending)
                PackScreenRebuild.Rebuild(scene);
        }

        /// <summary>The ids a scene's SongInfo list holds, as text, or "?".</summary>
        private static string SongIdsText(IntPtr scene, int field)
        {
            if (field < 0) return "?";

            IntPtr list = Memory.Ptr(scene + field);
            if (!Memory.LooksLikeObject(list)) return "?";

            int count = Memory.I32(list + Offsets.Runtime.ListSize);
            IntPtr items = Memory.Ptr(list + Offsets.Runtime.ListItems);
            if (!Memory.LooksLikeObject(items)) return "?";

            var text = new System.Text.StringBuilder();
            int shown = count < 120 ? count : 120;
            for (int i = 0; i < shown; i++)
            {
                if (i > 0) text.Append(',');
                text.Append(Memory.U16(items + Offsets.Runtime.ArrayDataOffset +
                                       i * Offsets.Song.Size + Offsets.Song.Id));
            }
            if (shown < count) text.Append(",...");
            return text.ToString();
        }

        /// <summary>Hands the rebuilt view list to both card containers.</summary>
        private static void RefreshCards(IntPtr scene)
        {
            if (_yoInfo == IntPtr.Zero || _viewListField < 0 || _selectedIndexField < 0) return;

            IntPtr views = Memory.Ptr(scene + _viewListField);
            if (!Memory.LooksLikeObject(views)) return;

            int count = Memory.I32(views + Offsets.Runtime.ListSize);
            if (count <= 0) return;

            int index = Memory.I32(scene + _selectedIndexField);
            if (index >= count) index = count - 1;
            if (index < 0) index = 0;

            int at = index;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = views;
            args[1] = (IntPtr)(&at);

            CallCards(scene, _leftCardField, args);
            CallCards(scene, _rightCardField, args);
        }

        private static void CallCards(IntPtr scene, int field, IntPtr* args)
        {
            if (field < 0) return;

            IntPtr container = Memory.Ptr(scene + field);
            if (!Memory.LooksLikeObject(container)) return;

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_yoInfo, container, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
                Diagnostics.Error($"the song cards could not be refreshed: {Raised.Text(raised)}");
        }

        /// <summary>Puts the rebuilt pack view where the pack screen looks it up.</summary>
        private static int ReplacePackView(IntPtr layer, ushort packId, IntPtr fresh)
        {
            if (_packListField < 0 || _packIndexField < 0 || fresh == IntPtr.Zero) return -1;

            IntPtr list = Memory.Ptr(layer + _packListField);
            if (!Memory.TryList(list, out IntPtr items, out int count)) return -1;

            int at = Memory.I32(layer + _packIndexField);
            if (at >= 0 && at < count && IsPackSlot(items, at, packId))
            {
                Memory.WritePtr(items + at * IntPtr.Size, fresh);
                return at;
            }

            for (int i = 0; i < count; i++)
            {
                if (!IsPackSlot(items, i, packId)) continue;

                Memory.WritePtr(items + i * IntPtr.Size, fresh);
                return i;
            }

            return -1;
        }

        /// <summary>Whether one pack view is the pack with this id.</summary>
        private static bool IsPackSlot(IntPtr items, int index, ushort packId)
        {
            IntPtr view = Memory.Ptr(items + index * IntPtr.Size);
            return Memory.LooksLikeObject(view) && Memory.U16(view + _ihPackField) == packId;
        }

        /// <summary>Rebuilds the pack screen so its cards follow the reloaded files.</summary>
        private static void RefreshPackScreen(IntPtr layer, IntPtr fresh, int slot)
        {
            if (fresh == IntPtr.Zero || slot < 0 || layer == IntPtr.Zero) return;

            if (_packRebuildInfo == IntPtr.Zero || _packVisualField < 0)
            {
                Diagnostics.Error("the category screen's cards could not be rebuilt; it will keep the " +
                                  "pictures it was built with");
                return;
            }

            IntPtr visual = Memory.Ptr(layer + _packVisualField);
            if (!Memory.LooksLikeObject(visual)) return;

            int index = slot;
            int direction = 0;

            IntPtr* args = stackalloc IntPtr[4];
            args[0] = layer;
            args[1] = fresh;
            args[2] = (IntPtr)(&index);
            args[3] = (IntPtr)(&direction);

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_packRebuildInfo, visual, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
            {
                Diagnostics.Error("the category screen's cards could not be rebuilt: " + Raised.Text(raised));
                return;
            }

            Hooks.PackVisualRebuilds++;
        }

        /// <summary>
        /// Resolves what a pack switch needs, or says once that a switched pack will keep its old view.
        /// </summary>
        internal static bool PreparePackSwitch()
        {
            if (_switchReady) return true;
            if (_switchMissing) return false;
            if (!Resolve())
            {
                PackSwitchMissing();
                return false;
            }

            _packSceneAccessField = FieldResolver.Field("PackSelectScene", "dataAccess");
            _packDifficultyField = FieldResolver.Field("PackSelectScene", "_Gf");
            _ihSongArrayField = FieldResolver.Field("_iH", "_sDb");
            _shSongField = FieldResolver.Field("_SH", "_WEb");
            _localizationInfo = MethodResolver.MethodInfoByRuntimeInImage("Str.dll", "Str", "Strings",
                                                                           "GetCurrentLocalization");
            if (_packSceneAccessField < 0 || _packDifficultyField < 0 ||
                _ihSongArrayField < 0 || _shSongField < 0 ||
                _packListField < 0 || _packIndexField < 0 ||
                _packRebuildInfo == IntPtr.Zero ||
                _localizationInfo == IntPtr.Zero ||
                Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(_localizationInfo) != 0)
            {
                PackSwitchMissing();
                return false;
            }

            _switchReady = true;
            return true;
        }

        /// <summary>The game's whole-screen pack rebuild, as a MethodInfo.</summary>
        internal static IntPtr PackRebuildInfo => _packRebuildInfo;

        /// <summary>Says once that a switched pack could not be rebuilt.</summary>
        internal static void PackSwitchMissing()
        {
            if (_switchMissing) return;

            _switchMissing = true;
            Diagnostics.Error(PackSwitchFailed);
        }

        /// <summary>The one sentence a pack switch reports when it cannot rebuild the pack's view.</summary>
        internal const string PackSwitchFailed =
            "the pack switch's refresh is not in this build; a switched pack will keep the songs it was built with";

        /// <summary>
        /// A fresh view for the custom pack when the one about to be drawn was built from an older live row.
        /// </summary>
        internal static IntPtr PackViewFor(IntPtr layer, IntPtr info)
        {
            if (!PreparePackSwitch()) return IntPtr.Zero;
            if (!Memory.LooksLikeObject(layer) || !Memory.LooksLikeObject(info)) return IntPtr.Zero;

            ushort packId = Memory.U16(info + _ihPackField);
            if (!SongCatalog.IsCustomPack(packId)) return IntPtr.Zero;

            IntPtr liveSongs = SongCatalog.LivePackSongs(packId);
            if (liveSongs == IntPtr.Zero || PackViewCurrent(info, liveSongs)) return IntPtr.Zero;

            Diagnostics.Info($"probe: pack view stale songs={ArrayCount(Memory.Ptr(info + _ihSongArrayField))} " +
                             $"live={ArrayCount(liveSongs)}");

            int size = FieldResolver.ValueSize("PackInfo");
            if (size <= 0 || size > PackInfoLimit)
            {
                PackSwitchMissing();
                return IntPtr.Zero;
            }

            byte* live = stackalloc byte[size];
            if (!SongCatalog.CopyPackTo((IntPtr)live, packId))
            {
                PackSwitchMissing();
                return IntPtr.Zero;
            }

            IntPtr dataAccess = Memory.Ptr(layer + _packSceneAccessField);
            IntPtr save = Save();
            byte difficulty = Memory.U8(layer + _packDifficultyField);
            int localization = CurrentLocalization();

            if (!Memory.LooksLikeObject(dataAccess) || !Memory.LooksLikeObject(save) || localization < 0)
            {
                PackSwitchMissing();
                return IntPtr.Zero;
            }

            IntPtr fresh = BuildPackCore((IntPtr)live, dataAccess, save, difficulty, localization);
            if (fresh == IntPtr.Zero)
            {
                PackSwitchMissing();
                return IntPtr.Zero;
            }

            Diagnostics.Info($"pack view: rebuilt the custom pack's view (id {packId})");
            return fresh;
        }

        /// <summary>Whether a cached pack view already holds exactly the live pack's songs.</summary>
        private static bool PackViewCurrent(IntPtr info, IntPtr liveSongs)
        {
            IntPtr songs = Memory.Ptr(info + _ihSongArrayField);
            if (!Memory.LooksLikeObject(songs)) return false;

            int count = Memory.I32(songs + Offsets.Runtime.ArrayLength);
            int live = Memory.I32(liveSongs + Offsets.Runtime.ArrayLength);
            if (count != live) return false;

            for (int i = 0; i < live; i++)
            {
                ushort id = Memory.U16(liveSongs + Offsets.Runtime.ArrayDataOffset + i * 2);
                if (!PackViewHolds(songs, count, id)) return false;
            }

            return true;
        }

        /// <summary>Whether one cached pack view holds a song id.</summary>
        private static bool PackViewHolds(IntPtr songs, int count, ushort id)
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr song = Memory.Ptr(songs + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size);
                if (!Memory.LooksLikeObject(song)) continue;
                if (Memory.U16(song + _shSongField + Offsets.Song.Id) == id) return true;
            }

            return false;
        }

        /// <summary>Puts a rebuilt custom pack view where the pack screen looks it up.</summary>
        internal static void AdoptPackView(IntPtr layer, IntPtr fresh)
        {
            if (fresh == IntPtr.Zero || !PreparePackSwitch()) return;

            ushort packId = Memory.U16(fresh + _ihPackField);
            if (!SongCatalog.IsCustomPack(packId)) return;
            ReplacePackView(layer, packId, fresh);
        }

        /// <summary>Resolves what the category screen's pack list needs, or says once why it cannot be refreshed.</summary>
        private static bool PreparePackOutline()
        {
            if (_outlineReady) return true;
            if (_outlineMissing) return false;
            if (!PreparePackSwitch())
            {
                _outlineMissing = true;
                return false;
            }

            _packOutlineListField = FieldResolver.Field("PackSelectScene", "_ff");
            _outlinePackField = FieldResolver.Field("PackOutlineMember", "_df");
            _outlineUpdateInfo = MethodResolver.MethodInfoByRuntime("PackOutlineMember", "_XB");
            if (_packOutlineListField < 0 || _outlinePackField < 0 ||
                _outlineUpdateInfo == IntPtr.Zero ||
                Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(_outlineUpdateInfo) != 1)
            {
                _outlineMissing = true;
                Diagnostics.Error(OutlineFailed);
                return false;
            }

            _outlineReady = true;
            return true;
        }

        /// <summary>Resolves the small pack cards the pack screen pools, or says once why they cannot be refreshed.</summary>
        private static bool PreparePackCards()
        {
            if (_cardsReady) return true;
            if (_cardsMissing) return false;
            if (!PreparePackSwitch())
            {
                _cardsMissing = true;
                return false;
            }

            _topPackContainerField = FieldResolver.Field("PackSelectScene", "topScrollContainer");
            _bottomPackContainerField = FieldResolver.Field("PackSelectScene", "bottomScrollContainer");
            _packCardListField = FieldResolver.Field("PackSelectScrollContainer", "_jr");
            _cardPackField = FieldResolver.Field("PackVisualMemberSmall", "_Pf");
            _packCardUpdateInfo = MethodResolver.MethodInfoByRuntime("PackSelectScrollContainer", "_zn");
            if (_topPackContainerField < 0 || _bottomPackContainerField < 0 ||
                _packCardListField < 0 || _cardPackField < 0 ||
                _packCardUpdateInfo == IntPtr.Zero ||
                Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(_packCardUpdateInfo) != 1)
            {
                _cardsMissing = true;
                Diagnostics.Error(OutlineFailed);
                return false;
            }

            _cardsReady = true;
            return true;
        }

        /// <summary>The one sentence the pack list reports when its counts cannot be rebuilt.</summary>
        internal const string OutlineFailed =
            "the pack list's counts could not be refreshed in this build; a reloaded pack will keep the counts it was first shown with";

        /// <summary>Updates every category entry that belongs to one of this mod's packs.</summary>
        internal static void RefreshPackOutline()
        {
            if (!PreparePackOutline()) return;

            IntPtr scene = Scene();
            if (scene == IntPtr.Zero) return;

            IntPtr layer = Memory.Ptr(scene + _packLayerField);
            if (!Memory.LooksLikeObject(layer)) return;

            IntPtr outlines = Memory.Ptr(layer + _packOutlineListField);
            if (!Memory.TryList(outlines, out IntPtr items, out int count)) return;

            byte difficulty = Memory.U8(layer + _packDifficultyField);
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&difficulty);

            for (int i = 0; i < count; i++)
            {
                IntPtr outline = Memory.Ptr(items + i * IntPtr.Size);
                if (!Memory.LooksLikeObject(outline)) continue;

                IntPtr info = Memory.Ptr(outline + _outlinePackField);
                if (!Memory.LooksLikeObject(info)) continue;

                ushort packId = Memory.U16(info + _ihPackField);
                if (!SongCatalog.IsCustomPack(packId)) continue;

                IntPtr fresh = PackViewFor(layer, info);
                if (fresh == IntPtr.Zero) continue;

                Memory.WritePtr(outline + _outlinePackField, fresh);
                ReplacePackView(layer, packId, fresh);
                RefreshPackCards(layer, fresh);

                Diagnostics.Info($"probe: pack counts old={ArrayCount(Memory.Ptr(info + _ihPackField + Offsets.Pack.Songs))} " +
                                 $"new={ArrayCount(Memory.Ptr(fresh + _ihPackField + Offsets.Pack.Songs))}");

                IntPtr raised = IntPtr.Zero;
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_outlineUpdateInfo, outline,
                                                                    (void**)args, ref raised);
                if (raised != IntPtr.Zero)
                    Diagnostics.Error("the pack list's counts could not be refreshed: " + Raised.Text(raised));
                else
                    Diagnostics.Info($"probe: pack outline refreshed (id {packId})");
            }

            Hooks.ApplyPackMasks(layer);
            Hooks.ApplyPackButtonMask(scene);
        }

        /// <summary>Rebinds the pack screen's small cards for one custom pack and rewrites their texts.</summary>
        private static void RefreshPackCards(IntPtr layer, IntPtr fresh)
        {
            if (fresh == IntPtr.Zero) return;

            ushort packId = Memory.U16(fresh + _ihPackField);
            if (!SongCatalog.IsCustomPack(packId)) return;
            if (!PreparePackCards()) return;

            int touched = 0;
            touched += RebindPackCards(layer, _topPackContainerField, packId, fresh);
            touched += RebindPackCards(layer, _bottomPackContainerField, packId, fresh);
            Hooks.ApplyPackMasks(layer);
            Diagnostics.Info($"probe: pack cards refreshed (id {packId}, cards={touched})");
        }

        /// <summary>Rebinds one scroll container's visible cards, then lets the container rewrite them.</summary>
        private static int RebindPackCards(IntPtr layer, int field, ushort packId, IntPtr fresh)
        {
            if (field < 0) return 0;

            IntPtr container = Memory.Ptr(layer + field);
            if (!Memory.LooksLikeObject(container)) return 0;

            IntPtr cards = Memory.Ptr(container + _packCardListField);
            if (!Memory.TryList(cards, out IntPtr items, out int count)) return 0;

            int touched = 0;
            for (int i = 0; i < count; i++)
            {
                IntPtr card = Memory.Ptr(items + i * IntPtr.Size);
                if (!Memory.LooksLikeObject(card)) continue;

                IntPtr info = Memory.Ptr(card + _cardPackField);
                if (!Memory.LooksLikeObject(info)) continue;
                if (Memory.U16(info + _ihPackField) != packId) continue;

                Memory.WritePtr(card + _cardPackField, fresh);
                touched++;
            }

            byte difficulty = Memory.U8(layer + _packDifficultyField);
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&difficulty);

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_packCardUpdateInfo, container, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
                Diagnostics.Error("the pack list's counts could not be refreshed: " + Raised.Text(raised));

            return touched;
        }

        /// <summary>The number of elements in one array, or -1.</summary>
        private static int ArrayCount(IntPtr array) =>
            Memory.LooksLikeObject(array) ? Memory.I32(array + Offsets.Runtime.ArrayLength) : -1;

        /// <summary>Rebuilds the pack the category screen is showing, whether or not it is the current one.</summary>
        internal static void RefreshDisplayedPack()
        {
            if (!PreparePackSwitch()) return;

            IntPtr scene = Scene();
            if (scene == IntPtr.Zero) return;

            IntPtr layer = Memory.Ptr(scene + _packLayerField);
            if (!Memory.LooksLikeObject(layer)) return;

            IntPtr list = Memory.Ptr(layer + _packListField);
            if (!Memory.TryList(list, out IntPtr items, out int count)) return;

            int at = Memory.I32(layer + _packIndexField);
            if (at < 0 || at >= count) return;

            IntPtr info = Memory.Ptr(items + at * IntPtr.Size);
            if (!Memory.LooksLikeObject(info)) return;

            ushort packId = Memory.U16(info + _ihPackField);
            if (!SongCatalog.IsCustomPack(packId)) return;

            IntPtr fresh = PackViewFor(layer, info);
            if (fresh == IntPtr.Zero) return;

            Memory.WritePtr(items + at * IntPtr.Size, fresh);
            RefreshPackScreen(layer, fresh, at);
            Diagnostics.Info($"probe: displayed pack refreshed (id {packId})");
        }

        /// <summary>The game's current localization, or -1 when it cannot be asked.</summary>
        private static int CurrentLocalization()
        {
            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_localizationInfo, IntPtr.Zero,
                                                                             null, ref raised);
            if (raised != IntPtr.Zero || boxed == IntPtr.Zero) return -1;
            return Memory.I32(boxed + Offsets.Runtime.BoxedData);
        }

        /// <summary>What one card scroll container holds after the refresh.</summary>
        private static void LogCards(IntPtr scene, int field, string side)
        {
            if (field < 0 || _cardListField < 0 || _cardIndexField < 0) return;

            IntPtr container = Memory.Ptr(scene + field);
            if (!Memory.LooksLikeObject(container)) return;

            Diagnostics.Info($"probe: refresh {side} cards={ListCount(container, _cardListField)} " +
                             $"index={Memory.I32(container + _cardIndexField)} " +
                             $"selected={Memory.I32(scene + _selectedIndexField)}");
        }

        /// <summary>Rebuilds and sorts the list without the pack-select transition.</summary>
        private static void ApplyInline(IntPtr scene, IntPtr pack)
        {
            IntPtr* args = stackalloc IntPtr[1];

            args[0] = pack;
            Invoke(_vnInfo, scene, args, "the song list could not be rebuilt");

            int sort = Memory.I32(scene + _sortField);
            args[0] = (IntPtr)(&sort);
            Invoke(_aoInfo, scene, args, "the song list could not be sorted");

            ushort selected = Memory.U16(scene + _selectedField);
            bool shadowed = Selection.ForcePictureReload(scene, selected);
            if (shadowed) Hooks.ApplyReloaded++;

            args[0] = (IntPtr)(&selected);
            Invoke(_onInfo, scene, args, "the song could not be selected");
            if (shadowed) Selection.Unshadow(scene, selected);

            if (_snInfo != IntPtr.Zero && ListCount(scene, _viewListField) > 0)
                Invoke(_snInfo, scene, null, "the song select could not be refreshed");
        }

        /// <summary>Rebuilds the scene's all-song list from the live table.</summary>
        private static void RefreshSongs(IntPtr scene)
        {
            if (_songsListField < 0 || _yoAllInfo == IntPtr.Zero) return;

            IntPtr list = Memory.Ptr(scene + _songsListField);
            if (!Memory.LooksLikeObject(list)) return;

            ClearList(list);

            IntPtr save = Save();
            if (!Memory.LooksLikeObject(save)) return;

            byte difficulty = 1;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = list;
            args[1] = (IntPtr)(&difficulty);

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_yoAllInfo, save, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
            {
                Diagnostics.Error($"the song list could not be refilled: {Raised.Text(raised)}");
                return;
            }

            Diagnostics.Info($"probe: refresh songs={ListCount(scene, _songsListField)}");
        }

        /// <summary>Empties a game List.</summary>
        internal static void ClearList(IntPtr list)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(list);
            if (klass == IntPtr.Zero) return;

            IntPtr clear = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, "Clear", 0);
            if (clear == IntPtr.Zero) return;

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(clear, list, null, ref raised);
            if (raised != IntPtr.Zero)
                Diagnostics.Error($"the song list could not be cleared: {Raised.Text(raised)}");
        }

        /// <summary>The one sentence reported when the pack list cannot be read or built.</summary>
        internal const string PackListUnavailable =
            "the pack list is not in this build; an empty custom pack will keep its category entry";

        /// <summary>Resolves what taking a pack out of the live category screen needs.</summary>
        /// <summary>A fresh pack view model built from the live row.</summary>
        private static IntPtr BuildPack(IntPtr scene, IntPtr packInfo)
        {
            if (_dataAccessField < 0) return IntPtr.Zero;
            return BuildPackCore(packInfo, Memory.Ptr(scene + _dataAccessField), Save(), 1, 0);
        }

        /// <summary>A pack view model built from one PackInfo and the object that owns it.</summary>
        private static IntPtr BuildPackCore(IntPtr packInfo, IntPtr dataAccess, IntPtr save,
                                            byte difficulty, int localization)
        {
            if (_ihClass == IntPtr.Zero || _ihCtor == IntPtr.Zero) return IntPtr.Zero;
            if (!Memory.LooksLikeObject(dataAccess) || !Memory.LooksLikeObject(save)) return IntPtr.Zero;

            IntPtr fresh = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(_ihClass);
            if (fresh == IntPtr.Zero) return IntPtr.Zero;

            IntPtr* args = stackalloc IntPtr[5];
            args[0] = packInfo;
            args[1] = dataAccess;
            args[2] = save;
            args[3] = (IntPtr)(&difficulty);
            args[4] = (IntPtr)(&localization);

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_ihCtor, fresh, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
            {
                Diagnostics.Error($"the current pack could not be rebuilt: {Raised.Text(raised)}");
                return IntPtr.Zero;
            }

            return fresh;
        }

        /// <summary>Puts the save's selected pack back to all songs when the custom pack is empty.</summary>
        private static bool DeselectPack()
        {
            if (_selectPackInfo == IntPtr.Zero)
            {
                DeselectFailed("no pack setter in this build");
                return false;
            }

            IntPtr save = Save();
            if (!Memory.LooksLikeObject(save))
            {
                DeselectFailed("the save object is not there");
                return false;
            }

            ushort none = ushort.MaxValue;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&none);

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_selectPackInfo, save, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
            {
                DeselectFailed(Raised.Text(raised));
                return false;
            }

            return true;
        }

        /// <summary>Puts the save back on all songs when it points at a pack that is no longer live.</summary>
        internal static void DeselectIfGonePackSelected()
        {
            if (!Resolve()) return;
            if (!CurrentPackId(out ushort selected)) return;
            if (selected == ushort.MaxValue) return;
            if (SongCatalog.LivePackExists(selected)) return;

            DeselectPack();
        }

        /// <summary>The pack the save has selected.</summary>
        internal static bool SelectedPack(out ushort packId)
        {
            if (!Resolve())
            {
                packId = ushort.MaxValue;
                return false;
            }

            return CurrentPackId(out packId);
        }

        /// <summary>Writes the save's selected pack through the game's own setter.</summary>
        internal static bool SetSelectedPack(ushort packId)
        {
            if (!Resolve()) return false;
            if (_selectPackInfo == IntPtr.Zero)
            {
                HoldFailed("no pack setter in this build");
                return false;
            }

            IntPtr save = Save();
            if (!Memory.LooksLikeObject(save))
            {
                HoldFailed("the save object is not there");
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&packId);

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_selectPackInfo, save, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
            {
                HoldFailed(Raised.Text(raised));
                return false;
            }

            return true;
        }

        /// <summary>Records what the category screen was built from.</summary>
        internal static void NotePackScreenBuilt(string screenSignature) => _screenSignature = screenSignature;

        /// <summary>The one sentence reported when an empty pack could not be deselected.</summary>
        private const string DeselectFailedText =
            "the empty pack could not be deselected; re-entering the song select with it selected will fail: ";

        private static void DeselectFailed(string why) => Diagnostics.Error(DeselectFailedText + why);

        /// <summary>The one sentence reported when the category screen's position cannot be held across its rebuild.</summary>
        private const string HoldFailedText =
            "the category screen's position could not be held across its rebuild: ";

        internal static void HoldFailed(string why) => Diagnostics.Error(HoldFailedText + why);

        /// <summary>The one sentence reported when the pack title cannot be moved back to all songs.</summary>
        private const string PackTitleFailed =
            "the song select's pack title could not be updated in this build; it will keep the name of the pack that left";

        /// <summary>
        /// Puts the song select's pack button back on all songs, through the game's own `_Tn` and the
        /// all-songs pack info the scene keeps (`_pr`) — the same pair `_Mo(null)` uses.
        /// </summary>
        private static void ShowAllSongsTitle(IntPtr scene)
        {
            if (!PreparePackTitle()) return;

            IntPtr container = Memory.Ptr(scene + _packButtonField);
            if (!Memory.LooksLikeObject(container))
            {
                Diagnostics.Error(PackTitleFailed);
                return;
            }

            IntPtr dataAccess = Memory.Ptr(scene + _dataAccessField);
            IntPtr save = Save();
            int localization = CurrentLocalization();
            if (!Memory.LooksLikeObject(dataAccess) || !Memory.LooksLikeObject(save) || localization < 0)
            {
                Diagnostics.Error(PackTitleFailed);
                return;
            }

            IntPtr view = BuildPackCore(scene + _packInfoField, dataAccess, save, 1, localization);
            if (view == IntPtr.Zero)
            {
                Diagnostics.Error(PackTitleFailed);
                return;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&view);

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_packTitleInfo, container, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
            {
                Diagnostics.Error(PackTitleFailed + " " + Raised.Text(raised));
                return;
            }

            Diagnostics.Info("probe: pack title set to all songs");
        }

        /// <summary>Resolves what putting the pack button back on all songs needs.</summary>
        private static bool PreparePackTitle()
        {
            if (_titleReady) return true;
            if (_titleMissing) return false;
            if (!PreparePackSwitch())
            {
                _titleMissing = true;
                return false;
            }

            _packButtonField = FieldResolver.Field(SceneType, "packSelectButtonContainer");
            _packInfoField = FieldResolver.Field(SceneType, "_pr");
            _packTitleInfo = MethodInfo("PackSelectButtonContainer", "_Tn", 1);

            if (_packButtonField < 0 || _packInfoField < 0 || _packTitleInfo == IntPtr.Zero)
            {
                _titleMissing = true;
                Diagnostics.Error(PackTitleFailed);
                return false;
            }

            _titleReady = true;
            return true;
        }

        /// <summary>The player's save object.</summary>
        private static IntPtr Save()
        {
            IntPtr klass = FieldResolver.ClassPointer("_NH");
            if (klass == IntPtr.Zero) return IntPtr.Zero;

            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return IntPtr.Zero;

            return Memory.Ptr(statics);
        }

        /// <summary>The pack the save has selected, or 0xFFFF for all songs.</summary>
        private static bool CurrentPackId(out ushort packId)
        {
            packId = ushort.MaxValue;

            IntPtr klass = FieldResolver.ClassPointer("_NH");
            if (klass == IntPtr.Zero) return false;

            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return false;

            IntPtr save = Memory.Ptr(statics);
            if (!Memory.LooksLikeObject(save)) return false;

            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_aoAInfo, save, null, ref raised);
            if (raised != IntPtr.Zero || !Memory.LooksLikeObject(boxed)) return false;

            packId = Memory.U16(boxed + Offsets.Runtime.BoxedData);
            return true;
        }

        /// <summary>The pack layer's view model for one pack id.</summary>
        private static IntPtr PackFor(IntPtr layer, ushort packId)
        {
            ushort id = packId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&id);

            IntPtr raised = IntPtr.Zero;
            IntPtr pack = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_gcInfo, layer, (void**)args, ref raised);
            if (raised != IntPtr.Zero) return IntPtr.Zero;

            return pack;
        }

        /// <summary>The ids a pack copy holds, as text, or "?".</summary>
        private static string IdsText(IntPtr pack)
        {
            IntPtr ids = Memory.Ptr(pack + Offsets.Pack.Songs);
            if (!Memory.LooksLikeObject(ids)) return "?";

            int count = Memory.I32(ids + Offsets.Runtime.ArrayLength);
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) text.Append(',');
                text.Append(Memory.U16(ids + Offsets.Runtime.ArrayDataOffset + i * 2));
            }
            return text.ToString();
        }

        /// <summary>How many entries a scene list holds, or -1.</summary>
        private static int ListCount(IntPtr scene, int field)
        {
            if (field < 0) return -1;

            IntPtr list = Memory.Ptr(scene + field);
            return Memory.LooksLikeObject(list) ? Memory.I32(list + Offsets.Runtime.ListSize) : -1;
        }

        /// <summary>Calls a game method and reports the exception it raised.</summary>
        private static void Invoke(IntPtr method, IntPtr instance, IntPtr* args, string what)
        {
            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, instance, (void**)args, ref raised);
            if (raised == IntPtr.Zero) return;

            Diagnostics.Error($"{what}: {Raised.Text(raised)}");
        }

        /// <summary>The selected song's id and name before a rebuild moves the slots.</summary>
        internal static void CaptureSelection(out ushort id, out string name)
        {
            id = 0;
            name = null;

            IntPtr scene = Scene();
            if (scene == IntPtr.Zero) return;
            if (!Resolve()) return;
            if (_selectedField < 0) return;

            id = Memory.U16(scene + _selectedField);
            if (SongCatalog.ById.TryGetValue(id, out ChartInfo info)) name = info.BaseName;
        }

        /// <summary>Puts the selected id back on the slot its name now has.</summary>
        internal static void RestoreSelection(string name)
        {
            if (name == null) return;

            IntPtr scene = Scene();
            if (scene == IntPtr.Zero) return;
            if (!Resolve()) return;
            if (_selectedField < 0) return;

            foreach (KeyValuePair<ushort, ChartInfo> slot in SongCatalog.ById)
            {
                if (!string.Equals(slot.Value.BaseName, name, StringComparison.Ordinal)) continue;

                Memory.WriteU16(scene + _selectedField, slot.Key);
                return;
            }

            Memory.WriteU16(scene + _selectedField, 0);
        }

        /// <summary>The live song-select scene, or zero.</summary>
        internal static IntPtr Scene()
        {
            IntPtr scene = Hooks.SongSelect;
            if (!JacketFactory.IsAlive(scene)) return IntPtr.Zero;

            if (_sceneClass == IntPtr.Zero) _sceneClass = FieldResolver.ClassPointer(SceneType);
            if (_sceneClass == IntPtr.Zero) return IntPtr.Zero;

            return Memory.Ptr(scene) == _sceneClass ? scene : IntPtr.Zero;
        }

        /// <summary>Resolves the fields and methods the two refresh paths call.</summary>
        private static bool Resolve()
        {
            if (_resolved) return true;
            if (_missing) return false;

            _selectedField = FieldResolver.Field(SceneType, "_sr");
            _packLayerField = FieldResolver.Field(SceneType, "packSelectLayer");
            _ihPackField = FieldResolver.Field("_iH", "_RDb");
            _songsListField = FieldResolver.Lookup(SceneType, "_Qr");
            _viewListField = FieldResolver.Lookup(SceneType, "_rr");
            _leftCardField = FieldResolver.Lookup(SceneType, "leftSongCardContainer");
            _rightCardField = FieldResolver.Lookup(SceneType, "rightSongCardContainer");
            _cardListField = FieldResolver.Lookup("SongSelectSmallCardScrollContainer", "_yR");
            _cardIndexField = FieldResolver.Lookup("SongSelectSmallCardScrollContainer", "_YR");
            _selectedIndexField = FieldResolver.Lookup(SceneType, "_Rr");
            _sortField = FieldResolver.Field(SceneType, "_Tr");

            _vnInfo = MethodInfo(SceneType, "_VN", 1);
            _aoInfo = MethodInfo(SceneType, "_Ao", 1);
            _onInfo = MethodInfo(SceneType, "_ON", 1);
            _snInfo = MethodInfo(SceneType, "_SN", 0);
            _gcInfo = MethodInfo("PackSelectScene", "_Gc", 1);
            _aoAInfo = MethodInfo("_NH", "_aoA", 0);
            _selectPackInfo = MethodInfo("_NH", "_hoA", 1);
            _yoInfo = MethodInfo("SongSelectSmallCardScrollContainer", "_Yo", 2);
            _yoAllInfo = MethodInfo("_NH", "_YoA", 2);
            _dataAccessField = FieldResolver.Field(SceneType, "dataAccess");
            _packVisualField = FieldResolver.Field("PackSelectScene", "packVisualMemberLarge");
            _packListField = FieldResolver.Field("PackSelectScene", "_Ef");
            _packIndexField = FieldResolver.Field("PackSelectScene", "_gf");
            _packRebuildInfo = MethodResolver.MethodInfoByRuntime("PackVisualMemberLarge", "_Ef");
            if (_packRebuildInfo != IntPtr.Zero &&
                Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(_packRebuildInfo) != 4)
            {
                Diagnostics.Error("the category screen's cards could not be rebuilt; it will keep the " +
                                  "pictures it was built with");
                _packRebuildInfo = IntPtr.Zero;
            }
            _ihClass = FieldResolver.ClassPointer("_iH");
            if (_ihClass != IntPtr.Zero)
                _ihCtor = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(_ihClass, ".ctor", 5);

            if (_packLayerField < 0 || _ihPackField < 0 || _dataAccessField < 0 || _sortField < 0 ||
                _vnInfo == IntPtr.Zero || _aoInfo == IntPtr.Zero || _onInfo == IntPtr.Zero ||
                _gcInfo == IntPtr.Zero || _aoAInfo == IntPtr.Zero ||
                _ihClass == IntPtr.Zero || _ihCtor == IntPtr.Zero || _yoAllInfo == IntPtr.Zero)
            {
                _missing = true;
                Diagnostics.Error("the song select's refresh methods are not in this build; " +
                                  "a reloaded chart will not be redrawn until the scene is entered again");
                return false;
            }

            _resolved = true;
            return true;
        }

        /// <summary>A method's MethodInfo from the generated store.</summary>
        private static IntPtr MethodInfo(string storeType, string method, int parameters) =>
            MethodResolver.MethodInfoOf(storeType, method, parameters);
    }
}
