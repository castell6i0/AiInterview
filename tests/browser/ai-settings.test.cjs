'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../../src/AiInterview.Web/wwwroot/js/ai-settings.js'), 'utf8');
// Deliberately synthetic IDs: interaction tests should not become stale when the curated catalog changes.
const suggestions = [
    { provider: 'Gemini', id: 'gemini-test-lite', name: 'Gemini Test Lite', hasFreeTier: true, description: 'A lightweight model.' },
    { provider: 'Gemini', id: 'gemini-test-flash', name: 'Gemini Test Flash', hasFreeTier: true, description: 'A second model.' },
    { provider: 'OpenAI', id: 'gpt-test-mini', name: 'GPT Test Mini', hasFreeTier: false, description: 'Requires API credit.' },
    { provider: 'Gemini', id: 'gemini-test-paid', name: 'Gemini Test Paid', hasFreeTier: false, description: 'Requires API credit.' }
];

function harness({ provider = 'Gemini', model = 'gemini-test-lite', catalog = JSON.stringify(suggestions),
    savedGeminiModel = 'gemini-test-lite', savedOpenAiModel = 'gpt-test-mini' } = {}) {
    const sideEffects = [];
    let focused = null;
    const element = (tagName = 'DIV') => {
        let value = '';
        const attributes = new Map(), listeners = new Map();
        const node = {
            tagName, children: [], dataset: {}, textContent: '', hidden: false, disabled: false, type: '', href: '',
            get value() { return value; },
            set value(next) {
                const text = String(next);
                value = tagName === 'SELECT' && !this.children.some(child => child.value === text) ? '' : text;
            },
            setAttribute: (name, next) => attributes.set(name, String(next)),
            getAttribute: name => attributes.get(name) ?? null,
            removeAttribute: name => attributes.delete(name),
            addEventListener(name, listener) {
                if (!listeners.has(name)) listeners.set(name, []);
                listeners.get(name).push(listener);
            },
            fire(name, extra = {}) {
                const event = { target: this, defaultPrevented: false, preventDefault() { this.defaultPrevented = true; }, ...extra };
                for (const listener of listeners.get(name) || []) listener(event);
                return event;
            },
            replaceChildren(...children) { this.children = children; value = ''; },
            append(...children) { this.children.push(...children); },
            focus() { focused = this; },
            querySelector(selector) { return selector === 'button[type="submit"]' ? this.submitButton : null; },
            querySelectorAll(selector) {
                return selector === 'button[data-idle-text]'
                    ? this.children.filter(child => child.tagName === 'BUTTON' && child.dataset.idleText !== undefined) : [];
            },
            submit() { sideEffects.push('submit'); },
            requestSubmit() { sideEffects.push('requestSubmit'); }
        };
        return node;
    };
    const ids = ['aiSettings', 'aiConnectionCheckForm', 'aiSettingsForm', 'Provider', 'Model', 'ApiKey', 'toggleApiKey',
        'keyProviderName', 'selectedKeyState', 'providerChangeNotice', 'ModelPreset', 'modelQuickPick',
        'modelSelectionNote', 'modelProviderNote', 'modelTierLabel', 'modelPricingLink', 'saveAndCheckModel'];
    const elements = Object.fromEntries(ids.map(id => [id, element(id === 'ModelPreset' ? 'SELECT' : 'DIV')]));
    Object.assign(elements.aiSettings.dataset, { geminiModel: savedGeminiModel, openAiModel: savedOpenAiModel,
        hasGeminiKey: 'true', hasOpenAiKey: 'false', geminiKeyUnavailable: 'false', openAiKeyUnavailable: 'false' });
    elements.Provider.value = provider;
    elements.Model.value = model;
    elements.ApiKey.type = 'password';
    elements.aiConnectionCheckForm.submitButton = element('BUTTON');
    elements.saveAndCheckModel.tagName = 'BUTTON';
    elements.saveAndCheckModel.type = 'submit';
    elements.saveAndCheckModel.name = 'CheckConnection';
    elements.saveAndCheckModel.value = 'true';
    elements.saveAndCheckModel.textContent = 'Lưu & kiểm tra model →';
    elements.aiSettingsForm.submitButton = elements.saveAndCheckModel;
    elements.aiSettingsForm.append(elements.saveAndCheckModel);
    elements.saveOnlyModel = element('BUTTON');
    elements.saveOnlyModel.type = 'submit';
    elements.saveOnlyModel.textContent = 'Chỉ lưu';
    elements.aiSettingsForm.append(elements.saveOnlyModel);
    if (catalog !== null) { elements.aiModelCatalog = element('SCRIPT'); elements.aiModelCatalog.textContent = catalog; }
    const window = element('WINDOW');
    const network = () => { sideEffects.push('network'); throw new Error('Model selection must not call a provider.'); };
    window.fetch = network;
    window.location = { assign: () => sideEffects.push('navigation') };
    const document = { getElementById: id => elements[id] || null, createElement: tag => element(tag.toUpperCase()) };
    vm.runInNewContext(source, { document, window, fetch: network, XMLHttpRequest: network });
    return { elements, window, sideEffects, get focused() { return focused; },
        chooseModel(value) { elements.ModelPreset.value = value; elements.ModelPreset.fire('change'); },
        typeModel(value) { elements.Model.value = value; elements.Model.fire('input'); },
        changeProvider(value) { elements.Provider.value = value; elements.Provider.fire('change'); } };
}

