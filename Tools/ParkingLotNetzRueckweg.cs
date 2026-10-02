using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using SubNet = Game.Net.SubNet;

namespace ParkingLotTool.Tools
{
    /** Liegt bis zum Erfolg am ALTEN Lot, auch im Save. Keine Temp-ID wird
     *  gespeichert. Eigene entfernte Knoten bekommen im Rueckweg neue IDs. */
    [InternalBufferCapacity(0)]
    public struct ParkingLotRueckwegkurs : IBufferElementData, ISerializable
    {
        public Entity Kante, Prefab, Besitzer, Start, Ende;
        public NetCourse Kurs;
        public bool HatUpgrade;
        public Upgraded Upgrade;
        public void Serialize<TWriter>(TWriter w) where TWriter : IWriter
        {
            w.Write(1); w.Write(Kante); w.Write(Prefab); w.Write(Besitzer);
            w.Write(Start); w.Write(Ende);
            w.Write(Kurs.m_Curve.a); w.Write(Kurs.m_Curve.b);
            w.Write(Kurs.m_Curve.c); w.Write(Kurs.m_Curve.d);
            w.Write(Kurs.m_Length); w.Write(Kurs.m_Elevation); w.Write(HatUpgrade);
            w.Write((uint)Upgrade.m_Flags.m_General);
            w.Write((uint)Upgrade.m_Flags.m_Left); w.Write((uint)Upgrade.m_Flags.m_Right);
            SchreibePos(w, Kurs.m_StartPosition); SchreibePos(w, Kurs.m_EndPosition);
        }
        private static void SchreibePos<TWriter>(TWriter w, CoursePos p) where TWriter : IWriter
        {
            w.Write(p.m_Position); w.Write(p.m_Rotation); w.Write(p.m_Elevation);
            w.Write((uint)p.m_Flags);
        }
        public void Deserialize<TReader>(TReader r) where TReader : IReader
        {
            r.Read(out int version); if (version != 1) throw new InvalidOperationException("Rueckwegversion");
            r.Read(out Kante); r.Read(out Prefab); r.Read(out Besitzer);
            r.Read(out Start); r.Read(out Ende);
            r.Read(out float3 a); r.Read(out float3 b); r.Read(out float3 c); r.Read(out float3 d);
            r.Read(out float laenge); r.Read(out float2 elevation); r.Read(out HatUpgrade);
            r.Read(out uint general); r.Read(out uint links); r.Read(out uint rechts);
            Upgrade = new Upgraded { m_Flags = new CompositionFlags((CompositionFlags.General)general,
                (CompositionFlags.Side)links, (CompositionFlags.Side)rechts) };
            Kurs = new NetCourse { m_Curve = new Bezier4x3(a,b,c,d), m_Length = laenge,
                m_Elevation = elevation, m_FixedIndex = -1,
                m_StartPosition = LiesPos(r, Start, true), m_EndPosition = LiesPos(r, Ende, false) };
        }
        private static CoursePos LiesPos<TReader>(TReader r, Entity original, bool start) where TReader : IReader
        {
            r.Read(out float3 p); r.Read(out quaternion q); r.Read(out float2 e); r.Read(out uint flags);
            return new CoursePos { m_Entity = original, m_Position = p, m_Rotation = q,
                m_Elevation = e, m_Flags = (CoursePosFlags)flags, m_ParentMesh = -1,
                m_CourseDelta = start ? 0 : 1 };
        }
    }

