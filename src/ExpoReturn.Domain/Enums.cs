namespace ExpoReturn.Domain;

/// <summary>Estatus del flujo. Se DERIVA de los datos (StatusResolver); nunca se captura.</summary>
public enum WorkflowStatus
{
    Draft = 1,
    Requested = 2,
    Imported = 3,
    InRework = 4,
    PendingHse = 5,
    PendingExport = 6,
    DutyRisk = 7,
    Exported = 8,
    Scrapped = 9,
    ClosedDutyPaid = 10,
}

public static class WorkflowStatusInfo
{
    public static bool IsClosed(this WorkflowStatus s) =>
        s is WorkflowStatus.Exported or WorkflowStatus.Scrapped or WorkflowStatus.ClosedDutyPaid;

    public static string DisplayName(this WorkflowStatus s) => s switch
    {
        WorkflowStatus.Draft => "Draft",
        WorkflowStatus.Requested => "Requested",
        WorkflowStatus.Imported => "Imported",
        WorkflowStatus.InRework => "In rework",
        WorkflowStatus.PendingHse => "Pending HSE",
        WorkflowStatus.PendingExport => "Pending export",
        WorkflowStatus.DutyRisk => "Duty risk",
        WorkflowStatus.Exported => "Exported",
        WorkflowStatus.Scrapped => "Scrap confirmed",
        WorkflowStatus.ClosedDutyPaid => "Closed (duty paid)",
        _ => s.ToString(),
    };
}

public enum Severity { Error, Warning, Info }

/// <summary>
/// Tipo de documento de cierre capturado en "Customs Export".
/// En el Excel real aparecen PRyy-#####, F4 y V1. [VALIDAR GS/OSD43] el tratamiento de F4/V1.
/// </summary>
public enum ClosureDocumentType
{
    None,
    ExportPedimento,      // PRyy-#####  (retorno / exportación a El Paso)
    CambioRegimenF4,      // F4          (cambio de régimen -> se asume duty pagado)
    PedimentoVirtualV1,   // V1          (transferencia / envío nacional)
    Unrecognized,
}
