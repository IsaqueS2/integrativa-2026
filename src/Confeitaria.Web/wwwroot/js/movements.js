"use strict";
(async () => {
    const { api, money, cents, escape, date, rows, notify } = ui;
    const filter = document.getElementById("movement-filter");
    filter.elements.from.value = `${ui.today().slice(0, 7)}-01`;
    filter.elements.to.value = ui.today();
    let page = 1, hasNext = false;
    const actionButtons = (id, status, confirmAction, confirmLabel) => `<div class="inline-actions">${status === "Pending" ? `<button type="button" data-action="${confirmAction}" data-id="${id}">${confirmLabel}</button>` : ""}${status !== "Cancelled" ? `<button class="danger" type="button" data-action="cancel" data-id="${id}">${status === "Pending" ? "Cancelar" : "Estornar"}</button>` : ""}</div>`;
    async function load() {
        const common = ui.query(filter, { page, pageSize: 20 });
        const [sales, expenses, owners] = await Promise.all([api(`/sales?${common}`), api(`/expenses?${common}`), api(`/owner-movements?${common}`)]);
        rows("sale-rows", ui.items(sales).map(sale => `<tr><td>${date(sale.date)}${sale.consolidated ? "<br><small>Consolidado</small>" : ""}</td><td>${escape(sale.description || "Venda")}<br><small>${escape(ui.catalogName("channels", sale.channelId))}</small></td><td class="money">${money(sale.grossCents)}</td><td class="money">${money(sale.actualFeeCents ?? sale.estimatedFeeCents)}</td><td class="money">${money(sale.netCents)}</td><td><span class="status">${escape(ui.label(sale.status))}</span></td><td>${actionButtons(sale.id, sale.status, "receive", "Receber")}</td></tr>`).join(""), 7);
        rows("expense-rows", ui.items(expenses).map(expense => `<tr><td>${date(expense.date)}</td><td>${escape(expense.description || "Despesa")}<br><small>${escape(ui.catalogName("categories", expense.categoryId))}</small></td><td>${date(expense.dueDate)}</td><td class="money">${money(expense.amountCents)}</td><td><span class="status">${escape(ui.label(expense.status))}</span></td><td>${actionButtons(expense.id, expense.status, "pay", "Pagar")}</td></tr>`).join(""), 6);
        rows("owner-rows", ui.items(owners).map(owner => `<tr><td>${date(owner.date)}</td><td>${escape(ui.label(owner.kind))}</td><td>${escape(owner.description || "—")}</td><td class="money">${money(owner.amountCents)}</td><td>${owner.reversedOn ? "Estornado" : "Confirmado"}</td><td>${owner.reversedOn ? "" : `<button class="danger" type="button" data-action="cancel" data-id="${owner.id}">Estornar</button>`}</td></tr>`).join(""), 6);
        hasNext = [sales, expenses, owners].some(result => result.total !== undefined ? result.total > page * 20 : ui.items(result).length === 20);
        document.getElementById("previous-page").disabled = page <= 1;
        document.getElementById("next-page").disabled = !hasNext;
        document.getElementById("page-info").textContent = `Página ${page} · até 20 registros de cada tipo`;
    }
    ui.listenForm("sale-form", async (value, form) => {
        await api("/sales", "POST", {
            date: value.date, grossCents: cents(value.gross), discountCents: cents(value.discount),
            channelId: Number(value.channelId), paymentMethodId: Number(value.paymentMethodId),
            dueDate: value.expectedDate || null, description: value.description,
            consolidated: value.recordType === "Consolidated", directCostCents: value.cost ? cents(value.cost) : null,
            feeOverrideCents: value.fee ? cents(value.fee) : null, recipeId: value.recipeId ? Number(value.recipeId) : null,
            recipeUnits: value.recipeId ? Number(value.recipeQuantity) : 1
        });
        form.reset(); ui.initializeDates(); await load(); notify("Venda pendente registrada. Confirme o recebimento quando o dinheiro entrar no caixa.");
    });
    ui.listenForm("expense-form", async (value, form) => {
        await api("/expenses", "POST", { date: value.date, dueDate: value.dueDate, amountCents: cents(value.amount), categoryId: Number(value.categoryId), description: value.description });
        form.reset(); ui.initializeDates(); await load(); notify("Despesa pendente registrada. Confirme o pagamento para movimentar o caixa.");
    });
    ui.listenForm("owner-form", async (value, form) => {
        await api("/owner-movements", "POST", { date: value.date, kind: value.kind, amountCents: cents(value.amount), description: value.description });
        form.reset(); ui.initializeDates(); await load(); notify("Movimentação da proprietária registrada.");
    });
    ui.listenForm("movement-filter", async () => { page = 1; await load(); });
    ui.bindAction("sale-rows", async (action, id) => {
        if (action === "cancel") { const body = await ui.cancelData(); if (!body) return; await api(`/sales/${id}/cancel`, "POST", body); }
        else {
            const value = await ui.ask("Confirmar recebimento", [
                { name: "date", label: "Data do recebimento", type: "date", value: ui.today() },
                { name: "fee", label: "Taxa efetiva total em R$ (opcional)", type: "number", optional: true }
            ]);
            if (!value) return;
            await api(`/sales/${id}/receive`, "POST", { date: value.date, actualFeeCents: value.fee.trim() ? cents(value.fee) : null });
        }
        await load(); notify("Venda atualizada.");
    });
    ui.bindAction("expense-rows", async (action, id) => {
        if (action === "cancel") { const body = await ui.cancelData(); if (!body) return; await api(`/expenses/${id}/cancel`, "POST", body); }
        else { const on = await ui.actionDate(); if (!on) return; await api(`/expenses/${id}/pay`, "POST", { date: on }); }
        await load(); notify("Despesa atualizada.");
    });
    ui.bindAction("owner-rows", async (_, id) => { const body = await ui.cancelData(); if (!body) return; await api(`/owner-movements/${id}/cancel`, "POST", body); await load(); notify("Movimentação estornada."); });
    for (const [id, difference] of [["previous-page", -1], ["next-page", 1]]) document.getElementById(id).addEventListener("click", async () => {
        page += difference; try { await load(); } catch (error) { page -= difference; notify(error.message, true); }
    });
    try {
        await ui.loadCatalogs(); await load();
        try { const recipes = await api("/recipes"); ui.items(recipes).forEach(recipe => document.getElementById("sale-recipe").add(new Option(recipe.name, recipe.id))); }
        catch { document.getElementById("sale-recipe").closest("label").hidden = true; document.querySelector('[name="recipeQuantity"]').closest("label").hidden = true; }
    } catch (error) { notify(error.message, true); }
})();
