"use strict";
(async () => {
    const { api, money, escape, rows, date, notify } = ui;
    const form = document.getElementById("dashboard-filter");
    const filterOption = document.getElementById("dashboard-filters-option");
    const noticeOption = document.getElementById("dashboard-notices-option");
    const versionToggle = document.getElementById("dashboard-version-toggle");
    const dashboardVersionKey = "gere-doce.dashboard-version";
    const readPreviousVersion = () => {
        try { return localStorage.getItem(dashboardVersionKey) === "previous"; }
        catch { return false; }
    };
    const setDashboardVersion = previous => {
        document.documentElement.classList.toggle("dashboard-previous", previous);
        versionToggle.setAttribute("aria-pressed", String(previous));
        versionToggle.textContent = previous ? "Voltar ao painel atualizado" : "Usar painel anterior";
        versionToggle.title = previous
            ? "Ativa o painel com atalhos, alertas e filtros visíveis."
            : "Mostra o painel com filtros e avisos recolhidos.";
        filterOption.open = !previous;
        noticeOption.open = !previous;
        try { localStorage.setItem(dashboardVersionKey, previous ? "previous" : "updated"); }
        catch { /* The toggle still works for this page when browser storage is unavailable. */ }
    };
    setDashboardVersion(readPreviousVersion());
    versionToggle.addEventListener("click", () => {
        setDashboardVersion(versionToggle.getAttribute("aria-pressed") !== "true");
    });
    form.elements.from.value = ui.dateToInput(`${ui.today().slice(0, 7)}-01`);
    form.elements.to.value = ui.dateToInput(ui.today());
    let openingDate = null;
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
        const warnings = summary.warnings || [];
        const missingCosts = summary.salesCount - summary.salesWithCost;
        const costAction = missingCosts > 0
            ? `<li class="dashboard-alert-action"><a href="/Movements#movement-history-option">Revisar vendas e custos</a><span>Vendas já registradas não podem ter o custo editado; se necessário, cancele e registre novamente com o custo ou a receita.</span></li>`
            : "";
        document.getElementById("dashboard-notices").innerHTML = [
            ...warnings.map(warning => `<li class="notice-warning">${escape(warning)}</li>`),
            costAction
        ].filter(Boolean).join("");
        const hasNotices = warnings.length > 0 || Boolean(costAction);
        noticeOption.hidden = !hasNotices;
        const noticeMenuLink = document.querySelector('[data-expand-target="dashboard-notices-option"]');
        if (noticeMenuLink) noticeMenuLink.hidden = !hasNotices;
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
            container.innerHTML = `<p class="chart-empty"><span aria-hidden="true">—</span><strong>Sem ${label} neste período</strong><small>Registre vendas ou despesas para preencher este gráfico.</small></p>`;
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
    function previousPeriodQuery(filter) {
        const params = new URLSearchParams(filter);
        const from = params.get("from");
        const to = params.get("to");
        if (!from || !to) return null;
        const fromDay = Date.parse(`${from}T00:00:00Z`);
        const toDay = Date.parse(`${to}T00:00:00Z`);
        const dayMs = 24 * 60 * 60 * 1000;
        const days = (toDay - fromDay) / dayMs + 1;
        const previousTo = new Date(fromDay - dayMs).toISOString().slice(0, 10);
        const previousFrom = new Date(fromDay - days * dayMs).toISOString().slice(0, 10);
        if (previousFrom < openingDate) return null;
        params.set("from", previousFrom);
        params.set("to", previousTo);
        return { query: params.toString(), from: previousFrom, to: previousTo };
    }
    function comparePeriods(current, previous) {
        if (previous === null || previous === undefined)
            return "Sem comparação: o período anterior é anterior à abertura.";
        const difference = current - previous;
        const sign = difference > 0 ? "+" : difference < 0 ? "−" : "";
        const amount = `${sign}${money(Math.abs(difference))}`;
        if (previous === 0) return `vs. período anterior: ${amount} (sem base anterior).`;
        const percentage = new Intl.NumberFormat("pt-BR", {
            maximumFractionDigits: 1,
            signDisplay: "always"
        }).format(difference / Math.abs(previous) * 100);
        return `vs. período anterior: ${amount} (${percentage}%).`;
    }
    function renderTrends(summary, previous) {
        const points = trends.points || [];
        const periodLabel = `${date(summary.from)} a ${date(summary.to)}`;
        const previousPeriod = previousPeriodQuery(ui.query(form));
        document.getElementById("dashboard-period").textContent = previousPeriod
            ? `Período: ${periodLabel} · comparação: ${date(previousPeriod.from)} a ${date(previousPeriod.to)}`
            : `Período: ${periodLabel} · sem período anterior para comparar`;
        document.getElementById("profit-chart-total").textContent = money(summary.partialOperatingResultCents);
        document.getElementById("expense-chart-total").textContent = money(points.reduce((sum, point) => sum + point.expensesCents, 0));
        document.getElementById("profit-chart-note").textContent = summary.salesWithCost < summary.salesCount
            ? `Resultado parcial: ${summary.salesWithCost} de ${summary.salesCount} vendas têm custo informado.`
            : "Resultado estimado após custos diretos conhecidos e despesas operacionais.";
        document.getElementById("profit-chart-comparison").textContent = comparePeriods(
            summary.partialOperatingResultCents,
            previous?.partialOperatingResultCents ?? null
        );
        const expenses = summary.operatingExpensesCents + summary.suppliesPurchasesCents + summary.allocatedExpensesCents;
        const previousExpenses = previous
            ? previous.operatingExpensesCents + previous.suppliesPurchasesCents + previous.allocatedExpensesCents
            : null;
        document.getElementById("expense-chart-comparison").textContent = comparePeriods(expenses, previousExpenses);
        renderTrendChart("profit-chart", points, "estimatedProfitCents", "#648b73", "lucro estimado");
        renderTrendChart("expense-chart", points, "expensesCents", "#bd914e", "gastos");
    }
    let lastChartRender = null;
    async function load() {
        const filter = ui.query(form);
        const previousPeriod = previousPeriodQuery(filter);
        const [summary, ledger, trendData, previous] = await Promise.all([
            api(`/dashboard?${filter}`),
            api(`/ledger?${filter}&pageSize=100`),
            api(`/dashboard/trends?${filter}`),
            previousPeriod ? api(`/dashboard?${previousPeriod.query}`) : Promise.resolve(null)
        ]);
        trends = trendData;
        render(summary);
        lastChartRender = { summary, previous };
        renderTrends(summary, previous);
        rows("ledger-rows", ui.items(ledger).map(entry => `<tr><td>${date(entry.date)}</td><td>${escape(ui.label(entry.kind))}${entry.isReversal ? " · estorno" : ""}</td><td>${escape(entry.description || "—")}</td><td class="money ${entry.signedCents < 0 ? "negative" : "positive"}">${money(entry.signedCents)}</td></tr>`).join(""), 4, "Ainda não há movimentações de caixa neste período.");
        document.getElementById("ledger-count").textContent = `${ui.items(ledger).length} de ${ledger.total ?? ui.items(ledger).length} movimentações. O CSV inclui todos os registros do filtro.`;
        document.getElementById("export-csv").href = `/api/v1/exports/ledger.csv?${filter}`;
        document.getElementById("export-summary").href = `/api/v1/exports/summary.csv?${filter}`;
        document.getElementById("export-pdf").href = `/api/v1/exports/report.html?${filter}`;
    }
    let resizeTimer;
    window.addEventListener("resize", () => {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(() => {
            if (lastChartRender) renderTrends(lastChartRender.summary, lastChartRender.previous);
        }, 120);
    });
    ui.listenForm("dashboard-filter", load);
    document.getElementById("today-summary").addEventListener("click", async () => {
        try {
            form.elements.from.value = ui.dateToInput(ui.today()); form.elements.to.value = ui.dateToInput(ui.today());
            form.elements.channelId.value = ""; form.elements.paymentMethodId.value = ""; form.elements.categoryId.value = "";
            await load(); notify("Resumo do dia consultado. Nenhum lançamento foi criado.");
        } catch (error) { notify(error.message, true); }
    });
    try {
        const [, settings] = await Promise.all([ui.loadCatalogs(), api("/settings")]);
        openingDate = settings.openingDate;
        await load();
    } catch (error) { notify(error.message, true); }
    finally { ui.finishInitialLoad(); }
})();
