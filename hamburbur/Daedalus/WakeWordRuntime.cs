using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using UnityEngine;

namespace hamburbur.Daedalus;

/// <summary>
/// Extracts the local Vosk wake-word runtime next to the plugin on first run.
/// Looks for an already-extracted folder, a sidecar zip, then an embedded zip.
/// </summary>
public static class WakeWordRuntime
{
    public const string RuntimeVersion = "vosk-small-en-us-0.15-win64-0.3.45";
    public const string EmbeddedName   = "hamburbur.wakeword.runtime.zip";

    public static string RootPath => Path.Combine(Paths.PluginPath, "hamburbur", "wakeword");

    public static string ModelPath => Path.Combine(RootPath, "model");

    public static string NativeDir => RootPath;

    public static bool TryEnsureExtracted(out string error)
    {
        error = null;
        try
        {
            string marker = Path.Combine(RootPath, "VERSION");
            if (File.Exists(marker) &&
                string.Equals(File.ReadAllText(marker).Trim(), RuntimeVersion, StringComparison.Ordinal) &&
                Directory.Exists(ModelPath) &&
                File.Exists(Path.Combine(NativeDir, "libvosk.dll")))
            {
                return true;
            }

            Directory.CreateDirectory(RootPath);

            string zip = FindRuntimeZip();
            if (zip != null)
            {
                ExtractZip(zip, RootPath);
            }
            else if (!TryExtractEmbedded(RootPath))
            {
                error = "WakeWordRuntimeMissing";
                return false;
            }

            File.WriteAllText(marker, RuntimeVersion);
            if (!Directory.Exists(ModelPath) || !File.Exists(Path.Combine(NativeDir, "libvosk.dll")))
            {
                error = "WakeWordRuntimeIncomplete";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error = "WakeWordExtractFailed";
            Debug.LogWarning("[Hamburbur Voice Assistant] WakeRuntime:extract-failed:" + exception.GetType().Name);
            return false;
        }
    }

    public static bool TryLoadNative(out string error)
    {
        error = null;
        string lib = Path.Combine(NativeDir, "libvosk.dll");
        if (!File.Exists(lib))
        {
            error = "WakeWordNativeMissing";
            return false;
        }

        SetDllDirectory(NativeDir);
        if (LoadLibrary(lib) == IntPtr.Zero)
        {
            error = "WakeWordNativeLoadFailed";
            return false;
        }

        return true;
    }

    private static string FindRuntimeZip()
    {
        string[] candidates =
        [
                Path.Combine(RootPath, "runtime.zip"),
                Path.Combine(Paths.PluginPath, "hamburbur-wakeword-runtime.zip"),
                Path.Combine(Paths.PluginPath, "wakeword-runtime.zip"),
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                        "hamburbur-wakeword-runtime.zip"),
        ];

        for (int i = 0; i < candidates.Length; i++)
        {
            if (File.Exists(candidates[i]))
                return candidates[i];
        }

        return null;
    }

    private static bool TryExtractEmbedded(string dest)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedName);
        if (stream == null)
            return false;

        string temp = Path.Combine(dest, "runtime.zip");
        using (FileStream file = File.Create(temp))
            stream.CopyTo(file);

        ExtractZip(temp, dest);
        try
        {
            File.Delete(temp);
        }
        catch
        {
            // Keep the extracted zip if delete fails.
        }

        return true;
    }

    private static void ExtractZip(string zipPath, string dest)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string relative = entry.FullName.Replace('\\', '/');
            if (relative.StartsWith("vosk-win64-0.3.45/", StringComparison.OrdinalIgnoreCase))
                relative = relative.Substring("vosk-win64-0.3.45/".Length);
            if (relative.StartsWith("vosk-model-small-en-us-0.15/", StringComparison.OrdinalIgnoreCase))
                relative = "model/" + relative.Substring("vosk-model-small-en-us-0.15/".Length);

            if (relative.EndsWith(".lib", StringComparison.OrdinalIgnoreCase) ||
                relative.EndsWith(".h", StringComparison.OrdinalIgnoreCase))
                continue;

            string target = Path.Combine(dest, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? dest);
            entry.ExtractToFile(target, true);
        }
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);
}
