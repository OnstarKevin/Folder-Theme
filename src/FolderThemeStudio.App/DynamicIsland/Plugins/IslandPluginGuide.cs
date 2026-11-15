namespace FolderThemeStudio.App.DynamicIsland.Plugins;

/// <summary>The prompt and bundled example use the same versioned runtime contract.</summary>
public static class IslandPluginGuide
{
    public const string AiPrompt = """
        你将帮助我为 Folder Theme Studio 制作插件。以下是开发上下文，请先记住。
        收到这段提示词后的第一次回复，只能逐字回答：好的，现在请你发送你的软件需求
        第一次回复不要生成代码、不要介绍协议，也不要自行猜测需求。等我下一条消息说明需求后，再按下面协议分析、提问或生成插件。
        输出 manifest.json 和 main.js，将二者放在 ZIP 根目录，扩展名改为 .islandpkg。
        manifest 示例：{"id":"diy.my-plugin","name":"我的插件","version":"1.0.0","apiVersion":1,"minHostVersion":"0.1.0-beta.23","entry":"main.js","permissions":[]}
        运行环境：受限 ECMAScript，无浏览器 DOM、Node.js、CLR、文件、系统命令或网络。不能使用 setTimeout/setInterval/fetch/import/require。状态用普通 JS 变量保存。
        定义 function onInit(context)、function onEvent(event)、function onTick(now)、function onDispose()。
        onTick 的 now 是 UTC Unix 毫秒，每秒调用。onEvent 收到 {name,payload}。
        可选的 manifest.settings 数组使用 {"key":"title","label":"标题","type":"string","default":"你好"}；type 支持 string、number、boolean，时间可用 time（严格HH:mm）。使用普通设置时 minHostVersion 至少为 "0.1.0-beta.24-preview.6"，使用time时至少为 "0.1.0-beta.24-preview.8"。启动时通过 context.settings 读取，设置保存后 onEvent 收到 {name:"settings.changed",payload:完整设置对象}。key 必须以字母开头，仅含字母、数字和下划线。
        可订阅 media.changed（media.read：title/artist/playing/positionMs/durationMs）、clipboard.changed（clipboard.read：text）、notification.received（notifications.read：source/title/body）。权限写入 manifest.permissions；数据仅在宿主对应模块开启并获得系统权限时可用。
        通过 island.publish({text:"单行文字",icon:"",details:"短摘要",ttlMs:10000}) 输出，通过 island.clear() 清除。text 最多160字符，宿主单行省略；icon 最多16字符；ttlMs 为250至60000。持续显示需在 onTick 按需刷新，保持更新频率每秒一次以内。
        不创建窗口，不设置背景。宿主负责全透明、点击穿透、字体、最长302 DIP及轻量尺寸动画。消息、剪贴板、播放中的媒体优先，DIY内容在空闲时显示，优先于金句。
        每次回调有执行时间、内存和语句数限制。不要阻塞、无限循环或使用异步 Promise。onDispose 清理状态并调用 island.clear()。
        用户发送需求之后，才给出完整代码、打包命令、测试步骤和已知限制。若需求需要尚未提供的原生注入、任意UI或系统权限，应明确说明当前宿主不支持，不要编造API。只申请必要权限。
        """;

    public const string ExampleScript = """
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
        """;
}
