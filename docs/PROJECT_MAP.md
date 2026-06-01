# AudioCompressor — PROJECT_MAP

## [TECH_STACK]

| Component              | Technology                          | Version / Notes                     |
|------------------------|-------------------------------------|--------------------------------------|
| Language               | C# 13                               | .NET 9.0 (SDK 9.0.306)              |
| UI Framework           | WPF                                 | Included in .NET 9 Desktop Runtime   |
| Pattern                | MVVM (CommunityToolkit.Mvvm)        | v8.4+                               |
| Audio I/O & Playback   | NAudio                              | v2.3.0 (WAV/PCM read/write/play)    |
| Real-time Charts       | ScottPlot.WPF                       | v5.1.58 (lightweight, SkiaSharp-based)|
| Testing                | xUnit                               | xUnit v2.9.2                        |
| Build                  | MSBuild / dotnet CLI                | .NET 9.0 SDK                        |
| Async Logging          | System.Threading.Channels           | Built-in (Channel<T>)                |
| IDE                    | Visual Studio 2022+ / JetBrains Rider| —                                   |

> **Why WPF over WinForms:** WPF provides native Drag & Drop, rich data binding (MVVM), superior styling, and seamless ScottPlot hosting via WindowsFormsHost or WPF-backed ScottPlot control.

> **Why ScottPlot:** Lightweight, actively maintained, optimized for real-time scientific plotting (update plots without UI freezes via `Render` queue).

> **Why NAudio:** De facto standard for .NET audio; supports WAV header parsing, PCM sample extraction, and multi-format playback.

---

## [SYSTEM_FLOW]

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  1. USER DRAGS .wav FILE INTO UI DROP ZONE                                 │
│     │                                                                    │
│     ▼                                                                    │
│  2. Core.WavService.ReadFile()                                           │
│     ├─ Parse WAV header (RIFF chunks)                                     │
│     ├─ Extract PCM samples as float[] / short[]                           │
│     └─ Return WavFileInfo model (Size, Duration, SampleRate,              │
│        Channels, BitRate, Encoding)                                       │
│     │                                                                    │
│     ▼                                                                    │
│  3. UI displays file properties in read-only fields                       │
│     │                                                                    │
│     ▼                                                                    │
│  4. USER clicks Play → NAudio playback via WaveOutEvent                   │
│     │  (optional preview before compression)                              │
│     ▼                                                                    │
│  5. USER selects compression algorithm & parameters:                      │
│     ├─ [x] Nonlinear Quantization (A-law / μ-law, bits: 4/6/8)            │
│     ├─ [x] DPCM (predictor order: 1/2/4, quant bits: 4/6/8)              │
│     └─ [x] Delta Modulation (step size: auto/fixed)                       │
│     │                                                                    │
│     ▼                                                                    │
│  6. USER clicks "Compress"                                                │
│     │                                                                    │
│     ▼                                                                    │
│  7. CompressionEngine.CompressAsync() runs in background Task             │
│     ├─ Reports progress via IProgress<double> → UI ProgressBar            │
│     ├─ Pushes algorithm steps → AsyncLogger → Console                     │
│     ├─ Updates chart data (compression ratio over time)                   │
│     └─ Respects CancellationToken (Cancel button)                         │
│     │                                                                    │
│     ▼                                                                    │
│  8. On completion:                                                       │
│     ├─ Compressed WAV saved to disk (auto-named)                          │
│     ├─ ReportWindow shows: Before Size, After Size, Savings %,            │
│     │  Elapsed Time, Algorithm + Settings                                 │
│     └─ User can reset or load a new file                                  │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Cancel Path:** User clicks Cancel → CancellationTokenSource.Cancel() → Compression loop checks `token.IsCancellationRequested` → cleanup partial output.

**Reset Path:** Clears all fields, disposes audio, resets chart, frees WAV resources.

---

## [ARCHITECTURE]

### Layer Diagram (Strict Separation)

```
 ┌───────────────────────────────────────────────────────────┐
 │                   AudioCompressor.UI (WPF)                 │
 │  ┌──────────┐  ┌──────────────┐  ┌─────────────────────┐ │
 │  │  Views/  │  │  ViewModels/ │  │     Converters/     │ │
 │  │MainWindow│  │MainViewModel │  │FileSize, TimeFormat  │ │
 │  │ReportWnd │  │ReportVM      │  │BytesToHumanReadable │ │
 │  └────┬─────┘  └──────┬───────┘  └─────────────────────┘ │
 │       │               │  binds to                        │
 │       │  depends on   │  Core models/services            │
 └───────┼───────────────┼───────────────────────────────────┘
         │               │
         ▼               ▼
 ┌───────────────────────────────────────────────────────────┐
 │                 AudioCompressor.Core (Class Library)       │
 │  ┌──────────────┐  ┌────────────┐  ┌──────────────────┐  │
 │  │   Models/    │  │  Services/ │  │   Algorithms/    │  │
 │  │WavFileInfo   │  │ IWavService│  │ INonlinearQuant  │  │
 │  │CompressResult│  │ WavService │  │ IDPCM            │  │
 │  │              │  │ IPlayService│ │ IDeltaMod        │  │
 │  └──────┬───────┘  └─────┬──────┘  └────────┬─────────┘  │
 │         │                │                   │            │
 │         ▼                ▼                   ▼            │
 │  ┌─────────────────────────────────────────────────┐      │
 │  │            CompressionEngine.cs                 │      │
 │  │  Orchestrates: read → compress → write → report │      │
 │  └──────────────────────┬──────────────────────────┘      │
 │                         │                                  │
 │                         ▼                                  │
 │  ┌─────────────────────────────────────────────────┐      │
 │  │         Logging / AsyncLogger.cs                │      │
 │  │  Channel<string> producer-consumer → Console    │      │
 │  └─────────────────────────────────────────────────┘      │
 └───────────────────────────────────────────────────────────┘
```

