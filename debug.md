# MajdataViewAlpha 调试记录

本文记录项目中已经遇到的严重问题、根因、修复方式和后续修改约束。

## 基准版本

- 原版行为优先参考 `E:\MaiChartAssistant\MajdataView-4.3.1`。
- 旧全屏实现参考 Git 初始版本 `9556ad3`。
- 修改前必须确认实际运行对象、场景层级和完整调用链，不能只按对象名称猜测。

## 判定线

### 错误

- SampleScene 中额外存在启用状态的 `DebugOutline`。
- Server 场景中的 `Outline` 和 `DebugOutline` 都挂载 `CustomSkin`，启动后都会加载 `outline.png`。
- 只控制 `Outline` 时，`DebugOutline` 仍然显示，表现为判定线叠加或完全不受开关控制。
- 把判定线每帧强制写入会破坏原有插值状态机。
- 设置变化时重建整个 Track 会让渐变变成立即开关。

### 修复

- 禁用 SampleScene 中的 `DebugOutline`，运行时只保留 Server 场景的 `Outline`。
- 谱面语法继续由 `DisplayTrack` 按指定持续时间插值。
- 编辑器即时设置通过 `TransitionTo` 修改当前 Track，不重建判定线 Track。
- 暂停时允许判定线恢复显示；继续播放后按当前时间重新计算 Track。

### 约束

- 不要重新添加第二个 `CustomSkin + SpriteRenderer` 判定线对象。
- 不要使用 `FindAnyObjectByType<CustomSkin>()` 查找判定线。
- 不要在 `Update` 中无条件覆盖 Renderer 状态。

## Wifi Slide

### 错误

- Wifi 的箭头段会通过 `SetActive(false)` 隐藏。
- 暂停后从头播放时，旧 Wifi 对象仍存在，但判定队列已经被消费。
- 仅恢复 Sprite 可见性会导致 Wifi 出现约 0.1 秒后立即被判定完成并销毁。
- Animator 状态不能作为回放恢复的唯一依据。

### 修复

- 启动时保存 Wifi 判定队列模板。
- 检测时间回退时重新创建 `_judgeQueues` 和 `judgeQueues`，并调用 `JudgeArea.Reset()`。
- 同时重置 `canCheck`、`isChecking`、`isJudged` 和 `arriveTime`。
- 根据当前时间恢复尚未经过的箭头段和移动星。
- 淡入透明度采用确定性的时间计算，不依赖 Animator 保留状态。

### 约束

- 回放恢复必须同时恢复视觉状态和判定状态。
- `new List<List<T>>(source)` 只是外层浅拷贝，不能用于可变判定队列快照。
- 不要只在 Continue 请求中恢复一次；时间轴回退也必须能自行恢复。

## 背景与遮罩

### 错误

- 关闭 `1080Circle_Rev` 后，中央圆周外区域没有外圈亮度，只有左右补边变黑。
- 四块黑色 RawImage 与反圆遮罩重叠时会产生复合 Alpha，重叠区比其他区域更黑。
- 左右 RawImage 曾被缩放并向中间移动，与 4.3.1 尺寸不一致。
- 关闭整个 `CanvasInfo` 会同时隐藏判定统计和 Combo。

### 修复

- 播放期间保留 `1080Circle_Rev`。
- `1080Circle_Rev` 和四块黑色 RawImage 使用相同的 OuterBrightness。
- 四块 RawImage 的位置和缩放对齐 4.3.1。
- `CanvasInfo` 始终保留，只单独控制背景 RawImage 和文字透明度。
- BGA 保持原始宽高比，按高度铺满，不裁成正方形。

### 约束

- 不要通过关闭整个 `CanvasInfo` 实现全屏。
- 不要新增未经验证的三层遮罩或运行时重挂父节点。
- 修改遮罩前必须同时检查 SpriteRenderer 排序层、Canvas 排序层和 Alpha 重叠。

## TouchHold riser 音效

### 错误

- `touchHold_riser.wav` 长 12.68 秒，靠 `hasTouchHold` 开始播、`hasTouchHoldEnd` 停止（单一共享 channel）。
- `[1:0]` 这类写法 `getTimeFromBeats` 解析出 holdTime=0，TouchHold 的 riser 开始与结束落在同一时刻。
- `SoundEffect.SoundEffectUpdate` 里同一个 SE 先 `BASS_ChannelPlay` 再 `BASS_ChannelStop`；同刻事件经 `waitToBePlayed.Sort` 后顺序不定，Stop 可能排在 Play 前，导致 riser 停不下来、响满 12.68 秒。
- 对照：普通 Hold 的尾部音效有 `if (!(note.holdTime <= 0.00f))` 保护，TouchHold 之前没有。

