using Couples.Domain.Common;

namespace Couples.UnitTests;

public sealed class EntityTests
{
    private sealed class SampleEntity : Entity;

    [Fact]
    public void NewEntityGetsUniqueId()
    {
        var a = new SampleEntity();
        var b = new SampleEntity();

        Assert.NotEqual(Guid.Empty, a.Id);
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void TouchSetsCreatedOnceAndUpdatesEachTime()
    {
        var entity = new SampleEntity();
        var first = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        var later = first.AddHours(1);

        entity.Touch(first);
        entity.Touch(later);

        Assert.Equal(first, entity.CreatedAt);
        Assert.Equal(later, entity.UpdatedAt);
    }
}
