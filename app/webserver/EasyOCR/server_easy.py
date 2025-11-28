import socket
import json
import logging
import requests
import os
import time
import threading
import queue
import signal
import sys

# Import EasyOCR implementation
from process_image_easyocr import process_image, release_gpu_resources

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

# Server configuration
HOST = '127.0.0.1'  # Standard loopback interface address (localhost)
PORT = 9999         # Port to listen on
BUFFER_SIZE = 1024  # Buffer size for receiving data
MAX_CONNECTIONS = 5  # Maximum number of concurrent connections
CONNECTION_TIMEOUT = 60  # Connection timeout in seconds
MAX_WORKERS = 2  # Maximum number of worker threads for OCR processing

# Queue for OCR tasks
ocr_task_queue = queue.Queue(maxsize=10)  # Limit queue size to prevent memory issues
active_connections = 0  # Track active connections
server_running = True  # Flag to control server shutdown

def handle_client_connection(conn, addr):
    """
    Handle a client connection.
    """
    global active_connections
    active_connections += 1
    logger.info(f"Connected by {addr}. Active connections: {active_connections}")
    
    # Set connection timeout
    conn.settimeout(CONNECTION_TIMEOUT)
    
    try:
        while server_running:
            # Receive data from the client
            try:
                data = conn.recv(BUFFER_SIZE)
            except socket.timeout:
                logger.warning(f"Connection with {addr} timed out")
                break
            
            # If no data, the client has closed the connection
            if not data:
                logger.info(f"Client {addr} disconnected")
                break
            
            # Decode and process the command
            command = data.decode('utf-8').strip()
            logger.info(f"Received command: {command}")
            
            if command.startswith("read_image"):
                # Check if server is too busy
                if ocr_task_queue.full():
                    error_msg = json.dumps({"status": "error", "message": "Server is busy, try again later"}).encode('utf-8')
                    send_response(conn, error_msg)
                    logger.warning("Rejected task due to server load")
                    continue
                
                # Parse parameters if provided
                lang = 'english'  # Default language
                implementation = 'easyocr'  # Now only supporting EasyOCR
                char_level_rec = 'True'
                hdr_support_rec = 'False'
                
                if "|" in command:
                    parts = command.split("|")
                    if len(parts) > 1 and parts[1]:
                        lang = parts[1]
                    if len(parts) > 2 and parts[2]:
                        implementation = parts[2].lower()
                    if len(parts) > 3 and parts[3]:
                        char_level_rec = parts[3]
                    if len(parts) > 4 and parts[4]:
                        hdr_support_rec = parts[4]
                
                # Check if character-level OCR is requested
                char_level = char_level_rec  # Default to character-level
                
                # Log the OCR engine and language being used
                logger.info(f"Using EasyOCR with language: {lang}, character-level: {char_level}, OCR engine: {implementation}, HDR support: {hdr_support_rec}")
                
                # Process image with EasyOCR
                start_time = time.time()
                result = process_image("../image_to_process.png", lang=lang, char_level=char_level, preprocess_images=hdr_support_rec)
                
                
                release_gpu_resources()
                
                # Send results back to client as JSON
                response = json.dumps(result, ensure_ascii=False).encode('utf-8')
                send_response(conn, response)
                
                # Calculate time taken and log it
                time_taken = time.time() - start_time
                logger.info(f"Sent OCR results to client (time taken: {time_taken:.2f} seconds)")
            else:
                # Unknown command
                error_msg = json.dumps({"status": "error", "message": "Unknown command"}).encode('utf-8')
                send_response(conn, error_msg)
                logger.info(f"Unknown command: {command}")
    
    except Exception as e:
        logger.error(f"Error handling client connection: {e}")
    
    finally:
        # Clean up the connection
        conn.close()
        active_connections -= 1
        logger.info(f"Connection with {addr} closed. Active connections: {active_connections}")

def send_response(conn, response):
    """
    Send response to client with proper headers and chunking for large data.
    """
    try:
        # First send the size of the response
        response_size = len(response)
        size_header = str(response_size).encode('utf-8') + b'\r\n'
        
        # Clear socket buffers before sending
        conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        
        # Send size header
        conn.sendall(size_header)
        
        # Small delay to ensure header and data don't get merged
        time.sleep(0.01)
        
        # Send data in chunks for large responses
        chunk_size = 8192  # 8KB chunks
        for i in range(0, len(response), chunk_size):
            chunk = response[i:i+chunk_size]
            conn.sendall(chunk)
            # Small yield to prevent CPU hogging
            if i + chunk_size < len(response):
                time.sleep(0.001)
        
        logger.debug(f"Sent response with size: {response_size}")
    except Exception as e:
        logger.error(f"Error sending response: {e}")

def signal_handler(sig, frame):
    """
    Handle termination signals gracefully.
    """
    global server_running
    logger.info("Shutting down server...")
    server_running = False
    # Give connections time to close
    time.sleep(1)
    sys.exit(0)

def main():
    """Start the server and listen for connections."""
    global server_running
    
    # Set up signal handlers
    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)
    
    # Create a TCP/IP socket
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        # Set socket option to reuse address
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        
        # Bind the socket to the address and port
        s.bind((HOST, PORT))
        
        # Listen for incoming connections
        s.listen(MAX_CONNECTIONS)
        s.settimeout(1)  # Set a timeout so we can check server_running flag periodically
        
        logger.info(f"Server started on {HOST}:{PORT}")
        
        try:
            while server_running:
                try:
                    # Wait for a connection
                    conn, addr = s.accept()
                    
                    # Check if we can handle more connections
                    if active_connections >= MAX_CONNECTIONS:
                        logger.warning(f"Maximum connections reached. Rejecting connection from {addr}")
                        conn.close()
                        continue
                    
                    # Handle the connection in a new thread
                    client_thread = threading.Thread(target=handle_client_connection, args=(conn, addr))
                    client_thread.daemon = True
                    client_thread.start()
                
                except socket.timeout:
                    # This is expected due to the timeout we set
                    continue
                except Exception as e:
                    logger.error(f"Error accepting connection: {e}")
        
        except Exception as e:
            logger.error(f"Error in main server loop: {e}")
        
        finally:
            logger.info("Server shutdown complete")

if __name__ == "__main__":
    main()