test('choosing a suggested model updates the editable input without saving or calling AI', () => {
    const h = harness();
    assert.equal(h.elements.modelQuickPick.hidden, false);
    assert.deepEqual(h.elements.ModelPreset.children.map(option => option.value), ['', 'gemini-test-lite', 'gemini-test-flash']);
    assert.equal(h.elements.ModelPreset.value, 'gemini-test-lite');
    assert.match(h.elements.ModelPreset.children[1].textContent, /Có hạn mức miễn phí/);
    h.chooseModel('gemini-test-flash');
    assert.equal(h.elements.Model.value, 'gemini-test-flash');
    assert.equal(h.elements.ModelPreset.value, 'gemini-test-flash');
    assert.match(h.elements.modelSelectionNote.textContent, /Bấm Lưu & kiểm tra model/);
    assert.deepEqual(h.sideEffects, []);
});

test('typing a custom model keeps its exact value and leaves the preset list in manual mode', () => {
    const h = harness();
    h.typeModel('private-custom.model-v2');
    assert.equal(h.elements.Model.value, 'private-custom.model-v2');
    assert.equal(h.elements.ModelPreset.value, '');
    assert.match(h.elements.modelSelectionNote.textContent, /Model tự nhập/);
    h.typeModel('gemini-test-flash');
    assert.equal(h.elements.ModelPreset.value, 'gemini-test-flash');
    assert.deepEqual(h.sideEffects, []);
});

test('a saved custom model is preserved on page load instead of silently adopting a suggestion', () => {
    const h = harness({ model: 'my-private-model', savedGeminiModel: 'my-private-model' });
    assert.equal(h.elements.Model.value, 'my-private-model');
    assert.equal(h.elements.ModelPreset.value, '');
    assert.equal(h.elements.modelQuickPick.hidden, false);
    assert.deepEqual(h.sideEffects, []);
});

test('managing a legacy paid provider hides quick picks, preserves its model and removes typed credentials', () => {
    const h = harness({ savedOpenAiModel: 'my-openai-model' });
    h.elements.ApiKey.value = 'synthetic-typed-key';
    h.elements.toggleApiKey.fire('click');
    assert.equal(h.elements.ApiKey.type, 'text');
    h.typeModel('unsaved-gemini-model');
    h.changeProvider('OpenAI');
    assert.equal(h.elements.ApiKey.value, '');
    assert.equal(h.elements.ApiKey.type, 'password');
    assert.equal(h.elements.toggleApiKey.getAttribute('aria-pressed'), 'false');
    assert.equal(h.elements.Model.value, 'my-openai-model');
    assert.equal(h.elements.ModelPreset.value, '');
    assert.deepEqual(h.elements.ModelPreset.children.map(option => option.value), ['']);
    assert.equal(h.elements.modelQuickPick.hidden, true);
    assert.equal(h.elements.modelTierLabel.textContent, 'Cần số dư API');
    assert.match(h.elements.modelPricingLink.href, /openai\.com/);
    assert.equal(h.elements.keyProviderName.textContent, 'OpenAI');
    assert.equal(h.elements.selectedKeyState.textContent, 'Chưa có key cá nhân');
    assert.match(h.elements.providerChangeNotice.textContent, /xóa key vừa nhập/);
    h.chooseModel('gpt-test-mini');
    assert.equal(h.elements.Model.value, 'my-openai-model');
    h.changeProvider('Gemini');
    assert.equal(h.elements.Model.value, 'gemini-test-lite');
    assert.equal(h.elements.ModelPreset.value, 'gemini-test-lite');
    assert.match(h.elements.modelPricingLink.href, /ai\.google\.dev/);
    assert.equal(h.elements.selectedKeyState.textContent, 'Đã lưu key');
    assert.deepEqual(h.sideEffects, []);
});

