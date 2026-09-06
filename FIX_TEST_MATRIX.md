# Alpha 当前未提交改动与核对矩阵

本文覆盖当前工作区全部未提交功能改动，包含：

1. 用户本轮列出的 14 项接线问题；
2. Slide AST、Alpha 命令、播放协议、暂停预览、View 显示、录制和兼容性改动；
3. 每项的成因、修复方式、自动检查与人工验收方法；
4. 当前已经验证和仍需在 Unity/Windows 验收的边界。

逐项人工测试请以 `ALL_DIFF_TEST_CHECKLIST.md` 为准。该文件按当前
`v0.4.2 → working tree` 全部行为 diff 拆分为独立 checkbox；本文主要保留
14 个已知 bug 的成因、修法和发布门禁。

状态说明：

- `自动通过`：已被 `Tests/SlideAstRegression` 实际执行。
- `静态通过`：已编译或检查源码接线，但不能替代画面验收。
- `待 Unity`：必须进入 Unity View 观察对象、动画、材质或时序。
- `待 Windows`：必须在 Windows 编译或运行 WPF 编辑器。

## 一、当前验证基线

| 项目 | 当前结果 | 复核命令或方法 |
|---|---|---|
| Slide/Alpha 定向回归 | 自动通过：5110 assertions + 20,000 malformed cases | `dotnet run --no-restore --project Tests/SlideAstRegression/SlideAstRegression.csproj` |
| 定向测试项目编译 | 自动通过：0 error / 0 warning | `dotnet build --no-restore Tests/SlideAstRegression/SlideAstRegression.csproj` |
| 补丁格式 | 自动通过 | `git diff --check` |
| AppleDouble / Finder 文件 | 自动通过：`alpha`、`fix` 中均无 `._*`、`.DS_Store` | 搜索两个根目录 |
| `Desktop/fix` 同步 | 最新交付含 74 个文件；本轮新增清单与 SV 修复已同步 | 覆盖复制后按全量清单 `PACK-03` 复核 |
| Unity C# 编译 | 第三轮未能本机验证：`Assembly-CSharp` 目标 .NET Framework 4.7.1，本机补上参考程序集后 `dotnet build` 仍在 restore/build 阶段挂住（10 分钟无输出），因此本轮 Unity 侧改动只做了源码审阅 | 在 Windows/Unity 侧执行 `dotnet build Assembly-CSharp.csproj`，或直接看 Unity Console |
| Unity 画面与动画 | 待 Unity | Unity 打开工程，Console 不得有 error，再按第三、四节验收表现 |
| WPF Release 编译 | 待 Windows | `dotnet build MajdataEdit/MajdataEdit.csproj -c Release` |
| View 视觉与播放时序 | 待 Unity | 按第三、四节逐项执行 |

本次 14 项涉及播放状态机、对象生命周期和 View 变换，属于高风险接线，因此执行了一次定向回归；不是每次小改都跑完整发布回归。

### 0.4.2 正确、但新增接线曾破坏的行为

| 行为 | 0.4.2 为什么正常 | 新接线为什么出错 | 当前处理 |
|---|---|---|---|
| 文本/黄色光标 timing | 直接按原 parser 的 comma 边界取时，没有 overlay/shared-timeline 二次换算 | 新增 overlay 和共享时间轴后，`rawTextPositionX`、字符 offset、逗号推进前时间曾被当成同一种坐标，产生 off-by-one | 区分文本选择位置与 timing 采样；黄色光标固定在 closing comma 推进前采样 |
| `2^8dm` 增量识别 | 旧启发式会容忍未输入 duration 的半成品 | 新 AST 初版把完整播放的严格验证直接用于输入预览，缺少 duration 时整支被丢弃 | preview validator 与完整 validator 分离；新增 `2^8dm` 精确自动用例 |
| 负 SV 回中心 | note 一旦进入 Running，会继续使用积分后的真实半径向中心移动 | 新 `SPAWNMODE*rewind` 只看“当前是否越过 SPAWN”；回退越过 SPAWN 时把位置强制吸回 `spawnRadius`，造成顿挫 | 分离 `isPastSpawnNow` 与 `hasEverCrossedSpawn`；Rewind 缩小时仍使用积分半径连续回中心，EachLine 同步 |

## 二、用户列出的 14 项：成因、修法、状态

