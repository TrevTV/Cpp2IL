using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using CommandLine;

namespace Cpp2IL;

[SuppressMessage("ReSharper", "NotNullMemberIsNotInitialized")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public class CommandLineArgs
{
    [Option("binary-path", HelpText = "Force the path to the il2cpp binary. Don't use unless you know what you're doing, and use in conjunction with the other force options.")]
    public string? ForcedBinaryPath { get; set; }

    [Option("metadata-path", HelpText = "Force the path to the il2cpp metadata file. Don't use unless you know what you're doing, and use in conjunction with the other force options.")]
    public string? ForcedMetadataPath { get; set; }

    [Option("unity-version", HelpText = "Override the unity version detection. Don't use unless you know what you're doing, and use in conjunction with the other force options.")]
    public string? ForcedUnityVersion { get; set; }

    [Option("unity-managed-path", HelpText = "Override the unity version detection. Don't use unless you know what you're doing, and use in conjunction with the other force options.")]
    public string? UnityManagedPath { get; set; }

    [Option("verbose", HelpText = "Enable Verbose Logging.")]
    public bool Verbose { get; set; }

    [Option("low-memory-mode", HelpText = "Enable Low Memory Mode. This will attempt to reduce memory usage at the cost of performance.")]
    public bool LowMemoryMode { get; set; } = true;

    internal bool AreForceOptionsValid
    {
        get
        {
            if (ForcedBinaryPath != null && ForcedMetadataPath != null && ForcedUnityVersion != null)
                return true;

            return false;
        }
    }
}
