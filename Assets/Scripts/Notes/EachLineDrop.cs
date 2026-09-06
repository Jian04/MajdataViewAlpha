using UnityEngine;
using MajdataCore;
#nullable enable
public class EachLineDrop : MonoBehaviour
{
    public float time;
    public int startPosition = 1;
    public int curvLength = 1;
    public float speed = 1;
    public double noteScrollPos;
    public string scrollType;
    public float spawnRadius = NoteDrop.DefaultSpawnRadius;
    public float destroyRadius = NoteDrop.DefaultDestroyRadius;
    public SpawnVisualMode spawnMode = SpawnVisualMode.Rewind;
    private SpawnCrossingMemo spawnCrossingMemo;
    public float bounceDuration;
    public double bounceStartTime;
    public float bounceHSpeedMultiplier = 1f;
    public float bounceDirection = 1f;
    public bool previewOnly;

    public GameObject obj1;
    public GameObject obj2;

    public Sprite[] curvSprites;
    private SpriteRenderer sr;
    private SpriteRenderer obj1Renderer = null!;
    private SpriteRenderer obj2Renderer = null!;

    private AudioTimeProvider timeProvider;

    public void ConfigureBounce(float hSpeedMultiplier)
    {
        bounceHSpeedMultiplier = hSpeedMultiplier;
        bounceDirection = SvController.GetBounceDirection(
            time, hSpeedMultiplier, scrollType);
        bounceStartTime = SvController.GetBounceStartTime(
            time, bounceDuration, hSpeedMultiplier, scrollType);
    }

    // Only this object's own renderer, because that is the only one Start
    // suppresses; see NoteDrop.HideSpriteUntilInitialized for why Awake.
    private void Awake()
    {
        if (TryGetComponent<SpriteRenderer>(out var own))
            own.forceRenderingOff = true;
    }

    // Start is called before the first frame update
    private void Start()
    {
        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();

        sr = gameObject.GetComponent<SpriteRenderer>();
        obj1Renderer = obj1 != null ? obj1.GetComponent<SpriteRenderer>() : null;
        obj2Renderer = obj2 != null ? obj2.GetComponent<SpriteRenderer>() : null;
        sr.sprite = curvSprites[curvLength - 1];
        sr.forceRenderingOff = true;
    }

    // Update is called once per frame
    private void Update()
    {
        if ((!timeProvider.isStart && !timeProvider.IsPaused) || timeProvider.AudioTime < AlphaVisualTiming.GameplayRevealTime)
        {
            sr.forceRenderingOff = true;
            return;
        }
        if (obj1Renderer == null || obj2Renderer == null)
        {
            Destroy(gameObject);
            return;
        }

        var beforeJudge = timeProvider.AudioTime < time;
        var bounceProgress = bounceDuration > 0f && beforeJudge
            ? SvController.GetBounceProgress(
                time, bounceDuration, bounceHSpeedMultiplier, bounceDirection,
                timeProvider.AudioTime, scrollType)
            : 1f;
        var isBouncing = bounceDuration > 0f &&
                         beforeJudge &&
                         timeProvider.AudioTime >= bounceStartTime &&
                         (spawnMode == SpawnVisualMode.Once ||
                          bounceProgress >= -(float)AlphaVisualTiming.Epsilon);
        if (bounceDuration > 0f && beforeJudge && !isBouncing)
        {
            sr.forceRenderingOff = true;
            return;
        }

        var distance = isBouncing
            ? GetBounceDistance(bounceProgress)
            : SvController.GetVisualRadius(
                noteScrollPos,
                speed,
                timeProvider.AudioTime,
                spawnRadius,
                destroyRadius,
                scrollType);
        var pendingScale = Mathf.Clamp01(
            AlphaVisualTiming.GetSpawnScale(
                distance, spawnRadius, destroyRadius));
        if (timeProvider.AudioTime - time > 0)
        {
            if (!previewOnly)
                Destroy(gameObject);
            sr.forceRenderingOff = true;
            return;
        }
        var isPastSpawnNow = isBouncing ||
            SvController.IsPastSpawnNow(
                noteScrollPos,
                speed,
                timeProvider.AudioTime,
                spawnRadius,
                scrollType,
                destroyRadius);
        // Only SPAWNMODE=once asks where the line has ever been; the default mode
        // asks where it is now. Working the first one out either way made every
        // chart search its whole elapsed scroll curve once per line per frame for
        // an answer it discarded.
        var running = isBouncing || isPastSpawnNow ||
            (spawnMode == SpawnVisualMode.Once &&
             SvController.HasEverCrossedSpawn(
                 ref spawnCrossingMemo,
                 noteScrollPos,
                 speed,
                 timeProvider.AudioTime,
                 spawnRadius,
                 scrollType,
                 destroyRadius));
        if (!running)
        {
            // A line that is not running waits on the spawn ring, the same as the
            // notes it is drawn under. Keyed on "has ever crossed" instead, a line
            // that had crossed and rewound kept following the integrated radius out
            // past that ring while its notes waited on it.
            distance = spawnRadius;
            sr.forceRenderingOff = pendingScale <= 0.3f;
        }
        else
            sr.forceRenderingOff =
                obj1Renderer.forceRenderingOff || obj2Renderer.forceRenderingOff;

        var lineScale = Mathf.Abs(distance / NoteDrop.DefaultDestroyRadius);
        transform.localScale = new Vector3(lineScale, lineScale, 1f);
        transform.rotation = Quaternion.Euler(
            0, 0, -45f * (startPosition - 1) + (distance < 0f ? 180f : 0f));
    }

    private float GetBounceDistance(float progress)
    {
        var fromApex = progress * 2f - 1f;
        return spawnRadius + (destroyRadius - spawnRadius) * fromApex * fromApex;
    }
}
