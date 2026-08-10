using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Supports layered recording exports by hiding all rendering under selected roots
/// while keeping GameObjects active so judgement, clocks, and animations continue.
/// Notes are instantiated during recording, so LateUpdate scans for new renderers each frame.
/// Offline virtual-time capture makes this overhead negligible. ScreenRecorder restores and destroys this component.
/// </summary>
public class RecordLayerMasker : MonoBehaviour
{
    private readonly List<Transform> roots = new();
    private readonly HashSet<Renderer> hiddenRenderers = new();
    private readonly HashSet<Canvas> hiddenCanvases = new();
    private readonly Dictionary<Behaviour, bool> hiddenBehaviours = new();

    public void SetHiddenRoots(IEnumerable<GameObject> hiddenRoots)
    {
        roots.Clear();
        foreach (var root in hiddenRoots)
            if (root != null)
                roots.Add(root.transform);
        Apply();
    }

    public void SetHiddenBehaviour(Behaviour behaviour)
    {
        if (behaviour == null || hiddenBehaviours.ContainsKey(behaviour))
            return;
        hiddenBehaviours.Add(behaviour, behaviour.enabled);
        behaviour.enabled = false;
    }

    private void LateUpdate()
    {
        Apply();
    }

    private void Apply()
    {
        foreach (var root in roots)
        {
            if (root == null)
                continue;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer != null)
                {
                    hiddenRenderers.Add(renderer);
                    renderer.forceRenderingOff = true;
                }
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                if (canvas != null)
                {
                    hiddenCanvases.Add(canvas);
                    canvas.enabled = false;
                }
        }
    }

    public void Restore()
    {
        foreach (var renderer in hiddenRenderers)
            if (renderer != null)
                renderer.forceRenderingOff = false;
        foreach (var canvas in hiddenCanvases)
            if (canvas != null)
                canvas.enabled = true;
        foreach (var pair in hiddenBehaviours)
            if (pair.Key != null)
                pair.Key.enabled = pair.Value;
        hiddenRenderers.Clear();
        hiddenCanvases.Clear();
        hiddenBehaviours.Clear();
        roots.Clear();
    }

    private void OnDestroy()
    {
        Restore();
    }
}
