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

        // The note and its guide share the same optional tint material.
        if (colorOverrideMaterial != null)
        {
            spriteRenderer.sharedMaterial = colorOverrideMaterial;
            lineSpriteRender.sharedMaterial = colorOverrideMaterial;
        }
        ApplyExAlpha();

        spriteRenderer.forceRenderingOff = true;
        exSpriteRender.forceRenderingOff = true;
        var sensorRoot = GameObject.Find("Sensors");
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
        inputManager = GameObject.Find("Input")
                                 .GetComponent<InputManager>();
        sensorPos = (SensorType)SensorChildIndex;
        if (!JudgmentDisabled)
            BindJudgeInput(Check);
        State = NoteStatus.Initialized;
    }
}
