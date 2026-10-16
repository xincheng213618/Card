using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TableOverlayNavigationChecks
{

    [DllImport("user32.dll")] private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint handle, int index, int value);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint SetFocus(nint handle);
    [DllImport("user32.dll")] private static extern nint GetFocus();
}
