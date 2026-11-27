'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../src/AiInterview.Web/wwwroot/js/speech.js'), 'utf8');
const voice = (name, lang, extra = {}) => ({ name, lang, voiceURI: `${lang}:${name}`, default: false, ...extra });
const flush = async () => { for (let i = 0; i < 6; i++) await Promise.resolve(); };

function harness({ voices = [], language = 'vi-VN', storage = new Map(), storageFails = false, Audio, fetch } = {}) {
    let installed = voices, timerId = 0;
    const timers = new Map(), listeners = new Map(), spoken = [], notices = [];
    const synth = {
        cancelled: 0,
        getVoices: () => installed,
        addEventListener: (name, callback) => listeners.set(name, callback),
        cancel() { this.cancelled++; },
        speak: utterance => spoken.push(utterance)
    };
    const window = { Audio, fetch, speechSynthesis: synth, SpeechSynthesisUtterance: class { constructor(text) { this.text = text; } },
        localStorage: { getItem: key => { if (storageFails) throw new Error('Denied'); return storage.get(key); }, setItem: (key, value) => { if (storageFails) throw new Error('Denied'); storage.set(key, value); } } };
    vm.runInNewContext(source, { window, URL: { createObjectURL: () => 'blob:audio', revokeObjectURL() {} }, setTimeout(fn, ms) { const id = ++timerId; timers.set(id, { fn, ms }); return id; }, clearTimeout: id => timers.delete(id) });
    const adapter = new window.InterviewSpeech.BrowserTextToSpeech(language);
    const element = () => ({ value: '', disabled: false, textContent: '', children: [], events: {}, ownerDocument: { createElement: () => ({}) },
        addEventListener(name, callback) { this.events[name] = callback; }, replaceChildren() { this.children = []; }, appendChild(value) { this.children.push(value); } });
    const voiceSelect = element(), rateSelect = element(), statusElement = element();
    adapter.bindControls({ voiceSelect, rateSelect, statusElement, notice: message => notices.push(message) });
    return { adapter, synth, spoken, notices, voiceSelect, rateSelect, statusElement, storage, timers,
        install(voices, fireEvent = true) { installed = voices; if (fireEvent) listeners.get('voiceschanged')?.(); },
        async timeout() { const pending = [...timers.values()]; assert.ok(pending.length); for (const timer of pending) timer.fn(); await flush(); },
        drain() { let index = 0; while (index < spoken.length) { assert.ok(index < 1000); spoken[index++].onend(); } }
    };
}

test('TTS waits for asynchronous voice discovery and selects the requested language', async () => {
    const h = harness(); const speaking = h.adapter.speak('Bạn giải thích giúp mình.');
    await flush(); assert.equal(h.spoken.length, 0);
    const correct = voice('Vietnamese', 'vi-VN');
    h.install([voice('English default', 'en-US', { default: true }), correct]);
    assert.equal(await speaking, true); assert.equal(h.spoken[0].voice, correct); assert.equal(h.spoken[0].lang, 'vi-VN');
    assert.equal(h.timers.size, 0); assert.equal(h.voiceSelect.children.length, 2);
});

test('TTS reports a missing language instead of speaking Vietnamese through an English voice', async () => {
    const h = harness({ voices: [voice('English', 'en-US')] });
    const speaking = h.adapter.speak('Câu hỏi tiếng Việt.'); await h.timeout();
    assert.equal(await speaking, false); assert.equal(h.spoken.length, 0); assert.equal(h.notices.length, 1);
    assert.match(h.notices[0], /tiếng Việt/); assert.equal(h.voiceSelect.disabled, true);
});

test('TTS rechecks discovered voices after the timeout even when the browser emits no event', async () => {
    const h = harness(); const speaking = h.adapter.speak('Câu hỏi.');
    h.install([voice('Vietnamese', 'vi-VN')], false); await h.timeout();
    assert.equal(await speaking, true); assert.equal(h.spoken.length, 1);
});

test('stopping while voices load cancels the pending read and clears its timer', async () => {
    const h = harness(); const speaking = h.adapter.speak('Không được đọc sau khi dừng.');
    h.adapter.stop(); h.install([voice('Vietnamese', 'vi-VN')]);
    assert.equal(await speaking, false); assert.equal(h.spoken.length, 0); assert.equal(h.timers.size, 0);
});

