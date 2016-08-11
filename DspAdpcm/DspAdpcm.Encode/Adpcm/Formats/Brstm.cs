﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DspAdpcm.Encode.Helpers;

namespace DspAdpcm.Encode.Adpcm.Formats
{
    public class Brstm
    {
        public AdpcmStream AudioStream { get; set; }

        public BrstmConfiguration Configuration { get; } = new BrstmConfiguration();

        private int NumSamples => AudioStream.NumSamples;
        private int NumChannels => AudioStream.Channels.Count;
        private int NumTracks => AudioStream.Tracks.Count;
        private byte Codec { get; } = 2; // 4-bit ADPCM
        private byte Looping => (byte)(AudioStream.Looping ? 1 : 0);
        private int AudioDataOffset => DataChunkOffset + 0x20;
        private int InterleaveSize => GetBytesForAdpcmSamples(SamplesPerInterleave);
        private int SamplesPerInterleave => Configuration.SamplesPerInterleave;
        private int InterleaveCount => (NumSamples / SamplesPerInterleave) + (FullLastBlock ? 0 : 1);
        private int LastBlockSizeWithoutPadding => GetBytesForAdpcmSamples(LastBlockSamples);
        private int LastBlockSamples => FullLastBlock ? SamplesPerInterleave : NumSamples % SamplesPerInterleave;
        private int LastBlockSize => GetNextMultiple(LastBlockSizeWithoutPadding, 0x20);
        private bool FullLastBlock => NumSamples % SamplesPerInterleave == 0 && NumChannels > 0;
        private int SamplesPerAdpcEntry => Configuration.SamplesPerAdpcEntry;
        private bool FullLastAdpcEntry => NumSamples % SamplesPerAdpcEntry == 0 && NumSamples > 0;
        private int NumAdpcEntriesShortened => (GetBytesForAdpcmSamples(NumSamples) / SamplesPerAdpcEntry) + 1;
        private int NumAdpcEntries => Configuration.SeekTableType == SeekTableType.Standard ?
            (NumSamples / SamplesPerAdpcEntry) + (FullLastAdpcEntry ? 0 : 1) : NumAdpcEntriesShortened;
        private int BytesPerAdpcEntry => 4; //Or is it bits per sample?

        private int RstmHeaderLength => 0x40;

        private int HeadChunkOffset => RstmHeaderLength;
        private int HeadChunkLength => GetNextMultiple(HeadChunkHeaderLength + HeadChunkTableLength +
            HeadChunk1Length + HeadChunk2Length + HeadChunk3Length, 0x20);
        private int HeadChunkHeaderLength = 8;
        private int HeadChunkTableLength => 8 * 3;
        private int HeadChunk1Length => 0x34;
        private int HeadChunk2Length => 4 + (8 * NumTracks) + (TrackInfoLength * NumTracks);
        private BrstmType HeaderType => Configuration.HeaderType;
        private int TrackInfoLength => HeaderType == BrstmType.SSBB ? 4 : 0x0c;
        private int HeadChunk3Length => 4 + (8 * NumChannels) + (ChannelInfoLength * NumChannels);
        private int ChannelInfoLength => 0x38;

        private int AdpcChunkOffset => RstmHeaderLength + HeadChunkLength;
        private int AdpcChunkLength => GetNextMultiple(8 + NumAdpcEntries * NumChannels * BytesPerAdpcEntry, 0x20);

        private int DataChunkOffset => RstmHeaderLength + HeadChunkLength + AdpcChunkLength;
        private int DataChunkLength => GetNextMultiple(0x20 + (InterleaveCount - (LastBlockSamples == 0 ? 0 : 1)) * InterleaveSize * NumChannels + LastBlockSize * NumChannels, 0x20);

        private int FileLength => RstmHeaderLength + HeadChunkLength + AdpcChunkLength + DataChunkLength;