| # | 原问题 | 直接成因 | 修复方式 | 自动核对 | 人工验收 | 当前状态 |
|---|---|---|---|---|---|---|
| 1 | `{16}5/2?^8dm[12:1]` 报修饰符错误 | 增量预览和完整语法共用“必须已有时长”的验证入口；D-zone、`?`、Slide body `m` 组合在预览链路被过早判错 | 新增 `TryValidateForPreview`；完整播放仍严格验证时长，预览允许完整路径暂缺时长；`?` 留在 head，`m` 留在最后一段 body | 自动覆盖 `2?^8dm[12:1]`，检查 no-head、渐入、Mine body、D-zone end | 在编辑器输入完整例，确认不标红且 View 终点为 `8d` | 代码完成，自动通过，待 View |
| 2 | 波形左拖仍像放大，右侧延长且卡 | viewport 以历史红线 X 作为每秒像素基准；左边缘还叠加 LocationChanged，左右采用不同锚点；resize 每帧重建 GDI bitmap | viewport 固定以中心播放头和左右相同半时长计算；移除 LocationChanged 双通知；resize 时先由 WPF Stretch，75 ms 稳定后只重建一次 | 静态检查新 viewport 和 debounce 接线 | 分别拖左右边缘，播放头必须居中、两侧对称缩放、拖动不阻塞 | 代码完成，待 WPF |
| 3 | 暂停回拖后 Slide/Touch 残留；预览到播放闪烁；终止到播放黑边 | 暂停预览和 Fake 共用 `previewOnly`；首个回拖要重载可逆对象；重新播放先清空预览再异步建正式对象；Continue 可能与在途 TimelinePreview 竞态 | Fake 与 timeline preview 生命周期分离；Seek 仅移动共享时间；正式对象 ready 后再删预览对象；保留材质缓存；在途 preview 强制 fresh Start；View 对错误 Continue 返回失败 | 自动检查 staging、Fake 分离、在途 preview 竞态和协议错误 | 暂停前后反复拖动，再快速播放/暂停 10 次；不得残留、白闪/黑闪或 View 冻结 | 代码完成，自动/静态通过，待 Unity |
| 4 | `2?^8dm` 先显示 `2^8` 后卡住，D 星未继续构建 | 完整 AST 在缺少 `[duration]` 时验证失败，预览退回旧启发式；旧启发式不能继续表达 D-zone endpoint | 增量预览直接使用共享 AST 的 preview validation，并在内部临时补 `[4:1]`；D-zone 保留为一等位置数据 | 自动覆盖 `2dv4` 与 `2?^8dm[12:1]` | 逐字输入到 `8`、`8d`、`8dm`，每一步都应更新 | 代码完成，自动通过，待 View |
| 5 | Zoom 下 Touch/Tap 判定文字错位或消失；Mine 开关不一致；遮罩、弧线、发光不缩放 | Touch 判定、FAST/LATE、JUST、TouchHold 粒子曾脱离 NoteEffects 根节点；遮罩与 gameplay root 混在同一变换集合；动态判定文字需要重新应用总 alpha | 所有命中反馈归入 NoteEffects 变换平面；JudgeText guard 每帧恢复总 alpha；Mine 开关只短路 Mine feedback；视口遮罩从 gameplay transform 排除 | 静态检查 feedback parent、JudgeText guard、Mine 条件、mask filter | 同时执行 ZOOM/MOVE/ROTATE 并触发 Tap、D-zone、Touch、TouchHold；分别切换两个开关 | 代码完成，待 Unity |
| 6 | 快速暂停/播放音符闪烁 | Edit 的 Start/Pause/Continue 可与未完成 HTTP preview 交错；旧对象被清空后新对象还没 ready；在途 TimelinePreview 标志尚未回写时可能误发 Continue | control generation + 两阶段 Start/Continue；drain pending preview；在途 preview 视为 fresh Start；正式 binding ready 后替换 preview | 自动检查 generation、defer start、preview race、View 拒绝错误 Continue | 连续快速点击并拖动，编辑器音频和 View 必须保持同一状态 | 代码完成，自动通过，待 Unity |
| 7 | 录制阶段谱师字体缺失 | 歌曲卡原始 Text 可能引用缺失资源；`LoadFonts` 未在 ApplyMaster 前执行；字体资源失败时没有把 fallback 明确赋给 designer Text | ApplyMaster 前加载字体，并把 Aileron/系统/正文 fallback 显式赋给 designer Text | 静态检查字体加载和赋值 | 启用录制歌曲信息入场，检查中英日谱师名 | 代码完成，待 Unity/录制 |
| 8 | Fake Hold 结束后不销毁 | `component.previewOnly = previewOnly || isFake` 把 Fake 错当成可逆时间轴对象；FakeNoteLifetime 对 previewOnly 永不销毁 | `previewOnly` 只代表 timeline/standby preview；Fake 仅通过 `isFake` 禁用判定，正常执行 FakeNoteLifetime | 自动检查两个状态已分离 | 播放 Fake Hold 到尾部；暂停预览中的 Fake Hold 仍应可回退 | 代码完成，自动通过，待 Unity |
| 9 | 负 SV 后音符回中心不顺滑 | `SPAWNMODE*rewind` 用当前 crossing 同时决定状态和显示位置；音符回退越过 SPAWN 时，显示位置从积分半径强制切回 `spawnRadius`，破坏 0.4.2 的连续运动 | `isPastSpawnNow` 只决定是否保持 full scale；`hasEverCrossedSpawn` 决定是否继续使用积分半径。已经出现过的音符在 Rewind 时沿原路径回中心并缩小，EachLine 使用相同规则 | 自动断言反向越过 SPAWN 后仍使用 0.5 半径而不是吸回 1.225 | 正 SV 出现后切负 SV，慢速观察 Tap/Hold/Star/EachLine 连续经过中心 | 代码完成，自动数学验证，待 Unity 画面 |
| 10 | “最高分拍”仍留下较小分拍 | 转换器在发现“源分拍前缀 + 目标可整除”时优先保留一个源分拍，导致 `{4}`/`{8}` 残留 | 先按最高目标 unit 整除并直接输出最高分拍；只在确实不能表示时才回退源分拍 | 自动覆盖 `{4}` + `{16}`，断言结果无 `{4}` | 对混合分拍选区执行最高分拍，逐拍比对音符时间 | 代码完成，自动通过 |
| 11 | BOUNCE 不受 HS/SV | 旧直线时间模型只按固定秒数插值；没有以 note type/stream 的有效 HS 和 SV 积分寻找起飞点 | Bounce 保存有效 HS；使用对应 stream/type 的累计 SV 找起飞 crossing 和实时 progress；负 SV 可倒退；Once/Rewind 分开 | 源码与回归接线检查；Unity 数值表现需手测 | 比较 HS=1/2、SV=0.5/2/-1，并核对 Once/Rewind | 代码完成，待 Unity |
| 12 | 黄色光标时间仍是逗号后 | SelectionChanged 直接把任意 caret offset 交给 Serialize，处于边界时会采到下一分拍 | 先求当前 timing slot 的 closing comma，再在 parser 推进该逗号前采样 | 静态检查 `GetCaretTimingTime` | 在逗号前、逗号上、逗号后移动 caret，黄色光标应落在所属单元时间 | 代码完成，待 WPF |
| 13 | `E1-E7-E5-E3`、`2dv4` 不能边输边预览；V 星方向错误 | 预览只接受最终合法 AST；无 duration 的已完成 segment 被当成错误；尖角切线取 outgoing 方向 | preview validator 允许暂缺 duration；每个完整端点即时生成；尖角精确点优先使用 incoming tangent，D-zone v 中心跟随前半 | 自动覆盖两种增量表达式；切线代码静态检查 | 逐字输入并慢速播放经过 V 中点，星星不得提前转向后半 | 代码完成，自动/静态通过，待 Unity |
| 14 | `?` 和 `!` 没区别；`!` 应为 1.0 无渐入 | parser 曾把两者合并为同一 NoHead；后续直接把整个 Slide 的 `starSpeed` 改 1 又导致路径一起跳出 | AST 分成 NoHeadWithFade / NoHeadWithoutFade；`!` 只抑制 guide-star fade，路径仍使用正常 starSpeed 提前显示；same-head 继承该语义 | 自动覆盖 `?`、`!` 和 same-head；断言 loader 不再覆盖路径 starSpeed | 并排播放 `1?-5`、`1!-5`，路径出现时间相同，仅移动星星渐入不同 | 代码完成，自动通过，待 Unity |

### 2.1 这 14 项是不是新改动引起的

| # | 归属 | 新改动到 bug 的直接因果链 |
|---|---|---|
| 1 | 新 AST 接线直接引入 | 完整 validator 被直接复用于增量输入，缺 duration 的合法半成品在到 runtime 前就被过滤 |
| 2 | 新 waveform/shared-timeline 重构直接引入 | viewport 从 0.4.2 的单一坐标改成窗口锚点、cursor X、resize 通知多套坐标；左右边缘走了不同计算并重复重画 |
| 3 | 新 Pause/TimelinePreview 功能直接引入 | preview 对象、正式对象、Fake 对象共用生命周期标志；Start 又先清旧后异步建新，导致残留和空帧 |
| 4 | 新 AST 增量预览直接引入 | AST 严格失败后回退旧字符串启发式，旧分支只认识 `2^8`，不能继续携带 D-zone 与 body modifier |
| 5 | 新 ZOOM/MOVE/ROTATE 与 Mine 开关接线引入 | note、Touch feedback、动态文字、viewport mask 位于不同 transform/alpha 控制树；新效果同时作用时暴露分叉 |
| 6 | 新 Pause/Seek HTTP 状态机直接引入 | Edit 在 TimelinePreview 响应回写前可能误判为普通 Pause 并发 Continue；View 同时还在替换 loader |
| 7 | 新录制 SongDetail/字体 fallback 接线引入 | designer Text 在字体加载前保存引用，且 fallback 只生成字体对象、没有明确回写该 Text |
| 8 | 新 FAKE 与可逆预览接线直接引入 | `previewOnly = previewOnly || isFake` 把正式 Fake Hold 错归为永不销毁的时间轴预览对象 |
| 9 | 新 SPAWNMODE Rewind 直接引入 | 回退越过 SPAWN 后 `isPastSpawnNow=false`，旧实现立即把显示半径从积分值钳回 `spawnRadius=1.225`，所以出现吸附顿挫 |
| 10 | 新最高分拍转换分支直接引入 | 为保留原分拍可表示性增加的前缀回退在本可完全转成最高分拍时也保留 `{4}/{8}` |
| 11 | 新 BOUNCE 与 typed HS/SV 功能接线不完整 | 抛物线仍用固定 wall-clock duration，而 note 本体改用有效 HS 与累计 SV，两个 progress 来源不同 |
| 12 | 新 overlay/shared-timeline caret 接线直接引入 | `rawTextPositionX`、字符 offset、closing comma 推进前时间被混成一种坐标，边界处采到下一 timing |
| 13 | 新 AST preview 和新路径采样共同引入 | preview 过早要求最终 duration；路径采样在精确 V 角点选择 outgoing tangent，使星星提前转向 |
| 14 | 新 `!/?` 语义功能接线不完整 | parser 先合并成同一 NoHead；第一次修补又错误覆盖整个 Slide 的 starSpeed，而不是只关 guide-star fade |

结论：这 14 项不是 v0.4.2 自己已有的同一批缺陷；它们主要由当前新增功能直接引入，或由新功能把 0.4.2 的单一状态假设扩展后暴露。语法/数学类已有自动测试，WPF 交互和 Unity 画面仍必须手测，不能仅凭自动测试宣称视觉验收完成。

### 2.2 第二轮复现的 7 项：真正的根因

