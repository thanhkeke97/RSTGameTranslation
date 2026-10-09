using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace RSTGameTranslation
{
    /// <summary>
    /// WASAPI process-loopback capture (Windows 10 2004+): records everything the system plays
    /// EXCEPT the audio of one process tree. Used to capture game audio without our own TTS —
    /// a plain endpoint loopback hears the TTS too, so the app would transcribe and translate
    /// its own speech. Unlike pausing capture while TTS plays, no game audio is lost.
    /// Also not tied to one output device, so switching headphones/speakers keeps working.
    /// NAudio 2.2.1 ships the interop types but no capture class for this mode, hence this one.
    /// </summary>
    public sealed class ProcessLoopbackCapture : IWaveIn
    {
        private const string VirtualProcessLoopbackDevice = "VAD\\Process_Loopback";
        private static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        private const ushort VT_BLOB = 65;
        // PROCESS_LOOPBACK_MODE (NAudio's enum for it is internal)
        private const int ProcessLoopbackModeExcludeTargetProcessTree = 1;

        private readonly AudioClient audioClient;
        private readonly EventWaitHandle frameEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        private Thread? captureThread;
        private volatile bool stopRequested;

        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        // Process loopback has no mix format (GetMixFormat is not implemented), so we pick one
        // and let AUTOCONVERTPCM convert the system mix to it.
        public WaveFormat WaveFormat { get; set; } = new WaveFormat(48000, 16, 2);

        public bool IsCapturing => captureThread != null && !stopRequested;

        private ProcessLoopbackCapture(AudioClient audioClient)
        {
            this.audioClient = audioClient;
        }

        /// <summary>
        /// Create a capture of all system audio except what <paramref name="processId"/> and its
        /// child processes render. Throws if the OS does not support process loopback.
        /// Safe to call from any thread, including the WPF UI thread.
        /// </summary>
        public static Task<ProcessLoopbackCapture> CreateExcludingProcessAsync(int processId)
        {
            // The activated IAudioClient arrives on an MTA thread and is not marshalable to an
            // STA. Awaited from the WPF UI thread, the continuation ran on the STA and the cast
            // to IAudioClient failed with E_NOINTERFACE — the caller then silently fell back to
            // device loopback, which records our own TTS and fed it back into recognition.
            // So everything that touches the COM object runs on the thread pool (MTA).
            return Task.Run(async () =>
            {
                IAudioClient client = await ActivateAsync(processId, ProcessLoopbackModeExcludeTargetProcessTree)
                    .ConfigureAwait(false);
                var capture = new ProcessLoopbackCapture(new AudioClient(client));
                try
                {
                    capture.InitializeClient();
                }
                catch
                {
                    capture.Dispose();
                    throw;
                }
                return capture;
            });
        }

        private void InitializeClient()
        {
            audioClient.Initialize(
                AudioClientShareMode.Shared,
                AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback |
                AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality,
                200 * 10000, // 200 ms buffer, in 100 ns units
                0,
                WaveFormat,
                Guid.Empty);
            audioClient.SetEventHandle(frameEvent.SafeWaitHandle.DangerousGetHandle());
        }

        private static Task<IAudioClient> ActivateAsync(int processId, int loopbackMode)
        {
            var activationParams = new AudioClientActivationParams
            {
                ActivationType = 1, // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
                TargetProcessId = (uint)processId,
                ProcessLoopbackMode = loopbackMode
            };

            int paramsSize = Marshal.SizeOf<AudioClientActivationParams>();
            IntPtr paramsPtr = Marshal.AllocHGlobal(paramsSize);
            IntPtr propVariantPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariantBlob>());
            try
            {
                Marshal.StructureToPtr(activationParams, paramsPtr, false);
                var propVariant = new PropVariantBlob { vt = VT_BLOB, cbSize = (uint)paramsSize, pBlobData = paramsPtr };
                Marshal.StructureToPtr(propVariant, propVariantPtr, false);

                var handler = new ActivationHandler();
                ActivateAudioInterfaceAsync(VirtualProcessLoopbackDevice, IID_IAudioClient, propVariantPtr, handler, out _);
                // The activation params are copied during the call, so they can be freed once
                // the completion fires; awaiting keeps them alive until then.
                return handler.Task.ContinueWith(t =>
                {
                    Marshal.FreeHGlobal(propVariantPtr);
                    Marshal.FreeHGlobal(paramsPtr);
                    return t;
                }, TaskScheduler.Default).Unwrap();
            }
            catch
            {
                Marshal.FreeHGlobal(propVariantPtr);
                Marshal.FreeHGlobal(paramsPtr);
                throw;
            }
        }

        public void StartRecording()
        {
            if (captureThread != null) throw new InvalidOperationException("Already recording");
            stopRequested = false;
            captureThread = new Thread(CaptureLoop) { IsBackground = true, Name = "ProcessLoopbackCapture", Priority = ThreadPriority.AboveNormal };
            captureThread.Start();
        }

        public void StopRecording()
        {
            stopRequested = true;
            frameEvent.Set();
            var thread = captureThread;
            if (thread != null && thread != Thread.CurrentThread) thread.Join(2000);
            captureThread = null;
        }

        private void CaptureLoop()
        {
            Exception? error = null;
            try
            {
                var captureClient = audioClient.AudioCaptureClient;
                int bytesPerFrame = WaveFormat.BlockAlign;
                byte[] buffer = new byte[audioClient.BufferSize * bytesPerFrame];
                audioClient.Start();

                while (!stopRequested)
                {
                    // Timeout so a stream that delivers nothing still notices a stop request
                    frameEvent.WaitOne(200);
                    if (stopRequested) break;

                    int packetFrames = captureClient.GetNextPacketSize();
                    while (packetFrames > 0 && !stopRequested)
                    {
                        IntPtr data = captureClient.GetBuffer(out int frames, out AudioClientBufferFlags flags);
                        int bytes = frames * bytesPerFrame;
                        if (buffer.Length < bytes) buffer = new byte[bytes];
                        if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(buffer, 0, bytes);
                        else Marshal.Copy(data, buffer, 0, bytes);
                        captureClient.ReleaseBuffer(frames);

                        if (bytes > 0) DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytes));
                        packetFrames = captureClient.GetNextPacketSize();
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
                Console.WriteLine($"[ProcessLoopback] Capture error: {ex.Message}");
            }
            finally
            {
                try { audioClient.Stop(); } catch { }
                RecordingStopped?.Invoke(this, new StoppedEventArgs(error));
            }
        }

        public void Dispose()
        {
            StopRecording();
            // Release the COM object on an MTA thread too (Stop() runs on the UI thread)
            try { Task.Run(() => audioClient.Dispose()).Wait(2000); } catch { }
            frameEvent.Dispose();
        }

        // ---- Interop ----

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void ActivateAudioInterfaceAsync(
            [In, MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            [In, MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [In] IntPtr activationParams,
            [In] IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

        // AUDIOCLIENT_ACTIVATION_PARAMS (with the AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS union member)
        [StructLayout(LayoutKind.Sequential)]
        private struct AudioClientActivationParams
        {
            public int ActivationType;
            public uint TargetProcessId;
            public int ProcessLoopbackMode;
        }

        // PROPVARIANT holding a BLOB
        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariantBlob
        {
            public ushort vt;
            public ushort wReserved1;
            public ushort wReserved2;
            public ushort wReserved3;
            public uint cbSize;
            public IntPtr pBlobData;
        }

        // The completion callback arrives on an MTA worker thread; the handler must be agile.
        [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAgileObject { }

        [ComVisible(true)]
        private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
        {
            private readonly TaskCompletionSource<IAudioClient> tcs =
                new TaskCompletionSource<IAudioClient>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<IAudioClient> Task => tcs.Task;

            public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
            {
                try
                {
                    activateOperation.GetActivateResult(out int hr, out object activated);
                    if (hr < 0) tcs.TrySetException(Marshal.GetExceptionForHR(hr) ?? new COMException("Activation failed", hr));
                    else tcs.TrySetResult((IAudioClient)activated);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }
        }
    }
}
