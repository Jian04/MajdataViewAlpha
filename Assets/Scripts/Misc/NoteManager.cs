using Assets.Scripts.Types;
using System.Collections.Generic;
using UnityEngine;
#nullable enable
public class NoteManager : MonoBehaviour
{
    public List<GameObject> notes = new();
    public Dictionary<GameObject, int> noteOrder = new();
    public Dictionary<int, int> noteIndex = new();

    public Dictionary<GameObject, int> touchOrder = new();
    public Dictionary<SensorType, int> touchIndex = new();
    // Start is called before the first frame update
    void Start()
    {
        // Match AudioTimeProvider.PlaybackFrameRate: 60 in editor to reduce stutter, 120 in builds
        Application.targetFrameRate = Application.isEditor ? 60 : 120;
    }
    public void Clear()
    {
        ResetIndex();
        notes.Clear();
        noteOrder.Clear();
        touchOrder.Clear();
    }
    public void AddNote(GameObject obj,int index) => noteOrder.Add(obj, index);
    public void AddTouch(GameObject obj,int index) => touchOrder.Add(obj, index);
    public void Refresh()
    {
        var count = transform.childCount;
        notes.Clear();
        noteOrder.Clear();
        touchOrder.Clear();
        ResetIndex();
        for (int i = 0; i < count; i++)
        {
            var child = transform.GetChild(i);
            var tap = child.GetComponent<TapDrop>();
            var hold = child.GetComponent<HoldDrop>();
            var star = child.GetComponent<StarDrop>();
            var touch = child.GetComponent<TouchDrop>();
            var touchHold = child.GetComponent<TouchHoldDrop>();

            // D-zone notes use slots 9-16, separate from A-zone slots 1-8
            int keyOf(NoteDrop n) => n.isDZone ? n.startPosition + 8 : n.startPosition;

            if (tap != null)
                noteOrder[tap.gameObject] = noteIndex[keyOf(tap)]++;
            else if (hold != null)
                noteOrder[hold.gameObject] = noteIndex[keyOf(hold)]++;
            else if (star != null && !star.isNoHead)
                noteOrder[star.gameObject] = noteIndex[keyOf(star)]++;
            else if (touch != null)
                touchOrder[touch.gameObject] = touchIndex[touch.GetSensor()]++;
            else if(touchHold != null)
                touchOrder[touchHold.gameObject] = touchIndex[SensorType.C]++;

            notes.Add(child.gameObject);
        }
        ResetIndex();
    }
    void ResetIndex()
    {
        noteIndex.Clear();
        touchIndex.Clear();
        for (int i = 1; i < 17; i++) // 1-8=A zone, 9-16=D zone
            noteIndex.Add(i, 0);
        var sensorParent = GameObject.Find("Sensors");
        var count = sensorParent.transform.childCount;
        for (int i = 0; i < count; i++)
            touchIndex.Add(sensorParent.transform
                                       .GetChild(i)
                                       .GetComponent<Sensor>().Type, 0);
    }
    public int GetOrder(GameObject obj) => noteOrder[obj];
    public bool CanJudge(GameObject obj, int pos)
    {
        if (!noteOrder.ContainsKey(obj))
            return false;
        var index = noteOrder[obj];
        var nowIndex = noteIndex[pos];

        return index <= nowIndex;
    }

    public bool CanJudge(GameObject obj,SensorType t)
    {
        if (!touchOrder.ContainsKey(obj))
            return false;
        var index = touchOrder[obj];
        var nowIndex = touchIndex[t];

        return index <= nowIndex;
    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
