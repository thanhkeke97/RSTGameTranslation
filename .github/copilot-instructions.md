# Copilot / AI agent instructions — RSTGameTranslation

Purpose: help an AI coding agent become productive quickly in this repository by describing architecture, key files, build/run commands, conventions, and integration points.

1) Big picture
- Windows WPF application (UI in XAML) that performs real-time OCR, translation and optional audio STT.
- Core pieces:
  - UI & settings: main WPF app (open solution `RST.sln`).
  - OCR pipeline: selectable OCR backends (OneOCR, PaddleOCR, EasyOCR, RapidOCR). See `app/` and README for setup details.
  - Translation/backends: many adapters (Gemini, Groq, ChatGPT, Google Translate, Ollama, LM Studio). Config via files under `app/` and `config.txt`.
  - Audio STT: implemented in `src/localWhisperService.cs` — uses NAudio (WasapiLoopbackCapture) + Whisper.Net.
  - Core orchestration: singletons like `Logic.Instance` and `ConfigManager.Instance` coordinate capture → processing → UI.

2) Build & run (practical)
- Solution: `RST.sln` (root). Build with dotnet CLI:

  dotnet build RST.sln

- Publish or create distributable with:

  dotnet publish RST.sln

- Development run (fast iteration): use the provided task `watch` or `dotnet watch run --project RST.sln`.
- There are predefined VS Code tasks in the workspace (labels: `build`, `publish`, `watch`). Use them or the dotnet commands above.

3) Project-specific patterns & conventions
- Singletons: many services expose a static `Instance` property (e.g. `localWhisperService.Instance`, `Logic.Instance`, `ConfigManager.Instance`). Look for Instance usage when tracing flows.
- Background work: tasks + CancellationTokenSource are used for long-running processing (audio, OCR). Ensure `Stop()` is called to cancel tasks and dispose native resources.
- Exception handling: code frequently swallows exceptions (try/catch with empty bodies) — prefer adding minimal logs when changing behavior.
- UI updates: always marshal to UI thread using `Application.Current.Dispatcher.InvokeAsync(...)`.
- Audio pipeline: `WasapiLoopbackCapture` → `BufferedWaveProvider` → `WdlResamplingSampleProvider` → `Whisper.net` processor. See `src/localWhisperService.cs` for exact flow and debug toggles (debug WaveFileWriter lines commented).

4) Integration points & external dependencies
- Native/managed libs: NAudio, Whisper.Net (GGML), System.Speech, many optional Python-based OCR tools.
- Model files: audio models referenced by `ConfigManager.Instance._audioProcessingModelFolderPath` and named `*.bin` in code (see `localWhisperService`). Ensure models exist on disk for audio STT.
- App state & logs: runtime files under `app/` (for example `last_llm_reply_received.txt`, `custom_api_last_error.txt`, `translation_cache.json`) are useful for debugging live runs.

5) Where to start when changing features
- For audio STT fixes / shutdown behavior: edit `src/localWhisperService.cs` (StartServiceAsync, ProcessLoop, Stop).
- For OCR area capture & overlay: inspect code under `app/` and XAML windows in `src/`.
- For translation logic / queuing: inspect `Logic` class and how `TranslateTextObjectsAsync()` is called.

6) Debugging tips
- Enable console logging (many Console.WriteLine exist) and review `app/` log files.
- To capture raw audio for troubleshooting, uncomment the debug WaveFileWriter lines in `localWhisperService`.
- Typical failure modes: missing model files, audio device in use, long-running tasks not cancelled — search for `CancellationTokenSource` usages and ensure disposal/Stop is called.

7) Pull request & change guidance for AI agents
- Make minimalist, focused PRs. Keep public APIs and user-visible behaviors stable.
- When modifying long-running services, add proper cancellation and resource disposal; prefer small unit-of-work commits.
- Reference concrete files in PR descriptions (e.g., `src/localWhisperService.cs`, `RST.sln`).

If anything here is unclear or you want more detail for a specific subsystem (audio, OCR adapters, translation backends), say which area and I'll expand with code examples and key functions to inspect.
