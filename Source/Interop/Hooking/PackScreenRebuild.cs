extern alias UnityEngineCore;

using System;

namespace InFalsusChartLoader
{
    /// <summary>
    /// Rebuilds the category screen from the live pack table with the game's own entry initializer.
    /// </summary>
    internal static unsafe class PackScreenRebuild
    {
        /// <summary>Set while the category screen's own initializer runs.</summary>
        internal static bool InProgress;

        private static bool _resolved;
        private static bool _missing;

        private static int _sceneLayerField = Offsets.Unresolved;
        private static int _packIndexField = Offsets.Unresolved;
        private static int _packDifficultyField = Offsets.Unresolved;
        private static int _lastPackIndexField = Offsets.Unresolved;
        private static int _packListField = Offsets.Unresolved;
        private static int _outlineListField = Offsets.Unresolved;
        private static int _viewPackField = Offsets.Unresolved;
        private static int _topScrollField = Offsets.Unresolved;
        private static int _bottomScrollField = Offsets.Unresolved;
        private static int _scrollViewField = Offsets.Unresolved;
        private static int _scrollContentField = Offsets.Unresolved;
        private static int _scrollTargetField = Offsets.Unresolved;
        private static int _scrollSettledField = Offsets.Unresolved;
        private static IntPtr _awakeInfo;
        private static IntPtr _selectInfo;
        private static IntPtr _difficultyInfo;
        private static IntPtr _scrollPositionInfo;
        private static IntPtr _scrollSetInfo;
        private static bool _scrollResolved;
        private static bool _scrollMissing;
        private static bool _snapWarned;

        /// <summary>The one sentence reported when the game's own category screen cannot be rebuilt.</summary>
        internal const string RebuildFailed =
            "the category screen could not be rebuilt in this build; it will keep the packs it was built with";

        /// <summary>Rebuilds the category screen of one song-select scene.</summary>
        internal static bool Rebuild(IntPtr scene)
        {
            if (!Resolve()) return false;
            if (!Memory.LooksLikeObject(scene)) return false;

            IntPtr layer = Memory.Ptr(scene + _sceneLayerField);
            if (!Memory.LooksLikeObject(layer))
            {
                Diagnostics.Error(RebuildFailed);
                return false;
            }

            int shown = Memory.I32(layer + _packIndexField);
            ushort shownPack = PackIdAt(layer, shown);
            byte difficulty = Memory.U8(layer + _packDifficultyField);

            bool rebuilt = false;
            ushort heldPack = 0;
            bool held = false;

            Diagnostics.Info($"probe: pack screen rebuild begin shown={shown} pack={shownPack} " +
                             $"packs={PackCount(layer)}");

            InProgress = true;
            try
            {
                if (SongSelectRefresh.SelectedPack(out heldPack) &&
                    heldPack != shownPack &&
                    SongCatalog.LivePackExists(shownPack))
                    held = SongSelectRefresh.SetSelectedPack(shownPack);

                ClearOutlines(layer);
                SongSelectRefresh.ClearList(Memory.Ptr(layer + _packListField));

                Memory.WriteI32(layer + _lastPackIndexField, 0);

                if (!Invoke(layer, _awakeInfo, "the category screen could not be initialised"))
                    return false;

                Diagnostics.Info($"probe: pack screen awake returned packs={PackCount(layer)}");

                if (held)
                    held = !SongSelectRefresh.SetSelectedPack(heldPack);

                int afterAwake = Memory.I32(layer + _packIndexField);

                int target = PackIndex(layer, shownPack);
                if (target >= 0 && Memory.I32(layer + _packIndexField) != target)
                    Invoke(layer, _selectInfo, target);

                Diagnostics.Info($"probe: pack screen position shown={shown} pack={shownPack} -> {target} " +
                                 $"packs={PackCount(layer)} awake={afterAwake}");

                Invoke(layer, _difficultyInfo, difficulty);
                rebuilt = true;
            }
            catch (Exception e)
            {
                Diagnostics.Error(RebuildFailed + " " + Diagnostics.Describe(e));
            }
            finally
            {
                if (held) SongSelectRefresh.SetSelectedPack(heldPack);
                InProgress = false;
            }

            if (!rebuilt) return false;

            SongSelectRefresh.NotePackScreenBuilt(SongCatalog.PackScreenSignature());
            Diagnostics.Info("probe: pack screen rebuilt through the game's own build");
            return true;
        }

