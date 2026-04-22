"""
test_color.py — note color tint / size / SV 综合测试

命令（统一 <CMD*value> 风格，均为"之后所有 note"持续生效）：
  <NC*color>         所有类型统一颜色
  <NC*k=c,k=c,...>   逐类型 tap|each|hold|slide|star|break|touch|touchhold
  <NS*x>             大小倍率（1.0 原始，0.5 半size，2.0 两倍）
  <SV*x>             卷轴速度（影响已在场和新生成的 note 的视觉位置）
  <HS*x>             落下速度

段落：
  COLOR  颜色全类型验证（NC 持续生效演示）
  SIZE   tap 大小梯度（0.3→0.5→0.8→1.0→1.5→2.0）
  SV1    慢→快 Rush：SV*0.08 慢爬，突然 SV*12 暴速
  SV2    快→慢→冻→炸：SV*10→SV*0→SV*15→正常
  COMB   tap 缩小（NS*0.4）+ SV 慢→快（颜色=绿）
"""

import sys, time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from simai_parser import maidata_to_majson, save_temp_majson, send_to_majdata_view, play_sfx_for_chart

CHART = """\
&title=ColorSizeTest
&artist=Alpha
&des=NoteColor
&wholebpm=160
&inote_5=
(160){8}
||
|| COLOR: 颜色全类型持续生效验证
|| 设置后不重置 → 下一段还是同色，确认持久性
||
<NC*tap=FF0000,hold=0044FF,break=00FFEE,each=FF00FF,slide=00CC44,star=FFAA00>
1,2,3,4,5,6,7,8,
1h[8:1],2h[8:1],3h[8:1],4h[8:1],5h[8:1],6h[8:1],7h[8:1],8h[8:1],
1b,2b,3b,4b,5b,6b,7b,8b,
1hb[8:1],2hb[8:1],3hb[8:1],4hb[8:1],5hb[8:1],6hb[8:1],7hb[8:1],8hb[8:1],
1-5-2[8:1],2-6-3[8:1],3-7-4[8:1],4-8-5[8:1],
1/2,3/4,5/6,7/8,2/3,4/5,6/7,8/1,
||
|| NC 持续性验证：不写新 NC，颜色应保持上方设置
||
1,2,3,4,5,6,7,8,
1h[8:1],2h[8:1],3h[8:1],4h[8:1],5h[8:1],6h[8:1],7h[8:1],8h[8:1],
1b,2b,3b,4b,5b,6b,7b,8b,
1hb[8:1],2hb[8:1],3hb[8:1],4hb[8:1],5hb[8:1],6hb[8:1],7hb[8:1],8hb[8:1],
||
|| SIZE: tap 大小梯度（NS 持续生效）
||
<NC*tap=FF8800>
<NS*0.3>
1,2,3,4,5,6,7,8,
<NS*0.5>
1,2,3,4,5,6,7,8,
<NS*0.8>
1,2,3,4,5,6,7,8,
<NS*1.0>
1,2,3,4,5,6,7,8,
<NS*1.5>
1,2,3,4,5,6,7,8,
<NS*2.0>
1,2,3,4,5,6,7,8,
<NS*1.0>
||
|| SV1: 慢爬 → 突然暴速 → 再慢 → 再暴速
||
<NC*FF0000>
<SV*1>
1,2,3,4,5,6,7,8,
<SV*0.08>
1,2,3,4,5,6,7,8,
1,2,3,4,
<SV*12>
5,6,7,8,
1,2,3,4,
<SV*0.08>
5,6,7,8,
1,2,3,4,
<SV*14>
5,6,7,8,
<SV*1>
1,2,3,4,5,6,7,8,
||
|| SV2: 正常 → 超快 → 冻结（SV*0）→ 全炸出（SV*15）→ 慢爬 → 正常
||
<NC*0044FF>
<SV*1>
1,2,3,4,5,6,7,8,
<SV*8>
1,2,3,4,5,6,7,8,
<SV*0>
1,2,3,4,5,6,7,8,
1,2,3,4,5,6,7,8,
<SV*15>
1,2,3,4,5,6,7,8,
<SV*0.06>
1,2,3,4,5,6,7,8,
<SV*1>
1,2,3,4,5,6,7,8,
||
|| COMB: tap 缩小（NS*0.4）+ NC 绿色 + SV 慢→快组合
||
<NC*00DD44>
<NS*0.4>
<SV*0.1>
1,2,3,4,5,6,7,8,
1,2,3,4,
<SV*10>
5,6,7,8,
1,2,3,4,
<SV*0.1>
5,6,7,8,
1,2,3,4,
<SV*12>
5,6,7,8,
<SV*0>
1,2,3,4,5,6,7,8,
1,2,3,4,5,6,7,8,
<SV*15>
1,2,3,4,5,6,7,8,
<SV*1>
<NS*1.0>
1,2,3,4,5,6,7,8,
E
"""

OUT_DIR = Path(__file__).parent / "__color_test"
SFX_DIR = Path("C:/Users/syxx_/Desktop/Majdata/SFX")

def main():
    print("解析谱面...")
    majson = maidata_to_majson(CHART, diff_num=5)
    if not majson:
        print("解析失败！")
        return

    print(f"  note 数：{len(majson['timingList'])}")

    print(f"\n  colorTable ({len(majson['colorTable'])} 条)：")
    for c in majson['colorTable']:
        print(f"    t={c['time']:.2f}s  {c['noteType']}=#{c['color']}")

    print(f"\n  sizeTable ({len(majson['sizeTable'])} 条)：")
    for s in majson['sizeTable']:
        print(f"    t={s['time']:.2f}s  scale={s['scale']}")

    print(f"\n  svTable ({len(majson['svTable'])} 条)：")
    for sv in majson['svTable']:
        print(f"    t={sv['time']:.2f}s  x{sv['multiplier']}")

    json_path = save_temp_majson(majson, OUT_DIR)
    print(f"\nJSON：{json_path}")

    START_DELAY = 4.0
    print(f"\n发送到 MajdataView（{START_DELAY}s 后开始）...")
    ok, msg = send_to_majdata_view(json_path, start_delay=START_DELAY, note_speed=7.5, background_cover=0.75)
    if not ok:
        print(f"  失败：{msg}")
        return

    print("  成功！同步播放 SFX...")
    play_sfx_for_chart(majson, str(SFX_DIR), start_delay=START_DELAY)

    total = majson['timingList'][-1]['time'] + START_DELAY + 2.0
    print(f"\n  播放中（共约 {total:.0f}s）...")
    print()
    print("  段落说明：")
    print("    COLOR  全类型颜色 + 持续性验证（不写 NC 的那段应保持同色）")
    print("    SIZE   tap 大小梯度 0.3→0.5→0.8→1.0→1.5→2.0")
    print("    SV1    红色 慢爬→暴速×2")
    print("    SV2    蓝色 正常→超快→冻→炸→慢→正常")
    print("    COMB   绿+缩小(0.4)+慢→快 三合一")
    time.sleep(total)

if __name__ == '__main__':
    main()
