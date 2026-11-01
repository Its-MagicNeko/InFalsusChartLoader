using System;
using System.Reflection;

namespace InFalsusChartLoader
{
    /// <summary>Finds the native entry point of an IL2CPP method.</summary>
    /// <remarks>Every route resolves by name; a miss leaves the caller without a hook.</remarks>
    internal static unsafe class MethodResolver
    {
        private const BindingFlags AnyMethod =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        /// <summary>A method's native address by name, or zero.</summary>
        internal static IntPtr ByName(string typeName, string methodName)
        {
            IntPtr p = ViaMethodInfo(typeName, methodName);
            if (p != IntPtr.Zero)
            {
                Diagnostics.Info($"{typeName}.{methodName} -> 0x{p.ToInt64():X} (MethodInfo)");
                return p;
            }

            Diagnostics.Error($"{typeName}.{methodName}: no method by that name in this build");
            return IntPtr.Zero;
        }

        /// <summary>The runtime's <c>MethodInfo</c> for a method, or zero.</summary>
        internal static IntPtr MethodInfoOf(string typeName, string methodName) =>
            MethodInfoOf(typeName, methodName, -1);

        /// <summary>The runtime's <c>MethodInfo</c> for a method with a given parameter count.</summary>
        internal static IntPtr MethodInfoOf(string typeName, string methodName, int parameters)
        {
            try
            {
                if (!InteropTypeIndex.Resolve(typeName, out Type type)) return IntPtr.Zero;

                foreach (MethodInfo mi in type.GetMethods(AnyMethod | BindingFlags.DeclaredOnly))
                {
                    if (mi.Name != methodName) continue;
                    if (parameters >= 0 && mi.GetParameters().Length != parameters) continue;

                    FieldInfo f = Il2CppInterop.Common.Il2CppInteropUtils
                        .GetIl2CppMethodInfoPointerFieldForGeneratedMethod(mi);
                    if (f == null) continue;

                    IntPtr info = (IntPtr)f.GetValue(null);
                    if (info != IntPtr.Zero) return info;
                }
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"MethodInfo lookup for {typeName}.{methodName} failed: " +
                                 Diagnostics.Describe(e));
            }
            return IntPtr.Zero;
        }

        private static IntPtr ViaMethodInfo(string typeName, string methodName)
        {
            IntPtr info = MethodInfoOf(typeName, methodName);
            if (info == IntPtr.Zero) return IntPtr.Zero;

            return *(IntPtr*)info;
        }

        /// <summary>A method's native address, reached through the runtime.</summary>
        internal static IntPtr ByRuntime(string typeName, string methodName)
        {
            IntPtr klass = ClassOf(typeName);
            if (klass == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);

                IntPtr iter = IntPtr.Zero;
                IntPtr method;
                IntPtr found = IntPtr.Zero;

                IntPtr matched = IntPtr.Zero;

                while ((method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
                {
                    string name = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name_(method);
                    if (name != methodName) continue;

                    IntPtr code = *(IntPtr*)method;

#if DEBUG
                    Diagnostics.Info($"  {typeName}.{name}: code=0x{code.ToInt64():X} " +
                                     $"generic={Il2CppInterop.Runtime.IL2CPP.il2cpp_method_is_generic(method)} " +
                                     $"inflated={Il2CppInterop.Runtime.IL2CPP.il2cpp_method_is_inflated(method)} " +
                                     $"params={(int)Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(method)} " +
                                     $"words={JacketFactory.Words(method, 4)}");
#endif

                    if (code != IntPtr.Zero && found == IntPtr.Zero)
                    {
                        found = code;
                        matched = method;
                    }
                }

                if (found != IntPtr.Zero)
                {
                    if (Il2CppInterop.Runtime.IL2CPP.il2cpp_method_is_generic(matched) &&
                        Il2CppInterop.Runtime.IL2CPP.il2cpp_method_is_inflated(matched) == false)
                    {
                        IntPtr inflated = Inflated(klass, matched, typeName, methodName);
                        if (inflated == IntPtr.Zero)
                        {
                            Diagnostics.Error($"{typeName}.{methodName}: the generic instantiation is not in this build");
                            return IntPtr.Zero;
                        }

                        Diagnostics.Info($"{typeName}.{methodName} -> 0x{inflated.ToInt64():X} " +
                                         "(the instantiation, not the definition)");
                        return inflated;
                    }

                    Diagnostics.Info($"{typeName}.{methodName} -> 0x{found.ToInt64():X} (runtime walk)");
                    return found;
                }

                Diagnostics.Warn($"the runtime walk of {typeName} found no {methodName} with a code " +
                                 "pointer of its own");
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"the runtime walk for {typeName}.{methodName} failed: {Diagnostics.Describe(e)}");
            }

            return IntPtr.Zero;
        }