上一轮对 2、3、4、5、6、7 的处理没有命中根因，以下是重测后定位到的实际原因。

| 复现项 | 上一轮为什么没修好 | 真正根因 | 本轮修法 | 核对 |
|---|---|---|---|---|
| 左右拉动卡顿 | 只把 GDI 重建 debounce 掉，仍然每个 `SizeChanged` 给光标累加 `TranslateTransform` | 光标是 `Margin="357,0,0,0"` 的固定定位元素，且与 `MusicWave` 不在同一列，所以必须靠 transform 补偿；补偿与 WPF 拉伸旧位图不同步，看起来就是卡 | 光标改为与 `MusicWave` 同列、`HorizontalAlignment="Center"`、`Width="1"`，靠布局居中；删除 `UpdateWaveResizeAnchor` 整个补偿链路 | 拖窗口左右边缘：播放头始终居中，无逐帧补偿 |
| `4[12:1]` 报修饰符位置错误 | 只放宽了预览验证，没放宽 Hold 判定 | 带 `[..]` 但没有 `h` 的键位音符落到 Tap 分支后又被修饰符检查判为非法（v0.4.2 是静默丢时长当 Tap） | 有时长括号即视为 Hold/TouchHold；异常信息带上具体 note 文本 | 自动：`8[8:1]`、`4[12:1]`、`A1[8:1]` 及用户整段谱面 |
| `2?^` 冒出幽灵星星 | fallback 只是被“绕过”，仍保留端点猜测分支 | `TryGetIncompleteSlide` 会为缺端点的输入枚举候选端点，一次产生多条预览 | 整个端点猜测分支删除：缺端点不预览，端点齐全只补 `[4:1]` | 自动：`1p` 无预览、`1p1`/`2dv4`/`E1-E7-E5-E3` 有预览 |
| 判定文字开了不显示 | 只改了半径定位，没查默认值 | `DisplayTimelineController` 的 `initialJudgeText/JudgeLine/JudgeInfo/ComboInfo` 是 `float` 默认 0，standby 会把 0 推给 `JudgeTextAlpha` 并设 `forceRenderingOff` | 这些初值改为 1，与 `EditRequestjson` 的默认可见一致 | 冷启动直接播放，不进设置面板，判定文字必须可见 |
| Zoom 下 Touch 判定文字/特效错位 | 只把对象挂到 NoteEffects 之下 | 挂上去之后仍用 `transform.position = 传感器坐标` 写世界坐标，而 NoteEffects 本身被 ZOOM/MOVE/ROTATE 变换，等于抵消了变换 | 新增 `PlaceInFeedbackPlane`/`RotateInFeedbackPlane`，统一按 NoteEffects 平面空间放置；TouchHold 扇形改用 `localPosition` | ZOOM 2 倍时触发 Touch/TouchHold，判定文字、FAST/LATE、命中特效与音符同步缩放 |
| 暂停拖动预览慢、切难度不生效 | 只把 debounce 换成 33 ms 限流，仍走 `pendingNotePreviewSend` 串行 + `Dispatcher.Invoke` | 每个拖动帧排一次同步 HTTP，前一个没回来就阻塞；重载待处理时又整体退回 50 ms debounce，于是拖动期间完全不发包 | Seek 改为“发布最新位置 + 单飞 worker 追赶”，不排队不阻塞 UI；即使有待重载也先发 Seek | 暂停拖动波形连续移动，View 跟随；拖动中切难度，重载在停手后 50 ms 内完成 |
| 暂停再播放四角闪一下 | 只把 intro 交接改成淡入 | `SetCoverAlpha` 用 `AudioTime < 0` 判定“需要全遮”，恢复播放瞬间时间仍为负，于是重放一次 intro 交接 | 判定改为仅在 intro 交接尚未完成时全遮；交接淡入改 `SmoothStep`，时长 0.6 s | 反复暂停/播放 10 次不得闪；歌曲入场四角亮度平滑 |
| 最高分拍刷子无效 | 只调整了分拍选择优先级 | 两个真实缺陷：菜单在无选区时直接 `return`（什么都不做）；`EmitCommasOnly` 遇到任何分拍都除不尽的余数时直接丢弃该段时长，并把 `{n}` 写到音符之后 | 无选区时对整谱执行；余数用 `384/gcd` 求出能整除的分拍，保证时长守恒；换拍标记回到音符之前 | 自动：`{12}`/`{24}` 混合分拍前后总 unit 相等，多行不被压成一行 |

### 2.3 第三轮：实际用你的谱面跑解析器之后

前两轮都在猜。这一轮把你给的两段谱面直接喂给解析器打印每个 note 的结果，
才看到真实链路，所以以下四项是有证据的，其余项列出仍缺的信息。

| 复现项 | 之前判断错在哪 | 实测到的根因 | 本轮修法 | 核对 |
|---|---|---|---|---|
| 播放时先出 `2^8` 再变 `8d`（第 2、4 项） | 一直当成预览问题 | 这是运行时：D-zone 路线在 `BuildDZoneSlidePath` 里被重建，弧线变长后需要 `Instantiate` 额外 bar。淡入用的是 prefab 上的 Animator，它的 alpha 曲线只绑定 prefab 自带的 bar，克隆出来的 bar 淡入期间 alpha 一直是 0，所以先看到到 A8 的短弧，音符开始瞬间末端才补上到 D8。`-`/`p`/`q`/`s`/`z` 变形后不会变长、不克隆 bar，所以只有 `^`（以及 `<`/`>`）出现 | 路线一旦被重建就不再用 Animator，改由代码按同一条 0→0.55 曲线驱动全部 bar 的 alpha | 自动：`2?^8dm[12:1]` 必须解析成带 D-zone/Mine/no-head 的 Slide；整段谱面里 4 条弧线全部到达播放层 |
| `4[12:1]` 报“修饰符未知错误” | 上一轮改成静默当 Hold | 修饰符解析其实接受它；报错来自后面的 Hold/Slide 分支。而且真正会报 `SLIDE CHAIN ERROR` 的是另一个原因：`[12:1]` 在 BPM 无效时算不出秒数，错误信息却写成“组合星星有错误” | 取消 Hold 猜测：缺 `h` 直接报错并给出改法（`时长必须配合 h 写成 Hold（例 4h[12:1]）`）；BPM 无效单独报 `星星时长需要有效 BPM` | 自动：`4[12:1]`/`8[8:1]`/`A1[8:1]` 运行时与语法检查都必须报错，且错误文本含 `4h[12:1]`；无 BPM 时错误文本含 `BPM` |
| 最高分拍刷子留下较小 `{}` | 上一轮用 `384/gcd` 造出 `{48}` 这类没人写过的分拍 | `{12}1,{32},,1,` 会被输出成 `{32}1,,,,{48},{32}1,`，标记被拆成三段 | 规则简化成你说的两条：整段 unit 能被最高分拍整除就转成最高分拍；除不尽就按原分拍原样保留，不发明分拍、不丢 unit、不把 `{n}` 挪到音符后 | 自动：`{16}1,{32},,1,,,,1,,,` → `{32}1,,,,1,,,,1,,,`；`{12}1,{32},,1,` 原样保留；前后总 unit 恒等 |
| Fake Hold 结束不销毁 | 上一轮只把 `previewOnly` 与 `isFake` 分开 | 分开之后 `FakeNoteLifetime` 给所有 Fake 一律留 0.35 s 宽限，而真 Hold 在 `remainingTime == 0` 当帧就销毁，所以 Fake Hold 会多留 0.35 s | Hold/TouchHold 的 Fake 宽限改为 0，其余类型保留 0.35 s | Fake Hold 尾端到达即消失；Fake Tap 仍保留短暂 miss 窗口 |

仍需你提供信息才能继续的项（我不再猜）：

