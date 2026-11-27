'use strict';
// Visual cue only: the interview room's wave shows "speaking" while an utterance plays (no-op elsewhere).
const roomStage=(state,onlyFrom)=>{const stage=globalThis.document?.getElementById?.('roomStage');if(stage?.setAttribute&&(!onlyFrom||stage.getAttribute('data-state')===onlyFrom))stage.setAttribute('data-state',state);};
// Browser adapter contract; a future adapter can call a server STT service without changing the room.
class ISpeechToText {
    get supported(){return false;}
    start(){throw new Error('Nhận dạng giọng nói chưa được hỗ trợ.');}
    stop(){return Promise.resolve(true);}
}
const VAD_OPTIONS={vad_threshold:'0.35',vad_window_duration:'1.0',vad_look_behind_sample_count:'16000',vad_max_segment_duration:'20'};
class MoonshineSpeechToText extends ISpeechToText {
    static loadModule=()=>Promise.all([import('/lib/moonshine/mic-transcriber.js'),import('/lib/moonshine/enums.js'),import('/lib/moonshine/transcriber.js')]);
    constructor(language,callbacks){super();this.language=String(language).toLowerCase().startsWith('vi')?'vi':'en';this.callbacks=callbacks;this.mic=null;this.loading=null;this.listening=false;this.run=null;this.lines=[];this.speaking=false;}
    get supported(){return !!(globalThis.crossOriginIsolated&&window.navigator?.mediaDevices?.getUserMedia&&window.AudioContext);}
    load(){
        return this.loading??=MoonshineSpeechToText.loadModule().then(async([{MicTranscriber,wrapProgress},{ModelArch},{Transcriber}])=>{
            const transcriber=await Transcriber.load({language:this.language,modelArch:this.language==='vi'?ModelArch.Base:ModelArch.TinyStreaming,options:VAD_OPTIONS,onProgress:wrapProgress(fraction=>this.callbacks.loading?.(fraction))});
            return this.mic=new MicTranscriber().useTranscriber(transcriber).onText(text=>this.text(text)).onLine(line=>this.line(line.text));
        }).catch(error=>{this.loading=null;throw error;});
    }
    start(){
        if(!this.supported||this.run)return false;
        const run=this.run={capturing:false};this.listening=true;this.lines=[];this.speaking=false;
        (async()=>{
            try{const mic=await this.load();if(this.run!==run)return;run.capturing=true;await mic.start();if(this.run!==run){await mic.stop();return;}this.callbacks.start?.();}
            catch(error){if(this.run!==run)return;const name=error?.name;this.callbacks.error?.(name==='NotAllowedError'?'Microphone chưa được cấp quyền. Bạn vẫn có thể nhập văn bản.':name==='NotFoundError'?'Không tìm thấy microphone. Hãy nhập văn bản hoặc kiểm tra thiết bị.':'Chưa tải được bộ nhận dạng giọng nói. Kiểm tra mạng rồi bấm nói lại, hoặc nhập văn bản.');this.finish(run);}
        })();
        return true;
    }
    async stop(){const run=this.run;if(!run)return true;this.listening=false;try{if(run.capturing)await this.mic.stop();}catch{}this.finish(run);return true;}
    finish(run){if(this.run!==run)return;this.run=null;this.listening=false;if(this.speaking){this.speaking=false;this.callbacks.speechEnd?.();}this.callbacks.end?.();}
    text(text){if(!this.run?.capturing)return;if(text&&!this.speaking){this.speaking=true;this.callbacks.speechStart?.();}this.callbacks.result?.(this.lines.join(' '),text);}
    line(text){if(!this.run?.capturing)return;if(text?.trim())this.lines.push(text.trim());if(this.speaking){this.speaking=false;this.callbacks.speechEnd?.();}this.callbacks.result?.(this.lines.join(' '),'');}
}
class BrowserTextToSpeech {
    constructor(language){
        this.language=language;this.baseLanguage=language.toLowerCase().split(/[-_]/)[0];this.voices=[];this.waiters=new Set();this.generation=0;this.utterance=null;this.controls=null;
        this.preferenceKey=`interview-speech:${this.baseLanguage}`;this.voiceURI='';this.rate=0.95;
        try{const saved=JSON.parse(window.localStorage.getItem(this.preferenceKey)||'null');if(saved){this.voiceURI=typeof saved.voiceURI==='string'?saved.voiceURI:'';this.rate=this.validRate(saved.rate);}}catch{}
        this.voicesChanged=()=>this.refreshVoices();
        if(this.supported){window.speechSynthesis.addEventListener?.('voiceschanged',this.voicesChanged);this.refreshVoices();}
    }
    get supported(){return !!window.speechSynthesis&&typeof window.SpeechSynthesisUtterance==='function';}
    validRate(value){const number=Number(value);return Number.isFinite(number)&&number>=0.75&&number<=1.25?Math.round(number*20)/20:0.95;}
    get selectedVoice(){return this.voices.find(voice=>voice.voiceURI===this.voiceURI)||this.voices[0]||null;}
    persist(){try{window.localStorage.setItem(this.preferenceKey,JSON.stringify({voiceURI:this.voiceURI,rate:this.rate}));}catch{}}
    setVoice(uri){this.voiceURI=this.voices.some(voice=>voice.voiceURI===uri)?uri:'';this.stop();this.persist();this.renderControls();}
    setRate(rate){this.rate=this.validRate(rate);this.stop();this.persist();this.renderControls();}
    refreshVoices(){
        if(!this.supported)return;
        const locale=this.language.toLowerCase().replaceAll('_','-');
        const rank=voice=>(voice.lang.toLowerCase().replaceAll('_','-')===locale?100:0)+(/natural|neural|online/i.test(voice.name)?30:/google/i.test(voice.name)?20:0)+(voice.default?1:0);
        // Never let an unavailable Vietnamese voice silently become the browser's English default.
        this.voices=window.speechSynthesis.getVoices().filter(voice=>voice.lang.toLowerCase().split(/[-_]/)[0]===this.baseLanguage).sort((a,b)=>rank(b)-rank(a)||a.name.localeCompare(b.name));
        this.renderControls();if(this.voices.length)for(const finish of [...this.waiters])finish();
    }
    bindControls({voiceSelect,rateSelect,statusElement,notice}){
        this.controls={voiceSelect,rateSelect,statusElement,notice};
        voiceSelect?.addEventListener('change',()=>this.setVoice(voiceSelect.value));
        rateSelect?.addEventListener('change',()=>this.setRate(rateSelect.value));
        this.renderControls();
    }
    renderControls(){
        if(!this.controls)return;
        const {voiceSelect,rateSelect,statusElement}=this.controls;
        if(voiceSelect){
            const doc=voiceSelect.ownerDocument||document;voiceSelect.replaceChildren();
            const add=(value,label)=>{const option=doc.createElement('option');option.value=value;option.textContent=label;voiceSelect.appendChild(option);};
            add('',this.voices.length?`Tự chọn · ${this.voices[0].name}`:'Chưa có giọng phù hợp');
            for(const voice of this.voices)add(voice.voiceURI,`${voice.name} (${voice.lang})`);
            voiceSelect.value=this.voices.some(voice=>voice.voiceURI===this.voiceURI)?this.voiceURI:'';voiceSelect.disabled=!this.voices.length;
        }
        if(rateSelect){rateSelect.value=String(this.rate);rateSelect.disabled=!this.supported;}
        if(statusElement)statusElement.textContent=!this.supported?'Trình duyệt chưa hỗ trợ đọc câu hỏi.':this.voices.length?'Chọn giọng nghe rõ nhất trên máy của bạn. Chỉ đọc câu hỏi đã nhận đầy đủ.':`Chưa có giọng ${this.baseLanguage==='vi'?'tiếng Việt':'tiếng Anh'}. Hãy thử Microsoft Edge hoặc cài giọng đọc đúng ngôn ngữ trong Windows.`;
    }
    waitForVoices(){
        this.refreshVoices();if(this.voices.length)return Promise.resolve();
        return new Promise(resolve=>{let timer;const finish=()=>{clearTimeout(timer);this.waiters.delete(finish);resolve();};this.waiters.add(finish);timer=setTimeout(finish,1800);});
    }
    stop(){this.generation++;this.utterance=null;this.audio?.pause();this.audio=null;roomStage('idle','speaking');if(this.supported)window.speechSynthesis.cancel();for(const finish of [...this.waiters])finish();}
    normalize(text){
        const vi=this.baseLanguage==='vi';
        return String(text||'').normalize('NFC')
            .replace(/```[^\n]*\n[\s\S]*?(?:```|$)/g,vi?' Đoạn mã được hiển thị trên màn hình. ':' The code is shown on screen. ')
            .replace(/!\[([^\]]*)\]\([^)]*\)/g,'$1').replace(/\[([^\]]+)\]\([^)]*\)/g,'$1')
            .replace(/https?:\/\/\S+/g,vi?' đường dẫn trên màn hình ':' the link on screen ')
            .replace(/^\s{0,3}(?:#{1,6}\s+|[-*+]\s+|>\s*)/gm,'')
            .replace(/`([^`]+)`/g,'$1').replace(/(\*\*|__|~~)(.*?)\1/g,'$2').replace(/(^|\s)[*_]([^*_\n]+)[*_](?=$|[\s.,!?;:])/g,'$1$2')
            .replace(/\bC\+\+(?!\w)/gi,vi?'C cộng cộng':'C plus plus').replace(/\bC#(?!\w)/gi,vi?'C thăng':'C sharp')
            .replace(/\bASP\.NET\b/gi,vi?'A S P chấm nét':'A S P dot net').replace(/\.NET\b/gi,vi?'chấm nét':'dot net')
            .replace(/\bOOP\b/g,vi?'lập trình hướng đối tượng':'object oriented programming')
            .replace(/\b(API|HTTP|HTTPS|HTML|CSS|SQL|QA|UI|UX|JSON)\b/g,word=>word.split('').join(' '))
            .replace(/(\p{L})\s?\/\s?(?=\p{L})/gu,vi?'$1 hoặc ':'$1 or ')
            .replace(/[ \t]+[–—-][ \t]+/g,', ')
            .split(/\r?\n/).map(line=>line.trim()).filter(Boolean).map(line=>/[.!?;:,…。][)"'”’\]]*$/u.test(line)?line:`${line}.`).join(' ')
            .replace(/\s+/g,' ').trim();
    }
    chunks(text){
        const parts=[],limit=240;
        const pack=(pieces,split)=>{let current='';for(const piece of pieces){if(piece.length>limit&&split){if(current)parts.push(current);current='';split(piece);continue;}if(current&&current.length+piece.length+1>limit){parts.push(current);current='';}current+=(current?' ':'')+piece;}if(current)parts.push(current);};
        const words=clause=>pack(clause.split(/\s+/),null);
        // Bound utterances at whitespace, preserving every word and punctuation mark.
        for(const sentence of text.split(/(?<=[.!?。;:])\s+/u))pack(sentence.split(/(?<=[,，])\s+/u),words);
        return parts.filter(Boolean);
    }
    prefetch(url){window.fetch?.(url,{credentials:'same-origin'}).catch(()=>{});}
    async playAudio(url,generation){
        if(typeof window.fetch!=='function'||typeof window.Audio!=='function')return false;
        for(let attempt=1;;attempt++){
            const response=await window.fetch(url,{credentials:'same-origin'}).catch(()=>null);
            if(generation!==this.generation)return true;
            if(response?.status===404)return false;
            if(response?.ok){
                const src=URL.createObjectURL(await response.blob());
                if(generation!==this.generation){URL.revokeObjectURL(src);return true;}
                const audio=new window.Audio(src);this.audio=audio;audio.playbackRate=this.rate;
                audio.onended=()=>{URL.revokeObjectURL(src);if(this.audio===audio){this.audio=null;roomStage('idle','speaking');}};
                try{await audio.play();if(generation===this.generation)roomStage('speaking');}catch{if(this.audio===audio)this.audio=null;this.controls?.notice?.('Trình duyệt chặn phát âm thanh. Bấm Đọc câu hỏi để nghe.');}
                return true;
            }
            const failure=await response?.json?.().catch(()=>null);
            if(generation!==this.generation)return true;
            if(failure?.code==='quota_daily'){this.controls?.notice?.(failure.message);return true;}
            if(attempt>=4){this.controls?.notice?.('Giọng đọc AI đang quá tải. Bấm Đọc câu hỏi để thử lại sau ít phút.');return true;}
            const wait=Math.min(60,Number(response?.headers?.get('Retry-After'))||15);
            this.controls?.notice?.(`Giọng đọc AI đang bận, tự thử lại sau ${wait} giây…`);
            await new Promise(resolve=>setTimeout(resolve,wait*1000));
            if(generation!==this.generation)return true;
        }
    }
    async speak(text,audioUrl){
        this.stop();const generation=this.generation;
        if(audioUrl&&await this.playAudio(audioUrl,generation))return true;
        if(generation!==this.generation)return false;
        if(audioUrl)this.controls?.notice?.('Buổi này chưa có giọng Gemini, đang dùng giọng đọc của trình duyệt.');
        const normalized=this.normalize(text);if(!this.supported||!normalized)return false;
        await this.waitForVoices();if(generation!==this.generation)return false;this.refreshVoices();
        const voice=this.selectedVoice;
        if(!voice){this.controls?.notice?.(`Máy chưa có giọng ${this.baseLanguage==='vi'?'tiếng Việt':'tiếng Anh'} phù hợp. Bạn có thể tiếp tục đọc câu hỏi trên màn hình hoặc thử Microsoft Edge.`);return false;}
        const chunks=this.chunks(normalized),rate=this.rate;
        const next=()=>{
            if(generation!==this.generation||!chunks.length){this.utterance=null;if(generation===this.generation)roomStage('idle','speaking');return;}
            roomStage('speaking');
            const utterance=new window.SpeechSynthesisUtterance(chunks.shift());this.utterance=utterance;utterance.lang=voice.lang;utterance.voice=voice;utterance.rate=rate;
            utterance.onend=()=>{if(generation===this.generation)next();};
            utterance.onerror=event=>{if(generation!==this.generation)return;this.stop();if(!['interrupted','canceled'].includes(event.error))this.controls?.notice?.('Chưa phát được giọng đọc này. Hãy chọn giọng khác hoặc bấm Đọc câu hỏi để thử lại.');};
            try{window.speechSynthesis.speak(utterance);}catch{this.stop();this.controls?.notice?.('Chưa phát được giọng đọc. Hãy chọn giọng khác hoặc bấm Đọc câu hỏi để thử lại.');}
        };
        next();return true;
    }
}
window.InterviewSpeech={ISpeechToText,MoonshineSpeechToText,BrowserTextToSpeech};
