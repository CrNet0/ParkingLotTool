using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    internal static class ParkingLotKursabgleich
    {
        internal sealed class Teil
        {
            internal Entity Kante, Start, Ende;
            internal float2 Bereich;
            internal bool Umgekehrt => Bereich.x > Bereich.y;
        }

        internal static (float3 A,float3 B,float3 C,float3 D) Form(Bezier4x3 c) => (c.a,c.b,c.c,c.d);

        internal static float Abstand(Bezier4x3 soll, Bezier4x3 ist, out float2 bereich)
        {
            MathUtils.Distance(soll.xz,ist.a.xz,out float von);
            MathUtils.Distance(soll.xz,ist.d.xz,out float bis);
            bereich = new float2(von,bis);
            return HintergrundAbgleich.Kurvenabstand(Form(ist),HintergrundAbgleich.Schnitt(Form(soll),von,bis));
        }

        internal static bool Kette(List<Teil> teile, out Entity start, out Entity ende)
        {
            var pruefung = new List<(float2 Bereich,int Start,int Ende)>();
            foreach (var p in teile) pruefung.Add((p.Bereich,p.Start.Index,p.Ende.Index));
            start = ende = Entity.Null;
            if (!HintergrundAbgleich.Kette(pruefung,out int anfang,out int schluss)) return false;
            foreach (var p in teile)
            {
                if (p.Start.Index == anfang) start = p.Start;
                if (p.Ende.Index == anfang) start = p.Ende;
                if (p.Start.Index == schluss) ende = p.Start;
                if (p.Ende.Index == schluss) ende = p.Ende;
            }
            return true;
        }

        internal static List<Teil> Sammle(EntityManager em, IEnumerable<Entity> kandidaten,
            Bezier4x3 soll, Entity prefab, Entity besitzer, HashSet<Entity> benutzt = null)
        {
            var teile = new List<Teil>();
            foreach (var e in kandidaten)
            {
                if (benutzt != null && benutzt.Contains(e) || !em.HasComponent<Curve>(e) || !em.HasComponent<Edge>(e)
                    || !em.HasComponent<Game.Prefabs.PrefabRef>(e)
                    || em.GetComponentData<Game.Prefabs.PrefabRef>(e).m_Prefab != prefab) continue;
                if (besitzer != Entity.Null && (!em.HasComponent<Game.Common.Owner>(e)
                    || em.GetComponentData<Game.Common.Owner>(e).m_Owner != besitzer)) continue;
                var c = em.GetComponentData<Curve>(e).m_Bezier;
                if (!(Abstand(soll,c,out var bereich) <= .05f)) continue;
                var edge = em.GetComponentData<Edge>(e);
                teile.Add(new Teil { Kante = e, Start = edge.m_Start, Ende = edge.m_End, Bereich = bereich });
            }
            return teile;
        }
    }
}
