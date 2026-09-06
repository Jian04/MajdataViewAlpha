using System.Collections.Generic;
using UnityEngine;
#nullable enable
public class MultTouchHandler : MonoBehaviour
{
    private readonly List<TouchDrop>[] touchSlots = new List<TouchDrop>[33];

    private void Start()
    {
        for (var i = 0; i < 33; i++) touchSlots[i] = new List<TouchDrop>();
    }

    private int getAreaIndex(char area, int pos)
    {
        if (area == 'C') return 0;
        switch (area)
        {
            case 'A':
                return pos;
            case 'B':
                return 8 + pos;
            case 'D':
                return 16 + pos;
            case 'E':
                return 24 + pos;
        }
        return 0;
    }

    public void clearSlots()
    {
        for (var i = 0; i < 33; i++) touchSlots[i].Clear();
    }

    public void registerTouch(TouchDrop obj)
    {
        var areaIndex = getAreaIndex(obj.areaPosition, obj.startPosition);
        obj.setLayer(touchSlots[areaIndex].Count);
        touchSlots[areaIndex].Add(obj);
    }

    public void cancelTouch(TouchDrop obj)
    {
        var areaIndex = getAreaIndex(obj.areaPosition, obj.startPosition);
        var touchSlot = touchSlots[areaIndex];
        if (!touchSlot.Remove(obj))
            return;
        foreach (var each in touchSlot) each.layerDown();
    }
}
