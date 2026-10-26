using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DecisionCouncil.App.Infrastructure;

public static class WindowTheme
{
    /// <summary>Dark Windows title bar, so the frame matches the dark content (DWMWA_USE_IMMERSIVE_DARK_MODE).</summary>
    public static void UseDarkTitleBar(Window window)
    {
        void Apply()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            int on = 1;
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Apply();
        else window.SourceInitialized += (_, _) => Apply();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
