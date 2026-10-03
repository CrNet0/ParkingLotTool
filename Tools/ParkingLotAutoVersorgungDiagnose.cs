using System.Collections.Generic;
using Game.Net;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        internal static void AvDiagnoseMeldung(string text)
        {
            text = "PLT-VERSORGUNG-DIAG " + text;
            ParkingLotSchrittmarke.Setze(text);
            ParkingLotSchrittmarke.Versorgungsbild(text);
            ParkingLotLiveLog.Zeile(text);
            Mod.log.Info(text);
        }

        private void AvMeldeDiagnoseschalter()
        {
            var aktiv = new List<string>();
            foreach (var s in new[] { "versorgung-graphhelfer", "versorgung-strom",
                "versorgung-wasser", "versorgung-leitungsabriss", "versorgung-kantenziel",
                "versorgung-bilddiagnose", "versorgung-ohne-zoningstart",
                "versorgung-ohne-bushalt", "bushaltestellen" })
                if (Mod.Aus(s)) aktiv.Add(s);
            AvDiagnoseMeldung("SCHALTER " + (aktiv.Count == 0 ? "0, Normalbetrieb" : string.Join(",", aktiv))
                + "; Bilddiagnose 120 PostTool-Bilder plus ModificationEnd, auch nach ESC.");
        }

        partial void AvDiagnoseEditAbriss(Entity lot, Entity traeger)
        {
            if (lot == Entity.Null && traeger == Entity.Null) return;
            var diagnose = World.GetOrCreateSystemManaged<ParkingLotVersorgungsdiagnoseSystem>();
            diagnose.MerkeEdit(lot, traeger, Mod.Aus("versorgung-leitungsabriss"));
        }

        partial void AvDiagnoseAbrissWarten(ref bool behalten)
        {
            behalten = Mod.Aus("versorgung-leitungsabriss");
            if (behalten && _avAbrissKanten.Count > 0)
                AvDiagnoseMeldung($"EDIT-ABRISS-AUS {_avAbrissKanten.Count} alte Leitungen bleiben; "
                    + "Neubau wartet nur auf den alten Traeger. Alte Leitungen sind weiter Kollisionshindernisse.");
        }

        private void AvStarteBilddiagnose(string grund)
        {
            if (!ParkingLotVersorgungsdiagnoseSystem.DiagnoseAn) return;
            var roots = new HashSet<Entity>();
            foreach (var k in _avKurse)
            {
                roots.UnionWith(k.Kanten);
                roots.UnionWith(k.Anschlussstuecke);
                roots.UnionWith(k.Trasse.Startnetz);
                roots.Add(k.Trasse.Zielkante);
            }
            // Auch Vanilla-Strassenkopien und ihre Originale festhalten: Apply
            // kann auf Originale zurueckabbilden oder diese durch Teile ersetzen.
            using var temps = AvTempQuery().ToEntityArray(Allocator.Temp);
            foreach (var e in temps)
            {
                var original = EntityManager.GetComponentData<Temp>(e).m_Original;
                if (roots.Contains(original)) roots.Add(e);
            }
            World.GetOrCreateSystemManaged<ParkingLotVersorgungsdiagnoseSystem>()
                .Beginne(roots, grund);
        }
    }
}
