# FrameFlow 视频播放器

FrameFlow 是基于 mpv.net 修改的 Windows x64 桌面播放器，当前版本 **V27**，文件版本 `7.1.2.1027`。程序、窗口、任务栏、关于界面及发布包统一使用 FrameFlow 名称；蓝色播放图标和 `#0096FF` 工具栏继续使用。

源码仓库：[lingqifeng0/FrameFlow](https://github.com/lingqifeng0/FrameFlow)。当前作为私有源码备份，保存程序源码、构建脚本、测试和许可说明；个人配置、播放历史、测试媒体和构建产物不纳入 Git。公开分发的许可核对事项见下方开源声明。

## 本版变化

- 普通窗口保留圆角；最大化和全屏时，外层窗口、蓝色标题栏和系统合成器均采用直角，防止四角露出后方背景；恢复普通窗口后恢复圆角。
- Esc 默认不关闭播放器，保留菜单、音量／倍速／画面面板、设置搜索与对话框已有的关闭和取消行为。旧输入配置中的直接 Esc 退出绑定会在加载时停用；全局恢复默认也不会重新启用此行为。本版没有新增 Esc 退出全屏。
- 重命名为 FrameFlow，包含 `FrameFlow.exe`、程序元数据、关于界面、文件关联显示名称、设置帮助及发布包。旧前端配置会复制迁移为 `frameflow.conf`；历史脚本命令和命名空间保留兼容。
- 关于界面及发布包明确说明 mpv.net 来源、GNU GPL v2、修改日期、原作者、对应前端源码包与第三方组件许可。

现有功能包括精确倍速与记忆、独立长按快进、方向键轻点／长按导航、逐帧操作、可编辑工具栏、播放记录、多窗口、截图预览、视频亮度与夜间暖色，以及统一的快捷键与鼠标设置。

## 运行与升级

解压 **FrameFlow-V27-win-x64.zip**，运行 `FrameFlow/FrameFlow.exe`。DLL、`portable_config`、许可文本和说明文件需与 EXE 一起保留。

旧版升级时，先关闭旧播放器，将旧 `portable_config` 整个目录复制到新 FrameFlow 目录并保留备份。首次读取把 `mpvnet.conf` 复制为 `frameflow.conf`，不删除原文件；已有 `frameflow.conf` 时优先使用它。原有偏好、播放记录、书签及脚本格式继续兼容。新包不附会遮住旧配置的前端默认模板；新用户的前端默认值由代码提供。旧桌面快捷方式仍指向原程序，使用新目录的 FrameFlow 程序重新创建快捷方式即可。

详细说明见 [使用说明](docs/modern-player.md)，本版验证见 [V27 检查记录](docs/resize-audit-V27.md)。

## 开源来源与许可

FrameFlow 基于 [mpv.net](https://github.com/mpvnet-player/mpv.net) 修改，原作者为 stax76 及项目贡献者。保留原项目的 **GNU General Public License Version 2（GNU GPL v2）** 许可文件与声明，第三方组件分别适用各自许可；对外分发前仍须核对 GPL／LGPL 版本兼容性。FrameFlow 并非原项目官方发行。

许可正文为 [License.txt](License.txt)，修改说明、无担保声明及源码提供要求见 [开源声明](docs/open-source-notices.md)，实际随包原生库版本、哈希及各组件许可见 [第三方组件](docs/third-party.md)。发布同时提供 **FrameFlow-V27-source.zip**；此包覆盖本前端和仓库内代码，原生 DLL 的完整对应源码义务另见声明。

## 本地构建

```powershell
./scripts/bootstrap.ps1
./scripts/build-portable.ps1
```

构建生成 `dist/FrameFlow/FrameFlow.exe`。源码需要 .NET 10 SDK；引导脚本准备本地工具链和原生依赖，NuGet 从项目文件恢复托管依赖。版本由 `ModernVersion` 管理，沿用编号跳过含 4 的约定。

打包：

```powershell
python ./scripts/package.py
```

打包同时生成便携包、前端源码包及 SHA256 文件，并排除私有配置、播放历史和渲染缓存。
