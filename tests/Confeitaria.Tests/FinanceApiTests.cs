using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Confeitaria.Web.Contracts;
using Confeitaria.Web.Domain;

namespace Confeitaria.Tests;

public sealed class FinanceApiTests
{
    private static readonly DateOnly Opening = new(2026, 1, 1);
    private static readonly DateOnly SaleDay = new(2026, 1, 10);
    private static readonly DateOnly End = new(2026, 1, 31);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Discount_then_combined_fees_are_calculated_on_server_and_not_paid_until_received()
    {
        using var s = await Scenario.Create();
        var sale = await s.Sell(10000, 1000, 4000);
        Assert.Equal(1000, sale.RateBasisPointsSnapshot);
        Assert.Equal(200, sale.FixedFeeCentsSnapshot);
        Assert.Equal(1100, sale.EstimatedFeeCents);
        Assert.Equal(7900, sale.NetCents);
        Assert.Equal("Pending", sale.Status);

        var pending = await s.Dashboard();
        Assert.Equal(10000, pending.CashBalanceCents);
        Assert.Equal(7900, pending.AccountsReceivableCents);
        Assert.Equal(3900, pending.EstimatedOperatingResultCents);

        await s.Receive(sale.Id, SaleDay);
        var paid = await s.Dashboard();
        Assert.Equal(17900, paid.CashBalanceCents);
        Assert.Equal(7900, paid.ReceiptsCents);
        Assert.Equal(0, paid.AccountsReceivableCents);
    }

    [Fact]
    public async Task Receiving_a_sale_repeatedly_creates_exactly_one_cash_effect()
    {
        using var s = await Scenario.Create();
        var sale = await s.Sell();
        await s.Receive(sale.Id, SaleDay);
        await s.Receive(sale.Id, SaleDay);
        var dashboard = await s.Dashboard();
        var ledger = await s.Get<PageResult<LedgerRow>>("ledger?from=2026-01-01&to=2026-01-31");
        Assert.Equal(18800, dashboard.CashBalanceCents);
        Assert.Single(ledger.Items);
    }

