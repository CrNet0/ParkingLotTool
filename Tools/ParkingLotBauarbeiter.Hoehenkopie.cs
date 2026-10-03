using System;
using System.Collections.Generic;
using Game.Simulation;
using Unity.Collections;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private bool HintergrundHoehenkarteOffen()
            => _terrainSystem.heightMapRenderRequired
                || HoehenkarteZurueckgelesen?.GetValue(_terrainSystem) is bool offen && offen;

        /** TerrainSystem 2596-2610 wartet bei true auf GPU und Leserjobs.
         *  Im Hintergrund stattdessen Bilder abwarten; je Einheit maximal
         *  65536 Zellen kopieren (128 KiB), vor JEDEM eigenen Abriss. */
        private IEnumerable<int> ErfasseHintergrundhoehenSchritte()
        {
            VerwerfeEdithoehen();
            for (int versuch = 1; versuch <= 3; versuch++)
            {
                int seit = UnityEngine.Time.frameCount, ruhig = 0;
                while (ruhig < 2)
                {
                    ruhig = HintergrundHoehenkarteOffen() ? 0 : ruhig + 1;
                    if (UnityEngine.Time.frameCount-seit > 120)
                        throw new InvalidOperationException("Hoehenkarte vor Abriss nach 120 Bildern noch offen; alte Wege bleiben erhalten.");
                    int bild = UnityEngine.Time.frameCount;
                    while (UnityEngine.Time.frameCount == bild) yield return 0;
                }
                var source = _terrainSystem.GetHeightData(waitForPending:false);
                _editHeightReadTick = System.Diagnostics.Stopwatch.GetTimestamp();
                _editHeightCells = new NativeArray<ushort>(source.heights.Length,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                if (source.downscaledHeights.IsCreated)
                    _editHeightCellsDownscaled = new NativeArray<ushort>(source.downscaledHeights.Length,Allocator.Persistent,NativeArrayOptions.UninitializedMemory);
                int kopiert = 0; bool geaendert = false;
                foreach (bool backdrop in new[] {false,true})
                {
                    var ziel = backdrop ? _editHeightCellsDownscaled : _editHeightCells;
                    if (!ziel.IsCreated) continue;
                    for (int i = 0; i < ziel.Length; i += 65536)
                    {
                        // Keine geliehene Terrain-Sicht ueber Bilder hinweg:
                        // Kartenwechsel oder eine neue Planierung bleibt sichtbar.
                        var aktuell = _terrainSystem.GetHeightData(waitForPending:false);
                        var von = backdrop ? aktuell.downscaledHeights : aktuell.heights;
                        if (HintergrundHoehenkarteOffen() || !von.IsCreated || von.Length != ziel.Length)
                        { geaendert = true; break; }
                        int n = Math.Min(65536,ziel.Length-i);
                        NativeArray<ushort>.Copy(von,i,ziel,i,n); kopiert += n;
                        yield return 0;
                    }
                    if (geaendert) break;
                }
                if (geaendert || HintergrundHoehenkarteOffen())
                {
                    VerwerfeEdithoehen();
                    ParkingLotNetzRueckweg.Melde($"Hoehenkopie {versuch}/3: neue Terrain-Aenderung; Kopie erneut vor Abriss abwarten, 0 alte Wege entfernt.");
                    continue;
                }
                _editHeightSnapshot = new TerrainHeightData(_editHeightCells,_editHeightCellsDownscaled,
                    source.resolution,source.scale,source.offset,source.hasBackdrop);
                ParkingLotNetzRueckweg.Melde($"Hoehenkarte vor Abriss: {kopiert} Zellen in 65536er-Portionen, {UnityEngine.Time.frameCount-seit} Bilder; 0 GPU-WaitForCompletion-Aufrufe.");
                yield break;
            }
            throw new InvalidOperationException("Hoehenkarte bei 3 Kopien veraendert; alte Wege bleiben erhalten.");
        }
    }
}
