using System.Globalization;
using System.Text.Encodings.Web;
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
    public DashboardTrendsResult DashboardTrends(ReportFilter f)
    {
        var (from, to) = Range(f);
        var monthly = to.DayNumber - from.DayNumber > 31;
        var sales = Sales(f).Where(x => x.CancelledOn == null || x.CancelledOn > to);
        var expenses = Expenses(f).Where(x => x.CancelledOn == null || x.CancelledOn > to);
        var periods = new SortedDictionary<DateOnly, (long Profit, long Expenses)>();

        if (monthly)
        {
            for (var period = new DateOnly(from.Year, from.Month, 1); period <= to; period = period.AddMonths(1))
                periods.Add(period, (0, 0));
        }
        else
        {
            for (var period = from; period <= to; period = period.AddDays(1))
                periods.Add(period, (0, 0));
        }

        DateOnly PeriodFor(DateOnly date) => monthly
            ? new DateOnly(date.Year, date.Month, 1)
            : date;

        foreach (var sale in sales)
        {
            var period = PeriodFor(sale.Date);
            var current = periods[period];
            periods[period] = (checked(current.Profit + NetAt(sale, to) - (sale.DirectCostCents ?? 0)), current.Expenses);
        }

        foreach (var expense in expenses)
        {
            var period = PeriodFor(expense.Date);
            var current = periods[period];
            periods[period] = (
                expense.KindSnapshot == ExpenseKind.Operating
                    ? checked(current.Profit - expense.AmountCents)
                    : current.Profit,
                checked(current.Expenses + expense.AmountCents));
        }

        return new(monthly, periods.Select(entry =>
            new DashboardTrendPoint(entry.Key, entry.Value.Profit, entry.Value.Expenses)).ToList());
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
    public string PrintableReport(ReportFilter f)
    {
        var (from, to) = Range(f);
        var settings = db.Settings.AsNoTracking().Single();
        var dashboard = Dashboard(f);
        var ledger = Ledger(f);
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        var encoder = HtmlEncoder.Default;
        string E(string? value) => encoder.Encode(value ?? "");
        string Money(long? cents) => cents.HasValue ? (cents.Value / 100m).ToString("C", culture) : "Parcial";
        string Date(DateOnly value) => value.ToString("dd/MM/yyyy", culture);
        string Kind(string value) => value switch
        {
            "Receipt" => "Recebimento",
            "Payment" => "Pagamento",
            "Contribution" => "Aporte",
            "Withdrawal" => "Retirada",
            _ => value
        };
        var channels = dashboard.Channels.Count > 0
            ? string.Join("", dashboard.Channels.Select(channel => $"""
                <tr><td>{E(channel.Name)}</td><td class="amount">{Money(channel.NetSalesCents)}</td><td class="amount">{Money(channel.EstimatedMarginCents)}</td><td>{channel.SalesWithCost} de {channel.SalesCount}</td></tr>
                """))
            : """<tr><td colspan="4" class="empty">Nenhuma venda por canal neste período.</td></tr>""";
        var movements = ledger.Count > 0
            ? string.Join("", ledger.Select(row => $"""
                <tr><td>{Date(row.Date)}</td><td>{E(Kind(row.Kind))}{(row.IsReversal ? " · estorno" : "")}</td><td>{E(row.Description)}</td><td class="amount {(row.SignedCents < 0 ? "negative" : "positive")}">{Money(row.SignedCents)}</td></tr>
                """))
            : """<tr><td colspan="4" class="empty">Nenhuma movimentação de caixa neste período.</td></tr>""";
        var note = dashboard.SalesWithCost < dashboard.SalesCount
            ? $"Resultado parcial: custo informado em {dashboard.SalesWithCost} de {dashboard.SalesCount} vendas."
            : "Resultado estimado após custos diretos conhecidos e despesas operacionais.";
        var expenses = checked(dashboard.OperatingExpensesCents + dashboard.SuppliesPurchasesCents + dashboard.AllocatedExpensesCents);

        return $$$"""
            <!doctype html>
            <html lang="pt-BR">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Relatório financeiro - {{{E(settings.Name)}}}</title>
                <style>
                    :root{font-family:"Segoe UI",Arial,sans-serif;color:#432a2a;background:#f8f2e8;font-synthesis:none;text-rendering:optimizeLegibility}
                    *{box-sizing:border-box}
                    body{margin:0;padding:32px 18px;background:linear-gradient(145deg,#fff5e2,#f8f2e8 52%,#fce9df)}
                    .toolbar{max-width:900px;margin:0 auto 16px;display:flex;justify-content:flex-end}
                    .toolbar button{border:0;border-radius:10px;padding:12px 18px;background:#b8323b;color:white;font:600 14px "Segoe UI",Arial,sans-serif;cursor:pointer;box-shadow:0 4px 12px #81232e33}
                    .sheet{max-width:900px;margin:0 auto;padding:42px 48px;background:#fffefa;border:1px solid #efdfcf;border-radius:18px;box-shadow:0 18px 48px #68422817}
                    .masthead{display:flex;justify-content:space-between;align-items:flex-start;gap:24px;padding-bottom:24px;border-bottom:2px solid #f1dfca}
                    .brand{color:#81232e;font-size:13px;font-weight:800;letter-spacing:.13em;text-transform:uppercase}
                    h1{margin:8px 0 5px;color:#81232e;font-family:Georgia,serif;font-size:32px}
                    .business{margin:0;color:#745e53;font-size:15px;font-weight:650}
                    .period{flex:0 0 auto;padding:11px 14px;border:1px solid #f0dfb9;border-radius:11px;background:#fff5da;color:#70552f;font-size:13px;line-height:1.55;text-align:right}
                    .eyebrow{margin:28px 0 12px;color:#a52e38;font-size:11px;font-weight:800;letter-spacing:.12em;text-transform:uppercase}
                    .metrics{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:12px}
                    .metric{min-height:96px;padding:16px;border:1px solid #efdfcf;border-radius:12px;background:linear-gradient(145deg,#fffefa,#fff7e9)}
                    .metric.featured{border-color:#edb9a8;background:linear-gradient(145deg,#fff9f1,#fce5dc)}
                    .metric span{display:block;color:#806c61;font-size:12px}
                    .metric strong{display:block;margin-top:12px;color:#51292a;font-size:19px;font-variant-numeric:tabular-nums}
                    .note{margin:12px 0 0;padding:10px 12px;border-left:3px solid #d29d32;background:#fff8e9;color:#725b43;font-size:12px}
                    h2{margin:26px 0 11px;color:#51292a;font-size:16px}
                    table{width:100%;border-collapse:collapse;font-size:12px}
                    th{padding:10px 11px;background:#81232e;color:white;font-size:11px;text-align:left}
                    th:first-child{border-radius:8px 0 0 0}
                    th:last-child{border-radius:0 8px 0 0}
                    td{padding:9px 11px;border-bottom:1px solid #f0e7dc;vertical-align:top}
                    tbody tr:nth-child(even){background:#fffaf2}
                    .amount{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}
                    .positive{color:#4f704c;font-weight:650}
                    .negative{color:#a52e38;font-weight:650}
                    .empty{padding:18px;color:#806c61;text-align:center}
                    .foot{display:flex;justify-content:space-between;gap:18px;margin-top:28px;padding-top:14px;border-top:1px solid #efdfcf;color:#806c61;font-size:10px}
                    @page{size:A4;margin:13mm}
                    @media print{ :root,body{background:#fff!important}body{padding:0;-webkit-print-color-adjust:exact;print-color-adjust:exact}.toolbar{display:none}.sheet{max-width:none;margin:0;padding:0;border:0;border-radius:0;box-shadow:none}.masthead{break-inside:avoid}.metric{break-inside:avoid}h2{break-after:avoid}thead{display:table-header-group}tr{break-inside:avoid}table{break-inside:auto}.foot{break-inside:avoid}}
                    @media(max-width:640px){body{padding:14px 8px}.sheet{padding:24px 18px}.masthead{flex-direction:column}h1{font-size:27px}.period{text-align:left}.metrics{grid-template-columns:repeat(2,minmax(0,1fr))}.metric{padding:12px}.metric strong{font-size:16px}.table-wrap{overflow-x:auto}table{min-width:570px}}
                </style>
            </head>
            <body>
                <div class="toolbar"><button type="button" onclick="window.print()">Imprimir / Salvar em PDF</button></div>
                <main class="sheet">
                    <header class="masthead">
                        <div><div class="brand">Gere Doce · Confeitaria artesanal</div><h1>Relatório financeiro</h1><p class="business">{{{E(settings.Name)}}}</p></div>
                        <div class="period"><strong>Período</strong><br>{{{Date(from)}}} a {{{Date(to)}}}</div>
                    </header>
                    <p class="eyebrow">Resumo do período</p>
                    <div class="metrics">
                        <div class="metric featured"><span>Vendas líquidas</span><strong>{{{Money(dashboard.NetSalesCents)}}}</strong></div>
                        <div class="metric"><span>{{{(dashboard.EstimatedOperatingResultCents.HasValue ? "Lucro estimado" : "Resultado parcial")}}}</span><strong>{{{Money(dashboard.EstimatedOperatingResultCents ?? dashboard.PartialOperatingResultCents)}}}</strong></div>
                        <div class="metric"><span>Gastos registrados</span><strong>{{{Money(expenses)}}}</strong></div>
                        <div class="metric"><span>Saldo de caixa</span><strong>{{{Money(dashboard.CashBalanceCents)}}}</strong></div>
                    </div>
                    <p class="note">{{{E(note)}}} Custos diretos sem informação não são considerados zero.</p>
                    <h2>Vendas por canal</h2>
                    <table><thead><tr><th>Canal</th><th class="amount">Vendas líquidas</th><th class="amount">Margem conhecida</th><th>Vendas com custo</th></tr></thead><tbody>{{{channels}}}</tbody></table>
                    <h2>Movimentações de caixa</h2>
                    <table><thead><tr><th>Data</th><th>Tipo</th><th>Descrição</th><th class="amount">Valor</th></tr></thead><tbody>{{{movements}}}</tbody></table>
                    <footer class="foot"><span>Gere Doce · Relatório gerado para uso interno.</span><span>{{{Date(DateOnly.FromDateTime(DateTime.Today))}}}</span></footer>
                </main>
            </body>
            </html>
            """;
    }
}
