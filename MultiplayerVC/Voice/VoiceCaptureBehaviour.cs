using System;
using UnityEngine;
using System.Reflection;

namespace MultiplayerVC.Voice
{
    [DisallowMultipleComponent]
    public sealed class VoiceCaptureBehaviour : MonoBehaviour
    {
        [Header("Input")]
        public KeyCode pushToTalk = KeyCode.V;
        public bool openMic = false;
        [Range(-80, 0)] public float vadThresholdDb = -45f;
        [Range(0.5f, 2f)] public float inputGain = 1.0f;

        [Header("Codec")]
        public int sampleRate = OpusEncoderWrapper.DefaultSampleRate;
        public int frameMs = OpusEncoderWrapper.DefaultFrameMs;

        [Header("Debug")]
        public bool loopback = true;

        public IVoiceTransport? Transport { get; set; }

        // Exposed voice telemetry
        public bool IsSending { get; private set; }
        public float CurrentRms => _rms;

        private AudioClip? _micClip;
        private string _deviceName = string.Empty;
        private int _micReadPos;
    private float[] _readBuffer = Array.Empty<float>(); // interleaved if _micChannels > 1
    private float[] _monoBuffer = Array.Empty<float>();  // mono downmixed for encoding
        private byte[] _encodeBuffer = Array.Empty<byte>();
        private OpusEncoderWrapper? _encoder;
        private int _frameSamples;
    private int _micChannels = 1;
        private float _rms;

        // spam guards for missing deps
        private bool _warnedNoMic;
        private bool _warnedNoTransport;
        private bool _warnedNoEncoder;

        void Start()
        {
            _frameSamples = sampleRate * frameMs / 1000;
            _readBuffer = new float[_frameSamples]; // will be resized after mic starts
            _monoBuffer = new float[_frameSamples];
            _encodeBuffer = new byte[4000]; // safe for 20ms mono opus

            // Log Concentus assembly info for diagnostics
            try
            {
                var ca = typeof(Concentus.Enums.OpusApplication).Assembly;
                Debug.Log($"[VC] Capture: Concentus assembly loaded: {ca.FullName}, Location='{SafeLocation(ca)}'");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VC] Capture: Could not inspect Concentus assembly: {ex.Message}");
            }

            try
            {
                _encoder = new OpusEncoderWrapper(sampleRate, 1, 24000);
                Debug.Log($"[VC] Capture: Opus encoder created (sr={sampleRate}, frameMs={frameMs})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VC] Capture: Failed to create Opus encoder: {ex}");
            }

            // Log devices
            try
            {
                var devices = Microphone.devices;
                Debug.Log($"[VC] Capture: Microphone devices: {(devices != null ? string.Join(", ", devices) : "<none>")}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VC] Capture: Failed to enumerate microphones: {ex.Message}");
            }

            // Start with default device (first available) if any
            SelectAndStartDevice(null);
            if (Transport == null && loopback)
            {
                Debug.Log("[VC] Capture: No transport assigned; enabling local loopback for diagnostics");
                Transport = new LoopbackTransport();
            }
        }

        void OnDestroy()
        {
            if (_micClip != null)
            {
                Microphone.End(_deviceName);
            }
            _encoder?.Dispose();
        }

