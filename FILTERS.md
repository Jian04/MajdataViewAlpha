# ViewAlpha 新增画面滤镜

这些命令用于 ViewAlpha / Edit 的预览与录制，只改变画面，不改变判定、物量或分数。不同滤镜可以叠加；停止预览会复原，拖动时间会恢复对应状态。

| 命令 | 效果 | 强度 |
| --- | --- | --- |
| `SHATTER` | 中心冲击、轻微扰动的放射裂纹、厚玻璃快速向外震开，带厚边、层叠折射、反射和切面高光；拖回同一时间可复现 | 0～1 |
| `RADIALBLUR` | 从中心向外拉出径向模糊 | 0～1 |
| `RIPPLE` | 圆环从中心反复向外扩散，折射画面 | 0～1 |
| `PIXELATE` | 像素块随强度变大 | 0～1 |
| `LENSDISTORT` | 正数向外鼓起，负数向内收缩 | -1～1 |
| `SPLIT` | 八条横向条带交替左右错位 | 0～1 |
| `KALEIDOSCOPE` | 六个扇区镜像折叠，第二参数为旋转角度，过渡时逐渐转向目标 | 角度（度），可正可负 |
| `BLOOM` | 提取高亮并向周围泛光 | 0～4 |
| `INVERT` | 颜色反转，1 为完全反色 | 0～1 |
| `POSTERIZE` | 压缩颜色层级，强度越大层级越少 | 0～1 |

独立 RGB 分离本次不添加，继续使用已有 Neon。

## 通用写法

`效果` 替换成上表中的命令名；大小写不敏感。

```text
<效果*(True,强度[,过渡时间])>
<效果*(False[,过渡时间])>
<效果*(Instant,强度,持续时间)>
<效果*(进入时间,保持时间,退出时间,强度)>
<效果*(持续时间,强度)>
```

True 开启并保持到 False；再次 True 会从当前值过渡到新目标。过渡时间省略时立即切换。Instant 立即达到目标，持续指定时间后恢复。包络依次进入、保持、退出；时间支持秒数或 `8:1` 等拍数。

```text
<SHATTER*(True,0.45,8:1)>
<SHATTER*(False,4:1)>
<RADIALBLUR*(Instant,0.6,8:1)>
<RIPPLE*(0.2,0.8,0.3,0.7)>
<LENSDISTORT*(True,-0.5)>
<BLOOM*(True,2,0.3)>
<PIXELATE*(True,0.5)>
<INVERT*(Instant,1,16:1)>
```

## 黑角渐变

渐变开关默认 **True**，省略或留空均可。从圆周向外逐渐变黑；明确写 False 则保留硬边。

```text
<VIGNETTE*(True,0.5)>
<VIGNETTE*(True,0.5,0.3)>
<VIGNETTE*(True,0.5,0.3,)>
<VIGNETTE*(True,0.5,0.3,False)>
<VIGNETTE*(True,0.5,False)>
<VIGNETTE*(Instant,0.5,8:1)>
<VIGNETTE*(0.2,0.5,0.3,0.6,False)>
```

强度写在 True／Instant 后的第二个参数；True 的第三个参数是可选过渡时间，Instant 的第三个参数是必填持续时间。False 只需要可选关闭时间。Pixelate 已降低像素块上限，演示值为 0.35。

噪域语法见 [NOISE_ZONES.md](NOISE_ZONES.md)。

万花筒示例：<KALEIDOSCOPE*(True,45,1)> 在 1 秒内转到 45 度；<KALEIDOSCOPE*(True,-90,8:1)> 转到 -90 度；<KALEIDOSCOPE*(False,0.3)> 关闭。0 度仍是镜像万花筒。
