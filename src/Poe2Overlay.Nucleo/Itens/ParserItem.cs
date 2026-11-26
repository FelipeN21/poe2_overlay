using System.Text.RegularExpressions;

namespace Poe2Overlay.Nucleo.Itens;

// Lê o texto que o PoE2 coloca na área de transferência (Ctrl+C / Ctrl+Alt+C sobre um item).
// Só cliente em inglês por enquanto: o texto muda com o idioma.
public static partial class ParserItem
{
    const string Separador = "--------";

    static readonly HashSet<string> Marcadores =
        ["Corrupted", "Unidentified", "Mirrored", "Sanctified", "Split", "Unmodifiable", "Twice Corrupted"];

    public static bool PareceItem(string? texto) =>
        texto is not null && texto.TrimStart().StartsWith("Item Class: ", StringComparison.Ordinal);

    public static Item? Interpretar(string? texto)
    {
        if (!PareceItem(texto)) return null;

        var secoes = new List<List<string>>();
        var atual = new List<string>();
        foreach (var bruta in texto!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var linha = bruta.Trim();
            if (linha == Separador) { secoes.Add(atual); atual = []; }
            else if (linha.Length > 0) atual.Add(linha);
        }
        secoes.Add(atual);
        secoes.RemoveAll(s => s.Count == 0);

        var cabecalho = secoes[0];
        var nomes = cabecalho.Where(l => !l.StartsWith("Item Class: ") && !l.StartsWith("Rarity: ")).ToList();
        if (nomes.Count == 0) return null;

        var resto = secoes.Skip(1).SelectMany(s => s).ToList();
        return new Item
        {
            Classe = Valor(cabecalho, "Item Class: ") ?? "",
            Raridade = LerRaridade(Valor(cabecalho, "Rarity: ")),
            Nome = nomes[0],
            Base = nomes.Count > 1 ? nomes[1] : null,
            NivelItem = Numero(resto, ReNivel()),
            Pilha = Numero(resto, RePilha()),
            Qualidade = Numero(resto, ReQualidade()),
            Corrompido = resto.Contains("Corrupted") || resto.Contains("Twice Corrupted"),
            DuasVezesCorrompido = resto.Contains("Twice Corrupted"),
            NaoIdentificado = resto.Contains("Unidentified"),
            Arma = LerArma(resto),
            Mods = LerMods(secoes),
            TextoOriginal = texto,
        };
    }

    // "Physical Damage: 113-153 (augmented)", "Elemental Damage: 43-73 (augmented), 37-52 (augmented)",
    // "Cold Damage: 42-50" (um elemento só), "Chaos Damage: ...", "Attacks per Second: 1.10".
    static DanoArma? LerArma(List<string> linhas)
    {
        var aps = linhas.Select(l => ReAtaques().Match(l)).FirstOrDefault(m => m.Success);
        if (aps is null) return null;

        FaixaDano? fisico = null, caos = null;
        var elemental = new List<FaixaDano>();
        foreach (var l in linhas)
        {
            var m = ReDano().Match(l);
            if (!m.Success) continue;
            var faixas = ReFaixaDano().Matches(m.Groups[2].Value)
                .Select(f => new FaixaDano(Num(f.Groups[1].Value), Num(f.Groups[2].Value))).ToList();
            switch (m.Groups[1].Value)
            {
                case "Physical": fisico = faixas.FirstOrDefault(); break;
                case "Chaos": caos = faixas.FirstOrDefault(); break;
                default: elemental.AddRange(faixas); break; // Elemental, Fire, Cold, Lightning
            }
        }
        var qualidade = Numero(linhas, ReQualidade()) ?? 0;
        return new DanoArma(fisico, elemental, caos, Num(aps.Groups[1].Value), qualidade);
    }

