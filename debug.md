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

### 修复

- 保留当前稳定的音频暂停/继续链路，未经完整测试不要替换 BASS API。
- 可展示给用户的谱面错误使用普通警告和 `ErrText`，不要触发 Unity Error Pause。

### 约束

- HTTP 处理不能在主线程异常暂停时阻塞请求线程。
- 修改 Pause、Continue、Stop 前必须同时检查 Edit 音频位置、View 时间轴和 HTTP 响应。

## 性能与录制

- 不在播放开始或特效切换时创建大量材质、纹理或对象。
- 可复用材质必须缓存。
- 密集谱面应尽量在负时间加载阶段完成实例化。
- 录制结束由 All Perfect 动画正常完成触发，超时仅作为兜底。
- 30 FPS 与 60 FPS 导出必须同步 Unity 捕获帧率和 FFmpeg 输入帧率。

## 修改流程

1. 复现并记录准确触发条件。
2. 对比 4.3.1、旧 Alpha 和当前版本。
3. 找到实际对象和完整状态链。
4. 只修改根因，不叠加补偿逻辑。
5. 编译 MajdataEdit。
6. 检查 Unity `Editor.log` 和 `Assembly-CSharp.dll` 更新时间。
7. 测试首次播放、暂停继续、从头重播、录制和全屏。
