# SlideCode 写法

以下说明按 MajdataPlay 的解析器、几何节点和判定表整理。SlideCode 与 Alpha 的大 P/Q 可选绕行圆是两套语法。

## 结构

```text
起点 + 路径指令 + K终点[时长],
1A3B5K7[8:1],
```

起点和终点支持 1–8 号键、`1d`–`8d`，以及 `A1`–`A8`、`B1`–`B8`、`C`、`D1`–`D8`、`E1`–`E8`。指令使用大写字母，最后用 `K` 加终点结束。方括号沿用 Slide 时长写法，例如 `[8:1]`、`[190#8:1]`、`[0.2##8:1]`。

| 指令 | 参数 | 含义 |
| --- | --- | --- |
| A | 1–8 | 到判定线上的对应节点 |
| B | 1–8 | 到内侧几何交点；并非同编号 Touch B 区中心 |
| C | 不接数字 | 到圆心 |
| P | 0–9 | 逆时针绕选定圆，并以切线连接前后路径 |
| Q | 0–9 | 顺时针绕选定圆，并以切线连接前后路径 |
| K | 键位／D 区键位／Touch 位置 | 到最终位置，结束整条路径 |

圆编号：`0` 为中央圆，`1`–`8` 为经过对应 D 区方向的侧边圆，`9` 为外侧判定线圆。

## 示例

```text
(120){4}
1A3B5K7[8:1],
2B4K6[4:1],
1A3571P0K1[4:1],
5Q9A1P98CQ49K5[8:1],
1dA3K5d[8:1],
A1A3KB4[8:1],
B1CKE5[8:1],
```

`A1A3KB4` 从 Touch A1 出发，经过 SlideCode 几何节点 A3，到 Touch B4 结束。起终点以外的 A/B/C 节点含义保持不变；不能用 `A3d` 或额外的 D/E 指令指定中间节点。

端点沿用普通 D 区、TouchSlide 的位置：普通键半径 4.8，Touch A/D 为 4.1，B 为 2.3，E 为 3，C 为中心。D 区键比同号普通键偏转 22.5°。切线可达性检查同样适用于这些端点，位于目标圆内部的端点仍不能直接与该圆相切。

同一指令后可连续写数字：`A357` 表示依次到 A3、A5、A7；`P98` 表示依次绕 9、8 号圆。`C` 不接编号。

`P` 与 `Q` 不能直接相连；0 号圆与 9 号圆也不能直接互转，中间需要 A、B 或 C 节点。节点位于目标圆内部时，无法通过切线进入或离开该圆，因而会报错。

## 与大 P/Q 的区别

```text
1Q85[8:1],
1Q8K5[8:1],
```

第一条没有 K，属于 Alpha 的大 Q 可选绕行圆写法，保留其额外绕行逻辑；第二条有 K，属于 SlideCode，使用 MajdataPlay 的切线与圆弧连接规则。两者不能直接当作同一条轨迹使用。

Alpha 的大 P/Q 可用于普通 Slide、D 区和 TouchSlide，TouchSlide 的大 P/Q 也是 Alpha 扩展。没有 K 时，A/B/D/E 加编号表示触控分区，并非 SlideCode 节点：

```text
A1P9A5[8:1],
1P5A8-B4[8:1],
1dQ05d[8:1],
```

`1P5A8-B4` 从 1 号键绕 5 号圆到 A8，再直线到 B4。不要把这里的 A8/B4 按 SlideCode 的 A/B 几何节点解释。

## 判定区

普通键起终点的 SlideCode 使用 MajdataPlay 的路径采样阈值及分区转移表，包含二选一判定区和末区的特殊长度。路径中的 A/B/C 是几何节点，不能直接把指令列表当作判定区列表。

扩展端点接入原版 A/B/C 判定范围前、离开该范围后的连接段，使用现有 Touch 分区；中间 A/B/C 路段继续使用原版表。没有经过原版 A/B/C 范围的连接段按 Touch 分区处理。例：`1dK2d` 的判定顺序为 D1、A1、D2。

定义和参考实现：

- [MajdataPlay SlideCodeParser](https://github.com/TeamMajdata/MajdataPlay/blob/dev/Assets/Scripts/Scenes/Game/Misc/Parsing/SlideCodeParser.cs)
- [MajdataPlay 几何节点](https://github.com/TeamMajdata/MajdataPlay/blob/dev/Assets/Scripts/Scenes/Game/Misc/Parsing/SlideGeo.cs)
- [MajdataPlay 判定表](https://github.com/TeamMajdata/MajdataPlay/blob/dev/Assets/Scripts/Scenes/Game/Misc/Parsing/SlideDataBuilder.cs)
