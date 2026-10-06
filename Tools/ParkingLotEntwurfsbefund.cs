using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace ParkingLotTool.Tools
{
    /**
     * WAS HAELT EINEN TAUSCH AUF? (1.0.6, Nutzer 2026-10-06: "wie sicher
     * koennen wir sein, dass es jetzt funktioniert und nicht wieder zufaellig
     * klappt?")
     *
     * Die Tauschwerkzeuge starten nur in einem Bild ohne fremde Definitionen
     * und Temps. Am 2026-10-06 19:06 brachen alle fuenf Fahrwegtausche nach
     * 600 Bildern ab, und das Log sagte nur "fremde Entwuerfe". Wartet ein
     * Tausch laenger als eine Sekunde, schreibt diese Zeile einmal, WAS da
     * liegt: Definitionen mit und ohne `Updated`, mit unserer Auftragsmarke,
     * Temps nach Prefab und Flags. Ein zweiter Fehlschlag ist damit sofort
     * erklaert statt erraten.
     */
    internal static class ParkingLotEntwurfsbefund
    {
        internal static string Beschreibe(EntityManager em, PrefabSystem prefabs)
        {
            using var defs = em.CreateEntityQuery(ComponentType.ReadOnly<CreationDefinition>())
                .ToEntityArray(Allocator.Temp);
            int mitUpdated = 0, auftrag = 0;
            var defBeispiele = new List<string>();
            foreach (var d in defs)
            {
                if (em.HasComponent<Updated>(d)) mitUpdated++;
                if (em.HasComponent<ParkingLotAuftragsdefinition>(d)) auftrag++;
                if (defBeispiele.Count >= 5) continue;
                var c = em.GetComponentData<CreationDefinition>(d);
                defBeispiele.Add(Name(em, prefabs, c.m_Prefab) + (c.m_Original != Entity.Null ? " (Original " + c.m_Original.Index + ")" : "")
                    + " [" + c.m_Flags + "]");
            }
            using var temps = em.CreateEntityQuery(ComponentType.ReadOnly<Temp>()).ToEntityArray(Allocator.Temp);
            var tempGruppen = new Dictionary<string, int>();
            foreach (var t in temps)
            {
                var name = em.HasComponent<PrefabRef>(t) ? Name(em, prefabs, em.GetComponentData<PrefabRef>(t).m_Prefab) : "ohne PrefabRef";
                var schluessel = name + " [" + em.GetComponentData<Temp>(t).m_Flags + "]";
                tempGruppen[schluessel] = tempGruppen.TryGetValue(schluessel, out var n) ? n + 1 : 1;
            }
            return $"{defs.Length} Definition(en) ({mitUpdated} mit Updated, {auftrag} mit PLT-Auftragsmarke)"
                + (defBeispiele.Count > 0 ? ": " + string.Join("; ", defBeispiele) : "")
                + $" | {temps.Length} Temp(s)"
                + (tempGruppen.Count > 0 ? ": " + string.Join("; ", tempGruppen.OrderByDescending(g => g.Value).Take(6)
                    .Select(g => g.Value + "x " + g.Key)) : "");
        }

        private static string Name(EntityManager em, PrefabSystem prefabs, Entity prefab)
            => prefab == Entity.Null ? "kein Prefab" : prefabs.GetPrefabName(prefab);
    }
}
