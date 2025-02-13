using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime;
using CommandLine;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
#if !DEBUG
using Cpp2IL.Core.Exceptions;
#endif
using LibCpp2IL.Wasm;
using AssetRipper.Primitives;
using Cpp2IL.Core.Extensions;
using LibCpp2IL;
using LibCpp2IL.Metadata;

#if NET472
using LibCpp2IL;
#endif

namespace Cpp2IL;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal class Program
{
    private static readonly List<string> PathsToDeleteOnExit = [];

    public static readonly string Cpp2IlVersionString = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    private static void ResolvePathsFromCommandLine(string? gamePath, string? inputExeName, ref Cpp2IlRuntimeArgs args)
    {
        if (string.IsNullOrEmpty(gamePath))
            throw new SoftException("No force options provided, and no game path was provided either. Please provide a game path or use the --force- options.");
        
        //Somehow the above doesn't tell .net that gamePath can't be null on net472, so we do this stupid thing to avoid nullable warnings
#if NET472
        gamePath = gamePath!;
#endif

        Logger.VerboseNewline("Beginning path resolution...");

        if (Directory.Exists(gamePath) && File.Exists(Path.Combine(gamePath, "Contents/Frameworks/GameAssembly.dylib")))
            HandleMacOSGamePath(gamePath, inputExeName, ref args);
        else if (Directory.Exists(gamePath) && File.Exists(Path.Combine(gamePath, "GameAssembly.so")))
            HandleLinuxGamePath(gamePath, inputExeName, ref args);
        else if (Directory.Exists(gamePath))
            HandleWindowsGamePath(gamePath, inputExeName, ref args);
        else if (File.Exists(gamePath) && Path.GetExtension(gamePath).ToLowerInvariant() == ".apk")
            HandleSingleApk(gamePath, ref args);
        else if (File.Exists(gamePath) && Path.GetExtension(gamePath).ToLowerInvariant() is ".xapk" or ".apkm")
            HandleXapk(gamePath, ref args);
        else if (File.Exists(gamePath) && Path.GetExtension(gamePath).ToLowerInvariant() is ".ipa" or ".tipa")
            HandleIpa(gamePath, ref args);
        else
        {
            if (!Cpp2IlPluginManager.TryProcessGamePath(gamePath, ref args))
                throw new SoftException($"Could not find a valid unity game at {gamePath}");
        }
    }

    private static void HandleMacOSGamePath(string gamePath, string? inputExeName, ref Cpp2IlRuntimeArgs args)
    {
        //macOS game.
        args.PathToAssembly = Path.Combine(gamePath, "Contents", "Frameworks", "GameAssembly.dylib");
        var exeName = Path.GetFileName(Directory.GetFiles(Path.Combine(gamePath, "Contents", "MacOS"))
            .FirstOrDefault(f => MiscUtils.BlacklistedExecutableFilenames.Any(f.EndsWith)));

        exeName = inputExeName ?? exeName;

        Logger.VerboseNewline($"Trying HandleMacOSGamePath as provided bundle contains GameAssembly.dylib, potential GA is {args.PathToAssembly} and executable {exeName}");

        if (exeName == null)
            throw new SoftException("Failed to locate any executable in the provided game directory. Make sure the path is correct, and if you *really* know what you're doing (and know it's not supported), use the force options, documented if you provide --help.");

        var unityPlayerPath = Path.Combine(gamePath, "Contents", "MacOS", exeName);
        var gameDataPath = Path.Combine(gamePath, "Contents", "Resources", "Data");
        args.PathToMetadata = Path.Combine(gameDataPath, "il2cpp_data", "Metadata", "global-metadata.dat");

        if (!File.Exists(args.PathToAssembly) || !File.Exists(unityPlayerPath) || !File.Exists(args.PathToMetadata))
            throw new SoftException("Invalid game-path or exe-name specified. Failed to find one of the following:\n" +
                                    $"\t{args.PathToAssembly}\n" +
                                    $"\t{unityPlayerPath}\n" +
                                    $"\t{args.PathToMetadata}\n");

        Logger.VerboseNewline($"Found probable macOS game at path: {gamePath}. Attempting to get unity version...");

        var uv = Cpp2IlApi.DetermineUnityVersion(unityPlayerPath, gameDataPath);
        Logger.VerboseNewline($"First-attempt unity version detection gave: {uv}");

        if (uv == default)
        {
            Logger.Warn("Could not determine unity version, probably due to not running on windows and not having any assets files to determine it from. Enter unity version, if known, in the format of (xxxx.x.x), else nothing to fail: ");
            var userInputUv = Console.ReadLine();

            if (!string.IsNullOrEmpty(userInputUv))
                uv = UnityVersion.Parse(userInputUv);

            if (uv == default)
                throw new SoftException("Failed to determine unity version. If you're not running on windows, I need a globalgamemanagers file or a data.unity3d file, or you need to use the force options.");
        }

        args.UnityVersion = uv;

        if (args.UnityVersion.Major < 4)
        {
            Logger.WarnNewline($"Fail once: Unity version of provided executable is {args.UnityVersion}. This is probably not the correct version. Retrying with alternative method...");

            var readUnityVersionFrom = Path.Combine(gameDataPath, "globalgamemanagers");
            if (File.Exists(readUnityVersionFrom))
                args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(File.ReadAllBytes(readUnityVersionFrom));
            else
            {
                readUnityVersionFrom = Path.Combine(gameDataPath, "data.unity3d");
                using var stream = File.OpenRead(readUnityVersionFrom);

                args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(stream);
            }
        }

        Logger.InfoNewline($"Determined game's unity version to be {args.UnityVersion}");

        if (args.UnityVersion.Major <= 4)
            throw new SoftException($"Unable to determine a valid unity version (got {args.UnityVersion})");

        args.Valid = true;
    }

