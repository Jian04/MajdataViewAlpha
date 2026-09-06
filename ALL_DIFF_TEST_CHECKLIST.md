# Alpha v0.4.2 → 当前未提交 diff 全量验收清单

基线：`HEAD b179abc`（tag `v0.4.2`）。

用途：本文件不是 14 个已知 bug 的复测表，而是当前全部未提交 diff 的逐功能验收入口。每行应单独测试并勾选；一个组合测试可以同时覆盖多行，但不能因为自动测试通过就跳过 Unity/WPF 画面验收。

覆盖级别：

- `自动`：`Tests/SlideAstRegression` 有实际断言；
- `静态`：只检查源码接线、字段或分支存在；
- `手测`：没有有效自动覆盖；
- 当前自动结果：`PASS: 604 assertions and 20000 malformed-input cases`。

## 1. 共享 AST、语法、修饰符与预览

| 完成 | ID | 新增或变化 | 最小操作 | 通过条件 | 当前覆盖 |
|---|---|---|---|---|---|
| [ ] | AST-01 | Edit、SyntaxCheck、Preview、View 共用 Slide AST | 输入 `1-5[8:1]` | 四条链路都接受且路径一致 | 自动 |
| [ ] | AST-02 | Key/Touch/D-zone 都是一等 endpoint | 输入 `4d-E1-B3[8:1]` | 不按纯数字路径误判 | 自动+Unity |
| [ ] | AST-03 | `C/C1/C2` center alias | 分别输入 `C`、`C1`、`C2` | 都位于中心且分类正确 | 自动+Unity |
| [ ] | AST-04 | 完整验证仍强制 duration | 输入 `1-5` 后直接播放 | 标红/跳过，不阻止其他 note | 自动 |
| [ ] | AST-05 | 增量预览允许暂缺 duration | 逐字输入 `E1-E7-E5-E3` | 每完成一个 endpoint 即预览 | 自动+Unity |
| [ ] | AST-06 | `2^8dm` 精确识别 | 输入 `2^8dm`，再补 `[12:1]` | 预览不断；终点 8d；body 为 Mine | 自动+Unity |
| [ ] | AST-07 | `2?^8dm` head/body 修饰符分区 | 输入 `2?^8dm[12:1]` | `?` 属于 head，`m` 属于 body | 自动+Unity |
| [ ] | AST-08 | 一条总时长的多段 Key Slide | 输入 `1-3-5[8:1]` | 总时长分配正确 | 自动+Unity |
| [ ] | AST-09 | 每段时长的 Key Slide | 输入 `1-3[8:1]-5[4:1]` | 两段按各自时长播放 | 自动+Unity |
| [ ] | AST-10 | TouchSlide 每段时长明确拒绝 | 输入 Touch 多段且每段带时长 | 标红，不静默改成整条匀速 | 自动 |
| [ ] | AST-11 | delay duration | 输入 `[3##8:1]` | delay 与移动时长正确 | 自动+Unity |
| [ ] | AST-12 | 指定 BPM duration | 输入 `[3##150#8:1]` | 不崩溃，秒数正确 | 自动+Unity |
| [ ] | AST-13 | 非有限 duration 拒绝 | 输入 NaN/Infinity duration | 标红，不进入 View | 自动 |
| [ ] | AST-14 | path DTO 二次验证 | 手改 majson 制造断链 segment | 该 note 跳过，View 不崩溃 | 自动+Unity |
| [ ] | AST-15 | Touch/Key 逐 segment 分类 | 输入 Key→Touch 混合路径 | 每段按自身 endpoint 规则验证 | 自动 |
| [ ] | AST-16 | Touch 多圈 `<<` | 输入 `A1<<E5[8:1]` | 两圈同向且间距稳定 | 自动+Unity |
| [ ] | AST-17 | Touch 多圈 `<<<` | 输入 `A1<<<E5[8:1]` | 三圈同向；数字 Slide 同写法拒绝 | 自动+Unity |
| [ ] | AST-18 | same-head `*` | 输入 `1-5[8:1]*-7[8:1]` | 两支共用 head，均正常播放 | 自动+Unity |
| [ ] | AST-19 | Touch same-head | 输入 `A1-E2[8:1]*-B3[8:1]` | 两条 TouchSlide 都生成 | 自动+Unity |
| [ ] | AST-20 | 坏 `/` sibling 单独跳过 | 输入 `1/1r5[8:1]` | `1` 播放；坏支标红；整谱可播放 | 自动 |
| [ ] | MOD-01 | `?` 无头但保留 guide-star fade | `1?-5[8:1]` | 无 head；运动星渐入 | 自动+Unity |
| [ ] | MOD-02 | `!` 无头且无 guide-star fade | `1!-5[8:1]` | 路径出现时间不变，仅运动星无渐入 | 自动+Unity |
| [ ] | MOD-03 | same-head 继承 `!` | `1!-5[8:1]*-7[8:1]` | 两支运动星都无渐入 | 自动+Unity |
| [ ] | MOD-04 | `$` 静止 Star Tap | 输入 `1$` | 星形 Tap 不旋转 | 自动+Unity |
| [ ] | MOD-05 | `$$` 固定旋转 Star Tap | 输入 `1$$` | 星形 Tap 按固定规则旋转 | 自动+Unity |
| [ ] | MOD-06 | `f` 烟花 | 输入 `1f`、`Chf` | 命中时烟花；帮助有 `1f` | 自动+Unity |
| [ ] | MOD-07 | `h` 只作 Hold marker | 输入 `1h`、`1bh` | 都解析为短 Hold | 自动+Unity |
| [ ] | MOD-08 | Slide body 只允许 `b/m` | 对 body 加 `$`、`!`、`?` | 标红，不部分接受 | 自动 |
| [ ] | MOD-09 | Touch 不接受 `$` | 输入 Touch+$ | 标红 | 自动 |
| [ ] | MOD-10 | 非 Slide 不接受 `!/?` | 输入 Tap/Hold+`!/?` | 标红 | 自动 |

