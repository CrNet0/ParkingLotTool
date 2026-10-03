using Game;
using Game.SceneFlow;
using Game.Tools;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    /**
     * Schreibt alle 10 s ein Lebenszeichen (siehe ParkingLotSchrittmarke) und
     * treibt den Burst-Messlauf an, falls er angefordert ist.
     * Laeuft in MainLoop, also auch im Hauptmenue.
     */
    public sealed partial class ParkingLotHerzschlagSystem : GameSystemBase
    {
        private ToolSystem _toolSystem;


        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
        }

        [Preserve]
        protected override void OnUpdate()
        {
            var spiel = GameManager.instance;
            ParkingLotBurstjobs.Takt(spiel != null && !spiel.isGameLoading && spiel.gameMode.IsGame());
            ParkingLotSchrittmarke.Herzschlag(Zustand);
        }

        private string Zustand()
        {
            var spiel = GameManager.instance;
            return "Mode: " + (spiel == null ? "?" : spiel.gameMode.ToString())
                + (spiel != null && spiel.isGameLoading ? " (loading)" : "")
                + " | Tool: " + (_toolSystem?.activeTool?.toolID ?? "-")
                + " | Frame: " + UnityEngine.Time.frameCount;
        }
    }
}
