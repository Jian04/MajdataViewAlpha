using System;
using System.Collections.Generic;
using System.Numerics;

namespace MajdataCore
{
    // The same nearest-sensor partition used by dynamic touch-slide routes.
    // Convex cells share exact edges; union outlines discard all selected neighbours.
    public static class NoiseZoneGeometry
    {
        public sealed class Contour
        {
            public int Sensor;
            public readonly List<Vector2> Points = new();
        }
        // Stitch the union boundary into continuous polygons, including concave
        // corners. Shared internal edges never become warning-frame segments.
        public static List<Contour> UnionContours(List<Vector2>[] cells, IReadOnlyList<Vector2> centers,
            IReadOnlyList<bool> selected, float radius)
        {
            var edges=new List<(int sensor,Vector2 a,Vector2 b)>();
            for(int i=0;i<cells.Length;i++) if(selected[i])
                for(int n=0;n<cells[i].Count;n++) {
                    var a=cells[i][n]; var b=cells[i][(n+1)%cells[i].Count];
                    if(IsUnionBoundary(i,a,b,centers,selected,radius)) edges.Add((i,a,b));
                }
            var result=new List<Contour>();
            while(edges.Count>0) {
                var first=edges[0]; edges.RemoveAt(0);
                var contour=new Contour { Sensor=first.sensor }; contour.Points.Add(first.a);
                var next=first.b;
                while(Vector2.DistanceSquared(next,first.a)>.000001f) {
                    contour.Points.Add(next);
                    int found=edges.FindIndex(e=>Vector2.DistanceSquared(e.a,next)<.000001f);
                    if(found<0) break;
                    next=edges[found].b; edges.RemoveAt(found);
                }
                if(contour.Points.Count>=3) result.Add(contour);
            }
            return result;
        }
        public static void MergeWarningLevels(List<Vector2>[] cells,IReadOnlyList<Vector2> centers,
            IReadOnlyList<bool> selected,float radius,float[] opacity,float[] shine)
        {
            var visited=new bool[cells.Length];
            for(int seed=0;seed<cells.Length;seed++) {
                if(!selected[seed] || visited[seed]) continue;
                var group=new List<int>{seed}; visited[seed]=true;
                float a=opacity[seed],s=shine[seed];
                for(int n=0;n<group.Count;n++) {
                    int i=group[n];
                    for(int v=0;v<cells[i].Count;v++) {
                        int neighbor=EdgeNeighbor(i,cells[i][v],cells[i][(v+1)%cells[i].Count],centers,radius);
                        if(neighbor<0 || !selected[neighbor] || visited[neighbor]) continue;
                        visited[neighbor]=true; group.Add(neighbor);
                        a=Math.Max(a,opacity[neighbor]); s=Math.Max(s,shine[neighbor]);
                    }
                }
                foreach(int i in group) { opacity[i]=a; shine[i]=s; }
            }
        }
        public static List<Vector2>[] BuildCells(IReadOnlyList<Vector2> centers, float radius)
        {
            var cells = new List<Vector2>[centers.Count];
            for (int i = 0; i < centers.Count; i++)
            {
                var cell = new List<Vector2>();
                for (int v = 0; v < 128; v++)
                {
                    double angle = Math.PI * 2 * v / 128;
                    cell.Add(new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius));
                }
                for (int j = 0; j < centers.Count && cell.Count != 0; j++)
                {
                    if (i == j) continue;
                    var normal = centers[j] - centers[i];
                    if (normal.LengthSquared() < 0.000001f) continue;
                    var distance = (centers[j].LengthSquared() - centers[i].LengthSquared()) * .5f;
                    cell = Clip(cell, normal, distance);
                }
                cells[i] = cell;
            }
            return cells;
        }
        private static List<Vector2> Clip(List<Vector2> source, Vector2 normal, float distance)
        {
            var result = new List<Vector2>();
            var before = source[source.Count - 1];
            var dBefore = Vector2.Dot(before, normal) - distance;
            foreach (var current in source)
            {
                var dCurrent = Vector2.Dot(current, normal) - distance;
                if ((dBefore <= 0) != (dCurrent <= 0))
                    result.Add(Vector2.Lerp(before, current, dBefore / (dBefore - dCurrent)));
                if (dCurrent <= 0) result.Add(current);
                before = current; dBefore = dCurrent;
            }
            for (int i = result.Count - 1; i >= 0 && result.Count > 1; i--)
                if (Vector2.DistanceSquared(result[i], result[(i + 1) % result.Count]) < .00000001f)
                    result.RemoveAt(i);
            return result;
        }
        public static int EdgeNeighbor(int sensor,Vector2 a,Vector2 b,IReadOnlyList<Vector2> centers,float radius)
        {
            var edge=b-a;
            if(edge.LengthSquared()<.0000001f) return -1;
            var outside=(a+b)*.5f+Vector2.Normalize(new Vector2(edge.Y,-edge.X))*.003f;
            if(outside.LengthSquared()>=radius*radius) return -1;
            int nearest=-1; float best=float.MaxValue;
            for(int i=0;i<centers.Count;i++) {
                float d=Vector2.DistanceSquared(outside,centers[i]);
                if(d<best) { best=d; nearest=i; }
            }
            return nearest==sensor ? -1 : nearest;
        }
        public static List<Vector2> ShrinkToCenter(List<Vector2> cell,Vector2 center,float progress)
        {
            if(progress>=1) return cell;
            if(progress<=0) return new List<Vector2>();
            progress=progress*progress*(3-2*progress);
            var result=new List<Vector2>(cell.Count);
            foreach(var vertex in cell) result.Add(center+(vertex-center)*progress);
            return result;
        }

        public static List<Vector2> GrowFromEdge(List<Vector2> cell,Vector2 a,Vector2 b,float progress)
        {
            if(progress>=1) return cell;
            if(progress<=0 || cell.Count<3) return new List<Vector2>();
            var edge=b-a;
            var inward=Vector2.Normalize(new Vector2(-edge.Y,edge.X));
            float maximum=0;
            foreach(var vertex in cell) maximum=Math.Max(maximum,Vector2.Dot(vertex-a,inward));
            float eased=progress*progress*(3-2*progress);
            return Clip(cell,inward,Vector2.Dot(a,inward)+maximum*eased);
        }
        public static bool IsUnionBoundary(int sensor, Vector2 a, Vector2 b,
            IReadOnlyList<Vector2> centers, IReadOnlyList<bool> selected, float radius)
        {
            var middle = (a + b) * .5f;
            var edge = b - a;
            if (edge.LengthSquared() < 0.0000001f) return false;
            // CCW polygons have their outward normal on the right.
            var outside = middle + Vector2.Normalize(new Vector2(edge.Y, -edge.X)) * .003f;
            if (outside.LengthSquared() >= radius * radius) return true;
            int nearest = sensor;
            float best = float.MaxValue;
            for (int i = 0; i < centers.Count; i++)
            {
                var d = Vector2.DistanceSquared(outside, centers[i]);
                if (d < best) { best = d; nearest = i; }
            }
            return nearest == sensor || !selected[nearest];
        }
    }
}
