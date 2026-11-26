using System.Text.Json.Nodes;
using Poe2Overlay.Nucleo.Dados;
using Poe2Overlay.Nucleo.Itens;

namespace Poe2Overlay.Nucleo.Busca;

// Um mod na busca: o usuário liga/desliga e ajusta mín/máx no overlay. Começa com a margem aplicada.
public sealed class FiltroMod(ModCasado mod, bool ativo, double? min, double? max)
{
    public ModCasado Mod => mod;
    public bool Ativo { get; set; } = ativo;
    public double? Min { get; set; } = min;
    public double? Max { get; set; } = max;
}

// Filtro de propriedade do item (por enquanto, DPS de arma): equipment_filters do trade.
// Valor já na conta do trade (físico com 20% de qualidade); Min começa em Valor × margem, sem máximo.
public sealed class FiltroPropriedade(string id, string nome, double valor, double min, bool ativo)
{
    public string Id => id;        // dps, pdps, edps
    public string Nome => nome;
    public double Valor => valor;
    public double? Min { get; set; } = min;
    public double? Max { get; set; }
    public bool Ativo { get; set; } = ativo;
}

// O que vai ser buscado para um item (antes de escolher liga e moeda).
public sealed class BuscaItem
{
    public required Item Item { get; init; }
    public string? Nome { get; init; }
    public string? Tipo { get; init; }
    public string? Raridade { get; init; }      // filtro rarity do trade: unique, nonunique, normal
    public List<FiltroMod> Mods { get; init; } = [];
    public List<FiltroPropriedade> Propriedades { get; init; } = [];

    // Filtro "identified" do trade: null = qualquer, "true" = só identificados, "false" = só não identificados.
    // Começa em "false" se o item consultado está não identificado. Com "false" os mods não entram na busca
    // (item não identificado não mostra mods no trade).
    public string? Identificado { get; set; }

    // Corrupção: null = qualquer, "sim" (corrupted), "duas" (twice corrupted), "nao" (não corrompido).
    // Começa igual ao item consultado: corrupção (principalmente 2×) muda muito o preço de um único.
    public string? Corrupcao { get; set; }
    public string? NaoSuportada { get; init; }  // motivo, quando o overlay ainda não sabe buscar esse item
}

public static class MontadorBusca
{
    // Mods que entram ligados por padrão em raros/mágicos.
    static readonly HashSet<string> TiposLigados = ["explicit", "fractured", "desecrated", "crafted"];

    public static BuscaItem Preparar(Item item, BaseStats stats, BaseItens bases, double margem = 0.9)
    {
        var busca = PrepararSemArma(item, stats, bases, margem);
        if (item.NaoIdentificado) busca.Identificado = "false";
        busca.Corrupcao = item.DuasVezesCorrompido ? "duas" : item.Corrompido ? "sim" : null;
        if (item.Arma is not { } arma || busca.NaoSuportada is not null) return busca;

        // Arma: DPS como filtros opcionais (desligados por padrão), na mesma conta que o trade usa.
        FiltroPropriedade Dps(string id, string nome, double valor) => new(id, nome, valor, Math.Floor(valor * margem), false);
        var props = new List<FiltroPropriedade> { Dps("dps", "DPS total", arma.DpsQ20) };
        if (arma.DpsFisico > 0) props.Add(Dps("pdps", "DPS físico", arma.DpsFisicoQ20));
        if (arma.DpsElemental > 0) props.Add(Dps("edps", "DPS elemental", arma.DpsElemental));
        busca.Propriedades.AddRange(props);
        return busca;
    }

    static BuscaItem PrepararSemArma(Item item, BaseStats stats, BaseItens bases, double margem)
    {
        var mods = CasadorMods.Casar(item, stats);
        List<FiltroMod> Filtros(bool ligarPorTipo) =>
            mods.Select(m => CriarFiltro(m, ligarPorTipo && TiposLigados.Contains(m.Tipo), margem)).ToList();

        switch (item.Raridade)
        {
            case Raridade.Moeda:
                return new BuscaItem { Item = item, NaoSuportada = "Moedas ainda não: o preço delas vem do mercado de troca (próximo passo)." };

            case Raridade.Unico:
                return item.NaoIdentificado || item.Base is null
                    ? new BuscaItem { Item = item, Tipo = item.Base ?? item.Nome, Raridade = "unique" }
                    : new BuscaItem { Item = item, Nome = item.Nome, Tipo = item.Base, Raridade = "unique", Mods = Filtros(false) };

            case Raridade.Raro:
                return new BuscaItem { Item = item, Tipo = item.Base ?? item.Nome, Raridade = "nonunique", Mods = Filtros(true) };

            case Raridade.Magico:
            case Raridade.Normal:
                var tipo = bases.Existe(item.Nome) ? item.Nome : bases.AcharBase(item.Nome);
                if (tipo is null) return new BuscaItem { Item = item, NaoSuportada = "Não reconheci a base deste item." };
                return item.Raridade == Raridade.Normal
                    ? new BuscaItem { Item = item, Tipo = tipo, Raridade = "normal" }
                    : new BuscaItem { Item = item, Tipo = tipo, Raridade = "nonunique", Mods = Filtros(true) };

            default:
                return new BuscaItem { Item = item, Tipo = item.Base ?? item.Nome };
        }
    }

