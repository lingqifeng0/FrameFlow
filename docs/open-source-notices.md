# FrameFlow 开源声明

FrameFlow V27 是基于 **mpv.net** 源码修改的 Windows 视频播放器，保留原项目随附的 **GNU General Public License，Version 2（GNU GPL v2）** 许可文件和声明。第三方组件分别遵循各自许可，不能将整个二进制包一概认定为仅适用 GPL v2。FrameFlow 是独立修改版，使用自己的名称和蓝色播放图标，并非 mpv.net 原项目官方发行，也不代表原作者为此修改版提供支持或背书。

原项目：<https://github.com/mpvnet-player/mpv.net>。本仓库所采用的上游代码基线为 `ef45bae`。原作者为 stax76 及 mpv.net 项目贡献者；播放内核还包含 mpv、MPlayer、FFmpeg 等项目的工作。

保留原有署名：Copyright (C) 2000-2024 mpv.net/mpv/mplayer。源码文件内已有的作者、版权、许可证及第三方声明继续保留。软件界面更名不改变这些权利和声明。

## 本版修改记录

修改日期：2026-10-18。当前发行名为 FrameFlow V27，Windows 文件版本为 `7.1.2.1027`。

本修改版包含新的播放器界面、工具栏、设置、快捷键与鼠标操作、倍速与长按导航、播放记录、多窗口、截图、亮度与夜间暖色等功能。本版统一使用 FrameFlow 名称，修复最大化／全屏的窗口及标题栏圆角露底问题，并移除 Esc 直接退出播放器的默认绑定，保留已有菜单、面板和对话框的取消行为。

## 许可、源码与分发

- GPL v2 完整英文许可在发布目录的 `License.txt`；本说明是辅助说明，不能替代许可正文。
- 允许在 GPL v2 的条件下使用、复制、修改和再分发。再分发修改版时，应保留版权、许可及无担保声明，标明修改内容与日期，并履行相应源码提供义务。
- 本版前端及仓库内代码的对应源码包为 **FrameFlow-V27-source.zip**，包括本次修改、项目文件和构建脚本。它与 **FrameFlow-V27-win-x64.zip** 一同提供。构建方法见源码中的 `README.md` 和 `scripts/bootstrap.ps1`、`scripts/build-portable.ps1`。
- 上游仓库链接不能代替本修改版的对应源码。对外再分发时，应同时提供对应源码，或者采用 GPL v2 允许的其他源码提供方式。
- 单独的前端源码包不等于所附原生 DLL 及其所有依赖的完整对应源码。再分发包含这些 DLL 的完整软件包时，还须按各组件实际构建所适用的许可，提供所需的完整对应源码、构建资料和声明；其版本、哈希及来源线索见 `第三方组件.md`。
- 本软件按现状提供，在适用法律允许的范围内不提供任何担保，详见 GPL v2 第 11、12 节。

## 第三方组件

FrameFlow 的名称不替代第三方组件的名称与许可证。随包的 `libmpv-2.dll`、FFmpeg、MediaInfo、.NET、NGettext、CommunityToolkit.Mvvm、Microsoft.Xaml.Behaviors.Wpf 等组件分别保留各自许可和版权。

特别区分 **NGettext（MIT）** 与源码目录中的 **NGettext.Wpf（上游 LGPL v3）**。后者的对应源码随本仓库提供；LGPL v3 正文与其引用的 GPL v3 正文分别随包保存在 `licenses/NGettext.Wpf-LICENSE.txt`、`licenses/GPL-3.txt`。移植的 HandyControl 部分保留 MIT 许可，正文见 `licenses/HandyControl-LICENSE.txt`。

本次完成来源、已识别组件许可及本地构建资料的整理，没有取得额外的重新许可授权。源码仓库已公开，二进制软件包尚未发布为公开下载；公开源码不代表完整软件包的分发条件核验已经完成。若准备对外分发完整软件包，除原生库的完整对应源码外，还需核对上游 GPL v2 声明与 LGPL v3 等组件的版本兼容条件；保留两份许可证本身不能解决潜在的兼容性问题。本声明不将原项目的 GPL v2 自动解释为 GPL v2-or-later，也不擅自将原项目代码重新许可为 GPL v3。

具体组件、对应版本、来源和许可文件见 `第三方组件.md` 与 `licenses/`。mpv 的 GPL／LGPL 适用性取决于实际构建，不能仅凭 DLL 名称认定为 LGPL 构建。

## 兼容性名称

为兼容既有设置、脚本、字幕和外部调用，源码命名空间及部分底层命令仍使用历史技术标识；这些不是对外软件名称。新程序名为 `FrameFlow.exe`，新前端配置为 `frameflow.conf`，旧 `mpvnet.conf` 在首次读取时复制迁移并保留原文件。
