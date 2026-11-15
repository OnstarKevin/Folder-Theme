# 参考与第三方服务

Beta 23 的独立插件工作进程使用 Jint 4.16.3（BSD-2-Clause）及其依赖 Acornima 1.7.0（BSD-3-Clause）。精确版权与许可见 `licenses/Jint-BSD-2-Clause.txt`、`licenses/Acornima-BSD-3-Clause.txt`。Jint 项目：https://github.com/sebastienros/jint；Acornima：https://github.com/adams85/acornima。脚本不获得CLR或系统命令接口；进程隔离不宣称完整OS安全沙箱。


本项目参考 [NotchPeninsula](https://github.com/GEORGEWWWU/NotchPeninsula) 的 SMTC 信息获取、歌词多来源回退与本地消息通道设计。参考提交：`41eb3d610ece8c474cd2bf5e3542e477100b5fb0`（2026-10-04 获取）。

参考仓库采用 Apache License 2.0，原始许可证副本见 [licenses/NotchPeninsula-Apache-2.0.txt](licenses/NotchPeninsula-Apache-2.0.txt)。本项目在 WPF/.NET 8 中独立实现接口、缓存、消息鉴权及生命周期；没有复制其渲染资源、品牌素材或无条件跳过 TLS 证书校验的处理。

在线歌词来自落月 API（api.vkeys.cn）、网易云（music.163.com）、LRCLIB（lrclib.net）。来源可在灵动岛悬停摘要中查看。服务可用性和曲库覆盖由对应服务决定，歌词权利属于其权利人。在线歌词默认关闭，开启后查询歌名与歌手，不上传剪贴板或通知。没有下载音频或绕过付费歌曲权限。

开发依据：
- [NotchPeninsula MediaController](https://github.com/GEORGEWWWU/NotchPeninsula/blob/41eb3d610ece8c474cd2bf5e3542e477100b5fb0/Media/MediaController.cs)
- [NotchPeninsula Toast](https://github.com/GEORGEWWWU/NotchPeninsula/blob/41eb3d610ece8c474cd2bf5e3542e477100b5fb0/Notify/Toast.cs)
- [LRCLIB API 文档](https://lrclib.net/docs)
- [Microsoft 系统媒体会话](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager)