        public Brstm(AdpcmStream stream)
        {
            if (stream.Channels.Count < 1)
            {
                throw new InvalidDataException("Stream must have at least one channel ");
            }

            AudioStream = stream;
        }

        public Brstm(Stream stream)
        {
            ReadBrstmFile(stream);
        }

        public IEnumerable<byte> GetFile()
        {
            return Combine(GetRstmHeader(), GetHeadChunk(), GetAdpcChunk(), GetDataChunk());
        }

        private byte[] GetRstmHeader()
        {
            var header = new List<byte>();

            header.Add32("RSTM");
            header.Add16BE(0xfeff); //Endianness
            header.Add16BE(0x0100); //BRSTM format version
            header.Add32BE(FileLength);
            header.Add16BE(RstmHeaderLength);
            header.Add16BE(2); // NumEntries
            header.Add32BE(HeadChunkOffset);
            header.Add32BE(HeadChunkLength);
            header.Add32BE(AdpcChunkOffset);
            header.Add32BE(AdpcChunkLength);
            header.Add32BE(DataChunkOffset);
            header.Add32BE(DataChunkLength);

            header.AddRange(new byte[RstmHeaderLength - header.Count]);

            return header.ToArray();
        }

        private byte[] GetHeadChunk()
        {
            var chunk = new List<byte>();

            chunk.Add32("HEAD");
            chunk.Add32BE(HeadChunkLength);
            chunk.AddRange(GetHeadChunkHeader());
            chunk.AddRange(GetHeadChunk1());
            chunk.AddRange(GetHeadChunk2());
            chunk.AddRange(GetHeadChunk3());

            chunk.AddRange(new byte[HeadChunkLength - chunk.Count]);

            return chunk.ToArray();
        }

        private byte[] GetHeadChunkHeader()
        {
            var chunk = new List<byte>();

            chunk.Add32BE(0x01000000);
            chunk.Add32BE(HeadChunkTableLength); //Chunk 1 offset
            chunk.Add32BE(0x01000000);
            chunk.Add32BE(HeadChunkTableLength + HeadChunk1Length); //Chunk 1 offset
            chunk.Add32BE(0x01000000);
            chunk.Add32BE(HeadChunkTableLength + HeadChunk1Length + HeadChunk2Length); //Chunk 3 offset

            return chunk.ToArray();
        }

        private byte[] GetHeadChunk1()
        {
            var chunk = new List<byte>();

            chunk.Add(Codec);
            chunk.Add(Looping);
            chunk.Add((byte)NumChannels);
            chunk.Add(0); //padding
            chunk.Add16BE(AudioStream.SampleRate);
            chunk.Add16BE(0); //padding
            chunk.Add32BE(AudioStream.LoopStart);
            chunk.Add32BE(AudioStream.NumSamples);
            chunk.Add32BE(AudioDataOffset);
            chunk.Add32BE(InterleaveCount);
            chunk.Add32BE(InterleaveSize);
            chunk.Add32BE(SamplesPerInterleave);
            chunk.Add32BE(LastBlockSizeWithoutPadding);
            chunk.Add32BE(LastBlockSamples);
            chunk.Add32BE(LastBlockSize);
            chunk.Add32BE(SamplesPerAdpcEntry);
            chunk.Add32BE(BytesPerAdpcEntry);

            return chunk.ToArray();
        }

        private byte[] GetHeadChunk2()
        {
            var chunk = new List<byte>();

            chunk.Add((byte)NumTracks);
            chunk.Add((byte)(HeaderType == BrstmType.SSBB ? 0 : 1));
            chunk.Add16BE(0);

            int baseOffset = HeadChunkTableLength + HeadChunk1Length + 4;
            int offsetTableLength = NumTracks * 8;

            for (int i = 0; i < NumTracks; i++)
            {
                chunk.Add32BE(HeaderType == BrstmType.SSBB ? 0x01000000 : 0x01010000);
                chunk.Add32BE(baseOffset + offsetTableLength + TrackInfoLength * i);
            }

            foreach (AdpcmTrack track in AudioStream.Tracks)
            {
                if (HeaderType == BrstmType.Other)
                {
                    chunk.Add((byte)track.Volume);
                    chunk.Add((byte)track.Panning);
                    chunk.Add16BE(0);
                    chunk.Add32BE(0);
                }
                chunk.Add((byte)track.NumChannels);
                chunk.Add((byte)track.ChannelLeft); //First channel ID
                chunk.Add((byte)track.ChannelRight); //Second channel ID
                chunk.Add(0);
            }

            chunk.AddRange(new byte[HeadChunk2Length - chunk.Count]);

            return chunk.ToArray();
        }

