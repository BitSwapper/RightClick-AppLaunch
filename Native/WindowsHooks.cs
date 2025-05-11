// File: Native/WindowsHooks.cs
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

// CHANGE NAMESPACE
namespace RightClickAppLauncher.Native;

public class WindowsHooks
{
    // ... (rest of the code is the same)
    #region Win32 API Declarations

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    const int WH_MOUSE_LL = 14;
    //const int WM_RBUTTONDOWN = 0x0204; // We're interested in RBUTTONUP for menus
    const int WM_RBUTTONUP = 0x0205;

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    #endregion

    #region Hook Implementation

    IntPtr mouseHookHandle = IntPtr.Zero;
    HookProc mouseProcDelegate; // Keep a reference to the delegate
    public event EventHandler<MouseHookEventArgs> RightMouseClick;

    public WindowsHooks() =>
        // Store the delegate instance to prevent it from being garbage collected
        mouseProcDelegate = MouseHookCallback;


    public void InstallMouseHook()
    {
        if(mouseHookHandle == IntPtr.Zero)
        {
            // Get the handle of the current module (your application)
            // For .NET Core/5+ Process.GetCurrentProcess().MainModule.ModuleName might be problematic for single file exe.
            // Using GetModuleHandle(null) gets the handle of the file used to create the calling process (.exe file)
            IntPtr hMod = GetModuleHandle(null);
            if(hMod == IntPtr.Zero) // Fallback for safety, though GetModuleHandle(null) should work
            {
                hMod = Marshal.GetHINSTANCE(Assembly.GetExecutingAssembly().GetModules()[0]);
            }


            mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, mouseProcDelegate, hMod, 0);

            if(mouseHookHandle == IntPtr.Zero)
            {
                int errorCode = Marshal.GetLastWin32Error();
                Debug.WriteLine($"Failed to install mouse hook. Error code: {errorCode}");
                throw new System.ComponentModel.Win32Exception(errorCode);
            }
            Debug.WriteLine("Mouse hook installed successfully.");
        }
    }


    public void UninstallMouseHook()
    {
        if(mouseHookHandle != IntPtr.Zero)
        {
            if(!UnhookWindowsHookEx(mouseHookHandle))
            {
                int errorCode = Marshal.GetLastWin32Error();
                Debug.WriteLine($"Failed to uninstall mouse hook. Error code: {errorCode}");
            }
            else
            {
                Debug.WriteLine("Mouse hook uninstalled successfully.");
            }
            mouseHookHandle = IntPtr.Zero;
        }
    }


    IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if(nCode >= 0)
        {
            MSLLHOOKSTRUCT hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));

            if(wParam == (IntPtr)WM_RBUTTONUP)
            {
                POINT cursorPos = hookStruct.pt;
                // IntPtr windowUnderCursor = WindowFromPoint(cursorPos); // Not strictly needed for global menu

                RightMouseClick?.Invoke(this, new MouseHookEventArgs
                {
                    X = cursorPos.X,
                    Y = cursorPos.Y,
                    // WindowHandle = windowUnderCursor // Can be omitted if not used
                });
            }
        }
        return CallNextHookEx(mouseHookHandle, nCode, wParam, lParam);
    }

    #endregion

    #region Native Structures

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
    // POINT struct should be defined or accessible here if not already global
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT // Made public for MouseHookEventArgs
    {
        public int X;
        public int Y;
    }


    #endregion
}


public class MouseHookEventArgs : EventArgs
{
    public int X { get; set; }
    public int Y { get; set; }
    // public IntPtr WindowHandle { get; set; } // Can be omitted
}