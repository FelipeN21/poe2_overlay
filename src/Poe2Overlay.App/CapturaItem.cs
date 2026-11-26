using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Poe2Overlay.App.Win32;
using Poe2Overlay.Nucleo.Itens;

namespace Poe2Overlay.App;

// Manda Ctrl+C (ou Ctrl+Alt+C) para o jogo, espera o texto do item na área de transferência
// e devolve o que estava lá antes (se era texto).
internal static class CapturaItem
{
    public static async Task<string?> CapturarAsync(bool avancada)
    {
        var anterior = LerTexto();
        var sequencia = Nativo.GetClipboardSequenceNumber();
        await Teclado.EnviarCopiaAsync(avancada);

        string? texto = null;
        for (int i = 0; i < 30 && texto is null; i++) // até ~600 ms
        {
            await Task.Delay(20);
            if (Nativo.GetClipboardSequenceNumber() == sequencia) continue;
            var atual = LerTexto();
            if (ParserItem.PareceItem(atual)) texto = atual;
        }

        if (texto is not null && anterior is not null) Tentar(() => Clipboard.SetText(anterior));
        return texto;
    }

    static string? LerTexto()
    {
        string? texto = null;
        Tentar(() => { if (Clipboard.ContainsText()) texto = Clipboard.GetText(); });
        return texto;
    }

    // A área de transferência pode estar presa por outro programa por alguns milissegundos.
    static void Tentar(Action acao)
    {
        for (int i = 0; i < 5; i++)
        {
            try { acao(); return; }
            catch (ExternalException) { Thread.Sleep(10); }
        }
    }
}