## 2. Alpha 命令与状态表

| 完成 | ID | 新增或变化 | 最小操作 | 通过条件 | 当前覆盖 |
|---|---|---|---|---|---|
| [ ] | CMD-01 | 全局 SV | `<SV*0.5>` 与 `<SV*2>` | 环形 note 分别减速/加速 | 自动+Unity |
| [ ] | CMD-02 | typed SV | `<SV*tap=0.5,hold=2>` | Tap/Hold 独立速度 | 自动+Unity |
| [ ] | CMD-03 | overlay stream SV | 主谱与 `@` 谱用不同 SV | 两个 stream 互不覆盖 | 自动+Unity |
| [ ] | CMD-04 | 同时刻命令按 sourcePosition | 同 timing 写两次 SV | 后写命令稳定生效 | 自动+Unity |
| [ ] | CMD-05 | typed SV 全-or-无 | 合法 pair 后接非法 pair | 整条命令拒绝 | 自动 |
| [ ] | CMD-06 | SV NaN/Infinity 拒绝 | `<SV*NaN>`、`<SV*Infinity>` | 表中不产生 point | 自动 |
| [ ] | CMD-07 | `SV*slide` 控制 path progress | `<SV*slide=0.5>` + Slide | path 受影响，仍在 authored end 结束 | 自动+Unity |
| [ ] | CMD-08 | 正净 Slide SV 归一 | 全程 `SV*slide=0.5` | 到声明时长仍走完 | 自动+Unity |
| [ ] | CMD-09 | 零/负净 Slide SV 截止 | Slide 区间内 0 或负 SV | 不强行正向走完，声明时长后消失 | 自动+Unity |
| [ ] | CMD-10 | HS global | `<HS*2>` | 支持的 note head 提速 | 自动+Unity |
| [ ] | CMD-11 | HS typed + NULL reset | `<HS*tap=2>` 后 `tap=NULL` | 恢复继承全局 HS | 自动+Unity |
| [ ] | CMD-12 | typed HS slide 渐入 | `<HS*slide=-0.5>` | 只覆盖运动星渐入；全局 HS 不影响 Slide | 自动+Unity |
| [ ] | CMD-13 | SV/HS 不支持 slidestar | 分别输入 typed slidestar | 拒绝；只由 visual 命令控制 | 自动 |
| [ ] | CMD-14 | SPAWN global | `<SPAWN*0>` / `<SPAWN*2>` | 出生半径变化 | 自动+Unity |
| [ ] | CMD-15 | SPAWN typed/reset | typed 值后 NULL | 对应 type 改变后恢复 | 自动+Unity |
| [ ] | CMD-16 | SPAWNMODE Rewind | 出现后切负 SV | 回中心并缩小，重新正 SV 可再次出现 | 自动数学+Unity |
| [ ] | CMD-17 | SPAWNMODE Once | 出现后切负 SV | 不因倒退重新隐藏 | 自动+Unity |
| [ ] | CMD-18 | DESTROY global | `<DESTROY*3>` | 视觉终点变为 3；判定时刻不变 | 自动+Unity |
| [ ] | CMD-19 | DESTROY typed/reset | typed 值后 NULL | 对应 type 恢复默认 4.8 | 自动+Unity |
| [ ] | CMD-20 | BOUNCE 基础 | `<BOUNCE*8:1>` + Tap | DESTROY→SPAWN→DESTROY | 自动接线+Unity |
| [ ] | CMD-21 | BOUNCE 受 HS | BOUNCE + `HS*tap=2` | 起飞窗口按有效速度变化 | 静态+Unity |
| [ ] | CMD-22 | BOUNCE 受 SV | BOUNCE + `SV*tap=0.5/2/-1` | phase 随积分前进、暂停、倒退 | 静态+Unity |
| [ ] | CMD-23 | BOUNCE + Once/Rewind | 分别设置两种 SPAWNMODE | 倒退隐藏策略不同 | 静态+Unity |
| [ ] | CMD-24 | FAKE global | `<FAKE*True>` | note 可见但不判定 | 自动+Unity |
| [ ] | CMD-25 | FAKE typed/reset | 只 fake slide，再 NULL | 只影响目标 type，reset 后恢复 | 自动+Unity |
| [ ] | CMD-26 | Fake head/body 分离 | Slide 只 fake head 或只 fake body | 判定与音效按对应部分禁用 | 静态+Unity |
| [ ] | CMD-27 | COLOR typed | `COLOR*tap`、`slide`、`star` | 只改目标对象 | 自动+Unity |
| [ ] | CMD-28 | SIZE typed | 对各 note type 设置 SIZE | 只改目标尺寸 | 自动+Unity |
| [ ] | CMD-29 | ALPHA typed | 对各 note type设置 ALPHA | 只改目标透明度 | 自动+Unity |
| [ ] | CMD-30 | `slidestar` visual target | 分别 COLOR/SIZE/ALPHA*slidestar | 只改运动星，不改 path/head | 自动+Unity |
| [ ] | CMD-31 | COLORV live | note 出现后发 COLORV | 已加载对象立即变色 | 自动解析+Unity |
| [ ] | CMD-32 | SIZEV live | note 出现后发 SIZEV | 已加载对象立即缩放 | 自动解析+Unity |
| [ ] | CMD-33 | ALPHAV live | note 出现后发 ALPHAV | 已加载对象立即变透明 | 自动解析+Unity |
| [ ] | CMD-34 | V 系列 backward replay | 越过 V 命令后 Pause 回拖 | 恢复命令前视觉 | 静态+Unity |
| [ ] | CMD-35 | 新加载 note 应用当前 V 状态 | V 命令后才进入加载窗口的 note | 一出现就是当前状态 | 静态+Unity |
| [ ] | CMD-36 | typed visual 非法列表原子拒绝 | 一条含合法/非法 target | 不部分生效 | 自动 |
| [ ] | CMD-37 | Visual 数值 finite 检查 | SIZE/ALPHA NaN/Infinity | 命令拒绝 | 自动 |
| [ ] | CMD-38 | Instant 屏幕效果 overload | `<ZOOM*(Instant,0.6,0)>` | 无 easing，立即到值 | 自动提示+Unity |
| [ ] | CMD-39 | ZOOM 新直接倍率语义 | 比较 0.6、1、2 | 分别约 60%、100%、200%，不再乘 0.12 | 静态+Unity |
| [ ] | CMD-40 | 负 ZOOM | 设置负值 | 可缩小且 shader 不异常，最小倍率受保护 | 手测 |
| [ ] | CMD-41 | DISPLAY/TEXT/JLINE 原状态表 | 分别写三种命令并 seek | 时间轴回放和原功能正常 | 静态+Unity |
| [ ] | CMD-42 | AUDIO 命令 | AUDIO 开关/素材参数 | 波形有条带，View 对应播放 | 静态+Unity |
| [ ] | CMD-43 | PVOVERLAY | 开关、素材、crossfade | 波形有 PV 条带，View 渐变正确 | 静态+Unity |

