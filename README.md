# FH6Tools

FH6Tools 是面向《Forza Horizon 6》的 Windows 桌面启动器与社区工具管理中心。它用于统一启动游戏、下载和管理工具、查看运行状态，并自动备份本地游戏存档。

> 本项目是非官方社区项目，与 Microsoft、Xbox、Playground Games、Turn 10 Studios 或 Forza 官方无隶属关系。

## 主要功能

- 启动《Forza Horizon 6》，并可让已安装工具随游戏启动。
- 动态更新工具列表，下载、安装、更新和卸载社区工具。
- 为所有托管的 .NET 程序共享同一份 .NET 10 Desktop Runtime 与 ASP.NET Core Runtime。
- 查看工具运行状态，启动或停止工具，并支持前后端工具。
- 自动备份游戏存档，并通过独立窗口查看和恢复历史备份。
- 简体中文默认界面，可在设置中切换为英文。

## 存档备份

FH6Tools 根据检测到的游戏版本使用固定存档路径：

| 游戏版本 | 存档路径 |
| --- | --- |
| Microsoft Store | `%LOCALAPPDATA%\Packages\Microsoft.624F8B84B80_8wekyb3d8bbwe\SystemAppData\wgs` |
| Xbox / Steam | `C:\XboxGames\GameSave\pgs` |

自动备份时机：

- 启动游戏前创建一次备份，保留最近 3 份。
- 检测到游戏退出，并且存档目录连续 60 秒没有变化后创建备份，保留最近 10 份。
- 恢复历史存档前，自动为当前存档创建一份保护备份。

备份保存在：

```text
FH6ToolsData\game-save-backups
```

恢复存档前必须完全退出游戏。Xbox 云存档仍可能在之后覆盖本地存档，恢复操作需要由用户自行确认和处理云同步冲突。

## 运行与发布

开发环境需要：

- Windows 10/11 x64
- .NET 10 SDK

编译：

```powershell
dotnet build .\FH6Tools.slnx -c Release
```

生成包含共享运行时的发布版本：

```powershell
.\scripts\Publish-FH6Tools.ps1
```

发布结果位于：

```text
artifacts\publish\win-x64
```

发布脚本会在应用旁的 `dotnet` 目录中集成 .NET 10 Desktop Runtime 和 ASP.NET Core Runtime。FH6Tools 与由其启动的托管程序共享该运行时。

## 工具列表

项目维护者认可的候选工具记录在 [tools.md](tools.md)。FH6Tools 会在启动时动态更新可用工具清单，并阻止安装或启动违反项目安全策略的条目。

## 数据目录

FH6Tools 默认将运行数据保存在程序旁的 `FH6ToolsData`：

```text
FH6ToolsData\
├─ downloads\
├─ game-save-backups\
├─ tools\
└─ tool-state.json
```

## 许可证

FH6Tools 采用 [GNU General Public License v3.0](LICENSE) 开源。

你可以在 GPLv3 条款下使用、修改和分发本项目。分发修改版本时必须提供对应源代码，并继续采用兼容的 GPLv3 许可证。
