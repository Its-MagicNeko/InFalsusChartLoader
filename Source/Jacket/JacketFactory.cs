using System;
using System.Collections.Generic;
using System.IO;

namespace InFalsusChartLoader
{
    /// <summary>Builds the materials a jacket or background is drawn with.</summary>
    internal static unsafe class JacketFactory
    {
        /// <summary>The texture formats to try.</summary>
        private static readonly int[] TextureFormats = { 4 /* RGBA32 */, 5 /* ARGB32 */, 3 /* RGB24 */ };

        /// <summary>`Color32` is four bytes.</summary>
        private const int Color32Size = 4;

        /// <summary>Shaders to try.</summary>
        private static readonly string[] ShaderNames = { "Unlit/Texture", "Sprites/Default", "UI/Default" };

        private static IntPtr _textureClass;
        private static IntPtr _materialClass;
        private static IntPtr _shaderClass;
        private static IntPtr _color32Class;

        private static bool _resolved;

        /// <summary>Finds the four types a jacket is built from.</summary>
        private static bool Resolve()
        {
            if (_resolved) return true;

            _textureClass = Require("Texture2D");
            _materialClass = Require("Material");
            _shaderClass = Require("Shader");
            _color32Class = Require("Color32");

            _resolved = _textureClass != IntPtr.Zero && _materialClass != IntPtr.Zero &&
                        _shaderClass != IntPtr.Zero && _color32Class != IntPtr.Zero;

#if DEBUG
            JacketProbe.Report();
#endif

            return _resolved;
        }

        /// <summary>Where the Unity types live.</summary>
        private const string Assembly = "UnityEngine.CoreModule.dll";
        private const string Namespace = "UnityEngine";

        /// <summary>The constructor wanted, as its parameter type names in order.</summary>
        private static readonly string[] ConstructorWanted =
            { "Int32", "Int32", "TextureFormat", "Boolean", "Boolean" };

        private static IntPtr Require(string typeName) => Require(typeName, Assembly, Namespace);

        private static IntPtr Require(string typeName, string assembly, string namespaze)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(assembly, namespaze, typeName);
            if (klass != IntPtr.Zero)
            {
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);

                Diagnostics.Info($"{typeName} found by assembly");
                return klass;
            }

