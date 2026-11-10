using ExpoReturn.Domain.Rules;

namespace ExpoReturn.Domain;

public static class ClosureDocument
{
    /// <summary>Clasifica la referencia de "Customs Export". Textos libres ("scrap marzo", "sc1") NO cierran.</summary>
    public static ClosureDocumentType Classify(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return ClosureDocumentType.None;
        var s = reference.Trim();
        if (ClosureDocumentRx.Export().IsMatch(s)) return ClosureDocumentType.ExportPedimento;
        if (ClosureDocumentRx.F4().IsMatch(s)) return ClosureDocumentType.CambioRegimenF4;
        if (ClosureDocumentRx.V1().IsMatch(s)) return ClosureDocumentType.PedimentoVirtualV1;
        return ClosureDocumentType.Unrecognized;
    }

    public static bool Closes(ClosureDocumentType t) =>
        t is ClosureDocumentType.ExportPedimento or ClosureDocumentType.CambioRegimenF4 or ClosureDocumentType.PedimentoVirtualV1;

    public static string Describe(ClosureDocumentType t) => t switch
    {
        ClosureDocumentType.None => "No document",
        ClosureDocumentType.ExportPedimento => "Export (PRyy-#####)",
        ClosureDocumentType.CambioRegimenF4 => "Regime change F4 (duty paid)",
        ClosureDocumentType.PedimentoVirtualV1 => "Virtual pedimento V1 (domestic shipment)",
        _ => "Unrecognized text (does not close)",
    };
}
