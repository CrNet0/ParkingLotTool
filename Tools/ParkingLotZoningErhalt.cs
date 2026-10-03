using System;
using System.Collections.Generic;
using System.Linq;
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
    public sealed partial class ParkingLotToolSystem
    {
        private float2[][] _alteZoningkurse;
        private string _altesZoningprefab;
        private readonly HashSet<Entity> _erhalteneNetzteile = new HashSet<Entity>();
        private bool _zoningErhalten;

        private static float2[][] Zoningkurse(ParkingLayout layout)
            => layout.NetLine.Where(p => p.Kind == "zoning")
                .Select(p => new[] { p.A, p.B }).ToArray();

        private void MerkeZoningbestand(ParkingLayout layout)
        {
            _alteZoningkurse = Zoningkurse(layout);
            _altesZoningprefab = _baukontext?.Zoningstrasse ?? _uiSystem.CurrentSettings().Zoningstrasse;
        }

        /**
         * Welche Zoningteile des bearbeiteten Parkplatzes bleiben stehen?
         * Ohne Nebenwirkung auf den Bau: die Vorplanung der Leitungen fragt
         * das waehrend des Bearbeitens laufend (`melden: false`), der Bau
         * genau einmal ueber `PlaneNetzerhaltSchritte`.
         */
        private bool SammleErhalteneZoningteile(HashSet<Entity> ziel, bool melden)
        {
            foreach (int n in SammleErhalteneZoningteileSchritte(ziel,melden)) { }
            return ziel.Count > 0;
        }

        private IEnumerable<int> SammleErhalteneZoningteileSchritte(HashSet<Entity> ziel, bool melden)
        {
            ziel.Clear();
            if (!IsEditing || _areaPreviewLayout == null || !ZoningErhalt.Gleich(
                _alteZoningkurse, Zoningkurse(_areaPreviewLayout), _altesZoningprefab,
                _baukontext?.Zoningstrasse ?? _uiSystem.CurrentSettings().Zoningstrasse)) yield break;

            Entity[] kandidaten;
            if (_bauarbeiter) kandidaten = ParkingLotNetzRueckweg.Besitzteile(EntityManager,_editLot);
            else using (var teile = _editOwnerParts.ToEntityArray(Allocator.Temp)) kandidaten = teile.ToArray();
            if (!TryResolveZoningRoad(_altesZoningprefab,out var prefab)) yield break;
            MerkeZoningSeitenGrundlage();
            var indizes = new List<int>();
            for (int i = 0; i < _areaPreviewLayout.NetLine.Length; i++)
            {
                var piece = _areaPreviewLayout.NetLine[i];
                if (piece.Kind != "zoning") continue;
                indizes.Add(i);
                if (FindeNetzerhalt(i,piece,piece.A,piece.B,prefab,Entity.Null,kandidaten,ziel,melden)) { yield return 0; continue; }
                ziel.Clear();
                if (melden) foreach (int index in indizes) _erhalteneKursketten.Remove(("zoning",index));
                if (melden) Mod.log.Info($"PLT-Vorbauzettel Netzerhalt: Zoningkurs {i} in Kurs/Prefab/Seiten/Anschluss veraendert oder unvollstaendig; 0 Zoningkurse erhalten.");
                yield break;
            }
            if (melden) Mod.log.Info($"PLT-Vorbauzettel Netzerhalt: {indizes.Count} unveraenderte Zoningkurse, {ziel.Count} Kanten/Knoten erhalten.");

        }

        /**
         * Erhaltene Netzstrassen behalten Kante, Owner und Geometrie; nur
         * ihre Fahrspuren stammen noch aus dem alten Prefabstand. `Updated`
         * laesst Vanilla sie aus den fertigen Klonen neu ableiten (25 km/h,
         * Kosten). Gilt fuer Edit UND Hintergrund-Neubau (2026-10-02: nach
         * einem Edit standen 8 Vanilla-Spuren auf erhaltenen Kanten).
         */
        private int MeldeErhalteneNetzteileAn()
        {
            int n = 0;
            foreach (var e in _erhalteneNetzteile)
                if (ParkingLotNetzRueckweg.Lebt(EntityManager, e))
                { EntityManager.AddComponent<Updated>(e); n++; }
            return n;
        }

        private void UebertrageZoningbestand(Entity old, Entity next, Entity carrier)
        {
            if (_erhalteneNetzteile.Count == 0) return;
            foreach (var teil in _erhalteneNetzteile)
            {
                if (!EntityManager.Exists(teil) || EntityManager.HasComponent<Deleted>(teil)) continue;
                if (!_bauarbeiter && EntityManager.HasComponent<Owner>(teil))
                {
                    var anker = EntityManager.GetComponentData<Owner>(teil).m_Owner;
                    if (anker != old && ParkingLotBesitz.GehoertZu(EntityManager,anker,old))
                    {
                        EntityManager.SetComponentData(anker,new Owner(next));
                        EntityManager.SetComponentData(anker,new ParkingLotPartRelation { Lot = next, Carrier = carrier });
                    }
                }
                if (!_bauarbeiter && EntityManager.HasComponent<Owner>(teil)
                    && EntityManager.GetComponentData<Owner>(teil).m_Owner == old)
                    EntityManager.SetComponentData(teil, new Owner(next));
                if (EntityManager.HasComponent<ParkingLotPartRelation>(teil))
                {
                    var relation = EntityManager.GetComponentData<ParkingLotPartRelation>(teil);
                    if (relation.Lot == old)
                    {
                        relation.Lot = next; relation.Carrier = carrier;
                        EntityManager.SetComponentData(teil, relation);
                    }
                }
                if (!EntityManager.HasComponent<Edge>(teil)) continue;
                foreach (var owner in new[] { next, carrier })
                {
                    if (!EntityManager.HasBuffer<SubNet>(owner)) EntityManager.AddBuffer<SubNet>(owner);
                    var nets = EntityManager.GetBuffer<SubNet>(owner);
                    bool found = false;
                    for (int i = 0; i < nets.Length; i++) if (nets[i].m_SubNet == teil) found = true;
                    if (!found) nets.Add(new SubNet(teil));
                }
            }
            if (!_bauarbeiter) EntferneAlteZoningverweise(old);
            if (EntityManager.HasComponent<ParkingLotCarrierReference>(old))
                EntferneAlteZoningverweise(EntityManager.GetComponentData<ParkingLotCarrierReference>(old).Carrier);
        }

        private void EntferneAlteZoningverweise(Entity owner)
        {
            if (!EntityManager.Exists(owner) || !EntityManager.HasBuffer<SubNet>(owner)) return;
            var nets = EntityManager.GetBuffer<SubNet>(owner);
            for (int i = nets.Length - 1; i >= 0; i--)
                if (_erhalteneNetzteile.Contains(nets[i].m_SubNet)) nets.RemoveAt(i);
        }
    }
}
