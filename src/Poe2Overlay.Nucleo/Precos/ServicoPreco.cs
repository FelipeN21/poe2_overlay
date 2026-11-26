using Poe2Overlay.Nucleo.Busca;
using Poe2Overlay.Nucleo.Dados;
using Poe2Overlay.Nucleo.Itens;
using Poe2Overlay.Nucleo.Trade;

namespace Poe2Overlay.Nucleo.Precos;

public sealed record ResultadoPreco(string IdBusca, int Total, IReadOnlyList<Anuncio> Anuncios, ResumoPreco? Resumo, string UrlSite);

// Junta tudo: dados do trade (com cache), preparo da busca, busca + detalhe e resumo.
public sealed class ServicoPreco(ClienteTrade cliente, CacheDados cache)
{
    // Uma chamada de detalhe (10 anúncios) por busca, para economizar o limite.
    const int AnunciosPorBusca = 10;

    readonly SemaphoreSlim _trava = new(1, 1);
    BaseStats? _stats;
    BaseItens? _bases;

    public ClienteTrade Cliente => cliente;

    public async Task<IReadOnlyList<Liga>> LigasAsync(CancellationToken ct = default) =>
        ClienteTrade.LerLigas(await cache.ObterAsync("leagues", TimeSpan.FromHours(6), ct));

    public async Task CarregarDadosAsync(CancellationToken ct = default)
    {
        await _trava.WaitAsync(ct);
        try
        {
            _stats ??= BaseStats.Carregar(await cache.ObterAsync("stats", TimeSpan.FromHours(24), ct));
            _bases ??= BaseItens.Carregar(await cache.ObterAsync("items", TimeSpan.FromHours(24), ct));
        }
        finally
        {
            _trava.Release();
        }
    }

    public async Task<BuscaItem> PrepararAsync(Item item, double margem, CancellationToken ct = default)
    {
        await CarregarDadosAsync(ct);
        return MontadorBusca.Preparar(item, _stats!, _bases!, margem);
    }

    public async Task<ResultadoPreco> BuscarAsync(BuscaItem busca, string liga, string moeda, CancellationToken ct = default)
    {
        var r = await cliente.BuscarAsync(liga, MontadorBusca.Montar(busca, moeda), ct);
        var anuncios = r.Ids.Count == 0 ? [] : await cliente.DetalharAsync(r.Ids.Take(AnunciosPorBusca).ToList(), r.Id, ct);
        return new ResultadoPreco(r.Id, r.Total, anuncios, AgregadorPreco.Resumir(anuncios, moeda), cliente.UrlSite(liga, r.Id));
    }
}
