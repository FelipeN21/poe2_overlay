using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Poe2Overlay.App.Win32;
using Poe2Overlay.Nucleo.Busca;
using Poe2Overlay.Nucleo.Itens;
using Poe2Overlay.Nucleo.Precos;
using Poe2Overlay.Nucleo.Trade;

namespace Poe2Overlay.App;

// Janela separada por cima do jogo: transparente, sempre no topo e sem roubar o foco do jogo.
// Só mostra; quem decide o que buscar é o ControladorPreco (via eventos).
public partial class JanelaOverlay : Window
{
    static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    readonly Configuracao _config;
    readonly bool _foraDaTela;
    bool _preenchendo; // evita que mudanças feitas pelo código disparem eventos de "usuário trocou"
    bool _boasVindas;

    // Canto superior esquerdo (px). Definido ao abrir para um item novo; depois a janela fica parada
    // (clicar em moeda/mod não a faz "fugir" do cursor) e só se ajusta para não sair da tela.
    int? _x, _y;

    public event Action<string>? LigaEscolhida;
    public event Action<string>? MoedaEscolhida;
    public event Action<string?>? IdentificadoEscolhido; // null = qualquer, "true", "false"
    public event Action<string?>? CorrupcaoEscolhida;    // null = qualquer, "sim", "duas", "nao"
    public event Action<int, bool>? ModAlternado;
    public event Action<int, bool>? PropriedadeAlternada;
    public event Action<bool>? TodosModsAlternados;
    public event Action<int, double?, double?>? LimitesModAlterados;
    public event Action<int, double?, double?>? LimitesPropriedadeAlterados;
    public event Action? BuscarDeNovo;
    public event Action? AbrirSite;
    public event Action? AbrirConfiguracoes;
    public event Action? BuscaPorDpsPedida;                // clicou em "Busca por DPS"
    public event Action<List<int>>? BuscaPorDpsConfirmada; // escolheu os atributos e clicou em "Buscar por DPS"
    public event Action<int, int>? PosicaoArrastada;

    public Item? ItemAtual { get; private set; }
    public string Titulo => TxtNome.Text;
    public string TextoPreco => (TxtPreco.Visibility == Visibility.Visible ? TxtPreco.Text + "\n" : "") + TxtCorpo.Text;
    public bool PainelDpsAberto => PainelDps.Visibility == Visibility.Visible;
    public IEnumerable<(int Indice, string Texto)> CandidatosDps =>
        ListaDps.Children.OfType<CheckBox>().Select(c => ((int)c.Tag, ((TextBlock)c.Content).Text));
    public int AnunciosMostrados => ListaAnuncios.Children.Count;
    public string EsquemaAtual { get; private set; } = EsquemasCores.Padrao;

