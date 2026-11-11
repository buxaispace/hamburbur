using System;
using BepInEx;
using BepInEx.Configuration;

namespace hamburbur.Daedalus;

public static class DaedalusConfig
{
    public const string DefaultBaseUrl = "https://astrovect.online";
    public const string SpeechModel    = "daedalus-speech";

    private static ConfigEntry<string> baseUrl;
    private static ConfigEntry<string> apiKey;
    private static ConfigEntry<string> wakeEngine;
    private static ConfigEntry<float>  wakeSensitivity;
    private static ConfigEntry<float>  wakeCooldown;
    private static ConfigEntry<string> wakeDeviceName;
    private static ConfigEntry<bool>   wakeKeywordFallback;
    private static bool                loaded;

    public static string BaseUrl
    {
        get
        {
            EnsureLoaded();
            string value = (baseUrl?.Value ?? DefaultBaseUrl).Trim().TrimEnd('/');
            return string.IsNullOrEmpty(value) ? DefaultBaseUrl : value;
        }
    }

    public static string ApiKey
    {
        get
        {
            EnsureLoaded();
            return (apiKey?.Value ?? "").Trim();
        }
    }

    public static bool HasApiKey => ApiKey.StartsWith("dk_", StringComparison.Ordinal);

    /// <summary>Local, KeywordRecognizer, or Auto.</summary>
    public static string WakeEngine
    {
        get
        {
            EnsureLoaded();
            return (wakeEngine?.Value ?? "Auto").Trim();
        }
    }

    /// <summary>0 (strict) to 1 (loose). Maps to Vosk minimum word confidence.</summary>
    public static float WakeSensitivity
    {
        get
        {
            EnsureLoaded();
            float value = wakeSensitivity?.Value ?? 0.55f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public static float WakeMinConfidence => 0.18f + (1f - WakeSensitivity) * 0.62f;

    public static float WakeCooldownSeconds
    {
        get
        {
            EnsureLoaded();
            float value = wakeCooldown?.Value ?? 1.75f;
            return value < 0.25f ? 0.25f : value;
        }
    }

    public static string WakeDeviceName
    {
        get
        {
            EnsureLoaded();
            return (wakeDeviceName?.Value ?? "").Trim();
        }
    }

    public static bool WakeKeywordRecognizerFallback
    {
        get
        {
            EnsureLoaded();
            return wakeKeywordFallback?.Value ?? true;
        }
    }

    public static void Init(ConfigFile config)
    {
        if (config == null)
            return;

        Bind(config);
        loaded = true;
    }

    private static void EnsureLoaded()
    {
        if (loaded)
            return;

        string path = System.IO.Path.Combine(Paths.ConfigPath, Constants.PluginGuid + ".cfg");
        Bind(new ConfigFile(path, true));
        loaded = true;
    }

    private static void Bind(ConfigFile config)
    {
        baseUrl = config.Bind(
                "Daedalus",
                "BaseUrl",
                DefaultBaseUrl,
                "HTTPS origin for the personal Daedalus API (no trailing slash).");

        apiKey = config.Bind(
                "Daedalus",
                "ApiKey",
                "",
                "Personal Daedalus API key (dk_…). Never share this file.");

        wakeEngine = config.Bind(
                "WakeWord",
                "Engine",
                "Auto",
                "Local (offline Vosk on the game mic), KeywordRecognizer (Windows Speech), or Auto (Local then fallback).");

        wakeSensitivity = config.Bind(
                "WakeWord",
                "Sensitivity",
                0.55f,
                "0 = strict, 1 = loose. Default 0.55. Higher values accept lower Vosk confidence.");

        wakeCooldown = config.Bind(
                "WakeWord",
                "CooldownSeconds",
                1.75f,
                "Seconds to ignore wake words after a command turn so reply audio is not re-triggered.");

        wakeDeviceName = config.Bind(
                "WakeWord",
                "DeviceName",
                "",
                "Optional Unity microphone device name. Empty uses Photon Voice / Gorilla Tag / Unity default.");

        wakeKeywordFallback = config.Bind(
                "WakeWord",
                "KeywordRecognizerFallback",
                true,
                "If Local Vosk cannot start, use Unity KeywordRecognizer (Windows Speech, default input device).");
    }
}
