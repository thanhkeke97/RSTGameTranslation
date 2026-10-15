using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RSTGameTranslation
{
    /// <summary>
    /// Tells whether a captured frame shows the same picture as the last frame that was OCR'd, so the
    /// capture loop can skip OCR while the screen stands still (most of the time, between lines).
    /// </summary>
    public sealed class FrameChangeDetector
    {
        // A channel has to move more than this to count as a change: dithering and colour shimmer stay
        // below it, while a glyph stroke (even a full stop) is far above it
        private const int ChannelTolerance = 24;

        private byte[] _ocrFrame = Array.Empty<byte>();
        private byte[] _frame = Array.Empty<byte>();
        private int _ocrWidth, _ocrHeight, _ocrOriginX, _ocrOriginY;
        private int _width, _height, _originX, _originY;
        private bool _hasOcrFrame;
        private bool _hasFrame;

        /// <summary>
        /// Takes a copy of the frame whose top-left pixel is at (originX, originY) on screen and
        /// compares it with the last OCR'd frame. A moved capture area always counts as changed.
        /// </summary>
        public bool IsSameAsOcrFrame(Bitmap frame, int originX, int originY)
        {
            _hasFrame = TryCopyPixels(frame);
            _originX = originX;
            _originY = originY;
            if (!_hasFrame || !_hasOcrFrame)
                return false;

            if (_width != _ocrWidth || _height != _ocrHeight || _originX != _ocrOriginX || _originY != _ocrOriginY)
                return false;

            int length = _width * _height * 4;
            ReadOnlySpan<byte> current = _frame.AsSpan(0, length);
            ReadOnlySpan<byte> ocr = _ocrFrame.AsSpan(0, length);
            if (current.SequenceEqual(ocr))
                return true;

            // BGRA: compare the colour channels, alpha is always opaque on a screen capture
            for (int i = 0; i < length; i += 4)
            {
                if (Math.Abs(current[i] - ocr[i]) > ChannelTolerance
                    || Math.Abs(current[i + 1] - ocr[i + 1]) > ChannelTolerance
                    || Math.Abs(current[i + 2] - ocr[i + 2]) > ChannelTolerance)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The frame last passed to IsSameAsOcrFrame is being OCR'd: compare later frames with it.</summary>
        public void MarkOcrFrame()
        {
            if (!_hasFrame)
            {
                _hasOcrFrame = false;
                return;
            }

            (_ocrFrame, _frame) = (_frame, _ocrFrame);
            _ocrWidth = _width;
            _ocrHeight = _height;
            _ocrOriginX = _originX;
            _ocrOriginY = _originY;
            _hasOcrFrame = true;
            _hasFrame = false;
        }

        private bool TryCopyPixels(Bitmap bitmap)
        {
            try
            {
                int width = bitmap.Width;
                int height = bitmap.Height;
                int rowBytes = width * 4;
                if (_frame.Length < rowBytes * height)
                    _frame = new byte[rowBytes * height];

                BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, _frame, y * rowBytes, rowBytes);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                _width = width;
                _height = height;
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Frame change check failed, running OCR anyway: {ex.Message}");
                return false;
            }
        }
    }
}