            Diagnostics.Error($"{typeName}: no class to ask in this build");
            return IntPtr.Zero;
        }

        /// <summary>Builds a material showing the PNG at <paramref name="pngPath"/>.</summary>
        internal static IntPtr Build(string pngPath, out string reason) =>
            Build(pngPath, "jacket", out reason);

        /// <summary>The same build, for a jacket or a background.</summary>
        internal static IntPtr Build(string pngPath, string what, out string reason)
        {
            reason = null;

            string key = what + "\n" + pngPath;
            if (Built.TryGetValue(key, out IntPtr known)) return known;

            IntPtr material = BuildUncached(pngPath, what, out reason);
            if (material != IntPtr.Zero) Built[key] = material;
            return material;
        }

        /// <summary>Drops a cached build so the next request reads the file again.</summary>
        internal static IntPtr Forget(string pngPath, string what)
        {
            string key = what + "\n" + pngPath;
            if (!Built.TryGetValue(key, out IntPtr material)) return IntPtr.Zero;

            Built.Remove(key);
            return material;
        }

        /// <summary>Destroys a material this factory built, and the texture it owns; answers whether it did.</summary>
        internal static bool Release(IntPtr material)
        {
            if (material == IntPtr.Zero) return false;
            if (!Released.Add(material)) return false;

            IntPtr texture = MainTexture(material, out _);
            Destroy(material);
            if (texture != IntPtr.Zero) Destroy(texture);
            return true;
        }

        /// <summary>Destroys one material without touching the texture it shows.</summary>
        internal static void ReleaseMaterial(IntPtr material)
        {
            if (material != IntPtr.Zero) Destroy(material);
        }

        /// <summary>Destroys one texture.</summary>
        internal static void ReleaseTexture(IntPtr texture)
        {
            if (texture != IntPtr.Zero) Destroy(texture);
        }

        private static readonly HashSet<IntPtr> Released = new HashSet<IntPtr>();

        /// <summary>Destroys one Unity object.</summary>
        private static void Destroy(IntPtr instance)
        {
            try
            {
                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(Assembly, Namespace, "Object");
                if (klass == IntPtr.Zero) return;

                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);

                IntPtr method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, "Destroy", 1);
                if (method == IntPtr.Zero) return;

                IntPtr* args = stackalloc IntPtr[1];
                args[0] = instance;

                IntPtr raised = IntPtr.Zero;
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, IntPtr.Zero, (void**)args, ref raised);
                if (raised != IntPtr.Zero)
                    Diagnostics.Warn($"an old picture could not be destroyed: {Raised.Text(raised)}");
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"an old picture could not be destroyed: {Diagnostics.Describe(e)}");
            }
        }

        /// <summary>Every successful build, by kind and path.</summary>
        private static readonly Dictionary<string, IntPtr> Built =
            new Dictionary<string, IntPtr>(StringComparer.Ordinal);

        private static IntPtr BuildUncached(string pngPath, string what, out string reason)
        {
            reason = null;

            if (!Resolve())
            {
#if DEBUG
                reason = "the game's texture and material types could not be found";
#endif
                return IntPtr.Zero;
            }

            IntPtr texture = BuildTexture(pngPath, out reason);
            if (texture == IntPtr.Zero) return IntPtr.Zero;

            IntPtr material = BuildMaterial(texture, out reason);
            if (material == IntPtr.Zero) return IntPtr.Zero;

            Diagnostics.Info($"{what} '{Path.GetFileName(pngPath)}' ready");
            return material;
        }

        /// <summary>Builds a texture from a PNG without building a material around it.</summary>
        internal static IntPtr BuildTexture(string pngPath, out string reason)
        {
            reason = null;

            if (!Resolve())
            {
#if DEBUG
                reason = "the game's texture types could not be found";
#endif
                return IntPtr.Zero;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(pngPath);
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"{Path.GetFileName(pngPath)} could not be read: {Diagnostics.Describe(e)}";
#else
                _ = e;
#endif
                return IntPtr.Zero;
            }

            Png.Image image = Png.Decode(bytes, out reason);
            if (image == null) return IntPtr.Zero;

            IntPtr texture = BuildTexture(image, out reason);
            if (texture == IntPtr.Zero) return IntPtr.Zero;

            Diagnostics.Info($"texture '{Path.GetFileName(pngPath)}': {image.Width}x{image.Height}");
            return texture;
        }

        /// <summary>A material showing a texture this mod did not decode.</summary>
        internal static IntPtr BuildFromTexture(IntPtr texture, string what, out string reason)
        {
            reason = null;
            if (!Resolve())
            {
#if DEBUG
                reason = "the game's texture and material types could not be found";
#endif
                return IntPtr.Zero;
            }

            IntPtr material = BuildMaterial(texture, out reason);
            if (material != IntPtr.Zero) Diagnostics.Info($"{what} material built over its own texture");
            return material;
        }

        /// <summary>A <c>Texture2D</c> holding the decoded pixels.</summary>
        private static IntPtr BuildTexture(Png.Image image, out string reason)
        {
            reason = null;

            int pixels = image.Width * image.Height;

            Diagnostics.Info("jacket step: allocating the Color32 array");
            IntPtr colors = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(_color32Class, (ulong)pixels);
            if (colors == IntPtr.Zero)
            {
#if DEBUG
                reason = "the jacket's pixel array could not be allocated";
#endif
                return IntPtr.Zero;
            }

            byte* destination = (byte*)(colors + Offsets.Runtime.ArrayDataOffset);
            long rowBytes = (long)image.Width * Color32Size;

            fixed (byte* source = image.AsColor32)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    Buffer.MemoryCopy(source + (image.Height - 1 - y) * rowBytes,
                                      destination + (long)y * rowBytes,
                                      rowBytes, rowBytes);
                }
            }

            Diagnostics.Info("jacket step: creating the texture object");
            IntPtr texture = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(_textureClass);
            if (texture == IntPtr.Zero)
            {
#if DEBUG
                reason = "the jacket's texture could not be created";
#endif
                return IntPtr.Zero;
            }

            Diagnostics.Info("jacket step: looking up Texture2D..ctor(int,int,TextureFormat,bool,bool)");
            IntPtr constructor = ConstructorBySignature("Texture2D", ConstructorWanted);
            if (constructor == IntPtr.Zero)
            {
#if DEBUG
                reason = "Texture2D's constructor could not be found";
#endif
                return IntPtr.Zero;
            }

            int format = SupportedFormat();
            if (format == 0)
            {
#if DEBUG
                reason = "the device supports none of the texture formats a jacket can use";
#endif
                return IntPtr.Zero;
            }

            Diagnostics.Info($"jacket step: calling Texture2D..ctor with format {format}");

            int width = image.Width;
            int height = image.Height;
            int textureFormat = format;
            byte mipChain = 0;
            byte linear = 0;

            IntPtr* ctorArgs = stackalloc IntPtr[5];
            ctorArgs[0] = (IntPtr)(&width);
            ctorArgs[1] = (IntPtr)(&height);
            ctorArgs[2] = (IntPtr)(&textureFormat);
            ctorArgs[3] = (IntPtr)(&mipChain);
            ctorArgs[4] = (IntPtr)(&linear);
            if (!Invoke(constructor, texture, ctorArgs, 5, out reason)) return IntPtr.Zero;

            if (IsKnownToHaveNoNativeObject(texture))
            {
#if DEBUG
                reason = "Texture2D's constructor returned but the texture has no native object";
#endif
                return IntPtr.Zero;
            }

            int built = WidthOf(texture);
            if (built != image.Width)
            {
#if DEBUG
                reason = $"Texture2D's constructor returned but the texture reports {built} pixels " +
                         $"wide instead of {image.Width}";
#endif
                return IntPtr.Zero;
            }

            Diagnostics.Info("jacket step: looking up SetPixels32(Color32[])");
            IntPtr setPixels = Entry(_textureClass, "SetPixels32", 1);
            if (setPixels == IntPtr.Zero)
            {
#if DEBUG
                reason = "Texture2D.SetPixels32 could not be found";
#endif
                return IntPtr.Zero;
            }

            Diagnostics.Info("jacket step: calling SetPixels32");
            IntPtr* pixelsArgs = stackalloc IntPtr[1];
            pixelsArgs[0] = colors;
            if (!Invoke(setPixels, texture, pixelsArgs, 1, out reason)) return IntPtr.Zero;

            Diagnostics.Info("jacket step: looking up Apply()");
            IntPtr apply = Entry(_textureClass, "Apply", 0);
            if (apply == IntPtr.Zero)
            {
#if DEBUG
                reason = "Texture2D.Apply could not be found";
#endif
                return IntPtr.Zero;
            }

            Diagnostics.Info("jacket step: calling Apply");
            if (!Invoke(apply, texture, null, 0, out reason)) return IntPtr.Zero;

            return texture;
        }

        /// <summary>A <c>Material</c> whose main texture is <paramref name="texture"/>.</summary>
        private static IntPtr BuildMaterial(IntPtr texture, out string reason)
        {
            reason = null;

            IntPtr shader = IntPtr.Zero;
            IntPtr find = Entry(_shaderClass, "Find", 1);
            IntPtr* findArgs = stackalloc IntPtr[1];

            if (find != IntPtr.Zero)
            {
                foreach (string name in ShaderNames)
                {
                    findArgs[0] = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(name);
                    if (findArgs[0] == IntPtr.Zero) continue;

                    IntPtr raised = IntPtr.Zero;
                    IntPtr found = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(
                        find, IntPtr.Zero, (void**)findArgs, ref raised);

                    if (raised == IntPtr.Zero && found != IntPtr.Zero && Memory.LooksLikeObject(found))
                    {
                        shader = found;
                        break;
                    }
                }
            }

            if (shader == IntPtr.Zero) Diagnostics.Warn("no shader could be found for the jacket material");

            Diagnostics.Info("jacket step: creating the material object");
            IntPtr material = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(_materialClass);
            if (material == IntPtr.Zero)
            {
#if DEBUG
                reason = "the jacket's material could not be created";
#endif
                return IntPtr.Zero;
            }

            Diagnostics.Info("jacket step: looking up Material..ctor(Shader)");
            IntPtr constructor = Entry(_materialClass, ".ctor", 1);
            if (constructor == IntPtr.Zero)
            {
#if DEBUG
                reason = "Material's constructor could not be found";
#endif
                return IntPtr.Zero;
            }

            Diagnostics.Info("jacket step: calling Material..ctor");
            IntPtr* ctorArgs = stackalloc IntPtr[1];
            ctorArgs[0] = shader;
            if (!Invoke(constructor, material, ctorArgs, 1, out reason)) return IntPtr.Zero;

            if (IsKnownToHaveNoNativeObject(material))
            {
#if DEBUG
                reason = "Material's constructor returned but the material has no native object";
#endif
                return IntPtr.Zero;
            }

            Diagnostics.Info("jacket step: looking up set_mainTexture(Texture)");
            IntPtr setTexture = Entry(_materialClass, "set_mainTexture", 1);
            if (setTexture == IntPtr.Zero)
            {
#if DEBUG
                reason = "Material.set_mainTexture could not be found";
#endif
                return IntPtr.Zero;
            }

            Diagnostics.Info("jacket step: calling set_mainTexture");
            IntPtr* textureArgs = stackalloc IntPtr[1];
            textureArgs[0] = texture;
            if (!Invoke(setTexture, material, textureArgs, 1, out reason)) return IntPtr.Zero;

            return material;
        }

        /// <summary>A clone of a material the game loaded.</summary>
        internal static unsafe IntPtr CloneFrom(IntPtr source, out string reason)
        {
            reason = null;

            if (!Resolve())
            {
                reason = "the game's material types could not be found";
                return IntPtr.Zero;
            }

            if (!Memory.LooksLikeObject(source))
            {
                reason = "there is no material to clone";
                return IntPtr.Zero;
            }

            IntPtr material = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(_materialClass);
            if (material == IntPtr.Zero)
            {
                reason = "the clone material could not be created";
                return IntPtr.Zero;
            }

            IntPtr constructor = ConstructorBySignature("Material", MaterialCopyWanted);
            if (constructor == IntPtr.Zero)
            {
                reason = "Material's copy constructor could not be found";
                return IntPtr.Zero;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = source;
            if (!Invoke(constructor, material, args, 1, out reason)) return IntPtr.Zero;

            if (IsKnownToHaveNoNativeObject(material))
            {
                reason = "the clone returned but has no native object";
                return IntPtr.Zero;
            }

            return material;
        }

        /// <summary>The constructor a clone wants.</summary>
        private static readonly string[] MaterialCopyWanted = { "Material" };

        /// <summary>A material's main texture.</summary>
        internal static IntPtr MainTexture(IntPtr material, out string reason)
        {
            reason = null;

            IntPtr getter = Entry(_materialClass, "get_mainTexture", 0);
            if (getter == IntPtr.Zero)
            {
                reason = "Material.get_mainTexture could not be found";
                return IntPtr.Zero;
            }

            IntPtr raised = IntPtr.Zero;
            IntPtr texture = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(getter, material, null, ref raised);
            if (raised != IntPtr.Zero || !Memory.LooksLikeObject(texture))
            {
                reason = "the material's main texture could not be read";
                return IntPtr.Zero;
            }

            return texture;
        }

        /// <summary>Puts a picture on a material.</summary>
        internal static unsafe bool SetMainTexture(IntPtr material, IntPtr texture, out string reason)
        {
            reason = null;

            if (!Resolve())
            {
                reason = "the game's material types could not be found";
                return false;
            }

            IntPtr setter = Entry(_materialClass, "set_mainTexture", 1);
            if (setter == IntPtr.Zero)
            {
                reason = "Material.set_mainTexture could not be found";
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = texture;
            return Invoke(setter, material, args, 1, out reason);
        }

        /// <summary>Builds a picture with the source slot's shape in its own alpha channel.</summary>
        internal static IntPtr BuildMaskedTexture(string path, IntPtr source, out string reason)
        {
            reason = null;
            if (!Resolve()) { reason = "the game's texture types could not be found"; return IntPtr.Zero; }
            IntPtr mask = MainTexture(source, out reason);
            if (mask == IntPtr.Zero) return IntPtr.Zero;

            IntPtr target = IntPtr.Zero;
            IntPtr readable = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;
            bool activeChanged = false;
            IntPtr* restore = stackalloc IntPtr[1];
            IntPtr renderClass = Require("RenderTexture");
            IntPtr graphicsClass = Require("Graphics");
            if (renderClass == IntPtr.Zero || graphicsClass == IntPtr.Zero) return IntPtr.Zero;

            IntPtr activeGet = Entry(renderClass, "get_active", 0);
            IntPtr activeSet = Entry(renderClass, "set_active", 1);
            IntPtr blit = Entry(graphicsClass, "Blit", 4);
            IntPtr readPixels = Entry(_textureClass, "ReadPixels", 4);
            IntPtr getPixels = Entry(_textureClass, "GetPixels32", 0);
            if (activeGet == IntPtr.Zero || activeSet == IntPtr.Zero || blit == IntPtr.Zero ||
                readPixels == IntPtr.Zero || getPixels == IntPtr.Zero)
            {
                reason = "the pack picture's alpha readback methods could not be found";
                return IntPtr.Zero;
            }

            try
            {
                Png.Image image = Png.Decode(File.ReadAllBytes(path), out reason);
                if (image == null) return IntPtr.Zero;

                IntPtr raised = IntPtr.Zero;
                previous = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(activeGet, IntPtr.Zero, null, ref raised);
                if (raised != IntPtr.Zero) { reason = Raised.Text(raised); return IntPtr.Zero; }

                target = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(renderClass);
                if (target == IntPtr.Zero) { reason = "the alpha render target could not be allocated"; return IntPtr.Zero; }
                int width = image.Width, height = image.Height, depth = 0;
                IntPtr* args = stackalloc IntPtr[4];
                IntPtr constructor = Entry(renderClass, ".ctor", 3);
                if (constructor == IntPtr.Zero) { reason = "the alpha render target constructor could not be found"; return IntPtr.Zero; }
                args[0] = (IntPtr)(&width);
                args[1] = (IntPtr)(&height);
                args[2] = (IntPtr)(&depth);
                if (!Invoke(constructor, target, args, 3, out reason)) return IntPtr.Zero;

                float* scale = stackalloc float[2];
                scale[0] = scale[1] = 1f;
                float* offset = stackalloc float[2];
                offset[0] = offset[1] = 0f;
                args[0] = mask;
                args[1] = target;
                args[2] = (IntPtr)scale;
                args[3] = (IntPtr)offset;
                if (!Invoke(blit, IntPtr.Zero, args, 4, out reason)) return IntPtr.Zero;

                readable = BuildTexture(new Png.Image
                {
                    Width = width, Height = height, Rgba = new byte[image.Rgba.Length],
                }, out reason);
                if (readable == IntPtr.Zero) return IntPtr.Zero;

                args[0] = target;
                if (!Invoke(activeSet, IntPtr.Zero, args, 1, out reason)) return IntPtr.Zero;
                activeChanged = true;

                float* rect = stackalloc float[4];
                rect[0] = rect[1] = 0f;
                rect[2] = width;
                rect[3] = height;
                int zero = 0;
                byte recalculate = 0;
                args[0] = (IntPtr)rect;
                args[1] = args[2] = (IntPtr)(&zero);
                args[3] = (IntPtr)(&recalculate);
                if (!Invoke(readPixels, readable, args, 4, out reason)) return IntPtr.Zero;

                raised = IntPtr.Zero;
                IntPtr pixels = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(getPixels, readable, null, ref raised);
                if (raised != IntPtr.Zero || !Memory.LooksLikeObject(pixels) ||
                    Memory.I32(pixels + Offsets.Runtime.ArrayLength) != width * height)
                {
                    reason = "the pack picture's alpha pixels could not be read";
                    return IntPtr.Zero;
                }

                byte* rgba = (byte*)(pixels + Offsets.Runtime.ArrayDataOffset);
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int at = (y * width + x) * Color32Size + 3;
                        int shape = ((height - 1 - y) * width + x) * Color32Size + 3;
                        image.Rgba[at] = (byte)((image.Rgba[at] * rgba[shape] + 127) / 255);
                    }

                return BuildTexture(image, out reason);
            }
            catch (Exception e)
            {
                reason = "the pack picture's alpha could not be baked: " + Diagnostics.Describe(e);
                return IntPtr.Zero;
            }
            finally
            {
                if (activeChanged)
                {
                    restore[0] = previous;
                    IntPtr raised = IntPtr.Zero;
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(activeSet, IntPtr.Zero, (void**)restore, ref raised);
                    if (raised != IntPtr.Zero)
                        Diagnostics.Error("the render target could not be restored: " + Raised.Text(raised));
                }
                ReleaseTexture(readable);
                ReleaseTexture(target);
            }
        }

        // ---------------------------------------------------------------- runtime plumbing

        /// <summary>The native entry point of a method, or zero.</summary>
        private static IntPtr Entry(IntPtr klass, string name, int parameters) =>
            Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, name, parameters);

        /// <summary>A constructor's <c>MethodInfo</c>, picked by its parameter list.</summary>
        private static IntPtr ConstructorBySignature(string typeName, string[] wanted)
        {
            if (Constructors.TryGetValue(typeName, out IntPtr known)) return known;

            IntPtr found = FindConstructorBySignature(typeName, wanted);
            Constructors[typeName] = found;
            return found;
        }

        /// <summary>Constructors already looked for, by type name.</summary>
        private static readonly Dictionary<string, IntPtr> Constructors =
            new Dictionary<string, IntPtr>(StringComparer.Ordinal);

        private static IntPtr FindConstructorBySignature(string typeName, string[] wanted)
        {
            try
            {
                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(Assembly, Namespace, typeName);
                if (klass == IntPtr.Zero)
                {
                    Diagnostics.Warn($"{typeName}'s class could not be reached for its constructors");
                    return IntPtr.Zero;
                }

                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);

                IntPtr iter = IntPtr.Zero;
                IntPtr method;
                int walked = 0;
                while ((method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
                {
                    walked++;
                    if (walked > MethodsWalkLimit)
                    {
                        Diagnostics.Warn($"{typeName}'s method walk passed {MethodsWalkLimit} entries; stopping");
                        return IntPtr.Zero;
                    }

                    string name = Name(method);
#if DEBUG
                    if (walked <= MethodsToLog) Diagnostics.Info($"  {typeName} method {walked}: '{(name ?? "(null)")}'");
#endif

                    if (name != ".ctor") continue;

                    int count = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(method);
                    if (count != wanted.Length) continue;

                    if (!Matches(method, wanted)) continue;

                    string owner = DeclaringTypeName(method);
                    if (owner != typeName)
                    {
                        Diagnostics.Warn($"a {typeName} constructor was found but reports {owner ?? "(null)"} " +
                                         $"as its class; it is not one that can be called");
                        return IntPtr.Zero;
                    }

                    Diagnostics.Info($"{typeName}({string.Join(", ", wanted)}) found among the class's methods");

#if DEBUG
                    Diagnostics.Info($"  the constructor's MethodInfo is at 0x{method.ToInt64():X}");
                    Diagnostics.Info($"  class=0x{klass.ToInt64():X}  code={Words(method, 12)}");
#endif

                    return method;
                }

                Diagnostics.Info($"{typeName}: walked {walked} method(s)");

#if DEBUG
                int seen = 0;
                iter = IntPtr.Zero;
                while ((method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
                {
                    if (Name(method) != ".ctor") continue;

                    var parameters = new System.Collections.Generic.List<string>();
                    int arity = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(method);
                    for (int i = 0; i < arity; i++)
                    {
                        parameters.Add(TypeName(Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param(method, (uint)i)));
                    }

                    Diagnostics.Warn($"  {typeName}.ctor({string.Join(", ", parameters)})");
                    seen++;
                }
                Diagnostics.Warn($"the class offers {seen} constructor(s), none of them " +
                                 $"({string.Join(", ", ConstructorWanted)})");
#endif

                return IntPtr.Zero;
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"{typeName}'s constructors could not be read: {Diagnostics.Describe(e)}");
                return IntPtr.Zero;
            }
        }

        /// <summary>Whether a method's parameters are the wanted type names, in order.</summary>
        private static bool Matches(IntPtr method, string[] wanted)
        {
            for (int i = 0; i < wanted.Length; i++)
            {
                IntPtr type = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param(method, (uint)i);
                if (type == IntPtr.Zero) return false;

                if (TypeName(type) != wanted[i]) return false;
            }
            return true;
        }

        /// <summary>A method's name, or null.</summary>
        private static string Name(IntPtr method) =>
            Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name_(method);

        /// <summary>The first supported texture format, or zero.</summary>
        private static int SupportedFormat()
        {
            if (_format != int.MinValue) return _format;

            _format = 0;
            foreach (int candidate in TextureFormats)
            {
                if (!SupportsFormat(candidate)) continue;
                _format = candidate;
                Diagnostics.Info($"the device supports texture format {candidate}");
                break;
            }

            if (_format == 0)
                Diagnostics.Warn("the device reports no support for any texture format a jacket can use");
            return _format;
        }

        private static int _format = int.MinValue;

        /// <summary>Whether the device supports a <c>TextureFormat</c>.</summary>
        private static bool SupportsFormat(int format)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(Assembly, Namespace, "SystemInfo");
            if (klass == IntPtr.Zero)
            {
                Diagnostics.Warn("SystemInfo could not be reached, so the texture format was never asked about");
                return false;
            }

            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);

            IntPtr method = Entry(klass, "SupportsTextureFormat", 1);
            if (method == IntPtr.Zero)
            {
                Diagnostics.Warn("SystemInfo.SupportsTextureFormat could not be found, so the texture " +
                                 "format was never asked about");
                return false;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&format);

            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, IntPtr.Zero, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
            {
                Diagnostics.Warn($"asking about texture format {format} raised: {Raised.Text(raised)}");
                return false;
            }
            if (boxed == IntPtr.Zero)
            {
                Diagnostics.Warn($"asking about texture format {format} answered with nothing");
                return false;
            }

            return *(byte*)(boxed + Offsets.Runtime.BoxedData) != 0;
        }

