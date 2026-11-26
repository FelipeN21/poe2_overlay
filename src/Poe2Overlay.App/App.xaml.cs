using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Poe2Overlay.App.Win32;
using Poe2Overlay.Nucleo.Dados;
using Poe2Overlay.Nucleo.Itens;
using Poe2Overlay.Nucleo.Precos;
using Poe2Overlay.Nucleo.Trade;

namespace Poe2Overlay.App;

// Liga tudo: configuração, janela, atalhos globais, bandeja e (nos testes) o canal do harness.
public partial class AppOverlay : Application
{
    const int IdConsultar = 1, IdAlternar = 2, IdFechar = 3;

    public static string Versao => typeof(AppOverlay).Assembly.GetName().Version?.ToString() ?? "?";

    // Nome que o usuário vê (o técnico, de exe/pastas/código, continua Poe2Overlay).
    public const string Nome = "Pit Overlay's";

    internal Configuracao Config { get; private set; } = new();
    internal JanelaOverlay Janela { get; private set; } = null!;
    internal ControladorPreco Preco { get; private set; } = null!;
    internal bool AtalhoConsultarOk { get; private set; }
    internal bool AtalhoAlternarOk { get; private set; }

    bool _harness;
    Mutex? _instancia;
    GerenciadorAtalhos? _atalhos;
    Bandeja? _bandeja;
    ModoHarness? _modoHarness;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = Argumentos.Ler(e.Args);
        _harness = args.PipeHarness is not null;

        DispatcherUnhandledException += (_, ev) => { RegistrarErro(ev.Exception); ev.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => RegistrarErro(ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) => { RegistrarErro(ev.Exception); ev.SetObserved(); };

        if (!_harness)
        {
            _instancia = new Mutex(true, "Poe2Overlay.InstanciaUnica", out bool nova);
            if (!nova)
            {
                MessageBox.Show($"O {Nome} já está aberto (veja o ícone perto do relógio).", Nome);
                Shutdown();
                return;
            }
        }

        Config = Configuracao.Carregar(args.PastaConfig, msg => RegistrarErro(new Exception(msg)));
        Janela = new JanelaOverlay(Config, args.ForaDaTela);

        // O harness aponta para um trade falso local (--trade-url); senão, o site oficial.
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var cliente = new ClienteTrade(http, Versao, args.TradeUrl is null ? null : new Uri(args.TradeUrl));
        var servico = new ServicoPreco(cliente, new CacheDados(Path.Combine(Config.Pasta, "cache"), cliente));
        Preco = new ControladorPreco(servico, Janela, Config, RegistrarErro);

        _foraDaTela = args.ForaDaTela;
        _atalhos = new GerenciadorAtalhos();
        RegistrarAtalhos();

        // Esc fecha o overlay, mas só fica "preso" enquanto ele está na tela (fora dos testes).
        Janela.IsVisibleChanged += (_, _) => RegistrarEsc();

        Janela.AbrirConfiguracoes += AbrirConfiguracoes;
        Janela.PosicaoArrastada += (x, y) =>
        {
            Config.PosicaoX = x;
            Config.PosicaoY = y;
            Config.Salvar();
        };

        if (_harness)
        {
            _modoHarness = new ModoHarness(this, args.PipeHarness!);
            _modoHarness.Iniciar();
            return;
        }

        _bandeja = new Bandeja(this);
        var falhas = new List<string>();
        if (!AtalhoConsultarOk) falhas.Add(Config.AtalhoConsultar);
        if (!AtalhoAlternarOk) falhas.Add(Config.AtalhoAlternar);
        if (falhas.Count > 0)
            _bandeja.Avisar("Atalho indisponível",
                $"Não consegui registrar {string.Join(" e ", falhas)}. Outro programa usa? Troque em Configurações (ícone da bandeja).");
        else
            _bandeja.Informar($"{Nome} rodando",
                $"{Config.AtalhoConsultar} sobre um item consulta · {Config.AtalhoAlternar} mostra/oculta");
    }

    // ---- Atalhos e configurações ----

    bool _foraDaTela;
    internal JanelaConfiguracoes? Configuracoes { get; private set; }

    void RegistrarAtalhos()
    {
        AtalhoConsultarOk = _atalhos!.Registrar(IdConsultar, Config.AtalhoConsultar, () => _ = ConsultarAsync());
        AtalhoAlternarOk = _atalhos.Registrar(IdAlternar, Config.AtalhoAlternar, Alternar);
        RegistrarEsc();
    }

    void RegistrarEsc()
    {
        if (_harness || Configuracoes is not null) return;
        if (Janela.IsVisible) _atalhos!.Registrar(IdFechar, "Escape", Esconder);
        else _atalhos!.Remover(IdFechar);
    }

    // Enquanto as configurações estão abertas os atalhos ficam soltos: assim dá para "apertar" o atalho novo
    // no campo sem disparar o antigo.
    void SuspenderAtalhos()
    {
        _atalhos!.Remover(IdConsultar);
        _atalhos.Remover(IdAlternar);
        _atalhos.Remover(IdFechar);
    }

