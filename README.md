<div align="center">

# 🎮 RSTGameTranslation
### Real-time Screen Translation for Gaming

[![Version](https://img.shields.io/badge/version-3.3-blue.svg)](https://github.com/thanhkeke97/RSTGameTranslation/releases)
[![License](https://img.shields.io/badge/license-BSD-green.svg)](LICENSE.md)
[![Platform](https://img.shields.io/badge/platform-Windows%2010+-lightgrey.svg)]()

*Translate your games in real-time with AI-powered OCR and LLM technology*

[📥 Download](https://github.com/thanhkeke97/RSTGameTranslation/releases) • 
[🐛 Report Bug](https://github.com/thanhkeke97/RSTGameTranslation/issues)

<div>
  <a href="#english" id="en-btn" onclick="switchLanguage('en')" style="display:inline-block; padding:5px 15px; background-color:#4CAF50; color:white; text-decoration:none; border-radius:4px; margin-right:10px;">English</a>
  <a href="#vietnamese" id="vi-btn" onclick="switchLanguage('vi')" style="display:inline-block; padding:5px 15px; background-color:#2196F3; color:white; text-decoration:none; border-radius:4px;">Tiếng Việt</a>
</div>

</div>

---

<div id="english">

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
1. **Conda Install**
   - Go to **Settings** → **OCR**: Press button Setup Conda (Remember that the path to the folder containing the application must not have spaces; otherwise, the conda installation will fail)
   - Wait until the conda setup is successful, close the application and reopen it.

3. **OCR Options**:
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

</div>

<!-- Vietnamese Section -->
<div id="vietnamese" style="display:none">

## ✨ Tính năng

- **Dịch thời gian thực** với nhiều tùy chọn OCR (OneOCR, Windows OCR, PaddleOCR, EasyOCR, RapidOCR)
- **Dịch thuật bằng AI** với Gemini, ChatGPT, Google Translate, Ollama, Mistral, LM Studio
- **Nhận dạng thông minh** với nhận biết ngữ cảnh game và phát hiện tên nhân vật
- **Hiển thị linh hoạt** với overlay và cửa sổ chat
- **Chức năng Text-to-Speech** (đọc văn bản)

![Xem trước](media/preview_video.gif)

---

## 🚀 Bắt đầu nhanh

### Yêu cầu hệ thống
- Windows 10 trở lên và game ở chế độ cửa sổ/không viền
- Khuyến nghị có GPU NVIDIA (không bắt buộc)

### Cài đặt
1. Tải về từ [Trang Releases](https://github.com/thanhkeke97/RSTGameTranslation/releases) và giải nén

### Tùy chọn thiết lập

#### 🔵 Thiết lập đơn giản (Không cần cài đặt thêm)
1. Chạy `rst.exe`
2. Vào **Cài đặt** → **OCR**: Chọn **OneOCR**
3. Vào **Cài đặt** → **Language**: Chọn ngôn ngữ nguồn và đích
4. Vào **Cài đặt** → **Translation**: Chọn **Google Translate**
5. Nhấn nút ***Select Window***: Chọn cửa sổ bạn muốn chụp
6. Nhấn **Alt+Q** để chọn vùng, sau đó **Alt+F** để bật Overlay
7. Nhấn **Alt+G** để bắt đầu/dừng dịch

#### 🔴 Thiết lập nâng cao (Cần cài đặt thêm)
1. **Cài đặt Conda**
   - Vào **Cài đặt** → **OCR**: Nhấn nút Setup Conda (Lưu ý đường dẫn đến thư mục chứa ứng dụng không được có khoảng trắng, nếu không việc cài đặt conda sẽ thất bại)
   - Đợi cho đến khi cài đặt conda thành công, đóng ứng dụng và mở lại.

3. **Tùy chọn OCR**:
   - Tích hợp sẵn: OneOCR, Windows OCR (không cần thiết lập)
   - Bên ngoài: Nhấn **SetupOCR** cho PaddleOCR, RapidOCR, EasyOCR (đợi 5-15 phút)

4. **Dịch vụ dịch thuật**:
   - Không cần API: Google Translate
   - Cần API: Gemini, ChatGPT (thêm khóa API trong Cài đặt)
   - Tùy chọn cục bộ: Ollama, LM Studio

5. **Bắt đầu dịch**:
   - Nhấn **StartOCR** (nếu sử dụng OCR bên ngoài) và đợi cho đến khi khởi động thành công (Bạn sẽ thấy một dòng thông báo màu đỏ ở góc dưới bên trái)
   - Nhấn nút ***Select Window***: Chọn cửa sổ bạn muốn chụp
   - Chọn vùng (Alt+Q) sau đó bật overlay (Alt+F)
   - Bắt đầu dịch (Alt+G)

---

## ⌨️ Phím tắt

| Phím | Chức năng | | Phím | Chức năng |
|-----|----------|-|-----|----------|
| `Alt+G` | Bắt đầu/Dừng | | `Alt+F` | Hiện/Ẩn Overlay |
| `Alt+Q` | Chọn vùng | | `Alt+C` | Hiện/Ẩn ChatBox |
| `Alt+P` | Cài đặt | | `Alt+L` | Hiện/Ẩn Log |
| `Alt+B` | Hiện/Ẩn vùng đã chọn | | `Alt+H` | Xóa vùng đã chọn |

---

## ⚙️ Thiết lập đề xuất

### Cho sử dụng nhanh
- **OCR**: OneOCR hoặc Windows OCR (tích hợp sẵn, không cần thiết lập)
- **Dịch thuật**: Google Translate (không cần khóa API)

### Cho chất lượng tốt nhất
- **OCR**: PaddleOCR (tiếng Á Đông) hoặc RapidOCR (tiếng phương Tây) hoặc EasyOCR
- **Dịch thuật**: Gemini Flash lite 2.5 (Cần khóa API)
- **Phần cứng**: Khuyến nghị GPU NVIDIA

### Cho quyền riêng tư
- **OCR**: OneOCR hoặc Windows OCR
- **Dịch thuật**: Ollama hoặc LM Studio (100% cục bộ)

### Mẹo tăng hiệu suất
- Vùng chọn nhỏ hơn = xử lý nhanh hơn
- Thêm nhiều khóa API để dự phòng
- Tải ngôn ngữ lần đầu mất 1-2 phút (OCR bên ngoài)

---

## 💬 Cộng đồng

Tham gia [Discord](https://discord.gg/FusrDU5tdn) của chúng tôi để được hỗ trợ và cập nhật!

</div>

---

## 📄 License

BSD-style attribution - see [LICENSE.md](LICENSE.md)

**Acknowledgments**: Includes software developed by Seth A. Robinson - [UGTLive](https://github.com/SethRobinson/UGTLive)

<div align="center">

**Made with ❤️ for the gaming community**

[⭐ Star this project](https://github.com/thanhkeke97/RSTGameTranslation) if you find it helpful!

</div>

<script>
function switchLanguage(lang) {
  // Hide all language sections
  document.getElementById('english').style.display = 'none';
  document.getElementById('vietnamese').style.display = 'none';
  
  // Show the selected language
  document.getElementById(lang === 'en' ? 'english' : 'vietnamese').style.display = 'block';
  
  // Update button styles
  document.getElementById('en-btn').style.backgroundColor = lang === 'en' ? '#4CAF50' : '#808080';
  document.getElementById('vi-btn').style.backgroundColor = lang === 'vi' ? '#2196F3' : '#808080';
  
  // Save preference if possible
  try {
    localStorage.setItem('preferred_language', lang);
  } catch (e) {
    console.log('Could not save language preference');
  }
}

// Set initial language based on saved preference or default to English
document.addEventListener('DOMContentLoaded', function() {
  let lang = 'en';
  try {
    const saved = localStorage.getItem('preferred_language');
    if (saved === 'vi' || saved === 'en') {
      lang = saved;
    }
  } catch (e) {
    console.log('Could not retrieve language preference');
  }
  switchLanguage(lang);
});
</script>
