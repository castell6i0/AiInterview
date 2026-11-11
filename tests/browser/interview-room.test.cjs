'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const jsRoot = path.join(__dirname, '../../src/AiInterview.Web/wwwroot/js');
const source = name => fs.readFileSync(path.join(jsRoot, name), 'utf8');
const deferred = () => { let resolve; const promise = new Promise(r => { resolve = r; }); return { promise, resolve }; };
const flush = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };

function timers() {
    const pending = new Map(); let id = 0;
    return {
        pending,
        setTimeout(fn, ms) { const value = ++id; pending.set(value, { fn, ms }); return value; },
        clearTimeout(value) { pending.delete(value); },
        async run(ms) { const entry = [...pending].find(([, timer]) => timer.ms === ms); assert.ok(entry, `Missing ${ms}ms timer`); pending.delete(entry[0]); const result = entry[1].fn(); await flush(); return result; }
    };
}

function speechHarness() {
    const clock = timers(), instances = [];
    class Recognition {
        constructor() { instances.push(this); }
        start() { this.starts = (this.starts || 0) + 1; }
        stop() { this.stops = (this.stops || 0) + 1; }
        abort() { this.aborted = true; }
    }
    const context = { window: { SpeechRecognition: Recognition }, setTimeout: clock.setTimeout, clearTimeout: clock.clearTimeout };
    vm.runInNewContext(source('speech.js'), context);
    const results = [], errors = [];
    const adapter = new context.window.InterviewSpeech.BrowserSpeechToText('vi-VN', { result: text => results.push(text), error: text => errors.push(text) });
    const result = text => { const row = [{ transcript: text }]; row.isFinal = true; return { results: [row] }; };
    return { clock, adapter, instances, results, errors, result };
}

test('speech reserves the start immediately and waits for the final result before resolving stop', async () => {
    const h = speechHarness();
    assert.equal(h.adapter.start(), true);
    assert.equal(h.adapter.start(), false);
    assert.equal(h.instances.length, 1);
    const recognition = h.instances[0];
    const first = h.adapter.stop(), second = h.adapter.stop();
    assert.equal(first, second);
    assert.equal(recognition.stops, 1);
    let finished = false; first.then(() => { finished = true; }); await flush();
    assert.equal(finished, false);
    recognition.onresult(h.result('câu trả lời cuối'));
    recognition.onend();
    assert.equal(await first, true);
    recognition.onresult(h.result('kết quả đến muộn'));
    assert.deepEqual(h.results, ['câu trả lời cuối']);
    assert.equal(h.adapter.listening, false);
    assert.equal(h.clock.pending.size, 0);
});

test('speech timeout aborts the old run and prevents late callbacks from touching a new run', async () => {
    const h = speechHarness(); h.adapter.start(); const old = h.instances[0];
    const stopped = h.adapter.stop(); await h.clock.run(2500);
    assert.equal(await stopped, false); assert.equal(old.aborted, true);
    h.adapter.start(); old.onend(); old.onresult(h.result('stale'));
    assert.equal(h.adapter.listening, true); assert.deepEqual(h.results, []);
    h.instances[1].onresult(h.result('new')); h.instances[1].onend();
    assert.deepEqual(h.results, ['new']); assert.equal(h.errors.length, 1);
});

const ok = () => ({ ok: true, redirected: false });
function sse(text, splitEveryByte = false) {
    const bytes = new TextEncoder().encode(text);
    const chunks = splitEveryByte ? [...bytes].map(value => Uint8Array.of(value)) : [bytes];
    return { ok: true, redirected: false, headers: { get: () => 'text/event-stream' }, body: { getReader: () => ({
        async read() { return chunks.length ? { value: chunks.shift(), done: false } : { done: true }; },
        async cancel() {}, releaseLock() {}
    }) } };
}

