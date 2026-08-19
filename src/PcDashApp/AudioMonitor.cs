using NAudio.CoreAudioApi;

namespace PcDashApp;

/// <summary>Reads real system volume/mute via the Windows Core Audio API (NAudio wrapper).</summary>
public sealed class AudioMonitor : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public (int VolumePercent, bool Muted)? ReadVolumeAndMute()
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var endpointVolume = device.AudioEndpointVolume;
            var percent = (int)Math.Round(endpointVolume.MasterVolumeLevelScalar * 100);
            return (percent, endpointVolume.Mute);
        }
        catch (Exception)
        {
            // No default render device (e.g. nothing plugged in) -- report unknown, don't crash the loop.
            return null;
        }
    }

    public void Dispose() => _enumerator.Dispose();
}
