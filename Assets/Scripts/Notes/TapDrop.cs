using Assets.Scripts.Notes;
using Assets.Scripts.Types;
using UnityEngine;
#nullable enable
public class TapDrop : TapBase
{
    private void Start()
    {
        PreLoad();

        spriteRenderer.sprite = tapSpr;
        exSpriteRender.sprite = exSpr;

        if (isEX) exSpriteRender.color = exEffectTap;
        if (isEach)
        {
            spriteRenderer.sprite = eachSpr;
            if (!isMine)
                lineSpriteRender.sprite = eachLine;
            if (isEX) exSpriteRender.color = exEffectEach;
        }

        if (isBreak)
        {
            spriteRenderer.sprite = breakSpr;
            if (!isMine)
                lineSpriteRender.sprite = breakLine;
            if (isEX) exSpriteRender.color = exEffectBreak;
            spriteRenderer.sharedMaterial = breakMaterial;
        }

        // Share the optional tint, then set the guide's own opacity.
        if (colorOverrideMaterial != null)
        {
            spriteRenderer.sharedMaterial = colorOverrideMaterial;
            lineSpriteRender.sharedMaterial = colorOverrideMaterial;
        }
        InitializeTapLineAlpha(lineSpriteRender);
        ApplyExAlpha();

        // 4.3.1 orients ordinary Tap artwork to the key. Inherited pictures
        // intentionally remain upright so chart-authored images are readable.
        transform.rotation = string.IsNullOrEmpty(customSkin)
            ? Quaternion.Euler(0f, 0f,
                -22.5f - 45f * (startPosition - 1) + (isDZone ? 22.5f : 0f))
            : Quaternion.identity;

        spriteRenderer.forceRenderingOff = true;
        exSpriteRender.forceRenderingOff = true;
        var sensorRoot = manager != null ? manager.gameObject : GameObject.Find("Sensors");
        if (sensorRoot == null || SensorChildIndex < 0 || SensorChildIndex >= sensorRoot.transform.childCount)
        {
            Debug.LogError(
                $"TapDrop rejected invalid sensor: start={startPosition}, dZone={isDZone}, " +
                $"index={SensorChildIndex}, sensors={sensorRoot?.transform.childCount ?? 0}.");
            Destroy(gameObject);
            return;
        }

        sensor = sensorRoot.transform.GetChild(SensorChildIndex).GetComponent<Sensor>();
        manager = sensorRoot.GetComponent<SensorManager>();
        if (inputManager == null) inputManager = GameObject.Find("Input")
                                 .GetComponent<InputManager>();
        sensorPos = (SensorType)SensorChildIndex;
        if (!JudgmentDisabled)
            BindJudgeInput(Check);
        State = NoteStatus.Initialized;
    }
}
