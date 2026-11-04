"use strict";
(async () => {
    const { api, money, cents, escape, rows, notify } = ui;
    const ingredientForm = document.getElementById("ingredient-form");
    const recipeForm = document.getElementById("recipe-form");
    const cancelRecipeEdit = document.getElementById("recipe-new");
    const workflow = document.getElementById("recipe-workflow");
    const extraCosts = document.getElementById("recipe-extra-costs");
    const recipeSteps = [...workflow.querySelectorAll("[data-recipe-step]")];
    const stepNames = ["Ingredientes e embalagens", "Ficha técnica", "Custo da receita", "Simulação de preço"];
    let ingredients = [], recipes = [];
    let activeRecipeId = null;
    function showExtraCostsIfUsed(recipe) {
        extraCosts.open = Boolean(recipe && (
            recipe.packagingCents || recipe.laborMinutes || recipe.hourlyRateCents ||
            recipe.lossBasisPoints || recipe.allocatedCents
        ));
    }
    function showStep(step, scroll = true) {
        const targetStep = Number(step);
        if (!Number.isInteger(targetStep) || targetStep < 1 || targetStep > recipeSteps.length)
            throw new Error("Etapa de receita inválida.");
        if (targetStep > 1 && ingredients.length === 0)
            throw new Error("Cadastre pelo menos um ingrediente antes de montar a ficha técnica.");
        if (targetStep >= 3 && !activeRecipeId)
            throw new Error("Salve a ficha técnica antes de consultar custos ou simular preços.");
        recipeSteps.forEach(section => {
            section.hidden = Number(section.dataset.recipeStep) !== targetStep;
        });
        workflow.dataset.step = String(targetStep);
        workflow.querySelectorAll("[data-step-indicator]").forEach(indicator => {
            const indicatorStep = Number(indicator.dataset.stepIndicator);
            if (indicatorStep === targetStep) indicator.setAttribute("aria-current", "step");
            else indicator.removeAttribute("aria-current");
            indicator.classList.toggle("is-complete", indicatorStep < targetStep);
        });
        document.getElementById("recipe-step-status").textContent = `Etapa ${targetStep} de ${recipeSteps.length}: ${stepNames[targetStep - 1]}.`;
        if (scroll) workflow.scrollIntoView({ behavior: "smooth", block: "start" });
    }
    document.addEventListener("click", event => {
        if (!(event.target instanceof Element)) return;
        const nextButton = event.target.closest("[data-next-recipe-step]");
        const previousButton = event.target.closest("[data-previous-recipe-step]");
        const stepLink = event.target.closest("[data-recipe-step-link]");
        const control = nextButton || previousButton || stepLink;
        if (!control) return;
        event.preventDefault();
        try {
            showStep(nextButton ? nextButton.dataset.nextRecipeStep :
                previousButton ? previousButton.dataset.previousRecipeStep : stepLink.dataset.recipeStepLink);
        } catch (error) {
            notify(error.message, true);
        }
    });
    const quantity = value => Number(value).toLocaleString("pt-BR", { maximumFractionDigits: 6 });
    function populateIngredientSelect(select, selectedValue = select.value) {
        select.replaceChildren();
        if (!ingredients.length) {
            const option = new Option("Cadastre um ingrediente primeiro", "");
            option.disabled = true;
            option.selected = true;
            select.add(option);
            return;
        }
        select.add(new Option("Selecione um ingrediente", ""));
        ingredients.forEach(item => select.add(new Option(item.name, item.id)));
        select.value = ingredients.some(item => String(item.id) === String(selectedValue)) ? String(selectedValue) : "";
    }
    function addItem(item = {}) {
        const tr = document.createElement("tr");
        tr.innerHTML = `<td data-mobile-label="Ingrediente"><select name="ingredientId" aria-label="Ingrediente" required></select></td><td data-mobile-label="Quantidade"><input name="quantity" type="number" min="0.001" step="0.001" value="${escape(item.quantity ?? 1)}" aria-label="Quantidade usada" required /></td><td data-mobile-label="Unidade"><select name="unit" aria-label="Unidade usada"><option value="g">g</option><option value="kg">kg</option><option value="ml">ml</option><option value="l">L</option><option value="un">unidade</option></select></td><td data-mobile-label="Ação"><button type="button" class="secondary" data-remove-item>Remover ingrediente</button></td>`;
        populateIngredientSelect(tr.querySelector('[name="ingredientId"]'), item.ingredientId ?? "");
        tr.querySelector('[name="unit"]').value = item.unit || ingredients.find(i => i.id === Number(tr.querySelector('[name="ingredientId"]').value))?.unit || "g";
        tr.querySelector('[name="ingredientId"]').addEventListener("change", event => {
            const ingredient = ingredients.find(i => i.id === Number(event.target.value));
            if (ingredient) tr.querySelector('[name="unit"]').value = ingredient.unit;
        });
        tr.querySelector("[data-remove-item]").addEventListener("click", () => tr.remove());
        document.getElementById("recipe-items").append(tr);
    }
    async function load() {
        const [ingredientResult, recipeResult] = await Promise.all([api("/ingredients"), api("/recipes")]);
        ingredients = ui.items(ingredientResult); recipes = ui.items(recipeResult);
        const ingredientInfo = document.getElementById("ingredient-list-info");
        const recipeInfo = document.getElementById("recipe-list-info");
        if (ingredientInfo) ingredientInfo.textContent = `${ingredients.length} de ${ingredientResult.total ?? ingredients.length} ingredientes. A tela provisória exibe até 100 registros; a API permite consultar as demais páginas.`;
        if (recipeInfo) recipeInfo.textContent = `${recipes.length} de ${recipeResult.total ?? recipes.length} receitas. A tela provisória exibe até 100 registros; a API permite consultar as demais páginas.`;
        rows("ingredient-rows", ingredients.map(item => `<tr><td data-mobile-label="Ingrediente">${escape(item.name)}</td><td data-mobile-label="Embalagem">${quantity(item.packageQuantity)} ${escape(item.unit)}</td><td class="money" data-mobile-label="Preço">${money(item.packagePriceCents)}</td><td data-mobile-label="Estoque">${item.stockQuantity === null ? "Sem controle" : `${quantity(item.stockQuantity)} ${escape(item.unit)}`}</td><td data-mobile-label="Ação"><button type="button" class="secondary" data-action="edit" data-id="${item.id}">Editar</button></td></tr>`).join(""), 5, "Cadastre o primeiro ingrediente para começar sua ficha técnica.");
        rows("recipe-rows", recipes.map(recipe => `<tr><td data-mobile-label="Receita">${escape(recipe.name)}</td><td data-mobile-label="Rendimento">${quantity(recipe.yieldQuantity)} ${escape(recipe.yieldUnit)}</td><td data-mobile-label="Ações"><div class="inline-actions"><button type="button" data-action="cost" data-id="${recipe.id}">Ver custo</button><button class="secondary" type="button" data-action="edit" data-id="${recipe.id}">Editar</button></div></td></tr>`).join(""), 3, "Depois de cadastrar ingredientes, monte sua primeira ficha técnica.");
        document.querySelectorAll('#recipe-items select[name="ingredientId"]').forEach(select => {
            populateIngredientSelect(select);
        });
        const ingredientGuidance = document.getElementById("recipe-ingredient-guidance");
        if (ingredientGuidance) ingredientGuidance.hidden = ingredients.length > 0;
        document.getElementById("continue-to-recipe").disabled = ingredients.length === 0;
        if (!document.getElementById("recipe-items").children.length) addItem();
    }
    async function showCost(id, scroll = false) {
        const result = await api(`/recipes/${id}/cost`);
        const recipe = recipes.find(recipe => recipe.id === id);
        if (recipe) {
            recipeForm.elements.id.value = recipe.id;
            recipeForm.elements.name.value = recipe.name;
            recipeForm.elements.yieldQuantity.value = recipe.yieldQuantity;
            recipeForm.elements.yieldUnit.value = recipe.yieldUnit;
            recipeForm.elements.packaging.value = ui.decimalInput(recipe.packagingCents / 100);
            recipeForm.elements.laborMinutes.value = recipe.laborMinutes;
            recipeForm.elements.hourlyRate.value = ui.decimalInput(recipe.hourlyRateCents / 100);
            recipeForm.elements.loss.value = ui.decimalInput(recipe.lossBasisPoints / 100);
            recipeForm.elements.allocated.value = ui.decimalInput(recipe.allocatedCents / 100);
            showExtraCostsIfUsed(recipe);
            cancelRecipeEdit.hidden = false;
            document.getElementById("recipe-items").replaceChildren();
            recipe.items.forEach(addItem);
        }
        document.getElementById("cost-title").textContent = `Custo de ${recipe?.name || "receita"}`;
        const cards = [["Ingredientes do lote", result.ingredientCostCents], ["Embalagens", result.packagingCents], ["Perda estimada", result.lossCents], ["Mão de obra", result.laborCents], ["Outros rateios", result.allocatedCents], ["Custo total do lote", result.totalCostCents], ["Custo por unidade", result.unitCostCents]];
        document.getElementById("cost-cards").innerHTML = cards.map(([label, value], index) => `<dl class="card ${index === 5 ? "card-result" : index === 6 ? "card-featured" : ""}"><dt>${escape(label)}</dt><dd>${money(value)}</dd></dl>`).join("");
        rows("cost-lines", (result.lines || []).map(line => `<tr><td data-mobile-label="Ingrediente">${escape(line.name)}</td><td data-mobile-label="Quantidade usada">${quantity(line.quantity)} ${escape(line.unit)}</td><td data-mobile-label="Tamanho da embalagem">${quantity(line.packageQuantity)} ${escape(line.unit)}</td><td class="money" data-mobile-label="Preço da embalagem">${money(line.packagePriceCents)}</td><td class="money" data-mobile-label="Custo proporcional">${money(line.costCents)}</td></tr>`).join(""), 5, "Adicione ingredientes à receita para ver a memória de cálculo.");
        activeRecipeId = id;
        document.getElementById("price-form").elements.recipeId.value = id;
        document.getElementById("price-output").textContent = "";
        showStep(3, scroll);
    }
    ui.listenForm("ingredient-form", async (value, form) => {
        await api(value.id ? `/ingredients/${value.id}` : "/ingredients", value.id ? "PUT" : "POST", { name: value.name, unit: value.unit, packageQuantity: Number(value.packageQuantity), packagePriceCents: cents(value.packagePrice) });
        form.reset(); form.elements.id.value = ""; await load(); notify("Ingrediente salvo. Os custos de novas consultas usam o preço atualizado; vendas antigas preservam seu histórico.");
    });
    ui.listenForm("recipe-form", async (value, form) => {
        const items = [...document.querySelectorAll("#recipe-items tr")].map(row => ({ ingredientId: Number(row.querySelector('[name="ingredientId"]').value), quantity: Number(row.querySelector('[name="quantity"]').value), unit: row.querySelector('[name="unit"]').value }));
        if (!items.length) throw new Error("Adicione pelo menos um ingrediente à receita.");
        const result = await api(value.id ? `/recipes/${value.id}` : "/recipes", value.id ? "PUT" : "POST", { name: value.name, yieldQuantity: Number(value.yieldQuantity), yieldUnit: value.yieldUnit, items, packagingCents: cents(value.packaging), laborMinutes: Number(value.laborMinutes), hourlyRateCents: cents(value.hourlyRate), lossBasisPoints: cents(value.loss), allocatedCents: cents(value.allocated) });
        await load(); const id = Number(value.id || result.id); form.elements.id.value = id; await showCost(id, true); notify("Receita salva e custo calculado.");
    });
    ui.listenForm("price-form", async value => {
        const result = await api(`/recipes/${value.recipeId}/price`, "POST", { marginBasisPoints: cents(value.margin), salesRateBasisPoints: cents(value.salesRate), fixedFeeCents: cents(value.fixedFee) });
        document.getElementById("price-output").textContent = `Preço sugerido por unidade: ${money(result.suggestedPriceCents)}\nMarkup sobre o custo: ${result.markupBasisPoints === null ? "Não disponível" : `${(result.markupBasisPoints / 100).toLocaleString("pt-BR", { maximumFractionDigits: 2 })}%`}`;
    });
    ui.bindAction("ingredient-rows", async (_, id) => {
        const ingredient = ingredients.find(item => item.id === id);
        ingredientForm.elements.id.value = ingredient.id; ingredientForm.elements.name.value = ingredient.name;
        ingredientForm.elements.packageQuantity.value = ingredient.packageQuantity; ingredientForm.elements.unit.value = ingredient.unit;
        ingredientForm.elements.packagePrice.value = ui.decimalInput(ingredient.packagePriceCents / 100);
        showStep(1, false);
        ingredientForm.scrollIntoView({ behavior: "smooth" });
    });
    ui.bindAction("recipe-rows", async (action, id) => {
        if (action === "cost") return showCost(id, true);
        const recipe = recipes.find(recipe => recipe.id === id);
        recipeForm.elements.id.value = recipe.id; recipeForm.elements.name.value = recipe.name;
        recipeForm.elements.yieldQuantity.value = recipe.yieldQuantity; recipeForm.elements.yieldUnit.value = recipe.yieldUnit;
        recipeForm.elements.packaging.value = ui.decimalInput(recipe.packagingCents / 100); recipeForm.elements.laborMinutes.value = recipe.laborMinutes;
        recipeForm.elements.hourlyRate.value = ui.decimalInput(recipe.hourlyRateCents / 100); recipeForm.elements.loss.value = ui.decimalInput(recipe.lossBasisPoints / 100);
        recipeForm.elements.allocated.value = ui.decimalInput(recipe.allocatedCents / 100);
        showExtraCostsIfUsed(recipe);
        cancelRecipeEdit.hidden = false;
        activeRecipeId = recipe.id;
        document.getElementById("recipe-items").replaceChildren(); recipe.items.forEach(addItem);
        showStep(2);
    });
    document.getElementById("add-recipe-item").addEventListener("click", () => addItem());
    document.getElementById("ingredient-new").addEventListener("click", () => { ingredientForm.reset(); ingredientForm.elements.id.value = ""; });
    document.getElementById("recipe-new").addEventListener("click", () => {
        recipeForm.reset();
        recipeForm.elements.id.value = "";
        document.getElementById("recipe-items").replaceChildren();
        addItem();
        cancelRecipeEdit.hidden = true;
        extraCosts.open = false;
        activeRecipeId = null;
        showStep(2);
    });
    const purchaseForm = document.getElementById("purchase-import-form");
    const purchaseReview = document.getElementById("purchase-import-review");
    const purchaseRows = document.getElementById("purchase-import-rows");
    let purchaseFile = null;
    let purchasePreview = null;
    const purchaseUnits = ["g", "kg", "ml", "l", "un"];
    function addPurchaseLine(line = {}) {
        const tr = document.createElement("tr");
        tr.innerHTML = `<td data-mobile-label="Produto ou insumo"><input name="name" maxlength="120" value="${escape(line.name || "")}" aria-label="Nome do produto ou insumo" required /></td><td data-mobile-label="Quantidade"><input name="quantity" type="number" min="0.000001" step="0.000001" value="${escape(line.quantity ?? 1)}" aria-label="Quantidade da compra" required /></td><td data-mobile-label="Unidade"><select name="unit" aria-label="Unidade da quantidade">${purchaseUnits.map(unit => `<option value="${unit}">${unit}</option>`).join("")}</select></td><td data-mobile-label="Valor do item"><input name="total" type="text" inputmode="decimal" class="decimal-input" value="${escape(ui.decimalInput((line.totalCents ?? 0) / 100))}" aria-label="Valor total do item" required /></td><td data-mobile-label="Ingrediente correspondente"><select name="ingredientId" aria-label="Ingrediente correspondente"><option value="">Cadastrar como novo ingrediente</option>${ingredients.map(item => `<option value="${item.id}">${escape(item.name)}</option>`).join("")}</select></td><td data-mobile-label="Ação"><button type="button" class="secondary" data-remove-purchase-line>Remover item</button></td>`;
        if (purchaseUnits.includes(line.unit)) tr.querySelector('[name="unit"]').value = line.unit;
        if (line.ingredientId) tr.querySelector('[name="ingredientId"]').value = String(line.ingredientId);
        tr.querySelector("[data-remove-purchase-line]").addEventListener("click", () => tr.remove());
        purchaseRows.append(tr);
    }
    if (purchaseForm && purchaseReview && purchaseRows) {
    purchaseForm.addEventListener("submit", async event => {
        event.preventDefault();
        const file = purchaseForm.elements.file.files[0];
        if (!file) return;
        const button = purchaseForm.querySelector('[type="submit"]');
        button.disabled = true;
        try {
            const form = new FormData();
            form.append("file", file);
            purchasePreview = await api("/purchase-imports/preview", "POST", form);
            purchaseFile = file;
            document.getElementById("purchase-supplier").value = purchasePreview.supplier;
            document.getElementById("purchase-invoice-number").value = purchasePreview.invoiceNumber || "";
            document.getElementById("purchase-date").value = purchasePreview.purchaseDate;
            document.getElementById("purchase-total").value = ui.decimalInput(purchasePreview.totalCents / 100);
            document.getElementById("purchase-import-notice").textContent = purchasePreview.alreadyImported
                ? "Este documento ou chave de NF-e já consta no sistema. Ele não pode ser importado novamente."
                : `${purchasePreview.format}: confira especialmente nomes, quantidades e valores antes de confirmar. A leitura de fotos e PDFs depende da qualidade do documento.`;
            document.getElementById("purchase-confirm").disabled = purchasePreview.alreadyImported;
            purchaseRows.replaceChildren();
            purchasePreview.items.forEach(addPurchaseLine);
            if (!purchasePreview.items.length) addPurchaseLine();
            purchaseReview.hidden = false;
            purchaseReview.scrollIntoView({ behavior: "smooth", block: "start" });
        } catch (error) {
            notify(error.message, true);
        } finally {
            button.disabled = false;
        }
    });
    document.getElementById("purchase-add-line").addEventListener("click", () => addPurchaseLine());
    document.getElementById("purchase-cancel").addEventListener("click", () => {
        purchaseReview.hidden = true;
        purchaseRows.replaceChildren();
        purchaseForm.reset();
        purchaseFile = null;
        purchasePreview = null;
    });
    document.getElementById("purchase-confirm").addEventListener("click", async event => {
        const button = event.currentTarget;
        if (!purchasePreview || !purchaseFile || purchasePreview.alreadyImported) return;
        button.disabled = true;
        try {
            const items = [...purchaseRows.querySelectorAll("tr")].map(row => ({
                name: row.querySelector('[name="name"]').value.trim(),
                quantity: Number(row.querySelector('[name="quantity"]').value),
                unit: row.querySelector('[name="unit"]').value,
                totalCents: cents(row.querySelector('[name="total"]').value),
                ingredientId: row.querySelector('[name="ingredientId"]').value || null
            })).map(item => ({ ...item, ingredientId: item.ingredientId === null ? null : Number(item.ingredientId) }));
            const details = {
                documentHash: purchasePreview.documentHash,
                accessKey: purchasePreview.accessKey,
                invoiceNumber: document.getElementById("purchase-invoice-number").value.trim() || null,
                supplier: document.getElementById("purchase-supplier").value.trim(),
                purchaseDate: document.getElementById("purchase-date").value,
                totalCents: cents(document.getElementById("purchase-total").value),
                items
            };
            const form = new FormData();
            form.append("file", purchaseFile);
            form.append("request", JSON.stringify(details));
            const saved = await api("/purchase-imports/commit", "POST", form);
            purchaseReview.hidden = true;
            purchaseRows.replaceChildren();
            purchaseForm.reset();
            purchaseFile = null;
            purchasePreview = null;
            await load();
            notify(`Compra importada: ${saved.itemCount} item(ns), despesa ${money(saved.totalCents)} e estoque atualizado.`);
        } catch (error) {
            notify(error.message, true);
        } finally {
            button.disabled = false;
        }
    });
    }
    try { await load(); } catch (error) { notify(error.message, true); }
    finally { ui.finishInitialLoad(); }
})();