        private byte[] GetHeadChunk3()
        {
            var chunk = new List<byte>();

            chunk.Add((byte)NumChannels);
            chunk.Add(0); //padding
            chunk.Add16BE(0); //padding

            int baseOffset = HeadChunkTableLength + HeadChunk1Length + HeadChunk2Length + 4;
            int offsetTableLength = NumChannels * 8;

            if (AudioStream.Looping)
            {
                Parallel.ForEach(AudioStream.Channels, x => x.SetLoopContext(AudioStream.LoopStart));
            }

            for (int i = 0; i < NumChannels; i++)
            {
                chunk.Add32BE(0x01000000);
                chunk.Add32BE(baseOffset + offsetTableLength + ChannelInfoLength * i);
            }

            for (int i = 0; i < NumChannels; i++)
            {
                AdpcmChannel channel = AudioStream.Channels[i];
                chunk.Add32BE(0x01000000);
                chunk.Add32BE(baseOffset + offsetTableLength + ChannelInfoLength * i + 8);
                chunk.AddRange(channel.Coefs.SelectMany(x => x.ToBytesBE()));
                chunk.Add16BE(channel.Gain);
                chunk.Add16BE(channel.AudioData.First());
                chunk.Add16BE(channel.Hist1);
                chunk.Add16BE(channel.Hist2);
                chunk.Add16BE(AudioStream.Looping ? channel.LoopPredScale : channel.AudioData.First());
                chunk.Add16BE(AudioStream.Looping ? channel.LoopHist1 : 0);
                chunk.Add16BE(AudioStream.Looping ? channel.LoopHist2 : 0);
                chunk.Add16(0);
            }

            return chunk.ToArray();
        }

        private byte[] GetAdpcChunk()
        {
            var chunk = new List<byte>();

            chunk.Add32("ADPC");
            chunk.Add32BE(AdpcChunkLength);

            if (Configuration.RecalculateSeekTable)
            {
                Encode.CalculateAdpcTable(AudioStream.Channels, SamplesPerAdpcEntry);
            }

            chunk.AddRange(Encode.BuildAdpcTable(AudioStream.Channels, SamplesPerAdpcEntry, NumAdpcEntries));

            chunk.AddRange(new byte[AdpcChunkLength - chunk.Count]);

            return chunk.ToArray();
        }

        private byte[] GetDataChunk()
        {
            var chunk = new List<byte>();

            chunk.Add32("DATA");
            chunk.Add32BE(DataChunkLength);
            chunk.Add32BE(0x18);
            chunk.AddRange(new byte[0x14]); //Pad to 0x20 bytes

            var channels = AudioStream.Channels.Select(x => x.AudioData.ToArray()).ToArray();
            chunk.AddRange(channels.Interleave(InterleaveSize, LastBlockSize));

            chunk.AddRange(new byte[DataChunkLength - chunk.Count]);

            return chunk.ToArray();
        }

