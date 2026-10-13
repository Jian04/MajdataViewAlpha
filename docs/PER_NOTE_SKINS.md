# 单音符图片换肤

图片放在谱面 txt 所在目录。`~[a.png]` 紧跟要换肤的位置，修饰符和时长写在后面。支持 png、jpg、jpeg；子目录写作 `~[skins\a.png]`。

| 对象 | 示例 | 替换内容 |
| --- | --- | --- |
| Tap | `1~[a.png]` | Tap 主体 |
| Hold | `1~[hold.png]h[4:1]` | Hold 主体，包括按住、松开状态，保留拉伸边框 |
| Touch | `A1~[touch.png]` | 四片 Touch 花瓣 |
| Touch Hold | `C~[touch.png]h[4:1]` | 四片主体，保留中心点和进度边框 |
| 独立星星 | `1~[star.png]$` | 星星主体 |
| Slide 星星头 | `1~[star.png]-5[4:1]` | 起点的星星头，包括同头双星 |
| Slide 滑轨 | `1-5~[slide.png][4:1]` | 滑轨条，不替换引导星星 |
| 同时换头和轨道 | `1~[star.png]-5~[slide.png][4:1]` | 分别指定两张图片 |
| Touch Slide | `A1~[touch.png]-E5~[slide.png][4:1]` | Touch 头和滑轨 |
| Wifi | `1w5~[slide.png][4:1]` | Wifi 轨道条 |

Each、Break 也使用所指定的主体图片；EX 保护套、判定效果、连接线保留原皮肤。图片加载失败会报告原因并保留原贴图。

连续 Touch Slide 使用最后一个终点标记指定整个轨道的图片。普通分段 Slide 可以在各段终点指定图片。

`JsonDataLoader` Inspector 的旧 `starEach` 字段没有被读取；运行时 Each 星星使用所选皮肤的 `star_each.png`，双星使用 `star_each_double.png`。

`1~[5-7[8:1]]` 是借用轨迹的视觉对象，不产生 Slide 滑动声或判定音。本次只扩展图片换肤，没有扩展轨迹继承的音符类型。
