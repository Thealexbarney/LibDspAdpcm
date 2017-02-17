﻿using System;
using System.Collections.Generic;

namespace DspAdpcm.Formats.GcAdpcm
{
    public class GcAdpcmChannel
    {
        public byte[] AudioData { get; }
        public int SampleCount { get; }

        public short Gain { get; set; }
        public short[] Coefs { get; set; }
        public short PredScale => AudioData[0];
        public short Hist1 { get; set; }
        public short Hist2 { get; set; }

        public short LoopPredScale(int loopStart, bool ensureSelfCalculated = false) => LoopContext.PredScale(loopStart, ensureSelfCalculated);
        public short LoopHist1(int loopStart, bool ensureSelfCalculated = false) => LoopContext.Hist1(loopStart, ensureSelfCalculated);
        public short LoopHist2(int loopStart, bool ensureSelfCalculated = false) => LoopContext.Hist2(loopStart, ensureSelfCalculated);

        public List<GcAdpcmSeekTable> SeekTable { get; } = new List<GcAdpcmSeekTable>();
        private GcAdpcmLoopContext LoopContext { get; }
        public GcAdpcmAlignment Alignment { get; set; }

        public GcAdpcmChannel(int sampleCount)
        {
            SampleCount = sampleCount;
            AudioData = new byte[GcAdpcmHelpers.SampleCountToByteCount(sampleCount)];
            LoopContext = new GcAdpcmLoopContext(this);
        }

        public GcAdpcmChannel(int sampleCount, byte[] audio)
        {
            if (audio.Length < GcAdpcmHelpers.SampleCountToByteCount(sampleCount))
            {
                throw new ArgumentException("Audio array length is too short for the specified number of samples.");
            }

            SampleCount = sampleCount;
            AudioData = audio;
            LoopContext = new GcAdpcmLoopContext(this);
        }

        public byte[] GetAudioData()
        {
            return AudioData;
        }

        public void SetLoopContext(int loopStart, short predScale, short hist1, short hist2)
            => LoopContext.AddLoopContext(loopStart, predScale, hist1, hist2);
    }
}