        private void ReadBrstmFile(Stream stream)
        {
            using (var reader = new BinaryReaderBE(stream))
            {
                if (Encoding.UTF8.GetString(reader.ReadBytes(4), 0, 4) != "RSTM")
                {
                    throw new InvalidDataException("File has no RSTM header");
                }

                var structure = new BrstmStructure();

                reader.BaseStream.Position = 8;
                structure.FileLength = reader.ReadInt32BE();

                if (structure.FileLength != stream.Length)
                {
                    throw new InvalidDataException("Stated file length doesn't match actual length");
                }

                structure.RstmHeaderLength = reader.ReadInt16BE();
                reader.BaseStream.Position += 2;

                structure.HeadChunkOffset = reader.ReadInt32BE();
                structure.HeadChunkLengthRstm = reader.ReadInt32BE();
                structure.AdpcChunkOffset = reader.ReadInt32BE();
                structure.AdpcChunkLengthRstm = reader.ReadInt32BE();
                structure.DataChunkOffset = reader.ReadInt32BE();
                structure.DataChunkLengthRstm = reader.ReadInt32BE();

                reader.BaseStream.Position = structure.HeadChunkOffset;
                byte[] headChunk = reader.ReadBytes(structure.HeadChunkLengthRstm);
                ParseHeadChunk(headChunk, structure);

                Configuration.SamplesPerInterleave = structure.SamplesPerInterleave;
                Configuration.SamplesPerAdpcEntry = structure.SamplesPerAdpcEntry;
                Configuration.HeaderType = structure.HeaderType;

                AudioStream = new AdpcmStream(structure.NumSamples, structure.SampleRate);
                if (structure.Looping)
                {
                    AudioStream.SetLoop(structure.LoopStart, structure.NumSamples);
                }
                AudioStream.Tracks = structure.Tracks;

                ParseAdpcChunk(stream, structure);
                ParseDataChunk(stream, structure);

                Configuration.SeekTableType = structure.SeekTableType;
            }
        }

        private static void ParseHeadChunk(byte[] head, BrstmStructure structure)
        {
            int baseOffset = 8;

            if (Encoding.UTF8.GetString(head, 0, 4) != "HEAD")
            {
                throw new InvalidDataException("Unknown or invalid HEAD chunk");
            }

            using (var reader = new BinaryReaderBE(new MemoryStream(head)))
            {
                reader.BaseStream.Position = 4;
                structure.HeadChunkLength = reader.ReadInt32BE();
                if (structure.HeadChunkLength != head.Length)
                {
                    throw new InvalidDataException("HEAD chunk stated length does not match actual length");
                }

                reader.BaseStream.Position += 4;
                structure.HeadChunk1Offset = reader.ReadInt32BE();
                reader.BaseStream.Position += 4;
                structure.HeadChunk2Offset = reader.ReadInt32BE();
                reader.BaseStream.Position += 4;
                structure.HeadChunk3Offset = reader.ReadInt32BE();

                reader.BaseStream.Position = structure.HeadChunk1Offset + baseOffset;
                byte[] headChunk1 = reader.ReadBytes(structure.HeadChunk1Length);

                reader.BaseStream.Position = structure.HeadChunk2Offset + baseOffset;
                byte[] headChunk2 = reader.ReadBytes(structure.HeadChunk2Length);

                reader.BaseStream.Position = structure.HeadChunk3Offset + baseOffset;
                byte[] headChunk3 = reader.ReadBytes(structure.HeadChunk3Length);

                ParseHeadChunk1(headChunk1, structure);
                ParseHeadChunk2(headChunk2, structure);
                ParseHeadChunk3(headChunk3, structure);
            }
        }