| 项 | 我这边实测到的状态 | 需要你给的东西 |
|---|---|---|
| 第 1 项报“修饰符位置错误” | 你给的整段谱面加上 `(120)` 之后，解析器 0 错误、4 条弧线全部生成 | 出错时那份谱面的开头几行（`&inote` 之前的 BPM、`{}`，以及是否在 overlay `@` 段里） |
| 第 13 项增量预览 | `2dv4`、`E1-E7-E5`、`E1-E7-E5-E3[8:1]` 在编辑器整条链路（取 caret 组 → 展开 → 生成预览谱面）都能出预览 | 复现时的整行文本与光标位置；或确认是重编译前的现象 |
| 第 12 项黄色光标 | 采样点确实取在 closing comma 推进之前 | 一个具体例子：哪一行、caret 在第几个字符、你期望的时间 |
| 第 14 项 `!` 渐入 | 编辑器 `starSpeed` 默认 0，`!` 会被强制成 1.0 并把星星 alpha 压到 0；JSON 字段链路已确认存在 | 重编译后是否仍渐入 |

### 2.4 第四轮：遮罩/光圈与暂停→播放

| 复现项 | 实测到的根因 | 本轮修法 | 核对 |
|---|---|---|---|
| 第 5 项：MOVE 后遮罩与画面重叠、ZOOM 后外圈亮度不一致、弧线不缩小 | `Notes`/`NoteEffects`/`Outline`/背景都跟随 ZOOM/MOVE，但两层遮罩没跟：`1080Circle_Rev`（外圈圆框）与 `BackgroundCover`（内圈压暗）始终停在作者写的 1080 半径。所以 MOVE 后固定的框与移动后的判定圈错位（一边重叠一边留缝），ZOOM 缩小后圆框半径不变（看起来“弧线没缩小”），圆框与缩小后的画面之间那一圈也没有被压暗 | 两层遮罩加入 ZOOM/MOVE（不跟 ROTATE：可见部分是圆，且轴对齐才能让四块黑挡板严丝合缝）；四块黑挡板每帧按当前光圈边界重算，左右满高、上下补光圈宽度，`OnPostRender` 还原；同时把 HUD 文字与歌曲卡从 zoom/move 目标里去掉 | 自动：`ScreenEffectController` 必须把两层遮罩注册为 skipRotation 目标并调用 `FitOuterCoverToAperture`/`RestoreOuterCoverLayout`，且不再有 `IsViewportMask`。人工：ZOOM 放大/缩小、MOVE 上下左右，外部亮度一致、无缝无重叠，圆框跟着画面缩放 |
| 暂停拖动后 slide/touchslide 残留一堆 | `Tap`/`Hold`/`Touch`/`TouchHold`/`StarDrop` 都有“暂停预览且光标已过 → 自己隐藏”的分支，`SlideDrop` 与 `TouchSlideDrop` 没有；暂停预览不判定，所以没人回收它们 | 两者补上同样的分支（`time + LastFor` / `time + duration`） | 自动：两文件必须含该分支。人工：暂停来回拖动，路径与导引星随光标出现/消失 |
| 预览→播放要卡一下、四周黑边突然消失 | `TimelinePreview` 用 `previewOnly: true` 载入，这些 note 永远不判定，所以 View 直接拒掉 `Continue`（"send Start"），编辑器只能发 `Start`：整谱重载 + 皮肤/BGA/显示时间轴重配 + 封面淡入重新起一次 | 改成共享已加载状态：`Continue` 带 `jsonPath`，View 只把预览音符换成可判定音符（`LoadJsonImmediate` 按恢复位置忽略过去的音符，与 `Start` 行为一致），皮肤、BGA、显示/媒体时间轴一律不重载；编辑器发 `Continue` 前先等在途的 `TimelinePreview` 落地，避免它反过来把 View 拉回预览模式 | 自动：View 不得再含 "Continue cannot resume a timeline preview"，须含 `Continue from a timeline preview requires jsonPath.`；编辑器须含 `resumeFromTimelinePreview: resumePreview` 与在途预览 drain。人工：暂停 → 拖波形 → 播放，不应有整谱重载的顿卡与黑边闪 |
| Fake Hold（你的补充） | 0.4.2/0.4.40 根本没有 fake，`isFake` 不存在；Alpha 唯一的机制是 `FakeNoteLifetime`，因为 `JudgmentDisabled` 会让 `FixedUpdate` 提前 return，而真 Hold 的销毁写在判定块里 | 只改宽限值：Hold/TouchHold 用 0（与真 Hold 同帧消失），其余类型保留 0.35 s 的 miss 窗口；不新增任何判定相关行为 | Fake Hold 尾端到达即消失 |

### 2.5 第五轮：错误信息与断言整理

先做了一次全量清点，五个诊断产生者（`SlideSyntaxValidator`、`SimaiProcess`、`JsonDataLoader`、`MainWindowCore`、`SyntaxCheck`）的措辞、语言和是否带出错文本各写各的。统一成一条规范：**第一行中文，第二行同义大写英文，两行都把出错的那段谱面文本抄出来**；英文行里只有被引号包住的形状名、修饰符字母和出错文本可以是小写。

| 问题 | 现状（改前） | 本轮修法 | 核对 |
|---|---|---|---|
| 共享校验器只有英文 | `SlideSyntaxValidator` 的 19 条全是纯英文单行，中文用户看到的是 `Straight Slide endpoints are too close.` | 全部改成中英双行并带出错段落，新增内部 `Diagnose(中文, ENGLISH, 出错文本)` 作为全项目唯一拼装口径 | 自动：`CheckDiagnostics` 把整份非法语料跑一遍，逐条断言两行、中文行、英文行纯 ASCII 且大写、必须带出错文本 |
| 一句话对九种原因 | `组合星星有错误\nSLIDE CHAIN ERROR` 被 9 个互不相干的分支共用；`修饰符位置错误` 不带 note 文本，就是你说的“根本看不懂是哪” | 按原因拆开：内容为空 / 路径无法解析 / 存档路径不合法 / 时长写法错误 / 时长条数不匹配 / 修饰符位置（并说明主体只能写 `b`/`m`）；同头星星也拆成“无法拆分”和“每条都必须是星星” | 自动：断言三种不同链路问题产生三条不同文本。人工：`1-3b-5[8:1]` 与 `1-5[8:1]-7-3[8:1]` 报不同的错 |
| 遗留日文半句 | `JsonDataLoader` 里 18 处 `中文\nスライドエラー`，日文半句对所有形状都只写“slide 出错”，等于没有信息（v0.1.0 就是这样） | 换成与校验器同一套中英双行文本，并带出错段落 | 自动：仓库内不再有 `スライドエラー` |
| 编辑器靠中文子串反查翻译 | `LocalizeViewValidationMessage` 用 `Contains("星星不合法")` 之类匹配，再拿 `message[0]` 当形状名填进 `{0}`——改一个字就静默退化成原文 | 改成抛 `ChartValidationException`（携带 resx key 与参数），编辑器按 key 查表；30 多处中文字面量换成 key | 自动：`MainWindowCore` 不得再出现中文 `throw new Exception`。人工：切 en-US / ja，谱面报错跟着换语言 |
| 双语信息被压成一行、丢一半 | 诊断先 `Replace('\n', ' ')` 才进列表 | 改成 `" / "` 连接，两半都留 | 人工：错误列表里能同时看到中英 |
| 未知 Alpha 参数不说是谁 | 参数不合法时只报 `Alpha 语法格式错误` | 新增 `AlphaArgumentError`（三种语言），报出命令名与参数原文 | 人工：`<SV*abc>` 报 “Alpha 命令 SV 的参数无法解析：abc” |
| 其他 | `BasicParseErrorRenderer` 里硬编码“谱面语句无法解析”；`Langs.ja.resx` 的 `SyntaxError` 是没翻的英文 | 前者改查 `ChartStatementInvalid`，后者补日文 | 切换语言看编辑器下划线提示 |

### 2.6 第六轮：音符 AST 接线与 Touch 距离继承

这一轮做两件事：把「什么是音符」收敛成一个解析器，然后在这个解析器上加第一条继承语法。

