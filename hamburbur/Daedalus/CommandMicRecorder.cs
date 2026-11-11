using System;
using System.Collections;
using hamburbur.Managers;
using hamburbur.Tools;
using UnityEngine;

namespace hamburbur.Daedalus;

public static class CommandMicRecorder
{
    public const int   MaxSeconds          = 60;
    public const int   SampleRate          = 16000;
    public const float MinHoldSeconds      = 0.4f;
    public const float SilenceSeconds      = 1.15f;
    public const float SpeechRms           = 0.012f;
    public const float SpeechPeak          = 0.045f;
    public const float EndSilenceRms       = 0.008f;

    public sealed class Capture
    {
        public byte[] Wav;
        public bool   HadSpeech;
        public string ErrorClass;
    }

    public static IEnumerator RecordCommand(Action<Capture> done)
    {
        Capture capture = new();

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            capture.ErrorClass = "NoMicrophone";
            GameMic.EndExclusive();
            done?.Invoke(capture);
            yield break;
        }

        VoiceManager voice    = VoiceManager.Get();
        bool         wasMuted = voice != null && voice.MuteMicrophone;
        string       device   = GameMic.Resolve(out _) ?? Microphone.devices[0];

        GameMic.BeginExclusive();

        if (voice != null)
        {
            voice.MuteMicrophone = true;
            voice.StopRecording();
        }

        if (!string.IsNullOrEmpty(device) && Microphone.IsRecording(device))
            Microphone.End(device);

        AudioClip clip = Microphone.Start(device, false, MaxSeconds, SampleRate);
        if (clip == null)
        {
            capture.ErrorClass = "MicrophoneStartFailed";
            RestoreVoice(voice, wasMuted);
            GameMic.EndExclusive();
            done?.Invoke(capture);
            yield break;
        }

        while (Microphone.GetPosition(device) <= 0)
            yield return null;

        float elapsed     = 0f;
        float silenceFor  = 0f;
        bool  heardSpeech = false;
        int   lastPos     = 0;
        float[] ring      = new float[clip.samples];

        while (elapsed < MaxSeconds)
        {
            int pos = Microphone.GetPosition(device);
            if (pos < 0)
                break;

            if (pos != lastPos)
            {
                int count = pos > lastPos ? pos - lastPos : clip.samples - lastPos + pos;
                if (count > 0 && count <= ring.Length)
                {
                    float[] window = new float[count];
                    if (pos > lastPos)
                    {
                        clip.GetData(window, lastPos);
                    }
                    else
                    {
                        int     tailLen = clip.samples - lastPos;
                        float[] tail    = new float[tailLen];
                        float[] head    = new float[pos];
                        clip.GetData(tail, lastPos);
                        if (pos > 0)
                            clip.GetData(head, 0);
                        Array.Copy(tail, 0, window, 0, tailLen);
                        if (pos > 0)
                            Array.Copy(head, 0, window, tailLen, pos);
                    }

                    Analyze(window, out float rms, out float peak);
                    if (rms >= SpeechRms && peak >= SpeechPeak)
                    {
                        heardSpeech = true;
                        silenceFor  = 0f;
                    }
                    else if (heardSpeech && rms <= EndSilenceRms)
                    {
                        silenceFor += count / (float)SampleRate;
                    }
                    else if (!heardSpeech)
                    {
                        silenceFor = 0f;
                    }
                }

                lastPos = pos;
            }

            elapsed += Time.unscaledDeltaTime;

            if (heardSpeech && elapsed >= MinHoldSeconds && silenceFor >= SilenceSeconds)
                break;

            yield return null;
        }

        int endPos = Microphone.GetPosition(device);
        Microphone.End(device);

        if (endPos <= 0)
        {
            RestoreVoice(voice, wasMuted);
            GameMic.EndExclusive();
            done?.Invoke(capture);
            yield break;
        }

        float[] samples = new float[Math.Min(endPos, clip.samples)];
        clip.GetData(samples, 0);

        Analyze(samples, out float allRms, out float allPeak);
        capture.HadSpeech = heardSpeech || (allRms >= SpeechRms && allPeak >= SpeechPeak);

        if (elapsed < MinHoldSeconds && !capture.HadSpeech)
        {
            RestoreVoice(voice, wasMuted);
            GameMic.EndExclusive();
            done?.Invoke(capture);
            yield break;
        }

        if (capture.HadSpeech)
            capture.Wav = WAV.WritePcm16Mono(samples, SampleRate);

        RestoreVoice(voice, wasMuted);
        GameMic.EndExclusive();
        done?.Invoke(capture);
    }

    private static void RestoreVoice(VoiceManager voice, bool wasMuted)
    {
        if (voice == null)
            return;

        voice.StartRecording();
        voice.MuteMicrophone = wasMuted;
    }

    private static void Analyze(float[] samples, out float rms, out float peak)
    {
        rms  = 0f;
        peak = 0f;
        if (samples == null || samples.Length == 0)
            return;

        double sum  = 0;
        int    step = Math.Max(1, samples.Length / 8000);
        int    n    = 0;

        for (int i = 0; i < samples.Length; i += step)
        {
            float sample = samples[i];
            float mag    = sample < 0f ? -sample : sample;
            if (mag > peak)
                peak = mag;
            sum += sample * sample;
            n++;
        }

        rms = n > 0 ? (float)Math.Sqrt(sum / n) : 0f;
    }
}
