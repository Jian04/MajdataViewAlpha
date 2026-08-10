using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

// Dynamic background for the opening cover transition, recreating the layered
// maiDecorationBg from the official maimai site in code: radial gradient, slowly
// rotating pattern, three rings, riseAndFade tiles/stars, and orbiting objects.
// Three themes are available through introBgTheme, sent over HTTP by Edit:
//   default    = original pink/purple scene objects and Animator, kept unchanged
//   circleplus = Japanese CiRCLE PLUS website
//   circle     = international website
// Both website themes are fully preloaded and built in Init, so texture decoding
// and upload happen only at startup. Switching only changes visibility and cannot
// stall chart entry or first playback. Original objects move into a hidden holder,
// invalidating Animator paths, then return to their original positions for default.
// Every animation is a pure function of timeline t. During playback, t comes from
// AudioTimeProvider.TimelineTime, which is continuous, unclamped, and crosses zero
// smoothly so the exit ends exactly at t=0. AudioTime itself is clamped during
// preload and jumps at zero, which caused the old sudden disappearance at 0s.
// Edit preview scrubbing is anchored with SetTimeline; recording is frame-deterministic.
// Entry starts at t=-5 for EntryDuration: center art expands and fades in, objects
// fly outward from the center, corner frames slide in, and the backdrop fades in
// without scaling, which would expose black edges.
// Exit over t in [-ExitDuration,0] uses cubic acceleration: objects fly radially,
// corner frames retreat and fade, center layers expand at varied rates, and the
// gradient backdrop fades out with acceleration.
public class SongDetailIntroBg : MonoBehaviour
{
    private const float DesignWidth = 1920f;
    private const float DesignHeight = 1080f;
    private const float IntroLeadTime = 5f;    // Total cover transition time, matching PlaySongDetail's -5s
    private const float EntryDuration = 1.1f;
    private const float ExitDuration = 1.6f;
    // View uses a circular viewport whose diameter equals the screen height, so
    // side elements must move inward to remain visible.
    // Inset horizontal bars slightly and vertical bars more to keep the visible frame nearly square.
    private const float CornerInsetX = 340f;
    private const float CornerInsetY = 20f;
    private const float SideInsetX = 360f;     // Horizontal inset for side-anchored objects and tiles
    private const float PatternAlpha = 0.45f;  // Approximates the website's white overlay-blended pattern
    // Phase origin places t=-5 at the start of the colorful ring pulse for a clear spin and slowdown
    private const float ColorfulEpoch = 105f;
    private const float PhaseEpoch = 120f;

    private static readonly string[] LegacyObjectNames = { "BGColor", "Circles", "Mask", "1080BG" };
    private static readonly Dictionary<string, Texture2D> SharedTextures = new();

    private enum Anchor { Center, TopLeft, TopRight, BottomLeft, BottomRight }
    private enum ExitKind { None, FlyOutward, RiseAway, CornerRetreat, CenterZoom, Backdrop }

    private sealed class Deco
    {
        public RectTransform rt;
        public RawImage img;
        public float baseAlpha = 1f;
        public Vector2 designSize;
        public Vector2 designPos;      // (left/right, top), interpreted by anchor
        public Anchor anchor = Anchor.Center;
        public float risePeriod;       // >0 enables riseAndFade
        public float riseDelay;
        public bool riseReverse;       // International pink stars fall downward
        public bool flipY;             // Flip texture vertically
        public float rotDegPerSec;     // Self-rotation speed; positive is counterclockwise
        public float tiltRange;        // Nonzero enables oscillation in degrees with tiltPeriod
        public float tiltPeriod;
        public float zoomAmount;       // CenterZoom exit scale, varied per layer for depth
        public ExitKind exit = ExitKind.None;
        public bool inOrbit;
        public Vector2 basePos;
        public Vector2 centerOffset;   // Offset from basePos to canvas center for entry
        public Vector2 radialDir;      // Unit vector from center to element for radial exit
    }

    // Complete prebuilt website theme; switching only changes visibility
    private sealed class ThemeRig
    {
        public string name;
        public RectTransform root;
        public RectTransform orbitWrapper;
        public readonly List<Deco> decos = new();
        public Deco gradientDeco;
        public Deco colorfulDeco;
        public float layoutWidth = -1f;
        public float layoutHeight = -1f;
    }

