<div align="center">

# 🎮 RSTGameTranslation
### Real-time Screen Translation for Gaming

[![Version](https://img.shields.io/badge/version-6.1-blue.svg)](https://github.com/thanhkeke97/RSTGameTranslation/releases)
[![License](https://img.shields.io/badge/license-BSD-green.svg)](LICENSE.md)
[![Platform](https://img.shields.io/badge/platform-Windows%2010+-lightgrey.svg)]()


*Translate games, manga and game voices in real time with OCR and AI translation*

[📥 Download](https://github.com/thanhkeke97/RSTGameTranslation/releases) • [⚙️ Settings guide](https://thanhkeke97.github.io/RSTGameTranslation/#settings) • [📖 Vietnamese Guide](README_vi.md) • [🐛 Report Bug](https://github.com/thanhkeke97/RSTGameTranslation/issues)

</div>

---

## ✨ Features

- **Translate any game in real time**: reads the text on screen (OneOCR or Windows OCR, built in) and shows the translation right on top of it
- **Many translation services**: Google Translate (free, no key), Gemini, Groq, ChatGPT, Mistral, or fully offline with Ollama and LM Studio
- **Natural-looking overlay**: the original text is erased and the translation is drawn on the original background, in a matching color
- **Manga Mode**: recognizes speech bubbles, reads Japanese vertical text right to left, and also works with English comics
- **Translate game voices**: turns spoken dialogue into translated text (Whisper or FunASR, runs on your PC)
- **Read translations aloud** (Text-to-Speech): ElevenLabs, Google Cloud TTS, Windows TTS, or Supertonic (free, offline)
- **Smart translation**: uses previous lines and game information for more accurate results
- **Clipboard translation**: translates any text you copy

![Preview](media/preview_video.gif)

---

## 🚀 Quick Start (about 3 minutes)

**You need:** Windows 10 or 11. Run your game in **windowed** or **borderless windowed** mode (exclusive fullscreen hides the translation).

1. **Download** the latest version from [Releases](https://github.com/thanhkeke97/RSTGameTranslation/releases), extract it anywhere, and run `rst.exe`
2. **Choose languages**: **Settings → Language**, pick the game's language and your language
3. **Choose a translator**: **Settings → Translation**, select **Google Translate** (free, no API key needed)
4. **Choose what to translate**:
   - Click **Select Application** and pick your game window, **or**
   - Press **Alt+Q** and drag a box around the text area
5. Press **Alt+F** to show the **Overlay**, then **Alt+G** to **Start**

That's it. The translation appears over the game and updates as the text changes. Press **Alt+G** again to stop.

> 💡 On **Windows 10**, click **Select Application** before turning on the overlay.

---

## ⌨️ Hotkeys

| Hotkey | Action |
|---|---|
| **Alt+G** | Start / stop translating |
| **Alt+Q** | Select the area to translate |
| **Alt+F** | Show / hide the overlay |
| **Alt+C** | Show / hide the ChatBox (translation history window) |
| **Alt+T** | Translate the current text again |
| **Alt+K** | Start / stop voice translation |
| **Alt+B** | Show the selected areas |
| **Alt+P** | Open Settings |
| **Alt+L** | Open the log window |

You can change every hotkey in **Settings → HotKeys**.

---

## 📖 Guides

### 🎮 Translating a game

- **Select Application** follows the game window, even when you move it. **Select Area (Alt+Q)** translates a fixed part of the screen. Turn on **Multi Selection Area** to add up to 5 areas; all of them are translated at once.
- **Overlay** shows the translation on top of the original text. **ChatBox (Alt+C)** shows it as a list, which is easier for visual novels or when the text area is small.
- Turn on **Settings → Context/Overlay → Auto Set Overlay Background Color** to erase the original text and blend the translation into the game.
- Describe the game and its main characters in **Settings → Context/Overlay → Info about the game** for more accurate translations with AI services. Save your settings per game with **Settings → Game Profile**.
- If some on-screen text (menus, HUD) keeps getting translated, mark it in **Settings → Exclude Regions** or **Ignore Phrases**.

### 📚 Reading manga and comics

1. **Settings → OCR/Screen Selection**: choose **OneOCR**, then turn on **Manga Mode**
2. **Settings → Language**: set the source language (Japanese, English, …)
3. Press **Alt+Q** and select the whole page (or the reader window with **Select Application**)
4. **Alt+F** for the overlay, **Alt+G** to start

Manga Mode finds each speech bubble in the image, so neighbouring bubbles are never mixed up. For Japanese it reads vertical columns right to left and skips furigana. The whole page is translated in one go, while every bubble keeps its own translation.

### 🎙️ Translating game voices (speech-to-text)

1. **Settings → Audio**: choose a **Provider**
   - **Whisper**: many languages. Click **Download Whisper Model…** and pick a model (the dialog marks the recommended one)
   - **FunASR**: Japanese, Chinese, Korean, English, Cantonese. Fast and accurate. Click **Download FunASR Model**
2. Recommended under **Speech capture & detection**: **Capture source** = *All system audio except this app* and **Speech detection** = *Silero VAD*
3. Press **Alt+K** (or the **Audio STT** button) to start. Translations appear in the ChatBox

### 🔊 Reading translations aloud (Text-to-Speech)

Choose a service in **Settings → TTS**:

| Service | Cost | Internet | Languages | Best for |
|---|---|---|---|---|
| **Supertonic** | Free | Not needed | 31 | Offline, no API key |
| **Windows TTS** | Free | Not needed | Voices installed in Windows | No setup |
| **ElevenLabs** | Free tier + paid | Needed | Many | Most natural voices |
| **Google Cloud TTS** | Pay per character | Needed | 50+ | Widest language coverage |

When translations arrive faster than they can be read, **Speed up when behind** reads the next lines faster (up to 1.5x) to keep up with the game.

<details>
<summary><b>Setting up Supertonic (free, offline)</b></summary>

[Supertonic](https://github.com/supertone-inc/supertonic) by [Supertone](https://www.supertone.ai/) runs entirely on your CPU: no cloud, no API key. After a one-time download (about 400 MB) it works offline.

1. **Settings → TTS**, set **TTS Service** to `Supertonic`
2. Click **Download model** (from Hugging Face)
3. Pick a voice style (M1–M5, F1–F5)
4. Optional: adjust **Quality** (default 8) and **Speech speed** (default 1.05)

**Languages:** `en, ko, ja, ar, bg, cs, da, de, el, es, et, fi, fr, hi, hr, hu, id, it, lt, lv, nl, pl, pt, ro, ru, sk, sl, sv, tr, uk, vi` and `na` (auto-detect).

The first sentence after starting the app may take 1–2 seconds while the model loads. License: SDK MIT, model OpenRAIL-M ([model files](https://huggingface.co/Supertone/supertonic-3)).

</details>

### 📋 Translating copied text

Turn on **Settings → Clipboard → Enable Clipboard Translation**. Any text you copy (Ctrl+C) is translated automatically.

---

## ⚙️ Recommended setups

| Goal | OCR | Translation |
|---|---|---|
| **Quick start, free** | OneOCR | Google Translate (no key) |
| **Best quality** | OneOCR | Gemini, model `gemini-flash-lite-latest` (free API key from Google AI Studio) |
| **Fully offline / private** | OneOCR or Windows OCR | Ollama or LM Studio |

**Tips for speed:** select only the text area instead of the whole screen, and add several API keys so another one is used when one hits its limit.

---

## ❓ Troubleshooting

<details>
<summary><b>The translation doesn't appear over the game</b></summary>

- Switch the game to **windowed** or **borderless windowed**. Exclusive fullscreen draws over everything.
- Make sure the overlay is on (**Alt+F**).
- On Windows 10, click **Select Application** and pick the game first.

</details>

<details>
<summary><b>Google Translate stops translating ("429" / rate limited)</b></summary>

The free Google service limits how many requests one PC can send. The app waits automatically and meanwhile uses the service set in **Settings → Translation → When rate limited**. If it happens often, switch to Gemini (free API key) or a local model (Ollama, LM Studio).

</details>

<details>
<summary><b>Manga text from different bubbles is mixed together</b></summary>

Turn on **Manga Mode** (needs **OneOCR**) and set the correct source language. Bubble detection works best on white or light speech bubbles.

</details>

<details>
<summary><b>Translations are slow</b></summary>

- Select a smaller area that only contains the text.
- Online AI services (Gemini, Groq) are usually faster than local models on a weak PC.
- For voice translation, keep **Fast mode for short lines** on and try a smaller Whisper model or FunASR.

</details>

Still stuck? Ask on [Discord](https://discord.gg/FusrDU5tdn) or [report a bug](https://github.com/thanhkeke97/RSTGameTranslation/issues).

---

## 💬 Community

Join our [Discord](https://discord.gg/FusrDU5tdn) for support and updates!

---

## 💖 Support the author

If you find RST useful and would like to support development, you can buy the author a coffee — thank you! 👋

<div align="center">

[![Buy Me a Coffee](https://img.shields.io/badge/Buy%20me%20a%20coffee-%23FFDD00?style=for-the-badge&logo=buymeacoffee&logoColor=black)](https://www.buymeacoffee.com/thanhkeke97)

</div>

---

## 🛠️ Open Source Technologies

This project stands on the shoulders of giants. We gratefully acknowledge the following open-source projects:

### Core & UI
- **[WPF (Windows Presentation Foundation)](https://github.com/dotnet/wpf)** - UI Framework

### AI & Translation
- **[System.Text.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-overview)** - High-performance JSON serialization
- **[Hugging Face](https://huggingface.co/)** - For various AI models and datasets

### Audio
- **[NAudio](https://github.com/naudio/NAudio)** - Audio and MIDI library for .NET
- **[System.Speech](https://learn.microsoft.com/en-us/dotnet/api/system.speech.synthesis.speechsynthesizer)** - .NET Speech Synthesis Library
- **[Whisper.Net](https://github.com/sandrohanea/whisper.net)** - .NET Speech To Text Library

### Others
- **[Python](https://www.python.org/)** - Backend scripting
- **[PyTorch](https://pytorch.org/)** - Machine learning framework

---

## 📄 License

BSD-style attribution - see [LICENSE.md](LICENSE.md)

**Acknowledgments**: Includes software developed by Seth A. Robinson - [UGTLive](https://github.com/SethRobinson/UGTLive)

| <img src="https://signpath.org/assets/favicon.png" width="25" height="25" align="center"> | Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/). |
| :--- | :--- |

<div align="center">

**Made with ❤️ for the gaming community**

[⭐ Star this project](https://github.com/thanhkeke97/RSTGameTranslation) if you find it helpful!

</div>
