using System;
using System.Collections.Generic;
using System.Diagnostics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;
using SubNet = Game.Net.SubNet;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private HintergrundPortion _bauportion;
        private HintergrundPortion _vorbereitungportion, _rueckflussportion;
        private readonly Dictionary<(long,long),HashSet<Entity>> _hintergrundKnoten = new Dictionary<(long,long),HashSet<Entity>>();
        internal bool HintergrundAusgabeFertig => _bauportion?.Fertig == true;
        internal void HintergrundPortionenBeenden()
        {
            _bauportion?.Dispose(); _bauportion = null;
            _spurportion?.Dispose(); _spurportion = null;
            _kinderportion?.Dispose(); _kinderportion = null;
            _abrissportion?.Dispose(); _abrissportion = null;
            _ruecknahmeportion?.Dispose(); _ruecknahmeportion = null;
            _nebenportion?.Dispose(); _nebenportion = null;
            _vorbereitungportion?.Dispose(); _vorbereitungportion = null;
            _rueckflussportion?.Dispose(); _rueckflussportion = null;
            _anschlussportion?.Dispose(); _anschlussportion = null;
            _fehlstellenportion?.Dispose(); _fehlstellenportion = null;
        }

        internal int HintergrundBauportion()
        {
            if (_bauportion == null)
            {
                if (_lotOwner == Entity.Null || _lotCarrier == Entity.Null) throw new InvalidOperationException("Stufe A fehlt.");
                if (Mod.Aus("hintergrund-nach-a")) throw new InvalidOperationException("Fehlerprobe nach Stufe A.");
                _hintergrundStufeBBild = UnityEngine.Time.frameCount;
                _bauportion = new HintergrundPortion(HintergrundBauschritte());
            }
            MerkeHintergrundKnoten();
            var uhr = Stopwatch.StartNew();
            int n = _bauportion.Weiter(() => uhr.Elapsed.TotalMilliseconds);
            ParkingLotNetzRueckweg.Melde($"Bauportion Auftrag {_definitionsauftrag}: {_bauportion.Einheiten}/8 Einheiten, {n} Definitionen, gesamt {_bauportion.Gesamt}, {uhr.Elapsed.TotalMilliseconds:F3} ms, fertig={_bauportion.Fertig}.");
            return n;
        }

        private IEnumerable<int> HintergrundBauschritte()
        {
            // Sichtteile ebenfalls einzeln. Ein neues Layout wird erst nach
            // vollstaendiger Abnahme uebernommen; Rueckweg bleibt gespeichert.
            Entity[] kopie;
            using (var teile = _editRelatedParts.ToEntityArray(Unity.Collections.Allocator.Temp)) kopie = teile.ToArray();
            foreach (var e in kopie)
            {
                if (EntityManager.Exists(e) && EntityManager.HasComponent<ParkingLotPartRelation>(e)
                    && EntityManager.GetComponentData<ParkingLotPartRelation>(e).Lot == _editLot)
                { HidePart(e); yield return 0; }
            }
            foreach (int n in CreateAreaPreviewDefinitionsSchritte(_areaPreviewLayout)) yield return n;
        }

        private void MerkeHintergrundKnoten()
        {
            if (!EntityManager.HasBuffer<SubNet>(_lotOwner)) return;
            foreach (var s in EntityManager.GetBuffer<SubNet>(_lotOwner,true))
            {
                var e = s.m_SubNet;
                if (!ParkingLotNetzRueckweg.Lebt(EntityManager,e)) continue;
                if (EntityManager.HasComponent<Node>(e)) Merke(e);
                else if (EntityManager.HasComponent<Edge>(e))
                { var k = EntityManager.GetComponentData<Edge>(e); Merke(k.m_Start); Merke(k.m_End); }
            }
            void Merke(Entity e)
            {
                if (!ParkingLotNetzRueckweg.Lebt(EntityManager,e) || !EntityManager.HasComponent<Node>(e)) return;
                var p = EntityManager.GetComponentData<Node>(e).m_Position;
                var key = ((long)math.round(p.x*40),(long)math.round(p.z*40));
                if (!_hintergrundKnoten.TryGetValue(key,out var liste)) _hintergrundKnoten[key] = liste = new HashSet<Entity>();
                liste.Add(e);
            }
        }

        internal bool HintergrundVorbereitungFertig => _hintergrundPlanVorbereitet;
        private IEnumerable<int> HintergrundVorbereitungsschritte(ParkingLayout layout)
        {
            ParkingLotNetzRueckweg.Melde($"Geometrie Auftrag {_definitionsauftrag}: {_hintergrundRechenMs:F3} ms im Thread {_hintergrundRechenthread}; Spielthread {_hintergrundSpielthread}; 1 Layoutrechnung.");
            var altkurse = new List<float2[]>();
            foreach (var e in ParkingLotNetzRueckweg.Besitzteile(EntityManager,_editLot))
            {
                if (EntityManager.HasComponent<Edge>(e) && EntityManager.HasComponent<Curve>(e)
                    && EntityManager.HasComponent<PrefabRef>(e)
                    && _prefabSystem.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab) == "PLT Zoningstrasse (" + _altesZoningprefab + ")")
                { var c = EntityManager.GetComponentData<Curve>(e).m_Bezier; altkurse.Add(new[] { c.a.xz,c.d.xz }); }
                yield return 0;
            }
            _alteZoningkurse = altkurse.ToArray();
            ErgaenzeVorflaechen(layout,_areaPreviewSettings,_points.ToArray());
            _hintergrundPlanVorbereitet = true;
            ParkingLotNetzRueckweg.Melde("Planvorbereitung: 1 Vorflaechenlauf je Auftrag; weitere Prefabpruefungen verwenden diesen Plan.");
        }

        internal bool HintergrundRueckwegFlussPortion(Entity lot)
        {
            _rueckflussportion ??= new HintergrundPortion(HintergrundRueckflussschritte(lot));
            var uhr = Stopwatch.StartNew();
            _rueckflussportion.Weiter(() => uhr.Elapsed.TotalMilliseconds);
            return _rueckflussportion.Fertig;
        }
        private IEnumerable<int> HintergrundRueckflussschritte(Entity lot)
        {
            foreach (int n in ParkingLotNetzRueckweg.StelleFlussWiederHerSchritte(EntityManager,lot,
                (kante,knoten,prefab) => VerbindeVersorgungsgraph(kante,knoten,prefab,"Rueckweg"))) yield return n;
            foreach (int n in ParkingLotNetzRueckweg.FuellTraegerSchritte(EntityManager,lot)) yield return n;
        }

        private Unity.Collections.NativeArray<Entity> LokaleHintergrundStrassen(float2 punkt)
        {
            var baum = _netSearchSystem.GetNetSearchTree(true,out var deps);
            deps.Complete();
            using var result = new Unity.Collections.NativeList<Entity>(Unity.Collections.Allocator.Temp);
            var suche = new EntityIterator { Bounds = new Colossal.Mathematics.Bounds2(punkt-GassenSuchweite,punkt+GassenSuchweite), Results = result };
            baum.Iterate(ref suche);
            return new Unity.Collections.NativeArray<Entity>(result.AsArray(),Unity.Collections.Allocator.TempJob);
        }

        private IEnumerable<int> SchliesseHintergrundNetzbesitz()
        {
            // FindParkingConnection erreicht bei Owner Decal->Traeger->Lot
            // das Lot. Dessen SubNet muss VOR der Decal-Erzeugung voll sein.
            foreach (var e in _erhalteneNetzteile)
            {
                if (!ParkingLotNetzRueckweg.Lebt(EntityManager,e) || !EntityManager.HasComponent<Edge>(e)) continue;
                foreach (var ziel in new[] {_lotOwner,_lotCarrier})
                {
                    var b = EntityManager.GetBuffer<SubNet>(ziel);
                    if (!ParkingLotPuffer.Hat(b,e)) b.Add(new SubNet(e));
                }
                yield return 0;
            }
            Entity[] kopie;
            using (var teile = EntityManager.GetBuffer<SubNet>(_lotOwner,true).ToNativeArray(Unity.Collections.Allocator.Temp))
            {
                kopie = new Entity[teile.Length];
                for (int i = 0; i < teile.Length; i++) kopie[i] = teile[i].m_SubNet;
            }
            foreach (var e in kopie)
            {
                if (ParkingLotNetzRueckweg.Lebt(EntityManager,e)
                    && EntityManager.HasComponent<Owner>(e)
                    && EntityManager.GetComponentData<Owner>(e).m_Owner == _lotOwner
                    && EntityManager.HasComponent<Edge>(e))
                {
                    foreach (var ziel in new[] {_lotOwner,_lotCarrier})
                    {
                        var b = EntityManager.GetBuffer<SubNet>(ziel);
                        if (!ParkingLotPuffer.Hat(b,e)) b.Add(new SubNet(e));
                    }
                }
                yield return 0;
            }
        }
    }
}