    private sealed class LegacyObject
    {
        public Transform transform;
        public int siblingIndex;
    }

    private GameObject canvasRoot;
    private AudioTimeProvider provider;
    private readonly Dictionary<string, ThemeRig> rigs = new();
    private readonly List<LegacyObject> legacyObjects = new();
    private readonly List<Texture2D> ownedTextures = new();
    private Transform legacyHolder;
    private ThemeRig activeRig;
    private string activeTheme = "default";
    private float timeline = 1f;   // Hidden at >=0
    private float speed = 1f;
    private bool initialized;

    // For website themes this component also drives card entry/exit instead of Animator:
    // the cover grows and fades from 0.35 to 1 while the frame settles from 1.15 to 1;
    // on exit both expand and fade with the center art. The default theme keeps Animator.
    private Animator canvasAnimator;
    private Transform cardTransform;
    private Transform jacketTransform;
    private RawImage cardImage;
    private CanvasGroup cardGroup;
    private Vector3 cardBaseScale = Vector3.one;
    private Vector3 jacketBaseScale = Vector3.one;
    private Color cardBaseColor = Color.white;

    public bool TakesOverCard => activeRig != null;

    public void Init(GameObject canvasSongDetail, AudioTimeProvider timeProvider)
    {
        canvasRoot = canvasSongDetail;
        provider = timeProvider;
        if (canvasRoot == null)
            return;

        // Track original objects without destroying them because the default theme still uses them
        foreach (var name in LegacyObjectNames)
        {
            var found = canvasRoot.transform.Find(name);
            if (found != null)
                legacyObjects.Add(new LegacyObject
                {
                    transform = found,
                    siblingIndex = found.GetSiblingIndex()
                });
        }
        var holderObj = new GameObject("LegacyIntroHolder", typeof(RectTransform));
        holderObj.SetActive(false);
        legacyHolder = holderObj.transform;
        legacyHolder.SetParent(canvasRoot.transform, false);

        canvasAnimator = canvasRoot.GetComponent<Animator>();
        cardTransform = canvasRoot.transform.Find("SongDetail");
        if (cardTransform != null)
        {
            cardBaseScale = cardTransform.localScale;
            cardImage = cardTransform.GetComponent<RawImage>();
            if (cardImage != null)
                cardBaseColor = cardImage.color;
            cardGroup = cardTransform.GetComponent<CanvasGroup>();
            if (cardGroup == null)
                cardGroup = cardTransform.gameObject.AddComponent<CanvasGroup>();
            jacketTransform = cardTransform.Find("Jacket");
            if (jacketTransform != null)
                jacketBaseScale = jacketTransform.localScale;
        }

        // Build both website themes at startup so decoding and GPU upload never occur during playback
        rigs["circleplus"] = BuildRig("circleplus");
        rigs["circle"] = BuildRig("circle");
        PrewarmRigs();

        initialized = true;
        ApplyTheme(activeTheme);
        Apply(timeline);
    }

    private void PrewarmRigs()
    {
        if (canvasRoot == null)
            return;

        // Build layout and Canvas geometry during scene initialization. Waiting
        // until the first negative-time frame makes SetActive + layout + texture
        // upload land on the same frame as the intro transition.
        var canvasWasActive = canvasRoot.activeSelf;
        canvasRoot.SetActive(true);
        foreach (var rig in rigs.Values)
        {
            if (rig?.root == null)
                continue;
            rig.root.gameObject.SetActive(true);
            RelayoutIfNeeded(rig);
        }
        Canvas.ForceUpdateCanvases();
        foreach (var rig in rigs.Values)
            if (rig?.root != null)
                rig.root.gameObject.SetActive(false);
        canvasRoot.SetActive(canvasWasActive);
    }

    public void SetTheme(string themeName)
    {
        if (!initialized)
            return;
        themeName = themeName != null ? themeName.Trim().ToLowerInvariant() : "default";
        if (!rigs.ContainsKey(themeName))
            themeName = "default";
        if (themeName == activeTheme)
            return;
        activeTheme = themeName;
        ApplyTheme(themeName);
        Apply(timeline);
    }

