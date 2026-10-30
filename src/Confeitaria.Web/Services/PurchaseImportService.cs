using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Confeitaria.Web.Contracts;
using Confeitaria.Web.Data;
using Confeitaria.Web.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TesseractOCR;
using TesseractOCR.Enums;
using TesseractOCR.Exceptions;
using PixImage = TesseractOCR.Pix.Image;
using PdfDocument = UglyToad.PdfPig.PdfDocument;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Rendering.Skia;
using SkiaSharp;

namespace Confeitaria.Web.Services;

public sealed class PurchaseImportService(AppDbContext db, FinanceService finance, IWebHostEnvironment environment)
{
    private const long MaxFileBytes = 10 * 1024 * 1024;
    public const long MaxUploadRequestBytes = 11 * 1024 * 1024;
    private const int MaxPdfPages = 20;
    private static readonly SemaphoreSlim OcrLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex TextItemPattern = new(
        @"^\s*(?<name>.+?)\s+(?<unit>UNID|UND|UN|KG|KGS|G|ML|LT|L|CX|PCT|PC)\s+(?<quantity>[\d.,]+)\s+(?<unitPrice>[\d.,]+)\s+(?<total>[\d.,]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex AccessKeyPattern = new(@"(?<!\d)\d{44}(?!\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PackSizePattern = new(
        @"(?<quantity>\d+(?:[.,]\d+)?)\s*(?<unit>KG|G|ML|LT|L)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<PurchaseImportPreview> PreviewAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var (bytes, fileName) = await ReadFileAsync(file, cancellationToken);
        var parsed = await ParseFileAsync(bytes, fileName, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var alreadyImported = db.PurchaseDocuments.Any(x => x.DocumentHash == hash ||
            parsed.AccessKey != null && x.AccessKey == parsed.AccessKey);
        var ingredients = db.Ingredients.AsNoTracking().ToList();
        var items = parsed.Items.Select(item =>
        {
            var ingredient = ingredients.SingleOrDefault(x => string.Equals(x.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            return new PurchaseImportLinePreview(item.Name, item.Quantity, item.Unit, item.TotalCents, ingredient?.Id);
        }).ToList();
        return new PurchaseImportPreview(hash, fileName, parsed.Format, parsed.AccessKey, parsed.InvoiceNumber,
            parsed.Supplier, parsed.PurchaseDate, parsed.TotalCents, items, alreadyImported);
    }

    public async Task<PurchaseImportResponse> CommitAsync(
        IFormFile file,
        PurchaseImportCommitRequest request,
        CancellationToken cancellationToken)
    {
        var (bytes, _) = await ReadFileAsync(file, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Rules.Require(string.Equals(hash, request.DocumentHash, StringComparison.OrdinalIgnoreCase),
            "O arquivo mudou desde a prévia. Anexe o documento novamente para conferir os dados.");
        Rules.Require(request.DocumentHash.Length == 64 && request.DocumentHash.All(Uri.IsHexDigit),
            "Identificador do documento inválido.");
        Rules.Date(request.PurchaseDate);
        Rules.Money(request.TotalCents);
        var supplier = Rules.Name(request.Supplier);
        var accessKey = string.IsNullOrWhiteSpace(request.AccessKey) ? null : request.AccessKey.Trim();
        Rules.Require(accessKey == null || accessKey.Length == 44 && accessKey.All(char.IsDigit),
            "A chave da nota fiscal deve conter 44 dígitos.");
        var invoiceNumber = string.IsNullOrWhiteSpace(request.InvoiceNumber) ? null : request.InvoiceNumber.Trim();
        Rules.Require((invoiceNumber?.Length ?? 0) <= 40, "O número da nota fiscal deve ter até 40 caracteres.");
        Rules.Require(request.Items is { Count: > 0 and <= 100 }, "Informe de 1 a 100 itens para importar.");
        Rules.Require(request.Items.All(x => x is not null), "Há um item inválido na importação.");
        Rules.Require(request.Items.Select(x => x.IngredientId).Where(x => x.HasValue).Distinct().Count() ==
            request.Items.Count(x => x.IngredientId.HasValue), "Selecione cada ingrediente existente apenas uma vez.");

        var lines = request.Items.Select(line =>
        {
            var name = Rules.Name(line.Name);
            Rules.Money(line.TotalCents, allowZero: true);
            var (dimension, quantityMicros) = RecipeService.ConvertQuantity(line.Quantity, line.Unit);
            return new PreparedLine(name, dimension, quantityMicros, line.TotalCents, line.IngredientId);
        }).ToList();
        Rules.Require(lines.Where(x => x.IngredientId == null)
            .Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() ==
            lines.Count(x => x.IngredientId == null), "O mesmo ingrediente aparece mais de uma vez. Agrupe os itens antes de confirmar.");

        using var transaction = db.Database.BeginTransaction();
        Rules.Require(!db.PurchaseDocuments.Any(x => x.DocumentHash == hash ||
            accessKey != null && x.AccessKey == accessKey), "Este documento já foi importado.", 409);

        var suppliesCategory = db.Categories.SingleOrDefault(x => x.Kind == ExpenseKind.Supplies)
            ?? throw new DomainException("A categoria de compra de insumos não está configurada.", 409);
        var expense = finance.CreateExpense(new ExpenseRequest(request.PurchaseDate, request.TotalCents,
            suppliesCategory.Id, DueDate: request.PurchaseDate, Description: $"Compra de insumos - {supplier}" +
            (invoiceNumber == null ? "" : $" - NF {invoiceNumber}")));

        foreach (var line in lines)
        {
            var ingredient = line.IngredientId.HasValue
                ? db.Ingredients.SingleOrDefault(x => x.Id == line.IngredientId.Value)
                    ?? throw new DomainException("Ingrediente selecionado não encontrado.", 404)
                : new Ingredient();
            Rules.Require(ingredient.Dimension == line.Dimension || line.IngredientId == null,
                $"A unidade de {ingredient.Name} não corresponde à unidade lida na nota.");
            if (line.IngredientId == null)
            {
                Rules.Require(!db.Ingredients.Any(x => x.Name.ToLower() == line.Name.ToLower()),
                    $"Já existe o ingrediente “{line.Name}”. Selecione-o na prévia.");
                ingredient.Name = line.Name;
                ingredient.Dimension = line.Dimension;
                ingredient.StockQuantityMicros = 0;
                db.Ingredients.Add(ingredient);
            }
            ingredient.PackageQuantityMicros = line.QuantityMicros;
            ingredient.PackagePriceCents = line.TotalCents;
            var stock = checked((ingredient.StockQuantityMicros ?? 0) + line.QuantityMicros);
            Rules.Require(stock <= 1_000_000_000_000, $"A quantidade em estoque de {ingredient.Name} excede o limite.");
            ingredient.StockQuantityMicros = stock;
        }

        var document = new PurchaseDocument
        {
            DocumentHash = hash,
            AccessKey = accessKey,
            InvoiceNumber = invoiceNumber,
            Supplier = supplier,
            PurchaseDate = request.PurchaseDate,
            TotalCents = request.TotalCents,
            ExpenseId = expense.Id
        };
        db.PurchaseDocuments.Add(document);
        db.SaveChanges();
        transaction.Commit();
        return new PurchaseImportResponse(document.Id, expense.Id, document.TotalCents, lines.Count);
    }

    private async Task<(byte[] Bytes, string FileName)> ReadFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        Rules.Require(file.Length is > 0 and <= MaxFileBytes, "O documento deve ter até 10 MB e não pode estar vazio.");
        var fileName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        Rules.Require(extension is ".xml" or ".pdf" or ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tif" or ".tiff" or ".webp",
            "Formato não aceito. Envie XML de NF-e, PDF ou imagem PNG, JPG, TIFF, BMP ou WebP.");
        await using var memory = new MemoryStream((int)file.Length);
        await file.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        Rules.Require(HasExpectedSignature(bytes, extension), "O conteúdo do arquivo não corresponde ao formato informado.");
        return (bytes, fileName);
    }

    private async Task<ParsedDocument> ParseFileAsync(byte[] bytes, string fileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            if (extension == ".xml")
                return ParseXml(bytes);
            if (extension == ".pdf")
                return await ParsePdfAsync(bytes, cancellationToken);
            return ParseTextDocument(await RecognizeImageAsync(bytes, cancellationToken), "Imagem com OCR");
        }
        catch (XmlException)
        {
            throw new DomainException("O XML anexado não está bem formado ou não é uma NF-e válida.");
        }
        catch (PdfDocumentFormatException)
        {
            throw new DomainException("O PDF anexado está danificado ou não pode ser lido.");
        }
    }

    private static bool HasExpectedSignature(byte[] bytes, string extension)
    {
        var span = bytes.AsSpan();
        if (extension == ".xml")
        {
            var prefix = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 256)).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            return prefix.StartsWith('<');
        }
        return extension switch
        {
            ".pdf" => span.StartsWith("%PDF-"u8),
            ".png" => span.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".jpg" or ".jpeg" => span.StartsWith(new byte[] { 255, 216, 255 }),
            ".bmp" => span.StartsWith("BM"u8),
            ".tif" or ".tiff" => span.StartsWith(new byte[] { 73, 73, 42, 0 }) ||
                span.StartsWith(new byte[] { 77, 77, 0, 42 }),
            ".webp" => span.Length >= 12 && span.StartsWith("RIFF"u8) && span[8..].StartsWith("WEBP"u8),
            _ => false
        };
    }

    private static ParsedDocument ParseXml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxFileBytes });
        var xml = XDocument.Load(reader);
        var info = xml.Descendants().FirstOrDefault(x => x.Name.LocalName == "infNFe")
            ?? throw new DomainException("XML não parece ser uma NF-e válida.");
        var productLines = info.Elements().Where(x => x.Name.LocalName == "det").Select(detail =>
        {
            var product = detail.Elements().FirstOrDefault(x => x.Name.LocalName == "prod")
                ?? throw new DomainException("Há um item sem dados do produto na nota.");
            var name = ChildValue(product, "xProd") ?? throw new DomainException("Há um item sem nome de produto na nota.");
            var quantity = ParseInvariant(ChildValue(product, "qCom"));
            var unit = ChildValue(product, "uCom") ?? "UN";
            var total = TryParseInvariant(ChildValue(product, "vProd"), out var parsedTotal)
                ? ToCents(parsedTotal)
                : ToCents(quantity * ParseInvariant(ChildValue(product, "vUnCom")));
            return PrepareImportedLine(name, quantity, unit, total);
        }).ToList();
        Rules.Require(productLines.Count is > 0 and <= 100, "A NF-e deve conter de 1 a 100 itens para importação.");
        var ide = info.Elements().FirstOrDefault(x => x.Name.LocalName == "ide");
        var emit = info.Elements().FirstOrDefault(x => x.Name.LocalName == "emit");
        var totalValue = xml.Descendants().FirstOrDefault(x => x.Name.LocalName == "ICMSTot")?
            .Elements().FirstOrDefault(x => x.Name.LocalName == "vNF")?.Value;
        var dateText = ChildValue(ide, "dhEmi", "dEmi");
        var date = ParseDate(dateText) ?? throw new DomainException("A data de emissão não foi encontrada na NF-e.");
        var accessKey = info.Attribute("Id")?.Value;
        if (accessKey?.StartsWith("NFe", StringComparison.OrdinalIgnoreCase) == true) accessKey = accessKey[3..];
        if (accessKey is not { Length: 44 } || !accessKey.All(char.IsDigit))
            accessKey = null;
        var amount = TryParseInvariant(totalValue, out var invoiceTotal)
            ? ToCents(invoiceTotal)
            : productLines.Sum(x => x.TotalCents);
        return new ParsedDocument("XML de NF-e", accessKey, ChildValue(ide, "nNF"),
            ChildValue(emit, "xNome") ?? "Fornecedor não identificado", date, amount, productLines);
    }

    private async Task<ParsedDocument> ParsePdfAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var document = PdfDocument.Open(stream, SkiaRenderingParsingOptions.Instance);
        Rules.Require(document.NumberOfPages is > 0 and <= MaxPdfPages, "O PDF deve ter entre 1 e 20 páginas.");
        var text = string.Join(Environment.NewLine, document.GetPages().Select(x => x.Text));
        var format = "PDF com texto";
        if (string.IsNullOrWhiteSpace(text))
        {
            document.AddSkiaPageFactory();
            var pages = new List<string>();
            for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var bitmap = document.GetPageAsSKBitmap(pageNumber, 1.5f, SKColors.White);
                using var imageStream = new MemoryStream();
                bitmap.Encode(imageStream, SKEncodedImageFormat.Png, 100);
                pages.Add(await RecognizeImageAsync(imageStream.ToArray(), cancellationToken));
            }
            text = string.Join(Environment.NewLine, pages);
            format = "PDF digitalizado com OCR";
        }
        var lines = ParseText(text);
        return ParseTextDocument(text, format, lines);
    }

    private async Task<string> RecognizeImageAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        var dataPath = Path.Combine(environment.ContentRootPath, "tessdata");
        Rules.Require(File.Exists(Path.Combine(dataPath, "por.traineddata")),
            "O OCR local não está configurado. Instale o arquivo de idioma português indicado no guia do projeto.", 503);
        await OcrLock.WaitAsync(cancellationToken);
        try
        {
            using var engine = new Engine(dataPath, Language.Portuguese, EngineMode.Default);
            using var image = PixImage.LoadFromMemory(bytes);
            using var page = engine.Process(image);
            return page.Text;
        }
        catch (TesseractException ex)
        {
            throw new DomainException($"Não foi possível ler o documento com OCR: {ex.Message}");
        }
        catch (LeptonicaException ex)
        {
            throw new DomainException($"A imagem anexada não pôde ser processada: {ex.Message}");
        }
        catch (DllNotFoundException ex)
        {
            throw new DomainException($"A biblioteca OCR local não está disponível: {ex.Message}", 503);
        }
        finally
        {
            OcrLock.Release();
        }
    }

    private static List<ImportedLine> ParseText(string text)
    {
        var lines = new List<ImportedLine>();
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = TextItemPattern.Match(rawLine);
            if (!match.Success ||
                !TryParseBrazilian(match.Groups["quantity"].Value, out var quantity) ||
                !TryParseBrazilian(match.Groups["total"].Value, out var total))
                continue;
            var line = PrepareImportedLine(match.Groups["name"].Value, quantity, match.Groups["unit"].Value, ToCents(total));
            if (line.Quantity > 0 && line.TotalCents >= 0) lines.Add(line);
        }
        return lines.Take(100).ToList();
    }

    private static ParsedDocument ParseTextDocument(string text, string format, List<ImportedLine>? lines = null)
    {
        lines ??= ParseText(text);
        var metadata = ParseTextMetadata(text);
        return new ParsedDocument(format, metadata.AccessKey, metadata.InvoiceNumber, metadata.Supplier,
            metadata.PurchaseDate ?? BusinessClock.Today, metadata.TotalCents ?? lines.Sum(x => x.TotalCents), lines);
    }

    private static TextMetadata ParseTextMetadata(string text)
    {
        var accessKey = AccessKeyPattern.Match(text);
        var totalMatch = Regex.Match(text,
            @"(?:VALOR\s+TOTAL\s+DA\s+NOTA|VALOR\s+TOTAL\s+NOTA|TOTAL\s+DA\s+NOTA|VALOR\s+TOTAL)\s*[:\-]?\s*R?\$?\s*(?<value>[\d.]+,\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var dateMatch = Regex.Match(text, @"(?:DATA\s+DE\s+EMISS[AÃ]O|EMISS[AÃ]O|DATA)\s*[:\-]?\s*(?<date>\d{2}/\d{2}/\d{4})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var numberMatch = Regex.Match(text, @"(?:N[º°.]?\s*|NOTA\s+FISCAL\s+N[º°.]?\s*)(?<number>\d{1,20})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return new TextMetadata(
            accessKey.Success ? accessKey.Value : null,
            numberMatch.Success ? numberMatch.Groups["number"].Value : null,
            totalMatch.Success && TryParseBrazilian(totalMatch.Groups["value"].Value, out var total) ? ToCents(total) : null,
            dateMatch.Success ? ParseDate(dateMatch.Groups["date"].Value) : null,
            "Fornecedor não identificado");
    }

    private static ImportedLine PrepareImportedLine(string name, decimal quantity, string unit, long totalCents)
    {
        var normalizedUnit = NormalizeUnit(unit);
        if (normalizedUnit == "un")
        {
            var package = PackSizePattern.Match(name);
            if (package.Success && TryParseBrazilian(package.Groups["quantity"].Value, out var packageQuantity))
            {
                var packageUnit = NormalizeUnit(package.Groups["unit"].Value);
                if (packageUnit != "un")
                {
                    normalizedUnit = packageUnit;
                    quantity = checked(quantity * packageQuantity);
                }
            }
        }
        return new ImportedLine(Rules.Name(name), quantity, normalizedUnit, totalCents);
    }

    private static string NormalizeUnit(string unit) => unit.Trim().ToUpperInvariant() switch
    {
        "G" => "g",
        "KG" or "KGS" => "kg",
        "ML" => "ml",
        "L" or "LT" => "l",
        "UN" or "UND" or "UNID" or "PC" or "PCT" or "CX" => "un",
        _ => "un"
    };

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
            return DateOnly.FromDateTime(timestamp.DateTime);
        if (DateOnly.TryParseExact(value, ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        return null;
    }

    private static string? ChildValue(XElement? element, params string[] names) =>
        element?.Elements().FirstOrDefault(x => names.Contains(x.Name.LocalName, StringComparer.Ordinal))?.Value;

    private static decimal ParseInvariant(string? value) =>
        TryParseInvariant(value, out var parsed) ? parsed : throw new DomainException("Há um valor numérico inválido no documento.");

    private static bool TryParseInvariant(string? value, out decimal parsed) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);

    private static bool TryParseBrazilian(string value, out decimal parsed)
    {
        var normalized = value.Contains(',')
            ? value.Replace(".", "", StringComparison.Ordinal).Replace(',', '.')
            : value;
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);
    }

    private static long ToCents(decimal amount)
    {
        Rules.Require(amount >= 0 && amount <= Rules.MaxMoney / 100m, "O valor do documento está fora do intervalo permitido.");
        return checked(Rules.Round(amount * 100m));
    }

    private sealed record ImportedLine(string Name, decimal Quantity, string Unit, long TotalCents);
    private sealed record PreparedLine(string Name, IngredientDimension Dimension, long QuantityMicros, long TotalCents, int? IngredientId);
    private sealed record ParsedDocument(string Format, string? AccessKey, string? InvoiceNumber, string Supplier,
        DateOnly PurchaseDate, long TotalCents, IReadOnlyList<ImportedLine> Items);
    private sealed record TextMetadata(string? AccessKey, string? InvoiceNumber, long? TotalCents,
        DateOnly? PurchaseDate, string Supplier);
}