        private static void ParseHeadChunk1(byte[] chunk, BrstmStructure structure)
        {
            using (var reader = new BinaryReaderBE(new MemoryStream(chunk)))
            {
                structure.Codec = reader.ReadByte();
                if (structure.Codec != 2) //4-bit ADPCM codec
                {
                    throw new InvalidDataException("File must contain 4-bit ADPCM encoded audio");
                }

                structure.Looping = reader.ReadByte() == 1;
                structure.NumChannelsChunk1 = reader.ReadByte();
                reader.BaseStream.Position += 1;

                structure.SampleRate = reader.ReadInt16BE();
                reader.BaseStream.Position += 2;

                structure.LoopStart = reader.ReadInt32BE();
                structure.NumSamples = reader.ReadInt32BE();

                structure.AudioDataOffset = reader.ReadInt32BE();
                structure.InterleaveCount = reader.ReadInt32BE();
                structure.InterleaveSize = reader.ReadInt32BE();
                structure.SamplesPerInterleave = reader.ReadInt32BE();
                structure.LastBlockSizeWithoutPadding = reader.ReadInt32BE();
                structure.LastBlockSamples = reader.ReadInt32BE();
                structure.LastBlockSize = reader.ReadInt32BE();
                structure.SamplesPerAdpcEntry = reader.ReadInt32BE();
            }
        }

        private static void ParseHeadChunk2(byte[] chunk, BrstmStructure structure)
        {
            using (var reader = new BinaryReaderBE(new MemoryStream(chunk)))
            {
                int numTracks = reader.ReadByte();
                int[] trackOffsets = new int[numTracks];

                structure.HeaderType = reader.ReadByte() == 0 ? BrstmType.SSBB : BrstmType.Other;

                reader.BaseStream.Position = 4;
                for (int i = 0; i < numTracks; i++)
                {
                    reader.BaseStream.Position += 4;
                    trackOffsets[i] = reader.ReadInt32BE();
                }

                for (int i = 0; i < numTracks; i++)
                {
                    reader.BaseStream.Position = trackOffsets[i] - structure.HeadChunk2Offset;
                    var track = new AdpcmTrack();

                    if (structure.HeaderType == BrstmType.Other)
                    {
                        track.Volume = reader.ReadByte();
                        track.Panning = reader.ReadByte();
                        reader.BaseStream.Position += 6;
                    }

                    track.NumChannels = reader.ReadByte();
                    track.ChannelLeft = reader.ReadByte();
                    track.ChannelRight = reader.ReadByte();

                    structure.Tracks.Add(track);
                }
            }
        }

        private static void ParseHeadChunk3(byte[] chunk, BrstmStructure structure)
        {
            using (var reader = new BinaryReaderBE(new MemoryStream(chunk)))
            {
                structure.NumChannelsChunk3 = reader.ReadByte();
                reader.BaseStream.Position += 3;

                for (int i = 0; i < structure.NumChannelsChunk3; i++)
                {
                    var channel = new ChannelInfo();
                    reader.BaseStream.Position += 4;
                    channel.Offset = reader.ReadInt32BE();
                    structure.Channels.Add(channel);
                }

                foreach (ChannelInfo channel in structure.Channels)
                {
                    reader.BaseStream.Position = channel.Offset - structure.HeadChunk3Offset + 4;
                    int coefsOffset = reader.ReadInt32BE();
                    reader.BaseStream.Position = coefsOffset - structure.HeadChunk3Offset;

                    channel.Coefs = Enumerable.Range(0, 16).Select(x => reader.ReadInt16BE()).ToArray();
                    channel.Gain = reader.ReadInt16BE();
                    channel.PredScale = reader.ReadInt16BE();
                    channel.Hist1 = reader.ReadInt16BE();
                    channel.Hist2 = reader.ReadInt16BE();
                    channel.LoopPredScale = reader.ReadInt16BE();
                    channel.LoopHist1 = reader.ReadInt16BE();
                    channel.LoopHist2 = reader.ReadInt16BE();
                }
            }
        }