    internal static class ParkingLotNetzRueckweg
    {
        internal static void Sichere(EntityManager em, Entity lot, IEnumerable<Entity> teile,
            HashSet<Entity> erhalten)
        {
            if (em.HasBuffer<ParkingLotRueckwegkurs>(lot)) return;
            var kandidaten = new HashSet<Entity>(teile);
            var kurse = new List<ParkingLotRueckwegkurs>();
            var anschluesse = new List<ParkingLotRueckweganschluss>();
            int eigene = 0, fremde = 0;
            var knoten = new HashSet<Entity>();
            foreach (var e in kandidaten)
            {
                bool eigenerBesitzer = em.HasComponent<Owner>(e) && ParkingLotBesitz.GehoertZu(em,em.GetComponentData<Owner>(e).m_Owner,lot);
                if (!RueckwegKnotenregel.KursSichern(eigenerBesitzer,em.HasComponent<Edge>(e),
                    em.HasComponent<Deleted>(e),em.HasComponent<Temp>(e),erhalten.Contains(e))) continue;
                if (em.HasBuffer<ConnectedNode>(e))
                    foreach (var a in em.GetBuffer<ConnectedNode>(e,true))
                    {
                        bool eigen = em.HasComponent<Owner>(a.m_Node) && ParkingLotBesitz.GehoertZu(em,em.GetComponentData<Owner>(a.m_Node).m_Owner,lot);
                        bool original = RueckwegKnotenregel.OriginalVerwenden(eigen,!erhalten.Contains(a.m_Node));
                        anschluesse.Add(new ParkingLotRueckweganschluss { Kursindex = kurse.Count,
                            Knoten = original ? a.m_Node : Entity.Null,Lage = a.m_CurvePosition,
                            Prefab = em.GetComponentData<PrefabRef>(a.m_Node).m_Prefab,
                            Besitzer = em.HasComponent<Owner>(a.m_Node) ? em.GetComponentData<Owner>(a.m_Node).m_Owner : Entity.Null,
                            Position = em.GetComponentData<Game.Net.Node>(a.m_Node).m_Position });
                    }
                var edge = em.GetComponentData<Edge>(e);
                var curve = em.GetComponentData<Curve>(e);
                Entity Original(Entity n)
                {
                    var eigen = em.HasComponent<Owner>(n) && ParkingLotBesitz.GehoertZu(em,em.GetComponentData<Owner>(n).m_Owner,lot);
                    if (knoten.Add(n)) { if (eigen && !erhalten.Contains(n)) eigene++; else fremde++; }
                    return RueckwegKnotenregel.OriginalVerwenden(eigen, !erhalten.Contains(n)) ? n : Entity.Null;
                }
                var start = Original(edge.m_Start); var ende = Original(edge.m_End);
                var elevation = em.HasComponent<Elevation>(e) ? em.GetComponentData<Elevation>(e).m_Elevation : float2.zero;
                CoursePos Pos(Entity n, Entity original, float3 p, bool erste)
                {
                    var node = em.GetComponentData<Game.Net.Node>(n);
                    return new CoursePos { m_Entity = original, m_Position = p, m_Rotation = node.m_Rotation,
                        m_Elevation = em.HasComponent<Elevation>(n) ? em.GetComponentData<Elevation>(n).m_Elevation : elevation,
                        m_ParentMesh = -1, m_CourseDelta = erste ? 0 : 1,
                        m_Flags = erste ? CoursePosFlags.IsFirst : CoursePosFlags.IsLast };
                }
                kurse.Add(new ParkingLotRueckwegkurs { Kante = e,
                    Prefab = em.GetComponentData<PrefabRef>(e).m_Prefab, Besitzer = em.GetComponentData<Owner>(e).m_Owner,
                    Start = start, Ende = ende, HatUpgrade = em.HasComponent<Upgraded>(e),
                    Upgrade = em.HasComponent<Upgraded>(e) ? em.GetComponentData<Upgraded>(e) : default,
                    Kurs = new NetCourse { m_Curve = curve.m_Bezier, m_Length = curve.m_Length,
                        m_FixedIndex = -1, m_Elevation = elevation,
                        m_StartPosition = Pos(edge.m_Start, start, curve.m_Bezier.a, true),
                        m_EndPosition = Pos(edge.m_End, ende, curve.m_Bezier.d, false) } });
            }
            var buffer = em.AddBuffer<ParkingLotRueckwegkurs>(lot);
            foreach (var kurs in kurse) buffer.Add(kurs);
            var seiten = em.AddBuffer<ParkingLotRueckweganschluss>(lot);
            foreach (var a in anschluesse) seiten.Add(a);
            Melde($"Schnappschuss Lot {lot.Index}: Kanten {kurse.Count}, eigene Knoten {eigene}, erhaltene/fremde Anschlussknoten {fremde}, seitliche Anschluesse {anschluesse.Count}.");
        }

