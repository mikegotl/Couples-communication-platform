using System.Net;
using System.Text.RegularExpressions;
using Couples.Application.Rewards;
using Couples.Domain.Common;
using Couples.Domain.Rewards;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Couples.IntegrationTests.Rewards;

/// <summary>The public partner pages work as plain form posts, signed out, with no JavaScript.</summary>
public sealed class PartnerPagesHttpTests(CouplesWebFactory factory) : IClassFixture<CouplesWebFactory>
{
    private const string Key = "kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk";

    [Fact]
    public async Task ABusinessCanApplySignedOutAndBotsAreQuietlyIgnored()
    {
        var partners = new FakePartners();
        using var client = Client(partners);
        var page = await client.GetStringAsync("/partners");
        Assert.Contains("Be a date night partner", page, StringComparison.Ordinal);

        var fields = new Dictionary<string, string>
        {
            ["_handler"] = "partner-apply",
            ["__RequestVerificationToken"] = Token(page),
            ["Input.BusinessName"] = "Luna Bistro",
            ["Input.Kind"] = "Restaurant",
            ["Input.Area"] = "Springfield",
            ["Input.ContactName"] = "Dana Reyes",
            ["Input.ContactEmail"] = "dana@luna.example",
            ["Input.ProposedOffer"] = "A dessert to share.",
        };
        var sent = await PostAsync(client, "/partners", fields);
        Assert.Contains("Thank you", sent, StringComparison.Ordinal);
        Assert.Equal("Luna Bistro", Assert.Single(partners.Applications).BusinessName);

        // The hidden field is filled in: looks like success, stores nothing.
        var bot = await PostAsync(client, "/partners", new(fields) { ["Input.Fax"] = "123", ["__RequestVerificationToken"] = Token(await client.GetStringAsync("/partners")) });
        Assert.Contains("Thank you", bot, StringComparison.Ordinal);
        Assert.Single(partners.Applications);
    }

    [Fact]
    public async Task ThePartnerCheckPageConfirmsACodeThenMarksItUsed()
    {
        var partners = new FakePartners();
        using var client = Client(partners);
        var page = await client.GetStringAsync($"/partner/check/{Key}");
        Assert.Contains("Luna Bistro", page, StringComparison.Ordinal);

        var check = await PostAsync(client, $"/partner/check/{Key}", new()
        {
            ["_handler"] = "voucher-check", ["__RequestVerificationToken"] = Token(page), ["Input.Code"] = "BT-1",
        });
        Assert.Contains("<strong>Valid.</strong>", check, StringComparison.Ordinal);
        Assert.Contains("Mark as used", check, StringComparison.Ordinal);
        Assert.False(partners.Used);

        var used = await PostAsync(client, $"/partner/check/{Key}", new()
        {
            ["_handler"] = "voucher-check", ["__RequestVerificationToken"] = Token(check), ["Input.Code"] = "BT-1", ["Input.Use"] = "true",
        });
        Assert.Contains("<strong>Done.</strong>", used, StringComparison.Ordinal);
        Assert.True(partners.Used);

        Assert.Contains("isn&#x27;t active", await client.GetStringAsync("/partner/check/unknown"), StringComparison.Ordinal);
    }

    private static string Token(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;

    private static async Task<string> PostAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await client.PostAsync(new Uri(path, UriKind.Relative), content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private HttpClient Client(FakePartners partners) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IPartnerService>(_ => partners)))
            .CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private sealed class FakePartners : IPartnerService
    {
        public List<PartnerApplication> Applications { get; } = [];

        public bool Used { get; private set; }

        public Task<Result> ApplyAsync(PartnerApplication application, CancellationToken cancellationToken = default)
        {
            Applications.Add(application);
            return Task.FromResult(Result.Ok());
        }

        public Task<string?> PartnerNameAsync(string? checkKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(checkKey == Key ? "Luna Bistro" : null);

        public Task<VoucherCheck> CheckVoucherAsync(string? checkKey, string? voucherCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new VoucherCheck(Used ? VoucherState.Used : VoucherState.Valid, "Dessert to share"));

        public Task<VoucherCheck> MarkUsedAsync(string? checkKey, string? voucherCode, CancellationToken cancellationToken = default)
        {
            Used = true;
            return Task.FromResult(new VoucherCheck(VoucherState.Used, "Dessert to share"));
        }
    }
}
