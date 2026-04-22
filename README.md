# MajdataViewAlpha

> 本项目由 Claude Code 和 Codex 辅助完成。

`MajdataViewAlpha` 是基于原版 `MajdataView` 继续修改和整理出的 Unity 版本。

仓库地址：

- GitHub: <https://github.com/Jian04/MajdataViewAlpha>

## 致谢

- Original project: [LingFeng-bbben/MajdataView](https://github.com/LingFeng-bbben/MajdataView)
- Main Programmer of the original project: `bbben`

感谢原项目作者和原始工程提供的基础框架、编辑器与运行逻辑。  
本项目是在原版基础上继续修改、扩展语法并重新整理发布。

## 新增语法

### COLOR

支持两种写法：

- 整体写法：对指定时间点后的音符颜色整体修改
- 分类型写法：按 `tap / each / hold / slide / star / break / touch / touchhold` 分别指定颜色

示例：

```text
<COLOR*FFFFFF>
<COLOR*tap=FF0000,break=00FFEE>
```

### SV

支持 true SV 语法，用于控制谱面滚动速度变化。

示例：

```text
<SV*2.0>
<SV*0.5>
```

### SIZE

支持音符尺寸缩放语法，用于在指定时间点后整体调整 note 大小。

示例：

```text
<SIZE*1.5>
<SIZE*0.8>
```

### ALPHA

支持音符透明度语法，用于在指定时间点后整体调整 note 不透明度。

示例：

```text
<ALPHA*0.5>
<ALPHA*1.0>
```

### RQ / RP

支持 `rq`、`rp` 相关语法。

示例：

```text
1rp5
1rq5
```

## 已知 Bug

- `COLOR` 目前没有恢复默认值的语法。
- `RP / RQ` 在结束位置和起始位置相差 `0` 或 `1` 的情况下，特效位置半径存在错误。
- `COLOR` 会影响 Hold 按住时的特效颜色。

## 未来计划

- 添加自定义字幕功能
- 添加内外亮度分别调整功能

