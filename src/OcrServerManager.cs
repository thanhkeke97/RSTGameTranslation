using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace RSTGameTranslation
{
    public class OcrServerManager
    {
        private static OcrServerManager? _instance;
        private Process? _currentServerProcess;
        public bool serverStarted = false;

        public bool timeoutStartServer = false;
        
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
        /// Start OCR server
        /// </summary>
        public async Task<bool> StartOcrServerAsync(string ocrMethod)
        {
            try
            {
                // Stop the current OCR server if it's running
                StopOcrServer();
                serverStarted = false;
                timeoutStartServer = false;

                // Get the base directory of the application
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string webserverPath = Path.Combine(baseDirectory, "webserver");

                // Choose server script and virtual environment based on OCR method
                string serverScriptName;
                string venvFolderName;
                string workingDirectory;
                int targetPort;

                if (ocrMethod == "EasyOCR")
                {
                    serverScriptName = "server_easy.py";
                    venvFolderName = "ocrstuffeasyocr";
                    workingDirectory = Path.Combine(webserverPath, "EasyOCR");
                    targetPort = SocketManager.Instance.get_EasyOcrPort();
                }
                else if (ocrMethod == "PaddleOCR")
                {
                    serverScriptName = "server_paddle.py";
                    venvFolderName = "ocrstuffpaddleocr";
                    workingDirectory = Path.Combine(webserverPath, "PaddleOCR");
                    targetPort = SocketManager.Instance.get_PaddleOcrPort();


                }
                else if (ocrMethod == "RapidOCR")
                {
                    serverScriptName = "server_rapid.py";
                    venvFolderName = "ocrstuffrapidocr";
                    workingDirectory = Path.Combine(webserverPath, "RapidOCR");
                    targetPort = SocketManager.Instance.get_RapidOcrPort();


                }
                else
                {
                    Console.WriteLine($"OCR method not supported: {ocrMethod}");
                    return false;
                }

                // Preflight check 1: working directory exists
                if (!Directory.Exists(workingDirectory))
                {
                    Console.WriteLine($"Working directory not found: {workingDirectory}");
                    return false;
                }

                // Preflight check 2: Python executable in venv exists
                string pythonExecutablePath = Path.Combine(workingDirectory, venvFolderName, "Scripts", "python.exe");
                if (!File.Exists(pythonExecutablePath))
                {
                    Console.WriteLine($"Python executable not found: {pythonExecutablePath}");
                    return false;
                }

                // Preflight check 3: server script exists
                string serverScriptPath = Path.Combine(workingDirectory, serverScriptName);
                if (!File.Exists(serverScriptPath))
                {
                    Console.WriteLine($"Server script not found: {serverScriptPath}");
                    return false;
                }

                // Preflight check 4: target port should be free before start
                if (IsPortInUse(targetPort))
                {
                    Console.WriteLine($"Port {targetPort} is already in use. Cannot start {ocrMethod} server.");
                    return false;
                }

                string startupLogPath = Path.Combine(workingDirectory, "server_startup.log");
                var logWriter = new StreamWriter(new FileStream(startupLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    AutoFlush = true
                };
                object logLock = new object();
                void WriteStartupLog(string level, string message)
                {
                    string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
                    lock (logLock)
                    {
                        logWriter.WriteLine(line);
                    }
                }

                WriteStartupLog("INFO", "====================================================");
                WriteStartupLog("INFO", $"Starting OCR server: method={ocrMethod}, port={targetPort}");
                WriteStartupLog("INFO", $"Python executable: {pythonExecutablePath}");
                WriteStartupLog("INFO", $"Server script: {serverScriptPath}");

                // Initialize process start info
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = pythonExecutablePath,
                    Arguments = $"\"{serverScriptPath}\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };


                // Starting process
                _currentServerProcess = Process.Start(startInfo);
                if (_currentServerProcess == null)
                {
                    WriteStartupLog("ERROR", $"Unable to start OCR server process for {ocrMethod}");
                    lock (logLock)
                    {
                        logWriter.Dispose();
                    }
                    Console.WriteLine($"Unable to start OCR server process for {ocrMethod}");
                    return false;
                }

                Process processRef = _currentServerProcess;

                processRef.EnableRaisingEvents = true;
                processRef.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        WriteStartupLog("STDOUT", e.Data);
                    }
                };
                processRef.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        WriteStartupLog("STDERR", e.Data);
                    }
                };
                processRef.Exited += (_, _) =>
                {
                    WriteStartupLog("INFO", $"OCR process exited with code {processRef.ExitCode}");
                    lock (logLock)
                    {
                        logWriter.Dispose();
                    }
                };

                processRef.BeginOutputReadLine();
                processRef.BeginErrorReadLine();

                Console.WriteLine($"⏳ Waiting for {ocrMethod} server on port {targetPort}...");
                for (int i = 0; i < 90; i++) // 1 minute 30 seconds
                {
                    if (_currentServerProcess.HasExited)
                    {
                        WriteStartupLog("ERROR", $"Server exited early with code {_currentServerProcess.ExitCode}");
                        Console.WriteLine($"{ocrMethod} server process exited early with code {_currentServerProcess.ExitCode}");
                        return false;
                    }

                    if (await IsPortOpenAsync("127.0.0.1", targetPort, 1000))
                    {
                        Console.WriteLine($"✅ {ocrMethod} READY!");
                        serverStarted = true;
                        break;
                    }

                    await Task.Delay(1000);
                    Console.WriteLine($"Still waiting... {i + 1}s");
                }

                if (serverStarted == false)
                {
                    WriteStartupLog("ERROR", $"Cannot start {ocrMethod} OCR server (timeout)");
                    Console.WriteLine($"Cannot start {ocrMethod} OCR server (timeout)");
                    timeoutStartServer = true;
                    return false;
                }


                WriteStartupLog("INFO", $"{ocrMethod} server has been started successfully");
                Console.WriteLine($"{ocrMethod} server has been started");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting OCR server: {ex.Message}");
                return false;
            }
        }

        private static bool IsPortInUse(int port)
        {
            try
            {
                IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
                return properties.GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);
            }
            catch
            {
                // Be conservative: if check fails, treat as available and let startup logic decide.
                return false;
            }
        }

        private static async Task<bool> IsPortOpenAsync(string host, int port, int timeoutMs)
        {
            using TcpClient tcpClient = new TcpClient();
            try
            {
                Task connectTask = tcpClient.ConnectAsync(host, port);
                Task completedTask = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
                if (completedTask != connectTask)
                {
                    return false;
                }

                // Ensure exceptions from ConnectAsync are observed
                await connectTask;
                return tcpClient.Connected;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Stop the OCR server if it's running
        /// </summary>
        public void StopOcrServer()
        {
            try
            {
                if (_currentServerProcess != null && !_currentServerProcess.HasExited)
                {
                    KillProcessesByPort(SocketManager.Instance.get_EasyOcrPort());
                    KillProcessesByPort(SocketManager.Instance.get_PaddleOcrPort());
                    KillProcessesByPort(SocketManager.Instance.get_RapidOcrPort());
                    MainWindow.Instance.UpdateServerButtonStatus(OcrServerManager.Instance.serverStarted);
                    // Get the process ID of the current server process
                    int processId = _currentServerProcess.Id;
                    // Try to close the process gracefully
                    _currentServerProcess.CloseMainWindow();

                    // Wait for the process to exit gracefully for a short period of time
                    if (!_currentServerProcess.WaitForExit(1000))
                    {
                        // Failed to close gracefully, so kill the process forcefully
                        _currentServerProcess.Kill();
                    }

                    _currentServerProcess = null;
                    Console.WriteLine("OCR server has been stopped");
                    serverStarted = false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping OCR server: {ex.Message}");
            }
        }
        

        public void KillProcessesByPort(int port)
        {
            try
            {
                Console.WriteLine($"Looking for processes using port {port}...");
                

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c netstat -ano | findstr LISTENING | findstr :{port}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (Process? process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        Console.WriteLine("Failed to start netstat command");
                        return;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    if (string.IsNullOrEmpty(output))
                    {
                        Console.WriteLine($"No processes found using port {port}");
                        return;
                    }

                    // Find PIDs from netstat
                    foreach (string line in output.Split('\n'))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        

                        string[] parts = line.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 4)
                        {
                            if (int.TryParse(parts[parts.Length - 1], out int pid))
                            {
                                try
                                {
                                    Process processToKill = Process.GetProcessById(pid);
                                    Console.WriteLine($"Killing process {pid} using port {port}");
                                    processToKill.Kill();
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Failed to kill process {pid}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error killing processes by port: {ex.Message}");
            }
        }

        public bool InstallConda()
        {
            try
            {
                // Get the base directory of the application
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string webserverPath = Path.Combine(baseDirectory, "webserver");

                // Choose the appropriate batch file and working directory based on the OCR method
                string setupBatchFileName;
                string workingDirectory;

                
                setupBatchFileName = "CondaInstall.bat";
                workingDirectory = webserverPath;
                

                // Check if batch file exists
                string setupBatchFilePath = Path.Combine(workingDirectory, setupBatchFileName);
                if (!File.Exists(setupBatchFilePath))
                {
                    Console.WriteLine($"File installation not found: {setupBatchFilePath}");
                    return false;
                }

                // Initialize process start info
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {setupBatchFileName}",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                // Start the process
                using (Process? setupProcess = Process.Start(startInfo))
                {
                    if (setupProcess == null)
                    {
                        Console.WriteLine("Unable to install conda");
                        return false;
                    }

                    // Wait for the process to finish
                    setupProcess.WaitForExit();

                    Console.WriteLine($"The conda installation process has been completed");
                    App.ShutdownApplication();
                    return setupProcess.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error when installing conda: {ex.Message}");
                return false;
            }
        }

        

        /// <summary>
        /// Run bat file setup environment for OCR
        /// </summary>
        /// <param name="ocrMethod">OCR method ("EasyOCR" or "PaddleOCR")</param>
        public bool SetupOcrEnvironment(string ocrMethod)
        {
            try
            {
                // Get the base directory of the application
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string webserverPath = Path.Combine(baseDirectory, "webserver");

                // Choose the appropriate batch file and working directory based on the OCR method
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
                else if (ocrMethod == "RapidOCR")
                {
                    setupBatchFileName = "SetupServerCondaEnvNVidiaRapidOCR.bat";
                    workingDirectory = Path.Combine(webserverPath, "RapidOCR");
                }
                else
                {
                    Console.WriteLine($"This OCR method is not supported: {ocrMethod}");
                    return false;
                }

                // Check if batch file exists
                string setupBatchFilePath = Path.Combine(workingDirectory, setupBatchFileName);
                if (!File.Exists(setupBatchFilePath))
                {
                    Console.WriteLine($"File installation not found: {setupBatchFilePath}");
                    return false;
                }

                // Initialize process start info
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {setupBatchFileName}",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                // Start the process
                using (Process? setupProcess = Process.Start(startInfo))
                {
                    if (setupProcess == null)
                    {
                        Console.WriteLine("Unable to start the OCR server installation process");
                        return false;
                    }

                    // Wait for the process to finish
                    setupProcess.WaitForExit();

                    Console.WriteLine($"The {ocrMethod} server installation process has been completed");
                    return setupProcess.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error when installing OCR server: {ex.Message}");
                return false;
            }
        }
    }
}