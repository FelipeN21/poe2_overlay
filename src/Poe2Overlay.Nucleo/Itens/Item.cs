namespace Poe2Overlay.Nucleo.Itens;

public enum Raridade { Desconhecida, Normal, Magico, Raro, Unico, Moeda, Gema }

// Uma linha de mod como veio no Ctrl+C, já sem o sufixo "(implicit)" e sem as faixas da cópia avançada.
// Tipo: explicit, implicit, enchant, rune, fractured, crafted, desecrated (os grupos de stats do trade).
public sealed record LinhaMod(string Texto, string Tipo);

public sealed record FaixaDano(double Min, double Max)
{
    public double Media => (Min + Max) / 2;
}

// Dano de arma como o jogo mostra (já com qualidade e mods locais aplicados).
public sealed record DanoArma(FaixaDano? Fisico, IReadOnlyList<FaixaDano> Elemental, FaixaDano? Caos,
    double AtaquesPorSegundo, int Qualidade)
{
    public double DpsFisico => (Fisico?.Media ?? 0) * AtaquesPorSegundo;
    public double DpsElemental => Elemental.Sum(f => f.Media) * AtaquesPorSegundo;
    public double DpsCaos => (Caos?.Media ?? 0) * AtaquesPorSegundo;
    public double Dps => DpsFisico + DpsElemental + DpsCaos;

    // O trade compara DPS físico como se a arma tivesse 20% de qualidade (ou a qualidade dela, se for maior).
    public double DpsFisicoQ20 => DpsFisico / (1 + Qualidade / 100.0) * (1 + Math.Max(Qualidade, 20) / 100.0);
    public double DpsQ20 => DpsFisicoQ20 + DpsElemental + DpsCaos;
}

// Um item como o jogo descreve no Ctrl+C.
public sealed record Item
{
    public required string Classe { get; init; }
    public required Raridade Raridade { get; init; }
    public required string Nome { get; init; }

    // Segunda linha do nome (raros e únicos identificados). Nulo quando o jogo só mostra uma linha.
    public string? Base { get; init; }

    public int? NivelItem { get; init; }
    public int? Pilha { get; init; }
    public int? Qualidade { get; init; }
    public bool Corrompido { get; init; }
    public bool DuasVezesCorrompido { get; init; }
    public bool NaoIdentificado { get; init; }

    // Só em armas de ataque (tem "Attacks per Second").
    public DanoArma? Arma { get; init; }

    // Linhas depois do "Item Level". Podem incluir texto de ambientação dos únicos: quem decide o que é mod
    // de verdade é o CasadorMods (só o que existe na tabela de stats do trade).
    public IReadOnlyList<LinhaMod> Mods { get; init; } = [];

    public required string TextoOriginal { get; init; }
}
