# Cello.Audio.Windows

Windows-spezifische Implementierungen der plattformneutralen Audio-Schnittstellen
in `Cello.Core`. Dieses Projekt darf nicht von Razor-Komponenten referenziert
werden; der MAUI-Host stellt die Implementierungen per Dependency Injection
bereit.

## Aufnahme und Analyse

| Eigenschaft | Ausgangswert |
| --- | --- |
| WASAPI-Modus | Standard-Eingabegerät über `WasapiRecorderBuilder` |
| PCM-Format | 48.000 Hz, 16 Bit, mono |
| Aufnahme-Puffer | 30 ms |
| YIN-Fenster | 4.096 Samples, etwa 85,3 ms bei 48 kHz |
| Pegel-/Spektrumfenster | 2.048 Samples, etwa 42,7 ms bei 48 kHz |
| Veröffentlichungsintervall | mindestens 75 ms zwischen Snapshots |

Der NAudio-Callback fügt Samples synchron in wiederverwendete Analysepuffer
ein. Er wartet nicht auf UI- oder Razor-Code. `AnalysisAvailable` wird auf dem
Audio-Thread ausgelöst; Konsumenten müssen für UI-Zugriffe selbst auf ihren
Dispatcher wechseln. Eine spätere UI-Integration soll Snapshots begrenzen oder
bündeln, statt jeden Audioblock zu rendern.

Der Hybrid-Host begrenzt die Veröffentlichung an Razor auf 30 Hz. Während der
Aufnahme werden Callback-Abstände, vermutete Aussetzer, Analysezeiten,
Prozessallokationen und Gen-0-Sammlungen erfasst. Gerätename, Analysezeit und
Aussetzerzahl erscheinen direkt in der Tonerkennung. Die Aufnahme verwendet
NAudios Standardgeräte-Stream-Routing, damit ein Wechsel des Windows-
Standardmikrofons ohne Neuaufbau der Oberfläche übernommen wird.

Beim Suspendieren stoppt der MAUI-Host eine aktive Aufnahme. Beim Fortsetzen
wird sie nur dann erneut gestartet, wenn sie zuvor aktiv war.

`StartAsync` ist idempotent, solange die Aufnahme aktiv ist. Bei einem Fehler
während des Starts wird der Recorder freigegeben und der interne Zustand
zurückgesetzt. `Stop` und `Dispose` dürfen mehrfach aufgerufen werden. Sie
stoppen und entsorgen den Recorder und verwerfen die Analyseinstanzen.

## MIDI-Wiedergabe

| Eigenschaft | Ausgangswert |
| --- | --- |
| Synthese | MeltySynth mit FluidR3 GM/GS |
| Ausgabeformat | 44.100 Hz, IEEE-Float, stereo |
| WASAPI-Modus | Shared/Event-Sync |
| Gewünschte Ausgabelatenz | 80 ms |
| Polyphonie | maximal 64 Stimmen |
| Instrument | MIDI-Programm 42, Violoncello |

Alle Zugriffe auf den Synthesizer, einschließlich des Render-Callbacks, werden
mit demselben Lock serialisiert. `TryInitialize` ist wiederholbar und liefert
Fehler als Meldung zurück. `StopAll` und `Dispose` sind mehrfach aufrufbar;
`Dispose` stoppt zuerst alle Noten und danach die WASAPI-Ausgabe.

Die SoundFont und ihre Lizenz werden aus dem unveränderten Asset der
WinUI-Referenzanwendung in `Assets/SoundFonts` des Hybrid-Ausgabeverzeichnisses
kopiert. Dadurch wird die etwa 144 MB große Datei nicht im Repository
dupliziert.

## Noch ausstehende Messungen

Die Tabellenwerte sind konfigurierte oder aus der Fenstergröße berechnete
Ausgangswerte, keine Ende-zu-Ende-Messungen. Die Laufzeitdiagnostik liefert
erste Callback-, Analyse-, Aussetzer-, Allokations- und GC-Werte. Ein
reproduzierbarer Dauertest sowie reale Mikrofon- und MIDI-Latenzmessungen stehen
weiter aus. Ergebnisse sind getrennt nach Audiogerät, Treiber und
Build-Konfiguration zu dokumentieren.
