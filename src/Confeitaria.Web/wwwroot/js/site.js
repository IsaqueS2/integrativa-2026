"use strict";
window.ui = (() => {
    const moneyFormat = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
    const today = () => {
        const parts = new Intl.DateTimeFormat("en-US", { timeZone: "America/Sao_Paulo", year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(new Date());
        const get = type => parts.find(p => p.type === type).value;
        return `${get("year")}-${get("month")}-${get("day")}`;
    };
    const money = cents => cents === null || cents === undefined ? "Não disponível" : moneyFormat.format(cents / 100);
    const date = value => value ? value.slice(0, 10).split("-").reverse().join("/") : "—";
    const escape = value => String(value ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
    const label = value => ({ Pending: "Pendente", Received: "Recebida", Paid: "Paga", Cancelled: "Cancelado", Operating: "Operacional", Supplies: "Compra de insumos", AllocatedCost: "Custo já alocado", Contribution: "Aporte", Withdrawal: "Retirada", Receipt: "Recebimento", Payment: "Pagamento", Reversal: "Estorno" }[value] || value);
    const cents = value => {
        const raw = String(value ?? "").trim().replace(",", ".");
        if (!/^-?\d+(\.\d{1,2})?$/.test(raw)) throw new Error("Informe um valor em reais com até duas casas decimais.");
        const negative = raw.startsWith("-");
        const [whole, fraction = ""] = raw.replace("-", "").split(".");
        const amount = Number(BigInt(whole) * 100n + BigInt(fraction.padEnd(2, "0")));
        if (!Number.isSafeInteger(amount)) throw new Error("O valor informado é muito alto.");
        return negative ? -amount : amount;
    };
    function notify(message, error = false) {
        const target = document.getElementById("message");
        target.hidden = false;
        target.classList.toggle("error", error);
        target.textContent = message;
        target.scrollIntoView({ behavior: "smooth", block: "nearest" });
    }
    async function api(path, method = "GET", body) {
        const response = await fetch(`/api/v1${path}`, { method, headers: body === undefined ? {} : { "Content-Type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body) });
        const text = await response.text();
        let payload = null;
        try { payload = text ? JSON.parse(text) : null; } catch { payload = { detail: text }; }
        if (!response.ok) {
            const validation = payload?.errors ? Object.values(payload.errors).flat().join(" ") : "";
            throw new Error(validation || payload?.detail || payload?.title || `Não foi possível concluir a operação (${response.status}).`);
        }
        return payload;
    }
    function listenForm(id, submit) {
        const form = document.getElementById(id);
        form.addEventListener("submit", async event => {
            event.preventDefault();
            const button = form.querySelector('[type="submit"]');
            if (button) button.disabled = true;
            try { await submit(Object.fromEntries(new FormData(form)), form); }
            catch (error) { notify(error.message, true); }
            finally { if (button) button.disabled = false; }
        });
    }
    function query(form, extra = {}) {
        const values = { ...Object.fromEntries(new FormData(form)), ...extra };
        return new URLSearchParams(Object.entries(values).filter(([, value]) => value !== "" && value !== null && value !== undefined)).toString();
    }
    const items = result => Array.isArray(result) ? result : result?.items || [];
    let catalogs = { channels: [], categories: [], paymentMethods: [] };
    async function loadCatalogs() {
        catalogs = await api("/catalogs");
        document.querySelectorAll("select[data-catalog]").forEach(select => {
            const selected = select.value;
            const first = select.querySelector('option[value=""]');
            const emptyText = first?.textContent;
            select.replaceChildren();
            if (emptyText) select.add(new Option(emptyText, ""));
            (catalogs[select.dataset.catalog] || []).forEach(item => select.add(new Option(item.name, item.id)));
            if ([...select.options].some(option => option.value === selected)) select.value = selected;
        });
        return catalogs;
    }
    function catalogName(kind, id) { return catalogs[kind]?.find(item => item.id === id)?.name || (id ? `#${id}` : "—"); }
    function rows(id, content, colspan = 4) {
        document.getElementById(id).innerHTML = content || `<tr><td colspan="${colspan}" class="empty">Nenhum registro encontrado.</td></tr>`;
    }
    function initializeDates() {
        document.querySelectorAll("input[data-today]").forEach(input => input.value = today());
    }
    function bindAction(id, handler) {
        document.getElementById(id).addEventListener("click", async event => {
            const button = event.target.closest("button[data-action]");
            if (!button) return;
            button.disabled = true;
            try { await handler(button.dataset.action, Number(button.dataset.id)); }
            catch (error) { notify(error.message, true); }
            finally { button.disabled = false; }
        });
    }
    function ask(title, fields) {
        return new Promise(resolve => {
            const dialog = document.createElement("dialog");
            dialog.setAttribute("aria-labelledby", "action-title");
            dialog.innerHTML = `<form><h2 id="action-title">${escape(title)}</h2><div class="grid">${fields.map(field => `<label>${escape(field.label)}${field.options ? `<select name="${escape(field.name)}">${field.options.map(option => `<option value="${escape(option.value)}">${escape(option.label)}</option>`).join("")}</select>` : `<input name="${escape(field.name)}" type="${escape(field.type || "text")}" value="${escape(field.value ?? "")}" ${field.optional ? "" : "required"} ${field.type === "number" ? 'step="0.01" min="0"' : ""} />`}</label>`).join("")}</div><div class="actions"><button type="submit">Confirmar</button><button type="button" class="secondary" data-close>Voltar</button></div></form>`;
            const form = dialog.querySelector("form");
            fields.filter(field => field.options).forEach(field => form.elements[field.name].value = field.value);
            dialog.addEventListener("close", () => { const result = dialog.returnValue === "save" ? Object.fromEntries(new FormData(form)) : null; dialog.remove(); resolve(result); }, { once: true });
            form.addEventListener("submit", event => { event.preventDefault(); dialog.close("save"); });
            dialog.querySelector("[data-close]").addEventListener("click", () => dialog.close("cancel"));
            document.body.append(dialog); dialog.showModal();
        });
    }
    const actionDate = async () => (await ask("Confirmar movimentação", [{ name: "date", label: "Data da movimentação", type: "date", value: today() }]))?.date || null;
    const cancelData = async () => {
        const value = await ask("Cancelar ou estornar lançamento", [
            { name: "reason", label: "Motivo do cancelamento ou estorno" },
            { name: "date", label: "Data do cancelamento ou estorno", type: "date", value: today() }
        ]);
        if (!value) return null;
        if (!value.reason.trim()) throw new Error("Informe o motivo do cancelamento.");
        return { date: value.date, reason: value.reason.trim() };
    };
    document.querySelectorAll("nav a").forEach(a => { if (a.getAttribute("href").toLowerCase() === location.pathname.toLowerCase()) a.setAttribute("aria-current", "page"); });
    initializeDates();
    return { api, notify, listenForm, query, items, loadCatalogs, catalogName, rows, money, cents, today, date, escape, label, bindAction, ask, actionDate, cancelData, initializeDates };
})();
