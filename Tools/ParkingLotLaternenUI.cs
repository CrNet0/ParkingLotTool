using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.UI.Binding;
using Game.Prefabs;
using Game.SceneFlow;
using Newtonsoft.Json;
using ParkingLotTool.Geometry;

namespace ParkingLotTool.Tools
{
    internal sealed class LaternenModellEintrag
    {
        public string Id, Name, Icon, Bauart;
    }

    internal sealed class LaternenSetEintrag
    {
        public string Id, Name, Einzeln, Doppelt;
        public bool Custom;
    }

    /**
     * DAS LATERNENFENSTER (Nutzer 2026-10-04).
     *
     * Wie Vegetation, an derselben Stelle; das eine schliesst das andere.
     * Schalter und Knopf stehen unter "Roads". Kein Alter, kein Muster:
     * drei feste Sets (Strasse, Gewerbe, Industrie) und eigene Sets aus genau
     * einem Modell fuer "Einzeln" und einem fuer "Doppelt"; alle Modelle sind
     * in beiden Feldern waehlbar. Dazu der Abstand 20-40 m.
     *
     * \`Set\` ist abgeleitet, nie nachgefuehrt: passt die Wahl zu einem Set,
     * heisst sie so, sonst ist sie leer ("eigene Wahl").
     */
    public sealed partial class ParkingLotUISystem
    {
        private ValueBinding<string> _laternen, _laternenDefault, _laternenKatalog;
        private ValueBinding<bool> _laternenLichtkreise;

        /**
         * Lichtkreise in der Vorschau (Nutzer 2026-10-04: Schalter, Standard aus).
         * Eine Anzeigeeinstellung, kein Teil der Laternenwahl: steht nicht im
         * Bauzettel, sondern in den Einstellungen des Spielers.
         */
        internal bool LaternenLichtkreise => _laternenLichtkreise?.value ?? false;
        private readonly List<LaternenSetEintrag> _laternenSets = new List<LaternenSetEintrag>();
        private string LaternenSetsPath => Path.Combine(Path.GetDirectoryName(SettingsPath()), "laternen-sets.json");

        internal LaternenOptionen Laternen => Lies(_laternen?.value) ?? new LaternenOptionen();
        internal string LaternenJson => _laternen?.value ?? string.Empty;
        /** Der gespeicherte Standard des Spielers - auch fuer Sync-Schritt 11 und Hintergrundbauten alter Parkplaetze. */
        internal LaternenOptionen LaternenStandardOptionen => Lies(LaternenStandard()) ?? new LaternenOptionen();

        private static LaternenOptionen Lies(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<LaternenOptionen>(json); }
            catch (Exception e) { Mod.log.Warn("Laternenwahl unlesbar: " + e.Message); return null; }
        }

        /** Beim Bearbeiten: die Wahl des Parkplatzes, ohne Zettel der Standard. */
        internal void RestoreLaternen(LaternenOptionen optionen)
            => _laternen.Update(JsonConvert.SerializeObject(Ordne(optionen ?? LaternenStandardOptionen)));

        private void InitLaternen()
        {
            _laternenSets.AddRange(FesteLaternenSets());
            try
            {
                if (File.Exists(LaternenSetsPath))
                    _laternenSets.AddRange((JsonConvert.DeserializeObject<List<LaternenSetEintrag>>(File.ReadAllText(LaternenSetsPath))
                        ?? new List<LaternenSetEintrag>()).Where(s => s != null && s.Custom && Gueltig(s.Einzeln) && Gueltig(s.Doppelt)));
            }
            catch (Exception e) { Mod.log.Warn("Laternensets konnten nicht geladen werden: " + e.Message); }
            AddBinding(_laternen = new ValueBinding<string>(Group, "Laternen", LaternenStandard()));
            AddBinding(_laternenDefault = new ValueBinding<string>(Group, "LaternenDefault", LaternenStandard()));
            AddBinding(_laternenKatalog = new ValueBinding<string>(Group, "LaternenKatalog", "{\"Modelle\":[],\"Sets\":[]}"));
            AddBinding(new TriggerBinding(Group, "RefreshLaternen", VeroeffentlicheLaternen));
            AddBinding(_laternenLichtkreise = new ValueBinding<bool>(Group, "LaternenLichtkreise", _defaults?.LaternenLichtkreise ?? false));
            AddBinding(new TriggerBinding<bool>(Group, "SetLaternenLichtkreise", an =>
            {
                if (!UpdateValue(_laternenLichtkreise, an)) return;
                var next = _defaults.Clone();
                next.LaternenLichtkreise = an;
                if (TryWriteDefaults(next)) _defaults = next;
                Tool()?.RefreshLaternenPreview();
            }));
            AddBinding(new TriggerBinding(Group, "SaveLaternenDefault", SpeichereLaternenStandard));
            AddBinding(new TriggerBinding(Group, "ResetLaternen", SetzeLaternenZurueck));
            AddBinding(new TriggerBinding<string>(Group, "SetLaternen", json =>
            {
                var o = Lies(json);
                if (o == null) return;
                SetzeLaternen(Ordne(o), () => ParkingLotTexte.T("laternenUI.lanternsChanged"));
            }));
            AddBinding(new TriggerBinding<string>(Group, "SaveLaternenSet", json =>
            {
                try
                {
                    var set = JsonConvert.DeserializeObject<LaternenSetEintrag>(json);
                    if (set == null || string.IsNullOrWhiteSpace(set.Name) || !Gueltig(set.Einzeln) || !Gueltig(set.Doppelt)) return;
                    set.Name = set.Name.Trim().Substring(0, Math.Min(64, set.Name.Trim().Length));
                    set.Id = "custom:" + Guid.NewGuid().ToString("N");
                    set.Custom = true;
                    _laternenSets.Add(set);
                    SpeichereLaternenSets();
                    VeroeffentlicheLaternen();
                    SetzeLaternen(Ordne(Laternen), null);
                }
                catch (Exception e) { Mod.log.Warn("Laternenset konnte nicht gespeichert werden: " + e.Message); }
            }));
            AddBinding(new TriggerBinding<string>(Group, "DeleteLaternenSet", id =>
            {
                _laternenSets.RemoveAll(s => s.Custom && s.Id == id);
                SpeichereLaternenSets();
                VeroeffentlicheLaternen();
                SetzeLaternen(Ordne(Laternen), null);
            }));
        }

