﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using VGAudio.Formats;
using VGAudio.Formats.GcAdpcm;
using VGAudio.Utilities;
using static VGAudio.Formats.GcAdpcm.GcAdpcmHelpers;
using static VGAudio.Utilities.Helpers;

namespace VGAudio.Containers.Bxstm
{
    public class BCFstmReader
    {
        public BCFstmStructure ReadFile(Stream stream, bool readAudioData = true)
        {
            BCFstmType type;
            using (BinaryReader reader = GetBinaryReader(stream, Endianness.LittleEndian))
            {
                string magic = Encoding.UTF8.GetString(reader.ReadBytes(4), 0, 4);
                switch (magic)
                {
                    case "CSTM":
                        type = BCFstmType.Bcstm;
                        break;
                    case "FSTM":
                        type = BCFstmType.Bfstm;
                        break;
                    default:
                        throw new InvalidDataException("File has no CSTM or FSTM header");
                }
            }

            using (BinaryReader reader = GetBinaryReader(stream, GetTypeEndianess(type)))
            {
                BCFstmStructure structure = type == BCFstmType.Bcstm ? (BCFstmStructure)new BcstmStructure() : new BfstmStructure();

                ReadHeader(reader, structure);
                ReadInfoChunk(reader, structure);
                ReadSeekChunk(reader, structure);
                ReadDataChunk(reader, structure, readAudioData);

                return structure;
            }
        }

        public static IAudioFormat ToAudioStream(BCFstmStructure structure)
        {
            var channels = new GcAdpcmChannel[structure.ChannelCount];

            for (int c = 0; c < channels.Length; c++)
            {
                var channel = new GcAdpcmChannel(structure.SampleCount, structure.AudioData[c])
                {
                    Coefs = structure.Channels[c].Coefs,
                    Gain = structure.Channels[c].Gain,
                    Hist1 = structure.Channels[c].Hist1,
                    Hist2 = structure.Channels[c].Hist2
                };

                if (structure.SeekTable != null)
                {
                    channel.AddSeekTable(structure.SeekTable[c], structure.SamplesPerSeekTableEntry);
                }

                channel.SetLoopContext(structure.LoopStart, structure.Channels[c].LoopPredScale,
                    structure.Channels[c].LoopHist1, structure.Channels[c].LoopHist2);

                channels[c] = channel;
            }

            var adpcm = new GcAdpcmFormat(structure.SampleCount, structure.SampleRate, channels);
            adpcm.SetLoop(structure.Looping, structure.LoopStart, structure.SampleCount);
            adpcm.Tracks = structure.Tracks;

            return adpcm;
        }

        private static void ReadHeader(BinaryReader reader, BCFstmStructure structure)
        {
            reader.Expect((ushort)0xfeff);
            structure.HeaderSize = reader.ReadInt16();
            structure.Version = reader.ReadInt32() >> 16;
            structure.FileSize = reader.ReadInt32();

            if (reader.BaseStream.Length < structure.FileSize)
            {
                throw new InvalidDataException("Actual file length is less than stated length");
            }

            structure.HeaderSections = reader.ReadInt16();
            reader.BaseStream.Position += 2;

            for (int i = 0; i < structure.HeaderSections; i++)
            {
                int type = reader.ReadInt16();
                reader.BaseStream.Position += 2;
                switch (type)
                {
                    case 0x4000:
                        structure.InfoChunkOffset = reader.ReadInt32();
                        structure.InfoChunkSizeHeader = reader.ReadInt32();
                        break;
                    case 0x4001:
                        structure.SeekChunkOffset = reader.ReadInt32();
                        structure.SeekChunkSizeHeader = reader.ReadInt32();
                        break;
                    case 0x4002:
                        structure.DataChunkOffset = reader.ReadInt32();
                        structure.DataChunkSizeHeader = reader.ReadInt32();
                        break;
                    case 0x4003:
                        structure.RegnChunkOffset = reader.ReadInt32();
                        structure.RegnChunkSizeHeader = reader.ReadInt32();
                        break;
                    case 0x4004:
                        structure.PdatChunkOffset = reader.ReadInt32();
                        structure.PdatChunkSizeHeader = reader.ReadInt32();
                        break;
                    default:
                        throw new InvalidDataException($"Unknown section type {type}");
                }
            }
        }

