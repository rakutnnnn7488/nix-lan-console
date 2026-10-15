# 局域网桌面控制台

Windows 主机发送硬件状态与游戏进程，副机显示控制台和游戏时长。副机每 5 分钟检查 GitHub 最新 Release，并在无游戏运行时安装更新。

## 构建

在 Windows PowerShell 中运行 `powershell -ExecutionPolicy Bypass -File .\build.ps1`。需要 .NET Framework 4.8 和系统自带 C# 编译器。

## 更新发布

Release 标签使用 `v1.4.3.0` 等四段版本号；附加同版本的 `ControlConsole-update-1.4.3.0.zip`。压缩包根目录包含 `ControlConsole.exe`、`ApplyUpdate.ps1`、`version.txt` 与 `console-game-faithful.html`。副机安装目录中的 `update-source.txt` 填写 `用户名/仓库名`。更新时会保留本地游戏素材与个人数据。

首次安装需要完整的本地安装包。这个仓库不提供游戏提取素材，也不提供完整安装包；请在你自己的合法游戏副本中提取素材并放进 `game-assets`。不要公开分发游戏素材。GitHub 发布包只包含程序更新。

当前源码没有附带开源许可证；仓库公开不代表允许第三方使用或分发。

每次更新先修改 version.txt 和 BuildInfo.Version，再推送 main；GitHub Actions 会构建并发布代码更新包。