    public void SetTimeline(float t, float playSpeed)
    {
        timeline = t;
        speed = playSpeed;
        if (initialized && canvasRoot != null && canvasRoot.activeInHierarchy)
            Apply(timeline);
    }

    public void SetSpeed(float playSpeed)
    {
        speed = playSpeed;
    }

    private void Update()
    {
        if (!initialized || canvasRoot == null || !canvasRoot.activeInHierarchy)
            return;
        if (provider != null && provider.isStart && !provider.IsPreview)
            timeline = provider.TimelineTime;
        else
            timeline += Time.deltaTime * speed;
        Apply(timeline);
    }

    // ---------- Theme switching ----------

    private void ApplyTheme(string themeName)
    {
        var useLegacy = !rigs.TryGetValue(themeName, out var rig);
        activeRig = useLegacy ? null : rig;

        foreach (var pair in rigs)
            if (pair.Value.root != null)
                pair.Value.root.gameObject.SetActive(pair.Value == activeRig);

        // Website themes disable Animator and take over the card; default restores Animator.
        // Clear any final scale, alpha, or visibility state left by Animator when taking over again.
        if (canvasAnimator != null)
            canvasAnimator.enabled = useLegacy;
        if (cardTransform != null)
        {
            if (!useLegacy)
                cardTransform.gameObject.SetActive(true);
            cardTransform.localScale = cardBaseScale;
            if (jacketTransform != null)
                jacketTransform.localScale = jacketBaseScale;
            if (cardImage != null)
                cardImage.color = cardBaseColor;
            if (cardGroup != null)
                cardGroup.alpha = 1f;
        }

        // Move original objects into a hidden holder for website themes so changed paths
        // invalidate Animator bindings; restore their original sibling order for default.
        if (useLegacy)
        {
            foreach (var legacy in legacyObjects)
            {
                if (legacy.transform == null)
                    continue;
                legacy.transform.SetParent(canvasRoot.transform, false);
            }
            foreach (var legacy in legacyObjects)
                if (legacy.transform != null)
                    legacy.transform.SetSiblingIndex(
                        Mathf.Min(legacy.siblingIndex, canvasRoot.transform.childCount - 1));
        }
        else
        {
            foreach (var legacy in legacyObjects)
                if (legacy.transform != null)
                    legacy.transform.SetParent(legacyHolder, false);
            if (activeRig?.root != null)
                activeRig.root.SetSiblingIndex(0);
        }
    }

    // ---------- Construction ----------