## 3. Slide、TouchSlide、D-zone 与镜像几何

| 完成 | ID | 新增或变化 | 最小操作 | 通过条件 | 当前覆盖 |
|---|---|---|---|---|---|
| [ ] | GEO-01 | D-zone Tap 位置 | `1d`～`8d` | 位于对应两 A 区之间 | 自动+Unity |
| [ ] | GEO-02 | Key→D endpoint 变形 | `2-6d[8:1]` | 路径准确到 D6 | 自动+Unity |
| [ ] | GEO-03 | D→Key endpoint 变形 | `2d-6[8:1]` | 路径从 D2 出发 | 自动+Unity |
| [ ] | GEO-04 | D→D 路径 | `2d-6d[8:1]` | 两端及切线正确 | 自动+Unity |
| [ ] | GEO-05 | D-zone `v` 经中心 | `2dv4[8:1]` | 星到中心前保持前半方向 | 自动+Unity |
| [ ] | GEO-06 | D-zone lightning `s/z` | 对合法组合测试 s/z | 尖角不被平滑掉 | 自动语义+Unity |
| [ ] | GEO-07 | TouchSlide V turn | 输入带明确 turn 的 Touch V | 必须经过 turn，星方向分段切换 | 自动+Unity |
| [ ] | GEO-08 | `pq` 真切线 | 慢速播放 pq | 直线与圆弧在同一点相切 | 静态+Unity |
| [ ] | GEO-09 | `ppqq` 真切线 | 慢速播放 pp/qq | 无微弯假拐角 | 静态+Unity |
| [ ] | GEO-10 | pq 涉及 B 区 | B/E/A 混合 pq | 不退化成直线 | 静态+Unity |
| [ ] | GEO-11 | Slide authored lifetime | 慢/停 SV Slide | 到声明 end 消失 | 静态+Unity |
| [ ] | GEO-12 | Wifi authored lifetime | 慢/停 SV Wifi | 到声明 end 消失 | 静态+Unity |
| [ ] | GEO-13 | TouchSlide authored lifetime | 慢/停 SV TouchSlide | 到声明 end 消失 | 静态+Unity |
| [ ] | MIR-01 | Alpha 命令不参与镜像 | 镜像 `A1<E5,<HS*2>` | HS 原文保留 | 自动 |
| [ ] | MIR-02 | block/comment 保护 | 镜像含注释和 protected text | 保护区原文保留 | 自动 |
| [ ] | MIR-03 | A/B/D/E LR 映射 | 分别镜像区域字符 | 区号正确 | 自动 |
| [ ] | MIR-04 | A/B/D/E UD 映射 | 分别镜像区域字符 | 区号正确 | 自动 |
| [ ] | MIR-05 | C/C1/C2 保持中心 | 各模式镜像中心 | 不绕环旋转 | 自动 |
| [ ] | MIR-06 | `1d` LR/UD | LR、UD 各做一次 | LR/UD 结果符合 D 区几何 | 自动 |
| [ ] | MIR-07 | p/q chirality | LR/UD 镜像 p/q | p↔q 正确交换 | 自动 |
| [ ] | MIR-08 | pp/qq chirality | LR/UD 镜像 pp/qq | pp↔qq 正确交换 | 自动 |
| [ ] | MIR-09 | `<`/`>` 环方向 | UD 镜像环 Slide | 方向翻转 | 自动 |
| [ ] | MIR-10 | 旋转保持 chirality | 45°/180°旋转 pq | 只改位置，不错误翻手性 | 自动 |
| [ ] | MIR-11 | 镜像可逆 | LR×2、UD×2、CW+CCW | 回到原串 | 自动 |

## 4. WPF 编辑器、波形、帮助、设置与 Media