    static FiltroMod CriarFiltro(ModCasado mod, bool ativo, double margem)
    {
        if (mod.Valor is not { } v) return new FiltroMod(mod, ativo, null, null);
        // Itens parecidos, não idênticos: mínimo de 90% do valor (ou máximo, se o valor for negativo).
        return v >= 0
            ? new FiltroMod(mod, ativo, Math.Floor(v * margem), null)
            : new FiltroMod(mod, ativo, null, Math.Ceiling(v * margem));
    }

    // ---- Busca por DPS (armas) ----

    // Mods que o usuário pode escolher considerar junto com o DPS (os que já entram no DPS ficam de fora).
    public static IReadOnlyList<int> CandidatosBuscaPorDps(BuscaItem busca) =>
        busca.Item.Arma is null ? [] :
        Enumerable.Range(0, busca.Mods.Count).Where(i => !CasadorMods.ContribuiParaDps(busca.Mods[i].Mod)).ToList();

    // Liga só o DPS total (na conta do trade) e os mods escolhidos; desliga o resto. Assim armas com DPS parecido
    // aparecem mesmo que os outros mods (inclusive fractured/desecrated) sejam diferentes.
    public static void AplicarBuscaPorDps(BuscaItem busca, IReadOnlyCollection<int> escolhidos)
    {
        for (int i = 0; i < busca.Mods.Count; i++) busca.Mods[i].Ativo = escolhidos.Contains(i);
        foreach (var p in busca.Propriedades) p.Ativo = p.Id == "dps";
    }

    public static JsonObject Montar(BuscaItem busca, string moeda)
    {
        var query = new JsonObject { ["status"] = new JsonObject { ["option"] = "available" } };
        if (busca.Nome is not null) query["name"] = busca.Nome;
        if (busca.Tipo is not null) query["type"] = busca.Tipo;

        var filtros = new JsonArray();
        var modsValem = busca.Identificado != "false";
        foreach (var f in busca.Mods.Where(f => f.Ativo && modsValem))
        {
            var filtro = new JsonObject { ["id"] = f.Mod.IdBusca, ["disabled"] = false };
            if (f.Min is not null || f.Max is not null)
            {
                var valor = new JsonObject();
                if (f.Min is { } min) valor["min"] = min;
                if (f.Max is { } max) valor["max"] = max;
                filtro["value"] = valor;
            }
            filtros.Add(filtro);
        }
        query["stats"] = new JsonArray(new JsonObject { ["type"] = "and", ["filters"] = filtros });

        var grupos = new JsonObject();
        var props = busca.Propriedades.Where(p => p.Ativo).ToList();
        if (props.Count > 0)
        {
            var equipamento = new JsonObject();
            foreach (var p in props)
            {
                var valor = new JsonObject();
                if (p.Min is { } min) valor["min"] = min;
                if (p.Max is { } max) valor["max"] = max;
                equipamento[p.Id] = valor;
            }
            grupos["equipment_filters"] = new JsonObject { ["filters"] = equipamento };
        }
        var misc = new JsonObject();
        if (busca.Identificado is not null) misc["identified"] = new JsonObject { ["option"] = busca.Identificado };
        switch (busca.Corrupcao)
        {
            case "sim":
                misc["corrupted"] = new JsonObject { ["option"] = "true" };
                break;
            case "duas":
                misc["corrupted"] = new JsonObject { ["option"] = "true" };
                misc["twice_corrupted"] = new JsonObject { ["option"] = "true" };
                break;
            case "nao":
                misc["corrupted"] = new JsonObject { ["option"] = "false" };
                break;
        }
        if (misc.Count > 0) grupos["misc_filters"] = new JsonObject { ["filters"] = misc };
        if (busca.Raridade is not null)
            grupos["type_filters"] = new JsonObject
            {
                ["filters"] = new JsonObject { ["rarity"] = new JsonObject { ["option"] = busca.Raridade } },
            };
        // Um anúncio por vendedor. Moeda específica: só anúncios com preço nela. "Qualquer": sem filtro de preço,
        // o site ordena todas as moedas pelo valor equivalente.
        var trade = new JsonObject { ["collapse"] = new JsonObject { ["option"] = "true" } };
        if (moeda != Trade.Moedas.Qualquer) trade["price"] = new JsonObject { ["option"] = moeda };
        grupos["trade_filters"] = new JsonObject { ["filters"] = trade };
        query["filters"] = grupos;

        return new JsonObject { ["query"] = query, ["sort"] = new JsonObject { ["price"] = "asc" } };
    }
}
