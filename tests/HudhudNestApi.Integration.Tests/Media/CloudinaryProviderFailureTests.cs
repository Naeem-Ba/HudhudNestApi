using System.Net.Sockets;
using HudhudNestApi.Infrastructure.Media;

namespace HudhudNestApi.Integration.Tests.Media;

/// <summary>QA Phase 8, P8-D06: a provider outage must read as a failed upload, not an unhandled 500.</summary>
public sealed class CloudinaryProviderFailureTests
{
    [Fact]
    public void NetworkFailuresAndTimeouts_AreTreatedAsTheProviderBeingUnreachable()
    {
        var none = CancellationToken.None;

        Assert.True(CloudinaryMediaStorageService.IsProviderUnreachable(new HttpRequestException("down"), none));
        Assert.True(CloudinaryMediaStorageService.IsProviderUnreachable(new IOException("reset", new SocketException()), none));
        Assert.True(CloudinaryMediaStorageService.IsProviderUnreachable(new TimeoutException(), none));
        // HttpClient reports its own timeout as a cancellation the caller did not ask for.
        Assert.True(CloudinaryMediaStorageService.IsProviderUnreachable(new TaskCanceledException("timeout"), none));
    }

    [Fact]
    public void ACallerCancellation_IsNotMistakenForAnOutage()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.False(CloudinaryMediaStorageService.IsProviderUnreachable(new OperationCanceledException(cts.Token), cts.Token));
    }

    [Fact]
    public void ABugInOurOwnCode_IsNotSwallowed()
    {
        Assert.False(CloudinaryMediaStorageService.IsProviderUnreachable(new InvalidOperationException("bug"), CancellationToken.None));
        Assert.False(CloudinaryMediaStorageService.IsProviderUnreachable(new ArgumentException("bad"), CancellationToken.None));
    }
}