| 完成 | ID | 新增或变化 | 最小操作 | 通过条件 | 当前覆盖 |
|---|---|---|---|---|---|
| [ ] | EDT-01 | 黄色光标按 closing comma 前取时 | caret 放逗号前/上/后 | 黄色线属于正确 timing | 静态+WPF |
| [ ] | EDT-02 | 从时间跳文本位置 | 点击不同波形时间 | 文本 caret 到对应 comma，不偏字符 | 自动 overlay+WPF |
| [ ] | EDT-03 | overlay 文本位置 | 从时间跳 `@{4}` 行 | caret 到 overlay 对应 timing | 自动+WPF |
| [ ] | EDT-04 | 波形中心 viewport | 左右调整窗口宽度 | playhead 始终居中 | 手测 |
| [ ] | EDT-05 | 左右 resize 对称 | 分别拖左、右边缘 | 两边都围绕同一时刻缩放 | 手测 |
| [ ] | EDT-06 | resize 75ms debounce | 连续拖窗口边缘 | 拖时不卡，停后清晰重画 | 静态+WPF |
| [ ] | EDT-07 | 波形 scrub 局部坐标 | 从波形不同位置拖 | 移动量与鼠标一致 | 手测 |
| [ ] | EDT-08 | 播放中点击波形进入 Pause | Play 后按下波形 | 不 Stop，进入共享 Pause | 手测 |
| [ ] | EDT-09 | scrub 松手同步文本 | 拖到另一分拍松开 | caret 跳到目标 timing | 手测 |
| [ ] | EDT-10 | 滚轮操作同步 caret | 在波形滚轮 | 时间和文本按预期同步 | 手测 |
| [ ] | EDT-11 | Stop standby preview | Stop 后输入 note | View 背景预览可见，DJAuto 不运行 | 静态+Unity |
| [ ] | EDT-12 | Pause shared timeline | Pause 后拖波形 | View 直接跟随，不 Stop | 静态+Unity |
| [ ] | EDT-13 | Pause 改谱后重载一次 | Pause、改 note、继续拖 | 新 note 出现，旧对象不残留 | 静态+Unity |
| [ ] | EDT-14 | 快速 Play/Pause generation | 连续操作 10 次 | Edit/View 不分叉、不闪空 | 静态+Unity |
| [ ] | EDT-15 | Preview 请求去抖 | 快速连续输入 | View 不排队延迟数秒 | 静态+Unity |
| [ ] | EDT-16 | 有效 sibling 预览保留 | `1/2dv4/` | 两支预览均在 | 自动 |
| [ ] | EDT-17 | 最高分拍 | `{4}`/`{8}`/`{16}` 混合选择 | 全部转最高分拍且时间不变 | 自动+WPF |
| [ ] | EDT-18 | timeline overlay 五 lane | 同时放 TEXT/AUDIO/PV/effect | 标签不完全重叠 | 静态+WPF |
| [ ] | EDT-19 | Media 面板播放中拖动 | Play 时拖 media playhead | 先 Pause，再共享 seek | 静态+WPF/Unity |
| [ ] | EDT-20 | 无 mtproj 自动导入 BGM | 删除 media project 后开谱 | 仍生成 track clip/波形 | 手测 |
| [ ] | EDT-21 | Pause media replay | 带字幕/PV/effect Pause seek | 所有媒体到目标帧 | 静态+Unity |
| [ ] | EDT-22 | pending media seek | 大视频未 ready 时立即 seek | ready 后自动到目标帧 | 静态+Unity |
| [ ] | EDT-23 | ShowMineHitFeedback 设置保存 | 切换设置、重开 Edit | 值保留并下发 View | 静态+WPF/Unity |
| [ ] | EDT-24 | Mine 音量滑块 | 0%/100% 播 Mine | 只改变 Mine 音量 | 手测 |
| [ ] | EDT-25 | Mine 实时独立 stream | Mine 与普通 Tap 同时播放 | 两类音量互不覆盖 | 手测 |
| [ ] | EDT-26 | Alpha hints 新命令 | 输入 SPAWNMODE/DESTROY/FAKE/V | overload 与参数提示出现 | 自动部分+WPF |
| [ ] | EDT-27 | NULL Tab 补全 | 在支持 reset 的参数输入 `N`+Tab | 补成 NULL | 手测 |
| [ ] | EDT-28 | Instant overload 提示 | 输入 ZOOM Instant | 显示对应签名 | 自动+WPF |
| [ ] | EDT-29 | `<<`/`>>` duration target hint | caret 位于多圈 TouchSlide | 时长提示识别完整 target | 静态+WPF |
| [ ] | EDT-30 | 三语言 Help key 一致 | 切 en/ja/zh-CN | 无空字符串/缺 key | 自动+WPF |
| [ ] | EDT-31 | Help 命令顺序 | 查看 SPAWN 段 | SPAWN→SPAWNMODE→DESTROY | 自动 |
| [ ] | EDT-32 | Help 含 `1f`、`!/?`、V、FAKE | 三语言逐项查看 | 文义与实际一致 | 自动部分+WPF |
| [ ] | EDT-33 | Alpha Help 不宣称 EX x 为新增 | 搜索 Alpha Help 的 EX/x | 不出现错误新增说明 | 自动文本+WPF |
| [ ] | EDT-34 | Record 窗 Utage label | Original 难度打开录制窗 | 显示 label 与协谱开关 | 静态+WPF |
| [ ] | EDT-35 | Utage 字段传给 View | 填 label/coop 后录制 | 成片卡片一致 | 静态+Unity |
| [ ] | EDT-36 | RefreshPreview | 改标题/谱师后刷新 | card 立即更新 | 手测 |
| [ ] | EDT-37 | 封面不锁文件 | 预览生成中连续刷新 | 无 IOException | 静态+WPF |
| [ ] | EDT-38 | 封面 Uniform | 打开录制窗 | 完整 card 可见且不拉伸 | 手测 |
| [ ] | EDT-39 | SongDetail 预烘焙缓存 | 改 metadata 并保存/刷新 | base/overlay/full 文件更新 | 静态+WPF |

