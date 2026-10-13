using MajdataEdit.Editor;

namespace MajdataEdit;
internal static class DurationBrushRegression
{
    public static void Run()
    {
        foreach(var item in new[]{
            ("1-5[8:1]/2h[4:1]/3/12",'b',"1b-5b[8:1]/2hb[4:1]/3b/1b/2b"),
            ("1-5[8:1]*-7[4:1]",'b',"1b-5b[8:1]*-7b[4:1]"),
            ("1-3[8:1]-5[4:1]",'b',"1b-3b[8:1]-5b[4:1]"),
            ("1-5",'b',"1b-5b"),
            ("1b-5b[8:1]",'b',"1b-5b[8:1]"),
            ("8P91K1[8:1]",'b',"8bP91K1b[8:1]"),
            ("{4}<TEXT*(\"b/x\")>1h~[b/x.png],||1-5[4:1]\nXD1[4:1]",'x',"{4}<TEXT*(\"b/x\")>1xh~[b/x.png],||1-5[4:1]\nXD1[4:1]"),
            ("1-5[8:1]/2dh[4:1]",'x',"1x-5[8:1]/2dxh[4:1]"),
            ("2h[8:1]",'b',"2hb[8:1]"),
            ("2h[8:1]/4<8d",'b',"2hb[8:1]/4b<8db"),
            ("}2h[8:1]/4<8d",'b',"}2hb[8:1]/4b<8db"),
            ("{4}2h[8:1]/4<8d",'b',"{4}2hb[8:1]/4b<8db"),
            (" 2xh[8:1]~[hold.png] ",'b'," 2xhb[8:1]~[hold.png] "),
            ("2hb[8:1]/2bh[4:1]",'b',"2hb[8:1]/2bh[4:1]"),
            ("A1h[8:1]/E2h",'b',"A1hb[8:1]/E2hb") })
        {
            var changed=NoteModifierBrush.Transform(item.Item1,item.Item2);
            if(changed!=item.Item3) throw new Exception($"Modifier brush: {changed}, expected {item.Item3}");
        }
        Console.WriteLine("MODIFIER BRUSH PASS: 15 cases including plain and simultaneous Holds, partial selection boundaries, TouchHold breaks, paths, skins and EX heads.");
        var cases = new (string Input, string Mode, string Expected)[] {
            ("1-5[8:1]/1h", "max", "1-5[8:1]/1h[8:1]"),
            ("1-5[8:1]/1h", "min", "1-5[384:1]/1h[384:1]"),
            ("{4}1h~[a.png]/E1h[4:1]/XD1[2:1],2,", "8:1", "{4}1h[8:1]~[a.png]/E1h[8:1]/XD1[8:1],2,"),
            ("1-5[1##8:1]/2-6[190#4:1]", "16:1", "1-5[1##16:1]/2-6[190#16:1]"),
            ("<TEXT*(\"[4:1]\")>1h[4:1]~[a.png]", "2:1", "<TEXT*(\"[4:1]\")>1h[2:1]~[a.png]"),
            ("1-5[8:1]*-7[4:1],1-3[4:1]-5[8:1]", "1:1", "1-5[1:1]*-7[1:1],1-3[1:1]-5[1:1]"),
            ("1~[A1<A1[4:1]]/1h~[hold.png]", "8:1", "1~[A1<A1[4:1]]/1h[8:1]~[hold.png]"),
            ("1h[#0.5],|| 2h[4:1]\n2h", "4:1", "1h[4:1],|| 2h[4:1]\n2h[4:1]"),
            ("1-5,\n2h", "8:1", "1-5[8:1],\n2h[8:1]")
        };
        foreach (var (input, mode, expected) in cases)
        {
            var actual = NoteDurationBrush.Transform(input, mode);
            if (actual != expected) throw new Exception($"Duration brush: {input} => {actual}, expected {expected}");
        }
        Console.WriteLine($"DURATION BRUSH PASS: {cases.Length} cases including extremes, implicit holds, branches, skins and slide delays.");
    }
}
