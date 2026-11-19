namespace Spice86.Audio.Backend.Audio.CrossPlatform;

using System.Diagnostics;
using System.Text.Json;
using System.Threading;

// Optional diagnostics of generated game audio only. No microphone or system audio capture.
internal sealed class DuneAudioDiagnostics : IDisposable {
    private readonly string _path;
    private readonly int _capacity;
    private readonly int _rate;
    private readonly int _channels;
    private readonly Timer _timer;
    private long _callbacks, _underruns, _requested, _missing, _maxTicks;

    private DuneAudioDiagnostics(string path, int capacity, int rate, int channels) {
        _path = path;
        _capacity = capacity;
        _rate = rate;
        _channels = channels;
        _timer = new Timer(_ => Report(), null, 1000, 1000);
    }

    public static DuneAudioDiagnostics? Create(int capacity, int rate, int channels) {
        string? path = Environment.GetEnvironmentVariable("DUNE_AUDIO_DIAGNOSTICS");
        return string.IsNullOrEmpty(path) ? null : new(path, capacity, rate, channels);
    }

    public void Record(int requested, int received, long started) {
        Interlocked.Increment(ref _callbacks);
        Interlocked.Add(ref _requested, requested);
        if (received < requested) {
            Interlocked.Increment(ref _underruns);
            Interlocked.Add(ref _missing, requested - received);
        }
        long ticks = Stopwatch.GetTimestamp() - started;
        long old;
        do {
            old = Volatile.Read(ref _maxTicks);
            if (ticks <= old) break;
        } while (Interlocked.CompareExchange(ref _maxTicks, ticks, old) != old);
    }

    private void Report() {
        try {
            File.WriteAllText(_path, JsonSerializer.Serialize(new {
                time = DateTimeOffset.Now,
                pid = Environment.ProcessId,
                callbacks = Volatile.Read(ref _callbacks),
                underruns = Volatile.Read(ref _underruns),
                requestedSamples = Volatile.Read(ref _requested),
                missingSamples = Volatile.Read(ref _missing),
                maxCallbackMs = Volatile.Read(ref _maxTicks) * 1000.0 / Stopwatch.Frequency,
                queueCapacitySamples = _capacity,
                queueCapacityMs = _capacity * 1000.0 / _rate / _channels,
                sampleRate = _rate,
                channels = _channels
            }));
        } catch (IOException) {
            // Diagnostics must never interrupt playback.
        } catch (UnauthorizedAccessException) {
        }
    }

    public void Dispose() {
        _timer.Dispose();
        Report();
    }
}
