# FrameFlow V27 第三方组件

FrameFlow 基于 mpv.net 源码修改，保留原项目的 GNU GPL v2 许可文本。第三方组件适用各自许可，完整说明与再分发前的核对事项见 `开源声明.md`，原项目许可正文见 `License.txt`。

## 原生组件

V27 保持与此前 V26 及本机原播放器一致的原生 DLL，没有在本次改名中升级播放内核。以下版本由实际库的运行时属性读取，哈希为实际随包文件的 SHA256。

| 文件／组件 | 实际版本 | SHA256／源码线索 |
| --- | --- | --- |
| libmpv-2.dll | mpv v0.41.0-60-g85bf9f4ff | `44af8b7f5ff45cf50db49b9866fe9238e84c4d935122576c4c6f7ce859226e8a` |
| FFmpeg（内核内） | N-122394-g272c273d3 | 源码提交前缀 `272c273d3` |
| MediaInfo.dll | 25.10.0.0 | `b591ac1b2e08f48bce81c67b12f82ddb86d8395c16da9f93e455ed1374dab02f` |

来源与资料：

- mpv.net 原项目：<https://github.com/mpvnet-player/mpv.net>；原发行资源：<https://github.com/mpvnet-player/mpv.net/releases/tag/v7.1.2.0>。
- mpv 对应源码：<https://github.com/mpv-player/mpv/tree/85bf9f4ff>。其 `Copyright` 与 GPL v2 正文随包保存在 `licenses/mpv-Copyright.txt`、`licenses/mpv-GPL-2.txt`。
- FFmpeg 对应源码：<https://github.com/FFmpeg/FFmpeg/tree/272c273d3>。许可说明与 GPL v2 正文为 `licenses/FFmpeg-LICENSE.md`、`licenses/FFmpeg-GPL-2.txt`。实际适用的 GPL／LGPL 及版本条件取决于构建配置与所链接的库，不能将所有 FFmpeg 构建统一认定为同一种许可。
- MediaInfoLib：<https://github.com/MediaArea/MediaInfoLib>，BSD 风格许可与许可选项见 `licenses/MediaInfo-LICENSE.html`。
- 内核记录的构建路径包含 `mpv-winbuild-cmake`；构建系统上游为 <https://github.com/shinchiro/mpv-winbuild-cmake>。此前的构建树及全部依赖并未包含在本前端仓库内。

这些链接用于标识来源，不表示前端源码包已包含上述原生库及其所有依赖的完整对应源码。对外再分发二进制时，仍应核对并满足实际组件的源码和构建资料提供义务，详见 `开源声明.md`。

## 托管依赖与运行库

CommunityToolkit.Mvvm 8.4.0、NGettext 0.6.7、Microsoft.Xaml.Behaviors.Wpf 1.1.135 采用各自 MIT 许可，许可文本位于 `licenses/`。

自包含运行库为 .NET／Windows Desktop 10.0.12，附 Microsoft 许可和第三方声明。源项目内移植的 HandyControl、NGettext.Wpf 代码保留已有声明。自定义插件管理器及其 Jint／Acornima 依赖已在此前版本移除。

- 移植的 **HandyControl**：MIT，Copyright HandyOrg 2018-2026，来源 <https://github.com/HandyOrg/HandyControl>，许可为 `licenses/HandyControl-LICENSE.txt`。
- **NGettext.Wpf**：上游为 LGPL v3，与上面的 MIT NGettext 核心库不同。来源 <https://github.com/robert-j-engdahl/ngettext-wpf>；上游包署名 Copyright 2017, 2018, 2019 Accuratech ApS，出处 <https://www.nuget.org/packages/NGettext.Wpf/1.2.4>。本仓库内的移植源码位于 `src/NGettext.Wpf/`，LGPL v3 与引用的 GPL v3 正文均随包提供。对外分发前须核对与主项目 GPL 版本的兼容条件，不能仅凭此清单认定整包许可兼容性已经全部解决。
