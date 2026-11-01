using System.Text.Json;

namespace Confeitaria.Tests;

public class OpenApiTests
{
    [Fact]
    public async Task Render_hostname_is_allowed_by_default()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Host = "integrativa-2026.onrender.com";

        using var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Documentation_enum_contract_matches_string_values_returned_by_api()
    {
        using var factory = new TestAppFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var schema = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var definitions = schema.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var (type, values) in new[]
        {
            ("ExpenseKind", new[] { "Operating", "Supplies", "AllocatedCost" }),
            ("CashKind", new[] { "Receipt", "Payment", "Contribution", "Withdrawal" })
        })
        {
            var definition = definitions.GetProperty(type);
            Assert.Equal("string", definition.GetProperty("type").GetString());
            Assert.Equal(values, definition.GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray());
        }
        Assert.True(schema.RootElement.GetProperty("paths").TryGetProperty("/api/v1/settings", out _));
    }
}
