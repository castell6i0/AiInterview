(() => {
    'use strict';
    const waitMessage = seconds => `Bạn thao tác hơi nhanh, vui lòng chờ ${Math.max(1, Math.ceil(seconds))} giây rồi thử lại. Nội dung đang nhập vẫn được giữ.`;
    function retrySeconds(response, body = {}, now = Date.now()) {
        const header = response.headers?.get('Retry-After');
        const seconds = header && /^\d+(?:\.\d+)?$/.test(header.trim()) ? Number(header)
            : header ? (Date.parse(header) - now) / 1000 : NaN;
        const fallback = Number(body.retryAfterSeconds);
        return Math.max(1, Math.ceil(Number.isFinite(seconds) && seconds >= 0 ? seconds
            : Number.isFinite(fallback) && fallback > 0 ? fallback : 60));
    }
    async function responseError(response, fallback) {
        let body = {};
        try { body = await response.json() || {}; } catch { /* Never show a raw HTML error page. */ }
        const error = new Error(response.status === 429 ? waitMessage(retrySeconds(response, body))
            : typeof body.message === 'string' ? body.message : fallback);
        error.status = response.status;
        error.code = response.status === 429 ? 'rate_limited' : body.code;
        error.feature = body.feature;
        if (response.status === 429) error.retryAfterSeconds = retrySeconds(response, body);
        return error;
    }
    // Progressive enhancement: credentials/files stay in the current form on rejection, never in browser storage.
    const groups = new Map();
    function enhanceForm(form) {
        const groupName = form.dataset.rateGroup || form.action;
        if (!groups.has(groupName)) groups.set(groupName, { until: 0, timer: null, forms: [] });
        const group = groups.get(groupName);
        const buttons = [...form.querySelectorAll('button[type="submit"], input[type="submit"]')];
        const disabled = new Map(buttons.map(button => [button, button.disabled]));
        const message = document.createElement('p');
        message.className = 'alert alert-warning mt-3 mb-0'; message.hidden = true;
        message.setAttribute('role', 'status'); message.setAttribute('aria-live', 'polite');
        form.append(message);
        const state = { form, message, buttons, disabled, busy: false };
        group.forms.push(state);
        function refresh() {
            const remaining = Math.max(0, Math.ceil((group.until - Date.now()) / 1000));
            group.forms.forEach(item => {
                item.buttons.forEach(button => { button.disabled = item.busy || remaining > 0 || item.disabled.get(button); });
                if (remaining > 0) { item.message.hidden = false; item.message.textContent = waitMessage(remaining); }
                else if (group.until) { item.message.hidden = false; item.message.textContent = 'Bạn có thể bấm thử lại. Nội dung đang nhập vẫn được giữ.'; }
            });
            clearTimeout(group.timer);
            if (remaining > 0) group.timer = setTimeout(refresh, 1000);
        }
        form.addEventListener('submit', async event => {
            if (event.defaultPrevented) return;
            event.preventDefault();
            if (state.busy || group.until > Date.now()) { refresh(); return; }
            const data = new FormData(form);
            if (event.submitter?.name) data.append(event.submitter.name, event.submitter.value);
            state.busy = true; group.until = 0; form.setAttribute('aria-busy', 'true'); refresh();
            message.hidden = false; message.textContent = 'Đang xử lý, vui lòng chờ…';
            const controller = new AbortController();
            const timeout = setTimeout(() => controller.abort(), 90000);
            try {
                const response = await fetch(form.action, { method: 'POST', credentials: 'same-origin',
                    headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }, body: data, signal: controller.signal });
                if (!response.ok) throw await responseError(response, 'Chưa thực hiện được thao tác. Nội dung đang nhập vẫn được giữ; hãy thử lại.');
                if (response.redirected) throw new Error('Phiên đăng nhập có thể đã hết. Hãy mở đăng nhập trong tab khác rồi thử lại; nội dung tại đây vẫn được giữ.');
                const result = await response.json();
                const target = typeof result.redirectUrl === 'string' ? result.redirectUrl : '';
                if (!target.startsWith('/') || target.startsWith('//') || target.includes('\\')) throw new Error('Chưa nhận được kết quả hợp lệ. Hãy kiểm tra lại trang trước khi gửi lại.');
                window.location.assign(target);
            } catch (error) {
                if (error.code === 'rate_limited') group.until = Date.now() + error.retryAfterSeconds * 1000;
                message.hidden = false;
                message.textContent = error.name === 'AbortError' ? 'Yêu cầu mất nhiều thời gian. Nội dung vẫn được giữ; hãy kiểm tra lại trạng thái trước khi gửi lại.'
                    : error.message || 'Chưa kết nối được. Nội dung vẫn được giữ; hãy thử lại.';
            } finally {
                clearTimeout(timeout); state.busy = false; form.removeAttribute('aria-busy'); refresh();
            }
        });
        window.addEventListener('pageshow', event => {
            if (event.persisted) { state.busy = false; form.removeAttribute('aria-busy'); refresh(); }
        });
    }
    window.InterviewRequests = { waitMessage, retrySeconds, responseError, enhanceForm };
    document.querySelectorAll('form[data-preserve-on-limit]').forEach(enhanceForm);
})();
