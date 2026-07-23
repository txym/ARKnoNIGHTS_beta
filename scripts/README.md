# Unity 测试启动器

`Invoke-UnityTests.ps1` 只管理它自己启动的 Unity 进程：先确认 NUnit XML 已完整写入且测试数大于 0，再给 Unity 一段正常退出时间；仅在超时后才结束该子进程。

项目当前 Unity China Editor 会在测试完成后对 `public-cdn.cloud.unitychina.cn/config/production` 的在线配置请求超时，并留下 `Leftover web requests after shut down`。因此测试 XML 已写出但 Unity 仍驻留时，不能把该进程当作仍在执行测试，也不能在 XML 缺失或为零测试时宣称通过。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-UnityTests.ps1 `
  -UnityPath 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -TestPlatform EditMode `
  -NoGraphics
```

`-ExecutionPolicy Bypass` 仅作用于这个启动的 PowerShell 进程，不会更改用户或机器的执行策略。

可选 `-TestFilter '<完整 NUnit fixture 或测试名>'`。每轮输出写到 `Temp/UnityTests/<UTC 时间戳>/`，包含 XML、Unity 日志和 `summary.txt`；脚本返回非零退出码代表未验证或测试失败。
