using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    // Messung ohne ECS-Schreibzugriffe. Nur der explizite Edit-Abrissschalter
    // loest eigene Leitungen aus der Besitzkaskade. 120 Bilder bleiben auch
    // bei ESC/Kamerawerkzeug, unabhaengig vom Werkzeugzustand.
    public sealed partial class ParkingLotVersorgungsdiagnoseSystem : GameSystemBase
    {
        private readonly HashSet<Entity> _physisch = new HashSet<Entity>();
        private readonly HashSet<Entity> _wurzeln = new HashSet<Entity>();
        private readonly HashSet<Entity> _flusskanten = new HashSet<Entity>();
        private readonly Dictionary<Entity, Entity> _flussursprung = new Dictionary<Entity, Entity>();
        private readonly HashSet<Entity> _alte = new HashSet<Entity>();
        private readonly HashSet<Entity> _behalteneLots = new HashSet<Entity>();
        private readonly HashSet<Entity> _behalteneTraeger = new HashSet<Entity>();
        private bool _aktiv;
        private int _lauf, _bilder, _letztesBild = -1;

        internal bool Behalten(ParkingLotVersorgungsleitung tag)
            => _behalteneLots.Contains(tag.Lot) || _behalteneTraeger.Contains(tag.Carrier);

        internal void MerkeEdit(Entity lot, Entity traeger, bool behalten)
        {
            if (behalten)
            {
                if (lot != Entity.Null) _behalteneLots.Add(lot);
                if (traeger != Entity.Null) _behalteneTraeger.Add(traeger);
            }
            if (Mod.Aus("versorgung-bilddiagnose") && !behalten) return;
            var q = GetEntityQuery(ComponentType.ReadOnly<ParkingLotVersorgungsleitung>(),
                ComponentType.ReadOnly<Edge>(), ComponentType.Exclude<Temp>());
            using var entities = q.ToEntityArray(Allocator.Temp);
            var roots = new List<Entity>();
            foreach (var e in entities)
            {
                var tag = EntityManager.GetComponentData<ParkingLotVersorgungsleitung>(e);
                if ((lot != Entity.Null && tag.Lot == lot)
                    || (traeger != Entity.Null && tag.Carrier == traeger)) roots.Add(e);
            }
            if (behalten) TrenneAbrissbesitz(roots, lot, traeger);
            if (Mod.Aus("versorgung-bilddiagnose")) return;
            _alte.Clear();
            _wurzeln.UnionWith(roots);
            ErfasseUmfeld(roots);
            foreach (var e in roots) _alte.Add(e);
            foreach (var e in _physisch.ToArray()) ErfasseFluss(e);
            Schreibe("EDIT-VOR-ABRISS", 0);
        }

        private void TrenneAbrissbesitz(List<Entity> leitungen, Entity lot, Entity traeger)
        {
            // Beim Permanent-Bau gehoeren Leitungen dem Traeger. Nur den PLT-
            // Abriss auszulassen genuegt dann nicht: Vanilla vererbt Deleted
            // aus SubNet. Schutz ausschliesslich fuer die markierten Leitungen
            // und deren eigene Knoten; fremde gemeinsame Knoten bleiben fremd.
            var halten = new HashSet<Entity>(leitungen);
            foreach (var e in leitungen)
            {
                var k = EntityManager.GetComponentData<Edge>(e);
                foreach (var n in new[] { k.m_Start, k.m_End })
                    if (Existiert(n) && EntityManager.HasComponent<Owner>(n))
                    {
                        var owner = EntityManager.GetComponentData<Owner>(n).m_Owner;
                        if (owner == lot || owner == traeger) halten.Add(n);
                    }
            }
            var ausListen = 0; var ownerFort = 0;
            foreach (var besitzer in new[] { lot, traeger })
            {
                if (!Existiert(besitzer) || !EntityManager.HasBuffer<Game.Net.SubNet>(besitzer)) continue;
                var puffer = EntityManager.GetBuffer<Game.Net.SubNet>(besitzer);
                for (var i = puffer.Length - 1; i >= 0; i--)
                    if (halten.Contains(puffer[i].m_SubNet)) { puffer.RemoveAt(i); ausListen++; }
            }
            foreach (var e in halten)
                if (Existiert(e) && EntityManager.HasComponent<Owner>(e))
                {
                    var owner = EntityManager.GetComponentData<Owner>(e).m_Owner;
                    if (owner != lot && owner != traeger) continue;
                    EntityManager.RemoveComponent<Owner>(e);
                    ownerFort++;
                }
            ParkingLotToolSystem.AvDiagnoseMeldung($"EDIT-ABRISS-AUS {leitungen.Count} eigene Leitungen behalten; "
                + $"{ausListen} eigene SubNet-Eintraege und {ownerFort} eigene Owner-Verweise "
                + "aus der alten Besitzkaskade geloest; 0 Flussgraph-Aenderungen.");
        }

        internal void Beginne(IEnumerable<Entity> roots, string grund)
        {
            if (Mod.Aus("versorgung-bilddiagnose")) return;
            if (!_aktiv) _lauf++;
            _aktiv = true;
            _bilder = 0;
            _letztesBild = -1;
            _wurzeln.UnionWith(roots);
            ErfasseUmfeld(roots);
            Schreibe("BEGIN " + grund, 0);
        }

        private bool Existiert(Entity e) => e != Entity.Null && EntityManager.Exists(e);

        private string Zustand(Entity e)
        {
            if (!Existiert(e)) return e + " FEHLT";
            return e + $" exists=1 D={(EntityManager.HasComponent<Deleted>(e) ? 1 : 0)}"
                + $" T={(EntityManager.HasComponent<Temp>(e) ? 1 : 0)}"
                + $" C={(EntityManager.HasComponent<Created>(e) ? 1 : 0)}"
                + $" U={(EntityManager.HasComponent<Updated>(e) ? 1 : 0)}";
        }

        private void Merke(Entity e) { if (e != Entity.Null) _physisch.Add(e); }

        // Genau eine Nachbarschaftsschicht, kein Weg durch die ganze Stadt.
        // Einmal gemerkte Entity+Version bleiben auch nach Zerstoerung stehen.
        private void ErfasseUmfeld(IEnumerable<Entity> roots)
        {
            var basis = roots.Where(e => e != Entity.Null).ToArray();
            foreach (var e in basis)
            {
                Merke(e);
                if (!Existiert(e)) continue;
                if (EntityManager.HasComponent<Temp>(e))
                    Merke(EntityManager.GetComponentData<Temp>(e).m_Original);
                if (EntityManager.HasComponent<Edge>(e))
                {
                    var k = EntityManager.GetComponentData<Edge>(e);
                    Merke(k.m_Start); Merke(k.m_End);
                }
                if (EntityManager.HasBuffer<ConnectedNode>(e))
                    foreach (var n in EntityManager.GetBuffer<ConnectedNode>(e, true)) Merke(n.m_Node);
            }
            var knoten = _physisch.Where(e => Existiert(e) && EntityManager.HasComponent<Node>(e)).ToArray();
            foreach (var e in knoten)
                if (EntityManager.HasBuffer<ConnectedEdge>(e))
                    foreach (var k in EntityManager.GetBuffer<ConnectedEdge>(e, true)) Merke(k.m_Edge);
        }

        private void ErfasseFluss(Entity physisch)
        {
            if (!Existiert(physisch)) return;
            if (EntityManager.HasComponent<ElectricityNodeConnection>(physisch))
                _flussursprung[EntityManager.GetComponentData<ElectricityNodeConnection>(physisch).m_ElectricityNode] = physisch;
            if (EntityManager.HasComponent<WaterPipeNodeConnection>(physisch))
                _flussursprung[EntityManager.GetComponentData<WaterPipeNodeConnection>(physisch).m_WaterPipeNode] = physisch;
        }

        private bool HatKante(Entity knoten, Entity kante)
            => Existiert(knoten) && EntityManager.HasBuffer<ConnectedEdge>(knoten)
                && ParkingLotPuffer.Hat(EntityManager.GetBuffer<ConnectedEdge>(knoten, true), kante);

        private bool HatKnoten(Entity kante, Entity knoten)
        {
            if (!Existiert(kante) || !EntityManager.HasComponent<Edge>(kante)) return false;
            var k = EntityManager.GetComponentData<Edge>(kante);
            return k.m_Start == knoten || k.m_End == knoten
                || EntityManager.HasBuffer<ConnectedNode>(kante)
                && ParkingLotPuffer.Hat(EntityManager.GetBuffer<ConnectedNode>(kante, true), knoten);
        }

        private string OwnerText(Entity e)
            => EntityManager.HasComponent<Owner>(e)
                ? " Owner=" + Zustand(EntityManager.GetComponentData<Owner>(e).m_Owner) : " Owner=0";

        private string Komposition(Entity c)
        {
            var s = Zustand(c);
            if (!Existiert(c)) return s;
            s += $" Data={(EntityManager.HasComponent<NetCompositionData>(c) ? 1 : 0)}";
            if (EntityManager.HasComponent<NetCompositionMeshRef>(c))
            {
                var mesh = EntityManager.GetComponentData<NetCompositionMeshRef>(c).m_Mesh;
                s += " Mesh=" + Zustand(mesh);
                s += $" MeshData={(Existiert(mesh) && EntityManager.HasComponent<NetCompositionMeshData>(mesh) ? 1 : 0)}";
            }
            else s += " MeshRef=0";
            return s;
        }

        private void PhysischeZeile(Entity e, StringBuilder text, ref int tot, ref int asymmetrisch)
        {
            text.Append("NET ").Append(Zustand(e)).Append(_alte.Contains(e) ? " ALT" : "");
            if (!Existiert(e)) { text.AppendLine(); return; }
            text.Append(OwnerText(e));
            if (EntityManager.HasComponent<PrefabRef>(e))
            {
                var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                text.Append(" Prefab=").Append(Zustand(prefab));
                var prefabs = World.GetExistingSystemManaged<PrefabSystem>();
                if (prefabs != null && prefabs.TryGetPrefab<PrefabBase>(prefab, out var asset))
                    text.Append(" '").Append(asset.name).Append('\'');
            }
            if (EntityManager.HasComponent<Temp>(e))
                text.Append(" Original=").Append(Zustand(EntityManager.GetComponentData<Temp>(e).m_Original));
            if (EntityManager.HasComponent<LocalConnect>(e)) text.Append(" LocalConnect=1");
            if (EntityManager.HasComponent<Node>(e))
                text.Append(" Position=").Append(EntityManager.GetComponentData<Node>(e).m_Position);
            if (EntityManager.HasComponent<Edge>(e))
            {
                var k = EntityManager.GetComponentData<Edge>(e);
                foreach (var n in new[] { k.m_Start, k.m_End })
                {
                    var rueck = HatKante(n, e);
                    text.Append(" Ende=").Append(Zustand(n)).Append(" CE-rueck=").Append(rueck ? 1 : 0);
                    if (!Existiert(n)) tot++;
                    else if (!rueck) asymmetrisch++;
                }
            }
            if (EntityManager.HasComponent<Curve>(e))
            {
                var c = EntityManager.GetComponentData<Curve>(e);
                text.Append($" Laenge={c.m_Length:F3} Y={c.m_Bezier.a.y:F3}/{c.m_Bezier.d.y:F3}");
            }
            if (EntityManager.HasComponent<Composition>(e))
            {
                var c = EntityManager.GetComponentData<Composition>(e);
                text.Append(" Composition=[").Append(Komposition(c.m_Edge))
                    .Append("; Start ").Append(Komposition(c.m_StartNode))
                    .Append("; Ende ").Append(Komposition(c.m_EndNode)).Append(']');
            }
            else text.Append(" Composition=0");
            if (EntityManager.HasComponent<Orphan>(e))
                text.Append(" OrphanComposition=").Append(Komposition(EntityManager.GetComponentData<Orphan>(e).m_Composition));
            if (EntityManager.HasBuffer<Game.Net.SubLane>(e)) text.Append(" SubLane=").Append(EntityManager.GetBuffer<Game.Net.SubLane>(e, true).Length);
            if (EntityManager.HasBuffer<Game.Net.SubNet>(e)) text.Append(" SubNet=").Append(EntityManager.GetBuffer<Game.Net.SubNet>(e, true).Length);
            text.AppendLine();
            if (EntityManager.HasBuffer<ConnectedNode>(e))
                foreach (var n in EntityManager.GetBuffer<ConnectedNode>(e, true))
                {
                    var rueck = HatKante(n.m_Node, e);
                    text.AppendLine($"CN {e} -> {Zustand(n.m_Node)} t={n.m_CurvePosition:F5} CE-rueck={(rueck ? 1 : 0)}");
                    if (!Existiert(n.m_Node)) tot++; else if (!rueck) asymmetrisch++;
                }
            if (EntityManager.HasBuffer<ConnectedEdge>(e))
                foreach (var k in EntityManager.GetBuffer<ConnectedEdge>(e, true))
                {
                    var rueck = HatKnoten(k.m_Edge, e);
                    text.AppendLine($"CE {e} -> {Zustand(k.m_Edge)} Ende-oder-CN-rueck={(rueck ? 1 : 0)}");
                    if (!Existiert(k.m_Edge)) tot++; else if (!rueck) asymmetrisch++;
                }
        }

        private bool HatFlusskante(Entity n, Entity k)
            => Existiert(n) && EntityManager.HasBuffer<ConnectedFlowEdge>(n)
                && ParkingLotPuffer.Hat(EntityManager.GetBuffer<ConnectedFlowEdge>(n, true), k);

        private bool AktuellerFlussverweis(Entity physisch, Entity n)
            => Existiert(physisch) &&
                (EntityManager.HasComponent<ElectricityNodeConnection>(physisch)
                    && EntityManager.GetComponentData<ElectricityNodeConnection>(physisch).m_ElectricityNode == n
                || EntityManager.HasComponent<WaterPipeNodeConnection>(physisch)
                    && EntityManager.GetComponentData<WaterPipeNodeConnection>(physisch).m_WaterPipeNode == n);

        private void Flusszeilen(StringBuilder text, ref int tot, ref int asymmetrisch, ref int verwaist)
        {
            foreach (var paar in _flussursprung)
            {
                var n = paar.Key;
                var lebt = Existiert(n);
                var waise = lebt && !Existiert(paar.Value);
                var aktuell = AktuellerFlussverweis(paar.Value, n);
                if (aktuell && !lebt) tot++;
                if (waise) verwaist++;
                text.AppendLine($"FLOWNODE {Zustand(n)} NET-Ursprung={Zustand(paar.Value)} VERWAIST={(waise ? 1 : 0)}"
                    + $" AktuellerVerweis={(aktuell ? 1 : 0)}"
                    + (lebt ? OwnerText(n) : "")
                    + $" StromNode={(lebt && EntityManager.HasComponent<ElectricityFlowNode>(n) ? 1 : 0)}"
                    + $" WasserNode={(lebt && EntityManager.HasComponent<WaterPipeNode>(n) ? 1 : 0)}");
                if (!lebt || !EntityManager.HasBuffer<ConnectedFlowEdge>(n)) continue;
                foreach (var k in EntityManager.GetBuffer<ConnectedFlowEdge>(n, true))
                {
                    _flusskanten.Add(k.m_Edge);
                    text.AppendLine($"CFE {n} -> {Zustand(k.m_Edge)}");
                    if (!Existiert(k.m_Edge)) tot++;
                    else if (EntityManager.HasComponent<ElectricityFlowEdge>(k.m_Edge))
                    {
                        var f = EntityManager.GetComponentData<ElectricityFlowEdge>(k.m_Edge);
                        if (f.m_Start != n && f.m_End != n) asymmetrisch++;
                    }
                    else if (EntityManager.HasComponent<WaterPipeEdge>(k.m_Edge))
                    {
                        var f = EntityManager.GetComponentData<WaterPipeEdge>(k.m_Edge);
                        if (f.m_Start != n && f.m_End != n) asymmetrisch++;
                    }
                    else tot++;
                }
            }
            foreach (var e in _flusskanten)
            {
                Entity a, b; string art;
                if (EntityManager.HasComponent<ElectricityFlowEdge>(e))
                {
                    var f = EntityManager.GetComponentData<ElectricityFlowEdge>(e);
                    a = f.m_Start; b = f.m_End; art = "Strom";
                }
                else if (EntityManager.HasComponent<WaterPipeEdge>(e))
                {
                    var f = EntityManager.GetComponentData<WaterPipeEdge>(e);
                    a = f.m_Start; b = f.m_End; art = "Wasser";
                }
                else { text.AppendLine("FLOWEDGE " + Zustand(e) + " Daten=0"); continue; }
                var hin = HatFlusskante(a, e); var rueck = HatFlusskante(b, e);
                bool Typ(Entity n) => art == "Strom" ? EntityManager.HasComponent<ElectricityFlowNode>(n)
                    : EntityManager.HasComponent<WaterPipeNode>(n);
                text.AppendLine($"FLOWEDGE {art} {Zustand(e)} {Zustand(a)} -> {Zustand(b)}"
                    + $" Endtyp={(Typ(a) ? 1 : 0)}/{(Typ(b) ? 1 : 0)} CFE-rueck={(hin ? 1 : 0)}/{(rueck ? 1 : 0)}" + OwnerText(e));
                if (!Existiert(a) || !Existiert(b) || !Typ(a) || !Typ(b)) tot++;
                else if (!hin || !rueck) asymmetrisch++;
            }
        }

        private void Schreibe(string phase, int bild)
        {
            var kopf = $"PLT-VERSORGUNG-BILD Lauf={_lauf} Bild={bild}/120 Frame={UnityEngine.Time.frameCount} Phase={phase}";
            // BEGIN vor dem ersten ECS-Lesen: ein fehlendes END grenzt auch
            // einen Absturz waehrend einer abhaengigkeitsbedingten Job-Wartezeit ein.
            ParkingLotSchrittmarke.Setze(kopf + " BEGIN");
            ParkingLotSchrittmarke.Versorgungsbild(kopf + " BEGIN");
            try
            {
                ErfasseUmfeld(_wurzeln);
                foreach (var e in _physisch.ToArray()) ErfasseFluss(e);
                var text = new StringBuilder();
                var tot = 0; var asymmetrisch = 0; var verwaist = 0;
                foreach (var e in _physisch) PhysischeZeile(e, text, ref tot, ref asymmetrisch);
                Flusszeilen(text, ref tot, ref asymmetrisch, ref verwaist);
                var ende = kopf + $" END Net={_physisch.Count} FlowNodes={_flussursprung.Count}"
                    + $" FlowEdges={_flusskanten.Count} ToteReferenzen={tot} Asymmetrien={asymmetrisch} VerwaisteFlussknoten={verwaist}";
                text.AppendLine(ende);
                ParkingLotSchrittmarke.Versorgungsbild(text.ToString());
                ParkingLotSchrittmarke.Setze(ende);
                ParkingLotLiveLog.Zeile(text.ToString());
                Mod.log.Info(kopf + Environment.NewLine + text);
            }
            catch (Exception ex)
            {
                ParkingLotToolSystem.AvDiagnoseMeldung(kopf + " MESSFEHLER " + ex);
            }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!_aktiv || _bilder >= 120 || _letztesBild == UnityEngine.Time.frameCount) return;
            _letztesBild = UnityEngine.Time.frameCount;
            _bilder++;
            Schreibe("PostTool", _bilder);
        }

        internal void NachModification()
        {
            if (!_aktiv || _bilder == 0) return;
            Schreibe("ModificationEnd", _bilder);
            if (_bilder < 120) return;
            _aktiv = false;
            _physisch.Clear(); _wurzeln.Clear(); _flusskanten.Clear(); _flussursprung.Clear(); _alte.Clear();
        }
    }

    public sealed partial class ParkingLotVersorgungsdiagnoseEndSystem : GameSystemBase
    {
        [Preserve]
        protected override void OnUpdate()
            => World.GetExistingSystemManaged<ParkingLotVersorgungsdiagnoseSystem>()?.NachModification();
    }
}
