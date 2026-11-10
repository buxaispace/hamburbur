using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using hamburbur.Managers;
using hamburbur.Misc;
using hamburbur.Mods.Settings;
using hamburbur.Tools;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace hamburbur.Libs;

public class AudioLib : MonoBehaviour
{
    private        AudioSource source;

    public static AudioLib Instance
    {
        get
        {
            // ReSharper disable once InvertIf
            if (!field)
            {
                GameObject obj = new("Hamburbur AudioLib");
                field = obj.AddComponent<AudioLib>();
                DontDestroyOnLoad(obj);
            }

            return field;
        }
    }

    private void Awake()
    {
        if (source != null)
            return;

        source             = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
    }

    public void SpeakText(string text, float volume = 1f) => StartCoroutine(SpeakRoutine(text, volume));

    public IEnumerator SpeakReplyAudio(string audioField, string displayText)
    {
        if (!TryDecodeAudioField(audioField, out byte[] bytes, out AudioType audioType, out bool rawWav))
        {
            NotifyReply(displayText, 2.5f);
            yield break;
        }

        AudioClip clip = null;
        if (rawWav)
        {
            clip = ClipFromWav(bytes);
        }
        else
        {
            string tempPath = Path.Combine(Application.temporaryCachePath, "daedalus-reply.bin");
            File.WriteAllBytes(tempPath, bytes);

            using UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + tempPath, audioType);
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
                clip = DownloadHandlerAudioClip.GetContent(www);
        }

        if (clip == null)
        {
            Debug.LogWarning("[AudioLib] ReplyAudioUnavailable");
            NotifyReply(displayText, 2.5f);
            yield break;
        }

