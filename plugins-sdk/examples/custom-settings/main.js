let config = {}, started = 0, remindedDay = "", reminderUntil = 0;
function minutes(value, fallback, maximum) { return Math.max(1, Math.min(maximum, Number(value) || fallback)); }
function pad(value) { return String(value).padStart(2, "0"); }
function show(now) {
  const date = new Date(now), clock = pad(date.getHours()) + ":" + pad(date.getMinutes());
  const day = date.getFullYear() + "-" + date.getMonth() + "-" + date.getDate() + "/" + config.reminderTime;
  if (config.reminderEnabled && clock === config.reminderTime && remindedDay !== day) { remindedDay = day; reminderUntil = now + 10000; }
  let text = String(config.message || "我的专注").slice(0, 60), details = "每日提醒使用本机时间；仅插件运行期间生效，不补发错过的提醒。";
  if (now < reminderUntil && config.reminderEnabled) text = String(config.reminderText || "时间到了。").slice(0, 80);
  else if (config.focusEnabled) {
    const focus = minutes(config.focusMinutes, 25, 180) * 60000, rest = minutes(config.breakMinutes, 5, 60) * 60000;
    const elapsed = Math.max(0, now - started), position = config.autoCycle ? elapsed % (focus + rest) : elapsed;
    const resting = config.autoCycle && position >= focus;
    const remaining = Math.max(0, Math.ceil(((resting ? focus + rest : focus) - position) / 1000));
    text += remaining > 0 ? " · " + (resting ? "休息 " : "专注 ") + pad(Math.floor(remaining / 60)) + ":" + pad(remaining % 60) : " · 专注完成";
    details += "\n专注 " + focus / 60000 + " 分钟，休息 " + rest / 60000 + " 分钟；修改时长会重置计时。";
  }
  if (config.showClock) text += " · " + clock;
  island.publish({ text: text.slice(0, 150), icon: config.showIcon ? "✦" : "", details: details, ttlMs: 2500 });
}
function onInit(context) { config = context.settings || {}; started = Date.now(); show(started); }
function onEvent(event) {
  if (event.name !== "settings.changed") return;
  const next = event.payload || {};
  if (next.focusMinutes !== config.focusMinutes || next.breakMinutes !== config.breakMinutes || next.focusEnabled !== config.focusEnabled || next.autoCycle !== config.autoCycle) started = Date.now();
  config = next; show(Date.now());
}
function onTick(now) { show(now); }
function onDispose() { island.clear(); }