        private static void ReadInfoChunk(BinaryReader reader, BCFstmStructure structure)
        {
            reader.BaseStream.Position = structure.InfoChunkOffset;
            if (Encoding.UTF8.GetString(reader.ReadBytes(4), 0, 4) != "INFO")
            {
                throw new InvalidDataException("Unknown or invalid INFO chunk");
            }

            structure.InfoChunkSize = reader.ReadInt32();
            if (structure.InfoChunkSize != structure.InfoChunkSizeHeader)
            {
                throw new InvalidDataException("INFO chunk size in CSTM header doesn't match size in INFO header");
            }

            reader.Expect((short)0x4100);
            reader.BaseStream.Position += 2;
            structure.InfoChunk1Offset = reader.ReadInt32();
            reader.Expect((short)0x0101, (short)0);
            reader.BaseStream.Position += 2;
            structure.InfoChunk2Offset = reader.ReadInt32();
            reader.Expect((short)0x0101);
            reader.BaseStream.Position += 2;
            structure.InfoChunk3Offset = reader.ReadInt32();

            ReadInfoChunk1(reader, structure);
            ReadInfoChunk2(reader, structure);
            ReadInfoChunk3(reader, structure);
        }

        private static void ReadInfoChunk1(BinaryReader reader, BCFstmStructure structure)
        {
            reader.BaseStream.Position = structure.InfoChunkOffset + 8 + structure.InfoChunk1Offset;
            structure.Codec = (BxstmCodec)reader.ReadByte();
            if (structure.Codec != BxstmCodec.Adpcm)
            {
                throw new NotSupportedException("File must contain 4-bit ADPCM encoded audio");
            }

            structure.Looping = reader.ReadByte() == 1;
            structure.ChannelCount = reader.ReadByte();
            reader.BaseStream.Position += 1;

            structure.SampleRate = reader.ReadInt32();

            structure.LoopStart = reader.ReadInt32();
            structure.SampleCount = reader.ReadInt32();

            structure.InterleaveCount = reader.ReadInt32();
            structure.InterleaveSize = reader.ReadInt32();
            structure.SamplesPerInterleave = reader.ReadInt32();
            structure.LastBlockSizeWithoutPadding = reader.ReadInt32();
            structure.LastBlockSamples = reader.ReadInt32();
            structure.LastBlockSize = reader.ReadInt32();
            structure.BytesPerSeekTableEntry = reader.ReadInt32();
            structure.SamplesPerSeekTableEntry = reader.ReadInt32();

            reader.Expect((short)0x1f00);
            reader.BaseStream.Position += 2;
            structure.AudioDataOffset = reader.ReadInt32() + structure.DataChunkOffset + 8;
            structure.InfoPart1Extra = reader.ReadInt16() == 0x100;
            if (structure.InfoPart1Extra)
            {
                reader.BaseStream.Position += 10;
            }
            if (structure.Version == 4)
            {
                structure.LoopStartUnaligned = reader.ReadInt32();
                structure.LoopEndUnaligned = reader.ReadInt32();
            }
        }

        private static void ReadInfoChunk2(BinaryReader reader, BCFstmStructure structure)
        {
            if (structure.InfoChunk2Offset == -1)
            {
                structure.IncludeTracks = false;
                return;
            }

            structure.IncludeTracks = true;
            int part2Offset = structure.InfoChunkOffset + 8 + structure.InfoChunk2Offset;
            reader.BaseStream.Position = part2Offset;

            int numTracks = reader.ReadInt32();

            int[] trackOffsets = new int[numTracks];
            for (int i = 0; i < numTracks; i++)
            {
                reader.Expect((short)0x4101);
                reader.BaseStream.Position += 2;
                trackOffsets[i] = reader.ReadInt32();
            }

            foreach (int offset in trackOffsets)
            {
                reader.BaseStream.Position = part2Offset + offset;

                var track = new GcAdpcmTrack();
                track.Volume = reader.ReadByte();
                track.Panning = reader.ReadByte();
                reader.BaseStream.Position += 2;

                reader.BaseStream.Position += 8;
                track.ChannelCount = reader.ReadInt32();
                track.ChannelLeft = reader.ReadByte();
                track.ChannelRight = reader.ReadByte();
                structure.Tracks.Add(track);
            }
        }

