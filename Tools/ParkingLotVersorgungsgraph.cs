using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        /**
         * DIE NACHBARSCHAFT IST NICHT DIE LEITUNG.
         *
         * Am 2026-09-05 um 17:54 hielt der Anschluss zum ersten Mal
         * ("2 von 2 ... haengen wieder an einer neuen Strasse"), und der
         * Nutzer meldete trotzdem weiter fehlenden Strom und fehlendes Wasser.
         * Das ist kein Widerspruch: `ConnectedNode` und `ConnectedEdge` sind
         * nur die geometrische Nachbarschaft. Wer wirklich leitet, steht in
         * einem zweiten, eigenen Netz.
         *
         * Belegt in `Game.Simulation.ElectricityEdgeGraphSystem` (Wasser
         * gleichlautend in `WaterPipeEdgeGraphSystem`):
         *
         *   - Seine Abfrage ist `ElectricityConnection + Edge + PrefabRef +
         *     Created`, ohne `Temp`. Sie laeuft also NUR in dem Frame, in dem
         *     eine Kante ENTSTEHT - nicht, wenn sie sich spaeter aendert.
         *   - `CreateEdgeMiddleNodeConnections` geht dabei den
         *     `ConnectedNode`-Puffer der Kante durch und legt fuer jeden
         *     angehaengten Leitungsknoten eine FLUSSKANTE an.
         *
         * Unsere neuen Strassen entstehen beim Uebernehmen. In genau diesem
         * Frame ist ihr `ConnectedNode`-Puffer noch leer - wir tragen den
         * Anschluss erst ein paar Frames spaeter ein, wenn Lot und Traeger
         * dauerhaft sind. Die Flusskante wurde deshalb nie gebaut: Nachbarn
         * ja, Leitung nein.
         *
         * Also wird sie hier gebaut, mit CS2s eigenem Werkzeug
         * (`ElectricityGraphUtils` / `WaterPipeGraphUtils`), zwischen genau
         * denselben zwei Flussknoten, die `CreateEdgeMiddleNodeConnections`
         * verbunden haette, und mit den Kennwerten desselben Leitungsprefabs.
         *
         * Fehlt der Strassenkante ihr Flussknoten, dann fuehrt unsere Strasse
         * diese Versorgungsart ueberhaupt nicht - ein anderer Mangel, und
         * einer, den diese Meldung benennt statt ihn zu verschlucken.
         */
        private void VerbindeVersorgungsgraph(Entity kante, Entity knoten,
            Entity leitungsprefab, string leitung)
        {
            if (Mod.Aus("versorgung-graphhelfer"))
            {
                AvDiagnoseMeldung($"GRAPHHELFER-AUS {leitung}: {kante} / {knoten}; 0 Flusskanten durch PLT erzeugt.");
                return;
            }
            AvDiagnoseMeldung($"GRAPHHELFER-BEGIN {leitung}: {kante} / {knoten}, Prefab {leitungsprefab}.");
            if (EntityManager.HasComponent<ElectricityConnectionData>(
                    leitungsprefab))
            {
                if (!EntityManager.HasComponent<
                        Game.Simulation.ElectricityNodeConnection>(kante))
                    Mod.log.Warn($"PLT-Versorgungsgraph: unsere Kante "
                        + $"#{kante.Index} hat keinen Stromflussknoten - sie "
                        + "fuehrt gar keinen Strom. Der Anschluss von "
                        + $"{leitung} kann nichts leiten.");
                else if (!EntityManager.HasComponent<
                        Game.Simulation.ElectricityNodeConnection>(knoten))
                    Mod.log.Warn($"PLT-Versorgungsgraph: {leitung} "
                        + $"#{knoten.Index} hat keinen Stromflussknoten.");
                else
                {
                    var kantenknoten = EntityManager.GetComponentData<
                        Game.Simulation.ElectricityNodeConnection>(kante)
                        .m_ElectricityNode;
                    var leitungsknoten = EntityManager.GetComponentData<
                        Game.Simulation.ElectricityNodeConnection>(knoten)
                        .m_ElectricityNode;
                    if (VersorgungsflussBereit(leitungsknoten, kantenknoten, "Strom")
                        && StromflusskanteFehlt(leitungsknoten, kantenknoten))
                    {
                        var daten = EntityManager.GetComponentData<
                            ElectricityConnectionData>(leitungsprefab);
                        var fluss = World.GetOrCreateSystemManaged<
                            Game.Simulation.ElectricityFlowSystem>();
                        var neu = Game.Simulation.ElectricityGraphUtils.CreateFlowEdge(
                            EntityManager, fluss.edgeArchetype,
                            leitungsknoten, kantenknoten,
                            daten.m_Direction, daten.m_Capacity);
                        AvDiagnoseMeldung($"GRAPHHELFER-STROM {neu}: {leitungsknoten} -> {kantenknoten}.");
                        Mod.log.Info($"PLT-Versorgungsgraph: Stromflusskante "
                            + $"fuer {leitung} #{knoten.Index} an Kante "
                            + $"#{kante.Index} angelegt (Kapazitaet "
                            + $"{daten.m_Capacity}, Richtung "
                            + $"{daten.m_Direction}).");
                    }
                }
            }

            if (!EntityManager.HasComponent<WaterPipeConnectionData>(
                    leitungsprefab)) return;
            if (!EntityManager.HasComponent<
                    Game.Simulation.WaterPipeNodeConnection>(kante))
            {
                Mod.log.Warn($"PLT-Versorgungsgraph: unsere Kante "
                    + $"#{kante.Index} hat keinen Wasserflussknoten - sie "
                    + $"fuehrt gar kein Wasser. Der Anschluss von {leitung} "
                    + "kann nichts leiten.");
                return;
            }
            if (!EntityManager.HasComponent<
                    Game.Simulation.WaterPipeNodeConnection>(knoten))
            {
                Mod.log.Warn($"PLT-Versorgungsgraph: {leitung} "
                    + $"#{knoten.Index} hat keinen Wasserflussknoten.");
                return;
            }
            var wKantenknoten = EntityManager.GetComponentData<
                Game.Simulation.WaterPipeNodeConnection>(kante)
                .m_WaterPipeNode;
            var wLeitungsknoten = EntityManager.GetComponentData<
                Game.Simulation.WaterPipeNodeConnection>(knoten)
                .m_WaterPipeNode;
            if (!VersorgungsflussBereit(wLeitungsknoten, wKantenknoten, "Wasser")) return;
            if (!WasserflusskanteFehlt(wLeitungsknoten, wKantenknoten)) return;
            var wDaten = EntityManager
                .GetComponentData<WaterPipeConnectionData>(leitungsprefab);
            var wFluss = World.GetOrCreateSystemManaged<
                Game.Simulation.WaterPipeFlowSystem>();
            var wNeu = Game.Simulation.WaterPipeGraphUtils.CreateFlowEdge(
                EntityManager, wFluss.edgeArchetype,
                wLeitungsknoten, wKantenknoten,
                wDaten.m_FreshCapacity, wDaten.m_SewageCapacity);
            AvDiagnoseMeldung($"GRAPHHELFER-WASSER {wNeu}: {wLeitungsknoten} -> {wKantenknoten}.");
            Mod.log.Info($"PLT-Versorgungsgraph: Wasserflusskante fuer "
                + $"{leitung} #{knoten.Index} an Kante #{kante.Index} angelegt "
                + $"(frisch {wDaten.m_FreshCapacity}, Abwasser "
                + $"{wDaten.m_SewageCapacity}).");
        }

        /** Gibt es zwischen den beiden Flussknoten noch KEINE Stromkante? */
        private bool StromflusskanteFehlt(Entity a, Entity b)
        {
            if (a == Entity.Null || b == Entity.Null) return false;
            if (!EntityManager.HasBuffer<Game.Simulation.ConnectedFlowEdge>(a))
                return false;
            var kanten = EntityManager
                .GetBuffer<Game.Simulation.ConnectedFlowEdge>(a, true);
            for (var i = 0; i < kanten.Length; i++)
            {
                if (!EntityManager.HasComponent<
                        Game.Simulation.ElectricityFlowEdge>(kanten[i].m_Edge))
                    continue;
                var f = EntityManager.GetComponentData<
                    Game.Simulation.ElectricityFlowEdge>(kanten[i].m_Edge);
                if ((f.m_Start == a && f.m_End == b)
                    || (f.m_Start == b && f.m_End == a)) return false;
            }
            return true;
        }

        /** Dasselbe fuer Wasser und Abwasser. */
        private bool WasserflusskanteFehlt(Entity a, Entity b)
        {
            if (a == Entity.Null || b == Entity.Null) return false;
            if (!EntityManager.HasBuffer<Game.Simulation.ConnectedFlowEdge>(a))
                return false;
            var kanten = EntityManager
                .GetBuffer<Game.Simulation.ConnectedFlowEdge>(a, true);
            for (var i = 0; i < kanten.Length; i++)
            {
                if (!EntityManager.HasComponent<
                        Game.Simulation.WaterPipeEdge>(kanten[i].m_Edge))
                    continue;
                var f = EntityManager.GetComponentData<
                    Game.Simulation.WaterPipeEdge>(kanten[i].m_Edge);
                if ((f.m_Start == a && f.m_End == b)
                    || (f.m_Start == b && f.m_End == a)) return false;
            }
            return true;
        }

    }
}