    private ThemeRig BuildRig(string themeName)
    {
        var rig = new ThemeRig { name = themeName };
        var intl = themeName == "circle";

        var rootObj = new GameObject("IntroBg_" + themeName, typeof(RectTransform));
        rootObj.SetActive(false);
        rig.root = (RectTransform)rootObj.transform;
        rig.root.SetParent(canvasRoot.transform, false);
        rig.root.SetSiblingIndex(0);
        rig.root.anchorMin = Vector2.zero;
        rig.root.anchorMax = Vector2.one;
        rig.root.offsetMin = Vector2.zero;
        rig.root.offsetMax = Vector2.zero;

        // Radial-gradient backdrop: Japanese is pink with purple/cyan edges; international is pink to magenta
        var gradientStops = intl
            ? new[]
            {
                (0.00f, new Color32(0xFF, 0xB7, 0xCD, 0xFF)),
                (0.40f, new Color32(0xFF, 0xB7, 0xCD, 0xFF)),
                (1.00f, new Color32(0xFF, 0x47, 0x99, 0xFF))
            }
            : new[]
            {
                (0.00f, new Color32(0xFF, 0xBC, 0xD7, 0xFF)),
                (0.27f, new Color32(0xFF, 0xBC, 0xD7, 0xFF)),
                (0.35f, new Color32(0xFC, 0x9C, 0xCC, 0xFF)),
                (0.56f, new Color32(0xFF, 0x97, 0xEF, 0xFF)),
                (0.77f, new Color32(0xBF, 0x9C, 0xFF, 0xFF)),
                (0.98f, new Color32(0x55, 0xE5, 0xFD, 0xFF)),
                (1.00f, new Color32(0x55, 0xE5, 0xFD, 0xFF))
            };
        rig.gradientDeco = AddDeco(rig, "Gradient", BuildRadialGradientTexture(themeName, gradientStops),
            new Vector2(DesignWidth, DesignHeight), Vector2.zero, Anchor.Center);
        rig.gradientDeco.exit = ExitKind.Backdrop;

        // Slowly rotating white pattern
        var pattern = AddDeco(rig, "Pattern", LoadTexture(themeName, "bg_pattern"),
            new Vector2(DesignWidth, DesignWidth), Vector2.zero, Anchor.Center);
        pattern.baseAlpha = PatternAlpha;
        pattern.rotDegPerSec = -360f / 500f;
        pattern.exit = ExitKind.CenterZoom;
        pattern.zoomAmount = 0.3f;   // Center group expands slowest, with slight speed differences between layers

        // Three center rings expand at different exit rates for depth
        var yellow = AddDeco(rig, "CircleYellow", LoadTexture(themeName, "circle_yellow"),
            intl ? new Vector2(1026f, 898f) : new Vector2(1026f, 530f),
            Vector2.zero, Anchor.Center);
        yellow.exit = ExitKind.CenterZoom;
        yellow.zoomAmount = 0.22f;
        if (intl)
        {
            yellow.tiltRange = -90f;   // International theme oscillates between 0 and 90 degrees over 80s
            yellow.tiltPeriod = 80f;
        }

        var white = AddDeco(rig, "CircleWhite", LoadTexture(themeName, "circle_white"),
            new Vector2(788f, 762f), Vector2.zero, Anchor.Center);
        white.rotDegPerSec = 360f / 110f;
        white.exit = ExitKind.CenterZoom;
        white.zoomAmount = 0.1f;     // Innermost layer expands slowest

        rig.colorfulDeco = AddDeco(rig, "CircleColorful", LoadTexture(themeName, "circle_colorful"),
            new Vector2(953f, 947f), Vector2.zero, Anchor.Center);
        rig.colorfulDeco.exit = ExitKind.CenterZoom;
        rig.colorfulDeco.zoomAmount = 0.16f;

        // Rising, fading tiles and stars; international pink stars fall and flip vertically
        AddRiser(rig, themeName, "TileGreen", "tile_green", 216f, 692f, Anchor.TopRight, 42f, 167f, 12f, 0f);
        AddRiser(rig, themeName, "TilePurpleL", "tile_purple_left", 192f, 593f, Anchor.TopLeft, 30f, 28f, 15f, 3f);
        AddRiser(rig, themeName, "TilePurpleR", "tile_purple_right", 140f, 340f, Anchor.TopRight, 300f, 0f, 10f, 1.5f);
        var pinkL = AddRiser(rig, themeName, "StarPinkL", "star_pink", 90f, 306f, Anchor.TopLeft, 268f, 562f, 6f, 4f);
        var pinkR = AddRiser(rig, themeName, "StarPinkR", "star_pink", 52f, 174f, Anchor.TopRight, 300f, 402f, 8f, 4f);
        if (intl)
        {
            pinkL.riseReverse = pinkR.riseReverse = true;
            pinkL.flipY = pinkR.flipY = true;
        }
        AddRiser(rig, themeName, "StarYellowL", "star_yellow", 64f, 213f, Anchor.TopLeft, 332f, 168f, 7f, 0.5f);
        AddRiser(rig, themeName, "StarYellowR", "star_yellow", 64f, 213f, Anchor.TopRight, 524f, 618f, 10f, 5f);

        // Orbit layer completes one revolution in 70s while each object also spins
        var orbitObj = new GameObject("Orbit", typeof(RectTransform));
        rig.orbitWrapper = (RectTransform)orbitObj.transform;
        rig.orbitWrapper.SetParent(rig.root, false);
        rig.orbitWrapper.anchorMin = Vector2.zero;
        rig.orbitWrapper.anchorMax = Vector2.one;
        rig.orbitWrapper.offsetMin = Vector2.zero;
        rig.orbitWrapper.offsetMax = Vector2.zero;

        AddOrbiter(rig, themeName, "CubeSmall", "3d_cube", 88f, 88f, Anchor.TopLeft, 80f, 130f, -360f / 18f);
        AddOrbiter(rig, themeName, "Cube", "3d_cube", 113f, 102f, Anchor.TopRight, 100f, 400f, 360f / 25f);
        AddOrbiter(rig, themeName, "StarSmall3d", "3d_star_small", 34f, 40f, Anchor.TopRight, 506f, 192f, -360f / 15f);
        AddOrbiter(rig, themeName, "Stars3d", "3d_stars", 93f, 78f, Anchor.TopRight, 260f, 700f, -360f / 28f);
        AddOrbiter(rig, themeName, "GloveBlue", "3d_glove_blue", 69f, 75f, Anchor.TopLeft, 702f, 34f, 360f / 20f);
        AddOrbiter(rig, themeName, "GlovePink", "3d_glove_pink", 108f, 128f, Anchor.TopLeft, 568f, 34f, 360f / 16f);
        if (!intl)
        {
            // These three exist only on the Japanese site; replace its missing 3d_star.png with an enlarged small star
            AddOrbiter(rig, themeName, "Star3d", "3d_star_small", 85f, 87f, Anchor.TopLeft, 540f, 714f, 360f / 22f);
            AddOrbiter(rig, themeName, "CirclePink3d", "3d_pink", 120f, 120f, Anchor.TopLeft, 200f, 500f, 360f / 30f);
            AddOrbiter(rig, themeName, "CircleOrange3d", "3d_orange", 120f, 120f, Anchor.TopRight, 200f, 600f, -360f / 30f);
        }

        // Corner frames use theme-specific sizes, sliding inward on entry and retreating on exit
        AddCorner(rig, themeName, "CornerTL", "corner_top_left", 853f, 150f, Anchor.TopLeft);
        AddCorner(rig, themeName, "CornerTR", "corner_top_right",
            intl ? 316f : 568f, intl ? 382f : 1155f, Anchor.TopRight);
        AddCorner(rig, themeName, "CornerBL", "corner_bottom_left",
            intl ? 231f : 280f, intl ? 429f : 911f, Anchor.BottomLeft);
        AddCorner(rig, themeName, "CornerBR", "corner_bottom_right", 683f, 168f, Anchor.BottomRight);

        return rig;
    }

