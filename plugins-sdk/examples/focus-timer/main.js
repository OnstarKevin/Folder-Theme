let endAt = 0;
function show(now) {
  const remaining = Math.max(0, Math.ceil((endAt - now) / 1000));
  const minutes = String(Math.floor(remaining / 60)).padStart(2, "0");
  const seconds = String(remaining % 60).padStart(2, "0");
  island.publish({ text: remaining > 0 ? "专注 " + minutes + ":" + seconds : "专注完成，休息一下。", details: "25 分钟专注计时 · DIY 示例", ttlMs: 2500 });
}
function onInit(context) { endAt = Date.now() + 25 * 60 * 1000; show(Date.now()); }
function onTick(now) { show(now); }
function onDispose() { island.clear(); }
