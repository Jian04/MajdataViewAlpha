# Alpha 开发包

`Build-Desktop.ps1` 将 Edit 和 Launcher 发布到 `alpha/Release/Alpha`，使用 .NET 6 Desktop Runtime（本机已安装）。它不会复制旧 View，也不会修改已安装的其他 Majdata 版本。

最终目录：

```text
Release/Alpha/
  MajdataLauncher.exe
  MajdataLauncher.dll、运行配置及依赖文件
  Pets/
  App/
    MajdataEdit/
      MajdataEdit.exe
      MajdataEdit.dll、运行配置及依赖文件
      SFX/、Themes/、Resources/、tools/ …
    MajdataView/
      MajdataView.exe                  ← 当前 alpha Unity 工程构建
      MajdataView_Data/                ← 与 exe 同次构建
      UnityPlayer.dll 等构建输出
```

Unity 工程位于 `alpha`，工程版本见 `ProjectSettings/ProjectVersion.txt`。使用该工程的 Windows 构建配置，将输出选为 `Release/Alpha/App/MajdataView/MajdataView.exe`。必须保留完整 Unity 构建输出，不能只替换 exe 或从旧版借用 View。

Unity 的构建后处理会额外发布自包含的 Edit 和单文件 Launcher，需要 **.NET SDK**，仅安装 Desktop Runtime 不够。脚本依次检查 `MAJDATA_DOTNET` 指定的可执行文件、项目内 `.codex-build/dotnet/dotnet.exe`、`DOTNET_ROOT_X64` / `DOTNET_ROOT`、用户和系统安装目录以及 PATH；每个候选都会验证 SDK 是否可用。其他电脑可安装 .NET 8 SDK，或把 `MAJDATA_DOTNET` 设为 SDK 安装目录内 `dotnet.exe` 的完整路径。

首次发布可能需要 NuGet 下载自包含运行库及 ReadyToRun 工具。脚本会复用项目内现有的 `.codex-build/nuget` 缓存；显式设置的 `NUGET_PACKAGES` 优先。Edit / Launcher 发布失败时，不会再移动本次生成的 Unity Player。

Edit 和 Launcher 在本次开发中已发布到以上目录。View 只完成运行时源码编译检查，尚未通过 Unity 完整构建和双 View 播放/录制验收。不要把当前目录当作已经完成验收的整包。

联机写谱用法见 [COLLABORATION.md](COLLABORATION.md)；多实例用法见 [MULTI_INSTANCE.md](MULTI_INSTANCE.md)。两个功能均需新版 Edit/Launcher，多实例还需当前源码构建的 View。
