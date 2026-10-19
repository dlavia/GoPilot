using System.Text.Json.Nodes;
using NAudio.CoreAudioApi;

namespace GoSPanel.Bridge;

/// <summary>
/// Volumen maestro del dispositivo de salida predeterminado (Core Audio, con NAudio). Se hace siempre en un hilo del
/// grupo (MTA): con las interfaces COM declaradas a mano, según el hilo que llegaba la conversión fallaba
/// (InvalidCastException) y el widget se quedaba con un valor viejo y sin poder cambiarlo.
/// </summary>
internal static class Volume
{
    public static JsonObject Get() => Task.Run(() => Read()).GetAwaiter().GetResult();

    public static JsonObject Set(int value)
    {
        value = Math.Clamp(value, 0, 100);
        return Task.Run(() =>
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = value / 100f;
            if (value > 0) device.AudioEndpointVolume.Mute = false;
            return Read();
        }).GetAwaiter().GetResult();
    }

    private static JsonObject Read()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var volume = device.AudioEndpointVolume;
        return new JsonObject { ["value"] = (int)Math.Round(volume.MasterVolumeLevelScalar * 100), ["muted"] = volume.Mute };
    }
}
