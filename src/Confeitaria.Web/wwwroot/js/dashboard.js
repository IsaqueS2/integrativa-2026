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
            ["Contas a receber", summary.accountsReceivableCents], ["Contas a pagar", summary.accountsPayableCents],
            ["Custos diretos conhecidos", summary.knownDirectCostsCents], ["Compras de insumos", summary.suppliesPurchasesCents],
            ["Custos já alocados", summary.allocatedExpensesCents, "Classificados como incluídos no custo das receitas; não são descontados novamente do resultado."],
            ["Aportes", summary.contributionsCents], ["Retiradas pessoais", summary.withdrawalsCents],
            ["Projeção geral de caixa", summary.projectedCashCents, "Previsão simplificada com os compromissos pendentes."],
            ["Reserva mínima configurada", summary.reserveCents]
        ];
        const keyMetrics = new Set(["Vendas líquidas", "Saldo de caixa geral", "Contas a receber"]);
        const renderCard = ([name, value, hint]) => {
            const emphasis = name === "Vendas líquidas" ? "card-featured" : name === "Resultado operacional estimado" ? "card-result" : "";
            const cardHint = keyMetrics.has(name) ? "" : hint;
            return `<dl class="card ${emphasis}"><dt>${escape(name)}</dt><dd>${escape(money(value))}</dd>${cardHint ? `<small>${escape(cardHint)}</small>` : ""}</dl>`;
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
        rows("channel-totals", channels.map(channel => `<tr><td>${escape(channel.name)}</td><td class="money">${money(channel.netSalesCents)}</td><td class="money">${money(channel.estimatedMarginCents)}</td><td>${channel.salesWithCost}/${channel.salesCount} vendas</td></tr>`).join(""), 4, "Nenhum canal teve vendas no período selecionado.");
    }
    function renderTrendChart(elementId, values, field, color, label) {
        const container = document.getElementById(elementId);
        if (!values.length) {
            container.innerHTML = `<p class="empty-state">Sem dados para exibir neste período.</p>`;
            return;
        }

        const data = values.map(value => value[field]);
        if (data.every(value => value === 0)) {
            container.innerHTML = `<p class="chart-empty"><span aria-hidden="true">—</span><strong>Sem ${label} neste período</strong><small>Os dados aparecerão aqui quando houver movimentações.</small></p>`;
            return;
        }

        const width = Math.max(320, Math.floor(container.clientWidth));
        const height = 250;
        const left = 64;
        const right = width - 14;
        const top = 24;
        const bottom = height - 42;
        const plotHeight = bottom - top;
        const low = Math.min(0, ...data);
        const high = Math.max(0, ...data);
        const span = high - low || 1;
        const minimum = low < 0 ? low - span * .08 : 0;
        const maximum = high > 0 ? high + span * .12 : 1;
        const range = maximum - minimum;
        const yFor = amount => bottom - (amount - minimum) / range * plotHeight;
        const zeroY = bottom - (0 - minimum) / range * plotHeight;
        const slot = (right - left) / values.length;
        const barWidth = Math.max(3, Math.min(16, slot * .62));
        const labelStep = Math.max(1, Math.ceil(values.length / 6));
        const compactMoney = amount => {
            const reais = amount / 100;
            return new Intl.NumberFormat("pt-BR", Math.abs(reais) >= 1000
                ? { style: "currency", currency: "BRL", notation: "compact", maximumFractionDigits: 1 }
                : { style: "currency", currency: "BRL", maximumFractionDigits: 0 }).format(reais);
        };
        const dateLabel = value => {
            const date = new Date(`${value.periodStart}T12:00:00`);
            return date.toLocaleDateString("pt-BR", trends.monthly
                ? { month: "short", year: "2-digit" }
                : { day: "2-digit", month: "2-digit" });
        };
        const horizontalGrid = [0, .33, .66, 1].map(step => {
            const amount = maximum - range * step;
            const y = top + plotHeight * step;
            return `<g><line x1="${left}" y1="${y.toFixed(1)}" x2="${right}" y2="${y.toFixed(1)}"></line><text x="${left - 9}" y="${(y + 4).toFixed(1)}" text-anchor="end">${escape(compactMoney(amount))}</text></g>`;
        }).join("");
        const points = values.map((value, index) => {
            const x = left + slot * (index + .5);
            return { x, y: yFor(value[field]), value };
        });
        const xLabels = points.map((point, index) => {
            if (index % labelStep !== 0) return "";
            return `<text x="${point.x.toFixed(1)}" y="${height - 12}" text-anchor="middle">${escape(dateLabel(point.value))}</text>`;
        }).join("");
        const chartContent = field === "estimatedProfitCents"
            ? (() => {
                const line = points.map((point, index) => `${index ? "L" : "M"}${point.x.toFixed(1)},${point.y.toFixed(1)}`).join(" ");
                const area = `${line} L${points[points.length - 1].x.toFixed(1)},${zeroY.toFixed(1)} L${points[0].x.toFixed(1)},${zeroY.toFixed(1)} Z`;
                const markers = points.filter(point => point.value[field] !== 0).map(point => `<g><title>${escape(dateLabel(point.value))}: ${label} ${money(point.value[field])}</title><circle cx="${point.x.toFixed(1)}" cy="${point.y.toFixed(1)}" r="4"></circle></g>`).join("");
                return `<path class="dashboard-chart-area" d="${area}"></path><path class="dashboard-chart-line" d="${line}"></path>${markers}`;
            })()
            : points.map(point => {
                const y = Math.min(point.y, zeroY);
                const barHeight = Math.max(1, Math.abs(zeroY - point.y));
                return `<g><title>${escape(dateLabel(point.value))}: ${label} ${money(point.value[field])}</title><rect x="${(point.x - barWidth / 2).toFixed(1)}" y="${y.toFixed(1)}" width="${barWidth.toFixed(1)}" height="${barHeight.toFixed(1)}" rx="3"></rect></g>`;
            }).join("");
        container.innerHTML = `<div class="dashboard-chart-scroll"><svg viewBox="0 0 ${width} ${height}" role="presentation" aria-hidden="true"><g class="dashboard-chart-grid">${horizontalGrid}</g><line class="dashboard-chart-zero" x1="${left}" y1="${zeroY.toFixed(1)}" x2="${right}" y2="${zeroY.toFixed(1)}"></line><g class="dashboard-chart-data ${field === "estimatedProfitCents" ? "profit" : "expenses"}">${chartContent}</g>${xLabels}</svg></div>`;
    }
    let trends = { monthly: false, points: [] };
    function renderTrends(summary) {
        const points = trends.points || [];
        document.getElementById("profit-chart-total").textContent = money(summary.partialOperatingResultCents);
        document.getElementById("expense-chart-total").textContent = money(points.reduce((sum, point) => sum + point.expensesCents, 0));
        document.getElementById("profit-chart-note").textContent = summary.salesWithCost < summary.salesCount
            ? `Resultado parcial: ${summary.salesWithCost} de ${summary.salesCount} vendas têm custo informado.`
            : "Resultado estimado após custos diretos conhecidos e despesas operacionais.";
        renderTrendChart("profit-chart", points, "estimatedProfitCents", "#648b73", "lucro estimado");
        renderTrendChart("expense-chart", points, "expensesCents", "#bd914e", "gastos");
    }
    async function load() {
        const filter = ui.query(form);
        const [summary, ledger, trendData] = await Promise.all([
            api(`/dashboard?${filter}`),
            api(`/ledger?${filter}&pageSize=100`),
            api(`/dashboard/trends?${filter}`)
        ]);
        trends = trendData;
        render(summary);
        renderTrends(summary);
        rows("ledger-rows", ui.items(ledger).map(entry => `<tr><td>${date(entry.date)}</td><td>${escape(ui.label(entry.kind))}${entry.isReversal ? " · estorno" : ""}</td><td>${escape(entry.description || "—")}</td><td class="money ${entry.signedCents < 0 ? "negative" : "positive"}">${money(entry.signedCents)}</td></tr>`).join(""), 4, "Ainda não há movimentações de caixa neste período.");
        document.getElementById("ledger-count").textContent = `${ui.items(ledger).length} de ${ledger.total ?? ui.items(ledger).length} movimentações. O CSV inclui todos os registros do filtro.`;
        document.getElementById("export-csv").href = `/api/v1/exports/ledger.csv?${filter}`;
        document.getElementById("export-summary").href = `/api/v1/exports/summary.csv?${filter}`;
        document.getElementById("export-pdf").href = `/api/v1/exports/report.html?${filter}`;
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
    finally { ui.finishInitialLoad(); }
})();