function roomHarness({ coding = false, pending = true, main = 1, completed = 0, autoRead = false, startedAt = new Date().toISOString(), endedAt = null, storageFails = false, fetch: handler } = {}) {
    const clock = timers(), nodes = new Map(), requests = [], scripts = [], storage = new Map(), spoken = [];
    let now = Date.now();
    class ClockDate extends Date { static now() { return now; } }
    function node(id) {
        if (!nodes.has(id)) nodes.set(id, { id, value: '', textContent: '', hidden: false, disabled: false, checked: false, style: {}, events: {}, classList: { add() {}, remove() {}, toggle() {} }, addEventListener(name, fn) { this.events[name] = fn; } });
        return nodes.get(id);
    }
    const turn = { id: 'turn-1', isCoding: coding, codeLanguage: 'csharp', question: 'Câu hỏi?', mainQuestionNumber: main, followUpDepth: 0, answer: '' };
    node('roomConfig').textContent = JSON.stringify({ sessionId: 'session-1', pending: pending ? turn : null, ready: false, planned: 5, completedMainCount: completed, language: 'vi-VN', voice: true, startedAt, endedAt });
    node('autoRead').checked = autoRead;
    node('roomError').hidden = true;
    let speech, editor, resolveMonaco, interval;
    class FakeSpeech {
        constructor(language, callbacks) { this.callbacks = callbacks; this.supported = true; this.listening = false; speech = this; }
        start() { this.listening = true; this.callbacks.start(); return true; }
        stop() { if (!this.listening) return Promise.resolve(true); this.stopping ??= deferred(); return this.stopping.promise; }
        finish(text) { if (text) this.callbacks.result(text, ''); this.listening = false; this.callbacks.end(); this.stopping?.resolve(true); }
    }
    class FakeTts { constructor() { this.supported = true; } stop() {} speak(text) { spoken.push(text); } }
    const monaco = { editor: {
        create(element, options) {
            editor = { value: options.value, options, getValue() { return this.value; }, setValue(value) { this.value = value; this.changed?.(); }, updateOptions(value) { Object.assign(this.options, value); }, onDidChangeModelContent(fn) { this.changed = fn; }, getModel() { return {}; }, layout() {} }; return editor;
        }, setModelLanguage() {}
    } };
    const window = { InterviewSpeech: { BrowserSpeechToText: FakeSpeech, BrowserTextToSpeech: FakeTts }, confirm: () => true, addEventListener() {}, location: { assign() {} }, monaco,
        require(dependencies, done) { resolveMonaco = done; } };
    window.require.config = () => {};
    const context = { window, Date: ClockDate, document: { querySelectorAll: () => [], getElementById: node, querySelector: () => ({ value: 'antiforgery' }), createElement: () => ({}), head: { appendChild: value => scripts.push(value) } },
        sessionStorage: { getItem: key => storage.get(key) ?? null, setItem(key, value) { if (storageFails) throw new Error('Storage denied'); storage.set(key, value); }, removeItem(key) { if (storageFails) throw new Error('Storage denied'); storage.delete(key); } },
        async fetch(url, options) { const request = { url, body: options.body ? JSON.parse(options.body) : null }; requests.push(request); return handler ? await handler(request) : url.includes('/Stream/') ? sse('event: ready\ndata: {}\n\n') : ok(); },
        TextDecoder, Uint8Array, setTimeout: clock.setTimeout, clearTimeout: clock.clearTimeout, setInterval(fn) { interval = fn; } };
    vm.runInNewContext(source('request-feedback.js'), context);
    vm.runInNewContext(source('interview-room.js'), context);
    return { clock, node, requests, storage, spoken, get speech() { return speech; }, get editor() { return editor; },
        tick: () => interval(), advance: milliseconds => { now += milliseconds; interval(); },
        click: id => node(id).events.click(),
        edit(value) { node('answerInput').value = value; node('answerInput').events.input(); },
        async loadEditor() { assert.ok(scripts.length); scripts[0].onload(); resolveMonaco(); await flush(); }
    };
}

test('submit locks controls and includes the final speech result received during stop', async () => {
    const h = roomHarness(); h.click('micButton');
    const sending = h.click('answerButton'); await flush();
    assert.equal(h.node('endButton').disabled, true);
    assert.equal(h.node('micButton').disabled, true);
    assert.equal(h.requests.length, 0);
    h.speech.finish('final recognized answer'); await sending;
    const answer = h.requests.find(item => item.url.includes('/Answer/'));
    assert.equal(answer.body.answer, 'final recognized answer');
    assert.equal(answer.body.telemetry.usedRecognition, true);
    assert.equal(h.node('evaluateButton').hidden, false);
});

test('an incomplete speech stop keeps the answer editable for review instead of submitting it', async () => {
    const h = roomHarness(); h.edit('existing transcript'); h.click('micButton');
    const sending = h.click('answerButton'); await flush();
    h.speech.listening = false; h.speech.callbacks.end(); h.speech.stopping.resolve(false); await sending;
    assert.equal(h.requests.length, 0);
    assert.equal(h.node('answerInput').value, 'existing transcript');
    assert.equal(h.node('answerInput').disabled, false);
    assert.equal(h.node('answerButton').disabled, false);
    assert.equal(h.node('roomError').hidden, false);
});

