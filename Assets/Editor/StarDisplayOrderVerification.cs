using System;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StarDisplayOrderVerification
{
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Star order verification");
        try
        {
            StarDisplayOrder.SetMode(1);
            var first = Add(root, -5, true);
            var middle = Add(root, -10, true);
            var last = Add(root, -20, true);
            var guideFirst = Add(root, -30, false);
            var guideLast = Add(root, -40, false);
            StarDisplayOrder.Flush();
            Check(first, -5); Check(last, -20); Check(guideFirst, -30); Check(guideLast, -40);
            StarDisplayOrder.SetMode(0);
            Check(first, -20); Check(middle, -15); Check(last, -5);
            Check(guideFirst, -40); Check(guideLast, -30);
            UnityEngine.Object.DestroyImmediate(middle);
            StarDisplayOrder.Flush();
            Check(first, -20); Check(last, -5);
            StarDisplayOrder.SetMode(1);
            Check(first, -5); Check(last, -20);
            UnityEngine.Object.DestroyImmediate(first);
            StarDisplayOrder.Flush();
            Check(last, -20); Check(guideFirst, -30);
            Debug.Log("STAR ORDER PASS: both display modes, head/guide separation, EX layer offset and deletion preserve ordering.");
        }
        finally { StarDisplayOrder.SetMode(1); UnityEngine.Object.DestroyImmediate(root); }
    }
    private static GameObject Add(GameObject root, int order, bool head)
    {
        var star = new GameObject("Star"); star.transform.SetParent(root.transform);
        star.AddComponent<SpriteRenderer>().sortingOrder = head ? order : 0;
        var ex = new GameObject("EX"); ex.transform.SetParent(star.transform);
        ex.AddComponent<SpriteRenderer>().sortingOrder = (head ? order : 0) + 1;
        StarDisplayOrder.Register(star, order, head);
        return star;
    }
    private static void Check(GameObject star, int order)
    {
        var sprites = star.GetComponentsInChildren<SpriteRenderer>();
        if (sprites[0].sortingOrder != order || sprites[1].sortingOrder != order + 1)
            throw new Exception("Star order or EX layer changed unexpectedly");
    }
}
