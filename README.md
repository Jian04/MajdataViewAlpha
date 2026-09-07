# MajdataViewAlpha

基于 [MajdataView / MajdataEdit 4.4.0](https://github.com/LingFeng-bbben/MajdataView) 扩展，感谢原作者 bbben（LingFeng-bbben）及原项目贡献者。本项目遵循 GPL-3.0。

## v0.5.3 新增功能

相对 v0.4.2，依据 [v0.5.3 Release](https://github.com/Jian04/MajdataViewAlpha/releases/tag/v0.5.3) 与当前源码整理。

### 音符与轨迹

- **SlideCode**：通过 `A/B/C` 节点、`P/Q` 轨道和末尾 `K` 终点组合连续轨迹，支持同一指令连续填写多个参数，例如 `5Q9A1P98CQ49K5[8:1]`。
- **大写 `P/Q` 星星**：可选择绕行的圆圈，例如 `1P85[8:1]`、`2Q96[8:1]`；`0` 表示中央圈，`1–8` 表示侧边圈，`9` 表示最外圈。
- **Touch Slide 路径扩展**：在已有直线、圆弧基础上增加 `p/q`、`pp/qq`、大写 `P/Q` 及连续同向 `<<` / `>>` 多圈螺旋路径，支持普通键与 Touch 区之间的连接。例如 `E1pp5d[8:1]`、`A1<<E5[8:1]`、`1P3E5Q0A5[8:1]`。
- **继承语法**：支持编写任意半径的 Touch，以及沿星星轨道运动的 Tap。
- **Mine 设置**：可单独调整 Mine 音量，并选择是否显示判定特效。

### Alpha 命令

- **`ALPHAV` / `SIZEV` / `COLORV`**：即时修改已加载音符的透明度、尺寸和颜色，支持按音符类型设置，也可分别控制星形头 `star`、运动星 `slidestar` 和轨道 `slide`。例如 `<ALPHAV*slidestar=0.5>`、`<SIZEV*tap=1.5>`、`<COLORV*slidestar=FF0000>`。
- **`FAKE`**：让当前音符流后续音符仅用于显示，不计物量、不判定，也不产生击打音效、判定文字或特效。支持全局和分类设置，例如 `<FAKE*TRUE>`、`<FAKE*tap=TRUE,slide=TRUE>`，用 `<FAKE*FALSE>` 关闭。
- **`DESTROY`**：修改 Tap、Star、Each、Hold 的视觉终点半径，不改变判定时刻。支持分类设置，例如 `<DESTROY*tap=3,hold=4>`，`<DESTROY*NULL>` 恢复默认半径 `4.8`。
- **`TEXT` 扩展选项**：可设置持续时间、位置、字号、字体、字幕索引、显示样式和过渡时间，支持渐入 `Fade` 与逐字显示 `Typewriter`。

### 编辑器

- **多行音符流 `@* … *@`**：在已有可重叠音符流基础上增加跨行写法，方便分别管理音符与特效；可将音符流按时间合并回主谱。
- **实时预览**：新增实时预览功能，便于边编辑边查看谱面效果。

## 相对原版新增功能

以下为相对 MajdataView / MajdataEdit 4.4.0 的累计新增功能，包含 v0.5.3。

### 谱面语法与音符控制

- **动态音符属性**：`SV`、`HS` 控制速度，`SPAWN` / `SPAWNMODE` 控制出生位置与回退显示行为，`DESTROY` 控制视觉终点，`BOUNCE` 控制往返运动。
- **音符外观**：`COLOR` / `COLORV`、`SIZE` / `SIZEV`、`ALPHA` / `ALPHAV` 控制颜色、尺寸与透明度；支持分类设置，并分别控制星形头、运动星和轨道。
- **扩展音符**：D 区 Tap / Hold / Slide、非 C 区 TouchHold、Break Touch / TouchHold、Mine、`FAKE` 视觉音符，以及继承语法。
- **扩展轨迹**：`rp/rq`、可选绕圈的大写 `P/Q`、Touch Slide、Touch Slide 多圈螺旋与 `p/q`、`pp/qq` 路径，以及 SlideCode。
- **独立音符流**：`@{分拍}…` 与 `@* … *@`，可将不同音符或特效分流编写。
- **编辑标记**：块注释 `|* … *|`、波形拍号 `@分子/分母`、编辑区分段背景色 `@RRGGBB` / `@NULL`。

### 画面、字幕与媒体

- **动态显示控制**：判定线颜色、判定线与判定区显隐、判定文字、左右信息栏、中央数据显示和内外圈亮度。
- **字幕**：通过 `TEXT` 添加可定位、可设置字体与动画样式的字幕。
- **画面特效**：Gaussian、Neon、Trail、Fade、Flash、Brightness、Saturation、Contrast、Rainbow、Vignette、Zoom、Glitch、TVNoise、Hue、Tint、Move、Rotate、Shake。
- **谱面媒体命令**：`AUDIO` 播放附加音频；`PVOVERLAY` 用图片或视频覆盖当前 PV，并支持渐变切换。
- **展示样式**：新版 Master / Re:Master 歌曲信息卡片、DX 满分与等级显示、可选开头背景和 All Perfect 结尾。

### 制谱辅助与编辑器

- **界面与外观**：简体中文、英语、日语界面；深色、浅色、CiRCLE、CiRCLE PLUS 主题；编辑器字体、字号与播放器字体设置；`dx`、`sd` 及自定义皮肤目录。
- **语法辅助**：Alpha 命令补全、参数提示、分类语法帮助、语法错误标记和导出无特效谱面。
- **谱面整理**：8 / 12 / 16 / 24 / 32 / 最高分拍格式刷、全谱整理、选区镜像和小节模板。
- **配置与预览**：配置库 / 节奏型检索、配置即时预览、星星形状预览和实时预览。
- **可视化插入**：在 View 中点击或拖动生成 Tap、Touch 与 Slide。
- **分析辅助**：音符密度图、自动踩音，以及集成 MaiMuriDX 无理配置检查。
- **波形信息**：显示音符、BPM、Clock Count、拍号、歌曲信息卡片、All Perfect 和录制区段。

### 媒体编辑与录制

- **媒体时间线**：双视频轨、双音频轨，支持拖放、拍线吸附、切割、复制、删除、撤销、混合导出及主波形同步预览；媒体修改与谱面保存状态统一管理。
- **音视频处理**：音频 / 视频区段剪辑、可选音频转 44100 Hz、PV / BGA 保持宽高比缩放。
- **录制与导出设置**：独立参数窗口，可选择输出分辨率、帧率、码率、歌曲信息卡片、开头背景和 All Perfect；支持固定帧视频录制与以 20 MB 以内为目标的智能压制。

### 桌宠启动器

- 自动查找并依次启动 View 与 Edit。
- 显示启动、播放、制谱、录制和错误状态，支持透明动画与状态气泡。
- 可跟随 Edit 窗口或固定在桌面位置。

完整命令签名与示例见 Edit 的「工具 → Alpha 语法帮助」。