    static double Num(string s) => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    // Mods ficam nas seções depois da que tem "Item Level:".
    static List<LinhaMod> LerMods(List<List<string>> secoes)
    {
        var inicio = secoes.FindIndex(s => s.Any(l => l.StartsWith("Item Level: ", StringComparison.Ordinal)));
        var mods = new List<LinhaMod>();
        if (inicio < 0) return mods;

        foreach (var secao in secoes.Skip(inicio + 1))
        {
            string? tipoDoBloco = null; // cópia avançada: "{ Implicit Modifier }", "{ Prefix Modifier ... }"
            foreach (var linha in secao)
            {
                if (linha.StartsWith('{') && linha.EndsWith('}'))
                {
                    tipoDoBloco = TipoDoCabecalho(linha);
                    continue;
                }
                if (Marcadores.Contains(linha) || linha.StartsWith("Note: ", StringComparison.Ordinal)) continue;
                if (linha.StartsWith('(') && linha.EndsWith(')')) continue; // texto explicativo da cópia avançada

                // "+10(10-15) to Intelligence" → "+10 to Intelligence";
                // "Allocates Jack of all Trades — Unscalable Value" → "Allocates Jack of all Trades".
                var texto = ReAnexo().Replace(ReFaixa().Replace(linha, ""), "");
                var tipo = tipoDoBloco ?? "explicit";
                var sufixo = ReSufixo().Match(texto);
                if (sufixo.Success)
                {
                    tipo = sufixo.Groups[1].Value == "augment" ? "rune" : sufixo.Groups[1].Value;
                    texto = texto[..sufixo.Index];
                }
                mods.Add(new LinhaMod(texto.Trim(), tipo));
            }
        }
        return mods;
    }

    // Cabeçalhos reais do PoE2 (cópia avançada): "{ Enhancement }", "{ Corruption Enhancement — Attribute }" (enchants),
    // "{ Implicit Modifier — ... }", "{ Unique Modifier — ... }", "{ Prefix Modifier "Nome" (Tier: 1) — ... }".
    static string TipoDoCabecalho(string cabecalho) =>
        cabecalho.Contains("Enhancement") || cabecalho.Contains("Enchant") ? "enchant"
        : cabecalho.Contains("Implicit") ? "implicit"
        : cabecalho.Contains("Rune") || cabecalho.Contains("Augment") ? "rune"
        : cabecalho.Contains("Desecrated") ? "desecrated"
        : cabecalho.Contains("Fractured") ? "fractured"
        : cabecalho.Contains("Crafted") ? "crafted"
        : "explicit";

    static string? Valor(List<string> linhas, string prefixo) =>
        linhas.FirstOrDefault(l => l.StartsWith(prefixo, StringComparison.Ordinal))?[prefixo.Length..];

    static Raridade LerRaridade(string? texto) => texto switch
    {
        "Normal" => Raridade.Normal,
        "Magic" => Raridade.Magico,
        "Rare" => Raridade.Raro,
        "Unique" => Raridade.Unico,
        "Currency" => Raridade.Moeda,
        "Gem" => Raridade.Gema,
        _ => Raridade.Desconhecida,
    };

    static int? Numero(List<string> linhas, Regex re)
    {
        foreach (var l in linhas)
        {
            var m = re.Match(l);
            if (m.Success && int.TryParse(m.Groups[1].Value.Replace(",", "").Replace(".", ""), out var n)) return n;
        }
        return null;
    }

    [GeneratedRegex(@"^Item Level: (\d+)")] private static partial Regex ReNivel();
    [GeneratedRegex(@"^Stack Size: ([\d,\.]+)/")] private static partial Regex RePilha();
    // "Quality: +20%" ou "Quality (Attribute Modifiers): +20%".
    [GeneratedRegex(@"^Quality(?: \([^)]*\))?: \+(\d+)%")] private static partial Regex ReQualidade();

    // Anexo da cópia avançada no fim da linha: " — Unscalable Value".
    [GeneratedRegex(@"\s+—\s.*$")] private static partial Regex ReAnexo();
    [GeneratedRegex(@"^Attacks per Second: ([\d.]+)")] private static partial Regex ReAtaques();
    [GeneratedRegex(@"^(Physical|Elemental|Fire|Cold|Lightning|Chaos) Damage: (.+)$")] private static partial Regex ReDano();
    [GeneratedRegex(@"(\d+(?:\.\d+)?)-(\d+(?:\.\d+)?)")] private static partial Regex ReFaixaDano();

    // "+112(100-119)" → "+112" (faixa do tier na cópia avançada).
    [GeneratedRegex(@"\(-?\d+(?:\.\d+)?--?\d+(?:\.\d+)?\)")] private static partial Regex ReFaixa();

    [GeneratedRegex(@"\s*\((implicit|enchant|rune|augment|crafted|fractured|desecrated)\)$")]
    private static partial Regex ReSufixo();
}
