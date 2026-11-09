using Couples.Domain.Privacy;
using Couples.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Privacy;

public sealed class ClassificationTests(CouplesWebFactory factory) : IClassFixture<CouplesWebFactory>
{
    [Fact]
    public void EveryApplicationEntityDeclaresClassificationAndRetention()
    {
        using var scope = factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<CouplesDbContext>().Model;

        var appTypes = model.GetEntityTypes()
            .Select(t => t.ClrType)
            .Where(t => t.Namespace?.StartsWith("Couples.", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(appTypes);
        var missing = appTypes
            .Where(t => t.GetCustomAttributes(typeof(PrivacyAttribute), inherit: false) is not [PrivacyAttribute { Retention.Length: > 0 }])
            .Select(t => t.Name)
            .ToList();
        Assert.True(missing.Count == 0, "Missing [Privacy] on: " + string.Join(", ", missing));
    }

    [Fact]
    public void DataInventoryDocumentsEveryEntity()
    {
        using var scope = factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<CouplesDbContext>().Model;
        var inventory = File.ReadAllText(FindRepoFile("docs/privacy/data-inventory.md"));

        var undocumented = model.GetEntityTypes()
            .Select(t => t.ClrType)
            .Where(t => t.Namespace?.StartsWith("Couples.", StringComparison.Ordinal) == true)
            .Where(t => !inventory.Contains($"`{t.Name}`", StringComparison.Ordinal))
            .Select(t => t.Name)
            .ToList();
        Assert.True(undocumented.Count == 0, "Not in data-inventory.md: " + string.Join(", ", undocumented));
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(relative);
    }
}
