namespace MajdataCore
{
    // Shared design coordinates and animation periods for WPF and Unity.
    // Sources and original filenames are recorded beside the downloaded assets.
    public static class ChunithmBackground
    {
        public const string XVerseX = "chunithm-xversex";
        public const string Mate = "chunithm-mate";
        public const string Magical = "magical";
        public static bool Contains(string theme) => theme == XVerseX || theme == Mate || theme == Magical;

        public sealed class Layer
        {
            public readonly string file;
            public readonly float x, y, width, height;
            public float rotation, pulse, period, driftX, driftY, tileX, tileY;
            public float tileHeight, shimmerPeriod, opacity = 1;
            public float enterX, enterY, exitX, exitY;
            public string solidColor;
            public bool grayscaleScreen;
            public bool backdrop;
            public Layer(string file, float x, float y, float width, float height)
            { this.file = file; this.x = x; this.y = y; this.width = width; this.height = height; }
        }

        public static Layer[] Layers(string theme) => theme == Magical ? new[]
        {
            new Layer("", 0, 0, 1920, 1080) { backdrop = true, solidColor = "#3AADEE" },
            new Layer("croud_01.png", 0, 0, 1920, 400) { enterX = 220, exitX = -220, tileX = 800, tileHeight = 800, period = 20 },
            new Layer("croud_02.png", 0, 0, 1920, 400) { enterX = 220, exitX = -220, tileX = 800, tileHeight = 800, period = 35 },
            new Layer("croud_03.png", 0, 0, 1920, 400) { enterX = 220, exitX = -220, tileX = 800, tileHeight = 800, period = 50 },
            new Layer("sp_bg_02.png", 0, 0, 1920, 1080) { backdrop = true },
            new Layer("sea.png", 0, 642, 1920, 438) { enterX = 220, exitX = -220, tileX = 600, tileHeight = 600, period = 60, opacity = .7f, shimmerPeriod = 6, grayscaleScreen = true },
            new Layer("staff_01.png", 57.6f, -21.6f, 268.8f, 402.5f) { opacity = .65f },
            new Layer("island_01.png", 0, 378, 614.4f, 423.4f) { enterX = -900, exitX = -900 },
            new Layer("island_02.png", 1286.4f, 453.6f, 633.6f, 440.3f) { enterX = 900, exitX = 900 },
            new Layer("staff_03.png", -76.8f, 410.4f, 576, 640.8f) { opacity = .65f },
            new Layer("staff_02.png", 1536, 594, 499.2f, 581.7f) { opacity = .65f },
            new Layer("note_01.png", 38.4f, 507.6f, 211.2f, 105.1f),
            new Layer("note_02.png", 1633, 702, 140.6f, 183.6f),
            new Layer("front.png", 0, 937, 1920, 143) { enterY = 360, exitY = 360 },
        } : theme == Mate ? new[]
        {
            new Layer("bg.jpg", 0, 0, 1920, 1080) { backdrop = true },
            new Layer("mate_flot-1.png", 384, 0, 500, 1080) { tileY = 1088, period = 15 },
            new Layer("mate_flot-3.png", 0, 0, 1920, 1080) { enterX = -600, enterY = 400, exitX = -600, exitY = 400 },
            new Layer("mate_flot-2.png", 876, 216, 744, 540) { enterY = -650, exitY = -650 },
            new Layer("mate_flot-5.png", 0, 0, 1920, 1080) { enterX = -500, enterY = -350, exitX = -500, exitY = -350, pulse = .05f, period = 8 },
            new Layer("mate_flot-4.png", 0, 0, 1920, 1080) { enterX = 500, enterY = 350, exitX = 500, exitY = 350, pulse = .05f, period = 5 },
            new Layer("mate_flot-6.png", 0, 2, 1920, 10) { enterY = -250, exitY = -250, tileX = 1777.5f, period = 60 },
            new Layer("mate_flot-6.png", 0, 1068, 1920, 10) { enterY = 250, exitY = 250, tileX = 1777.5f, period = -60 },
        } : theme == XVerseX ? new[]
        {
            new Layer("bg_pc.jpg", 0, 0, 1920, 1080) { backdrop = true },
            new Layer("verse_belt-left.png", 240, 0, 220, 558) { driftX = 176, driftY = 1728, period = 15 },
            new Layer("verse_belt-left.png", 620, 0, 200, 507.2f) { driftX = 160, driftY = 1728, period = 20 },
            new Layer("verse_belt-right.png", 1138, 0, 220, 542.4f) { driftX = -176, driftY = 1728, period = 25 },
            new Layer("verse_belt-right.png", 1478, 0, 180, 443.7f) { driftX = -144, driftY = 1728, period = 15 },
            new Layer("verse_town_pc.png", 0, 108, 1920, 938.4f),
            new Layer("globe_center.png", 637, 100, 492, 492) { rotation = -60, pulse = -.4f, period = 60 },
            new Layer("globe.png", 658, 121, 450, 450) { rotation = 240 },
            new Layer("globe_cross.png", 673, 188.8f, 420, 314.4f),
            new Layer("top_right.png", 1075, 0, 845, 176.5f),
            new Layer("bottom_left.png", 0, 860, 1055, 220),
            new Layer("floating_text.png", 0, 100, 1920, 32) { enterX = 500, exitX = -500, tileX = 493, period = 60 },
            new Layer("floating_text.png", 0, 948, 1920, 32) { enterX = -500, exitX = 500, tileX = 493, period = -60 },
        } : System.Array.Empty<Layer>();
    }
}
