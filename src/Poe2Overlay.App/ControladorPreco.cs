using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Poe2Overlay.Nucleo.Busca;
using Poe2Overlay.Nucleo.Itens;
using Poe2Overlay.Nucleo.Precos;
using Poe2Overlay.Nucleo.Trade;

namespace Poe2Overlay.App;

// Cuida da busca de preço do item mostrado: liga, moeda, mods ligados e o resultado.
// Cada mudança feita pelo usuário (moeda, liga, mod, "buscar de novo") dispara uma nova busca.
internal sealed class ControladorPreco
{
    readonly ServicoPreco _servico;
    readonly JanelaOverlay _janela;
    readonly Configuracao _config;
    readonly Action<Exception> _registrarErro;
    readonly Task _carregandoLigas;
    CancellationTokenSource? _cts;

    public IReadOnlyList<Liga> Ligas { get; private set; } = [];
    public string Liga { get; private set; }
    public string Moeda => _config.Moeda;
    public BuscaItem? Busca { get; private set; }
    public ResultadoPreco? Resultado { get; private set; }

    // "", "preparando", "buscando", "ok", "limite", "erro", "nao-suportado", "pendente" (busca manual)
    public string Status { get; private set; } = "";

    public ControladorPreco(ServicoPreco servico, JanelaOverlay janela, Configuracao config, Action<Exception> registrarErro)
    {
        _servico = servico;
        _janela = janela;
        _config = config;
        _registrarErro = registrarErro;
        Liga = config.Liga.Length > 0 ? config.Liga : "Standard";

        _janela.DefinirMoeda(Moeda);
        _janela.MoedaEscolhida += m => _ = TrocarMoedaAsync(m);
        _janela.LigaEscolhida += l => _ = TrocarLigaAsync(l);
        _janela.IdentificadoEscolhido += v => _ = TrocarIdentificadoAsync(v);
        _janela.CorrupcaoEscolhida += v => _ = TrocarCorrupcaoAsync(v);
        _janela.ModAlternado += (i, ativo) => _ = AlternarModAsync(i, ativo);
        _janela.PropriedadeAlternada += (i, ativo) => _ = AlternarPropriedadeAsync(i, ativo);
        _janela.TodosModsAlternados += ativo => _ = AlternarTodosModsAsync(ativo);
        _janela.LimitesModAlterados += (i, min, max) => _ = AlterarLimitesModAsync(i, min, max);
        _janela.LimitesPropriedadeAlterados += (i, min, max) => _ = AlterarLimitesPropriedadeAsync(i, min, max);
        _janela.BuscaPorDpsPedida += PedirBuscaPorDps;
        _janela.BuscaPorDpsConfirmada += escolhidos => _ = ConfirmarBuscaPorDpsAsync(escolhidos);
        _janela.BuscarDeNovo += () => _ = BuscarAsync();
        _janela.AbrirSite += AbrirSite;

        _carregandoLigas = CarregarLigasAsync();
        _ = PreCarregarDadosAsync();
    }

    async Task CarregarLigasAsync()
    {
        try
        {
            Ligas = await _servico.LigasAsync();
            var atual = Nucleo.Trade.Ligas.Atual(Ligas);
            Liga = _config.Liga.Length > 0 && Ligas.Any(l => l.Id == _config.Liga) ? _config.Liga : atual;
        }
        catch (Exception ex) when (EhFalhaDeRede(ex))
        {
            Ligas = [new Liga(Liga, Liga)]; // sem internet agora: fica com a da config (ou Standard)
        }
        _janela.DefinirLigas(Ligas, Liga);
    }

    // Stats e bases são grandes (~1 MB): baixa logo ao abrir, não na primeira consulta.
    async Task PreCarregarDadosAsync()
    {
        try { await _servico.CarregarDadosAsync(); }
        catch (Exception ex) when (EhFalhaDeRede(ex)) { /* tenta de novo na consulta */ }
    }

