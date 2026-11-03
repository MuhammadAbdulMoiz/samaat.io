using Samaat.Core.Audio;

namespace Samaat.Core.Tests;

public class AudioShareTests
{
    [Fact]
    public async Task NoAudioShare_IsUnsupported_AndEmpty()
    {
        IAudioShare share = new NoAudioShare();

        Assert.False(share.IsSupported);
        Assert.Empty(share.Outputs);
        await Assert.ThrowsAsync<NotSupportedException>(() => share.StartAsync("any"));
        await share.DisposeAsync();
    }
}