    private Deco AddDeco(ThemeRig rig, string name, Texture2D texture, Vector2 designSize,
        Vector2 designPos, Anchor anchor, Transform parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent != null ? parent : rig.root, false);
        var img = go.GetComponent<RawImage>();
        img.texture = texture;
        img.raycastTarget = false;
        go.SetActive(texture != null);

        var deco = new Deco
        {
            rt = rt,
            img = img,
            designSize = designSize,
            designPos = designPos,
            anchor = anchor
        };
        rig.decos.Add(deco);
        return deco;
    }

    private Deco AddRiser(ThemeRig rig, string themeName, string name, string file,
        float w, float h, Anchor anchor, float x, float y, float period, float delay)
    {
        var deco = AddDeco(rig, name, LoadTexture(themeName, file),
            new Vector2(w, h), new Vector2(x, y), anchor);
        deco.risePeriod = period;
        deco.riseDelay = delay;
        deco.exit = ExitKind.RiseAway; // Long tiles and stars exit quickly along their travel direction
        return deco;
    }

    private void AddOrbiter(ThemeRig rig, string themeName, string name, string file,
        float w, float h, Anchor anchor, float x, float y, float rotDegPerSec)
    {
        var deco = AddDeco(rig, name, LoadTexture(themeName, file),
            new Vector2(w, h), new Vector2(x, y), anchor, rig.orbitWrapper);
        deco.rotDegPerSec = rotDegPerSec;
        deco.exit = ExitKind.FlyOutward;
        deco.inOrbit = true;
    }

    private void AddCorner(ThemeRig rig, string themeName, string name, string file,
        float w, float h, Anchor anchor)
    {
        var deco = AddDeco(rig, name, LoadTexture(themeName, file),
            new Vector2(w, h), Vector2.zero, anchor);
        deco.exit = ExitKind.CornerRetreat;
    }

    private Texture2D LoadTexture(string themeName, string fileName)
    {
        var cacheKey = themeName + ":" + fileName;
        if (SharedTextures.TryGetValue(cacheKey, out var cached) && cached != null)
            return cached;

        var path = Path.Combine(Application.streamingAssetsPath,
            "Background", "SongDetailBg", themeName, fileName + ".png");
        if (!File.Exists(path))
            return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(path)))
        {
            Destroy(texture);
            return null;
        }
        texture.wrapMode = TextureWrapMode.Clamp;
        SharedTextures[cacheKey] = texture;
        return texture;
    }

    private Texture2D BuildRadialGradientTexture(string themeName, (float pos, Color32 color)[] stops)
    {
        var cacheKey = "gradient:" + themeName;
        if (SharedTextures.TryGetValue(cacheKey, out var cached) && cached != null)
            return cached;

        const int width = 480;
        const int height = 270;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[width * height];
        // Circle gradient radius reaches the farthest corner, matching CSS radial-gradient(circle,...)
        var maxDist = Mathf.Sqrt(0.25f * (DesignWidth * DesignWidth + DesignHeight * DesignHeight));
        for (var yPix = 0; yPix < height; yPix++)
        {
            var dy = (yPix / (height - 1f) - 0.5f) * DesignHeight;
            for (var xPix = 0; xPix < width; xPix++)
            {
                var dx = (xPix / (width - 1f) - 0.5f) * DesignWidth;
                var t = Mathf.Sqrt(dx * dx + dy * dy) / maxDist;
                pixels[yPix * width + xPix] = SampleStops(stops, t);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        SharedTextures[cacheKey] = texture;
        return texture;
    }

    private static Color32 SampleStops((float pos, Color32 color)[] stops, float t)
    {
        for (var i = 1; i < stops.Length; i++)
        {
            if (t > stops[i].pos)
                continue;
            var (p0, c0) = stops[i - 1];
            var (p1, c1) = stops[i];
            var f = p1 > p0 ? Mathf.Clamp01((t - p0) / (p1 - p0)) : 0f;
            return Color32.Lerp(c0, c1, f);
        }
        return stops[stops.Length - 1].color;
    }

    // ---------- Layout ----------

    private static Vector2 CornerDirOf(Anchor anchor) => anchor switch
    {
        Anchor.TopLeft => new Vector2(-1f, 1f),
        Anchor.TopRight => new Vector2(1f, 1f),
        Anchor.BottomLeft => new Vector2(-1f, -1f),
        _ => new Vector2(1f, -1f)
    };

    private void RelayoutIfNeeded(ThemeRig rig)
    {
        var canvasRect = (RectTransform)canvasRoot.transform;
        var width = canvasRect.rect.width;
        var height = canvasRect.rect.height;
        if (width <= 0f || height <= 0f ||
            (Mathf.Abs(width - rig.layoutWidth) < 0.001f && Mathf.Abs(height - rig.layoutHeight) < 0.001f))
            return;
        rig.layoutWidth = width;
        rig.layoutHeight = height;
        // Scale by height, equivalent to width at 16:9. A square player window is much
        // narrower than the editor Game view but keeps element sizes consistent, with tighter sides.
        var scale = height / DesignHeight;

        // Anchor every element to canvas center using coordinates from a fixed 1920x1080 design.
        // Composition is independent of window width and aspect ratio, matching the 16:9 Game view.
        // Resizing scales by height without shifting; narrow windows only crop the sides.
        foreach (var deco in rig.decos)
        {
            if (deco.rt == null)
                continue;
            deco.rt.sizeDelta = deco.designSize * scale;
            Vector2 pivot, point;
            switch (deco.anchor)
            {
                case Anchor.TopLeft:
                    pivot = new Vector2(0f, 1f);
                    point = new Vector2(deco.designPos.x - DesignWidth * 0.5f,
                        DesignHeight * 0.5f - deco.designPos.y);
                    break;
                case Anchor.TopRight:
                    pivot = new Vector2(1f, 1f);
                    point = new Vector2(DesignWidth * 0.5f - deco.designPos.x,
                        DesignHeight * 0.5f - deco.designPos.y);
                    break;
                case Anchor.BottomLeft:
                    pivot = Vector2.zero;
                    point = new Vector2(-DesignWidth * 0.5f, -DesignHeight * 0.5f);
                    break;
                case Anchor.BottomRight:
                    pivot = new Vector2(1f, 0f);
                    point = new Vector2(DesignWidth * 0.5f, -DesignHeight * 0.5f);
                    break;
                default:
                    pivot = new Vector2(0.5f, 0.5f);
                    point = Vector2.zero;
                    break;
            }

            // Circular viewport: inset frames and side-anchored objects by fixed design-space amounts
            if (deco.exit == ExitKind.CornerRetreat)
            {
                var cornerDir = CornerDirOf(deco.anchor);
                point -= new Vector2(cornerDir.x * CornerInsetX, cornerDir.y * CornerInsetY);
            }
            else if (deco.anchor == Anchor.TopLeft)
                point.x += SideInsetX;
            else if (deco.anchor == Anchor.TopRight)
                point.x -= SideInsetX;

            var pos = point * scale;
            deco.rt.anchorMin = new Vector2(0.5f, 0.5f);
            deco.rt.anchorMax = new Vector2(0.5f, 0.5f);
            deco.rt.pivot = pivot;
            deco.rt.anchoredPosition = pos;
            deco.basePos = pos;

            // With the canvas center as anchor, pos directly defines entry and radial-exit vectors
            deco.centerOffset = -pos;
            deco.radialDir = pos.sqrMagnitude > 1e-4f
                ? pos.normalized
                : new Vector2(0f, 1f);
        }

        // Stretch the gradient backdrop to fill the screen even beyond the 16:9 design height
        if (rig.gradientDeco?.rt != null)
        {
            rig.gradientDeco.rt.anchorMin = Vector2.zero;
            rig.gradientDeco.rt.anchorMax = Vector2.one;
            rig.gradientDeco.rt.offsetMin = Vector2.zero;
            rig.gradientDeco.rt.offsetMax = Vector2.zero;
        }
    }

    // ---------- Per-frame application ----------

    private void Apply(float t)
    {
        var rig = activeRig;
        if (rig?.root == null)
            return;
        var visible = t < 0f;
        if (rig.root.gameObject.activeSelf != visible)
            rig.root.gameObject.SetActive(visible);
        if (!visible)
        {
            if (cardGroup != null)
                cardGroup.alpha = 0f; // End the card with the background so nothing remains
            return;
        }

        RelayoutIfNeeded(rig);
        if (rig.layoutWidth <= 0f)
            return; // Wait one frame for layout after activating the canvas

        // Entry starts at t=-5 with an out-cubic deceleration
        var enterP = Mathf.Clamp01((t + IntroLeadTime) / EntryDuration);
        var enterEase = 1f - (1f - enterP) * (1f - enterP) * (1f - enterP);
        var enterFade = Mathf.Clamp01(enterP * 2.5f); // Finish fading in during the first 40%
        // Exit uses cubic acceleration and reaches 1 exactly at t=0
        var exitP = Mathf.Clamp01(t / ExitDuration + 1f);
        var exitEase = exitP * exitP * exitP;
        var exitFade = 1f - exitP;
        var flyDist = rig.layoutWidth * 0.85f * exitEase; // Small objects fly outward quickly
        var orbitAngle = -(360f / 70f) * (t + PhaseEpoch);

        if (rig.orbitWrapper != null)
            rig.orbitWrapper.localEulerAngles = new Vector3(0f, 0f, orbitAngle);

        foreach (var deco in rig.decos)
        {
            if (deco.rt == null || !deco.rt.gameObject.activeSelf)
                continue;

            var alpha = deco.baseAlpha;
            var pos = deco.basePos;
            var uniformScale = 1f;

            if (deco.risePeriod > 0f)
            {
                var frac = Mathf.Repeat(t + PhaseEpoch - deco.riseDelay, deco.risePeriod) / deco.risePeriod;
                var travel = deco.rt.sizeDelta.y * 1.5f;
                pos.y += deco.riseReverse
                    ? Mathf.Lerp(travel, -travel, frac)
                    : Mathf.Lerp(-travel, travel, frac);
                if (frac < 0.1f)
                    alpha *= frac / 0.1f;
                else if (frac > 0.9f)
                    alpha *= (1f - frac) / 0.1f;
            }

            if (deco.rotDegPerSec != 0f)
                deco.rt.localEulerAngles =
                    new Vector3(0f, 0f, deco.rotDegPerSec * (t + PhaseEpoch));
            else if (deco.tiltRange != 0f && deco.tiltPeriod > 0f)
                deco.rt.localEulerAngles = new Vector3(0f, 0f,
                    deco.tiltRange * Mathf.PingPong((t + PhaseEpoch) / deco.tiltPeriod, 1f));

            switch (deco.exit)
            {
                case ExitKind.FlyOutward:
                    // Enter from canvas center; continue outward radially on exit.
                    // Orbit-local radial vectors map to screen-space radial vectors after rotation.
                    pos += deco.centerOffset * (1f - enterEase);
                    pos += deco.radialDir * flyDist;
                    alpha *= enterFade * exitFade;
                    break;
                case ExitKind.RiseAway:
                    // Enter like FlyOutward, then exit quickly along the rise direction
                    pos += deco.centerOffset * (1f - enterEase);
                    pos.y += (deco.riseReverse ? -1f : 1f) * rig.layoutHeight * 1.2f * exitEase;
                    alpha *= enterFade * exitFade;
                    break;
                case ExitKind.CornerRetreat:
                    // Slide in from offscreen, then retreat to the same corner and fade out
                    var outward = Vector2.Scale(CornerDirOf(deco.anchor), deco.rt.sizeDelta);
                    pos += outward * (1.35f * (1f - enterEase));
                    pos += outward * (1.35f * exitEase);
                    alpha *= enterFade * exitFade;
                    break;
                case ExitKind.CenterZoom:
                    // Expand and fade in from center; each layer continues outward by its own amount on exit
                    uniformScale = Mathf.Lerp(0.4f, 1f, enterEase) * (1f + deco.zoomAmount * exitEase);
                    alpha *= enterFade * exitFade;
                    break;
                case ExitKind.Backdrop:
                    // Fade in without scaling to avoid black edges.
                    // Accelerate the fade-out so it finishes exactly at t=0.
                    alpha *= enterFade * (1f - exitEase);
                    break;
            }

            deco.rt.anchoredPosition = pos;
            deco.rt.localScale = new Vector3(uniformScale, deco.flipY ? -uniformScale : uniformScale, 1f);
            var color = deco.img.color;
            color.a = alpha;
            deco.img.color = color;
        }

        // Colorful ring: 100s cycle with a burst-and-stop cubic-bezier(.01,.99,.28,.99) pulse
        if (rig.colorfulDeco?.rt != null)
        {
            var frac = Mathf.Repeat(t + ColorfulEpoch, 100f) / 100f;
            var progress = CssBezier(0.01f, 0.99f, 0.28f, 0.99f, frac);
            rig.colorfulDeco.rt.localEulerAngles = new Vector3(0f, 0f, 360f * progress);
        }

        // Card takeover: cover grows and fades from 0.35 to 1 while the frame settles
        // from 1.15 to 1, meeting exactly. Exit only fades with the background.
        if (cardTransform != null)
        {
            var frameEnter = Mathf.Lerp(1.15f, 1f, enterEase);
            var jacketEnter = Mathf.Lerp(0.35f, 1f, enterEase);
            cardTransform.localScale = cardBaseScale * frameEnter;
            if (jacketTransform != null)
                jacketTransform.localScale = jacketBaseScale * (jacketEnter / frameEnter);
            if (cardGroup != null)
                cardGroup.alpha = enterFade * exitFade;
        }
    }

    // CSS cubic-bezier timing: solve progress y from normalized time x
    private static float CssBezier(float x1, float y1, float x2, float y2, float x)
    {
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;
        float lo = 0f, hi = 1f;
        for (var i = 0; i < 20; i++)
        {
            var mid = (lo + hi) * 0.5f;
            if (BezierComponent(x1, x2, mid) < x)
                lo = mid;
            else
                hi = mid;
        }
        return BezierComponent(y1, y2, (lo + hi) * 0.5f);
    }

    private static float BezierComponent(float p1, float p2, float u)
    {
        var v = 1f - u;
        return 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u;
    }

    private void OnDestroy()
    {
        foreach (var texture in ownedTextures)
            if (texture != null)
                Destroy(texture);
        ownedTextures.Clear();
    }
}
