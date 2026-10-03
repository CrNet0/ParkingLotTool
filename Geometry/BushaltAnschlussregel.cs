using System.Collections.Generic;

namespace ParkingLotTool.Geometry
{
    /** Diagnose-A/B: keine Trassenwahl startet/endet im Netz mit PLT-Halt.
     * Die Gruppe bleibt in der Bedarfsbilanz. Die Probe verlangt eine weiter
     * gefundene Alternative in einer anderen Gruppe und abgewiesenen Vorplan. */
    internal static class BushaltAnschlussregel
    {
        internal static void Sperre(Versorgungseingabe e)
        {
            if (e.Bushaltkanten.Count == 0) return;
            var gesperrt = new HashSet<int>();
            foreach (var gruppe in VersorgungstrassenPlan.Gruppen(e.Eigene.FindAll(k => k.Versorgung)))
                if (gruppe.Kanten.Exists(k => e.Bushaltkanten.Contains(k.Id)))
                    foreach (var k in gruppe.Kanten) gesperrt.Add(k.Id);
            foreach (var k in e.Eigene)
                if (gesperrt.Contains(k.Id)) k.AnschlussGesperrt = true;
            foreach (var k in e.Ziele)
                if (gesperrt.Contains(k.Id)) k.AnschlussGesperrt = true;
        }
    }
}