        /// <summary>The code pointer of a generic method's instantiation.</summary>
        private static IntPtr Inflated(IntPtr klass, IntPtr method, string typeName, string methodName)
        {
            IntPtr native = InflatedMethod(klass, method, typeName, methodName, IntPtr.Zero);
            return native == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)native;
        }

        /// <summary>The native <c>MethodInfo</c> of a generic method's instantiation.</summary>
        internal static IntPtr InflatedMethod(IntPtr klass, IntPtr method, string typeName, string methodName,
                                              IntPtr typeArgument)
        {
            try
            {
                IntPtr argument = typeArgument;
                if (argument == IntPtr.Zero)
                {
                    argument = klass;
                    for (IntPtr parent = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_parent(argument);
                         parent != IntPtr.Zero; )
                    {
                        argument = parent;
                        parent = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_parent(argument);
                    }
                }

                IntPtr argumentType = Il2CppInterop.Runtime.IL2CPP.il2cpp_type_get_object(
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_type(argument));
                if (argumentType == IntPtr.Zero)
                {
                    Diagnostics.Warn($"could not get a Type for the argument of {typeName}.{methodName}");
                    return IntPtr.Zero;
                }

                IntPtr typeArrayClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_class_get(
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(argumentType), 1);
                if (typeArrayClass == IntPtr.Zero) return IntPtr.Zero;

                IntPtr arguments = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(typeArrayClass, 1);
                if (arguments == IntPtr.Zero) return IntPtr.Zero;
                *(IntPtr*)(arguments + Offsets.Runtime.ArrayDataOffset) = argumentType;

                IntPtr reflection = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_object(method, klass);
                if (reflection == IntPtr.Zero)
                {
                    Diagnostics.Warn($"{typeName}.{methodName} could not be turned into a reflection method");
                    return IntPtr.Zero;
                }

                IntPtr reflectionClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(reflection);
                IntPtr make = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                    reflectionClass, "MakeGenericMethod", 1);
                if (make == IntPtr.Zero)
                {
                    Diagnostics.Warn("the reflection method has no MakeGenericMethod");
                    return IntPtr.Zero;
                }

                IntPtr* args = stackalloc IntPtr[1];
                args[0] = arguments;

                IntPtr raised = IntPtr.Zero;
                IntPtr inflated = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(
                    make, reflection, (void**)args, ref raised);
                if (raised != IntPtr.Zero)
                {
                    Diagnostics.Warn($"MakeGenericMethod raised for {typeName}.{methodName}: {Raised.Text(raised)}");
                    return IntPtr.Zero;
                }
                if (inflated == IntPtr.Zero) return IntPtr.Zero;

                IntPtr native = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_from_reflection(inflated);
                if (native == IntPtr.Zero)
                {
                    Diagnostics.Warn($"the inflated {typeName}.{methodName} could not be turned back " +
                                     "into a native method");
                    return IntPtr.Zero;
                }

#if DEBUG
                string argumentName = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(argument) ?? "?";
                IntPtr code = *(IntPtr*)native;
                Diagnostics.Info($"  {typeName}.{methodName}<{argumentName}>: code=0x{code.ToInt64():X} " +
                                 $"words={JacketFactory.Words(native, 4)}");
#endif
                return native;
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"inflating {typeName}.{methodName} failed: {Diagnostics.Describe(e)}");
                return IntPtr.Zero;
            }
        }

        /// <summary>A method's <c>MethodInfo</c>, reached through the runtime.</summary>
        internal static IntPtr MethodInfoByRuntime(string typeName, string methodName)
        {
            IntPtr klass = ClassOf(typeName);
            return klass == IntPtr.Zero ? IntPtr.Zero : MethodInfoIn(klass, typeName, methodName);
        }

        /// <summary>A method's <c>MethodInfo</c> for a type reached by image and namespace.</summary>
        internal static IntPtr MethodInfoByRuntimeInImage(string image, string namespaze, string typeName,
                                                          string methodName)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(image, namespaze, typeName);
            if (klass == IntPtr.Zero)
            {
                Diagnostics.Warn($"{namespaze}.{typeName} could not be found in image '{image}'");
                return IntPtr.Zero;
            }

            return MethodInfoIn(klass, namespaze + "." + typeName, methodName);
        }

        private static IntPtr MethodInfoIn(IntPtr klass, string typeName, string methodName)
        {
            try
            {
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);

                IntPtr iter = IntPtr.Zero;
                IntPtr method;
                while ((method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
                {
                    if (Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name_(method) != methodName) continue;

                    Diagnostics.Info($"{typeName}.{methodName} -> MethodInfo at 0x{method.ToInt64():X} (runtime walk)");
                    return method;
                }

                Diagnostics.Warn($"the runtime walk of {typeName} found no method named {methodName}");
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"the runtime walk for {typeName}.{methodName} failed: {Diagnostics.Describe(e)}");
            }

            return IntPtr.Zero;
        }

        /// <summary>A class pointer from the generated types.</summary>
        private static IntPtr ClassOf(string typeName)
        {
            IntPtr klass = FieldResolver.ClassPointer(typeName);
            if (klass == IntPtr.Zero)
                Diagnostics.Warn($"{typeName} could not be found in the generated types");

            return klass;
        }
    }
}
