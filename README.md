# MajdataViewAlpha

> 本项目由 Claude Code 和 Codex 辅助完成。  
> 本项目基于原版 [MajdataView / MajdataEdit](https://github.com/LingFeng-bbben/MajdataView) 修改、扩展与重新整理发布。感谢原作者 bbben、LingFeng-bbben 以及 MajdataView / MajdataEdit 的所有贡献者。

MajdataViewAlpha 是面向 maimai / 舞萌谱面制作的视频预览、调试与导出工具。它保留原版 MajdataView 的播放逻辑，并在其基础上增加 Alpha 扩展语法、动态显示控制、视频特效、皮肤切换、歌曲信息卡片、配置库检索、配置实时预览、视频录制导出和编辑器辅助功能。

仓库地址：<https://github.com/Jian04/MajdataViewAlpha>

## 下载与运行

前往 [Releases](https://github.com/Jian04/MajdataViewAlpha/releases) 下载发布包，解压后运行：

```text
MajdataEdit.exe
```

`MajdataEdit.exe` 会自动拉起同目录的 `MajdataView.exe`。请保持发布包目录结构完整。

```text
MajdataViewAlpha/
  MajdataEdit.exe
  MajdataView.exe
  MajdataView_Data/
  SFX/
  Skin/
  EditorSetting.json
  bass.dll / bass_fx.dll
```

## v0.3.0 更新重点

### 歌曲信息卡片
- 新增新版 Master / Re:Master 歌曲信息卡片，可在编辑器设置中切换“原版 / 新版”。Master 与 Re:Master 使用独立模板和独立缓存（`songdetail_master.png`、`songdetail_remaster.png`），互不覆盖。
- 卡片在 DXSCORE 右侧、星级上方显示 DX 满分（谱面总物量 × 3），并随物量变化自动刷新。
- Re:Master 等级文字使用白描边和深紫渐变，14.6 及以上自动显示为 `14+`。
- 卡片缓存只在标题、曲师、谱师、BPM、封面或对应难度等级变化时刷新；缓存失效时只删除签名文件、保留 PNG，避免 View 回退到实时拼 UI。修改 Master 等级不会删除 Re:Master 缓存，反之亦然。

### 编辑器与工具
- 编辑器字体设置改为真实系统字体族，新增 `DengXian`、`Noto Serif SC`、`Global Monospace` 等多个可读字体选项。
- 支持皮肤目录切换：`Skin/dx`、`Skin/sd`，也可以添加自定义皮肤目录。
- 新增 / 完善 Alpha 语法帮助。
- 新增格式刷，支持 8 / 12 / 16 / 24 / 32 / 最大分音。
- 新增配置库检索，内置约 1600 张谱供检索参考（请勿在比赛中使用）。
- 新增内置音频转换与媒体剪辑工具：可将音频转为 44100Hz，或剪掉某段乐曲并拼接剩余部分（操作后需重新打开谱面）。
- 新增配置预览，可实时看到正在书写的配置效果以及星星形状。
- 谱面信息面板新增 BPM 和 Clock Count 显示。
- 时间轴时间显示精确到小数点后六位。
- 录制的预览定位由按键改为直接拖拽时间轴；移除了旧版录制模式、note 速度等冗余 UI，以及 trial version 限制。

### 语法与显示
- 改进 `COLOR` 染色逻辑，保留音符原本的明暗、纹理和层次。
- 新增屏幕中央显示内容控制语法（`ComboStatus`）。（现在暂不可用！！）
- 新增 `&RRGGBB` / `&NULL` 背景分段语法，可对编辑区文本分块标记。
- 波形图时间轴支持录制准备区、开头歌曲信息展示区和 All Perfect 区域预览，并提供 All Perfect 开关。
- 导出视频支持 30 FPS / 60 FPS 两个档位。

### 稳定性
- All Perfect 出现时不再强制清空最后一批判定特效。
- 修正多处播放、暂停、时间轴拖拽、预览与导出流程的稳定性问题。
- 修正视频导出：Edit ↔ View 通信不再因过短超时误判“端口断开”；自动停止在 All Perfect 演出放完或谱面结束后触发（关闭 All Perfect 时谱末 +5 秒兜底），手动点“终止”也会让 FFmpeg 正常收尾、生成视频。

### 已知问题
- 第一次播放未构建缓存时，预览可能导致键位卡死，djauto产生miss。
- 未构建歌曲封面缓存或第一次播放时，如果直接导出视频，可能由于卡顿导致pv和原本错位。
- 屏幕中央显示内容控制语法（`ComboStatus`）暂时无法使用，疑似逻辑有冲突。

## 从源码构建

仓库中的 `alpha/` 是主工程目录，包含 Unity View 和 WPF Edit。

### MajdataEdit

```powershell
cd alpha
dotnet build MajdataEdit\MajdataEdit.csproj -c Release
```

Debug 调试：

```powershell
dotnet build MajdataEdit\MajdataEdit.csproj -c Debug
```

### MajdataView

用 Unity 打开 `alpha/` 工程，构建 Windows Standalone。构建出的 `MajdataView.exe` 需要和 `MajdataEdit.exe` 放在同一发布目录。

## 编辑器设置

编辑器设置中新增或调整了以下项目：

- `Skin`：选择 `Skin/` 下的皮肤目录，例如 `dx`、`sd`。
- `整体主题`：编辑器界面配色主题。
- `编辑器字体`：谱面编辑区字体。
- `歌曲信息卡片`：`原版` 或 `新版 Master/Re:Master`。
- `内圈亮度` / `外圈亮度`：分别控制圆形区域内外遮罩亮度。
- `显示判定线`：播放时开关判定线。
- `显示判定文字`：开关 Critical Perfect、Perfect、Great、Fast/Late、Slide 判定牌等素材。
- `显示左侧判定统计` / `显示右侧 Combo 信息`：分别控制左右 UI。
- `显示 All Perfect`：关闭后不播放 All Perfect 动画和语音，但不改变谱面结束时机。

## Alpha 扩展语法

Alpha 扩展语法统一使用尖括号：

```text
<名称*参数>
<名称*(参数,持续秒数)>
```

多个语法可以写在同一时间点：

```text
<COLOR*FF00FF><SV*1.5><TEXT*(HELLO,2)>
```

### COLOR

修改后续音符颜色。

```text
<COLOR*FF00FF>
<COLOR*tap=FF69B4,slide=00BFFF,break=FF4500>
```

可用类型：

```text
tap, each, hold, slide, star, break, touch, touchhold
```

恢复默认：

```text
<COLOR*NULL>
<COLOR*tap=NULL,slide=NULL>
```

颜色支持 `RRGGBB` 和 `RRGGBBAA`。

### SV

修改谱面滚动速度倍率。

```text
<SV*2.0>
<SV*0.5>
<SV*1.0>
```

### SIZE

修改后续音符大小倍率。

```text
<SIZE*1.5>
<SIZE*tap=0.8,slide=1.2>
<SIZE*1.0>
```

恢复默认：

```text
<SIZE*NULL>
<SIZE*tap=NULL>
```

### ALPHA

修改后续音符透明度。

```text
<ALPHA*0.5>
<ALPHA*tap=0.3,slide=0.8>
<ALPHA*1.0>
```

恢复默认：

```text
<ALPHA*NULL>
<ALPHA*tap=NULL>
```

### TEXT

在左上角显示字幕。

显示指定秒数：

```text
<TEXT*(你好,2)>
```

持续显示到下一条 `TEXT`：

```text
<TEXT*你好>
```

清除字幕：

```text
<TEXT*>
```

### 显示控制

格式：

```text
<名称*(目标值,渐变秒数)>
```

示例：

```text
<ShowJudgeLine*(False,2)>
<ShowJudgeInfo*(False,1)>
<ShowComboInfo*(True,0.5)>
<ShowJudgeText*(False,2)>
<InnerBrightness*(0.8,3)>
<OuterBrightness*(0.5,3)>
```

支持项目：

| 名称 | 作用 |
| --- | --- |
| `ShowJudgeLine` | 判定线 |
| `ShowJudgeInfo` | 左侧判定统计 |
| `ShowComboInfo` | 右侧 Combo / 分数信息 |
| `ShowJudgeText` | 音符判定文字、Fast/Late、Slide 判定牌 |
| `InnerBrightness` | 内圈背景遮罩亮度 |
| `OuterBrightness` | 外圈背景遮罩亮度 |

### Combo 显示内容（现在暂时不可用！！！）

切换中间显示内容，使用和显示控制相同的时间渐变逻辑。

```text
<ComboStatus*(Combo,1)>
<ComboStatus*(Score,1)>
<ComboStatus*(Achievement,1)>
```

具体可用名称以编辑器设置中的 Combo 显示选项为准。

### 全屏视频特效

格式：

```text
<特效名*(持续秒数,强度)>
```

示例：

```text
<Gaussian*(2,1.5)>
<Neon*(3,1)>
<Trail*(3,0.8)>
<Flash*(1,-1)>
<Vignette*(2,0.8)>
<TVNoise*(2,1)>
```

| 特效 | 说明 |
| --- | --- |
| `Gaussian` | 高斯模糊 |
| `Neon` | 前景边缘 RGB 分离 / 霓虹效果 |
| `Trail` | 残影拖尾 |
| `Flash` | 正强度白闪，负强度黑闪 |
| `Fade` | 兼容旧写法，等价于黑闪 |
| `Zoom` | 画面放大后恢复 |
| `Vignette` | 圆形可视区域向内收缩后展开 |
| `Glitch` | 横向分段故障抖动 |
| `TVNoise` | 横向电视噪声、扫描线和错位 |
| `Brightness` | 亮度变化 |
| `Saturation` | 饱和度变化，强度 `1` 接近黑白 |
| `Contrast` | 对比度变化 |
| `Rainbow` | 环形动态彩虹染色 |

多个高强度全屏特效叠加会增加播放与导出负担。

### 背景分段标记

编辑器中可用 `&RRGGBB` 给后续谱面段落加淡色背景标记，用于分段。

```text
&FF0000
1,2,3,4
&00AAFF
5,6,7,8
&NULL
```

`&NULL` 结束当前背景色。

### 注释

单行注释：

```text
|| 这里是注释
```

块注释：

```text
|*
这里的内容会作为注释
*|
```

### 地雷音符

音符尾部加 `m` 表示地雷音符，显示为低饱和灰色。

```text
1m
1hm[8:1]
1bm-5[8:1]
1-5m[8:1]
```

规则和 Break 标记类似，但含义是创建地雷 note。

### 旋转 Slide

新增 `rq` / `rp` 旋转 Slide。

```text
1rq5
1rp5
```

终点越接近起点，旋转特效半径会相应缩小。

## 编辑器工具

- `格式刷（自动最高分拍）`
- `格式刷（8分）`
- `格式刷（12分）`
- `格式刷（16分）`
- `格式刷（24分）`
- `格式刷（32分）`
- `Alpha 语法帮助`
- `音频无损转 44100Hz`
- `剪掉某段音频 / 视频并拼接剩余部分`
- `配置库 / 节奏型检索`（内置约 1600 张谱供参考，请勿在比赛中使用）

音频工具会先在 `backup/` 中备份原文件。

> 书写 Alpha 配置或 Slide 时，编辑器会实时预览效果与星星形状，无需手动触发。

## 录制与导出

- 文件菜单提供 `导出视频（30 帧）` 和 `导出视频（60 帧）`。
- 60 帧模式会同步设置 Unity 捕获帧率和 FFmpeg 输入帧率，避免音画速度不一致。
- 录制的预览定位改为直接拖拽波形图 / 时间轴，不再依赖旧版录制模式按键。
- 自动停止：开启 All Perfect 时，演出动画放完即停止并出视频；关闭 All Perfect 时，谱面结束 +5 秒兜底停止。中途点“终止”也会让 FFmpeg 正常收尾、生成视频。
- 左下角生成标识为 `Generated by MajdataViewAlpha`。

## 已知问题

- 书写非 note 内容时触发预览，偶尔会在 View 左上角报错；点击“终止”刷新即可消除。
- 刚进入谱面、加载过量物量和特效时，Edit 清除预览不及时可能导致判定区占位、DJ Auto 误 Miss。
- 使用内置音频转换工具转换后程序可能闪退（转换通常已完成，重新打开即可）。
- 高分辨率录制叠加多个高强度全屏特效时，性能压力较大。
- 原版限制仍存在：不支持动态比特率 mp3；内置录屏要求 View 分辨率为偶数。

## 致谢与许可

- 原项目：[LingFeng-bbben/MajdataView](https://github.com/LingFeng-bbben/MajdataView)
- 配置库 / 节奏型检索及内置谱库：[MaiChartAssistant](https://github.com/Jian04/MaiChartAssistant)
- 配置库谱面来源：[Maichart-Converts](https://github.com/Neskol/Maichart-Converts)（作者 Neskol 等）
- Simai：Celeca
- Hanabi 特效：青山散人
- 仿官谱歌曲封面制作：筱崎文音

感谢原作者提供的基础框架、编辑器和运行逻辑。MajdataViewAlpha 是在原项目基础上的二次修改与扩展。

本项目遵循 GPL-3.0，与原版保持一致。谱面与素材版权归各自原作者所有。
