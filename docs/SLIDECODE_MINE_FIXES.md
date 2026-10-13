# SlideCode、MOVE 与 mine 判定修正

## 修正内容

- `8P91K1` 的内外圈连接不再按离起点最近的连接弧选择，而按 P/Q 方向选择固定连接弧；连接角度统一为 45°。`8P9A7P91K1` 仍可使用。
- 旧式 MOVE 效果过期后返回零强度，避免较长效果与较短效果重叠时，较短效果产生负包络而反向移动。正 X 向右，正 Y 向上；状态式 MOVE 的坐标仍是目标位置，过渡从当前位置开始。
- mine 不会主动生成 DJAuto 点击、持续按压或划动输入。避开成功正常结算；在有效窗口被点击、Hold 身体被按住、Slide 判定链被提前完成时扣分。Alpha 没有独立的 TooFast 等级，误触统一显示和记录为 MISS。
- mine Tap/Hold 头部使用 ±50ms 窗口；Touch/TouchHold 在经过头部时刻后无误触则自动通过头判。Hold 身体仍检查实际按压。
- SlideCode 的 DJAuto 经过传感器输入系统，能与普通 Tap、Hold、mine 相互作用；判定区按当前区/下一区顺序推进，末区按压即可完成，中间区需要按下/释放或进入下一区。
- SlideCode 根据末区进入时刻与末区停留时间计算判定，未划完保留到结束后 600ms；不再只到结束时刻直接给 DJAuto PERFECT。
- 暂停、回拖、禁用和销毁 TouchSlide/SlideCode 时释放其模拟输入。

## 依据

对照 TeamMajdata/MajdataPlay 的 dev 提交 `c3423a4bba536e53921e8fdedab2b9d91121b393`：

- [SlideCodeParser](https://github.com/TeamMajdata/MajdataPlay/blob/c3423a4bba536e53921e8fdedab2b9d91121b393/Assets/Scripts/Scenes/Game/Misc/Parsing/SlideCodeParser.cs) 与 [SlideGeo](https://github.com/TeamMajdata/MajdataPlay/blob/c3423a4bba536e53921e8fdedab2b9d91121b393/Assets/Scripts/Scenes/Game/Misc/Parsing/SlideGeo.cs)：轨道转换方向及几何常数。
- [SlideBase](https://github.com/TeamMajdata/MajdataPlay/blob/c3423a4bba536e53921e8fdedab2b9d91121b393/Assets/Scripts/Scenes/Game/NoteBehaviours/SlideBase.cs)：传感器队列、mine 自动通过与输入判定。
- [TapDrop](https://github.com/TeamMajdata/MajdataPlay/blob/c3423a4bba536e53921e8fdedab2b9d91121b393/Assets/Scripts/Scenes/Game/NoteBehaviours/TapDrop.cs)、同目录 HoldDrop、TouchDrop、TouchHoldDrop：mine 头部窗口及长条身体处理。

## 验证

自动回归覆盖 3321 条 SlideCode 几何/判定路径，包括两个报告样例、所有键位的内外圈双向转换及镜像；另外验证了传感器顺序、成对判定区、MOVE 过期和回拖包络、mine 头判边界、SlideCode 早晚判定。

Unity 源码通过独立 C# 编译检查。这不是 Unity Player 构建，也不是 Unity 实机验收。需编译当前 Alpha View 后检查：

1. 两个报告样例显示完整，左右/上下镜像后保持几何对称。
2. DJAuto 播放纯 mine 谱面，不产生主动传感器按压，不丢分。
3. 普通 Slide/SlideCode 经过 mine Tap 时，落在有效窗口会扣分；手动点击窗口外不扣分。
4. Tap/Hold 的输入完成 mine Slide 判定链时扣分；仅按远端尾区不能跳过整条路径。
5. 暂停与来回拖动后，传感器不残留按压，继续播放判定正常。