        /// <summary>Takes the built category entries out of the screen.</summary>
        private static void ClearOutlines(IntPtr layer)
        {
            IntPtr list = Memory.Ptr(layer + _outlineListField);
            if (!Memory.LooksLikeObject(list)) return;

            int count = Memory.I32(list + Offsets.Runtime.ListSize);
            IntPtr items = Memory.Ptr(list + Offsets.Runtime.ListItems);
            if (Memory.LooksLikeObject(items))
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr outline = Memory.Ptr(items + Offsets.Runtime.ArrayDataOffset + i * sizeof(IntPtr));
                    if (!Memory.LooksLikeObject(outline)) continue;

                    IntPtr go = InvokeNoArg(outline, "get_gameObject");
                    if (Memory.LooksLikeObject(go))
                        UnityEngineCore::UnityEngine.Object.Destroy(new UnityEngineCore::UnityEngine.Object(go));
                }
            }

            SongSelectRefresh.ClearList(list);
        }

        /// <summary>The pack id of one entry in the screen's own pack list.</summary>
        private static ushort PackIdAt(IntPtr layer, int index)
        {
            IntPtr view = ViewAt(layer, index);
            return Memory.LooksLikeObject(view) ? Memory.U16(view + _viewPackField) : ushort.MaxValue;
        }

        /// <summary>The index of a pack id in the rebuilt pack list, or -1 when it is gone.</summary>
        private static int PackIndex(IntPtr layer, ushort id)
        {
            int count = PackCount(layer);
            for (int i = 0; i < count; i++)
            {
                if (PackIdAt(layer, i) == id) return i;
            }
            return -1;
        }

        private static int PackCount(IntPtr layer)
        {
            IntPtr list = Memory.Ptr(layer + _packListField);
            return Memory.LooksLikeObject(list) ? Memory.I32(list + Offsets.Runtime.ListSize) : 0;
        }

        private static IntPtr ViewAt(IntPtr layer, int index)
        {
            if (index < 0) return IntPtr.Zero;

            IntPtr list = Memory.Ptr(layer + _packListField);
            if (!Memory.LooksLikeObject(list)) return IntPtr.Zero;

            int count = Memory.I32(list + Offsets.Runtime.ListSize);
            if (index >= count) return IntPtr.Zero;

            IntPtr items = Memory.Ptr(list + Offsets.Runtime.ListItems);
            if (!Memory.LooksLikeObject(items)) return IntPtr.Zero;

            return Memory.Ptr(items + Offsets.Runtime.ArrayDataOffset + index * sizeof(IntPtr));
        }

        /// <summary>Puts both card windows on the position the game's own select has just chosen.</summary>
        internal static void SnapScrolls(IntPtr layer)
        {
            if (!ResolveScroll()) return;
            Diagnostics.Info("probe: snap scrolls begin");

            SnapScroll(Memory.Ptr(layer + _topScrollField));
            SnapScroll(Memory.Ptr(layer + _bottomScrollField));
            Diagnostics.Info("probe: snap scrolls done");
        }

        /// <summary>Applies one card window's target offset immediately.</summary>
        private static void SnapScroll(IntPtr container)
        {
            if (!Memory.LooksLikeObject(container)) return;

            float target = Memory.F32(container + _scrollTargetField);

            IntPtr view = Memory.Ptr(container + _scrollViewField);
            if (!Memory.LooksLikeObject(view)) return;

            IntPtr content = Memory.Ptr(view + _scrollContentField);
            if (!Memory.LooksLikeObject(content)) return;

            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(
                _scrollPositionInfo, content, null, ref raised);
            if (raised != IntPtr.Zero || !Memory.LooksLikeObject(boxed))
            {
                SnapFailed("read");
                return;
            }

            float* position = stackalloc float[2];
            position[0] = Memory.F32(boxed + Offsets.Runtime.BoxedData);
            position[1] = target;

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)position;

            raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(
                _scrollSetInfo, content, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
            {
                SnapFailed("write");
                return;
            }

            Memory.WriteU8(container + _scrollSettledField, 0);
        }

        private static void SnapFailed(string why)
        {
            if (_snapWarned) return;

            _snapWarned = true;
            SongSelectRefresh.HoldFailed(why);
        }

        /// <summary>Calls a parameterless game method on the layer.</summary>
        private static bool Invoke(IntPtr instance, IntPtr method, string failure)
        {
            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, instance, null, ref raised);
            if (raised == IntPtr.Zero) return true;

            Diagnostics.Error(failure + ": " + Raised.Text(raised));
            return false;
        }

        /// <summary>Calls a one-int game method on the layer.</summary>
        private static void Invoke(IntPtr instance, IntPtr method, int value)
        {
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&value);
            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, instance, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
                Diagnostics.Error(RebuildFailed + " " + Raised.Text(raised));
        }

        /// <summary>Calls a one-byte game method on the layer.</summary>
        private static void Invoke(IntPtr instance, IntPtr method, byte value)
        {
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&value);
            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, instance, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
                Diagnostics.Error(RebuildFailed + " " + Raised.Text(raised));
        }

        /// <summary>Calls a parameterless game or Unity method and returns its result.</summary>
        private static IntPtr InvokeNoArg(IntPtr instance, string name)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(instance);
            if (klass == IntPtr.Zero) return IntPtr.Zero;

            IntPtr method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, name, 0);
            if (method == IntPtr.Zero) return IntPtr.Zero;

            IntPtr raised = IntPtr.Zero;
            IntPtr result = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, instance, null, ref raised);
            return raised == IntPtr.Zero ? result : IntPtr.Zero;
        }

        /// <summary>Resolves the game's names before a screen is built.</summary>
        internal static void Prepare(IntPtr awakeInfo)
        {
            if (_awakeInfo == IntPtr.Zero) _awakeInfo = awakeInfo;
            ResolveScroll();
        }

        /// <summary>Resolves the two card windows and their scroll setters.</summary>
        private static bool ResolveScroll()
        {
            if (_scrollResolved) return true;
            if (_scrollMissing) return false;

            _topScrollField = FieldResolver.Field("PackSelectScene", "topScrollContainer");
            _bottomScrollField = FieldResolver.Field("PackSelectScene", "bottomScrollContainer");
            _scrollViewField = FieldResolver.Field("PackSelectScrollContainer", "scrollView");
            _scrollContentField = FieldResolver.Field("UIScrollListView", "contentContainer");
            _scrollTargetField = FieldResolver.Field("PackSelectScrollContainer", "_Kr");
            _scrollSettledField = FieldResolver.Field("PackSelectScrollContainer", "_lr");
            _scrollPositionInfo = MethodResolver.MethodInfoByRuntime("Constrained2D", "_iQA");
            _scrollSetInfo = MethodResolver.MethodInfoByRuntime("Constrained2D", "_sQA");

            if (_topScrollField < 0 || _bottomScrollField < 0 || _scrollViewField < 0 ||
                _scrollContentField < 0 || _scrollTargetField < 0 || _scrollSettledField < 0 ||
                _scrollPositionInfo == IntPtr.Zero || _scrollSetInfo == IntPtr.Zero)
            {
                _scrollMissing = true;
                SnapFailed("scroll");
                return false;
            }

            _scrollResolved = true;
            return true;
        }

        /// <summary>Resolves the fields and methods the rebuild asks the game for.</summary>
        private static bool Resolve()
        {
            if (_resolved) return true;
            if (_missing) return false;

            _sceneLayerField = FieldResolver.Field("SongSelectScene", "packSelectLayer");
            _packIndexField = FieldResolver.Field("PackSelectScene", "_gf");
            _packDifficultyField = FieldResolver.Field("PackSelectScene", "_Gf");
            _lastPackIndexField = FieldResolver.Field("PackSelectScene", "_Hf");
            _packListField = FieldResolver.Field("PackSelectScene", "_Ef");
            _outlineListField = FieldResolver.Field("PackSelectScene", "_ff");
            _viewPackField = FieldResolver.Field("_iH", "_RDb");
            _awakeInfo = _awakeInfo != IntPtr.Zero
                ? _awakeInfo
                : MethodResolver.MethodInfoByRuntime("PackSelectScene", "Awake");
            _selectInfo = MethodResolver.MethodInfoByRuntime("PackSelectScene", "_ic");
            _difficultyInfo = MethodResolver.MethodInfoByRuntime("PackSelectScene", "_lc");

            if (_sceneLayerField < 0 || _packIndexField < 0 || _packDifficultyField < 0 ||
                _lastPackIndexField < 0 || _packListField < 0 || _outlineListField < 0 ||
                _viewPackField < 0 || _awakeInfo == IntPtr.Zero ||
                _selectInfo == IntPtr.Zero || _difficultyInfo == IntPtr.Zero ||
                Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(_awakeInfo) != 0 ||
                Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(_selectInfo) != 1 ||
                Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(_difficultyInfo) != 1)
            {
                _missing = true;
                Diagnostics.Error(RebuildFailed);
                return false;
            }

            _resolved = true;
            return true;
        }
    }
}
