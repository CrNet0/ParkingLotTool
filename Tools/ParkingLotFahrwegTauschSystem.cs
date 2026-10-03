using System.Collections.Generic;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Tools;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /*
     * STEUERUNG DES FAHRWEGTAUSCHS (Sync-Schritt 9).
     *
     * Wunsch des Nutzers (2026-10-04): der Sync schliesst das Panel selbst,
     * zeigt seinen Fortschritt, tauscht, und oeffnet das Panel danach
     * wieder. Deshalb wird kurz das Tauschwerkzeug aktiv (das schliesst ein
     * offenes PLT-Panel regulaer) und danach das vorige Werkzeug wieder.
     * Gestartet wird nur, wenn das PLT-Werkzeug gerade nichts zeichnet oder
     * bearbeitet - ein Werkzeugwechsel wuerde das sonst verwerfen.
     */
    public sealed partial class ParkingLotFahrwegTauschSystem : GameSystemBase
    {
        private readonly List<Entity> _warteschlange = new();
        private ToolSystem _toolSystem;
        private ParkingLotFahrwegTauschWerkzeug _werkzeug;
        private ParkingLotToolSystem _plt;
        private PrefabSystem _prefabs;
        private ToolBaseSystem _vorher;
        private bool _alleOk = true;
        private int _ruheBis;

        internal int Offen => _warteschlange.Count + (_werkzeug != null && _werkzeug.Beschaeftigt ? 1 : 0);

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _werkzeug = World.GetOrCreateSystemManaged<ParkingLotFahrwegTauschWerkzeug>();
            _plt = World.GetOrCreateSystemManaged<ParkingLotToolSystem>();
            _prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            _werkzeug.Fertig = Fertig;
        }

        internal void Einreihen(Entity lot)
        {
            if (_warteschlange.Contains(lot)) return;
            _warteschlange.Add(lot);
            Mod.log.Info($"PLT-Fahrwegtausch: Lot {lot.Index} eingereiht; offen {Offen}.");
        }

        /** Die alten Vanilla-Fahrwege eines Lots samt Ziel-Klon und Besitzer. */
        internal List<(Entity Kante, Entity Ziel, Entity Besitzer)> Plan(Entity lot)
        {
            var plan = new List<(Entity, Entity, Entity)>();
            if (!EntityManager.Exists(lot) || !EntityManager.HasComponent<ParkingLotCarrierReference>(lot)) return plan;
            var fahr = World.GetOrCreateSystemManaged<ParkingLotFahrprefabSystem>();
            var gesehen = new HashSet<Entity>();
            var traeger = EntityManager.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
            foreach (var besitzer in new[] { lot, traeger })
            {
                if (!EntityManager.Exists(besitzer) || !EntityManager.HasBuffer<Game.Net.SubNet>(besitzer)) continue;
                var netze = EntityManager.GetBuffer<Game.Net.SubNet>(besitzer, true);
                for (var i = 0; i < netze.Length; i++)
                {
                    var e = netze[i].m_SubNet;
                    if (!gesehen.Add(e) || !EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)
                        || EntityManager.HasComponent<Temp>(e) || !EntityManager.HasComponent<Edge>(e)
                        || !EntityManager.HasComponent<PrefabRef>(e) || !EntityManager.HasComponent<Owner>(e)) continue;
                    var name = _prefabs.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab);
                    if (!ParkingLotFahrregeln.IstFahrweg(name) || !fahr.TryWeg(name, out var ziel)) continue;
                    plan.Add((e, ziel, EntityManager.GetComponentData<Owner>(e).m_Owner));
                }
            }
            return plan;
        }

        internal bool BrauchtTausch(Entity lot) => Plan(lot).Count > 0;

        [Preserve]
        protected override void OnUpdate()
        {
            var spiel = GameManager.instance;
            if (spiel == null || spiel.isGameLoading || !spiel.gameMode.IsGame()) return;
            if (_werkzeug.Beschaeftigt || _warteschlange.Count == 0) return;
            if (UnityEngine.Time.frameCount < _ruheBis) return;
            // Ein Werkzeugwechsel wuerde einen laufenden Entwurf verwerfen.
            if (_toolSystem.activeTool == _plt && _plt.ArbeitetGerade) return;
            var lot = _warteschlange[0];
            _warteschlange.RemoveAt(0);
            var plan = Plan(lot);
            if (plan.Count == 0) { Fertig(lot, true, "nichts zu tauschen"); return; }
            if (_toolSystem.activeTool != _werkzeug) _vorher = _toolSystem.activeTool;
            _werkzeug.Starte(lot, plan);
            _toolSystem.activeTool = _werkzeug;
        }

        private void Fertig(Entity lot, bool ok, string text)
        {
            if (!ok) _alleOk = false;
            World.GetOrCreateSystemManaged<ParkingLotSyncSystem>().MeldeHintergrundEnde(lot, lot, ok);
            _ruheBis = UnityEngine.Time.frameCount + 10;
            if (_warteschlange.Count > 0) return;
            // Alles erledigt: voriges Werkzeug zurueck (oeffnet ein vorher
            // offenes PLT-Panel wieder). Nach einem Fehlschlag bleibt das
            // Panel zu, damit die Meldung sichtbar bleibt.
            var zurueck = _alleOk && _vorher != null && _vorher != _werkzeug ? _vorher : null;
            if (_toolSystem.activeTool == _werkzeug)
                _toolSystem.activeTool = zurueck ?? World.GetOrCreateSystemManaged<DefaultToolSystem>();
            _vorher = null;
            _alleOk = true;
        }
    }

    public sealed partial class ParkingLotToolSystem
    {
        /** Zeichnet oder bearbeitet das Werkzeug gerade? Dann darf kein Sync-Tausch das Werkzeug wechseln. */
        internal bool ArbeitetGerade => IsEditing || _pendingEditLot != Entity.Null || _points.Count > 0
            || _buildStage != BuildStage.Idle || _buildTask != null;
    }
}
