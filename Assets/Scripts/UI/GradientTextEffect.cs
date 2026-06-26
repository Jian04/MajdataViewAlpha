using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Text))]
public sealed class GradientTextEffect : BaseMeshEffect
{
    public Color topColor = new Color32(255, 255, 255, 255);
    public Color bottomColor = new Color32(206, 166, 255, 255);

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0)
            return;

        var vertices = new List<UIVertex>(vh.currentVertCount);
        vh.GetUIVertexStream(vertices);
        if (vertices.Count == 0)
            return;

        var minY = float.MaxValue;
        var maxY = float.MinValue;
        for (var i = 0; i < vertices.Count; i++)
        {
            var y = vertices[i].position.y;
            if (y < minY)
                minY = y;
            if (y > maxY)
                maxY = y;
        }

        var range = Mathf.Max(0.001f, maxY - minY);
        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            var t = Mathf.Clamp01((vertex.position.y - minY) / range);
            vertex.color = (Color)vertex.color * Color.Lerp(bottomColor, topColor, t);
            vertices[i] = vertex;
        }

        vh.Clear();
        vh.AddUIVertexTriangleStream(vertices);
    }
}
