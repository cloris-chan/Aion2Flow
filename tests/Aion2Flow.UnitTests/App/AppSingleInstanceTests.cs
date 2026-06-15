using Cloris.Aion2Flow.Services;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class AppSingleInstanceTests
{
    [Fact]
    public void TryAcquire_ReturnsSecondaryWhileInstanceIsHeld()
    {
        var mutexName = CreateMutexName();
        using var primary = AppSingleInstance.TryAcquire(mutexName);
        using var secondary = AppSingleInstance.TryAcquire(mutexName);

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
    }

    [Fact]
    public void TryAcquire_AllowsNewPrimaryAfterRelease()
    {
        var mutexName = CreateMutexName();
        using (var primary = AppSingleInstance.TryAcquire(mutexName))
        {
            Assert.True(primary.IsPrimary);
        }

        using var nextPrimary = AppSingleInstance.TryAcquire(mutexName);

        Assert.True(nextPrimary.IsPrimary);
    }

    private static string CreateMutexName() => $@"Global\Cloris.Aion2Flow.Tests.{Guid.NewGuid():N}";
}
