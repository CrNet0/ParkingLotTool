using System;
using System.Collections.Generic;
using System.Linq;
using Game.Prefabs;
using ParkingLotTool.Tools;

internal static class Program
{
    private static int _pruefungen, _fehler;
    private static void Pruefe(bool ok, string name)
    {
        _pruefungen++;
        if (!ok) { _fehler++; Console.WriteLine("FEHLER: " + name); }
    }

    private static int Main()
    {
        var kosten = new PathfindPrefab { name = "Invisible Path Pathfind", m_TrackTrafficFlow = true };
        kosten.AddOrGetComponent<CarPathfind>().m_DrivingCost = new(0, 0.01f, 0.01f, 0.01f);
        kosten.GetComponent<CarPathfind>().m_ParkingCost = new(10, 2, 3, 4);
        var fusskosten = new PathfindPrefab { name = "Pedestrian Pathfind" };
        fusskosten.AddOrGetComponent<PedestrianPathfind>().m_Cost = 7;
        var auto = new NetLaneGeometryPrefab { name = "Invisible Car Oneway Lane 3", m_PathfindPrefab = kosten };
        auto.AddOrGetComponent<CarLane>().m_RoadTypes = 123;
        auto.AddOrGetComponent<SpawnableLane>();
        var fuss = new NetLanePrefab { name = "Foot", m_PathfindPrefab = fusskosten };
        fuss.AddOrGetComponent<PedestrianLane>();
        // Zyklus und indirekte Autospur: kein endloses Klonen, keine Vanilla-Lane.
        auto.AddOrGetComponent<SecondaryLane>().m_LeftLanes = new[] { new NetLaneInfo { m_Lane = fuss } };
        fuss.AddOrGetComponent<SecondaryLane>().m_LeftLanes = new[] { new NetLaneInfo { m_Lane = auto } };
        var piece = new NetPiecePrefab { name = "Drive Piece", m_Width = 3, geometryAsset = new() };
        var lod = new NetPiecePrefab { name = "Drive LOD", geometryAsset = new() };
        piece.AddOrGetComponent<LodProperties>().m_LodMeshes = new RenderPrefab[] { lod };
        auto.m_Meshes = new RenderPrefab[] { piece };
        piece.AddOrGetComponent<NetPieceLanes>().m_Lanes = new[]
            { new NetLaneInfo { m_Lane = auto }, new NetLaneInfo { m_Lane = fuss } };
        var section = new NetSectionPrefab { name = "Drive Section", m_Pieces = new[] { new NetPieceInfo { m_Piece = piece } } };
        section.m_SubSections = new[] { new NetSubSectionInfo { m_Section = section } };
        var vanilla = new PathwayPrefab { name = "Vanilla", m_SpeedLimit = 40,
            m_Sections = new[] { new NetSectionInfo { m_Section = section, m_Flip = true },
                new NetSectionInfo { m_Section = section } } };
        var anmeldung = new List<(PrefabBase Quelle, PrefabBase Klon)>();
        var root = new PathwayPrefab { name = "PLT Weg", m_Sections = vanilla.m_Sections, m_SpeedLimit = 40 };
        new ParkingLotFahrprefabKopie(root.name, (q, k) => anmeldung.Add((q, k))).Isoliere(root);
        Pruefe(vanilla.m_SpeedLimit == 40 && root.m_SpeedLimit == 25, "Tempo nur am Klon");
        Pruefe(!ReferenceEquals(root.m_Sections, vanilla.m_Sections), "eigene SectionInfo-Liste");
        Pruefe(!ReferenceEquals(root.m_Sections[0], vanilla.m_Sections[0]) && root.m_Sections[0].m_Flip,
            "eigene Infoobjekte, Einbahn-Richtung erhalten");
        var s = root.m_Sections[0].m_Section;
        Pruefe(s != section && s == root.m_Sections[1].m_Section, "Aliase im Klon, Quelle getrennt");
        Pruefe(s.m_SubSections[0].m_Section == s, "Untersektionszyklus bleibt intern");
        var p = s.m_Pieces[0].m_Piece;
        Pruefe(p != piece && p.geometryAsset == piece.geometryAsset, "Piece getrennt, Asset nur gelesen");
        Pruefe(p.GetComponent<LodProperties>().m_LodMeshes[0] != lod, "LOD-Meshcache ebenfalls getrennt");
        var a = (NetLaneGeometryPrefab)p.GetComponent<NetPieceLanes>().m_Lanes[0].m_Lane;
        var f = p.GetComponent<NetPieceLanes>().m_Lanes[1].m_Lane;
        Pruefe(a != auto && a.m_PathfindPrefab != kosten, "Lane und Car-Pathfind getrennt");
        Pruefe(a.m_Meshes[0] == p, "Lane-Mesh-Referenz intern");
        Pruefe(a.GetComponent<SecondaryLane>().m_LeftLanes[0].m_Lane == f
            && f.GetComponent<SecondaryLane>().m_LeftLanes[0].m_Lane == a, "SecondaryLane-Zyklus intern");
        Pruefe(a.GetComponent<CarLane>().m_RoadTypes == 123, "Fahrzeugzulassung unveraendert");
        var car = a.m_PathfindPrefab.GetComponent<CarPathfind>();
        Pruefe(car.m_DrivingCost.m_Time == 0 && car.m_DrivingCost.m_Behaviour == 0.25f
            && car.m_DrivingCost.m_Money == 0.01f && car.m_DrivingCost.m_Comfort == 0.05f, "alle vier Kostenkanaele");
        Pruefe(car.m_ParkingCost.m_Time == 10 && car.m_ParkingCost.m_Comfort == 4, "Parkkosten unveraendert");
        Pruefe(kosten.GetComponent<CarPathfind>().m_DrivingCost.m_Behaviour == 0.01f
            && section.m_Pieces[0].m_Piece == piece && piece.GetComponent<NetPieceLanes>().m_Lanes[0].m_Lane == auto,
            "Vanilla-Kette unveraendert");
        Pruefe(f.m_PathfindPrefab == fusskosten && fusskosten.GetComponent<PedestrianPathfind>().m_Cost == 7,
            "Fuss-Pathfind unveraendert");
        Pruefe(anmeldung.Select(k => k.Klon.name).Distinct().Count() == anmeldung.Count,
            "stabile eindeutige Unterprefabnamen");
        var zweiter = new RoadPrefab { name = "PLT Strasse", m_Sections = vanilla.m_Sections, m_SpeedLimit = 60,
            m_ZoneBlock = new PrefabBase() };
        new ParkingLotFahrprefabKopie(zweiter.name, (_, _) => { }).Isoliere(zweiter);
        Pruefe(zweiter.m_SpeedLimit == 25 && zweiter.m_ZoneBlock != null, "Zoningblock und Roadtempo");
        Pruefe(zweiter.m_Sections[0].m_Section.m_Pieces[0].m_Piece != p, "Meshpieces zwischen PLT-Netzarten getrennt");
        foreach (var klon in anmeldung.Select(k => k.Klon).Append(root))
        {
            Pruefe(klon.prefab == klon && klon.components.All(c => c.prefab == klon), "Rueckverweise " + klon.name);
            Pruefe(klon.GetComponent<SpawnableLane>() == null && klon.asset == null, "kein Spawn/Asset " + klon.name);
            var meta = klon.GetComponent<EditorAssetCategoryOverride>();
            Pruefe(meta != null && !meta.active && meta.m_IncludeCategories.Any()
                && meta.m_ExcludeCategories.Contains("FindIt"), "FindIt sichtbar, CS2 inaktiv " + klon.name);
        }
        foreach (var verboten in new ComponentBase[] { new UIObject(), new ObsoleteIdentifiers(), new SpawnableArea(),
            new SpawnableObject(), new SpawnableLane(), new SpawnableBuilding(), new PlaceholderArea(),
            new PlaceholderObject(), new PlaceholderLane(), new PlaceholderBuilding(), new AssetPackItem(),
            new EditorAssetCategoryOverride() })
            Pruefe(!ParkingLotFahrprefabKopie.Erben(verboten), "kein Listenbauteil " + verboten.GetType().Name);
        ParkingLotToolSystem.PruefeFahrwegeMigration(Pruefe);
        Console.WriteLine($"FAHRPREFABS: {_pruefungen} Pruefungen, {_fehler} Fehler (Produktions-Klonfunktion, Container-Test).");
        return _fehler == 0 ? 0 : 1;
    }
}
