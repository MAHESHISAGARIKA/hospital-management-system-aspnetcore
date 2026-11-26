document.querySelectorAll("[data-password-toggle]").forEach((button) => {
    const input = document.getElementById(button.getAttribute("aria-controls"));
    if (!(input instanceof HTMLInputElement)) return;

    button.hidden = false;
    button.addEventListener("click", () => {
        const show = input.type === "password";
        input.type = show ? "text" : "password";
        button.textContent = show ? "Hide password" : "Show password";
    });
});

const sidebarToggle = document.querySelector("[data-sidebar-toggle]");
sidebarToggle?.addEventListener("click", () => {
    const collapsed = document.body.classList.toggle("hms-sidebar-collapsed");
    sidebarToggle.setAttribute("aria-expanded", String(!collapsed));
    sidebarToggle.setAttribute("aria-label", collapsed ? "Expand navigation" : "Collapse navigation");
});
