'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, '../../src/AiInterview.Web/wwwroot/js/request-feedback.js'), 'utf8');
const flush = async () => { for (let i=0;i<12;i++) await Promise.resolve(); };
const deferred = () => { let resolve; const promise = new Promise(r => { resolve = r; }); return {resolve,promise}; };
function harness({response, twoForms = false} = {}) {
    let now = Date.parse('2026-11-10T00:00:00Z'), timerId = 0;
    const timers = new Map(), calls = [], navigations = [], windowEvents = {};
    class ClockDate extends Date { static now() { return now; } }
    const makeButton = disabled => ({ disabled, name: 'CheckConnection', value: 'true' });
    function makeForm(action) {
        const button = makeButton(false), unavailable = makeButton(true), attributes = new Map();
        return { action, dataset: { rateGroup: 'ai-settings' }, children: [], buttons: [button, unavailable],
            fields: new Map([['ApiKey','synthetic-entered-key'],['Model','custom-model'],['__RequestVerificationToken','synthetic-csrf']]),
            file: { name: 'synthetic-resume.pdf', type: 'application/pdf' },
            querySelectorAll() { return this.buttons; },
            append(node) { this.children.push(node); },
            addEventListener(name, callback) { this[name] = callback; },
            setAttribute: (name, value) => attributes.set(name, value), removeAttribute: name => attributes.delete(name), getAttribute: name => attributes.get(name),
            async send(button = this.buttons[0]) { const event = { submitter: button, preventDefault() { this.defaultPrevented = true; } }; await this.submit(event); return event; }
        };
    }
    const forms = [makeForm('/AiSettings/Save')];
    if(twoForms) forms.push(makeForm('/AiSettings/TestConnection'));
    class Data { constructor(form) { this.fields = new Map(form.fields); this.file = form.file; } append(name,value) { this.fields.set(name,value); } }
    const context = { Date: ClockDate, FormData: Data, AbortController,
        document: { querySelectorAll: () => forms, createElement: () => ({ setAttribute(name,value) { this[name] = value; } }) },
        window: { location: { assign: value => navigations.push(value) }, addEventListener(name,fn) { windowEvents[name] = fn; } },
        setTimeout(fn,ms) { const id=++timerId;timers.set(id,{fn,at:now+ms,ms});return id; }, clearTimeout(id) { timers.delete(id); },
        async fetch(url, options) { calls.push({url,options});return typeof response === 'function' ? await response(calls.length) : response; }
    };
    vm.runInNewContext(source, context);
    return { forms, calls, navigations, timers, api:context.window.InterviewRequests,
        async advance(ms) { now+=ms;const pending=[...timers];for(const [id,t] of pending) if(t.at<=now&&timers.delete(id)) t.fn();await flush(); },
        restore() { windowEvents.pageshow({persisted:true}); }
    };
}
const limited = (seconds=4) => ({ ok:false,status:429,headers:{ get:name=>name==='Retry-After'?String(seconds):null },json:async()=>({code:'rate_limited',retryAfterSeconds:seconds}) });
const redirect = url => ({ ok:true,status:200,json:async()=>({redirectUrl:url}) });

test('upload rejection keeps selected file and form values, waits then enables an explicit retry', async()=>{
    const h=harness({response:count=>count===1?limited():redirect('/Profile/Cv/7')});
    const form=h.forms[0], file=form.file;
    form.action='/Profile/Upload';
    await form.send();
    assert.equal(h.calls.length,1); assert.equal(form.file,file);
    assert.equal(form.fields.get('ApiKey'),'synthetic-entered-key');
    assert.equal(form.children[0].role,'status'); assert.match(form.children[0].textContent,/chờ 4 giây/);
    assert.equal(form.buttons[0].disabled,true);
    await form.send();assert.equal(h.calls.length,1);
    await h.advance(4000);assert.equal(h.calls.length,1);assert.equal(form.buttons[0].disabled,false);
    assert.equal(form.buttons[1].disabled,true,'originally unavailable controls stay disabled');
    await form.send();assert.deepEqual(h.navigations,['/Profile/Cv/7']);assert.equal(h.calls[1].options.body.file,file);
});

test('AI settings fetch preserves CSRF and clicked save-and-check choice; rate cooldown is shared by related forms', async()=>{
    const h=harness({response:limited(5),twoForms:true});
    await h.forms[0].send();
    const request=h.calls[0];
    assert.equal(request.options.headers.Accept,'application/json');
    assert.equal(request.options.headers['X-Requested-With'],'XMLHttpRequest');
    assert.equal(request.options.body.fields.get('__RequestVerificationToken'),'synthetic-csrf');
    assert.equal(request.options.body.fields.get('CheckConnection'),'true');
    assert.equal(request.options.credentials,'same-origin');
    assert.equal(h.forms[1].buttons[0].disabled,true);
    await h.forms[1].send();assert.equal(h.calls.length,1);
    await h.advance(5000);assert.equal(h.forms[1].buttons[0].disabled,false);
    assert.equal(h.forms[0].fields.get('Model'),'custom-model');
});

test('double-click while posting makes one request; network failure unlocks controls and keeps fields', async()=>{
    const pending=deferred();const h=harness({response:()=>pending.promise});
    const sending=h.forms[0].send();await flush();await h.forms[0].send();
    assert.equal(h.calls.length,1);pending.resolve({ok:false,status:503,json:async()=>({message:'Tạm thời chưa xử lý được.'})});await sending;
    assert.equal(h.forms[0].buttons[0].disabled,false);assert.equal(h.forms[0].getAttribute('aria-busy'),undefined);
    assert.equal(h.forms[0].fields.get('ApiKey'),'synthetic-entered-key');
    assert.match(h.forms[0].children[0].textContent,/Tạm thời/);
});

test('retry parser honors seconds or HTTP date, falls back to JSON, and rejects malformed values safely',()=>{
    const h=harness();const response=value=>({headers:{get:()=>value}});
    assert.equal(h.api.retrySeconds(response('12'),{retryAfterSeconds:1}),12);
    const now=Date.parse('2026-11-10T00:00:00Z');
    assert.equal(h.api.retrySeconds(response('Tue, 10 Nov 2026 00:00:09 GMT'),{},now),9);
    assert.equal(h.api.retrySeconds(response('bad'),{retryAfterSeconds:8}),8);
    assert.equal(h.api.retrySeconds(response(null),{retryAfterSeconds:-1}),60);
});

test('missing JSON or HTML error is never inserted into the page',async()=>{
    const h=harness({response:{ok:false,status:429,headers:{get:()=>null},json:async()=>{throw new Error('<script>bad()</script>');}}});
    Object.defineProperty(h.forms[0].children[0],'innerHTML',{set(){throw new Error('Unsafe HTML sink');}});
    await h.forms[0].send();assert.match(h.forms[0].children[0].textContent,/chờ 60 giây/);assert.deepEqual(h.navigations,[]);
});

test('success navigates only to safe same-origin relative destinations',async()=>{
    for(const target of ['https://evil.example','//evil.example','/\\evil.example','javascript:bad()']) {
        const h=harness({response:redirect(target)});await h.forms[0].send();assert.deepEqual(h.navigations,[]);assert.equal(h.forms[0].buttons[0].disabled,false);
    }
    const h=harness({response:redirect('/AiSettings?provider=Gemini')});await h.forms[0].send();assert.deepEqual(h.navigations,['/AiSettings?provider=Gemini']);
});
