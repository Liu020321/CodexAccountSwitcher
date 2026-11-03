# Codex Account Switcher

Windows 11 风格的 Codex 本地账户切换与 CLI 启动工具，绿色免安装。

## 使用

在 Releases 下载便携版 ZIP，解压后运行 CodexSettings.exe。需要 Windows 11 x64 / .NET Framework 4.8。Codex CLI 请自行安装。

- 以完整配置目录迁移切换桌面账户，提供中转、校验、恢复记录与撤销。
- CLI 使用与桌面不同的账户，为新终端设置临时 CODEX_HOME。
- 托盘单击查看额度，双击打开设置，右键退出。
- 额度仅在打开面板时查询，三分钟缓存；图标按最近结果更新，无后台额度轮询。

详细步骤见 [使用说明](使用说明.md)。请分享原始发行 ZIP，不要分享使用后的账户目录或设置、日志。

## 构建与验证

在 Windows PowerShell 中运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
.\CodexSettings.exe --self-test
.\CodexSettings.exe --ui-test
```

默认构建便携版。测试使用临时虚构账户；UI 测试会打开测试窗口。运行账户切换前请退出使用相应配置的程序。遇到占用或校验失败应停止处理，不要强制删除源目录。

## 来源与许可

本项目代码由作者借助 AI 编程工具开发，项目维护者为 Liu020321。界面设计参考 Microsoft PowerToys / Windows 11 Fluent；并非微软或 OpenAI 官方产品。图标由项目代码绘制，系统字体不随软件分发。来源及第三方边界见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

本次暂未授予源码开源许可证，公开供查看；复制、修改或再分发源码请先联系维护者确定授权。作者提供原始便携版供下载分享。

## 安全与隐私

发行包不包含作者账户、登录凭证、聊天记录或个人日志。运行后会读取使用者本机认证配置，生成的配置和日志可能包含本机路径，请勿公开。

程序默认普通权限运行；结束进程或提权由用户主动触发。没有设计遥测上传、远程控制及网络监听功能；额度查询通过独立安装的官方 Codex CLI 完成。软件未经独立完整安全审计，也未进行代码签名，不能保证零漏洞或绝对安全。
