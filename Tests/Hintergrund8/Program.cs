using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

int n=0,fehler=0;
void Pruefe(bool gut,string text) { n++; if (!gut) { fehler++; Console.WriteLine("FEHLER: "+text); } }
for (int m=0;m<32;m++)
foreach (float d in new[] {0f,.049f,.05f,.051f,1f,float.NaN,float.PositiveInfinity})
    Pruefe(Netzerhalt.Unveraendert((m&1)!=0,(m&2)!=0,(m&4)!=0,(m&8)!=0,(m&16)!=0,d)
        == (m==31 && d<=.05f),"Prefab, Richtung, Stadt-ID, Gegenpuffer, Seiten UND endlicher Kursabstand");

Netzerhalt.Teil<int> Teil(int id,int start,int ende,float2 a,float2 d)
{
    var x=new float3(a.x,190,a.y); var y=new float3(d.x,191,d.y);
    return new Netzerhalt.Teil<int> { Kante=id,Start=start,Ende=ende,
        Kurve=(x,math.lerp(x,y,1f/3f),math.lerp(x,y,2f/3f),y),
        PrefabGleich=true,Verbunden=true,SeitenGleich=true };
}
List<Netzerhalt.Teil<int>> Suche(float2 a,float2 d,List<Netzerhalt.Teil<int>> teile,int stadt=10,bool aus=false)
    => Netzerhalt.FindeKette(a,d,stadt,aus,-1,teile);
