using System.Text.Json;

namespace Poe2Overlay.Nucleo.Dados;

public sealed record EntradaStat(string Id, string Texto, string Grupo);

// Tabela de stats do trade (/api/trade2/data/stats): "# to maximum Life" → "explicit.stat_3299347043".
public sealed class BaseStats
{
    readonly Dictionary<string, List<EntradaStat>> _porChave = [];
    readonly HashSet<string> _ids = [];

    public bool Existe(string id) => _ids.Contains(id);

    public int Quantidade { get; private set; }

    public static BaseStats Carregar(string json)
    {
        var tabela = new BaseStats();
        using var doc = JsonDocument.Parse(json);
        foreach (var grupo in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            foreach (var e in grupo.GetProperty("entries").EnumerateArray())
            {
                var id = e.GetProperty("id").GetString()!;
                var texto = e.GetProperty("text").GetString()!;
                var nomeGrupo = id[..id.IndexOf('.')];
                var chave = Chave(nomeGrupo, Normalizar(texto));
                if (!tabela._porChave.TryGetValue(chave, out var lista)) tabela._porChave[chave] = lista = [];
                lista.Add(new EntradaStat(id, texto, nomeGrupo));
                tabela._ids.Add(id);
                tabela.Quantidade++;
            }
        }
        return tabela;
    }

    public IReadOnlyList<EntradaStat> Procurar(string grupo, string modelo) =>
        _porChave.TryGetValue(Chave(grupo, modelo), out var lista) ? lista : [];

    // Os textos do trade às vezes trazem "+#%"; as linhas do jogo viram "#" sem sinal (o sinal vai para o valor).
    public static string Normalizar(string texto) => texto.Replace("+#", "#").Replace("\r", "");

    static string Chave(string grupo, string modelo) => grupo + "|" + modelo;
}