        internal static int Erzeuge(EntityManager em, Entity lot, int auftrag)
        {
            if (!em.HasBuffer<ParkingLotRueckwegkurs>(lot)) return 0;
            // Strukturveraenderungen invalidieren DynamicBuffer: ZUERST kopieren.
            var kopie = em.GetBuffer<ParkingLotRueckwegkurs>(lot, true).ToNativeArray(Allocator.Temp);
            try
            {
                // Erst ALLE Originale validieren, dann schreiben. Ein fehlender
                // Anschluss darf keinen halb erzeugten Rueckweg hinterlassen.
                foreach (var alt in kopie)
                {
                    if (!Lebt(em,alt.Besitzer) || !em.Exists(alt.Prefab))
                        throw new InvalidOperationException("Gesicherter Besitzer/Prefab fehlt.");
                    foreach (var n in new[] { alt.Start, alt.Ende })
                        if (n != Entity.Null && !Lebt(em,n))
                            throw new InvalidOperationException("Gesicherter Anschlussknoten fehlt: " + n);
                }
                if (em.HasBuffer<ParkingLotRueckweganschluss>(lot))
                    foreach (var a in em.GetBuffer<ParkingLotRueckweganschluss>(lot,true))
                        if (a.Knoten != Entity.Null && !Lebt(em,a.Knoten))
                            throw new InvalidOperationException("Gesicherter seitlicher Anschluss fehlt: " + a.Knoten);
                using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(),
                    ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
                using var kanten = q.ToEntityArray(Allocator.Temp);
                var benutzt = new HashSet<Entity>();
                int erzeugt = 0;
                foreach (var alt in kopie)
                {
                    // Auch nach einem unterbrochenen Rueckweg: vorhandene
                    // exakte Ergebnisse nicht ein zweites Mal erzeugen.
                    if (FindeKurs(em,kanten,alt,benutzt,out _) != Entity.Null) continue;
                    var d = em.CreateEntity();
                    em.AddComponentData(d, new ParkingLotAuftragsdefinition { Auftrag = auftrag });
                    em.AddComponentData(d, new CreationDefinition { m_Prefab = alt.Prefab,
                        m_Owner = alt.Besitzer, m_Flags = CreationFlags.Permanent,
                        m_RandomSeed = math.max(1, d.Index) });
                    var kurs = alt.Kurs;
                    if (HintergrundKurspruefung.NullhoeheFixieren(kurs.m_Elevation))
                    { kurs.m_StartPosition.m_ParentMesh = 0; kurs.m_EndPosition.m_ParentMesh = 0; }
                    em.AddComponentData(d, kurs);
                    if (alt.HatUpgrade) em.AddComponentData(d, alt.Upgrade);
                    em.AddComponent<Updated>(d); erzeugt++;
                }
                return erzeugt;
            }
            finally { kopie.Dispose(); }
        }

