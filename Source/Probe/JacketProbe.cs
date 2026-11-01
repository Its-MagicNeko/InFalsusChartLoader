#if DEBUG
using System;
using System.Reflection;

namespace InFalsusChartLoader
{
    /// <summary>Debug-only report of the generated Unity types.</summary>
    internal static class JacketProbe
    {
        private const BindingFlags AnyField =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance |
            BindingFlags.DeclaredOnly;

        private const string CtorField =
            "NativeMethodInfoPtr__ctor_Public_Void_Int32_Int32_TextureFormat_Boolean_Boolean_0";

        internal static void Report()
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = asm.GetName().Name;
                if (name == null || name.IndexOf("UnityEngine.CoreModule", StringComparison.Ordinal) < 0) continue;

                Diagnostics.Info($"probe: assembly '{name}' location='{Location(asm)}'");
                Diagnostics.Info($"probe:   identity '{asm.FullName}'");

                Type direct = null;
                try { direct = asm.GetType("UnityEngine.Texture2D", throwOnError: false); }
                catch (Exception) { }
                if (direct != null)
                {
                    Report(direct);
                    continue;
                }

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                    Diagnostics.Info($"probe:   GetTypes threw, {e.LoaderExceptions?.Length ?? 0} loader exception(s)");
                    if (e.LoaderExceptions != null)
                    {
                        foreach (Exception loader in e.LoaderExceptions)
                        {
                            if (loader == null) continue;
                            Diagnostics.Info($"probe:     {loader.GetType().Name}: {loader.Message}");
                        }
                    }
                }
                catch (Exception e)
                {
                    Diagnostics.Info($"probe:   GetTypes threw {e.GetType().Name}: {e.Message}");
                    continue;
                }

                int found = 0;
                foreach (Type t in types)
                {
                    if (t == null || t.Name != "Texture2D") continue;
                    found++;
                    Report(t);
                }
                if (found == 0) Diagnostics.Info("probe:   no type named Texture2D in this assembly");
            }
        }

        private static void Report(Type t)
        {
            FieldInfo[] fields;
            try
            {
                fields = t.GetFields(AnyField);
            }
            catch (Exception e)
            {
                Diagnostics.Info($"probe:   '{t.FullName}' GetFields threw {e.GetType().Name}");
                return;
            }

            Diagnostics.Info($"probe:   type '{t.FullName}' in '{t.Assembly.GetName().Name}' " +
                             $"declares {fields.Length} field(s), assembly-qualified '{t.AssemblyQualifiedName}'");

            int native = 0;
            foreach (FieldInfo f in fields)
            {
                if (f.Name.IndexOf("NativeMethodInfoPtr", StringComparison.Ordinal) < 0) continue;
                if (native < 5) Diagnostics.Info($"probe:     {f.Name}");
                native++;
            }
            Diagnostics.Info($"probe:     ({native} field(s) named NativeMethodInfoPtr*, showing up to 5)");

            FieldInfo byName = null;
            try { byName = t.GetField(CtorField, AnyField); }
            catch (Exception e) { Diagnostics.Info($"probe:     GetField threw {e.GetType().Name}"); }

            FieldInfo byHand = null;
            foreach (FieldInfo f in fields) { if (f.Name == CtorField) { byHand = f; break; } }

            Diagnostics.Info($"probe:     GetField('{CtorField}') -> " +
                             (byName == null ? "null" : "found") +
                             ", searched by hand -> " + (byHand == null ? "null" : "found"));
        }

        private static string Location(Assembly asm)
        {
            try { return asm.Location; }
            catch (Exception) { return "(dynamic)"; }
        }
    }
}
#endif
