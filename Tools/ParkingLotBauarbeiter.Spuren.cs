using System;
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using CarLane = Game.Net.CarLane;
using SubLane = Game.Net.SubLane;
using SubNet = Game.Net.SubNet;
using ParkingLane = Game.Net.ParkingLane;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private readonly Dictionary<int,Entity> _spurEntities = new Dictionary<int,Entity>();
        private HintergrundPortion _spurportion;
        private bool _spurenRichtig;
        private int _hintergrundParkSoll;
        internal bool HintergrundSpurpruefungFertig => _spurportion?.Fertig == true;

        private bool FremdeSpurverbindung(Entity spur, Entity kante)
        {
            if (!EntityManager.HasComponent<Lane>(spur) || !EntityManager.HasComponent<CarLane>(spur)) return false;
            var l = EntityManager.GetComponentData<Lane>(spur);
            var c = EntityManager.GetComponentData<CarLane>(spur);
            return HintergrundSpurregel.FremdeVerbindung((c.m_Flags & CarLaneFlags.SideConnection) != 0,
                kante.Index,l.m_StartNode.GetOwnerIndex(),l.m_EndNode.GetOwnerIndex(),FremdesObjekt);
        }

        private bool FremdesObjekt(int index)
        {
            if (!_spurEntities.TryGetValue(index,out var e)) return false;
            if (ParkingLotBesitz.GehoertZu(EntityManager,e,_lotOwner)
                || ParkingLotBesitz.GehoertZu(EntityManager,e,_editLot)) return false;
            for (int i = 0; i < 32 && ParkingLotNetzRueckweg.Lebt(EntityManager,e); i++)
            {
                if (EntityManager.HasComponent<Game.Buildings.Building>(e)
                    || EntityManager.HasComponent<Game.Objects.Object>(e)) return true;
                if (!EntityManager.HasComponent<Owner>(e)) return false;
                e = EntityManager.GetComponentData<Owner>(e).m_Owner;
            }
            return false;
        }

        internal bool HintergrundFahrwegePortion()
        {
            if (_spurportion == null) { _spurenRichtig = true; _spurportion = new HintergrundPortion(HintergrundSpurschritte()); }
            var uhr = System.Diagnostics.Stopwatch.StartNew();
            _spurportion.Weiter(() => uhr.Elapsed.TotalMilliseconds);
            return _spurportion.Fertig && _spurenRichtig;
        }

        private IEnumerable<int> HintergrundSpurschritte()
        {
            _spurEntities.Clear();
            var gebraucht = new HashSet<int>();
            var kanten = new HashSet<Entity>(_eigeneDauerteile);
            kanten.UnionWith(_erhalteneNetzteile);
            foreach (var e in kanten)
            {
                if (!EntityManager.HasComponent<Edge>(e) || !EntityManager.HasBuffer<SubLane>(e)) continue;
                foreach (var s in EntityManager.GetBuffer<SubLane>(e,true))
                    if (EntityManager.HasComponent<Lane>(s.m_SubLane))
                    {
                        var l = EntityManager.GetComponentData<Lane>(s.m_SubLane);
                        gebraucht.Add(l.m_StartNode.GetOwnerIndex()); gebraucht.Add(l.m_EndNode.GetOwnerIndex());
                    }
                yield return 0;
            }
            Entity[] teile;
            // Gebaeude besitzen oft KEIN Owner. Ein Owner-only-Scan wuerde
            // genau die fremden ParkingFacility-Enden auslassen.
            using (var q = EntityManager.CreateEntityQuery(new EntityQueryDesc {
                Any = new[] {ComponentType.ReadOnly<Owner>(),ComponentType.ReadOnly<Game.Objects.Object>()},
                None = new[] {ComponentType.ReadOnly<Game.Tools.Temp>(),ComponentType.ReadOnly<Deleted>()} }))
            using (var a = q.ToEntityArray(Allocator.Temp)) teile = a.ToArray();
            int gelesen = 0;
            foreach (var e in teile)
            { if (gebraucht.Contains(e.Index)) _spurEntities[e.Index] = e; if (++gelesen % 128 == 0) yield return 0; }
            int eigene = 0, fremde = 0, falsch = 0, parkSoll = 0, parkIst = 0;
            foreach (var e in kanten)
            {
                if (!EntityManager.HasComponent<Edge>(e) || !EntityManager.HasBuffer<SubLane>(e)) continue;
                SubLane[] lanes;
                using (var a = EntityManager.GetBuffer<SubLane>(e,true).ToNativeArray(Allocator.Temp)) lanes = a.ToArray();
                foreach (var l in lanes)
                {
                    var spur = l.m_SubLane;
                    if (EntityManager.HasComponent<CarLane>(spur))
                    {
                        if (FremdeSpurverbindung(spur,e)) fremde++;
                        else
                        {
                            eigene++;
                            if (!HintergrundKurspruefung.FahrtempoStimmt(EntityManager.GetComponentData<CarLane>(spur).m_SpeedLimit)
                                || FahrspurkostenFalsch(spur)) falsch++;
                        }
                    }
                    yield return 0;
                }
            }
            foreach (var r in _objectRecords)
            {
                if (r.Kind != "BayDecal") continue;
                parkSoll++;
                bool ok = false;
                if (_hintergrundObjekttreffer.TryGetValue(r,out var e))
                {
                    if (!EntityManager.HasBuffer<SubLane>(e)) { yield return 0; continue; }
                    var suchziel = HintergrundSpurregel.Suchbesitzer(e,
                        x => EntityManager.HasComponent<Owner>(x) ? EntityManager.GetComponentData<Owner>(x).m_Owner : Entity.Null,
                        x => EntityManager.HasComponent<Game.Buildings.Building>(x),
                        x => EntityManager.HasComponent<Game.Objects.Attached>(x) ? EntityManager.GetComponentData<Game.Objects.Attached>(x).m_Parent : Entity.Null,
                        x => EntityManager.HasComponent<PrefabRef>(x), x => ParkingLotNetzRueckweg.Lebt(EntityManager,x), Entity.Null);
                    bool besitz = (suchziel == _lotOwner || suchziel == _lotCarrier)
                        && EntityManager.HasBuffer<SubNet>(suchziel) && EntityManager.GetBuffer<SubNet>(suchziel,true).Length > 0
                        && EntityManager.HasComponent<ParkingLotPartRelation>(e)
                        && EntityManager.GetComponentData<ParkingLotPartRelation>(e).Lot == _lotOwner
                        && EntityManager.HasBuffer<Game.Objects.SubObject>(_lotCarrier)
                        && HatSubObject(_lotCarrier,e);
                    int real = 0, verbunden = 0;
                    foreach (var l in EntityManager.GetBuffer<SubLane>(e,true))
                    {
                        var spur = l.m_SubLane;
                        if (!EntityManager.HasComponent<ParkingLane>(spur) || EntityManager.HasComponent<Game.Net.SlaveLane>(spur)) continue;
                        // VirtualLane dient dem Einsteigen, nicht dem Parken.
                        if (EntityManager.HasComponent<PrefabRef>(spur)
                            && EntityManager.HasComponent<NetLaneData>(EntityManager.GetComponentData<PrefabRef>(spur).m_Prefab)
                            && (EntityManager.GetComponentData<NetLaneData>(EntityManager.GetComponentData<PrefabRef>(spur).m_Prefab).m_Flags & LaneFlags.Virtual) != 0) continue;
                        real++;
                        if (EntityManager.HasComponent<LaneConnection>(spur))
                        {
                            var c = EntityManager.GetComponentData<LaneConnection>(spur);
                            if (ParkingLotNetzRueckweg.Lebt(EntityManager,c.m_StartLane)
                                && EntityManager.HasComponent<CarLane>(c.m_StartLane)) verbunden++;
                        }
                    }
                    ok = besitz && real > 0 && verbunden == real;
                    if (!ok) ParkingLotNetzRueckweg.Melde($"Parkspur OFFEN: Decal {e}, Suchbesitzer {suchziel}, Besitzerpuffer/Relation={besitz}, reale Spuren {real}, Auto-LaneConnection {verbunden}.");
                }
                if (ok) parkIst++;
                yield return 0;
            }
            _spurenRichtig = eigene > 0 && falsch == 0 && parkSoll == _hintergrundParkSoll && parkIst == parkSoll;
            int erhalten = 0;
            foreach (var kette in _erhalteneKursketten)
            {
                if (ErhalteneKursketteDa(kette.Value)) erhalten++;
                else _spurenRichtig = false;
                yield return 0;
            }
            ParkingLotNetzRueckweg.Melde($"Netzerhalt nach Updated: {erhalten}/{_erhalteneKursketten.Count} Sollkurse mit unveraenderten Originalkanten/Owner/Prefab/3D-Kurven/Knoten; 0 Neubau dafuer.");
            ParkingLotNetzRueckweg.Melde($"Spurpruefung: eigene Autospuren {eigene}, belegte fremde Seitenverbindungen {fremde}, falsche Fahrwerte {falsch}; Park-Decals mit Besitzerpuffern/Attached/Relation und realer Auto-LaneConnection {parkIst}/{parkSoll}.");
            foreach (int n in HintergrundEinmuendungsschritte()) yield return n;
        }

        private bool HatSubObject(Entity traeger,Entity objekt)
        {
            var b = EntityManager.GetBuffer<Game.Objects.SubObject>(traeger,true);
            for (int i = 0; i < b.Length; i++) if (b[i].m_SubObject == objekt) return true;
            return false;
        }
    }
}