    public async Task NovoItemAsync(Item item)
    {
        _cts?.Cancel();
        Busca = null;
        Resultado = null;
        Status = "preparando";
        _janela.MostrarItem(item);
        try
        {
            await _carregandoLigas;
            var busca = await _servico.PrepararAsync(item, _config.MargemMods);
            if (!ReferenceEquals(_janela.ItemAtual, item)) return; // outro item chegou enquanto preparava
            Busca = busca;
            _janela.MostrarBusca(busca);
            if (busca.NaoSuportada is not null)
            {
                Status = "nao-suportado";
                return;
            }
            await BuscarAsync();
        }
        catch (Exception ex)
        {
            Falhou(ex);
        }
    }

    public Task TrocarMoedaAsync(string moeda)
    {
        if (!Moedas.Valida(moeda) || moeda == _config.Moeda) return Task.CompletedTask;
        _config.Moeda = moeda;
        _config.Salvar();
        _janela.DefinirMoeda(moeda);
        return AposMudanca();
    }

    public Task TrocarLigaAsync(string liga)
    {
        if (liga == Liga || string.IsNullOrWhiteSpace(liga)) return Task.CompletedTask;
        Liga = liga;
        // Guardar vazio quando é a liga do momento: na próxima liga, o overlay acompanha sozinho.
        _config.Liga = Ligas.Count > 0 && liga == Nucleo.Trade.Ligas.Atual(Ligas) ? "" : liga;
        _config.Salvar();
        if (Ligas.All(l => l.Id != liga)) Ligas = [.. Ligas, new Liga(liga, liga)];
        _janela.DefinirLigas(Ligas, Liga);
        return AposMudanca();
    }

    // Identificação (null = qualquer, "true", "false"). Vale só para o item atual.
    public Task TrocarIdentificadoAsync(string? valor)
    {
        if (Busca is not { NaoSuportada: null } busca || busca.Identificado == valor) return Task.CompletedTask;
        busca.Identificado = valor;
        _janela.MostrarBusca(busca);
        return AposMudanca();
    }

    // Corrupção (null = qualquer, "sim", "duas", "nao"). Vale só para o item atual.
    public Task TrocarCorrupcaoAsync(string? valor)
    {
        if (Busca is not { NaoSuportada: null } busca || busca.Corrupcao == valor) return Task.CompletedTask;
        busca.Corrupcao = valor;
        _janela.MostrarBusca(busca);
        return AposMudanca();
    }

    // Liga padrão escolhida nas configurações ("" = liga do momento). Grava e, se mudou, busca de novo.
    public Task DefinirLigaPadraoAsync(string ligaConfig)
    {
        _config.Liga = ligaConfig;
        _config.Salvar();
        var alvo = ligaConfig.Length > 0 ? ligaConfig : Nucleo.Trade.Ligas.Atual(Ligas);
        if (alvo == Liga) return Task.CompletedTask;
        Liga = alvo;
        if (Ligas.All(l => l.Id != alvo)) Ligas = [.. Ligas, new Liga(alvo, alvo)];
        _janela.DefinirLigas(Ligas, Liga);
        return AposMudanca();
    }

    public Task AlternarModAsync(int indice, bool ativo)
    {
        if (Busca is null || indice < 0 || indice >= Busca.Mods.Count || Busca.Mods[indice].Ativo == ativo) return Task.CompletedTask;
        Busca.Mods[indice].Ativo = ativo;
        _janela.MostrarBusca(Busca);
        return AposMudanca();
    }

    // "Todos os mods": marca ou desmarca todos de uma vez (uma busca só). Filtros de DPS não mudam.
    public Task AlternarTodosModsAsync(bool ativo)
    {
        if (Busca is null || Busca.Mods.Count == 0 || Busca.Mods.All(f => f.Ativo == ativo))
        {
            if (Busca is not null) _janela.MostrarBusca(Busca); // redesenha a caixinha no estado certo
            return Task.CompletedTask;
        }
        foreach (var f in Busca.Mods) f.Ativo = ativo;
        _janela.MostrarBusca(Busca);
        return AposMudanca();
    }

    public Task AlternarPropriedadeAsync(int indice, bool ativo)
    {
        if (Busca is null || indice < 0 || indice >= Busca.Propriedades.Count || Busca.Propriedades[indice].Ativo == ativo)
            return Task.CompletedTask;
        Busca.Propriedades[indice].Ativo = ativo;
        _janela.MostrarBusca(Busca);
        return AposMudanca();
    }

