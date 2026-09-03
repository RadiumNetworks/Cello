using Cello.Audio;
using Microsoft.JSInterop;

namespace Cello.Hybrid.Web.Client.Services;

public sealed class BrowserMidiPlayback(IJSRuntime jsRuntime) : IMidiPlayback
{
    private IJSObjectReference? _module;
    private bool _disposed;

    public string? OutputName => "Browser-Audio";

    public bool TryInitialize(out string? errorMessage)
    {
        errorMessage = null;
        return !_disposed;
    }

    public void PlayNote(int midiNote, bool pizzicato = false) => _ = PlayNoteAsync(midiNote, pizzicato);

    public void PlayNotes(IReadOnlyList<int> midiNotes, bool pizzicato = false) => _ = PlayNotesAsync(midiNotes, pizzicato);

    private async Task PlayNoteAsync(int midiNote, bool pizzicato)
    {
        if (_disposed) return;
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./browserAudio.js");
        await _module.InvokeVoidAsync("playNote", midiNote, pizzicato);
    }

    private async Task PlayNotesAsync(IReadOnlyList<int> midiNotes, bool pizzicato)
    {
        if (_disposed || midiNotes.Count == 0) return;
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./browserAudio.js");
        await _module.InvokeVoidAsync("playNotes", midiNotes, pizzicato);
    }

    public void StopNote()
    {
        if (_module is not null) _ = _module.InvokeVoidAsync("stopNote");
    }

    public void StopAll() => StopNote();

    public void Dispose()
    {
        if (_disposed) return;
        StopAll();
        if (_module is not null) _ = _module.DisposeAsync();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}