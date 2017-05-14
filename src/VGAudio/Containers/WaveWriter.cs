﻿using System;
using System.IO;
using VGAudio.Containers.Wave;
using VGAudio.Formats;
using VGAudio.Utilities;
using static VGAudio.Utilities.Helpers;

namespace VGAudio.Containers
{
    public class WaveWriter : AudioWriter<WaveWriter, WaveConfiguration>
    {
        private Pcm16Format Pcm16 { get; set; }
        private Pcm8Format Pcm8 { get; set; }
        private IAudioFormat AudioFormat { get; set; }

        private WaveCodec Codec => Configuration.Codec;
        private int ChannelCount => AudioFormat.ChannelCount;
        private int SampleCount => AudioFormat.SampleCount;
        private int SampleRate => AudioFormat.SampleRate;
        private bool Looping => AudioFormat.Looping;
        private int LoopStart => AudioFormat.LoopStart;
        private int LoopEnd => AudioFormat.LoopEnd;
        protected override int FileSize => 8 + RiffChunkSize;
        private int RiffChunkSize => 4 + 8 + FmtChunkSize + 8 + DataChunkSize;
        private int FmtChunkSize => ChannelCount > 2 ? 40 : 16;
        private int DataChunkSize => ChannelCount * SampleCount * BytesPerSample;
        private int SmplChunkSize => 0x3c;

        private int BitDepth => Configuration.Codec == WaveCodec.Pcm16Bit ? 16 : 8;
        private int BytesPerSample => BitDepth.DivideByRoundUp(8);
        private int BytesPerSecond => SampleRate * BytesPerSample * ChannelCount;
        private int BlockAlign => BytesPerSample * ChannelCount;

        // ReSharper disable InconsistentNaming
        private static readonly Guid KSDATAFORMAT_SUBTYPE_PCM =
            new Guid("00000001-0000-0010-8000-00aa00389b71");
        private const ushort WAVE_FORMAT_PCM = 1;
        private const ushort WAVE_FORMAT_EXTENSIBLE = 0xfffe;
        // ReSharper restore InconsistentNaming

        protected override void SetupWriter(AudioData audio)
        {
            if (Codec == WaveCodec.Pcm16Bit)
            {
                Pcm16 = audio.GetFormat<Pcm16Format>();
                AudioFormat = Pcm16;
            }
            else if (Codec == WaveCodec.Pcm8Bit)
            {
                Pcm8 = audio.GetFormat<Pcm8Format>();
                AudioFormat = Pcm8;
            }
        }

        protected override void WriteStream(Stream stream)
        {
            using (BinaryWriter writer = GetBinaryWriter(stream, Endianness.LittleEndian))
            {
                stream.Position = 0;
                WriteRiffHeader(writer);
                WriteFmtChunk(writer);
                WriteDataChunk(writer);
                if (Looping)
                    WriteSmplChunk(writer);
            }
        }

        private void WriteRiffHeader(BinaryWriter writer)
        {
            writer.WriteUTF8("RIFF");
            writer.Write(RiffChunkSize);
            writer.WriteUTF8("WAVE");
        }

        private void WriteFmtChunk(BinaryWriter writer)
        {
            writer.WriteUTF8("fmt ");
            writer.Write(FmtChunkSize);
            writer.Write((short)(ChannelCount > 2 ? WAVE_FORMAT_EXTENSIBLE : WAVE_FORMAT_PCM));
            writer.Write((short)ChannelCount);
            writer.Write(SampleRate);
            writer.Write(BytesPerSecond);
            writer.Write((short)BlockAlign);
            writer.Write((short)BitDepth);

            if (ChannelCount > 2)
            {
                writer.Write((short)22);
                writer.Write((short)BitDepth);
                writer.Write(GetChannelMask(ChannelCount));
                writer.Write(KSDATAFORMAT_SUBTYPE_PCM.ToByteArray());
            }
        }

        private void WriteDataChunk(BinaryWriter writer)
        {
            writer.WriteUTF8("data");
            writer.Write(DataChunkSize);

            if (Codec == WaveCodec.Pcm16Bit)
            {
                byte[] audioData = Pcm16.Channels.ShortToInterleavedByte();
                writer.BaseStream.Write(audioData, 0, audioData.Length);
            }
            else if (Codec == WaveCodec.Pcm8Bit)
            {
                Pcm8.Channels.Interleave(writer.BaseStream, BytesPerSample);
            }           
        }

        private void WriteSmplChunk(BinaryWriter writer)
        {
            writer.WriteUTF8("smpl");
            writer.Write(SmplChunkSize);
            for (int i = 0; i < 7; i++)
                writer.Write(0);
            writer.Write(1);
            for (int i = 0; i < 3; i++)
                writer.Write(0);
            writer.Write(LoopStart);
            writer.Write(LoopEnd);
            writer.Write(0);
            writer.Write(0);
        }

        private static int GetChannelMask(int channelCount)
        {
            //Nothing special about these masks. I just choose
            //whatever channel combinations seemed okay.
            switch (channelCount)
            {
                case 4:
                    return 0x0033;
                case 5:
                    return 0x0133;
                case 6:
                    return 0x0633;
                case 7:
                    return 0x01f3;
                case 8:
                    return 0x06f3;
                default:
                    return (1 << channelCount) - 1;
            }
        }
    }
}
