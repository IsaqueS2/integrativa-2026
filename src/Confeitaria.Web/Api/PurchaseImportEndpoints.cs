using System.Text.Json;
using Confeitaria.Web.Contracts;
using Confeitaria.Web.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Confeitaria.Web.Api;

public static class PurchaseImportEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapPurchaseImports(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/purchase-imports");
        api.MapPost("/preview", async Task<Results<ProblemHttpResult, Ok<PurchaseImportPreview>>> (
            IFormFile file, HttpRequest request, PurchaseImportService service,
            CancellationToken cancellationToken) =>
        {
            if (!IsSameOrigin(request)) return TypedResults.Problem(statusCode: 403, title: "Origem da requisição não permitida.");
            var preview = await service.PreviewAsync(file, cancellationToken);
            return TypedResults.Ok(preview);
        })
            .WithName("PreviewPurchaseDocument")
            .WithSummary("Extrai itens de uma nota de compra para revisão")
            .WithDescription("Lê XML de NF-e, PDF e imagens localmente. Os dados só são gravados após confirmação.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<PurchaseImportPreview>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithMetadata(new RequestSizeLimitAttribute(PurchaseImportService.MaxUploadRequestBytes),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = PurchaseImportService.MaxUploadRequestBytes })
            .DisableAntiforgery();

        api.MapPost("/commit", async Task<Results<ProblemHttpResult, Created<PurchaseImportResponse>>> (
            HttpRequest request, PurchaseImportService service, CancellationToken cancellationToken) =>
        {
            if (!IsSameOrigin(request)) return TypedResults.Problem(statusCode: 403, title: "Origem da requisição não permitida.");
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file") ?? throw new DomainException("Anexe o mesmo documento revisado na prévia.");
            PurchaseImportCommitRequest details;
            try
            {
                details = JsonSerializer.Deserialize<PurchaseImportCommitRequest>(form["request"].ToString(), JsonOptions)
                    ?? throw new JsonException("Dados da revisão ausentes.");
            }
            catch (JsonException)
            {
                throw new DomainException("Os dados revisados da importação estão inválidos.");
            }
            var result = await service.CommitAsync(file, details, cancellationToken);
            return TypedResults.Created($"/api/v1/expenses/{result.ExpenseId}", result);
        })
            .WithName("CommitPurchaseDocument")
            .WithSummary("Confirma a importação de uma nota de compra")
            .WithDescription("Cria uma despesa de insumos, atualiza os ingredientes e incrementa o estoque em uma transação.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<PurchaseImportResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithMetadata(new RequestSizeLimitAttribute(PurchaseImportService.MaxUploadRequestBytes),
                new RequestFormLimitsAttribute { MultipartBodyLengthLimit = PurchaseImportService.MaxUploadRequestBytes })
            .DisableAntiforgery();
    }

    private static bool IsSameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin)) return true;
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}