        internal static bool Pruefe(EntityManager em, Entity lot, out int ist, out int soll, bool anschluesse = true)
        {
            ist = soll = 0;
            if (!em.Exists(lot) || !em.HasBuffer<ParkingLotRueckwegkurs>(lot)) return false;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(),
                ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(), ComponentType.Exclude<Temp>());
            using var kanten = query.ToEntityArray(Allocator.Temp);
            var benutzt = new HashSet<Entity>();
            int index = -1;
            foreach (var alt in em.GetBuffer<ParkingLotRueckwegkurs>(lot, true))
            {
                index++;
                soll++;
                var e = FindeKurs(em,kanten,alt,benutzt,out bool andersherum);
                if (e == Entity.Null) continue;
                if (anschluesse && !SeitenanschluesseDa(em,lot,index,e,andersherum)) continue;
                ist++;
            }
            return ist == soll;
        }
        private static Entity FindeKurs(EntityManager em, NativeArray<Entity> kanten,
            ParkingLotRueckwegkurs alt, HashSet<Entity> benutzt, out bool andersherum)
        {
            andersherum = false;
            foreach (var e in kanten)
            {
                if (benutzt.Contains(e) || em.GetComponentData<Owner>(e).m_Owner != alt.Besitzer
                    || em.GetComponentData<PrefabRef>(e).m_Prefab != alt.Prefab) continue;
                var c = em.GetComponentData<Curve>(e).m_Bezier;
                var edge = em.GetComponentData<Edge>(e);
                bool gleich = KurveGleich(c,alt.Kurs.m_Curve);
                bool rueckwaerts = !gleich && KurveGleich(c,alt.Kurs.m_Curve,true);
                if (!gleich && !rueckwaerts) continue;
                var start = rueckwaerts ? edge.m_End : edge.m_Start;
                var ende = rueckwaerts ? edge.m_Start : edge.m_End;
                if (alt.Start != Entity.Null && alt.Start != start || alt.Ende != Entity.Null && alt.Ende != ende) continue;
                var upgrade = alt.Upgrade;
                if (rueckwaerts) upgrade.m_Flags = new CompositionFlags(upgrade.m_Flags.m_General,
                    upgrade.m_Flags.m_Right,upgrade.m_Flags.m_Left);
                if (em.HasComponent<Upgraded>(e) != alt.HatUpgrade
                    || alt.HatUpgrade && !em.GetComponentData<Upgraded>(e).Equals(upgrade)) continue;
                if (!Verbunden(em,start,e) || !Verbunden(em,ende,e)) continue;
                benutzt.Add(e); andersherum = rueckwaerts; return e;
            }
            return Entity.Null;
        }

