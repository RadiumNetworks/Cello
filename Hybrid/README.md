# Cello Blazor Hybrid

Dieser Unterordner enthält die schrittweise Migration der bestehenden WinUI-3-Anwendung zu einer .NET-MAUI-Blazor-Hybrid-Lösung. Die native Anwendung unter `Cello/` bleibt während und nach der Migration funktionsfähig und dient als Referenz.

## Architekturentscheidung

Die primäre Desktopanwendung wird als **.NET MAUI Blazor Hybrid** umgesetzt. Razor-Komponenten laufen nativ im .NET-Prozess und werden über `BlazorWebView` dargestellt. Zeitkritische Audiofunktionen bleiben native Windows-Dienste; sie werden nicht über WebAssembly oder JavaScript ausgeführt.

Eine optionale Blazor-Web-/PWA-Anwendung kann dieselben Razor-Komponenten und plattformneutralen Kernbibliotheken verwenden. Für Mikrofon, MIDI und SoundFont benötigt sie jedoch eigene Browseradapter.

## Zielstruktur

```text
Hybrid/
├── Cello.Hybrid.sln
├── README.md
├── src/
│   ├── Cello.Core/                 # DSP, Pitch-Erkennung, MusicXML, Modelle, Loop-Logik
│   ├── Cello.Components/           # Gemeinsam verwendete Razor-Komponenten, CSS und SVG
│   ├── Cello.Hybrid/               # .NET-MAUI-Blazor-Hybrid-Host
│   ├── Cello.Audio.Windows/        # NAudio/WASAPI, MeltySynth und SoundFont-Zugriff
│   └── Cello.Web/                  # Optionale Blazor-Web-/PWA-Ausgabe
└── tests/
    ├── Cello.Core.Tests/
    └── Cello.Components.Tests/
```

Die erste Projektvorlage kann leicht abweichende Namen erzeugen. Beim Ausbau wird sie an diese Struktur angenähert.

## Empfohlene Schritte

### Phase 1 – Grundgerüst

- [x] Zielarchitektur und Migrationsschritte dokumentieren.
- [x] .NET-MAUI-Blazor-Hybrid- und Web-Projektvorlage erzeugen.
- [x] Solution und alle erzeugten Projekte wiederherstellen und kompilieren.
- [x] Windows als erste unterstützte native Plattform validieren.

### Phase 2 – Plattformneutralen Kern extrahieren
Wie beschrieben soll die bisherige WinUI Cello Applikation so bestehen bleiben.

- [x] `PitchDetector`, `PitchStabilizer` und `AudioSignalAnalyzer` nach `Cello.Core` kopieren.
- [x] MusicXML-Modelle, Leser und Exporter nach `Cello.Core` kopieren.
- [x] Wiedergabebereichs- und Loop-Zustand vom UI entkoppeln.
- [x] Referenzen der bestehenden WinUI-Anwendung auf `Cello.Core` umstellen.
- [x] Unit-Tests für Pitch-Erkennung, MusicXML und Loop-Grenzen ergänzen.

`Cello.Core` ist plattformneutral auf `net9.0` ausgerichtet. Die bestehenden
Namespaces bleiben vorerst erhalten, damit die WinUI-Referenzanwendung ohne
öffentliche API-Brüche auf die gemeinsame Assembly umgestellt werden kann.
Die Core-Tests umfassen Pitch-Erkennung, Pegelanalyse, MusicXML-Roundtrips und
die Grenzen des Übungs- und Loop-Bereichs.

### Phase 3 – Native Windows-Audioebene

- [x] Schnittstellen wie `IMicrophoneCapture`, `IMidiPlayback` und `IAudioAnalysisStream` definieren.
- [x] NAudio/WASAPI-Aufnahme nach `Cello.Audio.Windows` kopieren.
- [x] MeltySynth und FluidR3-SoundFont in der Windows-Implementierung behalten.
- [ ] Lebenszyklus, Threading, Puffergrößen und Dispose-Verhalten messen und dokumentieren.
- [x] Dienste per Dependency Injection im MAUI-Host registrieren.

Die konfigurierten Ausgangswerte sowie Lebenszyklus-, Threading- und
Dispose-Regeln sind in `src/Cello.Audio.Windows/README.md` dokumentiert. Die
noch offene Checkliste betrifft empirische Latenz-, Jitter- und Lastmessungen
mit realer Audiohardware; diese werden zusammen mit den Ende-zu-Ende-Messungen
aus Phase 5 durchgeführt.

### Phase 4 – Razor-Benutzeroberfläche

- [x] Navigation und Seitenlayout als Razor-Komponenten anlegen.
- [x] Anpassbares Cockpit mit verschiebbaren, skalierbaren und aktivierbaren Elementen anlegen.
- [x] Cockpit-Layout lokal speichern, per Tastatur bedienbar machen und zurücksetzbar gestalten.
- [x] Mikrofonsteuerung, Live-Tonerkennung, Stimmgerät, Spektrum und Notenverlauf mit den nativen Audiodiensten verbinden.
- [x] Tuner, Pegelanzeige und Spektrum als performante SVG-Komponenten umsetzen.
- [x] MusicXML-Partitur als interaktive SVG-Anzeige portieren und Dateiimport ergänzen.
- [x] Start-/Endmarkierungen, Tempo, Wiedergabe und Übungsloop übernehmen.
- [x] Häufige Audioereignisse bündeln, damit nicht jeder Audioblock einen vollständigen Blazor-Render auslöst.

