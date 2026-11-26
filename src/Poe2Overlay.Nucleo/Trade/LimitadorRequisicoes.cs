namespace Poe2Overlay.Nucleo.Trade;

public sealed record RegraLimite(int Maximo, int JanelaSegundos, int CastigoSegundos);

// Respeita os limites que a GGG manda em cada resposta, sem número fixo no código:
//   X-Rate-Limit-Rules: Ip            X-Rate-Limit-Ip: 5:10:60,15:60:300   (máximo:janela:castigo)
//   X-Rate-Limit-Ip-State: 1:10:0,...  (usado:janela:castigo restante)       Retry-After (no 429)
// Cada chave ("busca", "detalhe"...) é uma política separada. Fica 1 abaixo do máximo, por segurança.
public sealed class LimitadorRequisicoes(Func<DateTimeOffset>? relogio = null)
{
    sealed class Politica
    {
        public List<RegraLimite> Regras = [];
        public readonly List<DateTimeOffset> Envios = [];
        public DateTimeOffset BloqueadoAte;
    }

    readonly Func<DateTimeOffset> _agora = relogio ?? (() => DateTimeOffset.UtcNow);
    readonly Dictionary<string, Politica> _politicas = [];
    readonly object _trava = new();

    // Zero: pode enviar agora (e o envio já fica registrado). Senão, quanto falta para poder.
    public TimeSpan Reservar(string chave)
    {
        lock (_trava)
        {
            var p = Obter(chave);
            var agora = _agora();
            var espera = p.BloqueadoAte > agora ? p.BloqueadoAte - agora : TimeSpan.Zero;
            foreach (var regra in p.Regras)
            {
                var janela = TimeSpan.FromSeconds(regra.JanelaSegundos);
                var naJanela = p.Envios.Where(t => t > agora - janela).OrderBy(t => t).ToList();
                var permitido = Math.Max(1, regra.Maximo - 1);
                if (naJanela.Count >= permitido)
                {
                    var libera = naJanela[naJanela.Count - permitido] + janela - agora;
                    if (libera > espera) espera = libera;
                }
            }
            if (espera <= TimeSpan.Zero)
            {
                p.Envios.Add(agora);
                var maiorJanela = p.Regras.Count == 0 ? 0 : p.Regras.Max(r => r.JanelaSegundos);
                p.Envios.RemoveAll(t => t < agora - TimeSpan.FromSeconds(Math.Max(maiorJanela, 60)));
                return TimeSpan.Zero;
            }
            return espera;
        }
    }

    // cabecalho(nome) devolve o valor do cabeçalho ou null.
    public void Atualizar(string chave, Func<string, string?> cabecalho, int status, TimeSpan? retryAfter)
    {
        lock (_trava)
        {
            var p = Obter(chave);
            var agora = _agora();
            var regrasNomes = cabecalho("X-Rate-Limit-Rules");
            if (regrasNomes is not null)
            {
                var regras = new List<RegraLimite>();
                foreach (var nome in regrasNomes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    regras.AddRange(LerTrincas(cabecalho($"X-Rate-Limit-{nome}")));
                    foreach (var estado in LerTrincas(cabecalho($"X-Rate-Limit-{nome}-State")))
                    {
                        // Castigo em andamento, ou o servidor já nos vê no limite (outro programa no mesmo IP?).
                        if (estado.CastigoSegundos > 0) Bloquear(p, agora + TimeSpan.FromSeconds(estado.CastigoSegundos));
                        var regra = regras.FirstOrDefault(r => r.JanelaSegundos == estado.JanelaSegundos);
                        if (regra is not null && estado.Maximo >= regra.Maximo)
                            Bloquear(p, agora + TimeSpan.FromSeconds(regra.JanelaSegundos));
                    }
                }
                if (regras.Count > 0) p.Regras = regras;
            }
            if (status == 429) Bloquear(p, agora + (retryAfter ?? TimeSpan.FromSeconds(60)));
        }
    }

    public IReadOnlyList<RegraLimite> Regras(string chave)
    {
        lock (_trava) return Obter(chave).Regras.ToList();
    }

    static void Bloquear(Politica p, DateTimeOffset ate)
    {
        if (ate > p.BloqueadoAte) p.BloqueadoAte = ate;
    }

    Politica Obter(string chave)
    {
        if (!_politicas.TryGetValue(chave, out var p)) _politicas[chave] = p = new Politica();
        return p;
    }

    // "5:10:60,15:60:300" → regras (o mesmo formato serve para o -State).
    static IEnumerable<RegraLimite> LerTrincas(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) yield break;
        foreach (var parte in texto.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var n = parte.Split(':');
            if (n.Length == 3 && int.TryParse(n[0], out var a) && int.TryParse(n[1], out var b) && int.TryParse(n[2], out var c))
                yield return new RegraLimite(a, b, c);
        }
    }
}
