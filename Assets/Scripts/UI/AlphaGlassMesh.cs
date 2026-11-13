using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class AlphaGlassMesh
{
    public static readonly int MapId = Shader.PropertyToID("_AlphaGlassMap");
    static readonly int StrengthId = Shader.PropertyToID("_GlassStrength");
    static readonly int PhaseId = Shader.PropertyToID("_GlassPhase");
    static readonly int AspectId = Shader.PropertyToID("_GlassAspect");
    static Material material;
    static Mesh mesh;

    public const GraphicsFormat MapFormat = GraphicsFormat.R16G16B16A16_SFloat;
    public static Material Material => material ? material : material = new Material(Resources.Load<Shader>("AlphaGlassMesh"))
        { hideFlags = HideFlags.HideAndDontSave };
    public static Mesh Geometry => mesh ? mesh : mesh = Build();

    public static void SetParameters(MaterialPropertyBlock properties, float strength, float phase, float aspect)
    {
        properties.SetFloat(StrengthId, Mathf.Clamp01(strength));
        properties.SetFloat(PhaseId, Mathf.Max(0f, phase));
        properties.SetFloat(AspectId, aspect);
    }

    // Built-in renderer and editor samples share the exact same geometry pass.
    public static void Blit(Texture source, RenderTexture destination, Material filters, int pass = 0)
    {
        if (!filters.IsKeywordEnabled("ALPHA_SHATTER") || filters.GetFloat("_Shatter") <= .0001f)
        {
            Graphics.Blit(source, destination, filters, pass);
            return;
        }

        var descriptor = new RenderTextureDescriptor(source.width, source.height, MapFormat, 24);
        var map = RenderTexture.GetTemporary(descriptor);
        var previous = RenderTexture.active;
        try
        {
            map.filterMode = FilterMode.Point;
            Graphics.SetRenderTarget(map);
            GL.Clear(true, true, Color.clear);
            var draw = Material;
            draw.SetFloat(StrengthId, Mathf.Clamp01(filters.GetFloat("_Shatter")));
            draw.SetFloat(PhaseId, Mathf.Max(0f, filters.GetFloat("_ShatterPhase")));
            draw.SetFloat(AspectId, (float)map.width / map.height);
            draw.SetPass(0);
            Graphics.DrawMeshNow(Geometry, Matrix4x4.identity);
            RenderTexture.active = previous;
            filters.SetTexture(MapId, map);
            Graphics.Blit(source, destination, filters, pass);
        }
        finally
        {
            filters.SetTexture(MapId, null);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(map);
        }
    }

    static Mesh Build()
    {
        var random = new System.Random(6173);
        float Next() => (float)random.NextDouble();
        var sites = new List<Vector2>();
        // Stress is dense near the impact and sparse outside. No grid or rings.
        for(var attempt=0; attempt<2000 && sites.Count<66; attempt++)
        {
            var angle=Next()*Mathf.PI*2;
            var radius=Mathf.Pow(Next(),.8f)*.78f;
            var point=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
            if(Mathf.Abs(point.x)>.5f || Mathf.Abs(point.y)>.5f) continue;
            var spacing=.022f+radius*.14f;
            if(sites.Exists(other=>(other-point).sqrMagnitude<spacing*spacing)) continue;
            sites.Add(point);
        }
        var polygons = new List<List<Vector2>>();
        foreach(var site in sites)
        {
            var polygon=new List<Vector2>{new(-.5f,-.5f),new(.5f,-.5f),new(.5f,.5f),new(-.5f,.5f)};
            foreach(var other in sites)
            {
                if(other==site) continue;
                polygon=Cut(polygon,other-site,(other+site)*.5f);
            }
            if(polygon.Count>=3) polygons.Add(polygon);
        }
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uv = new List<Vector2>();
        var shard = new List<Vector4>();
        var edges = new List<Vector2>();
        var bevelNormals = new List<Vector2>();
        var bevelNormal = Vector2.zero;
        var triangles = new List<int>();
        foreach (var original in polygons)
        {
            var polygon = new List<Vector2>();
            for (var i=0; i<original.Count; i++)
            {
                var a=original[i]; var b=original[(i+1)%original.Count];
                polygon.Add(a);
                var reverse=a.x>b.x || a.x==b.x && a.y>b.y;
                var first=reverse?b:a; var second=reverse?a:b;
                var direction=second-first; var length=direction.magnitude;
                var divisions=Mathf.Clamp(Mathf.CeilToInt(length/.04f),1,6);
                if (Mathf.Abs(a.x)==.5f && a.x==b.x || Mathf.Abs(a.y)==.5f && a.y==b.y) divisions=1;
                var normal=new Vector2(-direction.y,direction.x)/length;
                var key= new Vector2(Mathf.Round((first.x+second.x)*10000),Mathf.Round((first.y+second.y)*10000));
                var seed=key.x*.173f+key.y*.317f;
                for(var part=1;part<divisions;part++)
                {
                    var t=(float)part/divisions;
                    var sharedT=reverse?1-t:t;
                    var chip=Mathf.Sin(seed+sharedT*37)*Mathf.Sin(sharedT*Mathf.PI)*Mathf.Min(.003f,length*.035f);
                    polygon.Add(Vector2.Lerp(a,b,t)+normal*chip);
                }
            }
            var center = Vector2.zero;
            foreach (var point in polygon) center += point;
            center /= polygon.Count;
            var area = 0f;
            for (var i = 0; i < polygon.Count; i++)
                area += Cross(polygon[i] - center, polygon[(i+1) % polygon.Count] - center) * .5f;
            var radius = Mathf.Sqrt(area);
            var thickness = .012f + .040f * Mathf.Clamp01(radius / .14f);
            var metadata = new Vector4(center.x, center.y, Next(), radius);
            int Add(Vector2 point, float depth, Vector3 normal, float edge, float side)
            {
                var index = positions.Count;
                positions.Add(new Vector3(point.x-center.x, point.y-center.y, depth));
                uv.Add(point + new Vector2(.5f,.5f));
                normals.Add(normal);
                shard.Add(metadata);
                edges.Add(new Vector2(edge, side));
                bevelNormals.Add(bevelNormal);
                return index;
            }
            void Triangle(int a, int b, int c)
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }

            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i+1) % polygon.Count];
                var edge = b - a;
                var length = edge.magnitude;
                if (length < .000001f) continue;
                var toEdge = Cross(edge, center - a) / length;
                bevelNormal = new Vector2(edge.y,-edge.x)/length;
                Triangle(Add(center,0,Vector3.forward,toEdge,0),
                    Add(a,0,Vector3.forward,0,0), Add(b,0,Vector3.forward,0,0));
                Triangle(Add(center,-thickness,Vector3.back,toEdge,0),
                    Add(b,-thickness,Vector3.back,0,0), Add(a,-thickness,Vector3.back,0,0));

                var side = new Vector3(edge.y / length,-edge.x / length,0);
                var af = Add(a,0,side,0,1); var bf = Add(b,0,side,0,1);
                var ab = Add(a,-thickness,side,thickness,1); var bb = Add(b,-thickness,side,thickness,1);
                Triangle(af,ab,bf); Triangle(bf,ab,bb);
            }
        }

        var result = new Mesh { name = "Glass fracture polygons", hideFlags = HideFlags.HideAndDontSave };
        result.SetVertices(positions);
        result.SetNormals(normals);
        result.SetUVs(0,uv);
        result.SetUVs(1,shard);
        result.SetUVs(2,edges);
        result.SetUVs(3,bevelNormals);
        result.SetTriangles(triangles,0);
        result.UploadMeshData(true);
        return result;
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    static List<Vector2> Cut(List<Vector2> polygon, Vector2 normal, Vector2 midpoint)
    {
        var result = new List<Vector2>(polygon.Count + 1);
        if (polygon.Count == 0) return result;
        var previous = polygon[^1];
        var previousDistance = Vector2.Dot(previous-midpoint, normal);
        foreach (var next in polygon)
        {
            var distance = Vector2.Dot(next-midpoint, normal);
            if ((previousDistance <= 0f) != (distance <= 0f))
                result.Add(Vector2.LerpUnclamped(previous,next,previousDistance/(previousDistance-distance)));
            if (distance <= 0f) result.Add(next);
            previous = next;
            previousDistance = distance;
        }
        return result;
    }
}
