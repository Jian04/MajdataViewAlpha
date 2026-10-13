using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DSlideJudgeVerification
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void BuildCheckedView()
    {
        Run();
        AlphaCommandLineBuild.BuildView();
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var sensors = UnityEngine.Object.FindObjectsByType<Sensor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .OrderBy(s => (int)s.Type).Select(s => s.GetComponent<RectTransform>()).ToArray();
        if (sensors.Length != 33) throw new Exception("Expected 33 live View sensor regions, got " + sensors.Length);
        Debug.Log("VIEW SENSOR CENTERS: " + string.Join(";", sensors.Where(s => s.name is "A1" or "B1" or "C" or "D1" or "E1").Select(s => s.name + "=" + s.position.ToString("F4"))));
        System.IO.File.WriteAllLines(".codex-build/d-slide-sensors.csv", sensors.Select(s => $"{(int)s.GetComponent<Sensor>().Type},{s.position.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},{s.position.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}"));
        foreach (var pair in new[] { (true, true, (bool?)null), (true, false, (bool?)null), (false, true, (bool?)null), (true, true, (bool?)false), (false, false, (bool?)true) })
        {
            var prefab = pair.Item3.HasValue ? "Star_L_5" : "Star_Line_5";
            var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SlidePrefab/" + prefab + ".prefab"));
            var note = root.GetComponent<SlideDrop>() ?? root.AddComponent<SlideDrop>();
            try
            {
                note.startPosition = 1; note.endPosition = 5; note.slideType = pair.Item3.HasValue ? "L5" : "line5";
                note.isDZone = pair.Item1; note.isDZoneEnd = pair.Item2;
                if (pair.Item3.HasValue) note.pathSegment = new MajdataCore.SlidePathSegmentData { startPosition = 1, endPosition = 5, middlePosition = 3, hasMiddle = true, middleIsDZone = pair.Item3.Value };
                var bars = (List<GameObject>)typeof(SlideDrop).GetField("slideBars", Flags).GetValue(note);
                for (var i = 0; i < root.transform.childCount - 1; i++) bars.Add(root.transform.GetChild(i).gameObject);
                typeof(SlideDrop).GetField("slideOK", Flags).SetValue(note, root.transform.GetChild(root.transform.childCount - 1).gameObject);
                typeof(SlideDrop).GetMethod("BuildSlidePath", Flags).Invoke(note, null);
                typeof(SlideDrop).GetMethod("GetSensors", Flags).Invoke(note, new object[] { sensors });
                var regions = (List<Sensor>)typeof(SlideDrop).GetField("judgeSensors", Flags).GetValue(note);
                var route = string.Join(",", regions.Select(s => s.Type));
                var shape = pair.Item3.HasValue ? "V3" + (pair.Item3.Value ? "d" : "") : "-";
                Debug.Log($"VIEW D SLIDE ROUTE: 1{(pair.Item1 ? "d" : "")}{shape}5{(pair.Item2 ? "d" : "")} = {route}");
                if (!pair.Item3.HasValue && pair.Item1 && pair.Item2 && route != "D1,E1,C,E5,D5") throw new Exception("Unexpected View D-D partition: " + route);
                if (pair.Item3.HasValue && !regions.Any(s => s.Type.ToString() == (pair.Item3.Value ? "D3" : "A3"))) throw new Exception("Authored mixed middle sensor excluded: " + route);
                if (regions.Count < 3) throw new Exception("D slide did not generate sensor partitions");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        Debug.Log("VIEW D SLIDE CHECK PASS: real prefabs and scene sensors, D-D / D-A / A-D and mixed middle dynamic regions.");
    }
}