        private static IEnumerable<LaternenSetEintrag> FesteLaternenSets()
            => LaternenKatalog.Sets.Select(s => new LaternenSetEintrag
            {
                Id = s.Id, Einzeln = s.Einzeln, Doppelt = s.Doppelt, Custom = false,
                Name = ParkingLotTexte.T("laternenSet." + s.Id),
            });

        /** Feste Sets nach einem Sprachwechsel neu beschriften; eigene behalten ihren Namen. */
        private void BenenneFesteLaternenSets()
        {
            foreach (var s in _laternenSets)
                if (!s.Custom) s.Name = ParkingLotTexte.T("laternenSet." + s.Id);
        }

        private static bool Gueltig(string modell) => LaternenKatalog.Modell(modell) != null;

        /** Werte in ihre Grenzen, unbekannte Modelle durch den Standard, \`Set\` aus der Wahl abgeleitet. */
        private LaternenOptionen Ordne(LaternenOptionen o)
        {
            var werk = new LaternenOptionen();
            o.Abstand = (float)Math.Round(Math.Max(ParkingLanterns.MinAbstand, Math.Min(ParkingLanterns.MaxAbstand, o.Abstand)));
            if (!Gueltig(o.Einzeln)) o.Einzeln = werk.Einzeln;
            if (!Gueltig(o.Doppelt)) o.Doppelt = werk.Doppelt;
            o.Set = _laternenSets.FirstOrDefault(s => s.Einzeln == o.Einzeln && s.Doppelt == o.Doppelt)?.Id ?? string.Empty;
            return o;
        }

        private void SetzeLaternen(LaternenOptionen o, Func<string> rueckgaengig)
        {
            var tool = Tool();
            var vorher = rueckgaengig != null ? tool?.CaptureUndoState() : null;
            if (!UpdateValue(_laternen, JsonConvert.SerializeObject(o))) return;
            if (rueckgaengig != null) tool?.CommitUndoState(vorher, rueckgaengig);
            tool?.RefreshLaternenPreview();
        }

        private string LaternenStandard()
        {
            var o = Lies(_defaults?.Laternen) ?? new LaternenOptionen();
            return JsonConvert.SerializeObject(Ordne(o));
        }

        private void SpeichereLaternenStandard()
        {
            var next = _defaults.Clone();
            next.Laternen = JsonConvert.SerializeObject(Laternen);
            if (!TryWriteDefaults(next)) return;
            _defaults = next;
            _laternenDefault.Update(LaternenStandard());
            SetStatus(ParkingLotTexte.T("laternenUI.lanternsSavedAsDefault"));
        }

        private void SetzeLaternenZurueck()
        {
            SetzeLaternen(LaternenStandardOptionen, () => ParkingLotTexte.T("laternenUI.lanternsReset"));
            SetStatus(ParkingLotTexte.T("laternenUI.lanternsResetToYourDefault"));
        }

        private void SpeichereLaternenSets()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LaternenSetsPath));
            var tmp = LaternenSetsPath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(_laternenSets.Where(s => s.Custom), Formatting.Indented));
            if (File.Exists(LaternenSetsPath)) File.Replace(tmp, LaternenSetsPath, LaternenSetsPath + ".bak");
            else File.Move(tmp, LaternenSetsPath);
        }

        /**
         * Modelle mit Namen aus den Sprachdateien und Bild: das UIObject-Symbol,
         * sonst das Vorschaubild des Prefabs (Asset Icon Library liefert es fuer
         * Assets ohne eigenes Symbol).
         */
        private void VeroeffentlicheLaternen()
        {
            var prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            var woerter = GameManager.instance?.localizationManager?.activeDictionary;
            var modelle = LaternenKatalog.Modelle.Select(m =>
            {
                var eintrag = new LaternenModellEintrag { Id = m.Prefab, Name = m.Prefab, Icon = string.Empty, Bauart = m.Bauart.ToString() };
                if (woerter != null && woerter.TryGetValue("Assets.NAME[" + m.Prefab + "]", out var name)) eintrag.Name = name;
                if (prefabs.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), m.Prefab), out var prefab) && prefab != null)
                    eintrag.Icon = prefab.TryGet<UIObject>(out var ui) && !string.IsNullOrEmpty(ui.m_Icon) ? ui.m_Icon : prefab.thumbnailUrl ?? string.Empty;
                return eintrag;
            }).ToArray();
            _laternenKatalog.Update(JsonConvert.SerializeObject(new { Modelle = modelle, Sets = _laternenSets }));
        }
    }
}
