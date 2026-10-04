import {
  editSelectedParkingLot, gewaehltenReparieren, meldeGewaehltenParkplatz,
  gewaehltenBearbeiten,
} from "./bindings";
import { TooltipKnopf } from "./controls";
import styles from "./fee-section.module.scss";
import { useTexte } from "./texte";

/**
 * Zwei Einstiege; die Sichtbarkeit entscheidet die C#-Sektion.
 *
 * Neben "Bearbeiten" steht seit der Testveroeffentlichung "Bericht
 * schreiben". Ansage des Nutzers: *"Wenn denen was auffaellt, sollen die
 * einfach auf nen Parkplatz klicken koennen und nen Button druecken, um nen
 * Debug zu erstellen."* Genau hier ist der Ort dafuer - der Nutzer hat den
 * Parkplatz schon angeklickt, um sich zu wundern.
 */
export const ParkingEditSection = (props: {
  waise?: number; bauzettel?: boolean; schluessel?: string;
}) => {
  const t = useTexte();
  const waise = props.waise ?? 0;
  /*
   * DIE FLAECHE KOMMT ALS SCHLUESSEL MIT, NICHT AUS DER SPIELAUSWAHL.
   *
   * Der Knopf las frueher `SelectedInfoUISystem.selectedEntity` erst beim
   * Klick. Der Einstieg ins Bearbeiten wechselt aber das Werkzeug, und CS2
   * raeumt dabei die Auswahl im Infofenster auf - mal vor, mal nach dem
   * Klick. Von Zeit zu Zeit verschwand so die Auswahl, und nichts geschah.
   *
   * Jetzt fast derselbe Weg wie aus der Liste: den Schluessel, der beim
   * Zeichnen der Sektion feststand, an `GewaehltenBearbeiten`. Der waehlt
   * die Flaeche erneut an und oeffnet das Werkzeug - ohne Kamerasprung.
   * Nur wenn kein Schluessel da ist, bleibt der alte Auswahlweg als
   * Rueckfall.
   */
  const bearbeiten = () => {
    const schluessel = props.schluessel ?? "";
    if (schluessel !== "") gewaehltenBearbeiten(schluessel);
    else editSelectedParkingLot();
  };
  if (waise === 3) {
    return (
      <div className={styles.editSection}>
        <div className={styles.waiseTitel}>{t.bauplanTitel}</div>
        <div className={styles.waiseText}>{t.bauplanText}</div>
        <TooltipKnopf
          text={t.tooltipBauplanWiederherstellen}
          className={styles.editButton}
          onMouseDown={(event: any) => event.stopPropagation()}
          onClick={gewaehltenReparieren}
        >
          {t.bauplanWiederherstellen}
        </TooltipKnopf>
      </div>
    );
  }
  if (waise > 0) {
    return (
      <div className={styles.editSection}>
        <div className={styles.waiseTitel}>{t.waiseTitel}</div>
        <div className={styles.waiseText}>{t.waiseErklaerung}</div>
        {waise === 1
          ? <TooltipKnopf
              text={t.tooltipWaiseReparieren}
              className={styles.editButton}
              onMouseDown={(event: any) => event.stopPropagation()}
              onClick={gewaehltenReparieren}
            >
              {t.waiseReparieren}
            </TooltipKnopf>
          : <div className={styles.waiseText}>{t.waiseNichtReparierbar}</div>}
      </div>
    );
  }
  return (
    <div className={styles.editSection}>
      {/* Der reine HTML-`title` erzeugt in Cohtml KEINEN Tooltip. Die
          Projektregel in controls.tsx sagt es woertlich: "Kein
          title-Attribut als alleinige Quelle." `TooltipKnopf` setzt den
          Spiel-Tooltip und den Rueckfalltext aus derselben Quelle. */}
      <TooltipKnopf
        text={t.tooltipBearbeiten}
        className={styles.editButton}
        onMouseDown={(event: any) => event.stopPropagation()}
        onClick={bearbeiten}
      >
        {t.bearbeiten}
      </TooltipKnopf>
      <TooltipKnopf
        text={t.tooltipLotBericht}
        className={styles.editButton}
        onMouseDown={(event: any) => event.stopPropagation()}
        onClick={meldeGewaehltenParkplatz}
      >
        {t.lotBericht}
      </TooltipKnopf>
    </div>
  );
};
