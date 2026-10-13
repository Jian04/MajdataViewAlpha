# 可扩展滤镜

ViewAlpha 已实现下表中除独立 RGB 分离以外的所有滤镜，包括反色和色阶压缩两个独立命令。完整语法、参数与示例见 [新增滤镜语法](FILTERS.md)，Edit 的语法帮助与输入提示已同步。本次先接入 ViewAlpha，PlayAlpha 尚未迁移。

| 滤镜 | 画面效果与音游用途 | 实现难度 | 大致开销 |
| --- | --- | --- | --- |
| 玻璃碎裂 | 不规则碎片小幅平移/旋转，裂缝带亮边；适合重拍和段落切换 | 中 | 中 |
| 独立 RGB 分离 | 红绿蓝轻微错开，可单独使用，不附带现有 Neon 的边缘发光 | 低 | 低 |
| 径向模糊 | 从中心向外拉出速度线，适合加速、爆点 | 中 | 中，多次采样 |
| 冲击波 / 水波 | 圆环从指定中心扩散，让经过的画面折射 | 中 | 低到中 |
| 像素化 | 逐渐变成大像素块，可做复古、故障转场 | 低 | 低 |
| 镜头畸变 / 鱼眼 | 中心鼓起或收缩，产生镜头冲击感 | 低到中 | 低 |
| 分屏错位 | 横向或纵向切成几条，各条独立位移 | 低 | 低 |
| 万花筒 / 镜像折叠 | 按扇区镜像重复画面，适合对称段落 | 中 | 低到中 |
| 高亮泛光 Bloom | 只让高亮部分向外发光，与现有 Neon 的轮廓效果不同 | 中 | 中到高，需要亮部提取和模糊 |
| 反色 / 色阶压缩 | 颜色取反，或减少颜色层级，适合短促切换 | 低 | 低 |

玻璃碎裂采用固定种子的分块、小幅位移旋转、裂缝高光和统一强度包络，支持时间拖动和复原。当前参数为强度，沿用 Instant / 持续 / 包络语法。独立 RGB 分离本次不添加，继续使用 Neon。

当前已有 Gaussian、Neon、Trail、Flash/Fade、Brightness、Saturation、Contrast、Rainbow、Vignette、Glitch、TVNoise、Zoom、Hue、Tint、Move、Rotate、Shake。

黑角渐变：`<VIGNETTE*(True,0.5,0.3)>`。末尾渐变开关省略或留空默认为 True，使圆周向外逐渐变黑；明确写 False 保留硬边。Instant 和旧包络形式同样可以在末尾添加开关。

Unity 官方提供 [后处理效果列表](https://docs.unity.com/en-us/engine/6000.6/manual/post-processing-and-full-screen-effects/urp/effect-list)，包含 Bloom、色差和镜头畸变；[自定义屏幕后处理](https://docs.unity.com/en-us/engine/6000.7/manual/post-processing-and-full-screen-effects/urp/custom-post-processing)支持扩展其他效果。具体候选难度和玻璃方案为本项目的实现评估。
