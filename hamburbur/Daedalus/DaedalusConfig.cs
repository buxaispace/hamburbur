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
    }
}
