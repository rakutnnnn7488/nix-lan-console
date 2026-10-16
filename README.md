# 局域网桌面控制台

Windows 主机发送硬件状态与游戏进程，副机显示控制台和游戏时长。副机每 5 分钟检查 GitHub 最新 Release，并在无游戏运行时安装更新。

## 构建

在 Windows PowerShell 中运行 `powershell -ExecutionPolicy Bypass -File .\build.ps1`。需要 .NET Framework 4.8 和系统自带 C# 编译器。

## 更新发布

Release 标签使用四段版本号；附加同版本的 `ControlConsole-update-版本.zip`。GitHub 更新包只含程序、更新脚本和版本号，不公开游戏素材。完整本地包由 `package-local.ps1 -GameAssetsDir <本地游戏素材目录>` 生成，内含 `update-source.txt` 和仅供个人使用的本地游戏素材。控制台画面由 WinForms 原生窗口绘制，不需要浏览器或 HTML 服务。

首次安装需要完整的本地安装包。这个仓库不提供游戏提取素材，也不提供完整安装包；请在你自己的合法游戏副本中提取素材并放进 `game-assets`。不要公开分发游戏素材。GitHub 发布包只包含程序更新。

当前源码没有附带开源许可证；仓库公开不代表允许第三方使用或分发。

每次更新先修改 version.txt 和 BuildInfo.Version，再推送 main；GitHub Actions 会构建并发布代码更新包。

1.4.4.0：游戏按完整可执行文件路径匹配，发现 Steam 库与官方终末地程序；历史中断会话会结清，记录界面可纠错。主机和副机均会自动检查更新（需先各安装一次此版本）。

1.4.5.0：同一角色单实例运行；通过主机进程标识判断真正的新启动；副机复用已有 Edge 窗口并清理重复窗口。
v1.4.6.0: Exclude Wallpaper Engine; Start opens settings for game records and updates.

1.4.6.1：Webcam 预览改用本地游戏副本提取的原生房间与侧栏素材。素材未纳入公开源码或公开发布包。`build.ps1 -GameAssetsDir <本地素材目录>` 可用于本地完整构建。

1.4.6.2：按游戏窗口边框切片调整四个窗口的边框和内容区；加入最小化、最大化、关闭、拖动、缩放与任务栏还原。公开 Release 仅含程序更新；本地游戏素材由完整安装包保留。


1.4.6.3：设置页的软件版本选项卡显示更新状态和下载进度。自动更新会报告检查、下载百分比、校验、安装、等待游戏结束与错误状态；修复 GitHub 更新包下载后误将文件复制到自身而导致更新中断的问题。

1.4.6.4：将任务栏按钮改为游戏原生弹起/按下两态素材，按钮状态随窗口最小化和恢复切换；修正开始按钮乱码。

1.4.6.5：修复 GitHub 下载未返回 Content-Length 时进度条停在 0% 的问题；改为不定进度动画并显示已下载的 MB。
