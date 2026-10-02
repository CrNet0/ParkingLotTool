# Hintergrund-Runde 3: Zulassung und Vanilla-Befund

`dotnet run -c Release --project Tests/Hintergrund3/Hintergrund3.csproj`
prueft die **Produktionsregel** des exklusiven Bilds und inspiziert den
Besitzerresolver der echten Game.dll. 28 Pruefungen, keine ECS-Welt.
Ein gruener Lauf belegt weder einen Neubau noch einen Rueckweg.

`Tests/Hintergrund3/mutation.ps1` prueft zwei Defekte an einer separaten
Quellkopie. Sie muessen 4 bzw. 1 Fehler und Exitcode 1 ergeben. Anschliessend
wird der unveraenderte Produktionsstand wieder uebersetzt und geprueft.

`Tests/Hintergrund3/Pflichtlaeufe.ps1` fuehrt die 27 GeometryParity-Varianten
aus; Ausgabe und Exitcodes stehen unter `artifacts/`.

Mit `PLT_SCHREIBERINVENTAR=1` statt des normalen Laufs wird die Game.dll
vollstaendig nach typisierten Add-/Set-Aufrufen fuer CreationDefinition
durchsucht, einschliesslich Konstruktoren. Im untersuchten Stand:
38031 IL-Methoden, 68 Schreibaufrufe in 53 Methoden / 17 Systemen.
Die Ausgabe nennt die schreibenden Jobmethoden; Phasen und Wiedergabepunkte
sind im [Bericht](../../BERICHT-HINTERGRUND3.md) zugeordnet.
