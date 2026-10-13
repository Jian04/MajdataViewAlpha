using System;

namespace MajdataCore
{
    // Shared by grammar, completion and the View material controller.
    public static class AlphaExtraFilters
    {
        public sealed class Definition
        {
            public readonly string Name, Canonical, Chinese, English, Japanese;
            public string Uniform => "_" + Canonical;
            public Definition(string name, string canonical, string chinese, string english, string japanese)
            { Name = name; Canonical = canonical; Chinese = chinese; English = english; Japanese = japanese; }
        }
        public static readonly Definition[] All =
        {
            new("SHATTER", "Shatter", "玻璃碎裂：中心冲击形成放射裂纹，分块仅轻微扰动，厚玻璃迅速向外震开，带立体厚边、层叠折射、反射和切面高光；强度 0～1，0 完全复原。", "Glass shatter: a central impact sends thick shards outward along gently varied radial fractures, with extruded edges, layered refraction, reflection and facet highlights; strength 0–1, 0 restores the image.", "ガラス破砕：中央の衝撃から放射状の亀裂が広がり、大きな破片が素早く外へ飛び、屈折・反射と面のハイライトを表示。強度0～1、0で復元。"),
            new("RADIALBLUR", "RadialBlur", "径向模糊：从中心向外拉出速度感；强度 0～1。", "Radial blur: streaks radiate from the center; strength 0–1.", "放射状ぼかし：中心から外側へ速度感。強度0～1。"),
            new("RIPPLE", "Ripple", "冲击波：圆环从中心反复向外扩散并折射画面；强度 0～1。", "Ripple: repeated expanding rings refract the image from the center; strength 0–1.", "衝撃波：中心から円環が繰り返し広がって画面を屈折。強度0～1。"),
            new("PIXELATE", "Pixelate", "像素化：强度 0～1，越大像素块越大；0 原样。", "Pixelation: strength 0–1 controls block size; 0 is unchanged.", "ピクセル化：強度0～1でブロックの大きさを変更。0は元画像。"),
            new("LENSDISTORT", "LensDistort", "镜头畸变：强度 -1～1，正数向外鼓起、负数向内收缩；0 原样。", "Lens distortion: strength -1–1; positive bulges outward, negative contracts inward; 0 is unchanged.", "レンズ歪み：強度-1～1。正で外側へ膨張、負で内側へ収縮、0は元画像。"),
            new("SPLIT", "Split", "分屏错位：横向条带交替左右错开；强度 0～1。", "Split displacement: horizontal strips alternate left and right; strength 0–1.", "分割ずれ：横帯を交互に左右へ移動。強度0～1。"),
            new("KALEIDOSCOPE", "Kaleidoscope", "万花筒：六个扇区镜像折叠，第 2 参数为旋转角度（度），支持正负；过渡时间内逐渐旋转到目标角度，False 关闭，0 度仍是万花筒。", "Kaleidoscope: six mirrored sectors; parameter 2 is rotation in degrees, including negative angles. Transitions rotate to the target; False turns it off, while 0 degrees remains enabled.", "万華鏡：6つの鏡映扇形。第2引数は回転角度（度）、負の値も可。遷移時間で目標角度へ回転。Falseで無効、0度は有効。"),
            new("BLOOM", "Bloom", "高亮泛光：提取高亮并向周围扩散；强度 0～4，不附带 RGB 分离。", "Bloom: bright areas glow into their surroundings; strength 0–4, without RGB splitting.", "ブルーム：明るい部分を周囲へ拡散。強度0～4、RGB分離なし。"),
            new("INVERT", "Invert", "反色：强度 0～1，1 完全反转 RGB 颜色。", "Invert: strength 0–1; 1 fully inverts RGB colors.", "反転：強度0～1、1でRGB色を完全に反転。"),
            new("POSTERIZE", "Posterize", "色阶压缩：强度 0～1，越大颜色层级越少。", "Posterization: strength 0–1; higher strength reduces color levels.", "ポスタリゼーション：強度0～1、高いほど色の階調が少なくなります。")
        };
        public static Definition Find(string name)
        {
            foreach (var item in All)
                if (string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Canonical, name, StringComparison.OrdinalIgnoreCase)) return item;
            return null;
        }
    }
}