        yield return PlayClip(clip, displayText);
    }

    public IEnumerator SpeakRoutine(string text, float volume)
    {
        string fileName = $"{GetSHA256(text)}.mp3";
        string directoryPath = Path.Combine(Application.persistentDataPath,
                $"Hamburbur_TextToSpeech_Voice-{JarvisVoice.Voices[JarvisVoice.Instance.IncrementalValue]}");

        string filePath = Path.Combine(directoryPath, fileName);

        if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

        if (!File.Exists(filePath))
        {
            string narratorName = JarvisVoice.Voices[JarvisVoice.Instance.IncrementalValue];

            if (text.Length > 550)
                text = text[..550];

            using UnityWebRequest request = new(
                    "https://lazypy.ro/tts/request_tts.php?service=Streamlabs&voice="
                  + narratorName + "&text=" + UnityWebRequest.EscapeURL(text),
                    "POST");

            request.downloadHandler = new DownloadHandlerBuffer();

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[AudioLib] Error getting TTS: " + request.error);

                yield break;
            }

            string jsonResponse = request.downloadHandler.text;

            if (string.IsNullOrWhiteSpace(jsonResponse))
            {
                Debug.LogError("[AudioLib] Empty TTS JSON response.");

                yield break;
            }

            Dictionary<string, object> responseData =
                    JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);

            if (responseData == null || !responseData.ContainsKey("audio_url"))
            {
                Debug.LogError("[AudioLib] Invalid TTS JSON, missing audio_url: " + jsonResponse);

                yield break;
            }

            if (responseData == null || !responseData.ContainsKey("audio_url") || responseData["audio_url"] == null)
            {
                Debug.LogError("[AudioLib] Streamlabs JSON missing or null audio_url: " + jsonResponse);

                yield break;
            }

            string audioUrl = responseData["audio_url"].ToString().Replace("\\", "");

            using UnityWebRequest dataRequest = UnityWebRequest.Get(audioUrl);

            yield return dataRequest.SendWebRequest();

            if (dataRequest.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[AudioLib] Error downloading TTS: " + audioUrl);

                yield break;
            }

            File.WriteAllBytes(filePath, dataRequest.downloadHandler.data);
        }

        using UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + filePath, AudioType.MPEG);

        yield return www.SendWebRequest();

        if (www.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[AudioLib] Error loading TTS clip: {www.error}");

            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
        if (clip == null)
        {
            Debug.LogError("[AudioLib] Clip is null after download.");

            yield break;
        }

        while (clip.loadState != AudioDataLoadState.Loaded)
            yield return null;

        yield return PlayClip(clip, text);
    }

    private IEnumerator PlayClip(AudioClip clip, string displayText)
    {
        while (clip.loadState != AudioDataLoadState.Loaded)
            yield return null;

        NotifyReply(displayText, clip.length);
        VoiceManager.Get().AudioClip(clip);
        Debug.Log("[AudioLib] PlayingClip");

        yield return new WaitForSeconds(clip.length);
    }

    private static void NotifyReply(string displayText, float duration)
    {
        if (!JarvisNotifications.IsEnabled || string.IsNullOrWhiteSpace(displayText))
            return;

        string wakeWord = VoiceControls.Instance != null ? VoiceControls.Instance.LastUsedWakeWord : "jarvis";
        if (!string.IsNullOrEmpty(wakeWord))
            wakeWord = char.ToUpper(wakeWord[0]) + wakeWord[1..];

        NotificationManager.SendNotification(
                $"<color=#{ColorUtility.ToHtmlStringRGB(Plugin.Instance.MainColour)}>{wakeWord}</color>",
                displayText, duration, false, false);
    }

    private static bool TryDecodeAudioField(string audioField, out byte[] bytes, out AudioType audioType, out bool rawWav)
    {
        bytes    = null;
        audioType = AudioType.WAV;
        rawWav   = false;

        if (string.IsNullOrWhiteSpace(audioField))
            return false;

        string value = audioField.Trim();
        string mime  = "";
        string b64   = value;

        const string Prefix = "data:";
        if (value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            int comma = value.IndexOf(',');
            if (comma < 0)
                return false;

            string header = value[Prefix.Length..comma];
            b64 = value[(comma + 1)..];
            int slash = header.IndexOf(';');
            mime = (slash >= 0 ? header[..slash] : header).Trim().ToLowerInvariant();
        }

        try
        {
            bytes = Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes == null || bytes.Length < 4)
            return false;

        bool isRiff = bytes.Length >= 12 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' &&
                      bytes[2] == (byte)'F' && bytes[3] == (byte)'F';
        bool isOgg  = bytes[0] == (byte)'O' && bytes[1] == (byte)'g' && bytes[2] == (byte)'g' && bytes[3] == (byte)'S';
        bool isId3  = bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3';

        if (mime.Contains("wav") || isRiff)
        {
            rawWav    = true;
            audioType = AudioType.WAV;
            return true;
        }

        if (mime.Contains("ogg") || isOgg)
        {
            audioType = AudioType.OGGVORBIS;
            return true;
        }

        audioType = AudioType.MPEG;
        return mime.Contains("mpeg") || mime.Contains("mp3") || isId3 || mime.Length == 0;
    }

    private static AudioClip ClipFromWav(byte[] wavFile)
    {
        try
        {
            WAV wav = new(wavFile);
            if (wav.LeftChannel == null || wav.SampleCount <= 0)
                return null;

            AudioClip clip = AudioClip.Create("daedalus-reply", wav.SampleCount, wav.ChannelCount, wav.Frequency, false);
            if (wav.ChannelCount == 2 && wav.RightChannel != null)
            {
                float[] interleaved = new float[wav.SampleCount * 2];
                for (int i = 0; i < wav.SampleCount; i++)
                {
                    interleaved[i * 2]     = wav.LeftChannel[i];
                    interleaved[i * 2 + 1] = wav.RightChannel[i];
                }

                clip.SetData(interleaved, 0);
            }
            else
            {
                clip.SetData(wav.LeftChannel, 0);
            }

            return clip;
        }
        catch (Exception)
        {
            Debug.LogWarning("[AudioLib] ReplyAudioUnavailable");
            return null;
        }
    }

    public static string GetSHA256(string text)
    {
        using SHA256  sha   = SHA256.Create();
        byte[]        bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        StringBuilder sb    = new();
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));

        return sb.ToString();
    }
}