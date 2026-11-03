# 来源及第三方声明

维护者：Liu020321。代码由作者借助 AI 编程工具开发。本次对待发布文件检查，未发现从其他项目复制的代码块或第三方包；这并不构成对全部代码来源或权利的法律保证。如发现遗漏，请联系维护者补充或移除。

- Microsoft PowerToys（Microsoft Corporation 及项目贡献者）：界面布局与交互设计参考。https://github.com/microsoft/PowerToys 。本发行包未包含其源文件、截图、商标图标或其他品牌素材；不据此声明其源码的作者身份。
- Windows / .NET Framework / WPF：使用系统 API 和框架，框架由用户系统提供。Microsoft YaHei、Segoe Fluent Icons / Segoe MDL2 Assets、Cascadia Code 等字体从本机调用，不附带字体文件。
- OpenAI Codex：独立安装的外部程序，本项目调用其命令及额度查询能力，不包含 Codex CLI 二进制、OpenAI 图标或用户凭证。https://github.com/openai/codex 。Codex 名称及相关商标归其权利人所有。
- app.ico 为本项目 QuotaIcon.cs 绘制的终端圆环图标，不是第三方品牌图标。

以上第三方项目和产品各自保留其版权、商标与许可证权利。本项目不代表其官方背书。

## PowerToys File Locksmith 可选集成

软件可调用用户单独安装的 Microsoft PowerToys File Locksmith 官方界面。PowerToys 由 Microsoft Corporation 及其贡献者开发，项目采用 MIT 许可证。
本项目自行编写调用适配器，根据官方 `FileLocksmithLib/IPC.cpp` 和 `Constants.h` 的路径列表协议传递待检查路径；不附带、复制或改写 PowerToys 程序、源文件或图标。
官方来源：https://github.com/microsoft/PowerToys/tree/main/src/modules/FileLocksmith
使用说明：https://learn.microsoft.com/windows/powertoys/file-locksmith

## EXE 安装器

安装器使用 Inno Setup 编译。Inno Setup：Copyright © 1997–2026 Jordan Russell；Portions Copyright © 2000–2026 Martijn Laan。项目及许可：https://github.com/jrsoftware/issrc ，官网：https://jrsoftware.org/ 。安装包包含其安装/卸载运行引擎，不附带编译器；第三方引擎保留原版权及许可证。
