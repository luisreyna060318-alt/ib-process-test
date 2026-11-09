using System.Text.RegularExpressions;

namespace ExpoReturn.Domain.Rules;

/// <summary>Normalización y validación de identificadores (P/N, HU, lote, factura, PO).</summary>
public static partial class Identifiers
{
    [GeneratedRegex(@"^[0-9A-Z]{4}\.[0-9A-Z]{3}\.[0-9A-Z]{3}-[0-9A-Z]{2,3}$")]
    private static partial Regex MaterialRx();
    [GeneratedRegex(@"^\d{10}$")] private static partial Regex HuRx();
    [GeneratedRegex(@"^[A-Z]{2}\d{8}$")] private static partial Regex BatchRx();
    [GeneratedRegex(@"^MPS\d{8}$")] private static partial Regex InvoiceRx();
    [GeneratedRegex(@"^\d{8}(\s*-\s*\d{1,5})?$")] private static partial Regex PoRx();
    [GeneratedRegex(@"^\d{10}$")] private static partial Regex GrRx();

    /// <summary>
    /// '0203306644-250' -> '0203.306.644-250' · 'F005V04580-5ZS' -> 'F005.V04.580-5ZS'
    /// '020330704824W' -> '0203.307.048-24W' · 'F00H.M00.196.2ZB' -> 'F00H.M00.196-2ZB' · minúsculas -> mayúsculas.
    /// (Variantes encontradas en el Excel real.)
    /// </summary>
    public static string? NormalizeMaterial(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToUpperInvariant().Replace(" ", "");
        var core = s.Replace(".", "").Replace("-", "");
        if (core.Length is 12 or 13 && core.All(char.IsLetterOrDigit))
            return $"{core[..4]}.{core.Substring(4, 3)}.{core.Substring(7, 3)}-{core[10..]}";
        return s;
    }

    public static bool IsValidMaterial(string? pn) => pn is not null && MaterialRx().IsMatch(pn);
    public static bool IsHandlingUnit(string? v) => v is not null && HuRx().IsMatch(v.Trim());
    public static bool IsBatch(string? v) => v is not null && BatchRx().IsMatch(v.Trim().ToUpperInvariant());
    public static bool IsInvoice(string? v) => v is not null && InvoiceRx().IsMatch(v.Trim().ToUpperInvariant());
    public static bool IsPurchaseOrder(string? v) => v is not null && PoRx().IsMatch(v.Trim());
    public static bool IsGrNumber(string? v) => v is not null && GrRx().IsMatch(v.Trim());

    /// <summary>Año embebido en la factura: MPS<b>26</b>000009 -> 2026.</summary>
    public static int? InvoiceYear(string? invoice) =>
        IsInvoice(invoice) ? 2000 + int.Parse(invoice!.Trim().Substring(3, 2)) : null;
}

public static partial class ClosureDocumentRx
{
    [GeneratedRegex(@"^PR\d{2}-\d{4,5}$", RegexOptions.IgnoreCase)] internal static partial Regex Export();
    [GeneratedRegex(@"^F4\b", RegexOptions.IgnoreCase)] internal static partial Regex F4();
    [GeneratedRegex(@"^V1\b", RegexOptions.IgnoreCase)] internal static partial Regex V1();
}
