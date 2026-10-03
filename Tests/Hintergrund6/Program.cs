using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ParkingLotTool.Geometry;
using ParkingLotTool.Tools;
using Unity.Entities;
using Unity.Mathematics;

int n=0,fehler=0;
void Pruefe(bool gut,string text) { n++; if (!gut) { fehler++; Console.WriteLine("FEHLER: "+text); } }
// Die Kontrollpunkte aus Logzeile 1260, unabhaengig vom Produktionsschnitt.
var soll=(new float3(-1032.8080f,122.9202f,-301.8957f),new float3(-1030.6310f,122.9740f,-296.1615f),
    new float3(-1028.4550f,123.0277f,-290.4274f),new float3(-1026.2780f,123.0815f,-284.6932f));
var ist=(soll.Item1,new float3(soll.Item2.x,122.8127f,soll.Item2.z),new float3(soll.Item3.x,123.1084f,soll.Item3.z),soll.Item4);
Pruefe(HintergrundAbgleich.Kurvenabstand(ist,soll,true)<=.05f,"Logkurs MUSS trotz Vanilla-Innenhoehe gefunden werden");
Pruefe(HintergrundAbgleich.Kurvenabstand(ist,soll)>.16f,"Gemessene Abweichung bleibt im strengen Messmodus sichtbar");
foreach (var d in new[] {new float3(.051f,0,0),new float3(0,.051f,0),new float3(0,0,.051f),new float3(0,-183,0)})
{
    Pruefe(HintergrundAbgleich.Kurvenabstand((soll.Item1+d,ist.Item2,ist.Item3,soll.Item4),soll,true)>.05f,"Endpunkt bleibt 3D mit 5 cm");
    Pruefe(HintergrundAbgleich.Kurvenabstand((soll.Item1,ist.Item2,ist.Item3,soll.Item4+d),soll,true)>.05f,"Beide Enden bleiben streng");
}
Pruefe(HintergrundAbgleich.Kurvenabstand((ist.Item1,ist.Item2+new float3(.051f,0,0),ist.Item3,ist.Item4),soll,true)>.05f,"Inneres XZ bleibt streng");
Pruefe(float.IsPositiveInfinity(HintergrundAbgleich.Kurvenabstand((ist.Item1,new float3(0,float.NaN,0),ist.Item3,ist.Item4),soll,true)),"Auch freigegebenes Y muss endlich sein");
for (int mask=0;mask<32;mask++)
{
    bool gerade=(mask&1)!=0,boden=(mask&2)!=0,parent=(mask&4)!=0,flatten=(mask&8)!=0,owner=(mask&16)!=0;
    bool erwartet = !gerade || (boden && !parent && (!flatten || owner));
    Pruefe(HintergrundKurspruefung.InnenhoeheVanilla(gerade,boden,parent,flatten,owner)==erwartet,"32 Vanilla-Flagkombinationen");
}
float3 Punkt((float3 A,float3 B,float3 C,float3 D) c,float t)
{ float u=1-t; return c.A*(u*u*u)+c.B*(3*u*u*t)+c.C*(3*u*t*t)+c.D*(t*t*t); }
for (int i=0;i<=16;i++)
{
    bool innen=i>0&&i<16;
    Pruefe(HintergrundKurspruefung.LageGedeckt(Punkt(soll,i/16f),new[] {Punkt(ist,i/16f)},innen),"Alle 17 Proben des gueltigen Logkurses");
    Pruefe(!HintergrundKurspruefung.LageGedeckt(Punkt(soll,i/16f),Array.Empty<float3>(),innen),"Fehlende Kurve bleibt Fehler");
    Pruefe(!HintergrundKurspruefung.LageGedeckt(float3.zero,new[] {new float3(.051f,0,0)},innen),"5,1 cm XZ an allen Proben erkannt");
    if (!innen) Pruefe(!HintergrundKurspruefung.LageGedeckt(float3.zero,new[] {new float3(0,-183,0)},innen),"183 m Absacken an Endproben erkannt");
}
// Rueckwegdiagnose und Abnahme verwenden denselben Abstand, auch bei Umkehr
// und Teilung. Analytischer Halbschnitt, ohne Schnitt-Funktion als Sollgeber.
var c=(new float3(0,0,0),new float3(30,12,0),new float3(60,-6,30),new float3(90,3,30));
var halb=(c.Item1,new float3(15,6,0),new float3(30,4.5f,7.5f),new float3(45,2.625f,15));
var h=(halb.Item1,halb.Item2+new float3(0,.16129f,0),halb.Item3-new float3(0,.08067f,0),halb.Item4);
Pruefe(HintergrundAbgleich.Kurvenabstand(h,HintergrundAbgleich.Schnitt(c,0,.5f),true)<.00001f,"Teilkurs mit Vanilla-Y gefunden");
Pruefe(HintergrundAbgleich.Kurvenabstand((h.Item4,h.Item3,h.Item2,h.Item1),HintergrundAbgleich.Schnitt(c,.5f,0),true)<.00001f,"Umgekehrter Teilkurs gefunden");

