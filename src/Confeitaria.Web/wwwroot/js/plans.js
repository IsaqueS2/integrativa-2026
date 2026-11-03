"use strict";
(() => {
    const dialog = document.getElementById("plan-selection");
    let lastTrigger;

    document.querySelectorAll("[data-select-plan]").forEach(button => {
        button.addEventListener("click", () => {
            const card = button.closest("[data-plan]");
            document.getElementById("selected-plan-name").textContent = card.querySelector("h2").textContent;
            document.getElementById("selected-plan-price").textContent = card.querySelector(".plan-price").textContent.trim();
            document.getElementById("selected-plan-description").textContent = card.querySelector(".plan-description").textContent;
            lastTrigger = button;
            dialog.showModal();
        });
    });

    dialog.addEventListener("close", () => lastTrigger?.focus());
    dialog.addEventListener("click", event => {
        if (event.target !== dialog) return;
        const bounds = dialog.getBoundingClientRect();
        if (event.clientX < bounds.left || event.clientX > bounds.right ||
            event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close();
    });

    // The comparison stays usable without JavaScript, including its anchor link.
    document.querySelector('a[href="#plan-comparison"]').addEventListener("click", () => {
        document.getElementById("plan-comparison").open = true;
    });
    ui.finishInitialLoad();
})();
