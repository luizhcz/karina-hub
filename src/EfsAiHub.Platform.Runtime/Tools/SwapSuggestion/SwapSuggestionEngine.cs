using Microsoft.Extensions.Logging;

namespace EfsAiHub.Platform.Runtime.Tools.SwapSuggestion;

/// <summary>
/// Motor de sugestão de troca de ativos. Auto-contido: depende só dos 3 endpoints
/// (posição, ativo, recomendação). Produz um plano estratégico por peso (soma 100%)
/// e os trades executáveis em quantidade (múltiplos de 5). Ver <see cref="SuggestAsync"/>
/// e o bloco de lógica em &lt;remarks&gt;.
/// </summary>
/// <remarks>
/// ═══════════════════════════════ LÓGICA DA SUGESTÃO ═══════════════════════════════
///
/// ENTRADAS (3 endpoints)
///   • posição     : carteira do cliente — {symbol, totalQuantity, volume}.
///   • ativo       : catálogo — {symbol, nome, securityType, sector, priceAtual}.
///   • recomendação: visão da casa — {symbol, sector, recomendacao, potential, inTopPicks}.
///                   recomendacao ∈ {compra, venda, neutro, semcobertura}; potential = upside %.
///
/// PRINCÍPIO
///   Alinhar a carteira à visão da casa SEM mudar a cara dela: preservar o peso dos
///   setores que a casa cobre e migrar só para nomes de maior convicção (top picks).
///   Nunca aumentar uma posição que o cliente já tem — sempre recomendar um ativo novo.
///   Toda quantidade negociada em lotes múltiplos de 5.
///
/// PASSO 1 — Enriquecimento e peso
///   Junta posição × ativo (setor, preço) × recomendação. peso(holding) = volume / Σvolume.
///   Sem recomendação para o símbolo ⇒ tratado como 'semcobertura'.
///
/// PASSO 2 — Classificação (manter vs sugerir troca)
///   • MANTÉM   : recomendacao = compra (alinhado à casa) — "consolida carteira parcial".
///   • SUGERE   : venda, semcobertura e neutro — todos recebem sugestão de troca.
///                neutro pode ser preservado via filtro KeepNeutral.
///   • PROIBIDO : setores em ProhibitedSectors são sempre vendidos e nunca comprados.
///
/// PASSO 3 — Cobertura do setor
///   Setor é "coberto" quando a casa tem ≥1 top pick nele (inTopPicks e não-venda).
///   "Sem cobertura" = setor que o cliente tem mas a casa não cobre.
///
/// PASSO 4 — Swap intra-setor (setor COBERTO)  →  preserva o peso do setor
///   Vende o ativo desalinhado e compra o top pick do MESMO setor (maior potential que o
///   cliente NÃO tem) com o peso liberado. O peso total do setor fica inalterado.
///
/// PASSO 5 — Reserva
///   O peso vendido que NÃO pode ser reinvestido no próprio setor vira "reserva". Origens:
///   setor sem cobertura, setor proibido, ou setor cujo único top pick o cliente já tem.
///
/// PASSO 6 — Distribuição da reserva (POOLED por ranking GLOBAL)
///   Cada venda da reserva é distribuída no cesto dos melhores top picks globais por potential
///   (proporcional, ou peso igual via ReserveEqualWeight), limitado a MaxReserveNames — 1 venda
///   → N compras. INVARIANTE load-bearing: o universo exclui ativos já possuídos e os já
///   comprados no passo 4 — só entram nomes que o cliente não tem. (Sem candidato elegível ⇒
///   reserva em caixa.)
///
/// PASSO 7 — Validação da soma 100% (plano estratégico)
///   Por construção todo peso é (mantido) ou (re-investido no setor) ou (redistribuído pela
///   reserva) ⇒ a soma dos pesos finais é 100% (com tolerância de arredondamento).
///
/// PASSO 8 — Execução em lotes de 5 e resíduo de caixa
///   Cada compra é dimensionada em quantidade = ⌊volumeAlvo / preço / 5⌋ × 5 — arredonda
///   PRA BAIXO, nunca estoura o caixa liberado pela venda. Venda = saída total da posição.
///   O floor deixa um pequeno RESÍDUO DE CAIXA (tipicamente &lt; 1%), exposto em
///   Summary.CashResidual; hoje ele permanece como saldo.
///
/// IMPORTÂNCIA — cada sugestão recebe priority = peso% × urgência × (1 + potential/100),
///   com urgência = proibido(4) > venda(3) > semcobertura(2) > neutro(1). Os Swaps saem
///   ordenados da mais importante para a menos (PriorityRank 1 = mais importante).
///
/// SAÍDA (duas camadas)
///   • Swaps  : plano estratégico por peso, ORDENADO por importância — cada venda → 1..N compras, soma 100%.
///   • Trades : ordens executáveis — quantidade em lote de 5, volume = quantidade × preço.
///
/// FILTROS (SwapFilters): ProhibitedSectors, KeepNeutral, MaxReserveNames, ReserveEqualWeight.
/// ══════════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class SwapSuggestionEngine
{
    private const double WeightTolerance = 0.0005; // 0,05% de folga pra arredondamento
    private const decimal Lot = 5m;                // múltiplo de 5

    private readonly IPositionEndpoint _positions;
    private readonly IAssetEndpoint _assets;
    private readonly IRecommendationEndpoint _recs;
    private readonly ILogger<SwapSuggestionEngine> _logger;

    public SwapSuggestionEngine(
        IPositionEndpoint positions,
        IAssetEndpoint assets,
        IRecommendationEndpoint recs,
        ILogger<SwapSuggestionEngine> logger)
    {
        _positions = positions;
        _assets = assets;
        _recs = recs;
        _logger = logger;
    }

    public async Task<SwapSuggestionResult> SuggestAsync(
        string account, SwapFilters? filters = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("account é obrigatório.", nameof(account));
        filters ??= new SwapFilters();

        var positions = await _positions.GetPositionsAsync(account, ct).ConfigureAwait(false);
        if (positions.Count == 0)
            return new SwapSuggestionResult { Account = account, AsOf = DateTime.UtcNow };

        var assetList = await _assets.GetAssetsAsync(ct).ConfigureAwait(false);
        var recList = await _recs.GetRecommendationsAsync(ct).ConfigureAwait(false);

        var assetBySymbol = ToDict(assetList, a => a.Symbol);
        var recBySymbol = ToDict(recList, r => r.Symbol);
        var prohibited = new HashSet<string>(
            filters.ProhibitedSectors.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);

        var totalVolume = positions.Sum(p => p.Volume);
        var notes = new List<string>();

        // ── Passo 1: enriquecer holdings (setor + preço + recomendação + peso) ──
        var holdings = positions.Select(p =>
        {
            assetBySymbol.TryGetValue(p.Symbol, out var asset);
            recBySymbol.TryGetValue(p.Symbol, out var rec);
            var sector = asset?.Sector ?? rec?.Sector ?? "Desconhecido";
            var price = asset?.PriceAtual ?? (p.TotalQuantity > 0 ? p.Volume / p.TotalQuantity : 0m);
            return new Holding(
                Symbol: p.Symbol, Nome: asset?.Nome ?? p.Symbol, Sector: sector,
                Volume: p.Volume, TotalQuantity: p.TotalQuantity, Price: price,
                Weight: totalVolume > 0 ? (double)(p.Volume / totalVolume) : 0d,
                Rec: rec is null ? Rec.SemCobertura : Rec.Normalize(rec.Recomendacao));
        }).ToList();

        // ── Passo 2: top picks por setor + cobertura ────────────────────────────
        var heldSymbols = new HashSet<string>(holdings.Select(h => h.Symbol), StringComparer.OrdinalIgnoreCase);
        var topPicks = recList
            .Where(r => r.InTopPicks && Rec.Normalize(r.Recomendacao) != Rec.Venda)
            .ToList();
        var topPicksBySector = topPicks
            .GroupBy(r => r.Sector, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Potential).ToList(), StringComparer.OrdinalIgnoreCase);

        // Mantém só os alinhados (compra). venda / semcobertura / neutro recebem
        // sugestão de troca; neutro pode ser preservado via filtro KeepNeutral.
        bool IsSell(Holding h) =>
            prohibited.Contains(h.Sector)
            || (h.Rec != Rec.Compra && !(filters.KeepNeutral && h.Rec == Rec.Neutro));

        // Urgência da venda (peso na importância da sugestão): proibido > venda >
        // semcobertura > neutro.
        int SeverityOf(Holding h) =>
            prohibited.Contains(h.Sector) ? 4
            : h.Rec == Rec.Venda ? 3 : h.Rec == Rec.SemCobertura ? 2 : h.Rec == Rec.Neutro ? 1 : 0;
        string SeverityLabel(Holding h) =>
            prohibited.Contains(h.Sector) ? "proibido"
            : h.Rec == Rec.Venda ? "venda" : h.Rec == Rec.SemCobertura ? "semcobertura"
            : h.Rec == Rec.Neutro ? "neutro" : h.Rec;

        // ── Passos 3–6: classificar, swap intra-setor, separar reserva ──────────
        var swaps = new List<SwapPlanItem>();
        var trades = new List<TradeAction>();
        var buys = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase); // symbol → peso comprado (estratégico)
        var reserveSells = new List<Holding>();
        var boughtSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double keptWeight = 0, intraWeight = 0;

        foreach (var h in holdings)
        {
            if (!IsSell(h)) { keptWeight += h.Weight; continue; } // consolidar carteira parcial (mantém alinhados)

            var sectorTopPick = prohibited.Contains(h.Sector)
                ? null
                : topPicksBySector.TryGetValue(h.Sector, out var picks)
                    ? picks.FirstOrDefault(r => !heldSymbols.Contains(r.Symbol) && !boughtSymbols.Contains(r.Symbol))
                    : null;

            if (sectorTopPick is not null)
            {
                // substituir mantendo o peso do setor (swap 1×1 intra-setor)
                intraWeight += h.Weight;
                boughtSymbols.Add(sectorTopPick.Symbol);
                buys[sectorTopPick.Symbol] = buys.GetValueOrDefault(sectorTopPick.Symbol) + h.Weight;

                trades.Add(BuildSell(h, $"recomendação {h.Rec}"));
                var buyTrade = BuildBuy(sectorTopPick.Symbol, h.Sector, h.Volume, totalVolume, assetBySymbol,
                    $"top pick do setor {h.Sector} (potential {sectorTopPick.Potential}%)");
                trades.Add(buyTrade);
                swaps.Add(new SwapPlanItem
                {
                    Severity = SeverityLabel(h),
                    Priority = PriorityScore(Pct(h.Weight), SeverityOf(h), sectorTopPick.Potential),
                    SellSymbol = h.Symbol, SellSector = h.Sector, WeightPct = Pct(h.Weight), Volume = h.Volume,
                    Reason = $"{h.Symbol} ({h.Rec}) → top pick do setor {h.Sector}, peso do setor preservado",
                    Targets =
                    [
                        new SwapTarget
                        {
                            Symbol = sectorTopPick.Symbol, Nome = NomeOf(assetBySymbol, sectorTopPick.Symbol),
                            Sector = h.Sector, WeightPct = buyTrade.WeightPct, Volume = buyTrade.Volume,
                            Potential = sectorTopPick.Potential, Kind = "intra_sector",
                        }
                    ],
                });
            }
            else
            {
                // setor sem cobertura (ou proibido, ou top pick do setor já possuído) → reserva
                reserveSells.Add(h);
            }
        }

        // ── Passo 7: distribuir a reserva nos melhores top picks globais ────────
        var reserveWeight = reserveSells.Sum(h => h.Weight);
        double distributedReserve = 0;
        if (reserveWeight > 0)
        {
            // Princípio: setor do cliente sem cobertura → NUNCA aumentar posição já
            // existente; recomendar um ativo que ele NÃO tem, por ranking global.
            var candidates = topPicks
                .Where(r => !heldSymbols.Contains(r.Symbol)
                         && !boughtSymbols.Contains(r.Symbol)
                         && !prohibited.Contains(r.Sector))
                .OrderByDescending(r => r.Potential) // ranking global
                .ToList();

            if (filters.MaxReserveNames > 0 && candidates.Count > filters.MaxReserveNames)
                candidates = candidates.Take(filters.MaxReserveNames).ToList();

            if (candidates.Count == 0)
            {
                notes.Add($"Reserva de {Pct(reserveWeight):0.##}% sem destino (nenhum top pick global elegível) — manter em caixa.");
            }
            else
            {
                // POOLED: cada venda da reserva é distribuída no cesto dos melhores top picks
                // globais (proporcional ao potential, ou peso igual via ReserveEqualWeight) —
                // 1 venda → N compras. O capital sem cobertura vai para as melhores ideias da casa.
                var fractions = AllocationFractions(candidates, filters.ReserveEqualWeight);
                var bestPotential = candidates[0].Potential; // ganho do cesto, para o ranking de importância

                foreach (var sell in reserveSells)
                {
                    trades.Add(BuildSell(sell, sell.Rec == Rec.SemCobertura ? "setor sem cobertura da casa" : $"recomendação {sell.Rec}"));
                    var targets = new List<SwapTarget>(candidates.Count);
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var tp = candidates[i];
                        var w = sell.Weight * fractions[i];
                        buys[tp.Symbol] = buys.GetValueOrDefault(tp.Symbol) + w;
                        targets.Add(new SwapTarget
                        {
                            Symbol = tp.Symbol, Nome = NomeOf(assetBySymbol, tp.Symbol), Sector = tp.Sector,
                            WeightPct = Pct(w), Volume = RoundVol(totalVolume, w), Potential = tp.Potential, Kind = "reserve",
                        });
                    }
                    swaps.Add(new SwapPlanItem
                    {
                        Severity = SeverityLabel(sell),
                        Priority = PriorityScore(Pct(sell.Weight), SeverityOf(sell), bestPotential),
                        SellSymbol = sell.Symbol, SellSector = sell.Sector, WeightPct = Pct(sell.Weight), Volume = sell.Volume,
                        Reason = $"{sell.Symbol} ({sell.Rec}) — setor sem cobertura; reserva distribuída por ranking global",
                        Targets = targets,
                    });
                }
                distributedReserve = reserveWeight;

                // BUYs agregados da reserva (1 ordem por top pick), em lote de 5
                foreach (var tp in candidates)
                    trades.Add(BuildBuy(tp.Symbol, tp.Sector, RoundVol(totalVolume, buys[tp.Symbol]), totalVolume, assetBySymbol,
                        $"top pick global (potential {tp.Potential}%) — recebe reserva"));
            }
        }

        // ── Passo 8: validar soma 100% (estratégico) + resíduo de execução ──────
        var totalAfter = keptWeight + intraWeight + distributedReserve;
        var valid = Math.Abs(totalAfter - 1.0) <= WeightTolerance;
        if (!valid && distributedReserve < reserveWeight)
            notes.Add("Soma estratégica < 100% porque parte da reserva ficou sem destino (ver nota acima).");

        var soldVol = trades.Where(t => t.Side == "SELL").Sum(t => t.Volume);
        var boughtVol = trades.Where(t => t.Side == "BUY").Sum(t => t.Volume);
        var cashResidual = Math.Round(soldVol - boughtVol, 2);
        if (cashResidual != 0)
            notes.Add($"Resíduo de caixa R$ {cashResidual:0.00} do arredondamento de quantidade ao múltiplo de 5.");

        // Classifica as sugestões da mais importante para a menos importante.
        var rankedSwaps = swaps.OrderByDescending(s => s.Priority).ToList();
        for (int i = 0; i < rankedSwaps.Count; i++)
            rankedSwaps[i].PriorityRank = i + 1;

        return new SwapSuggestionResult
        {
            Account = account,
            AsOf = DateTime.UtcNow,
            TotalVolume = totalVolume,
            Swaps = rankedSwaps,
            Trades = trades,
            Summary = new SwapSummary
            {
                KeptWeightPct = Pct(keptWeight),
                IntraSectorWeightPct = Pct(intraWeight),
                ReserveWeightPct = Pct(reserveWeight),
                SectorWeightsBefore = holdings
                    .GroupBy(h => h.Sector, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => Pct(g.Sum(x => x.Weight)), StringComparer.OrdinalIgnoreCase),
                SectorWeightsAfter = BuildAfterSectors(holdings, IsSell, buys, recBySymbol, assetBySymbol),
                WeightsValid = valid,
                TotalWeightPct = Pct(totalAfter),
                CashResidual = cashResidual,
                Notes = notes,
            },
        };
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed record Holding(
        string Symbol, string Nome, string Sector, decimal Volume, decimal TotalQuantity, decimal Price, double Weight, string Rec);

    private static Dictionary<string, T> ToDict<T>(IEnumerable<T> items, Func<T, string> key) =>
        items.GroupBy(key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    // Importância da sugestão = impacto (peso%) × urgência (severidade) × ganho
    // (1 + potential do destino). Maior = mais importante.
    private static double PriorityScore(double weightPct, int severity, decimal targetPotential) =>
        Math.Round(weightPct * severity * (1 + (double)targetPotential / 100), 2);

    // Frações de distribuição da reserva (pooled): proporcional ao potential, ou peso igual.
    private static double[] AllocationFractions(IReadOnlyList<SwapRecommendation> picks, bool equal)
    {
        if (equal) return Enumerable.Repeat(1.0 / picks.Count, picks.Count).ToArray();
        var totalPot = picks.Sum(p => (double)p.Potential);
        if (totalPot <= 0) return Enumerable.Repeat(1.0 / picks.Count, picks.Count).ToArray();
        return picks.Select(p => (double)p.Potential / totalPot).ToArray();
    }

    private static Dictionary<string, double> BuildAfterSectors(
        List<Holding> holdings, Func<Holding, bool> isSell, Dictionary<string, double> buys,
        Dictionary<string, SwapRecommendation> recBySymbol, Dictionary<string, SwapAsset> assetBySymbol)
    {
        var after = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        void Add(string sector, double w) => after[sector] = after.GetValueOrDefault(sector) + w;

        foreach (var h in holdings)
            if (!isSell(h)) Add(h.Sector, h.Weight); // mantidos

        foreach (var (sym, w) in buys)
        {
            var sector = assetBySymbol.TryGetValue(sym, out var a) ? a.Sector
                       : recBySymbol.TryGetValue(sym, out var r) ? r.Sector : "Desconhecido";
            Add(sector, w);
        }
        return after.ToDictionary(kv => kv.Key, kv => Pct(kv.Value), StringComparer.OrdinalIgnoreCase);
    }

    // Compra dimensionada em lotes múltiplos de 5 (arredonda PRA BAIXO — nunca
    // estoura o caixa liberado pela venda). Volume executado = quantidade × preço.
    private static TradeAction BuildBuy(
        string symbol, string sector, decimal targetVolume, decimal totalVolume,
        Dictionary<string, SwapAsset> assets, string reason)
    {
        var price = assets.TryGetValue(symbol, out var a) ? a.PriceAtual : 0m;
        var qty = price > 0 ? Math.Floor(targetVolume / price / Lot) * Lot : 0m;
        var execVol = Math.Round(qty * price, 2);
        var w = totalVolume > 0 ? (double)(execVol / totalVolume) : 0d;
        return new TradeAction
        {
            Side = "BUY", Symbol = symbol, Nome = assets.TryGetValue(symbol, out var aa) ? aa.Nome : symbol,
            Sector = sector, WeightPct = Pct(w), Quantity = qty, Price = price, Volume = execVol, Reason = reason,
        };
    }

    // Venda = saída total da posição (quantidade já é o que o cliente tem).
    private static TradeAction BuildSell(Holding h, string reason) => new()
    {
        Side = "SELL", Symbol = h.Symbol, Nome = h.Nome, Sector = h.Sector,
        WeightPct = Pct(h.Weight), Quantity = h.TotalQuantity, Price = h.Price, Volume = h.Volume, Reason = reason,
    };

    private static string NomeOf(Dictionary<string, SwapAsset> assets, string symbol) =>
        assets.TryGetValue(symbol, out var a) ? a.Nome : symbol;

    private static decimal RoundVol(decimal total, double weight) => Math.Round(total * (decimal)weight, 2);
    private static double Pct(double w) => Math.Round(w * 100, 2);
}
