using System.Text.Json;

namespace Poe2Overlay.Nucleo.Dados;

// Bases de item do trade (/api/trade2/data/items). Serve para achar a base de um item mágico,
// cujo nome mistura base e afixos ("Sapphire Ring of the Lynx" → "Sapphire Ring").
public sealed class BaseItens
{
    readonly List<string> _tipos;

    BaseItens(IEnumerable<string> tipos) =>
        _tipos = tipos.Distinct().OrderByDescending(t => t.Length).ToList(); // a mais longa ganha

    public int Quantidade => _tipos.Count;

    public static BaseItens Carregar(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var tipos = new List<string>();
        foreach (var grupo in doc.RootElement.GetProperty("result").EnumerateArray())
            foreach (var e in grupo.GetProperty("entries").EnumerateArray())
                if (e.TryGetProperty("type", out var t) && t.GetString() is { Length: > 0 } tipo) tipos.Add(tipo);
        return new BaseItens(tipos);
    }

    public bool Existe(string tipo) => _tipos.Contains(tipo);

    public string? AcharBase(string nome)
    {
        foreach (var t in _tipos)
        {
            var i = nome.IndexOf(t, StringComparison.Ordinal);
            if (i < 0) continue;
            bool inicioOk = i == 0 || nome[i - 1] == ' ';
            bool fimOk = i + t.Length == nome.Length || nome[i + t.Length] == ' ';
            if (inicioOk && fimOk) return t;
        }
        return null;
    }
}
