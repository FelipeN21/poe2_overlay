using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Poe2Overlay.App.Win32;

// Simula o Ctrl+C (ou Ctrl+Alt+C) sobre o item. É uma única ação por tecla apertada,
// dentro do que a GGG permite (nada de automatizar jogo).
internal static class Teclado
{
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_C = 0x43;
    const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    public struct Entrada { public uint type; public Uniao u; }

    // A união precisa do MOUSEINPUT para o struct ter o tamanho que o Windows espera.
    [StructLayout(LayoutKind.Explicit)]
    public struct Uniao { [FieldOffset(0)] public Mouse mi; [FieldOffset(0)] public Tecla ki; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Mouse { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Tecla { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    // Pausa entre cada tecla: o jogo lê o teclado a cada quadro. Tudo de uma vez, ele podia perder o "soltar Alt",
    // achar que o Alt continuava apertado e deixar os detalhes avançados do item fixos na tela.
    const int PausaMs = 25;

    public static async Task EnviarCopiaAsync(bool avancada)
    {
        bool ctrl = Pressionada(VK_CONTROL), alt = Pressionada(VK_MENU), shift = Pressionada(VK_SHIFT);
        var seq = new List<Entrada>();
        if (shift) seq.Add(Evento(VK_SHIFT, false));      // Ctrl+Shift+C não copia o item
        if (!ctrl) seq.Add(Evento(VK_CONTROL, true));
        if (avancada && !alt) seq.Add(Evento(VK_MENU, true));
        seq.Add(Evento(VK_C, true));
        seq.Add(Evento(VK_C, false));
        if (avancada && !alt) seq.Add(Evento(VK_MENU, false));
        if (!ctrl) seq.Add(Evento(VK_CONTROL, false));
        foreach (var evento in seq)
        {
            Nativo.SendInput(1, [evento], Marshal.SizeOf<Entrada>());
            await Task.Delay(PausaMs);
        }
    }

    static bool Pressionada(int vk) => (Nativo.GetAsyncKeyState(vk) & 0x8000) != 0;

    static Entrada Evento(int vk, bool descendo) => new()
    {
        type = INPUT_KEYBOARD,
        u = new Uniao { ki = new Tecla { wVk = (ushort)vk, dwFlags = descendo ? 0 : KEYEVENTF_KEYUP } },
    };
}
