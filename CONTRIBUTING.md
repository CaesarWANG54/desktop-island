# 开发与验证

使用 Windows x64 和 .NET 10 SDK。执行 `dotnet run --project tests/Island.Tests.csproj -c Release` 运行离线行为检查。

制作完整发行包还需要 Visual Studio Build Tools 的 C++ 桌面开发组件。运行 `scripts/package.ps1`，产物在 `release/`。不把该目录直接加入 Git，仅通过 Release 上传程序 ZIP 和校验值。

测试不会要求登录音乐账号或运行游戏。真实播放器、系统授权和不同 GPU 的兼容检查需要在对应机器上完成；不要将某一次测试描述为所有游戏兼容。

只提交源代码、合成测试数据及必要文档。不提交设置、日志、歌词缓存、用户 SID、个人路径、诊断截图、API 密钥或签名证书。开发诊断 `--diagnostics` 仅供本机使用。

提交前执行 `scripts/check-privacy.ps1`。发布依赖的运行环境与授权组件更新时，应重新测试首次引导、安装、普通权限启动、FPS 恢复、卸载路径和音乐控制。
