using System;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace RPChronicles.Data;

/// <summary>
/// Turns the game's territory id into a place name such as "The Goblet, Ul'dah".
/// </summary>
internal static class LocationNames
{
    public static string Describe(IDataManager data, IClientState client)
    {
        try
        {
            var sheet = data.GetExcelSheet<TerritoryType>();
            if (sheet == null || !sheet.TryGetRow(client.TerritoryType, out var territory))
                return "Unknown location";

            var area = Read(territory.PlaceName);
            var zone = Read(territory.PlaceNameZone);
            if (string.IsNullOrWhiteSpace(area))
                return string.IsNullOrWhiteSpace(zone) ? "Unknown location" : zone;

            if (string.IsNullOrWhiteSpace(zone) || string.Equals(area, zone, StringComparison.OrdinalIgnoreCase))
                return area;

            return $"{area}, {zone}";
        }
        catch
        {
            return "Unknown location";
        }
    }

    /// <summary>The character's home world, not the world they are visiting.</summary>
    public static string HomeWorldName(IPlayerState player) => ReadWorld(player.HomeWorld);

    public static string HomeWorldName(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player) =>
        ReadWorld(player.HomeWorld);

    private static string ReadWorld(Lumina.Excel.RowRef<World> world)
    {
        try
        {
            if (!world.IsValid)
                return string.Empty;

            return world.Value.Name.ExtractText().Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Read(RowRef<PlaceName> place)
    {
        if (!place.IsValid)
            return string.Empty;

        return place.Value.Name.ExtractText().Trim();
    }
}
