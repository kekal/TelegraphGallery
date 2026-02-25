using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace TelegraphGallery.Services
{
    /// <summary>
    /// Extracts thumbnails via Windows Shell (IShellItemImageFactory).
    /// Works for any file type that Explorer can thumbnail, including videos.
    /// Runs on a dedicated STA thread as required by Shell COM.
    /// </summary>
    internal static class ShellThumbnailHelper
    {
        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(NativeSize size, int flags, out IntPtr hBitmap);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int Width;
            public int Height;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            string pszPath, IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        private static readonly Guid ShellItemImageFactoryGuid =
            new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        // SIIGBF_BIGGERSIZEOK = 0x08 — allow returning a larger thumbnail than requested
        private const int Flags = 0x08;

        public static Task<BitmapSource> GetThumbnailAsync(string filePath, int size)
        {
            var tcs = new TaskCompletionSource<BitmapSource>();

            var thread = new Thread(() =>
            {
                try
                {
                    var bitmap = GetThumbnailCore(filePath, size);
                    tcs.SetResult(bitmap);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            return tcs.Task;
        }

        private static BitmapSource GetThumbnailCore(string filePath, int size)
        {
            SHCreateItemFromParsingName(filePath, IntPtr.Zero,
                ShellItemImageFactoryGuid, out var factory);

            var nativeSize = new NativeSize { Width = size, Height = size };

            var hr = factory.GetImage(nativeSize, Flags, out var hBitmap);
            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);

            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
    }
}
