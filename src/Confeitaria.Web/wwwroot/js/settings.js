"use strict";
(async () => {
    const { api, money, cents, escape, rows, notify } = ui;
    const settingsForm = document.getElementById("settings-form");
    const editButton = id => `<button class="secondary" type="button" data-action="edit" data-id="${id}">Editar</button>`;
    let catalog;
    async function load() {
        const [settings, catalogs] = await Promise.all([api("/settings"), ui.loadCatalogs()]);
        catalog = catalogs;
        settingsForm.elements.name.value = settings.name;
        settingsForm.elements.openingBalance.value = (settings.openingBalanceCents / 100).toFixed(2);
        settingsForm.elements.openingDate.value = settings.openingDate;
        settingsForm.elements.reserve.value = (settings.reserveCents / 100).toFixed(2);
        rows("category-rows", catalogs.categories.map(category => `<tr><td>${escape(category.name)}</td><td>${escape(ui.label(category.kind))}</td><td>${editButton(category.id)}</td></tr>`).join(""), 3);
        for (const [key, id] of [["channels", "channel-rows"], ["paymentMethods", "payment-rows"]]) rows(id, catalogs[key].map(item => `<tr><td>${escape(item.name)}</td><td>${(item.rateBasisPoints / 100).toLocaleString("pt-BR")}%</td><td class="money">${money(item.fixedFeeCents)}</td><td>${editButton(item.id)}</td></tr>`).join(""));
    }
    ui.listenForm("settings-form", async value => {
        await api("/settings", "PUT", { name: value.name, openingDate: value.openingDate, openingBalanceCents: cents(value.openingBalance), reserveCents: cents(value.reserve) });
        await load(); notify("Configurações salvas.");
    });
    ui.listenForm("category-form", async (value, form) => { await api("/categories", "POST", { name: value.name, kind: value.kind }); form.reset(); await load(); notify("Categoria adicionada."); });
    for (const [formId, endpoint] of [["channel-form", "/channels"], ["payment-form", "/payment-methods"]]) ui.listenForm(formId, async (value, form) => {
        await api(endpoint, "POST", { name: value.name, rateBasisPoints: cents(value.rate), fixedFeeCents: cents(value.fixedFee) });
        form.reset(); await load(); notify("Cadastro adicionado.");
    });
    ui.bindAction("category-rows", async (_, id) => {
        const item = catalog.categories.find(item => item.id === id);
        const value = await ui.ask("Editar categoria", [
            { name: "name", label: "Nome da categoria", value: item.name },
            { name: "kind", label: "Tipo de categoria", value: item.kind, options: [
                { value: "Operating", label: "Despesa operacional" }, { value: "Supplies", label: "Compra de insumos" }, { value: "AllocatedCost", label: "Custo já alocado nas receitas" }
            ] }
        ]);
        if (!value) return;
        await api(`/categories/${id}`, "PUT", value); await load(); notify("Categoria atualizada. Lançamentos antigos mantêm sua classificação.");
    });
    for (const [target, key, endpoint] of [["channel-rows", "channels", "/channels"], ["payment-rows", "paymentMethods", "/payment-methods"]]) ui.bindAction(target, async (_, id) => {
        const item = catalog[key].find(item => item.id === id);
        const value = await ui.ask("Editar cadastro e taxas", [
            { name: "name", label: "Nome", value: item.name },
            { name: "rate", label: "Taxa percentual %", type: "number", value: (item.rateBasisPoints / 100).toFixed(2) },
            { name: "fixed", label: "Taxa fixa em R$", type: "number", value: (item.fixedFeeCents / 100).toFixed(2) }
        ]);
        if (!value) return;
        await api(`${endpoint}/${id}`, "PUT", { name: value.name, rateBasisPoints: cents(value.rate), fixedFeeCents: cents(value.fixed) }); await load(); notify("Cadastro atualizado. Vendas antigas mantêm as taxas anteriores.");
    });
    try { await load(); } catch (error) { notify(error.message, true); }
})();
