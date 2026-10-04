using System.Collections.Generic;
using System.Linq;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    /**
     * LATERNEN IM WERKZEUG (Plan vom 2026-10-04).
     *
     * Ein Plan fuer alles: dieselbe Rechnung (Geometry/ParkingLanterns) fuettert
     * die Vorschau, die Freihaltung der Pflanzen und den Bau. Die Pflanzen
     * weichen den Laternen aus, deshalb entsteht der Laternenplan immer zuerst.
     *
     * Gesetzt werden die Laternen wie Pflanzen und Aufkleber: als
     * Objektdefinition in derselben Vorschau-Transaktion, danach haengt
     * `HefteObjekteAnTraeger` jedes aufgezeichnete Objekt mit Besitzer,
     * Attached und Teilrelation an den Traeger. Der Besitzer haelt das
     * Baumwerkzeug fern (Brushable greift nur ohne Owner) und schuetzt vor
     * unseren raeumenden Flaechen (gleiche Besitzerwurzel). Gedreht wird nur
     * um die Hochachse: keine Neigung.
     */
    public sealed partial class ParkingLotToolSystem
    {
        private static readonly LaternenOptionen StandardLaternen = new LaternenOptionen();

        /** Laternenpunkte des laufenden Baus - auch die Pflanzenplanung des Baus haelt sie frei. */
        private List<float2> _bauLaternenPunkte;
        private int _laternenCount;
        private readonly Dictionary<string, Entity> _laternenPrefabs = new Dictionary<string, Entity>();

        internal LaternenOptionen AktuelleLaternen => _uiSystem?.Laternen ?? StandardLaternen;

        internal static LaternenPlan LaternenPlanFuer(ParkingLayout layout, LaternenOptionen optionen)
            => optionen != null && optionen.Enabled && layout != null
                ? ParkingLanterns.Plan(layout, optionen.Abstand)
                : new LaternenPlan();

        internal static List<float2> Laternenpunkte(LaternenPlan plan)
            => plan.Laternen.Select(l => l.Position).ToList();

        /** Laternen und Pflanzen der Vorschau neu, ohne neue Layoutrechnung. */
        internal void RefreshLaternenPreview() => RefreshVegetationPreview();

        internal Entity LaternenPrefab(string name)
        {
            if (string.IsNullOrEmpty(name)) return Entity.Null;
            if (_laternenPrefabs.TryGetValue(name, out var e) && e != Entity.Null && EntityManager.Exists(e)) return e;
            e = _prefabSystem.TryGetPrefab(new PrefabID(nameof(StaticObjectPrefab), name), out var basis) && basis != null
                ? _prefabSystem.GetEntity(basis) : Entity.Null;
            _laternenPrefabs[name] = e;
            return e;
        }

        private IEnumerable<int> CreateLanternDefinitionsSchritte(ParkingLayout layout, TerrainHeightData heightData)
        {
            var optionen = AktuelleLaternen;
            var plan = LaternenPlanFuer(layout, optionen);
            _bauLaternenPunkte = Laternenpunkte(plan);
            _laternenCount = 0;
            if (plan.Laternen.Count == 0) yield break;
            var fehlend = new HashSet<string>();
            foreach (var platz in plan.Laternen)
            {
                yield return 0;
                var name = optionen.ModellFuer(platz);
                var prefab = LaternenPrefab(name);
                if (prefab == Entity.Null) { fehlend.Add(name); continue; }
                var bauart = LaternenKatalog.Modell(name)?.Bauart
                    ?? (platz.Doppelt ? LaternenBauart.Doppelt : LaternenBauart.Einseitig);
                var position = new float3(platz.Position.x, 0f, platz.Position.y);
                position.y = TerrainUtils.SampleHeight(ref heightData, position);
                if (!math.all(math.isfinite(position))) continue;
                var vorn = LaternenKatalog.Vorwaerts(platz, bauart);
                var definition = EntityManager.CreateEntity();
                EntityManager.AddComponentData(definition, new CreationDefinition
                {
                    m_Prefab = prefab,
                    // Aus der Lage: dieselbe Laterne bekommt bei jedem Bau
                    // denselben Wuerfel (Schaltschwelle in der Daemmerung).
                    m_RandomSeed = (int)math.hash(new int2((int)math.round(position.x * 16f), (int)math.round(position.z * 16f))),
                });
                EntityManager.AddComponent<Updated>(definition);
                EntityManager.AddComponentData(definition, new ObjectDefinition
                {
                    m_Position = position,
                    m_Rotation = quaternion.LookRotationSafe(new float3(vorn.x, 0f, vorn.y), math.up()),
                    m_Probability = 100, m_PrefabSubIndex = -1, m_Scale = new float3(1f), m_Intensity = 1f, m_ParentMesh = -1,
                });
                RecordObjectDefinition("Laterne", _laternenCount++, prefab, definition, position);
                yield return 1;
            }
            var arten = string.Join(", ", plan.Laternen.GroupBy(l => l.Art).Select(g => g.Key + " " + g.Count()));
            Mod.log.Info($"PLT-Laternen: {_laternenCount} von {plan.Laternen.Count} gesetzt ({arten}); Abstand "
                + $"{optionen.Abstand:F0} m, Einzeln {optionen.Einzeln}, Doppelt {optionen.Doppelt}"
                + (fehlend.Count > 0 ? "; NICHT GEFUNDEN: " + string.Join(", ", fehlend) : "") + ".");
        }
    }
}
