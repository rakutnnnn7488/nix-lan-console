# Nix LAN Console

面向个人双机环境的 Windows 原生桌面控制台。主机采集硬件状态与游戏进程；副机通过 TCP 主动连接主机，并用 WinForms 窗口展示状态、角色动画和游戏记录。控制台不依赖浏览器。

## 当前源码

源码版本见 `version.txt` 和 `src/ControlConsole.cs` 的 `BuildInfo.Version`，两处必须一致。当前源码为 **1.7.4.0**，含 Folia 原生窗口嵌入和后台传感器采样优化；源码版本不代表远端已发布该版本。

## 安装布局

```
<安装目录>\
  ControlConsole.exe            启动器（稳定，不随版本更新）
  current.txt                   当前版本，例如 1.7.0.0
  previous.txt                  上一版（首次安装时可能不存在）
  version.txt                   当前版本（信息展示）
  update-source.txt             GitHub 仓库 owner/repo
  versions\<版本>\ControlConsole.exe   真正的应用
  versions\<版本>\.complete           安装完成标记
  game-assets\                  本地游戏素材，所有版本共用
```

## 构建

环境：Windows PowerShell、.NET Framework 4.8 与系统 C# 编译器。

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

输出 `build\ControlConsole.exe`（启动器）、`build\versions\<版本>\ControlConsole.exe`（应用）和 `current.txt`。制作含角色与界面素材的完整包：

```powershell
powershell -ExecutionPolicy Bypass -File .\package-local.ps1 -GameAssetsDir 'D:\你的本地素材目录'
```

公开仓库和公开代码更新 ZIP **不包含**从游戏中提取的角色、字体及界面素材。不要上传个人完整包或素材目录。仓库目前没有附带开源许可证。

## 运行与连接

- 两台电脑运行同一程序，分别选择“主机”和“副机”；主、副机均有托盘图标。
- 主机监听 **TCP 46702**。首次配置可在主机管理员 PowerShell 运行 `OpenHostTcpPort.ps1`。副机从“开始 → 设置 → 软件版本”填写主机当前 IPv4 地址。
- 副机显示主机 CPU、内存、GPU、可读取的温度、最近一分钟曲线，以及按游戏进程记录的时长。主机地址变化后应更新副机设置。
- 副机可在“开始 → 设置 → Folia”选择本机 `Folia.exe`；桌面 Folia 图标会启动播放器并把 Folia 原生窗口嵌入可拖动的控制台卡片。关闭控制台时会还原 Folia 窗口。Folia 必须安装在使用此功能的电脑上。
- 程序数据保存在 `%LOCALAPPDATA%\NixLanConsole`。升级前请先从托盘退出旧进程。

## 更新与发布

- 更新由应用自己完成：下载并校验 GitHub Release 的 `ControlConsole-update-<版本>.zip`，只解压 `versions\<版本>\`，写 `.complete` 和 `current.txt`/`previous.txt`，再交给启动器重启。
- 启动器等待旧进程退出，启动新版并检查 `startup-ready.txt`；失败自动回滚 `previous.txt`，删除失败版本并写 `failed-update.txt`（24 小时内不再自动重试）。
- 不再使用 `Updater.exe` / `ApplyUpdate.ps1`，也不再整目录复制或改名。代码更新包只含程序，不含游戏素材。
- **迁移顺序：**先用本地完整包装好 1.7.0.0（两台机器各一次），确认后再发布 1.7.1.0（新格式）。1.7.0.0 本身不要上传 GitHub，否则 1.6.x 的旧更新器会失败。
- 发布前应同步 `version.txt` 与 `BuildInfo.Version`，运行 `Updater` 时代的隔离测试已由新流程取代：构建、`build-installer.ps1 --verify`、`--test-install`/`--test-rollback`、以及本机启动器/更新模拟。

历史设计过程见 `NATIVE-UI-MIGRATION.md`；本地项目的当前任务和验收标准见工作区根目录的 `工作日志.md`（最新事实来源）。
