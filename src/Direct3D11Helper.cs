using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace RSTGameTranslation
{
    /// <summary>
    /// Manages a D3D11 device and provides helpers for converting WGC frame
    /// surfaces to System.Drawing.Bitmap via synchronous GPU texture mapping.
    /// </summary>
    internal sealed class Direct3D11Helper : IDisposable
    {
        private IntPtr _d3dDevice;
        private IntPtr _d3dContext;
        private IDirect3DDevice? _winrtDevice;
        private bool _disposed;

        public IDirect3DDevice WinRTDevice =>
            _winrtDevice ?? throw new ObjectDisposedException(nameof(Direct3D11Helper));

        private Direct3D11Helper(IntPtr d3dDevice, IntPtr d3dContext, IDirect3DDevice winrtDevice)
        {
            _d3dDevice = d3dDevice;
            _d3dContext = d3dContext;
            _winrtDevice = winrtDevice;
        }

        /// <summary>
        /// Create a hardware D3D11 device together with its WinRT wrapper.
        /// </summary>
        public static Direct3D11Helper Create()
        {
            int hr = D3D11CreateDevice(
                IntPtr.Zero,
                D3D_DRIVER_TYPE_HARDWARE,
                IntPtr.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero, 0,
                D3D11_SDK_VERSION,
                out IntPtr d3dDevice,
                out _,
                out IntPtr d3dContext);

            if (hr != 0)
                throw new COMException("Failed to create D3D11 device", hr);

            try
            {
                Guid dxgiGuid = IID_IDXGIDevice;
                hr = Marshal.QueryInterface(d3dDevice, ref dxgiGuid, out IntPtr dxgiDevice);
                if (hr != 0)
                    throw new COMException("Failed to get IDXGIDevice", hr);

                try
                {
                    hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out IntPtr inspectable);
                    if (hr != 0)
                        throw new COMException("Failed to create WinRT Direct3D device", hr);

                    try
                    {
                        var winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
                        return new Direct3D11Helper(d3dDevice, d3dContext, winrtDevice);
                    }
                    finally
                    {
                        Marshal.Release(inspectable);
                    }
                }
                finally
                {
                    Marshal.Release(dxgiDevice);
                }
            }
            catch
            {
                Marshal.Release(d3dContext);
                Marshal.Release(d3dDevice);
                throw;
            }
        }

        /// <summary>
        /// Convert a WGC captured surface to a <see cref="Bitmap"/> synchronously.
        /// </summary>
        public Bitmap SurfaceToBitmap(IDirect3DSurface surface, int contentWidth, int contentHeight)
        {
            // Get the ABI pointer for the WinRT surface using CsWinRT marshaling.
            IntPtr surfacePtr = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
            try
            {
                // QI for IDirect3DDxgiInterfaceAccess.
                Guid accessIid = IID_IDirect3DDxgiInterfaceAccess;
                int hr = Marshal.QueryInterface(surfacePtr, ref accessIid, out IntPtr accessPtr);
                Marshal.ThrowExceptionForHR(hr);

                try
                {
                    // IDirect3DDxgiInterfaceAccess::GetInterface → ID3D11Texture2D
                    // vtable: IUnknown(3 slots) + GetInterface = slot 3
                    var getInterface = VTable<GetInterfaceDelegate>(accessPtr, 3);
                    Guid textureGuid = IID_ID3D11Texture2D;
                    hr = getInterface(accessPtr, ref textureGuid, out IntPtr srcTexture);
                    Marshal.ThrowExceptionForHR(hr);

                    try
                    {
                        return TextureToBitmap(srcTexture, contentWidth, contentHeight);
                    }
                    finally
                    {
                        Marshal.Release(srcTexture);
                    }
                }
                finally
                {
                    Marshal.Release(accessPtr);
                }
            }
            finally
            {
                Marshal.Release(surfacePtr);
            }
        }

        private Bitmap TextureToBitmap(IntPtr srcTexture, int contentWidth, int contentHeight)
        {
            // Read source texture dimensions.
            var getDesc = VTable<GetDescDelegate>(srcTexture, SLOT_Texture2D_GetDesc);
            getDesc(srcTexture, out D3D11_TEXTURE2D_DESC desc);

            int width = Math.Min(contentWidth, (int)desc.Width);
            int height = Math.Min(contentHeight, (int)desc.Height);
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException($"Invalid capture dimensions {width}x{height}");

            // Create a CPU-readable staging copy.
            var stagingDesc = new D3D11_TEXTURE2D_DESC
            {
                Width = desc.Width,
                Height = desc.Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = desc.Format,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = D3D11_USAGE_STAGING,
                BindFlags = 0,
                CPUAccessFlags = D3D11_CPU_ACCESS_READ,
                MiscFlags = 0
            };

            var createTex = VTable<CreateTexture2DDelegate>(_d3dDevice, SLOT_Device_CreateTexture2D);
            int hr = createTex(_d3dDevice, ref stagingDesc, IntPtr.Zero, out IntPtr stagingTexture);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                // GPU → staging copy.
                var copyResource = VTable<CopyResourceDelegate>(_d3dContext, SLOT_Context_CopyResource);
                copyResource(_d3dContext, stagingTexture, srcTexture);

                // Map for CPU read.
                var map = VTable<MapDelegate>(_d3dContext, SLOT_Context_Map);
                hr = map(_d3dContext, stagingTexture, 0, D3D11_MAP_READ, 0, out D3D11_MAPPED_SUBRESOURCE mapped);
                Marshal.ThrowExceptionForHR(hr);

                try
                {
                    var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    var bitmapData = bitmap.LockBits(
                        new Rectangle(0, 0, width, height),
                        ImageLockMode.WriteOnly,
                        PixelFormat.Format32bppArgb);

                    try
                    {
                        int bytesPerRow = width * 4;
                        for (int row = 0; row < height; row++)
                        {
                            IntPtr srcRow = IntPtr.Add(mapped.pData, row * (int)mapped.RowPitch);
                            IntPtr dstRow = IntPtr.Add(bitmapData.Scan0, row * bitmapData.Stride);
                            RtlMoveMemory(dstRow, srcRow, (uint)bytesPerRow);
                        }
                    }
                    finally
                    {
                        bitmap.UnlockBits(bitmapData);
                    }

                    return bitmap;
                }
                finally
                {
                    var unmap = VTable<UnmapDelegate>(_d3dContext, SLOT_Context_Unmap);
                    unmap(_d3dContext, stagingTexture, 0);
                }
            }
            finally
            {
                Marshal.Release(stagingTexture);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _winrtDevice?.Dispose();
            _winrtDevice = null;

            if (_d3dContext != IntPtr.Zero) { Marshal.Release(_d3dContext); _d3dContext = IntPtr.Zero; }
            if (_d3dDevice != IntPtr.Zero) { Marshal.Release(_d3dDevice); _d3dDevice = IntPtr.Zero; }
        }

        // ───────── COM vtable helper ─────────

        private static T VTable<T>(IntPtr comObj, int slot) where T : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(comObj);
            IntPtr fn = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer<T>(fn);
        }

        // ───────── Vtable slot indices ─────────
        // ID3D11Texture2D  : ID3D11Resource(3) : ID3D11DeviceChild(4) : IUnknown(3)  →  GetDesc = 10
        private const int SLOT_Texture2D_GetDesc = 10;
        // ID3D11Device     : IUnknown(3) → CreateTexture2D = 5
        private const int SLOT_Device_CreateTexture2D = 5;
        // ID3D11DeviceContext : ID3D11DeviceChild(4) : IUnknown(3) → Map=14, Unmap=15, CopyResource=47
        private const int SLOT_Context_Map = 14;
        private const int SLOT_Context_Unmap = 15;
        private const int SLOT_Context_CopyResource = 47;

        // ───────── D3D11 / DXGI constants ─────────
        private const int D3D_DRIVER_TYPE_HARDWARE = 1;
        private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
        private const uint D3D11_SDK_VERSION = 7;
        private const uint D3D11_USAGE_STAGING = 3;
        private const uint D3D11_CPU_ACCESS_READ = 0x20000;
        private const uint D3D11_MAP_READ = 1;

        // ───────── GUIDs ─────────
        private static Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        private static Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        private static Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

        // ───────── P/Invoke ─────────
        [DllImport("d3d11.dll")]
        private static extern int D3D11CreateDevice(
            IntPtr pAdapter, int driverType, IntPtr software, uint flags,
            IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
            out IntPtr device, out int featureLevel, out IntPtr context);

        [DllImport("d3d11.dll")]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(
            IntPtr dxgiDevice, out IntPtr graphicsDevice);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void RtlMoveMemory(IntPtr dest, IntPtr src, uint length);

        // ───────── Native structs ─────────
        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11_TEXTURE2D_DESC
        {
            public uint Width, Height, MipLevels, ArraySize;
            public int Format;
            public DXGI_SAMPLE_DESC SampleDesc;
            public uint Usage, BindFlags, CPUAccessFlags, MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DXGI_SAMPLE_DESC { public uint Count, Quality; }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3D11_MAPPED_SUBRESOURCE
        {
            public IntPtr pData;
            public uint RowPitch, DepthPitch;
        }

        // ───────── COM vtable delegates ─────────
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetInterfaceDelegate(IntPtr self, ref Guid iid, out IntPtr ptr);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetDescDelegate(IntPtr self, out D3D11_TEXTURE2D_DESC desc);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateTexture2DDelegate(
            IntPtr self, ref D3D11_TEXTURE2D_DESC desc, IntPtr initialData, out IntPtr texture);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void CopyResourceDelegate(IntPtr self, IntPtr dst, IntPtr src);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int MapDelegate(
            IntPtr self, IntPtr resource, uint subresource, uint mapType, uint mapFlags,
            out D3D11_MAPPED_SUBRESOURCE mapped);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void UnmapDelegate(IntPtr self, IntPtr resource, uint subresource);
    }
}