        private static void ReadInfoChunk3(BinaryReader reader, BCFstmStructure structure)
        {
            int part3Offset = structure.InfoChunkOffset + 8 + structure.InfoChunk3Offset;
            reader.BaseStream.Position = part3Offset;

            reader.Expect(structure.ChannelCount);

            for (int i = 0; i < structure.ChannelCount; i++)
            {
                var channel = new BxstmChannelInfo();
                reader.Expect((short)0x4102);
                reader.BaseStream.Position += 2;
                channel.Offset = reader.ReadInt32();
                structure.Channels.Add(channel);
            }

            foreach (BxstmChannelInfo channel in structure.Channels)
            {
                int channelInfoOffset = part3Offset + channel.Offset;
                reader.BaseStream.Position = channelInfoOffset;
                reader.Expect((short)0x0300);
                reader.BaseStream.Position += 2;
                int coefsOffset = reader.ReadInt32() + channelInfoOffset;
                reader.BaseStream.Position = coefsOffset;

                channel.Coefs = Enumerable.Range(0, 16).Select(x => reader.ReadInt16()).ToArray();
                channel.PredScale = reader.ReadInt16();
                channel.Hist1 = reader.ReadInt16();
                channel.Hist2 = reader.ReadInt16();
                channel.LoopPredScale = reader.ReadInt16();
                channel.LoopHist1 = reader.ReadInt16();
                channel.LoopHist2 = reader.ReadInt16();
                channel.Gain = reader.ReadInt16();
            }
        }

        private static void ReadSeekChunk(BinaryReader reader, BCFstmStructure structure)
        {
            reader.BaseStream.Position = structure.SeekChunkOffset;

            if (Encoding.UTF8.GetString(reader.ReadBytes(4), 0, 4) != "SEEK")
            {
                throw new InvalidDataException("Unknown or invalid SEEK chunk");
            }
            structure.SeekChunkSize = reader.ReadInt32();

            if (structure.SeekChunkSizeHeader != structure.SeekChunkSize)
            {
                throw new InvalidDataException("SEEK chunk size in header doesn't match size in SEEK header");
            }

            int bytesPerEntry = 4 * structure.ChannelCount;
            int numSeekTableEntries = structure.SampleCount.DivideByRoundUp(structure.SamplesPerSeekTableEntry);

            structure.SeekTableSize = bytesPerEntry * numSeekTableEntries;

            byte[] tableBytes = reader.ReadBytes(structure.SeekTableSize);

            structure.SeekTable = tableBytes.ToShortArray()
                .DeInterleave(2, structure.ChannelCount);
        }

        private static void ReadDataChunk(BinaryReader reader, BCFstmStructure structure, bool readAudioData)
        {
            reader.BaseStream.Position = structure.DataChunkOffset;

            if (Encoding.UTF8.GetString(reader.ReadBytes(4), 0, 4) != "DATA")
            {
                throw new InvalidDataException("Unknown or invalid DATA chunk");
            }
            structure.DataChunkSize = reader.ReadInt32();

            if (structure.DataChunkSizeHeader != structure.DataChunkSize)
            {
                throw new InvalidDataException("DATA chunk size in header doesn't match size in DATA header");
            }

            if (!readAudioData) return;

            reader.BaseStream.Position = structure.AudioDataOffset;
            int audioDataLength = structure.DataChunkSize - (structure.AudioDataOffset - structure.DataChunkOffset);

            structure.AudioData = reader.BaseStream.DeInterleave(audioDataLength, structure.InterleaveSize,
                structure.ChannelCount, SampleCountToByteCount(structure.SampleCount));
        }

        private enum BCFstmType
        {
            Bcstm,
            Bfstm
        }

        private static Endianness GetTypeEndianess(BCFstmType type) =>
            type == BCFstmType.Bcstm ? Endianness.LittleEndian : Endianness.BigEndian;
    }
}