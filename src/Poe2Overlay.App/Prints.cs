using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Poe2Overlay.App;

// Print de uma janela (ou parte dela) em PNG, sobre um fundo opcional. Usado pelo harness.
internal static class Prints
{
    public static void Salvar(Visual janela, FrameworkElement alvo, string caminho, Brush? fundo)
    {
        alvo.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(janela);
        double w = alvo.ActualWidth, h = alvo.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (fundo is not null) dc.DrawRectangle(fundo, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(alvo), null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        using var arquivo = File.Create(caminho);
        png.Save(arquivo);
    }
}
