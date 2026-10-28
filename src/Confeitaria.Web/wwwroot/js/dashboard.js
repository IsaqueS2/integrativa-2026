"use strict";
(async () => {
    const { api, money, escape, rows, date, notify } = ui;
    const form = document.getElementById("dashboard-filter");
    form.elements.from.value = ui.dateToInput(`${ui.today().slice(0, 7)}-01`);
    form.elements.to.value = ui.dateToInput(ui.today());
    function render(summary) {
        const cards = [
            ["Vendas brutas", summary.grossSalesCents], ["Descontos", summary.discountsCents],
            ["Taxas das vendas", summary.feesCents], ["Vendas líquidas", summary.netSalesCents],
            ["Recebimentos no período", summary.receiptsCents], ["Pagamentos no período", summary.paymentsCents],
            ["Saldo de caixa geral", summary.cashBalanceCents, "Posição geral ao fim do período, sem filtros de canal ou categoria."],
            ["Resultado operacional estimado", summary.estimatedOperatingResultCents, `${summary.salesWithCost}/${summary.salesCount} vendas com custo. Resultado parcial: ${money(summary.partialOperatingResultCents)}.`],
            ["Contas a receber", summary.accountsReceivableCents], ["Contas a pagar", summary.accountsPayableCents],
            ["Custos diretos conhecidos", summary.knownDirectCostsCents], ["Compras de insumos", summary.suppliesPurchasesCents], ["Despesas operacionais", summary.operatingExpensesCents],
            ["Custos já alocados", summary.allocatedExpensesCents, "Classificados como incluídos no custo das receitas; não são descontados novamente do resultado."],
            ["Aportes", summary.contributionsCents], ["Retiradas pessoais", summary.withdrawalsCents],
            ["Projeção geral de caixa", summary.projectedCashCents, "Previsão simplificada com os compromissos pendentes."],
            ["Reserva mínima configurada", summary.reserveCents]
        ];
        const keyMetrics = new Set(["Vendas líquidas", "Saldo de caixa geral", "Resultado operacional estimado", "Contas a receber"]);
        const renderCard = ([name, value, hint]) => {
            const emphasis = name === "Vendas líquidas" ? "card-featured" : name === "Resultado operacional estimado" ? "card-result" : "";
            return `<dl class="card ${emphasis}"><dt>${escape(name)}</dt><dd>${escape(money(value))}</dd>${hint ? `<small>${escape(hint)}</small>` : ""}</dl>`;
        };
        document.getElementById("dashboard-primary-cards").innerHTML = cards.filter(([name]) => keyMetrics.has(name)).map(renderCard).join("");
        document.getElementById("dashboard-cards").innerHTML = cards.filter(([name]) => !keyMetrics.has(name)).map(renderCard).join("");
        const hasWarnings = Boolean(summary.warnings?.length);
        const notices = hasWarnings
            ? summary.warnings
            : summary.salesCount === 0 ? ["Ainda não há vendas neste período. Registre uma venda para acompanhar o desempenho da confeitaria."]
                : ["Tudo em dia para o período consultado."];
        document.getElementById("dashboard-notices").innerHTML = notices.map(w => `<li class="${hasWarnings ? "notice-warning" : "notice-info"}">${escape(w)}</li>`).join("");
        const channels = summary.channels || [];
        const maxSales = Math.max(0, ...channels.map(channel => channel.netSalesCents));
        document.getElementById("channel-chart").innerHTML = channels.length
            ? channels.map(channel => {
                const width = maxSales > 0 ? Math.max(0, Math.min(100, channel.netSalesCents / maxSales * 100)) : 0;
                return `<div class="channel-chart-item" role="listitem"><div class="channel-chart-label"><span>${escape(channel.name)}</span><strong>${money(channel.netSalesCents)}</strong></div><div class="channel-chart-track" role="img" aria-label="${escape(channel.name)}: ${escape(money(channel.netSalesCents))}"><span style="width:${width}%"></span></div></div>`;
            }).join("")
            : `<p class="empty-state">Ainda não há vendas por canal neste período. Assim que registrar vendas, a comparação aparecerá aqui.</p>`;
        rows("channel-totals", channels.map(channel => `<tr><td>${escape(channel.name)}</td><td class="money">${money(channel.netSalesCents)}</td><td class="money">${money(channel.estimatedMarginCents)}</td><td>${channel.salesWithCost}/${channel.salesCount} vendas</td></tr>`).join(""), 4, "Nenhum canal teve vendas no período selecionado.");
    }
    async function load() {
        const filter = ui.query(form);
        const [summary, ledger] = await Promise.all([api(`/dashboard?${filter}`), api(`/ledger?${filter}&pageSize=100`)]);
        render(summary);
        rows("ledger-rows", ui.items(ledger).map(entry => `<tr><td>${date(entry.date)}</td><td>${escape(ui.label(entry.kind))}${entry.isReversal ? " · estorno" : ""}</td><td>${escape(entry.description || "—")}</td><td class="money ${entry.signedCents < 0 ? "negative" : "positive"}">${money(entry.signedCents)}</td></tr>`).join(""), 4, "Ainda não há movimentações de caixa neste período.");
        document.getElementById("ledger-count").textContent = `${ui.items(ledger).length} de ${ledger.total ?? ui.items(ledger).length} movimentações. O CSV inclui todos os registros do filtro.`;
        document.getElementById("export-csv").href = `/api/v1/exports/ledger.csv?${filter}`;
        document.getElementById("export-summary").href = `/api/v1/exports/summary.csv?${filter}`;
    }
    ui.listenForm("dashboard-filter", load);
    document.getElementById("today-summary").addEventListener("click", async () => {
        try {
            form.elements.from.value = ui.dateToInput(ui.today()); form.elements.to.value = ui.dateToInput(ui.today());
            form.elements.channelId.value = ""; form.elements.paymentMethodId.value = ""; form.elements.categoryId.value = "";
            await load(); notify("Resumo do dia consultado. Nenhum lançamento foi criado.");
        } catch (error) { notify(error.message, true); }
    });
    try { await ui.loadCatalogs(); await load(); } catch (error) { notify(error.message, true); }
})();
