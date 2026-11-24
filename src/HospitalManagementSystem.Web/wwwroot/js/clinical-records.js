(() => {
    const medicines = document.querySelector("[data-medicines]");
    if (medicines) {
        const rows = Array.from(medicines.querySelectorAll("[data-medicine]"));
        const add = medicines.querySelector("[data-add-medicine]");
        function updateAddButton() {
            add.disabled = rows.every(row => !row.hidden);
        }

        add.addEventListener("click", () => {
            const row = rows.find(row => row.hidden);
            if (!row) return;
            row.hidden = false;
            row.querySelector('input:not([type="hidden"])').focus();
            updateAddButton();
        });
        for (const row of rows) {
            row.querySelector("[data-remove-medicine]").addEventListener("click", () => {
                for (const field of row.querySelectorAll("input, textarea")) field.value = "";
                row.hidden = true;
                updateAddButton();
                add.focus();
            });
        }
        updateAddButton();
    }

    for (const form of document.querySelectorAll('main form[method="post"]')) {
        form.addEventListener("submit", event => {
            if (form.dataset.submitted || (form.dataset.confirm && !window.confirm(form.dataset.confirm))) {
                event.preventDefault();
                return;
            }
            form.dataset.submitted = "true";
            for (const button of form.querySelectorAll('button:not([type]), button[type="submit"]')) button.disabled = true;
        });
    }
    document.querySelector("[data-print-clinical]")?.addEventListener("click", () => window.print());
})();
