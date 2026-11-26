using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Forms = System.Windows.Forms;

namespace Poe2Overlay.App;

// Um item do menu da bandeja (null na lista = separador). Fica separado do WinForms para o harness testar.
internal sealed record ItemMenu(string Texto, Func<bool> Habilitado, Action Acao, bool Principal = false);

internal static class MenuBandeja
{
    public static IReadOnlyList<ItemMenu?> Itens(AppOverlay app) =>
    [
        new("Mostrar overlay", () => !app.Janela.IsVisible, app.Mostrar, Principal: true),
        new("Esconder overlay", () => app.Janela.IsVisible, app.Esconder),
        null,
        new("Configurações…", () => true, app.AbrirConfiguracoes),
        new("Abrir pasta de configuração", () => true,
            () => Process.Start(new ProcessStartInfo(app.Config.Pasta) { UseShellExecute = true })),
        null,
        new($"Sair do {AppOverlay.Nome}", () => true, app.Sair),
    ];
}

// Ícone perto do relógio. Clique (esquerdo ou direito) abre o menu; o overlay não tem janela na barra de tarefas.
internal sealed class Bandeja : IDisposable
{
    readonly Forms.NotifyIcon _icone;

    internal Bandeja(AppOverlay app)
    {
        _icone = new Forms.NotifyIcon
        {
            Icon = CarregarIcone(),
            Text = $"{AppOverlay.Nome} {AppOverlay.Versao}",
            ContextMenuStrip = CriarMenu(app),
            Visible = true,
        };
        // Clique esquerdo também abre o menu (o NotifyIcon só faz isso sozinho no direito).
        _icone.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                typeof(Forms.NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_icone, null);
        };
    }

    public static Forms.ContextMenuStrip CriarMenu(AppOverlay app)
    {
        var menu = new Forms.ContextMenuStrip { Renderer = new Forms.ToolStripProfessionalRenderer(new CoresMenu()), ShowImageMargin = false };
        var itens = new List<(Forms.ToolStripMenuItem, ItemMenu)>();
        foreach (var item in MenuBandeja.Itens(app))
        {
            if (item is null)
            {
                menu.Items.Add(new Forms.ToolStripSeparator());
                continue;
            }
            var botao = new Forms.ToolStripMenuItem(item.Texto) { ForeColor = CoresMenu.Texto, Padding = new Forms.Padding(4, 3, 4, 3) };
            if (item.Principal) botao.Font = new Font(botao.Font, FontStyle.Bold);
            botao.Click += (_, _) => item.Acao();
            menu.Items.Add(botao);
            itens.Add((botao, item));
        }
        // Mostrar/Esconder: só o que faz sentido agora fica habilitado.
        menu.Opening += (_, _) =>
        {
            foreach (var (botao, item) in itens)
            {
                botao.Enabled = item.Habilitado();
                botao.ForeColor = botao.Enabled ? CoresMenu.Texto : CoresMenu.Desabilitado;
            }
        };
        return menu;
    }

    // Harness: abre o menu fora da tela e salva um print dele.
    public static void SalvarPrintMenu(AppOverlay app, string caminho)
    {
        using var menu = CriarMenu(app);
        menu.Show(-20000, -20000);
        using var bmp = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bmp, new Rectangle(0, 0, menu.Width, menu.Height));
        menu.Close();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(caminho)!);
        bmp.Save(caminho, System.Drawing.Imaging.ImageFormat.Png);
    }

    // O .ico tem 16/20/24/32 px: pede o tamanho de ícone pequeno do sistema (muda com a escala do monitor).
    static Icon CarregarIcone()
    {
        using var s = typeof(Bandeja).Assembly.GetManifestResourceStream("icone.ico");
        return s is null ? SystemIcons.Application : new Icon(s, Forms.SystemInformation.SmallIconSize);
    }

    public void Informar(string titulo, string texto) => _icone.ShowBalloonTip(4000, titulo, texto, Forms.ToolTipIcon.Info);
    public void Avisar(string titulo, string texto) => _icone.ShowBalloonTip(8000, titulo, texto, Forms.ToolTipIcon.Warning);

    public void Dispose()
    {
        _icone.Visible = false;
        _icone.Dispose();
    }

    // Menu escuro com borda dourada, no estilo do overlay.
    sealed class CoresMenu : Forms.ProfessionalColorTable
    {
        public static readonly Color Texto = Color.FromArgb(0xE8, 0xE2, 0xD4);
        public static readonly Color Desabilitado = Color.FromArgb(0x6E, 0x6A, 0x62);
        static readonly Color Fundo = Color.FromArgb(0x1C, 0x1A, 0x18);
        static readonly Color Selecionado = Color.FromArgb(0x3A, 0x33, 0x26);
        static readonly Color Borda = Color.FromArgb(0x8C, 0x7A, 0x4E);
        static readonly Color Linha = Color.FromArgb(0x40, 0x3A, 0x30);

        public override Color ToolStripDropDownBackground => Fundo;
        public override Color MenuBorder => Borda;
        public override Color MenuItemBorder => Borda;
        public override Color MenuItemSelected => Selecionado;
        public override Color MenuItemSelectedGradientBegin => Selecionado;
        public override Color MenuItemSelectedGradientEnd => Selecionado;
        public override Color ImageMarginGradientBegin => Fundo;
        public override Color ImageMarginGradientMiddle => Fundo;
        public override Color ImageMarginGradientEnd => Fundo;
        public override Color SeparatorDark => Linha;
        public override Color SeparatorLight => Fundo;
    }
}
