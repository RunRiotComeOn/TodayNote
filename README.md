# 今日 · TodayNote

**今天的事，轻一点。**

一张可以钉在桌面最上方的待办便利贴。把今天要做的事写下来，做完一件就划掉一件；完成的留下，没做完的明天接着来。

灰白与炭灰两种外观，Windows / macOS 可用，无需账号，待办保存在自己的电脑上。

[下载 Windows 版](https://github.com/RunRiotComeOn/TodayNote/releases/download/v0.1.0-preview.1/TodayNote_Windows_v1.1.1.zip) · [下载 macOS 版](https://github.com/RunRiotComeOn/TodayNote/releases/download/v0.1.0-preview.1/TodayNote_macOS.zip) · [版本说明](https://github.com/RunRiotComeOn/TodayNote/releases/tag/v0.1.0-preview.1)

<p align="center"><img src="assets/poster-zh.png" alt="今日 TodayNote：今天的事，轻一点。桌面置顶、完成划线、未完延续。" width="660"></p>

> 当前是首次预览版。核心逻辑和界面交互已做检查，Windows / macOS 的系统功能仍需要更多实机反馈，欢迎通过 Issues 报告问题。

## 下载

不需要编译源码，下载对应系统的 ZIP、解压后打开即可。软件包发布在 [Releases](https://github.com/RunRiotComeOn/TodayNote/releases)，文件校验值见 [SHA256SUMS.txt](SHA256SUMS.txt)。

| 系统 | 下载 | 要求 |
| --- | --- | --- |
| Windows | [下载 Windows 版](https://github.com/RunRiotComeOn/TodayNote/releases/download/v0.1.0-preview.1/TodayNote_Windows_v1.1.1.zip) | Windows 10 / 11，.NET Framework 4.8 |
| macOS | [下载 macOS 通用版](https://github.com/RunRiotComeOn/TodayNote/releases/download/v0.1.0-preview.1/TodayNote_macOS.zip) | macOS 11+，支持 Apple 芯片与 Intel |

### Windows

完整解压后双击 `TodayNote.exe`。请把 `TodayNote.exe.config` 与程序放在一起。设置中可以创建桌面快捷方式。

### macOS

解压后，将 `TodayNote.app` 拖入“应用程序”再打开。应用未通过 Apple 开发者公证；若首次被拦截，在“系统设置 → 隐私与安全性”中选择“仍要打开”。不需要关闭系统安全检查。

## 一眼就会用

- **置顶陪伴**：小窗口放在手边，图钉可以随时取消置顶。
- **两种程度，各三档**：紧急程度与重要程度分别选择低、中、高。
- **完成不消失**：勾选后自动划线，今天仍然留在原位，进度也会跟着往前。
- **未完自动延续**：到点只归档已完成项，没完成的继续留着，不重复生成。
- **换日由你定**：默认凌晨 04:00，适合经常工作到午夜之后的人；时间可以在设置中修改。
- **明暗都舒服**：浅色、深色或跟随系统，自动记住选择。
- **简单但够用**：编辑、删除撤销、完成记录、字号、开机启动、备份导出与恢复。

比如：11 月 6 日凌晨 01:00 仍算 11 月 5 日。到凌晨 04:00，完成项进入记录，未完成项自然延续到 11 月 6 日。关机或休眠跨过换日时间，下次打开也会补做更新。

双击文字编辑，右键或省略号菜单中删除；输入内容后按 Enter 添加。更多操作见压缩包内的“使用说明”。

## 本地数据与迁移

| 平台 | 数据位置 |
| --- | --- |
| Windows | `%LOCALAPPDATA%\TodayNote\data.json` |
| macOS | `~/Library/Application Support/TodayNoteMac/data.json` |

修改后自动保存，同时保留上一份 `.bak`。恢复备份前会另存当前内容。

Mac 与 Windows 版使用相同的 JSON 备份格式：在原电脑导出，再到另一台电脑恢复。**目前没有自动云同步。**

## 构建与检查

| 目录 | 内容 |
| --- | --- |
| `windows/` | C# / WPF 源码、Windows 构建脚本与检查代码 |
| `macos/` | AppKit + 系统 WebKit 源码、Mac 构建脚本与检查代码 |
| `assets/` | 中文宣传海报与制作说明 |

Windows：在 Windows PowerShell 中运行 `windows/build.ps1`，输出到 `windows/build/`。

macOS：安装 Xcode Command Line Tools 后，在终端运行 `bash macos/build-macos.sh`，生成 Apple 芯片与 Intel 通用应用。

检查命令：

```sh
# 需要 .NET 8 SDK；验证共享的 C# 核心逻辑
dotnet run --project windows/tests/CoreTests.csproj -c Release

# 需要 Node.js；验证 Mac 版的换日与待办逻辑
node macos/tests/core.test.js

# 浏览器界面检查需要 Playwright
npm install
npx playwright install chromium
npm run test:ui
```

当前检查结果：47 项 C# 核心检查、34 项 JavaScript 核心检查、25 项浏览器界面检查通过。后者使用模拟的本地保存接口，**不等同于 Windows / macOS 系统层的端到端验证**。系统置顶、菜单栏／托盘、开机启动及原生文件对话框仍需实机测试。海报为功能示意，界面以实际版本为准。

## 反馈

遇到问题，请在 Issues 中附上系统版本、软件平台、复现步骤和报错截图。发布截图前请先遮住自己的待办等私人内容。

## 开源许可

源码采用 [MIT License](LICENSE)，可以使用、修改与分发，请保留版权与许可声明。

让每一件完成，都被看见。