test('draft writes are serialized so a slow older write cannot overwrite the latest edit', async () => {
    const first = deferred(); let draftCount = 0;
    const h = roomHarness({ fetch: request => { if (request.url.includes('/Draft/')) { draftCount++; return draftCount === 1 ? first.promise : ok(); } return ok(); } });
    h.edit('old draft');
    // Start the timer without waiting for the deliberately pending network response.
    const timer = [...h.clock.pending].find(([, value]) => value.ms === 1000); h.clock.pending.delete(timer[0]); timer[1].fn(); await flush();
    h.edit('latest draft');
    const nextTimer = [...h.clock.pending].find(([, value]) => value.ms === 1000); h.clock.pending.delete(nextTimer[0]); nextTimer[1].fn(); await flush();
    assert.equal(draftCount, 1);
    first.resolve(ok()); await flush(); await h.clock.run(250);
    assert.equal(draftCount, 2);
    assert.deepEqual(h.requests.map(item => item.body.answer), ['old draft', 'latest draft']);
    assert.equal(h.node('draftStatus').textContent, 'Đã lưu bản nháp');
});

test('late Monaco initialization keeps current textarea edits and honors a pending submit lock', async () => {
    const answerResponse = deferred();
    const h = roomHarness({ coding: true, fetch: request => request.url.includes('/Answer/') ? answerResponse.promise : sse('event: ready\ndata: {}\n\n') });
    h.edit('return 42;');
    const sending = h.click('answerButton'); await flush();
    await h.loadEditor();
    assert.equal(h.editor.value, 'return 42;'); assert.equal(h.editor.options.readOnly, true);
    assert.equal(h.requests[0].body.answer, 'return 42;');
    answerResponse.resolve(ok()); await sending;
});

test('SSE recognizes CRLF framing and UTF-8 when every byte arrives in a separate chunk', async () => {
    const question = { id: 'turn-2', question: 'Giải thích kế thừa?', mainQuestionNumber: 1, followUpDepth: 0, isCoding: false };
    const h = roomHarness({ pending: false, fetch: () => sse(`event: question\r\ndata: ${JSON.stringify(question)}\r\n\r\n`, true) });
    await h.click('nextButton');
    assert.equal(h.node('questionText').textContent, question.question);
    assert.equal(h.node('answerPanel').hidden, false); assert.equal(h.node('roomError').hidden, true);
});

test('denied browser storage cannot turn an acknowledged answer into a failed submission', async () => {
    const h = roomHarness({ storageFails: true }); h.edit('answer'); await h.click('answerButton');
    assert.equal(h.requests.filter(item => item.url.includes('/Answer/')).length, 1);
    assert.equal(h.requests.filter(item => item.url.includes('/Stream/')).length, 1);
    assert.equal(h.node('roomError').hidden, true); assert.equal(h.node('evaluateButton').hidden, false);
});

test('auto read speaks the validated question once, never provisional streamed content', async () => {
    const question = { id: 'turn-2', question: 'Câu hỏi đã kiểm tra đầy đủ?', mainQuestionNumber: 1, followUpDepth: 0, isCoding: false };
    const h = roomHarness({ pending: false, autoRead: true, fetch: () => sse(
        `event: delta\ndata: ${JSON.stringify({ text: '{"question":"Nội dung tạm thời.' })}\n\n` +
        'event: reset\ndata: {}\n\n' +
        `event: delta\ndata: ${JSON.stringify({ text: '{"question":"Bản thử lại.' })}\n\n` +
        `event: question\ndata: ${JSON.stringify(question)}\n\n`) });
    await h.click('nextButton');
    assert.deepEqual(h.spoken, [question.question]);
    assert.equal(h.node('questionText').textContent, question.question);
});

test('failed AI validation clears the provisional question and leaves a usable retry', async () => {
    const h = roomHarness({ pending: false, autoRead: true, completed: 4, fetch: () => sse(
        `event: delta\ndata: ${JSON.stringify({ text: '{"question":"Câu hỏi chưa hợp lệ.' })}\n\n` +
        'event: error\ndata: {"message":"AI chưa tạo được câu hỏi hợp lệ."}\n\n') });
    await h.click('nextButton');
    assert.deepEqual(h.spoken, []);
    assert.equal(h.node('roomError').hidden, false);
    assert.equal(h.node('nextButton').hidden, false);
    assert.equal(h.node('nextButton').disabled, false);
    assert.doesNotMatch(h.node('questionText').textContent, /Câu hỏi chưa hợp lệ/);
    assert.match(h.node('questionText').textContent, /Nội dung đã gửi được giữ nguyên/);
});

