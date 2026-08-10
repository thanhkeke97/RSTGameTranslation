using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using WinRT;

namespace RSTGameTranslation
{
    /// <summary>
    /// Captures a window using the Windows Graphics Capture API (Windows 10 2004+).
    /// GPU-friendly alternative to PrintWindow that avoids flickering in games.
    /// </summary>
    internal sealed class GraphicsCaptureService : IDisposable
    {
        private Direct3D11Helper? _d3dHelper;
        private Direct3D11CaptureFramePool? _framePool;
        private GraphicsCaptureSession? _session;
        private GraphicsCaptureItem? _captureItem;
        private SizeInt32 _lastSize;
        private bool _disposed;
        private bool _itemClosed;
        private Bitmap? _lastFrame;

        /// <summary>
        /// Check whether Windows Graphics Capture is available on this system.
        /// </summary>
        public static bool IsSupported()
        {
            try
            {
                return GraphicsCaptureSession.IsSupported();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Start capturing the specified window.
        /// Returns true if capture started successfully.
        /// </summary>
        public bool StartCapture(IntPtr hwnd)
        {
            try
            {
                StopCapture();

                _d3dHelper = Direct3D11Helper.Create();

                _captureItem = CreateCaptureItemForWindow(hwnd);
                if (_captureItem == null)
                    return false;

                _captureItem.Closed += (item, _) =>
                {
                    _itemClosed = true;
                    Console.WriteLine("GraphicsCaptureItem closed (target window gone)");
                };

                _lastSize = _captureItem.Size;

                // Free-threaded pool: FrameArrived fires on thread-pool (not used here,
                // but avoids creating a DispatcherQueue dependency).
                _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _d3dHelper.WinRTDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    _captureItem.Size);

                _session = _framePool.CreateCaptureSession(_captureItem);

                // Hide the yellow capture border (requires Windows 11 22H2+).
                // We use COM interop (QueryInterface for IGraphicsCaptureSession3)
                // instead of reflection because the 19041 SDK projection does not
                // include the IsBorderRequired property, so GetProperty returns null.
                // On older Windows (10 or 11 < 22H2), QueryInterface will return
                // E_NOINTERFACE and the yellow border will remain visible —
                // that is an OS limitation, not a bug.
                try
                {
                    IntPtr unknown = Marshal.GetIUnknownForObject(_session);
                    if (unknown != IntPtr.Zero)
                    {
                        try
                        {
                            Guid iid3 = typeof(IGraphicsCaptureSession3).GUID;
                            int hr = Marshal.QueryInterface(unknown, in iid3, out IntPtr session3Ptr);
                            if (hr == 0 && session3Ptr != IntPtr.Zero)
                            {
                                try
                                {
                                    var session3 = (IGraphicsCaptureSession3)Marshal.GetObjectForIUnknown(session3Ptr);
                                    session3.put_IsBorderRequired(0); // 0 = false → hide border
                                    Console.WriteLine("WGC: Yellow capture border disabled via IGraphicsCaptureSession3");
                                }
                                finally
                                {
                                    Marshal.Release(session3Ptr);
                                }
                            }
                            else
                            {
                                Console.WriteLine("WGC: IGraphicsCaptureSession3 not available (requires Windows 11 22H2+), yellow border will remain");
                            }
                        }
                        finally
                        {
                            Marshal.Release(unknown);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WGC: Could not disable yellow border: {ex.Message}");
                }

                // Exclude the mouse cursor from captured frames.
                try { _session.IsCursorCaptureEnabled = false; } catch { }

                _session.StartCapture();
                Console.WriteLine("Windows Graphics Capture started successfully");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start Windows Graphics Capture: {ex.Message}");
                StopCapture();
                return false;
            }
        }

        /// <summary>
        /// Grab the latest frame synchronously.
        /// Returns null when no frame is available or the target window is gone.
        /// </summary>
        public Bitmap? CaptureFrame()
        {
            if (_disposed || _itemClosed || _framePool == null || _d3dHelper == null)
                return null;

            try
            {
                using var frame = _framePool.TryGetNextFrame();
                if (frame == null)
                {
                    // No new frame since last call — return a copy of the last captured frame.
                    if (_lastFrame != null)
                        return (Bitmap)_lastFrame.Clone();
                    return null;
                }

                // React to window resizes.
                var contentSize = frame.ContentSize;
                if (contentSize.Width != _lastSize.Width || contentSize.Height != _lastSize.Height)
                {
                    _lastSize = contentSize;
                    _framePool.Recreate(
                        _d3dHelper.WinRTDevice,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        2,
                        contentSize);
                }

                var bitmap = _d3dHelper.SurfaceToBitmap(frame.Surface, contentSize.Width, contentSize.Height);

                // Cache the latest frame for reuse when TryGetNextFrame returns null.
                _lastFrame?.Dispose();
                _lastFrame = (Bitmap)bitmap.Clone();

                return bitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WGC CaptureFrame error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Stop the current capture session and release resources.
        /// </summary>
        public void StopCapture()
        {
            _itemClosed = false;

            try { _session?.Dispose(); } catch { }
            _session = null;

            try { _framePool?.Dispose(); } catch { }
            _framePool = null;

            _captureItem = null;

            try { _d3dHelper?.Dispose(); } catch { }
            _d3dHelper = null;

            _lastFrame?.Dispose();
            _lastFrame = null;

            Console.WriteLine("Windows Graphics Capture stopped");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopCapture();
        }

        // ═════════ IGraphicsCaptureSession3 (Windows 11 22H2+) ═════════
        // This interface is not available in the 19041 SDK projection, so we
        // define it manually to access IsBorderRequired (hide yellow border)
        // without upgrading the target framework.
        // GUID verified from Windows.Graphics.winmd: f2cdd966-22ae-5ea1-9596-3a289344c3be
        [ComImport]
        [Guid("F2CDD966-22AE-5EA1-9596-3A289344C3BE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureSession3
        {
            // IInspectable methods (vtable slots 3-5; IUnknown slots 0-2 are
            // handled automatically by InterfaceIsIUnknown).
            void GetIids(out uint iidCount, out IntPtr iids);
            void GetRuntimeClassName(out IntPtr className);
            void GetTrustLevel(out int trustLevel);

            // IGraphicsCaptureSession3 methods (vtable slots 6-7).
            // WinRT Boolean is a 1-byte value (0 = false, 1 = true).
            void get_IsBorderRequired(out byte value);
            void put_IsBorderRequired(byte value);
        }

        // ═════════ HWND → GraphicsCaptureItem interop ═════════

        private static GraphicsCaptureItem? CreateCaptureItemForWindow(IntPtr hwnd)
        {
            string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
            int hr = WindowsCreateString(className, className.Length, out IntPtr hClassName);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                Guid interopGuid = typeof(IGraphicsCaptureItemInterop).GUID;
                hr = RoGetActivationFactory(hClassName, ref interopGuid, out IntPtr factoryPtr);
                Marshal.ThrowExceptionForHR(hr);

                try
                {
                    var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);

                    // IGraphicsCaptureItem interface GUID.
                    Guid itemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                    IntPtr itemPtr = interop.CreateForWindow(hwnd, ref itemIid);

                    try
                    {
                        return (GraphicsCaptureItem)MarshalInspectable<object>.FromAbi(itemPtr);
                    }
                    finally
                    {
                        Marshal.Release(itemPtr);
                    }
                }
                finally
                {
                    Marshal.Release(factoryPtr);
                }
            }
            finally
            {
                WindowsDeleteString(hClassName);
            }
        }

        // ═════════ COM interfaces ═════════

        [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        }

        // ═════════ P/Invoke ═════════

        [DllImport("combase.dll")]
        private static extern int WindowsCreateString(
            [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
            int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(
            IntPtr activatableClassId, ref Guid iid, out IntPtr factory);
    }
}
