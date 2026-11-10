using System;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;

namespace hamburbur.Daedalus;

/// <summary>
/// Offline Vosk keyword grammar spotter. Never logs audio, transcripts, or JSON.
/// </summary>
public sealed class VoskWakeSpotter : IDisposable
{
    public const int ModelSampleRate = 16000;

    private static readonly string[] WakeWords =
    [
            "jarvis", "system", "assistant", "friday", "echo", "cortana", "mainframe", "ultron", "terminal",
    ];

    private static readonly string GrammarJson =
            "[\"jarvis\",\"system\",\"assistant\",\"friday\",\"echo\",\"cortana\",\"mainframe\",\"ultron\",\"terminal\",\"[unk]\"]";

    private readonly float minConfidence;
    private IntPtr         model;
    private IntPtr         recognizer;
    private bool           disposed;

    public VoskWakeSpotter(string modelPath, float minConfidence, int sampleRate = ModelSampleRate)
    {
        this.minConfidence = minConfidence;
        Native.vosk_set_log_level(-2);

        model = Native.vosk_model_new(modelPath);
        if (model == IntPtr.Zero)
            throw new InvalidOperationException("VoskModelFailed");

        float rate = sampleRate > 0 ? sampleRate : ModelSampleRate;
        recognizer = Native.vosk_recognizer_new_grm(model, rate, GrammarJson);
        if (recognizer == IntPtr.Zero)
            throw new InvalidOperationException("VoskRecognizerFailed");

        Native.vosk_recognizer_set_words(recognizer, 1);
        Native.vosk_recognizer_set_partial_words(recognizer, 1);
    }

    public string[] MissingVocabulary()
    {
        System.Collections.Generic.List<string> missing = new();
        for (int i = 0; i < WakeWords.Length; i++)
        {
            if (Native.vosk_model_find_word(model, WakeWords[i]) < 0)
                missing.Add(WakeWords[i]);
        }

        return missing.ToArray();
    }

    public void Reset()
    {
        if (recognizer != IntPtr.Zero)
            Native.vosk_recognizer_reset(recognizer);
    }

    public int Accept(float[] samples, int count)
    {
        if (recognizer == IntPtr.Zero || samples == null || count <= 0)
            return 0;

        short[] pcm = new short[count];
        for (int i = 0; i < count; i++)
        {
            float clamped = samples[i] < -1f ? -1f : samples[i] > 1f ? 1f : samples[i];
            pcm[i] = (short)Math.Round(clamped * (clamped < 0f ? 32768f : 32767f));
        }

        return Native.vosk_recognizer_accept_waveform_s(recognizer, pcm, count);
    }

    public bool TryGetWakeWord(out string word, bool flush = false, int acceptStatus = 0)
    {
        word = null;
        if (recognizer == IntPtr.Zero)
            return false;

        if (acceptStatus > 0 &&
            TryMatch(PtrToUtf8(Native.vosk_recognizer_result(recognizer)), "text", true, out word))
            return true;

        if (TryMatch(PtrToUtf8(Native.vosk_recognizer_partial_result(recognizer)), "partial", false, out word))
            return true;

        if (!flush)
            return false;

        return TryMatch(PtrToUtf8(Native.vosk_recognizer_final_result(recognizer)), "text", true, out word);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (recognizer != IntPtr.Zero)
        {
            Native.vosk_recognizer_free(recognizer);
            recognizer = IntPtr.Zero;
        }

        if (model != IntPtr.Zero)
        {
            Native.vosk_model_free(model);
            model = IntPtr.Zero;
        }
    }

    private bool TryMatch(string json, string textField, bool useConfidence, out string word)
    {
        word = null;
        if (string.IsNullOrEmpty(json))
            return false;

        JObject obj;
        try
        {
            obj = JObject.Parse(json);
        }
        catch
        {
            return false;
        }


        if (useConfidence && obj["result"] is JArray words)
        {
            foreach (JToken token in words)
            {
                if (token is not JObject item)
                    continue;

                string candidate = ((string)item["word"] ?? "").Trim().ToLowerInvariant();
                float  conf      = item["conf"] != null ? (float)item["conf"] : 1f;
                if (IsWakeWord(candidate) && conf >= minConfidence)
                {
                    word = candidate;
                    return true;
                }
            }
        }

        string text = ((string)obj[textField] ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text) || text == "[unk]")
            return false;

        string[] parts = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (IsWakeWord(parts[i]))
            {
                word = parts[i];
                return true;
            }
        }

        return false;
    }

    private static bool IsWakeWord(string value)
    {
        for (int i = 0; i < WakeWords.Length; i++)
        {
            if (WakeWords[i] == value)
                return true;
        }

        return false;
    }

    private static string PtrToUtf8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
            return null;

        int length = 0;
        while (Marshal.ReadByte(ptr, length) != 0)
            length++;

        if (length == 0)
            return "";

        byte[] buffer = new byte[length];
        Marshal.Copy(ptr, buffer, 0, length);
        return Encoding.UTF8.GetString(buffer);
    }

    private static class Native
    {
        private const string Lib = "libvosk";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void vosk_set_log_level(int logLevel);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern IntPtr vosk_model_new(string modelPath);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void vosk_model_free(IntPtr model);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int vosk_model_find_word(IntPtr model, string word);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern IntPtr vosk_recognizer_new_grm(IntPtr model, float sampleRate, string grammar);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void vosk_recognizer_set_words(IntPtr recognizer, int words);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void vosk_recognizer_set_partial_words(IntPtr recognizer, int partialWords);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int vosk_recognizer_accept_waveform_s(IntPtr recognizer, short[] data, int length);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vosk_recognizer_result(IntPtr recognizer);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vosk_recognizer_partial_result(IntPtr recognizer);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr vosk_recognizer_final_result(IntPtr recognizer);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void vosk_recognizer_reset(IntPtr recognizer);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void vosk_recognizer_free(IntPtr recognizer);
    }
}