Das Cockpit zeigt im Windows-Hybrid-Host Live-Daten aus der WASAPI-Analyse.
Position, Größe und Aktivierungszustand werden im lokalen Browser-/WebView-
Speicher persistiert. Die UI übernimmt höchstens 30 gebündelte Aktualisierungen
pro Sekunde. Web und noch nicht unterstützte native Plattformen verwenden
sichere Fallback-Dienste und zeigen einen verständlichen Verfügbarkeitshinweis.
Das stufenlose SVG-Stimmgerät visualisiert Abweichungen von −50 bis +50 Cent.
Eine eigene dBFS-Pegelanzeige zeigt RMS, Spitzenwert, zu leise Signale und
Übersteuerung. Das geglättete 16-Band-Spektrum hebt die dominante Frequenz
hervor und hält kurzzeitig die Spitzen jedes logarithmischen Frequenzbands.
Das Cello-Bild aus `Media/cello.png` steht als aktivierbares, verschiebbares und
skalierbares Cockpit-Element in beiden Hosts zur Verfügung.
MusicXML-Dateien bis 10 MB können direkt im Cockpit geöffnet werden. Noten sind
per Maus oder Tastatur als normalisierter Übungsbereich auswählbar; der
Windows-Hybrid-Host spielt die Auswahl mit einstellbarem Tempo und optionalem
Loop über MeltySynth ab. Eine kleine Testpartitur liegt unter
`tests/Fixtures/practice-scale.musicxml`.

### Phase 5 – Integration und Latenzprüfung

- [x] Audioverarbeitung ausschließlich außerhalb des UI-Threads ausführen.
- [x] UI-Aktualisierung auf 30 Hz begrenzen.
- [ ] Ende-zu-Ende-Latenz für Mikrofon, FFT/Pitch und MIDI messen.
- [ ] Aussetzer, Speicherallokationen und Garbage-Collection unter Dauerlast prüfen (Laufzeitdiagnostik ist eingebaut, Hardware-Dauertest steht aus).
- [ ] Verhalten bei Suspend/Resume und Audio-Gerätewechsel mit realer Hardware testen (Lifecycle und Standardgeräte-Routing sind implementiert).

### Phase 6 – Optionale PWA

- [x] Gemeinsame Razor-Komponenten im Web-Host aktivieren.
- [ ] Browseradapter mit `getUserMedia`, Web Audio und `AudioWorklet` entwickeln (`getUserMedia` und Web-Audio-Ausgabe sind aktiv; Umstellung des Capture-Callbacks auf `AudioWorklet` steht aus).
- [ ] Web-MIDI-Verfügbarkeit und Berechtigungsfluss behandeln.
- [ ] Die etwa 144 MB große SoundFont nicht ungeprüft in den Standard-PWA-Cache aufnehmen.
- [ ] Offline-Updates und Versionskompatibilität des Service Workers absichern.

Der Browser-Mikrofonadapter benötigt einen sicheren Kontext (`https://` oder
`http://localhost`) und die Zustimmung im Berechtigungsdialog. Für „Erkannten
Ton hören“ erzeugt der Browser derzeit einen Web-Audio-Sinuston; die native
SoundFont-/MIDI-Wiedergabe bleibt dem Windows-Host vorbehalten.

## Technische Leitlinien

1. Der Audio-Callback darf niemals auf Razor-Rendering warten.
2. Zwischen Audioengine und UI werden unveränderliche Snapshots oder begrenzte Channels verwendet.
3. FFT- und Pitch-Puffer werden wiederverwendet, um Allokationen im Echtzeitpfad zu vermeiden.
4. Die Windows-Audioimplementierung bleibt austauschbar und hängt nur von Schnittstellen aus `Cello.Core` ab.
5. Razor-Komponenten enthalten keine direkten NAudio-, WASAPI- oder WinRT-Abhängigkeiten.
6. Die bestehende WinUI-Anwendung wird nicht entfernt selbst wenn Funktionsumfang und Latenz des Hybrid-Hosts verifiziert sind. Sie bleibt als Verifizierungsprojekt weiterhin vorhanden.

## Lokale Voraussetzungen

- .NET 9 SDK
- .NET-MAUI-Workload
- Windows App SDK / WebView2 Runtime
- Für andere Zielplattformen die jeweiligen Android-, iOS- oder MacCatalyst-Werkzeuge

## Referenzen

- [ASP.NET Core Blazor Hybrid](https://learn.microsoft.com/aspnet/core/blazor/hybrid/)
- [.NET MAUI BlazorWebView](https://learn.microsoft.com/dotnet/maui/user-interface/controls/blazorwebview)
- [Blazor Hybrid und Web App](https://learn.microsoft.com/aspnet/core/blazor/hybrid/tutorials/maui-blazor-web-app)
- [Blazor WebAssembly PWA](https://learn.microsoft.com/aspnet/core/blazor/progressive-web-app/)
