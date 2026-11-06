(function () {
  'use strict';
  const C=window.NoteCore, $=id=>document.getElementById(id);
  const levels=['','低','中','高'];
  let state, initialized=false, nextId=0, urgency=2, importance=2, editId=null, pendingImport=null;
  let revision=0, savedRevision=0, saveQueue=Promise.resolve(), saving=0, failure=false;
  let statusText='', statusUntil=0, undo=null, lastFocus=null, startup=false;
  const pending=new Map();
  const clone=value=>JSON.parse(JSON.stringify(value));
  const escape=value=>String(value).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  function request(action,args={}) {
    return new Promise((resolve,reject)=>{
      const id=++nextId;
      let timer=null;
      if (!['export','import','alert','restore'].includes(action)) timer=setTimeout(()=>{pending.delete(id);reject(new Error('这一步没有完成，请重试。'));},15000);
      pending.set(id,{resolve,reject,timer});
      try { window.webkit.messageHandlers.todayNote.postMessage({id,action,args}); }
      catch (_) { clearTimeout(timer);pending.delete(id);reject(new Error('请从“今日”应用打开。')); }
    });
  }
  function receive(message) {
    const item=pending.get(message.id); if(!item)return;
    clearTimeout(item.timer);pending.delete(message.id);
    if(message.ok)item.resolve(message.data);else item.reject(new Error(message.error||'这一步没有完成。'));
  }
  function say(message,seconds=4) { statusText=message;statusUntil=Date.now()+seconds*1000;renderStatus(); }
  function renderStatus() {
    if(!initialized)return;
    $('status').classList.toggle('error',failure);
    $('status').textContent=failure?'未保存 · 点击重试':Date.now()<statusUntil?statusText:`${timeString(state.Settings.RolloverMinutes)} 换日`;
    $('status').title=failure?'点击重新保存当前待办':'';
    if(undo && Date.now()>undo.until)undo=null;
    $('undo').hidden=!undo;
  }
  function alertError(error) { say(error.message||String(error),9);request('alert',{message:error.message||String(error)}).catch(()=>{}); }
  function persist() {
    if(!initialized)return Promise.resolve();
    const rev=++revision,snapshot=clone(state);saving++;
    const task=saveQueue.catch(()=>{}).then(()=>request('save',{state:snapshot})).then(()=>{
      savedRevision=Math.max(savedRevision,rev);if(savedRevision===revision)failure=false;renderStatus();
    });
    saveQueue=task;
    task.catch(()=>{failure=true;renderStatus();}).finally(()=>{saving--;});
    return task;
  }
  function refresh() {
    if(!initialized)return false;
    if(C.refresh(state)){undo=null;persist();render();return true;}
    return false;
  }
  function applyTheme() {
    document.documentElement.dataset.theme=state.Settings.Theme;
    document.documentElement.style.setProperty('--font',state.Settings.FontSize+'px');
    request('options',{theme:state.Settings.Theme,topmost:state.Settings.Topmost}).catch(alertError);
  }
  function timeString(minutes) {return `${String(Math.floor(minutes/60)).padStart(2,'0')}:${String(minutes%60).padStart(2,'0')}`;}
  function render(animatedId) {
    const list=C.visible(state),done=list.filter(t=>t.CompletedDay).length;
    const [y,m,d]=state.Day.split('-').map(Number),day=new Date(y,m-1,d);
    $('date').textContent=`${m} 月 ${d} 日  ·  ${day.toLocaleDateString('zh-CN',{weekday:'long'})}`;
    $('count').textContent=`${done} / ${list.length}`;
    $('count').title=`已完成 ${done} 项，共 ${list.length} 项`;
    $('progress').style.width=(list.length?done/list.length*100:0)+'%';
    $('empty').hidden=list.length!==0;
    const scroll=$('tasks').scrollTop;
    $('tasks').innerHTML=list.map(t=>`<article class="task ${t.CompletedDay?'done':''} ${t.Id===animatedId?'just-done':''}" data-task="${escape(t.Id)}"><button class="check" data-action="toggle" aria-label="${t.CompletedDay?'取消完成：':'完成：'}${escape(t.Title)}"><span><svg viewBox="0 0 20 20"><path d="m4 10 4 4 8-9"/></svg></span></button><div class="task-body"><div class="task-title" data-action="edit" title="双击编辑">${escape(t.Title)}</div><div class="badges"><button class="badge ${t.Urgency===3?'high':''}" data-dimension="Urgency">紧急 · ${levels[t.Urgency]}</button><button class="badge ${t.Importance===3?'high':''}" data-dimension="Importance">重要 · ${levels[t.Importance]}</button></div></div><button class="more" data-action="more" aria-label="编辑或删除待办" title="更多">⋯</button></article>`).join('');
    $('tasks').scrollTop=scroll;
    $('pin').classList.toggle('active',state.Settings.Topmost);
    $('pin').setAttribute('aria-pressed',String(state.Settings.Topmost));
    $('pin').title=state.Settings.Topmost?'已置顶 · 点击取消':'置顶';
    $('new-urgency').innerHTML=`紧急 · ${levels[urgency]} <span>⌄</span>`;
    $('new-importance').innerHTML=`重要 · ${levels[importance]} <span>⌄</span>`;
    renderStatus();
    if(animatedId && done>0 && done===list.length)say('今天做得很好。',8);
  }
  function openModal(id) {
    closePopover();lastFocus=document.activeElement;
    document.querySelectorAll('.veil').forEach(v=>v.hidden=true);
    $(id).hidden=false;
    setTimeout(()=>{$(id).querySelector('input,select,textarea,button')?.focus();},0);
  }
  function closeModal(modal) {modal.hidden=true;if(modal.id==='confirm-modal')pendingImport=null;lastFocus?.focus?.();}
  function openSettings() {
    if(!initialized)return;refresh();
    $('theme').value=state.Settings.Theme;$('topmost').checked=state.Settings.Topmost;
    $('startup').checked=startup;$('sort').checked=state.Settings.PrioritySort;
    $('cutoff').value=timeString(state.Settings.RolloverMinutes);$('font').value=String(state.Settings.FontSize>=16?16:state.Settings.FontSize<=12?12:14);
    openModal('settings-modal');
  }
  function closePopover() {$('popover').hidden=true;$('popover').replaceChildren();}
  function popover(anchor,items) {
    closePopover();const menu=$('popover');
    for(const item of items){const b=document.createElement('button');b.textContent=item.label;b.classList.toggle('selected',!!item.selected);b.onclick=()=>{closePopover();item.run();};menu.append(b);}
    menu.hidden=false;const rect=anchor.getBoundingClientRect();
    const left=Math.max(8,Math.min(rect.left,innerWidth-menu.offsetWidth-8));
    const top=rect.bottom+menu.offsetHeight+8>innerHeight?rect.top-menu.offsetHeight-5:rect.bottom+5;
    menu.style.left=left+'px';menu.style.top=Math.max(8,top)+'px';menu.firstElementChild?.focus();
  }
  function chooseLevel(anchor,id,dimension) {
    const task=id?state.Tasks.find(t=>t.Id===id):null;
    popover(anchor,[1,2,3].map(value=>({label:`${dimension==='Urgency'?'紧急':'重要'} · ${levels[value]}`,selected:value===(task?task[dimension]:dimension==='Urgency'?urgency:importance),run:()=>{
      refresh();if(task){task[dimension]=value;persist();}else if(dimension==='Urgency')urgency=value;else importance=value;render();
    }})));
  }
  function edit(id) {
    refresh();const t=C.visible(state).find(t=>t.Id===id);if(!t)return;
    editId=id;$('edit-title').value=t.Title;$('edit-urgency').value=String(t.Urgency);$('edit-importance').value=String(t.Importance);
    openModal('edit-modal');setTimeout(()=>{$('edit-title').focus();$('edit-title').select();},0);
  }
  function remove(id) {
    refresh();const index=state.Tasks.findIndex(t=>t.Id===id);if(index<0)return;
    undo={task:state.Tasks[index],index,until:Date.now()+12000};state.Tasks.splice(index,1);
    persist();say('已删除',12);render();
  }
  function renderRecords() {
    const tasks=state.Tasks.filter(t=>t.CompletedDay===$('history-day').value);
    $('records').innerHTML=tasks.length?tasks.map(t=>`<div class="record">✓　${escape(t.Title)}</div>`).join(''):'<p class="record-empty">每完成一件小事，都会记在这里。</p>';
  }
  function openHistory() {
    refresh();const days=[...new Set(state.Tasks.filter(t=>t.CompletedDay).map(t=>t.CompletedDay))].sort().reverse();
    $('history-day').innerHTML=days.map(d=>`<option>${escape(d)}</option>`).join('');$('history-day').hidden=!days.length;renderRecords();openModal('history-modal');
  }
  async function importBackup() {
    try{const data=await request('import');if(!data)return;pendingImport=C.normalize(data);openModal('confirm-modal');}
    catch(e){alertError(e);}
  }
  async function confirmImport() {
    if(!pendingImport)return;
    const next=pendingImport;C.refresh(next);$('confirm-import').disabled=true;
    try{
      await saveQueue.catch(()=>{});await request('restore',{previous:clone(state),state:next});state=next;revision++;savedRevision=revision;failure=false;undo=null;
      pendingImport=null;closeModal($('confirm-modal'));applyTheme();render();say('备份已恢复',6);
    }catch(e){alertError(e);}finally{$('confirm-import').disabled=false;}
  }
  async function requestQuit() {
    try{if(initialized){if(savedRevision<revision || failure)await persist();else await saveQueue;}await request('quit');}
    catch(_){request('cancelQuit').catch(()=>{});alertError(new Error('还有内容没有成功保存，请先导出备份，再退出软件。'));}
  }
  $('add-form').onsubmit=e=>{
    e.preventDefault();if(!initialized)return;const title=$('new-title').value.trim();if(!title){$('new-title').focus();return;}
    try{C.add(state,title,urgency,importance);persist();$('new-title').value='';render();$('new-title').focus();$('tasks').scrollTop=$('tasks').scrollHeight;}catch(e){alertError(e);}
  };
  $('tasks').onclick=e=>{
    const row=e.target.closest('[data-task]');if(!row)return;const id=row.dataset.task;
    const dimension=e.target.closest('[data-dimension]');if(dimension){chooseLevel(dimension,id,dimension.dataset.dimension);return;}
    const button=e.target.closest('button');if(!button)return;
    if(button.dataset.action==='toggle'){if(C.toggle(state,id)){persist();const t=state.Tasks.find(t=>t.Id===id);render(t.CompletedDay?id:null);const match=[...$('tasks').querySelectorAll('[data-task]')].find(el=>el.dataset.task===id);match?.querySelector('.check')?.focus({preventScroll:true});}else render();}
    if(button.dataset.action==='more')popover(button,[{label:'编辑',run:()=>edit(id)},{label:'删除',run:()=>remove(id)}]);
  };
  $('tasks').ondblclick=e=>{if(e.target.closest('.task-title'))edit(e.target.closest('[data-task]').dataset.task);};
  $('tasks').oncontextmenu=e=>{const row=e.target.closest('[data-task]');if(!row)return;e.preventDefault();popover(row.querySelector('.more'),[{label:'编辑',run:()=>edit(row.dataset.task)},{label:'删除',run:()=>remove(row.dataset.task)}]);};
  $('edit-form').onsubmit=e=>{e.preventDefault();const t=state.Tasks.find(t=>t.Id===editId),title=$('edit-title').value.trim();if(!t||!title)return;t.Title=title;t.Urgency=Number($('edit-urgency').value);t.Importance=Number($('edit-importance').value);refresh();persist();render();closeModal($('edit-modal'));};
  $('new-urgency').onclick=e=>chooseLevel(e.currentTarget,null,'Urgency');$('new-importance').onclick=e=>chooseLevel(e.currentTarget,null,'Importance');
  $('pin').onclick=()=>{state.Settings.Topmost=!state.Settings.Topmost;applyTheme();persist();render();};
  $('settings').onclick=openSettings;$('history').onclick=openHistory;$('history-day').onchange=renderRecords;
  $('theme').onchange=()=>{state.Settings.Theme=$('theme').value;applyTheme();persist();};
  $('topmost').onchange=()=>{state.Settings.Topmost=$('topmost').checked;applyTheme();persist();render();};
  $('sort').onchange=()=>{state.Settings.PrioritySort=$('sort').checked;persist();render();};
  $('font').onchange=()=>{state.Settings.FontSize=Number($('font').value);applyTheme();persist();render();};
  $('cutoff').onchange=()=>{if(!/^\d{2}:\d{2}$/.test($('cutoff').value)){$('cutoff').value=timeString(state.Settings.RolloverMinutes);return;}const [h,m]=$('cutoff').value.split(':').map(Number);state.Settings.RolloverMinutes=h*60+m;C.refresh(state);persist();render();};
  $('startup').onchange=async()=>{try{startup=!!await request('startup',{enabled:$('startup').checked});$('startup').checked=startup;}catch(e){$('startup').checked=startup;alertError(e);}};
  $('export').onclick=async()=>{try{refresh();const result=await request('export',{state:clone(state)});if(result)say('备份已导出');}catch(e){alertError(e);}};
  $('import').onclick=importBackup;$('confirm-import').onclick=confirmImport;$('quit').onclick=requestQuit;
  $('status').onclick=()=>{if(failure)persist();};
  $('undo').onclick=()=>{if(!undo)return;const item=undo;undo=null;state.Tasks.splice(Math.min(item.index,state.Tasks.length),0,item.task);refresh();persist();render();say('已恢复');};
  document.querySelectorAll('.close').forEach(b=>b.onclick=()=>closeModal(b.closest('.veil')));
  document.addEventListener('click',e=>{if(!e.target.closest('#popover,.badge,.more'))closePopover();});
  document.addEventListener('keydown',e=>{
    if(e.key==='Escape'){if(!$('popover').hidden)closePopover();else {const modal=[...document.querySelectorAll('.veil')].find(v=>!v.hidden);if(modal)closeModal(modal);}e.preventDefault();}
    if((e.metaKey||e.ctrlKey)&&e.key.toLowerCase()==='n'){e.preventDefault();$('new-title').focus();}
    if((e.metaKey||e.ctrlKey)&&e.key===','){e.preventDefault();openSettings();}
    if(e.key==='Tab'){const scope=!$('popover').hidden?$('popover'):[...document.querySelectorAll('.veil')].find(v=>!v.hidden);if(scope){const items=[...scope.querySelectorAll('button,input,select,textarea')].filter(el=>!el.disabled&&!el.hidden);const first=items[0],last=items[items.length-1];if(e.shiftKey&&document.activeElement===first){last.focus();e.preventDefault();}else if(!e.shiftKey&&document.activeElement===last){first.focus();e.preventDefault();}}}
    if((e.metaKey||e.ctrlKey)&&e.key==='Enter'&&!$('edit-modal').hidden){e.preventDefault();$('edit-form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));}
  });
  window.addEventListener('focus',()=>refresh());
  window.TodayApp={receive,openSettings,requestQuit,focusInput:()=>$('new-title').focus(),togglePin:()=>{$('pin').click();}};
  request('load').then(data=>{
    state=C.normalize(data.state||C.fresh());C.refresh(state);startup=!!data.startup;initialized=true;
    applyTheme();render();persist();if(data.notice)request('alert',{message:data.notice}).catch(()=>{});
    setInterval(()=>{refresh();renderStatus();if(failure&&!saving)persist();},5000);
  }).catch(e=>{document.querySelector('.composer').hidden=true;$('date').textContent='暂时无法打开';$('status').textContent=e.message;request('alert',{message:e.message}).catch(()=>{});});
})();
