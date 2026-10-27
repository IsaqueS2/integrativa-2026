using System.Text;
using Confeitaria.Web.Contracts;
using Confeitaria.Web.Data;
using Confeitaria.Web.Domain;
using Confeitaria.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Confeitaria.Web.Api;

public static class FinanceEndpoints
{
    public static void MapFinance(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapGet("/settings", (FinanceService s) => s.Settings());
        api.MapPut("/settings", (SettingsRequest r, FinanceService s) => s.UpdateSettings(r));
        api.MapGet("/catalogs", (AppDbContext db) => new { categories = db.Categories.AsNoTracking().ToList(), channels = db.Channels.AsNoTracking().ToList(), paymentMethods = db.PaymentMethods.AsNoTracking().ToList() });
        api.MapGet("/categories/{id:int}", (int id, AppDbContext db) => db.Categories.AsNoTracking().SingleOrDefault(x => x.Id == id) is { } x ? Results.Ok(x) : Results.NotFound());
        api.MapGet("/channels/{id:int}", (int id, AppDbContext db) => db.Channels.AsNoTracking().SingleOrDefault(x => x.Id == id) is { } x ? Results.Ok(x) : Results.NotFound());
        api.MapGet("/payment-methods/{id:int}", (int id, AppDbContext db) => db.PaymentMethods.AsNoTracking().SingleOrDefault(x => x.Id == id) is { } x ? Results.Ok(x) : Results.NotFound());
        api.MapPost("/categories", (CategoryRequest r, FinanceService s) => { var x = s.SaveCategory(r); return Results.Created($"/api/v1/categories/{x.Id}", x); });
        api.MapPut("/categories/{id:int}", (int id, CategoryRequest r, FinanceService s) => s.SaveCategory(r, id));
        api.MapPost("/channels", (FeeCatalogRequest r, FinanceService s) => { var x = s.SaveChannel(r); return Results.Created($"/api/v1/channels/{x.Id}", x); });
        api.MapPut("/channels/{id:int}", (int id, FeeCatalogRequest r, FinanceService s) => s.SaveChannel(r, id));
        api.MapPost("/payment-methods", (FeeCatalogRequest r, FinanceService s) => { var x = s.SavePaymentMethod(r); return Results.Created($"/api/v1/payment-methods/{x.Id}", x); });
        api.MapPut("/payment-methods/{id:int}", (int id, FeeCatalogRequest r, FinanceService s) => s.SavePaymentMethod(r, id));
        api.MapGet("/sales", ([AsParameters] ReportFilter f, ReportService s, string? status = null, int page = 1, int pageSize = 25) => ReportService.Paginate(s.Sales(f, status), page, pageSize));
        api.MapGet("/sales/{id:int}", (int id, AppDbContext db) => db.Sales.AsNoTracking().SingleOrDefault(x => x.Id == id) is { } x ? Results.Ok(x) : Results.NotFound());
        api.MapPost("/sales", (SaleRequest r, FinanceService s) => { var x = s.CreateSale(r); return Results.Created($"/api/v1/sales/{x.Id}", x); });
        api.MapPost("/sales/{id:int}/receive", (int id, ReceiptRequest r, FinanceService s) => s.Receive(id, r));
        api.MapPost("/sales/{id:int}/cancel", (int id, CancelRequest r, FinanceService s) => s.CancelSale(id, r));
        api.MapGet("/expenses", ([AsParameters] ReportFilter f, ReportService s, string? status = null, int page = 1, int pageSize = 25) => ReportService.Paginate(s.Expenses(f, status), page, pageSize));
        api.MapGet("/expenses/{id:int}", (int id, AppDbContext db) => db.Expenses.AsNoTracking().SingleOrDefault(x => x.Id == id) is { } x ? Results.Ok(x) : Results.NotFound());
        api.MapPost("/expenses", (ExpenseRequest r, FinanceService s) => { var x = s.CreateExpense(r); return Results.Created($"/api/v1/expenses/{x.Id}", x); });
        api.MapPost("/expenses/{id:int}/pay", (int id, PaymentRequest r, FinanceService s) => s.Pay(id, r));
        api.MapPost("/expenses/{id:int}/cancel", (int id, CancelRequest r, FinanceService s) => s.CancelExpense(id, r));
        api.MapGet("/owner-movements", ([AsParameters] ReportFilter f, AppDbContext db, ReportService s, int page = 1, int pageSize = 25) =>
        {
            var (from, to) = s.Range(f);
            var rows = db.CashEntries.AsNoTracking().Where(x => (x.Kind == CashKind.Contribution || x.Kind == CashKind.Withdrawal) && x.Date >= from && x.Date <= to).ToList();
            if (f.ChannelId.HasValue || f.CategoryId.HasValue) rows = [];
            if (f.PaymentMethodId.HasValue) rows = rows.Where(x => x.PaymentMethodId == f.PaymentMethodId).ToList();
            return ReportService.Paginate(rows.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToList(), page, pageSize);
        });
        api.MapPost("/owner-movements", (OwnerMovementRequest r, FinanceService s) => { var x = s.CreateOwnerMovement(r); return Results.Created($"/api/v1/owner-movements/{x.Id}", x); });
        api.MapGet("/owner-movements/{id:int}", (int id, AppDbContext db) => db.CashEntries.AsNoTracking().SingleOrDefault(x => x.Id == id && (x.Kind == CashKind.Contribution || x.Kind == CashKind.Withdrawal)) is { } x ? Results.Ok(x) : Results.NotFound());
        api.MapPost("/owner-movements/{id:int}/cancel", (int id, CancelRequest r, FinanceService s) => s.CancelOwnerMovement(id, r));
        api.MapGet("/dashboard", ([AsParameters] ReportFilter f, ReportService s) => s.Dashboard(f));
        api.MapGet("/daily-summary", (DateOnly date, ReportService s) => s.Dashboard(new ReportFilter(date, date)));
        api.MapGet("/ledger", ([AsParameters] ReportFilter f, ReportService s, int page = 1, int pageSize = 25) => ReportService.Paginate(s.Ledger(f), page, pageSize));
        api.MapGet("/exports/ledger.csv", ([AsParameters] ReportFilter f, ReportService s) => Csv(s.LedgerCsv(f), "movimentacoes.csv"));
        api.MapGet("/exports/summary.csv", ([AsParameters] ReportFilter f, ReportService s) => Csv(s.SummaryCsv(f), "resumo.csv"));
    }
    static IResult Csv(string text, string name) => Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray(), "text/csv; charset=utf-8", name);
}
