(() => {
    const form = document.querySelector("[data-appointment-form]");
    const department = form.elements.DepartmentId;
    const doctor = form.elements.DoctorId;
    const date = form.elements.AppointmentDate;
    const duration = form.elements.DurationMinutes;
    const slots = form.elements.StartTime;
    const submit = form.querySelector('button[type="submit"]');
    let doctorRequest;
    let slotRequest;
    let submitting = false;

    function reset(select, message) {
        select.replaceChildren(new Option(message, ""));
        select.disabled = true;
    }

    function populate(select, items, placeholder, empty, selected) {
        reset(select, items.length ? placeholder : empty);
        for (const item of items) {
            select.add(new Option(item.text, item.value));
        }
        if (items.some(item => item.value === selected)) {
            select.value = selected;
        }
        select.disabled = items.length === 0;
    }

    async function fetchOptions(url, query, signal) {
        const response = await fetch(url + "?" + new URLSearchParams(query), { signal });
        if (!response.ok) throw new Error("Could not load appointment options");
        return response.json();
    }

    async function refreshSlots(selected = "") {
        slotRequest?.abort();
        submit.disabled = true;
        reset(slots, "Choose doctor, date and duration");
        if (!doctor.value || !date.value || !duration.value) return;

        reset(slots, "Loading available times...");
        const request = new AbortController();
        slotRequest = request;
        const query = { doctorId: doctor.value, date: date.value, durationMinutes: duration.value };
        if (form.dataset.appointmentId) query.appointmentId = form.dataset.appointmentId;

        try {
            const items = await fetchOptions(form.dataset.slotsUrl, query, request.signal);
            if (request.signal.aborted) return;
            populate(slots, items, "Select a time", "No available times", selected);
            submit.disabled = submitting || items.length === 0;
        } catch {
            if (!request.signal.aborted) reset(slots, "Could not load times. Change the date or doctor to retry.");
        }
    }

    async function refreshDoctors(selectedDoctor = "", selectedSlot = "") {
        doctorRequest?.abort();
        reset(doctor, "Choose a department");
        refreshSlots();
        if (!department.value) return;

        reset(doctor, "Loading doctors...");
        const request = new AbortController();
        doctorRequest = request;
        try {
            const items = await fetchOptions(form.dataset.doctorsUrl, { departmentId: department.value }, request.signal);
            if (request.signal.aborted) return;
            populate(doctor, items.map(item => ({ value: item.id, text: item.displayName })),
                "Select a doctor", "No active doctors in this department", selectedDoctor);
            refreshSlots(selectedSlot);
        } catch {
            if (!request.signal.aborted) reset(doctor, "Could not load doctors. Select the department again.");
        }
    }

    department.addEventListener("change", () => refreshDoctors());
    for (const input of [doctor, date, duration]) {
        input.addEventListener("change", () => refreshSlots());
    }
    form.addEventListener("submit", event => {
        if (submitting || !slots.value) {
            event.preventDefault();
            return;
        }
        submitting = true;
        submit.disabled = true;
    });
    window.addEventListener("pageshow", event => {
        if (event.persisted) {
            submitting = false;
            refreshSlots(slots.value);
        }
    });
    refreshDoctors(doctor.value, slots.dataset.selected);
})();
