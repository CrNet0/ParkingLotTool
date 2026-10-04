using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /*
     * PFLANZEN NEU SETZEN STATT NEU BAUEN (Sync-Schritt 10, 2026-10-04).
     *
     * Bis zum 2026-10-04 pflanzte der Planer jede Art mit TreeData wie einen
     * Baum - auch Buesche mit Altersstufen (Wildblumenbusch 01): 3 m zur
     * Laterne, Baumgruppen, 30 % Einsatz am Streifenrand. Nutzer: "Das auch als
     * Sync, dass auch die neue Dichte passt."
     *
     * Am Bestand muss sich nur eines aendern: die Pflanzen. Also kein Neubau
     * des Parkplatzes, sondern dasselbe Muster wie der Fahrwegtausch: alte
     * Pflanzen loeschen (CreationDefinition m_Original + Delete, wie der
     * Bulldozer) und neue anlegen (dieselbe Definition wie der Bau,
     * ParkingLotToolSystem.PflanzenDefinition) - als EINE Vorschau, EIN Apply.
     * Wechselt der Nutzer mittendrin das Werkzeug, wird verworfen und nichts
     * aendert sich.
     *
     * Gruen kommt aus dem nachgerechneten Layout (Bauzettel), wie beim Bau -
     * die gebauten Flaechen verraten ihre Rolle nicht (Gruen und Zoningbelag
     * teilen sich -94). Gegen eine seit dem Bau geaenderte Geometrie schuetzt
     * die Deckung: fast alle alten Pflanzen muessen im nachgerechneten Gruen
     * liegen, sonst bleibt der Parkplatz unveraendert und wird gemeldet.
     */
    public sealed partial class ParkingLotPflanzenTauschSystem : GameSystemBase
    {
        /** So viel der alten Pflanzen muss im nachgerechneten Gruen liegen. */
        private const float MindestDeckung = 0.98f;

        private readonly List<Entity> _warteschlange = new();
        private ToolSystem _toolSystem;
        private ParkingLotPflanzenTauschWerkzeug _werkzeug;
        private ParkingLotToolSystem _plt;
        private ToolBaseSystem _vorher;
        private bool _alleOk = true;
        private int _ruheBis;
        private EntityQuery _teile;

        private sealed class Vorbereitung
        {
            internal Entity Lot, Traeger;
            internal VegetationOptions Optionen;
            internal VegetationAsset[] Arten;
            internal List<Entity> Alte = new();
            internal Task<(VegetationPlan Plan, string Fehler, float Deckung)> Rechnung;
        }

        private Vorbereitung _laeuft;

        internal int Offen => _warteschlange.Count + (_laeuft != null ? 1 : 0);
        internal bool Beschaeftigt => _laeuft != null || _werkzeug.Beschaeftigt || _toolSystem.activeTool == _werkzeug;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _werkzeug = World.GetOrCreateSystemManaged<ParkingLotPflanzenTauschWerkzeug>();
            _plt = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            _werkzeug.Fertig = Fertig;
            _teile = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkingLotPartRelation>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
        }

        internal void Einreihen(Entity lot)
        {
            if (_warteschlange.Contains(lot) || _laeuft?.Lot == lot) return;
            _warteschlange.Add(lot);
            Mod.log.Info($"PLT-Pflanzentausch: Lot {lot.Index} eingereiht; offen {Offen}.");
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var spiel = GameManager.instance;
            if (spiel == null || spiel.isGameLoading || !spiel.gameMode.IsGame()) return;
            if (_werkzeug.Beschaeftigt) return;
            if (_laeuft == null)
            {
                if (_warteschlange.Count == 0 || UnityEngine.Time.frameCount < _ruheBis) return;
                // Nie zwei Tauschwerkzeuge gleichzeitig: jedes setzt das aktive Werkzeug.
                if (World.GetOrCreateSystemManaged<ParkingLotFahrwegTauschSystem>().Offen > 0) return;
                var lot = _warteschlange[0];
                _warteschlange.RemoveAt(0);
                var fehler = Bereite(lot, out _laeuft);
                if (fehler != null) { _laeuft = null; Fertig(lot, false, fehler); }
                return;
            }
            if (!_laeuft.Rechnung.IsCompleted) return;
            var v = _laeuft;
            var (plan, grund, deckung) = v.Rechnung.IsFaulted
                ? (null, "Rechnung fehlgeschlagen: " + v.Rechnung.Exception?.GetBaseException().Message, 0f)
                : v.Rechnung.Result;
            if (grund != null) { _laeuft = null; Fertig(v.Lot, false, grund); return; }
            // Ein Werkzeugwechsel wuerde einen laufenden Entwurf verwerfen.
            if (_toolSystem.activeTool == _plt && _plt.ArbeitetGerade) return;
            if (World.GetOrCreateSystemManaged<ParkingLotFahrwegTauschSystem>().Offen > 0) return;
            _laeuft = null;
            Mod.log.Info($"PLT-Pflanzentausch: Lot {v.Lot.Index}: {v.Alte.Count} alte Pflanzen, {plan.Plants.Count} neue "
                + $"geplant; alte im nachgerechneten Gruen {deckung:P0}; verworfen an Laternen {plan.LaternenVerworfen}.");
            if (_toolSystem.activeTool != _werkzeug) _vorher = _toolSystem.activeTool;
            _werkzeug.Starte(v.Lot, v.Traeger, v.Optionen, v.Arten, plan, v.Alte);
            _toolSystem.activeTool = _werkzeug;
        }

        /** Sammelt alles auf dem Spielthread und startet die Rechnung im Hintergrund. Fehlertext oder null. */
        private string Bereite(Entity lot, out Vorbereitung v)
        {
            v = null;
            if (!EntityManager.Exists(lot) || EntityManager.HasComponent<Deleted>(lot)
                || !EntityManager.HasComponent<ParkingLotCarrierReference>(lot)) return "Lot existiert nicht mehr";
            var traeger = EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
            if (!EntityManager.Exists(traeger)) return "Traeger fehlt";
            if (!ParkingLotBaukontextLeser.TryRead(EntityManager, lot, out var kontext, out var lesefehler, melden: false))
                return "Bauzettel unlesbar: " + lesefehler;
            var optionen = _plt.VegetationVon(lot);
            var katalog = World.GetOrCreateSystemManaged<ParkingLotUISystem>().VegetationAssets;
            var arten = katalog.Where(a => optionen.Species.Contains(a.Id)).ToArray();
            if (arten.Length == 0) return "keine der gewaehlten Arten im Katalog";
            v = new Vorbereitung { Lot = lot, Traeger = traeger, Optionen = optionen, Arten = arten };
            var altePositionen = new List<float2>();
            var laternen = new List<LaternenKoerper>();
            using (var teile = _teile.ToEntityArray(Allocator.Temp))
                foreach (var t in teile)
                {
                    if (EntityManager.GetComponentData<ParkingLotPartRelation>(t).Lot != lot) continue;
                    var prefab = EntityManager.GetComponentData<PrefabRef>(t).m_Prefab;
                    if (!EntityManager.HasComponent<Game.Objects.Transform>(t)) continue;
                    var lage = EntityManager.GetComponentData<Game.Objects.Transform>(t);
                    if (EntityManager.HasComponent<PlantData>(prefab))
                    {
                        v.Alte.Add(t);
                        altePositionen.Add(lage.m_Position.xz);
                    }
                    else if (EntityManager.HasComponent<StreetLight>(t) && EntityManager.HasComponent<ObjectGeometryData>(prefab))
                    {
                        var g = EntityManager.GetComponentData<ObjectGeometryData>(prefab);
                        laternen.Add(new LaternenKoerper
                        {
                            Position = lage.m_Position.xz,
                            Vorwaerts = math.normalizesafe(math.mul(lage.m_Rotation, new float3(0, 0, 1)).xz),
                            Bein = (g.m_Flags & GeometryFlags.Standing) != 0 ? g.m_LegSize : float3.zero,
                            Max = g.m_Bounds.max,
                        });
                    }
                }
            var settings = ParkingLotToolSystem.LayoutEinstellungen(kontext);
            var polygon = kontext.Punkte.Select(p => p.xz).ToArray();
            var species = arten.Select(a => new VegetationSpecies { Id = a.Id, Tree = a.Tree, Spacing = a.Spacing, Hoehe = a.Hoehe }).ToArray();
            ParkingVegetation.Dichtefaktor = Mod.Optionen?.Vegetationsdichte ?? 1f;
            v.Rechnung = Task.Run(() =>
            {
                var layout = ParkingGeometry.Build(polygon, settings);
                var gras = layout.GrassForVegetation ?? Array.Empty<float2[]>();
                var drin = altePositionen.Count(p => gras.Any(r => r != null && r.Length >= 3 && InRing(p, r)));
                var deckung = altePositionen.Count == 0 ? 1f : drin / (float)altePositionen.Count;
                if (deckung < MindestDeckung)
                    return ((VegetationPlan)null, $"nur {deckung:P0} der alten Pflanzen liegen im nachgerechneten Gruen - "
                        + "die Geometrie hat sich seit dem Bau geaendert; Parkplatz bleibt unveraendert", deckung);
                return (ParkingVegetation.Plan(gras, optionen, species, laternen), (string)null, deckung);
            });
            return null;
        }

        private static bool InRing(float2 p, float2[] ring)
        {
            var innen = false;
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
                if ((ring[i].y > p.y) != (ring[j].y > p.y)
                    && p.x < (ring[j].x - ring[i].x) * (p.y - ring[i].y) / (ring[j].y - ring[i].y) + ring[i].x)
                    innen = !innen;
            return innen;
        }

        private void Fertig(Entity lot, bool ok, string text)
        {
            if (!ok) _alleOk = false;
            Mod.log.Info($"PLT-Pflanzentausch: Lot {lot.Index}: {(ok ? "fertig" : "nicht getauscht")} - {text}.");
            World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeHintergrundEnde(lot, lot, ok);
            _ruheBis = UnityEngine.Time.frameCount + 10;
            if (_warteschlange.Count > 0 || _laeuft != null) return;
            // Alles erledigt: voriges Werkzeug zurueck (oeffnet ein vorher offenes
            // PLT-Panel wieder). Nach einem Fehlschlag bleibt das Panel zu.
            var zurueck = _alleOk && _vorher != null && _vorher != _werkzeug ? _vorher : null;
            if (_toolSystem.activeTool == _werkzeug)
                _toolSystem.activeTool = zurueck ?? World.GetOrCreateSystemManaged<DefaultToolSystem>();
            _vorher = null;
            _alleOk = true;
        }
    }

    public sealed partial class ParkingLotPflanzenTauschWerkzeug : ToolBaseSystem
    {
        public const string Id = "PLT Pflanzentausch";
        public override string toolID => Id;
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        private enum Stufe { Frei, Anlegen, Warten, Uebernehmen, Pruefen, Nachsetzen }

        private Stufe _stufe;
        private Entity _lot, _traeger;
        private VegetationOptions _optionen;
        private VegetationAsset[] _arten;
        private VegetationPlan _plan;
        private readonly List<Entity> _alte = new();
        private readonly HashSet<Entity> _alteMenge = new();
        private readonly HashSet<Entity> _prefabs = new();
        private readonly List<Entity> _definitionen = new();
        private readonly Dictionary<(Entity, float2), Game.Objects.Tree> _ziele = new();
        private int _geplant, _seit;
        private EntityQuery _fremdeDefinitionen, _temps, _teile;
        private ParkingLotToolSystem _plt;
        private TerrainSystem _terrain;
        internal Action<Entity, bool, string> Fertig;

        internal bool Beschaeftigt => _stufe != Stufe.Frei;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _fremdeDefinitionen = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>());
            _temps = GetEntityQuery(ComponentType.ReadOnly<Temp>());
            _teile = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ParkingLotPartRelation>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            _plt = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            _terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        internal void Starte(Entity lot, Entity traeger, VegetationOptions optionen, VegetationAsset[] arten,
            VegetationPlan plan, List<Entity> alte)
        {
            _lot = lot; _traeger = traeger; _optionen = optionen; _arten = arten; _plan = plan;
            _alte.Clear(); _alte.AddRange(alte);
            _alteMenge.Clear(); foreach (var a in alte) _alteMenge.Add(a);
            _prefabs.Clear(); foreach (var a in arten) _prefabs.Add(a.Prefab);
            _definitionen.Clear(); _ziele.Clear(); _geplant = 0;
            _stufe = Stufe.Anlegen;
            _seit = UnityEngine.Time.frameCount;
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            applyMode = ApplyMode.None;
            var bild = UnityEngine.Time.frameCount;
            if (m_ToolSystem.activeTool != this)
            {
                if (_stufe == Stufe.Warten || _stufe == Stufe.Uebernehmen) applyMode = ApplyMode.Clear;
                if (_stufe != Stufe.Frei && _stufe != Stufe.Pruefen && _stufe != Stufe.Nachsetzen)
                    Beende(false, "Werkzeug gewechselt, Tausch verworfen");
                return inputDeps;
            }
            switch (_stufe)
            {
                case Stufe.Anlegen:
                    // Nur in einem Bild ohne fremde Entwuerfe: unsere Vorschau soll
                    // nie mit einer anderen gemeinsam uebernommen werden.
                    if (_fremdeDefinitionen.CalculateEntityCount() > 0 || _temps.CalculateEntityCount() > 0)
                    {
                        if (bild - _seit > 600) Beende(false, "fremde Entwuerfe blieben 600 Bilder");
                        break;
                    }
                    LegeDefinitionenAn();
                    _stufe = Stufe.Warten;
                    _seit = bild;
                    break;
                case Stufe.Warten:
                    {
                        ZaehleTemps(out var weg, out var neu);
                        if (weg >= _alte.Count && neu >= _geplant) { _stufe = Stufe.Uebernehmen; break; }
                        if (bild - _seit > 60)
                        {
                            applyMode = ApplyMode.Clear;
                            Beende(false, $"Vorschau unvollstaendig: {weg}/{_alte.Count} Loeschungen, {neu}/{_geplant} neue Pflanzen");
                        }
                        break;
                    }
                case Stufe.Uebernehmen:
                    HefteNeue();
                    ParkingLotSchrittmarke.Aenderung("Pflanzentausch: Lot " + _lot.Index + " wird uebernommen (Apply)");
                    applyMode = ApplyMode.Apply;
                    _stufe = Stufe.Pruefen;
                    _seit = bild;
                    break;
                case Stufe.Pruefen:
                    if (bild - _seit < 3) break;
                    if (!Pruefe(out var text)) { Beende(false, text); break; }
                    _stufe = Stufe.Nachsetzen;
                    _seit = bild;
                    break;
                case Stufe.Nachsetzen:
                    // Wie nach dem Bau: der Baumzustand gehoert an den DAUERHAFTEN Baum.
                    if (bild - _seit < 30) break;
                    var gesetzt = SetzeBaumzustaende();
                    try { _plt.MarkiereVegetationNachWuchs(_lot); }
                    catch (Exception e) { Beende(false, "Zettel nicht hochgesetzt: " + e.Message); break; }
                    Beende(true, $"{_alte.Count} alte ersetzt durch {_geplant} neue, {gesetzt} Baumzustaende gesetzt, Zettel Version 2");
                    break;
            }
            return inputDeps;
        }

        private void LegeDefinitionenAn()
        {
            foreach (var alt in _alte)
            {
                if (!EntityManager.Exists(alt) || EntityManager.HasComponent<Deleted>(alt)) continue;
                var lage = EntityManager.GetComponentData<Game.Objects.Transform>(alt);
                var d = EntityManager.CreateEntity();
                // Wie der Bulldozer (BulldozeToolSystem.AddEntity): Original, Besitzer, Delete.
                EntityManager.AddComponentData(d, new CreationDefinition
                {
                    m_Original = alt,
                    m_Owner = EntityManager.HasComponent<Owner>(alt) ? EntityManager.GetComponentData<Owner>(alt).m_Owner : Entity.Null,
                    m_Flags = CreationFlags.Delete,
                });
                EntityManager.AddComponent<Updated>(d);
                EntityManager.AddComponentData(d, new ObjectDefinition
                {
                    m_Position = lage.m_Position, m_Rotation = lage.m_Rotation,
                    m_ParentMesh = -1, m_Probability = 100, m_PrefabSubIndex = -1,
                });
                _definitionen.Add(d);
            }
            var hoehen = _terrain.GetHeightData();
            foreach (var pflanze in _plan.Plants)
            {
                var asset = _arten[pflanze.Species];
                var d = _plt.PflanzenDefinition(asset, pflanze, _optionen, ref hoehen, out var position, out _, out var ziel);
                if (d == Entity.Null) continue;
                _definitionen.Add(d);
                _geplant++;
                if (ziel.HasValue) _ziele[(asset.Prefab, position.xz)] = ziel.Value;
            }
            ParkingLotSchrittmarke.Aenderung("Pflanzentausch: Lot " + _lot.Index + ", " + _definitionen.Count + " Definitionen angelegt");
            Mod.log.Info($"PLT-Pflanzentausch: Lot {_lot.Index}: {_alte.Count} Loeschungen und {_geplant} neue Pflanzen als Vorschau (Temp + Apply).");
        }

        private void ZaehleTemps(out int weg, out int neu)
        {
            weg = 0; neu = 0;
            using var alle = _temps.ToEntityArray(Allocator.Temp);
            foreach (var e in alle)
            {
                var temp = EntityManager.GetComponentData<Temp>(e);
                if (_alteMenge.Contains(temp.m_Original)) { weg++; continue; }
                if (temp.m_Original == Entity.Null && EntityManager.HasComponent<PrefabRef>(e)
                    && _prefabs.Contains(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab)) neu++;
            }
        }

        /** Wie HefteObjekteAnTraeger beim Bau: Besitzer, Anhaenger, Zuordnung - an der Vorschau, vor dem Apply. */
        private void HefteNeue()
        {
            using var alle = _temps.ToEntityArray(Allocator.Temp);
            foreach (var e in alle)
            {
                var temp = EntityManager.GetComponentData<Temp>(e);
                if (temp.m_Original != Entity.Null || !EntityManager.HasComponent<PrefabRef>(e)) continue;
                if (!_prefabs.Contains(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab)) continue;
                _plt.SetVegetationOwner(e, _traeger, _lot);
                EntityManager.AddComponentData(e, new Attached(_traeger, Entity.Null, 0f));
                EntityManager.AddComponentData(e, new ParkingLotPartRelation { Lot = _lot, Carrier = _traeger });
            }
        }

        private List<Entity> NeuePflanzen()
        {
            var liste = new List<Entity>();
            using var teile = _teile.ToEntityArray(Allocator.Temp);
            foreach (var t in teile)
            {
                if (EntityManager.GetComponentData<ParkingLotPartRelation>(t).Lot != _lot || _alteMenge.Contains(t)) continue;
                if (EntityManager.HasComponent<PlantData>(EntityManager.GetComponentData<PrefabRef>(t).m_Prefab)) liste.Add(t);
            }
            return liste;
        }

        private bool Pruefe(out string text)
        {
            var weg = _alte.Count(a => !EntityManager.Exists(a) || EntityManager.HasComponent<Deleted>(a));
            var neue = NeuePflanzen();
            var besitz = neue.Count(n => EntityManager.HasComponent<Owner>(n) && EntityManager.GetComponentData<Owner>(n).m_Owner == _traeger);
            text = $"{weg}/{_alte.Count} alte entfernt, {neue.Count}/{_geplant} neue, Besitzer am Traeger {besitz}/{neue.Count}";
            return weg == _alte.Count && neue.Count == _geplant && besitz == neue.Count;
        }

        private int SetzeBaumzustaende()
        {
            var gesetzt = 0;
            foreach (var n in NeuePflanzen())
            {
                if (!EntityManager.HasComponent<Game.Objects.Tree>(n)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(n).m_Prefab;
                var stelle = EntityManager.GetComponentData<Game.Objects.Transform>(n).m_Position.xz;
                if (!_ziele.TryGetValue((prefab, stelle), out var ziel)) continue;
                EntityManager.SetComponentData(n, ziel);
                if (!EntityManager.HasComponent<BatchesUpdated>(n)) EntityManager.AddComponent<BatchesUpdated>(n);
                gesetzt++;
            }
            return gesetzt;
        }

        private void Beende(bool ok, string text)
        {
            foreach (var d in _definitionen)
                if (EntityManager.Exists(d)) EntityManager.DestroyEntity(d);
            _definitionen.Clear();
            var lot = _lot;
            _stufe = Stufe.Frei;
            _lot = Entity.Null;
            ParkingLotSchrittmarke.Aenderung("Pflanzentausch: Lot " + lot.Index + (ok ? " fertig" : " abgebrochen"));
            Fertig?.Invoke(lot, ok, text);
        }
    }
}