### 修复

- `SoundEffect.cs` TouchHold 分支加 `if (note.holdTime > 0.00f)` 守卫：0 时长不设置 `hasTouchHold`、不注册结尾，退化成普通 Touch（烟花保留）。
- 同时覆盖实时播放与录制导出两条路径（都以 `hasTouchHold`/`hasTouchHoldEnd` 为准）。

### 约束

- 改动后必须重新编译 MajdataEdit（`dotnet build -c Debug`），WPF 侧改动不会被 Unity 自动重编。
- 不要给 TouchHold 的 riser 改成每个音符独立 channel 之外的临时补偿；根因是 0 时长，按时长守卫即可。

## PV/BGA 缩放

### 错误

- 修全屏时把视频面 `videoSurfaceSprite` 改成 `Sprite.Create(Texture2D.whiteTexture, Rect(0,0,1,1), ...)`。
- `Texture2D.whiteTexture` 是 4x4，rect 只取 1x1，sprite 的 UV 仅覆盖 0~0.25。
- VideoPlayer 以 MaterialOverride 覆写 `_MainTex` 后，面片按 sprite 的 UV 采样，只取到视频左下角 1/16，再拉满整个面 → 画面被放大约 4 倍（面片高度其实仍是 10.8，被放大的是采样内容）。

### 修复

- `videoSurfaceSprite` 改用 `new Texture2D(1,1)`，rect=贴图尺寸，UV 回到 0~1，采样完整视频。
- 缩放基准用 `Camera.main.orthographicSize * 2`（相机可见高度），`scaleY = 相机高度 / spriteHeight(=10.8) = 1`，`scaleX = scaleY * 视频宽高比`。

### 约束

- 自建 sprite 给 VideoPlayer/MaterialOverride 用时，rect 必须等于贴图尺寸，否则 UV 不是 0~1，视频会被裁切放大。
- 不要把 BGA 尺寸耦合到遮罩物体（BackgroundCover/1080Circle_Rev）的 bounds，按相机高度对齐即可。

## 歌曲信息封面缓存 (songdetail_master.png)

### 架构

- Master 难度(diffNum==4)播放时,`MajdataEdit.MainWindowCore.EnsureSongDetailCache` 用 System.Drawing 把 DxBase + 封面 + DxOverlay + 动态文字烤成 `<谱面目录>/songdetail_master.png`。
- Unity `SongDetailTemplateView.TryApplyCachedCard` 优先读这张 PNG 贴到卡面;不走实时渲染(实时渲染观感不对,已弃用)。
- 视觉基准:`dist/songdetail_preview_*.png`(341×588,和烤图同坐标系)。

### 错误

- 谱师整行被省略号截断、字体偏宽:`DrawFitText` 用 `MeasureString(text, font, rect.Size, fmt)` 且 `fmt.Trimming = EllipsisCharacter`,测量的是**截断后**的宽度。17px 时报 w≈204≤215 直接判定"放得下"并 break,于是用满 17px 画了截断版,自适应缩放从未触发。(Aileron OTF 其实加载正常,排除字体加载嫌疑。)
- 等级超大且未转换:`14.9` 原样画出,且 scale-to-box 用 AddString 的紧致 bounds 反向放大;没有 `LV` 前缀、没有小数→`+` 转换。

### 修复

- `DrawFitText` 改用 `StringFormat.GenericTypographic`(去掉边距 padding)+ `NoWrap` 测**单行紧排宽度**,`width <= rect.Width` 才停;绘制用 `NoWrap | NoClip`、不带 Trimming。谱师落在 ~13px 单行铺满,和预览一致。
- 等级:`LV` 前缀**已烤进 DxOverlay 模板**,代码只画数字(Allerta 50px≈36px cap)+ `+`(25px,顶对齐),不要再画 LV(否则和模板重叠变粗)。数字左缘锚定 `box.Left+43`(≈x264,贴 LV 右侧),垂直居中 box 中线(≈y377);`SplitLevelForCache` 加小数→`+`(整数部分,小数 ≥0.6 显示 `+`)。灰紫描边 `Pen` 宽度 5f(对齐预览,比初版 2f 粗)。
- 落地前先用 PowerShell + System.Drawing 原型对拍 `songdetail_preview`,像素级吻合后再改 C#。

