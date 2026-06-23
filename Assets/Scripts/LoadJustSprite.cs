using UnityEngine;

public class LoadJustSprite : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    public int _0curv1str2wifi;

    public int indexOffset;
    public int judgeOffset = 0;

    // Start is called before the first frame update
    private void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        //gameObject.GetComponent<SpriteRenderer>().sprite = GameObject.Find("Outline").GetComponent<CustomSkin>().Just[_0curv1str2wifi + 3];
        //setR();
    }

    // Update is called once per frame
    private void Update()
    {
    }

    private void LateUpdate()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            var color = spriteRenderer.color;
            color.a = NoteEffectManager.JudgeTextAlpha;
            spriteRenderer.color = color;
            spriteRenderer.forceRenderingOff = NoteEffectManager.JudgeTextAlpha <= 0.001f;
        }
    }

    public int setR()
    {
        indexOffset = 0;
        refreshSprite();
        return _0curv1str2wifi;
    }

    public int setL()
    {
        indexOffset = 3;
        refreshSprite();
        return _0curv1str2wifi;
    }
    public void setFastGr()
    {
        judgeOffset = 6;
        refreshSprite();
    }
    public void setFastGd()
    {
        judgeOffset = 12;
        refreshSprite();
    }
    public void setLateGr()
    {
        judgeOffset = 18;
        refreshSprite();
    }
    public void setLateGd()
    {
        judgeOffset = 24;
        refreshSprite();
    }
    public void setMiss()
    {
        judgeOffset = 30;
        refreshSprite();
    }
    private void refreshSprite()
    {
        gameObject.GetComponent<SpriteRenderer>().sprite = GameObject.Find("Outline").GetComponent<CustomSkin>()
            .Just[_0curv1str2wifi + indexOffset + judgeOffset];
    }
}
