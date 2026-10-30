using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Confeitaria.Web.Contracts;
using Confeitaria.Web.Domain;

namespace Confeitaria.Tests;

public sealed class PurchaseImportApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task Nfe_preview_and_confirmation_create_expense_update_cost_and_add_stock_once()
    {
        using var scenario = await Scenario.Create();
        var xml = Nfe("12345678901234567890123456789012345678901234", "Farinha", "KG", "2.0000", "40.00");
        var preview = await scenario.Preview(xml);

        Assert.False(preview.AlreadyImported);
        Assert.Equal("XML de NF-e", preview.Format);
        Assert.Equal("Fornecedor teste", preview.Supplier);
        Assert.Equal(4000, preview.TotalCents);
        Assert.Equal(2, Assert.Single(preview.Items).Quantity);
        Assert.Equal("kg", Assert.Single(preview.Items).Unit);

        var request = new PurchaseImportCommitRequest(preview.DocumentHash, preview.AccessKey,
            preview.InvoiceNumber, preview.Supplier, preview.PurchaseDate, preview.TotalCents,
            [new PurchaseImportLineRequest("Farinha", 2000, "g", 4000, null)]);
        var result = await scenario.Commit(xml, request);

        Assert.Equal(4000, result.TotalCents);
        Assert.Equal(1, result.ItemCount);
        var ingredient = Assert.Single((await scenario.Get<PageResult<IngredientResponse>>("ingredients")).Items);
        Assert.Equal(2000, ingredient.StockQuantity);
        Assert.Equal(2000, ingredient.PackageQuantity);
        Assert.Equal(4000, ingredient.PackagePriceCents);
        var expense = await scenario.Get<Expense>($"expenses/{result.ExpenseId}");
        Assert.Equal(4000, expense.AmountCents);
        Assert.Equal("Supplies", expense.KindSnapshot.ToString());

        Assert.True((await scenario.Preview(xml)).AlreadyImported);
        using var duplicate = await scenario.CommitResponse(xml, request);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Sales_linked_to_recipes_decrease_tracked_stock_and_cancellation_restores_it()
    {
        using var scenario = await Scenario.Create();
        var xml = Nfe("22345678901234567890123456789012345678901234", "Farinha", "KG", "2.0000", "40.00");
        var preview = await scenario.Preview(xml);
        await scenario.Commit(xml, new PurchaseImportCommitRequest(preview.DocumentHash, preview.AccessKey,
            preview.InvoiceNumber, preview.Supplier, preview.PurchaseDate, preview.TotalCents,
            [new PurchaseImportLineRequest("Farinha", 2000, "g", 4000, null)]));
        var ingredient = Assert.Single((await scenario.Get<PageResult<IngredientResponse>>("ingredients")).Items);
        var recipe = await scenario.Post<RecipeResponse>("recipes", new RecipeRequest("Bolo de teste", 10, "un",
            [new RecipeItemRequest(ingredient.Id, 500, "g")]));

        var sale = await scenario.Post<Sale>("sales", new SaleRequest(BusinessClock.Today, 5000,
            scenario.ChannelId, scenario.PaymentId, RecipeId: recipe.Id, RecipeUnits: 2));
        Assert.Equal(1900, (await scenario.Get<IngredientResponse>($"ingredients/{ingredient.Id}")).StockQuantity);

        using var insufficient = await scenario.Client.PostAsJsonAsync("/api/v1/sales",
            new SaleRequest(BusinessClock.Today, 5000, scenario.ChannelId, scenario.PaymentId,
                RecipeId: recipe.Id, RecipeUnits: 100), Json);
        Assert.Equal(HttpStatusCode.Conflict, insufficient.StatusCode);

        var cancellation = new CancelRequest(BusinessClock.Today, "Venda de teste cancelada");
        await scenario.Post<Sale>($"sales/{sale.Id}/cancel", cancellation);
        await scenario.Post<Sale>($"sales/{sale.Id}/cancel", cancellation);
        Assert.Equal(2000, (await scenario.Get<IngredientResponse>($"ingredients/{ingredient.Id}")).StockQuantity);
    }

    [Fact]
    public async Task Upload_rejects_mismatched_file_signature_and_invalid_nfe_xml()
    {
        using var scenario = await Scenario.Create();
        using var wrongFormat = await scenario.Client.PostAsync("/api/v1/purchase-imports/preview",
            FileForm("nota.xml", Encoding.UTF8.GetBytes("%PDF-1.7")));
        Assert.Equal(HttpStatusCode.BadRequest, wrongFormat.StatusCode);

        using var invalidXml = await scenario.Client.PostAsync("/api/v1/purchase-imports/preview",
            FileForm("nota.xml", Encoding.UTF8.GetBytes("<nota>inválida</nota>")));
        Assert.Equal(HttpStatusCode.BadRequest, invalidXml.StatusCode);
    }

    private static MultipartFormDataContent FileForm(string name, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", name);
        return form;
    }

    private static string Nfe(string accessKey, string name, string unit, string quantity, string total)
    {
        var date = BusinessClock.Today.ToString("yyyy-MM-dd");
        return $"""
            <nfeProc xmlns="http://www.portalfiscal.inf.br/nfe">
              <NFe><infNFe Id="NFe{accessKey}">
                <ide><dhEmi>{date}T12:00:00-03:00</dhEmi><nNF>123</nNF></ide>
                <emit><xNome>Fornecedor teste</xNome></emit>
                <det nItem="1"><prod><cProd>01</cProd><xProd>{name}</xProd><uCom>{unit}</uCom><qCom>{quantity}</qCom><vUnCom>20.00</vUnCom><vProd>{total}</vProd></prod></det>
                <total><ICMSTot><vNF>{total}</vNF></ICMSTot></total>
              </infNFe></NFe>
            </nfeProc>
            """;
    }

    private sealed class Scenario : IDisposable
    {
        private readonly TestAppFactory factory = new();
        public HttpClient Client { get; }
        public int ChannelId { get; private set; }
        public int PaymentId { get; private set; }

        private Scenario() => Client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost") });

        public static async Task<Scenario> Create()
        {
            var scenario = new Scenario();
            var today = BusinessClock.Today;
            await scenario.Put<BusinessSettings>("settings", new SettingsRequest("Confeitaria teste",
                new DateOnly(today.Year, today.Month, 1), 0));
            scenario.ChannelId = (await scenario.Post<Channel>("channels", new FeeCatalogRequest("Canal teste"))).Id;
            scenario.PaymentId = (await scenario.Post<PaymentMethod>("payment-methods", new FeeCatalogRequest("Pix teste"))).Id;
            return scenario;
        }

        public async Task<PurchaseImportPreview> Preview(string xml)
        {
            using var response = await Client.PostAsync("/api/v1/purchase-imports/preview",
                FileForm("nota.xml", Encoding.UTF8.GetBytes(xml)));
            return await Read<PurchaseImportPreview>(response);
        }

        public async Task<PurchaseImportResponse> Commit(string xml, PurchaseImportCommitRequest request) =>
            await Read<PurchaseImportResponse>(await CommitResponse(xml, request));

        public Task<HttpResponseMessage> CommitResponse(string xml, PurchaseImportCommitRequest request)
        {
            var form = FileForm("nota.xml", Encoding.UTF8.GetBytes(xml));
            form.Add(new StringContent(JsonSerializer.Serialize(request, Json)), "request");
            return Client.PostAsync("/api/v1/purchase-imports/commit", form);
        }

        public async Task<T> Get<T>(string path) => await Read<T>(await Client.GetAsync("/api/v1/" + path));
        public async Task<T> Post<T>(string path, object request) =>
            await Read<T>(await Client.PostAsJsonAsync("/api/v1/" + path, request, Json));
        public async Task<T> Put<T>(string path, object request) =>
            await Read<T>(await Client.PutAsJsonAsync("/api/v1/" + path, request, Json));

        private static async Task<T> Read<T>(HttpResponseMessage response)
        {
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
                return JsonSerializer.Deserialize<T>(body, Json) ?? throw new InvalidOperationException("Resposta JSON vazia.");
            }
        }

        public void Dispose()
        {
            Client.Dispose();
            factory.Dispose();
        }
    }
}