### Domain-Driven Module Map

```
src/
├── AudioCompressor.UI/               [WPF App, .NET 9]
│   ├── App.xaml / App.cs
│   ├── Views/
│   │   ├── MainWindow.xaml           [Main workspace: drop zone, props, controls, chart]
│   │   └── ReportWindow.xaml         [Post-compression report modal]
│   ├── ViewModels/
│   │   ├── MainViewModel.cs          [State management, commands]
│   │   └── ReportViewModel.cs        [Report data binding]
│   └── Converters/
│       └── FormatConverters.cs       [FileSize, TimeSpan, Percentage]
│
├── AudioCompressor.Core/             [Class Library, .NET 9]
│   ├── Models/
│   │   ├── WavFileInfo.cs            [Immutable record: Size, Duration, SampleRate, Channels, BitRate, Encoding]
│   │   ├── CompressionResult.cs      [Result: algorithm, settings, before/after size, ratio, time]
│   │   └── CompressionConfig.cs      [Parameters: algorithm type, quant bits, step size, sample rate override]
│   ├── Services/
│   │   ├── IWavService.cs / WavService.cs       [Read WAV header + samples, write WAV]
│   │   └── IAudioPlaybackService.cs / NAudioPlaybackService.cs  [Play/Stop/Pause via NAudio]
│   ├── Algorithms/
│   │   ├── INonlinearQuantization.cs / NonlinearQuantization.cs  [A-law / μ-law encode/decode]
│   │   ├── IDPCM.cs / DPCM.cs                                    [DPCM encode/decode with configurable order]
│   │   └── IDeltaModulation.cs / DeltaModulation.cs              [Delta Mod / Adaptive Delta Mod]
│   ├── CompressionEngine.cs          [Orchestrator: validate → compress → write]
│   └── Logging/
│       └── AsyncLogger.cs            [Channel<string> → background consumer → Console]
│
└── AudioCompressor.Tests/            [xUnit, .NET 9]
    └── AlgorithmTests.cs             [Round-trip: compress → decompress → compare samples]
```

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| MVVM with CommunityToolkit.Mvvm | Source generators for `[ObservableProperty]`, `[RelayCommand]` — zero boilerplate |
| Interfaces per algorithm | Swap algorithms without touching engine; test each in isolation |
| Channel<string> for logging | Non-blocking producer-consumer; background task drains to Console |
| IProgress<double> for progress | Built-in .NET pattern; decouples progress reporting from UI thread |
| CancellationToken per compress | Clean cancellation; no thread abort or dirty flags |
| All state in ViewModel | No code-behind logic; XAML only binds to ViewModel properties |

---

## [MILESTONES COMPLETED]

| # | Milestone | Key Deliverables | Status |
|---|-----------|------------------|--------|
| M1 | Scaffolding + WAV Engine | Solution setup, WavService (read/write 8/16/24/32-bit), NAudioPlaybackService, 6 tests | ✅ DONE |
| M2 | Compression Algorithms | ICompressionAlgorithm interface, NonlinearQuantization (μ-law/A-law), DPCM, DeltaMod (fixed/adaptive), CompressionEngine, AsyncLogger, 10 more tests (16 total) | ✅ DONE |
| M3 | WPF UI (MVVM) | MainWindow with drag-drop, file properties panel, playback controls, compress settings (ComboBox/Slider/CheckBox), MainViewModel with 17 observable properties + 4 relay commands | ✅ DONE |
| M4 | Real-Time Monitoring | Progress reporting via IProgress<double> in all algorithms, ScottPlot real-time chart, ProgressBar, log ListBox, Cancel button, chart data lists with PlotRefreshVersion signaling | ✅ DONE |
| M5 | Final Report + Polish | ReportWindow with detailed compression results, auto-save .wav to disk, chart reset on clear, exception handling, PROJECT_MAP finalization | ✅ DONE |

> **Project Status: 100% COMPLETE** — All requirements implemented, `dotnet build` passes with 0 errors, 16/16 tests passing.