    private static void HandleLinuxGamePath(string gamePath, string? inputExeName, ref Cpp2IlRuntimeArgs args)
    {
        //Linux game.
        args.PathToAssembly = Path.Combine(gamePath, "GameAssembly.so");
        var exeName = Path.GetFileName(Directory.GetFiles(gamePath)
            .FirstOrDefault(f =>
                (f.EndsWith(".x86_64") || f.EndsWith(".x86")) &&
                !MiscUtils.BlacklistedExecutableFilenames.Any(f.EndsWith)));

        exeName = inputExeName ?? exeName;

        Logger.VerboseNewline($"Trying HandleLinuxGamePath as provided directory contains a GameAssembly.so, potential GA is {args.PathToAssembly} and executable {exeName}");

        if (exeName == null)
            throw new SoftException("Failed to locate any executable in the provided game directory. Make sure the path is correct, and if you *really* know what you're doing (and know it's not supported), use the force options, documented if you provide --help.");

        var exeNameNoExt = exeName.Replace(".x86_64", "").Replace(".x86", "");

        var unityPlayerPath = Path.Combine(gamePath, exeName);
        args.PathToMetadata = Path.Combine(gamePath, $"{exeNameNoExt}_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

        if (!File.Exists(args.PathToAssembly) || !File.Exists(unityPlayerPath) || !File.Exists(args.PathToMetadata))
            throw new SoftException("Invalid game-path or exe-name specified. Failed to find one of the following:\n" +
                                    $"\t{args.PathToAssembly}\n" +
                                    $"\t{unityPlayerPath}\n" +
                                    $"\t{args.PathToMetadata}\n");

        Logger.VerboseNewline($"Found probable linux game at path: {gamePath}. Attempting to get unity version...");
        var gameDataPath = Path.Combine(gamePath, $"{exeNameNoExt}_Data");
        var uv = Cpp2IlApi.DetermineUnityVersion(unityPlayerPath, gameDataPath);
        Logger.VerboseNewline($"First-attempt unity version detection gave: {uv}");

        if (uv == default)
        {
            Logger.Warn("Could not determine unity version, probably due to not running on windows and not having any assets files to determine it from. Enter unity version, if known, in the format of (xxxx.x.x), else nothing to fail: ");
            var userInputUv = Console.ReadLine();

            if (!string.IsNullOrEmpty(userInputUv))
                uv = UnityVersion.Parse(userInputUv);

            if (uv == default)
                throw new SoftException("Failed to determine unity version. If you're not running on windows, I need a globalgamemanagers file or a data.unity3d file, or you need to use the force options.");
        }

        args.UnityVersion = uv;

        if (args.UnityVersion.Major < 4)
        {
            Logger.WarnNewline($"Fail once: Unity version of provided executable is {args.UnityVersion}. This is probably not the correct version. Retrying with alternative method...");

            var readUnityVersionFrom = Path.Combine(gameDataPath, "globalgamemanagers");
            if (File.Exists(readUnityVersionFrom))
                args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(File.ReadAllBytes(readUnityVersionFrom));
            else
            {
                readUnityVersionFrom = Path.Combine(gameDataPath, "data.unity3d");
                using var stream = File.OpenRead(readUnityVersionFrom);

                args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(stream);
            }
        }

        Logger.InfoNewline($"Determined game's unity version to be {args.UnityVersion}");

        if (args.UnityVersion.Major <= 4)
            throw new SoftException($"Unable to determine a valid unity version (got {args.UnityVersion})");

        args.Valid = true;
    }

    private static void HandleWindowsGamePath(string gamePath, string? inputExeName, ref Cpp2IlRuntimeArgs args)
    {
        //Windows game.
        args.PathToAssembly = Path.Combine(gamePath, "GameAssembly.dll");
        var exeName = Path.GetFileNameWithoutExtension(Directory.GetFiles(gamePath)
            .FirstOrDefault(f => f.EndsWith(".exe") && !MiscUtils.BlacklistedExecutableFilenames.Any(f.EndsWith)));

        exeName = inputExeName ?? exeName;

        Logger.VerboseNewline($"Trying HandleWindowsGamePath as provided path is a directory with no GameAssembly.so, potential GA is {args.PathToAssembly} and executable {exeName}");

        if (exeName == null)
            throw new SoftException("Failed to locate any executable in the provided game directory. Make sure the path is correct, and if you *really* know what you're doing (and know it's not supported), use the force options, documented if you provide --help.");

        var unityPlayerPath = Path.Combine(gamePath, $"{exeName}.exe");
        args.PathToMetadata = Path.Combine(gamePath, $"{exeName}_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

        if (!File.Exists(args.PathToAssembly) || !File.Exists(unityPlayerPath) || !File.Exists(args.PathToMetadata))
            throw new SoftException("Invalid game-path or exe-name specified. Failed to find one of the following:\n" +
                                    $"\t{args.PathToAssembly}\n" +
                                    $"\t{unityPlayerPath}\n" +
                                    $"\t{args.PathToMetadata}\n");

        Logger.VerboseNewline($"Found probable windows game at path: {gamePath}. Attempting to get unity version...");
        var gameDataPath = Path.Combine(gamePath, $"{exeName}_Data");
        var uv = Cpp2IlApi.DetermineUnityVersion(unityPlayerPath, gameDataPath);
        Logger.VerboseNewline($"First-attempt unity version detection gave: {uv}");

        if (uv == default)
        {
            Logger.Warn("Could not determine unity version, probably due to not running on windows and not having any assets files to determine it from. Enter unity version, if known, in the format of (xxxx.x.x), else nothing to fail: ");
            var userInputUv = Console.ReadLine();

            if (!string.IsNullOrEmpty(userInputUv))
                uv = UnityVersion.Parse(userInputUv);

            if (uv == default)
                throw new SoftException("Failed to determine unity version. If you're not running on windows, I need a globalgamemanagers file or a data.unity3d file, or you need to use the force options.");
        }

        args.UnityVersion = uv;

        if (args.UnityVersion.Major < 4)
        {
            Logger.WarnNewline($"Fail once: Unity version of provided executable is {args.UnityVersion}. This is probably not the correct version. Retrying with alternative method...");

            var readUnityVersionFrom = Path.Combine(gameDataPath, "globalgamemanagers");
            if (File.Exists(readUnityVersionFrom))
                args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(File.ReadAllBytes(readUnityVersionFrom));
            else
            {
                readUnityVersionFrom = Path.Combine(gameDataPath, "data.unity3d");
                using var stream = File.OpenRead(readUnityVersionFrom);

                args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(stream);
            }
        }

        Logger.InfoNewline($"Determined game's unity version to be {args.UnityVersion}");

        if (args.UnityVersion.Major <= 4)
            throw new SoftException($"Unable to determine a valid unity version (got {args.UnityVersion})");

        args.Valid = true;
    }

    private static void HandleSingleApk(string gamePath, ref Cpp2IlRuntimeArgs args)
    {
        //APK
        //Metadata: assets/bin/Data/Managed/Metadata
        //Binary: lib/(armeabi-v7a)|(arm64-v8a)/libil2cpp.so

        Logger.VerboseNewline("Trying HandleSingleApk as provided path is an apk file");

        Logger.InfoNewline($"Attempting to extract required files from APK {gamePath}", "APK");

        using var stream = File.OpenRead(gamePath);
        using var zipArchive = new ZipArchive(stream);

        var globalMetadata = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("assets/bin/Data/Managed/Metadata/global-metadata.dat"));
        var binary = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("lib/x86_64/libil2cpp.so"));
        binary ??= zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("lib/x86/libil2cpp.so"));
        binary ??= zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("lib/arm64-v8a/libil2cpp.so"));
        binary ??= zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("lib/armeabi-v7a/libil2cpp.so"));

        var globalgamemanagers = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("assets/bin/Data/globalgamemanagers"));
        var dataUnity3d = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("assets/bin/Data/data.unity3d"));

        if (binary == null)
            throw new SoftException("Could not find libil2cpp.so inside the apk.");
        if (globalMetadata == null)
            throw new SoftException("Could not find global-metadata.dat inside the apk");
        if (globalgamemanagers == null && dataUnity3d == null)
            throw new SoftException("Could not find globalgamemanagers or data.unity3d inside the apk");

        var tempFileBinary = Path.GetTempFileName();
        var tempFileMeta = Path.GetTempFileName();

        PathsToDeleteOnExit.Add(tempFileBinary);
        PathsToDeleteOnExit.Add(tempFileMeta);

        Logger.InfoNewline($"Extracting APK/{binary.FullName} to {tempFileBinary}", "APK");
        binary.ExtractToFile(tempFileBinary, true);
        Logger.InfoNewline($"Extracting APK/{globalMetadata.FullName} to {tempFileMeta}", "APK");
        globalMetadata.ExtractToFile(tempFileMeta, true);

        args.PathToAssembly = tempFileBinary;
        args.PathToMetadata = tempFileMeta;

        if (globalgamemanagers != null)
        {
            Logger.InfoNewline("Reading globalgamemanagers to determine unity version...", "APK");
            var ggmBytes = new byte[0x40];
            using var ggmStream = globalgamemanagers.Open();

            // ReSharper disable once MustUseReturnValue
            ggmStream.Read(ggmBytes, 0, 0x40);

            args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(ggmBytes);
        }
        else
        {
            Logger.InfoNewline("Reading data.unity3d to determine unity version...", "APK");
            using var du3dStream = dataUnity3d!.Open();

            args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(du3dStream);
        }

        Logger.InfoNewline($"Determined game's unity version to be {args.UnityVersion}", "APK");

        args.Valid = true;
    }

    private static void HandleXapk(string gamePath, ref Cpp2IlRuntimeArgs args)
    {
        //XAPK file
        //Contains two APKs - one starting with `config.` and one with the package name
        //The config one is architecture-specific and so contains the binary
        //The other contains the metadata

        Logger.VerboseNewline("Trying HandleXapk as provided path is an xapk or apkm file");

        Logger.InfoNewline($"Attempting to extract required files from XAPK {gamePath}", "XAPK");

        using var xapkStream = File.OpenRead(gamePath);
        using var xapkZip = new ZipArchive(xapkStream);

        ZipArchiveEntry? configApk = null;
        var configApks = xapkZip.Entries.Where(e => e.FullName.Contains("config.") && e.FullName.EndsWith(".apk")).ToList();

        var instructionSetPreference = new[] { "arm64_v8a", "arm64", "armeabi_v7a", "arm" };
        foreach (var instructionSet in instructionSetPreference)
        {
            configApk = configApks.FirstOrDefault(e => e.FullName.Contains(instructionSet));
            if (configApk != null)
                break;
        }

        //Try for base.apk, else find any apk that isn't the config apk
        var mainApk = xapkZip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".apk") && e.FullName.Contains("base.apk"))
                      ?? xapkZip.Entries.FirstOrDefault(e => e != configApk && e.FullName.EndsWith(".apk"));

        Logger.InfoNewline($"Identified APKs inside XAPK - config: {configApk?.FullName}, mainPackage: {mainApk?.FullName}", "XAPK");

        if (configApk == null)
            throw new SoftException("Could not find a config apk inside the XAPK");
        if (mainApk == null)
            throw new SoftException("Could not find a main apk inside the XAPK");

        using var configZip = new ZipArchive(configApk.Open());
        using var mainZip = new ZipArchive(mainApk.Open());
        var binary = configZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("libil2cpp.so"));
        var globalMetadata = mainZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("global-metadata.dat"));

        var globalgamemanagers = mainZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("globalgamemanagers"));
        var dataUnity3d = mainZip.Entries.FirstOrDefault(e => e.FullName.EndsWith("data.unity3d"));

        if (binary == null)
            throw new SoftException("Could not find libil2cpp.so inside the config APK");
        if (globalMetadata == null)
            throw new SoftException("Could not find global-metadata.dat inside the main APK");
        if (globalgamemanagers == null && dataUnity3d == null)
            throw new SoftException("Could not find globalgamemanagers or data.unity3d inside the main APK");

        var tempFileBinary = Path.GetTempFileName();
        var tempFileMeta = Path.GetTempFileName();

        PathsToDeleteOnExit.Add(tempFileBinary);
        PathsToDeleteOnExit.Add(tempFileMeta);

        Logger.InfoNewline($"Extracting XAPK/{configApk.Name}/{binary.FullName} to {tempFileBinary}", "XAPK");
        binary.ExtractToFile(tempFileBinary, true);
        Logger.InfoNewline($"Extracting XAPK{mainApk.Name}/{globalMetadata.FullName} to {tempFileMeta}", "XAPK");
        globalMetadata.ExtractToFile(tempFileMeta, true);

        args.PathToAssembly = tempFileBinary;
        args.PathToMetadata = tempFileMeta;

        if (globalgamemanagers != null)
        {
            Logger.InfoNewline("Reading globalgamemanagers to determine unity version...", "XAPK");
            var ggmBytes = new byte[0x40];
            using var ggmStream = globalgamemanagers.Open();

            // ReSharper disable once MustUseReturnValue
            ggmStream.Read(ggmBytes, 0, 0x40);

            args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(ggmBytes);
        }
        else
        {
            Logger.InfoNewline("Reading data.unity3d to determine unity version...", "XAPK");
            using var du3dStream = dataUnity3d!.Open();

            args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(du3dStream);
        }

        Logger.InfoNewline($"Determined game's unity version to be {args.UnityVersion}", "XAPK");

        args.Valid = true;
    }

    private static void HandleIpa(string gamePath, ref Cpp2IlRuntimeArgs args)
    {
        //IPA
        //Metadata: Payload/AppName.app/Data/Managed/Metadata/global-metadata.dat
        //Binary: Payload/AppName.app/Frameworks/UnityFramework.framework/UnityFramework
        //GlobalGameManager: Payload/AppName.app/Data/globalgamemanagers
        //Unity3d: Payload/AppName.app/Data/data.unity3d

        Logger.VerboseNewline("Trying HandleIpa as provided path is an ipa or tipa file");

        Logger.InfoNewline($"Attempting to extract required files from IPA {gamePath}", "IPA");

        using var stream = File.OpenRead(gamePath);
        using var zipArchive = new ZipArchive(stream);

        var globalMetadata = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("Data/Managed/Metadata/global-metadata.dat"));
        var binary = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("Frameworks/UnityFramework.framework/UnityFramework"));

        var globalgamemanagers = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("Data/globalgamemanagers"));
        var dataUnity3d = zipArchive.Entries.FirstOrDefault(e => e.FullName.EndsWith("Data/data.unity3d"));

        if (binary == null)
            throw new SoftException("Could not find UnityFramework inside the ipa.");
        if (globalMetadata == null)
            throw new SoftException("Could not find global-metadata.dat inside the ipa.");
        if (globalgamemanagers == null && dataUnity3d == null)
            throw new SoftException("Could not find globalgamemanagers or unity3d inside the ipa.");

        var tempFileBinary = Path.GetTempFileName();
        var tempFileMeta = Path.GetTempFileName();

        PathsToDeleteOnExit.Add(tempFileBinary);
        PathsToDeleteOnExit.Add(tempFileMeta);

        Logger.InfoNewline($"Extracting IPA/{binary.FullName} to {tempFileBinary}", "IPA");
        binary.ExtractToFile(tempFileBinary, true);
        Logger.InfoNewline($"Extracting IPA/{globalMetadata.FullName} to {tempFileMeta}", "IPA");
        globalMetadata.ExtractToFile(tempFileMeta, true);

        args.PathToAssembly = tempFileBinary;
        args.PathToMetadata = tempFileMeta;

        if (globalgamemanagers != null)
        {
            Logger.InfoNewline("Reading globalgamemanagers to determine unity version...", "IPA");
            var ggmBytes = new byte[0x40];
            using var ggmStream = globalgamemanagers.Open();

            // ReSharper disable once MustUseReturnValue
            ggmStream.Read(ggmBytes, 0, 0x40);

            args.UnityVersion = Cpp2IlApi.GetVersionFromGlobalGameManagers(ggmBytes);
        }
        else
        {
            Logger.InfoNewline("Reading data.unity3d to determine unity version...", "IPA");
            using var du3dStream = dataUnity3d!.Open();

            args.UnityVersion = Cpp2IlApi.GetVersionFromDataUnity3D(du3dStream);
        }

        Logger.InfoNewline($"Determined game's unity version to be {args.UnityVersion}", "IPA");

        args.Valid = true;
    }

