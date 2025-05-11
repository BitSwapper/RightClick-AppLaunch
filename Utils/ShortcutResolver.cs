// File: Utils/ShortcutResolver.cs
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RightClickAppLauncher.Utils;

public static class ShortcutResolver
{
    // COM Interop for IShellLink
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    public static string ResolveShortcut(string shortcutPath)
    {
        if(!File.Exists(shortcutPath) || !Path.GetExtension(shortcutPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        IShellLinkW link = null;
        try
        {
            link = (IShellLinkW)new ShellLink();
            // Load the shortcut
            (link as System.Runtime.InteropServices.ComTypes.IPersistFile)?.Load(shortcutPath, 0); // 0 = STGM_READ

            // Resolve the shortcut (optional, but good practice)
            // link.Resolve(IntPtr.Zero, 0); // No UI, no update if broken link

            StringBuilder sb = new StringBuilder(260); // MAX_PATH
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0); // 0 = SLGP_SHORTPATH (can use SLGP_UNCPRIORITY)

            string targetPath = sb.ToString();
            Debug.WriteLine($"Resolved shortcut '{shortcutPath}' to '{targetPath}'");
            return targetPath;
        }
        catch(COMException ex)
        {
            Debug.WriteLine($"COMException resolving shortcut '{shortcutPath}': {ex.Message}");
            return null; // Or return shortcutPath itself if resolution fails and you want to try executing the .lnk
        }
        catch(Exception ex)
        {
            Debug.WriteLine($"Exception resolving shortcut '{shortcutPath}': {ex.Message}");
            return null;
        }
        finally
        {
            if(link != null)
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }
}