    [Fact]
    public async Task Concurrent_receipt_retries_do_not_duplicate_cash()
    {
        using var s = await Scenario.Create();
        var sale = await s.Sell();
        var responses = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => s.Client.PostAsJsonAsync($"/api/v1/sales/{sale.Id}/receive", new ReceiptRequest(SaleDay), Json)));
        foreach (var response in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            response.Dispose();
        }
        Assert.Equal(18800, (await s.Dashboard()).CashBalanceCents);
        Assert.Single((await s.Get<PageResult<LedgerRow>>("ledger?from=2026-01-01&to=2026-01-31")).Items);
    }

    [Fact]
    public async Task Actual_fee_replaces_estimate_and_is_not_subtracted_twice()
    {
        using var s = await Scenario.Create();
        var sale = await s.Sell(10000, 1000, 4000);
        var received = await s.Post<Sale>($"sales/{sale.Id}/receive", new ReceiptRequest(SaleDay, 1500));
        Assert.Equal(1500, received.ActualFeeCents);
        Assert.Equal(7500, received.NetCents);
        await s.Post<Sale>($"sales/{sale.Id}/receive", new ReceiptRequest(SaleDay, 1500));
        var report = await s.Dashboard();
        Assert.Equal(17500, report.CashBalanceCents);
        Assert.Equal(1500, report.FeesCents);
        Assert.Equal(3500, report.EstimatedOperatingResultCents);
    }

    [Fact]
    public async Task Report_before_receipt_uses_estimate_and_after_receipt_uses_actual_fee()
    {
        using var s = await Scenario.Create();
        var sale = await s.Sell(10000, 1000, 4000);
        await s.Post<Sale>($"sales/{sale.Id}/receive", new ReceiptRequest(SaleDay.AddDays(2), 1500));
        var before = await s.Get<DashboardResult>("dashboard?from=2026-01-01&to=2026-01-11");
        var after = await s.Get<DashboardResult>("dashboard?from=2026-01-01&to=2026-01-13");
        Assert.Equal(1100, before.FeesCents);
        Assert.Equal(7900, before.NetSalesCents);
        Assert.Equal(10000, before.CashBalanceCents);
        Assert.Equal(1500, after.FeesCents);
        Assert.Equal(7500, after.NetSalesCents);
        Assert.Equal(17500, after.CashBalanceCents);
    }

    [Fact]
    public async Task Fee_configuration_changes_do_not_rewrite_existing_sale_snapshot()
    {
        using var s = await Scenario.Create();
        var old = await s.Sell();
        await s.Put<Channel>($"channels/{s.ChannelId}", new FeeCatalogRequest("Canal atualizado", 2000, 300));
        var newer = await s.Sell();
        var loaded = await s.Get<Sale>($"sales/{old.Id}");
        Assert.Equal(1200, loaded.EstimatedFeeCents);
        Assert.Equal(1000, loaded.RateBasisPointsSnapshot);
        Assert.Equal(2500, newer.RateBasisPointsSnapshot);
        Assert.Equal(2900, newer.EstimatedFeeCents);
    }

    [Fact]
    public async Task Expense_is_pending_until_payment_and_payment_retries_are_idempotent()
    {
        using var s = await Scenario.Create();
        var expense = await s.Expense(1500, s.OperatingId);
        var pending = await s.Dashboard();
        Assert.Equal(10000, pending.CashBalanceCents);
        Assert.Equal(1500, pending.AccountsPayableCents);
        Assert.Equal(-1500, pending.EstimatedOperatingResultCents);

        await s.Pay(expense.Id, SaleDay);
        await s.Pay(expense.Id, SaleDay);
        var paid = await s.Dashboard();
        Assert.Equal(8500, paid.CashBalanceCents);
        Assert.Equal(1500, paid.PaymentsCents);
        Assert.Equal(0, paid.AccountsPayableCents);
    }

    [Fact]
    public async Task Contribution_and_withdrawal_change_cash_without_changing_revenue_or_result()
    {
        using var s = await Scenario.Create();
        await s.Post<CashEntry>("owner-movements", new OwnerMovementRequest(SaleDay, CashKind.Contribution, 5000));
        await s.Post<CashEntry>("owner-movements", new OwnerMovementRequest(SaleDay, CashKind.Withdrawal, 2000));
        var d = await s.Dashboard();
        Assert.Equal(13000, d.CashBalanceCents);
        Assert.Equal(5000, d.ContributionsCents);
        Assert.Equal(2000, d.WithdrawalsCents);
        Assert.Equal(0, d.GrossSalesCents);
        Assert.Equal(0, d.EstimatedOperatingResultCents);
    }

    [Fact]
    public async Task Supply_purchase_affects_cash_but_not_profit_again_when_sale_already_includes_its_cost()
    {
        using var s = await Scenario.Create();
        var sale = await s.Sell(10000, 0, 4000, feeOverride: 0);
        await s.Receive(sale.Id, SaleDay);
        var purchase = await s.Expense(4000, s.SuppliesId);
        await s.Pay(purchase.Id, SaleDay);
        var operating = await s.Expense(500, s.OperatingId);
        await s.Pay(operating.Id, SaleDay);
        var allocated = await s.Expense(700, s.AllocatedId);
        await s.Pay(allocated.Id, SaleDay);
        var d = await s.Dashboard();
        Assert.Equal(14800, d.CashBalanceCents);
        Assert.Equal(5500, d.EstimatedOperatingResultCents);
        Assert.Equal(4000, d.SuppliesPurchasesCents);
        Assert.Equal(700, d.AllocatedExpensesCents);
        Assert.Equal(500, d.OperatingExpensesCents);
    }

    [Fact]
    public async Task Category_reclassification_does_not_rewrite_existing_expense_treatment()
    {
        using var s = await Scenario.Create();
        var purchase = await s.Expense(4000, s.SuppliesId);
        await s.Put<Category>($"categories/{s.SuppliesId}", new CategoryRequest("Categoria atualizada", ExpenseKind.Operating));
        var d = await s.Dashboard();
        Assert.Equal(4000, d.SuppliesPurchasesCents);
        Assert.Equal(0, d.OperatingExpensesCents);
        Assert.Equal(0, d.EstimatedOperatingResultCents);
        Assert.Equal(ExpenseKind.Supplies, purchase.KindSnapshot);
    }

    [Fact]
    public async Task Missing_cost_does_not_appear_as_zero_cost_or_false_complete_profit()
    {
        using var s = await Scenario.Create();
        await s.Sell(directCost: 1000);
        await s.Sell(directCost: null);
        var d = await s.Dashboard();
        Assert.Equal(2, d.SalesCount);
        Assert.Equal(1, d.SalesWithCost);
        Assert.Null(d.EstimatedOperatingResultCents);
        Assert.NotEmpty(d.Warnings);
        Assert.Null(Assert.Single(d.Channels).EstimatedMarginCents);
    }

    [Fact]
    public async Task Sale_cancellation_reverses_receipt_once_and_preserves_prior_cash_history()
    {
        using var s = await Scenario.Create();
        var sale = await s.Sell();
        var receiptDate = SaleDay.AddDays(2);
        var cancellationDate = SaleDay.AddDays(5);
        await s.Receive(sale.Id, receiptDate);
        await s.Post<Sale>($"sales/{sale.Id}/cancel", new CancelRequest(cancellationDate, "Cancelamento de teste"));
        await s.Post<Sale>($"sales/{sale.Id}/cancel", new CancelRequest(cancellationDate, "Cancelamento de teste"));

        var before = await s.Get<DashboardResult>("dashboard?from=2026-01-01&to=2026-01-13");
        var after = await s.Dashboard();
        Assert.Equal(18800, before.CashBalanceCents);
        Assert.Equal(10000, after.CashBalanceCents);
        Assert.Equal(0, after.GrossSalesCents);
        Assert.Equal(0, after.AccountsReceivableCents);

        var ledger = await s.Get<PageResult<LedgerRow>>("ledger?from=2026-01-01&to=2026-01-31");
        Assert.Equal(2, ledger.Items.Count);
        Assert.Equal(0, ledger.Items.Sum(x => x.SignedCents));
        Assert.Single(ledger.Items, x => x.IsReversal);
    }

    [Fact]
    public async Task Cancelling_pending_sale_removes_receivable_without_creating_cash_entries()
    {
        using var s = await Scenario.Create();
        var sale = await s.Sell();
        await s.Post<Sale>($"sales/{sale.Id}/cancel", new CancelRequest(SaleDay, "Pedido cancelado"));
        var d = await s.Dashboard();
        Assert.Equal(10000, d.CashBalanceCents);
        Assert.Equal(0, d.AccountsReceivableCents);
        Assert.Equal(0, d.GrossSalesCents);
        Assert.Empty((await s.Get<PageResult<LedgerRow>>("ledger?from=2026-01-01&to=2026-01-31")).Items);
        await s.Rejected($"sales/{sale.Id}/receive", new ReceiptRequest(SaleDay));
    }

    [Fact]
    public async Task Repeated_cancellation_with_null_reason_returns_client_error_and_keeps_original_reason()
    {
        using var s = await Scenario.Create();
        var sale = await s.Sell();
        await s.Post<Sale>($"sales/{sale.Id}/cancel", new CancelRequest(SaleDay, "Pedido cancelado"));
        await s.Rejected($"sales/{sale.Id}/cancel", new CancelRequest(SaleDay, null!));
        var saved = await s.Get<Sale>($"sales/{sale.Id}");
        Assert.Equal("Pedido cancelado", saved.CancellationReason);
        Assert.Equal("Cancelled", saved.Status);
        Assert.Equal(10000, (await s.Dashboard()).CashBalanceCents);
    }

    [Fact]
    public async Task Expense_and_owner_cancellations_reverse_cash_without_erasing_original_records()
    {
        using var s = await Scenario.Create();
        var expense = await s.Expense(1000, s.OperatingId);
        await s.Pay(expense.Id, SaleDay);
        await s.Post<Expense>($"expenses/{expense.Id}/cancel", new CancelRequest(SaleDay.AddDays(1), "Compra cancelada"));
        await s.Post<Expense>($"expenses/{expense.Id}/cancel", new CancelRequest(SaleDay.AddDays(1), "Compra cancelada"));
        var withdrawal = await s.Post<CashEntry>("owner-movements", new OwnerMovementRequest(SaleDay, CashKind.Withdrawal, 2000));
        await s.Post<CashEntry>($"owner-movements/{withdrawal.Id}/cancel", new CancelRequest(SaleDay.AddDays(1), "Retirada estornada"));
        await s.Post<CashEntry>($"owner-movements/{withdrawal.Id}/cancel", new CancelRequest(SaleDay.AddDays(1), "Retirada estornada"));
        Assert.Equal(10000, (await s.Dashboard()).CashBalanceCents);
        var ledger = await s.Get<PageResult<LedgerRow>>("ledger?from=2026-01-01&to=2026-01-31");
        Assert.Equal(4, ledger.Items.Count);
        Assert.Equal(2, ledger.Items.Count(x => x.IsReversal));
    }

    [Fact]
    public async Task Filters_use_sale_date_for_sales_and_effective_date_for_receipts()
    {
        using var s = await Scenario.Create();
        var sale = await s.Sell();
        await s.Receive(sale.Id, SaleDay.AddDays(2));
        var onSale = await s.Get<DashboardResult>("dashboard?from=2026-01-10&to=2026-01-10");
        var onReceipt = await s.Get<DashboardResult>("dashboard?from=2026-01-12&to=2026-01-12");
        Assert.Equal(10000, onSale.GrossSalesCents);
        Assert.Equal(0, onSale.ReceiptsCents);
        Assert.Equal(0, onReceipt.GrossSalesCents);
        Assert.Equal(8800, onReceipt.ReceiptsCents);
        Assert.Equal(8800, onReceipt.FilteredNetMovementCents);
    }

    [Fact]
    public async Task Lists_paginate_and_filter_channels_and_payment_methods_consistently()
    {
        using var s = await Scenario.Create();
        await s.Sell();
        await s.Sell();
        var channel = await s.Post<Channel>("channels", new FeeCatalogRequest("Outro canal"));
        await s.Post<Sale>("sales", new SaleRequest(SaleDay, 5000, channel.Id, s.PaymentId, DirectCostCents: 500));
        var page = await s.Get<PageResult<Sale>>($"sales?from=2026-01-01&to=2026-01-31&channelId={s.ChannelId}&paymentMethodId={s.PaymentId}&page=1&pageSize=1");
        Assert.Equal(2, page.Total);
        Assert.Single(page.Items);
        Assert.All(page.Items, sale => Assert.Equal(s.ChannelId, sale.ChannelId));
        Assert.Equal(1, page.PageSize);
        var other = await s.Get<DashboardResult>($"dashboard?from=2026-01-01&to=2026-01-31&channelId={channel.Id}");
        Assert.Equal(5000, other.GrossSalesCents);
        Assert.Equal(10000, other.CashBalanceCents);
    }

    [Fact]
    public async Task Daily_summary_is_read_only_and_csv_preserves_filters()
    {
        using var s = await Scenario.Create();
        var sale = await s.Sell();
        await s.Receive(sale.Id, SaleDay);
        var channel = await s.Post<Channel>("channels", new FeeCatalogRequest("Outro canal"));
        var other = await s.Post<Sale>("sales", new SaleRequest(SaleDay, 5000, channel.Id, s.PaymentId, DirectCostCents: 500, FeeOverrideCents: 0));
        await s.Receive(other.Id, SaleDay);
        await s.Get<DashboardResult>("daily-summary?date=2026-01-10");
        await s.Get<DashboardResult>("daily-summary?date=2026-01-10");
        Assert.Equal(23800, (await s.Dashboard()).CashBalanceCents);
        var response = await s.Client.GetAsync($"/api/v1/exports/ledger.csv?from=2026-01-01&to=2026-01-31&channelId={s.ChannelId}");
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.Contains("8800", csv);
        Assert.DoesNotContain("5000", csv);
    }

    [Fact]
    public async Task Invalid_amount_discount_and_fee_are_rejected_without_creating_sales()
    {
        using var s = await Scenario.Create();
        await s.Rejected("sales", new SaleRequest(SaleDay, -100, s.ChannelId, s.PaymentId));
        await s.Rejected("sales", new SaleRequest(SaleDay, 1000, s.ChannelId, s.PaymentId, DiscountCents: 1001));
        await s.Rejected("sales", new SaleRequest(SaleDay, 1000, s.ChannelId, s.PaymentId, FeeOverrideCents: 1001));
        Assert.Empty((await s.Get<PageResult<Sale>>("sales")).Items);
        Assert.Equal(10000, (await s.Dashboard()).CashBalanceCents);
    }

    [Fact]
    public async Task Actual_events_cannot_be_future_or_before_sale_or_opening()
    {
        using var s = await Scenario.Create();
        await s.Rejected("sales", new SaleRequest(BusinessClock.Today.AddDays(1), 1000, s.ChannelId, s.PaymentId));
        await s.Rejected("sales", new SaleRequest(Opening.AddDays(-1), 1000, s.ChannelId, s.PaymentId));
        var sale = await s.Sell();
        await s.Rejected($"sales/{sale.Id}/receive", new ReceiptRequest(SaleDay.AddDays(-1)));
        await s.Rejected($"sales/{sale.Id}/receive", new ReceiptRequest(BusinessClock.Today.AddDays(1)));
        await s.Receive(sale.Id, SaleDay.AddDays(1));
        await s.Rejected($"sales/{sale.Id}/cancel", new CancelRequest(SaleDay, "Anterior ao recebimento"));
    }

    [Fact]
    public async Task Opening_balance_and_reference_cannot_change_after_cash_movements()
    {
        using var s = await Scenario.Create();
        await s.Post<CashEntry>("owner-movements", new OwnerMovementRequest(SaleDay, CashKind.Contribution, 1000));
        var response = await s.Client.PutAsJsonAsync("/api/v1/settings", new SettingsRequest("Minha loja", Opening, 99999, 500), Json);
        Assert.True((int)response.StatusCode >= 400 && (int)response.StatusCode < 500, await response.Content.ReadAsStringAsync());
        Assert.Equal(11000, (await s.Dashboard()).CashBalanceCents);
    }

    [Fact]
    public async Task Half_cent_percentage_rounds_away_from_zero_to_a_whole_cent()
    {
        using var s = await Scenario.Create();
        var channel = await s.Post<Channel>("channels", new FeeCatalogRequest("Meio por cento", 50));
        var payment = await s.Post<PaymentMethod>("payment-methods", new FeeCatalogRequest("Sem taxa"));
        var sale = await s.Post<Sale>("sales", new SaleRequest(SaleDay, 100, channel.Id, payment.Id, DirectCostCents: 0));
        Assert.Equal(1, sale.EstimatedFeeCents);
        Assert.Equal(99, sale.NetCents);
    }

    private sealed class Scenario : IDisposable
    {
        private readonly TestAppFactory _factory = new();
        public HttpClient Client { get; }
        public int ChannelId { get; private set; }
        public int PaymentId { get; private set; }
        public int OperatingId { get; private set; }
        public int SuppliesId { get; private set; }
        public int AllocatedId { get; private set; }
        private Scenario() => Client = _factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });

        public static async Task<Scenario> Create()
        {
            var s = new Scenario();
            await s.Put<BusinessSettings>("settings", new SettingsRequest("Confeitaria fictícia", Opening, 10000, 500));
            s.ChannelId = (await s.Post<Channel>("channels", new FeeCatalogRequest("Canal teste", 500, 100))).Id;
            s.PaymentId = (await s.Post<PaymentMethod>("payment-methods", new FeeCatalogRequest("Cartão teste", 500, 100))).Id;
            s.OperatingId = (await s.Post<Category>("categories", new CategoryRequest("Energia teste", ExpenseKind.Operating))).Id;
            s.SuppliesId = (await s.Post<Category>("categories", new CategoryRequest("Ingredientes teste", ExpenseKind.Supplies))).Id;
            s.AllocatedId = (await s.Post<Category>("categories", new CategoryRequest("Rateio teste", ExpenseKind.AllocatedCost))).Id;
            return s;
        }

        public Task<Sale> Sell(long gross = 10000, long discount = 0, long? directCost = 2000, long? feeOverride = null)
            => Post<Sale>("sales", new SaleRequest(SaleDay, gross, ChannelId, PaymentId, discount, directCost, FeeOverrideCents: feeOverride));
        public Task<Expense> Expense(long amount, int categoryId) => Post<Expense>("expenses", new ExpenseRequest(SaleDay, amount, categoryId, PaymentMethodId: PaymentId));
        public Task<Sale> Receive(int id, DateOnly date) => Post<Sale>($"sales/{id}/receive", new ReceiptRequest(date));
        public Task<Expense> Pay(int id, DateOnly date) => Post<Expense>($"expenses/{id}/pay", new PaymentRequest(date, PaymentId));
        public Task<DashboardResult> Dashboard() => Get<DashboardResult>($"dashboard?from={Opening:yyyy-MM-dd}&to={End:yyyy-MM-dd}");

        public async Task<T> Get<T>(string path) => await Read<T>(await Client.GetAsync("/api/v1/" + path));
        public async Task<T> Post<T>(string path, object request) => await Read<T>(await Client.PostAsJsonAsync("/api/v1/" + path, request, Json));
        public async Task<T> Put<T>(string path, object request) => await Read<T>(await Client.PutAsJsonAsync("/api/v1/" + path, request, Json));

        public async Task Rejected(string path, object request)
        {
            using var response = await Client.PostAsJsonAsync("/api/v1/" + path, request, Json);
            Assert.True((int)response.StatusCode >= 400 && (int)response.StatusCode < 500, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        private static async Task<T> Read<T>(HttpResponseMessage response)
        {
            using (response)
            {
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
                return JsonSerializer.Deserialize<T>(body, Json) ?? throw new InvalidOperationException("Empty JSON response");
            }
        }

        public void Dispose() { Client.Dispose(); _factory.Dispose(); }
    }
}
