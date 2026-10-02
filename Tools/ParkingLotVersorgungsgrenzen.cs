using Game;
using UnityEngine.Scripting;

namespace ParkingLotTool.Tools
{
    // Messpunkte ohne Aenderungen an Vanilla-Systemen. Die erste vollstaendige
    // Datenprobe liegt vor Mod3 (0052 endete bisher im Intervall Mod3 bis Mod5).
    internal static class ParkingLotVersorgungsgrenzen
    {
        internal static void Registriere(UpdateSystem s)
        {
            s.UpdateBefore<ParkingLotVersorgungVor3System>(SystemUpdatePhase.Modification3);
            s.UpdateAfter<ParkingLotVersorgungNach3System>(SystemUpdatePhase.Modification3);
            s.UpdateAfter<ParkingLotVersorgungNach4System>(SystemUpdatePhase.Modification4);
            s.UpdateAfter<ParkingLotVersorgungNach4BSystem>(SystemUpdatePhase.Modification4B);
            s.UpdateAfter<ParkingLotVersorgungNach5System>(SystemUpdatePhase.Modification5);
        }
    }

    public sealed partial class ParkingLotVersorgungVor3System : GameSystemBase
    {
        [Preserve] protected override void OnUpdate()
            => World.GetExistingSystemManaged<ParkingLotVersorgungsdiagnoseSystem>()?.Phasenmarke("vor-Mod3", true);
    }
    public sealed partial class ParkingLotVersorgungNach3System : GameSystemBase
    {
        [Preserve] protected override void OnUpdate()
            => World.GetExistingSystemManaged<ParkingLotVersorgungsdiagnoseSystem>()?.Phasenmarke("nach-Mod3", true);
    }
    public sealed partial class ParkingLotVersorgungNach4System : GameSystemBase
    {
        [Preserve] protected override void OnUpdate()
            => World.GetExistingSystemManaged<ParkingLotVersorgungsdiagnoseSystem>()?.Phasenmarke("nach-Mod4", true);
    }
    public sealed partial class ParkingLotVersorgungNach4BSystem : GameSystemBase
    {
        [Preserve] protected override void OnUpdate()
            => World.GetExistingSystemManaged<ParkingLotVersorgungsdiagnoseSystem>()?.Phasenmarke("nach-Mod4B");
    }
    public sealed partial class ParkingLotVersorgungNach5System : GameSystemBase
    {
        [Preserve] protected override void OnUpdate()
            => World.GetExistingSystemManaged<ParkingLotVersorgungsdiagnoseSystem>()?.Phasenmarke("nach-Mod5");
    }
}