## 5. Edit ↔ View 协议与播放状态

| 完成 | ID | 新增或变化 | 最小操作 | 通过条件 | 当前覆盖 |
|---|---|---|---|---|---|
| [ ] | NET-01 | JSON response | 正常 Play | 返回 `{ok:true,protocolVersion:1}` | 静态 |
| [ ] | NET-02 | 空 body 400 | curl POST 空 body | JSON error，listener 保持 | 静态+手测 |
| [ ] | NET-03 | 协议不匹配 409 | 发 protocolVersion 99 | 明确 mismatch error | 静态+手测 |
| [ ] | NET-04 | 命令失败 500 | 发非法 Continue/Record | `ok:false` + 具体 error | 静态+手测 |
| [ ] | NET-05 | Edit 显示 LastError | 制造 View 拒绝 | 不再只显示 PortClear | 静态+WPF |
| [ ] | NET-06 | client 断线不中止 listener | 中止一次请求后再 Play | 第二次仍成功 | 静态+Unity |
| [ ] | NET-07 | ManualResetEventSlim 同步 | 快速并发控制 | 无 busy-loop/死锁 | 静态+Unity |
| [ ] | NET-08 | localhost 禁代理 | 开系统代理后 Play | 127.0.0.1 不被代理劫持 | 静态+WPF |
| [ ] | NET-09 | 冷加载不使用 2s 超时 | 大谱首次 Record | 不在 2s 假失败 | 静态+WPF/Unity |
| [ ] | NET-10 | TimelinePreview | Pause 后首次更新 | View 加载可逆整谱 | 静态+Unity |
| [ ] | NET-11 | Seek | 已激活 paused preview 后拖动 | 只改时间，不重载整谱 | 静态+Unity |
| [ ] | NET-12 | paused preview 禁 Continue | 直接发送 Continue | 被拒绝并要求 Start | 自动静态+Unity |
| [ ] | NET-13 | Start 替换 paused preview | Pause seek 后 Play | 正式对象 ready 后替换 | 静态+Unity |
| [ ] | NET-14 | live chart 忽略 caret Preview | 播放时快速打字 | 不插入 preview note/抢 judge slot | 静态+Unity |
| [ ] | NET-15 | 两阶段 Start | 普通 Play | 先 bind，再 future Continue | 静态+Unity |
| [ ] | NET-16 | scheduledStart | 观察约 100ms lead | lead 内不移动/判定 | 静态+Unity |
| [ ] | NET-17 | Start/Pause stale callback 丢弃 | 快速 Play/Pause×10 | 旧回调不重新启动 | 静态+Unity |
| [ ] | NET-18 | Pause 禁用 Input/AP | Pause 时观察 DJAuto | 不触发传感器 | 静态+Unity |
| [ ] | NET-19 | Seek 清判定特效 | 命中特效期间 Pause seek | 不残留旧特效 | 静态+Unity |
| [ ] | NET-20 | command exception 恢复 | 加载损坏 JSON 后再 Start | 时钟/录制/live flags 均恢复 | 静态+Unity |
| [ ] | NET-21 | Stop 播放重载场景 | 普通 Play 后 Stop | Scene/Hierarchy 清干净 | 静态+Unity |
| [ ] | NET-22 | Stop 录制只收尾编码 | Record 中 Stop | 不按普通 Stop 破坏文件 | 静态+Unity |
| [ ] | NET-23 | OpStart 异步等待 ready | 大谱进入 OP flow | 主线程不长卡，ready 后 ack | 静态+Unity |
| [ ] | NET-24 | SetDisplay 重建 live V | 播放中切显示设置 | V 状态仍正确 | 静态+Unity |

## 6. View Note 行为与对象生命周期

