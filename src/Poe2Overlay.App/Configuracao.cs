using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Poe2Overlay.Nucleo.Trade;

namespace Poe2Overlay.App;

// %APPDATA%\Poe2Overlay\config.json (criado com os padrões na primeira execução).
public sealed class Configuracao
{
    public string AtalhoConsultar { get; set; } = "Ctrl+D";
    public string AtalhoAlternar { get; set; } = "Ctrl+Shift+Space";

    // Ctrl+Alt+C: o jogo inclui tiers e faixas dos mods no texto copiado.
    public bool CopiaAvancada { get; set; } = true;

    // Só consulta se a janela em foco for o Path of Exile (evita mandar Ctrl+C para outro programa).
    public bool ApenasComJogoEmFoco { get; set; } = true;

    // Vazio = liga do momento (muda sozinha quando sai liga nova). Trocar no overlay grava aqui.
    public string Liga { get; set; } = "";

    // Moeda do preço (divine, exalted, chaos). Trocar no overlay grava aqui.
    public string Moeda { get; set; } = Moedas.Padrao;

    // Mínimo dos mods na busca = valor do item × margem (0,9 = itens com pelo menos 90% do valor).
    public double MargemMods { get; set; } = 0.9;

    // Esquema de cores do overlay (EsquemasCores: dourado, gelo, esmeralda, carmesim, grafite).
    public string Esquema { get; set; } = EsquemasCores.Padrao;

    // Opacidade do fundo do overlay (1 = opaco; 0,3 = bem transparente). O texto continua sempre nítido.
    public double OpacidadeFundo { get; set; } = 0.95;

    // true: mudar moeda/liga/mod/mín/máx já busca de novo. false: só busca ao clicar em "Buscar".
    public bool BuscaAutomatica { get; set; } = true;

    // Onde o overlay abre num item novo: "cursor" (ao lado do item) ou "fixa" (onde foi arrastado por último).
    public string AbrirOverlay { get; set; } = "cursor";
    public int? PosicaoX { get; set; }
    public int? PosicaoY { get; set; }

    [JsonIgnore] public string Pasta { get; private set; } = PastaPadrao;

    public static string PastaPadrao =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Poe2Overlay");

    // Relaxed: grava "Ctrl+D" em vez de "Ctrl+D" (o arquivo é para ser editado à mão).
    static readonly JsonSerializerOptions Opcoes = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Configuracao Carregar(string? pasta, Action<string> aoFalhar)
    {
        pasta ??= PastaPadrao;
        Directory.CreateDirectory(pasta);
        var arquivo = Path.Combine(pasta, "config.json");
        var cfg = new Configuracao();
        if (File.Exists(arquivo))
        {
            try { cfg = JsonSerializer.Deserialize<Configuracao>(File.ReadAllText(arquivo), Opcoes) ?? new(); }
            catch (Exception ex) { aoFalhar($"config.json inválido, usando o padrão: {ex.Message}"); cfg = new(); }
        }
        if (!Moedas.Valida(cfg.Moeda)) cfg.Moeda = Moedas.Padrao;
        if (cfg.MargemMods is <= 0 or > 1) cfg.MargemMods = 0.9;
        cfg.Esquema = EsquemasCores.Obter(cfg.Esquema).Id;
        cfg.OpacidadeFundo = Math.Clamp(cfg.OpacidadeFundo, EsquemasCores.OpacidadeMinima, 1);
        if (cfg.AbrirOverlay is not ("cursor" or "fixa")) cfg.AbrirOverlay = "cursor";
        cfg.Pasta = pasta;
        if (!File.Exists(arquivo)) cfg.Salvar();
        return cfg;
    }

    public void Salvar() => File.WriteAllText(Path.Combine(Pasta, "config.json"), JsonSerializer.Serialize(this, Opcoes));
}
