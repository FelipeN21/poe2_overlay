using Poe2Overlay.Nucleo.Trade;

namespace Poe2Overlay.Nucleo.Precos;

// Mediana = preço típico; Minimo = o mais barato. Cada um com a sua moeda (com "qualquer moeda" podem diferir).
public sealed record ResumoPreco(int Quantidade, decimal Minimo, decimal Mediana, string Moeda, string? MoedaDoMinimo = null)
{
    public string MoedaMinimo => MoedaDoMinimo ?? Moeda;
}

// Transforma os anúncios mais baratos numa faixa de preço. Nunca converte moeda.
public static class AgregadorPreco
{
    // Abaixo de 30% da mediana é "preço isca" (golpe/erro) e fica de fora.
    const decimal LimiteIsca = 0.3m;

    public static ResumoPreco? Resumir(IReadOnlyList<Anuncio> anuncios, string moeda)
    {
        if (moeda == Moedas.Qualquer) return ResumirQualquerMoeda(anuncios);

        var precos = anuncios.Where(a => a.Moeda == moeda && a.Preco > 0).Select(a => a.Preco).Order().ToList();
        if (precos.Count == 0) return null;
        var mediana = Mediana(precos);
        var limpos = precos.Where(p => p >= mediana * LimiteIsca).ToList();
        return new ResumoPreco(limpos.Count, limpos[0], Mediana(limpos), moeda);
    }

    // Moedas misturadas: os anúncios já vêm ordenados pelo trade (valor equivalente). Então o mais barato é o
    // primeiro e o típico é o do meio da lista — sem converter nada. (Sem conversão não dá para detectar preço isca.)
    static ResumoPreco? ResumirQualquerMoeda(IReadOnlyList<Anuncio> anuncios)
    {
        var validos = anuncios.Where(a => a.Preco > 0 && a.Moeda.Length > 0).ToList();
        if (validos.Count == 0) return null;
        var meio = validos[(validos.Count - 1) / 2];
        return new ResumoPreco(validos.Count, validos[0].Preco, meio.Preco, meio.Moeda, validos[0].Moeda);
    }

    static decimal Mediana(List<decimal> ordenados)
    {
        var m = ordenados.Count / 2;
        return ordenados.Count % 2 == 1 ? ordenados[m] : (ordenados[m - 1] + ordenados[m]) / 2;
    }
}
