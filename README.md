<div align="center">

# 🎮 RSTGameTranslation
### Real-time Screen Translation for Gaming

[![Version](https://img.shields.io/badge/version-3.3-blue.svg)](https://github.com/thanhkeke97/RSTGameTranslation/releases)
[![License](https://img.shields.io/badge/license-BSD-green.svg)](LICENSE.md)
[![Platform](https://img.shields.io/badge/platform-Windows%2010+-lightgrey.svg)]()

*Translate your games in real-time with AI-powered OCR and LLM technology*

[📥 Download](https://github.com/thanhkeke97/RSTGameTranslation/releases) • [📖 Hướng dẫn tiếng việt](README_vi.md) • [🐛 Report Bug](https://github.com/thanhkeke97/RSTGameTranslation/issues)

</div>

---

## ✨ Features

- **Real-time Translation** with multiple OCR options (OneOCR, Windows OCR, PaddleOCR, EasyOCR, RapidOCR)
- **AI-Powered Translation** with Gemini, ChatGPT, Google Translate, Ollama, Mistral, LM Studio
- **Smart Recognition** with game context awareness and character name detection
- **Flexible Display** options with overlay and chat window
- **Text-to-Speech** functionality

![Preview](media/preview_video.gif)

---

## 🚀 Quick Start

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
1. **Conda Install**: (Setup is only needed the first time the application is opened, no need to reinstall)
   - Go to **Settings** → **OCR**: Press button Setup Conda (Remember that the path to the folder containing the application must not have spaces; otherwise, the conda installation will fail)
   - Wait until the conda setup is successful, close the application and reopen it.

3. **OCR Options**: (Setup is only needed the first time the new OCR is chosen, no need to reinstall.)
   - Built-in: OneOCR, Windows OCR (no setup needed)
   - External: Click **SetupOCR** for PaddleOCR, RapidOCR, EasyOCR (5-15 min wait)

4. **Translation Services**:
   - No API needed: Google Translate
   - API required: Gemini, ChatGPT (add keys in Settings)
   - Local options: Ollama, LM Studio

5. **Start translating**:
   - Click **StartOCR** (if using external OCR) and wait until it starts successfully (You will see a red notification line at the bottom left corner)
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

## ⌨️ Keyboard Shortcuts

| Key | Function | | Key | Function |
|-----|----------|-|-----|----------|
| `Alt+G` | Start/Stop | | `Alt+F` | Show/Hide Overlay |
| `Alt+Q` | Select Area | | `Alt+C` | Show/Hide ChatBox |
| `Alt+P` | Settings | | `Alt+L` | Show/Hide Log |
| `Alt+B` | Show/Hide Area | | `Alt+H` | Clear Area |

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

## 📄 License

BSD-style attribution - see [LICENSE.md](LICENSE.md)

**Acknowledgments**: Includes software developed by Seth A. Robinson - [UGTLive](https://github.com/SethRobinson/UGTLive)

<div align="center">

**Made with ❤️ for the gaming community**

[⭐ Star this project](https://github.com/thanhkeke97/RSTGameTranslation) if you find it helpful!

</div>
