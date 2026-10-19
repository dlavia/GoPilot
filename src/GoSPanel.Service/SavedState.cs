using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoSPanel.Service;

/// <summary>
/// Últimos ajustes pedidos por el usuario, guardados en %ProgramData%\GoSPanel\state.json para poder
/// reaplicarlos al reanudar de la suspensión (el firmware a veces vuelve a sus valores de fábrica).
/// </summary>
internal static class SavedState
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GoSPanel", "state.json");
    private static readonly object Lock = new();

    public static JsonObject Load()
    {
        lock (Lock)
        {
            try
            {
                if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject state)
                    return state;
            }
            catch (Exception) { /* archivo dañado: se empieza de cero */ }
            return new JsonObject { ["reapplyOnResume"] = true };
        }
    }

    public static void Update(Action<JsonObject> change)
    {
        lock (Lock)
        {
            var state = Load();
            change(state);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, state.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public static bool ReapplyOnResume => Load()["reapplyOnResume"]?.GetValue<bool>() ?? true;
}