#if DEBUG
        /// <summary>The first words at an address, as hex.</summary>
        internal static string Words(IntPtr address, int count)
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                IntPtr at = address + i * IntPtr.Size;
                if (i > 0) text.Append(' ');
                text.Append(Memory.Ptr(at).ToInt64().ToString("X"));
            }
            return text.ToString();
        }
#endif

        /// <summary>The class a method says it belongs to, or null.</summary>
        private static string DeclaringTypeName(IntPtr method)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_declaring_type(method);
            if (klass == IntPtr.Zero) return null;
            return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(klass);
        }

        /// <summary>A type's name, without namespace or generic arguments.</summary>
        private static string TypeName(IntPtr type)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_from_type(type);
            if (klass == IntPtr.Zero) return null;
            return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(klass);
        }

        /// <summary>Whether an object is known to have no native object behind it.</summary>
        private static bool IsKnownToHaveNoNativeObject(IntPtr instance)
        {
            if (_cachedPtrOffset == int.MinValue) _cachedPtrOffset = CachedPointerOffset();

            if (_cachedPtrOffset <= 0) return false;

            return Memory.Ptr(instance + _cachedPtrOffset) == IntPtr.Zero;
        }

        /// <summary>Whether a Unity object still has a native object behind it.</summary>
        internal static bool IsAlive(IntPtr instance) =>
            Memory.LooksLikeObject(instance) && !IsKnownToHaveNoNativeObject(instance);

        /// <summary>Where <c>UnityEngine.Object.m_CachedPtr</c> sits, or zero.</summary>
        private static int CachedPointerOffset()
        {
            try
            {
                IntPtr klass = Il2CppInterop.Runtime.IL2CPP.GetIl2CppClass(Assembly, Namespace, "Object");
                if (klass == IntPtr.Zero)
                {
                    Diagnostics.Warn("UnityEngine.Object could not be reached, so whether a built object " +
                                     "has a native one will not be checked");
                    return 0;
                }

                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);

                IntPtr field = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, "m_CachedPtr");
                if (field == IntPtr.Zero)
                {
                    Diagnostics.Warn("UnityEngine.Object has no m_CachedPtr, so whether a built object " +
                                     "has a native one will not be checked");
                    return 0;
                }

                int offset = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field);
                if (offset <= 0)
                {
                    Diagnostics.Warn($"m_CachedPtr is at 0x{offset:X}, which cannot be an offset; whether a " +
                                     "built object has a native one will not be checked");
                    return 0;
                }

                Diagnostics.Info($"m_CachedPtr is at 0x{offset:X}");
                return offset;
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"Object.m_CachedPtr could not be resolved: {Diagnostics.Describe(e)}");
                return 0;
            }
        }

        private static int _cachedPtrOffset = int.MinValue;

        /// <summary>Calls a method through <c>il2cpp_runtime_invoke</c>.</summary>
        private static bool Invoke(IntPtr method, IntPtr instance, IntPtr* args, int count, out string reason)
        {
            reason = null;

            for (int i = 0; i < count; i++)
            {
                if (args[i] != IntPtr.Zero) continue;
#if DEBUG
                reason = $"argument {i} could not be built, so the call was not made";
#endif
                return false;
            }

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, instance, (void**)args, ref raised);

            if (raised == IntPtr.Zero) return true;

#if DEBUG
            reason = $"a call into the game's rendering types raised: {Raised.Text(raised)}";
#endif
            return false;
        }

        /// <summary>How wide a <c>Texture2D</c> says it is, or -1.</summary>
        private static int WidthOf(IntPtr texture)
        {
            IntPtr getter = Entry(_textureClass, "get_width", 0);
            if (getter == IntPtr.Zero)
            {
                Diagnostics.Warn("Texture2D.get_width could not be found, so the built texture cannot be checked");
                return -1;
            }

            IntPtr raised = IntPtr.Zero;
            IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(getter, texture, null, ref raised);
            if (raised != IntPtr.Zero || boxed == IntPtr.Zero) return -1;

            return *(int*)(boxed + Offsets.Runtime.BoxedData);
        }

        /// <summary>How many methods a class may report before the walk is treated as broken.</summary>
        private const int MethodsWalkLimit = 4096;

#if DEBUG
        /// <summary>How many entries of a method walk are logged by name.</summary>
        private const int MethodsToLog = 30;
#endif
    }
}
