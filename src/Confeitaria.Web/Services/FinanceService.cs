using Confeitaria.Web.Contracts;
using Confeitaria.Web.Data;
using Confeitaria.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Confeitaria.Web.Services;

public class FinanceService(AppDbContext db, IServiceProvider services)
{
    public BusinessSettings Settings() => db.Settings.Single();
    public BusinessSettings UpdateSettings(SettingsRequest r)
    {
        Rules.Date(r.OpeningDate); Rules.Money(r.OpeningBalanceCents, true); Rules.Money(r.ReserveCents, true);
        var s = Settings();
        if (s.OpeningDate != r.OpeningDate || s.OpeningBalanceCents != r.OpeningBalanceCents)
            Rules.Require(!db.Sales.Any() && !db.Expenses.Any() && !db.CashEntries.Any(), "A abertura não pode ser alterada após lançamentos. Registre um aporte ou retirada para ajustar o caixa.", 409);
        s.Name = Rules.Name(r.Name); s.OpeningDate = r.OpeningDate; s.OpeningBalanceCents = r.OpeningBalanceCents; s.ReserveCents = r.ReserveCents;
        db.SaveChanges(); return s;
    }
    public Category SaveCategory(CategoryRequest r, int? id = null)
    {
        Rules.Require(Enum.IsDefined(r.Kind), "Tipo de categoria inválido.");
        var c = id.HasValue ? db.Categories.Find(id.Value) ?? throw new DomainException("Categoria não encontrada.", 404) : new Category();
        c.Name = Rules.Name(r.Name); c.Kind = r.Kind;
        if (!id.HasValue) db.Categories.Add(c);
        db.SaveChanges(); return c;
    }
    public Channel SaveChannel(FeeCatalogRequest r, int? id = null)
    {
        ValidateFee(r); var c = id.HasValue ? db.Channels.Find(id.Value) ?? throw new DomainException("Canal não encontrado.", 404) : new Channel();
        c.Name = Rules.Name(r.Name); c.RateBasisPoints = r.RateBasisPoints; c.FixedFeeCents = r.FixedFeeCents;
        if (!id.HasValue) db.Channels.Add(c);
        db.SaveChanges(); return c;
    }
    public PaymentMethod SavePaymentMethod(FeeCatalogRequest r, int? id = null)
    {
        ValidateFee(r); var c = id.HasValue ? db.PaymentMethods.Find(id.Value) ?? throw new DomainException("Meio de pagamento não encontrado.", 404) : new PaymentMethod();
        c.Name = Rules.Name(r.Name); c.RateBasisPoints = r.RateBasisPoints; c.FixedFeeCents = r.FixedFeeCents;
        if (!id.HasValue) db.PaymentMethods.Add(c);
        db.SaveChanges(); return c;
    }
    static void ValidateFee(FeeCatalogRequest r)
    { Rules.Require(r.RateBasisPoints is >= 0 and <= 10000, "Taxa deve estar entre 0 e 10000 pontos-base."); Rules.Money(r.FixedFeeCents, true); }
    public static long CalculateFee(long gross, long discount, int basisPoints, long fixedCents)
    {
        Rules.Money(gross); Rules.Money(discount, true); Rules.Require(discount <= gross, "Desconto maior que o valor bruto.");
        Rules.Require(basisPoints is >= 0 and <= 10000, "Soma de percentuais inválida."); Rules.Money(fixedCents, true);
        var fee = checked(Rules.Round((gross - discount) * (decimal)basisPoints / 10000m) + fixedCents);
        Rules.Require(fee <= gross - discount, "As taxas superam o valor após desconto."); return fee;
    }
    public Sale CreateSale(SaleRequest r)
    {
        Rules.Date(r.Date); Rules.Require(r.Date >= Settings().OpeningDate, "Venda anterior à abertura do negócio.");
        Rules.Money(r.GrossCents); Rules.Money(r.DiscountCents, true);
        Rules.Require(r.DiscountCents <= r.GrossCents, "Desconto maior que a venda.");
        var channel = db.Channels.Find(r.ChannelId) ?? throw new DomainException("Canal não encontrado.", 404);
        var method = db.PaymentMethods.Find(r.PaymentMethodId) ?? throw new DomainException("Pagamento não encontrado.", 404);
        var rate = channel.RateBasisPoints + method.RateBasisPoints;
        var fixedFee = checked(channel.FixedFeeCents + method.FixedFeeCents);
        long fee;
        if (r.FeeOverrideCents.HasValue)
        { fee = r.FeeOverrideCents.Value; Rules.Money(fee, true); Rules.Require(fee <= r.GrossCents - r.DiscountCents, "Taxa efetiva maior que a venda."); }
        else fee = CalculateFee(r.GrossCents, r.DiscountCents, rate, fixedFee);
        long? cost = r.DirectCostCents; string? snapshot = null;
        Rules.Require(!(r.RecipeId.HasValue && cost.HasValue), "Escolha custo manual ou receita, nunca os dois.");
        if (r.RecipeId.HasValue)
        {
            var provider = services.GetService<IRecipeCostProvider>() ?? throw new DomainException("Módulo de receitas indisponível.");
            var calculated = provider.GetSaleCost(r.RecipeId.Value, r.RecipeUnits); cost = calculated.TotalCostCents; snapshot = calculated.SnapshotJson;
        }
        if (cost.HasValue) Rules.Money(cost.Value, true);
        var due = r.DueDate ?? r.Date; Rules.Require(due >= r.Date && due.Year <= 2100, "Previsão de recebimento inválida.");
        var sale = new Sale { Date = r.Date, DueDate = due, ChannelId = r.ChannelId, PaymentMethodId = r.PaymentMethodId,
            GrossCents = r.GrossCents, DiscountCents = r.DiscountCents, EstimatedFeeCents = fee,
            RateBasisPointsSnapshot = rate, FixedFeeCentsSnapshot = fixedFee, DirectCostCents = cost,
            RecipeId = r.RecipeId, CostSnapshotJson = snapshot, Description = Rules.Description(r.Description), Consolidated = r.Consolidated };
        db.Sales.Add(sale); db.SaveChanges(); return sale;
    }
    public Sale Receive(int id, ReceiptRequest r)
    {
        using var tx = db.Database.BeginTransaction();
        var sale = db.Sales.Find(id) ?? throw new DomainException("Venda não encontrada.", 404);
        Rules.Require(sale.CancelledOn == null, "Venda cancelada não pode ser recebida.", 409);
        var fee = r.ActualFeeCents ?? sale.EstimatedFeeCents;
        Rules.Money(fee, true); Rules.Require(fee <= sale.GrossCents - sale.DiscountCents, "Taxa maior que a venda.");
        if (sale.ReceivedOn != null)
        { Rules.Require(sale.ReceivedOn == r.Date && sale.ActualFeeCents == fee, "Recebimento já confirmado com outros valores.", 409); return sale; }
        ValidateCashDate(r.Date, sale.Date);
        sale.ActualFeeCents = fee; sale.ReceivedOn = r.Date;
        db.CashEntries.Add(new CashEntry { Date = r.Date, Kind = CashKind.Receipt, AmountCents = sale.NetCents, SaleId = id, PaymentMethodId = sale.PaymentMethodId, Description = sale.Description });
        db.SaveChanges(); tx.Commit(); return sale;
    }
    public Sale CancelSale(int id, CancelRequest r)
    {
        using var tx = db.Database.BeginTransaction();
        var sale = db.Sales.Find(id) ?? throw new DomainException("Venda não encontrada.", 404);
        if (sale.CancelledOn != null) { RequireSameCancellation(sale.CancelledOn, sale.CancellationReason, r); return sale; }
        ValidateCashDate(r.Date, sale.ReceivedOn ?? sale.Date);
        sale.CancellationReason = Rules.Name(r.Reason); sale.CancelledOn = r.Date;
        var entry = db.CashEntries.SingleOrDefault(x => x.SaleId == id);
        if (entry != null) { entry.ReversedOn = r.Date; entry.ReversalReason = sale.CancellationReason; }
        db.SaveChanges(); tx.Commit(); return sale;
    }
    public Expense CreateExpense(ExpenseRequest r)
    {
        Rules.Date(r.Date); Rules.Require(r.Date >= Settings().OpeningDate, "Despesa anterior à abertura."); Rules.Money(r.AmountCents);
        var category = db.Categories.Find(r.CategoryId) ?? throw new DomainException("Categoria não encontrada.", 404);
        ValidatePaymentMethod(r.PaymentMethodId);
        var due = r.DueDate ?? r.Date; Rules.Require(due >= r.Date && due.Year <= 2100, "Vencimento inválido.");
        var expense = new Expense { Date = r.Date, DueDate = due, AmountCents = r.AmountCents, CategoryId = r.CategoryId,
            KindSnapshot = category.Kind, PaymentMethodId = r.PaymentMethodId, Description = Rules.Description(r.Description) };
        db.Expenses.Add(expense); db.SaveChanges(); return expense;
    }
    public Expense Pay(int id, PaymentRequest r)
    {
        using var tx = db.Database.BeginTransaction();
        var expense = db.Expenses.Find(id) ?? throw new DomainException("Despesa não encontrada.", 404);
        Rules.Require(expense.CancelledOn == null, "Despesa cancelada não pode ser paga.", 409);
        var method = r.PaymentMethodId ?? expense.PaymentMethodId; ValidatePaymentMethod(method);
        if (expense.PaidOn != null)
        { Rules.Require(expense.PaidOn == r.Date && expense.PaymentMethodId == method, "Pagamento já confirmado com outros dados.", 409); return expense; }
        ValidateCashDate(r.Date, expense.Date); expense.PaidOn = r.Date; expense.PaymentMethodId = method;
        db.CashEntries.Add(new CashEntry { Date = r.Date, Kind = CashKind.Payment, AmountCents = expense.AmountCents, ExpenseId = id, PaymentMethodId = method, Description = expense.Description });
        db.SaveChanges(); tx.Commit(); return expense;
    }
    public Expense CancelExpense(int id, CancelRequest r)
    {
        using var tx = db.Database.BeginTransaction();
        var expense = db.Expenses.Find(id) ?? throw new DomainException("Despesa não encontrada.", 404);
        if (expense.CancelledOn != null) { RequireSameCancellation(expense.CancelledOn, expense.CancellationReason, r); return expense; }
        ValidateCashDate(r.Date, expense.PaidOn ?? expense.Date);
        expense.CancelledOn = r.Date; expense.CancellationReason = Rules.Name(r.Reason);
        var entry = db.CashEntries.SingleOrDefault(x => x.ExpenseId == id);
        if (entry != null) { entry.ReversedOn = r.Date; entry.ReversalReason = expense.CancellationReason; }
        db.SaveChanges(); tx.Commit(); return expense;
    }
    public CashEntry CreateOwnerMovement(OwnerMovementRequest r)
    {
        Rules.Require(r.Kind is CashKind.Contribution or CashKind.Withdrawal, "Use Contribution ou Withdrawal.");
        Rules.Money(r.AmountCents); ValidateCashDate(r.Date, Settings().OpeningDate); ValidatePaymentMethod(r.PaymentMethodId);
        var entry = new CashEntry { Date = r.Date, Kind = r.Kind, AmountCents = r.AmountCents, PaymentMethodId = r.PaymentMethodId, Description = Rules.Description(r.Description) };
        db.CashEntries.Add(entry); db.SaveChanges(); return entry;
    }
    public CashEntry CancelOwnerMovement(int id, CancelRequest r)
    {
        using var tx = db.Database.BeginTransaction();
        var e = db.CashEntries.Find(id) ?? throw new DomainException("Movimento não encontrado.", 404);
        Rules.Require(e.Kind is CashKind.Contribution or CashKind.Withdrawal, "Estorne a venda ou despesa de origem.");
        if (e.ReversedOn != null) { RequireSameCancellation(e.ReversedOn, e.ReversalReason, r); return e; }
        ValidateCashDate(r.Date, e.Date); e.ReversedOn = r.Date; e.ReversalReason = Rules.Name(r.Reason);
        db.SaveChanges(); tx.Commit(); return e;
    }
    void ValidateCashDate(DateOnly date, DateOnly minimum)
    { Rules.Date(date); Rules.Require(date >= minimum && date >= Settings().OpeningDate, "Data anterior à origem ou ao marco de abertura."); }
    void ValidatePaymentMethod(int? id)
    { if (id.HasValue) Rules.Require(db.PaymentMethods.Any(x => x.Id == id), "Meio de pagamento não encontrado.", 404); }
    static void RequireSameCancellation(DateOnly? date, string? reason, CancelRequest r)
    { var normalizedReason = Rules.Name(r.Reason); Rules.Require(date == r.Date && reason == normalizedReason, "Estorno já registrado com outros dados.", 409); }
}
