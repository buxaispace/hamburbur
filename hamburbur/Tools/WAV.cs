using System;
using System.IO;

namespace hamburbur.Tools;

public class WAV
{
    public WAV(byte[] wavFile)
    {
        using MemoryStream stream = new(wavFile);
        using BinaryReader reader = new(stream);

        reader.ReadChars(4);
        reader.ReadInt32();
        reader.ReadChars(4);

        string chunkID   = new(reader.ReadChars(4));
        int    chunkSize = reader.ReadInt32();

        short audioFormat = reader.ReadInt16();
        ChannelCount = reader.ReadInt16();
        Frequency    = reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadInt16();
        short bitDepth = reader.ReadInt16();

        int fmtExtra = chunkSize - 16;
        if (fmtExtra > 0)
            reader.ReadBytes(fmtExtra);

        string dataID = new(reader.ReadChars(4));
        while (dataID != "data")
        {
            int skip = reader.ReadInt32();
            reader.ReadBytes(skip);
            dataID = new string(reader.ReadChars(4));
        }

        int    dataSize  = reader.ReadInt32();
        byte[] byteArray = reader.ReadBytes(dataSize);

        int bytesPerSample = bitDepth / 8;
        SampleCount = dataSize / bytesPerSample / ChannelCount;

        float[] data   = new float[SampleCount * ChannelCount];
        int     offset = 0;

        for (int i = 0; i < data.Length; i++)
            switch (bytesPerSample)
            {
                case 2:
                {
                    short value = BitConverter.ToInt16(byteArray, offset);
                    data[i] =  value / 32768f;
                    offset  += 2;

                    break;
                }

                case 1:
                    data[i] =  (byteArray[offset] - 128) / 128f;
                    offset  += 1;

                    break;
            }

        if (ChannelCount == 2)
        {
            LeftChannel  = new float[SampleCount];
            RightChannel = new float[SampleCount];

            for (int i = 0; i < SampleCount; i++)
            {
                LeftChannel[i]  = data[i * 2];
                RightChannel[i] = data[i * 2 + 1];
            }
        }
        else
        {
            LeftChannel  = data;
            RightChannel = null;
        }
    }

    public float[] LeftChannel  { get; }
    public float[] RightChannel { get; }
    public int     ChannelCount { get; }
    public int     SampleCount  { get; }
    public int     Frequency    { get; private set; }

    public static byte[] WritePcm16Mono(float[] samples, int sampleRate)
    {
        samples    ??= Array.Empty<float>();
        sampleRate =   sampleRate > 0 ? sampleRate : 16000;

        int    dataBytes = samples.Length * 2;
        byte[] wav       = new byte[44 + dataBytes];

        WriteAscii(wav, 0,  "RIFF");
        WriteInt32(wav, 4,  wav.Length - 8);
        WriteAscii(wav, 8,  "WAVE");
        WriteAscii(wav, 12, "fmt ");
        WriteInt32(wav, 16, 16);
        WriteInt16(wav, 20, 1);
        WriteInt16(wav, 22, 1);
        WriteInt32(wav, 24, sampleRate);
        WriteInt32(wav, 28, sampleRate * 2);
        WriteInt16(wav, 32, 2);
        WriteInt16(wav, 34, 16);
        WriteAscii(wav, 36, "data");
        WriteInt32(wav, 40, dataBytes);

        for (int i = 0; i < samples.Length; i++)
        {
            float clamped = samples[i] < -1f ? -1f : samples[i] > 1f ? 1f : samples[i];
            short pcm     = (short)Math.Round(clamped * (clamped < 0f ? 32768f : 32767f));
            wav[44 + i * 2]     = (byte)(pcm       & 0xFF);
            wav[44 + i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }

        return wav;
    }

    private static void WriteAscii(byte[] dest, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++)
            dest[offset + i] = (byte)text[i];
    }

    private static void WriteInt16(byte[] dest, int offset, short value)
    {
        dest[offset]     = (byte)(value       & 0xFF);
        dest[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteInt32(byte[] dest, int offset, int value)
    {
        dest[offset]     = (byte)(value        & 0xFF);
        dest[offset + 1] = (byte)((value >> 8)  & 0xFF);
        dest[offset + 2] = (byte)((value >> 16) & 0xFF);
        dest[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}