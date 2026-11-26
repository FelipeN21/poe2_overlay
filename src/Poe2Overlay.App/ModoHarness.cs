using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Poe2Overlay.App;

// Só existe com --harness <pipe>: o harness controla o app por um named pipe, uma linha por comando.
//   estado | consultar <arquivo.txt> | alternar | esconder | print <arquivo.png> | sair
//   moeda <id> | liga <nome> | mod <índice> on|off | prop <índice> on|off | buscar   (o mesmo que o usuário faz)
//   limites mod|prop <índice> <min|-> <max|->
// Responde "ok", um JSON (estado) ou "erro: ...". Buscas rodam em segundo plano: acompanhe pelo "status".
internal sealed class ModoHarness(AppOverlay app, string nomePipe) : IDisposable
{
    readonly CancellationTokenSource _cts = new();

    public void Iniciar() => Task.Run(LoopAsync);

    async Task LoopAsync()
    {
        var utf8 = new UTF8Encoding(false);
        while (!_cts.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(nomePipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try { await pipe.WaitForConnectionAsync(_cts.Token); }
            catch (OperationCanceledException) { return; }

            using var leitor = new StreamReader(pipe, utf8);
            using var escritor = new StreamWriter(pipe, utf8) { AutoFlush = true };
            try
            {
                while (await leitor.ReadLineAsync(_cts.Token) is { } linha)
                {
                    (string resposta, bool sair) r;
                    try { r = app.Dispatcher.Invoke(() => Executar(linha)); }
                    catch (Exception ex) { r = ("erro: " + ex.Message, false); }
                    await escritor.WriteLineAsync(r.resposta);
                    if (r.sair)
                    {
                        _ = app.Dispatcher.BeginInvoke(app.Sair);
                        return;
                    }
                }
            }
            catch (IOException) { /* o harness desconectou */ }
            catch (OperationCanceledException) { return; }
        }
    }

    (string, bool) Executar(string linha)
    {
        var espaco = linha.IndexOf(' ');
        var comando = espaco < 0 ? linha : linha[..espaco];
        var argumento = espaco < 0 ? "" : linha[(espaco + 1)..];
        switch (comando)
        {
            case "estado":
                return (JsonSerializer.Serialize(new
                {
                    versao = AppOverlay.Versao,
                    visivel = app.Janela.IsVisible,
                    ativa = app.Janela.IsActive,
                    alvos = app.Janela.AlvosNaTela(),
                    posicao = app.Janela.IsVisible ? app.Janela.PosicaoNaTela() : null,
                    titulo = app.Janela.Titulo,
                    raridade = app.Janela.ItemAtual?.Raridade.ToString(),
                    atalhos = new
                    {
                        consultar = app.Config.AtalhoConsultar,
                        consultarOk = app.AtalhoConsultarOk,
                        alternar = app.Config.AtalhoAlternar,
                        alternarOk = app.AtalhoAlternarOk,
                    },
                    esquema = app.Janela.EsquemaAtual,
                    opacidade = app.Janela.OpacidadeAtual,
                    painelDps = new
                    {
                        aberto = app.Janela.PainelDpsAberto,
                        candidatos = app.Janela.CandidatosDps.Select(c => new { indice = c.Indice, texto = c.Texto }).ToArray(),
                    },
                    configuracoes = new
                    {
                        aberta = app.Configuracoes is not null,
                        ativa = app.Configuracoes?.IsActive ?? false,
                        campos = app.Configuracoes?.Ler(),
                        erro = app.Configuracoes?.Erro ?? "",
                        alvos = app.Configuracoes?.AlvosNaTela() ?? [],
                        salva = new
                        {
                            app.Config.Liga, app.Config.Esquema, app.Config.AbrirOverlay, app.Config.PosicaoX, app.Config.PosicaoY,
                            app.Config.BuscaAutomatica, app.Config.OpacidadeFundo, app.Config.CopiaAvancada,
                        },
                    },
                    preco = new
                    {
                        status = app.Preco.Status,
                        liga = app.Preco.Liga,
                        ligas = app.Preco.Ligas.Select(l => l.Id).ToArray(),
                        moeda = app.Preco.Moeda,
                        texto = app.Janela.TextoPreco,
                        anuncios = app.Janela.AnunciosMostrados,
                        total = app.Preco.Resultado?.Total,
                        url = app.Preco.Resultado?.UrlSite,
                        mods = app.Preco.Busca?.Mods.Select(f => new { id = f.Mod.IdStat, texto = f.Mod.Texto, ativo = f.Ativo, min = f.Min, max = f.Max }).ToArray()
                            ?? [],
                        propriedades = app.Preco.Busca?.Propriedades.Select(p => new { id = p.Id, texto = p.Nome, ativo = p.Ativo, min = p.Min, max = p.Max }).ToArray()
                            ?? [],
                        dps = app.Janela.ItemAtual?.Arma?.Dps,
                        identificado = app.Preco.Busca?.Identificado,
                        corrupcao = app.Preco.Busca?.Corrupcao,
                    },
                }), false);
            case "moeda":
                _ = app.Preco.TrocarMoedaAsync(argumento);
                return ("ok", false);
            case "liga":
                _ = app.Preco.TrocarLigaAsync(argumento);
                return ("ok", false);
            case "mod":
                var partes = argumento.Split(' ');
                _ = app.Preco.AlternarModAsync(int.Parse(partes[0]), partes[1] == "on");
                return ("ok", false);
            case "clicar": // clicar fechar|config → botões do cabeçalho
                return (app.Janela.ClicarBotao(argumento) ? "ok" : $"erro: botão '{argumento}' não está na tela", false);
            case "todos": // todos on|off → caixinha "Todos os mods"
                _ = app.Preco.AlternarTodosModsAsync(argumento == "on");
                return ("ok", false);
            case "prop":
                var p = argumento.Split(' ');
                _ = app.Preco.AlternarPropriedadeAsync(int.Parse(p[0]), p[1] == "on");
                return ("ok", false);
            case "identificado": // identificado qualquer|sim|nao
                _ = app.Preco.TrocarIdentificadoAsync(argumento switch { "sim" => "true", "nao" => "false", _ => null });
                return ("ok", false);
            case "corrupcao": // corrupcao qualquer|sim|duas|nao
                _ = app.Preco.TrocarCorrupcaoAsync(argumento is "sim" or "duas" or "nao" ? argumento : null);
                return ("ok", false);
            case "dps": // dps → clica em "Busca por DPS"; dps buscar <índices...> → marca e confirma; dps cancelar
                if (argumento.Length == 0) app.Preco.PedirBuscaPorDps();
                else if (argumento == "cancelar") app.Janela.EsconderPainelDps();
                else if (argumento.StartsWith("buscar"))
                    app.Janela.ConfirmarPainelDps(argumento.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(int.Parse).ToList());
                else return ($"erro: ação desconhecida '{argumento}'", false);
                return ("ok", false);
            case "menu-print":
                Bandeja.SalvarPrintMenu(app, argumento);
                return ("ok", false);
            case "menu": // menu → itens do menu da bandeja; menu <texto> → clica no item (como o usuário)
                var itensMenu = MenuBandeja.Itens(app).Where(i => i is not null).Select(i => i!).ToList();
                if (argumento.Length == 0)
                    return (JsonSerializer.Serialize(itensMenu.Select(i => new { texto = i.Texto, habilitado = i.Habilitado() })), false);
                var escolhido = itensMenu.FirstOrDefault(i => i.Texto == argumento);
                if (escolhido is null) return ($"erro: item de menu '{argumento}' não existe", false);
                if (!escolhido.Habilitado()) return ("erro: item desabilitado", false);
                if (escolhido.Texto.StartsWith("Sair")) return ("ok", true); // responde antes de fechar
                if (escolhido.Texto.StartsWith("Abrir pasta")) return ("ok", false); // não abre o Explorer nos testes
                escolhido.Acao();
                return ("ok", false);
            case "configuracoes": // configuracoes abrir | fechar | salvar <json DadosConfiguracoes> | print <arquivo.png>
                var espacoCfg = argumento.IndexOf(' ');
                var acao = espacoCfg < 0 ? argumento : argumento[..espacoCfg];
                var resto = espacoCfg < 0 ? "" : argumento[(espacoCfg + 1)..];
                switch (acao)
                {
                    case "abrir":
                        app.AbrirConfiguracoes();
                        return ("ok", false);
                    case "fechar":
                        app.Configuracoes?.Close();
                        return ("ok", false);
                    case "salvar":
                        if (app.Configuracoes is null) return ("erro: configurações fechadas", false);
                        var dados = JsonSerializer.Deserialize<DadosConfiguracoes>(resto, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                        var erro = app.Configuracoes.PreencherESalvar(dados);
                        return (erro is null ? "ok" : "erro: " + erro, false);
                    case "print":
                        if (app.Configuracoes is null) return ("erro: configurações fechadas", false);
                        app.Configuracoes.SalvarPrint(resto);
                        return ("ok", false);
                    default:
                        return ($"erro: ação desconhecida '{acao}'", false);
                }
            case "limites": // limites mod|prop <índice> <min|-> <max|->
                var l = argumento.Split(' ');
                double? Valor(string s) => s == "-" ? null : double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
                _ = l[0] == "mod"
                    ? app.Preco.AlterarLimitesModAsync(int.Parse(l[1]), Valor(l[2]), Valor(l[3]))
                    : app.Preco.AlterarLimitesPropriedadeAsync(int.Parse(l[1]), Valor(l[2]), Valor(l[3]));
                return ("ok", false);
            case "buscar":
                _ = app.Preco.BuscarAsync();
                return ("ok", false);
            case "consultar":
                app.MostrarTexto(File.ReadAllText(argumento));
                return ("ok", false);
            case "alternar":
                app.Alternar();
                return ("ok", false);
            case "esconder":
                app.Esconder();
                return ("ok", false);
            case "print":
                app.Janela.SalvarPrint(argumento);
                return ("ok", false);
            case "sair":
                return ("ok", true);
            default:
                return ($"erro: comando desconhecido '{comando}'", false);
        }
    }

    public void Dispose() => _cts.Cancel();
}
