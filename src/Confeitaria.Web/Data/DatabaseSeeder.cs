using Confeitaria.Web.Contracts;
using Confeitaria.Web.Domain;
using Confeitaria.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Confeitaria.Web.Data;

public static class DatabaseSeeder
{
    public static void Initialize(AppDbContext db)
    {
        db.Database.Migrate();
        if (!db.Settings.Any()) db.Settings.Add(new BusinessSettings { Id = 1, OpeningDate = new(BusinessClock.Today.Year, BusinessClock.Today.Month, 1) });
        if (!db.Categories.Any()) db.Categories.AddRange(
            new Category { Name = "Despesas operacionais", Kind = ExpenseKind.Operating },
            new Category { Name = "Compra de insumos", Kind = ExpenseKind.Supplies },
            new Category { Name = "Rateio já incluído nas receitas", Kind = ExpenseKind.AllocatedCost });
        if (!db.Channels.Any()) db.Channels.AddRange(new[] { "WhatsApp", "Pronta entrega", "iFood", "Aiqfome" }.Select(n => new Channel { Name = n }));
        if (!db.PaymentMethods.Any()) db.PaymentMethods.AddRange(new[] { "Dinheiro", "Pix", "Cartão" }.Select(n => new PaymentMethod { Name = n }));
        db.SaveChanges();
    }
    public static void Demo(AppDbContext db, FinanceService service)
    {
        if (db.Sales.Any() || db.Expenses.Any() || db.CashEntries.Any()) return;
        var date = BusinessClock.Today;
        service.UpdateSettings(new("Confeitaria demonstrativa", new(date.Year, date.Month, 1), 30000, 20000));
        var channel = db.Channels.OrderBy(x => x.Id).First();
        var pix = db.PaymentMethods.OrderBy(x => x.Id).First();
        // Explicitly fictional fees: these are not supplier prices.
        service.SaveChannel(new(channel.Name, 1000, 200), channel.Id);
        var sale = service.CreateSale(new(date, 10000, channel.Id, pix.Id, 1000, 3000, Description: "Venda fictícia com desconto"));
        service.Receive(sale.Id, new(date));
        service.CreateSale(new(date, 5000, channel.Id, pix.Id, DueDate: date.AddDays(3), Description: "Venda fictícia sem custo e a receber"));
        var suppliesCategory = db.Categories.OrderBy(x => x.Id).FirstOrDefault(x => x.Kind == ExpenseKind.Supplies)
            ?? service.SaveCategory(new("Insumos demonstrativos", ExpenseKind.Supplies));
        var operatingCategory = db.Categories.OrderBy(x => x.Id).FirstOrDefault(x => x.Kind == ExpenseKind.Operating)
            ?? service.SaveCategory(new("Operação demonstrativa", ExpenseKind.Operating));
        var supplies = service.CreateExpense(new(date, 2000, suppliesCategory.Id, PaymentMethodId: pix.Id, Description: "Compra fictícia de ingredientes"));
        service.Pay(supplies.Id, new(date, pix.Id));
        service.CreateExpense(new(date, 1000, operatingCategory.Id, date.AddDays(2), Description: "Despesa fictícia a pagar"));
        service.CreateOwnerMovement(new(date, CashKind.Withdrawal, 1000, "Retirada fictícia", pix.Id));
    }
}
