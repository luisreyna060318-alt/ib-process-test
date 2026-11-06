using ExpoReturn.Application;
using ExpoReturn.Domain;

namespace ExpoReturn.Infrastructure.InMemory;

/// <summary>
/// Datos de demostración SINTÉTICOS (no son registros reales). Cubren cada estatus del flujo,
/// incluidos los casos que el Excel no detectaba (falso cierre, faltantes, scrap sin confirmar).
/// </summary>
public static class SampleData
{
    private enum Scenario { Draft, Requested, Imported, InRework, PendingExport, PendingHse, Exported, ExportedWithScrap, Scrapped, Missing, AgingCritical, ClosedF4, PendingExportOld }

    private static readonly (string Pn, decimal Cost, string Desc)[] Materials =
    {
        ("0265.957.012-24W", 23.5871m, "ECU airbag"),
        ("0261.S10.8CU-6DY", 84.3378m, "Control unit"),
        ("F00H.J04.154-24W", 20.6467m, "Sensor module"),
        ("0203.306.644-250", 29.9981m, "Control unit"),
        ("0285.500.708-24W", 1.2677m, "Sensor"),
        ("0273.010.645-24W", 46.1200m, "Radar sensor"),
        ("F005.V04.580-5ZS", 12.8400m, "Ultrasonic sensor"),
    };

    private static readonly string[] Requestors = { "Montaño, Sofía", "Quiroga, Tomás", "Bermúdez, Elena", "Cisneros, Marco", "Olvera, Rubén", "Treviño, Lucía" };
    private static readonly string[] Depts = { "MFQ4", "LOP2", "MFQ7", "MFQ9", "PUQ", "LOP1" };

    public static void Seed(IPalletReturnRepository repo, IAuditTrail audit, IChampionsDirectory champions, IClock clock)
    {
        var today = clock.Today;
        var scenarios = Enum.GetValues<Scenario>();
        var vsList = new[] { 1, 4, 5, 7, 9 };
        var list = new List<PalletReturn>();
        var folio = new Dictionary<int, int>();
        var n = 0;
        for (var round = 0; round < 3; round++)
        {
            foreach (var sc in scenarios)
            {
                if (round == 2 && n % 2 == 1) { n++; continue; }      // ~32 registros
                var vs = vsList[n % vsList.Length];
                folio[vs] = folio.GetValueOrDefault(vs) + 1;
                var p = Build(sc, vs, folio[vs], n, today, champions);
                PalletEvaluator.Apply(p, CalcParameters.Default, today);
                list.Add(p);
                n++;
            }
        }
        repo.ReplaceAll(list);

        foreach (var p in repo.GetAll())
        {
            audit.Record(new[]
            {
                new AuditEntry(p.Id, p.FolioCode, "Registro", null, "Alta de solicitud", p.CreatedBy, p.CreatedAtUtc, null),
            });
            if (p.ImportDateElpJup1 is not null)
                audit.Record(new[] { new AuditEntry(p.Id, p.FolioCode, nameof(PalletReturn.ImportDateElpJup1), null,
                    p.ImportDateElpJup1.Value.ToString("yyyy-MM-dd"), GsUser(champions, p.ValueStreamId), p.ImportDateElpJup1.Value.ToUniversalTime().AddHours(10), null) });
        }
    }

    private static string GsUser(IChampionsDirectory champions, int vs) =>
        ChampionsDirectory.DemoNtUser(champions.All().FirstOrDefault(c => c.Role == RoleCode.Gs && c.ValueStreamId == vs && !c.IsPlaceholder)?.Name ?? "gs osd43");

