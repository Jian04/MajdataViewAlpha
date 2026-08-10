using Assets.Scripts.Types;
using UnityEngine;
#nullable enable
public class NoteEffectManager : MonoBehaviour
{
    public static float JudgeTextAlpha { get; private set; } = 1f;
    
    public Sprite hex;
    public Sprite star;
    // Slots 0-7 are A1-A8; slots 8-15 are D1-D8.
    private readonly Animator[] judgeAnimators = new Animator[16];
    private readonly GameObject[] judgeEffects = new GameObject[16];
    private readonly Animator[] tapAnimators = new Animator[16];
    private readonly Animator[] greatAnimators = new Animator[16];
    private readonly Animator[] goodAnimators = new Animator[16];
    private readonly SpriteRenderer[][] tapEffectRenderers = new SpriteRenderer[16][];
    private readonly SpriteRenderer[] judgeTextRenderers = new SpriteRenderer[16];
    private readonly SpriteRenderer[] judgeBreakTextRenderers = new SpriteRenderer[16];
    private readonly SpriteRenderer[] fastLateTextRenderers = new SpriteRenderer[16];

    private readonly GameObject[] tapEffects = new GameObject[16];
    private readonly GameObject[] greatEffects = new GameObject[16];
    private readonly GameObject[] goodEffects = new GameObject[16];

    private readonly Animator[] fastLateAnims = new Animator[16];
    private readonly GameObject[] fastLateEffects = new GameObject[16];
    private CustomSkin customSkin;
    Sprite[] judgeText;

    private static GameObject CloneForDZone(GameObject source)
    {
        var clone = Instantiate(source, source.transform.parent);
        var rotate = Quaternion.Euler(0f, 0f, 22.5f);
        clone.transform.localPosition = rotate * source.transform.localPosition;
        clone.transform.localRotation = rotate * source.transform.localRotation;
        return clone;
    }

    // Start is called before the first frame update
    private void Start()
    {
        var tapEffectParent = transform.GetChild(0).gameObject;
        var greatEffectParent = transform.GetChild(2).gameObject;
        var goodEffectParent = transform.GetChild(3).gameObject;
        var judgeEffectParent = transform.GetChild(1).gameObject;
        var flParent = transform.GetChild(4).gameObject;

        for (var i = 0; i < 16; i++)
        {
            if (i < 8)
            {
                judgeEffects[i] = judgeEffectParent.transform.GetChild(i).gameObject;
                fastLateEffects[i] = flParent.transform.GetChild(i).gameObject;
                goodEffects[i] = goodEffectParent.transform.GetChild(i).gameObject;
                greatEffects[i] = greatEffectParent.transform.GetChild(i).gameObject;
                tapEffects[i] = tapEffectParent.transform.GetChild(i).gameObject;
            }
            else
            {
                judgeEffects[i] = CloneForDZone(judgeEffects[i - 8]);
                fastLateEffects[i] = CloneForDZone(fastLateEffects[i - 8]);
                goodEffects[i] = CloneForDZone(goodEffects[i - 8]);
                greatEffects[i] = CloneForDZone(greatEffects[i - 8]);
                tapEffects[i] = CloneForDZone(tapEffects[i - 8]);
            }

            judgeAnimators[i] = judgeEffects[i].GetComponent<Animator>();
            judgeTextRenderers[i] = judgeEffects[i].transform.GetChild(0).GetChild(0).GetComponent<SpriteRenderer>();
            judgeBreakTextRenderers[i] = judgeEffects[i].transform.GetChild(0).GetChild(1).GetComponent<SpriteRenderer>();

            fastLateAnims[i] = fastLateEffects[i].GetComponent<Animator>();
            fastLateTextRenderers[i] = fastLateEffects[i].transform.GetChild(0).GetChild(0).GetComponent<SpriteRenderer>();

            greatAnimators[i] = goodEffects[i].GetComponent<Animator>();
            goodEffects[i].SetActive(false);

            greatAnimators[i] = greatEffects[i].GetComponent<Animator>();
            greatEffects[i].SetActive(false);

            tapAnimators[i] = tapEffects[i].GetComponent<Animator>();
            tapEffectRenderers[i] = tapEffects[i].GetComponentsInChildren<SpriteRenderer>(true);
            tapEffects[i].SetActive(false);
        }

        LoadSkin();
        ApplyJudgeTextAlpha(JudgeTextAlpha);
    }

    /// <summary>
    ///     Loads the judgement-text skin
    /// </summary>
    private void LoadSkin()
    {
        customSkin = GameObject.Find("Outline").GetComponent<CustomSkin>();
        judgeText = customSkin.JudgeText;

        foreach (var judgeEffect in judgeEffects)
        {
            judgeEffect.transform.GetChild(0).GetChild(0).gameObject.GetComponent<SpriteRenderer>().sprite =
                customSkin.JudgeText[0];
            judgeEffect.transform.GetChild(0).GetChild(1).gameObject.GetComponent<SpriteRenderer>().sprite =
                customSkin.JudgeText_Break;
        }
    }

