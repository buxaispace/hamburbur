using System;
using System.IO;
using hamburbur.Daedalus;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: WakeWordHeadless <modelDir> <wav> [wav...]");
            return 2;
        }

        string modelDir = args[0];
        int failed = 0;
        for (int i = 1; i < args.Length; i++)
        {
            string path = args[i];
            if (!TryReadWavFloats(path, out float[] samples, out int rate))
            {
                Console.WriteLine(Path.GetFileName(path) + "\tFAIL\tread");
                failed++;
                continue;
            }

            using VoskWakeSpotter spotter = new(modelDir, 0.20f, rate);
            if (i == 1)
                Console.WriteLine("model-ok missing-vocab=" + spotter.MissingVocabulary().Length);

            float[] feed = samples;
            int count = samples.Length;
            const int hop = 1600;
            string hit = null;
            for (int n = 0; n < count; n += hop)
            {
                int take = Math.Min(hop, count - n);
                float[] chunk = new float[take];
                Array.Copy(feed, n, chunk, 0, take);
                int status = spotter.Accept(chunk, take);
                if (spotter.TryGetWakeWord(out hit, false, status))
                    break;
            }

            if (hit == null)
                spotter.TryGetWakeWord(out hit, true);

            string expect = Path.GetFileNameWithoutExtension(path).Split('_')[0];
            bool isWake = expect is "jarvis" or "system" or "assistant" or "friday" or "echo"
                    or "cortana" or "mainframe" or "ultron" or "terminal";
            bool ok = isWake
                    ? string.Equals(hit, expect, StringComparison.OrdinalIgnoreCase)
                    : hit == null;
            Console.WriteLine(Path.GetFileName(path) + "\thit=" + (hit ?? "-") + "\t" + (ok ? "PASS" : "FAIL"));
            if (!ok)
                failed++;
            spotter.Reset();
        }

        return failed == 0 ? 0 : 1;
    }

    private static bool TryReadWavFloats(string path, out float[] samples, out int sampleRate)
    {
        samples = null;
        sampleRate = 0;
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new(stream);
        if (new string(reader.ReadChars(4)) != "RIFF")
            return false;
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE")
            return false;

        short channels = 0;
        short bits = 0;
        byte[] data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            string id = new(reader.ReadChars(4));
            int size = reader.ReadInt32();
            if (id == "fmt ")
            {
                reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bits = reader.ReadInt16();
                int extra = size - 16;
                if (extra > 0)
                    reader.ReadBytes(extra);
            }
            else if (id == "data")
            {
                data = reader.ReadBytes(size);
                break;
            }
            else
            {
                reader.ReadBytes(size);
            }
        }

        if (data == null || bits != 16 || channels < 1)
            return false;

        int frames = data.Length / 2 / channels;
        samples = new float[frames];
        for (int i = 0; i < frames; i++)
            samples[i] = BitConverter.ToInt16(data, i * channels * 2) / 32768f;
        return true;
    }
}
