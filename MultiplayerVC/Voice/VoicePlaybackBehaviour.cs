using System;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerVC.Voice
{
    [DisallowMultipleComponent]
    public sealed class VoicePlaybackBehaviour : MonoBehaviour
    {
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0f, 1f)] public float spatialBlend = 1f;
        [Range(0f, 50f)] public float maxDistance = 25f;
        // public int jitterBufferMs = 80;

        public IVoiceTransport? Transport { get; set; }

        private class Stream
        {
            public readonly Queue<VoiceFrame> Queue = new Queue<VoiceFrame>(64);
            public readonly OpusDecoderWrapper Decoder = new OpusDecoderWrapper();
            public float[] Pcm = new float[OpusEncoderWrapper.DefaultFrameSamples];
            // public float JitterTimer;
            public ushort LastSeq;
            public AudioSource? Audio;
            public AudioClip? Clip;
            public float[] Ring = new float[OpusEncoderWrapper.DefaultSampleRate]; // 1s
            public int RingWrite;
            public int RingRead;
        }

        private readonly Dictionary<ulong, Stream> _streams = new Dictionary<ulong, Stream>();

        void Start()
        {
            if (Transport != null)
            {
                Debug.Log("[VC] Playback: subscribing to transport OnVoiceFrame");
                Transport.OnVoiceFrame += OnVoiceFrame;
            }
            else
            {
                Debug.LogWarning("[VC] Playback: no transport assigned; cannot receive audio");
            }
        }

        void OnDestroy()
        {
            if (Transport != null)
                Transport.OnVoiceFrame -= OnVoiceFrame;
            foreach (var s in _streams.Values)
                s.Decoder.Dispose();
            _streams.Clear();
        }

        private void OnVoiceFrame(VoiceFrame frame)
        {
            if (frame.Codec != VoiceCodecId.Opus) return;
            if (!_streams.TryGetValue(frame.SenderId, out var stream))
            {
                Debug.Log($"[VC] Playback: creating audio stream for sender {frame.SenderId}");
                stream = new Stream();
                var go = new GameObject($"VC_Speaker_{frame.SenderId}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.spatialBlend = spatialBlend;
                src.maxDistance = maxDistance;
                src.loop = true;
                src.playOnAwake = true;
                src.volume = volume;
                stream.Audio = src;
                stream.Clip = AudioClip.Create($"VC_Clip_{frame.SenderId}", OpusEncoderWrapper.DefaultSampleRate, 1, OpusEncoderWrapper.DefaultSampleRate, true, data => OnAudioRead(stream, data));
                src.clip = stream.Clip;
                src.Play();
                _streams[frame.SenderId] = stream;
            }
            stream.Queue.Enqueue(frame);
        }

        private void OnAudioRead(Stream stream, float[] data)
        {
            int needed = data.Length;
            int copied = 0;

            // Feed ring buffer from queued compressed frames if ring has space
            // Decode while we have at least one frame worth of space available.
            while (stream.Queue.Count > 0 && RingAvailable(stream) >= OpusEncoderWrapper.DefaultFrameSamples)
            {
                var frame = stream.Queue.Dequeue();
                // Basic PLC: if we detect a gap, ask decoder to conceal for each missing frame
                if (stream.LastSeq != 0)
                {
                    int expected = (ushort)(stream.LastSeq + 1);
                    int gap = (frame.Sequence - expected) & 0xFFFF; // handle wraparound
                    if (gap > 0 && gap < 10) // cap to avoid long stalls
                    {
                        for (int i = 0; i < gap; i++)
                        {
                            try { stream.Decoder.Decode(Array.Empty<byte>(), 0, 0, stream.Pcm, 0, OpusEncoderWrapper.DefaultFrameSamples); WriteRing(stream, stream.Pcm, OpusEncoderWrapper.DefaultFrameSamples); }
                            catch { /* ignore PLC errors */ }
                        }
                    }
                }
                int decoded = 0;
                try
                {
                    decoded = stream.Decoder.Decode(frame.Payload, 0, frame.Payload.Length, stream.Pcm, 0, OpusEncoderWrapper.DefaultFrameSamples);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[VC] Playback: decode error for sender {frame.SenderId}: {ex.Message}");
                }
                if (decoded > 0)
                {
                    WriteRing(stream, stream.Pcm, decoded);
                    stream.LastSeq = frame.Sequence;
                }
            }

            // Read from ring buffer into audio callback
            while (copied < needed && RingCount(stream) > 0)
            {
                data[copied++] = stream.Ring[stream.RingRead];
                stream.RingRead = (stream.RingRead + 1) % stream.Ring.Length;
            }

            // Pad if underflow
            if (copied < needed)
            {
                // This will happen if network starves. It's useful to see in logs, but avoid spamming per-sample.
                // Uncomment for deep debugging:
                // Debug.Log("[VC] Playback: audio underflow");
            }
            for (; copied < needed; copied++)
                data[copied] = 0f;
        }

        private static int RingCount(Stream s)
        {
            int diff = s.RingWrite - s.RingRead;
            if (diff < 0) diff += s.Ring.Length;
            return diff;
        }

        private static int RingAvailable(Stream s)
        {
            return s.Ring.Length - 1 - RingCount(s);
        }

        private static void WriteRing(Stream s, float[] src, int samples)
        {
            for (int i = 0; i < samples; i++)
            {
                s.Ring[s.RingWrite] = src[i];
                s.RingWrite = (s.RingWrite + 1) % s.Ring.Length;
            }
        }
    }
}
