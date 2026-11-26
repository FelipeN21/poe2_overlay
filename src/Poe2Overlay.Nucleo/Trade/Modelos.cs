namespace Poe2Overlay.Nucleo.Trade;

public sealed record Liga(string Id, string Texto);

public static class Ligas
{
    // "Liga do momento": a primeira liga temporária softcore e trade da lista da API
    // (pula Standard, Hardcore, "HC ..." e SSF). Sem nenhuma, Standard.
    public static string Atual(IReadOnlyList<Liga> ligas) =>
        ligas.FirstOrDefault(l => !EhPermanente(l.Id) && !EhHardcore(l.Id) && !l.Id.Contains("SSF", StringComparison.OrdinalIgnoreCase))?.Id
        ?? "Standard";

    static bool EhPermanente(string id) => id is "Standard" or "Hardcore";
    static bool EhHardcore(string id) => id.StartsWith("HC ", StringComparison.Ordinal) || id.Contains("Hardcore");
}

// Moedas do seletor. "qualquer" = sem filtro de preço: o trade ordena todos os anúncios pelo valor equivalente
// (conversão feita pelo próprio site) e cada preço aparece na moeda do anúncio. Com uma moeda específica, só vêm
// anúncios naquela moeda — e itens baratos listados "a 1 divine" por preguiça distorcem o preço.
public static class Moedas
{
    public const string Qualquer = "qualquer";

    public static readonly IReadOnlyList<(string Id, string Nome)> Opcoes =
    [
        (Qualquer, "Qualquer"),
        ("divine", "Divine"),
        ("exalted", "Exalted"),
        ("chaos", "Chaos"),
    ];

    public const string Padrao = Qualquer;

    public static bool Valida(string id) => Opcoes.Any(o => o.Id == id);
}

public sealed record ResultadoBusca(string Id, int Total, IReadOnlyList<string> Ids);

// Um anúncio do trade: preço na moeda em que o vendedor listou.
public sealed record Anuncio(decimal Preco, string Moeda, string TipoPreco, string Conta, DateTimeOffset Indexado);

public sealed class LimiteAtingidoException(TimeSpan espera)
    : Exception($"Limite de buscas do site atingido: aguarde {Math.Max(1, Math.Ceiling(espera.TotalSeconds))} s.")
{
    public TimeSpan Espera => espera;
}

public sealed class ErroTradeException(int status, string mensagem)
    : Exception($"O site de trade respondeu {status}: {mensagem}")
{
    public int Status => status;
}