    // Mín/máx digitados no overlay (null = sem limite). Mexer no valor liga o filtro.
    public Task AlterarLimitesModAsync(int indice, double? min, double? max)
    {
        if (Busca is null || indice < 0 || indice >= Busca.Mods.Count) return Task.CompletedTask;
        var f = Busca.Mods[indice];
        if (f.Min == min && f.Max == max) return Task.CompletedTask;
        f.Min = min;
        f.Max = max;
        if (min is not null || max is not null) f.Ativo = true;
        _janela.MostrarBusca(Busca);
        return AposMudanca();
    }

    public Task AlterarLimitesPropriedadeAsync(int indice, double? min, double? max)
    {
        if (Busca is null || indice < 0 || indice >= Busca.Propriedades.Count) return Task.CompletedTask;
        var p = Busca.Propriedades[indice];
        if (p.Min == min && p.Max == max) return Task.CompletedTask;
        p.Min = min;
        p.Max = max;
        if (min is not null || max is not null) p.Ativo = true;
        _janela.MostrarBusca(Busca);
        return AposMudanca();
    }

    // Mudou moeda/liga/mod/mín/máx: busca já (padrão) ou espera o "Buscar" (busca manual nas configurações).
    Task AposMudanca()
    {
        if (_config.BuscaAutomatica) return BuscarAsync();
        if (Busca is not { NaoSuportada: null }) return Task.CompletedTask;
        _cts?.Cancel();
        Status = "pendente";
        _janela.MostrarPendente();
        return Task.CompletedTask;
    }

    // "Busca por DPS": mostra os atributos que dá para considerar junto com o DPS.
    public void PedirBuscaPorDps()
    {
        if (Busca is not { NaoSuportada: null } busca || busca.Item.Arma is null) return;
        _janela.MostrarPainelDps(busca, MontadorBusca.CandidatosBuscaPorDps(busca));
    }

    // Escolheu os atributos: liga só DPS total + eles, e busca (é um clique explícito em "Buscar", busca sempre).
    public Task ConfirmarBuscaPorDpsAsync(IReadOnlyCollection<int> escolhidos)
    {
        if (Busca is not { NaoSuportada: null } busca || busca.Item.Arma is null) return Task.CompletedTask;
        MontadorBusca.AplicarBuscaPorDps(busca, escolhidos);
        _janela.MostrarBusca(busca);
        return BuscarAsync();
    }

    public async Task BuscarAsync()
    {
        if (Busca is not { NaoSuportada: null } busca) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Status = "buscando";
        _janela.MostrarBuscando(Liga, Moeda);
        try
        {
            var r = await _servico.BuscarAsync(busca, Liga, Moeda, cts.Token);
            if (cts.IsCancellationRequested) return;
            Resultado = r;
            Status = "ok";
            _janela.MostrarResultado(r, Moeda);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // substituída por uma busca mais nova
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) Falhou(ex);
        }
    }

    void Falhou(Exception ex)
    {
        switch (ex)
        {
            case LimiteAtingidoException limite:
                Status = "limite";
                _janela.MostrarStatus(limite.Message, aviso: true);
                break;
            case ErroTradeException erro:
                Status = "erro";
                _janela.MostrarStatus(erro.Message, aviso: true);
                break;
            case var _ when EhFalhaDeRede(ex):
                Status = "erro";
                _janela.MostrarStatus("Sem conexão com o site de trade. Confira a internet e tente de novo.", aviso: true);
                break;
            default:
                Status = "erro";
                _registrarErro(ex);
                _janela.MostrarStatus("Erro inesperado na busca (registrado em erros.log).", aviso: true);
                break;
        }
    }

    void AbrirSite()
    {
        if (Resultado is null) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Resultado.UrlSite) { UseShellExecute = true });
    }

    static bool EhFalhaDeRede(Exception ex) =>
        ex is HttpRequestException || ex is TaskCanceledException { InnerException: TimeoutException };
}
