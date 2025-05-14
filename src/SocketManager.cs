using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UGTLive
{
    public class SocketManager
    {
        private static SocketManager? _instance;
        private Socket? _clientSocket;
        private int _port;
        private readonly string _host;
        public bool _isConnected;
        private bool _tryingToConnect = false;
        // Semaphore to prevent concurrent connect/disconnect operations
        private SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(1, 1);
        
        // Constants for OCR server ports
        public const int EASYOCR_PORT = 9999;
        public const int PADDLEOCR_PORT = 9998;
        
        // Events for data received and connection status changes
        public event EventHandler<string>? DataReceived;
        public event EventHandler<bool>? ConnectionChanged;
        
        // Singleton pattern
        public static SocketManager Instance 
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SocketManager();
                }
                return _instance;
            }
        }
        
        // Constructor
        private SocketManager()
        {
            _host = "localhost";
            // Lấy phương thức OCR từ ConfigManager
            string ocrMethod = ConfigManager.Instance.GetOcrMethod();
            Console.WriteLine($"SocketManager initializing with OCR method: {ocrMethod}");
            // Thiết lập cổng dựa trên phương thức OCR
            _port = ocrMethod switch
            {
                "PaddleOCR" => PADDLEOCR_PORT,
                "EasyOCR" => EASYOCR_PORT,
                _ => EASYOCR_PORT // Mặc định sử dụng cổng EasyOCR
            };
            Console.WriteLine($"SocketManager initialized with port: {_port} for {ocrMethod}");
            _isConnected = false;
        }

        // Method to set the port based on OCR method
        public void UpdatePortBasedOnOcrMethod(string ocrMethod)
        {
            int newPort = ocrMethod switch
            {
                "PaddleOCR" => PADDLEOCR_PORT,
                "EasyOCR" => EASYOCR_PORT,
                _ => EASYOCR_PORT // Default to EasyOCR port
            };

            if (_port != newPort)
            {
                Console.WriteLine($"Changing OCR server port from {_port} to {newPort} for {ocrMethod}");
                SetPort(newPort);
            }
        }

        // Method to set the port directly
        public void SetPort(int port)
        {
            if (_port != port)
            {
                // If we're changing the port and currently connected, disconnect first
                if (_isConnected)
                {
                    Disconnect();
                }
                _port = port;
                Console.WriteLine($"OCR server port set to: {_port}");
            }
        }

        
        public async Task<bool> SwitchOcrMethod(string ocrMethod)
        {
            // Lấy phương thức OCR hiện tại
            string currentOcrMethod = ConfigManager.Instance.GetOcrMethod();
            Console.WriteLine($"Switching OCR method from {currentOcrMethod} to {ocrMethod}");
            
            // Bước 1: Reset trạng thái OCR hiện tại
            Logic.Instance.ResetHash();
            Logic.Instance.ClearAllTextObjects();
            
            // Bước 2: Nếu không phải Windows OCR, cập nhật port và kết nối
            if (ocrMethod != "Windows OCR")
            {
                
                // Cập nhật port trước khi kết nối
                UpdatePortBasedOnOcrMethod(ocrMethod);
                
                // Ngắt kết nối hiện tại nếu đang kết nối
                bool wasConnected = _isConnected;
                if (wasConnected)
                {
                    Console.WriteLine("Disconnecting from current OCR server...");
                    Disconnect();
                    
                    // Xử lý đặc biệt khi chuyển từ EasyOCR sang PaddleOCR
                    if (ocrMethod == "PaddleOCR")
                    {
                        Console.WriteLine("Special case: EasyOCR to PaddleOCR switch detected - performing complete reconnect cycle");
                        
                        // Đóng hoàn toàn socket và đảm bảo tài nguyên được giải phóng
                        _clientSocket?.Close();
                        _clientSocket?.Dispose();
                        _clientSocket = null;
                        
                        // Đảm bảo socket được giải phóng hoàn toàn trước khi kết nối lại
                        await Task.Delay(3000);  // Đợi 3 giây
                        Console.WriteLine("Socket resources released, proceeding with fresh connection");
                        
                        // Khởi tạo mới hoàn toàn socket thay vì sử dụng lại
                        _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        
                        try
                        {
                            // Kết nối với timeout
                            Console.WriteLine($"Establishing fresh connection to PaddleOCR on port {_port}...");
                            var connectTask = _clientSocket.ConnectAsync(IPAddress.Parse("127.0.0.1"), _port);
                            
                            // Thêm timeout cho kết nối
                            if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
                            {
                                Console.WriteLine("Connection attempt timed out after 5 seconds");
                                Disconnect();
                                return false;
                            }
                            
                            // Kiểm tra kết nối thành công
                            if (!_clientSocket.Connected)
                            {
                                Console.WriteLine("Socket connected state is false after connection attempt");
                                Disconnect();
                                return false;
                            }
                            
                            // Kết nối thành công
                            _isConnected = true;
                            ConnectionChanged?.Invoke(this, true);
                            
                            // Khởi động thread lắng nghe
                            _ = StartListeningAsync();
                            
                            Console.WriteLine("Successfully connected to PaddleOCR with fresh connection");
                            
                            // Nếu đang ở chế độ Started, kích hoạt OCR check
                            if (MainWindow.Instance.GetIsStarted())
                            {
                                Console.WriteLine("OCR process active, triggering new OCR check");
                                MainWindow.Instance.SetOCRCheckIsWanted(true);
                            }
                            
                            return true;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error during fresh connection attempt: {ex.Message}");
                            Disconnect();
                            return false;
                        }
                    }
                }
                
                // Cho các trường hợp không phải chuyển từ EasyOCR sang PaddleOCR
                if (ocrMethod != "PaddleOCR")
                {
                    // Kết nối lại với port mới
                    Console.WriteLine($"Connecting to {ocrMethod} server on port {_port}...");
                    bool connected = await TryReconnectAsync();
                    
                    // Thông báo kết quả
                    if (connected)
                    {
                        Console.WriteLine($"Successfully connected to {ocrMethod} server");
                        
                        // Nếu đang ở chế độ Started, kích hoạt OCR check
                        if (MainWindow.Instance.GetIsStarted())
                        {
                            Console.WriteLine("OCR process active, triggering new OCR check");
                            MainWindow.Instance.SetOCRCheckIsWanted(true);
                        }
                        
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"Failed to connect to {ocrMethod} server");
                        return false;
                    }
                }
                
                // Nếu đã xử lý trường hợp EasyOCR sang PaddleOCR ở trên, trả về true
                return _isConnected;
            }
            else
            {
                // Trường hợp Windows OCR, không cần kết nối socket
                Console.WriteLine("Switched to Windows OCR (no socket connection required)");
                
                // Nếu đang ở chế độ Started, kích hoạt OCR check
                if (MainWindow.Instance.GetIsStarted())
                {
                    Console.WriteLine("OCR process active, triggering new Windows OCR check");
                    MainWindow.Instance.SetOCRCheckIsWanted(true);
                }
                
                return true;
            }
        }

        public bool IsWaitingForSomething()
        {
            return _tryingToConnect;
        }

        // Connect to server
        public async Task ConnectAsync()
        {

            if (_tryingToConnect) return;

                 if (_isConnected && _clientSocket != null) 
                {
                    Console.WriteLine("Already connected, ignoring connect request");
                    return;
                }
                
                // Reset connection state to prevent state conflicts
                _isConnected = false;
                
                Console.WriteLine($"Connecting to {_host}:{_port}...");

            // If there's an existing socket, close it properly first
            if (_clientSocket != null)
            {
                try
                {
                    _clientSocket.Close();
                    _clientSocket.Dispose();
                }
                catch (Exception)
                {
                    _clientSocket = null;
                }
            }
                
                // Initialize the socket
                _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                
                // Set socket options for reliability
                _clientSocket.NoDelay = true; // Disable Nagle's algorithm
                _clientSocket.ReceiveTimeout = 10000; // 10 second timeout
                _clientSocket.SendTimeout = 10000; // 10 second timeout
                
                // Connect to the server
                await _clientSocket.ConnectAsync(IPAddress.Parse("127.0.0.1"), _port);
                
                //_isConnected = true;
                ConnectionChanged?.Invoke(this, true);

                try
                { 
                // Start listening for incoming data
                _ = StartListeningAsync();
                
                Console.WriteLine($"Connected to {_host}:{_port}");
                MainWindow.Instance.SetStatus($"Connected to {_host}:{_port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection error: {ex.Message}");
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
            }
           
        }
         public void Disconnect()
        {
           //disconnect
           _isConnected = false;
            ConnectionChanged?.Invoke(this, false);
            _clientSocket?.Close();
            _clientSocket?.Dispose();
            _clientSocket = null;
            _tryingToConnect = false;
        }
        
        // Send data to server
        public async Task<bool> SendDataAsync(string data)
        {
            if (!_isConnected || _clientSocket == null) 
            {
                Console.WriteLine("Not connected when trying to send data. Reconnect will be attempted automatically.");
                // Signal that we're not connected - the reconnect timer in Logic will handle reconnection
                ConnectionChanged?.Invoke(this, false);
                return false;
            }
            
            try
            {
                byte[] messageBytes = Encoding.UTF8.GetBytes(data);
                await _clientSocket.SendAsync(messageBytes, SocketFlags.None);
                //Console.WriteLine($"Sent data: {data}");
                return true;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Socket error when sending data: {ex.Message}");
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
                return false;
            }
            catch (ObjectDisposedException ex)
            {
               
                Console.WriteLine($"Socket was closed. Reconnect will be attempted automatically. {ex.Message}");
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Send error: {ex.Message}");
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
                return false;
            }
        }
        
        // Listen for incoming data
        private async Task StartListeningAsync()
        {
            if (_clientSocket == null) return;
            
            try
            {
                byte[] buffer = new byte[4096];
                
                while (_isConnected)
                {
                    try
                    {
                        // First, receive the size header (terminated by \r\n)
                        byte[] sizeBuffer = new byte[128]; // Buffer for size header (should be much smaller)
                        int sizeBytesRead = 0;
                        bool sizeComplete = false;
                        
                        // Read until we find \r\n sequence
                        while (!sizeComplete && sizeBytesRead < sizeBuffer.Length && _clientSocket != null)
                        {
                            
                            try
                            {
                                // Read one byte at a time for reliable header detection
                                int b = await _clientSocket.ReceiveAsync(
                                    new ArraySegment<byte>(sizeBuffer, sizeBytesRead, 1), 
                                    SocketFlags.None);
                                
                                if (b <= 0)
                                {
                                    // Connection closed
                                    _isConnected = false;
                                    ConnectionChanged?.Invoke(this, false);
                                    Console.WriteLine("Server disconnected (received 0 bytes)");
                                    break;
                                }
                                
                                sizeBytesRead++;
                                
                                // Check for \r\n sequence
                                if (sizeBytesRead >= 2 && 
                                    sizeBuffer[sizeBytesRead - 2] == '\r' && 
                                    sizeBuffer[sizeBytesRead - 1] == '\n')
                                {
                                    sizeComplete = true;
                                    sizeBytesRead -= 2; // Remove \r\n from size
                                    //Console.WriteLine($"Found \\r\\n at position {sizeBytesRead}");
                                }
                            }
                            catch (SocketException sockEx)
                            {
                                Console.WriteLine($"Socket error while receiving header: {sockEx.Message}");
                                _isConnected = false;
                                ConnectionChanged?.Invoke(this, false);
                                break;
                            }
                            catch (ObjectDisposedException)
                            {
                                Console.WriteLine("Socket was closed while receiving header");
                                _isConnected = false;
                                ConnectionChanged?.Invoke(this, false);
                                break;
                            }
                        }
                        
                        if (!_isConnected || _clientSocket == null)
                        {
                            break; // Exit the outer loop if we're not connected anymore
                        }
                        
                        if (!sizeComplete)
                        {
                            // Failed to get complete size header
                            Console.WriteLine("Failed to receive complete size header");
                            continue;
                        }
                        
                        // Convert the size header to an integer
                        string sizeStr = Encoding.UTF8.GetString(sizeBuffer, 0, sizeBytesRead);
                        
                        // Debug: Print all bytes in the size buffer for troubleshooting
                        //Console.WriteLine($"Size buffer content: {BitConverter.ToString(sizeBuffer, 0, Math.Min(100, sizeBytesRead))}");
                        
                        if (!int.TryParse(sizeStr, out int messageSize))
                        {
                            Console.WriteLine($"Invalid size header: '{sizeStr}'");
                            continue;
                        }
                        
                        //Console.WriteLine($"Expecting JSON response of {messageSize} bytes");
                        
                        // Now receive the actual JSON message
                        byte[] jsonBuffer = new byte[messageSize];
                        int jsonBytesRead = 0;
                        
                        // Read until we have the entire message
                        while (jsonBytesRead < messageSize && _clientSocket != null)
                        {
                            try
                            {
                                int remainingBytes = messageSize - jsonBytesRead;
                                
                                // Read in chunks of up to 4096 bytes
                                int chunkSize = Math.Min(4096, remainingBytes);
                                int b = await _clientSocket.ReceiveAsync(
                                    new ArraySegment<byte>(jsonBuffer, jsonBytesRead, chunkSize), 
                                    SocketFlags.None);
                                    
                                if (b <= 0)
                                {
                                    // Connection closed
                                    _isConnected = false;
                                    ConnectionChanged?.Invoke(this, false);
                                    Console.WriteLine("Server disconnected during JSON read");
                                    break;
                                }
                                
                                jsonBytesRead += b;
                            }
                            catch (SocketException sockEx)
                            {
                                Console.WriteLine($"Socket error while receiving JSON data: {sockEx.Message}");
                                _isConnected = false;
                                ConnectionChanged?.Invoke(this, false);
                                break;
                            }
                            catch (ObjectDisposedException)
                            {
                                Console.WriteLine("Socket was closed while receiving JSON data");
                                _isConnected = false;
                                ConnectionChanged?.Invoke(this, false);
                                break;
                            }
                        }
                        
                        if (!_isConnected || _clientSocket == null )
                        {
                            break; // Exit the outer loop if we're not connected anymore
                        }
                        
                        if (jsonBytesRead == messageSize)
                        {
                            // Successfully received the entire message
                            string jsonData = Encoding.UTF8.GetString(jsonBuffer);
                            
                            // For debugging, we'll still save the response to a file
                            try
                            {
                                System.IO.File.WriteAllText("last_ocr_response.json", jsonData);
                            }
                            catch (Exception) { /* Ignore file saving errors */ }
                            
                            // Raise the event with the received data
                            DataReceived?.Invoke(this, jsonData);
                        }
                        else
                        {
                            Console.WriteLine($"Incomplete message: received {jsonBytesRead} of {messageSize} bytes");
                        }
                    }
                   
                    catch (SocketException sockEx)
                    {
                        Console.WriteLine($"Socket error in main listening loop: {sockEx.Message}");
                        _isConnected = false;
                        ConnectionChanged?.Invoke(this, false);
                        break; // Break out of the loop to allow reconnection
                    }
                    catch (ObjectDisposedException)
                    {
                        Console.WriteLine("Socket was closed in main listening loop");
                        _isConnected = false;
                        ConnectionChanged?.Invoke(this, false);
                        break; // Break out of the loop to allow reconnection
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Console.WriteLine($"Error receiving data: {ex.Message}");
                        // Don't break here for general exceptions - just continue the loop
                    }
                }
            }
          
            catch (Exception ex)
            {
                Console.WriteLine($"Listening error: {ex.Message}");
                _isConnected = false;
                ConnectionChanged?.Invoke(this, false);
            }
            finally
            {
                Console.WriteLine("Listening loop has ended. Reconnect timer will attempt to reconnect if needed.");
            }
        }
        
        // Check if connected
        public bool IsConnected => _isConnected;
        
        // Get the port number
        public int GetPort() => _port;

        // Try to reconnect if disconnected
        public async Task<bool> TryReconnectAsync()
        {

            if (_isConnected && _clientSocket != null && _clientSocket.Connected)
            {
                Console.WriteLine("TryReconnectAsync: Already connected");
                return true;
            }

            if (_tryingToConnect) return false;

            // Reset connection state
            Disconnect();

            _tryingToConnect = true;

            try
            {
             
                _isConnected = false;

                // Small delay to ensure clean state
                await Task.Delay(300);

                // Create a new socket
                _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                // Set socket options for reliability
                _clientSocket.NoDelay = true; // Disable Nagle's algorithm
                _clientSocket.ReceiveTimeout = 10000; // 10 second timeout
                _clientSocket.SendTimeout = 10000; // 10 second timeout

                Console.WriteLine("TryReconnectAsync: Connecting to server...");

                // Connect with timeout
                var connectTask = _clientSocket.ConnectAsync(IPAddress.Parse("127.0.0.1"), _port);
                if (await Task.WhenAny(connectTask, Task.Delay(5000)) != connectTask)
                {
                    Console.WriteLine("TryReconnectAsync: Connection timed out");
                    _tryingToConnect = false;
                    return false;
                }
                if (_clientSocket != null)
                {
                    if (!_clientSocket.Connected)
                    {
                        _tryingToConnect = false;

                        Console.WriteLine("TryReconnectAsync: Failed to connect");
                        return false;
                    }
                }

                _isConnected = true;
                _tryingToConnect = false;
                ConnectionChanged?.Invoke(this, true);
                _ = StartListeningAsync();
                Console.WriteLine("TryReconnectAsync: Successfully connected");
                return true;


            }
            catch (Exception ex)
            {
                Console.WriteLine($"TryReconnectAsync: Error during reconnection: {ex.Message}");
                _tryingToConnect = false;
                _isConnected = false;
                return false;
            }
        }

    }
}