using System.Text.Json.Nodes;
using Windows.Management.Deployment;

namespace GoSPanel.Bridge;

/// <summary>
/// Widget de Legion Space en Game Bar (el icono fijo a la izquierda de la barra). Viene aprovisionado para todo
/// el equipo, así que se puede quitar solo para este usuario y volver a registrar después sin descargar nada.
/// Se quita al desactivar Legion Space y vuelve al activarlo.
/// </summary>
internal static class LegionWidget
{
    private const string FamilyName = "LegionGoGameBarWidget_ra2g5j82mn5h8";

    public static JsonObject Get()
    {
        var pm = new PackageManager();
        var installed = pm.FindPackagesForUser("", FamilyName).Any();
        return new JsonObject { ["visible"] = installed };
    }

    public static async Task<JsonObject> SetAsync(bool visible)
    {
        var pm = new PackageManager();
        var package = pm.FindPackagesForUser("", FamilyName).FirstOrDefault();
        if (!visible && package != null)
        {
            var result = await pm.RemovePackageAsync(package.Id.FullName);
            if (result.ExtendedErrorCode != null) throw new InvalidOperationException(result.ErrorText);
        }
        else if (visible && package == null)
        {
            // Sigue aprovisionado en el equipo: basta con volver a registrarlo para el usuario.
            var result = await pm.RegisterPackageByFamilyNameAsync(FamilyName, null, DeploymentOptions.None, null, null);
            if (result.ExtendedErrorCode != null) throw new InvalidOperationException(result.ErrorText);
        }
        return Get();
    }
}
