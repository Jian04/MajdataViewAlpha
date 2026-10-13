# 自定义皮肤与 EX 保护套

把皮肤文件夹放在配套 `App/MajdataEdit/Skin/` 下，重新打开设置页即可发现新增文件夹。Tap、Hold、Star 下方的箭头分别切换对应音符皮肤；文件夹需包含对应的 `tap.png`、`hold.png` 或 `star.png` 才会进入该类选项。

各类 EX 保护套跟随该类的独立选择：`tap_ex.png`、`hold_ex.png`、`star_ex.png`、`star_ex_double.png`。所选文件夹缺少相应图片时才回退到基础皮肤。

View 优先查找配套 Edit 的 Skin，再查找自身及上级目录的 Skin，最后使用 Unity StreamingAssets 中的内置资源。独立新增的皮肤文件夹不需要加入 Unity 项目；修改 DX 图片时，直接替换 Edit/Skin/dx 下相应 PNG，重启 View 后加载。

源码资源为 `Skin/` 和 `Assets/StreamingAssets/Skin/`。桌面发布会将前者带入 Edit/Skin，Unity 构建携带后者。本次 DX 单星、双星 EX 图片已同步两处。
