using ParkingLotTool.Geometry;

namespace ParkingLotTool.Tools
{
    /**
     * Die Laternenauswahl des Spielers. Das Fenster dazu (wie Vegetation, an
     * derselben Stelle) kommt im UI-Schritt; bis dahin gilt der Standard:
     * an, 30 m, Einzel- und Doppelstrassenlaterne 02.
     */
    public sealed partial class ParkingLotUISystem
    {
        internal LaternenOptionen Laternen { get; private set; } = new LaternenOptionen();
    }
}
