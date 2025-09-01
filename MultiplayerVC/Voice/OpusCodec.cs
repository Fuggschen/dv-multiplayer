using System;
using System.Buffers;
using Concentus;
using Concentus.Enums;
using Concentus.Structs;

namespace MultiplayerVC.Voice
{
    public sealed class OpusEncoderWrapper : IDisposable
    {
        public const int DefaultSampleRate = 48000;
        public const int DefaultFrameMs = 20;
        public const int DefaultFrameSamples = DefaultSampleRate * DefaultFrameMs / 1000; // 960 per 20ms mono

        private readonly OpusEncoder _encoder;
        private readonly int _channels;
        private bool _disposed;

        public OpusEncoderWrapper(int sampleRate = DefaultSampleRate, int channels = 1, int bitrate = 24000)
        {
            _channels = channels;
            // Concentus 2.x uses constructors (Create(...) removed)
            _encoder = (OpusEncoder)OpusCodecFactory.CreateEncoder(DefaultSampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            _encoder.Bitrate = bitrate; // bits per second
            _encoder.Complexity = 6;     // reasonable CPU vs quality tradeoff for Unity
            _encoder.UseVBR = true;      // allow variable bitrate
            _encoder.UseInbandFEC = true;
            _encoder.UseDTX = true;
            _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;
            _encoder.Bandwidth = OpusBandwidth.OPUS_BANDWIDTH_FULLBAND;
        }

        public int Encode(float[] pcm, int pcmOffset, byte[] dst, int dstOffset, int frameSamples = DefaultFrameSamples)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(OpusEncoderWrapper));
            // Concentus expects 16-bit PCM shorts; convert from floats [-1,1]
            int sampleCount = frameSamples * _channels;
            var temp = new short[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float f = pcm[pcmOffset + i];
                if (f > 1f) f = 1f; else if (f < -1f) f = -1f;
                short s = (short)System.Math.Round((double)f * short.MaxValue);
                temp[i] = s;
            }
            ReadOnlySpan<short> inputSpan = new ReadOnlySpan<short>(temp);
            Span<byte> outputSpan = new Span<byte>(dst, dstOffset, dst.Length - dstOffset);
            const int maxDataBytes = 1275;
            return _encoder.Encode(inputSpan, frameSamples, outputSpan, maxDataBytes);
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }

    public sealed class OpusDecoderWrapper(int sampleRate = OpusEncoderWrapper.DefaultSampleRate, int channels = 1) : IDisposable
    {
    private readonly OpusDecoder _decoder = (OpusDecoder)OpusCodecFactory.CreateDecoder(sampleRate, channels);
    private bool _disposed;

        // Concentus 2.x uses constructors (Create(...) removed)
        //_decoder = new OpusDecoder(sampleRate, channels);

        public int Decode(byte[] payload, int payloadOffset, int payloadLength, float[] dstFloat, int dstOffset, int frameSamples)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(OpusDecoderWrapper));
            int sampleCount = frameSamples * channels;
            var temp = new short[sampleCount];

            ReadOnlySpan<byte> inputSpan = new ReadOnlySpan<byte>(payload, payloadOffset, payloadLength);
            Span<short> outputSpan = new Span<short>(temp);
            int decoded = _decoder.Decode(inputSpan, outputSpan, frameSamples, false);

            // Convert to floats
            int total = decoded * channels;
            for (int i = 0; i < total; i++)
            {
                dstFloat[dstOffset + i] = temp[i] / (float)short.MaxValue;
            }
            return decoded;
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