| 完成 | ID | 新增或变化 | 最小操作 | 通过条件 | 当前覆盖 |
|---|---|---|---|---|---|
| [ ] | VIEW-01 | Tap 统一 spawn presentation | 正/负 SV + SPAWN | 出现/回退连续 | 静态+Unity |
| [ ] | VIEW-02 | Star 统一 spawn presentation | 同上 | 行为与 Tap 一致 | 静态+Unity |
| [ ] | VIEW-03 | Hold 头尾独立 spawn | 负 SV 长 Hold | 头尾不跳、body 长度正确 | 静态+Unity |
| [ ] | VIEW-04 | EachLine 跟随 spawn/destroy | Each + 自定义半径 | 与两个 note 同步 | 静态+Unity |
| [ ] | VIEW-05 | EachLine 跟随 BOUNCE | Each + Bounce | 连线与 note 同 phase | 静态+Unity |
| [ ] | VIEW-06 | EachLine 跟随 Once/Rewind | Each + 负 SV | 不与 note 分离 | 静态+Unity |
| [ ] | VIEW-07 | 负 SV 回中心连续 | Tap/Star/Hold/Each 出现后负 SV | 不吸回 spawnRadius；连续经过中心 | 自动数学+Unity |
| [ ] | VIEW-08 | 负半径 180° 视觉旋转 | 让 Tap/Hold 穿过中心 | 图形朝向连续正确 | 静态+Unity |
| [ ] | VIEW-09 | Hold bounce body 裁剪 | Hold + Bounce + DESTROY | body 不超可视区 | 静态+Unity |
| [ ] | VIEW-10 | Hold break shine 保留 live COLOR | break Hold 中途 COLORV | 动画不覆盖颜色 | 静态+Unity |
| [ ] | VIEW-11 | No-head Star 到 Running 才销毁 | 慢速 `?` Slide | 路径先出现，head 时机正确 | 静态+Unity |
| [ ] | VIEW-12 | `!` guide star 无 fade | 与 `?` 并排 | 仅 fade 不同 | 自动静态+Unity |
| [ ] | VIEW-13 | Slide path/slidestar 分材质 | 分别染色 | 不串色 | 自动+Unity |
| [ ] | VIEW-14 | Wifi 继承 mine/fake/`!` | 分别组合测试 | 与普通 Slide 语义一致 | 静态+Unity |
| [ ] | VIEW-15 | Wifi Continue refresh | Wifi 中途 Pause/Continue | 从正确进度续播 | 静态+Unity |
| [ ] | VIEW-16 | Touch 视觉受 SV | Touch typed SV 0.5/2 | 展开速度变化 | 静态+Unity |
| [ ] | VIEW-17 | Touch 判定不受视觉 SV | 同一组 Touch | 判定时刻不变 | 静态+Unity |
| [ ] | VIEW-18 | TouchHold mask 用 judge clock | TouchHold typed SV | 持续时长与判定对齐 | 静态+Unity |
| [ ] | VIEW-19 | Touch motion duration 新公式 | 多档 touchSpeed | 展开平滑，无突变 | 静态+Unity |
| [ ] | VIEW-20 | TouchSlide AST path | 复杂多段 TouchSlide | 与 Edit preview 一致 | 自动语义+Unity |
| [ ] | VIEW-21 | Fake Tap 生命周期 | FAKE Tap | 禁判定，到尾销毁 | 静态+Unity |
| [ ] | VIEW-22 | Fake Hold 生命周期 | FAKE Hold | 禁判定，到尾销毁 | 自动静态+Unity |
| [ ] | VIEW-23 | Fake Slide 生命周期 | FAKE Slide | 禁判定/按定义结束 | 静态+Unity |
| [ ] | VIEW-24 | paused preview Tap 可逆 | 前后 seek | 只隐藏，不销毁 | 静态+Unity |
| [ ] | VIEW-25 | paused preview Hold 可逆 | 前后 seek | 头/尾/body 恢复 | 静态+Unity |
| [ ] | VIEW-26 | paused preview Slide 可逆 | 前后 seek | path/star 恢复且无残留 | 静态+Unity |
| [ ] | VIEW-27 | paused preview Touch 可逆 | 前后 seek | Touch 恢复 | 静态+Unity |
| [ ] | VIEW-28 | paused preview TouchHold 可逆 | 前后 seek | 特效/body 恢复 | 静态+Unity |
| [ ] | VIEW-29 | paused preview Wifi 可逆 | 前后 seek | Wifi 恢复 | 静态+Unity |
| [ ] | VIEW-30 | Slide moving star 销毁清理 | Stop/Reload×5 | Hierarchy 不累积 star | 静态+Unity |
| [ ] | VIEW-31 | Wifi moving star 清理 | Stop/Reload×5 | 不累积 helper | 静态+Unity |
| [ ] | VIEW-32 | TouchHold holdEffect 清理 | Stop/Reload×5 | 不残留 particle object | 静态+Unity |
| [ ] | VIEW-33 | sensor/slot reload 清理 | Stop/Start×5 | 不占旧 judge slot | 静态+Unity |
| [ ] | VIEW-34 | Mine Tap 不切 break/each 线 | Mine 与 break/each 组合 | 保持 Mine 设计外观 | 静态+Unity |
| [ ] | VIEW-35 | Pink skin EX Star | pinkStar skin + EX Star | 使用固定粉色 | 手测 |
| [ ] | VIEW-36 | TouchHold 排序使用真实 sensor | 多区域重叠 TouchHold | 层级顺序正确 | 静态+Unity |
| [ ] | VIEW-37 | 旧 Mine JSON alias | 加载 `isMonoHead/isSlideMono` | Mine 仍生效 | 自动 |
| [ ] | VIEW-38 | 全量 binding 后才播放 | 极大复杂谱首次 Play | 不边播边生成后半路径 | 静态+Unity |
| [ ] | VIEW-39 | 材质预热 | 冷启动复杂染色谱 | 首帧不因 shader compile 明显卡顿 | 静态+Unity |
| [ ] | VIEW-40 | V 回放性能 | 大量 V 命令反复 seek | 无明显 O(events×notes) 卡顿 | 静态+Unity |

## 7. Screen Effect、判定、Media、Skin 与全屏

