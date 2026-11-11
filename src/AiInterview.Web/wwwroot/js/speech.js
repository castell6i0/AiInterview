'use strict';
// Browser adapter contract; a future adapter can call a server STT service without changing the room.
class ISpeechToText {
    get supported(){return false;}
    start(){throw new Error('Nhận dạng giọng nói chưa được hỗ trợ.');}
    stop(){return Promise.resolve(true);}
}
class BrowserSpeechToText extends ISpeechToText {
    constructor(language,callbacks){super();this.language=language;this.callbacks=callbacks;this.Recognition=window.SpeechRecognition||window.webkitSpeechRecognition;this.recognition=null;this.listening=false;this.run=null;}
    get supported(){return !!this.Recognition;}
    start(){
        if(!this.supported||this.run)return false;
        const recognition=new this.Recognition();
        const run={recognition,timer:null,stopPromise:null,resolveStop:null};
        this.run=run;this.recognition=recognition;this.listening=true;
        const active=()=>this.run===run;
        const finish=(completed=true)=>{
            if(!active())return;
            clearTimeout(run.timer);this.run=null;this.recognition=null;this.listening=false;
            try{this.callbacks.end?.();}finally{run.resolveStop?.(completed);}
        };
        run.finish=finish;
        recognition.lang=this.language;recognition.continuous=true;recognition.interimResults=true;
        recognition.onstart=()=>{if(active())this.callbacks.start?.();};
        recognition.onspeechstart=()=>{if(active())this.callbacks.speechStart?.();};
        recognition.onspeechend=()=>{if(active())this.callbacks.speechEnd?.();};
        recognition.onresult=e=>{if(!active())return;let final='',interim='';for(let i=0;i<e.results.length;i++){const result=e.results[i];if(result.isFinal)final+=result[0].transcript+' ';else interim+=result[0].transcript;}this.callbacks.result?.(final.trim(),interim.trim());};
        recognition.onerror=e=>{if(!active())return;const messages={'not-allowed':'Microphone chưa được cấp quyền. Bạn vẫn có thể nhập văn bản.','audio-capture':'Không tìm thấy microphone. Hãy nhập văn bản hoặc kiểm tra thiết bị.','network':'Dịch vụ nhận dạng giọng nói chưa kết nối được. Bạn có thể nhập văn bản.','no-speech':'Chưa nghe thấy giọng nói. Bạn có thể bấm nói lại.','language-not-supported':'Trình duyệt chưa hỗ trợ ngôn ngữ này. Hãy nhập văn bản.'};if(e.error!=='aborted')this.callbacks.error?.(messages[e.error]||'Nhận dạng giọng nói đã dừng. Bạn có thể thử lại hoặc nhập văn bản.');finish(false);};
        recognition.onend=()=>finish();
        try{recognition.start();return true;}catch{this.callbacks.error?.('Chưa bật được microphone. Bạn có thể thử lại hoặc nhập văn bản.');finish();return false;}
    }
    stop(){
        const run=this.run;if(!run)return Promise.resolve(true);if(run.stopPromise)return run.stopPromise;
        run.stopPromise=new Promise(resolve=>{run.resolveStop=resolve;});
        // stop() can produce one final result before onend. Keep this run active until then.
        run.timer=setTimeout(()=>{if(this.run!==run)return;run.finish(false);try{run.recognition.abort();}catch{}this.callbacks.error?.('Nhận dạng đã dừng quá thời gian chờ. Hãy kiểm tra transcript trước khi gửi.');},2500);
        try{run.recognition.stop();}catch{run.finish(false);try{run.recognition.abort();}catch{}}
        return run.stopPromise;
    }
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
    stop(){this.generation++;this.utterance=null;if(this.supported)window.speechSynthesis.cancel();for(const finish of [...this.waiters])finish();}
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
            .replace(/\s+/g,' ').trim();
    }
    chunks(text){
        const parts=[];let current='';
        // Bound utterances at whitespace, preserving every word and punctuation mark.
        for(const sentence of text.split(/(?<=[.!?。;:])\s+/u)){
            for(const word of sentence.split(/\s+/)){if(current&&current.length+word.length+1>240){parts.push(current);current='';}current+=(current?' ':'')+word;}
            if(current){parts.push(current);current='';}
        }
        return parts.filter(Boolean);
    }
    async speak(text){
        this.stop();const generation=this.generation;
        const normalized=this.normalize(text);if(!this.supported||!normalized)return false;
        await this.waitForVoices();if(generation!==this.generation)return false;this.refreshVoices();
        const voice=this.selectedVoice;
        if(!voice){this.controls?.notice?.(`Máy chưa có giọng ${this.baseLanguage==='vi'?'tiếng Việt':'tiếng Anh'} phù hợp. Bạn có thể tiếp tục đọc câu hỏi trên màn hình hoặc thử Microsoft Edge.`);return false;}
        const chunks=this.chunks(normalized),rate=this.rate;
        const next=()=>{
            if(generation!==this.generation||!chunks.length){this.utterance=null;return;}
            const utterance=new window.SpeechSynthesisUtterance(chunks.shift());this.utterance=utterance;utterance.lang=voice.lang;utterance.voice=voice;utterance.rate=rate;
            utterance.onend=()=>{if(generation===this.generation)next();};
            utterance.onerror=event=>{if(generation!==this.generation)return;this.stop();if(!['interrupted','canceled'].includes(event.error))this.controls?.notice?.('Chưa phát được giọng đọc này. Hãy chọn giọng khác hoặc bấm Đọc câu hỏi để thử lại.');};
            try{window.speechSynthesis.speak(utterance);}catch{this.stop();this.controls?.notice?.('Chưa phát được giọng đọc. Hãy chọn giọng khác hoặc bấm Đọc câu hỏi để thử lại.');}
        };
        next();return true;
    }
}
window.InterviewSpeech={ISpeechToText,BrowserSpeechToText,BrowserTextToSpeech};
