"use strict";
window.ui = (() => {
    const pageLoader = document.getElementById("page-loader");
    const loaderMain = document.getElementById("conteudo");
    const loaderStartedAt = performance.now();
    let loaderFinished = false;
    let loaderHideTimer;
    const hidePageLoader = () => {
        if (!pageLoader) return;
        pageLoader.classList.add("is-hiding");
        pageLoader.setAttribute("aria-hidden", "true");
        loaderMain?.setAttribute("aria-busy", "false");
        loaderHideTimer = setTimeout(() => {
            pageLoader.hidden = true;
            pageLoader.classList.remove("is-hiding");
        }, 250);
    };
    const finishInitialLoad = () => {
        if (loaderFinished) return;
        loaderFinished = true;
        clearTimeout(loaderHideTimer);
        const remaining = Math.max(0, 550 - (performance.now() - loaderStartedAt));
        loaderHideTimer = setTimeout(hidePageLoader, remaining);
    };
    const showPageLoader = () => {
        if (!pageLoader) return;
        clearTimeout(loaderHideTimer);
        pageLoader.hidden = false;
        pageLoader.classList.remove("is-hiding");
        pageLoader.setAttribute("aria-hidden", "false");
        loaderMain?.setAttribute("aria-busy", "true");
    };
    if (pageLoader) {
        pageLoader.setAttribute("aria-hidden", "false");
        loaderHideTimer = setTimeout(finishInitialLoad, 15000);
        document.addEventListener("click", event => {
            if (!(event.target instanceof Element) || event.defaultPrevented ||
                event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
            const link = event.target.closest("a[href]");
            if (!link || link.hasAttribute("download") || link.target === "_blank" ||
                !(link.matches("a.brand") || link.closest("header > nav"))) return;
            const destination = new URL(link.href, location.href);
            if (destination.origin !== location.origin ||
                (destination.pathname === location.pathname && destination.search === location.search)) return;
            showPageLoader();
        });
    }
    const mobileMenuToggle = document.getElementById("mobile-menu-toggle");
    const primaryNavigation = document.getElementById("primary-navigation");
    if (mobileMenuToggle && primaryNavigation) {
        const mobileBreakpoint = window.matchMedia("(max-width: 1023px)");
        const setMenuOpen = open => {
            mobileMenuToggle.setAttribute("aria-expanded", String(open));
            mobileMenuToggle.querySelector(".mobile-menu-label").textContent = open ? "Fechar" : "Menu";
            primaryNavigation.classList.toggle("is-open", open);
        };
        mobileMenuToggle.addEventListener("click", () => {
            setMenuOpen(mobileMenuToggle.getAttribute("aria-expanded") !== "true");
        });
        primaryNavigation.addEventListener("click", event => {
            if (event.target instanceof Element && event.target.closest("a[href]") && mobileBreakpoint.matches)
                setMenuOpen(false);
        });
        document.addEventListener("click", event => {
            if (mobileBreakpoint.matches && mobileMenuToggle.getAttribute("aria-expanded") === "true" &&
                event.target instanceof Node && !primaryNavigation.contains(event.target) && !mobileMenuToggle.contains(event.target))
                setMenuOpen(false);
        });
        document.addEventListener("keydown", event => {
            if (event.key === "Escape" && mobileMenuToggle.getAttribute("aria-expanded") === "true")
                setMenuOpen(false);
        });
        mobileBreakpoint.addEventListener("change", () => setMenuOpen(false));
    }
    const moneyFormat = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });
    const today = () => {
        const parts = new Intl.DateTimeFormat("en-US", { timeZone: "America/Sao_Paulo", year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(new Date());
        const get = type => parts.find(p => p.type === type).value;
        return `${get("year")}-${get("month")}-${get("day")}`;
    };
    const dateToInput = value => value ? `${value.slice(8, 10)}/${value.slice(5, 7)}/${value.slice(0, 4)}` : "";
    const dateToApi = value => {
        const match = /^([0-3]\d)\/([01]\d)\/(\d{4})$/.exec(String(value ?? "").trim());
        if (!match) throw new Error("Informe uma data válida no formato dia/mês/ano.");
        const [, day, month, year] = match;
        const parsed = new Date(Date.UTC(Number(year), Number(month) - 1, Number(day)));
        if (parsed.getUTCFullYear() !== Number(year) || parsed.getUTCMonth() !== Number(month) - 1 || parsed.getUTCDate() !== Number(day))
            throw new Error("A data informada não existe. Confira o dia, mês e ano.");
        return `${year}-${month}-${day}`;
    };
    const decimalInput = value => value === null || value === undefined || value === "" ? "" : Number(value).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const formValues = form => {
        const values = Object.fromEntries(new FormData(form));
        form.querySelectorAll("[data-date-br]").forEach(input => {
            if (input.value.trim()) values[input.name] = dateToApi(input.value);
        });
        return values;
    };
    const money = cents => cents === null || cents === undefined ? "Não disponível" : moneyFormat.format(cents / 100);
    const date = value => value ? value.slice(0, 10).split("-").reverse().join("/") : "—";
    const escape = value => String(value ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
    const label = value => ({ Pending: "Pendente", Received: "Recebida", Paid: "Paga", Cancelled: "Cancelado", Operating: "Operacional", Supplies: "Compra de insumos", AllocatedCost: "Custo já alocado", Contribution: "Aporte", Withdrawal: "Retirada", Receipt: "Recebimento", Payment: "Pagamento", Reversal: "Estorno" }[value] || value);
    const cents = value => {
        let raw = String(value ?? "").trim();
        if (raw.includes(",")) {
            if (!/^-?(?:\d+|\d{1,3}(?:\.\d{3})+),\d{1,2}$/.test(raw))
                throw new Error("Informe um valor em reais, por exemplo 1.234,56.");
            raw = raw.replace(/\./g, "").replace(",", ".");
        } else if (/^-?\d{1,3}(?:\.\d{3})+$/.test(raw)) {
            raw = raw.replace(/\./g, "");
        } else if (!/^-?\d+(\.\d{1,2})?$/.test(raw)) {
            throw new Error("Informe um valor em reais com até duas casas decimais.");
        }
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
    const clearNotice = () => { document.getElementById("message").hidden = true; };
    async function api(path, method = "GET", body) {
        const isFormData = body instanceof FormData;
        const response = await fetch(`/api/v1${path}`, {
            method,
            headers: body === undefined || isFormData ? {} : { "Content-Type": "application/json" },
            body: body === undefined ? undefined : isFormData ? body : JSON.stringify(body)
        });
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
            clearNotice();
            const button = form.querySelector('[type="submit"]');
            if (button) button.disabled = true;
            try { await submit(formValues(form), form); }
            catch (error) { notify(error.message, true); }
            finally { if (button) button.disabled = false; }
        });
    }
    function query(form, extra = {}) {
        const values = { ...formValues(form), ...extra };
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
    function rows(id, content, colspan = 4, emptyMessage = "Nenhum registro encontrado.") {
        document.getElementById(id).innerHTML = content || `<tr><td colspan="${colspan}" class="empty">${escape(emptyMessage)}</td></tr>`;
    }
    function initializeDates() {
        document.querySelectorAll("input[data-today]").forEach(input => input.value = dateToInput(today()));
        document.querySelectorAll("input[data-date-br]").forEach(input => input.title = "Informe a data no formato dia/mês/ano.");
        document.querySelectorAll("input.decimal-input").forEach(input => {
            input.pattern = "-?(?:[0-9]+|[0-9]{1,3}(?:\\.[0-9]{3})+)(?:,[0-9]{1,2}|[.][0-9]{1,2})?";
            input.title = "Informe um valor com até duas casas decimais. Exemplo: 1.234,56.";
        });
    }
    document.addEventListener("blur", event => {
        if (event.target instanceof HTMLInputElement) event.target.classList.add("touched");
    }, true);
    document.addEventListener("click", event => {
        const link = event.target.closest("a[data-expand-target]");
        if (!link) return;
        const target = document.getElementById(link.dataset.expandTarget);
        if (!(target instanceof HTMLDetailsElement)) return;
        event.preventDefault();
        target.open = true;
        history.replaceState(null, "", link.getAttribute("href"));
        target.scrollIntoView({ behavior: "smooth", block: "start" });
    });
    const targetFromHash = document.getElementById(decodeURIComponent(location.hash.slice(1)));
    if (targetFromHash instanceof HTMLDetailsElement && targetFromHash.classList.contains("page-option"))
        targetFromHash.open = true;
    function bindAction(id, handler) {
        document.getElementById(id).addEventListener("click", async event => {
            const button = event.target.closest("button[data-action]");
            if (!button) return;
            clearNotice();
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
            dialog.innerHTML = `<form><h2 id="action-title">${escape(title)}</h2><div class="grid">${fields.map(field => {
                if (field.options) return `<label>${escape(field.label)}<select name="${escape(field.name)}">${field.options.map(option => `<option value="${escape(option.value)}">${escape(option.label)}</option>`).join("")}</select></label>`;
                const isDate = field.type === "date";
                const isDecimal = field.type === "number";
                const type = isDate || isDecimal ? "text" : field.type || "text";
                const value = isDate ? dateToInput(field.value) : isDecimal ? decimalInput(field.value) : field.value ?? "";
                const format = isDate ? 'data-date-br placeholder="dd/mm/aaaa" inputmode="numeric" pattern="[0-3][0-9]/[01][0-9]/[0-9]{4}" title="Informe a data no formato dia/mês/ano."' : isDecimal ? 'inputmode="decimal" class="decimal-input" pattern="-?(?:[0-9]+|[0-9]{1,3}(?:\\.[0-9]{3})+)(?:,[0-9]{1,2}|[.][0-9]{1,2})?" title="Exemplo: 1.234,56."' : "";
                return `<label>${escape(field.label)}<input name="${escape(field.name)}" type="${type}" value="${escape(value)}" ${field.optional ? "" : "required"} ${format} /></label>`;
            }).join("")}</div><div class="actions"><button type="submit">Confirmar</button><button type="button" class="secondary" data-close>Voltar</button></div></form>`;
            const form = dialog.querySelector("form");
            fields.filter(field => field.options).forEach(field => form.elements[field.name].value = field.value);
            dialog.addEventListener("close", () => { const result = dialog.returnValue === "save" ? Object.fromEntries(new FormData(form)) : null; dialog.remove(); resolve(result); }, { once: true });
            form.addEventListener("submit", event => {
                event.preventDefault();
                try {
                    form.querySelectorAll("[data-date-br]").forEach(input => { if (input.value.trim()) input.value = dateToApi(input.value); });
                    dialog.close("save");
                } catch (error) {
                    const input = form.querySelector("[data-date-br]");
                    input.setCustomValidity(error.message);
                    input.reportValidity();
                    input.addEventListener("input", () => input.setCustomValidity(""), { once: true });
                }
            });
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
    document.querySelectorAll("header > nav a").forEach(a => { if (a.getAttribute("href").toLowerCase() === location.pathname.toLowerCase()) a.setAttribute("aria-current", "page"); });
    initializeDates();
    return { api, notify, listenForm, query, items, loadCatalogs, catalogName, rows, money, cents, today, date, dateToInput, decimalInput, escape, label, bindAction, ask, actionDate, cancelData, initializeDates, finishInitialLoad };
})();
