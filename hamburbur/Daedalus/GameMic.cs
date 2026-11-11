using System;
using GorillaNetworking;
using hamburbur.Managers;
using Photon.Voice;
using Photon.Voice.Unity;
using UnityEngine;

namespace hamburbur.Daedalus;

/// <summary>
/// Resolves the Unity microphone device Gorilla Tag / Photon Voice actually uses.
/// Does not log audio or transcripts.
/// </summary>
public static class GameMic
{
    public static bool Exclusive { get; private set; }

    public static void BeginExclusive() => Exclusive = true;

    public static void EndExclusive() => Exclusive = false;

    public static string UnityDefault()
    {
        string[] devices = Microphone.devices;
        return devices != null && devices.Length > 0 ? devices[0] : null;
    }

    public static bool IsKnownDevice(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        string[] devices = Microphone.devices;
        if (devices == null)
            return false;

        for (int i = 0; i < devices.Length; i++)
        {
            if (string.Equals(devices[i], name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Pick the same device the game's voice chat is bound to.
    /// </summary>
    public static string Resolve(out string source)
    {
        string overrideName = DaedalusConfig.WakeDeviceName;
        if (IsKnownDevice(overrideName))
        {
            source = "config";
            return overrideName;
        }

        VoiceManager voice = VoiceManager.Instance;
        if (voice != null && IsKnownDevice(voice.DeviceName))
        {
            source = "VoiceManager";
            return voice.DeviceName;
        }

        if (TryPhotonRecorder(out string photon, out string photonSource) && IsKnownDevice(photon))
        {
            source = photonSource;
            return photon;
        }

        string unity = UnityDefault();
        source = unity == null ? "none" : "UnityDefault";
        return unity;
    }

    private static bool TryPhotonRecorder(out string device, out string source)
    {
        device = null;
        source = "Photon";

        try
        {
            GorillaTagger tagger = GorillaTagger.Instance;
            Recorder recorder = tagger != null ? tagger.myRecorder : null;
            if (recorder == null)
                return false;

            string unityMic = recorder.UnityMicrophoneDevice;
            if (!string.IsNullOrEmpty(unityMic) &&
                !string.Equals(unityMic, "Default", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(unityMic, "None", StringComparison.OrdinalIgnoreCase))
            {
                device = unityMic;
                source = "Photon.UnityMicrophoneDevice";
                return true;
            }

            DeviceInfo info = recorder.MicrophoneDevice;
            if (!info.IsDefault)
            {
                string id = info.IDString;
                if (string.IsNullOrEmpty(id))
                    id = info.ToString();
                if (!string.IsNullOrEmpty(id) &&
                    !string.Equals(id, "Default", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(id, "None", StringComparison.OrdinalIgnoreCase))
                {
                    device = id;
                    source = "Photon.MicrophoneDevice";
                    return true;
                }
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }
}
