using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /*
     * FAHRWEGE TAUSCHEN STATT NEU BAUEN (Sync-Schritt 9, 2026-10-04).
     *
     * Fuer die 25-km/h-Fahrwege muss an einem alten Parkplatz nur eines
     * passieren: die unsichtbaren VANILLA-Wege bekommen unseren Klon als
     * Prefab. Gassen und Zoningstrassen sind schon Klone und frischen ihre
     * Spuren ueber `Updated` auf.
     *
     * Vanilla tauscht ein Netz-Prefab ueber das Ersetzen des Strassenwerkzeugs
     * (NetToolSystem.CreateReplacement: CreationDefinition.m_Original = Kante,
     * neues Prefab, Align|SubElevation). Das geht NUR ueber Temp + Apply: eine
     * Permanent-Definition mit m_Original legt eine ZWEITE Kante an
     * (GenerateEdges, TryGetOldEntity -> CreateEntity). ApplyNet uebernimmt
     * dann Owner und PrefabRef von der Vorschau auf das Original und behaelt
     * die Kante samt Knoten (Replace nur bei Zoning-Wechsel, GenerateEdges
     * 1615ff). Deshalb bekommt jede Vorschau den Owner des alten Weges -
     * ohne ihn entfernt ApplyNet den Owner am Original (UpdateComponent).
     *
     * Ein Temp-Tausch aendert NICHTS, bis er uebernommen wird. Wechselt der
     * Nutzer mittendrin das Werkzeug, wird die Vorschau verworfen und alles
     * bleibt wie es war - kein Abriss, kein Rueckweg.
     */
    public sealed partial class ParkingLotFahrwegTauschWerkzeug : ToolBaseSystem
    {
        public const string Id = "PLT Fahrwegtausch";
        public override string toolID => Id;
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        private enum Stufe { Frei, Anlegen, Warten, Uebernehmen, Pruefen }

        private Stufe _stufe;
        private Entity _lot;
        private readonly List<(Entity Kante, Entity Ziel, Entity Besitzer)> _plan = new();
        private readonly List<Entity> _definitionen = new();
        private int _seit;
        private EntityQuery _fremdeDefinitionen;
        private EntityQuery _temps;
        internal System.Action<Entity, bool, string> Fertig;

        internal bool Beschaeftigt => _stufe != Stufe.Frei;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _fremdeDefinitionen = GetEntityQuery(ComponentType.ReadOnly<CreationDefinition>());
            _temps = GetEntityQuery(ComponentType.ReadOnly<Temp>());
        }

        internal void Starte(Entity lot, List<(Entity Kante, Entity Ziel, Entity Besitzer)> plan)
        {
            _lot = lot;
            _plan.Clear();
            _plan.AddRange(plan);
            _definitionen.Clear();
            _stufe = Stufe.Anlegen;
            _seit = UnityEngine.Time.frameCount;
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            applyMode = ApplyMode.None;
            var bild = UnityEngine.Time.frameCount;
            // Letztes Update nach einem Werkzeugwechsel durch den Nutzer:
            // eigene Vorschau verwerfen, nichts wurde uebernommen.
            if (m_ToolSystem.activeTool != this)
            {
                if (_stufe == Stufe.Warten || _stufe == Stufe.Uebernehmen) applyMode = ApplyMode.Clear;
                if (_stufe != Stufe.Frei && _stufe != Stufe.Pruefen) Beende(false, "Werkzeug gewechselt, Tausch verworfen");
                return inputDeps;
            }
            switch (_stufe)
            {
                case Stufe.Anlegen:
                    // Nur in einem Bild ohne fremde Entwuerfe: unsere Vorschau
                    // soll nie mit einer anderen gemeinsam uebernommen werden.
                    if (_fremdeDefinitionen.CalculateEntityCount() > 0 || _temps.CalculateEntityCount() > 0)
                    {
                        if (bild - _seit > 600) Beende(false, "fremde Entwuerfe blieben 600 Bilder");
                        break;
                    }
                    LegeDefinitionenAn();
                    _stufe = Stufe.Warten;
                    _seit = bild;
                    break;
                case Stufe.Warten:
                    {
                        // Angelegt wurde nur in einem Bild ohne fremde Temps;
                        // alles, was jetzt Temp ist, stammt von uns (auch
                        // Nachbarkopien fuer die Knotenform).
                        var eigene = ZaehleEigeneTemps();
                        if (eigene >= _plan.Count) { _stufe = Stufe.Uebernehmen; break; }
                        if (bild - _seit > 30) { applyMode = ApplyMode.Clear; Beende(false, $"nur {eigene}/{_plan.Count} Tauschvorschauen entstanden"); }
                        break;
                    }
                case Stufe.Uebernehmen:
                    ParkingLotSchrittmarke.Aenderung("Fahrwegtausch: Lot " + _lot.Index + " wird uebernommen (Apply)");
                    applyMode = ApplyMode.Apply;
                    _stufe = Stufe.Pruefen;
                    _seit = bild;
                    break;
                case Stufe.Pruefen:
                    if (bild - _seit < 3) break;
                    var ok = Pruefe(out var text);
                    Beende(ok, text);
                    break;
            }
            return inputDeps;
        }

        private void LegeDefinitionenAn()
        {
            for (var i = 0; i < _plan.Count; i++)
            {
                var (kante, ziel, besitzer) = _plan[i];
                var edge = EntityManager.GetComponentData<Edge>(kante);
                var curve = EntityManager.GetComponentData<Curve>(kante);
                var d = EntityManager.CreateEntity();
                EntityManager.AddComponentData(d, new CreationDefinition
                {
                    m_Original = kante,
                    m_Prefab = ziel,
                    m_Owner = besitzer,
                    m_Flags = CreationFlags.Align | CreationFlags.SubElevation,
                });
                EntityManager.AddComponent<Updated>(d);
                var kurs = new NetCourse
                {
                    m_Curve = curve.m_Bezier,
                    m_Length = curve.m_Length,
                    m_FixedIndex = EntityManager.HasComponent<Fixed>(kante)
                        ? EntityManager.GetComponentData<Fixed>(kante).m_Index : -1,
                };
                kurs.m_StartPosition.m_Entity = edge.m_Start;
                kurs.m_StartPosition.m_Position = curve.m_Bezier.a;
                kurs.m_StartPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve.m_Bezier));
                kurs.m_StartPosition.m_CourseDelta = 0f;
                kurs.m_StartPosition.m_Flags |= CoursePosFlags.IsFirst;
                kurs.m_EndPosition.m_Entity = edge.m_End;
                kurs.m_EndPosition.m_Position = curve.m_Bezier.d;
                kurs.m_EndPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve.m_Bezier));
                kurs.m_EndPosition.m_CourseDelta = 1f;
                kurs.m_EndPosition.m_Flags |= CoursePosFlags.IsLast;
                EntityManager.AddComponentData(d, kurs);
                _definitionen.Add(d);
            }
            ParkingLotSchrittmarke.Aenderung("Fahrwegtausch: Lot " + _lot.Index + ", " + _plan.Count + " Ersetzungs-Definitionen angelegt");
            Mod.log.Info($"PLT-Fahrwegtausch: Lot {_lot.Index}: {_plan.Count} Ersetzungs-Definitionen (Vanilla-Ersetzen, Temp + Apply).");
        }

        private int ZaehleEigeneTemps()
        {
            var originale = new HashSet<Entity>();
            foreach (var p in _plan) originale.Add(p.Kante);
            using var alle = _temps.ToEntityArray(Allocator.Temp);
            int eigene = 0;
            foreach (var e in alle)
                if (EntityManager.HasComponent<Edge>(e)
                    && originale.Contains(EntityManager.GetComponentData<Temp>(e).m_Original)) eigene++;
            return eigene;
        }

        private bool Pruefe(out string text)
        {
            int getauscht = 0, besitzOk = 0;
            foreach (var (kante, ziel, besitzer) in _plan)
            {
                if (!EntityManager.Exists(kante) || EntityManager.HasComponent<Deleted>(kante)) continue;
                if (EntityManager.GetComponentData<PrefabRef>(kante).m_Prefab == ziel) getauscht++;
                if (EntityManager.HasComponent<Owner>(kante)
                    && EntityManager.GetComponentData<Owner>(kante).m_Owner == besitzer) besitzOk++;
            }
            text = $"{getauscht}/{_plan.Count} Wege mit neuem Prefab, Besitzer erhalten {besitzOk}/{_plan.Count}";
            return getauscht == _plan.Count && besitzOk == _plan.Count;
        }

        private void Beende(bool ok, string text)
        {
            foreach (var d in _definitionen)
                if (EntityManager.Exists(d)) EntityManager.DestroyEntity(d);
            _definitionen.Clear();
            var lot = _lot;
            _stufe = Stufe.Frei;
            _lot = Entity.Null;
            _plan.Clear();
            Mod.log.Info($"PLT-Fahrwegtausch: Lot {lot.Index}: {(ok ? "fertig" : "abgebrochen")} - {text}.");
            ParkingLotSchrittmarke.Aenderung("Fahrwegtausch: Lot " + lot.Index + (ok ? " fertig" : " abgebrochen"));
            Fertig?.Invoke(lot, ok, text);
        }
    }
}
