using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Poe2Overlay.Nucleo.Trade;

// Cliente da API do site oficial de trade do PoE2 (a mesma que pathofexile.com/trade2 usa).
//   POST api/trade2/search/poe2/{liga}          → id da busca + ids dos anúncios
//   GET  api/trade2/fetch/{ids}?query={id}       → anúncios (até 10 por chamada)
//   GET  api/trade2/data/{stats|items|leagues}   → dados estáticos
public sealed class ClienteTrade
{
    public const string Contato = "https://github.com/FelipeN21";
    public static readonly Uri SiteOficial = new("https://www.pathofexile.com/");

    public static string UserAgent(string versao) => $"Poe2Overlay/{versao} (contact: {Contato})";

    readonly HttpClient _http;

    public Uri Base { get; }
    public LimitadorRequisicoes Limitador { get; }

    // Esperas curtas (fila: ex. 5ª busca em 10 s) acontecem sozinhas; mais que isso (castigo, 429)
    // vira LimiteAtingidoException para o overlay avisar.
    public TimeSpan EsperaMaxima { get; init; } = TimeSpan.FromSeconds(10);

    public ClienteTrade(HttpClient http, string versao, Uri? baseUrl = null, LimitadorRequisicoes? limitador = null)
    {
        _http = http;
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent(versao));
        Base = baseUrl ?? SiteOficial;
        Limitador = limitador ?? new LimitadorRequisicoes();
    }

    public Task<string> DadosAsync(string nome, CancellationToken ct = default) =>
        EnviarAsync("dados", HttpMethod.Get, $"api/trade2/data/{nome}", null, ct);

    public static IReadOnlyList<Liga> LerLigas(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("result").EnumerateArray()
            .Where(l => !l.TryGetProperty("realm", out var r) || r.GetString() == "poe2")
            .Select(l => new Liga(l.GetProperty("id").GetString()!, l.TryGetProperty("text", out var t) ? t.GetString()! : l.GetProperty("id").GetString()!))
            .ToList();
    }

    public async Task<ResultadoBusca> BuscarAsync(string liga, JsonObject consulta, CancellationToken ct = default)
    {
        var json = await EnviarAsync("busca", HttpMethod.Post, $"api/trade2/search/poe2/{Uri.EscapeDataString(liga)}",
            consulta.ToJsonString(), ct);
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var ids = r.TryGetProperty("result", out var lista) ? lista.EnumerateArray().Select(x => x.GetString()!).ToList() : [];
        var total = r.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : ids.Count;
        return new ResultadoBusca(r.GetProperty("id").GetString()!, total, ids);
    }

    public async Task<IReadOnlyList<Anuncio>> DetalharAsync(IReadOnlyList<string> ids, string idBusca, CancellationToken ct = default)
    {
        var anuncios = new List<Anuncio>();
        foreach (var lote in ids.Chunk(10))
        {
            var json = await EnviarAsync("detalhe", HttpMethod.Get,
                $"api/trade2/fetch/{string.Join(',', lote)}?query={Uri.EscapeDataString(idBusca)}", null, ct);
            anuncios.AddRange(LerAnuncios(json));
        }
        return anuncios;
    }

    public static IEnumerable<Anuncio> LerAnuncios(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var lista = new List<Anuncio>();
        foreach (var r in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("listing", out var listing)) continue;
            if (!listing.TryGetProperty("price", out var preco) || preco.ValueKind != JsonValueKind.Object) continue;
            var conta = listing.TryGetProperty("account", out var a) && a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var indexado = listing.TryGetProperty("indexed", out var i)
                && DateTimeOffset.TryParse(i.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : DateTimeOffset.MinValue;
            lista.Add(new Anuncio(preco.GetProperty("amount").GetDecimal(), preco.GetProperty("currency").GetString() ?? "",
                preco.TryGetProperty("type", out var tp) ? tp.GetString() ?? "" : "", conta, indexado));
        }
        return lista;
    }

    public string UrlSite(string liga, string idBusca) =>
        new Uri(Base, $"trade2/search/poe2/{Uri.EscapeDataString(liga)}/{idBusca}").AbsoluteUri; // ToString() desfaz o %20

    async Task<string> EnviarAsync(string chave, HttpMethod metodo, string caminho, string? corpo, CancellationToken ct)
    {
        // Fila: espera (com folga, porque o timer pode acordar uns ms antes) e tenta de novo, até EsperaMaxima no total.
        var esperado = TimeSpan.Zero;
        for (var espera = Limitador.Reservar(chave); espera > TimeSpan.Zero; espera = Limitador.Reservar(chave))
        {
            if (esperado + espera > EsperaMaxima) throw new LimiteAtingidoException(espera);
            var pausa = espera + TimeSpan.FromMilliseconds(50);
            await Task.Delay(pausa, ct);
            esperado += pausa;
        }

        using var req = new HttpRequestMessage(metodo, new Uri(Base, caminho));
        if (corpo is not null) req.Content = new StringContent(corpo, Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, ct);

        var retry = resp.Headers.RetryAfter?.Delta;
        Limitador.Atualizar(chave, nome => resp.Headers.TryGetValues(nome, out var v) ? v.FirstOrDefault() : null,
            (int)resp.StatusCode, retry);
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            throw new LimiteAtingidoException(retry ?? TimeSpan.FromSeconds(60));

        var texto = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new ErroTradeException((int)resp.StatusCode, MensagemDeErro(texto));
        return texto;
    }

    // {"error":{"code":2,"message":"Invalid query"}}
    static string MensagemDeErro(string corpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            return doc.RootElement.GetProperty("error").GetProperty("message").GetString() ?? corpo;
        }
        catch
        {
            return corpo.Length > 120 ? corpo[..120] : corpo;
        }
    }
}