        void Update()
        {
            if (_micClip == null)
            {
                if (!_warnedNoMic)
                {
                    Debug.LogWarning("[VC] Capture: No microphone active; voice will not transmit");
                    _warnedNoMic = true;
                }
                IsSending = false; return;
            }
            if (Transport == null)
            {
                if (!_warnedNoTransport)
                {
                    Debug.LogWarning("[VC] Capture: No transport assigned; voice will not be sent");
                    _warnedNoTransport = true;
                }
                IsSending = false; return;
            }
            if (_encoder == null)
            {
                if (!_warnedNoEncoder)
                {
                    Debug.LogError("[VC] Capture: Encoder not initialized; cannot encode audio");
                    _warnedNoEncoder = true;
                }
                IsSending = false; return;
            }

            bool shouldSend = openMic ? IsAboveThreshold() : Input.GetKey(pushToTalk);
            // reflect transmit intent immediately for UI responsiveness
            IsSending = shouldSend;

            int micPos = Microphone.GetPosition(_deviceName);
            int samplesAvailable = micPos - _micReadPos;
            if (samplesAvailable < 0) samplesAvailable += _micClip.samples;

            while (samplesAvailable >= _frameSamples)
            {
                // Ensure buffers sized for current device channels
                if (_readBuffer.Length != _frameSamples * _micChannels)
                {
                    _readBuffer = new float[_frameSamples * _micChannels];
                }
                if (_monoBuffer.Length != _frameSamples)
                {
                    _monoBuffer = new float[_frameSamples];
                }

                _micClip.GetData(_readBuffer, _micReadPos);

                // Apply gain and compute RMS
                _rms = 0f;
                if (_micChannels == 1)
                {
                    for (int i = 0; i < _frameSamples; i++)
                    {
                        float s = _readBuffer[i] * inputGain;
                        s = Mathf.Clamp(s, -1f, 1f);
                        _monoBuffer[i] = s;
                        _rms += s * s;
                    }
                }
                else
                {
                    // Downmix interleaved multi-channel input to mono (average channels)
                    for (int i = 0; i < _frameSamples; i++)
                    {
                        float sum = 0f;
                        int baseIdx = i * _micChannels;
                        for (int c = 0; c < _micChannels; c++)
                            sum += _readBuffer[baseIdx + c];
                        float s = (sum / _micChannels) * inputGain;
                        s = Mathf.Clamp(s, -1f, 1f);
                        _monoBuffer[i] = s;
                        _rms += s * s;
                    }
                }
                _rms = Mathf.Sqrt(_rms / _frameSamples);

                if (shouldSend)
                {
                    int len = _encoder.Encode(_monoBuffer, 0, _encodeBuffer, 0, _frameSamples);
                    if (len > 0)
                    {
                        var payload = new byte[len];
                        Buffer.BlockCopy(_encodeBuffer, 0, payload, 0, len);
                        var frame = new VoiceFrame
                        {
                            Codec = VoiceCodecId.Opus,
                            Channels = 1,
                            SampleRate = sampleRate,
                            Payload = payload,
                            InputRms = _rms,
                        };
                        Transport.SendVoiceFrame(frame);
                    }
                }

                _micReadPos = (_micReadPos + _frameSamples) % _micClip.samples;
                samplesAvailable -= _frameSamples;
            }
        }

        private bool IsAboveThreshold()
        {
            float db = 20f * Mathf.Log10(_rms + 1e-7f);
            return db >= vadThresholdDb;
        }

    public void SetDevice(string deviceName)
        {
            SelectAndStartDevice(deviceName);
        }

    private void SelectAndStartDevice(string? desired)
        {
            Debug.Log($"[VC] Capture: SelectAndStartDevice requested='{desired ?? "<null>"}'");
            // Determine desired device
            if (string.IsNullOrEmpty(desired))
            {
                // default to first if available
                string[] devices = Microphone.devices;
                if (devices.Length == 0)
                {
                    Debug.LogWarning("[VC] Capture: No microphone devices detected");
                    _micClip = null;
                    return;
                }
                desired = devices[0];
                Debug.Log($"[VC] Capture: Using default device '{desired}'");
            }

            // If already using desired, do nothing
            if (_micClip != null && string.Equals(_deviceName, desired))
                return;

            // Stop previous
            if (_micClip != null)
            {
                try { Microphone.End(_deviceName); } catch { /* ignore */ }
                _micClip = null;
            }

            _deviceName = desired ?? string.Empty;
            if (!string.IsNullOrEmpty(_deviceName))
            {
                try
                {
                    _micClip = Microphone.Start(_deviceName, true, 1, sampleRate);
                    _micReadPos = 0;
                    _micChannels = Mathf.Max(1, _micClip.channels);
                    _readBuffer = new float[_frameSamples * _micChannels];
                    _monoBuffer = new float[_frameSamples];
                    Debug.Log($"[VC] Capture: Started microphone '{_deviceName}' at {sampleRate} Hz");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[VC] Capture: Failed to start microphone '{_deviceName}': {ex.Message}");
                    _micClip = null;
                }
            }
        }

        private static string SafeLocation(Assembly asm)
        {
            try { return asm.Location; } catch { }
            try { return asm.CodeBase; } catch { }
            return "<unknown>";
        }
    }
}