#if !NETFRAMEWORK
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "Cpp2IL.CommandLineArgs", "Cpp2IL")]
#endif
    private static Cpp2IlRuntimeArgs GetRuntimeOptionsFromCommandLine(string[] commandLine)
    {
        var parserResult = Parser.Default.ParseArguments<CommandLineArgs>(commandLine);

        if (parserResult is NotParsed<CommandLineArgs> notParsed && notParsed.Errors.Count() == 1 && notParsed.Errors.All(e => e.Tag is ErrorType.VersionRequestedError or ErrorType.HelpRequestedError))
            //Version or help requested
            Environment.Exit(0);

        if (parserResult is not Parsed<CommandLineArgs> { Value: { } options })
            throw new SoftException("Failed to parse command line arguments");

        ConsoleLogger.ShowVerbose = options.Verbose;

#pragma warning disable IL2026 // RequiresUnreferencedCode
        Cpp2IlApi.Init();
#pragma warning restore IL2026

        if (!options.AreForceOptionsValid)
            throw new SoftException("Invalid force option configuration");

        Cpp2IlApi.ConfigureLib(false);

        var result = new Cpp2IlRuntimeArgs();

        result.PathToAssembly = options.ForcedBinaryPath!;
        result.PathToMetadata = options.ForcedMetadataPath!;
        result.UnityVersion = UnityVersion.Parse(options.ForcedUnityVersion!);

        if (result.UnityVersion.Type == UnityVersionType.Alpha && result.UnityVersion.Build == 0)
            //Map a0 to f1 - we assume the user simply didn't provide the final part of the version number
            result.UnityVersion = new UnityVersion(result.UnityVersion.Major, result.UnityVersion.Minor, result.UnityVersion.Build, UnityVersionType.Final, 1);

        result.Valid = true;

        result.LowMemoryMode = options.LowMemoryMode;

        return result;
    }

    public static int Main(string[] args)
    {
        Console.WriteLine("===Cpp2IL by Samboy063===");
        Console.WriteLine("A Tool to Reverse Unity's \"il2cpp\" Build Process.");
        Console.WriteLine($"Version {Cpp2IlVersionString}\n");

        ConsoleLogger.Initialize();

        Logger.InfoNewline("Running on " + Environment.OSVersion.Platform);

        try
        {
            var runtimeArgs = GetRuntimeOptionsFromCommandLine(args);

            if (runtimeArgs.LowMemoryMode)
                //Force an early collection for all the zip shenanigans we may have just done
                GC.Collect();

            return MainWithArgs(runtimeArgs);
        }
        catch (SoftException e)
        {
            Logger.ErrorNewline($"Execution Failed: {e.Message}");
            return -1;
        }
#if !DEBUG
            catch (DllSaveException e)
            {
                Logger.ErrorNewline(e.ToString());
                Console.WriteLine();
                Console.WriteLine();
                Logger.ErrorNewline("Waiting for you to press enter - feel free to copy the error...");
                Console.ReadLine();

                return -1;
            }
            catch (LibCpp2ILInitializationException e)
            {
                Logger.ErrorNewline($"\n\n{e}\n\nWaiting for you to press enter - feel free to copy the error...");
                Console.ReadLine();
                return -1;
            }
#endif
    }

    public static int MainWithArgs(Cpp2IlRuntimeArgs runtimeArgs)
    {
        if (!runtimeArgs.Valid)
            throw new SoftException("Arguments have Valid = false");

        Cpp2IlApi.RuntimeOptions = runtimeArgs;

        var executionStart = DateTime.Now;

        GCSettings.LatencyMode = runtimeArgs.LowMemoryMode ? GCLatencyMode.Interactive : GCLatencyMode.SustainedLowLatency;

        Cpp2IlApi.InitializeLibCpp2Il(runtimeArgs.PathToAssembly, runtimeArgs.PathToMetadata, runtimeArgs.UnityVersion);

        if (runtimeArgs.LowMemoryMode)
            GC.Collect();

        WeirdStripStuff();

        Logger.InfoNewline($"Done. Total execution time: {(DateTime.Now - executionStart).TotalMilliseconds}ms");
        return 0;
    }

    private static void WeirdStripStuff()
    {
        var m = LibCpp2IlMain.TheMetadata!;

        // hard coded injections for Muse Dash currently, will be transitiond to dynamic later
        var imgIndex = InjectAssemblyImage(m, "UnityEngine.UnityAnalyticsModule");
        var asiType = InjectType(m, imgIndex, "AnalyticsSessionInfo", "UnityEngine.Analytics");
        var assType = InjectType(m, imgIndex, "AnalyticsSessionState", "UnityEngine.Analytics");
        var ceType = InjectType(m, imgIndex, "ContinuousEvent", "UnityEngine.Analytics");
        var rcsType = InjectType(m, imgIndex, "RemoteConfigSettings", "UnityEngine");
        var rsType = InjectType(m, imgIndex, "RemoteSettings", "UnityEngine");
        var rcshType = InjectType(m, imgIndex, "RemoteConfigSettingsHelper", "UnityEngine");
        var tagType = InjectType(m, imgIndex, "Tag", "UnityEngine", rcshType);

        InjectMethod(m, asiType, "CallIdentityTokenChanged");
        InjectMethod(m, asiType, "CallSessionStateChanged");
        InjectMethod(m, rcsType, "RemoteConfigSettingsUpdated");
        InjectMethod(m, rsType, "RemoteSettingsBeforeFetchFromServer");
        InjectMethod(m, rsType, "RemoteSettingsUpdateCompleted");
        InjectMethod(m, rsType, "RemoteSettingsUpdated");

        var aiImgIndex = InjectAssemblyImage(m, "UnityEngine.AIModule");
        var nvType = InjectType(m, aiImgIndex, "NavMesh", "UnityEngine.AI");
        InjectMethod(m, nvType, "Internal_CallOnNavMeshPreUpdate");

        Il2CppMetadataWriter.WriteTo(m, "E:\\global-metadata-mod.dat");
    }

    private static Dictionary<string, int> _injectedAssemblies = [];
    private static Dictionary<string, int> _injectedImages = [];
    private static Dictionary<string, int> _injectedNamespaces = [];
    private static Dictionary<string, int> _injectedTypes = [];
    private static Dictionary<string, int> _injectedMethods = [];

    private static int InjectAssemblyImage(Il2CppMetadata m, string assembly)
    {
        var potentialImg = m.imageDefinitions.FirstOrDefault(a => a.Name == assembly);
        if (potentialImg != null)
            return Array.IndexOf(m.imageDefinitions, potentialImg);

        var asmNameIndex = m.InjectNewString(assembly);
        var imgNameIndex = m.InjectNewString(assembly + ".dll");

        var asmDef = m.AssemblyDefinitions.First(a => a.AssemblyName.Name.StartsWith("UnityEngine.Core")).Clone<Il2CppAssemblyDefinition>();
        var imgDef = m.imageDefinitions.First(a => a.Name?.StartsWith("UnityEngine.Core") ?? false).Clone<Il2CppImageDefinition>();

        var asmIndex = m.AssemblyDefinitions.Length;
        var imgIndex = m.imageDefinitions.Length;

        asmDef.ImageIndex = imgIndex;
        asmDef.AssemblyName = asmDef.AssemblyName.Clone<Il2CppAssemblyNameDefinition>();
        asmDef.AssemblyName.nameIndex = asmNameIndex;

        imgDef.assemblyIndex = asmIndex;
        imgDef.nameIndex = imgNameIndex;
        imgDef.firstTypeIndex = m.typeDefs.Length;
        imgDef.typeCount = 0; // bumped by InjectType

        var imageList = m.imageDefinitions.ToList();
        imageList.Add(imgDef);
        m.imageDefinitions = [.. imageList];

        var asmList = m.AssemblyDefinitions.ToList();
        asmList.Add(asmDef);
        m.AssemblyDefinitions = [.. asmList];

        _injectedAssemblies.Add(assembly, asmIndex);
        _injectedImages.Add(assembly + ".dll", imgIndex);

        InjectType(m, imgIndex, "<Module>", null);

        return imgIndex;
    }

    private static int InjectType(Il2CppMetadata m, int imageIndex, string typeName, string? namespaceName = null, int declaringTypeIndex = -1)
    {
        var potentialType = m.typeDefs.FirstOrDefault(a => a.Namespace == namespaceName && a.Name == typeName);
        if (potentialType != null)
            return Array.IndexOf(m.typeDefs, potentialType);

        var typeDef = m.imageDefinitions.First(a => a.Name?.StartsWith("UnityEngine.Core") ?? false).Types!.First(a => a.Name == "<Module>").Clone<Il2CppTypeDefinition>();

        // <Module> has no namespace so global works as default
        var namespaceIndex = typeDef.NamespaceIndex;

        // nested types always use global namespace
        if (declaringTypeIndex == -1 && !string.IsNullOrWhiteSpace(namespaceName) && !_injectedNamespaces.TryGetValue(namespaceName!, out namespaceIndex))
            _injectedNamespaces.Add(namespaceName!, namespaceIndex = m.InjectNewString(namespaceName!));

        var typeNameIndex = m.InjectNewString(typeName);
        typeDef.NameIndex = typeNameIndex;
        typeDef.NamespaceIndex = namespaceIndex;

        typeDef.DeclaringTypeIndex = declaringTypeIndex;
        typeDef.ParentIndex = -1;
        typeDef.ElementTypeIndex = -1;

        typeDef.RgctxStartIndex = 0;
        typeDef.RgctxCount = 0;

        typeDef.GenericContainerIndex = -1;

        if (declaringTypeIndex == -1)
            typeDef.Flags = (int)TypeAttributes.Public;
        else
            typeDef.Flags = (int)TypeAttributes.NestedPublic;

        typeDef.FirstFieldIdx = -1;
        typeDef.FirstMethodIdx = -1;
        typeDef.FirstEventId = -1;
        typeDef.FirstPropertyId = -1;

        typeDef.NestedTypesStart = 0;
        typeDef.InterfacesStart = 0;
        typeDef.VtableStart = 0;
        typeDef.InterfaceOffsetsStart = 0;

        typeDef.MethodCount = 0;
        typeDef.PropertyCount = 0;
        typeDef.NestedTypeCount = 0;
        typeDef.FieldCount = 0;
        typeDef.EventCount = 0;
        typeDef.VtableCount = 0;
        typeDef.InterfacesCount = 0;
        typeDef.InterfaceOffsetsCount = 0;

        typeDef.Bitfield = 0;

        var index = m.typeDefs.Length;

        if (declaringTypeIndex != -1)
        {
            var indice = m.nestedTypeIndices.Length;

            var indiceList = m.nestedTypeIndices.ToList();
            indiceList.Add(index);
            m.nestedTypeIndices = [.. indiceList];

            var declaringType = m.typeDefs[declaringTypeIndex];
            declaringType.NestedTypeCount++;
            if (declaringType.NestedTypesStart == 0)
                declaringType.NestedTypesStart = indice;
            else
                throw new NotImplementedException("adding multiple nested types isnt supported");
        }

        var typeList = m.typeDefs.ToList();
        typeList.Add(typeDef);
        m.typeDefs = [.. typeList];


        var img = m.imageDefinitions[imageIndex];
        img.typeCount++;

        _injectedTypes.Add("[" + img.Name + "]" + namespaceName + "." + typeName, index);

        return index;
    }

    private static int InjectMethod(Il2CppMetadata m, int typeIndex, string methodName)
    {
        // TODO: empty body injection (if possible?)
        // TODO: injection on an existing type
        if (!_injectedTypes.ContainsValue(typeIndex))
            throw new NotImplementedException("injecting methods on a non-injected type is not supported");

        var methodDef = m.typeDefs.First(a => a.Namespace == "System" && a.Name == "Object")
            .Methods!.First(a => a.Name == ".ctor")
            .Clone<Il2CppMethodDefinition>();

        Console.WriteLine($"using {methodDef.ToString()} as base");

        var methodNameIndex = m.InjectNewString(methodName);
        methodDef.nameIndex = methodNameIndex;
        methodDef.declaringTypeIdx = typeIndex;

        methodDef.genericContainerIndex = -1;
        methodDef.customAttributeIndex = 0;

        methodDef.methodIndex = -1;
        methodDef.invokerIndex = -1;
        methodDef.delegateWrapperIndex = -1;
        methodDef.rgctxStartIndex = -1;
        methodDef.rgctxCount = -1;
        methodDef.token = 0;

        methodDef.flags = (ushort)(MethodAttributes.Public | MethodAttributes.Static);

        var index = m.methodDefs.Length;

        var methodList = m.methodDefs.ToList();
        methodList.Add(methodDef);
        m.methodDefs = [.. methodList];

        var typeDef = m.typeDefs[typeIndex];
        typeDef.MethodCount++;
        if (typeDef.FirstMethodIdx == -1)
            typeDef.FirstMethodIdx = index;

        _injectedMethods.Add("[" + typeDef.DeclaringAssembly!.Name + "]" + typeDef.Namespace + "." + typeDef.Name + "::" + methodName, index);

        return index;
    }
}
