using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

internal static class VanillaBefund
{
    // Originale Game.dll nur lesen; NetUtils.CanConnect ist eine reine
    // Bitmaskenfunktion. Keine ECS-Welt, keine nativen Jobs, kein Spielcode kopiert.
    internal static void Messen(Action<bool,string> pruefe)
    {
        string managed=Environment.GetEnvironmentVariable("CSII_MANAGEDPATH")!;
        AssemblyLoadContext.Default.Resolving+=(_,name)=>
        {
            string p=Path.Combine(managed,name.Name+".dll");
            return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;
        };
        string dll=Path.Combine(managed,"Game.dll");
        var game=AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
        var daten=game.GetType("Game.Prefabs.NetData",true)!;
        var layer=game.GetType("Game.Net.Layer",true)!;
        var verbinden=game.GetType("Game.Net.NetUtils",true)!.GetMethod("CanConnect",new[]{daten,daten})!;
        object Netz(string required,string connect)
        {
            var n=Activator.CreateInstance(daten)!;
            daten.GetField("m_RequiredLayers")!.SetValue(n,Enum.Parse(layer,required));
            daten.GetField("m_ConnectLayers")!.SetValue(n,Enum.Parse(layer,connect));
            return n;
        }
        bool Anschluss(string a,string ac,string b,string bc)
            => (bool)verbinden.Invoke(null,new[]{Netz(a,ac),Netz(b,bc)})!;
        pruefe(!Anschluss("MarkerPathway","Pathway, MarkerPathway","Road","Road, TrainTrack"),"Installierte Vanilla verwirft MarkerPathway -> Road in CourseSplit");
        pruefe(!Anschluss("Road","Road, TrainTrack","MarkerPathway","Pathway, MarkerPathway"),"Layer-Sperre gilt auch in Gegenrichtung, unabhaengig vom Owner");
        pruefe(Anschluss("MarkerPathway","Pathway, MarkerPathway","MarkerPathway","Pathway, MarkerPathway"),"Gleiche Wegknoten passieren CourseSplit");
        pruefe(Anschluss("Road","Road, TrainTrack","Road","Road, TrainTrack"),"Road-Kurse mit AuxiliaryNet behalten ihren regulaeren Anschlussweg");
        Console.WriteLine("Vanilla Game.dll SHA256: "+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))));
    }
}
