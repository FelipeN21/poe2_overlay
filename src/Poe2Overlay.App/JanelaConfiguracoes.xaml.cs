using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Poe2Overlay.Nucleo.Trade;

namespace Poe2Overlay.App;

// O que a janela de configurações edita. Liga "" = liga do momento (automático).
public sealed record DadosConfiguracoes(string AtalhoConsultar, string AtalhoAlternar, string Liga, string Esquema, string AbrirOverlay,
    bool BuscaAutomatica = true, double OpacidadeFundo = 0.95, bool CopiaAvancada = true);

// Janela normal (pode pegar o teclado: o usuário está configurando, não jogando).
// Enquanto ela está aberta os atalhos globais ficam suspensos, para dar para "apertar" um atalho novo.
public partial class JanelaConfiguracoes : Window
{
    readonly Func<DadosConfiguracoes, string?> _salvar;

    public JanelaConfiguracoes(Configuracao config, IReadOnlyList<Liga> ligas, Func<DadosConfiguracoes, string?> salvar, bool foraDaTela)
    {
        InitializeComponent();
        _salvar = salvar;
        try { Icon = BitmapFrame.Create(typeof(JanelaConfiguracoes).Assembly.GetManifestResourceStream("icone.ico")!); }
        catch { /* sem ícone, sem problema */ }
        if (foraDaTela)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Top = -20000;
        }

        TxtAtalhoConsultar.Text = config.AtalhoConsultar;
        TxtAtalhoAlternar.Text = config.AtalhoAlternar;
        foreach (var campo in new[] { TxtAtalhoConsultar, TxtAtalhoAlternar })
            campo.PreviewKeyDown += (_, e) => Capturar(campo, e);

        CmbLiga.Items.Add(new ComboBoxItem { Content = $"Liga do momento (automático{(ligas.Count > 0 ? ": " + Ligas.Atual(ligas) : "")})", Tag = "" });
        foreach (var l in ligas) CmbLiga.Items.Add(new ComboBoxItem { Content = l.Texto, Tag = l.Id });
        if (config.Liga.Length > 0 && ligas.All(l => l.Id != config.Liga))
            CmbLiga.Items.Add(new ComboBoxItem { Content = config.Liga, Tag = config.Liga });
        Selecionar(CmbLiga, config.Liga);

        foreach (var e in EsquemasCores.Todos) CmbEsquema.Items.Add(new ComboBoxItem { Content = e.Nome, Tag = e.Id });
        CmbEsquema.SelectionChanged += (_, _) => MostrarAmostra();
        Selecionar(CmbEsquema, config.Esquema);
        MostrarAmostra();

        (config.AbrirOverlay == "fixa" ? RbFixa : RbCursor).IsChecked = true;
        (config.BuscaAutomatica ? RbBuscaAutomatica : RbBuscaManual).IsChecked = true;
        ChkCopiaAvancada.IsChecked = config.CopiaAvancada;
        SldTransparencia.ValueChanged += (_, _) => MostrarAmostra();
        SldTransparencia.Value = Math.Round((1 - config.OpacidadeFundo) * 100 / 5) * 5;
        MostrarAmostra();

        BtnSalvar.Click += (_, _) => Salvar();
        BtnCancelar.Click += (_, _) => Close();
    }

    public DadosConfiguracoes Ler() => new(
        TxtAtalhoConsultar.Text.Trim(),
        TxtAtalhoAlternar.Text.Trim(),
        (CmbLiga.SelectedItem as ComboBoxItem)?.Tag as string ?? "",
        (CmbEsquema.SelectedItem as ComboBoxItem)?.Tag as string ?? EsquemasCores.Padrao,
        RbFixa.IsChecked == true ? "fixa" : "cursor",
        RbBuscaManual.IsChecked != true,
        Opacidade,
        ChkCopiaAvancada.IsChecked == true);

    double Opacidade => Math.Round(1 - SldTransparencia.Value / 100, 2);

    // Usado pelo harness: preenche como o usuário faria e clica em Salvar. Devolve o erro (ou null).
    public string? PreencherESalvar(DadosConfiguracoes d)
    {
        TxtAtalhoConsultar.Text = d.AtalhoConsultar;
        TxtAtalhoAlternar.Text = d.AtalhoAlternar;
        if (CmbLiga.Items.Cast<ComboBoxItem>().All(i => (string)i.Tag != d.Liga))
            CmbLiga.Items.Add(new ComboBoxItem { Content = d.Liga, Tag = d.Liga });
        Selecionar(CmbLiga, d.Liga);
        Selecionar(CmbEsquema, d.Esquema);
        (d.AbrirOverlay == "fixa" ? RbFixa : RbCursor).IsChecked = true;
        (d.BuscaAutomatica ? RbBuscaAutomatica : RbBuscaManual).IsChecked = true;
        ChkCopiaAvancada.IsChecked = d.CopiaAvancada;
        SldTransparencia.Value = Math.Round((1 - d.OpacidadeFundo) * 100);
        return Salvar();
    }

    string? Salvar()
    {
        var erro = _salvar(Ler());
        if (erro is null)
        {
            Close();
            return null;
        }
        TxtErro.Text = erro;
        TxtErro.Visibility = Visibility.Visible;
        return erro;
    }

    // Aperta a combinação: vira "Ctrl+Shift+F8". Teclas de modificador sozinhas só esperam a próxima.
    static void Capturar(TextBox campo, KeyEventArgs e)
    {
        e.Handled = true;
        var tecla = e.Key == Key.System ? e.SystemKey : e.Key;
        if (tecla is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.None) return;
        var partes = new List<string>();
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) partes.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Shift)) partes.Add("Shift");
        if (m.HasFlag(ModifierKeys.Alt)) partes.Add("Alt");
        if (m.HasFlag(ModifierKeys.Windows)) partes.Add("Win");
        partes.Add(tecla is >= Key.D0 and <= Key.D9 ? ((int)tecla - (int)Key.D0).ToString() : tecla.ToString());
        campo.Text = string.Join("+", partes);
    }

    void MostrarAmostra()
    {
        if (SldTransparencia is null || Amostra is null) return; // ainda montando a janela
        var e = EsquemasCores.Obter((CmbEsquema.SelectedItem as ComboBoxItem)?.Tag as string);
        Amostra.Background = e.FundoCom(Opacidade);
        TxtTransparencia.Text = $"{SldTransparencia.Value:0}%";
        Amostra.BorderBrush = e.Borda;
        AmostraTitulo.Foreground = e.Destaque;
        AmostraTexto.Foreground = e.Texto;
    }

    static void Selecionar(ComboBox combo, string tag) =>
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? combo.Items[0];

    // ---- Harness ----

    public Dictionary<string, int[]> AlvosNaTela()
    {
        var alvos = new Dictionary<string, int[]>();
        void Adicionar(string nome, FrameworkElement el)
        {
            if (!el.IsVisible) return;
            var p = el.PointToScreen(new Point(el.ActualWidth / 2, el.ActualHeight / 2));
            alvos[nome] = [(int)p.X, (int)p.Y];
        }
        Adicionar("consultar", TxtAtalhoConsultar);
        Adicionar("alternar", TxtAtalhoAlternar);
        Adicionar("salvar", BtnSalvar);
        Adicionar("cancelar", BtnCancelar);
        return alvos;
    }

    public string Erro => TxtErro.Visibility == Visibility.Visible ? TxtErro.Text : "";

    public void SalvarPrint(string caminho) =>
        Prints.Salvar(this, Conteudo, caminho, new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)));
}
