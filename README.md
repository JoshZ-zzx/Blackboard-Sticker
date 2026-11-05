# Blackboard Sticker

面向教室电脑的 Windows 桌面课表工具。本 fork 正在将配置改为日历周视图，并支持按日期调课、学校预备铃同步提醒和纯文本小黑板。

已移除天气、ClassIsland 联动及手写墨迹。保留日期、倒数日、启动台和全屏时钟。

使用、迁移、实现选择及验证范围见 [实现说明](docs/IMPLEMENTATION.md)。

## 构建

Windows + Visual Studio Build Tools（.NET 桌面开发、.NET Framework 4.7.2 targeting pack）：

```powershell
msbuild ZongziTEK.BlackboardSticker.sln /restore /p:Configuration=Release /p:Platform="Any CPU"
dotnet run --project tests/ScheduleTests/ScheduleTests.csproj --configuration Release
```

GitHub Actions 自动编译并上传 Blackboard-Sticker-Windows 产物。

原项目：[ZongziTEK-Blackboard-Sticker](https://github.com/ZongziTEK/ZongziTEK-Blackboard-Sticker)。沿用 GPL-3.0 许可证，详见 LICENSE。