| 完成 | ID | 新增或变化 | 最小操作 | 通过条件 | 当前覆盖 |
|---|---|---|---|---|---|
| [ ] | FX-01 | ZOOM/MOVE 改 gameplay root transform | 同时显示 note 与 judge effect | 所有玩法元素同倍率/位移 | 静态+Unity |
| [ ] | FX-02 | ROTATE 与玩法根统一 | ROTATE 中判定 | note、文字、弧线同转 | 静态+Unity |
| [ ] | FX-03 | viewport mask 不参与 transform | ZOOM<1 | 不露黑边 | 自动静态+Unity |
| [ ] | FX-04 | cover 亮度保留 | 调 Inner/OuterBrightness + ZOOM | 亮度可控且 mask 不移 | 静态+Unity |
| [ ] | FX-05 | Background/media 随 effect | 有 PV 时 MOVE/ROTATE | 背景/媒体与设计语义一致 | 静态+Unity |
| [ ] | FX-06 | OnPreCull/OnPostRender 恢复 | SongDetail 入场叠加 effect | Animator 不累计偏移 | 静态+Unity |
| [ ] | JUDGE-01 | Tap 判定反馈随 DESTROY | DESTROY*tap=3 后命中 | effect 位于 3 半径 | 静态+Unity |
| [ ] | JUDGE-02 | Hold 判定反馈随 DESTROY | DESTROY*hold=3 | effect 对齐 | 静态+Unity |
| [ ] | JUDGE-03 | D-zone 判定反馈位置 | 命中 D-zone Tap/Hold | 角度/半径正确 | 静态+Unity |
| [ ] | JUDGE-04 | Touch 判定挂 NoteEffects | ZOOM/MOVE/ROTATE 下命中 | JUST/文字/粒子对齐 | 静态+Unity |
| [ ] | JUDGE-05 | TouchHold 判定挂 NoteEffects | 同上 | 全部 feedback 对齐 | 静态+Unity |
| [ ] | JUDGE-06 | ShowJudgeText 总开关 | 关闭后命中所有类型 | 所有静态/动态文字隐藏 | 静态+Unity |
| [ ] | JUDGE-07 | Mine feedback 独立开关 | 关 Mine 反馈命中 mixed notes | 只 Mine 隐藏 | 静态+Unity |
| [ ] | JUDGE-08 | 判区 overlay z-order | 开 showJudgeArea | 不盖住 outline/UI | 静态+Unity |
| [ ] | TL-01 | display timeline Pause replay | DISPLAY/TEXT 谱 Pause seek | 状态到目标时刻 | 静态+Unity |
| [ ] | TL-02 | media timeline Pause replay | AUDIO/PV 谱 Pause seek | 视频/音频到目标帧 | 静态+Unity |
| [ ] | TL-03 | deferred Start media gating | 两阶段 Start | Continue 前媒体不前进 | 静态+Unity |
| [ ] | UI-01 | 录制谱师字体 | SongDetail 中英日谱师 | Aileron/雅黑 fallback，无缺字 | 静态+Unity |
| [ ] | UI-02 | SongDetail effect 不破坏动画 | 入退场时执行 effect | 动画轨迹保持 | 静态+Unity |
| [ ] | UI-03 | 全屏记住窗口尺寸 | 调窗口→F11→F11 | 回到原尺寸 | 手测 |
| [ ] | UI-04 | 分辨率下拉保存 windowed size | 选分辨率后切全屏 | 退出尺寸正确 | 手测 |
| [ ] | UI-05 | FullScreenWindow 最高分辨率 | 进入全屏 | 使用目标显示器最高分辨率 | 手测 |
| [ ] | UI-06 | CustomSkin pink star 分支 | 切对应皮肤 | 资源和颜色正确 | 手测 |

## 8. 录制、ffmpeg、音频与导出

| 完成 | ID | 新增或变化 | 最小操作 | 通过条件 | 当前覆盖 |
|---|---|---|---|---|---|
| [ ] | REC-01 | 单一录制停止所有者 | AP 开录一次 | 不被 DestroySelf 提前停 | 静态+Unity |
| [ ] | REC-02 | 无 AP chartEnd+5s | AP 关录短谱 | 约末 note 后 5 秒结束 | 静态+录制 |
| [ ] | REC-03 | AP 动画+尾留白 cutoff | AP 开录短谱 | 动画完整后结束 | 静态+录制 |
| [ ] | REC-04 | Edit chartLength 下发 | 录不同长度谱 | View cutoff 与 Edit 末 note 一致 | 静态+录制 |
| [ ] | REC-05 | Record120 帧率 | Record120 | 输出 120 fps | 静态+录制 |
| [ ] | REC-06 | 普通 Record 帧率 | 普通 Record | 输出 60 fps | 静态+录制 |
| [ ] | REC-07 | PrepareRecording 重入门禁 | 连点 Record | 第二次明确失败 | 静态+录制 |
| [ ] | REC-08 | Record 重入 HTTP error | 同上 | Edit 不假显示成功 | 静态+WPF |
| [ ] | REC-09 | ffmpeg Process ownership | Record→Stop→Record | 无 orphan，第二次可录 | 静态+录制 |
| [ ] | REC-10 | ffmpeg 15s finalize deadline | 模拟 encoder hang | 超时 terminate 并提示 | 静态+录制 |
| [ ] | REC-11 | OnDestroy encoder cleanup | 编码中关闭 View | 无遗留 ffmpeg | 静态+录制 |
| [ ] | REC-12 | startup cancellation cleanup | Record 后立即 Stop | pipe/process 全清 | 静态+录制 |
| [ ] | REC-13 | exit code 判成功 | 正常/故障编码各一次 | 只有 exit 0 显示成功 | 静态+录制 |
| [ ] | REC-14 | Mine 实时音量 | 0%/100% 比较 | Mine 音量变化 | 手测 |
| [ ] | REC-15 | Mine 导出音量 | 同设置各录一次 | 成片音量与实时一致 | 手测 |
| [ ] | REC-16 | 其他通道实时音量进 mix | 调 BGM/judge/answer 后录 | 导出响度跟随设置 | 静态+录制 |
| [ ] | REC-17 | Mine head/body 音效分流 | 仅 head m / 仅 body m | 对应音效准确 | 静态+录制 |
| [ ] | REC-18 | Fake Slide 不调度 slide SE | FAKE Slide 录制 | 无 Slide 音效 | 静态+录制 |
| [ ] | REC-19 | Mine 两个共享 buffer | 长谱多 Mine 类型 | 峰值内存不随类型倍增 | 静态+性能 |
| [ ] | REC-20 | Utage card | label+coop 录制 | 首屏文字/协作标志正确 | 静态+录制 |
| [ ] | REC-21 | SongDetail cache 几何 | Preview 与成片首屏比对 | 构图一致 | 静态+录制 |
| [ ] | REC-22 | ShowSongDetail 开/AP 开 | 完整录制 | 字体、动画、尾部正常 | 手测 |
| [ ] | REC-23 | ShowSongDetail 开/AP 关 | 完整录制 | chartEnd+5s，首屏正常 | 手测 |
| [ ] | REC-24 | ShowSongDetail 关/AP 关 | 完整录制 | 不出现 card，尾时长正确 | 手测 |

