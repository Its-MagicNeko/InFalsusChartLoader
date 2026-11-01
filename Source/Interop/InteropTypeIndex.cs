using System;
using System.Collections.Generic;
using System.Reflection;
#if BEPINEX
using System.IO;
#endif

namespace InFalsusChartLoader
{
    /// <summary>The generated interop types, indexed by name.</summary>
    internal static class InteropTypeIndex
    {
        private static Dictionary<string, List<Type>> _byName;
        private static Dictionary<string, Type> _lookup;

        /// <summary>Generated types by simple name. A name can hit more than one type.</summary>
        internal static Dictionary<string, List<Type>> ByName()
        {
            Build();
            return _byName;
        }

        /// <summary>The one generated type a name or full name denotes.</summary>
        internal static bool Resolve(string typeName, out Type type)
        {
            Build();

            if (!_lookup.TryGetValue(typeName, out type) || type == null)
            {
                Diagnostics.Error($"{typeName}: no single class to ask in this build");
                return false;
            }

            return true;
        }

        private static void Build()
        {
            if (_byName != null) return;

            var index = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
            var lookup = new Dictionary<string, Type>(StringComparer.Ordinal);
#if DEBUG
            int scanned = 0;
#endif

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!IsGenerated(asm)) continue;

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                catch (Exception e)
                {
                    Diagnostics.Warn($"{asm.GetName().Name} was skipped: {Diagnostics.Describe(e)}");
                    continue;
                }

                foreach (Type t in types)
                {
                    if (t == null) continue;

                    try
                    {
#if DEBUG
                        scanned++;
#endif

                        Add(index, t.Name, t);
                        AddLookup(lookup, t.Name, t);

                        if (t.DeclaringType != null)
                        {
                            string nested = t.DeclaringType.Name + "." + t.Name;
                            Add(index, nested, t);
                            AddLookup(lookup, nested, t);
                        }

                        AddLookup(lookup, FullName(t), t);
                    }
                    catch (Exception)
                    {
                    }
                }
            }

#if DEBUG
            Diagnostics.Info($"indexed {scanned} interop types by name");
#endif
            _byName = index;
            _lookup = lookup;
        }

        /// <summary>A type's namespace-qualified name, with its declaring types.</summary>
        private static string FullName(Type type)
        {
            if (type.DeclaringType != null) return FullName(type.DeclaringType) + "." + type.Name;

            return string.IsNullOrEmpty(type.Namespace) ? type.Name : type.Namespace + "." + type.Name;
        }

        /// <summary>Whether an assembly is one of the game's generated interop assemblies.</summary>
        private static bool IsGenerated(Assembly assembly)
        {
            string name = assembly.GetName().Name;
            if (name == null) return false;
            if (name.StartsWith("Il2Cpp", StringComparison.Ordinal)) return true;

#if BEPINEX
            if (name.StartsWith("Unity", StringComparison.Ordinal)) return false;
            return InteropAssemblyNames().Contains(name);
#else
            return false;
#endif
        }

#if BEPINEX
        private static HashSet<string> _interopNames;

        /// <summary>The assembly names in BepInEx's interop folder.</summary>
        private static HashSet<string> InteropAssemblyNames()
        {
            if (_interopNames != null) return _interopNames;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string folder = Path.Combine(BepInEx.Paths.BepInExRootPath, "interop");
                foreach (string path in Directory.GetFiles(folder, "*.dll"))
                    names.Add(Path.GetFileNameWithoutExtension(path));
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"the interop folder could not be read: {Diagnostics.Describe(e)}");
            }

            return _interopNames = names;
        }
#endif

        private static void Add(Dictionary<string, List<Type>> index, string key, Type type)
        {
            if (!index.TryGetValue(key, out List<Type> bucket))
                index[key] = bucket = new List<Type>(1);
            bucket.Add(type);
        }

        private static void AddLookup(Dictionary<string, Type> lookup, string key, Type type)
        {
            if (lookup.TryGetValue(key, out Type existing))
            {
                if (existing != type) lookup[key] = null;
                return;
            }

            lookup[key] = type;
        }
    }
}
