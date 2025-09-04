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
        [Range(0.5f, 5f)] public float inputGain = 1.0f;

        [Header("Codec")]
        public int sampleRate = OpusEncoderWrapper.DefaultSampleRate;
        public int frameMs = OpusEncoderWrapper.DefaultFrameMs;

        public IVoiceTransport? Transport { get; set; }

        // Exposed voice telemetry
        public bool IsSending { get; private set; }
        public float CurrentRms => _rms;

        private AudioClip? _micClip;
        private string _deviceName = string.Empty;
        private int _micReadPos;
        private float[] _readBuffer = Array.Empty<float>();
        private byte[] _encodeBuffer = Array.Empty<byte>();
        private OpusEncoderWrapper? _encoder;
        private int _frameSamples;
        private float _rms;
        private float _smoothedDb = -100f; // Smoothed decibel value for voice activity detection
        private const float _vadSmoothingFactor = 0.3f; // How quickly the smoothed level adapts to new levels

        // spam guards for missing deps
        private bool _warnedNoMic;
        private bool _warnedNoTransport;
        private bool _warnedNoEncoder;

        void Start()
        {
            _frameSamples = sampleRate * frameMs / 1000;
            _readBuffer = new float[_frameSamples];
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

            int micPos = Microphone.GetPosition(_deviceName);
            int samplesAvailable = micPos - _micReadPos;
            if (samplesAvailable < 0) samplesAvailable += _micClip.samples;

            // Default to not sending
            bool shouldSend = false;

            while (samplesAvailable >= _frameSamples)
            {
                _micClip.GetData(_readBuffer, _micReadPos);

                // Apply gain and compute RMS
                _rms = 0f;
                for (int i = 0; i < _frameSamples; i++)
                {
                    float s = _readBuffer[i] * inputGain;
                    _readBuffer[i] = Mathf.Clamp(s, -1f, 1f);
                    _rms += _readBuffer[i] * _readBuffer[i];
                }
                _rms = Mathf.Sqrt(_rms / _frameSamples);

                // Now make the send decision with current frame's RMS
                shouldSend = openMic ? IsAboveThreshold() : Input.GetKey(pushToTalk);

                // Debug logging for open mic issues
                if (openMic && !shouldSend)
                {
                    float db = 20f * Mathf.Log10(_rms + 1e-7f);
                    if (db > vadThresholdDb - 10f) // Only log when close to threshold to avoid spam
                    {
                        Debug.Log($"[VC] Capture: OpenMic RMS={_rms:F6}, dB={db:F1}, threshold={vadThresholdDb:F1}, sending={shouldSend}");
                    }
                }

                if (shouldSend)
                {
                    int len = _encoder.Encode(_readBuffer, 0, _encodeBuffer, 0, _frameSamples);
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
                        };
                        Transport.SendVoiceFrame(frame);

                        // Log successful transmission for debugging
                        if (openMic)
                        {
                            float db = 20f * Mathf.Log10(_rms + 1e-7f);
                            Debug.Log($"[VC] Capture: OpenMic SENDING frame, RMS={_rms:F6}, dB={db:F1}, len={len}");
                        }
                    }
                }

                _micReadPos = (_micReadPos + _frameSamples) % _micClip.samples;
                samplesAvailable -= _frameSamples;
            }

            // Reflect transmit intent for UI responsiveness
            IsSending = shouldSend;
        }

        private bool IsAboveThreshold()
        {
            // Calculate current dB level
            float currentDb = 20f * Mathf.Log10(_rms + 1e-7f);

            // Apply smoothing to the dB value for more stable voice detection
            _smoothedDb = Mathf.Lerp(_smoothedDb, currentDb, _vadSmoothingFactor);

            // Hysteresis: add a small bonus when already above threshold to prevent rapid on/off switching
            float hysteresisBonus = _smoothedDb >= vadThresholdDb ? 3f : 0f;
            bool isAbove = _smoothedDb >= (vadThresholdDb - hysteresisBonus);

            // Log threshold checks at regular intervals for debugging
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"[VC] Capture: VAD check: RMS={_rms:F6}, current dB={currentDb:F1}, smoothed dB={_smoothedDb:F1}, threshold={vadThresholdDb:F1}, above={isAbove}");
            }

            return isAbove;
        }

    public void SetDevice(string? deviceName)
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
