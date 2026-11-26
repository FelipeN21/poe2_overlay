using System.Globalization;
using System.Text.RegularExpressions;
using Poe2Overlay.Nucleo.Dados;
using Poe2Overlay.Nucleo.Itens;

namespace Poe2Overlay.Nucleo.Busca;

// Um mod do item reconhecido na tabela do trade. Valor: o número do mod (média, se tiver dois: "Adds 1 to 4").
// IdBusca: o id usado na busca. Mods fractured/desecrated são buscados como explicit: no trade o filtro explicit
// também encontra mods fractured e desecrated (conferido na API em 26/11/2026), e assim a busca não exige que o
// outro item tenha o mod com a mesma origem (o que deixava a busca restritiva demais).
public sealed record ModCasado(string IdStat, string Texto, string Tipo, double? Valor)
{
    public string IdBusca { get; init; } = IdStat;
}

// Liga as linhas de mod do Ctrl+C aos ids de stat do trade.
public static partial class CasadorMods
{
    // Nessas classes não existe mod "local": o mod vale para o personagem.
    static readonly HashSet<string> ClassesSemLocal =
        ["Rings", "Amulets", "Belts", "Jewels", "Charms", "Life Flasks", "Mana Flasks", "Flasks", "Relics"];

    public static List<ModCasado> Casar(Item item, BaseStats stats)
    {
        var local = !ClassesSemLocal.Contains(item.Classe);
        var casados = new List<ModCasado>();
        var linhas = item.Mods;
        for (int i = 0; i < linhas.Count; i++)
        {
            var achado = CasarLinha(linhas[i].Texto, linhas[i].Tipo, local, stats);
            // Alguns stats ocupam duas linhas no jogo (e no trade vêm com "\n").
            if (achado is null && i + 1 < linhas.Count && linhas[i + 1].Tipo == linhas[i].Tipo)
            {
                achado = CasarLinha(linhas[i].Texto + "\n" + linhas[i + 1].Texto, linhas[i].Tipo, local, stats);
                if (achado is not null) i++;
            }
            if (achado is not null) casados.Add(achado);
        }
        return casados;
    }

    static ModCasado? CasarLinha(string texto, string tipo, bool local, BaseStats stats)
    {
        var (modelo, valores) = Modelo(texto);
        var entrada = Procurar(stats, tipo, modelo, local);
        var sinal = 1.0;
        if (entrada is null)
        {
            // "10% reduced X" costuma existir no trade só como "#% increased X" com valor negativo.
            var invertido = Inverter(modelo);
            if (invertido is not null && (entrada = Procurar(stats, tipo, invertido, local)) is not null) sinal = -1;
        }
        if (entrada is null) return null;
        double? valor = valores.Count == 0 ? null : valores.Average() * sinal;
        var idBusca = entrada.Id;
        if (tipo is "fractured" or "desecrated")
        {
            var explicito = "explicit" + entrada.Id[entrada.Id.IndexOf('.')..];
            if (stats.Existe(explicito)) idBusca = explicito;
        }
        return new ModCasado(entrada.Id, texto, tipo, valor) { IdBusca = idBusca };
    }

    // Mods que já estão dentro do DPS mostrado pela arma (dano adicionado, % de dano físico, velocidade de ataque).
    // Na busca por DPS eles não precisam ser filtrados um por um: o filtro de DPS já cobre.
    public static bool ContribuiParaDps(ModCasado m) => ReDps().IsMatch(m.Texto);

    [GeneratedRegex(@"^(Adds \d+(\.\d+)? to \d+(\.\d+)? (Physical|Fire|Cold|Lightning|Chaos) Damage|\d+(\.\d+)?% increased (Physical Damage|Attack Speed))$")]
    private static partial Regex ReDps();

    static EntradaStat? Procurar(BaseStats stats, string tipo, string modelo, bool local)
    {
        if (local && stats.Procurar(tipo, modelo + " (Local)") is [var l, ..]) return l;
        return stats.Procurar(tipo, modelo) is [var e, ..] ? e : null;
    }

    static string? Inverter(string modelo) =>
        modelo.Contains(" reduced ") ? modelo.Replace(" reduced ", " increased ")
        : modelo.Contains(" less ") ? modelo.Replace(" less ", " more ")
        : null;

    // "+112 to maximum Life" → ("# to maximum Life", [112]); "-20% to all ..." → ("#% to all ...", [-20]).
    public static (string Modelo, List<double> Valores) Modelo(string texto)
    {
        var valores = new List<double>();
        var modelo = ReNumero().Replace(texto, m =>
        {
            valores.Add(double.Parse(m.Value, CultureInfo.InvariantCulture));
            return "#";
        });
        return (modelo, valores);
    }

    [GeneratedRegex(@"[+-]?\d+(?:\.\d+)?")] private static partial Regex ReNumero();
}
