# Nix LAN Console

面向个人双机环境的 Windows 原生桌面控制台。主机采集硬件状态与游戏进程；副机通过 TCP 主动连接主机，并用 WinForms 窗口展示状态、角色动画和游戏记录。界面不依赖浏览器。

## 当前源码

源码版本见 `version.txt` 和 `src/ControlConsole.cs` 的 `BuildInfo.Version`，目前为 **1.6.2.2 本地候选版**。本地源码版本不代表 GitHub 主分支或 Release 已发布该版本。

## 构建

环境：Windows PowerShell、.NET Framework 4.8 与系统 C# 编译器。

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

如需在个人电脑上制作含角色与界面素材的完整包，使用本地游戏素材目录：

```powershell
powershell -ExecutionPolicy Bypass -File .\package-local.ps1 -GameAssetsDir 'D:\你的本地素材目录'
```

公开仓库和公开代码更新 ZIP **不包含**从游戏中提取的角色、字体及界面素材。不要上传个人完整包或素材目录。仓库目前没有附带开源许可证。

## 运行与连接

- 两台电脑运行同一程序，分别选择“主机”和“副机”；主、副机均有托盘图标。
- 主机监听 **TCP 46702**。首次配置可在主机管理员 PowerShell 运行 `OpenHostTcpPort.ps1`。副机从“开始 → 设置 → 软件版本”填写主机当前 IPv4 地址。
- 副机显示主机 CPU、内存、GPU、可读取的温度、最近一分钟曲线，以及按游戏进程记录的时长。主机地址变化后应更新副机设置。
- 程序数据保存在 `%LOCALAPPDATA%\NixLanConsole`。升级前请先从托盘退出旧进程。

## 更新与发布

`build.ps1` 构建程序；`package-local.ps1` 制作个人完整包；`.github/workflows/release.yml` 负责公开代码更新包。代码更新包只适合已有完整素材的安装目录，不能代替首次完整安装。更新器设计包含进度显示、备份、启动检查和失败回滚；真实副机升级须单独验证。

发布前应同步 `version.txt` 与 `BuildInfo.Version`，检查构建结果和素材排除规则，再处理 GitHub 分支与 Release。历史设计过程见 `NATIVE-UI-MIGRATION.md`；本地项目的当前任务和验收标准见工作区根目录的 `项目总览与任务计划.md`。
