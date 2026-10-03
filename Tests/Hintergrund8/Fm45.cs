using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ParkingLotTool.Geometry;
using Unity.Mathematics;

internal static class Fm45
{
    private sealed class Float2Leser : JsonConverter<float2>
    {
        public override float2 Read(ref Utf8JsonReader r,Type t,JsonSerializerOptions o)
        {
            using var doc=JsonDocument.ParseValue(ref r);
            return new float2(doc.RootElement[0].GetSingle(),doc.RootElement[1].GetSingle());
        }
        public override void Write(Utf8JsonWriter w,float2 v,JsonSerializerOptions o) => throw new NotSupportedException();
    }
    internal static void Pruefe(string root,Action<bool,string> pruefe)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"Tests/Hintergrund8/fm45.json")));
        var j=doc.RootElement;
        float2 XZ(JsonElement p)=>new(p.GetProperty("X").GetSingle(),p.GetProperty("Z").GetSingle());
        float3 P(JsonElement p)=>new(p.GetProperty("X").GetSingle(),p.GetProperty("Y").GetSingle(),p.GetProperty("Z").GetSingle());
        int Id(JsonElement p)=>p.GetProperty("Index").GetInt32();
        var optionen=new JsonSerializerOptions(); optionen.Converters.Add(new Float2Leser());
        optionen.Converters.Add(new JsonStringEnumConverter());
        var settings=JsonSerializer.Deserialize<LayoutSettings>(j.GetProperty("Settings").GetRawText(),optionen)!;
        settings.Zellen=true;
        var polygon=j.GetProperty("Polygon").EnumerateArray().Select(XZ).ToArray();
        var layout=ParkingGeometry.Build(polygon,settings);
        var gassen=layout.NetLine.Where(p=>p.Kind=="entrance"&&Zufahrtsarten.IstGasse(p.Art)).ToArray();
        pruefe(gassen.Length==2,"FM45: gleicher Bauzettel liefert genau zwei Gassen");
        int erhalten=0,stadtknoten=0;
        foreach(var g in j.GetProperty("Gassen").EnumerateArray())
        {
            int start=Id(g.GetProperty("StartNode")),ende=Id(g.GetProperty("EndNode"));
            bool stadt=j.GetProperty("Stadt").EnumerateArray().Any(e=>Id(e.GetProperty("StartNode"))==start||Id(e.GetProperty("EndNode"))==start);
            if(stadt) stadtknoten++;
            var c=g.GetProperty("Curve"); var a=P(c.GetProperty("A")); var d=P(c.GetProperty("D"));
            var kandidaten=new List<Netzerhalt.Teil<int>> { new() { Kante=Id(g.GetProperty("Entity")),Start=start,Ende=ende,
                Kurve=(a,P(c.GetProperty("B")),P(c.GetProperty("C")),d),
                PrefabGleich=g.GetProperty("Prefab").GetProperty("Name").GetString()=="PLT Zufahrtsgasse (Alley)",
                // Der Nutzerabzug enthaelt keine ConnectedEdge-Puffer. Die
                // Geometrieprobe setzt deren Zulassung explizit voraus.
                Verbunden=true,SeitenGleich=true } };
            bool gefunden=false;
            foreach(var piece in gassen)
                if(Netzerhalt.FindeKette(a.xz,piece.B,start,false,-1,kandidaten).Count==1) gefunden=true;
            pruefe(gefunden&&stadt,$"FM45: Gasse {Id(g.GetProperty("Entity"))} mit geplantem innerem Ende und identischem Stadtknoten bleibt stehen");
            if(gefunden&&stadt) erhalten++;
            pruefe(math.distance(XZ(g.GetProperty("StartNodePosition")),a.xz)<=.05f,"FM45: gemeinsamer Stadtknoten steht am Kurvenanfang");
        }
        Console.WriteLine($"FM45: {erhalten}/2 Gassen geometrisch erhalten, {stadtknoten}/2 bestehende Stadtknoten; ConnectedEdge/Spuren im Spiel offen.");
    }
}
