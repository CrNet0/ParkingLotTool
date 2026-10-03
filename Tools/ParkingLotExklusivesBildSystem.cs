using System;
using Game;
using Game.SceneFlow;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    internal enum ParkingLotDefinitionsmodus { Temp, Permanent }

    /** Einmaliger Erzeugungspunkt fuer ein exklusives Bild.
     *  Vorgesehene Anmeldung: UpdateAfter<..., ToolOutputBarrier>(ToolUpdate).
     *  ToolOutputBarrier ist dann abgespielt; CourseSplit folgt in PostTool,
     *  GenerateNodes/Objects/Areas in Modification1, Edges in Modification2.
     *  Angemeldet hinter dem Auftragsupdate. Runde 4 benutzt getrennte
     *  exklusive Bilder fuer Besitzer, Kinder, Rueckweg und Nebenarbeit.
     *  Globale Queries sind ausschliesslich lesende E1-Zaehler. */
    [DisableAutoCreation]
    internal sealed partial class ParkingLotExklusivesBildSystem : GameSystemBase
    {
        private EntityQuery _definitionen;
        private EntityQuery _temps;
        private Entity _lot;
        private Func<ParkingLotDefinitionsmodus, int> _anlegen;
        private int _wartendeBilder;
        private int _letztesWartebild = -1;
        private int _letzteDefinitionen = -1, _letzteTemps = -1;
        private bool _letzteBereitschaft;
        internal string Wartehinweis { get; private set; } = string.Empty;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            // Kein Updated-/Deleted-Filter: E1 fordert die ganzen Mengen.
            var alle = EntityQueryOptions.IncludeDisabledEntities
                | EntityQueryOptions.IncludePrefab;
            _definitionen = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<CreationDefinition>() },
                Options = alle,
            });
            _temps = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Temp>() },
                Options = alle,
            });
        }

        /** Der Aufruf muss synchron ALLE Definitionen anlegen. Kein eigener
         *  CommandBuffer darf sie hinter die Vanilla-Phasen verschieben.
         *  Nur ein Erzeuger: die spaetere Auftragsmaschine bleibt die Queue. */
        internal void Erwarte(Entity lot, Func<ParkingLotDefinitionsmodus, int> anlegen)
        {
            if (_anlegen != null) throw new InvalidOperationException(
                "Der exklusive Erzeugungspunkt ist bereits belegt.");
            if (lot == Entity.Null) throw new ArgumentException("Lot fehlt.", nameof(lot));
            _anlegen = anlegen ?? throw new ArgumentNullException(nameof(anlegen));
            _lot = lot;
            _wartendeBilder = 0;
            _letztesWartebild = -1;
            _letzteDefinitionen = _letzteTemps = -1;
            Wartehinweis = string.Empty;
        }

        internal void Verwerfe(Entity lot)
        {
            if (_lot != lot) return;
            _anlegen = null;
            _lot = Entity.Null;
            Wartehinweis = string.Empty;
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (_anlegen == null) return;
            var spiel = GameManager.instance;
            var bereit = spiel != null && !spiel.isGameLoading && spiel.gameMode.IsGame();
            // CalculateEntityCount synchronisiert die Query-Abhaengigkeiten.
            // Ausstehende Tool-Schreiber sind am vorgesehenen Wiedergabepunkt
            // bereits in der Welt; EndFrame-Schreiber laufen vor ToolSystem.
            var definitionen = _definitionen.CalculateEntityCount();
            var temps = _temps.CalculateEntityCount();
            if (!ExklusivesBaubild.DarfAnlegen(definitionen, temps, bereit))
            {
                var werkzeug = World.GetExistingSystemManaged<ParkingLotToolSystem>();
                bool pltZeichnet = World.GetExistingSystemManaged<ToolSystem>()?.activeTool == werkzeug
                    && (definitionen > 0 || temps > 0);
                Wartehinweis = pltZeichnet ? ParkingLotTexte.T(
                    "Synchronisation wartet: Parking-Lot-Panel/Werkzeug schliessen.",
                    "Synchronization is waiting: close the Parking Lot panel/tool.")
                    : ParkingLotTexte.T("Synchronisation wartet auf einen freien Bauzyklus. Aktuellen Werkzeugentwurf beenden.",
                        "Synchronization is waiting for a free build cycle. Finish the current tool draft.");
                var bild = UnityEngine.Time.frameCount;
                var neuesBild = bild != _letztesWartebild;
                if (neuesBild) _wartendeBilder++;
                _letztesWartebild = bild;
                if (definitionen != _letzteDefinitionen || temps != _letzteTemps
                    || bereit != _letzteBereitschaft || (neuesBild && _wartendeBilder % 120 == 0))
                    Melde($"wartet Lot {_lot.Index}: "
                        + ExklusivesBaubild.Wartegrund(definitionen, temps, bereit)
                        + "; " + Wartehinweis
                        + $"; {_wartendeBilder} wartende Bilder.");
                _letzteDefinitionen = definitionen;
                _letzteTemps = temps;
                _letzteBereitschaft = bereit;
                return;
            }
            var anlegen = _anlegen;
            var lot = _lot;
            // Vor dem Callback austragen: ein Fehler darf keinen zweiten,
            // ueberlappenden Neubau beim naechsten Update erzeugen.
            Verwerfe(lot);
            var gemeldet = anlegen(ParkingLotDefinitionsmodus.Permanent);
            var anzahl = _definitionen.CalculateEntityCount();
            if (gemeldet != anzahl) Melde($"Definitionszaehler: Erzeuger {gemeldet}, ECS {anzahl}; ECS ist massgeblich.");
            Melde($"exklusives Bild Lot {lot.Index}: fremde Definitionen 0, "
                + $"Temps 0; {anzahl} Definitionen angelegt; "
                + $"{_wartendeBilder} Bilder gewartet.");
        }

        protected override void OnGameLoaded(Colossal.Serialization.Entities.Context context)
        {
            base.OnGameLoaded(context);
            // Delegates enthalten alte Entity-IDs und werden nie geladen.
            Verwerfe(_lot);
        }

        private static void Melde(string text)
        {
            var zeile = "PLT-Hintergrund: " + text;
            Mod.log.Info(zeile);
            ParkingGeometry.Live(zeile);
        }
    }
}
