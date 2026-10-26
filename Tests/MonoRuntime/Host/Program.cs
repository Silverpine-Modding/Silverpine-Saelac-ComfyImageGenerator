using System.Runtime.InteropServices;

if (args.Length != 2)
    throw new ArgumentException("Usage: MonoHost <game-directory> <checks-exe>");
string game = Path.GetFullPath(args[0]);
string checks = Path.GetFullPath(args[1]);
string managed = Path.Combine(game, "Silverpine_Data/Managed");
IntPtr library = NativeLibrary.Load(Path.Combine(game,
    "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll"));
T Export<T>(string name) where T : Delegate =>
    Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

Export<SetDirs>("mono_set_dirs")(managed, Path.Combine(game, "MonoBleedingEdge/etc"));
Export<SetPath>("mono_set_assemblies_path")(string.Join(";", new[]
{
    managed, Path.GetDirectoryName(checks)!, Path.Combine(game, "BepInEx/core")
}));
Export<ParseConfig>("mono_config_parse")(IntPtr.Zero);
IntPtr domain = Export<Initialize>("mono_jit_init_version")(
    "Comfy portrait upload checks", "v4.0.30319");
if (domain == IntPtr.Zero) throw new Exception("Could not initialize Unity Mono.");
IntPtr assembly = Export<OpenAssembly>("mono_domain_assembly_open")(domain, checks);
if (assembly == IntPtr.Zero) throw new Exception("Could not open verification assembly.");
string[] arguments = { checks, game };
IntPtr[] strings = arguments.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
IntPtr argv = Marshal.AllocHGlobal(IntPtr.Size * strings.Length);
Marshal.Copy(strings, 0, argv, strings.Length);
try
{
    Environment.ExitCode = Export<Execute>("mono_jit_exec")(
        domain, assembly, strings.Length, argv);
}
finally
{
    Marshal.FreeHGlobal(argv);
    foreach (IntPtr value in strings) Marshal.FreeCoTaskMem(value);
    // The OS tears down this disposable process. Do not tear down Mono while
    // Harmony's runtime-detour finalizers may still reference generated code.
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void SetDirs([MarshalAs(UnmanagedType.LPUTF8Str)] string assemblies,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string config);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void SetPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void ParseConfig(IntPtr filename);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate IntPtr Initialize([MarshalAs(UnmanagedType.LPUTF8Str)] string name,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string version);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate IntPtr OpenAssembly(IntPtr domain,
    [MarshalAs(UnmanagedType.LPUTF8Str)] string filename);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate int Execute(IntPtr domain, IntPtr assembly, int count, IntPtr args);
