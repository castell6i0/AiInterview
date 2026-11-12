'use strict';
(()=>{
const config=JSON.parse(document.getElementById('roomConfig').textContent),$=id=>document.getElementById(id),token=document.querySelector('input[name="__RequestVerificationToken"]').value;
const endpoint=name=>`/Interviews/${name}/${encodeURIComponent(config.sessionId)}`;
const tts=new window.InterviewSpeech.BrowserTextToSpeech(config.language);
let current=config.pending,busy=false,ready=config.ready,editor=null,editorLoading=null,editorTurnId=null,dirty=false,draftTimer=null,draftFlight=null,streamed='',completedMain=Number(config.completedMainCount)||0,baseSpeech='',speechTurnId=null,suppressEditor=false,submitting=false;
let sessionStarted=Date.parse(config.startedAt)||null,sessionEnded=Date.parse(config.endedAt)||null;
let displayedAt=Date.now(),firstInput=null,usedRecognition=false,windows=[],speechStart=null;
const requestFeedback=window.InterviewRequests, cooldowns={};
let activeCooldown=null,draftRetryAt=0,draftFailures=0;
const remaining=name=>Math.max(0,Math.ceil((Math.max(cooldowns[name]||0,cooldowns.all||0)-Date.now())/1000));
function rememberCooldown(name,error){if(error.code!=='rate_limited')return;cooldowns[error.feature==='global'?'all':name]=Date.now()+(error.retryAfterSeconds||60)*1000;if(name!=='Draft')activeCooldown=name;refreshControls();}
function refreshControls(){['answerButton','skipButton'].forEach(id=>$(id).disabled=busy||remaining('Answer')>0);['nextButton','evaluateButton'].forEach(id=>$(id).disabled=busy||remaining('Stream')>0);$('endButton').disabled=busy||ready||remaining('End')>0;}
function updateCooldown(){refreshControls();if(activeCooldown){const seconds=remaining(activeCooldown);showError(seconds?requestFeedback.waitMessage(seconds):'Bạn có thể bấm thử lại. Nội dung đã nhập và đã lưu vẫn được giữ.');if(!seconds)activeCooldown=null;}if(current&&dirty&&remaining('Draft'))$('draftStatus').textContent=`Chờ ${remaining('Draft')} giây để lưu lại · bản nháp vẫn ở trong tab`;}
function scheduleDraft(delay=1000){clearTimeout(draftTimer);draftTimer=setTimeout(saveDraft,Math.max(delay,draftRetryAt-Date.now(),remaining('Draft')*1000));}
const key=()=>`interview-draft:${config.sessionId}:${current?.id}`;
const readAnswer=()=>editor&&current?.isCoding&&editorTurnId===current.id?editor.getValue():$('answerInput').value;
function setAnswer(value){$('answerInput').value=value;if(editor&&editorTurnId===current?.id&&editor.getValue()!==value){suppressEditor=true;try{editor.setValue(value);}finally{suppressEditor=false;}}updateCount();}
function guide(title,text){$('nextActionTitle').textContent=title;$('nextActionText').textContent=text;}
function status(text){$('roomStatus').textContent=text;}
function showError(message,code){$('roomErrorMessage').textContent=message;$('roomError').hidden=false;$('roomAiSettings').hidden=!['not_configured','authentication','model_unavailable','quota','quota_daily','quota_exhausted','system_quota_daily','system_quota_monthly','system_disabled','email_unconfirmed','request_too_large','invalid_request','configuration'].includes(code);}
function clearError(){activeCooldown=null;$('roomError').hidden=true;$('roomAiSettings').hidden=true;}
function notice(message){$('voiceNotice').textContent=message;$('voiceNotice').hidden=false;}
tts.bindControls?.({voiceSelect:$('ttsVoice'),rateSelect:$('ttsRate'),statusElement:$('ttsStatus'),notice});
function updateCount(){const text=readAnswer().trim();$('wordCount').textContent=`${text?text.split(/\s+/).length:0} từ` ;}
function lockAnswer(){const locked=busy||speech.listening;$('answerInput').disabled=locked;if(editor)editor.updateOptions({readOnly:locked});}
function setBusy(value){busy=value;$('statusDot').classList.toggle('busy',value);['answerButton','skipButton','nextButton','evaluateButton','micButton','readButton'].forEach(id=>$(id).disabled=value);$('endButton').disabled=value||ready;$('readButton').disabled=value||!current||!tts.supported;refreshControls();lockAnswer();if(!value&&dirty&&current&&!submitting)scheduleDraft();}
function stopListening(){const stopped=speech.stop();if(speechStart!==null){windows.push({startMs:speechStart,endMs:Math.max(speechStart,Date.now()-displayedAt)});speechStart=null;}return stopped;}
function snapshot(){return{answer:readAnswer(),displayedAt,firstInput,usedRecognition,windows};}
function stash(){try{sessionStorage.setItem(key(),JSON.stringify(snapshot()));}catch{}}
function removeDraft(){try{sessionStorage.removeItem(key());}catch{}}
function edited(){if(!current||busy)return;firstInput??=Date.now()-displayedAt;dirty=true;stash();updateCount();$('draftStatus').textContent='Đang lưu…';scheduleDraft();}
async function post(name,body){
    if(remaining(name)>0){const error=new Error(requestFeedback.waitMessage(remaining(name)));error.code='rate_limited';error.retryAfterSeconds=remaining(name);throw error;}
    const response=await fetch(endpoint(name),{method:'POST',headers:{'Content-Type':'application/json','Accept':'application/json','RequestVerificationToken':token},credentials:'same-origin',body:JSON.stringify(body??{})});
    if(!response.ok){const error=await requestFeedback.responseError(response,'Chưa lưu được dữ liệu. Hãy thử lại.');rememberCooldown(name,error);throw error;}
    if(response.redirected)throw new Error('Phiên đăng nhập đã hết. Hãy tải lại trang để đăng nhập; bản nháp được giữ trong tab này.');
    return response;
}
function saveDraft(){
    if(draftFlight)return draftFlight;
    if(!current||!dirty||busy)return Promise.resolve();
    if(draftRetryAt>Date.now()||remaining('Draft')>0){scheduleDraft();return Promise.resolve();}
    const turnId=current.id,value=readAnswer();
    // Serialize writes and keep backoff independent of input events: typing must not bypass a cooldown.
    draftFlight=(async()=>{let saved=false,retryable=true;try{
        await post('Draft',{turnId,answer:value});saved=true;draftFailures=0;draftRetryAt=0;
        if(current?.id===turnId&&readAnswer()===value){dirty=false;$('draftStatus').textContent='Đã lưu bản nháp';}
    }catch(error){
        draftFailures=Math.min(draftFailures+1,6);
        retryable=!error.status||error.status===429||error.status>=500;
        draftRetryAt=Date.now()+Math.max(Math.min(60000,2000*2**(draftFailures-1)),remaining('Draft')*1000);
        if(current?.id===turnId)$('draftStatus').textContent=error.code==='rate_limited'
            ? `Chờ ${remaining('Draft')} giây để lưu lại · bản nháp vẫn ở trong tab`
            : retryable ? 'Chưa lưu trực tuyến · bản nháp vẫn ở trong tab, hệ thống sẽ thử lại chậm hơn' : 'Chưa lưu trực tuyến · bản nháp vẫn ở trong tab; hãy kiểm tra nội dung hoặc phiên đăng nhập';
    }finally{draftFlight=null;if(dirty&&current&&!busy&&(saved||retryable))scheduleDraft(saved?250:1000);}})();
    return draftFlight;
}

function telemetry(){const elapsed=Date.now()-displayedAt;if(elapsed<0||elapsed>3600000)return null;return{usedRecognition,firstSpeechMs:firstInput,elapsedMs:elapsed,windows:usedRecognition?windows:[]};}
const speech=new window.InterviewSpeech.BrowserSpeechToText(config.language,{
start:()=>{$('roomStage').setAttribute?.('data-state','listening');$('micButton').classList.add('recording');$('micButton').textContent='Dừng nói';lockAnswer();if(!busy)status('AI đang nghe…');},
speechStart:()=>{firstInput??=Date.now()-displayedAt;if(speechStart===null)speechStart=Date.now()-displayedAt;},
speechEnd:()=>{if(speechStart!==null){windows.push({startMs:speechStart,endMs:Date.now()-displayedAt});speechStart=null;}},
result:(final,interim)=>{if(!current||current.id!==speechTurnId||(busy&&!submitting))return;if(final){usedRecognition=true;firstInput??=Date.now()-displayedAt;setAnswer([baseSpeech,final].filter(Boolean).join(' '));if(submitting){dirty=true;stash();}else edited();}$('interimTranscript').textContent=interim;},
end:()=>{if(speechStart!==null){windows.push({startMs:speechStart,endMs:Date.now()-displayedAt});speechStart=null;}$('roomStage').setAttribute?.('data-state','idle');$('micButton').classList.remove('recording');$('micButton').textContent='Bắt đầu nói';$('interimTranscript').textContent='';lockAnswer();if(!busy)status('Đang chờ câu trả lời của bạn');},error:message=>notice(message)});
if(config.voice&&!speech.supported){$('micButton').hidden=true;notice('Trình duyệt này chưa hỗ trợ nhận dạng giọng nói. Buổi luyện tự động dùng ô nhập văn bản.');}
if(!tts.supported){$('readButton').hidden=true;$('autoRead').checked=false;$('autoRead').disabled=true;}
$('micButton').addEventListener('click',()=>{if(busy||submitting||!current)return;if(speech.listening)stopListening();else{tts.stop();baseSpeech=readAnswer().trim();speechTurnId=current.id;speech.start();lockAnswer();}});
$('readButton').addEventListener('click',async()=>{if(busy||submitting)return;$('readButton').disabled=true;try{await stopListening();tts.stop();tts.speak($('questionText').textContent);}finally{$('readButton').disabled=busy;}});
$('autoRead').addEventListener('change',()=>{if(!$('autoRead').checked)tts.stop();});
$('answerInput').addEventListener('input',edited);
// Dark editor theme read from the CSS tokens; falls back to vs-dark where styles are unavailable.
function editorTheme(monaco){const css=window.getComputedStyle?.(document.documentElement),token=name=>css.getPropertyValue(name).trim();if(!css||!monaco.editor.defineTheme)return 'vs-dark';monaco.editor.defineTheme('interviewlab',{base:'vs-dark',inherit:true,rules:[],colors:{'editor.background':token('--bg'),'editor.foreground':token('--text'),'editor.lineHighlightBackground':token('--surface-2'),'editorLineNumber.foreground':token('--muted'),'editorCursor.foreground':token('--accent'),'editorWidget.background':token('--surface-1')}});return 'interviewlab';}
function loadMonaco(){if(editorLoading)return editorLoading;editorLoading=new Promise((resolve,reject)=>{const script=document.createElement('script');script.src='/lib/monaco/vs/loader.js';script.onload=()=>{window.require.config({paths:{vs:'/lib/monaco/vs'}});window.require(['vs/editor/editor.main'],()=>resolve(window.monaco),reject);};script.onerror=reject;document.head.appendChild(script);});return editorLoading;}
async function prepareEditor(){
    const turn=current;if(!turn?.isCoding){$('codePanel').hidden=true;$('answerInput').hidden=false;return;}
    $('answerInput').hidden=false;
    try{const monaco=await loadMonaco();if(current?.id!==turn.id)return;
        const language=['csharp','javascript','python'].includes(turn.codeLanguage)?turn.codeLanguage:'plaintext';
        if(!editor){editor=monaco.editor.create($('monacoEditor'),{value:$('answerInput').value,language,theme:editorTheme(monaco),readOnly:busy||speech.listening,automaticLayout:true,minimap:{enabled:false},fontSize:14,wordWrap:'on',scrollBeyondLastLine:false,tabSize:4});editor.onDidChangeModelContent(()=>{if(!suppressEditor&&editorTurnId===current?.id){$('answerInput').value=editor.getValue();edited();}});}
        else{monaco.editor.setModelLanguage(editor.getModel(),language);suppressEditor=true;try{editor.setValue($('answerInput').value);}finally{suppressEditor=false;}}
        editorTurnId=turn.id;lockAnswer();$('codePanel').hidden=false;$('answerInput').hidden=true;editor.layout();updateCount();
    }catch{if(current?.id!==turn.id)return;editorLoading=null;editorTurnId=null;notice('Chưa tải được trình soạn thảo code. Bạn vẫn có thể viết code trong ô văn bản.');$('codePanel').hidden=true;$('answerInput').hidden=false;}
}
function showQuestion(turn,restore=false){sessionStarted??=Date.now();guide('Đến lượt bạn trả lời',config.voice?'Bấm Bắt đầu nói hoặc nhập câu trả lời bên dưới. Kiểm tra nội dung rồi bấm Gửi câu trả lời.':'Đọc câu hỏi, nhập câu trả lời bên dưới rồi bấm Gửi câu trả lời.');current=turn;ready=false;dirty=false;editorTurnId=null;speechTurnId=null;clearTimeout(draftTimer);$('codePanel').hidden=true;$('answerInput').hidden=false;displayedAt=Date.now();firstInput=null;usedRecognition=false;windows=[];speechStart=null;let answer=turn.answer||'';if(restore){try{const saved=JSON.parse(sessionStorage.getItem(key())||'null');if(saved&&typeof saved.answer==='string'){answer=saved.answer;displayedAt=Number(saved.displayedAt)||Date.now();firstInput=saved.firstInput??null;usedRecognition=!!saved.usedRecognition;windows=Array.isArray(saved.windows)?saved.windows:[];dirty=answer!==(turn.answer||'');}}catch{}}$('questionText').textContent=turn.question;$('questionCounter').textContent=`Câu ${turn.mainQuestionNumber}/${config.planned}${turn.followUpDepth?` · Hỏi thêm ${turn.followUpDepth}/2`:''}`;setAnswer(answer);$('answerPanel').hidden=false;$('nextButton').hidden=true;$('evaluateButton').hidden=true;$('endButton').disabled=busy;status('Đang chờ câu trả lời của bạn');$('draftStatus').textContent=answer?'Đã khôi phục bản nháp':'Tự động lưu khi bạn viết';prepareEditor();if(dirty)scheduleDraft();if(!restore&&$('autoRead').checked)tts.speak(turn.question);const completed=Math.max(completedMain,turn.mainQuestionNumber-(turn.followUpDepth?0:1));$('progressCount').textContent=`${completed} / ${config.planned} câu chính`;$('progressBar').style.width=`${100*completed/config.planned}%`;}
function showReady(){sessionEnded??=Date.now();guide('Bạn đã hoàn tất phần trả lời','Bấm Tạo và xem báo cáo để nhận điểm, góp ý và gợi ý luyện tiếp.');$('progressCount').textContent=completedMain+' / '+config.planned+' câu chính';$('progressBar').style.width=(100*completedMain/config.planned)+'%';ready=true;current=null;dirty=false;editorTurnId=null;speechTurnId=null;clearTimeout(draftTimer);$('answerPanel').hidden=true;$('nextButton').hidden=true;$('evaluateButton').hidden=false;$('endButton').disabled=true;$('questionText').textContent='Cảm ơn bạn đã dành thời gian luyện tập. Hãy xem báo cáo để biết bước tiếp theo của mình.';status('Đã hoàn tất phần trả lời');}
// Decode only the first JSON string property while its response streams in. It is provisional until validated server-side.
function partialQuestion(raw){const match=/"question"\s*:\s*"/.exec(raw);if(!match)return '';let text='',escape=false;for(let i=match.index+match[0].length;i<raw.length;i++){const c=raw[i];if(escape){if(c==='u'){const hex=raw.slice(i+1,i+5);if(!/^[0-9a-fA-F]{4}$/.test(hex))break;text+=String.fromCharCode(parseInt(hex,16));i+=4;}else{text+=({n:'\n',r:'\r',t:'\t','"':'"','\\':'\\','/':'/'}[c]??c);}escape=false;}else if(c==='\\')escape=true;else if(c==='"')break;else text+=c;}return text;}
function handleEvent(name,data){if(name==='status')status(data.text);else if(name==='reset'){streamed='';tts.stop();$('questionText').textContent='AI đang điều chỉnh phản hồi…';}else if(name==='delta'){streamed+=data.text;const question=partialQuestion(streamed);if(question){$('questionText').textContent=question;status('AI đang soạn câu hỏi · đang kiểm tra…');}}else if(name==='question')showQuestion(data);else if(name==='ready')showReady();else if(name==='complete'){removeDraft();window.location.assign(data.reportUrl);}else if(name==='error'){const error=new Error(data.message);error.code=['quota','rate_limit','system_concurrency'].includes(data.code)?'rate_limited':data.code;error.retryAfterSeconds=data.retryAfterSeconds||60;if(error.code==='rate_limited')error.message=requestFeedback.waitMessage(error.retryAfterSeconds);throw error;}}
async function stream(operation){
    if(busy||remaining('Stream')>0)return;clearError();setBusy(true);guide(operation==='evaluate'?'Đang tạo báo cáo cho bạn':'Đang chuẩn bị câu hỏi',operation==='evaluate'?'AI đang chấm từng câu đã lưu. Báo cáo sẽ tự mở khi hoàn tất.':'Câu hỏi sẽ xuất hiện khi được kiểm tra xong. Bạn hãy chờ một chút.');tts.stop();streamed='';let terminal=false,reader=null;
    try{await stopListening();const response=await fetch(endpoint('Stream')+'?operation='+operation,{method:'POST',headers:{'RequestVerificationToken':token,'Accept':'text/event-stream, application/json'},credentials:'same-origin'});if(!response.ok)throw await requestFeedback.responseError(response,'Chưa kết nối được buổi luyện. Hãy thử lại.');if(response.redirected||!response.headers.get('content-type')?.includes('text/event-stream')||!response.body)throw new Error('Chưa kết nối được buổi luyện. Hãy tải lại trang nếu phiên đăng nhập đã hết.');
        reader=response.body.getReader();const decoder=new TextDecoder();let buffer='';
        while(true){const{value,done}=await reader.read();buffer=(buffer+decoder.decode(value||new Uint8Array(),{stream:!done})).replace(/\r\n/g,'\n');let end;
            while((end=buffer.indexOf('\n\n'))>=0){const event=buffer.slice(0,end);buffer=buffer.slice(end+2);let name='message',data=[];event.split('\n').forEach(line=>{if(line.startsWith('event:'))name=line.slice(6).trim();else if(line.startsWith('data:'))data.push(line.slice(5).replace(/^ /,''));});if(data.length){handleEvent(name,JSON.parse(data.join('\n')));if(['question','ready','complete'].includes(name))terminal=true;}}
            if(done)break;
        }
        if(!terminal)throw new Error('Kết nối bị ngắt. Dữ liệu đã lưu vẫn được giữ; hãy thử lại.');
    }catch(error){
        rememberCooldown('Stream',error);tts.stop();showError(error.message||'AI chưa trả lời được. Hãy thử lại.',error.code);
        const needsSettings=['not_configured','authentication','model_unavailable','quota_daily','quota_exhausted','system_quota_daily','system_quota_monthly','system_disabled','email_unconfirmed','configuration'].includes(error.code);
        status(needsSettings?'Cần kiểm tra kết nối AI':'Chưa hoàn tất · có thể thử lại');
        if(needsSettings)guide('Kiểm tra kết nối trước khi tiếp tục','Buổi luyện và nội dung đã lưu vẫn được giữ. Mở Cài đặt AI, xử lý lỗi được báo và kiểm tra kết nối; sau đó quay lại buổi này để thử lại.');
        else if(error.code==='rate_limited')guide('Vui lòng chờ trước khi thử lại','Nội dung đã lưu vẫn được giữ. Nút tiếp tục sẽ bật lại khi hết thời gian chờ.');
        else guide('Bạn có thể tiếp tục buổi này',operation==='evaluate'?'Các câu trả lời và phần đã chấm vẫn được giữ. Bấm Tạo và xem báo cáo để thử lại.':'Câu trả lời đã gửi vẫn được giữ. Bấm Thử nhận câu hỏi lại để tiếp tục.');
        if(operation==='question'&&!current){$('questionText').textContent=needsSettings?'Chưa nhận được câu hỏi. Hãy xử lý kết nối AI rồi quay lại buổi này để tiếp tục.':'Chưa nhận được câu hỏi hợp lệ. Nội dung đã gửi được giữ nguyên; bấm thử lại để tiếp tục.';$('nextButton').hidden=false;$('nextButton').textContent='Thử nhận câu hỏi lại →';}
    }
    finally{if(reader){try{await reader.cancel();}catch{}reader.releaseLock();}setBusy(false);}
}
async function submit(skip){
    if(!current||busy||submitting||remaining('Answer')>0)return;if(skip&&!window.confirm('Bỏ qua câu hỏi này? Báo cáo sẽ ghi rõ câu chưa trả lời.'))return;
    submitting=true;setBusy(true);guide('Đang lưu câu trả lời','Chờ lưu xong, hệ thống sẽ chuyển sang câu tiếp theo.');clearError();clearTimeout(draftTimer);tts.stop();
    try{const speechComplete=await stopListening();if(!skip&&speechComplete===false)throw new Error('Hãy kiểm tra transcript rồi bấm Gửi câu trả lời lại.');
        const value=readAnswer();if(!skip&&!value.trim())throw new Error('Hãy nhập câu trả lời hoặc chọn Bỏ qua.');
        stash();if(draftFlight)await draftFlight;
        await post('Answer',{turnId:current.id,answer:value,skip,telemetry:telemetry()});completedMain=Math.max(completedMain,current.mainQuestionNumber);removeDraft();current=null;dirty=false;editorTurnId=null;speechTurnId=null;clearTimeout(draftTimer);$('answerPanel').hidden=true;$('nextButton').hidden=false;$('nextButton').textContent='Nhận câu tiếp theo →';status('Đã lưu câu trả lời');setBusy(false);submitting=false;await stream('question');
    }catch(error){rememberCooldown('Answer',error);showError(error.message);guide('Kiểm tra câu trả lời của bạn','Nội dung vẫn nằm trong ô trả lời. Kiểm tra thông báo phía trên rồi gửi lại.');}finally{submitting=false;setBusy(false);}
}
$('answerButton').addEventListener('click',()=>submit(false));$('skipButton').addEventListener('click',()=>submit(true));$('nextButton').addEventListener('click',()=>stream('question'));$('evaluateButton').addEventListener('click',()=>stream('evaluate'));
$('endButton').addEventListener('click',async()=>{if(busy||submitting||remaining('End')>0||!window.confirm('Kết thúc sớm? Câu đang trả lời sẽ được đánh dấu bỏ qua, báo cáo được ghi là chưa đầy đủ.'))return;tts.stop();setBusy(true);try{await stopListening();if(draftFlight)await draftFlight;await post('End');if(current)removeDraft();showReady();}catch(error){rememberCooldown('End',error);showError(error.message);}finally{setBusy(false);}});
window.addEventListener('beforeunload',()=>{stopListening();tts.stop();if(current)stash();});
setInterval(()=>{updateCooldown();const seconds=sessionStarted===null?0:Math.max(0,Math.floor(((sessionEnded??Date.now())-sessionStarted)/1000));$('sessionTimer').textContent=`${String(Math.floor(seconds/60)).padStart(2,'0')}:${String(seconds%60).padStart(2,'0')}`;},1000);
if(current)showQuestion(current,true);else if(ready)showReady();
})();
