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
            // Third-party Python OCR engines (EasyOCR, PaddleOCR, RapidOCR) have been removed.
            // Only built-in OCR (OneOCR, Windows OCR) is supported, which doesn't need a server.
            Console.WriteLine($"StartOcrServerAsync: '{ocrMethod}' is not a server-based OCR (built-in only).");
            return false;
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
                    // KillProcessesByPort calls for EasyOCR/PaddleOCR/RapidOCR ports removed below
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
        /// <param name="ocrMethod">OCR method</param>
        public bool SetupOcrEnvironment(string ocrMethod)
        {
            // Third-party Python OCR engines (EasyOCR, PaddleOCR, RapidOCR) have been removed.
            // Only built-in OCR (OneOCR, Windows OCR) is supported, which doesn't need setup.
            Console.WriteLine($"SetupOcrEnvironment: '{ocrMethod}' is a built-in OCR, no setup required.");
            return false;
        }
    }
}