| 问题 | 现状（改前） | 本轮修法 | 核对 |
|---|---|---|---|
| 四层各自判断音符种类 | `getSingleNote`、`SyntaxCheck.NoteSyntaxCheck`、`NotePreviewModule`、`IsTap/IsHold/IsTouch` 各写一套 `Contains('h')` / `Contains('[')` / `isSlideNote()`；同一段文本可以在编辑器报红、在播放正常，或者反过来 | 三层全部改为调用 `NoteExpressionParser.TryParse`：种类、位置、修饰符、时长只在 MajdataCore 判定一次。预览的宽松度作为 `forPreview` 参数留在同一个入口里，而不是各调用方自己放宽 | 自动：`CheckNoteExpression` 拿 47 条语料逐条比对 AST 与运行时的接受/拒绝、种类、位置；`CheckSyntax` 全量语料要求编辑器与运行时结论一致 |
| 旧校验器留在原地 | `SyntaxCheck.cs` 里 `IsNote/IsTap/IsHold/IsSlide/IsTouch/SlideSyntaxCheck/SlidePathCheck/HoldSyntaxCheck/RatioSyntaxCheck` 等 648 行已经没人调用，但随时可能被再次接上去 | 全部删除（`IsNum/IsInteger/IsFloat` 仍被 BPM、分拍检查使用，保留）；`SimaiProcess` 里的 `isSlideNote/isTouchNote/IndexOfDuration/ModifierPositionError` 和 `SlidePathParser.TryReadKeyPosition/KeyPositionData` 同样删除 | 自动：测试工程编译这两个文件，删多了会直接编译失败；`SyntaxCheck.cs` 1109 → 421 行 |
| Hold 与 Slide 的时长解析是两套 | `getTimeFromBeats` 与 `getStarWaitTime` 各自手写 `#` 计数，`[#2]`（绝对秒）只有 Hold 那套认，共享校验器判非法；`[3##8:1]` 反过来只有共享校验器认 | 两个函数改为调用 `SlideSyntaxValidator`；同时给共享校验器补上 `[#秒]`，并用 `hasDelay` 区分「写了 `[0##8:1]`」和「没写延迟」 | 自动：语料里 `1h[#2]`、`1h[3##160#8:1]`、`1-5[#2]` 的接受与秒数；人工：`1h[#2]` 在编辑器不报红且播放长度为 2 秒 |
| 新语法：Touch 距离继承 | 无 | `E1~[4.8]`：保留 E1 的方向与判定感应区，只改绘制距离。距离与 SPAWN/DESTROY 同单位（判定圈 4.8，B 2.3 / E 3.0 / A·D 4.1），范围 0–10。`~[]` 括号在解析时属于位置，先于 Hold 时长被吃掉，所以两个方括号不会互相误认 | 自动：`CheckTouchRadius` 共 19 条非法组合 + 接受、回写、`SimaiNote.touchRadius` 落地、预览、镜像保留。人工：写 `E1~[4.8]`，音符画在判定圈上，打 E1 感应区能判到 |
| 新语法的边界 | — | 键位（`1~[4.8]`）、C 区（没有方向）、TouchHold（扇形与距离绑定）、星星内部（`A1~[3]-A5[8:1]`）四种情况各报专属双语错误，不静默接受也不落到「修饰符位置错误」 | 自动：上面 19 条逐条断言拒绝 + 诊断格式；人工：帮助页「Touch 自定义距离」一节 |

### 2.7 第七轮：形状表与时隙拆分收敛到一处

第六轮统一的是「什么是音符」，这一轮统一「这条星星画哪个 prefab」和「一个逗号槽里有几个音符」——这两件事之前还是各层各写一套。

| 问题 | 现状（改前） | 本轮修法 | 核对 |
|---|---|---|---|
| 形状语法有三份 | `JsonDataLoader.DetectShape(段)` 按解析结果判，`JsonDataLoader.detectShapeFromText(文本)` 260 行按文本再判一次，`MainWindowCore.ValidateSlideShapeForView(文本)` 又是第三份（编辑器为了播放前报错）。三份的判定顺序和取子串方式都不同 | 新增 `Assets/MajdataCore/SlideShapeResolver.cs`：段 → prefab key + 拒绝原因（枚举 + 双语文本）。View 的 `DetectShape` 变成 3 行转发，`detectShapeFromText` 删除，编辑器改成 `ResolveSlideShapeForView(段)` 只做「枚举 → resx 键」的翻译（155 行 → 24 行） | 自动：`CheckSlideShapeResolver` 把旧文本语法作为 oracle 放在测试里，`14 形状 × 8 起点 × 8 终点` + `V 的 8×8×8` 共 1408 组合逐个比对 key 或拒绝原因一致；并断言两个文件里不再出现 `Split('>')` 这类文本扫描 |
| prefab 白名单有两份 | 编辑器 `ViewSlideShapes` 手抄了 View 的 `SLIDE_PREFAB_MAP` 键 | 白名单移入 `SlideShapeResolver.SupportedPrefabKeys`，编辑器只调 `IsPrefabKeySupported` | 自动：测试直接读 `JsonDataLoader.cs` 里的 `SLIDE_PREFAB_MAP`，断言两边键集合完全相等（以后加 prefab 忘了同步会红） |
| D 区路线在文本层直接崩 | `4d-8`、`1dV35` 这类文本进入按固定偏移取子串的旧实现时，`int.Parse("4d")` 抛格式异常，于是同一条路线在不同层结论不同 | 全部走解析结果，不再看文本 | 自动：`4d-8 / 2d^8 / 1dV35 / 3dpp7d / 5drq1` 断言与去掉 `d` 的写法解析到同一个 key，同时断言旧 oracle 在这些输入上确实会失败（说明这是真修掉的 bug） |
| Wifi 与 V 拐点也在扫文本 | `InstantiateWifi` 开头 `noteContent.Substring(0,3).Split('w')`；`detectJustType` 按字符位置取终点；`SlideDrop.TryGetLargeVTurnPosition` 找 `'V'` 后面那个字符 | 三处都改成读解析出来的段（`startPosition` / `endPosition` / `middlePosition` / `middleIsDZone`）；`InstantiateWifi` 里那段算完从未被读取的相对终点一并删掉 | 自动：形状矩阵覆盖 `isJustR` 依据的起终点关系；人工：`1w5`、`4dw8`、`1V35` 与 `1dV35` 的星星朝向与拐点位置 |
| 时隙拆分有三份且互不一致 | `getNotes` 认「两位数字 = 两个 Tap」（`12`），`SyntaxCheck` 不认（报红），`NotePreviewModule` 也不认（不预览）。即你担心的「编辑器报红但能播」真实存在一例 | 新增 `NoteSlotParser.SplitTopLevel` / `TrySplit`（两位简写 + `/` + `*` 展开，`*` 分支带 `fromSameHead` 和 `groupIndex`）。运行时、语法检查、预览统一调用 | 自动：`CheckLayerAgreement` 用 253 条语料断言「编辑器结论 == 运行时结论」，且编辑器放行的每一条都能过播放门禁；另有 `12` 在拆分/预览/播放三处都是两个 Tap 的断言 |
| 同头星星一半能出 | 之前 `getSameHeadSlide` 整组抛错；改成逐条容错后，`1*-5[8:1]` 的头（Tap）报错但尾巴仍作为无头星星出现在场上 | 一个 `*` 组按 `groupIndex` 整组成败：组内任一分支失败，整组不出，只报该组的错，`/` 的其他兄弟音符照常出 | 自动：`1*-5[8:1]/3` 断言只剩 `3` 且有报错；`1*-5[8:1]` 断言 0 个音符 |

### 2.8 第八轮：可视化编辑写回改走 AST

可视化面板点出来的音符要合并进光标所在的逗号槽。这段逻辑（`MainWindowCore` 里 277 行）全靠字符偏移判断「这是不是星星」「头是哪个键」「尾在哪」，于是会写出解析器不接受的文本。现在整段抽成 `MajdataEdit/Editor/VisualNoteEditor.cs`（不依赖 WPF，测试工程直接编译），所有判断改问共享解析器。