test('paid, unverified and other-provider entries never enter the free model list', () => {
    const h = harness({ catalog: JSON.stringify([...suggestions,
        { provider: 'Gemini', id: 'gemini-unverified', name: 'Unverified' },
        { provider: 'OpenAI', id: 'gpt-labelled-free', name: 'Wrong provider', hasFreeTier: true }
    ]) });
    assert.deepEqual(h.elements.ModelPreset.children.map(option => option.value), ['', 'gemini-test-lite', 'gemini-test-flash']);
    h.typeModel('gemini-test-paid');
    assert.equal(h.elements.ModelPreset.value, '');
    assert.match(h.elements.modelSelectionNote.textContent, /kiểm tra phí/);
    h.changeProvider('OpenAI');
    assert.equal(h.elements.modelQuickPick.hidden, true);
    assert.deepEqual(h.sideEffects, []);
});

test('choosing manual entry focuses the model input without deleting the existing text', () => {
    const h = harness();
    h.chooseModel('');
    assert.equal(h.focused, h.elements.Model);
    assert.equal(h.elements.Model.value, 'gemini-test-lite');
    h.typeModel('another-model');
    h.chooseModel('');
    assert.equal(h.elements.Model.value, 'another-model');
    assert.deepEqual(h.sideEffects, []);
});

for (const [name, catalog] of [['missing', null], ['invalid JSON', '{broken'], ['non-array JSON', '{}'],
    ['invalid entries', '[null,42,{"id":"wrong","name":"Wrong","provider":"Other"}]']]) {
    test(`${name} catalog keeps manual model entry usable and does not advertise a broken list`, () => {
        const h = harness({ catalog, model: 'saved-custom' });
        assert.equal(h.elements.modelQuickPick.hidden, true);
        assert.equal(h.elements.Model.value, 'saved-custom');
        h.typeModel('new-custom');
        assert.equal(h.elements.Model.value, 'new-custom');
        assert.equal(h.elements.ModelPreset.value, '');
        assert.match(h.elements.modelSelectionNote.textContent, /Model tự nhập/);
        h.changeProvider('OpenAI');
        h.typeModel('another-custom');
        assert.equal(h.elements.Model.value, 'another-custom');
        assert.equal(h.elements.modelQuickPick.hidden, true);
        assert.deepEqual(h.sideEffects, []);
    });
}

test('model interactions preserve credential clearing when leaving or restoring the page', () => {
    const h = harness();
    h.elements.ApiKey.value = 'synthetic-typed-key';
    h.elements.toggleApiKey.fire('click');
    h.chooseModel('gemini-test-flash');
    h.window.fire('pagehide');
    assert.equal(h.elements.ApiKey.value, '');
    assert.equal(h.elements.ApiKey.type, 'password');
    h.elements.ApiKey.value = 'synthetic-restored-key';
    h.window.fire('pageshow', { persisted: true });
    assert.equal(h.elements.ApiKey.value, '');
    assert.equal(h.elements.ApiKey.type, 'password');
    assert.equal(h.elements.Model.value, 'gemini-test-flash');
});

test('save and check posts the submitter choice while preventing a second form submission', () => {
    const h = harness();
    const form = h.elements.aiSettingsForm, button = h.elements.saveAndCheckModel;
    const idleText = button.textContent;
    h.chooseModel('gemini-test-flash');
    const first = form.fire('submit', { submitter: button });
    assert.equal(first.defaultPrevented, false);
    assert.equal(form.getAttribute('aria-busy'), 'true');
    assert.equal(button.getAttribute('aria-disabled'), 'true');
    assert.equal(button.disabled, false, 'A disabled submitter would lose CheckConnection in the native POST.');
    const postedSubmitter = button.name && !button.disabled ? { [button.name]: button.value } : {};
    assert.deepEqual(postedSubmitter, { CheckConnection: 'true' });
    assert.match(button.textContent, /Đang lưu & thử model/);
    assert.equal(form.fire('submit', { submitter: button }).defaultPrevented, true);
    assert.equal(form.fire('submit', { submitter: h.elements.saveOnlyModel }).defaultPrevented, true);
    h.window.fire('pageshow', { persisted: true });
    assert.equal(form.getAttribute('aria-busy'), null);
    assert.equal(button.getAttribute('aria-disabled'), null);
    assert.equal(button.textContent, idleText);
    assert.equal(form.fire('submit', { submitter: button }).defaultPrevented, false);
});

test('save-only and keyboard submission remain distinct from save-and-check', () => {
    const h = harness();
    const form = h.elements.aiSettingsForm, button = h.elements.saveOnlyModel;
    assert.equal(form.fire('submit', { submitter: button }).defaultPrevented, false);
    assert.equal(button.name, undefined);
    assert.equal(button.disabled, false);
    assert.equal(button.textContent, 'Đang lưu…');
    h.window.fire('pageshow', { persisted: true });
    assert.equal(button.textContent, 'Chỉ lưu');
    assert.equal(form.fire('submit').defaultPrevented, false);
    assert.equal(form.getAttribute('aria-busy'), 'true');
    assert.deepEqual(h.sideEffects, []);
});
