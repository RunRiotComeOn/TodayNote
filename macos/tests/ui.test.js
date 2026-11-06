const assert=require('node:assert/strict');
const path=require('node:path');
const {pathToFileURL}=require('node:url');
const {chromium}=require('playwright');
const C=require('../src/core.js');
const html=pathToFileURL(path.resolve(__dirname,'../src/index.html')).href;
let count=0;const check=(condition,label)=>{assert.ok(condition,label);count++;console.log('PASS',label);};
async function mock(page, initial) {
 await page.addInitScript(initial=>{
  window.nativeState=initial;window.nativeCalls=[];window.nativeAlerts=[];
  window.webkit={messageHandlers:{todayNote:{postMessage(message){
   window.nativeCalls.push(message);let data=null;
   if(message.action==='load')data={state:window.nativeState,startup:false,notice:''};
   if(message.action==='save'){window.nativeState=message.args.state;data=true;}
   if(message.action==='startup')data=message.args.enabled;
   if(message.action==='export'){window.exported=message.args.state;data=true;}
   if(message.action==='import')data=window.importFixture;
   if(message.action==='restore'){window.restorePrevious=message.args.previous;window.nativeState=message.args.state;data=true;}
   if(message.action==='alert')window.nativeAlerts.push(message.args.message);
   setTimeout(()=>window.TodayApp.receive({id:message.id,ok:true,data}),0);
  }}}};
 },initial);
}
(async()=>{
 const browser=await chromium.launch({executablePath:process.env.TODAYNOTE_CHROME||undefined,headless:true,args:['--no-sandbox']});
 try{
  const context=await browser.newContext({viewport:{width:400,height:640},timezoneId:'Asia/Shanghai'});
  const page=await context.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await mock(page,C.fresh());await page.goto(html);await page.waitForFunction(()=>document.querySelector('#date').textContent.includes('月'));
  check(await page.locator('#empty').isVisible(),'empty state renders');
  await page.fill('#new-title','阅读论文');await page.press('#new-title','Enter');
  await page.fill('#new-title','修改研究笔记');await page.click('#new-urgency');await page.getByRole('button',{name:'紧急 · 高',exact:true}).click();await page.click('#new-importance');await page.getByRole('button',{name:'重要 · 低',exact:true}).click();await page.press('#new-title','Enter');
  check(await page.locator('.task').count()===2,'add tasks with independent urgency and importance');
  check((await page.locator('.task').last().innerText()).includes('紧急 · 高')&&(await page.locator('.task').last().innerText()).includes('重要 · 低'),'classification badges reflect chosen values');
  await page.locator('.check').first().click();check(await page.locator('.task.done').count()===1&&await page.locator('.task').count()===2,'completion stays visible');
  check((await page.locator('.task.done .task-title').evaluate(e=>getComputedStyle(e).textDecoration)).includes('line-through'),'completed text is crossed out');
  await page.click('#history');check((await page.locator('#records').innerText()).includes('阅读论文'),'completion appears in history');await page.locator('#history-modal .close').click();
  await page.locator('.task-title').last().dblclick();await page.fill('#edit-title','整理实验结果\n写下一步计划');await page.click('#edit-form button[type=submit]');check((await page.locator('.task-title').last().innerText()).includes('下一步'),'edit supports multiline titles');
  await page.locator('.task-title').last().dblclick();await page.press('#edit-title','Meta+Enter');check(await page.locator('#edit-modal').isHidden(),'Command-Enter saves editor without newer Safari form APIs');
  await page.locator('.task').last().hover();await page.locator('.more').last().click();await page.getByRole('button',{name:'删除',exact:true}).click();check(await page.locator('.task').count()===1,'delete a task');await page.click('#undo');check(await page.locator('.task').count()===2,'undo deletion restores task');
  await page.click('#settings');await page.selectOption('#theme','dark');check(await page.locator('html').getAttribute('data-theme')==='dark','dark appearance applies immediately');
  check(await page.locator('body').evaluate(e=>getComputedStyle(e).backgroundColor)==='rgb(30, 34, 40)','dark background renders');
  await page.selectOption('#theme','system');await page.emulateMedia({colorScheme:'dark'});check(await page.locator('body').evaluate(e=>getComputedStyle(e).backgroundColor)==='rgb(30, 34, 40)','system mode follows dark preference');
  await page.emulateMedia({colorScheme:'light'});check(await page.locator('body').evaluate(e=>getComputedStyle(e).backgroundColor)==='rgb(253, 253, 253)','system mode follows light preference');
  await page.selectOption('#theme','dark');await page.selectOption('#font','16');await page.check('#topmost');await page.click('#export');await page.waitForFunction(()=>!!window.exported);check(await page.evaluate(()=>window.exported.Settings.Theme)==='dark','backup exports selected theme');
  await page.locator('#settings-modal .close').click();await page.waitForFunction(()=>window.nativeState?.Settings.Theme==='dark'&&window.nativeState?.Settings.FontSize===16);check(await page.evaluate(()=>window.nativeState.Tasks.length)===2,'save bridge receives tasks and preferences');
  await page.setViewportSize({width:340,height:460});await page.click('#settings');check(await page.locator('#theme').isVisible(),'settings fits minimum window width');check(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'no horizontal overflow at minimum size');await page.locator('#settings-modal .close').click();
  await page.setViewportSize({width:400,height:640});await page.screenshot({path:path.resolve(__dirname,'../build/ui-dark.png')});
  await page.click('#settings');const legacy=C.fresh();delete legacy.Settings.Theme;C.add(legacy,'从 Windows 迁移的待办',1,3);await page.evaluate(data=>{window.importFixture=data;},legacy);await page.click('#import');await page.click('#confirm-import');await page.waitForFunction(()=>document.querySelector('.task-title')?.textContent==='从 Windows 迁移的待办');check(await page.locator('html').getAttribute('data-theme')==='light','Windows backup imports with old theme default');check(await page.evaluate(()=>window.restorePrevious.Tasks.length)===2,'restore preserves previous snapshot');
  const title='<img src=x onerror=alert(1)>';await page.fill('#new-title',title);await page.press('#new-title','Enter');check(await page.locator('.task img').count()===0,'task text cannot inject HTML');
  check(errors.length===0,'no JavaScript errors during complete flow');check(await page.evaluate(()=>window.nativeAlerts.length)===0,'no unexpected error alerts');
  const late=await context.newPage();await late.clock.install({time:new Date('2026-11-06T03:59:58+08:00')});
  const rollover=C.fresh();C.refresh(rollover,new Date('2026-11-05T20:00:00+08:00'));const t=C.add(rollover,'昨天完成的',2,2,new Date('2026-11-05T20:00:00+08:00'));C.toggle(rollover,t.Id,new Date('2026-11-05T21:00:00+08:00'));C.add(rollover,'未完成的',3,3,new Date('2026-11-05T21:01:00+08:00'));
  await mock(late,rollover);await late.goto(html);await late.clock.runFor(30);check(await late.locator('.task').count()===2,'pre-cutoff UI keeps previous workday completion');await late.clock.runFor(5100);check(await late.locator('.task').count()===1&&(await late.locator('.task-title').innerText())==='未完成的','UI rollover carries pending task and archives completion');
  console.log(`${count} UI checks passed.`);await context.close();
 }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exit(1);});