### 缓存与失效

- 不再每次播放都重烤。`EnsureSongDetailCache` 先用 `BuildSongDetailSignature`(版本号 + 标题/曲师/谱师/等级/BPM + 封面路径/修改时间/大小,`` 分隔)和 `songdetail_master.sig` 比对,签名一致就直接返回,跳过整个合成。
- 签名比对只是读一个小文本文件 + 字符串比较,不渲染,**不卡**;只有信息或封面真变了才重烤。
- 关闭 WPF "谱面信息"(`Infomation`)对话框后调用 `InvalidateSongDetailCache()` 删掉 `.sig`,强制下次播放重烤一次(PNG 先留着避免封面瞬间空缺)。
- 渲染逻辑改动时把 `SongDetailCacheVersion` +1,旧缓存签名自动失效、重烤一次。

### 约束

- System.Drawing 自适应字号**不要**用带 `rect.Size` + `EllipsisCharacter` 的 `MeasureString`,会把截断宽度当成"放得下"。用 `GenericTypographic` 测单行紧排宽度。
- 不要在播放路径里无条件重烤封面;按签名跳过,信息变更(尤其关闭谱面信息对话框)才失效。
- 等级 glyph 用固定字号(50/25)按相对位置摆,不要 scale-to-box 放大紧致 bounds(会爆大)。
- 坐标系是 341×588;等级组中心 ≈ (277.5, 377),`+` 顶部 ≈ y358。改前后都和 `dist/songdetail_preview_*.png` 比对。

## `m` 灰度语法

### 最终规则

- `1bm-5`：实际播放只灰起始击打星。
- `1-5m[8:1]`：实际播放只灰 Slide 轨道和移动星，起始击打星保持原色。
- 编辑器高亮中，只要 Slide 含尾部 `m`，整段文本显示为灰色，方便定位。
- `m` 的优先级与 `b` 类似，但灰度是最终视觉修饰，覆盖颜色高亮。

### 约束

- 编辑器高亮规则和实际渲染规则可以不同，必须分别记录，不能混为一谈。
- 不要仅根据字符是否位于字符串末尾判断 Slide 修饰范围，应先定位 Slide 路径。

## Edit 与 View 通信

### 错误

- 修改 BASS 暂停 API 曾导致 Edit 请求线程等待 View 响应并卡死。
- View 报错时使用异常日志会触发 Unity Error Pause，造成同步 HTTP 请求死锁。
- **拖动后立刻播放，开头一批 note 全掉**。根因是 codex 把 View `HttpHandler.Start` 从原版的「先 `SetStartTime` → 异步 `LoadJson`」改成了「同步 `LoadJsonImmediate` 把整谱实例化完 → 才 `SetStartTime`」。而 Edit 端 Normal play 先在主线程 `Bass.BASS_ChannelPlay`、再后台发包，BGM 已经在跑。View 同步加载耗时 X 使 `startAt` 过期 X，`AudioTimeProvider.Update` 第一帧 `AudioTime = offset + X` 直接快进，跳过 `[offset, offset+X]` 这段已加载的 note → 瞬间判 Miss。X 随谱面规模/封面烤图波动，故「有概率、拖到密集段更明显」。在 `startTime` 数值上修修补补治不了本质（codex 试了几十次）。OpStart/Record 走 Edit 端 5 秒倒计时缓冲，X 被吸收，不中招。

### 修复

- 保留当前稳定的音频暂停/继续链路，未经完整测试不要替换 BASS API。
- 可展示给用户的谱面错误使用普通警告和 `ErrText`，不要触发 Unity Error Pause。
- 掉 note：只把 `Start` case 回退到原版顺序——**先 `timeProvider.SetStartTime`，再异步 `loader.LoadJson`（不是 `LoadJsonImmediate`）**。ALPHA 表/封面在异步 `LoadJson` 的 `Update` 分支里同样会做，功能不丢。只改 Normal play 这一处，OpStart/Record 不动。

### 约束

