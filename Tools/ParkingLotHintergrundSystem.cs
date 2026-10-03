using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.SceneFlow;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    public struct ParkingLotOffenerErsatz : IComponentData, ISerializable
    {
        public Entity Lot, Traeger;
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter { w.Write(Lot); w.Write(Traeger); }
        public void Deserialize<TReader>(TReader r) where TReader : IReader { r.Read(out Lot); r.Read(out Traeger); }
    }

    /** Ein Auftrag, ein Gate, dieselbe Queue auch fuer den Edit-Rueckweg.
     *  Getickt nach ToolOutputBarrier, also vor jedem Vanilla-Generator und
     *  vor Net.References in Modification2B. Kein Werkzeugupdate. */
    [DisableAutoCreation]
    internal sealed partial class ParkingLotHintergrundSystem : GameSystemBase
    {
        private enum Phase { Planung, Terrain, Besitzer, Kinder, Anschluesse, Nachpruefung, Nebenarbeit, Ruecknahme, Rueckweg, Ruhe }
        private sealed class Auftrag
        {
            internal Entity Lot, Neu, Traeger;
            internal int Id, Seit, Versuch, Rueckwegpruefungen, PrefabSeit = -1;
            internal bool NurRueckweg, GateOffen, AnschlussAngestossen, RueckwegAusgegeben;
            internal Phase Phase;
            internal Task<ParkingLayout> Rechnung;
            internal ParkingLotToolSystem Bauer;
        }
        private readonly List<Auftrag> _queue = new List<Auftrag>();
        private readonly HashSet<Entity> _angehalten = new HashSet<Entity>();
        private readonly List<ParkingLotToolSystem> _flussbeobachter = new List<ParkingLotToolSystem>();
        private HintergrundAuftragsregel<Entity> _regel = new HintergrundAuftragsregel<Entity>();
        private Auftrag _aktiv;
        private int _nummer;
        private ParkingLotExklusivesBildSystem _gate;
        internal int Offen => _queue.Count + (_aktiv != null ? 1 : 0) + _angehalten.Count;
        internal string Fortschrittshinweis
        {
            get
            {
                if (_angehalten.Count > 0) return RueckwegHinweis;
                if (_aktiv == null && _queue.Count > 0
                    && World.GetExistingSystemManaged<ParkingLotToolSystem>()?.BearbeitetLot(_queue[0].Lot) == true)
                    return ParkingLotTexte.T(
                        "Synchronisation wartet: aktuelle Parkplatzbearbeitung abschliessen oder abbrechen und Panel/Werkzeug schliessen.",
                        "Synchronization is waiting: finish or cancel the current parking lot edit and close the panel/tool.");
                return _gate.Wartehinweis;
            }
        }
        private static string RueckwegHinweis => ParkingLotTexte.T(
            "Wiederherstellung angehalten. Parkplatz bleibt gesperrt. Bauzettel sichern und Spielstand vor der Synchronisation laden.",
            "Restoration stopped. The parking lot remains locked. Keep the build log and load the save from before synchronization.");
        internal bool Gesperrt(Entity lot) => Gesperrt(lot,out _);
        private bool Gesperrt(Entity lot, out bool angehalten)
        {
            angehalten = false;
            for (int i = 0; i < 8 && lot != Entity.Null && EntityManager.Exists(lot); i++)
            {
                if (_angehalten.Contains(lot)) { angehalten = true; return true; }
                if (_regel.Gesperrt(lot) || _aktiv != null && (_aktiv.Neu == lot || _aktiv.Traeger == lot)
                    || _queue.Any(a => a.Neu == lot || a.Traeger == lot)) return true;
                if (EntityManager.HasComponent<ParkingLotPartRelation>(lot))
                    lot = EntityManager.GetComponentData<ParkingLotPartRelation>(lot).Lot;
                else if (EntityManager.HasComponent<Owner>(lot))
                    lot = EntityManager.GetComponentData<Owner>(lot).m_Owner;
                else return false;
            }
            return false;
        }
        internal bool Sperrmeldung(Entity lot)
        {
            if (!Gesperrt(lot,out bool angehalten)) return false;
            var text = angehalten ? RueckwegHinweis : ParkingLotTexte.T(
                "Der Parkplatz wird im Hintergrund gebaut oder wiederhergestellt. Nach Abschluss erneut versuchen.",
                "This parking lot is being rebuilt or restored in the background. Try again after completion.");
            World.GetOrCreateSystemManaged<ParkingLotUISystem>().SetStatus(text);
            ParkingLotNetzRueckweg.Melde($"Lotsperre {lot.Index}: {text}; offene Auftraege {Offen}.");
            return true;
        }

        internal void Einreihen(Entity lot, bool rueckweg = false)
        {
            if (_angehalten.Contains(lot)) { Sperrmeldung(lot); return; }
            bool erster = Offen == 0;
            if (rueckweg)
            {
                var wartend = _queue.Find(a => a.Lot == lot);
                if (wartend != null) { wartend.NurRueckweg = true; return; }
                if (!_regel.EinreihenRueckweg(lot)) return;
            }
            else if (!_regel.Einreihen(lot))
            {
                if (!_regel.Gesperrt(lot))
                    World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeHintergrundEnde(lot,Entity.Null,false);
                return;
            }
            var auftrag = new Auftrag { Lot = lot, Id = ++_nummer, NurRueckweg = rueckweg };
            if (EntityManager.HasComponent<ParkingLotOffenerErsatz>(lot))
            {
                var ersatz = EntityManager.GetComponentData<ParkingLotOffenerErsatz>(lot);
                auftrag.Neu = ersatz.Lot; auftrag.Traeger = ersatz.Traeger;
            }
            _queue.Add(auftrag);
            if (rueckweg) World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeRueckwegStart(erster);
            ParkingLotNetzRueckweg.Melde($"Auftrag {_nummer} Lot {lot.Index} eingereiht; Rueckweg={rueckweg}; offen {Offen}.");
        }

        [Preserve]
        protected override void OnCreate()
        { base.OnCreate(); _gate = World.GetOrCreateSystemManaged<ParkingLotExklusivesBildSystem>(); }

        [Preserve]
        protected override void OnUpdate()
        {
            var spiel = GameManager.instance;
            if (spiel == null || spiel.isGameLoading || !spiel.gameMode.IsGame()) return;
            for (int i = _flussbeobachter.Count-1; i >= 0; i--)
            {
                var b = _flussbeobachter[i];
                try { b.HintergrundPflegeFluss(); }
                catch (Exception e)
                {
                    ParkingLotNetzRueckweg.Melde("Flussbeobachtung beendet: " + e.Message + "; keine Flussabnahme behauptet.");
                    World.DestroySystemManaged(b); _flussbeobachter.RemoveAt(i); continue;
                }
                if (b.HintergrundBeobachtetFluss) continue;
                World.DestroySystemManaged(b); _flussbeobachter.RemoveAt(i);
            }
            if (_aktiv == null)
            {
                if (_queue.Count == 0) return;
                var next = _queue[0];
                // Dasselbe Lot darf seinen bereits begonnenen Edit beenden.
                // Andere Lots bleiben frei; 0/0 regelt nur Definitionsbilder.
                if (World.GetOrCreateSystemManaged<ParkingLotToolSystem>().BearbeitetLot(next.Lot)) return;
                _queue.RemoveAt(0); _aktiv = next;
                next.Seit = UnityEngine.Time.frameCount;
                next.Versuch = next.NurRueckweg ? _regel.StartRueckweg(next.Lot) : _regel.Start(next.Lot);
                next.Phase = next.NurRueckweg ? (EntityManager.HasComponent<ParkingLotOffenerErsatz>(next.Lot)
                    || EntityManager.HasBuffer<ParkingLotStufeAKnoten>(next.Lot) ? Phase.Ruecknahme : Phase.Rueckweg) : Phase.Planung;
                ParkingLotNetzRueckweg.Melde($"Start Lot {next.Lot.Index}, Versuch {next.Versuch}/3; Auftrag {next.Id}; ohne Werkzeugeinstieg.");
                if (!next.NurRueckweg)
                {
                    try { next.Bauer = ParkingLotToolSystem.Bauarbeiter(World); next.Rechnung = next.Bauer.BereiteHintergrund(next.Lot,next.Id); }
                    catch (Exception e) { Fehler(next,e); }
                }
            }
            var a = _aktiv;
            if (a == null || a.GateOffen) return;
            try { Pflege(a); }
            catch (Exception e) { Fehler(a,e); }
        }

        private void Exklusiv(Auftrag a, Func<int> arbeit)
        {
            a.GateOffen = true;
            _gate.Erwarte(a.Lot, _ =>
            {
                a.GateOffen = false;
                try { return arbeit(); }
                catch (Exception e) { Fehler(a,e); return 0; }
            });
        }

        private void Pflege(Auftrag a)
        {
            int bild = UnityEngine.Time.frameCount;
            switch (a.Phase)
            {
                case Phase.Planung:
                    if (!a.Rechnung.IsCompleted) return;
                    if (a.PrefabSeit < 0) a.PrefabSeit = bild;
                    if (!a.Bauer.HintergrundPrefabs(a.Rechnung.GetAwaiter().GetResult()))
                    {
                        if (bild-a.PrefabSeit > 120) throw new InvalidOperationException("Bauprefabs nach 120 Bildern nicht bereit; kein eigener Abriss.");
                        return;
                    }
                    Exklusiv(a, () => { a.Bauer.HintergrundAbriss(); a.Phase = Phase.Terrain; a.Seit = UnityEngine.Time.frameCount; return 0; });
                    break;
                case Phase.Terrain:
                    if (!a.Bauer.HintergrundTerrainFertig()) return;
                    Exklusiv(a, () =>
                    {
                        int n = a.Bauer.HintergrundStufeA();
                        if (n != 1) throw new InvalidOperationException($"Stufe A: {n}/1 Lot-Definitionen.");
                        a.Phase = Phase.Besitzer; a.Seit = UnityEngine.Time.frameCount; return n;
                    });
                    break;
                case Phase.Besitzer:
                    if (!a.Bauer.HintergrundBesitzerDa())
                    { if (bild-a.Seit > 90) throw new InvalidOperationException("Stufe A nach 90 Bildern nicht materialisiert."); return; }
                    a.Neu = a.Bauer.HintergrundLot; a.Traeger = a.Bauer.HintergrundTraeger;
                    Exklusiv(a, () => { int n = a.Bauer.HintergrundStufeB(); a.Phase = Phase.Kinder; a.Seit = UnityEngine.Time.frameCount; return n; });
                    break;
                case Phase.Kinder:
                    if (!a.Bauer.HintergrundKinderDa(out var messung))
                    {
                        if (bild-a.Seit > 90)
                        { a.Bauer.HintergrundFehlstellen(); throw new InvalidOperationException(messung); }
                        return;
                    }
                    ParkingLotNetzRueckweg.Melde(messung);
                    Exklusiv(a, () => { a.Bauer.HintergrundMeldeAnschluesse(); a.Phase = Phase.Anschluesse; a.Seit = UnityEngine.Time.frameCount; return 0; });
                    break;
                case Phase.Anschluesse:
                    if (!a.Bauer.HintergrundAnschluesseDa(out int angeschlossen,out int erfasst))
                    { if (bild-a.Seit > 90) throw new InvalidOperationException($"Versorgungsanschluesse {angeschlossen}/{erfasst} nach 90 Bildern."); return; }
                    ParkingLotNetzRueckweg.Melde($"Versorgungsanschluesse beidseitig geprueft: {angeschlossen}/{erfasst}; 0 direkte physische Netzeintraege.");
                    a.Phase = Phase.Nachpruefung; a.Seit = UnityEngine.Time.frameCount;
                    break;
                case Phase.Nachpruefung:
                    if (bild-a.Seit < 30) return;
                    if (!a.Bauer.HintergrundWirtschaftBereit())
                    { if (bild-a.Seit > 120) throw new InvalidOperationException("Gebuehr/Begleiter nach 120 Bildern nicht uebernommen."); return; }
                    if (Mod.Aus("hintergrund-spurpruefung") || !a.Bauer.HintergrundFahrwegeRichtig())
                        throw new InvalidOperationException("Fahrwege-Nachpruefung fehlgeschlagen.");
                    ParkingLotNetzRueckweg.Melde("Fahrwege-Nachpruefung: echte Fahrspuren vorhanden, Fahrwert-/Kostenabweichungen 0.");
                    // Letzter exklusiver Abschluss: keine fremden Definitionen
                    // laufen in die Uebernahme/Nacharbeit hinein.
                    Exklusiv(a, () =>
                    {
                        a.Bauer.HintergrundUebernahme();
                        EntityManager.SetComponentData(a.Neu, new ParkingLotDatenstand { Stand = 7 });
                        EntityManager.AddComponent<ParkingLotNebenarbeitOffen>(a.Neu);
                        a.Phase = Phase.Nebenarbeit; a.Seit = UnityEngine.Time.frameCount; return 0;
                    });
                    break;
                case Phase.Nebenarbeit:
                    if (World.GetOrCreateSystemManaged<ParkingLotCleanupSystem>().AbrissLaeuft) return;
                    int nebenstand = a.Bauer.HintergrundNebenstand();
                    if (nebenstand == 0) return;
                    if (nebenstand == 1) { Exklusiv(a, () => a.Bauer.HintergrundNebenbild()); return; }
                    if (nebenstand == 3)
                    {
                        Exklusiv(a, () => { int n = a.Bauer.HintergrundVerwerfeNebenbau(); a.Seit = UnityEngine.Time.frameCount; return n; });
                        return;
                    }
                    if (nebenstand == 4) { Exklusiv(a, () => a.Bauer.HintergrundMeldeNebenanschluesse()); return; }
                    World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeHintergrundEnde(a.Lot,a.Neu,true);
                    EntityManager.RemoveComponent<ParkingLotNebenarbeitOffen>(a.Neu);
                    ParkingLotNetzRueckweg.Melde($"Abschluss Lot {a.Neu.Index}, Stand 8; offene Auftraege {Offen-1}.");
                    Ende(a);
                    break;
                case Phase.Ruecknahme:
                    Exklusiv(a, () =>
                    {
                        a.Bauer?.HintergrundRuecknahme();
                        RaumeGespeichertesA(a.Lot);
                        if (EntityManager.HasComponent<ParkingLotOffenerErsatz>(a.Lot))
                        {
                            var ersatz = EntityManager.GetComponentData<ParkingLotOffenerErsatz>(a.Lot);
                            foreach (var e in new[] { ersatz.Lot, ersatz.Traeger })
                                if (ParkingLotNetzRueckweg.Lebt(EntityManager,e)) EntityManager.AddComponent<Deleted>(e);
                        }
                        a.Phase = Phase.Ruhe; a.Seit = UnityEngine.Time.frameCount; return 0;
                    });
                    break;
                case Phase.Ruhe:
                    if (bild-a.Seit < 3 || World.GetOrCreateSystemManaged<ParkingLotCleanupSystem>().AbrissLaeuft) return;
                    a.Phase = Phase.Rueckweg; a.Seit = UnityEngine.Time.frameCount;
                    break;
                case Phase.Rueckweg:
                    if (!EntityManager.HasBuffer<ParkingLotRueckwegkurs>(a.Lot)) { RueckwegFertig(a,0,0); return; }
                    if (ParkingLotNetzRueckweg.Pruefe(EntityManager,a.Lot,out var ist,out var soll))
                    { RueckwegFertig(a,ist,soll); return; }
                    if (a.RueckwegAusgegeben)
                    {
                        if (!a.AnschlussAngestossen && ParkingLotNetzRueckweg.Pruefe(EntityManager,a.Lot,out _,out _,false))
                        {
                            Exklusiv(a, () => { ParkingLotNetzRueckweg.MeldeAnschluesse(EntityManager,a.Lot); a.AnschlussAngestossen = true; return 0; });
                            return;
                        }
                        if (bild-a.Seit >= HintergrundRueckwegfrist.Fenster)
                        {
                            a.Rueckwegpruefungen++;
                            ParkingLotNetzRueckweg.Pruefe(EntityManager,a.Lot,out _,out _,diagnose:true);
                            ParkingLotNetzRueckweg.Melde($"Rueckweg OFFEN: Kanten {ist}/{soll}; Pruefung {a.Rueckwegpruefungen}/{HintergrundRueckwegfrist.MaxPruefungen}; "
                                + $"Lot {a.Lot.Index} bleibt gesperrt; 0 neue Definitionen.");
                            if (!HintergrundRueckwegfrist.Weiter(a.Rueckwegpruefungen))
                            { HalteRueckwegAn(a); return; }
                            a.Seit = bild;
                        }
                        return;
                    }
                    Exklusiv(a, () =>
                    {
                        var n = ParkingLotNetzRueckweg.Erzeuge(EntityManager,a.Lot,a.Id);
                        ParkingLotNetzRueckweg.Melde($"Rueckweg gestartet: alte Kanten {n}; Lot {a.Lot.Index}.");
                        a.RueckwegAusgegeben = true;
                        a.Seit = UnityEngine.Time.frameCount; return n;
                    });
                    break;
            }
        }

        private void Fehler(Auftrag a, Exception e)
        {
            bool uebernommen = a.Phase == Phase.Nebenarbeit
                || a.Phase == Phase.Nachpruefung && ParkingLotNetzRueckweg.Lebt(EntityManager,a.Neu)
                    && EntityManager.HasComponent<ParkingLotBuildReceipt>(a.Neu)
                    && (!EntityManager.Exists(a.Lot) || !EntityManager.HasComponent<Game.Areas.Area>(a.Lot)
                        || EntityManager.HasComponent<Deleted>(a.Lot));
            ParkingLotNetzRueckweg.Melde($"Fehler Auftrag {a.Id}, Phase {a.Phase}: {e.Message}; "
                + (uebernommen ? "dauerhafter Neubau bleibt auf Stand 7 erhalten." : "Rueckweg erforderlich."));
            a.GateOffen = false;
            if (uebernommen)
            {
                // Auch bei einer Ausnahme NACH der irreversiblen Uebernahme
                // niemals den geprueften Neubau samt Zoningbesitzern loeschen.
                EntityManager.SetComponentData(a.Neu,new ParkingLotDatenstand { Stand = 7 });
                EntityManager.AddComponent<ParkingLotNebenarbeitOffen>(a.Neu);
                if (EntityManager.Exists(a.Lot))
                {
                    EntityManager.RemoveComponent<ParkingLotRueckwegkurs>(a.Lot);
                    EntityManager.RemoveComponent<ParkingLotRueckweganschluss>(a.Lot);
                    EntityManager.RemoveComponent<ParkingLotOffenerErsatz>(a.Lot);
                    EntityManager.RemoveComponent<ParkingLotStufeAKnoten>(a.Lot);
                    EntityManager.RemoveComponent<ParkingLotStufeAPrefab>(a.Lot);
                }
                World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeHintergrundEnde(a.Lot,a.Neu,false);
                Ende(a); return;
            }
            if (a.Phase == Phase.Rueckweg)
            {
                HalteRueckwegAn(a); return;
            }
            a.Phase = Phase.Ruecknahme;
        }

        private void HalteRueckwegAn(Auftrag a)
        {
            // Keine Freigabe bei 20/22 und keine neue Definitionsserie. Der
            // serialisierte Schnappschuss bleibt auch nach World-Wechsel erhalten.
            _angehalten.Add(a.Lot);
            World.GetOrCreateSystemManaged<ParkingLotUISystem>().SetStatus(RueckwegHinweis);
            ParkingLotNetzRueckweg.Melde($"Rueckweg ANGEHALTEN Lot {a.Lot.Index}, nach {a.Rueckwegpruefungen} Messfenstern; "
                + "Schnappschuss und Sperre erhalten. " + RueckwegHinweis);
            World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeHintergrundEnde(a.Lot,Entity.Null,false);
            Ende(a);
        }

        private void RueckwegFertig(Auftrag a, int ist, int soll)
        {
            bool gesichert = EntityManager.HasBuffer<ParkingLotRueckwegkurs>(a.Lot);
            if (gesichert)
            {
                a.Bauer ??= ParkingLotToolSystem.Bauarbeiter(World);
                a.Bauer.HintergrundRueckwegFluss(a.Lot);
            }
            ParkingLotNetzRueckweg.FuellTraeger(EntityManager,a.Lot);
            ParkingLotNetzRueckweg.Melde(gesichert
                ? $"Rueckweg geprueft: Kanten {ist}/{soll}, Kurven-/Hoehen-/Anschlussabweichungen 0; Versuch {a.Versuch}/3."
                : $"Auftrag vor eigenem Abriss beendet; kein Rueckweg erforderlich; Versuch {a.Versuch}/3.");
            if (EntityManager.HasBuffer<ParkingLotRueckwegkurs>(a.Lot)) EntityManager.RemoveComponent<ParkingLotRueckwegkurs>(a.Lot);
            if (EntityManager.HasComponent<ParkingLotOffenerErsatz>(a.Lot)) EntityManager.RemoveComponent<ParkingLotOffenerErsatz>(a.Lot);
            if (EntityManager.HasBuffer<ParkingLotRueckweganschluss>(a.Lot)) EntityManager.RemoveComponent<ParkingLotRueckweganschluss>(a.Lot);
            if (EntityManager.HasBuffer<ParkingLotStufeAKnoten>(a.Lot)) EntityManager.RemoveComponent<ParkingLotStufeAKnoten>(a.Lot);
            if (EntityManager.HasComponent<ParkingLotStufeAPrefab>(a.Lot)) EntityManager.RemoveComponent<ParkingLotStufeAPrefab>(a.Lot);
            bool wieder = !a.NurRueckweg && _regel.Wiederholen(a.Lot,true);
            if (!a.NurRueckweg && !wieder) World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeHintergrundEnde(a.Lot,Entity.Null,false);
            if (!a.NurRueckweg && !wieder)
                World.GetOrCreateSystemManaged<ParkingLotUISystem>().SetStatus(gesichert ? ParkingLotTexte.T(
                    "Hintergrundbau nach drei Versuchen beendet. Die alten Wege sind geprueft wiederhergestellt. Fuer einen neuen Versuch den Spielstand neu laden und synchronisieren.",
                    "Background rebuild stopped after three attempts. The restored old paths have been verified. Reload the save and synchronize to try again.") : ParkingLotTexte.T(
                    "Hintergrundbau nach drei Versuchen beendet. Es wurden keine alten Wege abgerissen. Fuer einen neuen Versuch den Spielstand neu laden und synchronisieren.",
                    "Background rebuild stopped after three attempts. No old paths were removed. Reload the save and synchronize to try again."));
            Ende(a);
            if (a.NurRueckweg) World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeRueckwegEnde();
            if (wieder) Einreihen(a.Lot);
        }
        private void Ende(Auftrag a)
        {
            _regel.Ende(a.Lot);
            if (a.Bauer != null)
            {
                if (a.Phase == Phase.Nebenarbeit && a.Bauer.HintergrundBeobachtetFluss) _flussbeobachter.Add(a.Bauer);
                else World.DestroySystemManaged(a.Bauer);
            }
            _aktiv = null;
        }

        protected override void OnGameLoaded(Context context)
        {
            base.OnGameLoaded(context);
            _gate.Verwerfe(_aktiv?.Lot ?? Entity.Null);
            if (_aktiv?.Bauer != null) World.DestroySystemManaged(_aktiv.Bauer);
            foreach (var b in _flussbeobachter) World.DestroySystemManaged(b);
            _flussbeobachter.Clear();
            _aktiv = null; _queue.Clear(); _angehalten.Clear(); _regel = new HintergrundAuftragsregel<Entity>();
            using var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ParkingLotRueckwegkurs>());
            using var lots = query.ToEntityArray(Allocator.Temp);
            foreach (var lot in lots)
            {
                Einreihen(lot,true);
                var a = _queue[_queue.Count-1];
                if (EntityManager.HasComponent<ParkingLotOffenerErsatz>(lot)) a.Phase = Phase.Ruecknahme;
            }
        }

        private void RaumeGespeichertesA(Entity lot)
        {
            if (EntityManager.HasComponent<ParkingLotOffenerErsatz>(lot)
                || !EntityManager.HasBuffer<ParkingLotStufeAKnoten>(lot)
                || !EntityManager.HasComponent<ParkingLotStufeAPrefab>(lot)) return;
            var stufeA = EntityManager.GetBuffer<ParkingLotStufeAKnoten>(lot,true);
            var punkte = new Unity.Mathematics.float3[stufeA.Length];
            for (var i = 0; i < stufeA.Length; i++) punkte[i] = stufeA[i].Position;
            var prefab = EntityManager.GetComponentData<ParkingLotStufeAPrefab>(lot).Prefab;
            using var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Game.Areas.Area>(),
                ComponentType.ReadOnly<Game.Areas.Node>(), ComponentType.ReadOnly<Game.Prefabs.PrefabRef>(),
                ComponentType.Exclude<Game.Tools.Temp>(),ComponentType.Exclude<Deleted>());
            using var areas = q.ToEntityArray(Allocator.Temp);
            foreach (var e in areas)
                if (e != lot && !EntityManager.HasComponent<ParkingLotBuildReceipt>(e)
                    && EntityManager.GetComponentData<Game.Prefabs.PrefabRef>(e).m_Prefab == prefab
                    && ParkingLotToolSystem.AreaNodesMatch(punkte,EntityManager.GetBuffer<Game.Areas.Node>(e,true)))
                { EntityManager.AddComponent<Deleted>(e); ParkingLotNetzRueckweg.Melde($"Gespeicherte leere Stufe-A-Flaeche {e.Index} zum Abriss angemeldet."); }
        }
    }
}