        private static void ParseAdpcChunk(Stream chunk, BrstmStructure structure)
        {
            var reader = new BinaryReaderBE(chunk);
            reader.BaseStream.Position = structure.AdpcChunkOffset;

            if (Encoding.UTF8.GetString(reader.ReadBytes(4), 0, 4) != "ADPC")
            {
                throw new InvalidDataException("Unknown or invalid ADPC chunk");
            }
            structure.AdpcChunkLength = reader.ReadInt32BE();

            if (structure.AdpcChunkLengthRstm != structure.AdpcChunkLength)
            {
                throw new InvalidDataException("ADPC chunk length in RSTM header doesn't match length in ADPC header");
            }

            bool fullLastAdpcEntry = structure.NumSamples % structure.SamplesPerAdpcEntry == 0 && structure.NumSamples > 0;
            int bytesPerEntry = 4 * structure.NumChannelsChunk1;
            int numAdpcEntriesShortened = (GetBytesForAdpcmSamples(structure.NumSamples) / structure.SamplesPerAdpcEntry) + 1;
            int numAdpcEntriesStandard = (structure.NumSamples / structure.SamplesPerAdpcEntry) + (fullLastAdpcEntry ? 0 : 1);

            //Chunk pads to 0x20 bytes and has 8 header bytes, so check if the length's within that range.
            if (Math.Abs(structure.AdpcChunkLength - bytesPerEntry * numAdpcEntriesStandard - 0x14) < 0x14)
            {
                structure.AdpcTableLength = bytesPerEntry * numAdpcEntriesStandard;
                structure.SeekTableType = SeekTableType.Standard;
            }
            else if (Math.Abs(structure.AdpcChunkLength - bytesPerEntry * numAdpcEntriesShortened - 0x14) < 0x14)
            {
                structure.AdpcTableLength = bytesPerEntry * numAdpcEntriesShortened;
                structure.SeekTableType = SeekTableType.Short;
            }
            else
            {
                return; //Unknown format. Don't parse table
            }

            byte[] tableBytes = reader.ReadBytes(structure.AdpcTableLength);
            short[] table = new short[(int)Math.Ceiling((double)tableBytes.Length / 2)];
            Buffer.BlockCopy(tableBytes, 0, table, 0, tableBytes.Length);

            structure.SeekTable = table.Select(x => x.FlipBytes()).ToArray()
                .DeInterleave(2, structure.NumChannelsChunk1);
        }

        private void ParseDataChunk(Stream chunk, BrstmStructure structure)
        {
            var reader = new BinaryReaderBE(chunk);
            reader.BaseStream.Position = structure.DataChunkOffset;

            if (Encoding.UTF8.GetString(reader.ReadBytes(4), 0, 4) != "DATA")
            {
                throw new InvalidDataException("Unknown or invalid DATA chunk");
            }
            structure.DataChunkLength = reader.ReadInt32BE();

            if (structure.DataChunkLengthRstm != structure.DataChunkLength)
            {
                throw new InvalidDataException("DATA chunk length in RSTM header doesn't match length in DATA header");
            }

            reader.BaseStream.Position = structure.AudioDataOffset;
            int audioDataLength = structure.DataChunkLength - (structure.AudioDataOffset - structure.DataChunkOffset);

            List<AdpcmChannel> channels = structure.Channels.Select(channelInfo =>
                new AdpcmChannel(structure.NumSamples)
                {
                    Coefs = channelInfo.Coefs,
                    Gain = channelInfo.Gain,
                    Hist1 = channelInfo.Hist1,
                    Hist2 = channelInfo.Hist2
                })
                .ToList();

            byte[] audioData = reader.ReadBytes(audioDataLength);

            byte[][] deInterleavedAudioData = audioData.DeInterleave(structure.InterleaveSize, structure.NumChannelsChunk1,
                structure.LastBlockSize, GetBytesForAdpcmSamples(structure.NumSamples));

            for (int c = 0; c < structure.NumChannelsChunk1; c++)
            {
                channels[c].AudioByteArray = deInterleavedAudioData[c];
                channels[c].SeekTable = structure.SeekTable[c];
                channels[c].SamplesPerSeekTableEntry = structure.SamplesPerAdpcEntry;
            }

            foreach (AdpcmChannel channel in channels)
            {
                AudioStream.Channels.Add(channel);
            }
        }

