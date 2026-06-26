using Assets.Scripts.Types;
using UnityEngine;
#nullable enable
public class NoteEffectManager : MonoBehaviour
{
    public static float JudgeTextAlpha { get; private set; } = 1f;
    
    public Sprite hex;
    public Sprite star;
    private readonly Animator[] judgeAnimators = new Animator[8];
    private readonly GameObject[] judgeEffects = new GameObject[8];
    private readonly Animator[] tapAnimators = new Animator[8];
    private readonly Animator[] greatAnimators = new Animator[8];
    private readonly Animator[] goodAnimators = new Animator[8];
    private readonly Quaternion[] tapBaseRots = new Quaternion[8];
    private readonly Vector3[] tapBasePositions = new Vector3[8];
    private readonly Vector3[] greatBasePositions = new Vector3[8];
    private readonly Vector3[] goodBasePositions = new Vector3[8];
    private readonly Vector3[] judgeBasePositions = new Vector3[8];
    private readonly Vector3[] fastLateBasePositions = new Vector3[8];
    private readonly SpriteRenderer[][] tapEffectRenderers = new SpriteRenderer[8][];
    private readonly SpriteRenderer[] judgeTextRenderers = new SpriteRenderer[8];
    private readonly SpriteRenderer[] judgeBreakTextRenderers = new SpriteRenderer[8];
    private readonly SpriteRenderer[] fastLateTextRenderers = new SpriteRenderer[8];

    private readonly GameObject[] tapEffects = new GameObject[8];
    private readonly GameObject[] greatEffects = new GameObject[8];
    private readonly GameObject[] goodEffects = new GameObject[8];

    private readonly Animator[] fastLateAnims = new Animator[8];
    private readonly GameObject[] fastLateEffects = new GameObject[8];
    private CustomSkin customSkin;
    Sprite[] judgeText;

    // Start is called before the first frame update
    private void Start()
    {
        var tapEffectParent = transform.GetChild(0).gameObject;
        var greatEffectParent = transform.GetChild(2).gameObject;
        var goodEffectParent = transform.GetChild(3).gameObject;
        var judgeEffectParent = transform.GetChild(1).gameObject;
        var flParent = transform.GetChild(4).gameObject;

        for (var i = 0; i < 8; i++)
        {
            judgeEffects[i] = judgeEffectParent.transform.GetChild(i).gameObject;
            judgeBasePositions[i] = judgeEffects[i].transform.position;
            judgeAnimators[i] = judgeEffects[i].GetComponent<Animator>();
            judgeTextRenderers[i] = judgeEffects[i].transform.GetChild(0).GetChild(0).GetComponent<SpriteRenderer>();
            judgeBreakTextRenderers[i] = judgeEffects[i].transform.GetChild(0).GetChild(1).GetComponent<SpriteRenderer>();

            fastLateEffects[i] = flParent.transform.GetChild(i).gameObject;
            fastLateBasePositions[i] = fastLateEffects[i].transform.position;
            fastLateAnims[i] = fastLateEffects[i].GetComponent<Animator>();
            fastLateTextRenderers[i] = fastLateEffects[i].transform.GetChild(0).GetChild(0).GetComponent<SpriteRenderer>();

            goodEffects[i] = goodEffectParent.transform.GetChild(i).gameObject;
            goodBasePositions[i] = goodEffects[i].transform.position;
            greatAnimators[i] = goodEffects[i].GetComponent<Animator>();
            goodEffects[i].SetActive(false);

            greatEffects[i] = greatEffectParent.transform.GetChild(i).gameObject;
            greatBasePositions[i] = greatEffects[i].transform.position;
            greatAnimators[i] = greatEffects[i].GetComponent<Animator>();
            greatEffects[i].SetActive(false);

            tapEffects[i] = tapEffectParent.transform.GetChild(i).gameObject;
            tapBasePositions[i] = tapEffects[i].transform.position;
            tapAnimators[i] = tapEffects[i].GetComponent<Animator>();
            tapEffectRenderers[i] = tapEffects[i].GetComponentsInChildren<SpriteRenderer>(true);
            tapEffects[i].SetActive(false);
            tapBaseRots[i] = tapEffects[i].transform.rotation;
        }

        LoadSkin();
        ApplyJudgeTextAlpha(JudgeTextAlpha);
    }

    /// <summary>
    ///     加载判定文本的皮肤
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
    public void PlayEffect(int position, bool isBreak, JudgeType judge = JudgeType.Perfect, Color noteColor = default, Quaternion? overrideRotation = null, Vector3? overridePosition = null)
    {
        var pos = position - 1;
        ApplyEffectTransform(pos, overridePosition, overrideRotation);

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
    public void PlayFastLate(int position,JudgeType judge, Vector3? overridePosition = null, Quaternion? overrideRotation = null)
    {
        var pos = position - 1;
        fastLateEffects[pos].transform.position = overridePosition ?? fastLateBasePositions[pos];
        fastLateEffects[pos].transform.rotation = overrideRotation ?? tapBaseRots[pos];
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
        for (var i = 0; i < 8; i++)
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

    private void ApplyEffectPosition(int pos, Vector3? overridePosition)
    {
        ApplyEffectTransform(pos, overridePosition, null);
    }

    private void ApplyEffectTransform(int pos, Vector3? overridePosition, Quaternion? overrideRotation)
    {
        var tapTransform = tapEffects[pos].transform;
        var greatTransform = greatEffects[pos].transform;
        var goodTransform = goodEffects[pos].transform;
        var judgeTransform = judgeEffects[pos].transform;

        tapTransform.position = overridePosition ?? tapBasePositions[pos];
        greatTransform.position = overridePosition ?? greatBasePositions[pos];
        goodTransform.position = overridePosition ?? goodBasePositions[pos];
        judgeTransform.position = overridePosition ?? judgeBasePositions[pos];

        var rotation = overrideRotation ?? tapBaseRots[pos];
        tapTransform.rotation = rotation;
        greatTransform.rotation = rotation;
        goodTransform.rotation = rotation;
        judgeTransform.rotation = rotation;
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
        Renderer.forceRenderingOff = alpha <= 0.001f;
        var color = Renderer.color;
        color.a = alpha;
        Renderer.color = color;
    }

    private void LateUpdate()
    {
        Apply();
    }
}
