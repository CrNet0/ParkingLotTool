using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Game;
using Game.Areas;
using Game.Common;
using Game.Rendering;
using Game.SceneFlow;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private (int Areas, int Objects) HideVisibleParts(Entity lot)
        {
            var areas = 0;
            var objects = 0;
            using (var parts = _editOwnerParts.ToEntityArray(Allocator.Temp))
            {
                for (var i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (EntityManager.GetComponentData<Owner>(part).m_Owner != lot
                        || !EntityManager.HasComponent<Area>(part)) continue;
                    if (HidePart(part)) areas++;
                }
            }
            using (var parts = _editRelatedParts.ToEntityArray(Allocator.Temp))
            {
                for (var i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (EntityManager
                            .GetComponentData<ParkingLotPartRelation>(part).Lot != lot
                        || !EntityManager.HasComponent<Game.Objects.Object>(part))
                        continue;
                    if (HidePart(part)) objects++;
                }
            }
            return (areas, objects);
        }

        private bool HidePart(Entity part)
        {
            if (EntityManager.HasComponent<Hidden>(part)) return false;
            EntityManager.AddComponent<Hidden>(part);
            if (!EntityManager.HasComponent<BatchesUpdated>(part))
                EntityManager.AddComponent<BatchesUpdated>(part);
            _hiddenByEdit.Add(part);
            return true;
        }

        private void RestoreHiddenParts()
        { foreach (int n in RestoreHiddenPartsSchritte()) { } }
        private IEnumerable<int> RestoreHiddenPartsSchritte()
        {
            foreach (var part in _hiddenByEdit)
            {
                yield return 0;
                if (!EntityManager.Exists(part)
                    || !EntityManager.HasComponent<Hidden>(part)) continue;
                EntityManager.RemoveComponent<Hidden>(part);
                if (!EntityManager.HasComponent<BatchesUpdated>(part))
                    EntityManager.AddComponent<BatchesUpdated>(part);
            }
            _hiddenByEdit.Clear();
        }

        /** Prueft Lot-Loeschung und den zweiten Schritt eines Umbaus. */
        /**
         * DIE ALTEN WEGE MUESSEN WEG, BEVOR DIE NEUEN ENTSTEHEN.
         *
         * GEMESSEN am 2026-08-31, Log des Nutzers:
         *
         *     PLT-Bearbeiten: Einstieg ...; NETZE BLEIBEN AKTIV.
         *     PLT-Besitzer: nur 54 Wegteile aus 72 Kursen haben einen
         *                   Besitzer bekommen.
         *
         * Ein `NetCourse` ergibt normalerweise eine Kante UND ihre Knoten,
         * also MEHR Entities als Kurse. 54 aus 72 heisst: fuer die Mehrzahl
         * der Kurse entstand gar nichts Neues.
         *
         * Der Grund: unsere Kurse geben zwar keinen Verbindungspunkt vor
         * (`m_Entity = Entity.Null` in `ParkingLotNetBuilder`), aber CS2
         * verbindet ueber IDENTISCHE ENDPUNKTE. Wo sich am Layout nichts
         * geaendert hat, liegt der neue Kurs exakt auf der alten Kante; CS2
         * legt dort nichts an, die Stelle gehoert weiter dem ALTEN Traeger -
         * und faellt mit ihm. Beide Haelften des Nutzerbefundes: einige Wege
         * werden nicht gesetzt, andere verschwinden.
         *
         * Der Aufruf sitzt genau im Uebergang `Idle -> CreateDefinitions`:
         * Enter ist angenommen, die Definitionen entstehen erst im naechsten
         * Durchgang. Damit liegt ein Frame zwischen Loeschen und Anlegen, und
         * die neuen Kurse finden nichts mehr, woran sie haengenbleiben.
         *
         * Nur die NETZE. Flaechen, Aufkleber, Lot und Traeger bleiben stehen,
         * bis der Neubau vollstaendig ist.
         */
        private void EntferneAlteNetzeVorDemNeubau()
        { foreach (int n in EntferneAlteNetzeSchritte()) { } }
        private IEnumerable<int> EntferneAlteNetzeSchritte()
        {
            if (!IsEditing) yield break;
            foreach (int n in PlaneNetzerhaltSchritte()) yield return n;
            yield return 0;
            /*
             * ZUERST ERFASSEN, DANN LOESCHEN.
             *
             * Was der Nutzer selbst an unsere Strassen gelegt hat - Strom
             * unterirdisch, Wasser und Abwasser als Doppelrohr - haengt an
             * genau den Kanten, die gleich verschwinden. Danach ist nicht mehr
             * feststellbar, was dort hing.
             *
             * Sein Befund am 2026-09-05: *"Dann sind die ehemaligen
             * Verbindungen unterbrochen weil neue Strasse."*
             *
             * Diese Zeile repariert noch nichts. Sie schreibt auf, was
             * verlorengeht - und das ist die Voraussetzung fuer jede Abhilfe.
             * Ohne die Liste waere sie geraten.
             */
            ErfasseVersorgungsanschluesse(_editLot);
            yield return 0;
            if (_bauarbeiter) foreach (int n in ErfasseHintergrundhoehenSchritte()) yield return n;
            else ErfasseEdithoehenVorAbriss();
            yield return 0;
            /*
             * UEBER DEN BESITZER, NICHT UEBER DIE TEILRELATION.
             *
             * Der erste Anlauf am 2026-08-31 durchsuchte
             * `ParkingLotPartRelation` - und fand NICHTS: das Log meldete
             * "0 alte Wegteile ... entfernt", waehrend im Spiel weiterhin
             * Fahrgassen fehlten. Die Relation tragen die Aufkleber und
             * Ladesaeulen; die WEGE bekommen in `AttachPartsToLotOwner` einen
             * `Owner` und landen im SubNet der Lot-Flaeche. Zwei verschiedene
             * Zugehoerigkeiten, und ich hatte die falsche genommen.
             *
             * Ein Zaehler, der stumm 0 meldet, sieht aus wie "nichts zu tun".
             * Deshalb steht die Zahl jetzt in derselben Zeile neben der Zahl
             * der ueberhaupt betrachteten Teile.
             */
            Entity[] teile;
            if (_bauarbeiter) teile = ParkingLotNetzRueckweg.Besitzteile(EntityManager,_editLot);
            else using (var a = _editOwnerParts.ToEntityArray(Allocator.Temp)) teile = a.ToArray();
            foreach (int n in ParkingLotNetzRueckweg.SichereSchritte(EntityManager, _editLot, teile, _erhalteneNetzteile)) yield return n;
            var entfernt = 0;
            var besessen = 0;
            var fremdeKnoten = new HashSet<Entity>();
            /*
             * KNOTEN ERST NACH ALLEN KANTEN, IN EINEM BILD (2026-10-03).
             *
             * Der Hintergrund-Neubau verteilt diese Schleife ueber mehrere
             * Bilder. Ein in Bild 1 geloeschter Knoten wird am Bildende
             * zerstoert; haengt an ihm eine erst in Bild 2 geloeschte Kante,
             * zeigt sie ein Bild lang auf eine tote Entity -> nativer Absturz
             * (Sync-Absturz nach "Schnappschuss Lot 447078"). Kanten duerfen
             * portionsweise fallen, das ist Vanilla-normal; Knoten fallen
             * gesammelt danach, ohne Unterbrechung.
             */
            var spaeteKnoten = new List<Entity>();
            for (var i = 0; i < teile.Length; i++)
            {
                yield return 0;
                var teil = teile[i];
                if (!EntityManager.Exists(teil) || !EntityManager.HasComponent<Owner>(teil)
                    || !ParkingLotBesitz.GehoertZu(EntityManager,EntityManager.GetComponentData<Owner>(teil).m_Owner,_editLot)) continue;
                besessen++;
                if (!EntityManager.HasComponent<Game.Net.Edge>(teil)
                    && !EntityManager.HasComponent<Game.Net.Node>(teil))
                    continue;
                if (EntityManager.HasComponent<Deleted>(teil)) continue;

                if (_erhalteneNetzteile.Contains(teil))
                {
                    // Die Hoehenkopie leert den alten Hoehenspeicher. Erst
                    // danach auch erhaltene Kursenden als Quelle aufnehmen:
                    // 2026-09-26 lagen Gassenkurve und Wegknoten 0,23 m auseinander.
                    if (EntityManager.HasComponent<Game.Net.Edge>(teil))
                    {
                        var k = EntityManager.GetComponentData<Game.Net.Edge>(teil);
                        MerkeAltknotenhoehe(k.m_Start); MerkeAltknotenhoehe(k.m_End);
                        MerkeAltkante(teil); MerkeAltgassenende(teil);
                    }
                    continue;
                }

                // Die Knoten der Kante MERKEN, bevor sie verschwindet -
                // danach ist nicht mehr zu sehen, wo sie angesetzt hat.
                if (EntityManager.HasComponent<Game.Net.Edge>(teil))
                {
                    var kante = EntityManager
                        .GetComponentData<Game.Net.Edge>(teil);
                    fremdeKnoten.Add(kante.m_Start);
                    fremdeKnoten.Add(kante.m_End);
                    MerkeAltknotenhoehe(kante.m_Start);
                    MerkeAltknotenhoehe(kante.m_End);
                    // Und ihre Kurve: unter ihr ist das Gelaende von ihr
                    // selbst geformt (ParkingLotEditHeight.Altkanten.cs).
                    MerkeAltkante(teil);
                    MerkeAltgassenende(teil);
                }
                else
                {
                    MerkeAltknotenhoehe(teil);
                    spaeteKnoten.Add(teil);
                    continue;
                }

                EntityManager.AddComponent<Deleted>(teil);
                entfernt++;
            }
            foreach (var knoten in spaeteKnoten)
            {
                if (!EntityManager.Exists(knoten) || EntityManager.HasComponent<Deleted>(knoten)) continue;
                EntityManager.AddComponent<Deleted>(knoten);
                entfernt++;
            }

            FrischeTeilungsknotenAuf(fremdeKnoten);

            _editNetRemovalTick = System.Diagnostics.Stopwatch.GetTimestamp();
            MerkeAbrissFuerGelaende();
            MeldeAbriss(entfernt, besessen, teile.Length);
        }

        /**
         * Sagt den Knoten an der Stadtstrasse, dass sie keine Kreuzung mehr
         * sind.
         *
         * Betroffen ist nur, was uns NICHT gehoert und die Loeschung
         * ueberlebt: der Knoten, an dem unsere Gasse die Stadtstrasse
         * geteilt hat. Unsere eigenen Knoten sind zu diesem Zeitpunkt
         * bereits `Deleted` und fallen hier durch.
         *
         * Angefasst wird nichts strukturell - nur `Updated`, damit CS2 die
         * Komposition neu waehlt.
         */
        private void FrischeTeilungsknotenAuf(HashSet<Entity> knoten)
        {
            var betrachtet = 0;
            var aufgefrischt = 0;
            var zeilen = new List<string>();

            foreach (var k in knoten)
            {
                if (k == Entity.Null || !EntityManager.Exists(k)) continue;
                if (EntityManager.HasComponent<Deleted>(k)) continue;
                if (!EntityManager.HasBuffer<Game.Net.ConnectedEdge>(k)) continue;
                betrachtet++;

                /*
                 * DIE KANTEN ZAEHLEN, DIE UEBRIG BLEIBEN.
                 *
                 * `Deleted` steht zu diesem Zeitpunkt schon an unseren
                 * Kanten, sie zaehlen also korrekt nicht mit. Bleiben zwei,
                 * ist der Knoten ein Durchgang und keine Kreuzung mehr -
                 * genau der Fall, den der Nutzer sieht.
                 */
                /*
                 * ERST ABSCHREIBEN, DANN ANFASSEN.
                 *
                 * `AddComponent` ist eine strukturelle Aenderung: sie kann
                 * die Entity in einen anderen Chunk verschieben und macht
                 * damit jeden vorher geholten `DynamicBuffer` ungueltig.
                 * Den Puffer danach weiterzulesen liest fremden Speicher.
                 */
                var uebrig = new List<Entity>();
                var puffer = EntityManager
                    .GetBuffer<Game.Net.ConnectedEdge>(k, true);
                for (var i = 0; i < puffer.Length; i++)
                {
                    var kante = puffer[i].m_Edge;
                    if (kante == Entity.Null || !EntityManager.Exists(kante))
                        continue;
                    if (EntityManager.HasComponent<Deleted>(kante)) continue;
                    if (EntityManager.HasComponent<Temp>(kante)) continue;
                    uebrig.Add(kante);
                }

                zeilen.Add("Knoten " + k.Index + ": " + uebrig.Count
                    + " Kante(n)");
                if (uebrig.Count == 0) continue;

                if (!EntityManager.HasComponent<Updated>(k))
                    EntityManager.AddComponent<Updated>(k);
                foreach (var kante in uebrig)
                    if (!EntityManager.HasComponent<Updated>(kante))
                        EntityManager.AddComponent<Updated>(kante);
                aufgefrischt++;
            }

            if (betrachtet == 0) return;
            Mod.log.Info("PLT-Teilungsknoten: " + betrachtet
                + " fremde(r) Knoten an den geloeschten Kanten, "
                + aufgefrischt + " aufgefrischt. " + string.Join(" | ", zeilen)
                + ". Zwei Kanten heisst Durchgang - steht dort trotzdem noch "
                + "eine Kreuzung, liegt es nicht an der Komposition.");
        }

        private void MeldeAbriss(int entfernt, int besessen, int gesamt)
        {
            Mod.log.Info("PLT-Bearbeiten: " + entfernt + " alte Wegteile von "
                + "Lot " + _editLot.Index + " vor dem Neubau entfernt (von "
                + besessen + " Teilen dieses Lots, " + gesamt
                + " mit Besitzer insgesamt). Ohne das wuerden die neuen Kurse "
                + "auf den alten Kanten liegen und mit ihnen verschwinden.");
        }

        /**
         * Nach dem Uebernehmen: wer noch da ist, wird wieder sichtbar.
         *
         * Laeuft auch dann, wenn gerade nicht bearbeitet wird - der Abriss
         * des alten Lots zieht sich ueber mehrere Durchgaenge, das Ende der
         * Bearbeitung ist also nicht das Ende des Aufraeumens.
         *
         * Grenze, die ich offen benenne: der Aufruf haengt am ToolUpdate,
         * laeuft also nur, solange das PLT-Werkzeug aktiv ist. Uebernommen
         * wird IM Werkzeug, und der Abriss braucht nur wenige Durchgaenge -
         * der Normalfall ist damit gedeckt. Verlaesst jemand das Werkzeug
         * genau in diesen Frames, wird der Ueberlebende beim naechsten
         * Oeffnen sichtbar gemacht, nicht frueher.
         */
        private void PruefeUeberlebendeAusgeblendete()
        {
            if (_hiddenNachUebernahme.Count == 0) return;

            _hiddenPruefliste.Clear();
            foreach (var teil in _hiddenNachUebernahme)
                if (!EntityManager.Exists(teil)
                    || EntityManager.HasComponent<Deleted>(teil))
                    _hiddenPruefliste.Add(teil);
            for (var i = 0; i < _hiddenPruefliste.Count; i++)
                _hiddenNachUebernahme.Remove(_hiddenPruefliste[i]);
            if (_hiddenNachUebernahme.Count == 0) return;

            /*
             * Was noch existiert und NICHT mehr zum Abriss vorgemerkt ist,
             * hat den Abriss ueberlebt. Es gehoert keinem alten Lot mehr an,
             * also darf es auch nicht unsichtbar bleiben.
             */
            _hiddenPruefliste.Clear();
            foreach (var teil in _hiddenNachUebernahme)
            {
                if (!EntityManager.HasComponent<Hidden>(teil))
                {
                    _hiddenPruefliste.Add(teil);
                    continue;
                }
                if (EntityManager.HasComponent<Temp>(teil)) continue;
                EntityManager.RemoveComponent<Hidden>(teil);
                if (!EntityManager.HasComponent<BatchesUpdated>(teil))
                    EntityManager.AddComponent<BatchesUpdated>(teil);
                _hiddenPruefliste.Add(teil);
                Mod.log.Info("PLT-Bearbeiten: Teil " + teil.Index
                    + " hat den Abriss des alten Lots ueberlebt und war noch "
                    + "ausgeblendet - wieder sichtbar gemacht.");
            }
            for (var i = 0; i < _hiddenPruefliste.Count; i++)
                _hiddenNachUebernahme.Remove(_hiddenPruefliste[i]);
        }

    }
}