| 问题 | 现状（改前） | 本轮修法 | 核对 |
|---|---|---|---|
| D 区头被切掉一个字符 | 同头合并做 `incoming.Substring(1)`，`4d-8d[8:1]` + `4d-1d[8:1]` 写成 `4d-8d[8:1]*d-1d[8:1]`，展开后是 `4dd-1d[8:1]` | 分支文本由解析出来的 head 与各段结构化渲染，`*` 后面不再重复头 | 自动：`4d-8d[8:1]*-1d[8:1]` 断言 |
| 接到 D 区尾巴上会多一个 d | 串接做 `incoming.Substring(1)`，`1-5d[8:1]` + `5d-8[8:1]` 写成 `1-5d[8:1]d-8[8:1]` | 同上，串接时还会去掉分支自己的头修饰符（`b` 落在两段之间会让解析失败） | 自动：`1-5d[8:1]-8[8:1]`、`1-5[8:1]/5b-8[8:1]` 合并成 `1-5[8:1]-8[8:1]` |
| 拆分连段丢 D 区、丢时长 | 按字符找拆分点，`4d-8d-3d[8:1]` 点 8 拆出 `8-3d`（少了 `d`）；`1-5-8[8:1]` 点 5 拆出没有时长的 `1-5`，编辑器立刻报红 | 按段拆分，两半都带上原来的时长（都没有就补 `[8:1]`） | 自动：`4d-8d[8:1]/8d-3d[8:1]`、`1-5[8:1]/5-8[8:1]` |
| 点同一个键会写出重复音符 | 变体循环只认死写的 `1`、`1b`、`1h[8:1]`、`1hb[8:1]` 四种。谱面里是 `1h[4:1]` 就匹配不上，于是在同一个键上再加一个 `1` | 按结构判断（同位置、非星星即为同一音符），循环 Tap→Hold→break Tap→break Hold；自己写的时长保留，`x`/`f`/`m`/`$` 这些不属于点击的修饰符一并保留；某一步写不出合法文本（例如 `$` 不能配 Hold）就跳过该步 | 自动：`1h[4:1]` → `1b`、`1x` → `1xh[8:1]` 等 10 条 |
| 两键简写会被写坏 | 槽里是 `12` 时再点一个键，写出 `12/3`——运行时按 `/` 拆开后 `getSingleNote("12")` 直接报错 | 槽的拆分统一用 `NoteSlotParser.SplitTopLevel`，`12` 先展开成 `1`、`2` | 自动：`1/2/3` 断言 |
| 同头组当成普通星星编辑 | `1-5[8:1]*-3[8:1]` 的整段文本被当作一条星星：加分支时判重失败（重复追加），改破头时按字符插 `b` | 组按「第一条分支 + 其余原样带走」处理；组没有唯一尾巴，所以不参与串接与拆分 | 自动：重复分支不追加、组头 break 切换 |
| 合并不出来时写坏文本 | 直接把拼出来的字符串写进谱面 | 合并结果先过解析器；不合法就退回「放在旁边」（`/` 并列），两者都不合法才保留合并结果 | 自动：30 槽 × 17 点击 × 3 动作 = 1530 组合，逐个断言写回的文本能被运行时解析 |

## 三、全部新增功能与行为改动

### 3.1 统一 Slide AST、位置与修饰符

| ID | 改动或新增功能 | 核心实现 | 如何核对 |
|---|---|---|---|
| AST-01 | 共享 Slide AST | `SlidePositionData`、`SlidePathSegmentData`、`SlidePathData` 统一表达 head、segment、turn、duration、modifier | 运行回归；解析后逐段 `ToExpression` 往返 |
| AST-02 | Key/Touch/D-zone/C2 一等位置 | 不再假设节点只能是 `1-8`；支持 `E1`、`4d`、`C/C1/C2` | 测 `E1-E2`、`4d-8d`、`C2-E1` |
| AST-03 | 共享 parser + semantic validator | Edit 解析、SyntaxCheck、预览、View fallback 共用 MajdataCore | 故意输入 unknown shape、断链、错误 turn，四层结果必须一致 |
| AST-04 | 完整与增量验证分离 | `TryValidate` 严格要求时长；`TryValidateForPreview` 允许暂缺最终时长 | 输入 `E1-E7-E5-E3` 后再补 `[8:1]` |
| AST-05 | duration 统一 | ratio、秒数、指定 BPM、delay、`[3##8:1]`、`[3##150#8:1]` 统一计算 | 回归 duration cases；View 不得 `double.Parse` 崩溃 |
| AST-06 | 连结 Slide 时长规则 | Key Slide 接受一条总时长或每段时长；Touch 多段逐段时长明确拒绝 | 测单总时长、每段时长、Touch 每段时长报错 |
| AST-07 | segment-specific Touch 分类 | 每段按实际 start/end 判定 Key/Touch 形状合法性，不再整条 path-wide | 测 `1<<5-A1` 等混合路径 |
| AST-08 | serialized AST 再验证 | View 不直接信任 JSON `slidePath`；有 expression 时重解析，无 expression 时校验 DTO | 构造断链/冲突 DTO，View 应拒绝该 note 而非崩溃 |
| MOD-01 | head/body 修饰符分区 | head 接受对应 `b/x/f/m/!/?/$`；Slide body 只接受 `b/m` 且只在末段 | 测 `1-5mq8`、Hold `$`、Touch `$` 等错误组合 |
| MOD-02 | `!` / `?` 独立语义 | `?` no-head + fade；`!` no-head + no fade | 回归 + Unity 并排观察 |
| MOD-03 | `$` / `$$` 独立语义 | `$` 强制静止星；`$$` 强制旋转星 | 测 Tap 星旋转 |
| MOD-04 | `h` 保持 Hold marker | 不把 `h` 当通用 flag；短 Hold 不依赖 modifier 顺序 | 测 `1h`、`1bh`、TouchHold |
| MOD-05 | 单个坏 note 不拖垮 `/` siblings | 每个 sibling 单独 try/catch，错误 note 跳过并标红 | `1/1r5[8:1]` 应保留 `1` |

### 3.2 SV、HS、SPAWN、DESTROY、BOUNCE、FAKE

| ID | 改动或新增功能 | 核心实现 | 如何核对 |
|---|---|---|---|
| ALPHA-01 | typed SV/HS 原子解析 | 任一 key/value 非法则整条命令拒绝，不再部分生效 | 测混合合法/非法列表 |
| ALPHA-02 | NaN/Infinity 拒绝 | SV、HS、SIZE、ALPHA、BOUNCE 只接受 finite number | 回归非有限值 |
| ALPHA-03 | SV stream/type 曲线 | global、overlay stream、typed note curve 独立累计并按 sourcePosition 稳定排序 | 同时间多条命令与 overlay 对比 |
| ALPHA-04 | Slide SV 固定 authored duration | 正净积分归一到谱面时长；零/负净积分不反向伪归一，到 authored end 截止 | 测 0.5、pause、negative-net |
| ALPHA-05 | typed HS Slide 渐入 | `HS*slide` 只覆盖运动星渐入速度；全局 HS 不影响 Slide，轨迹仍由 `SV*slide` 驱动 | SyntaxCheck、帮助与 Unity 一致 |
| ALPHA-06 | `slidestar` 独立视觉目标 | COLOR/SIZE/ALPHA 及 V 系列可只改 moving guide star | 对比 `star`、`slide`、`slidestar` |
| ALPHA-07 | `SV*slide` 保留 | Slide path 速度由 SV 积分控制；不支持 `SV/HS*slidestar` | SyntaxCheck + Unity path |
| ALPHA-08 | SPAWN/SPAWNMODE/DESTROY | typed、break、each、stream 状态和 reset fallback 统一 | Edit 波形与 View 半径一致 |
| ALPHA-09 | BOUNCE + HS/SV | 以有效 HS 和 SV 积分驱动抛物线 phase | 执行第四节 Bounce 组合 |
| ALPHA-10 | SPAWNMODE Once/Rewind | Once 首次越过 spawn 后保持；Rewind 随负 SV 回退隐藏 | 正/负 SV 往返 |
| ALPHA-11 | FAKE 状态 | FAKE 命令按 stream/type 生效；Fake 禁判定但仍按正式生命周期结束 | Fake Tap/Hold/Slide |
| ALPHA-12 | Visual V 回放优化 | COLORV/SIZEV/ALPHAV 用 active kind 集合 O(changes + notes) 重放 | 大量 visual events 拖动时间轴 |
| ALPHA-13 | overlay 命令时序与 caret | overlay 独立 stream；命令位置按对应 timing comma，不回退到 stream start | overlay caret 与播放时序 |

