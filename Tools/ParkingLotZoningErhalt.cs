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
        private readonly HashSet<Entity> _erhalteneZoningteile = new HashSet<Entity>();
        private bool _zoningErhalten;

        private static float2[][] Zoningkurse(ParkingLayout layout)
            => layout.NetLine.Where(p => p.Kind == "zoning")
                .Select(p => new[] { p.A, p.B }).ToArray();

        private void MerkeZoningbestand(ParkingLayout layout)
        {
            _alteZoningkurse = Zoningkurse(layout);
            _altesZoningprefab = _baukontext?.Zoningstrasse ?? _uiSystem.CurrentSettings().Zoningstrasse;
        }

        private void PlaneZoningerhalt()
        {
            _zoningErhalten = SammleErhalteneZoningteile(_erhalteneZoningteile, true);
        }

        /**
         * Welche Zoningteile des bearbeiteten Parkplatzes bleiben stehen?
         * Ohne Nebenwirkung auf den Bau: die Vorplanung der Leitungen fragt
         * das waehrend des Bearbeitens laufend (`melden: false`), der Bau
         * genau einmal ueber `PlaneZoningerhalt`.
         */
        private bool SammleErhalteneZoningteile(HashSet<Entity> ziel, bool melden)
        {
            ziel.Clear();
            if (!IsEditing || _areaPreviewLayout == null || !ZoningErhalt.Gleich(
                _alteZoningkurse, Zoningkurse(_areaPreviewLayout), _altesZoningprefab,
                _baukontext?.Zoningstrasse ?? _uiSystem.CurrentSettings().Zoningstrasse)) return false;

            using var teile = _editOwnerParts.ToEntityArray(Allocator.Temp);
            foreach (var teil in teile)
            {
                if (!ParkingLotBesitz.GehoertZu(EntityManager,EntityManager.GetComponentData<Owner>(teil).m_Owner,_editLot)
                    || !EntityManager.HasComponent<Edge>(teil)
                    || EntityManager.HasComponent<Deleted>(teil)
                    || EntityManager.HasComponent<Temp>(teil)
                    || !EntityManager.HasComponent<PrefabRef>(teil)) continue;
                var prefab = EntityManager.GetComponentData<PrefabRef>(teil).m_Prefab;
                if (!_prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var asset)
                    || asset.name != "PLT Zoningstrasse (" + _altesZoningprefab + ")") continue;
                ziel.Add(teil);
                var edge = EntityManager.GetComponentData<Edge>(teil);
                ziel.Add(edge.m_Start);
                ziel.Add(edge.m_End);
            }
            /*
             * ERHALTEN NUR, WENN AUCH DIE SEITEN STIMMEN.
             *
             * Die Seiten kommen seit 2026-09-25 ausschliesslich ueber die
             * Baudefinition (`EntscheideZoningseiten`). Eine erhaltene Kante
             * wird nicht neu definiert - hat der Nutzer eine Seite
             * umgeschaltet, kaeme die Aenderung nie an. Dann wird neu gebaut.
             */
            MerkeZoningSeitenGrundlage();
            foreach (var teil in ziel)
            {
                if (!EntityManager.HasComponent<Edge>(teil)
                    || !EntityManager.HasComponent<Curve>(teil)) continue;
                var kurve = EntityManager.GetComponentData<Curve>(teil).m_Bezier;
                if (!EntscheideZoningseiten(kurve.a.xz, kurve.d.xz, false,
                        out var linksAus, out var rechtsAus, out _)) continue;
                if (LiestSeite(teil, true) == linksAus
                    && LiestSeite(teil, false) == rechtsAus) continue;
                if (melden)
                    Mod.log.Info("PLT-Vorbauzettel Zoningerhalt: Strassenplan gleich, "
                        + "aber eine Seite wurde umgeschaltet - die Zoningstrassen "
                        + "werden neu gebaut.");
                ziel.Clear();
                break;
            }
            if (melden)
                Mod.log.Info("PLT-Vorbauzettel Zoningerhalt: unveraenderter Strassenplan; "
                    + ziel.Count + " bestehende Kanten/Knoten erhalten. "
                    + "Zonenbloecke und Gebaeude werden nicht neu erzeugt.");
            return ziel.Count > 0;
        }

        /**
         * Erhaltene Zoningstrassen behalten Kante, Owner und Geometrie; nur
         * ihre Fahrspuren stammen noch aus dem alten Prefabstand. `Updated`
         * laesst Vanilla sie aus den fertigen Klonen neu ableiten (25 km/h,
         * Kosten). Gilt fuer Edit UND Hintergrund-Neubau (2026-10-02: nach
         * einem Edit standen 8 Vanilla-Spuren auf erhaltenen Kanten).
         */
        private int MeldeErhalteneZoningteileAn()
        {
            int n = 0;
            foreach (var e in _erhalteneZoningteile)
                if (ParkingLotNetzRueckweg.Lebt(EntityManager, e))
                { EntityManager.AddComponent<Updated>(e); n++; }
            return n;
        }

        private void UebertrageZoningbestand(Entity old, Entity next, Entity carrier)
        {
            if (!_zoningErhalten) return;
            foreach (var teil in _erhalteneZoningteile)
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
                if (_erhalteneZoningteile.Contains(nets[i].m_SubNet)) nets.RemoveAt(i);
        }
    }
}
