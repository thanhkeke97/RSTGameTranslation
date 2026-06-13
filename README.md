<div align="center">

# 🎮 RSTGameTranslation
### Real-time Screen Translation for Gaming

[![Version](https://img.shields.io/badge/version-5.1-blue.svg)](https://github.com/thanhkeke97/RSTGameTranslation/releases)
[![License](https://img.shields.io/badge/license-BSD-green.svg)](LICENSE.md)
[![Platform](https://img.shields.io/badge/platform-Windows%2010+-lightgrey.svg)]()


*Translate your games in real-time with AI-powered OCR and LLM technology*

[📥 Download](https://github.com/thanhkeke97/RSTGameTranslation/releases) • [⚙️ Settings guide](https://thanhkeke97.github.io/RSTGameTranslation/#settings) • [📖 Vietnamese Guide](README_vi.md) • [🐛 Report Bug](https://github.com/thanhkeke97/RSTGameTranslation/issues)

</div>

---

## ✨ Features

- **Real-time Translation** with multiple OCR options (OneOCR, Windows OCR, PaddleOCR, EasyOCR, RapidOCR)
- **AI-Powered Translation** with Gemini, Groq, ChatGPT, Google Translate, Ollama, Mistral, LM Studio
- **Smart Recognition** with game context awareness and character name detection
- **Flexible Display** options with overlay and chat window
- **Text-to-Speech** feature with 4 backends: ElevenLabs (cloud), Google Cloud TTS (cloud), Windows TTS (local), and **Supertonic** (free, on-device, 31 languages, OpenRAIL-M model)
- **Speech-to-Text** functionality (Recognize speech from game audio and translate it)

![Preview](media/preview_video.gif)

---

## � Text-to-Speech (TTS)

RST supports four TTS backends. Pick the one that fits your needs:

| Backend | Cost | Internet | Privacy | Languages | Best for |
|---|---|---|---|---|---|
| **ElevenLabs** | Free tier + paid | Required | Cloud | Many | Highest naturalness |
| **Google Cloud TTS** | Pay-per-character | Required | Cloud | 50+ | Wide language coverage |
| **Windows TTS** | Free | Not required | Local | System voices | No setup, no network |
| **Supertonic** (new) | **Free** | **Not required** | **100% local** | **31** | Offline, multilingual, no API key |

### Supertonic (recommended for offline / free TTS)

[Supertonic](https://github.com/supertone-inc/supertonic) is a lightning-fast, on-device multilingual TTS by [Supertone](https://www.supertone.ai/). It runs entirely on your CPU via ONNX Runtime - no cloud, no API key, no privacy concerns. After the one-time ~400 MB model download, it works completely offline.

**How to enable:**

1. Open **Settings → TTS**
2. Set **TTS Service** = `Supertonic`
3. Click **Download model** (≈400 MB, downloaded from Hugging Face)
4. Pick a voice style (M1, F1, M2, F2, M3, M3, M4, M5, F3, F4, F5)
5. Optionally tune **Quality** (denoise steps, default 8) and **Speech speed** (default 1.05)

**Supported languages** (31): `en, ko, ja, ar, bg, cs, da, de, el, es, et, fi, fr, hi, hr, hu, id, it, lt, lv, nl, pl, pt, ro, ru, sk, sl, sv, tr, uk, vi` plus `na` (language-agnostic auto-detect).

**Notes:**
- First SpeakText call after app launch may take 1-2s while the model loads; the app will warm the engine in the background if Supertonic is already selected, so subsequent calls are instant.
- License: SDK is **MIT**, model is **OpenRAIL-M** (free for commercial use with responsible-AI conditions).
- Model files: https://huggingface.co/Supertone/supertonic-3

---

## �🚀 Quick Start

### Prerequisites
- Windows 10+ and game in windowed/borderless mode
- NVIDIA GPU recommended but optional

### Installation
1. Download from [Releases](https://github.com/thanhkeke97/RSTGameTranslation/releases) and extract

### Setup Options

#### 🔵 Simple Setup (No Installation)
1. Run `rst.exe`
2. Go to **Settings** → **OCR**: Select **OneOCR** 
3. Go to **Settings** → **Language**: Choose languages
4. Go to **Settings** → **Translation**: Select **Google Translate**
5. Press button ***Select Window***: Choose window which you want to capture
6. Press **Alt+Q** to select area, then **Alt+F** to turn on Overlay
7. Press **Alt+G** to start/stop

#### 🔴 Advanced Setup (Need Installation)
1. **OCR Options**: (Setup is only needed the first time the new OCR is chosen, no need to reinstall.)
   - Built-in: OneOCR, Windows OCR (no setup needed)
   - External: Click **SetupOCR** for PaddleOCR, RapidOCR, EasyOCR (5-15 min wait)
   
2. **Translation Services**:
   - No API needed: Google Translate
   - API required: Gemini, Groq, Mistral, ChatGPT (add keys in Settings)
   - Local options: Ollama, LM Studio

3. **Start translating**:
   - Click **StartOCR** (if using external OCR) and wait until it starts successfully (You will see a red notification line at the bottom right corner)
   - Press button ***Select Window***: Choose window which you want to capture
   - Select area (Alt+Q) then turn on overlay (Alt+F)
   - Start translate (Alt+G)

---

## 🔄 How to Update

RSTGameTranslation will automatically check for updates when you start it. If there's a new version, you'll see a notification asking if you want to download it. To update:

1. Download the latest version from the notification or from [Releases](https://github.com/thanhkeke97/RSTGameTranslation/releases)
2. Close RSTGameTranslation if it's running
3. Extract the new files to your current installation folder
4. Done! Your settings and options will be preserved

---

## ⚙️ Recommended Setups

### For Quick Use
- **OCR**: OneOCR or Windows OCR (built-in, no setup)
- **Translation**: Google Translate (no API key needed)

### For Best Quality
- **OCR**: PaddleOCR (Asian) or RapidOCR (Western) or EasyOCR
- **Translation**: Gemini Flash lite 2.5 (Need API key)
- **Hardware**: NVIDIA GPU recommended

### For Privacy
- **OCR**: OneOCR or Windows OCR
- **Translation**: Ollama or LM Studio (100% local)

### Performance Tips
- Smaller areas = faster processing
- Add multiple API keys for failover
- First language download takes 1-2 minutes (external OCR)

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

### OCR (Optical Character Recognition)
- **[PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR)** - Awesome multilingual OCR toolkits
- **[EasyOCR](https://github.com/JaidedAI/EasyOCR)** - Ready-to-use OCR with 80+ supported languages
- **[RapidOCR](https://github.com/RapidAI/RapidOCR)** - Cross platform OCR library based on OnnxRuntime

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