        private class BrstmStructure
        {
            public int FileLength { get; set; }
            public int RstmHeaderLength { get; set; }
            public int HeadChunkOffset { get; set; }
            public int HeadChunkLengthRstm { get; set; }
            public int AdpcChunkOffset { get; set; }
            public int AdpcChunkLengthRstm { get; set; }
            public int DataChunkOffset { get; set; }
            public int DataChunkLengthRstm { get; set; }

            public int HeadChunkLength { get; set; }
            public int HeadChunk1Offset { get; set; }
            public int HeadChunk2Offset { get; set; }
            public int HeadChunk3Offset { get; set; }
            public int HeadChunk1Length => HeadChunk2Offset - HeadChunk1Offset;
            public int HeadChunk2Length => HeadChunk3Offset - HeadChunk2Offset;
            public int HeadChunk3Length => HeadChunkLength - HeadChunk3Offset;

            public int Codec { get; set; }
            public bool Looping { get; set; }
            public int NumChannelsChunk1 { get; set; }
            public int SampleRate { get; set; }
            public int LoopStart { get; set; }
            public int NumSamples { get; set; }
            public int AudioDataOffset { get; set; }
            public int InterleaveCount { get; set; }
            public int InterleaveSize { get; set; }
            public int SamplesPerInterleave { get; set; }
            public int LastBlockSizeWithoutPadding { get; set; }
            public int LastBlockSamples { get; set; }
            public int LastBlockSize { get; set; }
            public int SamplesPerAdpcEntry { get; set; }

            public BrstmType HeaderType { get; set; } = BrstmType.SSBB;
            public List<AdpcmTrack> Tracks { get; set; } = new List<AdpcmTrack>();

            public int NumChannelsChunk3 { get; set; }
            public List<ChannelInfo> Channels { get; set; } = new List<ChannelInfo>();

            public int AdpcChunkLength { get; set; }
            public int AdpcTableLength { get; set; }
            public short[][] SeekTable { get; set; }
            public SeekTableType SeekTableType { get; set; } = SeekTableType.Standard;

            public int DataChunkLength { get; set; }
        }

        private class ChannelInfo
        {
            public int Offset { get; set; }

            public short[] Coefs { get; set; }

            public short Gain { get; set; }
            public short PredScale { get; set; }
            public short Hist1 { get; set; }
            public short Hist2 { get; set; }

            public short LoopPredScale { get; set; }
            public short LoopHist1 { get; set; }
            public short LoopHist2 { get; set; }
        }

        public enum BrstmType
        {
            /// <summary>
            /// The header type used in Super Smash Bros. Brawl
            /// </summary>
            SSBB,
            /// <summary>
            /// The header type used in most games other than Super Smash Bros. Brawl
            /// </summary>
            Other
        }

        public enum SeekTableType
        {
            /// <summary>
            /// A normal length seek table.
            /// </summary>
            Standard,
            /// <summary>
            /// A shortened seek table used in games including Pokémon Battle Revolution and Mario Party 8.
            /// </summary>
            Short
        }

        public class BrstmConfiguration
        {
            private int _samplesPerInterleave = 0x3800;
            private int _samplesPerAdpcEntry = 0x3800;
            public BrstmType HeaderType { get; set; } = BrstmType.SSBB;
            public SeekTableType SeekTableType { get; set; } = SeekTableType.Standard;
            public bool RecalculateSeekTable { get; set; } = true;

            public int SamplesPerInterleave
            {
                get { return _samplesPerInterleave; }
                set
                {
                    if (value < 1)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value), value,
                            "Number of samples per interleave must be positive");
                    }
                    if (value % SamplesPerBlock != 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value), value,
                            "Number of samples per interleave must be divisible by 14");
                    }
                    _samplesPerInterleave = value;
                }
            }

            public int SamplesPerAdpcEntry
            {
                get { return _samplesPerAdpcEntry; }
                set
                {
                    if (value < 2)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value), value,
                            "Number of samples per interleave must be 2 or greater");
                    }
                    _samplesPerAdpcEntry = value;
                }
            }
        }
    }
}
