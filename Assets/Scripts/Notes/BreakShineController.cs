using Assets.Scripts.Interfaces;
using System;
using UnityEngine;
#nullable enable
public class BreakShineController : MonoBehaviour
{
    private static AudioTimeProvider sharedTimeProvider;
    public IFlasher? parent = null;

    SpriteRenderer spriteRenderer;
    AudioTimeProvider timeProvider;
    MaterialPropertyBlock propertyBlock;
    // Start is called before the first frame update
    void Start()
    {
        
    }
    // Update is called once per frame
    void Update()
    {
        if(parent is not null && parent.CanShine())
        {
            var extra = Math.Max(Mathf.Sin(timeProvider.GetFrame() * 0.17f) * 0.5f, 0);
            spriteRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat("_Brightness", 0.95f + extra);
            spriteRenderer.SetPropertyBlock(propertyBlock);
        }
    }
    private void OnEnable()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        propertyBlock = new MaterialPropertyBlock();
        if (sharedTimeProvider == null)
            sharedTimeProvider = FindAnyObjectByType<AudioTimeProvider>();
        timeProvider = sharedTimeProvider;
    }
}
