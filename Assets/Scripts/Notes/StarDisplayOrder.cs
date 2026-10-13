using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

// Only changes star renderers. Track bars, input order and timing stay unchanged.
public sealed class StarDisplayOrder : MonoBehaviour
{
    private static readonly HashSet<StarDisplayOrder> Instances = new();
    private static int mode = 1; // 0: later on top; 1: earlier on top.
    private static bool dirty;
    private static readonly ProfilerMarker SortMarker = new("Alpha.StarDisplayOrder");
    private SpriteRenderer[] renderers;
    private int[] baseOrders;
    private int sourceOrder;
    private bool head;

    public static void SetMode(int value)
    {
        value = value == 0 ? 0 : 1;
        if (mode == value) return;
        mode = value;
        ApplyAll();
    }

    public static void Register(GameObject star, int order, bool isHead = false)
    {
        var component = star.GetComponent<StarDisplayOrder>() ?? star.AddComponent<StarDisplayOrder>();
        component.sourceOrder = order;
        component.head = isHead;
        component.renderers = star.GetComponentsInChildren<SpriteRenderer>(true);
        component.baseOrders = new int[component.renderers.Length];
        for (var i = 0; i < component.renderers.Length; i++)
            component.baseOrders[i] = component.renderers[i].sortingOrder - (isHead ? order : 0);
        Instances.Add(component);
        component.Apply(order);
        if (mode == 0) dirty = true;
    }

    public static void Flush()
    {
        if (dirty) ApplyAll();
    }

    private static void ApplyAll()
    {
        using var sample = SortMarker.Auto();
        // Reverse within the original range, preserving the surrounding note layers.
        var headMin = int.MaxValue; var headMax = int.MinValue;
        var guideMin = int.MaxValue; var guideMax = int.MinValue;
        if (mode == 0)
            foreach (var item in Instances)
            {
                if (item == null) continue;
                if (item.head) { headMin = Mathf.Min(headMin, item.sourceOrder); headMax = Mathf.Max(headMax, item.sourceOrder); }
                else { guideMin = Mathf.Min(guideMin, item.sourceOrder); guideMax = Mathf.Max(guideMax, item.sourceOrder); }
            }
        foreach (var item in Instances)
        {
            if (item == null) continue;
            var order = mode == 1 ? item.sourceOrder :
                (item.head ? headMin + headMax : guideMin + guideMax) - item.sourceOrder;
            item.Apply(order);
        }
        dirty = false;
    }

    private void Apply(int order)
    {
        for (var i = 0; i < renderers.Length; i++)
            if (renderers[i] != null && renderers[i].sortingOrder != baseOrders[i] + order)
                renderers[i].sortingOrder = baseOrders[i] + order;
    }

    private void OnDestroy()
    {
        // In the new mode each star keeps its authored order. Removing a star
        // cannot change any survivor's order, so it needs no chart-wide pass.
        if (Instances.Remove(this) && mode == 0) dirty = true;
    }
}
