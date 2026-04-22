"""
test_sv.py — 真 SV（Scroll Velocity）效果测试
语法：<SV*X> 在该时刻插入全局滚动速度变化，X 为倍率。
     <HS*X> 仍有效，影响之后 note 的基础速度。

段落（BPM=200, {8}，每逗号=0.15s）：
  S1 [0~10s]  : 正常基准
  S2 [10~18s] : SV*3.0  快速滚动
  S3 [18~22s] : SV*8.0  极快
  S4 [22~30s] : 故障效果：SV*0.001（几乎冻结）→ SV*20（每4拍突然爆发）
  S5 [30~38s] : SV*0.1  极慢爬行
  S6 [38~48s] : 恢复正常 SV*1.0
  S7 [48~58s] : 每2拍交替 SV*0.001 / SV*15 制造卡顿节奏感
"""

import sys, time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from simai_parser import maidata_to_majson, save_temp_majson, send_to_majdata_view, play_sfx_for_chart

CHART = """\
&title=True_SV_Test
&artist=Alpha
&des=TrueSV
&wholebpm=200
&inote_5=
(200){8}
||
|| S1: Normal SV=1.0
||
1,2,3,4,5,6,7,8,
1b,2b,3b,4b,5b,6b,7b,8b,
1,3,5,7,2,4,6,8,
1b,3b,5b,7b,2b,4b,6b,8b,
1,2,3,4,5,6,7,8,
1b,2b,3b,4b,5b,6b,7b,8b,
1,3,5,7,2,4,6,8,
1b,3b,5b,7b,2b,4b,6b,8b,
1,2,3,4,5,6,7,8,
1b,2b,3b,4b,5b,6b,7b,8b,
||
|| S2: SV*3.0 fast scroll
||
<SV*3.0>
1,2,3,4,5,6,7,8,
1b,2b,3b,4b,5b,6b,7b,8b,
1,3,5,7,2,4,6,8,
1b,3b,5b,7b,2b,4b,6b,8b,
1,2,3,4,5,6,7,8,
1b,2b,3b,4b,5b,6b,7b,8b,
1,3,5,7,2,4,6,8,
1b,3b,5b,7b,2b,4b,6b,8b,
||
|| S3: SV*8.0 extreme fast
||
<SV*8.0>
1,2,3,4,5,6,7,8,
1b,2b,3b,4b,5b,6b,7b,8b,
1,3,5,7,2,4,6,8,
1b,3b,5b,7b,2b,4b,6b,8b,
||
|| S4: Glitch — freeze then burst every 4 beats
||
<SV*0.001>
1,2,3,4,
<SV*20.0>
5,6,7,8,
<SV*0.001>
1,2,3,4,
<SV*20.0>
5,6,7,8,
<SV*0.001>
1,2,3,4,
<SV*20.0>
5,6,7,8,
<SV*0.001>
1b,2b,3b,4b,
<SV*20.0>
5b,6b,7b,8b,
<SV*0.001>
1b,2b,3b,4b,
<SV*20.0>
5b,6b,7b,8b,
<SV*0.001>
1b,2b,3b,4b,
<SV*20.0>
5b,6b,7b,8b,
||
|| S5: SV*0.1 ultra slow crawl
||
<SV*0.1>
1,2,3,4,5,6,7,8,
1b,2b,3b,4b,5b,6b,7b,8b,
1,3,5,7,2,4,6,8,
1b,3b,5b,7b,2b,4b,6b,8b,
1,2,3,4,5,6,7,8,
1b,2b,3b,4b,5b,6b,7b,8b,
1,3,5,7,2,4,6,8,
1b,3b,5b,7b,2b,4b,6b,8b,
||
|| S6: Back to normal SV*1.0
||
<SV*1.0>
1,2,3,4,5,6,7,8,
1b,2b,3b,4b,5b,6b,7b,8b,
1,3,5,7,2,4,6,8,
1b,3b,5b,7b,2b,4b,6b,8b,
1,2,3,4,5,6,7,8,
1b,2b,3b,4b,5b,6b,7b,8b,
1,3,5,7,2,4,6,8,
1b,3b,5b,7b,2b,4b,6b,8b,
1,2,3,4,5,6,7,8,
1b,2b,3b,4b,5b,6b,7b,8b,
||
|| S7: Rhythmic stutter — SV*0.001 / SV*15 every 2 beats
||
<SV*0.001>
1,2,
<SV*15.0>
3,4,
<SV*0.001>
5,6,
<SV*15.0>
7,8,
<SV*0.001>
1,2,
<SV*15.0>
3,4,
<SV*0.001>
5,6,
<SV*15.0>
7,8,
<SV*0.001>
1b,2b,
<SV*15.0>
3b,4b,
<SV*0.001>
5b,6b,
<SV*15.0>
7b,8b,
<SV*0.001>
1b,2b,
<SV*15.0>
3b,4b,
<SV*0.001>
5b,6b,
<SV*15.0>
7b,8b,
E
"""

OUT_DIR    = Path(__file__).parent / "__sv_test"
SFX_DIR    = Path("C:/Users/syxx_/Desktop/Majdata/SFX")

def main():
    print("解析谱面...")
    majson = maidata_to_majson(CHART, diff_num=5)
    if not majson:
        print("解析失败！")
        return

    print(f"  note 数：{len(majson['timingList'])}")
    print(f"  SV 点数：{len(majson['svTable'])}")
    for sv in majson['svTable']:
        print(f"    t={sv['time']:.2f}s → SV*{sv['multiplier']}")

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

    print()
    print("  段落时间表：")
    print("    0s  ~10s : 正常基准 SV*1.0")
    print("    10s ~18s : 快速 SV*3.0")
    print("    18s ~22s : 极快 SV*8.0")
    print("    22s ~30s : 故障 — 冻结(SV*0.001)→爆发(SV*20)每4拍")
    print("    30s ~38s : 极慢爬行 SV*0.1")
    print("    38s ~48s : 恢复正常 SV*1.0")
    print("    48s ~58s : 卡顿节奏 SV*0.001↔SV*15 每2拍")

    total = majson['timingList'][-1]['time'] + START_DELAY + 2.0
    print(f"\n  播放中（共约 {total:.0f}s）...")
    time.sleep(total)

if __name__ == '__main__':
    main()
