<div align="center">

# 🎮 RSTGameTranslation
### Real-time Screen Translation for Gaming

[![Version](https://img.shields.io/badge/version-2.8-blue.svg)](https://github.com/thanhkeke97/RSTGameTranslation/releases)
[![License](https://img.shields.io/badge/license-BSD-green.svg)](LICENSE.md)
[![Platform](https://img.shields.io/badge/platform-Windows%2010+-lightgrey.svg)]()

*Translate your games in real-time with AI-powered OCR and LLM technology*

[📥 Download](https://github.com/thanhkeke97/RSTGameTranslation/releases) • [📖 Vietnamese Guide](https://thanhkeke97.github.io/RSTGameTranslation/index_vi.html) • [🐛 Report Bug](https://github.com/thanhkeke97/RSTGameTranslation/issues)

</div>

---

## ✨ Features

🔥 **Real-time Translation**
- Live automatic translations using EasyOCR, PaddleOCR, RapidOCR, OneOCR and Windows OCR
- Powerful overlay chat window for visual novels
- Full-screen translation capability
- Can display translated text overlaid on the original text in the selected area (Hotkey Alt+F, Overlay on Windows 10 will flicker)

🤖 **AI-Powered Translation**
- Support for multiple LLMs: Gemini, ChatGPT, Ollama, Mistral, LM Studio
- Google Translate integration
- Context-aware translations for better accuracy

🎯 **Smart Recognition**
- Game-specific translation optimization
- Previous context consideration
- Character and location name recognition

🔊 **Additional Features**
- Text-to-speech functionality
- 100% local translation option with Ollama or LM Studio

---

![Preview](media/preview_video.gif)

## 🚀 Quick Start

### Prerequisites

- **Windows 10+ and** (Required)
- **NVIDIA GPU** (Recommended for best performance, Optional)
- Game in **windowed/borderless mode** (Required)

### Installation

1. **Download** the latest version from [Releases](https://github.com/thanhkeke97/RSTGameTranslation/releases)
2. **Extract** the zip file to your desired location
3. **Install Conda** For the Conda installation guide, follow this [link](https://thanhkeke97.github.io/RSTGameTranslation/) (See on Installation and Usage)

### Setup Guide

<details>
<summary>📋 Step-by-step Setup</summary>

#### 1. Initial Configuration
- Run `RSTGameTranslation/rst.exe`
- Go to **Settings** → **OCR** tab: Choose OCR method
- Go to **Settings** → **Language** tab: Choose source and target languages (If you are using Windows OCR, please click the "Check" button to verify the language pack before starting)
- Go to **Settings** → **Translation** tab: Select your preferred translation service

#### 2. Server Setup (One-time)
- Click **SetupOCR** button (Skip if using Windows OCR, OneOCR)
- Wait 5-15 minutes for setup completion
- Look for "environment setup completed" message

#### 3. Start Translating
- Click **StartOCR** and wait for connection confirmation (Skip if using Windows OCR, OneOCR)
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
| `Alt + B` | Show/Hide Selected Area | Works globally |
| `Alt + H` | Clear Selected Area | Works globally |

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
- **NVIDIA**: PaddleOCR, RapidOCR, OneOCR or EasyOCR
- **AMD, INTEL**: RapidOCR, OneOCR or Windows OCR

### Alternative Options
- **Ollama**: 100% local translation (RTX 4090: ~5s per translation)
- **LM Studio**: Local translation with customizable models
- **ChatGPT**: GPT-4.1 Nano for premium results

### Performance Tips
- Smaller translation areas = faster processing
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
- Built on EasyOCR, RapidOCR and PaddleOCR foundations

---

## ⚠️ Important Notes

> **Privacy**: Only checks GitHub for version updates. Cloud translation services (Gemini, ChatGPT) will see translated content. Ollama and LM Studio keep everything local.

> **Performance**: CPU-only mode will use 30-50% CPU power. Dedicated NVIDIA GPU strongly recommended.

---

## 💬 Community

Join our Discord community for support, discussions, and updates:

[![Discord](https://img.shields.io/badge/Join%20our-Discord-7289DA.svg)](https://discord.gg/FusrDU5tdn)

Get help from other users, share your experiences, and stay updated on the latest developments!

---

## 📄 License

This project is licensed under BSD-style attribution - see [LICENSE.md](LICENSE.md) for details.

**Acknowledgments**: This product includes software developed by Seth A. Robinson - [UGTLive](https://github.com/SethRobinson/UGTLive)

---

<div align="center">

**Made with ❤️ for the gaming community**

[⭐ Star this project](https://github.com/thanhkeke97/RSTGameTranslation) if you find it helpful!

</div>