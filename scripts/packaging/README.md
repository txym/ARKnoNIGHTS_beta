# 发布包生成

生成发布包前必须关闭以同一项目路径运行的 Unity Editor。

## Android APK

当前项目未配置发布密钥，因此该命令生成可安装、使用 Android debug
证书签名的 APK。发布到应用商店前应另行配置受保护的正式签名流程。

```powershell
$env:ARKNIGHTS_ANDROID_APK_OUTPUT = `
  'G:\ARKnoNIGHTS_beta\Artifacts\Release\Android\ARKnoNIGHTS-0.1-debug.apk'

& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode `
  -accept-apiupdate `
  -buildTarget Android `
  -projectPath 'G:\ARKnoNIGHTS_beta' `
  -executeMethod Task006StandaloneBuild.BuildAndroidApk `
  -logFile 'G:\ARKnoNIGHTS_beta\Logs\android-apk-build.log'
```

构建入口强制关闭 App Bundle，输出单个 APK，并在 APK 同目录写入
`android-build-summary.txt`。

## Windows x64 安装包

先生成 Windows x64 独立程序：

```powershell
$env:ARKNIGHTS_BUILD_OUTPUT = `
  'G:\ARKnoNIGHTS_beta\Artifacts\Release\Windows\ARKnoNIGHTS\ARKnoNIGHTS.exe'

& 'D:\2022.3.62f1c1\Editor\Unity.exe' `
  -batchmode `
  -accept-apiupdate `
  -buildTarget StandaloneWindows64 `
  -projectPath 'G:\ARKnoNIGHTS_beta' `
  -executeMethod Task006StandaloneBuild.BuildWindowsX64 `
  -logFile 'G:\ARKnoNIGHTS_beta\Logs\pc-release-build.log'
```

再使用 Windows 自带 IExpress 生成便携 ZIP 和当前用户安装程序：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\packaging\Build-PCInstaller.ps1 `
  -BuildDirectory .\Artifacts\Release\Windows\ARKnoNIGHTS `
  -OutputDirectory .\Artifacts\Release\PC
```

输出包括：

- `ARKnoNIGHTS-Windows-x64.zip`
- `ARKnoNIGHTS-Setup-x64.exe`
- `pc-installer-summary.txt`

安装程序默认安装到
`%LOCALAPPDATA%\Programs\ARKnoNIGHTS`，不请求管理员权限，并创建桌面与
开始菜单快捷方式。安装程序当前没有代码签名，Windows 可能显示
SmartScreen 提示。
