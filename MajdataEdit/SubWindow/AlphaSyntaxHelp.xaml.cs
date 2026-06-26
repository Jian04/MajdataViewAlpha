using System.Windows;

namespace MajdataEdit;

public partial class AlphaSyntaxHelp : Window
{
    public AlphaSyntaxHelp()
    {
        InitializeComponent();
        HelpTextBox.Text = HelpText;
    }

    private const string HelpText = @"
Alpha 语法帮助
==============

基本规则
--------
Alpha 命令写在谱面时间轴里，命令本身不占拍。
常用格式：
  <NAME*value>
  <NAME*(value,duration)>

duration 单位是秒。显示控制和画面特效通常会在 duration 秒内渐变。

一、音符外观
------------
COLOR：修改后续音符颜色。
  <COLOR*FF00FF>
  <COLOR*tap=FF77AA,hold=66DDFF,slide=55CCFF,star=FFFFFF,break=FF5533,touch=AAFFAA,touchhold=66FFFF>
  <COLOR*NULL>
  <COLOR*tap=NULL,slide=NULL>

SIZE：修改后续音符整体大小倍率。
  <SIZE*1.25>
  <SIZE*0.8>
  <SIZE*1>

ALPHA：修改后续音符透明度，0 为透明，1 为不透明。
  <ALPHA*0.5>
  <ALPHA*tap=0.5,slide=0.8,touch=0.4>
  <ALPHA*1>

m：地雷 note 修饰，优先级类似 break。
  1m
  1hm[8:1]
  1bm-5[8:1]
  1-5m[8:1]
  1-5[8:1]m

二、速度 / SV
-------------
SV：修改视觉滚动速度，支持负数。
  <SV*2.0>
  <SV*0.5>
  <SV*-1.0>
  <SV*1>

三、显示控制
------------
判定线：
  <ShowJudgeLine*(False,2)>
  <ShowJudgeLine*(True,1)>

左侧判定统计：
  <ShowJudgeInfo*(False,1)>
  <ShowJudgeInfo*(True,1)>

右侧 combo / 分数信息：
  <ShowComboInfo*(False,1)>
  <ShowComboInfo*(True,1)>

判定文字：
  <ShowJudgeText*(False,1)>
  <ShowJudgeText*(True,1)>

内外背景亮度：
  <InnerBrightness*(0.5,2)>
  <OuterBrightness*(0.9,2)>

中间显示内容：
  <ComboDisplay*(none,0)>
  <ComboDisplay*(combo,0)>
  <ComboDisplay*(score,1)>
  <ComboDisplay*(achievement,1)>
  <ComboDisplay*(dxscore,1)>

四、字幕 TEXT
-------------
持续到下一条 TEXT：
  <TEXT*你好>

持续指定秒数：
  <TEXT*(你好,2)>

清空字幕：
  <TEXT*>
  <TEXT*(,0)>

五、画面特效
------------
统一格式：
  <Effect*(duration,intensity)>

参数：
  duration：持续 / 渐变时间，单位秒。
  intensity：强度，0 通常表示关闭。

示例：
  <Gaussian*(2,2)>      高斯模糊，强度 2，持续 2 秒
  <Fade*(1,1)>          黑屏 / 闪场，强度 1，持续 1 秒
  <Brightness*(2,0.4)>  亮度变化
  <Saturation*(2,-0.5)> 饱和度变化
  <Contrast*(2,0.3)>    对比度变化
  <Neon*(1,2)>          霓虹 / RGB 分离
  <Trail*(1,1)>         残影 / 拖尾
  <Rainbow*(1,1)>       彩虹偏移
  <Flash*(1,1)>         闪白
  <Vignette*(1,1)>      暗角 / 收缩感
  <Zoom*(1,1)>          缩放冲击
  <Glitch*(1,1)>        故障效果
  <TVNoise*(1,1)>       横向电视噪声

六、编辑器分段背景
------------------
只影响编辑器显示，不发送给 View。
  &FF00FF
  &55AAFF
  &NULL
";
}