### 3.3 TouchSlide、D-zone、镜像与路径几何

| ID | 改动或新增功能 | 核心实现 | 如何核对 |
|---|---|---|---|
| GEO-01 | D-zone path 变形 | Key ring 与 D ring 混合路径按 endpoint offset 重新采样 | `2d-6`、`2-6d`、`2d-6d` |
| GEO-02 | `pq/ppqq` 自适应切线 | 保留 line-circle tangent 点并按长度自适应采样 | 慢速观察连接点，不得出现圆滑假拐角 |
| GEO-03 | B-area 边界修复 | 避免 pq 涉及 B 区时退化成直线 | Touch B/E/A 混合样例 |
| GEO-04 | V/尖角方向 | 精确角点优先 incoming tangent，经过角点后再转 outgoing | `2dv4` 与 Touch V |
| GEO-05 | authored lifetime | Slide/Wifi/TouchSlide 判定与销毁以声明时长结束，不被 SV 延长 | 慢 SV、停 SV |
| GEO-06 | 镜像字母区域 | A/B/D/E、C alias、D-zone 根据 LR/UD/rotation 映射 | 两次镜像应还原 |
| GEO-07 | pq/ppqq chirality | LR/UD 交换 p/q、pp/qq；180°/45°旋转保持 chirality | 回归 mirror assertions |
| GEO-08 | `<` / `>` 环方向 | 反射时翻转，旋转时保持 | Touch ring mirror |
| GEO-09 | Alpha command 保护 | Mirror 用 AlphaCommandBoundary，不把 Touch `<` 当 Alpha 命令 | 镜像 `A1<E5,<HS*2>` |

### 3.4 编辑器预览、波形与共享时间轴

| ID | 改动或新增功能 | 核心实现 | 如何核对 |
|---|---|---|---|
| EDIT-01 | caret note 增量预览 | complete AST 无时长可临时补 `[4:1]`；无效 sibling 不清空有效 sibling | 本轮 #1/#4/#13 |
| EDIT-02 | Stop standby preview | 终止状态继续使用 isolated preview JSON，不激活 DJAuto | Stop 后输入单 note |
| EDIT-03 | Pause shared timeline | Pause/Seek 不终止 View；拖动直接移动 View 时间 | 本轮 #3/#8/#9 |
| EDIT-04 | 可逆对象 | previewOnly note 越过时间只隐藏/复位，不自毁 | 前后往返拖动 |
| EDIT-05 | preview 替换 staging | 正式对象 ready 后才清除 timeline preview | 快速拖动后播放 |
| EDIT-06 | HTTP 去抖与顺序 | preview timer 合并；pending request drain；control generation 丢弃旧回调 | 快速操作 10 次 |
| EDIT-07 | 波形中心 viewport | 时间窗对称于 playhead，窗口两边 resize 行为一致 | 本轮 #2 |
| EDIT-08 | resize 延迟重画 | WPF Stretch 提供即时反馈，稳定 75 ms 后 GDI 重画 | 连续拖窗口边 |
| EDIT-09 | 波形拖动性能 | 拖动期间不逐像素改 caret；MouseUp 才同步文本 | 长距离拖动波形 |
| EDIT-10 | 黄色 caret slot time | closing comma 推进前采样 | 本轮 #12 |
| EDIT-11 | 最高分拍 | 所有可整除间隔统一到最大 beat | 本轮 #10 |
| EDIT-12 | Media timeline | TimelinePreview 时重放 display/media/screen effect；异步媒体准备后补 paused time | 有视频、字幕、effect 的 paused seek |

### 3.5 View 播放协议、显示和对象生命周期

| ID | 改动或新增功能 | 核心实现 | 如何核对 |
|---|---|---|---|
| VIEW-01 | protocolVersion + structured response | View 返回 `{ok, protocolVersion, error}`；Edit 显示具体错误 | 故意版本不匹配/无效 Record |
| VIEW-02 | HTTP listener 断线恢复 | client IOException/HttpListenerException 不终止 listen loop | 请求中断后再次 Start |
| VIEW-03 | 两阶段 Start | 先 load/bind，后 Continue 发布未来 clock anchor | 快速 Start/Pause |
| VIEW-04 | future clock 不提前判定 | `scheduledStart` 到 realtime anchor 后才 `isStart` | 100 ms lead 内不得判 note |
| VIEW-05 | TimelinePreview/Continue 防竞态 | in-flight preview 强制 fresh Start；View 对 preview 上 Continue 返回 error | 本轮 #6 |
| VIEW-06 | Fake 与 preview lifetime 分离 | Fake 正常结束；timeline preview 保持可逆 | 本轮 #8 |
| VIEW-07 | Slide/Wifi helper 清理 | 销毁 moving star 后置 null；TouchHold 清理 holdEffect | 反复 Stop/Reload 观察 Hierarchy |
| VIEW-08 | sensor/input 清理 | reload 前标记 IsReloding，解绑 sensor、清 slots，避免 DJAuto 残留 | Pause/Stop/Start 后 AP |
| VIEW-09 | 判定反馈共同变换 | Touch/TouchHold 判定、JUST、粒子挂到 NoteEffects | 本轮 #5 |
| VIEW-10 | viewport mask 隔离 | BackgroundCover、circle、Mask/Cover/Background/full-screen RawImage 不参加 gameplay transform | ZOOM<1 不得露黑边 |
| VIEW-11 | JudgeText 总开关 | reusable 与动态 renderer 统一受 alpha guard 控制 | Toggle ShowJudgeText |
| VIEW-12 | Mine feedback 开关 | 只屏蔽 isMine feedback，不改变普通 note | Mine/普通 note 同时测试 |
| VIEW-13 | 屏幕效果保护 Animator | OnPreCull 捕获 Animator 完成后的 base transform，OnPostRender 恢复 | SongDetail 入退场 + effects |
| VIEW-14 | 遮罩亮度 | Inner/OuterBrightness 继续只控制 cover alpha | 0/0.5/1 三档 |
| VIEW-15 | 录制谱师字体 | Aileron 优先，系统/正文 fallback | 本轮 #7 |

### 3.6 录制、音频与导出

| ID | 改动或新增功能 | 核心实现 | 如何核对 |
|---|---|---|---|
| REC-01 | 单一录制停止所有者 | 移除 DestroySelf 的 StopRecording；AP 动画或 cutoff 负责停止 | AP 开/关各录一次 |
| REC-02 | 无 AP 结束后 5 秒 | `chartEnd + 5s` cutoff；有 AP 使用动画时长与尾留白 | 对比输出总时长 |
| REC-03 | ffmpeg 有界收尾 | 保存 Process；15 秒 deadline；超时 terminate；OnDestroy 清理 | 模拟 encoder 不退出 |
| REC-04 | Record 重入拒绝 | PrepareRecording 失败返回 structured error，不假成功 | 连点两次 Record |
| REC-05 | Mine 音频内存 | 多类型 mine 改为两个共享 full-song buffer，mixdown 时按类型音量 | 长谱录制观察峰值内存 |
| REC-06 | 实时音量进入导出 | BGM/answer/mine 等当前音量进入 mix | 修改音量后录制比对 |
| REC-07 | SongDetail cache | Edit 预烘焙 base/overlay/full；View 使用同一 card 几何 | 预览图与录制首屏比较 |

### 3.7 兼容性、帮助、CI 与工程清理