test('saved credential and quota errors expose settings recovery while keeping the interview retryable', async () => {
    for (const code of ['authentication', 'model_unavailable', 'quota_daily', 'quota_exhausted', 'not_configured']) {
        const message = 'Connection needs attention';
        const h = roomHarness({ pending: false, fetch: () => sse(`event: error\ndata: ${JSON.stringify({ code, message })}\n\n`) });
        await h.click('nextButton');
        assert.equal(h.node('roomErrorMessage').textContent, message);
        assert.equal(h.node('roomAiSettings').hidden, false, code);
        assert.match(h.node('nextActionTitle').textContent, /Kiểm tra kết nối/);
        assert.match(h.node('nextActionText').textContent, /Mở Cài đặt AI/);
        assert.doesNotMatch(h.node('nextActionText').textContent, /Bấm Thử nhận câu hỏi lại/);
        assert.equal(h.node('nextButton').hidden, false);
        assert.equal(h.node('nextButton').disabled, false);
        assert.equal(h.requests.length, 1);
    }
});

test('transient and internal AI errors retain retry without falsely requesting a key change', async () => {
    for (const code of ['timeout', 'network', 'invalid_response', 'schema_rejected', 'server_error']) {
        const h = roomHarness({ pending: false, fetch: () => sse(`event: error\ndata: ${JSON.stringify({ code, message: 'Retry later' })}\n\n`) });
        await h.click('nextButton');
        assert.equal(h.node('roomError').hidden, false);
        assert.equal(h.node('roomAiSettings').hidden, true, code);
        assert.equal(h.node('nextButton').disabled, false);
    }
});

test('retry clears old connection recovery after receiving a valid question', async () => {
    let calls = 0;
    const question = { id: 'turn-2', question: 'Recovered question', mainQuestionNumber: 1, followUpDepth: 0, isCoding: false };
    const h = roomHarness({ pending: false, fetch: () => ++calls === 1
        ? sse('event: error\ndata: {"code":"authentication","message":"Reconnect"}\n\n')
        : sse(`event: question\ndata: ${JSON.stringify(question)}\n\n`) });
    await h.click('nextButton');
    assert.equal(h.node('roomAiSettings').hidden, false);
    await h.click('nextButton');
    assert.equal(h.node('roomError').hidden, true);
    assert.equal(h.node('roomAiSettings').hidden, true);
    assert.equal(h.node('questionText').textContent, question.question);
    assert.equal(h.node('answerPanel').hidden, false);
});

test('the acknowledged fifth answer updates progress to five before showing the report button', async () => {
    const h = roomHarness({ main: 5, completed: 4 });
    h.edit('Câu trả lời cuối cùng'); await h.click('answerButton');
    assert.equal(h.node('progressCount').textContent, '5 / 5 câu chính');
    assert.equal(h.node('progressBar').style.width, '100%');
    assert.equal(h.node('evaluateButton').hidden, false);
});

test('a prepared interview does not count waiting time before the first question', () => {
    const h = roomHarness({ pending: false, startedAt: null });
    h.tick();
    assert.equal(h.node('sessionTimer').textContent, '00:00');
});

test('a finished interview keeps its saved duration while the report is prepared', () => {
    const h = roomHarness({ pending: false, startedAt: '2026-10-12T00:00:00Z', endedAt: '2026-10-12T00:01:30Z' });
    h.tick();
    assert.equal(h.node('sessionTimer').textContent, '01:30');
    h.tick();
    assert.equal(h.node('sessionTimer').textContent, '01:30');
});

const limited = (seconds = 3, feature = 'interview') => ({ ok: false, status: 429, redirected: false,
    headers: { get: name => name.toLowerCase() === 'retry-after' ? String(seconds) : 'application/json' },
    json: async () => ({ code: 'rate_limited', retryAfterSeconds: seconds, feature }) });

test('stream 429 uses countdown, blocks rapid retry, then enables manual retry without losing session', async () => {
    let attempts = 0;
    const h = roomHarness({ pending: false, fetch: () => ++attempts === 1 ? limited(3) : sse('event: ready\ndata: {}\n\n') });
    await h.click('nextButton');
    assert.match(h.node('roomErrorMessage').textContent, /chờ 3 giây/);
    assert.equal(h.node('nextButton').disabled, true);
    assert.equal(h.node('roomAiSettings').hidden, true);
    await h.click('nextButton'); assert.equal(attempts, 1);
    h.advance(2000); assert.match(h.node('roomErrorMessage').textContent, /chờ 1 giây/);
    h.advance(1000); assert.equal(h.node('nextButton').disabled, false); assert.equal(attempts, 1);
    await h.click('nextButton'); assert.equal(attempts, 2); assert.equal(h.node('evaluateButton').hidden, false);
});

