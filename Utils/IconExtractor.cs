using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RightClickAppLauncher.Utils;

public static class IconExtractor
{
    public static ImageSource GetIcon(string filePath, bool smallIcon = true)
    {
        if(string.IsNullOrEmpty(filePath))
            return null;

        try
        {
            if(File.Exists(filePath))
            {
                var imageExtensions = new[] { ".ico", ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if(Array.Exists(imageExtensions, e => e == ext))
                {
                    var bmi = new BitmapImage();
                    bmi.BeginInit();
                    bmi.UriSource = new Uri(filePath);
                    bmi.CacheOption = BitmapCacheOption.OnLoad;
                    bmi.EndInit();
                    bmi.Freeze();
                    return bmi;
                }

                Icon icon = ExtractIconFromFile(filePath, smallIcon);
                if(icon != null)
                {
                    ImageSource imgSource = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    imgSource.Freeze();
                    icon.Dispose();
                    return imgSource;
                }
            }
        }
        catch(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting icon for {filePath}: {ex.Message}");
        }
        return GetDefaultIcon();
    }

    static Icon ExtractIconFromFile(string filePath, bool small)
    {
        try
        {
            IntPtr[] hDummy = new IntPtr[1] { IntPtr.Zero };
            IntPtr[] hIconEx = new IntPtr[1] { IntPtr.Zero };

            uint readIconCount = Shell32.ExtractIconEx(
                filePath,
                0,
                small ? hDummy : hIconEx,
                small ? hIconEx : hDummy,
                1
            );

            if(readIconCount > 0 && hIconEx[0] != IntPtr.Zero)
            {
                Icon extractedIcon = (Icon)Icon.FromHandle(hIconEx[0]).Clone();
                User32.DestroyIcon(hIconEx[0]);
                return extractedIcon;
            }
        }
        catch(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ExtractIconEx failed for {filePath}: {ex.Message}");
        }
        return null;
    }

    static ImageSource GetDefaultIcon()
    {
        Icon sysIcon = SystemIcons.Application;
        ImageSource imgSource = Imaging.CreateBitmapSourceFromHIcon(
            sysIcon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        imgSource.Freeze();
        return imgSource;
    }


    static class Shell32
    {
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        internal static extern uint ExtractIconEx(
            string szFileName,
            int nIconIndex,
            IntPtr[] phiconLarge,
            IntPtr[] phiconSmall,
            uint nIcons);
    }

    static class User32
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr hIcon);
    }
}