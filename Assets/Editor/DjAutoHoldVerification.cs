using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Scripts.Types;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DjAutoHoldVerification
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Fields).SetValue(obj, value);
    static T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, Fields).GetValue(obj);
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var previousMode = InputManager.Mode;
        InputManager.Mode = AutoPlayMode.DJAuto;
        var root = new GameObject("DJAuto Hold regression");
        try
        {
            var clock = root.AddComponent<AudioTimeProvider>(); clock.isStart = true;
            var registry = root.AddComponent<NoteManager>();
            var counter = root.AddComponent<ObjectCounter>(); Set(counter, "notes", registry);
            var sensor = root.AddComponent<Sensor>(); sensor.Type = SensorType.A1;
            var input = root.AddComponent<InputManager>(); Set(input, "sensors", new List<Sensor> { sensor });
            var sensors = root.AddComponent<SensorManager>(); Set(sensors, "sensors", new List<Sensor> { sensor });
            var check = typeof(HoldDrop).GetMethod("Check", Fields);
            var tick = typeof(HoldDrop).GetMethod("FixedUpdate", Fields);
            foreach (var duration in new[] { 0f, 240f / 205 / 16 })
            {
                sensor.ResetState(); sensor.ClearEventHandlers(); registry.noteOrder.Clear(); registry.noteIndex[1] = 0;
                var holds = new List<HoldDrop>();
                for (var i = 0; i < 20; i++)
                {
                    var obj = new GameObject("Hold " + i); obj.transform.SetParent(root.transform);
                    var hold = obj.AddComponent<HoldDrop>();
                    hold.time = i * (240f / 205 / 8); hold.LastFor = duration; hold.startPosition = 1;
                    Set(hold, "timeProvider", clock); Set(hold, "objectCounter", counter); Set(hold, "noteManager", registry);
                    Set(hold, "sensor", sensor); Set(hold, "sensorPos", SensorType.A1);
                    Set(hold, "manager", sensors); Set(hold, "inputManager", input);
                    var effect = new GameObject("Hold effect"); effect.transform.SetParent(root.transform);
                    effect.AddComponent<ParticleSystem>(); effect.SetActive(false); Set(hold, "holdEffect", effect);
                    registry.noteOrder[obj] = i;
                    sensor.OnStatusChanged += (EventHandler<InputEventArgs>)Delegate.CreateDelegate(typeof(EventHandler<InputEventArgs>), hold, check);
                    holds.Add(hold);
                }
                var contacts = 0;
                sensor.OnStatusChanged += (_, args) => { if (args.IsClick) contacts++; };
                for (var i = 0; i < holds.Count; i++)
                {
                    clock.AudioTime = holds[i].time;
                    tick.Invoke(holds[i], null);
                    if (!Get<bool>(holds[i], "isJudged") || Get<JudgeType>(holds[i], "judgeResult") != JudgeType.Perfect)
                        throw new Exception($"Hold {i} / duration {duration} was not Perfect");
                    for (var j = i + 1; j < holds.Count; j++)
                        if (Get<bool>(holds[j], "isJudged")) throw new Exception("One contact judged multiple future Hold heads");
                    clock.AudioTime += duration;
                    sensors.SetSensorOff(sensor.Type, Get<Guid>(holds[i], "guid"));
                    if (registry.noteIndex[1] != i + 1) throw new Exception("Release judged another head / queue advanced twice");
                }
                if (contacts != holds.Count) throw new Exception("DJAuto generated duplicate press events or hid contacts from other observers");
                foreach (var hold in holds) { hold.previewOnly = true; UnityEngine.Object.DestroyImmediate(hold.gameObject); }
            }
            Set(input, "sensorObjs", new List<GameObject> { sensor.gameObject });
            foreach (var type in new[] { typeof(HoldDrop), typeof(TouchHoldDrop) })
            {
                sensor.ResetState(); sensor.ClearEventHandlers();
                registry.noteIndex[1] = 0; registry.touchIndex[SensorType.A1] = 0;
                var obj = new GameObject("Scrubbed " + type.Name); obj.transform.SetParent(root.transform);
                var hold = (NoteLongDrop)obj.AddComponent(type);
                hold.time = 0; hold.LastFor = 4; hold.startPosition = 1;
                Set(hold, "timeProvider", clock); Set(hold, "objectCounter", counter); Set(hold, "noteManager", registry);
                Set(hold, "sensor", sensor); Set(hold, "manager", sensors); Set(hold, "inputManager", input);
                if (type == typeof(HoldDrop))
                {
                    Set(hold, "sensorPos", SensorType.A1);
                    obj.AddComponent<SpriteRenderer>(); Set(hold, "animator", obj.AddComponent<Animator>());
                }
                else ((TouchHoldDrop)hold).boarder = obj.AddComponent<SpriteRenderer>();
                var effect = new GameObject("Restored hold effect", typeof(ParticleSystem));
                effect.transform.SetParent(root.transform); effect.SetActive(false); Set(hold, "holdEffect", effect);
                clock.AudioTime = 2; Set(hold, "timelineRestorePending", true);
                type.GetMethod("RestoreTimelineState", Fields).Invoke(hold, new object[] { 2f });
                // The real start transaction clears sensors AFTER restoring note heads.
                sensors.ResetAllSensors();
                var sustainTick = type.GetMethod("FixedUpdate", Fields);
                for (var frame = 0; frame < 20; frame++)
                {
                    clock.AudioTime = 2 + frame * .02f; sustainTick.Invoke(hold, null);
                    if (sensor.Status != SensorStatus.On || Get<float>(hold, "playerIdleTime") != 0)
                        throw new Exception(type.Name + " released its restored sustain");
                }
                InputManager.Mode = AutoPlayMode.Disable; clock.isStart = false; sustainTick.Invoke(hold, null);
                InputManager.Mode = AutoPlayMode.DJAuto; clock.isStart = true; sustainTick.Invoke(hold, null);
                if (sensor.Status != SensorStatus.On || Get<float>(hold, "playerIdleTime") != 0)
                    throw new Exception(type.Name + " released after pause/resume");
                if (type == typeof(HoldDrop) ? registry.noteIndex[1] != 1 : registry.touchIndex[SensorType.A1] != 1)
                    throw new Exception(type.Name + " consumed its restored head twice");
                hold.previewOnly = true; UnityEngine.Object.DestroyImmediate(obj);
                // Edit-mode objects do not execute the runtime OnDestroy callback.
                sensors.ResetAllSensors();
            }
            Debug.Log("DJAUTO SEEK HOLD PASS: Hold and TouchHold reclaim reset sensors, hold through resume, no idle time, one head commit.");
            Debug.Log("DJAUTO HOLD PASS: 205 BPM eighth-note repeats, 16:1 and zero duration, Perfect heads, one press per head, no release judgment, contacts remain observable.");
        }
        finally
        {
            foreach (var hold in root.GetComponentsInChildren<HoldDrop>(true)) hold.previewOnly = true;
            UnityEngine.Object.DestroyImmediate(root); InputManager.Mode = previousMode;
        }
    }
}
