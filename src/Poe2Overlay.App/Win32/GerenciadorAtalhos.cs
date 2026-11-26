using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;

namespace Poe2Overlay.App.Win32;

// Atalhos globais via RegisterHotKey, recebidos numa janela invisível só de mensagens.
// Texto no formato "Ctrl+Shift+Space", "Ctrl+D", "F8", "Alt+1".
internal sealed class GerenciadorAtalhos : IDisposable
{
    readonly HwndSource _janela;
    readonly Dictionary<int, Action> _acoes = [];

    public GerenciadorAtalhos()
    {
        _janela = new HwndSource(new HwndSourceParameters("Poe2Overlay.Atalhos")
        {
            ParentWindow = Nativo.HWND_MESSAGE,
            WindowStyle = 0,
        });
        _janela.AddHook(Mensagem);
    }

    public bool Registrar(int id, string atalho, Action acao)
    {
        Remover(id);
        if (!Interpretar(atalho, out var mods, out var vk)) return false;
        if (!Nativo.RegisterHotKey(_janela.Handle, id, mods | Nativo.MOD_NOREPEAT, vk)) return false;
        _acoes[id] = acao;
        return true;
    }

    public void Remover(int id)
    {
        if (_acoes.Remove(id)) Nativo.UnregisterHotKey(_janela.Handle, id);
    }

    public static bool Interpretar(string texto, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        var partes = texto.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0) return false;
        for (int i = 0; i < partes.Length - 1; i++)
        {
            switch (partes[i].ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= Nativo.MOD_CONTROL; break;
                case "shift": mods |= Nativo.MOD_SHIFT; break;
                case "alt": mods |= Nativo.MOD_ALT; break;
                case "win": mods |= Nativo.MOD_WIN; break;
                default: return false;
            }
        }
        var tecla = partes[^1];
        if (tecla.Length == 1 && char.IsDigit(tecla[0])) tecla = "D" + tecla;
        else if (tecla.Equals("esc", StringComparison.OrdinalIgnoreCase)) tecla = "Escape";
        if (int.TryParse(tecla, out _) || !Enum.TryParse<Key>(tecla, true, out var key)) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    // Por que um atalho não serve (ou null se serve).
    public static string? Problema(string atalho)
    {
        if (!Interpretar(atalho, out var mods, out var vk)) return "não reconheci essa combinação.";
        if (vk == 0x1B) return "Esc é reservado para fechar o overlay.";
        // Letra/número/espaço sem Ctrl, Alt ou Win atrapalharia digitar no chat do jogo.
        bool tecladoDeTexto = vk is >= 0x30 and <= 0x5A or 0x20 or 0x0D or 0x09;
        if (tecladoDeTexto && (mods & (Nativo.MOD_CONTROL | Nativo.MOD_ALT | Nativo.MOD_WIN)) == 0)
            return "use Ctrl, Alt ou Win junto com letras, números ou espaço (senão atrapalha o chat do jogo).";
        return null;
    }

    public static bool Iguais(string a, string b) =>
        Interpretar(a, out var ma, out var va) && Interpretar(b, out var mb, out var vb) && ma == mb && va == vb;

    IntPtr Mensagem(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Nativo.WM_HOTKEY && _acoes.TryGetValue(wParam.ToInt32(), out var acao))
        {
            handled = true;
            acao();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _acoes.Keys) Nativo.UnregisterHotKey(_janela.Handle, id);
        _acoes.Clear();
        _janela.Dispose();
    }
}
