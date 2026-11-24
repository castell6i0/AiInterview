// Login code popup: 6 boxes -> digits gather and spin -> spinner -> main page.
(function () {
    var form = document.querySelector("[data-otp]");
    if (!form) return;
    var row = form.querySelector(".otp-digits"), boxes = [].slice.call(form.querySelectorAll(".otp-digit"));
    var error = form.querySelector(".otp-error"), resend = form.querySelector("[data-otp-resend]");
    var token = form.querySelector("input[name=__RequestVerificationToken]").value;
    var busy = false;
    var wait = function (ms) { return new Promise(function (r) { setTimeout(r, ms); }); };
    var post = function (url, body) {
        return fetch(url, { method: "POST", body: body, headers: { RequestVerificationToken: token } })
            .then(function (r) { return r.json(); }).catch(function () { return { ok: false, error: "Mất kết nối. Thử lại." }; });
    };

    // Boxes only mirror the single real input, so an IME commit lands in one field, not a chain of boxes.
    var input = form.elements.code;
    function render() {
        var v = input.value.replace(/\D/g, "").slice(0, 6);
        if (input.value !== v) input.value = v;
        boxes.forEach(function (b, i) {
            b.textContent = v[i] || "";
            b.classList.toggle("is-active", document.activeElement === input && i === Math.min(v.length, 5));
        });
        return v;
    }
    input.addEventListener("input", function (e) { if (!e.isComposing && render().length === 6) submit(); });
    input.addEventListener("compositionend", function () { if (render().length === 6) submit(); });
    input.addEventListener("focus", render);
    input.addEventListener("blur", render);
    // Modal: the login form behind the popup is not reachable by keyboard or screen reader.
    document.querySelectorAll(".auth-card > :not(.otp-backdrop)").forEach(function (el) { el.inert = true; });
    input.focus();

    async function submit() {
        if (busy) return;
        busy = true; error.textContent = "";
        input.readOnly = true;
        // Each digit's distance to the row centre, so they all meet in the middle.
        var mid = row.getBoundingClientRect().left + row.offsetWidth / 2;
        boxes.forEach(function (b, i) {
            var r = b.getBoundingClientRect();
            b.style.setProperty("--dx", mid - (r.left + r.width / 2) + "px");
            b.style.setProperty("--a", i * 60 + "deg");
        });
        row.classList.add("is-gather");
        var result = await Promise.all([post(form.action, new FormData(form)), wait(450)]).then(function (x) { return x[0]; });
        if (result.ok) {
            row.classList.add("is-loading");
            await wait(500);
            location.assign(result.redirect); // the spinner keeps turning until the next page paints
            return;
        }
        row.classList.remove("is-gather");
        input.value = ""; input.readOnly = false;
        row.classList.remove("is-shake"); void row.offsetWidth; row.classList.add("is-shake");
        error.textContent = result.error;
        input.focus(); render();
        busy = false;
    }
    form.addEventListener("submit", function (e) { e.preventDefault(); submit(); });

    resend.addEventListener("click", async function () {
        resend.disabled = true;
        var result = await post(form.dataset.resend, new FormData());
        error.textContent = result.ok ? "Đã gửi mã mới. Mã cũ không còn dùng được." : result.error;
        for (var s = 60; s > 0; s--) { resend.textContent = "Gửi lại sau " + s + "s"; await wait(1000); }
        resend.textContent = "Gửi lại mã"; resend.disabled = false;
    });
})();
