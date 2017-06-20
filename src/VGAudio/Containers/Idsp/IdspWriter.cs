﻿using System;
using System.IO;
using System.Linq;
using VGAudio.Codecs.GcAdpcm;
using VGAudio.Formats;
using VGAudio.Formats.GcAdpcm;
using VGAudio.Utilities;
using static VGAudio.Formats.GcAdpcm.GcAdpcmHelpers;
using static VGAudio.Utilities.Helpers;

namespace VGAudio.Containers.Idsp
{
    public class IdspWriter : AudioWriter<IdspWriter, IdspConfiguration>
    {
        private GcAdpcmFormat Adpcm { get; set; }

        protected override int FileSize => HeaderSize + AudioDataSize * ChannelCount;

        private int SampleCount => (Configuration.TrimFile && Adpcm.Looping ? LoopEnd :
            Math.Max(Adpcm.SampleCount, LoopEnd));

        private int ChannelCount => Adpcm.ChannelCount;

        private int LoopStart => Adpcm.LoopStart;
        private int LoopEnd => Adpcm.LoopEnd;
        private short Looping => (short)(Adpcm.Looping ? 1 : 0);

        private int StartAddr => SampleToNibble(Adpcm.Looping ? LoopStart : 0);
        private int EndAddr => SampleToNibble(Adpcm.Looping ? LoopEnd : SampleCount - 1);
        private static int CurAddr => SampleToNibble(0);

        private int InterleaveSize => Configuration.BytesPerInterleave == 0 ?
            AudioDataSize : Configuration.BytesPerInterleave;
        private const int StreamInfoSize = 0x40;
        private int ChannelInfoSize => 0x60;
        private int HeaderSize => StreamInfoSize + ChannelCount * ChannelInfoSize;

        /// <summary>
        /// Size of a single channel's ADPCM audio data with padding when written to a file
        /// </summary>
        private int AudioDataSize => GetNextMultiple(SampleCountToByteCount(SampleCount),
            Configuration.BytesPerInterleave == 0 ? BytesPerFrame : InterleaveSize);

        protected override void SetupWriter(AudioData audio)
        {
            Adpcm = audio.GetFormat<GcAdpcmFormat>(new GcAdpcmParameters { Progress = Configuration.Progress });
        }

        protected override void WriteStream(Stream stream)
        {
            using (BinaryWriter writer = GetBinaryWriter(stream, Endianness.BigEndian))
            {
                stream.Position = 0;
                WriteHeader(writer);
                WriteData(writer);
            }
        }

        private void WriteHeader(BinaryWriter writer)
        {
            writer.WriteUTF8("IDSP");
            writer.Write(0);
            writer.Write(ChannelCount);
            writer.Write(Adpcm.SampleRate);
            writer.Write(SampleCount);
            writer.Write(LoopStart);
            writer.Write(LoopEnd);
            writer.Write(Configuration.BytesPerInterleave);
            writer.Write(StreamInfoSize);
            writer.Write(ChannelInfoSize);
            writer.Write(HeaderSize);
            writer.Write(AudioDataSize);

            for (int i = 0; i < ChannelCount; i++)
            {
                writer.BaseStream.Position = StreamInfoSize + i * ChannelInfoSize;
                var channel = Adpcm.Channels[i];

                writer.Write(channel.SampleCount);
                writer.Write(SampleCountToNibbleCount(channel.SampleCount));
                writer.Write(Adpcm.SampleRate);
                writer.Write(Looping);
                writer.Write((short)0);
                writer.Write(StartAddr);
                writer.Write(EndAddr);
                writer.Write(CurAddr);
                writer.Write(channel.Coefs.ToByteArray(Endianness.BigEndian));
                writer.Write(channel.Gain);
                writer.Write(channel.PredScale);
                writer.Write(channel.Hist1);
                writer.Write(channel.Hist2);
                writer.Write(channel.LoopPredScale);
                writer.Write(channel.LoopHist1);
                writer.Write(channel.LoopHist2);
            }
        }

        private void WriteData(BinaryWriter writer)
        {
            writer.BaseStream.Position = HeaderSize;

            byte[][] channels = Adpcm.Channels.Select(x => x.GetAdpcmAudio()).ToArray();
            channels.Interleave(writer.BaseStream, InterleaveSize, AudioDataSize);
        }
    }
}