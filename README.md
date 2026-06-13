# FH6Tools

FH6Tools 是面向《Forza Horizon 6》的 Windows 桌面启动器与社区工具管理中心。它用于统一启动游戏、下载和管理工具、查看运行状态，并自动备份本地游戏存档。

> 本项目是非官方社区项目，与 Microsoft、Xbox、Playground Games、Turn 10 Studios 或 Forza 官方无隶属关系。

## 主要功能

- 启动《Forza Horizon 6》，并可让已安装工具随游戏启动。
![alt text](image-1.png)
- 从独立远程元数据清单新增和更新工具，下载、安装、更新和卸载社区工具。
![alt text](image.png)
- 自动备份游戏存档，并通过独立窗口查看和恢复历史备份。
![alt text](image-2.png)

## 提供的工具

FH6Tools 当前远程工具清单提供以下 9 个社区工具。软件启动时会动态更新工具列表，并检查工具版本与下载状态。
注意，工具下载需要有效的Github连接。

| 工具 | 简介 | 项目地址 |
| --- | --- | --- |
| OmniMix VB.NET Frontend | 可以自定义的《极限竞速：地平线 6》音乐电台，支持网易云音乐、QQ 音乐和哔哩哔哩歌曲导入。 | [Dr-hydra/OmniMix-VBNet-Frontend](https://github.com/Dr-hydra/OmniMix-VBNet-Frontend) |
| FH6 Auction House Sniper | 用于监控《极限竞速：地平线 6》拍卖行的工具。 | [Dr-hydra/FH6-Auction-House-Sniper](https://github.com/Dr-hydra/FH6-Auction-House-Sniper) |
| FH Language Combo Tool | 用于调整《极限竞速》系列游戏语言组合的工具。 | [Dr-hydra/FH-Language-Combo-Tool](https://github.com/Dr-hydra/FH-Language-Combo-Tool) |
| FH6Farm | 用于获取超级抽奖的辅助工具。 | [Dr-hydra/FH6Farm](https://github.com/Dr-hydra/FH6Farm) |
| FH6 Road Scanner | 通过逐行扫描地图帮助定位遗漏道路。 | [Dr-hydra/FH6-Road-Scanner](https://github.com/Dr-hydra/FH6-Road-Scanner) |
| Virtual TCU | 虚拟变速箱控制单元，提供浏览器仪表盘及本地 WebSocket/HTTP 服务。 | [Forza-Love/fh6-virtual_tcu](https://github.com/Forza-Love/fh6-virtual_tcu) |
| HorizonHaptics | 通过 FH6 Data Out 遥测为 DualSense 控制器提供自适应扳机与触觉反馈。 | [haritha99ch/HorizonHaptics](https://github.com/haritha99ch/HorizonHaptics) |
| Forza Painter FH6 | 将图片转换为《极限竞速：地平线 6》涂装绘制数据，并提供可配置的几何参数。 | [bvzrays/forza-painter-fh6](https://github.com/bvzrays/forza-painter-fh6) |
| FH6 Adjust Tool | 提供车辆物理调校计算、方案管理和 AI 辅助连续调优。 | [Dr-hydra/FH6-Adjust-Tool](https://github.com/Dr-hydra/FH6-Adjust-Tool) |

## 存档备份

FH6Tools 根据检测到的游戏版本确定存档路径：

| 游戏版本 | 存档路径 |
| --- | --- |
| Xbox | `<游戏目录的父目录>\GameSave\pgs` |
| Steam | `C:\XboxGames\GameSave\pgs` |

Xbox 版和 Microsoft Store 版统一按 Xbox 版处理。游戏安装目录按实际安装盘符识别，例如：

```text
C:\Games\Forza Horizon 6
C:\Games\GameSave\pgs
```

如果自动识别结果不正确，可以在“游戏与数据”设置中单独指定存档目录；该设置优先于自动识别，并且可以随时恢复自动识别。
已知简单拷贝本地存档仍会被云端存档覆盖，遇到问题的暂时可以先前往b站寻找解决方案，保留好备份存档。

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

发布脚本会生成：

```text
artifacts\publish\win-x64\
artifacts\publish\win-x64-update\
artifacts\publish\packages\FH6Tools-<版本>-win-x64-with-runtime.zip
artifacts\publish\packages\FH6Tools-<版本>-win-x64-update.zip
```

`with-runtime` 包用于首次安装，包含共享 .NET 运行库。`update` 包不包含 `dotnet` 目录，仅用于覆盖已有安装。旧版本更新器会继续选择 `with-runtime` 包；2.0.2 及之后版本优先选择体积更小的 `update` 包。

## 工具列表

候选工具来源记录在 [tools.md](tools.md)。独立远程清单可以新增工具，并同步项目地址、名称和中英简介。远程新增工具仅接受 GitHub 项目地址，并使用固定的普通权限、普通风险、单进程启动默认值；现有工具的下载方式、启动方式、管理员要求与风险等级仍由软件内置清单控制。FH6Tools 也允许用户添加本地工具。
想要添加工具欢迎提交issue。或者你可以自行导入。

## 交流与反馈

QQ 交流群：`851586605`

## 升级说明

v2.1.0 将 FH6 Adjust Tool 写入内置可信工具清单，支持从其 GitHub Release 下载 framework-dependent 版本、直接启动调校工具，并备份已保存的车辆调校方案。AI 服务凭据不会包含在 FH6Tools 配置快照中。

v2.0.2 优化软件自动更新的临时文件清理：更新完成后会删除下载包、解压目录和更新脚本；从旧版本直接更新时，新版本首次启动也会自动清理旧更新器遗留的 `FH6ToolsData\app-update` 目录；检查到新版本时会展示 GitHub Release 中填写的更新内容。

v2.0.1 修复 Xbox 版启动后无法自动备份游戏存档的问题；修复日志文件无法正常记录的问题；内置 2026-06-11 春季赛攻略配置，并继续通过远程元数据同步给旧版本。

v1.3.1 在“游戏与数据”设置中新增手动版本兜底，自动检测失败时可以手动选择 Xbox 或 Steam，并可绑定已安装路径。

v1.3.0 将 Microsoft Store 版本合并为 Xbox 版处理，Xbox 版按各盘符下的 `XboxGames\Forza Horizon 6` 安装目录识别；FH6Tools 现在需要管理员权限启动，并继续使用 `C:\XboxGames\GameSave\pgs` 作为存档备份路径。

## 数据目录

FH6Tools 默认将运行数据保存在 `FH6ToolsData`：

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

## 与 PCL 的关系说明

FH6Tools 的部分 UI 设计与 UI 基础代码来源于或参考了 [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL) 及其衍生项目，并根据相应开源许可证进行使用。

除相关 UI 部分外，FH6Tools 的业务功能、工具管理、下载、游戏启动及存档备份等功能均为独立实现。FH6Tools 与 PCL 官方及其维护者不存在从属、合作、官方授权或维护关系。

PCL 项目及其名称、代码和相关权利归原作者及贡献者所有。
