using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Colossal.Mathematics;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private bool AvAnschlussLayer(Entity strassenprefab, Entity leitung,
            out bool strasseAktualisiert, out bool knotenVerbunden)
        {
            var strasse = EntityManager.GetComponentData<NetData>(strassenprefab);
            var netz = EntityManager.GetComponentData<NetData>(leitung);
            var lokal = EntityManager.GetComponentData<LocalConnectData>(leitung);
            // GenerateNodesSystem.EdgeIterator und
            // GenerateEdgesSystem.FindNodeConnections pruefen verschiedene
            // Layerpaare. Ein Zwischenstueck kann auch ohne ConnectedNode
            // direkt an einem Strassenknoten entstehen.
            strasseAktualisiert = (lokal.m_Layers & strasse.m_ConnectLayers) != 0
                && (netz.m_ConnectLayers & strasse.m_LocalConnectLayers) != 0;
            knotenVerbunden = (netz.m_ConnectLayers & strasse.m_ConnectLayers) != 0
                && (lokal.m_Layers & strasse.m_LocalConnectLayers) != 0;
            return strasseAktualisiert;
        }

        private bool AvZieltor(Entity strasse, Entity leitung, float2 ende,
            out float t, out float abstand)
        {
            t = 0; abstand = float.MaxValue;
            if (!EntityManager.HasComponent<Curve>(strasse)
                || !EntityManager.HasComponent<PrefabRef>(strasse)) return false;
            var prefab = EntityManager.GetComponentData<PrefabRef>(strasse).m_Prefab;
            if (!EntityManager.HasComponent<NetGeometryData>(prefab)
                || !EntityManager.HasComponent<NetData>(prefab)) return false;
            var g = EntityManager.GetComponentData<NetGeometryData>(prefab);
            var lc = EntityManager.GetComponentData<LocalConnectData>(leitung);
            var lg = EntityManager.GetComponentData<NetGeometryData>(leitung);
            var b = EntityManager.GetComponentData<Curve>(strasse).m_Bezier;
            abstand = MathUtils.Distance(b.xz, ende, out t);
            // GenerateEdgesSystem:1807-1814 verwendet bei NoEdgeConnection
            // genau 2 Endpunkte; eine Projektion in die Mitte waere falsch.
            if ((g.m_Flags & GeometryFlags.NoEdgeConnection) != 0)
            {
                t = math.distance(b.a.xz, ende) < math.distance(b.d.xz, ende) ? 0 : 1;
                abstand = math.distance(MathUtils.Position(b, t).xz, ende);
            }
            return VersorgungskursPruefung.Anschluss(abstand, g.m_DefaultWidth,
                lg.m_DefaultWidth, lc.m_SearchDistance,
                AvAnschlussLayer(prefab, leitung, out _, out _));
        }

        private void AvMesseZielumfeld(AvKurs kurs, Entity knoten)
        {
            var ziel = kurs.Trasse.Zielkante;
            var p = EntityManager.HasComponent<Node>(knoten)
                ? EntityManager.GetComponentData<Node>(knoten).m_Position : kurs.Ende;
            var geometrie = AvZieltor(ziel, kurs.Prefab, p.xz, out var t, out var abstand);
            var lc = EntityManager.GetComponentData<LocalConnectData>(kurs.Prefab);
            var breite = EntityManager.GetComponentData<NetGeometryData>(kurs.Prefab).m_DefaultWidth;
            var zielprefab = EntityManager.GetComponentData<PrefabRef>(ziel).m_Prefab;
            var zg = EntityManager.GetComponentData<NetGeometryData>(zielprefab);
            AvAnschlussLayer(zielprefab, kurs.Prefab, out var aktualisiert, out var verbunden);
            var hoehe = MathUtils.Position(EntityManager.GetComponentData<Curve>(ziel).m_Bezier, t).y - p.y;
            var temps = 0;
            using var kanten = AvTempQuery().ToEntityArray(Allocator.Temp);
            foreach (var e in kanten)
            {
                var temp = EntityManager.GetComponentData<Temp>(e);
                if (temp.m_Original != ziel) continue;
                temps++;
                var edge = EntityManager.GetComponentData<Edge>(e);
                var gemeinsam = edge.m_Start == knoten || edge.m_End == knoten;
                var eintraege = 0;
                var verbindungen = 0;
                if (EntityManager.HasBuffer<ConnectedNode>(e))
                    foreach (var n in EntityManager.GetBuffer<ConnectedNode>(e, true))
                    {
                        eintraege++;
                        if (n.m_Node == knoten) verbindungen++;
                    }
                AvZieltor(e, kurs.Prefab, p.xz, out var tt, out var da);
                Mod.log.Info($"PLT-Autoversorgung ZIEL-TEMP [{kurs.Name}]: {e}, Flags {temp.m_Flags}, "
                    + $"t {tt:F5}, Achsabstand {da:F3} m, gemeinsamer Knoten {(gemeinsam ? 1 : 0)}, "
                    + $"ConnectedNode {verbindungen}/{eintraege}.");
            }
            Mod.log.Info($"PLT-Autoversorgung ZIELTORE [{kurs.Name}]: Original {ziel}, "
                + $"Knoten {knoten}, LocalConnect {(EntityManager.HasComponent<LocalConnect>(knoten) ? 1 : 0)}, "
                + $"Temp-Strassen {temps}, Layer Strasse/ConnectedNode "
                + $"{(aktualisiert ? 1 : 0)}/{(verbunden ? 1 : 0)}, "
                + $"t {t:F5}, Randabstand {abstand - zg.m_DefaultWidth / 2:F3} m / "
                + $"Suchradius {math.max(0, breite / 2 + lc.m_SearchDistance):F3} m, "
                + $"Hoehe {hoehe:F3} m / Fenster {lc.m_HeightRange.min:F3}..{lc.m_HeightRange.max:F3}, "
                + $"Geometrie+Layer {(geometrie ? 1 : 0)}; Endversatz {math.distance(kurs.Ende.xz, kurs.Trasse.Ziel.xz):F3} m.");
        }
    }
}
