using UnityEngine;
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

    public GameObject obj1;
    public GameObject obj2;

    public Sprite[] curvSprites;
    private SpriteRenderer sr;
    private SpriteRenderer obj1Renderer = null!;
    private SpriteRenderer obj2Renderer = null!;
    private bool hasLeftSpawn;

    private AudioTimeProvider timeProvider;

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
        if ((!timeProvider.isStart && !timeProvider.IsPaused) || timeProvider.AudioTime < 0f)
        {
            sr.forceRenderingOff = true;
            return;
        }
        if (obj1Renderer == null || obj2Renderer == null)
        {
            Destroy(gameObject);
            return;
        }

        var distance = 4.8f - speed * (float)(noteScrollPos -
            SvController.GetCumulativeScroll(timeProvider.AudioTime, scrollType));
        var destScale = (distance - spawnRadius + 2.5f) / 2.5f;
        if (timeProvider.AudioTime - time > 0) Destroy(gameObject);
        if (!hasLeftSpawn && distance >= spawnRadius)
            hasLeftSpawn = true;
        if (!hasLeftSpawn)
        {
            distance = spawnRadius;
            sr.forceRenderingOff = destScale <= 0.3f;
        }
        else
            sr.forceRenderingOff = distance > 4.8f ||
                                   obj1Renderer.forceRenderingOff || obj2Renderer.forceRenderingOff;

        var lineScale = Mathf.Abs(distance / 4.8f);
        transform.localScale = new Vector3(lineScale, lineScale, 1f);
        transform.rotation = Quaternion.Euler(
            0, 0, -45f * (startPosition - 1) + (distance < 0f ? 180f : 0f));
    }
}
