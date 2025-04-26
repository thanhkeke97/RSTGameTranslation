# Hướng dẫn tối ưu hóa tốc độ dịch trong UGTLive

## Cài đặt tối ưu cho tốc độ dịch nhanh

### 1. Tối ưu hóa OCR
- **Block Power**: Giảm xuống 3-4 (giá trị mặc định là 5)
- **Settle Time**: Giảm xuống 0.2-0.3 giây (giá trị mặc định là 0.5)
- **Min text fragment size**: Tăng lên 3-4 (giá trị mặc định là 2)
- **Min letter confidence**: Tăng lên 0.2-0.3 (giá trị mặc định là 0.1)
- **Min line confidence**: Tăng lên 0.3-0.4 (giá trị mặc định là 0.2)

### 2. Tối ưu hóa ngữ cảnh dịch
- **Max Previous Context**: Giảm xuống 1-2 (giá trị mặc định là 3)
- **Min Context Size**: Tăng lên 30-40 (giá trị mặc định là 20)
- **Thêm từ khóa cần bỏ qua**: Thêm các từ/cụm từ thường xuất hiện nhưng không cần dịch

### 3. Lựa chọn dịch vụ dịch thuật tốc độ cao
- **Ollama** với mô hình nhỏ:
  - Mô hình `phi3:mini` hoặc `llama3:8b` (nhanh hơn các mô hình lớn)
  - Đảm bảo Ollama chạy trên máy có GPU

- **Gemini**:
  - Sử dụng mô hình `gemini-1.5-flash` thay vì `gemini-1.5-pro`
  - Đảm bảo kết nối internet ổn định

### 4. Tối ưu hóa phần cứng
- Chạy UGTLive trên máy tính có GPU
- Đảm bảo đủ RAM (tối thiểu 8GB, khuyến nghị 16GB)
- Đóng các ứng dụng không cần thiết để giải phóng tài nguyên

## Các lưu ý quan trọng
1. Tốc độ dịch nhanh có thể làm giảm chất lượng dịch thuật
2. Nếu sử dụng Ollama, hãy đảm bảo mô hình đã được tải xuống trước
3. Tắt Text-to-Speech nếu không cần thiết để tăng tốc độ xử lý

## Cấu hình đề xuất cho tốc độ cao
```
OCR Method: Windows OCR (nhanh hơn EasyOCR)
Block Power: 3
Settle Time: 0.2
Max Previous Context: 1
Min Context Size: 40
Translation Service: Ollama với phi3:mini hoặc Gemini với gemini-1.5-flash
```