# Desktop Island · 桌面灵动岛

Windows 桌面顶部的小型悬浮工具：听歌看歌词，游戏看帧率，空闲看时间与天气。

**[下载 Windows 版](../../releases/latest)** · [使用指南](docs/GETTING_STARTED.md) · [常见问题](docs/FAQ.md)

适用于 Windows 11 / Windows 10 22H2，64 位 x64。下载包自带运行环境，无需安装 .NET 或申请 API Key。

## 下载使用

1. 在 **[Releases](../../releases/latest)** 下载 `DesktopIsland-1.3.0-win-x64.zip`。
2. **完整解压**到普通文件夹，双击 `Install.cmd` 安装并创建桌面快捷方式。也可以直接运行 `DesktopIsland.exe`。
3. 跟随首次引导选择音乐软件、天气城市和需要的功能。开启 FPS 时接受一次 Windows 授权，之后自动连接。

请下载 Release 中的 `win-x64.zip`；**Code → Download ZIP** 是开发源码。

<details>
<summary>查看首次使用引导</summary>
<p><img src="docs/first-run.png" width="470" alt="首次使用引导：选择音乐软件、天气城市和需要的功能"></p>
</details>

## 四种状态，一眼看懂

默认悬停 **200ms** 展开，移开收起。**Ctrl + Alt + I** 随时展开/收起。

以下为程序界面截图，歌曲、天气与 FPS 均为示例数据。

<table>
<tr>
<td width="50%" valign="top">
<h3>音乐 · 看歌词，随手切歌</h3>
<p>在 QQ 音乐或网易云音乐播放歌曲。收起显示两句歌词，展开可暂停、切换上下首。</p>
<p><img src="docs/screenshots/music-compact.png" width="356" alt="音乐收起状态：显示当前和下一句示例歌词"></p>
<p><img src="docs/screenshots/music.png" width="360" alt="音乐展开状态：示例歌曲、双行歌词、播放暂停和上下首按钮"></p>
</td>
<td width="50%" valign="top">
<h3>天气 · 时间与晴雨随时看</h3>
<p>在“天气 → 管理地点”添加城市，最多保存 5 个。空闲时显示电脑时间、天气图标和最低/最高温。</p>
<p><img src="docs/screenshots/weather-compact.png" width="256" alt="空闲状态：示例时间、多云图标和最低最高温"></p>
<p><img src="docs/screenshots/weather.png" width="360" alt="天气展开状态：北京、上海、广州的示例晴雨图标和温度"></p>
</td>
</tr>
<tr>
<td width="50%" valign="top">
<h3>计时 · 倒计时与秒表</h3>
<p>打开“计时”，选时长后开始；也可切换秒表。收起后继续显示计时，结束时提醒。</p>
<p><img src="docs/screenshots/timer-compact.png" width="180" alt="计时收起状态：计时图标与剩余 12 分 34 秒"></p>
<p><img src="docs/screenshots/timer.png" width="360" alt="计时展开状态：倒计时、秒表切换、预设时长、暂停和重置"></p>
</td>
<td width="50%" valign="top">
<h3>游戏 · 只留一小块帧率</h3>
<p>首次启用 FPS 并完成 Windows 授权后，进入兼容游戏自动切换；默认鼠标穿透。</p>
<p><img src="docs/screenshots/game.png" width="112" alt="游戏状态的小型胶囊，显示 60 FPS 示例值，并非游戏实测"></p>
<p>推荐无边框全屏。未识别的窗口游戏可在设置里添加游戏 EXE。</p>
<p>需要操作时，按 <strong>Ctrl + Alt + I</strong> 展开。</p>
</td>
</tr>
</table>

还支持可选的 Windows 通知、Codex 完成提醒与其他 Agent 的本机通知入口。右键灵动岛或托盘图标进入设置，可调整开机启动、悬停速度、位置和歌词偏移。

## 音乐和游戏兼容性

- **QQ 音乐**：优先使用 Windows 系统媒体会话。请使用官方 Windows 桌面客户端，并先开始播放；部分版本需要开启播放器自己的系统媒体控制选项。
- **网易云音乐**：优先系统媒体会话；当前 3.x 客户端可回退到只读本地播放日志。回退控制使用系统媒体键，建议只运行一个音乐播放器。遇到其他媒体会话时会提示，避免错误控制另一款播放器。
- 在线歌词会按歌曲身份匹配，并保留本地缓存；可以导入 `.lrc`。没有匹配、纯音乐或接口不可用时显示歌名/歌手，不保证每首歌曲都有同步歌词。支持不同播放器版本的程度可能随其更新变化。
- FPS 使用独立进程读取 **DirectX / DXGI Present** 系统事件，不注入 DLL、不修改游戏、不改变游戏优先级。适用于通过 DXGI 提交画面的 Direct3D 游戏。窗口游戏可在设置中选择游戏 EXE；无边框全屏通常可自动识别。
- 使用**无边框全屏或窗口模式**。普通桌面置顶窗口不能保证覆盖独占全屏。Vulkan/OpenGL、云游戏及特殊反作弊环境不保证可用；无数据显示 `-- FPS`。此读数不等同于包含显卡插帧的最终显示帧率。
- 首次启用 FPS 需要当前 Windows 账户具备管理员权限并允许系统授权。标准账户仍可使用音乐、天气、时间和计时。监测存在少量资源开销，不承诺所有游戏零影响。

详细操作见 **[使用指南](docs/GETTING_STARTED.md)**、**[常见问题](docs/FAQ.md)**。解压后的 `使用说明.html` 可离线打开。

## 隐私

没有内置个人 API Key、账号、城市、设备编号或聊天记录。设置和缓存存放在当前用户的 `%LOCALAPPDATA%\DesktopIsland`，不在程序目录或仓库中。

Windows 通知与 Codex 监听默认关闭，需要自行启用。联网歌词发送歌曲信息，天气发送所选城市的坐标；无遥测或日志上传。完整说明见 **[隐私说明](docs/PRIVACY.md)**。

## 从源码构建

需要 Windows、.NET 10 SDK。制作完整下载包还需 Visual Studio / Build Tools 的“使用 C++ 的桌面开发”组件，用于小型快捷方式工具和验证程序。

```powershell
dotnet run --project tests/Island.Tests.csproj -c Release
powershell -ExecutionPolicy Bypass -File scripts/package.ps1
```

输出位于 `release/`。GitHub Actions 会执行构建与行为检查；发布 tag 后生成下载包。运行环境已包含在 Release 包中。

## 许可与参考

项目采用 [MIT](LICENSE)。使用或参考了 [kadrxy/Dynamic-Island](https://github.com/kadrxy/Dynamic-Island)、[netease-cloudmusic-detector](https://github.com/Coooookies/netease-cloudmusic-detector)、[QQMusicMonitor](https://github.com/GotKiCry/QQMusicMonitor) 和 [Microsoft TraceEvent](https://github.com/microsoft/perfview)。天气来自 [Open-Meteo](https://open-meteo.com/)。第三方声明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