test('answer 429 keeps text and local draft editable and does not double submit during cooldown', async () => {
    const h = roomHarness({ fetch: request => request.url.includes('/Answer/') ? limited(10) : ok() });
    h.edit('Câu trả lời cần giữ lại'); await h.click('answerButton');
    assert.equal(h.node('answerInput').value, 'Câu trả lời cần giữ lại');
    assert.equal(h.node('answerInput').disabled, false);
    assert.equal(h.node('answerButton').disabled, true);
    assert.equal(h.storage.size, 1);
    await h.click('answerButton'); assert.equal(h.requests.length, 1);
    h.advance(10000); assert.equal(h.node('answerButton').disabled, false);
});

test('draft 429 honors Retry-After even while the user types and sends only the latest draft afterward', async () => {
    let attempts = 0;
    const h = roomHarness({ fetch: () => ++attempts === 1 ? limited(5) : ok() });
    h.edit('first draft'); await h.clock.run(1000);
    assert.equal(attempts, 1); assert.match(h.node('draftStatus').textContent, /Chờ 5 giây/);
    h.edit('latest draft');
    assert.equal([...h.clock.pending.values()].some(timer => timer.ms === 1000), false);
    h.advance(5000); await h.clock.run(5000);
    assert.equal(attempts, 2); assert.equal(h.requests[1].body.answer, 'latest draft');
    assert.equal(h.node('draftStatus').textContent, 'Đã lưu bản nháp');
});

test('autosave backs off progressively for repeated transient failures instead of looping per keystroke', async () => {
    const h = roomHarness({ fetch: () => { throw new Error('network offline'); } });
    h.edit('draft'); await h.clock.run(1000);
    assert.equal(h.requests.length, 1);
    h.edit('draft updated'); assert.ok([...h.clock.pending.values()].some(timer => timer.ms === 2000));
    h.advance(2000); await h.clock.run(2000);
    assert.ok([...h.clock.pending.values()].some(timer => timer.ms === 4000));
    h.advance(4000); await h.clock.run(4000);
    assert.ok([...h.clock.pending.values()].some(timer => timer.ms === 8000));
    assert.equal(h.requests.length, 3); assert.equal(h.node('answerInput').value, 'draft updated');
});

test('draft validation rejection does not automatically retry the same oversized or invalid payload', async () => {
    const h = roomHarness({ fetch: () => ({ ok: false, status: 413, json: async () => ({ message: 'Quá dài' }) }) });
    h.edit('draft'); await h.clock.run(1000);
    assert.equal(h.requests.length, 1);
    assert.equal(h.clock.pending.size, 0);
    assert.equal(h.node('answerInput').value, 'draft');
});

test('HTTP 429 without JSON still shows a safe wait message rather than rendering the HTML response', async () => {
    const h = roomHarness({ pending: false, fetch: () => ({ ok: false, status: 429,
        headers: { get: () => null }, json: async () => { throw new Error('HTML response'); } }) });
    await h.click('nextButton');
    assert.match(h.node('roomErrorMessage').textContent, /chờ 60 giây/);
    assert.doesNotMatch(h.node('roomErrorMessage').textContent, /HTML|mất kết nối/);
});

test('AI question and provisional content are rendered as inert text even when they contain HTML/script', async () => {
    const payload = '<img src=x onerror="alert(1)"><script>steal()</script>';
    const question = { id: 'turn-2', question: payload, mainQuestionNumber: 1, followUpDepth: 0, isCoding: false };
    const h = roomHarness({ pending: false, fetch: () => sse(`event: delta\ndata: ${JSON.stringify({ text: JSON.stringify({ question: payload }) })}\n\nevent: question\ndata: ${JSON.stringify(question)}\n\n`) });
    Object.defineProperty(h.node('questionText'), 'innerHTML', { set() { throw new Error('Unsafe HTML sink'); } });
    await h.click('nextButton');
    assert.equal(h.node('questionText').textContent, payload); assert.equal(h.node('roomError').hidden, true);
});

test('provider transient throttle in SSE waits without immediate regeneration or asking for a new key', async () => {
    for (const code of ['quota','rate_limit','system_concurrency']) {
        const h=roomHarness({pending:false,fetch:()=>sse(`event: error\ndata: ${JSON.stringify({code,message:'Thử lại sau',retryAfterSeconds:7})}\n\n`)});
        await h.click('nextButton');
        assert.match(h.node('roomErrorMessage').textContent,/chờ 7 giây/);
        assert.equal(h.node('nextButton').disabled,true);assert.equal(h.node('roomAiSettings').hidden,true);
        await h.click('nextButton');assert.equal(h.requests.length,1);
        h.advance(7000);assert.equal(h.node('nextButton').disabled,false);
    }
});
