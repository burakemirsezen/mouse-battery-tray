using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MouseBatteryTray;

internal sealed record MouseDevice(string Id, string Name, string State);
internal sealed record BatteryReading(string Name, int? Percentage, bool Charging, string Status, bool IsStale = false);

internal static class Protocol
{
    public static List<MouseDevice> Mice(JsonElement payload)
    {
        var result = new List<MouseDevice>();
        if (!payload.TryGetProperty("deviceInfos", out var devices) || devices.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var device in devices.EnumerateArray())
        {
            if (Text(device, "deviceType") != "MOUSE" ||
                !device.TryGetProperty("capabilities", out var caps) ||
                !caps.TryGetProperty("hasBatteryStatus", out var hasBattery) || hasBattery.ValueKind != JsonValueKind.True)
                continue;
            var id = Text(device, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            result.Add(new(id, Text(device, "displayName") ?? "Logitech mouse", Text(device, "state") ?? "UNKNOWN"));
        }
        return result;
    }

    public static BatteryReading Battery(MouseDevice device, JsonElement payload)
    {
        int? percentage = payload.TryGetProperty("percentage", out var number) &&
            number.ValueKind == JsonValueKind.Number && number.TryGetDouble(out var value) && double.IsFinite(value) && value >= 0 && value <= 100
            ? (int)Math.Round(value, MidpointRounding.AwayFromZero) : null;
        var removed = payload.TryGetProperty("isBatteryRemoved", out var batteryRemoved) && batteryRemoved.ValueKind == JsonValueKind.True;
        if (removed || Text(payload, "batteryStateOfChargeSupport") == "STATE_OF_CHARGE_NOT_SUPPORTED") percentage = null;
        var charging = payload.TryGetProperty("charging", out var charge) && charge.ValueKind == JsonValueKind.True;
        var status = percentage is null ? "Battery unavailable" : charging ? "Charging" : "On battery";
        if (payload.TryGetProperty("chargingError", out var error) && error.ValueKind == JsonValueKind.True) status = "Charging error";
        if (device.State != "ACTIVE") status += " (" + device.State.ToLowerInvariant() + ")";
        return new(device.Name, percentage, charging, status);
    }

    public static string? Text(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}

internal static class GHub
{
    public static async Task<BatteryReading> ReadAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("json");
        socket.Options.Proxy = null;
        await socket.ConnectAsync(new Uri("ws://127.0.0.1:9010"), timeout.Token);
        var payload = await RequestAsync(socket, "/devices/list", timeout.Token);
        var devices = Protocol.Mice(payload);
        var mouse = devices.FirstOrDefault(d => d.State == "ACTIVE") ?? devices.FirstOrDefault();
        if (mouse is null) return new("Logitech mouse", null, false, "No battery mouse found");
        if (mouse.State == "NOT_CONNECTED") return new(mouse.Name, null, false, "Mouse disconnected");
        var battery = await RequestAsync(socket, $"/battery/{mouse.Id}/state", timeout.Token);
        return Protocol.Battery(mouse, battery);
    }

    private static async Task<JsonElement> RequestAsync(ClientWebSocket socket, string path, CancellationToken token)
    {
        var msgId = Guid.NewGuid().ToString();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { path, verb = "GET", msgId });
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, token);
        while (true)
        {
            using var message = new MemoryStream();
            var buffer = new byte[8192];
            ValueWebSocketReceiveResult frame;
            do
            {
                frame = await socket.ReceiveAsync(buffer.AsMemory(), token);
                if (frame.MessageType == WebSocketMessageType.Close) throw new IOException("G HUB closed the connection.");
                if (frame.MessageType != WebSocketMessageType.Text) throw new IOException("Unexpected G HUB message.");
                message.Write(buffer, 0, frame.Count);
                if (message.Length > 2 * 1024 * 1024) throw new IOException("G HUB message is too large.");
            } while (!frame.EndOfMessage);
            using var json = JsonDocument.Parse(message.ToArray());
            var root = json.RootElement;
            if (Protocol.Text(root, "path") != path || Protocol.Text(root, "verb") != "GET") continue;
            if (root.TryGetProperty("result", out var result) && Protocol.Text(result, "code") != "SUCCESS")
                throw new IOException("G HUB could not read this mouse.");
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                throw new IOException("G HUB returned no battery data.");
            return payload.Clone();
        }
    }
}
