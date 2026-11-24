(() => {
    const lines = [...document.querySelectorAll("[data-invoice-line]")];
    const add = document.querySelector("[data-add-charge]");
    if (add) {
        const updateAdd = () => { add.disabled = lines.every(line => !line.hidden); };
        for (const line of lines) {
            const service = line.querySelector("[data-billing-service]");
            const source = line.querySelector("[data-billing-source]");
            const quantity = line.querySelector("[data-billing-quantity]");
            const filterSources = () => {
                const selected = service.selectedOptions[0];
                for (const option of source.options) {
                    option.hidden = !!option.value && (option.dataset.category !== selected.dataset.category ||
                        (option.dataset.code || "") !== (selected.dataset.code || ""));
                }
                if (source.selectedOptions[0]?.hidden) source.value = "";
            };
            service.addEventListener("change", filterSources);
            source.addEventListener("change", () => {
                const value = source.selectedOptions[0]?.dataset.quantity;
                if (value) quantity.value = value;
            });
            line.querySelector("[data-remove-charge]").addEventListener("click", () => {
                service.selectedIndex = 0;
                source.value = "";
                quantity.value = "1";
                line.hidden = true;
                updateAdd();
                add.focus();
            });
            filterSources();
        }
        add.addEventListener("click", () => {
            const line = lines.find(item => item.hidden);
            if (line) {
                line.hidden = false;
                line.querySelector("select").focus();
            }
            updateAdd();
        });
        updateAdd();
    }
    const method = document.querySelector("[data-billing-method]");
    const reference = document.querySelector("[data-billing-reference]");
    if (method && reference) {
        const updateMethod = () => {
            reference.required = method.value === "BankTransfer";
            reference.disabled = !reference.required;
            if (reference.disabled) reference.value = "";
        };
        method.addEventListener("change", updateMethod);
        updateMethod();
    }
    for (const form of document.querySelectorAll("[data-billing-form]")) {
        form.addEventListener("submit", event => {
            if (form.dataset.submitting || (form.dataset.confirm && !window.confirm(form.dataset.confirm))) {
                event.preventDefault();
                return;
            }
            form.dataset.submitting = "true";
            for (const button of form.querySelectorAll('button:not([type]), button[type="submit"]')) button.disabled = true;
        });
    }
    document.querySelector("[data-billing-print]")?.addEventListener("click", () => window.print());
})();
