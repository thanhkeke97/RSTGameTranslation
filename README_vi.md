<div align="center">

# 🎮 RSTGameTranslation
### Phần mềm Dịch Màn hình Game Thời gian thực

[![Phiên bản](https://img.shields.io/badge/version-6.1-blue.svg)](https://github.com/thanhkeke97/RSTGameTranslation/releases)
[![Giấy phép](https://img.shields.io/badge/license-BSD-green.svg)](LICENSE.md)
[![Nền tảng](https://img.shields.io/badge/platform-Windows%2010+-lightgrey.svg)]()


*Dịch game, truyện tranh và lời thoại trong game theo thời gian thực bằng OCR và AI*

[📥 Tải xuống](https://github.com/thanhkeke97/RSTGameTranslation/releases) • [⚙️ Hướng dẫn cài đặt (Tất cả cài đặt)](https://thanhkeke97.github.io/RSTGameTranslation/index_vi.html#settings) • [📖 English Guide](README.md) • [🐛 Báo lỗi](https://github.com/thanhkeke97/RSTGameTranslation/issues)

</div>

---

## ✨ Tính năng

- **Dịch mọi game theo thời gian thực**: đọc chữ trên màn hình (OneOCR hoặc Windows OCR, có sẵn) và hiện bản dịch ngay trên đó
- **Nhiều dịch vụ dịch**: Google Dịch (miễn phí, không cần key), Gemini, Groq, ChatGPT, Mistral, hoặc dịch hoàn toàn offline với Ollama và LM Studio
- **Lớp phủ tự nhiên**: chữ gốc được xóa đi, bản dịch được vẽ lên nền gốc với màu chữ phù hợp
- **Chế độ Manga**: nhận diện từng bóng thoại, đọc chữ dọc tiếng Nhật từ phải sang trái, dùng được cả với truyện tiếng Anh
- **Dịch lời thoại trong game**: chuyển giọng nói thành chữ rồi dịch (Whisper hoặc FunASR, chạy trên máy bạn)
- **Đọc bản dịch thành tiếng** (Text-to-Speech): ElevenLabs, Google Cloud TTS, Windows TTS, hoặc Supertonic (miễn phí, offline)
- **Dịch thông minh**: dùng các câu trước đó và thông tin về game để dịch chính xác hơn
- **Dịch Clipboard**: tự động dịch bất kỳ đoạn chữ nào bạn sao chép

![Preview](media/preview_video.gif)

---

## 🚀 Bắt đầu nhanh (khoảng 3 phút)

**Yêu cầu:** Windows 10 hoặc 11. Chạy game ở chế độ **cửa sổ** hoặc **cửa sổ không viền (borderless)**; chế độ toàn màn hình độc quyền (exclusive fullscreen) sẽ che mất bản dịch.

1. **Tải về** bản mới nhất ở [Releases](https://github.com/thanhkeke97/RSTGameTranslation/releases), giải nén ra thư mục bất kỳ và chạy `rst.exe`
2. **Chọn ngôn ngữ**: **Cài đặt → Ngôn ngữ**, chọn ngôn ngữ của game và ngôn ngữ của bạn
3. **Chọn dịch vụ dịch**: **Cài đặt → Dịch thuật**, chọn **Google Translate** (miễn phí, không cần API key)
4. **Chọn chỗ cần dịch**:
   - Nhấn **Chọn ứng dụng** và chọn cửa sổ game, **hoặc**
   - Nhấn **Alt+Q** rồi kéo chuột khoanh vùng có chữ
5. Nhấn **Alt+F** để bật **Lớp phủ**, rồi **Alt+G** để **Bắt đầu**

Xong. Bản dịch hiện đè lên game và tự cập nhật khi chữ thay đổi. Nhấn **Alt+G** lần nữa để dừng.

> 💡 Trên **Windows 10**, hãy nhấn **Chọn ứng dụng** trước khi bật lớp phủ.

---

## ⌨️ Phím tắt

| Phím tắt | Chức năng |
|---|---|
| **Alt+G** | Bắt đầu / dừng dịch |
| **Alt+Q** | Chọn vùng cần dịch |
| **Alt+F** | Bật / tắt lớp phủ |
| **Alt+C** | Bật / tắt Hộp thoại (cửa sổ lịch sử bản dịch) |
| **Alt+T** | Dịch lại đoạn chữ hiện tại |
| **Alt+K** | Bật / tắt dịch âm thanh |
| **Alt+B** | Hiện các vùng đã chọn |
| **Alt+P** | Mở Cài đặt |
| **Alt+L** | Mở cửa sổ log |

Bạn có thể đổi mọi phím tắt trong **Cài đặt → Phím tắt**.

---

## 📖 Hướng dẫn

### 🎮 Dịch game

- **Chọn ứng dụng** bám theo cửa sổ game, kể cả khi bạn di chuyển cửa sổ. **Chọn vùng (Alt+Q)** dịch một phần cố định của màn hình. Bật **Chọn nhiều vùng** để thêm tối đa 5 vùng; tất cả các vùng được dịch cùng lúc.
- **Lớp phủ** hiện bản dịch đè lên chữ gốc. **Hộp thoại (Alt+C)** hiện bản dịch dạng danh sách, dễ theo dõi hơn với visual novel hoặc khi vùng chữ nhỏ.
- Bật **Cài đặt → Ngữ cảnh/Lớp phủ → Tự động đặt màu nền lớp phủ** để xóa chữ gốc và cho bản dịch hòa vào game.
- Mô tả game và các nhân vật chính ở **Cài đặt → Ngữ cảnh/Lớp phủ → Thông tin về game** để dịch chính xác hơn khi dùng dịch vụ AI. Lưu cài đặt riêng cho từng game bằng **Cài đặt → Hồ sơ Game**.
- Nếu một số chữ trên màn hình (menu, HUD) cứ bị dịch, hãy đánh dấu chúng trong **Cài đặt → Vùng loại trừ** hoặc **Cụm từ bỏ qua**.

### 📚 Đọc manga và truyện tranh

1. **Cài đặt → OCR/Chọn màn hình**: chọn **OneOCR**, rồi bật **Manga Mode**
2. **Cài đặt → Ngôn ngữ**: chọn ngôn ngữ gốc của truyện (tiếng Nhật, tiếng Anh, …)
3. Nhấn **Alt+Q** và khoanh cả trang truyện (hoặc chọn cửa sổ trình đọc bằng **Chọn ứng dụng**)
4. **Alt+F** để bật lớp phủ, **Alt+G** để bắt đầu

Chế độ Manga tìm từng bóng thoại trong ảnh, nên chữ ở các bóng thoại cạnh nhau không bị trộn lẫn. Với tiếng Nhật, chữ dọc được đọc theo cột từ phải sang trái và furigana được bỏ qua. Cả trang được dịch một lần, nhưng mỗi bóng thoại vẫn có bản dịch riêng.

### 🎙️ Dịch lời thoại trong game (giọng nói thành chữ)

1. **Cài đặt → Âm thanh**: chọn **Nhà cung cấp**
   - **Whisper**: nhiều ngôn ngữ. Nhấn **Tải model Whisper...** và chọn một model (hộp thoại có đánh dấu model khuyên dùng)
   - **FunASR**: tiếng Nhật, Trung, Hàn, Anh, Quảng Đông. Nhanh và chính xác. Nhấn **Tải model FunASR**
2. Khuyên dùng trong mục **Thu âm & phát hiện giọng nói**: **Nguồn thu** = *Toàn bộ âm thanh trừ ứng dụng này* và **Phát hiện giọng nói** = *Silero VAD*
3. Nhấn **Alt+K** (hoặc nút **Dịch âm thanh**) để bắt đầu. Bản dịch hiện trong Hộp thoại

### 🔊 Đọc bản dịch thành tiếng (Text-to-Speech)

Chọn dịch vụ trong **Cài đặt → TTS**:

| Dịch vụ | Chi phí | Internet | Ngôn ngữ | Phù hợp cho |
|---|---|---|---|---|
| **Supertonic** | Miễn phí | Không cần | 31 | Offline, không cần API key |
| **Windows TTS** | Miễn phí | Không cần | Giọng đã cài trong Windows | Không cần thiết lập |
| **ElevenLabs** | Có gói miễn phí + trả phí | Cần | Nhiều | Giọng tự nhiên nhất |
| **Google Cloud TTS** | Trả theo số ký tự | Cần | 50+ | Nhiều ngôn ngữ nhất |

Khi bản dịch đến nhanh hơn tốc độ đọc, **Tăng tốc khi đọc chậm** sẽ đọc các câu tiếp theo nhanh hơn (tối đa 1.5 lần) để bắt kịp game.

<details>
<summary><b>Thiết lập Supertonic (miễn phí, offline)</b></summary>

[Supertonic](https://github.com/supertone-inc/supertonic) của [Supertone](https://www.supertone.ai/) chạy hoàn toàn trên CPU: không cần cloud, không cần API key. Sau khi tải model một lần (khoảng 400 MB) là dùng được offline.

1. **Cài đặt → TTS**, đặt **Dịch vụ TTS** là `Supertonic`
2. Nhấn **Download model** (tải từ Hugging Face)
3. Chọn giọng đọc (M1–M5, F1–F5)
4. Tùy chọn: chỉnh **Quality** (mặc định 8) và **Speech speed** (mặc định 1.05)

**Ngôn ngữ:** `en, ko, ja, ar, bg, cs, da, de, el, es, et, fi, fr, hi, hr, hu, id, it, lt, lv, nl, pl, pt, ro, ru, sk, sl, sv, tr, uk, vi` và `na` (tự nhận diện).

Câu đầu tiên sau khi mở ứng dụng có thể mất 1–2 giây để nạp model. Giấy phép: SDK MIT, model OpenRAIL-M ([file model](https://huggingface.co/Supertone/supertonic-3)).

</details>

### 📋 Dịch chữ đã sao chép

Bật **Cài đặt → Clipboard → Bật dịch Clipboard**. Mọi đoạn chữ bạn sao chép (Ctrl+C) sẽ được dịch tự động.

---

## ⚙️ Thiết lập đề xuất

| Mục đích | OCR | Dịch |
|---|---|---|
| **Dùng nhanh, miễn phí** | OneOCR | Google Translate (không cần key) |
| **Chất lượng tốt nhất** | OneOCR | Gemini, model `gemini-flash-lite-latest` (API key miễn phí từ Google AI Studio) |
| **Hoàn toàn offline / riêng tư** | OneOCR hoặc Windows OCR | Ollama hoặc LM Studio |

**Mẹo để dịch nhanh hơn:** chỉ khoanh vùng có chữ thay vì cả màn hình, và thêm nhiều API key để khi một key hết lượt thì tự chuyển sang key khác.

---

## ❓ Xử lý sự cố

<details>
<summary><b>Bản dịch không hiện lên trên game</b></summary>

- Chuyển game sang chế độ **cửa sổ** hoặc **cửa sổ không viền (borderless)**. Chế độ toàn màn hình độc quyền sẽ vẽ đè lên mọi thứ.
- Kiểm tra lớp phủ đã bật chưa (**Alt+F**).
- Trên Windows 10, nhấn **Chọn ứng dụng** và chọn game trước.

</details>

<details>
<summary><b>Google Dịch ngừng dịch (lỗi "429" / bị giới hạn)</b></summary>

Google Dịch miễn phí giới hạn số lần gửi yêu cầu từ một máy. Ứng dụng sẽ tự chờ và trong lúc đó dùng dịch vụ được chọn ở **Cài đặt → Dịch thuật → Khi bị giới hạn**. Nếu bị thường xuyên, hãy chuyển sang Gemini (có API key miễn phí) hoặc model chạy trên máy (Ollama, LM Studio).

</details>

<details>
<summary><b>Chữ ở các bóng thoại manga bị trộn vào nhau</b></summary>

Bật **Manga Mode** (cần dùng **OneOCR**) và chọn đúng ngôn ngữ gốc. Nhận diện bóng thoại hoạt động tốt nhất với bóng thoại nền trắng hoặc nền sáng.

</details>

<details>
<summary><b>Dịch chậm</b></summary>

- Khoanh vùng nhỏ hơn, chỉ chứa phần có chữ.
- Dịch vụ AI online (Gemini, Groq) thường nhanh hơn model chạy trên máy yếu.
- Với dịch âm thanh, giữ bật **Chế độ nhanh cho câu ngắn** và thử model Whisper nhỏ hơn hoặc FunASR.

</details>

Vẫn chưa được? Hỏi trên [Discord](https://discord.gg/FusrDU5tdn) hoặc [báo lỗi](https://github.com/thanhkeke97/RSTGameTranslation/issues).

---

## 💬 Cộng đồng

Tham gia [Discord](https://discord.gg/FusrDU5tdn) của chúng tôi để được hỗ trợ và cập nhật!

---

## 💖 Ủng hộ tác giả

Nếu bạn thấy RST hữu ích và muốn hỗ trợ phát triển, bạn có thể mua một ly cà phê cho tác giả — cảm ơn bạn! 👋

<div align="center">

[![Ủng hộ tác giả (Buy Me a Coffee)](https://img.shields.io/badge/Ủng%20hộ-%23FFDD00?style=for-the-badge&logo=buymeacoffee&logoColor=black)](https://www.buymeacoffee.com/thanhkeke97)

</div>

---

## 🛠️ Công nghệ Mã nguồn mở

Dự án này được xây dựng dựa trên sự đóng góp của cộng đồng mã nguồn mở. Chúng tôi xin gửi lời cảm ơn chân thành đến các dự án sau:

### Core & UI
- **[WPF (Windows Presentation Foundation)](https://github.com/dotnet/wpf)** - Framework giao diện người dùng

### AI & Dịch thuật
- **[System.Text.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-overview)** - Tuần tự hóa JSON hiệu suất cao
- **[Hugging Face](https://huggingface.co/)** - Nền tảng cho các mô hình AI và bộ dữ liệu

### Âm thanh
- **[NAudio](https://github.com/naudio/NAudio)** - Thư viện âm thanh và MIDI cho .NET
- **[System.Speech](https://learn.microsoft.com/en-us/dotnet/api/system.speech.synthesis.speechsynthesizer)** - Thư viện tổng hợp giọng nói .NET
- **[Whisper.Net](https://github.com/sandrohanea/whisper.net)** - Thư viện chuyển đổi giọng nói sang văn bản .NET

### Khác
- **[Python](https://www.python.org/)** - Ngôn ngữ kịch bản backend
- **[PyTorch](https://pytorch.org/)** - Framework học máy

---

## 📄 Giấy phép

Giấy phép kiểu BSD - xem chi tiết tại [LICENSE.md](LICENSE.md)

**Ghi nhận**: Bao gồm phần mềm được phát triển bởi Seth A. Robinson - [UGTLive](https://github.com/SethRobinson/UGTLive)

| <img src="https://signpath.org/assets/favicon.png" width="25" height="25" align="center"> | Chữ ký số được cung cấp miễn phí bởi [SignPath.io](https://about.signpath.io/), chứng nhận bởi [SignPath Foundation](https://signpath.org/). |
| :--- | :--- |

<div align="center">

**Được tạo ra với ❤️ dành cho cộng đồng game**

[⭐ Gắn sao cho dự án này](https://github.com/thanhkeke97/RSTGameTranslation) nếu bạn thấy nó hữu ích!

</div>
