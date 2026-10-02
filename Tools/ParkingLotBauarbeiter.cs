using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using SubNet = Game.Net.SubNet;
using SubLane = Game.Net.SubLane;
using CarLane = Game.Net.CarLane;
using Colossal.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        // Separater ECS-Arbeitszustand fuer dieselben partiellen Bau-Funktionen.
        // Kein ToolBase.OnCreate, kein Eintrag in ToolSystem.tools, keine
        // Eingabeaktionen, kein Panel, kein Aufruf von Tool-OnUpdate/Apply/Clear.
        private bool _bauarbeiter;
        private int _hintergrundStufeBBild = -1;
        [ThreadStatic] private static bool _erzeugeBauarbeiter;
        private ParkingLotBaukontext _baukontext;
        private VegetationOptions _bauvegetation;
        private VegetationAsset[] _baupflanzen;
        private EntityQuery _dauerhafteFlaechen;
        private readonly HashSet<Entity> _vorhandeneLots = new HashSet<Entity>();
        private readonly HashSet<Entity> _eigeneDauerteile = new HashSet<Entity>();

        internal static ParkingLotToolSystem Bauarbeiter(World world)
        {
            var werkzeug = world.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            // World.CreateSystemManaged erzeugt eine zweite Instanz; die erste
            // bleibt im Typ-Lookup. AddSystemManaged wuerde den doppelten Typ
            // ablehnen (Unity.Entities.World, dekompiliert am 02.10.2026).
            ParkingLotToolSystem arbeiter;
            _erzeugeBauarbeiter = true;
            try { arbeiter = world.CreateSystemManaged<ParkingLotToolSystem>(); }
            finally { _erzeugeBauarbeiter = false; }
            arbeiter._lotOwnerPrefab = werkzeug._lotOwnerPrefab;
            return arbeiter;
        }

        private void InitialisiereBauarbeiter()
        {
            Enabled = false;
            _terrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            _overlay = new ParkingLotOverlay();
            InitializeAreaPreview(); InitializeNetBuilder(); InitializeBayObjects();
            InitializeEntranceArrows(); InitializeLotOwner(); InitializeBusStops();
            InitialisiereZoningSeiten(); InitialisiereZoningSeitenSpeicher(); InitializeEditing();
            _dauerhafteFlaechen = GetEntityQuery(ComponentType.ReadOnly<Game.Areas.Area>(),
                ComponentType.ReadOnly<Game.Areas.Node>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>());
        }

        internal Task<ParkingLayout> BereiteHintergrund(Entity lot, int auftrag)
        {
            if (!_bauarbeiter) throw new InvalidOperationException("Kein isolierter Bauarbeiter.");
            if (!ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out _baukontext, out var grund))
                throw new InvalidOperationException(grund);
            _definitionsmodus = ParkingLotDefinitionsmodus.Permanent;
            _definitionsauftrag = auftrag;
            _editLot = lot; _lotOwner = _lotCarrier = Entity.Null;
            _replacementNewLot = _replacementNewCarrier = Entity.Null;
            _eigeneDauerteile.Clear(); _vorhandeneLots.Clear();
            using (var areas = _dauerhafteFlaechen.ToEntityArray(Allocator.Temp))
                foreach (var area in areas) _vorhandeneLots.Add(area);
            _points.Clear(); _worldPoints.Clear(); _entrances.Clear(); _busStops.Clear();
            _points.AddRange(_baukontext.Punkte.Select(p => p.xz));
            _worldPoints.AddRange(_baukontext.Punkte); _entrances.AddRange(_baukontext.Zugaenge);
            _busStops.AddRange(_baukontext.Bushalte);
            _zoningflaechen.Clear(); _zoningflaechen.AddRange(_baukontext.Zonen ?? Array.Empty<ParkingGeometry.Zoningflaeche>());
            _randzoning.Clear(); _randzoning.AddRange(_baukontext.Randzoning);
            _zoningSeitenPlan.Clear(); _zoningSeitenPlan.AddRange(_baukontext.Seitenplan);
            _bauvegetation = VegetationVon(lot);
            // Nur der unveraenderliche Assetkatalog; KEINE Panelwahl.
            _baupflanzen = World.GetOrCreateSystemManaged<ParkingLotUISystem>().VegetationAssets;
            _editParkingFeeKnown = EntityManager.HasComponent<ParkingLotEconomyData>(lot);
            _editParkingFee = _editParkingFeeKnown ? EntityManager.GetComponentData<ParkingLotEconomyData>(lot).ParkingFee : 0;
            _editBuildingEconomyEnabled = HasEnabledCompanion(lot);
            _replacementEconomyTransferred = false;
            _closed = true;
            LadeZoningBedienwerte(_baukontext.Zettel);
            var k = _baukontext;
            var settings = k.Zettel.ToLayoutSettings(k.Zugaenge);
            settings.Zoningstrasse = k.Zoningstrasse;
            settings.Zoningflaechen = k.Zonen;
            settings.Randzoning = k.Randzoning.ToArray();
            settings.BusStops = k.Bushalte.ToArray(); settings.Teilflaechenschnitte = k.Schnitte;
            settings.TeilflaechenAusrichtungen = (k.Ausrichtungen ?? Array.Empty<Ausrichtzuweisung>())
                .Select(a => new TeilflaechenAusrichtung { Anker = a.Anker, Winkel = a.Winkel }).ToArray();
            settings.Ausrichtwinkel = double.IsNaN(k.Zettel.Ausrichtwinkel) ? (double?)null : k.Zettel.Ausrichtwinkel;
            _areaPreviewSettings = settings;
            _alteZoningkurse = null; _erhalteneZoningteile.Clear(); _zoningErhalten = false;
            return Task.Run(() => ParkingGeometry.Build(k.Punkte.Select(p => p.xz).ToArray(), settings));
        }

        internal bool HintergrundPrefabs(ParkingLayout layout)
        {
            _areaPreviewLayout = layout;
            _altesZoningprefab = _baukontext.Zoningstrasse;
            var altkurse = new List<float2[]>();
            using (var teile = _editOwnerParts.ToEntityArray(Allocator.Temp))
                foreach (var e in teile)
                    if (ParkingLotBesitz.GehoertZu(EntityManager,EntityManager.GetComponentData<Owner>(e).m_Owner,_editLot)
                        && EntityManager.HasComponent<Edge>(e) && EntityManager.HasComponent<Curve>(e)
                        && _prefabSystem.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab) == "PLT Zoningstrasse (" + _altesZoningprefab + ")")
                    { var c = EntityManager.GetComponentData<Curve>(e).m_Bezier; altkurse.Add(new[] { c.a.xz,c.d.xz }); }
            _alteZoningkurse = altkurse.ToArray();
            ErgaenzeVorflaechen(layout, _areaPreviewSettings, _points.ToArray());
            SyncAreaPreview(prefabsOnly: true);
            bool netze = ResolvePathPrefabs();
            foreach (var stueck in layout.NetLine)
            {
                if (stueck.Kind == "zoning") netze &= TryResolveZoningRoad(_areaPreviewSettings.Zoningstrasse, out _);
                if (Zufahrtsarten.IstGasse(stueck.Art)) netze &= TryResolveZufahrtsgasse(stueck.Art, out _);
            }
            return _areaPreviewPrefabsReady && netze && TryResolveLotOwnerPrefab(out _);
        }

        internal void HintergrundAbriss() => EntferneAlteNetzeVorDemNeubau();
        internal bool HintergrundTerrainFertig() => GelaendeNachAbrissFertig();
        internal Entity HintergrundLot => _lotOwner;
        internal Entity HintergrundTraeger => _lotCarrier;

        internal int HintergrundStufeA()
        {
            _areaTransferRecords.Clear();
            var height = _editHeightSnapshot.isCreated ? _editHeightSnapshot : _terrainSystem.GetHeightData(true);
            int n = CreateLotOwnerDefinition(ref height);
            if (n == 1)
            {
                var r = _areaTransferRecords[0];
                EntityManager.AddComponentData(_editLot,new ParkingLotStufeAPrefab { Prefab = r.Prefab });
                var b = EntityManager.AddBuffer<ParkingLotStufeAKnoten>(_editLot);
                foreach (var p in r.SentNodes) b.Add(new ParkingLotStufeAKnoten { Position = p });
            }
            return n;
        }

        internal bool HintergrundBesitzerDa()
        {
            if (_lotOwner != Entity.Null) return true;
            if (_areaTransferRecords.Count != 1) return false;
            var record = _areaTransferRecords[0];
            using var areas = _dauerhafteFlaechen.ToEntityArray(Allocator.Temp);
            var treffer = new List<Entity>();
            foreach (var a in areas)
                if (!_vorhandeneLots.Contains(a) && EntityManager.GetComponentData<PrefabRef>(a).m_Prefab == record.Prefab
                    && AreaNodesMatch(record.SentNodes, EntityManager.GetBuffer<Game.Areas.Node>(a, true))) treffer.Add(a);
            if (treffer.Count != 1) return false;
            AuditMaterializedArea(record,treffer[0]);
            if (record.GeometryAccepted != true) return false;
            _lotOwner = treffer[0];
            EnsureOwnerBuffers(_lotOwner);
            if (!CreateLotCarrier()) throw new InvalidOperationException("Neuer Traeger fehlt.");
            EnsureOwnerBuffers(_lotCarrier);
            EntityManager.AddComponentData(_lotCarrier, new Owner(_lotOwner));
            _eigeneDauerteile.Add(_lotOwner); _eigeneDauerteile.Add(_lotCarrier);
            // Die Ergebnisidentitaet liegt am alten Lot; nach Laden wird ein
            // unvollstaendiger Ersatz abgerissen und der Rueckweg aufgenommen.
            EntityManager.AddComponentData(_editLot, new ParkingLotOffenerErsatz { Lot = _lotOwner, Traeger = _lotCarrier });
            ParkingLotNetzRueckweg.Melde($"Stufe A materialisiert: Lot {_lotOwner.Index}, Traeger {_lotCarrier.Index}; 6 Besitzerpuffer vorhanden.");
            return true;
        }

        internal int HintergrundStufeB()
        {
            if (_lotOwner == Entity.Null || _lotCarrier == Entity.Null) throw new InvalidOperationException("Stufe A fehlt.");
            if (Mod.Aus("hintergrund-nach-a")) throw new InvalidOperationException("Fehlerprobe nach Stufe A.");
            HideVisibleParts(_editLot);
            _hintergrundStufeBBild = UnityEngine.Time.frameCount;
            return CreateAreaPreviewDefinitions(_areaPreviewLayout);
        }

        internal bool HintergrundKinderDa(out string messung)
        {
            SammleHintergrundteile();
            var areas = _eigeneDauerteile.Where(e => EntityManager.HasComponent<Game.Areas.Area>(e)).ToList();
            int flaechen = 0, objekte = 0, kurse = 0;
            var benutzt = new HashSet<Entity>();
            foreach (var r in _areaTransferRecords)
                foreach (var a in areas)
                    if (!benutzt.Contains(a) && EntityManager.GetComponentData<PrefabRef>(a).m_Prefab == r.Prefab
                        && AreaNodesMatch(r.SentNodes, EntityManager.GetBuffer<Game.Areas.Node>(a, true)))
                    {
                        AuditMaterializedArea(r, a);
                        if (r.GeometryAccepted != true) continue;
                        benutzt.Add(a); flaechen++; break;
                    }
            foreach (var r in _objectRecords)
                foreach (var e in _eigeneDauerteile)
                    if (!benutzt.Contains(e) && EntityManager.HasComponent<Game.Objects.Transform>(e)
                        && EntityManager.GetComponentData<PrefabRef>(e).m_Prefab == r.Prefab
                        && math.distance(EntityManager.GetComponentData<Game.Objects.Transform>(e).m_Position, r.From) <= .05f)
                    {
                        benutzt.Add(e); objekte++;
                        if (!EntityManager.HasComponent<ParkingLotPartRelation>(e))
                            EntityManager.AddComponentData(e, new ParkingLotPartRelation { Lot = _lotOwner, Carrier = _lotCarrier });
                        ApplyVegetationAge(e, r.Prefab); break;
                    }
            foreach (var r in _netRecords)
            {
                var abschnitte = new List<float2>();
                var deckendeKurven = new List<Bezier4x3>();
                foreach (var e in _eigeneDauerteile)
                {
                    if (!EntityManager.HasComponent<Curve>(e) || !EntityManager.HasComponent<Edge>(e)
                        || EntityManager.GetComponentData<PrefabRef>(e).m_Prefab != r.Prefab) continue;
                    var c = EntityManager.GetComponentData<Curve>(e).m_Bezier;
                    if (VersorgungskursPruefung.Abschnitt(r.From.xz, r.To.xz, c.a.xz,c.b.xz,c.c.xz,c.d.xz, out var abschnitt))
                    {
                        abschnitte.Add(abschnitt);
                        deckendeKurven.Add(c);
                    }
                }
                bool hoehen = r.Kurs.HasValue;
                for (int s = 0; s <= 16 && hoehen; s++)
                {
                    var punkt = MathUtils.Position(r.Kurs.Value.m_Curve,s/16f);
                    var projiziert = new List<float3>();
                    foreach (var c in deckendeKurven)
                    {
                        MathUtils.Distance(c.xz,punkt.xz,out float t);
                        projiziert.Add(MathUtils.Position(c,t));
                    }
                    hoehen &= HintergrundKurspruefung.LageGedeckt(punkt,projiziert);
                }
                if (hoehen && VersorgungskursPruefung.Vollstaendig(math.distance(r.From.xz,r.To.xz),abschnitte)) kurse++;
            }
            messung = $"Besitzerpruefung: Kurse mit 17 Hoehenproben (5 cm) {kurse}/{_netRecords.Count}, Flaechen {flaechen}/{_areaTransferRecords.Count}, Objekte {objekte}/{_objectRecords.Count}.";
            return _netRecords.Count > 0 && kurse == _netRecords.Count && flaechen == _areaTransferRecords.Count && objekte == _objectRecords.Count;
        }

        private void SammleHintergrundteile()
        {
            using var teile = _editOwnerParts.ToEntityArray(Allocator.Temp);
            foreach (var e in teile)
            {
                var owner = EntityManager.GetComponentData<Owner>(e).m_Owner;
                if (owner == _lotOwner || owner == _lotCarrier) _eigeneDauerteile.Add(e);
            }
            _eigeneDauerteile.RemoveWhere(e => !ParkingLotNetzRueckweg.Lebt(EntityManager,e) || EntityManager.HasComponent<Temp>(e));
            // Nur Besitzerpuffer, nie fertige Kanten/Nodes umschreiben. Das
            // ist derselbe Statistik-/Parking-Anschluss wie beim Temp-Bau.
            if (!EntityManager.Exists(_lotCarrier) || !EntityManager.HasBuffer<SubNet>(_lotCarrier)) return;
            var nets = _eigeneDauerteile.Where(e => EntityManager.HasComponent<Edge>(e)).ToArray();
            foreach (var e in nets)
            {
                var buffer = EntityManager.GetBuffer<SubNet>(_lotCarrier);
                if (!buffer.Any(n => n.m_SubNet == e)) buffer.Add(new SubNet(e));
            }
        }

        internal bool HintergrundWirtschaftBereit()
        {
            if (_editParkingFeeKnown && !_replacementEconomyTransferred)
            {
                if (!_editEconomySystem.TryInitializeReplacement(_lotOwner,_editParkingFee)) return false;
                _replacementEconomyTransferred = true;
            }
            return !_editBuildingEconomyEnabled || HasEnabledCompanion(_lotOwner);
        }

        internal bool HintergrundFahrwegeRichtig()
        {
            MesseSpurkosten(_lotCarrier);
            if (EntityManager.HasComponent<ParkingLotCarrierReference>(_editLot))
                MesseSpurkosten(EntityManager.GetComponentData<ParkingLotCarrierReference>(_editLot).Carrier);
            foreach (var e in _eigeneDauerteile.Concat(_erhalteneZoningteile).Distinct())
                if (EntityManager.HasComponent<Edge>(e) && EntityManager.HasBuffer<SubLane>(e))
                    foreach (var l in EntityManager.GetBuffer<SubLane>(e,true))
                        if (EntityManager.HasComponent<CarLane>(l.m_SubLane)
                            && (!HintergrundKurspruefung.FahrtempoStimmt(EntityManager.GetComponentData<CarLane>(l.m_SubLane).m_SpeedLimit)
                                || FahrspurkostenFalsch(l.m_SubLane))) return false;
            return HatGebauteFahrspuren(_lotCarrier) && !BrauchtFahrwegeNeubau(_lotCarrier);
        }

        internal void HintergrundUebernahme()
        {
            if (Mod.Aus("hintergrund-vor-uebernahme")) throw new InvalidOperationException("Fehlerprobe vor Uebernahme.");
            var k = _baukontext; var r = k.Zettel;
            var q = new Bauzettelquelle { Settings = _areaPreviewSettings, Punkte = k.Punkte,
                MedianWidth = r.MedianWidth, GreenMedian = r.GreenMedian, CrossBays = r.CrossBays,
                SurfaceRoadOn = r.SurfaceRoadOn, SurfaceDecorationOn = r.SurfaceDecorationOn,
                SurfaceApronOn = r.SurfaceApronOn, BayIcons = r.BayIcons,
                ZoningWinkelmodus = Winkelmodus.Dekodiere(r.ZoningWinkelmodus),
                ZoningReglerwinkel = r.ZoningReglerwinkel, ZoningAussentiefeVorwahl = r.ZoningAussentiefeVorwahl,
                ZoningAusrichtwinkel = double.IsNaN(r.ZoningAusrichtwinkel) ? (double?)null : r.ZoningAusrichtwinkel,
                Ausrichtwinkel = _areaPreviewSettings.Ausrichtwinkel, Ausrichtungen = k.Ausrichtungen,
                Trennschnitte = k.Schnitte, Zoningflaechen = k.Zonen, Seitenplan = k.Seitenplan,
                Randzoning = k.Randzoning, FlaecheStrasse = k.FlaecheStrasse,
                FlaecheDekoration = k.FlaecheDekoration, FlaecheZoning = k.FlaecheZoning };
            if (!SchreibeBauzettel(_lotOwner,q)) throw new InvalidOperationException("Neuer Bauzettel fehlt.");
            NameLotOwner(_areaPreviewLayout.Stalls);
            AppendBuildJournal(_areaPreviewLayout,_areaPreviewSettings,k.Punkte,OwnerDisplayName(_lotOwner),
                _eigeneDauerteile.Count(e => EntityManager.HasComponent<Edge>(e) || EntityManager.HasComponent<Game.Net.Node>(e)),
                _objectRecords.Count,_areaTransferRecords.Count(a => a.Kind == "Grass"),_areaTransferRecords.Count(a => a.Kind == "Asphalt"));
            BeginReplacementCommit(_lotOwner,_lotCarrier);
            if (!PollReplacementCommit()) throw new InvalidOperationException("Ersatzuebernahme nicht abgeschlossen.");
        }

        internal void HintergrundRuecknahme()
        {
            // Abriss wird am Gate ausgefuehrt, vor Modification1/2. Die ganze
            // neue Besitzerkette faellt; ALTES Lot und erhaltenes Zoning leben.
            if (_lotOwner == Entity.Null && _areaTransferRecords.Count == 1) HintergrundBesitzerDa();
            if (_lotOwner != Entity.Null) SammleHintergrundteile();
            foreach (var e in _eigeneDauerteile)
                if (ParkingLotNetzRueckweg.Lebt(EntityManager,e)) EntityManager.AddComponent<Deleted>(e);
            if (ParkingLotNetzRueckweg.Lebt(EntityManager,_lotOwner)) EntityManager.AddComponent<Deleted>(_lotOwner);
            if (ParkingLotNetzRueckweg.Lebt(EntityManager,_lotCarrier)) EntityManager.AddComponent<Deleted>(_lotCarrier);
            RestoreHiddenParts();
            VerwerfeEdithoehen();
        }
    }
}
