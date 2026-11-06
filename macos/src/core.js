(function (root) {
  'use strict';
  const DEFAULTS = Object.freeze({RolloverMinutes:240, Topmost:true, PrioritySort:false, Width:400, Height:640, Left:-99999, Top:-99999, FontSize:14, Theme:'light'});
  const clamp = (n, low, high, fallback) => Number.isFinite(n) ? Math.max(low, Math.min(high, n)) : fallback;
  const theme = value => ['light','dark','system'].includes(value) ? value : 'light';
  const dateKey = date => `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`;
  const dayAt = (now=new Date(), cutoff=240) => {
    const day = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    if (now.getHours()*60+now.getMinutes() < cutoff) day.setDate(day.getDate()-1);
    return dateKey(day);
  };
  const isDay = value => {
    if (typeof value!=='string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
    const [y,m,d] = value.split('-').map(Number);
    return dateKey(new Date(y,m-1,d))===value;
  };
  const fresh = () => ({Version:1, Day:null, Settings:{...DEFAULTS}, Tasks:[]});
  function normalize(data) {
    if (!data || data.Version!==1 || !Array.isArray(data.Tasks) || !data.Settings || typeof data.Settings!=='object') throw new Error('这份备份格式不受支持。');
    if (data.Tasks.length>100000) throw new Error('备份内容过大。');
    if (data.Day && !isDay(data.Day)) throw new Error('备份中的日期无效。');
    const prefs={...DEFAULTS,...data.Settings};
    prefs.RolloverMinutes=Math.floor(clamp(prefs.RolloverMinutes,0,1439,240));
    prefs.FontSize=clamp(prefs.FontSize,12,18,14); prefs.Theme=theme(prefs.Theme);
    prefs.Width=clamp(prefs.Width,340,900,400); prefs.Height=clamp(prefs.Height,440,1200,640);
    prefs.Topmost=typeof prefs.Topmost==='boolean' ? prefs.Topmost : true;
    prefs.PrioritySort=!!prefs.PrioritySort;
    const ids=new Set();
    const tasks=data.Tasks.map(t => {
      if (!t || typeof t.Id!=='string' || !t.Id || ids.has(t.Id) || typeof t.Title!=='string' || !t.Title.trim() || t.Title.length>500) throw new Error('备份中的待办内容无效。');
      if (t.CompletedDay && !isDay(t.CompletedDay)) throw new Error('备份中的完成日期无效。');
      ids.add(t.Id);
      return {...t, Title:t.Title.trim(), Urgency:Math.floor(clamp(t.Urgency,1,3,2)), Importance:Math.floor(clamp(t.Importance,1,3,2)), CompletedDay:t.CompletedDay||null, CompletedAt:t.CompletedAt||null};
    });
    return {Version:1,Day:data.Day||null,Settings:prefs,Tasks:tasks};
  }
  function refresh(state, now=new Date()) {
    const next=dayAt(now,state.Settings.RolloverMinutes);
    if (!state.Day || next>state.Day) { state.Day=next; return true; }
    return false;
  }
  function visible(state) {
    const all=state.Tasks.filter(t=>!t.CompletedDay || t.CompletedDay===state.Day);
    if (!state.Settings.PrioritySort) return all;
    return all.map((t,i)=>({t,i})).sort((a,b)=>Number(!!a.t.CompletedDay)-Number(!!b.t.CompletedDay) || (b.t.Urgency+b.t.Importance)-(a.t.Urgency+a.t.Importance) || b.t.Importance-a.t.Importance || a.i-b.i).map(x=>x.t);
  }
  function add(state, title, urgency=2, importance=2, now=new Date()) {
    refresh(state,now); title=title.trim();
    if (!title || title.length>500) throw new Error('待办内容需要在 1–500 字之间。');
    const id=typeof crypto!=='undefined' && crypto.randomUUID ? crypto.randomUUID() : Date.now().toString(36)+'-'+Math.random().toString(36).slice(2)+'-'+Math.random().toString(36).slice(2);
    const task={Id:id,Title:title,Urgency:Math.floor(clamp(urgency,1,3,2)),Importance:Math.floor(clamp(importance,1,3,2)),CreatedAt:now.toISOString(),CompletedDay:null,CompletedAt:null};
    state.Tasks.push(task); return task;
  }
  function toggle(state, id, now=new Date()) {
    refresh(state,now);
    const task=state.Tasks.find(t=>t.Id===id);
    if (!task || (task.CompletedDay && task.CompletedDay!==state.Day)) return false;
    const done=!!task.CompletedDay; task.CompletedDay=done?null:state.Day; task.CompletedAt=done?null:now.toISOString(); return true;
  }
  const api={DEFAULTS,fresh,normalize,refresh,visible,add,toggle,dayAt,dateKey,theme};
  if (typeof module!=='undefined' && module.exports) module.exports=api;
  else root.NoteCore=api;
})(typeof globalThis!=='undefined'?globalThis:this);