## 9. JSON 兼容、构建、CI、文档与交付

| 完成 | ID | 新增或变化 | 最小操作 | 通过条件 | 当前覆盖 |
|---|---|---|---|---|---|
| [ ] | COMP-01 | 旧 Mine 字段 alias | 加载旧 majson | 不丢 Mine | 自动 |
| [ ] | COMP-02 | 新 Alpha table JSON 往返 | Edit 生成含新命令 majson | View 字段完整 | 自动部分+Unity |
| [ ] | COMP-03 | slidePath/pathExpression 往返 | 复杂 Slide 生成 majson | View 路径一致 | 自动+Unity |
| [ ] | COMP-04 | utageLabel/coop 往返 | 宴录制请求 | View 收到字段 | 静态+Unity |
| [ ] | ENG-01 | MajdataCore Unity meta | Unity 重开工程 | 不重新生成 GUID/丢引用 | 静态+Unity |
| [ ] | ENG-02 | WPF 联编 MajdataCore | Windows Release build | 0 error | 待 Windows |
| [ ] | ENG-03 | AppleDouble 排除 | 含 `._*.cs` 时 build | 不再 CS2015 | 静态+Windows |
| [ ] | ENG-04 | HoldDrop shadowing 修复 | Unity compile | 不再 CS0136 | 静态+Unity |
| [ ] | ENG-05 | 独立测试工程 | 执行 dotnet run | 604 assertions + 20k malformed | 自动 |
| [ ] | ENG-06 | 测试不混入 Assembly-CSharp | Unity compile | 不缺 NUnit | 静态+Unity |
| [ ] | ENG-07 | CI parser/runtime 路径触发 | 修改核心文件并 push | workflow 运行 | 静态+GitHub |
| [ ] | ENG-08 | CI Record/UI/shader 路径核对 | 修改这些文件并 push | 若要求覆盖则 workflow 必须触发 | 当前存在缺口 |
| [ ] | ENG-09 | `.gitignore` 保留测试 csproj | clone/查看 status | test csproj 被跟踪 | 静态 |
| [ ] | DOC-01 | README Touch 多圈 | 阅读并照例输入 | 文档与实现一致 | 手测 |
| [ ] | DOC-02 | README Alpha 新命令 | 对照 Help | SPAWNMODE/DESTROY/FAKE/V 齐全 | 手测 |
| [ ] | DOC-03 | README 修饰符 | 对照实际语义 | `!/?/$/$$/f/b/m/h` 一致 | 自动部分 |
| [ ] | DOC-04 | 三语言 Help | 切换三语言逐项检查 | 无漏译/旧语义 | 自动 key+WPF |
| [ ] | PACK-01 | `Desktop/fix` 相对路径 | 覆盖复制到 alpha | 可直接编译 | 静态 |
| [ ] | PACK-02 | 无 `._*`/`.DS_Store` | 搜索 alpha/fix | 结果为空 | 静态 |
| [ ] | PACK-03 | fix 与工作区代码一致 | 对比交付文件 | 0 missing/0 mismatch | 每次改动后重做 |

## 10. 纯内部改动的统一门禁

这些改动没有独立 UI，但仍需通过统一门禁：

| 完成 | ID | 内部改动 | 验收方式 |
|---|---|---|---|
| [ ] | INT-01 | Alpha parser/validator 抽到 MajdataCore | 自动回归 + Unity/WPF 双编译 |
| [ ] | INT-02 | typed curve 按 stream/type 缓存 | 大谱性能测试 + overlay 行为 |
| [ ] | INT-03 | Live V active-kind HashSet | 大量 V 命令 seek 性能 |
| [ ] | INT-04 | HTTP busy-loop 改同步事件 | 快速控制压力测试 |
| [ ] | INT-05 | generation 丢 stale callback | Play/Pause 压力测试 |
| [ ] | INT-06 | tint material cache 保留 | paused preview→Play 画面测试 |
| [ ] | INT-07 | 全量 runtime binding ready 门槛 | 大复杂谱冷启动 |
| [ ] | INT-08 | Mine export 共享 buffer | 长谱内存峰值 |
| [ ] | INT-09 | ffmpeg Dispose/terminate 全路径 | Record/Stop/关闭 View 组合 |
| [ ] | INT-10 | sourcePosition 稳定排序 | 同时刻命令自动测试 + Unity |

## 11. 发布前执行顺序

1. 先运行自动回归，确认 `604 assertions + 20000 malformed`；
2. Windows 编译 WPF；
3. Unity 等待 0 error；
4. 按本文件第 1～9 节逐行勾选；
5. 对失败项记录：ID、谱面文本、操作顺序、Edit/View 日志、截图或录屏；
6. 修复后只复测相关 ID；发布前再执行完整回归；
7. 最后验证 `Desktop/fix` 同步和正式平台 Build。
