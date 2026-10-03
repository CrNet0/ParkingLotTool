using System.Collections.Generic;
using Colossal.Mathematics;
using ParkingLotTool.Geometry;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    // Trassenwahl je zusammenhaengendem eigenen Strassennetz.
    // Materialisierung, Vanilla-Apply und Graph-/Flussnachweis stehen in den
    // weiteren AutoVersorgung-Teildateien; Naehe allein gilt nicht als Anschluss.
    internal struct Versorgungstrasse
    {
        /** Wo an unserem Parkplatz die Leitung beginnt. */
        internal float3 Start;

        /** Die fremde Strassenkante, an der sie enden soll. */
        internal Entity Zielkante;
        internal List<Entity> Startnetz;
        internal Entity Startknoten;
        internal List<Entity> Startkanten;
        internal List<float2> Stromweg, Wasserweg;

        /** Der Punkt auf dieser Kante. */
        internal float3 Ziel;

        /** Wie der Startpunkt zustande kam - fuer den Bauzettel. */
        internal string Herkunft;

        /** Laenge der geplanten Trasse in Metern. */
        internal float Laenge;

        internal bool Gefunden => Zielkante != Entity.Null;
    }

    public sealed partial class ParkingLotToolSystem
    {
        /**
         * Wie weit um den Parkplatz herum nach einer Stadtstrasse gesucht
         * wird. Derselbe Wert wie bei der Zufahrtssuche - was fuer eine
         * Zufahrt zu weit weg ist, ist es fuer eine Leitung auch.
         */
        private const float AutoVersorgungSuchradius = 128f;

        /**
         * Zerlegt unsere Kanten in zusammenhaengende Netze.
         *
         * Verbunden heisst: gemeinsamer Knoten. Das ist dieselbe Bedingung,
         * nach der CS2 Strom und Wasser weiterleitet.
         */
        private List<List<Entity>> SammleVersorgungsgruppen(List<Entity> unsere)
        {
            var kanten = new List<Versorgungskante>();
            var entitaeten = new Dictionary<int, Entity>();
            foreach (var entity in unsere)
            {
                var edge = EntityManager.GetComponentData<Edge>(entity);
                kanten.Add(new Versorgungskante { Id = entity.Index,
                    Startknoten = edge.m_Start == Entity.Null ? 0 : edge.m_Start.Index + 1,
                    Endknoten = edge.m_End == Entity.Null ? 0 : edge.m_End.Index + 1 });
                entitaeten[entity.Index] = entity;
            }
            var gruppen = new List<List<Entity>>();
            foreach (var gruppe in VersorgungstrassenPlan.Gruppen(kanten))
                gruppen.Add(gruppe.Kanten.ConvertAll(k => entitaeten[k.Id]));
            return gruppen;
        }

        /** Alle dauerhaften Strassenkanten unseres Traegers. */
        private List<Entity> SammleUnsereKanten(Entity traeger)
        {
            var lot = EntityManager.HasComponent<ParkingLotPartRelation>(traeger)
                ? EntityManager.GetComponentData<ParkingLotPartRelation>(traeger).Lot
                : EntityManager.HasComponent<Owner>(traeger)
                    ? EntityManager.GetComponentData<Owner>(traeger).m_Owner : Entity.Null;
            return ParkingLotTeilnetz.Kanten(EntityManager, lot, traeger);
        }

        /**
         * NICHT JEDE UNSERER STRASSEN NIMMT EINE LEITUNG AN.
         *
         * GEMESSEN am 2026-09-05, 23:26. Drei Netze, drei verschiedene
         * Ergebnisse - und die neue Torzeile nennt den Grund beim Namen:
         *
         *   STARTTORE [Netz 1/3]: Kante ..., Layer hin/zurueck 0/0,
         *     Randabstand -2,000 m / Suchradius 0,750 m, Hoehe 10,000 m
         *   STARTTORE [Netz 2/3]: Kante ..., Layer hin/zurueck 1/1, ...
         *   STARTTORE [Netz 3/3]: Kante ..., Layer hin/zurueck 1/1, ...
         *
         * Abstand und Hoehe stimmten ueberall. Bei Netz 1 scheiterte allein
         * die EBENENPRUEFUNG, und zwar in beide Richtungen. Unser Parkplatz
         * besteht nicht nur aus der Zoningstrasse: die Fahrgassen und
         * Querwege sind unsichtbare Pfade, und die fuehren weder Strom noch
         * Wasser. Ein Anschlusspunkt an so einem Weg kann gar nicht klappen.
         *
         * Deshalb kommen nur Punkte an Kanten in Frage, die BEIDE Leitungsarten annehmen. Was keinen Strom
         * fuehrt, ist kein Anschlusspunkt - egal wie guenstig es liegt.
         */
        private bool KanteNimmtVersorgung(Entity kante)
        {
            if (kante == Entity.Null || !EntityManager.Exists(kante))
                return false;
            if (!EntityManager.HasComponent<PrefabRef>(kante)) return false;
            var prefab = EntityManager
                .GetComponentData<PrefabRef>(kante).m_Prefab;
            return AvPrefabNimmtVersorgung(prefab);
        }

        private bool AvPrefabNimmtVersorgung(Entity prefab)
        {
            if (!EntityManager.HasComponent<NetData>(prefab)) return false;
            var ebenen = EntityManager
                .GetComponentData<NetData>(prefab).m_LocalConnectLayers;
            return (ebenen & Layer.PowerlineLow) != 0
                && (ebenen & Layer.WaterPipe) != 0
                && (ebenen & Layer.SewagePipe) != 0;
        }

        /**
         * Fremde Strassen im Umkreis - die moeglichen Gegenstellen.
         *
         * Eigene Kanten bleiben Kandidaten. Welche davon erlaubt sind,
         * entscheidet `AvZielErlaubt` je Startgruppe: eine fremde Strasse
         * immer, eine eigene nur aus einem anderen Teil. Vorschau und
         * geloeschte Kanten bleiben ausgeschlossen. Eine Leitung an einen
         * Fussweg zu haengen bringt keinen Strom.
         */
        private List<(Entity Kante, Bezier4x3 Bogen)> SammleZielstrassen(
            List<Entity> unsere)
        {
            var treffer = new List<(Entity, Bezier4x3)>();
            if (_netSearchSystem == null) return treffer;

            /*
             * DAS SUCHFELD KOMMT AUS DEN EIGENEN STRASSEN, NICHT AUS DEM
             * UMRISS.
             *
             * Der Umriss ist zu diesem Zeitpunkt schon zurueckgesetzt (siehe
             * `_avUmriss`), und selbst wenn nicht: gesucht wird eine Strasse
             * in der Naehe unserer STRASSEN. Die stehen hier ohnehin fertig
             * da und sind die verlaesslichere Quelle.
             */
            if (!AvSuchfeld(unsere, out var suchfeld, out var min, out var max))
                return treffer;

            var baum = _netSearchSystem.GetNetSearchTree(
                readOnly: true, out var deps);
            deps.Complete();
            using var gefunden = new NativeList<Entity>(64, Allocator.Temp);
            var iterator = new EntityIterator
            {
                Bounds = suchfeld,
                Results = gefunden,
            };
            baum.Iterate(ref iterator);

            /*
             * ZAEHLER AN JEDER ABWEISUNG.
             *
             * Bleibt die Suche leer, muss die Meldung sagen WORAN es lag -
             * sonst steht da nur "keine Strasse gefunden" und die Ursache ist
             * wieder Ratesache. Genau das ist am 2026-09-05 um 19:28 passiert.
             */
            var gesehen = new HashSet<Entity>();
            var raus_geloescht = 0;
            var raus_temp = 0;
            var raus_keineKante = 0;
            var raus_layer = 0;
            var raus_keineStrasse = 0;
            for (var i = 0; i < gefunden.Length; i++)
            {
                var kante = gefunden[i];
                if (kante == Entity.Null || !gesehen.Add(kante)) continue;
                if (!EntityManager.Exists(kante)) continue;

                if (EntityManager.HasComponent<Deleted>(kante))
                { raus_geloescht++; continue; }
                if (EntityManager.HasComponent<Game.Tools.Temp>(kante))
                { raus_temp++; continue; }
                if (!EntityManager.HasComponent<Game.Net.Edge>(kante)
                    || !EntityManager.HasComponent<Game.Net.Curve>(kante)
                    || !EntityManager.HasComponent<PrefabRef>(kante))
                { raus_keineKante++; continue; }
                if (!KanteNimmtVersorgung(kante))
                { raus_layer++; continue; }
                var prefab = EntityManager
                    .GetComponentData<PrefabRef>(kante).m_Prefab;
                if (!EntityManager.HasComponent<RoadData>(prefab))
                { raus_keineStrasse++; continue; }
                treffer.Add((kante, EntityManager
                    .GetComponentData<Game.Net.Curve>(kante).m_Bezier));
            }
            Mod.log.Info($"PLT-Autoversorgung Strassensuche: Feld "
                + $"({min.x:F0}/{min.y:F0}) bis ({max.x:F0}/{max.y:F0}), "
                + $"{gefunden.Length} Treffer im Suchbaum, {treffer.Count} "
                + $"brauchbar. Verworfen: {raus_geloescht} geloescht, {raus_temp} Vorschau, "
                + $"{raus_keineKante} ohne Kante/Kurve, {raus_layer} ohne "
                + $"Versorgungslayer, {raus_keineStrasse} keine Strasse.");
            return treffer;
        }

        /**
         * Das Rechteck um unsere Strassen, in dem ueberhaupt gesucht wird.
         *
         * Gebraucht von der Zielsuche UND von der Hindernissuche. Zweimal
         * dasselbe Feld aufzubauen hiesse, zwei Antworten auf dieselbe Frage
         * zu pflegen.
         */
        private bool AvSuchfeld(List<Entity> unsere, out Bounds2 feld,
            out float2 min, out float2 max)
        {
            min = new float2(float.MaxValue, float.MaxValue);
            max = new float2(float.MinValue, float.MinValue);
            for (var i = 0; i < unsere.Count; i++)
            {
                if (!EntityManager.HasComponent<Game.Net.Curve>(unsere[i]))
                    continue;
                var bogen = EntityManager
                    .GetComponentData<Game.Net.Curve>(unsere[i]).m_Bezier;
                for (var s = 0; s <= 4; s++)
                {
                    var punkt = MathUtils.Position(bogen, s / 4f).xz;
                    min = math.min(min, punkt);
                    max = math.max(max, punkt);
                }
            }
            feld = default;
            if (min.x > max.x) return false;
            feld = new Bounds2(min - AutoVersorgungSuchradius,
                max + AutoVersorgungSuchradius);
            return true;
        }

        /**
         * FREMDE ERDLEITUNGEN IM UMKREIS - SIE SIND HINDERNISSE.
         *
         * Am 2026-09-16 hat ein 'High-voltage Ground Cable' des Nutzers die
         * Leitung zwischen zwei eigenen Zonen verhindert. Der Wegesucher kannte
         * nur unsere eigenen Strassen und ist deshalb schnurgerade hinein - er
         * hat nicht "trotzdem" entschieden, er hat das Kabel nicht gesehen.
         *
         * NICHT QUERBAR, anders als unsere Fahrgassen. Aus
         * `Game.Net.ValidationHelpers.CheckOverlap` (Dekompilat): Netz gegen
         * Netz ist ein reiner Geometrieschnitt der Huellen bei ueberlappender
         * Kollisionsmaske, Schwere `Error`. Es gibt keine Ausnahme fuers
         * Kreuzen - ohne echten Kreuzungsknoten, und den setzen wir nicht,
         * stoesst eine Querung genauso an wie ein Nebeneinanderherlaufen.
         *
         * Was eine Strasse ist, gehoert NICHT hierher: an Strassen schliessen
         * wir an, sie sind unsere Ziele. Gesucht sind die reinen Leitungen -
         * kein `RoadData`, aber Strom- oder Wasseranschlussdaten. Das ist
         * dieselbe Unterscheidung, die `AvErfasseStadtpfade` schon trifft.
         *
         * Unsere eigenen frueheren Leitungen stehen bewusst mit drin: fuer CS2
         * sind sie genauso im Weg wie fremde.
         */
        private void SammleFremdleitungen(List<Entity> unsere)
        {
            _avFremdleitungen.Clear();
            if (_netSearchSystem == null) return;
            if (!AvSuchfeld(unsere, out var suchfeld, out _, out _)) return;

            var baum = _netSearchSystem.GetNetSearchTree(
                readOnly: true, out var deps);
            deps.Complete();
            using var gefunden = new NativeList<Entity>(64, Allocator.Temp);
            var iterator = new EntityIterator { Bounds = suchfeld, Results = gefunden };
            baum.Iterate(ref iterator);

            var eigene = new HashSet<Entity>(unsere);
            var gesehen = new HashSet<Entity>();
            var strom = 0; var wasser = 0;
            for (var i = 0; i < gefunden.Length; i++)
            {
                var kante = gefunden[i];
                if (kante == Entity.Null || !gesehen.Add(kante)) continue;
                if (!EntityManager.Exists(kante)) continue;
                if (eigene.Contains(kante)) continue;
                if (EntityManager.HasComponent<Deleted>(kante)) continue;
                if (EntityManager.HasComponent<Game.Tools.Temp>(kante)) continue;
                if (!EntityManager.HasComponent<Game.Net.Edge>(kante)
                    || !EntityManager.HasComponent<Game.Net.Curve>(kante)
                    || !EntityManager.HasComponent<PrefabRef>(kante)) continue;
                var prefab = EntityManager
                    .GetComponentData<PrefabRef>(kante).m_Prefab;
                if (EntityManager.HasComponent<RoadData>(prefab)) continue;
                var hatStrom = EntityManager
                    .HasComponent<ElectricityConnectionData>(prefab);
                var hatWasser = EntityManager
                    .HasComponent<WaterPipeConnectionData>(prefab);
                if (!hatStrom && !hatWasser) continue;
                if (hatStrom) strom++; else wasser++;
                _avFremdleitungen.Add(kante);
            }
            Mod.log.Info($"PLT-Autoversorgung FREMDLEITUNGEN: {_avFremdleitungen.Count} "
                + $"Kante(n) im Suchfeld ({strom} Strom, {wasser} Wasser/Abwasser) "
                + "werden umfahren; Kreuzen ist bei Leitungen nicht erlaubt.");
        }

        private const float AutoVersorgungAnschlussbereich = 8f;
        private const float AutoVersorgungSicherheitszugabe = 0.5f;

        /**
         * Wieviele verschiedene Wege ein Netz bekommt, bevor aufgegeben wird.
         *
         * Jeder Anlauf kostet einen vollen Bauzyklus, deshalb keine offene
         * Zahl. Drei decken "da lag zufaellig etwas" ab; wer danach nicht
         * durchkommt, hat ein anderes Problem, und das soll im Log stehen.
         */
        private const int AutoVersorgungHoechstversuche = 3;
    }
}
