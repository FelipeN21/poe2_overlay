using Poe2Overlay.Nucleo.Trade;

namespace Poe2Overlay.Nucleo.Dados;

// Guarda em disco os dados estáticos do trade (stats, itens, ligas), que mudam só com patch/liga.
// Se o download falhar e houver cópia antiga, usa a antiga.
public sealed class CacheDados(string pasta, ClienteTrade cliente)
{
    public async Task<string> ObterAsync(string nome, TimeSpan validade, CancellationToken ct = default)
    {
        Directory.CreateDirectory(pasta);
        var arquivo = Path.Combine(pasta, nome + ".json");
        if (File.Exists(arquivo) && DateTime.UtcNow - File.GetLastWriteTimeUtc(arquivo) < validade)
            return await File.ReadAllTextAsync(arquivo, ct);
        try
        {
            var json = await cliente.DadosAsync(nome, ct);
            await File.WriteAllTextAsync(arquivo, json, ct);
            return json;
        }
        catch (Exception) when (File.Exists(arquivo) && !ct.IsCancellationRequested)
        {
            return await File.ReadAllTextAsync(arquivo, ct);
        }
    }
}
