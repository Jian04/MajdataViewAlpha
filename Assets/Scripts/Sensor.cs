using Assets.Scripts.Types;
using System;
using System.Collections.Generic;
using UnityEngine;

public class Sensor : MonoBehaviour
{
    public bool IsJudging { get; set; } = false;
    public SensorStatus Status = SensorStatus.Off;
    public SensorType Type;
    public SensorGroup Group 
    { 
        get
        {
            var i = (int)Type;
            if (i <= 7)
                return SensorGroup.A;
            else if (i <= 15)
                return SensorGroup.B;
            else if (i <= 16)
                return SensorGroup.C;
            else if (i <= 24)
                return SensorGroup.D;
            else
                return SensorGroup.E;
        }
    }

    public event EventHandler<InputEventArgs> OnStatusChanged;//oStatus nStatus

    List<Guid> tasks = new();
    public void ResetState()
    {
        tasks.Clear();
        Status = SensorStatus.Off;
        IsJudging = false;
    }

    public void ClearEventHandlers()
    {
        OnStatusChanged = null;
    }

    // One stale note must not abort other handlers or leave the sensor permanently on.
    private void Dispatch(InputEventArgs args)
    {
        var handlers = OnStatusChanged;
        if (handlers == null)
            return;
        foreach (EventHandler<InputEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Sensor {Type} status handler threw: {e}");
            }
        }
    }

    public void SetOn(Guid id)
    {
        if (tasks.Contains(id))
            return;
        var oStatus = Status;
        var nStatus = SensorStatus.On;

        Status = nStatus;

        if(!tasks.Contains(id))
            tasks.Add(id);
        if (oStatus != nStatus)
        {
            if (OnStatusChanged != null)
            {
                try
                {
                    Dispatch(new InputEventArgs()
                    {
                        IsButton = false,
                        Type = Type,
                        OldStatus = oStatus,
                        Status = nStatus
                    });
                }
                finally
                {
                    IsJudging = false;
                }
            }
        }
    }
    public void SetOff(Guid id)
    {
        if (!tasks.Contains(id))
            return;
        var nStatus = SensorStatus.Off;

        tasks.Remove(id);
        if(tasks.Count == 0)
        {
            var oStatus = Status;
            try
            {
                if (OnStatusChanged != null)
                {
                    Dispatch(new InputEventArgs()
                    {
                        IsButton = false,
                        Type = Type,
                        OldStatus = oStatus,
                        Status = nStatus
                    });
                }
            }
            finally
            {
                Status = nStatus;
            }
        }
    }
    public void Click()
    {
        if (Status == SensorStatus.On)
            return;
        else if (OnStatusChanged != null)
        {
            Status = SensorStatus.On;
            try
            {
                Dispatch(new InputEventArgs()
                {
                    IsButton = false,
                    Type = Type,
                    OldStatus = SensorStatus.Off,
                    Status = SensorStatus.On
                });
            }
            finally
            {
                IsJudging = false;
                Status = SensorStatus.Off;
            }
        }
    }

    // DJAuto is a synthetic arcade pulse. It must not be swallowed by a
    // physical input that is still On or by a stale IsJudging flag.
    public bool PulseForAutoPlay()
    {
        if (OnStatusChanged == null)
            return false;

        var previousStatus = Status;
        Status = SensorStatus.On;
        IsJudging = false;
        try
        {
            Dispatch(new InputEventArgs()
            {
                IsButton = false,
                Type = Type,
                OldStatus = SensorStatus.Off,
                Status = SensorStatus.On
            });
            return true;
        }
        finally
        {
            IsJudging = false;
            Status = previousStatus;
        }
    }
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