// Echter gemeinsamer Schreiber: Hintergrund, normaler Bau und Repair haben
// dieselbe Datenquelle. Alle optionalen Listen fehlen beim ersten Schreiben.
var bauer=new ParkingLotToolSystem(); var em=bauer.EntityManager;
var lot=new Entity {Index=10,Version=1}; em.Anlegen(lot);
em.AddComponentData(lot,new ParkingLotCarrierReference {Carrier=new Entity {Index=11,Version=1}});
var q=new Bauzettelquelle {
    Settings=new LayoutSettings {Es=8,Ai=6,Cw=6,Sl=5,Sw=3,Md=2,Cr=18,Gassenbreite=6,AngleMode="quer",Zoningstrasse="Small Road"},
    Punkte=new[] {new float3(0,25,0),new float3(100,26,0),new float3(100,27,100)},
    ZoningWinkelmodus="edge",ZoningAussentiefeVorwahl=2,FlaecheStrasse="Pavement Surface 01",FlaecheDekoration="Grass Surface 01",
    SurfaceRoadOn=true,SurfaceDecorationOn=true,SurfaceApronOn=true,BayIcons=true };
Pruefe(bauer.Schreibe(lot,q),"Nulloptionen muessen speicherbar sein: "+ParkingLotTool.Mod.log.LetzterFehler);
bool gelesen=ParkingLotBaukontextLeser.TryRead(em,lot,out var k,out var grund,melden:false);
Pruefe(gelesen,"Geschriebener Zettel muss vollstaendig lesbar sein: "+grund);
if (gelesen) Pruefe(k.Punkte.Length==3&&k.Zugaenge.Length==0&&k.Bushalte.Count==0&&k.Zettel.AngleMode==3,"Umriss/Across/Leerlisten erhalten");
q.Settings.Entrances=new[] {new Entrance {Edge=1,Along=.4,Art=Zufahrtsart.Zufahrt}};
q.Settings.BusStops=new[] {new BusStopPlacement {A=new float2(0,0),B=new float2(10,0),Along=.5f,Left=true}};
q.Seitenplan=new[] {(new float2(0,0),new float2(10,0),true,true)};
q.Randzoning=new[] {new ParkingGeometry.RandzoningLinie {A=new float2(0,0),B=new float2(10,0)}};
Pruefe(bauer.Schreibe(lot,q),"Zweiter Schreibvorgang mit Seiten und Bushalt: "+ParkingLotTool.Mod.log.LetzterFehler);
gelesen=ParkingLotBaukontextLeser.TryRead(em,lot,out k,out grund,melden:false);
Pruefe(gelesen&&k.Bushalte.Count==1&&k.Seitenplan.Count==1&&k.Randzoning.Count==1&&k.Zugaenge.Length==1,"Seiten/Zugang/Bushalt nachlesen");
var regel=new HintergrundAuftragsregel<Entity>();
foreach (var typ in new[] {typeof(ParkingLotBuildText),typeof(ParkingLotBuildPoint),typeof(ParkingLotBuildEntrance),typeof(ParkingLotBuildReceipt)})
{
    var entfernen=typeof(EntityManager).GetMethod("RemoveComponent").MakeGenericMethod(typ); entfernen.Invoke(em,new object[] {lot});
    bool voll=ParkingLotBaukontextLeser.TryRead(em,lot,out _,out _,melden:false);
    if (voll) { regel.Einreihen(lot); regel.Start(lot); }
    Pruefe(!voll&&regel.Offen==0&&regel.Versuche(lot)==0,"Jeder fehlende Pflichtteil verhindert Einreihen ohne Versuch");
    Pruefe(bauer.Schreibe(lot,q),"Repair durch vollstaendigen Schreibpfad");
}
em.SetComponentData(lot,new ParkingLotBuildReceipt {Version=99});
Pruefe(!ParkingLotBaukontextLeser.TryRead(em,lot,out _,out _,melden:false),"Unlesbare Version ist kein vollstaendiger Plan");
Pruefe(bauer.Schreibe(lot,q)&&ParkingLotBaukontextLeser.TryRead(em,lot,out _,out _,melden:false),"Reparierter Plan wird wieder zugelassen");

