using MiddlewareApp.Core.Models;

namespace MiddlewareApp;

/// <summary>In-memory wizard state (the app has no URL routing — spec §6).</summary>
public static class AppState
{
    public static string BusinessCode = "";
    public static List<Location> Locations = new();
    public static Location? SelectedLocation;
    public static Device? SelectedDevice;

    public static void Reset()
    {
        BusinessCode = "";
        Locations = new List<Location>();
        SelectedLocation = null;
        SelectedDevice = null;
    }
}
