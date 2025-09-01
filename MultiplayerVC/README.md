# MultiplayerVC (isolated voice chat module)

This project adds an Opus-based voice capture and playback pipeline that can be integrated with dv-multiplayer without touching existing code.

What’s included:
- Concentus Opus encoder/decoder wrappers
- `VoiceCaptureBehaviour` (Unity MonoBehaviour) for mic capture, PTT/Open-mic with simple VAD, Opus encoding
- `VoicePlaybackBehaviour` for per-speaker decoding, jitter buffering, and 3D AudioSource playback
- `IVoiceTransport` abstraction and a `LoopbackTransport` for local testing

Host wiring (no changes to existing files required):
- Implement `IVoiceNetworkBridge` in the host mod using your Steam transport and packet handlers.
- On world ready, create a GameObject and add `VoiceBootstrap`, then call `Initialize(bridge, proximity, mute)`.
- Provide `IProximityProvider` (optional) to filter recipients server-side, and `IMuteProvider` for local/server mutes.

Getting started (Unity scene test):
- Create a GameObject, add `VoiceCaptureBehaviour` and `VoicePlaybackBehaviour`
- Leave `loopback` enabled on capture: your mic will play back locally via Opus
- For networking, implement `IVoiceTransport` using your Steam transport and assign it to both components

Notes:
- 48 kHz mono, 20 ms frames, ~24 kbps VBR with DTX enabled
- Delivery should be UnreliableSequenced with per-sender sequence numbers
- Add proximity filtering and mute logic in the transport implementation

Build: The csproj targets net48 and copies the DLL to `build/` post-build.