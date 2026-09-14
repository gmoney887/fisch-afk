using System;
using System.Runtime.InteropServices;
using OpenCvSharp;
using FischMacroCS.Native;

namespace FischMacroCS.Capture;

public class ScreenCapture : IDisposable
{
    private IntPtr _hMemDC = IntPtr.Zero;
    private IntPtr _hDIBBitmap = IntPtr.Zero;
    private IntPtr _hOldBitmap = IntPtr.Zero;
    private IntPtr _pBits = IntPtr.Zero;
    private int _cachedWidth = 0;
    private int _cachedHeight = 0;

    public Mat? CaptureClientRegion(IntPtr hWnd, int clientX, int clientY, int width, int height)
    {
        if (hWnd == IntPtr.Zero || width <= 0 || height <= 0)
            return null;

        // Convert client relative (clientX, clientY) to physical desktop screen coordinates
        Win32.POINT pt = new Win32.POINT { X = clientX, Y = clientY };
        if (!Win32.ClientToScreen(hWnd, ref pt))
            return null;

        // Capture directly from Desktop DC to read the DWM hardware-composited frame
        IntPtr hDesktopDC = Win32.GetDC(IntPtr.Zero);
        if (hDesktopDC == IntPtr.Zero)
            return null;

        try
        {
            if (_hMemDC == IntPtr.Zero)
                _hMemDC = Win32.CreateCompatibleDC(IntPtr.Zero);
            if (_hMemDC == IntPtr.Zero)
                return null;

            if (_hDIBBitmap == IntPtr.Zero || _cachedWidth != width || _cachedHeight != height)
            {
                if (_hOldBitmap != IntPtr.Zero)
                    Win32.SelectObject(_hMemDC, _hOldBitmap);
                if (_hDIBBitmap != IntPtr.Zero)
                    Win32.DeleteObject(_hDIBBitmap);

                var bmi = new Win32.BITMAPINFO();
                bmi.bmiHeader.biSize = Marshal.SizeOf<Win32.BITMAPINFOHEADER>();
                bmi.bmiHeader.biWidth = width;
                bmi.bmiHeader.biHeight = -height; // Top-down DIB
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0; // BI_RGB

                _hDIBBitmap = Win32.CreateDIBSection(hDesktopDC, ref bmi, 0, out _pBits, IntPtr.Zero, 0);
                if (_hDIBBitmap == IntPtr.Zero || _pBits == IntPtr.Zero)
                    return null;

                _hOldBitmap = Win32.SelectObject(_hMemDC, _hDIBBitmap);
                _cachedWidth = width;
                _cachedHeight = height;
            }

            // Blit screen pixels directly into DIBSection memory buffer at _pBits
            Win32.BitBlt(_hMemDC, 0, 0, width, height, hDesktopDC, pt.X, pt.Y, Win32.SRCCOPY);

            // Wrap DIB section memory buffer directly with OpenCV Mat (zero copy!)
            using Mat bgraMat = Mat.FromPixelData(height, width, MatType.CV_8UC4, _pBits);

            // Convert to 3-channel BGR for OpenCV processing
            Mat bgrMat = new Mat();
            Cv2.CvtColor(bgraMat, bgrMat, ColorConversionCodes.BGRA2BGR);

            return bgrMat;
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, hDesktopDC);
        }
    }

    public void Dispose()
    {
        if (_hOldBitmap != IntPtr.Zero && _hMemDC != IntPtr.Zero)
        {
            Win32.SelectObject(_hMemDC, _hOldBitmap);
            _hOldBitmap = IntPtr.Zero;
        }
        if (_hDIBBitmap != IntPtr.Zero)
        {
            Win32.DeleteObject(_hDIBBitmap);
            _hDIBBitmap = IntPtr.Zero;
        }
        if (_hMemDC != IntPtr.Zero)
        {
            Win32.DeleteDC(_hMemDC);
            _hMemDC = IntPtr.Zero;
        }
        _pBits = IntPtr.Zero;
    }
}
