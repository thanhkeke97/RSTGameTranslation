<div align="center">

# 🎮 RSTGameTranslation
### Real-time Screen Translation for Gaming

[![Version](https://img.shields.io/badge/version-0.85-blue.svg)](https://github.com/thanhkeke97/RSTGameTranslation/releases)
[![License](https://img.shields.io/badge/license-BSD-green.svg)](LICENSE.md)
[![Platform](https://img.shields.io/badge/platform-Windows%2010+-lightgrey.svg)]()

*Translate your games in real-time with AI-powered OCR and LLM technology*

[📥 Download](https://github.com/thanhkeke97/RSTGameTranslation/releases) • [📖 Vietnamese Guide](https://thanhkeke97.github.io/RSTGameTranslation/index_vi.html) • [🐛 Report Bug](https://github.com/thanhkeke97/RSTGameTranslation/issues)

</div>

---

## ✨ Features

🔥 **Real-time Translation**
- Live automatic translations using EasyOCR or PaddleOCR
- Powerful overlay chat window for visual novels
- Full-screen translation capability
- Can display translated text overlaid on the original text in the selected area (Hotkey Alt+F, only work win 11)

🤖 **AI-Powered Translation**
- Support for multiple LLMs: Gemini, ChatGPT, Ollama
- Google Translate integration
- Context-aware translations for better accuracy

🎯 **Smart Recognition**
- Game-specific translation optimization
- Previous context consideration
- Character and location name recognition

🔊 **Additional Features**
- Text-to-speech functionality
- 100% local translation option with Ollama

---

![Preview](media/preview_video.gif)

## 🚀 Quick Start

### Prerequisites

- **Windows 10+ and** (Required)
- **Windows 11 for display translated text overlaid on the original text feature** (Optional)
- **NVIDIA GPU** (Recommended for best performance)
- **Conda** with PATH environment variable
- Game in **windowed/borderless mode**

### Installation

1. **Download** the latest version from [Releases](https://github.com/thanhkeke97/RSTGameTranslation/releases)
2. **Extract** the zip file to your desired location
3. **Install Miniconda** if you don't have Conda ([Download here](https://repo.anaconda.com/miniconda/Miniconda3-py39_25.3.1-1-Windows-x86_64.exe))

### Setup Guide

<details>
<summary>📋 Step-by-step Setup</summary>

#### 1. Initial Configuration
- Run `RSTGameTranslation/rst.exe`
- Go to **Settings** → **Language** tab: Choose source and target languages
- Go to **Settings** → **Translation** tab: Select your preferred translation service
- Go to **Settings** → **OCR** tab: Choose OCR method

#### 2. Server Setup (One-time)
- Click **SetupServer** button (Skip if using Windows OCR)
- Wait 5-15 minutes for setup completion
- Look for "environment setup completed" message

#### 3. Start Translating
- Click **StartServer** and wait for connection confirmation (Skip if using Windows OCR)
- Select translate area (ALT+Q or Click on SelectArea button)
- Click Start button (ALT+G) to begin translation
- View results in ChatBox or Monitor window

#### 4. LLM Configuration
- Add your **Gemini API key** in settings (You can enter multiple API keys, press Enter after entering each API key)
- Configure game name for better context (context tab)
- Adjust other settings as needed

</details>

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Function | Note |
|----------|----------|------|
| `Alt + G` | Start/Stop | Works globally |
| `Alt + Q` | Select Translation Area | Works globally |
| `Alt + F` | Show/Hide Monitor Overlay | Works globally |
| `Alt + C` | Show/Hide ChatBox | Works globally |
| `Alt + P` | Show/Hide Settings | Works globally |
| `Alt + L` | Show/Hide Log Console | Works globally |

---

## 🔄 Updates

RSTGameTranslation automatically checks for updates on startup. When available:

1. Download from notification or [Releases page](https://github.com/thanhkeke97/RSTGameTranslation/releases)
2. Close the application
3. Extract new files over existing installation
4. Restart - your settings are preserved!

---

## ⚙️ Advanced Configuration

### Recommended Setup
- **OCR**: PaddleOCR (Better for Asian languages, lower resource usage)
- **LLM**: Gemini Flash 2 Lite (Fast and accurate)
- **NVIDIA**: PaddleOCR or EasyOCR
- **AMD, INTEL**: Windows OCR

### Alternative Options
- **Windows OCR**: Lightweight but less accurate for gaming (only support for source language is English)
- **Ollama**: 100% local translation (RTX 4090: ~5s per translation)
- **ChatGPT**: GPT-4.1 Nano for premium results

### Performance Tips
- Smaller translation areas = faster processing
- NVIDIA GPU highly recommended
- First-time language downloads may take 1-2 minutes
- The application will automatically change API keys if the previous API is rate-limited, so please enter as many API keys as possible
- The translation speed depends on the LLM model; if you are using Gemini, you should check the translation speed at [here](https://aistudio.google.com/prompts/new_chat)
---

## 🛠️ For Developers

### Compilation
- Open solution in **Visual Studio 2022**
- Click compile (dependencies should auto-download)

### Python Server Development
- Use **VSCode** for development/debugging
- Built on EasyOCR and PaddleOCR foundations

---

## ⚠️ Important Notes

> **Privacy**: Only checks GitHub for version updates. Cloud translation services (Gemini, ChatGPT) will see translated content. Ollama keeps everything local.

> **Performance**: CPU-only mode will use 30-50% CPU power. Dedicated NVIDIA GPU strongly recommended.

> **Compatibility**: Experimental software, primarily tested with Japanese→English and English→Vietnamese translations.

---

## 📄 License

This project is licensed under BSD-style attribution - see [LICENSE.md](LICENSE.md) for details.

**Acknowledgments**: This product includes software developed by Seth A. Robinson - [UGTLive](https://github.com/SethRobinson/UGTLive)

---

<div align="center">

**Made with ❤️ for the gaming community**

[⭐ Star this project](https://github.com/thanhkeke97/RSTGameTranslation) if you find it helpful!

</div>