test('a compatible English regional voice uses its own language, never Vietnamese', async () => {
    const english = voice('British English', 'en-GB');
    const h = harness({ language: 'en-US', voices: [voice('Vietnamese', 'vi-VN'), english] });
    await h.adapter.speak('Explain this approach.');
    assert.equal(h.spoken[0].voice, english); assert.equal(h.spoken[0].lang, 'en-GB');
    assert.equal(h.voiceSelect.children.length, 2);
});

test('manual voice and speed preferences survive reload and remain separate for each language', async () => {
    const voices = [voice('Natural Voice', 'vi-VN'), voice('Chosen Voice', 'vi-VN'), voice('English Voice', 'en-US')];
    const h = harness({ voices }); h.voiceSelect.value = voices[1].voiceURI; h.voiceSelect.events.change();
    h.rateSelect.value = '0.85'; h.rateSelect.events.change();
    const restored = harness({ voices, storage: h.storage }); await restored.adapter.speak('Kiểm tra giọng đã chọn.');
    assert.equal(restored.spoken[0].voice, voices[1]); assert.equal(restored.spoken[0].rate, 0.85);
    const english = harness({ voices, storage: h.storage, language: 'en-US' }); await english.adapter.speak('English question.');
    assert.equal(english.spoken[0].voice, voices[2]); assert.equal(english.spoken[0].rate, 0.95);
});

test('invalid saved voices and rates cannot force a wrong-language voice or unsafe speed', async () => {
    const english = voice('English', 'en-US'), vietnamese = voice('Vietnamese', 'vi-VN');
    const storage = new Map([['interview-speech:vi', JSON.stringify({ voiceURI: english.voiceURI, rate: 100 })]]);
    const h = harness({ voices: [english, vietnamese], storage }); await h.adapter.speak('Xin chào.');
    assert.equal(h.spoken[0].voice, vietnamese); assert.equal(h.spoken[0].rate, 0.95);
});