        internal static void StelleFlussWiederHer(EntityManager em, Entity lot, Action<Entity,Entity,Entity> verbinde)
        {
            if (!em.HasBuffer<ParkingLotRueckweganschluss>(lot)) return;
            using var kurse = em.GetBuffer<ParkingLotRueckwegkurs>(lot,true).ToNativeArray(Allocator.Temp);
            using var seiten = em.GetBuffer<ParkingLotRueckweganschluss>(lot,true).ToNativeArray(Allocator.Temp);
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(),ComponentType.ReadOnly<Curve>(),
                ComponentType.ReadOnly<Owner>(),ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),ComponentType.Exclude<Temp>());
            using var kanten = q.ToEntityArray(Allocator.Temp);
            var benutzt = new HashSet<Entity>();
            var paare = new List<(Entity Kante,Entity Knoten,Entity Prefab)>();
            for (int i = 0; i < kurse.Length; i++)
            {
                var e = FindeKurs(em,kanten,kurse[i],benutzt,out _);
                if (e == Entity.Null) continue;
                foreach (var a in seiten)
                    if (a.Kursindex == i) paare.Add((e,Anschlussknoten(em,a),a.Prefab));
            }
            // Der gemeinsame Graphhelfer kann Strukturveraenderungen ausloesen;
            // die physische Netzpruefung ist davor vollstaendig abgeschlossen.
            foreach (var p in paare) verbinde(p.Kante,p.Knoten,p.Prefab);
        }

        private static bool SeitenanschluesseDa(EntityManager em, Entity lot, int index, Entity e, bool umgekehrt)
        {
            if (!em.HasBuffer<ParkingLotRueckweganschluss>(lot)) return true;
            foreach (var a in em.GetBuffer<ParkingLotRueckweganschluss>(lot,true))
            {
                if (a.Kursindex != index) continue;
                var knoten = Anschlussknoten(em,a);
                if (!Verbunden(em,knoten,e) || !em.HasBuffer<ConnectedNode>(e)) return false;
                bool gefunden = false;
                foreach (var n in em.GetBuffer<ConnectedNode>(e,true))
                    if (n.m_Node == knoten && math.abs(n.m_CurvePosition-(umgekehrt ? 1-a.Lage : a.Lage)) <= .001f)
                        gefunden = true;
                if (!gefunden) return false;
            }
            return true;
        }
        internal static void MeldeAnschluesse(EntityManager em, Entity lot)
        {
            if (!em.HasBuffer<ParkingLotRueckweganschluss>(lot)) return;
            var knoten = new HashSet<Entity>();
            // Query-Erzeugung ist eine Strukturveraenderung: Buffer zuerst kopieren.
            using var anschluesse = em.GetBuffer<ParkingLotRueckweganschluss>(lot,true).ToNativeArray(Allocator.Temp);
            foreach (var a in anschluesse) knoten.Add(Anschlussknoten(em,a));
            foreach (var n in knoten) if (Lebt(em,n)) em.AddComponent<Updated>(n);
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(),ComponentType.ReadOnly<Owner>(),
                ComponentType.Exclude<Temp>(),ComponentType.Exclude<Deleted>());
            using var kanten = q.ToEntityArray(Allocator.Temp);
            foreach (var e in kanten)
                if (ParkingLotBesitz.GehoertZu(em,em.GetComponentData<Owner>(e).m_Owner,lot)) em.AddComponent<Updated>(e);
            Melde($"Rueckweg: {knoten.Count} seitliche Knoten mit eigenen Kanten bei Vanilla angemeldet; 0 direkte Netzeintraege.");
        }
        private static Entity Anschlussknoten(EntityManager em, ParkingLotRueckweganschluss a)
        {
            if (a.Knoten != Entity.Null) return a.Knoten;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Net.Node>(),ComponentType.ReadOnly<Owner>(),
                ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Temp>(),ComponentType.Exclude<Deleted>());
            using var nodes = q.ToEntityArray(Allocator.Temp);
            Entity gefunden = Entity.Null;
            foreach (var e in nodes)
                if (em.GetComponentData<PrefabRef>(e).m_Prefab == a.Prefab
                    && em.GetComponentData<Owner>(e).m_Owner == a.Besitzer
                    && math.distance(em.GetComponentData<Game.Net.Node>(e).m_Position,a.Position) <= .05f)
                {
                    if (gefunden != Entity.Null) return Entity.Null;
                    gefunden = e;
                }
            return gefunden;
        }
        internal static void FuellTraeger(EntityManager em, Entity lot)
        {
            if (!em.HasComponent<ParkingLotCarrierReference>(lot)) return;
            var traeger = em.GetComponentData<ParkingLotCarrierReference>(lot).Carrier;
            if (!em.Exists(traeger) || !em.HasBuffer<SubNet>(traeger)) return;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(),ComponentType.ReadOnly<Owner>(),
                ComponentType.Exclude<Temp>(),ComponentType.Exclude<Deleted>());
            using var kanten = q.ToEntityArray(Allocator.Temp);
            foreach (var e in kanten)
                if (ParkingLotBesitz.GehoertZu(em,em.GetComponentData<Owner>(e).m_Owner,lot))
                {
                    var b = em.GetBuffer<SubNet>(traeger);
                    bool da = false; foreach (var n in b) if (n.m_SubNet == e) da = true;
                    if (!da) b.Add(new SubNet(e));
                }
        }
        private static bool Verbunden(EntityManager em, Entity node, Entity edge)
        {
            if (!em.HasBuffer<ConnectedEdge>(node)) return false;
            foreach (var c in em.GetBuffer<ConnectedEdge>(node, true)) if (c.m_Edge == edge) return true;
            return false;
        }
        private static bool KurveGleich(Bezier4x3 a, Bezier4x3 b, bool umgekehrt = false)
            => HintergrundKurspruefung.KurveGleich((a.a,a.b,a.c,a.d),(b.a,b.b,b.c,b.d),umgekehrt);
        internal static bool Lebt(EntityManager em, Entity e)
            => e != Entity.Null && em.Exists(e) && !em.HasComponent<Deleted>(e);
        internal static void Melde(string text)
        { var z = "PLT-Hintergrund: " + text; Mod.log.Info(z); ParkingGeometry.Live(z); }
    }
}
