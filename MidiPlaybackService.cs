using System;
using System.IO;
using MeltySynth;
using NAudio.Wave;

namespace Cello;

/// <summary>
/// Synthesizes score notes with MeltySynth and the bundled FluidR3 SoundFont,
/// then streams the generated stereo PCM samples to WASAPI through NAudio.
/// </summary>
public sealed class MidiPlaybackService : IDisposable
{
    private const int SampleRate = 44100;
    private const int MidiChannel = 0;
    private const int CelloProgram = 42;
    private const int DefaultVelocity = 92;
    private const string SoundFontRelativePath = @"Assets\SoundFonts\FluidR3_GM_GS.sf2";

    private readonly object _synthesizerLock = new();
    private Synthesizer? _synthesizer;
    private WasapiPlayer? _audioOutput;
    private int? _activeMidiNote;

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

    public void PlayNote(int midiNote)
    {
        if (_synthesizer is null)
        {
            return;
        }

        lock (_synthesizerLock)
        {
            StopNoteCore();
            int note = Math.Clamp(midiNote, 0, 127);
            _synthesizer.NoteOn(MidiChannel, note, DefaultVelocity);
            _activeMidiNote = note;
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
            _activeMidiNote = null;
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
        if (_synthesizer is not null && _activeMidiNote is int note)
        {
            _synthesizer.NoteOff(MidiChannel, note);
            _activeMidiNote = null;
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
            _activeMidiNote = null;
        }
    }

    private sealed class SynthesizerSampleProvider : ISampleProvider
    {
        private readonly Synthesizer _synthesizer;
        private readonly object _synthesizerLock;

        public SynthesizerSampleProvider(Synthesizer synthesizer, object synthesizerLock)
        {
            _synthesizer = synthesizer;
            _synthesizerLock = synthesizerLock;
        }

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

        public int Read(Span<float> buffer)
        {
            lock (_synthesizerLock)
            {
                _synthesizer.RenderInterleaved(buffer);
            }

            return buffer.Length;
        }
    }
}
