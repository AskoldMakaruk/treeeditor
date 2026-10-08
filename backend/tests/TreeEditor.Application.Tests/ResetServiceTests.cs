using TreeEditor.Application.Caching;
using TreeEditor.Application.Services;

namespace TreeEditor.Application.Tests;

public sealed class ResetServiceTests
{
    [Fact]
    public async Task Reset_uses_the_same_lock_as_Apply()
    {
        var repository = new FakeElementRepository();
        var distributedLock = new RecordingLock();
        var notifier = new RecordingNotifier();
        var service = new ResetService(repository, new NoopCache(), distributedLock, new FakeSampleData(), notifier);

        await service.ResetAsync(default);

        Assert.Equal([CacheKeys.TreeLock], distributedLock.AcquiredKeys);
    }

    [Fact]
    public async Task Reset_notifies_with_the_reset_flag()
    {
        var repository = new FakeElementRepository();
        var notifier = new RecordingNotifier();
        var service = new ResetService(
            repository, new NoopCache(), new RecordingLock(), new FakeSampleData(), notifier);

        await service.ResetAsync(default);

        var call = Assert.Single(notifier.Calls);
        Assert.True(call.Reset);
        Assert.Empty(call.ChangedIds);
    }
}