// Spatialsuche gegen brute force; Trefferzahl wird gemessen, keine FPS-Behauptung.
int kandidaten=0,brute=0;
foreach (float versatz in new[] {0f,-1250f,8000f,-8000f})
{
    var index=new HintergrundLageindex<int>(); var punkte=new List<float2>();
    for (int i=0;i<4200;i++) { var p=new float2(versatz+(i%100)*3f,versatz+(i/100)*3f); punkte.Add(p); index.Fuege(p,i); }
    for (int i=0;i<4200;i+=21)
    {
        var p=punkte[i]+new float2(.049f,-.049f); var nah=index.Nahe(p).ToArray(); kandidaten+=nah.Length; brute+=punkte.Count;
        Pruefe(nah.Contains(i),"Gueltiger Nachbar ueber Zellgrenze, auch bei 8 km");
    }
    foreach (float grenze in new[] {-.101f,-.1f,-.001f,0f,.099f,.1f,.101f})
    {
        var ix=new HintergrundLageindex<int>(); var p=new float2(versatz+grenze);
        ix.Fuege(p,1); Pruefe(ix.Nahe(p+new float2(.049f)).Contains(1),"Zellgrenzen duerfen Treffer nicht verlieren");
    }
}
Pruefe(kandidaten==800&&brute==3360000,"800 Kandidaten statt 3360000 im Abstandsraster");
Console.WriteLine($"Indexmessung: {kandidaten} Kandidaten, brute force {brute}.");
Pruefe(HintergrundTakt.Auftragspause>=30&&HintergrundTakt.Versuchspause>=120&&HintergrundTakt.Pruefabstand>=8,"Drei geforderte Bildpausen");
for (int i=0;i<120;i++) Pruefe(!HintergrundTakt.Faellig(i,120),"Kein vorzeitiger Wiederholungsstart");
Pruefe(HintergrundTakt.Faellig(120,120),"Auftrag MUSS nach der Pause beginnen koennen");
for (int mask=0;mask<8;mask++) Pruefe(HintergrundTakt.WerkzeugdefinitionBeenden((mask&1)!=0,(mask&2)!=0,(mask&4)!=0)==(mask==1),"Eigene Temp-Eingaben nur beim Ende; nie fremde/permanente/aktive");
// Der zweite CourseSplit-Abschnitt traegt Seed + 1 und keine PLT-Marke.
// Innen-Y wurde schon von Vanilla geaendert; Herkunft braucht alle vier XZ.
float herkunftAbstand=HintergrundDefinitionsherkunft.KontrollXZAbstand(h,halb);
Pruefe(herkunftAbstand<.00001f&&HintergrundDefinitionsherkunft.TempTeil(true,true,100,101,4,herkunftAbstand),"Eigener Teil MUSS trotz Vanilla-Y eine Herkunft erhalten");
foreach (int fremdseed in new[] {99,104,900})
    Pruefe(!HintergrundDefinitionsherkunft.TempTeil(true,true,100,fremdseed,4,0),"Fremde Seeds trotz identischer Lage nicht markieren");
Pruefe(HintergrundDefinitionsherkunft.TempTeil(true,true,int.MaxValue,int.MinValue,4,0),"Vanilla-Seedueberlauf erhaelt Herkunft");
Pruefe(!HintergrundDefinitionsherkunft.TempTeil(false,true,100,101,4,0),"Fremder Prefab bleibt unangetastet");
Pruefe(!HintergrundDefinitionsherkunft.TempTeil(true,false,100,101,4,0),"Fremder Owner bleibt unangetastet");
Pruefe(!HintergrundDefinitionsherkunft.TempTeil(true,true,100,101,0,0),"Keine Quelle bei leerer Kursmenge");
foreach (float falsch in new[] {.051f,float.NaN,float.PositiveInfinity})
    Pruefe(!HintergrundDefinitionsherkunft.TempTeil(true,true,100,101,4,falsch),"Falsche XZ-Lage ist kein Teil der Werkzeugquelle");
