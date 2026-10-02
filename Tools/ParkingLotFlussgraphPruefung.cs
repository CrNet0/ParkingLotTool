using System.Collections.Generic;
using System.Text;
using Game;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * TOTE VERWEISE IM VERSORGUNGSGRAPHEN - NUR LESEN.
     *
     * Bis 879139f markierte der Leitungsabriss alte Leitungen erst in
     * Modification2; Vanillas GraphDelete laeuft schon in Modification1. Die
     * Flussgraph-Eintraege dieser Leitungen blieben stehen und werden im
     * Spielstand mitgespeichert. Verdacht (2026-10-03): eine neue Leitung an
     * derselben Stelle laesst CS2 so einen Eintrag lesen -> nativer Absturz im
     * Bild der Uebernahme. Diese Pruefung zaehlt die Reste einmal nach jedem
     * Laden; sie aendert nichts.
     */
    public sealed partial class ParkingLotFlussgraphPruefungSystem : GameSystemBase
    {
        private int _pruefenAb = -1;

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode.IsGame()) _pruefenAb = UnityEngine.Time.frameCount + 60;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_pruefenAb < 0 || UnityEngine.Time.frameCount < _pruefenAb) return;
            _pruefenAb = -1;
            Pruefe();
        }

        internal void Pruefe()
        {
            var text = new StringBuilder();
            var beispiele = new List<string>();
            int strom = Kanten<ElectricityFlowEdge, ElectricityFlowNode>(
                e => (e.m_Start, e.m_End), "Strom", beispiele, out var stromAlle);
            int wasser = Kanten<WaterPipeEdge, WaterPipeNode>(
                e => (e.m_Start, e.m_End), "Wasser", beispiele, out var wasserAlle);
            int puffer = Flusspuffer(beispiele, out var pufferAlle);
            int anschluss = Anschluesse<ElectricityNodeConnection>(c => c.m_ElectricityNode, "Strom-Knotenanschluss", beispiele)
                + Anschluesse<WaterPipeNodeConnection>(c => c.m_WaterPipeNode, "Wasser-Knotenanschluss", beispiele)
                + Anschluesse<ElectricityValveConnection>(c => c.m_ValveNode, "Strom-Ventil", beispiele)
                + Anschluesse<WaterPipeValveConnection>(c => c.m_ValveNode, "Wasser-Ventil", beispiele);
            var summe = strom + wasser + puffer + anschluss;
            text.Append("PLT-Flussgraph-Pruefung (nur lesen): ")
                .Append(stromAlle).Append(" Strom-Flusskanten, davon ").Append(strom).Append(" mit totem Ende; ")
                .Append(wasserAlle).Append(" Wasser-Flusskanten, davon ").Append(wasser).Append(" mit totem Ende; ")
                .Append(pufferAlle).Append(" Flussknoten-Puffer, ").Append(puffer).Append(" tote Eintraege; ")
                .Append(anschluss).Append(" Anschluesse an tote Flussknoten.");
            if (beispiele.Count > 0) text.Append(" Beispiele: ").Append(string.Join(" | ", beispiele));
            if (summe > 0) Mod.log.Warn(text.ToString()); else Mod.log.Info(text.ToString());
            ParkingLotSchrittmarke.Setze("Flussgraph: " + summe + " tote Verweise");
            PruefeNetzbesitzer();
        }

        /**
         * Besitzer ohne SubNet-Puffer = nativer Absturz, sobald Vanilla das Netz
         * verarbeitet (Memory "cs2-subnet-subobject-puffer"; nackte Traeger
         * verlieren den Puffer beim Laden, "cs2-nackter-traeger"). Messung
         * 2026-10-03: am grossen Parkplatz haengen alte Vanilla-Wege mit
         * fremdem Besitzer 63223 an der Zoningstrasse. Nur lesen.
         */
        private void PruefeNetzbesitzer()
        {
            using var q = EntityManager.CreateEntityQuery(
                new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<Game.Common.Owner>(), ComponentType.ReadOnly<Game.Prefabs.PrefabRef>() },
                    Any = new[] { ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<Game.Net.Node>() },
                    None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
                });
            using var netze = q.ToEntityArray(Allocator.Temp);
            var besitzer = new Dictionary<Entity, int>();
            int tot = 0, ohnePuffer = 0;
            foreach (var n in netze)
            {
                var o = EntityManager.GetComponentData<Game.Common.Owner>(n).m_Owner;
                if (o == Entity.Null) continue;
                bool lebt = Lebt(o);
                if (lebt && EntityManager.HasBuffer<Game.Net.SubNet>(o)) continue;
                if (!lebt) tot++; else ohnePuffer++;
                besitzer[o] = besitzer.TryGetValue(o, out var c) ? c + 1 : 1;
            }
            var text = new StringBuilder("PLT-Netzbesitzer-Pruefung (nur lesen): ")
                .Append(netze.Length).Append(" Netzteile mit Besitzer; ")
                .Append(tot).Append(" mit totem Besitzer, ")
                .Append(ohnePuffer).Append(" mit Besitzer OHNE SubNet-Puffer.");
            int i = 0;
            foreach (var p in besitzer)
            {
                if (i++ >= 6) break;
                text.Append(" | Besitzer ").Append(p.Key).Append(" (").Append(p.Value).Append(" Netzteile): ");
                if (!Lebt(p.Key)) { text.Append("existiert nicht"); continue; }
                using var typen = EntityManager.GetComponentTypes(p.Key, Allocator.Temp);
                var namen = new List<string>();
                foreach (var t in typen) namen.Add(t.GetManagedType()?.Name ?? t.ToString());
                text.Append(string.Join(",", namen));
            }
            if (tot + ohnePuffer > 0) Mod.log.Warn(text.ToString()); else Mod.log.Info(text.ToString());
            ParkingLotSchrittmarke.Setze("Netzbesitzer: " + (tot + ohnePuffer) + " ohne SubNet-Puffer/tot");
        }

        private bool Lebt(Entity e)
            => e != Entity.Null && EntityManager.Exists(e) && !EntityManager.HasComponent<Deleted>(e);

        private int Kanten<TKante, TKnoten>(System.Func<TKante, (Entity, Entity)> enden, string art,
            List<string> beispiele, out int alle)
            where TKante : unmanaged, IComponentData where TKnoten : unmanaged, IComponentData
        {
            using var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TKante>());
            using var kanten = q.ToEntityArray(Allocator.Temp);
            alle = kanten.Length;
            int tot = 0;
            foreach (var k in kanten)
            {
                var (a, b) = enden(EntityManager.GetComponentData<TKante>(k));
                bool aOk = Lebt(a) && EntityManager.HasComponent<TKnoten>(a);
                bool bOk = Lebt(b) && EntityManager.HasComponent<TKnoten>(b);
                if (aOk && bOk) continue;
                tot++;
                if (beispiele.Count < 8) beispiele.Add($"{art}-Kante {k} Start {a}{(aOk ? "" : "!")} Ende {b}{(bOk ? "" : "!")}");
            }
            return tot;
        }

        private int Flusspuffer(List<string> beispiele, out int alle)
        {
            using var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ConnectedFlowEdge>());
            using var knoten = q.ToEntityArray(Allocator.Temp);
            alle = knoten.Length;
            int tot = 0;
            foreach (var n in knoten)
            {
                var puffer = EntityManager.GetBuffer<ConnectedFlowEdge>(n, true);
                for (var i = 0; i < puffer.Length; i++)
                {
                    var k = puffer[i].m_Edge;
                    if (Lebt(k) && (EntityManager.HasComponent<ElectricityFlowEdge>(k)
                        || EntityManager.HasComponent<WaterPipeEdge>(k))) continue;
                    tot++;
                    if (beispiele.Count < 8) beispiele.Add($"Flussknoten {n} -> tote Kante {k}");
                }
            }
            return tot;
        }

        private int Anschluesse<T>(System.Func<T, Entity> ziel, string art, List<string> beispiele)
            where T : unmanaged, IComponentData
        {
            using var q = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<T>(),
                ComponentType.Exclude<Deleted>());
            using var teile = q.ToEntityArray(Allocator.Temp);
            int tot = 0;
            foreach (var e in teile)
            {
                var n = ziel(EntityManager.GetComponentData<T>(e));
                if (n == Entity.Null || Lebt(n)) continue;
                tot++;
                if (beispiele.Count < 8) beispiele.Add($"{art} {e} -> toter Flussknoten {n}");
            }
            return tot;
        }
    }
}