    // Update is called once per frame
    public void PlayEffect(int position, bool isBreak, JudgeType judge = JudgeType.Perfect, Color noteColor = default)
    {
        var pos = position - 1;

        // COLOR only affects notes. These objects are reused, so also clear any
        // tint left by an earlier colored note.
        foreach (var sr in tapEffectRenderers[pos])
            sr.color = Color.white;

        switch (judge)
        {
            case JudgeType.LateGood:
            case JudgeType.FastGood:
                judgeTextRenderers[pos].sprite = judgeText[1];
                ResetEffect(position);
                if(isBreak)
                {
                    tapEffects[pos].SetActive(true);
                    tapAnimators[pos].speed = 0.9f;
                    tapAnimators[pos].SetTrigger("bGood");
                }
                else
                    goodEffects[pos].SetActive(true);
                break;
            case JudgeType.LateGreat:
            case JudgeType.LateGreat1:
            case JudgeType.LateGreat2:
            case JudgeType.FastGreat2:
            case JudgeType.FastGreat1:
            case JudgeType.FastGreat:
                judgeTextRenderers[pos].sprite = judgeText[2];
                ResetEffect(position);
                if (isBreak)
                {
                    tapEffects[pos].SetActive(true);
                    tapAnimators[pos].speed = 0.9f;
                    tapAnimators[pos].SetTrigger("bGreat");
                }
                else
                {
                    greatEffects[pos].SetActive(true);
                    greatAnimators[pos].SetTrigger("great");
                }
                break;
            case JudgeType.LatePerfect2:
            case JudgeType.FastPerfect2:
            case JudgeType.LatePerfect1:
            case JudgeType.FastPerfect1:
                judgeTextRenderers[pos].sprite = judgeText[3];
                ResetEffect(position);
                tapEffects[pos].SetActive(true);
                if (isBreak)
                {
                    tapAnimators[pos].speed = 0.9f;
                    tapAnimators[pos].SetTrigger("break");
                }
                break;
            case JudgeType.Perfect:
                judgeTextRenderers[pos].sprite = judgeText[4];
                ResetEffect(position);
                tapEffects[pos].SetActive(true);
                if (isBreak)
                {
                    tapAnimators[pos].speed = 0.9f;
                    tapAnimators[pos].SetTrigger("break");
                }
                break;
            default:
                judgeTextRenderers[pos].sprite = judgeText[0];
                break;
        }

        ApplyJudgeTextAlpha(judgeTextRenderers[pos]);

        if (isBreak && judge == JudgeType.Perfect)
            judgeAnimators[pos].SetTrigger("break");
        else
            judgeAnimators[pos].SetTrigger("perfect");
    }
    /// <summary>
    /// Tap，Hold，Star
    /// </summary>
    /// <param name="position"></param>
    /// <param name="judge"></param>
    public void PlayFastLate(int position,JudgeType judge)
    {
        var pos = position - 1;
        if ((int)judge is (0 or 7))
        {
            fastLateEffects[pos].SetActive(false);
            return;
        }
        fastLateEffects[pos].SetActive(true);
        bool isFast = (int)judge > 7;
        if(isFast)
             fastLateTextRenderers[pos].sprite = customSkin.FastText;
        else
            fastLateTextRenderers[pos].sprite = customSkin.LateText;
        ApplyJudgeTextAlpha(fastLateTextRenderers[pos]);
        fastLateAnims[pos].SetTrigger("perfect");

    }
    /// <summary>
    /// Touch，TouchHold
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="anim"></param>
    /// <param name="judge"></param>
    public void PlayFastLate(GameObject obj,Animator anim, JudgeType judge)
    {
        if ((int)judge is (0 or 7))
        {
            obj.SetActive(false);
            Destroy(obj);
            return;
        }
        obj.SetActive(true);
        bool isFast = (int)judge > 7;
        if (isFast)
            obj.transform.GetChild(0).GetChild(0).gameObject.GetComponent<SpriteRenderer>().sprite = customSkin.FastText;
        else
            obj.transform.GetChild(0).GetChild(0).gameObject.GetComponent<SpriteRenderer>().sprite = customSkin.LateText;
        ApplyJudgeTextAlpha(obj.transform.GetChild(0).GetChild(0).GetComponent<SpriteRenderer>());
        anim.SetTrigger("touch");

    }
    public void ResetEffect(int position)
    {
        tapEffects[position - 1].SetActive(false);
        greatEffects[position - 1].SetActive(false);
        goodEffects[position - 1].SetActive(false);
    }

    public void ResetAllEffects()
    {
        for (var i = 0; i < 16; i++)
        {
            tapEffects[i].SetActive(false);
            greatEffects[i].SetActive(false);
            goodEffects[i].SetActive(false);
            fastLateEffects[i].SetActive(false);
        }
    }

    public void ApplyJudgeTextAlpha(float alpha)
    {
        JudgeTextAlpha = Mathf.Clamp01(alpha);
        foreach (var renderer in judgeTextRenderers)
            ApplyJudgeTextAlpha(renderer);
        foreach (var renderer in judgeBreakTextRenderers)
            ApplyJudgeTextAlpha(renderer);
        foreach (var renderer in fastLateTextRenderers)
            ApplyJudgeTextAlpha(renderer);
    }

    public static void ApplyJudgeTextAlpha(SpriteRenderer renderer)
    {
        if (renderer == null)
            return;

        var guard = renderer.GetComponent<JudgeTextRendererGuard>();
        if (guard == null)
            guard = renderer.gameObject.AddComponent<JudgeTextRendererGuard>();
        guard.Renderer = renderer;
        guard.Apply();
    }

}

public class JudgeTextRendererGuard : MonoBehaviour
{
    public SpriteRenderer Renderer { get; set; }

    public void Apply()
    {
        if (Renderer == null)
            Renderer = GetComponent<SpriteRenderer>();
        if (Renderer == null)
            return;

        var alpha = NoteEffectManager.JudgeTextAlpha;
        var color = Renderer.color;
        // Runs every LateUpdate; avoid dirtying renderers when the value is unchanged
        if (Mathf.Approximately(color.a, alpha) &&
            Renderer.forceRenderingOff == alpha <= 0.001f)
            return;
        Renderer.forceRenderingOff = alpha <= 0.001f;
        color.a = alpha;
        Renderer.color = color;
    }

    private void LateUpdate()
    {
        Apply();
    }
}