foreach (float ort in new[] {0f,-1200f,8000f,-8000f})
foreach (var v in new[] {new float2(0,42),new float2(36,0),new float2(30,24),new float2(-20,45)})
for (int anzahl=1;anzahl<=12;anzahl++)
{
    var a=new float2(ort,ort); var d=a+v;
    var teile=new List<Netzerhalt.Teil<int>>();
    for (int i=0;i<anzahl;i++) teile.Add(Teil(100+i,10+i,11+i,math.lerp(a,d,(float)i/anzahl),math.lerp(a,d,(float)(i+1)/anzahl)));
    Pruefe(Suche(a,d,teile).Count==anzahl,"Alle unveraenderten Gassen/Zoning-Teilkurse MUSS die gemeinsame Regel erhalten");
    teile.Reverse();
    Pruefe(Suche(a,d,teile).Count==anzahl,"Reihenfolge des Besitzerpuffers ist bedeutungslos");
    Pruefe(Suche(a,d,teile,stadt:11+anzahl).Count==0,"Andere Stadt-ID trotz exakt gleicher Koordinate wird abgelehnt");
    Pruefe(Suche(a,d,teile,stadt:10+anzahl,aus:true).Count==anzahl,"Ausfahrt benutzt dieselbe Stadt-ID am Kursende");
    foreach (var p in teile)
    {
        p.PrefabGleich=false;
        Pruefe(Suche(a,d,teile).Count==0,"Jeder Vanilla->PLT-Wechsel erzwingt Neubau"); p.PrefabGleich=true;
        p.Verbunden=false;
        Pruefe(Suche(a,d,teile).Count==0,"Jeder fehlende echte Gegenpuffer verhindert Erhalt"); p.Verbunden=true;
        p.SeitenGleich=false;
        Pruefe(Suche(a,d,teile).Count==0,"Zoningseitenwechsel bleibt Neubau"); p.SeitenGleich=true;
        var vorher=p.Kurve;
        p.Kurve=(vorher.A,vorher.B+new float3(1,0,1),vorher.C,vorher.D);
        Pruefe(Suche(a,d,teile).Count==0,"Geaenderter innerer Kontrollpunkt trotz gleicher Enden ist kein Erhalt"); p.Kurve=vorher;
        var fehlt=teile.Where(t=>t.Kante!=p.Kante).ToList();
        Pruefe(Suche(a,d,fehlt).Count==0,"Jede fehlende Teilkante wird erkannt, kein halber Erhalt");
    }
    if (anzahl>1)
    {
        int ende=teile[0].Start; teile[0].Start=999;
        Pruefe(Suche(a,d,teile).Count==0,"Gleiche Lage ohne gemeinsame Zwischen-ID ist keine Kette"); teile[0].Start=ende;
    }
    var fremd=Teil(999,90,91,a+new float2(5),d+new float2(5));
    teile.Add(fremd);
    Pruefe(Suche(a,d,teile).Count==anzahl,"Gueltige Loesung wird auch neben einem falschen Kandidaten gefunden");
}
var normal=Teil(1,10,11,float2.zero,new float2(30,0));
var um=normal.Kurve;
normal.Kurve=(um.D,um.C,um.B,um.A);
Pruefe(Suche(float2.zero,new float2(30,0),new(){normal}).Count==0,"Einbahn-Richtungswechsel ist kein unveraenderter Kurs");
normal.Kurve=(um.A,um.B+new float3(0,5,0),um.C,um.D);
Pruefe(Suche(float2.zero,new float2(30,0),new(){normal}).Count==1,"Bisherige Terrain-Y-Kurve bleibt stehen, ohne sie neu ans Gelaende zu legen");
normal.Kurve=(um.A,new float3(float.NaN),um.C,um.D);
Pruefe(Suche(float2.zero,new float2(30,0),new(){normal}).Count==0,"Nichtendliche Hoehe darf nicht erhalten werden");
foreach (float delta in new[]{.049f,.051f})
{
    var p=Teil(1,10,11,float2.zero,new float2(30,0));
    p.Kurve=(p.Kurve.A,p.Kurve.B+new float3(0,0,delta),p.Kurve.C,p.Kurve.D);
    Pruefe((Suche(float2.zero,new float2(30,0),new(){p}).Count==1)==(delta<.05f),"5-cm-Grenze an Kontrollpunkten von beiden Seiten");
}
for(int geplant=1;geplant<=218;geplant++)
for(int erhalten=0;erhalten<=geplant;erhalten++)
{
    int neu=geplant-erhalten;
    Pruefe(Netzerhalt.Vollstaendig(geplant,erhalten,neu,neu),"Neu PLUS erhalten erfuellt den ganzen Plan bis 218 Kurse");
    if(neu>0) Pruefe(!Netzerhalt.Vollstaendig(geplant,erhalten,neu-1,neu),"Ein fehlender neuer Kurs bleibt rot");
    if(erhalten>0) Pruefe(!Netzerhalt.Vollstaendig(geplant,erhalten-1,neu,neu),"Ein fehlender erhaltener Kurs bleibt rot");
}
Pruefe(!Netzerhalt.Vollstaendig(0,0,0,0),"Leerer Plan ist kein erfolgreicher Sync");
var root=new DirectoryInfo(AppContext.BaseDirectory);
while(root!=null&&!File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root=root.Parent;
Fm45.Pruefe(root!.FullName,Pruefe);
string Lies(string p)=>File.ReadAllText(Path.Combine(root!.FullName,p));
var erhalt=Lies("Tools/ParkingLotNetzerhalt.cs"); var zoning=Lies("Tools/ParkingLotZoningErhalt.cs");
var netz=Lies("Tools/ParkingLotNetBuilder.cs"); var rueck=Lies("Tools/ParkingLotNetzRueckweg.cs");
Pruefe(erhalt.Contains("Netzerhalt.FindeKette(")&&zoning.Contains("FindeNetzerhalt(")
    &&erhalt.Contains("bool erhalten = stadt && FindeNetzerhalt("),"Zoning und Gassen nutzen denselben getesteten Kettenwaehler");
Pruefe(erhalt.Contains("if (!_bauarbeiter) yield break")&&netz.Contains("_bauarbeiter && _erhalteneKursketten.ContainsKey((\"entrance-gasse\",index))"),"Gassenerhalt nur im Sync, normaler Edit baut weiter wie bisher");
Pruefe(erhalt.Contains("InnereNetzkurse(piece,breit,schmal)")
    &&Lies("Tools/ParkingLotNetDefinitions.cs").Contains("InnereNetzkurse(piece,Entity.Null,Entity.Null)")
    &&Lies("Tools/ParkingLotNetCourse.cs").Contains("_erhalteneKursketten.ContainsKey((kind,index))"),
    "Bereits passende PLT-Innenwege werden ebenfalls erhalten, Vanilla-Prefabwechsel bleibt Neubau");
Pruefe(Lies("Tools/ParkingLotEditAbriss.cs").Contains("if (_erhalteneNetzteile.Contains(teil))")
    &&Lies("Tools/ParkingLotEditMode.cs").Contains("_erhalteneNetzteile.Count > 0 ? HintergrundBesitzanker"),"Auch gassen-only Lot benutzt Abrissschutz und Besitzeranker");
Pruefe(zoning.Contains("EntityManager.AddComponent<Updated>(e)")&&!erhalt.Contains("SetComponentData<Edge>")
    &&!erhalt.Contains("SetComponentData<Curve>"),"Auffrischen ueber Updated, 0 physische Netzschreiber im Erhaltpfad");
Pruefe(rueck.Contains("else kurse.Add(bestand)")&&rueck.Contains("em.AddBuffer<ParkingLotErhaltenerKurs>(lot)")
    &&rueck.Contains("ParkingLotNetzerhalt.Pruefe(em,r.Bestand)"),"Erhaltene IDs serialisiert und im Rueckweg gemessen, nicht ausgegeben");
Pruefe(Lies("Tools/ParkingLotBauarbeiter.Portionen.cs").Contains("foreach (var e in _erhalteneNetzteile)")
    &&Lies("Tools/ParkingLotBauarbeiter.cs").Contains("ParkingLotNetzerhalt.EntferneGelieheneVerweise(")
    &&Lies("Tools/ParkingLotHintergrundSystem.cs").Contains("ParkingLotNetzerhalt.EntferneGelieheneVerweise("),
    "Parkspuren sehen erhaltene Gassen; Ruecknahme entfernt geliehene SubNet-Verweise auch nach Laden vor Deleted");
Pruefe(Lies("Tools/ParkingLotBauarbeiter.cs").Contains("ErhalteneKursketteDa(kette.Value)")
    &&Lies("Tools/ParkingLotBauarbeiter.Einmuendungen.cs").Contains("kandidaten.UnionWith(_erhalteneNetzteile)"),"Abnahme und Einmuendung messen erhaltene Gassen");
Pruefe(Lies("Tools/ParkingLotBauarbeiter.cs").Contains("Netzerhalt.Vollstaendig(soll,erhalten,kurse,_netRecords.Count)"),"Kein Nichts-Tun: vollstaendiger neuer und erhaltener Sollplan");
Pruefe(Lies("Tools/ParkingLotHintergrundSystem.cs").Contains("case Phase.Anmeldung:")
    &&Lies("Tools/ParkingLotBauarbeiter.Anschluesse.cs").Contains("new HintergrundPortion(HintergrundAnschlussmeldeschritte())")
    &&Lies("Tools/ParkingLotBauarbeiter.Spuren.cs").Contains("Netzerhalt nach Updated:"),
    "Updated in eigenen Portionen ohne wiederholte Kinderphase; 3D-Erhalt nochmals nach Updated geprueft");
Pruefe(netz.Contains("_bauarbeiter && EntityManager.HasComponent<Edge>(p.Anschluss.Entity)")
    &&Lies("Tools/ParkingLotNetCourse.cs").Contains("Sync-Kurs {kind}/{index} verlangt eine Kantenteilung"),"Unerwartete Stadtkantenteilung wird belegt und vor Definition verhindert");
Pruefe(!Lies("Tools/ParkingLotDefinitionsendeSystem.cs").Contains("einmal.Contains")
    &&!Lies("Tools/ParkingLotDefinitionsendeSystem.cs").Contains("MerkeTemp"),"Teilungs-Auftragswelle entfernt, normale Werkzeugherkunft bleibt separat");
Console.WriteLine($"HINTERGRUND8: {n} Pruefungen, {fehler} Fehler (gemeinsame Produktionsregel und Quellenvertrag; keine ECS-Materialisierung).");
return fehler==0?0:1;
