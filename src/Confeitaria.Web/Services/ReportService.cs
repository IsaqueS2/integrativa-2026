using System.Globalization;
using System.Text;
using Confeitaria.Web.Contracts;
using Confeitaria.Web.Data;
using Confeitaria.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Confeitaria.Web.Services;

public class ReportService(AppDbContext db)
{
    public (DateOnly From, DateOnly To) Range(ReportFilter f)
    {
        var to = f.To ?? BusinessClock.Today;
        var from = f.From ?? new DateOnly(to.Year, to.Month, 1);
        Rules.Require(from <= to && from.Year >= 2000 && to.Year <= 2100, "Período inválido.");
        Rules.Require(to >= db.Settings.Single().OpeningDate, "Período anterior ao marco de abertura.");
        return (from, to);
    }
    public IReadOnlyList<Sale> Sales(ReportFilter f, string? status = null)
    {
        var (from, to) = Range(f);
        var query = db.Sales.AsNoTracking().Where(x => x.Date >= from && x.Date <= to);
        if (f.ChannelId.HasValue) query = query.Where(x => x.ChannelId == f.ChannelId);
        if (f.PaymentMethodId.HasValue) query = query.Where(x => x.PaymentMethodId == f.PaymentMethodId);
        if (f.CategoryId.HasValue) return [];
        var result = query.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToList();
        return status == null ? result : result.Where(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
    }
    public IReadOnlyList<Expense> Expenses(ReportFilter f, string? status = null)
    {
        var (from, to) = Range(f);
        if (f.ChannelId.HasValue) return [];
        var query = db.Expenses.AsNoTracking().Where(x => x.Date >= from && x.Date <= to);
        if (f.PaymentMethodId.HasValue) query = query.Where(x => x.PaymentMethodId == f.PaymentMethodId);
        if (f.CategoryId.HasValue) query = query.Where(x => x.CategoryId == f.CategoryId);
        var result = query.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToList();
        return status == null ? result : result.Where(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
    }
    public IReadOnlyList<LedgerRow> Ledger(ReportFilter f)
    {
        var (from, to) = Range(f);
        return AllLedger(to).Where(x => x.Date >= from)
            .Where(x => !f.ChannelId.HasValue || x.ChannelId == f.ChannelId)
            .Where(x => !f.PaymentMethodId.HasValue || x.PaymentMethodId == f.PaymentMethodId)
            .Where(x => !f.CategoryId.HasValue || x.CategoryId == f.CategoryId)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.SourceId).ToList();
    }
    List<LedgerRow> AllLedger(DateOnly to)
    {
        var opening = db.Settings.AsNoTracking().Single().OpeningDate;
        var entries = db.CashEntries.AsNoTracking().Where(x => x.Date >= opening && x.Date <= to).ToList();
        var sales = db.Sales.AsNoTracking().ToDictionary(x => x.Id);
        var expenses = db.Expenses.AsNoTracking().ToDictionary(x => x.Id);
        var rows = new List<LedgerRow>();
        foreach (var e in entries)
        {
            var channel = e.SaleId.HasValue ? sales[e.SaleId.Value].ChannelId : (int?)null;
            var category = e.ExpenseId.HasValue ? expenses[e.ExpenseId.Value].CategoryId : (int?)null;
            rows.Add(new(e.Date, e.Kind.ToString(), e.Id, e.SignedCents, e.Description, channel, e.PaymentMethodId, category, false));
            if (e.ReversedOn.HasValue && e.ReversedOn <= to)
                rows.Add(new(e.ReversedOn.Value, e.Kind.ToString(), e.Id, -e.SignedCents, "Estorno: " + e.ReversalReason, channel, e.PaymentMethodId, category, true));
        }
        return rows;
    }
    static long NetAt(Sale s, DateOnly to) => s.GrossCents - s.DiscountCents -
        (s.ReceivedOn <= to ? s.ActualFeeCents ?? s.EstimatedFeeCents : s.EstimatedFeeCents);
    public DashboardResult Dashboard(ReportFilter f)
    {
        var (from, to) = Range(f);
        var settings = db.Settings.AsNoTracking().Single();
        var sales = Sales(f).Where(x => x.CancelledOn == null || x.CancelledOn > to).ToList();
        var expenses = Expenses(f).Where(x => x.CancelledOn == null || x.CancelledOn > to).ToList();
        var rows = Ledger(f);
        var allRows = AllLedger(to);
        var known = sales.Where(x => x.DirectCostCents.HasValue).ToList();
        var net = sales.Sum(x => NetAt(x, to));
        var operating = expenses.Where(x => x.KindSnapshot == ExpenseKind.Operating).Sum(x => x.AmountCents);
        var knownCost = known.Sum(x => x.DirectCostCents!.Value);
        var partial = checked(net - knownCost - operating);
        var outstandingSales = db.Sales.AsNoTracking().Where(x => x.Date <= to && (x.CancelledOn == null || x.CancelledOn > to) && (x.ReceivedOn == null || x.ReceivedOn > to)).ToList();
        var outstandingExpenses = db.Expenses.AsNoTracking().Where(x => x.Date <= to && (x.CancelledOn == null || x.CancelledOn > to) && (x.PaidOn == null || x.PaidOn > to)).ToList();
        // Global balance/forecast intentionally never depend on category/channel filters.
        var balance = checked(settings.OpeningBalanceCents + allRows.Sum(x => x.SignedCents));
        var totalReceivable = outstandingSales.Sum(x => NetAt(x, to));
        var totalPayable = outstandingExpenses.Sum(x => x.AmountCents);
        var receivable = outstandingSales.Where(x => !f.CategoryId.HasValue && (!f.ChannelId.HasValue || x.ChannelId == f.ChannelId) && (!f.PaymentMethodId.HasValue || x.PaymentMethodId == f.PaymentMethodId)).Sum(x => NetAt(x, to));
        var payable = outstandingExpenses.Where(x => !f.ChannelId.HasValue && (!f.CategoryId.HasValue || x.CategoryId == f.CategoryId) && (!f.PaymentMethodId.HasValue || x.PaymentMethodId == f.PaymentMethodId)).Sum(x => x.AmountCents);
        var warnings = new List<string>();
        if (balance < settings.ReserveCents) warnings.Add("Caixa global abaixo da reserva configurada.");
        if (known.Count < sales.Count) warnings.Add($"Resultado parcial: {sales.Count - known.Count} venda(s) sem custo informado. Custo ausente não é zero.");
        if (outstandingSales.Any(x => x.DueDate < to)) warnings.Add("Há recebimentos vencidos no negócio.");
        if (outstandingExpenses.Any(x => x.DueDate < to)) warnings.Add("Há contas vencidas no negócio.");
        if (expenses.Any(x => x.KindSnapshot == ExpenseKind.AllocatedCost)) warnings.Add("Despesas classificadas como rateio já incluído nas receitas foram excluídas do resultado; confira a classificação.");
        var names = db.Channels.AsNoTracking().ToDictionary(x => x.Id, x => x.Name);
        var channels = sales.GroupBy(x => x.ChannelId).Select(g => new ChannelResult(g.Key, names[g.Key], g.Sum(x => NetAt(x, to)),
            g.Sum(x => x.DirectCostCents ?? 0), g.Count(), g.Count(x => x.DirectCostCents.HasValue),
            g.All(x => x.DirectCostCents.HasValue) ? g.Sum(x => NetAt(x, to) - x.DirectCostCents!.Value) : null)).ToList();
        long KindSum(string kind) => rows.Where(x => x.Kind == kind).Sum(x => x.SignedCents);
        return new(from, to, sales.Sum(x => x.GrossCents), sales.Sum(x => x.DiscountCents),
            sales.Sum(x => x.GrossCents - x.DiscountCents - NetAt(x, to)), net, knownCost, sales.Count, known.Count,
            known.Count == sales.Count ? partial : null, partial, operating,
            expenses.Where(x => x.KindSnapshot == ExpenseKind.Supplies).Sum(x => x.AmountCents),
            expenses.Where(x => x.KindSnapshot == ExpenseKind.AllocatedCost).Sum(x => x.AmountCents),
            KindSum("Receipt"), -KindSum("Payment"), KindSum("Contribution"), -KindSum("Withdrawal"), receivable, payable,
            balance, rows.Sum(x => x.SignedCents), checked(balance + totalReceivable - totalPayable), settings.ReserveCents, channels, warnings);
    }
    public static PageResult<T> Paginate<T>(IReadOnlyList<T> items, int page, int pageSize)
    {
        Rules.Require(page >= 1 && page <= 1_000_000 && pageSize is >= 1 and <= 100, "Paginação inválida; page >= 1, pageSize entre 1 e 100.");
        return new(items.Skip((page - 1) * pageSize).Take(pageSize).ToList(), items.Count, page, pageSize);
    }
    static string Cell(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (value is string && text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    public string LedgerCsv(ReportFilter f)
    {
        var result = new StringBuilder("data;tipo;id_movimento;valor_assinado_centavos;descricao;canal_id;pagamento_id;categoria_id;estorno\r\n");
        foreach (var r in Ledger(f)) result.AppendLine(string.Join(';', new object?[] { r.Date.ToString("yyyy-MM-dd"), r.Kind, r.SourceId, r.SignedCents, r.Description, r.ChannelId, r.PaymentMethodId, r.CategoryId, r.IsReversal }.Select(Cell)));
        return result.ToString();
    }
    public string SummaryCsv(ReportFilter f)
    {
        var d = Dashboard(f);
        return "indicador;valor_centavos\r\n" + string.Join("\r\n", new (string Label, long? Value)[] {
            ("Vendas brutas", d.GrossSalesCents), ("Descontos", d.DiscountsCents), ("Taxas", d.FeesCents),
            ("Vendas líquidas", d.NetSalesCents), ("Custos conhecidos", d.KnownDirectCostsCents), ("Despesas operacionais", d.OperatingExpensesCents),
            ("Resultado estimado (vazio se incompleto)", d.EstimatedOperatingResultCents), ("Recebimentos", d.ReceiptsCents),
            ("Pagamentos", d.PaymentsCents), ("Retiradas", d.WithdrawalsCents), ("Caixa global", d.CashBalanceCents),
            ("Movimento filtrado", d.FilteredNetMovementCents) }.Select(x => Cell(x.Label) + ";" + Cell(x.Value))) + "\r\n";
    }
}