    internal void AbrirConfiguracoes()
    {
        if (Configuracoes is { } aberta)
        {
            aberta.Activate();
            return;
        }
        Janela.DevolverTeclado();
        SuspenderAtalhos();
        Configuracoes = new JanelaConfiguracoes(Config, Preco.Ligas, AplicarConfiguracoes, _foraDaTela);
        Configuracoes.Closed += (_, _) =>
        {
            Configuracoes = null;
            RegistrarAtalhos();
        };
        Configuracoes.Show();
        Configuracoes.Activate();
    }

    // Valida, testa se o Windows deixa registrar os atalhos novos e aplica tudo. Devolve o erro (ou null).
    string? AplicarConfiguracoes(DadosConfiguracoes d)
    {
        foreach (var atalho in new[] { d.AtalhoConsultar, d.AtalhoAlternar })
            if (GerenciadorAtalhos.Problema(atalho) is { } problema) return $"Atalho \"{atalho}\": {problema}";
        if (GerenciadorAtalhos.Iguais(d.AtalhoConsultar, d.AtalhoAlternar)) return "Os dois atalhos são iguais.";

        // Outro programa pode já usar a combinação: testa registrando de verdade (e solta de novo).
        foreach (var (id, atalho) in new[] { (IdConsultar, d.AtalhoConsultar), (IdAlternar, d.AtalhoAlternar) })
        {
            var livre = _atalhos!.Registrar(id, atalho, () => { });
            _atalhos.Remover(id);
            if (!livre) return $"O atalho {atalho} já é usado por outro programa. Escolha outro.";
        }

        Config.AtalhoConsultar = d.AtalhoConsultar;
        Config.AtalhoAlternar = d.AtalhoAlternar;
        Config.Esquema = EsquemasCores.Obter(d.Esquema).Id;
        Config.AbrirOverlay = d.AbrirOverlay;
        Config.BuscaAutomatica = d.BuscaAutomatica;
        Config.CopiaAvancada = d.CopiaAvancada;
        Config.OpacidadeFundo = Math.Clamp(d.OpacidadeFundo, EsquemasCores.OpacidadeMinima, 1);
        Config.Salvar();
        Janela.AplicarEsquema(EsquemasCores.Obter(Config.Esquema), Config.OpacidadeFundo);
        Janela.AtualizarTextos();
        _ = Preco.DefinirLigaPadraoAsync(d.Liga);
        return null;
    }

    // Bind principal: copia o item sob o cursor e abre o overlay com ele.
    internal async Task ConsultarAsync()
    {
        if (!_harness && Config.ApenasComJogoEmFoco && !Nativo.JogoEmFoco()) return;
        var texto = await CapturaItem.CapturarAsync(Config.CopiaAvancada);
        if (texto is null)
        {
            Janela.MostrarAviso("Não consegui copiar o item", "Passe o mouse sobre o item no jogo e aperte o atalho de novo.");
            return;
        }
        GuardarTextoDoItem(texto);
        MostrarTexto(texto);
    }

    // Guarda o texto copiado do jogo (últimos 20) em %APPDATA%\Poe2Overlay\itens\: quando um item é lido errado,
    // dá para ver exatamente o que o jogo copiou e virar fixture do harness. Fica só no PC do usuário.
    void GuardarTextoDoItem(string texto)
    {
        try
        {
            var pasta = Path.Combine(Config.Pasta, "itens");
            Directory.CreateDirectory(pasta);
            File.WriteAllText(Path.Combine(pasta, $"{DateTime.Now:yyyyMMdd-HHmmss}.txt"), texto);
            foreach (var velho in new DirectoryInfo(pasta).GetFiles("*.txt").OrderByDescending(f => f.Name).Skip(20)) velho.Delete();
        }
        catch (Exception ex)
        {
            RegistrarErro(ex);
        }
    }

    internal void MostrarTexto(string texto)
    {
        var item = ParserItem.Interpretar(texto);
        if (item is null) Janela.MostrarAviso("Isso não parece um item do PoE2", "O cliente do jogo precisa estar em inglês por enquanto.");
        else _ = Preco.NovoItemAsync(item);
    }

    internal void Alternar() => Janela.Alternar();
    internal void Mostrar() => Janela.Mostrar();
    internal void Esconder() => Janela.Esconder();

    internal void Sair()
    {
        _modoHarness?.Dispose();
        _atalhos?.Dispose();
        _bandeja?.Dispose();
        _instancia?.Dispose();
        Shutdown();
    }

    internal void RegistrarErro(Exception? ex)
    {
        var texto = ex?.ToString() ?? "erro desconhecido";
        Console.Error.WriteLine(texto.Replace(Environment.NewLine, " ⏎ "));
        try { File.AppendAllText(Path.Combine(Config.Pasta, "erros.log"), $"[{DateTime.Now:s}] {texto}{Environment.NewLine}"); }
        catch { /* sem onde registrar */ }
    }
}

internal sealed record Argumentos(string? PipeHarness, string? PastaConfig, bool ForaDaTela, string? TradeUrl)
{
    public static Argumentos Ler(string[] a)
    {
        string? Valor(string nome)
        {
            var i = Array.IndexOf(a, nome);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
        return new(Valor("--harness"), Valor("--config"), Array.IndexOf(a, "--fora-da-tela") >= 0, Valor("--trade-url"));
    }
}