- HTTP 处理不能在主线程异常暂停时阻塞请求线程。
- 修改 Pause、Continue、Stop 前必须同时检查 Edit 音频位置、View 时间轴和 HTTP 响应。
- 播放路径必须「先设时间基准、再（异步）加载 note」。绝不能在 `SetStartTime` 之前同步加载整谱，否则 `startAt` 会过期、开头 note 被 `AudioTime` 快进跳过。

## 性能与录制

- 不在播放开始或特效切换时创建大量材质、纹理或对象。
- 可复用材质必须缓存。
- 密集谱面应尽量在负时间加载阶段完成实例化。
- 录制结束由 All Perfect 动画正常完成触发，超时仅作为兜底。
- 30 FPS 与 60 FPS 导出必须同步 Unity 捕获帧率和 FFmpeg 输入帧率。

## All Perfect 与判定特效

### 错误

- All Perfect 开始前调用 `NoteEffectManager.ResetAllEffects()`，会让最后一批打击特效和判定特效瞬间消失。

### 正确行为

- 与 4.3.1 一致：最后一个音符对象结束后可以立即显示 All Perfect。
- 不主动清理场上的打击特效，已有特效按各自 Animator 自然播放结束。
- `ResetAllEffects()` 只用于明确的暂停、停止或场景重置，不用于 All Perfect 入场。

## 音符实时预览 (待机界面)

### 架构

- 写谱时光标所在音符槽在 View 待机界面静态渲染:WPF `NotePreviewModule` 把光标处 note 组展开(残缺 slide → 8 个端点),`MainWindowCore` debounce(120ms)后以 `EditorControlMethod.Preview` 发包。
- View `HttpHandler.Preview` 用 `loader.LoadJson(previewJson, -999f, previewOnly:true)` 加载;`previewOnly` 的 note 在 `TapBase` 等里跳过 `FixedUpdate` 判定、`Check`、`OnDestroy` 的 `NextNote`,是**不推进判定队列的惰性 note**。

### 错误

- 开头预览转场(OpStart)非从头播放时,老版 `NOTE DESIGN` 静态字幕有概率闪出。根因:`HideLegacySongDetailTexts` 用 `FindObjectsByType<Text>` 全场景按文字内容扫,而该 API **跳过未激活对象**;`CanvasSongDetail` 在 `LoadJsonImmediate` 时若恰为未激活,字幕没被隐藏,随后 `PlaySongDetail` 激活面板时露出。
- 预览时快速拖动并播放,有概率判定区被占位、note 全漏。根因:WPF `NotePreviewTimer_Elapsed` 在定时器线程 POST,与 UI 线程的播放启动存在 TOCTOU;漏网的 Preview 在 Start 之后到达 View,`ClearLoadedNotes` 抹掉真实谱面并注入惰性预览 note,`noteIndex` 永不前进 → 后续 `CanJudge` 全 false。

### 修复

- `SongDetailTemplateView.HideLegacyCardTexts` 改为锚定 `designerState.parent`(卡片 TextWrapper 的**原始**父级,`ConfigureText` 会把受管文字重挂到 cardImage,实时父级不可靠),用 `GetComponentsInChildren<Text>(true)`(含未激活)只隐藏非受管字幕,记录到 `legacyHiddenTexts`;`ResetOriginal` 再恢复。确定性,不再依赖时机。
- View 侧权威闸门:`HttpHandler` 加 `liveChartActive`,Start/OpStart/Record 置 true、Stop 置 false;`Preview` case 开头若 `liveChartActive` 直接 `break`(在 `ClearLoadedNotes` 之前)。无论 WPF 跨线程时序如何,迟到的 Preview 都无法污染正在播放的谱面。`isStart` 不能区分预览/播放(`SetPreviewTime` 也置 `isStart=true`),故用显式标志。

### 约束

- Preview 只在待机界面有效;一旦有真实谱面活动(含暂停),View 必须丢弃 Preview,不得 `ClearLoadedNotes`。
- 隐藏卡片字幕用子树遍历(含未激活),不要用 `FindObjectsByType`(跳过未激活)或按文字内容全场景扫。

## 修改流程

1. 复现并记录准确触发条件。
2. 对比 4.3.1、旧 Alpha 和当前版本。
3. 找到实际对象和完整状态链。
4. 只修改根因，不叠加补偿逻辑。
5. 编译 MajdataEdit。
6. 检查 Unity `Editor.log` 和 `Assembly-CSharp.dll` 更新时间。
7. 测试首次播放、暂停继续、从头重播、录制和全屏。
