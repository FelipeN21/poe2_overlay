using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace Poe2Overlay.App;

// Esquema de cores do overlay (escolhido nas configurações). As cores de raridade não mudam: são as do jogo.
public sealed class EsquemaCores(string id, string nome, string fundo, string borda, string destaque, string texto,
    string secundario, string detalhe, string mod, string modSecundario, string dica)
{
    public string Id => id;
    public string Nome => nome;
    public Brush Fundo { get; } = Cor(fundo);
    public Brush Borda { get; } = Cor(borda);
    public Brush Destaque { get; } = Cor(destaque);   // título sem raridade, DPS, filtros de DPS
    public Brush Texto { get; } = Cor(texto);         // preço e avisos neutros
    public Brush Secundario { get; } = Cor(secundario); // base do item
    public Brush Detalhe { get; } = Cor(detalhe);     // classe/nível, anúncios, "mín"/"máx"
    public Brush Mod { get; } = Cor(mod);
    public Brush ModSecundario { get; } = Cor(modSecundario); // implicit, enchant...
    public Brush Dica { get; } = Cor(dica);

    public Brush Separador { get; } = ComAlfa(borda, 0x40);

    // Fundo com a opacidade escolhida nas configurações.
    public Brush FundoCom(double opacidade)
    {
        var c = ((SolidColorBrush)Fundo).Color;
        var b = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(opacidade, 0, 1) * 255), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    static SolidColorBrush Cor(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    static SolidColorBrush ComAlfa(string hex, byte alfa)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(Color.FromArgb(alfa, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}

public static class EsquemasCores
{
    public static readonly IReadOnlyList<EsquemaCores> Todos =
    [
        new("dourado", "Dourado (padrão)", "#F2141216", "#8C7A4E", "#E8C46A", "#E8E2D4", "#D8D0C0", "#9A9488", "#9CA8F0", "#8A8478", "#6E6A62"),
        new("gelo", "Azul gelo", "#F20E141C", "#3F6E96", "#8FD0F5", "#E2EAF2", "#C8D6E4", "#8698AA", "#A8C8FF", "#7C8C9C", "#5E6E7E"),
        new("esmeralda", "Esmeralda", "#F20D1511", "#3C7A57", "#86DDA8", "#E0EEE6", "#C4DACD", "#88A294", "#A6D8C0", "#7A9284", "#5A7266"),
        new("carmesim", "Carmesim", "#F2170E10", "#8E3B3E", "#F29A8E", "#F0E2E0", "#DCC8C4", "#A88E8A", "#F0B4A8", "#94807C", "#76605C"),
        new("grafite", "Grafite", "#F2121212", "#555555", "#F0F0F0", "#E6E6E6", "#CFCFCF", "#9A9A9A", "#B8C4FF", "#8A8A8A", "#6A6A6A"),
    ];

    public const string Padrao = "dourado";

    // Mais transparente que isso o texto fica difícil de ler por cima do jogo.
    public const double OpacidadeMinima = 0.3;

    public static EsquemaCores Obter(string? id) => Todos.FirstOrDefault(e => e.Id == id) ?? Todos[0];
}
