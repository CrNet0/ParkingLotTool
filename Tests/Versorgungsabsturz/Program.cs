using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Common;
using Game.Net;
using Game.Simulation;
using ParkingLotTool.Tools;
using Unity.Entities;

internal static class Program
{
    private static int _pruefungen;
    private static void Pruefe(bool ok, string name)
    { _pruefungen++; if (!ok) throw new Exception("FEHLER: " + name); }

    private static int Main()
    {
        try { Lauf(); return 0; }
        catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }
    }

    private static void Lauf()
    {
        var schedule = new UpdateSystem();
        ParkingLotVersorgungsphasen.Registriere(schedule);
        Pruefe(schedule.Registrierungen.Count == 1, "Abriss genau einmal registriert");
        // Registrierungskontrakt aus SystemOrder: beide GraphDelete-Systeme
        // sind normale Phase-1-Systeme. Keine native Graphsimulation im Test.
        var phase = schedule.Registrierungen.Single();
        Pruefe(phase.Phase == SystemUpdatePhase.Modification1 && phase.VorNormal,
            "Abriss liegt vor beiden normalen GraphDelete-Systemen in Phase 1");

        var d = new ParkingLotVersorgungsdiagnoseSystem();
        var em = d.EntityManager;
        var k = new Entity(1); var a = new Entity(2); var b = new Entity(3);
        em.Add(k, new Edge { m_Start = a, m_End = b });
        em.Add(a, new Node()); em.Add(b, new Node());
        em.Add(a, new List<ConnectedEdge> { new() { m_Edge = k } });
        em.Add(b, new List<ConnectedEdge> { new() { m_Edge = k } });
        var fa = new Entity(4); var fb = new Entity(5); var fk = new Entity(6);
        em.Add(a, new ElectricityNodeConnection { m_ElectricityNode = fa });
        em.Add(b, new ElectricityNodeConnection { m_ElectricityNode = fb });
        em.Add(fa, new ElectricityFlowNode()); em.Add(fb, new ElectricityFlowNode());
        em.Add(fk, new ElectricityFlowEdge { m_Start = fa, m_End = fb });
        em.Add(fa, new List<ConnectedFlowEdge> { new() { m_Edge = fk } });
        em.Add(fb, new List<ConnectedFlowEdge> { new() { m_Edge = fk } });
        d.Beginne(new[] { k }, "Apply");
        var sauber = ParkingLotSchrittmarke.Spur.Last();
        Pruefe(sauber.Contains("FLOWEDGE Strom") && sauber.Contains("ToteReferenzen=0 Asymmetrien=0 VerwaisteFlussknoten=0"), "gueltiges Netz mit Flusskante wird positiv bestaetigt");
        // Rueckpuffer weg, physische Entity weg, Flussendpunkt weg: drei
        // verschiedene Fehler muessen in der echten Messfunktion auftauchen.
        em.GetBuffer<ConnectedEdge>(b, true).Clear();
        UnityEngine.Time.frameCount = 1; d.Tick();
        Pruefe(ParkingLotSchrittmarke.Spur.Last().Contains("Asymmetrien=1"), "fehlende physische Gegenrichtung erkannt");
        em.Data.Remove(b);
        d.NachModification();
        Pruefe(ParkingLotSchrittmarke.Spur.Last().Contains("VerwaisteFlussknoten=1"), "alte Flussnode bleibt nach physischer Zerstoerung messbar");
        em.Data.Remove(fb);
        UnityEngine.Time.frameCount = 2; d.Tick();
        Pruefe(ParkingLotSchrittmarke.Spur.Last().Contains("FLOWEDGE Strom")
            && ParkingLotSchrittmarke.Spur.Last().Contains("ToteReferenzen=2"), "toter physischer Endpunkt UND tote Flussnode getrennt gezaehlt");
        em.GetBuffer<ConnectedFlowEdge>(fa, true).Add(new() { m_Edge = new Entity(99) });
        UnityEngine.Time.frameCount = 3; d.Tick();
        Pruefe(ParkingLotSchrittmarke.Spur.Last().Contains("CFE")
            && ParkingLotSchrittmarke.Spur.Last().Contains("Daten=0"), "tote Flusskante im Puffer sichtbar");
        // Kein Werkzeug-Update mehr: nur das eigene System tickt 120 Bilder.
        for (var i = 4; i <= 125; i++) { UnityEngine.Time.frameCount = i; d.Tick(); d.NachModification(); }
        Pruefe(ParkingLotSchrittmarke.Spur.Count(s => s.Contains("Phase=PostTool END")) == 120,
            "genau 120 PostTool-Messungen unabhaengig vom Werkzeug");
        Pruefe(ParkingLotSchrittmarke.Spur.Any(s => s.Contains("Bild=120/120") && s.Contains("Phase=ModificationEnd END")),
            "letztes Bild nach Modification ebenfalls gemessen");
        Pruefe(!ParkingLotSchrittmarke.Spur.Any(s => s.Contains("MESSFEHLER")), "Diagnose selbst ohne Messausnahme");
        var count = ParkingLotSchrittmarke.Spur.Count;
        Mod.Schalter.Add("versorgung-bilddiagnose");
        d.Beginne(new[] { k }, "Aus"); d.Tick(); d.NachModification();
        Pruefe(ParkingLotSchrittmarke.Spur.Count == count, "Diagnose einzeln abschaltbar");
        Mod.Schalter.Clear();
        var lot = new Entity(20); var carrier = new Entity(21);
        em.Add(k, new ParkingLotVersorgungsleitung { Lot = lot, Carrier = carrier });
        em.Add(k, new Owner { m_Owner = carrier });
        em.Add(a, new Owner { m_Owner = carrier });
        var fremd = new Entity(30); var fremdbesitzer = new Entity(31);
        em.Add(fremd, new Owner { m_Owner = fremdbesitzer });
        em.Add(carrier, new List<SubNet> { new() { m_SubNet = k }, new() { m_SubNet = a }, new() { m_SubNet = fremd } });
        d.MerkeEdit(lot, carrier, true);
        Pruefe(!em.HasComponent<Owner>(k) && !em.HasComponent<Owner>(a)
            && em.GetBuffer<SubNet>(carrier).Length == 1, "Edit-Schalter verhindert auch Vanilla-Besitzkaskade eigener Leitungen");
        Pruefe(em.HasComponent<Owner>(fremd) && em.GetBuffer<SubNet>(carrier).Single().m_SubNet == fremd,
            "fremde Owner und SubNet-Eintraege bleiben unveraendert");
        Pruefe(d.Behalten(new() { Lot = lot }) && d.Behalten(new() { Carrier = carrier }), "Edit-Schalter behaelt exakt alte Lot-/Traeger-Leitungen");
        Pruefe(!d.Behalten(new() { Lot = new Entity(20, 2) }), "wiederverwendeter Entity-Index erbt keinen Abrissschutz");
        var ohneFlussnode = new ParkingLotVersorgungsdiagnoseSystem();
        var p = new Entity(40);
        ohneFlussnode.EntityManager.Add(p, new ElectricityNodeConnection { m_ElectricityNode = new Entity(41) });
        ohneFlussnode.Beginne(new[] { p }, "fehlende NodeConnection");
        Pruefe(ParkingLotSchrittmarke.Spur.Last().Contains("ToteReferenzen=1"),
            "aktueller physischer Verweis auf fehlende Flussnode wird auch ohne Flusskante erkannt");
        Console.WriteLine($"Versorgungsabsturz: {_pruefungen} Pruefungen, 0 Fehler.");
    }
}
