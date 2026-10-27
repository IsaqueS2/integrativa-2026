namespace Confeitaria.Web.Services;

public class DomainException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
public static class Rules
{
    public const long MaxMoney = 1_000_000_000_000;
    public static void Require(bool condition, string message, int status = 400)
    { if (!condition) throw new DomainException(message, status); }
    public static string Name(string? value)
    {
        Require(!string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 120, "Informe um nome com até 120 caracteres.");
        return value!.Trim();
    }
    public static string Description(string? value)
    { Require((value?.Length ?? 0) <= 500, "A descrição deve ter até 500 caracteres."); return value?.Trim() ?? ""; }
    public static void Money(long amount, bool allowZero = false)
        => Require(amount >= (allowZero ? 0 : 1) && amount <= MaxMoney, "Valor em centavos fora do intervalo permitido.");
    public static long Round(decimal cents) => checked((long)decimal.Round(cents, 0, MidpointRounding.AwayFromZero));
    public static void Date(DateOnly date) => Require(date.Year >= 2000 && date <= Domain.BusinessClock.Today, "Use uma data entre 2000 e hoje em America/Sao_Paulo.");
}
