namespace Spice86.Audio.Backend.Audio.CrossPlatform;

using Spice86.Audio.Backend;

using System;

/// <summary>
/// Audio player that uses the cross-platform audio backend with callback-based audio.
/// Producer (mixer thread) blocks via <see cref="RWQueue{T}.BulkEnqueue(T[], int, int)"/>
/// until space is available; consumer (audio callback) dequeues non-blocking.
/// </summary>
public sealed class CrossPlatformAudioPlayer : AudioPlayer {
    private readonly IAudioBackend _backend;
    private readonly RWQueue<float> _queue;
    private volatile bool _started;
    private volatile bool _muted;
    private readonly DuneAudioDiagnostics? _diagnostics;

    /// <summary>
    /// Creates a new cross-platform audio player.
    /// </summary>
    /// <param name="format">Audio format.</param>
    /// <param name="backend">Platform-specific audio backend.</param>
    /// <param name="bufferFrames">Buffer size in frames for the audio device.</param>
    /// <param name="prebufferMs">Prebuffer duration in milliseconds.</param>
    /// <param name="allowNegotiate">Whether the audio device is allowed to negotiate buffer size with hardware.</param>
    public CrossPlatformAudioPlayer(AudioFormat format, IAudioBackend backend, int bufferFrames, int prebufferMs, bool allowNegotiate)
        : base(format) {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));

        // The shipped 20 ms cushion is vulnerable to bursts of Bluetooth callbacks.
        // Keep this per-process and configurable rather than changing the system audio device.
        if (OperatingSystem.IsMacOS()) {
            prebufferMs = int.TryParse(Environment.GetEnvironmentVariable("DUNE_AUDIO_PREBUFFER_MS"), out int configured)
                ? Math.Clamp(configured, 20, 500)
                : Math.Max(prebufferMs, 120);
        }

        AudioSpec spec = new AudioSpec {
            SampleRate = format.SampleRate,
            Channels = format.Channels,
            BufferFrames = bufferFrames,
            Callback = AudioCallback,
            AllowNegotiate = allowNegotiate
        };

        if (!_backend.Open(spec)) {
            throw new InvalidOperationException($"Failed to open audio backend: {_backend.LastError}");
        }

        AudioSpec obtainedSpec = _backend.ObtainedSpec;
        Format = new AudioFormat(obtainedSpec.SampleRate, obtainedSpec.Channels, format.SampleFormat);
        BufferFrames = obtainedSpec.BufferFrames;

        int prebufferFrames = obtainedSpec.SampleRate * prebufferMs / 1000;
        int queueFrames = obtainedSpec.BufferFrames + prebufferFrames;
        int queueCapacity = queueFrames * obtainedSpec.Channels;
        _queue = new RWQueue<float>(queueCapacity);
        _diagnostics = DuneAudioDiagnostics.Create(queueCapacity, obtainedSpec.SampleRate, obtainedSpec.Channels);
    }

    /// <summary>
    /// Audio callback invoked by the backend when it needs audio data.
    /// Non-blocking — fills with silence on underrun.
    /// </summary>
    private void AudioCallback(Span<float> buffer) {
        long started = _diagnostics == null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
        if (_muted) {
            buffer.Clear();
            return;
        }

        int samplesNeeded = buffer.Length;
        int received = _queue.NonblockingBulkDequeue(buffer, samplesNeeded);

        if (received < samplesNeeded) {
            buffer[received..].Clear();
        }
        _diagnostics?.Record(samplesNeeded, received, started);
    }

    /// <inheritdoc/>
    public override void Start() {
        if (_started) {
            return;
        }
        _started = true;
        _backend.Start();
    }

    /// <inheritdoc/>
    public override void ClearQueuedData() {
        _queue.Clear();
    }

    /// <inheritdoc/>
    public override void MuteOutput() {
        _muted = true;
        _queue.Clear();
    }

    /// <inheritdoc/>
    public override void UnmuteOutput() {
        _muted = false;
    }

    /// <inheritdoc/>
    public override int WriteData(Span<float> data) {
        // BulkEnqueue(Span<T>) processes in chunks within lock acquisitions,
        // avoiding the need to copy to a temporary array.
        return _queue.BulkEnqueue(data);
    }

    /// <summary>
    /// Gets the number of queued samples.
    /// </summary>
    public int QueuedSamples => _queue.Size;

    /// <inheritdoc/>
    protected override void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        if (disposing) {
            _diagnostics?.Dispose();
            _queue.Stop();
            _backend.Pause();
            _backend.Close();
            _backend.Dispose();
        }

        base.Dispose(disposing);
    }
}

