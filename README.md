# MajdataViewAlpha

> 基于 [MajdataView / MajdataEdit 4.4.0](https://github.com/LingFeng-bbben/MajdataView) 修改与扩展。
> 感谢原作者 bbben（GitHub：LingFeng-bbben）及原项目所有贡献者。

MajdataViewAlpha 是面向 maimai / 舞萌谱面制作、预览与视频导出的工具，在原版 4.4.0 基础上增加了 Alpha 扩展语法、编辑辅助、动态显示、画面特效、媒体时间线、录制导出和桌宠启动器。

项目地址：<https://github.com/Jian04/MajdataViewAlpha>

## v0.5.3 更新（相对 v0.4.2）

### Touch Slide 与 SlideCode

- 对照 AstroDX / SimaiSharp 重新核对 Touch Slide 的位置解析、`<`、`>`、`^`、`v`、`V`、`p/q` 与 D/E 区连接方向，修正同点弧线、端点切线和路径采样。
- 新增 Touch Slide 的多段路径、无头、Break 头、TouchHold 与 A/B/C/D/E 区互连支持，并将 Touch Slide 纳入语法错误检查、补全和语法帮助。
- 迁移 SlideCode 的节点、内外圈、切线、轨道转移、终点判定路由和终点特效位置；保留 Alpha 的 Unity 绘制与判定适配。
- 增加 SlideCode / Touch Slide 几何与判定回归测试，覆盖正常路径、边界路径和大量非法输入，避免解析异常直接阻塞播放。

### 编辑器与性能

- 将 Alpha 命令提示、语法着色、错误定位、格式刷、音符流合并、节奏型检索和媒体时间线逻辑整理为可独立维护的模块。
- 对长谱面播放、Slide / Touch Slide 路径、判定区查找和波形相关更新减少重复分配与重复计算，降低首次播放和密集谱面卡顿风险。
- 更新中英文日文语法帮助，补充 `BOUNCE`、`FAKE`、`DESTROY`、`COLORV`、`SIZEV`、`ALPHAV`、Touch Slide、SlideCode 与音符流示例。
- 保留与 4.4.0 播放、暂停、拖动、音效和媒体时间线的兼容处理，并加强异常音符和不完整输入的容错。

### 兼容边界

- Touch Slide 当前重点覆盖 Alpha 已支持的基础路径和 D/E 区扩展；AstroDX 中的 `s/z/w` 特殊路径、逐段 `#` 延迟以及部分逐段时长语义仍不宣称完全兼容。
- SlideCode 的路径几何、判定转移表和终点位置已按 MajdataPlay 对齐，但 Unity 侧的箭头采样和生命周期仍由 Alpha 自己管理。

## v0.4.2 更新（相对 v0.4.0）

### Edit 与媒体时间线

- 编辑器设置新增编辑区字号与播放器字体，可分别调整谱面文本和 View 判定信息；播放器仍保留原版字体选项，新增编辑器字体大小可调整。
- 修复编辑器宽度变化时波形图视野不同步、光标落在下一时间格以及暂停后媒体丢失等问题。
- 完善双视频轨、双音频轨媒体时间线：点击仅选中片段，拖动时才应用拍线吸附；支持切割、删除、撤销、续编和与主波形同步预览。
- 媒体导出支持手动选择分辨率、帧率与码率，也可使用智能策略优先保持 30 FPS，并逐步调整分辨率和码率至 20 MB 以内；音频可选是否转换为 44100 Hz。
- 新增可重叠音符流 `@{分拍}...` 与跨行写法 `@* ... *@`；编辑区右键可将音符流按精确时间合并回主谱。
- 补全 Alpha 命令提示和语法帮助，新增 `BOUNCE`、Touch Slide、Break Touch / TouchHold 的签名、参数和示例。

### Alpha 语法与音符

- 新增 `BOUNCE`：让 Tap、Star、Each 与 Hold 从判定线运动到指定出生半径后回落；支持秒数、拍长、按音符类型设置和 `NULL` 恢复。
- `SV`、`HS`、`SPAWN`、`SIZE` 等命令支持按音符类型单独设置；Slide 轨迹使用 `SV*slide` 控制运动，`HS*slide` 仅覆盖运动星渐入速度，视觉命令可用 `star`、`slidestar`、`slide` 分别控制星形头、运动星和轨迹。
- 新增 Touch Slide，支持普通键与 A/B/C/D/E Touch 区互连、连续路径、无头以及 Break 头 / Break 轨迹，例如 `1d-E5[8:1]`、`A1<A3<A5[8:1]`、`A1b-A2b[8:1]`。
- Touch Slide 可用连续同向的 `<` 或 `>` 生成等距螺旋：`A1<<E5[8:1]` 绕一圈后进入 E5，`A1<<<E5[8:1]` 绕两圈后进入 E5。该多圈写法不适用于普通数字 Slide，也不支持混合方向符号。
- 完善 D 区 Tap、Hold 与 Slide。D 区 `s/z` 保留原版中段判定路线，只重新连接 D 区头尾；D 区端点同时接入 Touch Slide 的绘制和传感器判定。
- 修正 D 区直线、圆弧、`p/q`、`pp/qq` 等路径的间距、切线衔接、星星方向和终点判定特效位置。
- `PVOVERLAY` 支持图片或视频替换当前 PV，并可在连续覆盖之间进行渐变。

### 播放、录制与发布

- 修复中途播放、暂停续播、时间轴拖动后播放、PV 恢复、All Perfect 语音和录制结束等流程中的状态同步问题。
- PV / BGA 新增保持宽高比的缩放方式，录制可独立选择常用分辨率、帧率、码率和智能压制策略。
- 完善 Launcher、桌宠状态和发布目录；Unity Windows 构建完成后会自动组装 View、Edit、Launcher、皮肤、主题及配套工具。
- 加强无效谱面和异常音符的容错，避免单条解析错误直接卡死 View 或 Edit。

## v0.4.0 更新

### Edit

- 新增「自动踩音」，支持填写 BPM、First、难度和阈值预设。
- 新增音符密度图和全谱 16 分整理。
- 新增 Alpha 命令补全、参数提示和分类语法帮助。
- 新增基础语法错误标记和「导出无特效谱面」。
- 新增 4/4、3/4 小节模板，以及选中配置直接检索谱面库。
- 新增可视化音符插入，可在 View 中点击或拖动生成 Tap、Touch 与 Slide。
- 新增统一的录制设置窗口，可选择 60 / 120 FPS、输出分辨率、歌曲信息卡片、开头背景和 All Perfect。
- 新增双视频轨、双音频轨媒体时间线，支持拖放、吸附、切割、复制、删除、撤销、混合导出及与主波形同步预览。
- 媒体时间线采用临时工程事务：退出再进入保留撤销记录，保存谱面时提交，选择不保存时丢弃修改。
- 视频时间线导出自动采用最高素材分辨率并按 20MB 内目标压制；音频导出固定为 44100Hz。
- 新增简体中文、英语和日语界面。
- 新增浅色、CiRCLE 和 CiRCLE PLUS 编辑器主题。

### View 与 Alpha

- 新增「星星速度」设置，仅调整 Slide 路径的提前显现时间；`0` 对应 4.4.0 默认效果，不改变 Slide 移动、判定或 DJAuto 逻辑。
- 优化 D 区 Slide 与 Touch Slide 的等距路径绘制、星星朝向和终点判定特效位置。
- `COLOR` 染色统一采用保留原素材明暗、纹理、高光与暗边的色相替换方式，覆盖 Tap、Each、Break、Hold、Star 与 Slide。
- 修复媒体时间线中途播放、暂停续播、视频轨层级和同轨重叠片段恢复，并补充保持宽高比的 PV / BGA 缩放模式。
- 新增 `JLINE` 判定线颜色和 `ShowJudgeArea` 判定区显示控制。
- 新增按音符类型分别设置的 SV / HS，以及环形音符视觉出生半径 `SPAWN`。
- 新增 Hue、Tint、Move、Rotate、Shake 画面特效。
- 新增 `AUDIO`，可在谱面中播放 OGG、WAV 或 MP3。
- 新增 `PVOVERLAY`，可用 PNG、JPG 或 MP4 替换当前 PV，并支持可选的交叉渐变时间。
- 新增 Touch Slide：`E1-E4[8:1]` 为直线，`E1<E6[8:1]` / `E1>E7[8:1]` 为定向弧线，`E1^E3[8:1]` 为短弧；起点和终点支持 A/B/C/D/E 区且必须填写时长。
- Touch Slide 支持连段与绝赞：`A1<A3<A5[8:1]`、`A1b-A2b[8:1]`；`b` 位于起点后表示绝赞 Touch 头，位于路径终点后表示绝赞 Slide 路径。`A1!-A2[8:1]` / `A1?-A2[8:1]` 为无头写法。
- 新增 `@3/4`、`@4/4` 波形拍号和 `@RRGGBB` 编辑区分段背景。
- 新增 D 区音符和 Break Touch / TouchHold 专用显示。
- 新增可选方向的 `Shake`，可直接填写角度控制震动方向。
- 新增 Default、CiRCLE、CiRCLE PLUS 三种开头背景。

### Launcher 与发布

- 新增桌宠启动器，负责依次启动 View 和 Edit，并显示运行状态。
- 发布包改为 Launcher 位于根目录，View 与 Edit 分别放在 `App/MajdataView` 和 `App/MajdataEdit`。
- Unity 构建结束后自动发布 Edit、Launcher，并复制皮肤、主题、工具与配置库。
- 发布根目录统一只保留一份 `README.md`。

## 新增功能

以下为 MajdataViewAlpha 在 MajdataView / MajdataEdit 4.4.0 基础上增加的主要功能。

### MajdataEdit

- 支持深色、浅色、CiRCLE、CiRCLE PLUS 主题和自定义编辑器字体。
- 支持 `dx`、`sd` 及自定义皮肤目录。
- 新增 Master / Re:Master 歌曲信息卡片、独立缓存、DX 满分和等级显示。
- 新增配置库 / 节奏型检索，可查找相似配置片段。
- 新增 8 / 12 / 16 / 24 / 32 / 最高分拍格式刷和全谱整理。
- 新增选区镜像、右键小节模板、语法错误标记和 Alpha 命令补全。
- 新增配置即时预览和星星形状预览。
- 新增可视化音符插入。
- 新增音符密度图。
- 新增自动踩音。
- 新增音频转 44100 Hz 和音频 / 视频区段剪辑。
- 新增媒体时间线编辑器，可组合两条视频轨和两条音频轨，并与谱面保存状态统一管理。
- 集成 MaiMuriDX 无理配置检查。
- 波形图可显示音符、BPM、Clock Count、拍号、歌曲信息卡片、All Perfect 和录制区段。
- 新增独立录制参数窗口。
- 支持简体中文、英语和日语。

### MajdataView

- 支持谱面内动态修改音符 SV、HS、视觉出生半径、颜色、尺寸和透明度。
- 支持按 Tap、Each、Hold、Slide、Star、Break、Touch、TouchHold 分别设置音符属性。
- 支持动态控制判定线、判定区、判定文字、左右信息栏、中央数据显示和背景亮度。
- 支持 Gaussian、Neon、Trail、Fade、Flash、Brightness、Saturation、Contrast、Rainbow、Vignette、Zoom、Glitch、TVNoise、Hue、Tint、Move、Rotate、Shake 等画面特效。
- 支持谱面内附加音频和图片 / 视频 PV 覆盖。
- 支持非 C 区 TouchHold、Break Touch / TouchHold、Mine、D 区和 `rp/rq` Slide。
- 支持新版歌曲信息卡片、开头背景、All Perfect 结尾和生成标识。
- 支持从 View 向 Edit 发送可视化音符编辑操作。
- 支持固定帧视频录制和自定义输出分辨率。

### Alpha 语法

Alpha 命令写在谱面时间线中：

```text
<命令*参数>
<命令*(参数,时间)>
```

命令分为四类：

- 音符：`SV`、`HS`、`SPAWN`、`SPAWNMODE`、`DESTROY`、`BOUNCE`、`FAKE`、`COLOR/COLORV`、`SIZE/SIZEV`、`ALPHA/ALPHAV`
- 显示：`JLINE`、`TEXT`、显示开关、`ComboDisplay`、内外圈亮度
- 滤镜：Gaussian、Neon、Trail、Fade、Flash、Brightness、Saturation、Contrast、Rainbow、Vignette、Zoom、Glitch、TVNoise、Hue、Tint、Move、Rotate、Shake
- 媒体：`AUDIO`、`PVOVERLAY`

示例：

```text
<SV*tap=1.5,slide=0.8>
<SPAWN*tap=-4.8,hold=0>
<BOUNCE*tap=8:1,hold=4:1>
<COLOR*star=FF66CC,break=FF4500>
<ShowJudgeLine*(False,8:1)>
<Tint*(True,000000,0.6,8:1)>
<Shake*(True,0.5,12,30,0.2)>
<AUDIO*(True,media/voice.ogg)>
<PVOVERLAY*(True,media/cutin.mp4,8:1)>
<PVOVERLAY*(False,8:1)>
```

完整签名和示例请在 Edit 中打开：

```text
工具 → Alpha 语法帮助
```

### 扩展谱面标记

- `||`：单行注释。
- `|* … *|`：块注释。
- `@分子/分母`：编辑器波形拍号。
- `@RRGGBB` / `@NULL`：编辑区分段背景色。
- `m`：Mine。
- `d`：D 区。
- `x`：EX 音符或 Slide 头；`f`：击打烟花。
- `$` / `$$`：不旋转 / 固定速度旋转的星形 TAP。
- `?` / `!`：无头 Slide；`?` 保留运动星淡入，`!` 在开始移动时直接显示。
- `b`：Break；在 Slide 中头和轨迹的修饰位置分别生效。
- Touch Slide：普通键与 A/B/C/D/E Touch 区之间的直线、圆弧或多圈等距螺旋 Slide，必须填写时长；多圈仅支持连续同向的 `<` 或 `>`。
- 大 `P/Q` Touch Slide：`1P35[8:1]` 绕 3 号侧边圈后切线进入 5，`1P3E5Q0A5[8:1]` 可继续连接中央圈；`0` 是中央圈、`1-8` 是侧边圈、`9` 是最外圈。
- SlideCode：`5Q9A1P98CQ49K5[8:1]` 使用 `A/B/C` 节点、`P/Q` 顺逆时针轨道和末尾 `K` 终点组合连续轨迹；同一指令可连续写多个参数，例如 `A357`、`P98`。
- `rp` / `rq`：反向圆弧 Slide。

### 桌宠启动器

- 自动查找并依次启动 View 与 Edit。
- 显示启动、播放、制谱、录制和错误状态。
- 支持透明桌宠动画和状态气泡。
- 支持跟随 Edit 窗口或固定在桌面位置。

## 下载与运行

从 [Releases](https://github.com/Jian04/MajdataViewAlpha/releases) 下载完整发布包，解压后运行：

```text
MajdataLauncher.exe
```

发布目录：

```text
MajdataViewAlpha/
  MajdataLauncher.exe
  README.md
  Pets/
  App/
    MajdataView/
    MajdataEdit/
```

请保持目录结构完整。也可以直接运行 `App/MajdataEdit/MajdataEdit.exe`。

## 从源码构建

### 环境

- Windows 10 / 11 x64
- Unity 6000.4.2f1
- Unity Windows Build Support（x86_64）
- .NET SDK 6.0 或更新版本

### 完整发布包

1. 用 Unity 打开仓库根目录。
2. 选择 Windows x86_64。
3. 使用 **Build**，不要使用 Build And Run。
4. 输出到例如：

```text
%USERPROFILE%\Desktop\MajdataViewAlpha\MajdataView.exe
```

Unity 构建完成后会自动组装完整发布包。

### 单独检查 WPF 项目

```powershell
dotnet build .\MajdataEdit\MajdataEdit.csproj -c Release
dotnet build .\MajdataLauncher\MajdataLauncher.csproj -c Release
```

这两个命令只检查 Edit 和 Launcher，不会重新构建 Unity View。

## 使用说明

- Alpha Edit 与 Alpha View 需要配套使用。
- 自动踩音使用发布包内的专用运行环境，请保持 `tools/Maicaiyin` 完整。
- 自定义录制分辨率的宽和高必须为偶数。
- 高分辨率、高帧率和多滤镜录制会增加设备负担，程序会提示但不会强制阻止。
- 分层导出当前关闭。

## 已知问题

- D 区 Tap 使用星星或与星星 Slide 组合时，可能出现未知的显示或判定表现。
- 部分 View 背景样式下，D 区音符可能不显示。

## 致谢与许可

- 原项目及原作者：[MajdataView / MajdataEdit](https://github.com/LingFeng-bbben/MajdataView)，bbben（LingFeng-bbben）
- Touch Slide 圆弧方向参考 [AstroDX / SimaiSharp](https://github.com/reflektone-games/SimaiSharp/blob/5ef06f91ffdcc77d494baba46d2df4c8d67ca1d6/SimaiSharp/src/Internal/SyntacticAnalysis/Deserializer.cs)；多圈螺旋等扩展不代表 AstroDX 全语法兼容。
- SlideCode 判定区阈值、转移表与终点特效位置参考 [MajdataPlay](https://github.com/TeamMajdata/MajdataPlay/tree/c3423a4bba536e53921e8fdedab2b9d91121b393/Assets/Scripts/Scenes/Game/Misc/Parsing)（GPL-3.0）。
- 自动踩音：[Maicaiyin](https://github.com/Jian04/Maicaiyin)
- 配置库 / 节奏型检索：[MaiChartAssistant](https://github.com/Jian04/MaiChartAssistant)
- 配置库谱面来源：[Maichart-Converts](https://github.com/Neskol/Maichart-Converts)
- Simai：Celeca
- Hanabi 特效：青山散人
- 仿官谱歌曲封面制作：筱崎文音

本项目遵循 GPL-3.0，与原版保持一致。谱面、音乐、图片、视频及其他素材版权归各自权利人所有。
