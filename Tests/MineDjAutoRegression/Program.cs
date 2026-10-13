using Assets.Scripts.Types;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

// A normal DJAuto tap and a mine both observe the same synthetic contact.
var sensor = new Sensor { Type = SensorType.A1 };
var tapContacts = 0;
var mineContacts = 0;
sensor.OnStatusChanged += (_, e) => { if (e.IsClick) tapContacts++; };
sensor.OnStatusChanged += (_, e) => { if (e.IsClick) mineContacts++; };
Check(sensor.PulseForAutoPlay(), "An idle sensor must accept a DJAuto tap.");
Check(tapContacts == 1 && mineContacts == 1,
    "The DJAuto tap and overlapping mine must receive the same click.");
Check(sensor.Status == SensorStatus.Off, "The pulse must release the sensor.");

// MajdataPlay's SimulateSensorClick refuses a click on a sensor already held
// by a slide. A forced second pulse would incorrectly clear another tap.
var slideFinger = Guid.NewGuid();
sensor.SetOn(slideFinger);
var before = tapContacts;
Check(!sensor.PulseForAutoPlay(), "A held slide sensor must reject a DJAuto tap.");
Check(tapContacts == before && sensor.Status == SensorStatus.On,
    "Rejected taps must not emit a second click or release the slide finger.");
sensor.SetOff(slideFinger);
Check(sensor.PulseForAutoPlay(), "The sensor must accept clicks after the slide leaves.");
Check(mineContacts == 3, "All actual contacts must remain visible to mines.");
Console.WriteLine("Mine DJAuto sensor-chain regression passed.");
