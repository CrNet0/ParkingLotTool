using Game.Prefabs;

namespace ParkingLotTool.Tools
{
    /** Gemeinsame Werte und der einzige Katalog der befahrbaren Innenwege. */
    internal static class ParkingLotFahrregeln
    {
        internal const float TempoKmh = 25f;

        // Messung 2026-10-02: Tiled Street Pathfind Verhalten 1/Komfort 0,1;
        // Road Pathfind nur Geld 0,01; Invisible Path Pathfind 0,01/0,01/0,01
        // (Verhalten/Geld/Komfort), Alley Geld 0,01/Komfort 0,01. Unter der
        // Fussgaengerzone bleiben: auch PARKER zahlen je Meter, sonst verliert
        // die Parkplatzwahl gegen Strassenrandparken. Keine Zutrittssperre.
        internal static readonly PathfindCostInfo Fahrkosten = new(0f, 0.25f, 0.01f, 0.05f);

        internal static readonly (string Quelle, float Breite)[] Wege =
        {
            ("Invisible Car Path - 1xTwoway", 3f),
            ("Invisible Road Path - 1xTwoway", 4f),
            ("Invisible Car Path - 2xTwoway", 6f),
            ("Invisible Road Path - 2xTwoway", 7f),
            ("Invisible Road Path - 1xOneway", 4f),
        };

        // Namen sind Spielstandformat. Auch Unterprefabs nie umbenennen.
        internal static string Wegname(string quelle) => "PLT " + quelle;
        internal static string Teilname(string netz, PrefabBase quelle)
            => netz + " :: " + quelle.GetType().Name + " (" + quelle.name + ")";

        internal static bool IstTeilname(string name)
            => name.StartsWith("PLT ", System.StringComparison.Ordinal)
               && name.Contains(" :: ");

        internal static bool IstFahrweg(string quelle)
        {
            foreach (var weg in Wege) if (weg.Quelle == quelle) return true;
            return false;
        }
    }
}