test('normalization preserves Vietnamese accents and prose while removing Markdown and fenced code', () => {
    const h = harness();
    const text = '# Câu hỏi\nGiải thích **kế thừa** trong `C#`, OOP và [tài liệu](https://example.test).\n```csharp\nvar shouldNotBeRead = 100;\n```\nGiữ tiếng Việt có dấu.';
    const spoken = h.adapter.normalize(text);
    assert.match(spoken, /Giải thích kế thừa trong C thăng, lập trình hướng đối tượng/);
    assert.match(spoken, /tài liệu/); assert.match(spoken, /Đoạn mã được hiển thị trên màn hình/);
    assert.match(spoken, /Giữ tiếng Việt có dấu/); assert.doesNotMatch(spoken, /shouldNotBeRead|```|https:/);
    assert.equal(h.adapter.normalize('Dùng user_id và phép tính x*y.'), 'Dùng user_id và phép tính x*y.');
});

test('English speech expands technical notation without changing the visible source question', () => {
    const h = harness({ language: 'en-US' }); const input = 'Explain C#, C++, ASP.NET, OOP and API.';
    assert.equal(h.adapter.normalize(input), 'Explain C sharp, C plus plus, A S P dot net, object oriented programming and A P I.');
    assert.equal(input, 'Explain C#, C++, ASP.NET, OOP and API.');
});

test('line breaks, dashes and slashes become natural spoken pauses and words', () => {
    const vi = harness(); const en = harness({ language: 'en-US' });
    assert.equal(vi.adapter.normalize('Bối cảnh dự án\n- Thiết kế REST/GraphQL\n- Kiểm thử — ví dụ cụ thể\nBạn chọn gì?'),
        'Bối cảnh dự án. Thiết kế REST hoặc GraphQL. Kiểm thử, ví dụ cụ thể. Bạn chọn gì?');
    assert.equal(en.adapter.normalize('Context: a legacy service\nCompare input / output - then decide.'),
        'Context: a legacy service. Compare input or output, then decide.');
    assert.equal(vi.adapter.normalize('Tỉ lệ 1/2 vẫn giữ nguyên.'), 'Tỉ lệ 1/2 vẫn giữ nguyên.');
});

test('a long sentence is split at commas before falling back to word boundaries', async () => {
    const h = harness({ voices: [voice('Vietnamese', 'vi-VN')] });
    const clause = 'mình muốn nghe cách bạn xử lý tình huống này trong dự án thực tế';
    const input = `${Array(6).fill(clause).join(', ')}.`;
    await h.adapter.speak(input); h.drain();
    assert.ok(h.spoken.length > 1); assert.ok(h.spoken.every(item => item.text.length <= 240));
    assert.ok(h.spoken.slice(0, -1).every(item => item.text.endsWith(',')));
    assert.equal(h.spoken.map(item => item.text).join(' '), h.adapter.normalize(input));
});

test('Gemini audio plays first, a busy quota waits instead of switching voices, and only a missing AI voice falls back', async () => {
    const created = [], responses = [];
    class Audio { constructor(src) { this.src = src; created.push(this); } play() { return Promise.resolve(); } pause() { this.paused = true; } }
    const h = harness({ voices: [voice('Vietnamese', 'vi-VN')], Audio, fetch: async () => responses.shift() });
    const ok = { ok: true, status: 200, blob: async () => 'wav' }, busy = { ok: false, status: 503, headers: { get: () => '20' } };
    responses.push(ok);
    assert.equal(await h.adapter.speak('Câu hỏi.', '/s'), true); assert.equal(created.length, 1); assert.equal(h.spoken.length, 0);
    responses.push(busy, ok);
    const waiting = h.adapter.speak('Câu hỏi.', '/s'); await flush();
    assert.equal(created[0].paused, true); assert.match(h.notices.at(-1), /20 giây/); assert.equal(h.spoken.length, 0);
    await h.timeout(); assert.equal(await waiting, true); assert.equal(created.length, 2); assert.equal(h.spoken.length, 0);
    responses.push({ ok: false, status: 503, json: async () => ({ code: 'quota_daily', message: 'Giọng Gemini đã hết lượt hôm nay, sẽ có lại lúc 15:00.' }) }, ok);
    assert.equal(await h.adapter.speak('Câu hỏi.', '/s'), true); assert.match(h.notices.at(-1), /15:00/); assert.equal(responses.length, 1); assert.equal(h.spoken.length, 0);
    responses.length = 0;
    responses.push({ ok: false, status: 404 });
    assert.equal(await h.adapter.speak('Câu hỏi.', '/s'), true); assert.equal(h.spoken.length, 1);
});

test('long questions are spoken sequentially without cutting words or queuing duplicate sentences', async () => {
    const h = harness({ voices: [voice('Vietnamese', 'vi-VN')] });
    const input = `${'từ dài có dấu '.repeat(60).trim()}. Câu kết thúc?`;
    await h.adapter.speak(input); assert.equal(h.spoken.length, 1); h.drain();
    assert.ok(h.spoken.length > 3); assert.ok(h.spoken.every(item => item.text.length <= 240));
    assert.equal(h.spoken.map(item => item.text).join(' '), h.adapter.normalize(input));
});

test('a new read replaces old playback and stale end/error events cannot restart the old question', async () => {
    const h = harness({ voices: [voice('Vietnamese', 'vi-VN')] });
    await h.adapter.speak('Câu cũ thứ nhất. Câu cũ thứ hai.'); const old = h.spoken[0];
    await h.adapter.speak('Câu mới.'); old.onend(); old.onerror({ error: 'network' });
    assert.deepEqual(h.spoken.map(item => item.text), ['Câu cũ thứ nhất.', 'Câu mới.']); assert.equal(h.notices.length, 0);
});

test('browser playback errors stop the remaining chunks and show an actionable message', async () => {
    const h = harness({ voices: [voice('Vietnamese', 'vi-VN')] });
    await h.adapter.speak('Câu một. Câu hai.'); const first = h.spoken[0];
    first.onerror({ error: 'voice-unavailable' }); first.onend();
    assert.equal(h.spoken.length, 1); assert.equal(h.notices.length, 1); assert.match(h.notices[0], /chọn giọng khác/);
});

test('denied local storage does not prevent voice selection or playback', async () => {
    const h = harness({ voices: [voice('Vietnamese', 'vi-VN')], storageFails: true });
    h.adapter.setVoice(h.adapter.voices[0].voiceURI); h.adapter.setRate(1.1);
    assert.equal(await h.adapter.speak('Vẫn đọc được.'), true); assert.equal(h.spoken[0].rate, 1.1);
});
