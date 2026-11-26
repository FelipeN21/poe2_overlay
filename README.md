<p align="center"><img src="assets/logo.png" alt="Pit Overlay's" width="520"></p>

# Pit Overlay's

Overlay para Windows que consulta o preço de itens do **Path of Exile 2** sem sair do jogo.

- **Ctrl+D** com o mouse sobre um item: copia o item, abre o overlay ao lado dele e busca o preço no trade oficial.
- **Ctrl+Shift+Space**: mostra/oculta o overlay.
- **Esc**: fecha o overlay.

No overlay:
- **Liga**: começa na liga do momento; dá para trocar (a escolha fica salva).
- **Corrupção**: corrompido ou não, só corrompidos, só 2× corrompidos ou só não corrompidos (começa igual ao seu item).
- **Identificação**: identificado ou não (padrão), só identificados, ou só não identificados (nesse caso os mods
  não entram na busca, porque o trade não mostra mods de itens não identificados).
- **Moeda**: **Qualquer moeda** (padrão: o site ordena todos os anúncios pelo valor e cada preço aparece na moeda do
  anúncio, ex.: "≈ 2 chaos", "mais barato 3 exalted"), ou só Divine, só Exalted ou só Chaos. Nunca há conversão. O preço aparece na moeda escolhida, sem conversão; trocar faz uma
  busca nova.
- **Mods** (raros e mágicos): cada mod com uma caixinha e campos **mín** e **máx**. Começa com mínimo = 90% do valor
  do item; digite outro valor e aperte Enter (o teclado volta para o jogo). Vazio = sem limite.
- **Armas**: DPS total, físico e elemental (como o jogo mostra; se a qualidade for menor que 20%, também o DPS
  "com 20% de qualidade", que é a conta do trade), com caixinhas opcionais para filtrar por DPS.
- **Busca por DPS** (armas): o botão pergunta quais atributos importam além do DPS e procura armas com DPS
  parecido, ignorando o resto. Útil quando mods raros (fractured, desecrated...) deixam a busca sem resultado.
- Mods fractured e desecrated são buscados sem exigir a mesma origem no outro item.
- Preço aproximado (mediana dos mais baratos), o mais barato, os 5 primeiros anúncios e **Abrir no site**.
- A janela abre ao lado do item e depois fica parada enquanto você clica nela. **Arraste pelo título** para mudar
  de lugar.

**Ícone da bandeja** (perto do relógio): clique nele para o menu — Mostrar overlay, Esconder overlay,
Configurações…, Abrir pasta de configuração e Sair.

**Configurações** (botão ⚙ no overlay ou menu da bandeja → Configurações…):
- atalhos de consultar e de mostrar/ocultar (clique no campo e aperte a combinação);
- liga padrão (liga do momento automática, ou uma fixa);
- ao mudar filtros: buscar na hora, ou esperar você clicar em Buscar (economiza buscas);
- esquema de cores: Dourado, Azul gelo, Esmeralda, Carmesim ou Grafite;
- transparência do fundo do overlay (0% a 70%);
- onde o overlay abre: ao lado do item, ou onde você deixou da última vez.

Tudo fica em `%APPDATA%\Poe2Overlay\config.json`. O jogo precisa estar em **tela cheia em janela**
(windowed fullscreen) e em inglês. O app fica na bandeja do Windows (perto do relógio); é por lá que se sai.

> Ainda não: preço de moedas (mercado de troca).

## Compilar

Precisa do SDK do .NET 10.

```sh
dotnet build
dotnet run --project src/Poe2Overlay.App
dotnet publish src/Poe2Overlay.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publicacao
```

## Estrutura

```
src/
├── Poe2Overlay.App/      WPF: janela, atalhos globais, área de transferência, bandeja
└── Poe2Overlay.Nucleo/   lógica pura: parser, mods → stats, busca, cliente do trade, limite, preço
```

O app usa a API do site oficial de trade, se identifica no User-Agent e respeita os limites que o site manda
(`X-Rate-Limit-*`): espera um pouco na fila e, se o site pedir mais tempo, mostra "aguarde".

Os testes ficam no repositório do harness, que clona este dentro dele.
