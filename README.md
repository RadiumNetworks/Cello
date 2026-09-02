# Cello

> The megacorps own the skyline, the grid never sleeps, and your fixer always calls five minutes too early. Sometimes you need to put the chrome down, pick up a bow, and breathe.

**Cello** is a small WinUI 3 desktop app for the quiet hours between corporate runs. It listens to cello or guitar, identifies the pitch, helps with tuning, writes MusicXML, displays scores, and plays them back with a sampled cello sound. No extraction plan required.

Built with C#, .NET 9, the Windows App SDK, and just enough audio tech to keep the neighbors guessing.

![Time to relax](./media/cello.png)

## What is in the case?

- Real-time monophonic pitch detection for cello and guitar
- Dedicated cello and standard six-string guitar tuners
- Shared microphone state across the entire app
- Signal level, clipping, dominant-frequency, and spectrum diagnostics
- Staff notation for the currently detected note
- Continuous notation recording with automatic MusicXML persistence
- MusicXML 4.0 import, metadata, notation, tempo, and score playback
- Four measures per score system with automatic scrolling during playback
- Playback speed from 20% to 120% of the written tempo
- Internal cello synthesis with MeltySynth, FluidR3, NAudio, and WASAPI

## Gear check

Before slipping into the grid, bring:

- Windows 10 version 1809, build 17763, or newer
- .NET 9 SDK
- Developer Mode for command-line launch of the packaged app
- A microphone, unless silent practice is part of the operation
- The FluidR3 GM+GS SoundFont described below

## Acquire the SoundFont

The FluidR3 binary is about 151 MB, too large for ordinary GitHub storage, so it stays out of the repository. Download it from [Internet Archive](https://archive.org/download/fluidr3-gm-gs/FluidR3_GM_GS.sf2) and save it here:

`Cello/Assets/SoundFonts/FluidR3_GM_GS.sf2`

Verify the payload before trusting it:

`SHA-256: 545B2833936F15F04DF5F0C5C4096B3BA6CED46EC7031F61991CAE46F8681986`

The FluidR3 copyright and MIT notice remain in `Cello/Assets/SoundFonts/FluidR3_LICENSE.txt`. The `.sf2` and `.sf3` binaries are deliberately excluded by `.gitignore`; their license files are not.

## Build the rig

In VS Code, run the **build Cello** task with `Ctrl+Shift+B`.

Or use the .NET CLI:

```powershell
dotnet restore Cello/Cello.sln -r win-x86 -p:Platform=x86
dotnet build Cello/Cello.sln --no-restore
```

The project also supports x64 and ARM64. Match the runtime identifier and platform when switching architecture.

## Take it for a spin

For the known-good x86 route:

```powershell
dotnet run --project .\Cello\Cello.csproj --no-build -r win-x86 -p:Platform=x86
```

`Microsoft.Windows.SDK.BuildTools.WinApp` handles development identity registration and launches the packaged application. If a previous instance is still lurking in the shadows, close it before rebuilding so it does not lock the output files.

## Audio surveillance, but friendly

The app captures 48 kHz, 16-bit mono PCM through NAudio/WASAPI with a 30 ms buffer. A YIN-based detector inspects the monophonic signal roughly 13 times per second and reports:

- nearest musical note
- detected frequency
- tuning offset in cents
- detection confidence
- RMS and peak levels in dBFS
- clipping and low-signal warnings
- dominant frequency
- 16 logarithmic spectrum bands from 65 Hz to 4.4 kHz

YIN is used instead of blindly trusting the strongest FFT bin. Bowed strings throw powerful harmonics into the air, and the loudest frequency is not always the fundamental. The detector currently covers 55–1200 Hz, comfortably surrounding the normal cello range.

One application-wide `MicrophonePitchService` owns the capture device. The side-pane switch controls it once for every page. Views subscribe to analysis results; they do not fight each other for the same microphone like rival runners over a single escape vehicle.

## Tuning before the run

The cello tuner covers standard tuning:

- C2 — 65.41 Hz
- G2 — 98.00 Hz
- D3 — 146.83 Hz
- A3 — 220.00 Hz

The guitar tuner covers standard six-string tuning:

- E2 — 82.41 Hz
- A2 — 110.00 Hz
- D3 — 146.83 Hz
- G3 — 196.00 Hz
- B3 — 246.94 Hz
- E4 — 329.63 Hz

Both tuners automatically select the nearest open string. The tuning display intentionally uses the raw pitch result, while analysis and notation recording apply note-boundary hysteresis to avoid nervous switching near semitone borders.

## Turn the noise into notation

The live analysis page renders the current pitch on a native WinUI staff. Low cello notes use bass clef; higher notes can move into treble clef.

The **Notation aufnehmen** page converts stable pitches into a continuously expanding score. Repeated detections are joined into one timed note, while short isolated glitches are ignored. Select a target file before or during recording and the app continually rewrites a valid multi-measure MusicXML 4.0 document. If the corp cuts the power, at least the last notes made it to disk.

## MusicXML operations

The **MusicXML anzeigen** page opens `.musicxml` and `.xml` files with the Windows file picker. It reads the first part and displays:

- title and composer
- part name
- measure and note counts
- key and time signatures
- score tempo
- note values, dots, staccato, ties, slurs, beams, and tuplets

The custom WinUI renderer lays out four measures per system and scrolls vertically. During playback, an orange marker moves across the current note and follows the next system automatically. The tempo slider adjusts visual and audible playback together from 20% to 120%.

External DTD resolution is disabled while parsing. Even a relaxing side project should not accept mystery payloads from the matrix.

## Why it sounds less like an arcade cabinet

MeltySynth 2.4.1 loads the local FluidR3 GM+GS SoundFont and selects General MIDI program 42, the cello preset. The synthesizer renders stereo floating-point PCM internally. NAudio 3 streams those samples to the default Windows output through the modern `WasapiPlayer` API.

This replaces the dated Microsoft GS Wavetable Synth and gives the preview a sampled cello voice with reverb and chorus. It is still a General MIDI SoundFont rather than a multi-articulation studio instrument, but it is much better company after midnight.

## Architecture, for runners who inspect the blueprints

The major systems stay separated so one can be swapped without burning down the safehouse:

- microphone capture and shared state
- YIN pitch detection
- pitch stabilization
- signal and spectrum analysis
- tuner string definitions
- notation rendering
- MusicXML reading and export
- visual playback timing
- SoundFont synthesis and audio output

The notation renderer is native WinUI and requires no external engraving package. It is a practical first-part preview, not a full replacement for a professional score engraver.

## Licenses

Cello is released under the MIT License. See `LICENSE`.

Core third-party components:

- MeltySynth 2.4.1 — MIT
- NAudio 3.0.1 — MIT
- FluidR3 GM+GS SoundFont — MIT; separate notice included with the SoundFont assets
- Microsoft Windows App SDK and build tooling — distributed under their respective Microsoft terms

Keep the third-party notices with redistributed binaries. Open source is no excuse for leaving fingerprints undocumented.

## Final note

This is not a corporate product, a combat implant, or a mission-critical targeting system. It is a place to tune the strings, watch the notes move, and remember that not every run needs an alarm at the end.
