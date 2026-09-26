using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using ParkingLotTool.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingLotTool.Tools
{
    public sealed partial class ParkingLotToolSystem
    {
        private sealed class VorplanEingabe
        {
            internal readonly Versorgungseingabe Plan = new Versorgungseingabe();
            internal readonly Dictionary<int, Entity> Entitaeten = new Dictionary<int, Entity>();
            internal readonly List<(Versorgungskante Kante, float2 Rand, float2 Innen, bool Hinaus)> Gassen =
                new List<(Versorgungskante, float2, float2, bool)>();
            internal readonly List<(Bezier4x3 Bogen, float Breite)> GassenStrassen =
                new List<(Bezier4x3, float)>();
            internal double SchnappschussMs;
            internal int Revision;
        }

        private sealed class VorplanErgebnis
        {
            internal readonly List<Versorgungsvorplan> Trassen = new List<Versorgungsvorplan>();
            internal int Revision;
            internal Entity Zielkante;
            internal float2 Start, Ziel;
            internal float Laenge;
            internal string Grund;
            internal double SchnappschussMs, RechnungMs;
            internal int Starts, Ziele, Huellen;
            internal int EigeneKanten, AlleZiele, Hinderniskanten, Fremdleitungen;
            internal bool ZielEigene;
            internal bool Gefunden => Trassen.Count > 0;
        }

        private Task<VorplanErgebnis> _avVorplanTask;
        private CancellationTokenSource _avVorplanAbbruch;
        private VorplanErgebnis _avVorplan;
        private VorplanErgebnis _avVorplanBeimBau;
        private bool _avVorplanGemeldet;
        private int _avVorplanIndex;
        private string _avVorplanRueckfallGrund;
        private int _avVorplanRevision = -1;
        private int _avVorplanUiRevision = -1;
        private long _avVorplanRuhigSeit;
        private const double VorplanRuheMs = 750;

        private static double VorplanMillis(long ticks)
            => ticks * 1000d / Stopwatch.Frequency;

        private void PflegeVorplanung()
        {
            // Waehrend der Materialisierung gehoert der fertige Vorplan noch
            // zum bestaetigten Stand. Erst am Apply wird er uebernommen.
            if (_buildStage != BuildStage.Idle) return;
            var uiRevision = _uiSystem?.Revision ?? 0;
            if (!_closed || _layoutDirty || _buildTask != null || _areaPreviewLayout == null
                || _dragPoint >= 0 || _dragEntrance >= 0
                || Mod.Optionen?.AutomatischVersorgung != true)
            {
                VerwerfeVorplanung();
                return;
            }
            if (_avVorplanRevision != _geometryRevision || _avVorplanUiRevision != uiRevision)
            {
                VerwerfeVorplanung();
                _avVorplanRevision = _geometryRevision;
                _avVorplanUiRevision = uiRevision;
                _avVorplanRuhigSeit = Stopwatch.GetTimestamp();
                return;
            }
            if (_avVorplanTask != null && _avVorplanTask.IsCompleted)
            {
                try
                {
                    var r = _avVorplanTask.GetAwaiter().GetResult();
                    if (r != null && r.Revision == _geometryRevision
                        && !_avVorplanAbbruch.IsCancellationRequested)
                        _avVorplan = r;
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { Mod.log.Error(e, "PLT-Autoversorgung Vorplanung fehlgeschlagen."); }
                _avVorplanTask = null;
            }
            if (_avVorplanTask != null || _avVorplan != null
                || VorplanMillis(Stopwatch.GetTimestamp() - _avVorplanRuhigSeit) < VorplanRuheMs) return;

            var eingabe = LeseVorplanSchnappschuss(_areaPreviewLayout, _areaPreviewSettings);
            if (eingabe == null)
            {
                _avVorplanRuhigSeit = Stopwatch.GetTimestamp();
                return;
            }
            _avVorplanAbbruch = new CancellationTokenSource();
            var token = _avVorplanAbbruch.Token;
            _avVorplanTask = Task.Run(() =>
            {
                var faden = Thread.CurrentThread;
                var vorher = faden.Priority;
                /*
                 * DER ABBRUCH ENDET HIER, NICHT ALS AUSNAHME.
                 *
                 * `VerwerfeVorplanung` bricht ab und vergisst den Task. Lief
                 * die Ausnahme hinaus, hat sie niemand mehr abgeholt; der
                 * Finalizer meldete sie dann als "Unobserved exception"
                 * (CRITICAL im Spiel, 2026-09-27 beim Ziehen einer Zone).
                 * Jeder Fehler wird deshalb im Task selbst beendet.
                 */
                try
                {
                    faden.Priority = ThreadPriority.BelowNormal;
                    return BerechneVorplan(eingabe, token);
                }
                catch (OperationCanceledException) { return null; }
                catch (Exception fehler)
                {
                    Mod.log.Error(fehler, "PLT-Autoversorgung Vorplanung fehlgeschlagen.");
                    return null;
                }
                finally { faden.Priority = vorher; }
            });
        }

        private void VerwerfeVorplanung()
        {
            _avVorplanAbbruch?.Cancel();
            _avVorplanTask = null;
            _avVorplan = null;
            _avVorplanRevision = -1;
        }

        private void MerkeVorplanungBeimBau()
        {
            if (_avVorplanTask != null && _avVorplanTask.IsCompleted)
            {
                try { _avVorplan = _avVorplanTask.GetAwaiter().GetResult(); }
                catch (Exception) { _avVorplan = null; }
            }
            _avVorplanBeimBau = _avVorplan != null && _avVorplan.Revision == _geometryRevision
                && _avVorplanUiRevision == (_uiSystem?.Revision ?? 0) ? _avVorplan : null;
            _avVorplanIndex = 0;
            _avVorplanRueckfallGrund = null;
            VerwerfeVorplanung();
        }

        private void VerwerfeRestvorplan(string grund)
        {
            _avVorplanBeimBau = null;
            _avVorplanRueckfallGrund = grund;
        }

        private void MeldeVorplanung(Versorgungstrasse ist)
        {
            if (_avVorplanGemeldet) return;
            _avVorplanGemeldet = true;
            var vor = _avVorplanBeimBau;
            if (vor == null)
            {
                Mod.log.Info("PLT-Autoversorgung VORPLAN: fehlte oder veraltet; Schnappschuss - ms; Istplanung "
                    + (ist.Gefunden ? $"{ist.Laenge:F3} m, Zielkante {ist.Zielkante}." : "ohne Trasse."));
                return;
            }
            var gleich = vor.Gefunden == ist.Gefunden && (!vor.Gefunden
                || ((vor.ZielEigene ? EntityManager.HasComponent<Owner>(ist.Zielkante)
                    : vor.Zielkante == ist.Zielkante)
                    && math.distance(vor.Start, ist.Start.xz) <= 0.1f
                    && math.distance(vor.Ziel, ist.Ziel.xz) <= 0.1f
                    && math.abs(vor.Laenge - ist.Laenge) <= 0.1f));
            var istAus = _avLetzteIstEingabe;
            var istGruppe = istAus?.Beste?.Gruppe;
            if (istGruppe == null && istAus != null)
                foreach (var gruppe in istAus.Gruppen)
                    if (!gruppe.AnStadt && gruppe.Starts.Count > 0)
                    { istGruppe = gruppe; break; }
            Mod.log.Info($"PLT-Autoversorgung VORPLAN: {(gleich ? "gleich" : "abweichend")}; "
                + $"Start ({vor.Start.x:F2}/{vor.Start.y:F2}) / ({ist.Start.x:F2}/{ist.Start.z:F2}), "
                + $"Ziel ({vor.Ziel.x:F2}/{vor.Ziel.y:F2}) / ({ist.Ziel.x:F2}/{ist.Ziel.z:F2}), "
                + $"Laenge {vor.Laenge:F3}/{ist.Laenge:F3} m, Zielkante {vor.Zielkante}/{ist.Zielkante}; "
                + $"Schnappschuss {vor.SchnappschussMs:F2} ms Hauptfaden, Rechnung {vor.RechnungMs:F1} ms Hintergrund; "
                + $"{vor.Grund}."
                + (gleich ? "" : $" Eingabe eigene/Ziele/Hindernisse/Leitungen "
                    + $"{vor.EigeneKanten}/{vor.AlleZiele}/{vor.Hinderniskanten}/{vor.Fremdleitungen} "
                    + $"vorher, {istAus?.EigeneKanten ?? 0}/{istAus?.AlleZiele ?? 0}/"
                    + $"{istAus?.Hinderniskanten ?? 0}/{istAus?.Fremdleitungen ?? 0} nachher; "
                    + $"Starts/erlaubte Ziele/Huellen {vor.Starts}/{vor.Ziele}/{vor.Huellen} "
                    + $"vorher, {istGruppe?.Starts.Count ?? 0}/{istGruppe?.Ziele.Count ?? 0}/"
                    + $"{istGruppe?.Huellen ?? 0} nachher."));
        }

    }
}