| ID | 改动或新增功能 | 核心实现 | 如何核对 |
|---|---|---|---|
| COMP-01 | Mine JSON 旧字段 | `isMonoHead`、`isSlideMono` 反序列化别名 | 旧 majson |
| COMP-02 | 语言完整性 | en-US、ja、zh-CN 保持相同 resource key | 自动比较 key 集合 |
| COMP-03 | Alpha 帮助 | FAKE、V 系列、SPAWNMODE、`1f`、`!/?`、Touch V turn 参数 | Tool → Help 三语言 |
| COMP-04 | 移除 EX x 的 Alpha 帮助 | `x` 不是 Alpha 新功能，不在 Alpha help 冒充新增语法 | 搜索 help |
| COMP-05 | SPAWNMODE 顺序 | 帮助中位于 SPAWN 与 DESTROY 之间 | 自动断言 |
| COMP-06 | AppleDouble 防编译 | csproj 排除 `._*.cs`、`.DS_Store`；bundle 清理 | Windows build 不得 CS2015 |
| COMP-07 | Unity `.meta` 完整 | MajdataCore 与新增 runtime script 配套 meta | Unity 不得重新生成 GUID |
| COMP-08 | CI 定向回归 | `.github/workflows/slide-ast-regression.yml` 覆盖 parser、View、Editor 相关路径 | 修改相关文件触发 CI |
| COMP-09 | 独立测试工程 | 测试不混入 Assembly-CSharp，不依赖 NUnit | `dotnet run` |
| COMP-10 | fix bundle | 按 alpha 根目录保留相对路径，包含源码、CI、Tests、矩阵 | 覆盖复制后按第五节编译 |

## 四、关键人工验收脚本

### 4.1 语法与增量预览

依次输入并观察每一步：

```text
{16}5/2?^8dm[12:1],
E1-E7-E5-E3[8:1],
2dv4,
1?-5[8:1]/1!-5[8:1],
```

通过条件：

1. 合法 note 不标红；错误 sibling 单独标红并消失；
2. 增量路径每完成一个 endpoint 就更新；
3. `?` 与 `!` 路径出现时间相同，只有 guide star 淡入不同；
4. `2dv4` 中心方向沿前半进入方向。

### 4.2 Pause/Seek/快速控制

1. 正常播放 3 秒；
2. 按 Pause；
3. 波形向后拖 2 秒，再向前拖 4 秒；
4. 立即 Play、Pause、Play；
5. 重复 10 轮。

通过条件：

- Edit 音频与 View 时间不分叉；
- View 不冻结、不闪空、不短暂进入 DJAuto；
- Slide/Touch/Hold/Fake 在前后 seek 时正确隐藏或恢复；
- Hierarchy 不持续累积旧 moving star、Touch effect 或 sensor slot。

### 4.3 Zoom、判定与遮罩

在同一段放置 Tap、D-zone Tap、Touch、TouchHold、Slide，执行 ZOOM/MOVE/ROTATE：

- note、判定文字、FAST/LATE、JUST、弧线、发光必须同向同倍率；
- full-screen cover 与 mask 不跟着缩小，不露黑边；
- ShowJudgeText 关闭影响所有判定文字；
- ShowMineHitFeedback 关闭只影响 Mine。

### 4.4 BOUNCE + HS/SV

基础：

```text
<BOUNCE*1>,1,
```

分别加入：

```text
<HS*tap=2>
<SV*tap=0.5>
<SV*tap=2>
<SV*tap=-1>
<SPAWNMODE*tap=once>
<SPAWNMODE*tap=rewind>
```

通过条件：

- HS=2 起飞到判定的实际时间缩短；
- SV=0.5 减慢，SV=2 加快；
- negative SV 让 Rewind phase 倒退；
- Once 首次出现后不因倒退再次隐藏；
- 最终判定时间不改变。

### 4.5 录制

分别录制：

1. ShowSongDetail 开 / AP 开；
2. ShowSongDetail 开 / AP 关；
3. ShowSongDetail 关 / AP 关；
4. 包含大量 Mine、TouchHold、PV 和屏幕效果的长谱。

检查字体、尾时长、音量、内存、ffmpeg 退出和输出文件完整性。

## 五、编译与复制后核对

### 5.1 Windows 编辑器

```powershell
cd E:\MaiChartAssistant\alpha
dotnet restore .\MajdataEdit\MajdataEdit.csproj
dotnet build .\MajdataEdit\MajdataEdit.csproj -c Release
```

不得出现：

- `CS2015 ._*.cs 是二进制文件`；
- `CS0136 tailScrollPosition`；
- MajdataCore 重复编译或缺失引用；
- resx/resource key 缺失。

### 5.2 Unity

1. 使用项目指定 Unity 版本打开 `alpha`；
2. 等待 AssetDatabase 和脚本编译完成；
3. Console 清空后重新进入 Play；
4. 按第四节执行 View 验收；
5. Build Settings 生成一次目标平台构建。

### 5.3 定向测试

```powershell
dotnet run --project .\Tests\SlideAstRegression\SlideAstRegression.csproj
```

当前预期结果：

```text
PASS: 5110 assertions and 20000 malformed-input cases
```

## 六、文件覆盖索引

| 子系统 | 主要文件 |
|---|---|
| 共享 AST/语义 | `Assets/MajdataCore/AlphaVisualTiming.cs`, `NoteModifiers.cs`, `SlidePathParser.cs`, `SlideSyntaxValidator.cs` 及 `.meta` |
| Edit parser/model | `MajdataEdit/SimaiProcess.cs`, `Majson.cs`, `SyntaxModule/SyntaxCheck.cs`, `Mirror.cs` |
| Edit preview/wave | `NotePreviewModule.cs`, `Editor/BeatFormatBrush.cs`, `MainWindowCore.cs`, `MainWindow.xaml(.cs)`, `MainWindow.MediaTimeline.cs` |
| Edit 设置/帮助 | `Editor/AlphaCommandHints.cs`, `Langs/*.resx`, `SubWindow/EditorSettingPanel.*`, `SoundSetting.*`, `RecordVideoWindow.*` |
| View loader/protocol | `Assets/Scripts/JsonDataLoader.cs`, `HttpHandler.cs`, `AudioTimeProvider.cs`, `Majson.cs`, `SvController.cs` |
| View notes | `Assets/Scripts/Notes/NoteDrop.cs`, `TapBase.cs`, `TapDrop.cs`, `StarDrop.cs`, `HoldDrop.cs`, `SlideDrop.cs`, `WifiDrop.cs`, `TouchBase.cs`, `TouchDrop.cs`, `TouchHoldDrop.cs`, `TouchSlideDrop.cs`, `EachLineDrop.cs`, `FakeNoteLifetime.cs` |
| View UI/effects | `UI/DisplayTimelineController.cs`, `LiveNoteVisualController.cs`, `MediaTimelineController.cs`, `ScreenEffectController.cs`, `SongDetailTemplateView.cs`, `BGManager.cs`, `CustomSkin.cs`, `ToggleFullScreen.cs` |
| 录制 | `ScreenRecorder.cs`, `Misc/DestroySelf.cs`, `MajdataEdit/SoundEffect.cs`, `ViewLocalization.cs` |
| Shader | `Assets/NoteColorTint.shader`, `Assets/Resources/AlphaScreenEffects.shader` |
| 构建/测试 | `MajdataEdit/MajdataEdit.csproj`, `.github/workflows/slide-ast-regression.yml`, `Tests/SlideAstRegression/*`, `.gitignore` |
| 交付 | `README.md`, `FIX_TEST_MATRIX.md`, `ALL_DIFF_TEST_CHECKLIST.md`, `/Users/zijianhu/Desktop/fix` |

## 七、发布前最终门禁

以下项目全部完成后才可称为“发布验证完成”：

- [x] 定向回归 5110 assertions；
- [x] 20,000 malformed-input fuzz；
- [x] 测试项目 0 error / 0 warning；
- [x] `git diff --check`；
- [x] fix bundle 字节一致且无 macOS 垃圾文件；
- [ ] Windows WPF Release build；
- [ ] Unity Console 0 error；
- [ ] 14 项 Unity/WPF 人工验收；
- [ ] 录制四种组合验收；
- [ ] 正式平台 Unity build；
- [ ] 发布前完整回归。
