# Island Plugin API v1 · Beta 23

入口：灵动岛设置 → 插件与 DIY → 一键安装插件包，或直接拖入 `.islandpkg`。内置“安装专注示例”无需额外数据权限。“用 AI 制作我的插件”默认折叠，可复制完整提示词；“开发者”可导出 SDK。

## 包格式

`.islandpkg` 是 ZIP 容器。根目录必须含 `manifest.json` 和 `main.js`，可选 `assets/`，不要再套一个文件夹。压缩包与解压总量均最多8 MiB、128个条目，脚本最多256 KiB。不接受 EXE、DLL、安装脚本、符号链接或不安全路径。首版素材可以随包保存，显示接口仅消费文字、小图标及摘要。

```json
{"id":"diy.my-plugin","name":"我的插件","description":"一句话说明","version":"1.0.0","apiVersion":1,"minHostVersion":"0.1.0-beta.23","entry":"main.js","permissions":[]}
```

id 用小写字母、数字、点、连字符，3–64字符，必须有点且不能含连续点。version 使用数字版本，如1.0.0。首次涉及敏感数据时需用户授权；更新新增权限会再次询问。最多启用8个DIY插件。

## 生命周期与输出

### 插件自定义设置（本地试用协议）

在 manifest 中添加 `settings` 数组，例如 `{"key":"message","label":"显示内容","type":"string","default":"你好"}`。带设置的插件须设 `minHostVersion` 为 `0.1.0-beta.24-preview.6` 或更高版本。支持 `string`、`number`、`boolean`，插件列表中点“设置”即可编辑。key 必须以英文字母开头，仅包含英文字母、数字和下划线，最长64字符；最多64项，字符串最多4096字符。启动时从 `context.settings` 读取；保存后运行中的插件收到 `onEvent({name:"settings.changed",payload:完整设置对象})`。更新插件时，类型仍兼容的同名设置会保留，其他设置使用新默认值。示例见 `examples/custom-settings`。

```js
function onInit(context) { // context.id / context.apiVersion
  island.publish({text:"你好，桌面。",icon:"✦",details:"这是我的第一个插件",ttlMs:10000});
}
function onEvent(event) { // event.name / event.payload
}
function onTick(now) { // UTC Unix毫秒，约每秒一次
}
function onDispose() { island.clear(); }
```

- `island.publish({text,icon,details,ttlMs})`：text最多160个UTF-16字符、icon最多16、details最多4000；宿主会单行省略，悬停摘要最多三行。ttlMs为250–60000毫秒，默认10000；宿主显示有效期按整秒向上取整。持续内容需按需刷新，如每秒一次。没有调用publish的tick不会延长旧内容寿命。
- `island.clear()` 清除本插件内容；停用、异常和卸载也会清除。
- 消息优先，其后剪贴板与播放中的媒体；DIY内容填充空闲，金句最低。多个DIY插件显示最近发布的有效内容。
- 背景透明、穿透、字体、尺寸和动画由宿主管理。首版不支持任意HTML/WPF窗口或插件自行改变窗口边界。

## 权限与事件

| 权限 | 事件 | payload |
| --- | --- | --- |
| media.read | media.changed | title, artist, playing, positionMs, durationMs |
| clipboard.read | clipboard.changed | text（最多80个文本元素的预览） |
| notifications.read | notification.received | source, title, body（预览） |

权限写入manifest.permissions，对应内置模块也须开启。系统通知额外遵循Windows访问授权。媒体事件在元数据或播放状态变化时发送，positionMs是事件时刻的快照；onTick可用于本地计时。启用插件时补发当前可用数据。没有权限的插件收不到对应事件。

首版不开放网络权限，不提供fetch、DOM、Node.js、CLR、文件或系统命令接口。不要使用require/import、setInterval/setTimeout或Promise；使用onTick和普通JS状态即可。没有通用全局键盘记录API，也不加载任意DLL。

## 资源与故障

各插件在独立子进程运行，脚本有150ms、25000语句、16MB引擎分配及递归深度限制；宿主另有启动5秒、请求2秒超时和IPC大小限制。宿主限流最多10次内容发布/秒，建议每秒一次或仅变化时发布。异常插件会自动停用。独立进程和解释器限制不等于完整的操作系统安全沙箱，请只安装自己信任的插件。

关闭灵动岛会停止全部工作进程，并保留启用偏好；下次开启恢复。无运行插件时调度器等待信号，没有空闲轮询。停用会清除显示内容，移除会删除该插件自己的安装文件。

## 打包专注示例

在 `examples/focus-timer` 中运行：

```powershell
Compress-Archive -LiteralPath manifest.json,main.js -DestinationPath focus-timer.zip
Rename-Item -LiteralPath focus-timer.zip -NewName focus-timer.islandpkg
```

拖入软件后即可使用；也可以直接点击软件内“安装专注示例”。代码可参考该示例并让AI修改。测试时暂时关闭音乐/消息等较高优先级显示，或暂停播放后查看DIY内容。

## 时间与复杂示例

Preview8支持type=time，值为严格24小时HH:mm，最低宿主为0.1.0-beta.24-preview.8。custom-settings示例含专注/休息时长、循环、时钟和每日提醒。提醒只在插件运行期间显示于灵动岛，不补发错过时间。

AI提示词作为前置上下文使用：先发给AI，首轮应回答“好的，现在请你发送你的软件需求”，之后再发送需求。实际AI是否遵循取决于对方模型与会话上下文。
