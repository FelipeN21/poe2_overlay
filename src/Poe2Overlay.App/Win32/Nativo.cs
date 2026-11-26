using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Poe2Overlay.App.Win32;

// Chamadas da API do Windows usadas pelo overlay.
internal static class Nativo
{
    public const int WM_HOTKEY = 0x0312;
    public const int WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_MESSAGE = new(-3);
    public const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)] public struct Ponto { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Retangulo { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct InfoMonitor { public int cbSize; public Retangulo rcMonitor; public Retangulo rcWork; public uint dwFlags; }

    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder texto, int max);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Ponto p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out Retangulo r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Ponto p, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref InfoMonitor info);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr depoisDe, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, Teclado.Entrada[] entradas, int tamanho);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr64(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong32(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong32(IntPtr h, int i, int v);

    public static void AdicionarEstiloEstendido(IntPtr h, long estilo)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(h, GWL_EXSTYLE, new IntPtr(GetWindowLongPtr64(h, GWL_EXSTYLE).ToInt64() | estilo));
        else SetWindowLong32(h, GWL_EXSTYLE, GetWindowLong32(h, GWL_EXSTYLE) | (int)estilo);
    }

    // A janela do PoE2 se chama "Path of Exile 2".
    public static bool JogoEmFoco()
    {
        var texto = new StringBuilder(256);
        GetWindowText(GetForegroundWindow(), texto, texto.Capacity);
        return texto.ToString().StartsWith("Path of Exile", StringComparison.Ordinal);
    }

    public static Retangulo AreaDeTrabalho(Ponto p)
    {
        var info = new InfoMonitor { cbSize = Marshal.SizeOf<InfoMonitor>() };
        GetMonitorInfo(MonitorFromPoint(p, 2 /* MONITOR_DEFAULTTONEAREST */), ref info);
        return info.rcWork;
    }
}
