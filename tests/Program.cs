using System.Text.Json;
using MouseBatteryTray;

static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();
static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
var mouse = new MouseDevice("test", "Test mouse", "ACTIVE");
Check(Protocol.Mice(Json("""{"deviceInfos":[{"id":"a","displayName":"Mouse","deviceType":"MOUSE","state":"ACTIVE","capabilities":{"hasBatteryStatus":true}},{"id":"b","deviceType":"KEYBOARD","capabilities":{"hasBatteryStatus":true}},{"id":"c","deviceType":"MOUSE","capabilities":{"hasBatteryStatus":false}}]}""")).Single().Id == "a", "Only battery mice are selected");
Check(Protocol.Battery(mouse, Json("""{"percentage":12,"charging":false}""")).Percentage == 12, "G HUB battery shape");
Check(Protocol.Battery(mouse, Json("""{"percentage":0}""")).Percentage == 0, "Zero is a valid battery reading");
Check(Protocol.Battery(mouse, Json("""{"percentage":100,"charging":true}""")).Charging, "Charging and full battery");
foreach (var data in new[] { "{}", "{\"percentage\":-1}", "{\"percentage\":101}", "{\"percentage\":\"12\"}", "{\"percentage\":null}", "{\"percentage\":true}", "{\"percentage\":50,\"isBatteryRemoved\":true}", "{\"percentage\":50,\"batteryStateOfChargeSupport\":\"STATE_OF_CHARGE_NOT_SUPPORTED\"}" })
    Check(Protocol.Battery(mouse, Json(data)).Percentage is null, "Unavailable or invalid percentage " + data);
Check(Protocol.Battery(mouse with { State = "INACTIVE" }, Json("""{"percentage":12}""")).Status.Contains("inactive"), "Inactive device marked in status");
Check(Protocol.Battery(mouse, Json("""{"percentage":12,"chargingError":true}""")).Status == "Charging error", "Charging errors surfaced");
Check(Protocol.Mice(Json("{}")).Count == 0, "Missing device list");
Console.WriteLine("All protocol tests passed.");
