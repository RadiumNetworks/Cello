using MeltySynth;
using NAudio.Wave;

namespace Cello.Audio.Windows;

/// <summary>
/// Synthesizes score notes with MeltySynth and streams stereo PCM through
/// Windows WASAPI. All synthesizer access is serialized with one lock.
/// </summary>
public sealed class WindowsMidiPlayback : IMidiPlayback
{
    private const int SampleRate = 44100;
    private const int MidiChannel = 0;
    private const int CelloProgram = 42;
    private const int PizzicatoStringsProgram = 45;
    private const int DefaultVelocity = 92;
    private const string SoundFontRelativePath = @"Assets\SoundFonts\FluidR3_GM_GS.sf2";

    private readonly object _synthesizerLock = new();
    private Synthesizer? _synthesizer;
    private WasapiPlayer? _audioOutput;
    private readonly HashSet<int> _activeMidiNotes = [];
    private int _activeProgram = CelloProgram;

    public string? OutputName => _synthesizer is null ? null : "MeltySynth · FluidR3 GM";

    public bool TryInitialize(out string? errorMessage)
    {
        if (_synthesizer is not null && _audioOutput is not null)
        {
            errorMessage = null;
            return true;
        }

        try
        {
            string soundFontPath = Path.Combine(AppContext.BaseDirectory, SoundFontRelativePath);
            if (!File.Exists(soundFontPath))
            {
                errorMessage = $"Die FluidR3-SoundFont wurde nicht gefunden: {soundFontPath}";
                return false;
            }

            var settings = new SynthesizerSettings(SampleRate)
            {
                EnableReverbAndChorus = true,
                MaximumPolyphony = 64
            };
            _synthesizer = new Synthesizer(soundFontPath, settings);
            _synthesizer.ProcessMidiMessage(MidiChannel, 0xC0, CelloProgram, 0);

            var sampleProvider = new SynthesizerSampleProvider(_synthesizer, _synthesizerLock);
            _audioOutput = new WasapiPlayerBuilder()
                .WithSharedMode()
                .WithLatency(80)
                .WithEventSync()
                .Build();
            _audioOutput.Init(sampleProvider.ToWaveProvider());
            _audioOutput.Play();
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            DisposeSynthesizer();
            errorMessage = $"Die MIDI-Ausgabe konnte nicht initialisiert werden: {ex.Message}";
            return false;
        }
    }

    public void PlayNote(int midiNote, bool pizzicato = false) => PlayNotes([midiNote], pizzicato);

    public void PlayNotes(IReadOnlyList<int> midiNotes, bool pizzicato = false)
    {
        if (_synthesizer is null || midiNotes.Count == 0)
        {
            return;
        }

        lock (_synthesizerLock)
        {
            StopNoteCore();
            int program = pizzicato ? PizzicatoStringsProgram : CelloProgram;
            if (program != _activeProgram)
            {
                _synthesizer.ProcessMidiMessage(MidiChannel, 0xC0, program, 0);
                _activeProgram = program;
            }
            foreach (int midiNote in midiNotes.Distinct())
            {
                int note = Math.Clamp(midiNote, 0, 127);
                _synthesizer.NoteOn(MidiChannel, note, DefaultVelocity);
                _activeMidiNotes.Add(note);
            }
        }
    }

    public void StopNote()
    {
        lock (_synthesizerLock)
        {
            StopNoteCore();
        }
    }

    public void StopAll()
    {
        lock (_synthesizerLock)
        {
            _synthesizer?.NoteOffAll(true);
            _activeMidiNotes.Clear();
        }
    }

    public void Dispose()
    {
        StopAll();
        DisposeSynthesizer();
        GC.SuppressFinalize(this);
    }

    private void StopNoteCore()
    {
        if (_synthesizer is not null)
        {
            foreach (int note in _activeMidiNotes)
            {
                _synthesizer.NoteOff(MidiChannel, note);
            }
            _activeMidiNotes.Clear();
        }
    }

    private void DisposeSynthesizer()
    {
        _audioOutput?.Stop();
        _audioOutput?.Dispose();
        _audioOutput = null;
        lock (_synthesizerLock)
        {
            _synthesizer = null;
            _activeMidiNotes.Clear();
            _activeProgram = CelloProgram;
        }
    }

    private sealed class SynthesizerSampleProvider(Synthesizer synthesizer, object synthesizerLock) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

        public int Read(Span<float> buffer)
        {
            lock (synthesizerLock)
            {
                synthesizer.RenderInterleaved(buffer);
            }
            return buffer.Length;
        }
    }
}
