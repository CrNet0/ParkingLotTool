using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

int n = 0, fehler = 0;
void Pruefe(bool gut,string text) { n++; if (!gut) { fehler++; Console.WriteLine("FEHLER: " + text); } }
for (int m = 0; m < 8; m++)
    Pruefe(HintergrundSpurregel.PrivateAutos((m&1)!=0,(m&2)!=0,(m&4)!=0)==(m==1),"PublicOnly/Forbidden sind keine privaten Autospuren");
for (int m = 0; m < 16; m++)
foreach (int rein in new[] {-1,0,1,4})
foreach (int raus in new[] {-1,0,1,4})
    Pruefe(HintergrundSpurregel.Einmuendung((m&1)!=0,(m&2)!=0,(m&4)!=0,(m&8)!=0,rein,raus)
        == ((m&3)==3 && ((m&4)==0 || rein>0) && ((m&8)==0 || raus>0)),"Beidseitiger Netzbezug UND jede angeforderte Fahrtrichtung");
for (int mask = 0; mask < 8; mask++)
foreach (var ids in new[] {(10,80),(80,10),(10,11),(11,10),(80,81),(0,10),(10,0)})
{
    bool side = (mask&1)!=0, foreign80=(mask&2)!=0, foreign81=(mask&4)!=0;
    bool Fremd(int i) => i==80 && foreign80 || i==81 && foreign81;
    bool soll = side && (ids.Item1==10 && ids.Item2==80 && foreign80 || ids.Item2==10 && ids.Item1==80 && foreign80);
    Pruefe(HintergrundSpurregel.FremdeVerbindung(side,10,ids.Item1,ids.Item2,Fremd)==soll,
        "Eigene Verbindungen bleiben streng; nur fremdes Seitenobjekt ausnehmen");
}
int Such(Dictionary<int,int> owner,Dictionary<int,int> attached,int building=0)
    => HintergrundSpurregel.Suchbesitzer(1,x=>owner.GetValueOrDefault(x),x=>building!=0&&x==building,
        x=>attached.GetValueOrDefault(x),x=>x==3||x==4,x=>x>0,0);
Pruefe(Such(new(){{1,2},{2,3}},new(){{1,2}})==3,"Background Owner Decal->Traeger->Lot endet am LOT trotz Attached am Decal");
Pruefe(Such(new(){{1,2},{2,3}},new(){{1,2},{3,4}})==4,"Attached wird am Ende der Owner-Kette gelesen");
Pruefe(Such(new(){{1,2},{2,3}},new(){{3,4}},3)==3,"Building stoppt Owner/Attached-Aufstieg");
Pruefe(Such(new(){{1,2},{2,2}},new())==0,"Selbstzyklus ist keine Parking-Anbindung");
Pruefe(Such(new(){{1,2},{2,3},{3,2}},new())==0,"Mehrknotenzyklus hat feste Schranke");
Pruefe(Such(new(),new())==0,"Kein Besitzer ist keine gueltige Suchbasis");
foreach (float offset in new[] {0f,-1200f,8000f,-8000f})
{
    var p = new float3(offset,190,offset);
    Pruefe(HintergrundAbgleich.Knoten(p,new[] {(1,p+new float3(0,.049f,0)),(2,p+new float3(1,0,0))},0)==1,"Gueltiger voriger Portionsknoten MUSS wiederverwendet werden");
    Pruefe(HintergrundAbgleich.Knoten(p,new[] {(1,p+new float3(0,.051f,0))},0)==0,"Falsche Hoehe bleibt streng");
}
foreach (int anzahl in new[] {0,1,8,9,218,2098,10000})
foreach (double kosten in new[] {0d,.1d,1d,3d})
{
    double zeit = 0; int bearbeitet = 0, bilder = 0;
    IEnumerable<int> Arbeit() { for (int i=0;i<anzahl;i++) { bearbeitet++; zeit+=kosten; yield return 1; } }
    using var p = new HintergrundPortion(Arbeit());
    while (!p.Fertig && bilder++ < anzahl+2)
    {
        int vorher = bearbeitet; double start=zeit;
        int ausgabe=p.Weiter(()=>zeit);
        Pruefe(ausgabe==bearbeitet-vorher && p.Einheiten<=8,"Echte Ausgaben, feste Einheitenobergrenze");
        Pruefe(zeit-start<=2+kosten+.000001,"Zeitbudget plus hoechstens eine unteilbare Einheit");
    }
    Pruefe(p.Fertig&&p.Gesamt==anzahl&&bearbeitet==anzahl,"Kein Nichts-Tun: ALLE gueltigen Ausgaben erhalten");
}

var root = new DirectoryInfo(AppContext.BaseDirectory);
while(root!=null&&!File.Exists(Path.Combine(root.FullName,"AGENTS.md"))) root=root.Parent;
string Lies(string p)=>File.ReadAllText(Path.Combine(root!.FullName,p));
var bau=Lies("Tools/ParkingLotBauarbeiter.Portionen.cs");
var spuren=Lies("Tools/ParkingLotBauarbeiter.Spuren.cs");
Pruefe(!File.Exists(Path.Combine(root!.FullName,"Tools/ParkingLotStadtteilung.cs"))
    && !Lies("Tools/ParkingLotHintergrundSystem.cs").Contains("HintergrundTeilungsbild()"),"Verworfene Stadtteilung und Temp-Apply-Zyklus entfernt");
Pruefe(bau.Contains("HintergrundBauportion")
    &&Lies("Tools/ParkingLotAreaPreviewDefinitions.cs").Contains("SchliesseHintergrundNetzbesitz()"),"Portionen und Besitzerpuffer vor Decals");
Pruefe(spuren.Contains("Suchbesitzer(e,")&&spuren.Contains("LaneConnection>(spur)")&&spuren.Contains("LaneFlags.Virtual")&&spuren.Contains("HatSubObject"),"Reale Parking-Anbindung und Besitzerpuffer geprueft");
Pruefe(spuren.Contains("ComponentType.ReadOnly<Game.Objects.Object>()")&&spuren.Contains("parkSoll == _hintergrundParkSoll"),"Fremde Gebaeude ohne Owner und vollstaendige Decal-Ausgabe");
var rueck=Lies("Tools/ParkingLotNetzRueckweg.cs");
Pruefe(rueck.Contains("em.AddComponent<ParkingLotRueckwegsicherungOffen>(lot)")
    &&rueck.Contains("em.RemoveComponent<ParkingLotRueckwegsicherungOffen>(lot)")
    &&Lies("Tools/ParkingLotHintergrundSystem.cs").Contains("ParkingLotNetzRueckweg.VerwerfeOffeneSicherung(EntityManager,lot)"),"Save zwischen Portionen erkennt unvollstaendige Sicherung vor Abriss");

// Reale installierte Schnittstelle; Unity-ECS-Jobs laufen im Container nicht.
string managed=Environment.GetEnvironmentVariable("CSII_MANAGEDPATH")!;
AssemblyLoadContext.Default.Resolving+=(_,name)=>{
    string path=Path.Combine(managed,name.Name+".dll");
    return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;
};
var game=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed,"Game.dll"));
Pruefe(game.GetType("Game.Net.LaneConnection",true)!.GetField("m_StartLane")!=null,
    "Installierte Parking-Anbindung hat die gepruefte Start-Lane-Schnittstelle");
Console.WriteLine($"HINTERGRUND7: {n} Pruefungen, {fehler} Fehler (Produktionsregeln, Portionslast, Quellenvertrag, Game.dll-Schnittstelle; keine Ingame-Materialisierung).");
return fehler==0?0:1;
