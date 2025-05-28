using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace RSTGameTranslation
{
    public class OcrServerManager
    {
        private static OcrServerManager? _instance;
        private Process? _currentServerProcess;
        public bool serverStarted = false;
        
        // Singleton pattern
        public static OcrServerManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new OcrServerManager();
                }
                return _instance;
            }
        }
        
        private OcrServerManager()
        {
            // Private constructor for singleton
        }
        
        /// <summary>
        /// Khởi động OCR server dựa trên phương thức OCR được chọn
        /// </summary>
        /// <param name="ocrMethod">Phương thức OCR ("EasyOCR" hoặc "PaddleOCR")</param>
        /// <returns>Kết quả khởi động (thành công hay không)</returns>
        public async Task<bool> StartOcrServerAsync(string ocrMethod)
        {
            string flagFile = "";
            try
            {
                // Đóng server hiện tại nếu đang chạy
                StopOcrServer();

                // Xác định đường dẫn đến thư mục webserver
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string webserverPath = Path.Combine(baseDirectory, "webserver");

                // Chọn batch file tương ứng với phương thức OCR
                string batchFileName;
                string workingDirectory;

                if (ocrMethod == "EasyOCR")
                {
                    batchFileName = "RunServerEasyOCR.bat";
                    workingDirectory = Path.Combine(webserverPath, "EasyOCR");
                    flagFile = Path.Combine(Path.GetTempPath(), "easyocr_ready.txt");
                    try
                    {
                        File.Delete(flagFile);
                        Console.WriteLine("Delete temp file success");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Delete temp file fail {e.Message}");
                    }
                }
                else if (ocrMethod == "PaddleOCR")
                {
                    batchFileName = "RunServerPaddleOCR.bat";
                    workingDirectory = Path.Combine(webserverPath, "PaddleOCR");
                    flagFile = Path.Combine(Path.GetTempPath(), "paddleocr_ready.txt");
                    try
                    {
                        File.Delete(flagFile);
                        Console.WriteLine("Delete temp file success");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Delete temp file fail {e.Message}");
                    }


                }
                else
                {
                    Console.WriteLine($"OCR method not supported: {ocrMethod}");
                    return false;
                }

                // Kiểm tra xem batch file có tồn tại không
                string batchFilePath = Path.Combine(workingDirectory, batchFileName);
                if (!File.Exists(batchFilePath))
                {
                    Console.WriteLine($"File not found: {batchFilePath}");
                    return false;
                }

                // Khởi tạo process để chạy batch file
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {batchFileName}",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = false
                };


                // Khởi động process
                _currentServerProcess = Process.Start(startInfo);
                // Chờ flag file
                Console.WriteLine("⏳ Waiting for ready flag...");
                for (int i = 0; i < 60; i++) // 1 phút
                {
                    if (File.Exists(flagFile))
                    {
                        Console.WriteLine("✅ PaddleOCR READY!");
                        serverStarted = true;
                        break;
                    }

                    await Task.Delay(1000);

                    if (i % 1 == 0)
                        Console.WriteLine($"Still waiting... {i}s");
                }

                if (serverStarted == false)
                {
                    Console.WriteLine("Cannot start OCR server");
                    return false;
                }


                Console.WriteLine($"{ocrMethod} server has been started");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting OCR server: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Dừng OCR server đang chạy (nếu có)
        /// </summary>
        public void StopOcrServer()
        {
            try
            {
                if (_currentServerProcess != null && !_currentServerProcess.HasExited)
                {
                    MainWindow.Instance.UpdateServerButtonStatus(OcrServerManager.Instance.serverStarted);
                    // Lấy tất cả các tiến trình con của cmd.exe
                    int processId = _currentServerProcess.Id;
                    // Thử đóng process một cách lịch sự trước
                    _currentServerProcess.CloseMainWindow();
                    
                    // Đợi một chút để process có thể đóng
                    if (!_currentServerProcess.WaitForExit(1000))
                    {
                        // Nếu không đóng được, buộc đóng
                        _currentServerProcess.Kill();
                    }
                    KillRelatedPythonProcesses();
                    
                    _currentServerProcess = null;
                    Console.WriteLine("OCR server has been stopped");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping OCR server: {ex.Message}");
            }
        }
        
        // Thêm phương thức mới để tìm và đóng các tiến trình Python liên quan
        private void KillRelatedPythonProcesses()
        {
            try
            {
                // Tìm tất cả các tiến trình Python đang chạy
                foreach (var process in Process.GetProcessesByName("python"))
                {
                    try
                    {
                        // Kiểm tra xem tiến trình này có liên quan đến OCR server không
                        // Bạn có thể kiểm tra thông qua command line arguments hoặc tên module
                        string? commandLine = GetCommandLine(process.Id);
                        
                        if (commandLine != null && 
                            (commandLine.Contains("server_paddle.py") || 
                            commandLine.Contains("server_easyocr.py") ||
                            commandLine.Contains("process_image_paddleocr.py") ||
                            commandLine.Contains("process_image_easyocr.py")))
                        {
                            Console.WriteLine($"Terminate related Python processes: {process.Id} - {commandLine}");
                            process.Kill();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Unable to terminate Python process {process.Id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding and closing Python processes: {ex.Message}");
            }
        }

        // Phương thức để lấy command line của một tiến trình
        private string? GetCommandLine(int processId)
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}"))
                {
                    using (var objects = searcher.Get())
                    {
                        foreach (var obj in objects)
                        {
                            return obj["CommandLine"]?.ToString();
                        }
                    }
                }
            }
            catch
            {
                // Bỏ qua lỗi nếu không thể lấy command line
            }
            
            return null;
        }
        
        /// <summary>
        /// Kiểm tra xem server có đang chạy không
        /// </summary>
        // public bool IsServerRunning()
        // {
        //     return _currentServerProcess != null && !_currentServerProcess.HasExited;
        // }

        /// <summary>
        /// Chạy batch file cài đặt môi trường Conda cho OCR server
        /// </summary>
        /// <param name="ocrMethod">Phương thức OCR ("EasyOCR" hoặc "PaddleOCR")</param>
        /// <returns>Kết quả cài đặt (thành công hay không)</returns>
        public bool SetupOcrEnvironment(string ocrMethod)
        {
            try
            {
                // Xác định đường dẫn đến thư mục webserver
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string webserverPath = Path.Combine(baseDirectory, "webserver");

                // Chọn batch file cài đặt tương ứng với phương thức OCR
                string setupBatchFileName;
                string workingDirectory;

                if (ocrMethod == "EasyOCR")
                {
                    setupBatchFileName = "SetupServerCondaEnvNVidiaEasyOCR.bat";
                    workingDirectory = Path.Combine(webserverPath, "EasyOCR");
                }
                else if (ocrMethod == "PaddleOCR")
                {
                    setupBatchFileName = "SetupServerCondaEnvNVidiaPaddleOCR.bat";
                    workingDirectory = Path.Combine(webserverPath, "PaddleOCR");
                }
                else
                {
                    Console.WriteLine($"Không hỗ trợ phương thức OCR: {ocrMethod}");
                    return false;
                }

                // Kiểm tra xem batch file có tồn tại không
                string setupBatchFilePath = Path.Combine(workingDirectory, setupBatchFileName);
                if (!File.Exists(setupBatchFilePath))
                {
                    Console.WriteLine($"Không tìm thấy file cài đặt: {setupBatchFilePath}");
                    return false;
                }

                // Khởi tạo process để chạy batch file cài đặt
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {setupBatchFileName}",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                // Khởi động process cài đặt
                using (Process? setupProcess = Process.Start(startInfo))
                {
                    if (setupProcess == null)
                    {
                        Console.WriteLine("Không thể khởi động quá trình cài đặt OCR server");
                        return false;
                    }

                    // Đợi quá trình cài đặt hoàn tất
                    setupProcess.WaitForExit();

                    Console.WriteLine($"Quá trình cài đặt {ocrMethod} server đã hoàn tất");
                    return setupProcess.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi khi cài đặt OCR server: {ex.Message}");
                return false;
            }
        }
    }
}