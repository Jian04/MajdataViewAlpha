# MajdataViewAlpha

> 本项目由 Claude Code 和 Codex 辅助完成。

`MajdataViewAlpha` 是基于原项目继续修改的 Unity 版本，当前发布为从 0 开始整理的首个公开版本。

## 新增语法

### COLOR

支持两种写法：

- 整体写法：对指定时间点后的音符颜色整体修改
- 分类型写法：按 `tap / each / hold / slide / star / break / touch / touchhold` 分别指定颜色

### SV

支持 true SV 语法，用于控制谱面滚动速度变化。

### SIZE

支持音符尺寸缩放语法，用于在指定时间点后整体调整 note 大小。

### ALPHA

支持音符透明度语法，用于在指定时间点后整体调整 note 不透明度。

### RQ / RP

支持 `rq`、`rp` 相关语法。

## 已知 Bug

- `COLOR` 目前没有恢复默认值的语法。
- `RP / RQ` 在结束位置和起始位置相差 `0` 或 `1` 的情况下，特效位置半径存在错误。
- `COLOR` 会影响 Hold 按住时的特效颜色。

## 未来计划

- 添加自定义字幕功能。
- 添加内外亮度分别调整功能。

## 仓库说明

本仓库建议只提交源码，不提交 Unity 生成目录。

已在 `.gitignore` 中忽略：

- `Library`
- `Temp`
- `Logs`
- `UserSettings`
- `obj`
- `bin`
- `.vs`
- `.vscode`

## 发布建议

- GitHub 仓库：提交 `alpha` 源码
- GitHub Release：上传打包好的运行版 zip