    private static PalletReturn Build(Scenario sc, int vs, int folio, int n, DateTime today, IChampionsDirectory champions)
    {
        var m = Materials[n % Materials.Length];
        var qty = new[] { 48, 96, 168, 240, 288, 528, 576, 960, 1440 }[n % 9];
        var age = sc switch
        {
            Scenario.Draft or Scenario.Requested => 0,
            Scenario.AgingCritical => 525,          // vence en ~15 días (plazo provisional 540)
            Scenario.PendingExportOld => 40,
            Scenario.Imported => 3 + n % 4,
            Scenario.InRework => 8 + n % 10,
            _ => 20 + (n * 13) % 200,
        };
        var cross = today.AddDays(-age);
        var qmm = champions.QmmFor(vs).FirstOrDefault() ?? "QMM VS" + vs;
        var p = new PalletReturn
        {
            ValueStreamId = vs,
            FolioNumber = folio,
            QmmResponsible = qmm,
            RequestingDepartment = Depts[n % Depts.Length],
            RequestorName = Requestors[n % Requestors.Length],
            ReasonId = (n % 10) + 1,
            ReturnReasonDetail = sc == Scenario.Missing ? "Contaminación en conector, sorteo 100%" : "Revisión solicitada por cliente",
            ReturnedMaterial = m.Pn,
            UnitCostUsd = n % 11 == 10 ? null : m.Cost,
            UnitCostSource = n % 11 == 10 ? "PENDING" : "MM60",
            QtyImported = qty,
            OriginalHU = (2107100000 + n * 37).ToString(),
            OriginalBatch = $"AA{23700000 + n * 11}",
            CreatedAtUtc = cross.AddDays(-3).ToUniversalTime(),
            CreatedBy = ChampionsDirectory.DemoNtUser(qmm),
        };
        if (sc == Scenario.Draft) { p.ReasonId = null; p.RequestorName = null; return p; }

        p.DateReturnedToWhs = cross.AddDays(-2);
        if (sc == Scenario.Requested) return p;

        p.ImportDate = cross;
        p.ImportInvoice = $"MPS{cross:yy}{(200 + n):000000}";
        p.ImportDateElpJup1 = cross;
        p.ReturnPONumber = (49845000 + n * 7).ToString();
        p.DeliveryNumber = (84012000 + n).ToString();
        p.DutyPaymentStatus = DutyPaymentStatus.NotRequired;
        if (sc == Scenario.Imported || sc == Scenario.AgingCritical) return p;

        var reflash = Catalogs.FindReason(p.ReasonId)!.RequiresNewMaterial;
        p.IbOrderTypeCode = "PPI2";
        p.IbOrderNumber = (5100000 + n).ToString();
        p.NewMaterial = reflash ? Materials[(n + 1) % Materials.Length].Pn : p.ReturnedMaterial;
        p.NewHU = (2106600000 + n * 41).ToString();
        p.ReworkBatch = p.OriginalBatch;
        if (sc == Scenario.InRework) { p.ScrapQty = qty / 48; p.ScrapDate = cross.AddDays(2); return p; }

        var scrap = sc is Scenario.ExportedWithScrap or Scenario.PendingHse ? Math.Max(1, qty / 96) : 0;
        if (sc == Scenario.Scrapped) scrap = qty;
        var old = qty - scrap - (sc == Scenario.Missing ? 1 : 0);
        p.ReinsertionQty = sc == Scenario.Exported && n % 3 == 0 ? 24 : 0;
        p.OldMaterialDeliveredQty = old;
        p.PhysicalDeliveredQty = old + (p.ReinsertionQty ?? 0);
        p.PhysicalDeliveredDate = cross.AddDays(3);
        p.DateSentBack = cross.AddDays(3);
        if (scrap > 0) { p.ScrapQty = scrap; p.ScrapDate = cross.AddDays(2); }
        if (sc == Scenario.Missing)
        {
            p.DeclaredMissing = true;
            p.StatusCategoryCode = "NO_REPORTADO_CONO_VERDE";
            p.GreenConeReported = false;
        }

        if (sc is Scenario.PendingExport or Scenario.PendingExportOld) return p;
        if (old > 0)
        {
            p.LowExportNumber = 1 + n % 5;
            p.LowExportDate = cross.AddDays(4);
            p.LowExportNote = $"Incluido en expo #{p.LowExportNumber} del día {cross.AddDays(4):dd/MM/yyyy}";
            p.CustomsExportReference = sc == Scenario.ClosedF4 ? "F4" : $"PR{cross:yy}-{(100 + n):00000}";
            p.CustomsExportDate = cross.AddDays(5);
        }
        if (scrap > 0 && sc != Scenario.PendingHse)
        {
            p.ScrapGrNumber = (4951422000 + n).ToString();
            p.HseScrapKg = Math.Round(scrap * 0.35m, 1);
            p.HseConfirmationDate = cross.AddDays(6);
        }
        return p;
    }
}
