(() => {
    for (const form of document.querySelectorAll("[data-lab-form]")) {
        form.addEventListener("submit", event => {
            if (form.dataset.submitting || (form.dataset.confirm && !window.confirm(form.dataset.confirm))) {
                event.preventDefault();
                return;
            }
            form.dataset.submitting = "true";
            for (const button of form.querySelectorAll('button:not([type]), button[type="submit"]')) {
                button.disabled = true;
            }
        });
    }
    document.querySelector("[data-lab-print]")?.addEventListener("click", () => window.print());
})();
