# FrameFlow V27 改名、Esc 与最大化圆角检查

日期：2026-10-18。Windows x64，.NET 10。公开版本 V27，文件版本 `7.1.2.1027`，产品名 FrameFlow。验证只操作本次测试自己的窗口与独立配置，没有替换 `C:/App/mpvnet-modern` 中的原播放器或修改 Windows 默认打开方式。

## 修改

- 程序名 `FrameFlow.exe`、产品元数据、空闲窗口标题、关于界面、设置帮助、文档及发布包使用 FrameFlow。资源程序集 URI 同步更新；原命名空间、脚本通信和配置的历史逻辑标识保留兼容。
- 外层窗口区域、Windows 11 合成器圆角偏好和蓝色栏使用同一状态判断。普通窗口为圆角；最大化及全屏为直角、无拉伸边距。最大化退出全屏后仍为直角，恢复普通窗口后恢复圆角。支持该属性的系统显式使用 `DWMWCP_DONOTROUND`；旧系统继续依靠无裁切区域和矩形绘制。
- Esc 有显式 `ignore` 默认，移除原默认直接退出绑定。旧配置中的普通 Esc 直接 `quit`／`quit-watch-later`、可选退出码和 async 形式在加载时中和，原文件保持。其他自定义命令、组合键和已有界面取消逻辑保留。本版不新增 Esc 退出全屏。
- `mpvnet.conf` 首次读取复制迁移为 `frameflow.conf`，保留旧文件。设置加载按历史逻辑类型匹配新物理文件名，防止丢失选项与注释。已有新配置优先；新 `FRAMEFLOW_HOME` 与旧 `MPVNET_HOME` 均支持。新非便携用户使用 FrameFlow 目录，已有旧非便携配置继续兼容读取。
- 新包只携带基础 `mpv.conf`，前端使用代码默认值，不附 `frameflow.conf` 模板，避免升级复制旧 `mpvnet.conf` 后被新模板遮住。旧前端配置首次读取后复制迁移；新用户修改前端选项时保存为 `frameflow.conf`。
- 关于与发布包增加来源、原作者、修改日期、许可正文、第三方清单及本版前端源码说明。已识别 NGettext.Wpf 的上游 LGPL v3，与 MIT 的 NGettext 分开声明；添加 HandyControl MIT、原生组件许可及 GPL v3 正文。

## 通过的验证

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 设置模型 | 138 项通过 | `test-results/v27/settings.log` |
| 实际 UI 与输入 | 126 项通过 | `test-results/controls-v27/live/observations.json` |
| 最大化与全屏边缘 | 外框区域、无拉伸边距、DWM 偏好、蓝色栏四角完全不透明；普通窗口恢复抗锯齿圆角 | 同上；`maximized-header.png`、`fullscreen-header.png`、`restored-maximized-header.png`、`header-corners.png` |
| Esc 与兼容配置 | 普通、最大化和全屏不退出；菜单、音量、倍速、设置搜索清空及设置关闭继续生效；旧输入配置及全局恢复默认均不会恢复 Esc 退出 | 同上 |
| 原有操作 | 方向键轻点／长按、独立 Tab 长按、进度条合并提交、音量增强、鼠标映射、设置搜索、缓存、偏好保存与恢复默认 | 同上 |
| 拉伸与暖色预览 | 两次手势、120 帧通过；GPU 窗口尺寸冻结、完整预览与黑色背景、松开后确认最终渲染，六个工具保留 | `test-results/resize-v27/live/observations.json` |
| 视频亮度与夜间模式 | 25 项通过；新进程重开 2 项通过 | `test-results/picture-v27/observations.json`、`observations-reopen.json` |
| 发布资产 | 七种尺寸蓝色播放图标、ZIP CRC、文件名、许可证、隐私排除及三份 SHA256 | `test-results/resize-v27/icons.json`、`package-check.json`；打包后验证 |

GUI 检查串行运行，使用工作区测试媒体及两条同目录视频。拉伸测试需要在 DLL 后传入媒体路径作为命令行参数；其他 UI 测试通过环境变量取得媒体。公开包不包含这些测试视频、配置、播放历史或缓存。编译保留原项目 nullable 警告；NuGet 漏洞数据端点不可用时出现 NU1900，本记录不宣称依赖漏洞扫描已经完成。

## 许可核对范围

保留 mpv.net 的 GPL v2 文本，不擅自将其认定为 GPL v2-or-later 或改为 GPL v3。各组件分别列出其许可，尤其是 NGettext.Wpf 的 LGPL v3。已完成来源和许可文本整理，但没有完成 GPL／LGPL 版本兼容性的对外分发法律核验，也未取得额外重新许可授权。

随包前端源码不是原生 DLL 及所有依赖的完整对应源码。若对外分发完整包，还须补齐实际依赖的对应源码／构建资料并确认许可兼容性。当前二进制产物为本地构建，尚未发布为公开下载；源码仓库已公开，详见 `docs/open-source-notices.md` 与 `docs/third-party.md`。
