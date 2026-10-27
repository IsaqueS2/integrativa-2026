"use strict";
(async () => {
    const { api, money, escape, rows, date, notify } = ui;
    const form = document.getElementById("dashboard-filter");
    form.elements.from.value = `${ui.today().slice(0, 7)}-01`;
    form.elements.to.value = ui.today();
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
        document.getElementById("dashboard-cards").innerHTML = cards.map(([name, value, hint]) => `<dl class="card"><dt>${escape(name)}</dt><dd>${escape(money(value))}</dd>${hint ? `<small>${escape(hint)}</small>` : ""}</dl>`).join("");
        document.getElementById("dashboard-notices").innerHTML = (summary.warnings?.length ? summary.warnings : ["Nenhum aviso para esta consulta."]).map(w => `<li>${escape(w)}</li>`).join("");
        rows("channel-totals", (summary.channels || []).map(channel => `<tr><td>${escape(channel.name)}</td><td class="money">${money(channel.netSalesCents)}</td><td class="money">${money(channel.estimatedMarginCents)}</td><td>${channel.salesWithCost}/${channel.salesCount} vendas</td></tr>`).join(""));
    }
    async function load() {
        const filter = ui.query(form);
        const [summary, ledger] = await Promise.all([api(`/dashboard?${filter}`), api(`/ledger?${filter}&pageSize=100`)]);
        render(summary);
        rows("ledger-rows", ui.items(ledger).map(entry => `<tr><td>${date(entry.date)}</td><td>${escape(ui.label(entry.kind))}${entry.isReversal ? " · estorno" : ""}</td><td>${escape(entry.description || "—")}</td><td class="money ${entry.signedCents < 0 ? "negative" : "positive"}">${money(entry.signedCents)}</td></tr>`).join(""));
        document.getElementById("ledger-count").textContent = `${ui.items(ledger).length} de ${ledger.total ?? ui.items(ledger).length} movimentações. O CSV inclui todos os registros do filtro.`;
        document.getElementById("export-csv").href = `/api/v1/exports/ledger.csv?${filter}`;
        document.getElementById("export-summary").href = `/api/v1/exports/summary.csv?${filter}`;
    }
    ui.listenForm("dashboard-filter", load);
    document.getElementById("today-summary").addEventListener("click", async () => {
        try {
            form.elements.from.value = ui.today(); form.elements.to.value = ui.today();
            form.elements.channelId.value = ""; form.elements.paymentMethodId.value = ""; form.elements.categoryId.value = "";
            await load(); notify("Resumo do dia consultado. Nenhum lançamento foi criado.");
        } catch (error) { notify(error.message, true); }
    });
    try { await ui.loadCatalogs(); await load(); } catch (error) { notify(error.message, true); }
})();
