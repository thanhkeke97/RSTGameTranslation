<div align="center">

# 🎮 RSTGameTranslation
### Phần mềm Dịch Màn hình Game Thời gian thực

[![Phiên bản](https://img.shields.io/badge/version-3.3-blue.svg)](https://github.com/thanhkeke97/RSTGameTranslation/releases)
[![Giấy phép](https://img.shields.io/badge/license-BSD-green.svg)](LICENSE.md)
[![Nền tảng](https://img.shields.io/badge/platform-Windows%2010+-lightgrey.svg)]()

*Dịch game của bạn theo thời gian thực với công nghệ OCR và LLM*

[📥 Tải xuống](https://github.com/thanhkeke97/RSTGameTranslation/releases) • [📖 English Guide](README.md) • [🐛 Báo lỗi](https://github.com/thanhkeke97/RSTGameTranslation/issues)

</div>

---

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
1. **Cài đặt Conda**: (Chỉ cài đặt ở lần đầu tiên ứng dụng được mở trên thiết bị, không cần cài đặt lại)
   - Vào **Cài đặt** → **OCR**: Nhấn nút Setup Conda (Lưu ý đường dẫn đến thư mục chứa ứng dụng không được có khoảng trắng, nếu không việc cài đặt conda sẽ thất bại)
   - Đợi cho đến khi cài đặt conda thành công, đóng ứng dụng và mở lại.

3. **Tùy chọn OCR**: (Chỉ cài đặt ở lần đầu tiên OCR được chọn trên thiết bị, không cần cài đặt lại)
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

## 🔄 Cách cập nhật phiên bản

RSTGameTranslation sẽ tự động kiểm tra cập nhật khi bạn khởi động. Nếu có phiên bản mới, bạn sẽ thấy thông báo hỏi xem bạn có muốn tải xuống không. Để cập nhật:

1. Tải phiên bản mới nhất từ thông báo hoặc từ [Trang Releases](https://github.com/thanhkeke97/RSTGameTranslation/releases)
2. Đóng RSTGameTranslation nếu đang chạy
3. Giải nén các tệp mới vào thư mục cài đặt hiện tại
4. Xong! Các cài đặt và tùy chọn của bạn sẽ được giữ nguyên

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

---

## 📄 Giấy phép

Giấy phép kiểu BSD - xem chi tiết tại [LICENSE.md](LICENSE.md)

**Ghi nhận**: Bao gồm phần mềm được phát triển bởi Seth A. Robinson - [UGTLive](https://github.com/SethRobinson/UGTLive)

<div align="center">

**Được tạo ra với ❤️ dành cho cộng đồng game**

[⭐ Gắn sao cho dự án này](https://github.com/thanhkeke97/RSTGameTranslation) nếu bạn thấy nó hữu ích!

</div>
