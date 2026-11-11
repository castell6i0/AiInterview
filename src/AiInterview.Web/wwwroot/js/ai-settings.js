(() => {
    'use strict';

    const root = document.getElementById('aiSettings');
    if (!root) return;
    // Native form fallback when progressive request handling is unavailable.
    if (!window.InterviewRequests) {
    const settingsForm = document.getElementById('aiSettingsForm');
    settingsForm?.addEventListener('submit', event => {
        if (settingsForm.getAttribute('aria-busy') === 'true') { event.preventDefault(); return; }
        settingsForm.setAttribute('aria-busy', 'true');
        const button = event.submitter;
        if (button) {
            button.dataset.idleText = button.textContent;
            // Keep the clicked button enabled so its CheckConnection name/value is posted.
            button.setAttribute('aria-disabled', 'true');
            button.textContent = button.name === 'CheckConnection' ? 'Đang lưu & thử model…' : 'Đang lưu…';
        }
    });
    window.addEventListener('pageshow', event => {
        if (!event.persisted || !settingsForm) return;
        settingsForm.removeAttribute('aria-busy');
        settingsForm.querySelectorAll('button[data-idle-text]').forEach(button => {
            button.textContent = button.dataset.idleText; button.removeAttribute('aria-disabled');
        });
    });
    const checkForm = document.getElementById('aiConnectionCheckForm');
    checkForm?.addEventListener('submit', event => {
        if (checkForm.getAttribute('aria-busy') === 'true') { event.preventDefault(); return; }
        checkForm.setAttribute('aria-busy', 'true');
        const button = checkForm.querySelector('button[type="submit"]');
        if (button) { button.disabled = true; button.textContent = 'Đang thử tạo nội dung…'; }
    });
    window.addEventListener('pageshow', event => {
        if (!event.persisted || !checkForm || checkForm.getAttribute('aria-busy') !== 'true') return;
        checkForm.removeAttribute('aria-busy');
        const button = checkForm.querySelector('button[type="submit"]');
        if (button) { button.disabled = false; button.textContent = 'Kiểm tra kết nối đã lưu'; }
    });
    }
    const provider = document.getElementById('Provider');
    const model = document.getElementById('Model');
    const key = document.getElementById('ApiKey');
    const reveal = document.getElementById('toggleApiKey');
    const keyName = document.getElementById('keyProviderName');
    const keyState = document.getElementById('selectedKeyState');
    const notice = document.getElementById('providerChangeNotice');
    if (!provider || !model || !key || !reveal || !keyName || !keyState || !notice) return;

    const preset = document.getElementById('ModelPreset');
    const picker = document.getElementById('modelQuickPick');
    const modelNote = document.getElementById('modelSelectionNote');
    const providerNote = document.getElementById('modelProviderNote');
    const tierLabel = document.getElementById('modelTierLabel');
    const pricingLink = document.getElementById('modelPricingLink');
    let catalog = [];
    try { catalog = JSON.parse(document.getElementById('aiModelCatalog')?.textContent || '[]'); } catch { /* Manual entry remains available. */ }
    if (!Array.isArray(catalog)) catalog = [];
    catalog = catalog.filter(item => item && typeof item.id === 'string' && typeof item.name === 'string'
        && item.provider === 'Gemini' && item.hasFreeTier === true);
    function syncModel() {
        if (!preset || !modelNote) return;
        const match = catalog.find(item => item.provider === provider.value && item.id === model.value.trim());
        preset.value = match ? match.id : '';
        modelNote.textContent = match
            ? match.description + ' Bấm Lưu & kiểm tra model để thử bằng key của bạn.'
            : 'Model tự nhập không nằm trong danh sách miễn phí; hãy kiểm tra phí và hạn mức trước khi dùng. Bấm Lưu & kiểm tra model để thử quyền truy cập và khả năng tạo nội dung.';
    }
    function renderModels() {
        if (!preset || !picker) return;
        preset.replaceChildren();
        const custom = document.createElement('option');
        custom.value = ''; custom.textContent = 'Tự nhập model bên dưới'; preset.append(custom);
        const suggestions = catalog.filter(item => item.provider === provider.value);
        suggestions.forEach(item => {
            const option = document.createElement('option'); option.value = item.id;
            option.textContent = item.name + ' · Có hạn mức miễn phí';
            preset.append(option);
        });
        picker.hidden = suggestions.length === 0;
        const isOpenAi = provider.value === 'OpenAI';
        if (tierLabel) tierLabel.textContent = isOpenAi ? 'Cần số dư API' : 'Có lựa chọn miễn phí';
        if (providerNote) providerNote.textContent = isOpenAi
            ? 'Đây là kết nối OpenAI cũ có phí; không có model gợi ý miễn phí. Chọn Google Gemini và nhập key Gemini để xem các lựa chọn có hạn mức miễn phí.'
            : 'Danh sách chỉ có model với hạn mức miễn phí cho tài khoản đủ điều kiện. Dùng dự án Google AI Studio thuộc gói miễn phí; dự án đã bật thanh toán vẫn có thể tính phí.';
        if (pricingLink) pricingLink.href = isOpenAi ? 'https://developers.openai.com/api/docs/pricing' : 'https://ai.google.dev/gemini-api/docs/pricing';
        syncModel();
    }
    preset?.addEventListener('change', () => {
        if (!preset.value) { model.focus(); return; }
        if (!catalog.some(item => item.provider === provider.value && item.id === preset.value)) return;
        model.value = preset.value; syncModel();
    });
    model.addEventListener('input', syncModel);
    renderModels();

    function hideKey() {
        key.type = 'password';
        reveal.textContent = 'Hiện key';
        reveal.setAttribute('aria-pressed', 'false');
    }

    reveal.hidden = false;
    reveal.addEventListener('click', () => {
        const shouldReveal = key.type === 'password';
        key.type = shouldReveal ? 'text' : 'password';
        reveal.textContent = shouldReveal ? 'Ẩn key' : 'Hiện key';
        reveal.setAttribute('aria-pressed', String(shouldReveal));
    });

    provider.addEventListener('change', () => {
        const isOpenAi = provider.value === 'OpenAI';
        const hadTypedKey = key.value.length > 0;
        // A key entered for one provider must never be submitted to another.
        key.value = '';
        hideKey();
        model.value = isOpenAi ? root.dataset.openAiModel : root.dataset.geminiModel;
        renderModels();
        keyName.textContent = isOpenAi ? 'OpenAI' : 'Gemini';
        const hasKey = (isOpenAi ? root.dataset.hasOpenAiKey : root.dataset.hasGeminiKey) === 'true';
        const unavailable = (isOpenAi ? root.dataset.openAiKeyUnavailable : root.dataset.geminiKeyUnavailable) === 'true';
        keyState.textContent = unavailable ? 'Cần nhập lại key' : hasKey ? 'Đã lưu key' : 'Chưa có key cá nhân';
        notice.textContent = hadTypedKey
            ? 'Đã đổi nhà cung cấp và xóa key vừa nhập. Hãy nhập key tương ứng hoặc để trống để dùng key đã lưu.'
            : 'Lựa chọn mới chưa được áp dụng. Chọn model rồi bấm “Lưu & kiểm tra model” để sử dụng.';
    });

    // Avoid leaving typed credentials in a restored page from the browser's back cache.
    window.addEventListener('pagehide', () => { key.value = ''; hideKey(); });
    window.addEventListener('pageshow', event => {
        if (event.persisted) { key.value = ''; hideKey(); }
    });
})();