Pruefe(HintergrundDefinitionsherkunft.KontrollXZAbstand((h.Item1,h.Item2+new float3(.051f,0,0),h.Item3,h.Item4),halb)>.05f,"Alle vier Kontroll-XZ fuer Herkunft messen");
Pruefe(float.IsPositiveInfinity(HintergrundDefinitionsherkunft.KontrollXZAbstand((h.Item1,new float3(float.NaN),h.Item3,h.Item4),halb)),"NaN-Herkunft wird verworfen");
for (int mask=0;mask<16;mask++) Pruefe(HintergrundDefinitionsherkunft.TempHilfskurs((mask&1)!=0,(mask&2)!=0,(mask&4)!=0,(mask&8)!=0)==(mask==13),"Temp-Hilfskurs nur mit belegtem Hauptkurs und AuxiliaryNet-Deklaration");
var root=new DirectoryInfo(AppContext.BaseDirectory);
while (root!=null&&!File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root=root.Parent;
if (root==null) throw new InvalidOperationException("Repo fehlt");
string Lies(string p)=>File.ReadAllText(Path.Combine(root.FullName,p));
var system=Lies("Tools/ParkingLotHintergrundSystem.cs"); var bau=Lies("Tools/ParkingLotBauarbeiter.cs");
var sync=Lies("Tools/ParkingLotSync.cs"); var preview=Lies("Tools/ParkingLotAreaPreview.cs");
Pruefe(system.IndexOf("KannNeubauen(lot,out var grund)")<system.IndexOf("_regel.Einreihen(lot)"),"Lesbarkeit vor Versuchssperre");
Pruefe(system.Contains("KannNeubauen(next.Lot,out var grund)")&&system.Contains("bild == _letztesArbeitsbild"),"Start erneut validiert und hoechstens einmal je Bild");
Pruefe(system.Contains("gesichert && _regel.Wiederholen")&&system.Contains("fruehestens:UnityEngine.Time.frameCount + HintergrundTakt.Versuchspause"),"Fehler vor Abriss wird nicht wiederholt; Rueckwegversuche pausieren");
Pruefe(sync.Contains("hintergrund.IstErsatz(lot)")&&sync.Contains("MeldeBauplanFehlt(lot,grund)"),"Ersatz und fehlender Plan nie neue Sync-Auftraege");
Pruefe(bau.Contains("Task.Run(() =>")&&bau.Contains("_hintergrundRechenthread")&&bau.Contains("if (!_hintergrundPlanVorbereitet)"),"Gemessener Hintergrundtask und einmalige Vorflaechenvorbereitung");
Pruefe(preview.Contains("WerkzeugdefinitionBeenden(eigen,permanent,false)")&&!preview.Contains("_definitionQuery = GetDefinitionQuery()"),"Definitionsende umfasst nur eigene Temp-Eingaben, auch Updated");
Pruefe(Lies("Tools/ParkingLotBuildStages.cs").Contains("if (built) TryDestroyDefinitionEntities"),"Normalbau beendet seine Eingaben nach Apply");
Pruefe(Lies("Tools/ParkingLotDefinitionsausgabe.cs").Contains(".MerkeWerkzeugentwurf(")&&Lies("Tools/ParkingLotDefinitionsendeSystem.cs").Contains("MarkiereTempAbschnitte(kurse)"),"Temp-Herkunft vor CourseSplit erfassen, danach zuordnen");
Pruefe(Lies("Tools/ParkingLotExklusivesBildSystem.cs").Contains("werkzeug?.HatAktivenEntwurf == true"),"Panel offen allein ist kein Entwurf");
var rueckweg=Lies("Tools/ParkingLotNetzRueckweg.cs");
Pruefe(rueckweg.Contains("em.AddComponentData(d, Eingabekurs(alt))")&&rueckweg.Contains("InnenhoeheVanilla(em,alt.Prefab,Eingabekurs(alt),alt.Besitzer)"),"Rueckweg erzeugt und prueft mit denselben ParentMesh-Werten");
Pruefe(Lies("Tools/ParkingLotGassenkurs.cs").Contains("if (anschluss.Entity != Entity.Null)\n")||Lies("Tools/ParkingLotGassenkurs.cs").Contains("if (anschluss.Entity != Entity.Null)\r\n"),"Nutzerkorrektur fuer Knoten UND Kanten erhalten");
Console.WriteLine($"HINTERGRUND6: {n} Pruefungen, {fehler} Fehler (Rechenregeln, echter Bauzettelschreiber/-leser und Quellenvertrag; keine ECS-Abnahme).");
return fehler==0?0:1;
