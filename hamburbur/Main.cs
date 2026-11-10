using BepInEx;
using hamburbur.Daedalus;
using UnityEngine;

namespace hamburbur;

[BepInPlugin(Constants.PluginGuid, Constants.PluginName, Constants.PluginVersion)]
public class Main : BaseUnityPlugin
{
    private void Awake()
    {
        DaedalusConfig.Init(Config);
        LoadNormally();
    }

    public static void Inject()
    {
        GameObject hamburburMenu = new(nameof(hamburburMenu));
        hamburburMenu.AddComponent<Plugin>();
    }

    private void LoadNormally()
    {
        GameObject hamburburMenu = new(nameof(hamburburMenu));
        hamburburMenu.AddComponent<Plugin>();
        DontDestroyOnLoad(hamburburMenu);
    }
}