    public JanelaOverlay(Configuracao config, bool foraDaTela)
    {
        InitializeComponent();
        _config = config;
        _foraDaTela = foraDaTela;
        AplicarEsquema(EsquemasCores.Obter(config.Esquema), config.OpacidadeFundo);

        foreach (var (id, nome) in Moedas.Opcoes) CmbMoeda.Items.Add(new ComboBoxItem { Content = nome, Tag = id });
        CmbMoeda.SelectionChanged += (_, _) =>
        {
            if (!_preenchendo && CmbMoeda.SelectedItem is ComboBoxItem { Tag: string id }) MoedaEscolhida?.Invoke(id);
        };
        foreach (var (valor, texto) in new (string, string)[] { ("", "Corrompido ou não"), ("sim", "Só corrompidos"), ("duas", "Só 2× corrompidos"), ("nao", "Só não corrompidos") })
            CmbCorrupcao.Items.Add(new ComboBoxItem { Content = texto, Tag = valor });
        CmbCorrupcao.SelectionChanged += (_, _) =>
        {
            if (!_preenchendo && CmbCorrupcao.SelectedItem is ComboBoxItem { Tag: string v }) CorrupcaoEscolhida?.Invoke(v.Length == 0 ? null : v);
        };
        foreach (var (valor, texto) in new (string, string)[] { ("", "Identificado ou não"), ("true", "Só identificados"), ("false", "Só não identificados") })
            CmbIdentificado.Items.Add(new ComboBoxItem { Content = texto, Tag = valor });
        CmbIdentificado.SelectionChanged += (_, _) =>
        {
            if (!_preenchendo && CmbIdentificado.SelectedItem is ComboBoxItem { Tag: string v }) IdentificadoEscolhido?.Invoke(v.Length == 0 ? null : v);
        };
        CmbLiga.SelectionChanged += (_, _) =>
        {
            if (!_preenchendo && CmbLiga.SelectedItem is ComboBoxItem { Tag: string id }) LigaEscolhida?.Invoke(id);
        };
        BtnBuscar.Click += (_, _) => BuscarDeNovo?.Invoke();
        BtnSite.Click += (_, _) => AbrirSite?.Invoke();
        BtnConfig.Click += (_, _) => AbrirConfiguracoes?.Invoke();
        BtnFechar.Click += (_, _) => Esconder();
        BtnDps.Click += (_, _) => BuscaPorDpsPedida?.Invoke();
        BtnDpsBuscar.Click += (_, _) => ConfirmarPainelDps(null);
        BtnDpsCancelar.Click += (_, _) => EsconderPainelDps();
        PrepararArraste();

        MostrarBoasVindas();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Não ativa ao aparecer (o jogo continua com o teclado) e não aparece no Alt+Tab.
        var h = new WindowInteropHelper(this).Handle;
        Nativo.AdicionarEstiloEstendido(h, Nativo.WS_EX_NOACTIVATE | Nativo.WS_EX_TOOLWINDOW);
        // Clicar no overlay (lista, caixinha, botão) também não ativa: o teclado continua no jogo.
        HwndSource.FromHwnd(h)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool tratado) =>
        {
            if (msg != Nativo.WM_MOUSEACTIVATE) return IntPtr.Zero;
            tratado = true;
            return new IntPtr(Nativo.MA_NOACTIVATE);
        });
    }

    // ---- Cores ----

    public double OpacidadeAtual { get; private set; }

    public void AplicarEsquema(EsquemaCores e, double opacidadeFundo)
    {
        EsquemaAtual = e.Id;
        OpacidadeAtual = opacidadeFundo;
        Resources["CorFundo"] = e.FundoCom(opacidadeFundo);
        Resources["CorBorda"] = e.Borda;
        Resources["CorDestaque"] = e.Destaque;
        Resources["CorTexto"] = e.Texto;
        Resources["CorSecundario"] = e.Secundario;
        Resources["CorDetalhe"] = e.Detalhe;
        Resources["CorMod"] = e.Mod;
        Resources["CorModSecundario"] = e.ModSecundario;
        Resources["CorDica"] = e.Dica;
        Resources["CorSeparador"] = e.Separador;
    }

    // Cor que acompanha o esquema (muda na hora se o esquema mudar).
    static void Cor(FrameworkElement el, DependencyProperty prop, string chave) => el.SetResourceReference(prop, chave);

    // ---- Chamados pelo ControladorPreco ----

    public void DefinirLigas(IReadOnlyList<Liga> ligas, string atual)
    {
        _preenchendo = true;
        CmbLiga.Items.Clear();
        foreach (var l in ligas) CmbLiga.Items.Add(new ComboBoxItem { Content = l.Texto, Tag = l.Id });
        CmbLiga.SelectedItem = CmbLiga.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == atual);
        _preenchendo = false;
    }

    public void DefinirMoeda(string moeda)
    {
        _preenchendo = true;
        CmbMoeda.SelectedItem = CmbMoeda.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == moeda);
        _preenchendo = false;
    }

    public void MostrarItem(Item item)
    {
        ItemAtual = item;
        Preencher(item.Nome, CorDaRaridade(item.Raridade), item.Base, string.Join("  ·  ", Detalhes(item)), "Preparando a busca…");
        if (item.Arma is { } arma)
        {
            // Com qualidade abaixo de 20%, mostra também o DPS na conta do trade (é o que os filtros comparam).
            var q20 = arma.Qualidade < 20 && arma.DpsFisico > 0 ? $" ({Valor(arma.DpsQ20, 0)} com 20% de qualidade)" : "";
            var partes = new List<string> { $"DPS {Valor(arma.Dps, 0)}{q20}" };
            if (arma.DpsFisico > 0) partes.Add($"físico {Valor(arma.DpsFisico, 0)}");
            if (arma.DpsElemental > 0) partes.Add($"elemental {Valor(arma.DpsElemental, 0)}");
            if (arma.DpsCaos > 0) partes.Add($"caos {Valor(arma.DpsCaos, 0)}");
            partes.Add($"{Valor(arma.AtaquesPorSegundo, 2)} ataques/s");
            TxtDps.Text = string.Join("  ·  ", partes);
            TxtDps.Visibility = Visibility.Visible;
        }
        Exibir(itemNovo: true);
    }

    public void MostrarBusca(BuscaItem busca)
    {
        ListaMods.Children.Clear();
        _preenchendo = true;
        if (busca.Mods.Count + busca.Propriedades.Count > 0) ListaMods.Children.Add(CabecalhoLimites(busca));
        for (int i = 0; i < busca.Mods.Count; i++)
        {
            var f = busca.Mods[i];
            var indice = i;
            var tipo = f.Mod.Tipo == "explicit" ? "" : $"[{f.Mod.Tipo}] ";
            ListaMods.Children.Add(LinhaFiltro($"mod{i}", tipo + f.Mod.Texto.Replace("\n", " "),
                f.Mod.Tipo == "explicit" ? "CorMod" : "CorModSecundario", f.Ativo, f.Min, f.Max,
                ativo => ModAlternado?.Invoke(indice, ativo),
                (min, max) => LimitesModAlterados?.Invoke(indice, min, max)));
        }
        for (int i = 0; i < busca.Propriedades.Count; i++)
        {
            var p = busca.Propriedades[i];
            var indice = i;
            // Mostra o DPS que o trade compara (os campos são o mín/máx da busca, 90% dele por padrão).
            var q20 = p.Id is "dps" or "pdps" && busca.Item.Arma is { Qualidade: < 20 } ? " com 20% de qualidade" : "";
            ListaMods.Children.Add(LinhaFiltro($"prop{i}", $"{p.Nome}: {Valor(p.Valor, 0)}{q20}", "CorDestaque", p.Ativo, p.Min, p.Max,
                ativo => PropriedadeAlternada?.Invoke(indice, ativo),
                (min, max) => LimitesPropriedadeAlterados?.Invoke(indice, min, max)));
        }
        CmbIdentificado.SelectedItem = CmbIdentificado.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == (busca.Identificado ?? ""));
        CmbCorrupcao.SelectedItem = CmbCorrupcao.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == (busca.Corrupcao ?? ""));
        _preenchendo = false;
        ListaMods.Visibility = ListaMods.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Só não identificados: os mods não valem na busca (o trade não mostra mods desses itens).
        var modsIgnorados = busca.Identificado == "false" && busca.Mods.Count > 0;
        if (_todosMods is not null) _todosMods.IsEnabled = !modsIgnorados;
        foreach (var linha in ListaMods.Children.OfType<Grid>().Where(l => l.Tag is string chave && chave.StartsWith("mod")))
        {
            linha.IsEnabled = !modsIgnorados;
            linha.Opacity = modsIgnorados ? 0.4 : 1;
        }
        CmbIdentificado.ToolTip = modsIgnorados
            ? "Itens não identificados não mostram mods: os mods ficam fora da busca"
            : "Buscar itens identificados, não identificados ou ambos";

        var suportada = busca.NaoSuportada is null;
        PainelSeletores.Visibility = suportada ? Visibility.Visible : Visibility.Collapsed;
        PainelBotoes.Visibility = suportada ? Visibility.Visible : Visibility.Collapsed;
        BtnDps.Visibility = suportada && busca.Item.Arma is not null ? Visibility.Visible : Visibility.Collapsed;
        PainelDps.Visibility = Visibility.Collapsed;
        if (!suportada) MostrarStatus(busca.NaoSuportada!, aviso: false);
        Reposicionar();
    }

    public void MostrarBuscando(string liga, string moeda)
    {
        LimparResultado();
        CorpoNeutro($"Buscando em {liga} ({(moeda == Moedas.Qualquer ? "qualquer moeda" : moeda)})…");
        BtnBuscar.IsEnabled = false;
        BtnSite.IsEnabled = false;
        Reposicionar();
    }

    public void MostrarResultado(ResultadoPreco r, string moeda)
    {
        LimparResultado();
        BtnBuscar.IsEnabled = true;
        BtnSite.IsEnabled = true;
        var qualquer = moeda == Moedas.Qualquer;
        if (r.Resumo is not { } s)
        {
            CorpoNeutro(r.Total == 0
                ? (qualquer ? "Nenhum anúncio nesta liga." : $"Nenhum anúncio em {moeda} nesta liga. Tente 'Qualquer moeda'.")
                  + " Desmarque mods ou use a busca por DPS."
                : $"{r.Total} anúncios, mas nenhum com preço{(qualquer ? "" : $" em {moeda}")}.");
        }
        else
        {
            // Preço em destaque; o resto logo abaixo. Cada valor na moeda do próprio anúncio (sem conversão).
            TxtPreco.Text = $"≈ {Valor(s.Mediana)} {s.Moeda}";
            TxtPreco.Visibility = Visibility.Visible;
            CorpoNeutro($"mais barato {Valor(s.Minimo)} {s.MoedaMinimo}   ·   {r.Total} anúncio(s) na busca");
            var primeiro = true;
            foreach (var a in r.Anuncios.Take(5))
            {
                var linha = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                linha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var preco = new TextBlock { Text = $"{Valor(a.Preco)} {a.Moeda}", FontSize = 14, FontWeight = FontWeights.SemiBold };
                Cor(preco, TextBlock.ForegroundProperty, primeiro ? "CorDestaque" : "CorTexto"); // o mais barato em destaque
                var idade = new TextBlock { Text = Idade(a.Indexado), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
                Cor(idade, TextBlock.ForegroundProperty, "CorSecundario");
                Grid.SetColumn(idade, 1);
                linha.Children.Add(preco);
                linha.Children.Add(idade);
                ListaAnuncios.Children.Add(linha);
                primeiro = false;
            }
            CaixaAnuncios.Visibility = ListaAnuncios.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        Reposicionar();
    }

    // Busca manual: os filtros mudaram, mas a busca só acontece ao clicar em "Buscar".
    public void MostrarPendente()
    {
        TxtPreco.Opacity = 0.45; // o preço mostrado é da busca anterior
        ListaAnuncios.Opacity = 0.45;
        TxtCorpo.Text = "Filtros alterados: clique em Buscar para atualizar o preço.";
        Cor(TxtCorpo, TextBlock.ForegroundProperty, "CorDestaque");
        BtnBuscar.Content = "Buscar";
        BtnBuscar.IsEnabled = true;
        if (TryFindResource("AccentButtonStyle") is Style destaque) BtnBuscar.Style = destaque;
        Reposicionar();
    }

    void LimparResultado()
    {
        TxtPreco.Visibility = Visibility.Collapsed;
        TxtPreco.Opacity = 1;
        ListaAnuncios.Opacity = 1;
        ListaAnuncios.Children.Clear();
        CaixaAnuncios.Visibility = Visibility.Collapsed;
        BtnBuscar.Content = "Buscar de novo";
        BtnBuscar.ClearValue(StyleProperty);
    }

    // ---- Busca por DPS ----

    public void MostrarPainelDps(BuscaItem busca, IReadOnlyList<int> candidatos)
    {
        var dps = busca.Propriedades.FirstOrDefault(p => p.Id == "dps");
        TxtPainelDps.Text = (dps is null ? "" : $"Procura armas com DPS total ≥ {Valor((decimal)(dps.Min ?? 0))} (90% do seu, na conta do trade). ")
            + (candidatos.Count == 0 ? "Este item não tem outros atributos além dos que já entram no DPS."
                                     : "Marque os atributos que também importam (os outros são ignorados):");
        ListaDps.Children.Clear();
        foreach (var i in candidatos)
        {
            var m = busca.Mods[i].Mod;
            var rotulo = new TextBlock { Text = (m.Tipo == "explicit" ? "" : $"[{m.Tipo}] ") + m.Texto.Replace("\n", " "), TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
            Cor(rotulo, TextBlock.ForegroundProperty, "CorMod");
            ListaDps.Children.Add(new CheckBox { Content = rotulo, Tag = i, Focusable = false, Margin = new Thickness(0, 1, 0, 1) });
        }
        ListaMods.Visibility = Visibility.Collapsed;
        PainelDps.Visibility = Visibility.Visible;
        Reposicionar();
    }

    public void EsconderPainelDps()
    {
        if (PainelDps.Visibility != Visibility.Visible) return;
        PainelDps.Visibility = Visibility.Collapsed;
        ListaMods.Visibility = ListaMods.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Reposicionar();
    }

    // escolhidos null = os marcados na tela (clique em "Buscar por DPS"); o harness passa a lista direto.
    public void ConfirmarPainelDps(IReadOnlyCollection<int>? escolhidos)
    {
        if (escolhidos is not null)
            foreach (var c in ListaDps.Children.OfType<CheckBox>()) c.IsChecked = escolhidos.Contains((int)c.Tag);
        var marcados = ListaDps.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (int)c.Tag).ToList();
        PainelDps.Visibility = Visibility.Collapsed;
        BuscaPorDpsConfirmada?.Invoke(marcados);
    }

    public void MostrarStatus(string texto, bool aviso)
    {
        BtnBuscar.IsEnabled = true;
        LimparResultado();
        if (aviso)
        {
            TxtCorpo.Text = texto;
            TxtCorpo.Foreground = CorAviso;
        }
        else
        {
            CorpoNeutro(texto);
        }
        Reposicionar();
    }

    public void MostrarAviso(string titulo, string detalhe)
    {
        ItemAtual = null;
        Preencher(titulo, CorAviso, null, "", detalhe);
        Exibir(itemNovo: true);
    }

    // Mostrar/ocultar pelo atalho: volta onde estava.
    public void Alternar()
    {
        if (IsVisible) Esconder();
        else Exibir(itemNovo: false);
    }

    public void Mostrar()
    {
        if (!IsVisible) Exibir(itemNovo: false);
    }

    public void Esconder()
    {
        DevolverTeclado();
        Hide();
    }

    // Atalhos mudaram nas configurações: atualiza os textos que citam as teclas.
    public void AtualizarTextos()
    {
        TxtDica.Text = Dica();
        if (_boasVindas) MostrarBoasVindas();
    }

    void MostrarBoasVindas()
    {
        Preencher(AppOverlay.Nome, null, null, "", $"Aperte {_config.AtalhoConsultar} com o mouse sobre um item no jogo.");
        _boasVindas = true;
    }

    string Dica() =>
        $"Esc ou {_config.AtalhoAlternar} fecha  ·  {_config.AtalhoConsultar} consulta outro item  ·  arraste pelo título";

    void CorpoNeutro(string texto)
    {
        TxtCorpo.Text = texto;
        Cor(TxtCorpo, TextBlock.ForegroundProperty, "CorTexto");
    }

    // ---- Arrastar (feito à mão: DragMove ativaria a janela e tiraria o teclado do jogo) ----

    bool _arrastando;
    Nativo.Ponto _cursorInicial;
    int _xInicial, _yInicial;

    void PrepararArraste()
    {
        // Só cliques que nenhum controle tratou chegam aqui (textos, fundo do cartão).
        Cartao.MouseLeftButtonDown += (_, e) =>
        {
            if (!PodeArrastarDe(e.OriginalSource as DependencyObject)) return;
            Nativo.GetCursorPos(out _cursorInicial);
            var p = PosicaoNaTela();
            (_xInicial, _yInicial) = (p[0], p[1]);
            _arrastando = Cartao.CaptureMouse();
            if (_arrastando) Mouse.OverrideCursor = Cursors.SizeAll;
            e.Handled = true;
        };
        Cartao.MouseMove += (_, _) =>
        {
            if (!_arrastando) return;
            Nativo.GetCursorPos(out var c);
            _x = _xInicial + c.X - _cursorInicial.X;
            _y = _yInicial + c.Y - _cursorInicial.Y;
            Nativo.SetWindowPos(new WindowInteropHelper(this).Handle, Nativo.HWND_TOPMOST, _x.Value, _y.Value, 0, 0,
                Nativo.SWP_NOSIZE | Nativo.SWP_NOACTIVATE);
        };
        // handledEventsToo: mesmo que algum controle "coma" o soltar do botão, o arraste termina.
        Cartao.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => TerminarArraste()), handledEventsToo: true);
        Cartao.LostMouseCapture += (_, _) => TerminarArraste();
    }

    // Não arrasta a partir da lista aberta (ela fica numa janelinha à parte, mas o clique "sobe" até o cartão)
    // nem de dentro de controles (botão, caixinha, campo, lista).
    bool PodeArrastarDe(DependencyObject? origem)
    {
        if (origem is null) return false;
        if (origem is Visual v && PresentationSource.FromVisual(v) != PresentationSource.FromVisual(this)) return false;
        for (var d = origem; d is not null && d != Cartao;
             d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.Selector or ComboBoxItem or System.Windows.Controls.Primitives.ScrollBar)
                return false;
        }
        return true;
    }

    void TerminarArraste()
    {
        if (!_arrastando) return;
        _arrastando = false;
        Mouse.OverrideCursor = null;
        Cartao.ReleaseMouseCapture();
        AplicarPosicao(); // se soltou meio fora da tela, puxa de volta
        var p = PosicaoNaTela();
        (_x, _y) = (p[0], p[1]);
        PosicaoArrastada?.Invoke(p[0], p[1]);
    }

    // ---- Mín/máx dos filtros ----

    const double LarguraNumero = 58;
    IntPtr _janelaAnterior; // quem tinha o teclado (o jogo) antes de clicar num campo de número

    Grid NovaLinha(object? chave)
    {
        var linha = new Grid { Margin = new Thickness(0, 1, 0, 1), Tag = chave };
        linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LarguraNumero) });
        linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LarguraNumero) });
        return linha;
    }

    CheckBox? _todosMods;

    // Cabeçalho da lista: caixinha "Todos os mods" + rótulos "mín" e "máx".
    Grid CabecalhoLimites(BuscaItem busca)
    {
        var linha = NovaLinha(null);
        _todosMods = null;
        if (busca.Mods.Count > 1)
        {
            var ativos = busca.Mods.Count(f => f.Ativo);
            var rotulo = new TextBlock { Text = "Todos os mods", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            Cor(rotulo, TextBlock.ForegroundProperty, "CorDetalhe");
            // Marcada = todos; vazia = nenhum; traço (null) = alguns. Clicar com traço marca todos.
            bool? antes = ativos == busca.Mods.Count ? true : ativos == 0 ? false : null;
            _todosMods = new CheckBox
            {
                Content = rotulo,
                Focusable = false,
                IsChecked = antes,
                ToolTip = "Marca ou desmarca todos os mods de uma vez",
            };
            // Decide pelo estado de antes do clique: o WPF leva "traço" para "vazia", e aí desmarcaria todos.
            _todosMods.Click += (_, _) => TodosModsAlternados?.Invoke(antes != true);
            linha.Children.Add(_todosMods);
        }
        foreach (var (texto, coluna) in new[] { ("mín", 1), ("máx", 3) })
        {
            var t = new TextBlock { Text = texto, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
            Cor(t, TextBlock.ForegroundProperty, "CorDetalhe");
            Grid.SetColumn(t, coluna);
            linha.Children.Add(t);
        }
        return linha;
    }

    // Caixinha (liga/desliga) + campos mín e máx. Enter (ou sair do campo) aplica e dispara nova busca.
    Grid LinhaFiltro(string chave, string texto, string corTexto, bool ativo, double? min, double? max,
        Action<bool> alternar, Action<double?, double?> limites)
    {
        var linha = NovaLinha(chave);
        var rotulo = new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
        Cor(rotulo, TextBlock.ForegroundProperty, corTexto);
        var caixa = new CheckBox { IsChecked = ativo, Focusable = false, VerticalAlignment = VerticalAlignment.Center, Content = rotulo };
        caixa.Checked += (_, _) => { if (!_preenchendo) alternar(true); };
        caixa.Unchecked += (_, _) => { if (!_preenchendo) alternar(false); };

        var campoMin = CampoNumero(min);
        var campoMax = CampoNumero(max);
        Grid.SetColumn(campoMin, 1);
        Grid.SetColumn(campoMax, 3);

        void Aplicar()
        {
            if (!LerNumero(campoMin.Text, out var novoMin) || !LerNumero(campoMax.Text, out var novoMax))
            {
                campoMin.Text = Numero(min);
                campoMax.Text = Numero(max);
                return;
            }
            if (novoMin == min && novoMax == max) return;
            min = novoMin;
            max = novoMax;
            limites(novoMin, novoMax);
        }

        foreach (var campo in new[] { campoMin, campoMax })
        {
            var c = campo;
            c.PreviewMouseLeftButtonDown += (_, e) =>
            {
                AtivarParaDigitar();
                if (!c.IsKeyboardFocusWithin) { c.Focus(); e.Handled = true; } // 1º clique seleciona tudo
            };
            c.GotKeyboardFocus += (_, _) => c.SelectAll();
            c.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                Aplicar();
                DevolverTeclado();
            };
            c.LostKeyboardFocus += (_, _) => Aplicar();
        }

        linha.Children.Add(caixa);
        linha.Children.Add(campoMin);
        linha.Children.Add(campoMax);
        return linha;
    }

    static TextBox CampoNumero(double? valor) => new()
    {
        Text = Numero(valor),
        FontSize = 12,
        MinHeight = 0,
        MinWidth = 0,
        Height = 28,
        Padding = new Thickness(4, 3, 4, 3),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Right,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    static string Numero(double? v) => v?.ToString("0.##", Br) ?? "";

    // Vazio = sem limite. Aceita vírgula ou ponto.
    static bool LerNumero(string texto, out double? valor)
    {
        valor = null;
        texto = texto.Trim();
        if (texto.Length == 0) return true;
        if (!double.TryParse(texto.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return false;
        valor = v;
        return true;
    }

    // Para digitar, o overlay precisa do teclado: pega emprestado de quem tinha (o jogo) e devolve no Enter/Esc.
    void AtivarParaDigitar()
    {
        if (IsActive) return;
        _janelaAnterior = Nativo.GetForegroundWindow();
        Nativo.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Activate();
    }

    public void DevolverTeclado()
    {
        Keyboard.ClearFocus();
        var anterior = _janelaAnterior;
        _janelaAnterior = IntPtr.Zero;
        if (anterior != IntPtr.Zero && IsActive) Nativo.SetForegroundWindow(anterior);
    }

    // ---- Posição ----

    // itemNovo: Ctrl+D (vai para perto do item, ou para a posição salva se "abrir onde deixei").
    void Exibir(bool itemNovo)
    {
        if (!IsVisible) Show();
        UpdateLayout();
        if (itemNovo || _x is null)
        {
            if (!_foraDaTela && _config.AbrirOverlay == "fixa" && _config.PosicaoX is int px && _config.PosicaoY is int py)
                (_x, _y) = (px, py);
            else
                AncorarNoCursor();
        }
        AplicarPosicao();
    }

    // O conteúdo mudou de tamanho: a janela fica onde está, só encolhe/cresce e não sai da tela.
    void Reposicionar()
    {
        if (!IsVisible) return;
        UpdateLayout();
        AplicarPosicao();
    }

    // corTitulo null = cor de destaque do esquema.
    void Preencher(string titulo, Brush? corTitulo, string? subtitulo, string detalhes, string corpo)
    {
        _boasVindas = false;
        TxtNome.Text = titulo;
        if (corTitulo is null) Cor(TxtNome, TextBlock.ForegroundProperty, "CorDestaque");
        else TxtNome.Foreground = corTitulo;
        TxtBase.Text = subtitulo ?? "";
        TxtBase.Visibility = string.IsNullOrEmpty(subtitulo) ? Visibility.Collapsed : Visibility.Visible;
        TxtDetalhes.Text = detalhes;
        TxtDetalhes.Visibility = detalhes.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        TxtDps.Visibility = Visibility.Collapsed;
        LimparResultado();
        CorpoNeutro(corpo);
        ListaMods.Children.Clear();
        ListaMods.Visibility = Visibility.Collapsed;
        PainelDps.Visibility = Visibility.Collapsed;
        PainelSeletores.Visibility = Visibility.Collapsed;
        PainelBotoes.Visibility = Visibility.Collapsed;
        TxtDica.Text = Dica();
    }

    (int Largura, int Altura) TamanhoEmPixels()
    {
        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        return ((int)Math.Ceiling(ActualWidth * m.M11), (int)Math.Ceiling(ActualHeight * m.M22));
    }

    // Ao lado do cursor (onde está o item), com o topo um pouco acima dele.
    void AncorarNoCursor()
    {
        if (_foraDaTela)
        {
            _x = _y = -20000; // harness: renderiza normalmente, mas longe da tela de quem está usando o PC
            return;
        }
        Nativo.GetCursorPos(out var c);
        var area = Nativo.AreaDeTrabalho(c);
        var (largura, _) = TamanhoEmPixels();
        var x = c.X + 24;
        if (x + largura > area.Right) x = c.X - 24 - largura;
        _x = x;
        _y = c.Y - 80;
    }

    // Aplica _x/_y, empurrando para dentro da área útil do monitor se o tamanho atual não couber.
    void AplicarPosicao()
    {
        int x = _x ?? 0, y = _y ?? 0;
        if (!_foraDaTela)
        {
            var (largura, altura) = TamanhoEmPixels();
            var area = Nativo.AreaDeTrabalho(new Nativo.Ponto { X = x + largura / 2, Y = y });
            x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - largura));
            y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - altura));
        }
        var h = new WindowInteropHelper(this).Handle;
        Nativo.SetWindowPos(h, Nativo.HWND_TOPMOST, x, y, 0, 0, Nativo.SWP_NOSIZE | Nativo.SWP_NOACTIVATE);
    }

    // Canto superior esquerdo real da janela (px), para o harness conferir que ela não pula.
    public int[] PosicaoNaTela()
    {
        Nativo.GetWindowRect(new WindowInteropHelper(this).Handle, out var r);
        return [r.Left, r.Top];
    }

    // Centro (em pixels de tela) dos controles clicáveis, para o harness clicar com o mouse de verdade.
    // Com uma lista aberta, inclui as opções: "moeda:exalted", "liga:Standard".
    public Dictionary<string, int[]> AlvosNaTela()
    {
        var alvos = new Dictionary<string, int[]>();
        if (!IsVisible) return alvos;
        void Adicionar(string nome, FrameworkElement e)
        {
            if (!e.IsVisible || e.ActualWidth == 0) return;
            var p = e.PointToScreen(new Point(e.ActualWidth / 2, e.ActualHeight / 2));
            alvos[nome] = [(int)p.X, (int)p.Y];
        }
        Adicionar("titulo", TxtNome);
        Adicionar("config", BtnConfig);
        Adicionar("fechar", BtnFechar);
        Adicionar("liga", CmbLiga);
        Adicionar("moeda", CmbMoeda);
        Adicionar("identificado", CmbIdentificado);
        Adicionar("corrupcao", CmbCorrupcao);
        if (_todosMods is not null) Adicionar("todos", _todosMods);
        Adicionar("buscar", BtnBuscar);
        Adicionar("site", BtnSite);
        Adicionar("dps", BtnDps);
        Adicionar("dps:buscar", BtnDpsBuscar);
        Adicionar("dps:cancelar", BtnDpsCancelar);
        foreach (var c in ListaDps.Children.OfType<CheckBox>()) Adicionar($"dps:{c.Tag}", c);
        foreach (var linha in ListaMods.Children.OfType<Grid>())
        {
            if (linha.Tag is not string chave) continue; // cabeçalho
            Adicionar(chave, (FrameworkElement)linha.Children[0]);
            Adicionar(chave + ":min", (FrameworkElement)linha.Children[1]);
            Adicionar(chave + ":max", (FrameworkElement)linha.Children[2]);
        }
        foreach (var (combo, prefixo) in new[] { (CmbMoeda, "moeda:"), (CmbLiga, "liga:"), (CmbIdentificado, "identificado:"), (CmbCorrupcao, "corrupcao:") })
            if (combo.IsDropDownOpen)
                foreach (ComboBoxItem opcao in combo.Items) Adicionar(prefixo + opcao.Tag, opcao);
        return alvos;
    }

    // Harness: "clica" num botão do cabeçalho pelo mesmo caminho do clique de verdade (automação do WPF).
    public bool ClicarBotao(string nome)
    {
        Button? botao = nome switch { "fechar" => BtnFechar, "config" => BtnConfig, _ => null };
        if (botao is null || !botao.IsVisible) return false;
        (new System.Windows.Automation.Peers.ButtonAutomationPeer(botao)
            .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke) as System.Windows.Automation.Provider.IInvokeProvider)?.Invoke();
        return true;
    }

    // Print do overlay sobre um fundo escuro (usado pelo harness).
    public void SalvarPrint(string caminho) => Prints.Salvar(this, (FrameworkElement)Content, caminho, FundoPrint);

    static string Valor(decimal v) => v.ToString("0.##", Br);
    static string Valor(double v, int casas) => v.ToString(casas == 0 ? "0" : "0.00", Br);
    static string Valor(double v) => v.ToString("0", Br);

    static string Idade(DateTimeOffset indexado)
    {
        var d = DateTimeOffset.UtcNow - indexado;
        if (indexado == DateTimeOffset.MinValue || d < TimeSpan.Zero) return "";
        if (d.TotalMinutes < 60) return $"há {Math.Max(1, (int)d.TotalMinutes)} min";
        if (d.TotalHours < 24) return $"há {(int)d.TotalHours} h";
        return $"há {(int)d.TotalDays} dia(s)";
    }

    static IEnumerable<string> Detalhes(Item item)
    {
        if (item.Classe.Length > 0) yield return item.Classe;
        yield return NomeRaridade(item.Raridade);
        if (item.NivelItem is { } n) yield return $"nível do item {n}";
        if (item.Qualidade is { } q) yield return $"qualidade +{q}%";
        if (item.Pilha is { } p) yield return $"pilha {p}";
        if (item.NaoIdentificado) yield return "não identificado";
        if (item.DuasVezesCorrompido) yield return "corrompido 2×";
        else if (item.Corrompido) yield return "corrompido";
    }

    static string NomeRaridade(Raridade r) => r switch
    {
        Raridade.Normal => "Normal",
        Raridade.Magico => "Mágico",
        Raridade.Raro => "Raro",
        Raridade.Unico => "Único",
        Raridade.Moeda => "Moeda",
        Raridade.Gema => "Gema",
        _ => "Raridade desconhecida",
    };

    static SolidColorBrush Cor(byte r, byte g, byte b)
    {
        var cor = new SolidColorBrush(Color.FromRgb(r, g, b));
        cor.Freeze();
        return cor;
    }

    // Fixas em qualquer esquema: aviso e raridades (são as cores do jogo).
    static readonly Brush CorAviso = Cor(0xE0, 0x7A, 0x5F), FundoPrint = Cor(0x2A, 0x26, 0x22);
    static readonly Brush CorNormal = Cor(0xC8, 0xC8, 0xC8), CorMagico = Cor(0x88, 0x88, 0xFF), CorRaro = Cor(0xFF, 0xFF, 0x77),
        CorUnico = Cor(0xAF, 0x60, 0x25), CorMoeda = Cor(0xAA, 0x9E, 0x82), CorGema = Cor(0x1B, 0xA2, 0x9B);

    // null = sem raridade conhecida: usa o destaque do esquema.
    static Brush? CorDaRaridade(Raridade r) => r switch
    {
        Raridade.Normal => CorNormal,
        Raridade.Magico => CorMagico,
        Raridade.Raro => CorRaro,
        Raridade.Unico => CorUnico,
        Raridade.Moeda => CorMoeda,
        Raridade.Gema => CorGema,
        _ => null,
    };
}
