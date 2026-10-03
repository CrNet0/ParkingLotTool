using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
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
        Pruefe(em.HasComponent<Owner>(fremd) && em.GetBuffer<SubNet>(carrier)[0].m_SubNet == fremd,
            "fremde Owner und SubNet-Eintraege bleiben unveraendert");
        Pruefe(d.Behalten(new() { Lot = lot }) && d.Behalten(new() { Carrier = carrier }), "Edit-Schalter behaelt exakt alte Lot-/Traeger-Leitungen");
        Pruefe(!d.Behalten(new() { Lot = new Entity(20, 2) }), "wiederverwendeter Entity-Index erbt keinen Abrissschutz");
        var ohneFlussnode = new ParkingLotVersorgungsdiagnoseSystem();
        var p = new Entity(40);
        ohneFlussnode.EntityManager.Add(p, new ElectricityNodeConnection { m_ElectricityNode = new Entity(41) });
        ohneFlussnode.Beginne(new[] { p }, "fehlende NodeConnection");
        Pruefe(ParkingLotSchrittmarke.Spur.Last().Contains("ToteReferenzen=1"),
            "aktueller physischer Verweis auf fehlende Flussnode wird auch ohne Flusskante erkannt");
        DatenLauf();
        BushaltLauf();
        Console.WriteLine($"Versorgungsabsturz: {_pruefungen} Pruefungen, 0 Fehler.");
    }

    private static void BushaltLauf()
    {
        var d = new ParkingLotVersorgungsdiagnoseSystem(); var em = d.EntityManager;
        var lot = new Entity(200); var carrier = new Entity(201); var anker = new Entity(202);
        var k = new Entity(203); var stop = new Entity(204); var pf = new Entity(205);
        em.Add(lot, new Updated()); em.Add(carrier, new Owner { m_Owner = lot });
        em.Add(anker, new Owner { m_Owner = lot });
        em.Add(k, new Edge()); em.Add(k, new Curve()); em.Add(k, new PrefabRef { m_Prefab = pf });
        em.Add(k, new Owner { m_Owner = anker });
        em.Add(stop, new Game.Routes.TransportStop()); em.Add(stop, new Game.Objects.Transform());
        em.Add(stop, new Game.Objects.Attached { m_Parent = k, m_CurvePosition = .84f });
        em.Add(stop, new PrefabRef { m_Prefab = pf });
        em.Add(stop, new ParkingLotPartRelation { Lot = lot, Carrier = carrier });
        em.Add(k, new List<Game.Objects.SubObject> { new() { m_SubObject = stop } });
        Pruefe(ParkingLotTeilnetz.Kanten(em, lot, carrier).SequenceEqual(new[] { k }),
            "erhaltene Kante am Besitzeranker ohne neuen Traeger-SubNet wird gefunden");
        em.Add(carrier, new List<SubNet> { new() { m_SubNet = k }, new() { m_SubNet = k } });
        Pruefe(ParkingLotTeilnetz.Kanten(em, lot, carrier).Count == 1, "Teilnetz vereint doppelte Inventarquellen");
        em.Add(k, new Game.Tools.Temp());
        Pruefe(ParkingLotTeilnetz.Kanten(em, lot, carrier).Count == 0, "Temp-Kante ist kein Wiederaufbauziel");
        em.RemoveComponent<Game.Tools.Temp>(k); em.Add(k, new Deleted());
        Pruefe(ParkingLotTeilnetz.Kanten(em, lot, carrier).Count == 0, "Deleted-Kante ist kein Wiederaufbauziel");
        var ersatz = new Entity(210);
        em.Add(ersatz, new Edge()); em.Add(ersatz, new Curve()); em.Add(ersatz, new PrefabRef());
        em.Add(ersatz, new Owner { m_Owner = anker });
        Pruefe(ParkingLotTeilnetz.Kanten(em, lot, carrier).SequenceEqual(new[] { ersatz }),
            "Vanilla-Replace mit altem SubNet-Eintrag findet neue Kante ueber unveraenderten Besitzer");
        em.Add(ersatz, new Deleted());
        em.RemoveComponent<Deleted>(k);
        var fremd = new Entity(206); em.Add(fremd, new Edge()); em.Add(fremd, new Curve());
        em.Add(fremd, new PrefabRef()); em.Add(fremd, new ParkingLotPartRelation { Lot = new Entity(200, 2) });
        Pruefe(!ParkingLotTeilnetz.Kanten(em, lot, carrier).Contains(fremd), "andere Entity-Version erbt keine Netzzuordnung");
        var zyklus = new Entity(207); em.Add(zyklus, new Owner { m_Owner = zyklus });
        Pruefe(!ParkingLotTeilnetz.GehoertZu(em, zyklus, lot, carrier), "Besitzzyklus wird endlich abgewiesen");
        Pruefe(ParkingLotTeilnetz.Bushaltkanten(em).SetEquals(new[] { k }), "PLT-Halt liefert genau seine lebende Elternkante");
        em.Add(stop, new Deleted());
        Pruefe(ParkingLotTeilnetz.Bushaltkanten(em).Count == 0, "abgerissener Halt sperrt kein Leitungsnetz");
        em.RemoveComponent<Deleted>(stop);
        var w = new Entity(208); var lane = new Entity(209);
        em.Add(stop, new List<Game.Routes.ConnectedRoute> { new() { m_Waypoint = w } });
        em.Add(w, new Game.Routes.RouteLane { m_StartLane = lane, m_EndLane = lane });
        em.Add(w, new Game.Routes.AccessLane { m_Lane = lane });
        em.Add(lane, new Lane()); em.Add(lane, new Curve()); em.Add(lane, new PrefabRef());
        d.Beginne(new[] { k }, "Bushalt"); d.Tick();
        string Probe()
        {
            var start = ParkingLotSchrittmarke.Spur.Count;
            var vorher = new HashSet<Entity>(em.Data.Keys);
            d.Phasenmarke("vor-Mod3", true);
            Pruefe(vorher.SetEquals(em.Data.Keys), "Bushaltprobe erzeugt und loescht 0 Entities");
            return string.Join("\n", ParkingLotSchrittmarke.Spur.Skip(start));
        }
        var daten = Probe();
        Pruefe(daten.Contains("BUSHALT PLT") && daten.Contains("m_CurvePosition=0.84")
            && daten.Contains("BUSHALT-PARENT") && daten.Contains("SubObject=1")
            && !daten.Contains("KANDIDAT BUSHALT"), "besitzerloser Halt mit Gegenrichtung wird positiv gemessen");
        Pruefe(daten.Contains("BUSHALT-ROUTEN Anzahl=1") && daten.Contains("BUSHALT-LANE Access"),
            "ConnectedRoute, RouteLane und AccessLane werden bis zur Spur verfolgt");
        em.Add(carrier, new List<Game.Objects.SubObject> { new() { m_SubObject = stop } });
        daten = Probe();
        Pruefe(daten.Contains("BUSHALT-PUFFER " + carrier) && daten.Contains("Parent=0 Owner=0"),
            "ehemaliger Besitzerpuffer bleibt nach Owner-Entfernung als eigener Befund sichtbar");
        em.Add(stop, new Created()); em.Add(stop, new Owner { m_Owner = lot }); daten = Probe();
        Pruefe(daten.Contains("fehlt Owner.SubObject; Leser=SubObjectReferences.Created"),
            "Created-Halt mit pufferlosem Besitzer erreicht direkten Mod3-Leser");
        em.RemoveComponent<Owner>(stop); em.RemoveComponent<Created>(stop);
        em.GetBuffer<Game.Objects.SubObject>(k).Clear(); daten = Probe();
        Pruefe(daten.Contains("fehlt Parent.SubObject-Gegenrichtung"), "fehlende Attach-Gegenrichtung sichtbar");
        em.Data.Remove(lane); daten = Probe();
        Pruefe(daten.Contains("toter Route-/AccessLane-Verweis"), "toter Linien-Spurrest sichtbar");
        em.Data.Remove(w); daten = Probe();
        Pruefe(daten.Contains("toter ConnectedRoute.Waypoint"), "toter Routenrest sichtbar");
        em.Data.Remove(k); daten = Probe();
        Pruefe(daten.Contains("toter Attached.Parent") && !daten.Contains("MESSFEHLER"), "tote Elternkante wird ohne Diagnoseausnahme gemessen");
        // Derselbe legale Besitzerzustand bei einem Vanilla-Halt an einer
        // betroffenen Strasse muss im Vergleich auftauchen.
        em.Add(k, new Edge()); em.Add(k, new List<Game.Objects.SubObject> { new() { m_SubObject = stop } });
        em.RemoveComponent<ParkingLotPartRelation>(stop); daten = Probe();
        Pruefe(daten.Contains("VANILLA-VERGLEICH"), "Vanilla-Stop ohne Owner wird auf betroffener Strasse erfasst");
        Pruefe(ParkingLotTeilnetz.Bushaltkanten(em, new[] { k }).SetEquals(new[] { k }),
            "Halt an eigener Kante sperrt den Pfad auch vor seiner PLT-Audit-Zuordnung");
        em.GetBuffer<Game.Objects.SubObject>(k).Add(new() { m_SubObject = new Entity(999) });
        daten = Probe();
        Pruefe(daten.Contains("KANDIDAT SUBOBJECT"), "toter Strassen-SubObject-Eintrag erreicht direkten Mod3-Leser");
    }

    private static void DatenLauf()
    {
        var s = new UpdateSystem(); ParkingLotVersorgungsgrenzen.Registriere(s);
        Pruefe(s.Registrierungen.Count == 5 && s.Registrierungen[0].VorNormal
            && s.Registrierungen[0].Phase == SystemUpdatePhase.Modification3,
            "Datenprobe vor Mod3, vier folgende Grenzen registriert");
        var d = new ParkingLotVersorgungsdiagnoseSystem(); var em = d.EntityManager;
        var k = new Entity(100); var a = new Entity(101); var b = new Entity(102);
        var pf = new Entity(103); var c = new Entity(104); var piece = new Entity(105);
        var lane = new Entity(106); var lp = new Entity(107); var building = new Entity(108);
        em.Add(k, new Edge { m_Start = a, m_End = b }); em.Add(k, new PrefabRef { m_Prefab = pf });
        em.Add(a, new Node()); em.Add(b, new Node());
        em.Add(a, new List<ConnectedEdge> { new() { m_Edge = k } });
        em.Add(b, new List<ConnectedEdge> { new() { m_Edge = k } });
        em.Add(pf, new Game.Prefabs.NetData()); em.Add(pf, new Game.Prefabs.NetGeometryData());
        em.Add(k, new Composition { m_Edge = c, m_StartNode = c, m_EndNode = c });
        em.Add(c, new Game.Prefabs.NetCompositionData
        { m_Width = 13.4f, m_Flags = new() { m_General = 17, m_Left = 3, m_Right = 5 } });
        em.Add(c, new List<Game.Prefabs.NetCompositionPiece> { new() { m_Piece = piece, m_PieceFlags = Game.Prefabs.NetPieceFlags.HasMesh } });
        em.Add(piece, new Game.Prefabs.NetPieceData());
        em.Add(k, new List<SubLane> { new() { m_SubLane = lane } });
        em.Add(lane, new Lane()); em.Add(lane, new Curve()); em.Add(lane, new PrefabRef { m_Prefab = lp });
        em.Add(lp, new Game.Prefabs.NetLaneData());
        em.Add(k, new Owner { m_Owner = building });
        em.Add(building, new Game.Buildings.Building { m_RoadEdge = k });
        em.Add(building, new Game.Objects.Transform());
        var before = ParkingLotSchrittmarke.Spur.Count;
        d.Phasenmarke("inaktiv", true);
        Pruefe(before == ParkingLotSchrittmarke.Spur.Count, "inaktive Grenzen lesen und schreiben nichts");
        d.Beginne(new[] { k }, "Daten"); d.Tick();
        string Probe()
        {
            var start = ParkingLotSchrittmarke.Spur.Count;
            d.Phasenmarke("vor-Mod3", true);
            return string.Join("\n", ParkingLotSchrittmarke.Spur.Skip(start));
        }
        var daten = Probe();
        Pruefe(em.Completions == 1 && daten.Contains("vor-Mod3 BEGIN") && daten.Contains("vor-Mod3 END"),
            "Phasengrenze beendet Jobs zwischen dauerhaftem BEGIN und END");
        Pruefe(daten.Contains("m_Width=13.4") && daten.Contains("m_General=17 m_Left=3 m_Right=5")
            && daten.Contains("SUBLANE [0]") && daten.Contains("GEBAEUDE"),
            "Kompositionswerte, echte Spur und Fremdgebaeude werden verfolgt");
        Pruefe(daten.Contains("fehlt NetGeometryComposition; Leser=CompositionSelect")
            && daten.Contains("NetCompositionCrosswalk=fehlt"),
            "fehlender Kompositionscache und Crosswalk-Puffer explizit sichtbar");
        em.Add(pf, new List<Game.Prefabs.NetGeometryComposition> { new() { m_Composition = c } });
        em.Add(c, new List<Game.Prefabs.NetCompositionCrosswalk> { new() { m_Lane = lp } });
        daten = Probe();
        Pruefe(!daten.Contains("fehlt NetGeometryComposition; Leser=")
            && daten.Contains("NetGeometryComposition[0]") && daten.Contains("NetCompositionCrosswalk[0]"),
            "gueltige Cache- und Crosswalk-Verweise mit Index statt Fehlbefund");
        Pruefe(daten.Contains("fehlt MeshData; Leser=NetCompositionMeshRef.HasMesh")
            && daten.Contains("fehlt MeshMaterial"), "HasMesh mit fehlenden Renderdaten erkannt");
        em.Add(piece, new Game.Prefabs.MeshData()); em.Add(piece, new List<Game.Prefabs.MeshMaterial>());
        daten = Probe();
        Pruefe(!daten.Contains("fehlt MeshData; Leser=") && !daten.Contains("fehlt MeshMaterial"),
            "gueltiges sichtbares Piece hat beide Pflichtdaten");
        em.RemoveComponent<Game.Prefabs.MeshData>(piece);
        em.GetBuffer<Game.Prefabs.NetCompositionPiece>(c).Clear();
        em.GetBuffer<Game.Prefabs.NetCompositionPiece>(c).Add(new() { m_Piece = piece });
        daten = Probe();
        Pruefe(!daten.Contains("fehlt MeshData; Leser="), "Piece ohne HasMesh braucht kein MeshData");
        em.RemoveComponent<PrefabRef>(lane); daten = Probe();
        Pruefe(daten.Contains("fehlt PrefabRef; Leser=SecondaryLane"), "fehlendes Prefab an Fremdspur erkannt");
        em.Add(k, new Created()); daten = Probe();
        Pruefe(daten.Contains("fehlt Owner.SubNet"), "neues Netz mit Owner ohne SubNet erkannt");
        em.Add(building, new List<SubNet> { new() { m_SubNet = k } });
        em.RemoveComponent<Created>(k); em.Add(k, new Updated()); daten = Probe();
        Pruefe(!daten.Contains("fehlt Owner.SubNet") && daten.Contains("KindEnthalten=1"),
            "Updated und gueltiger Besitzerpuffer sind kein Created-Fehler");
        var vanilla = new Entity(109); em.Add(vanilla, new Game.Prefabs.NetData());
        em.Add(vanilla, new Game.Prefabs.NetGeometryData());
        var f = new ParkingLotFahrprefabSystem(); f.Quellen[pf] = vanilla;
        d.World.Systems[typeof(ParkingLotFahrprefabSystem)] = f;
        daten = Probe();
        Pruefe(daten.Contains("PF-VERGLEICH Klon=") && daten.Contains("Vanilla="), "Klon hat ausdruecklichen Vergleich mit Vanilla-Quelle");
        em.Data.Remove(piece); daten = Probe();
        Pruefe(daten.Contains("Entity tot") && !daten.Contains("MESSFEHLER"), "totes Piece wird ohne eigenen ungesicherten Zugriff protokolliert");
        var nichtLinq = false;
        try { em.GetBuffer<SubNet>(building).Any(); } catch (NotImplementedException) { nichtLinq = true; }
        Pruefe(nichtLinq, "Testdouble verbietet wie CS2 LINQ auf DynamicBuffer");
        var c2 = new Entity(110); var n2 = new Entity(111); var c3 = new Entity(112);
        em.Add(n2, new Node());
        em.Add(c2, new Edge { m_Start = b, m_End = n2 });
        em.Add(b, new List<ConnectedEdge> { new() { m_Edge = k }, new() { m_Edge = c2 } });
        em.Add(c3, new Edge { m_Start = n2, m_End = new Entity(113) });
        em.Add(n2, new List<ConnectedEdge> { new() { m_Edge = c2 }, new() { m_Edge = c3 } });
        Probe();
        var begrenzt = Probe();
        Pruefe(!begrenzt.Contains("DATEN " + c3),
            "wiederholte Datenproben oeffnen keine zweite Stadtnachbarschaft");
    }
}
