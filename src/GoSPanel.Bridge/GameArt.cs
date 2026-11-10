using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Windows.Storage;

namespace GoSPanel.Bridge;

/// <summary>
/// Arte de cada juego para la baldosa Perfil (maqueta design/perfil-arte.html, opción A): fondo («hero»), carátula e
/// icono. Primero el que Steam ya tiene en la consola (appcache\librarycache\&lt;id&gt;, sin internet); después el que
/// declaran los juegos de la app de Xbox (MicrosoftGame.config / AppxManifest.xml); si no hay, el icono del .exe. Se copia a
/// LocalState\art\&lt;juego&gt;\ (el widget lo lee como ms-appdata:///local/art/…) y
/// solo se busca una vez por juego.
/// </summary>
internal static class GameArt
{
    private static string Root => Path.Combine(ApplicationData.Current.LocalFolder.Path, "art");
    private static readonly object Lock = new();
    private static readonly HashSet<string> Working = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Arte guardado de un juego (por su .exe). Si aún no se ha buscado, se busca aparte y vuelve «pending».</summary>
    public static JsonObject Get(string exe, string? path)
    {
        var key = Key(exe);
        var folder = Path.Combine(Root, key);
        var result = new JsonObject { ["key"] = key };
        var found = false;
        foreach (var (name, file) in new[] { ("hero", "hero.jpg"), ("capsule", "capsule.jpg"), ("icon", "icon.png") })
        {
            if (!File.Exists(Path.Combine(folder, file))) continue;
            result[name] = "art/" + key + "/" + file;
            found = true;
        }
        var searched = File.Exists(Path.Combine(folder, "searched"));
        // Sin ruta no se puede buscar (perfil antiguo): se buscará cuando el juego vuelva a abrirse
        if (!searched && !string.IsNullOrEmpty(path))
        {
            lock (Lock)
            {
                if (Working.Add(key)) _ = Task.Run(() => Search(key, folder, path));
            }
            result["pending"] = true;
        }
        result["found"] = found;
        return result;
    }

    private static void Search(string key, string folder, string path)
    {
        try
        {
            Directory.CreateDirectory(folder);
            if (!FromSteam(folder, path) && !FromXbox(folder, path)) FromExe(folder, path);
            File.WriteAllText(Path.Combine(folder, "searched"), DateTime.Now.ToString("s"));
        }
        catch (Exception) { /* sin arte: la baldosa se queda con el icono de mando */ }
        finally
        {
            lock (Lock) Working.Remove(key);
        }
    }

    // ---------------------------------------------------------------------
    // Steam: el id del juego sale de su carpeta (steamapps\common\<carpeta>) y su appmanifest
    // ---------------------------------------------------------------------

    private static bool FromSteam(string folder, string path)
    {
        var match = Regex.Match(path, @"^(.*\\steamapps)\\common\\([^\\]+)", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        var steamapps = match.Groups[1].Value;
        var installDir = match.Groups[2].Value;

        string? appId = null;
        foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
        {
            var text = File.ReadAllText(manifest);
            var dir = Regex.Match(text, "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
            if (!dir.Equals(installDir, StringComparison.OrdinalIgnoreCase)) continue;
            appId = Regex.Match(text, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
            break;
        }
        if (string.IsNullOrEmpty(appId)) return false;

        // La caché de imágenes está en la carpeta de Steam (no en cada biblioteca)
        var steamRoot = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string)?.Replace('/', '\\')
                        ?? Path.GetDirectoryName(steamapps)!;
        var cache = Path.Combine(steamRoot, "appcache", "librarycache", appId);
        if (!Directory.Exists(cache)) return false;

        string? Find(string name) => Directory.EnumerateFiles(cache, name, SearchOption.AllDirectories).FirstOrDefault();
        var hero = Find("library_hero.jpg");
        var capsule = Find("library_header.jpg") ?? Find("library_capsule.jpg");
        // El icono es un .jpg con nombre de hash directamente en la carpeta del juego
        var icon = Directory.EnumerateFiles(cache, "*.jpg", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => Regex.IsMatch(Path.GetFileNameWithoutExtension(f), "^[0-9a-f]{40}$"));

        if (hero != null) File.Copy(hero, Path.Combine(folder, "hero.jpg"), true);
        if (capsule != null) File.Copy(capsule, Path.Combine(folder, "capsule.jpg"), true);
        if (icon != null)
        {
            using var image = Image.FromFile(icon);
            image.Save(Path.Combine(folder, "icon.png"), ImageFormat.Png);
        }
        else FromExe(folder, path);
        return hero != null || capsule != null || icon != null;
    }

    // ---------------------------------------------------------------------
    // App de Xbox / Game Pass: el juego trae su arte declarado en MicrosoftGame.config (GDK) o AppxManifest.xml
    // ---------------------------------------------------------------------

    private static bool FromXbox(string folder, string path)
    {
        // El archivo de configuración está en la carpeta del juego o unas carpetas más arriba (…\Content)
        for (var dir = Path.GetDirectoryName(path); dir != null && dir.Length > 3; dir = Path.GetDirectoryName(dir))
        {
            var config = Path.Combine(dir, "MicrosoftGame.config");
            var manifest = Path.Combine(dir, "AppxManifest.xml");
            string? text = File.Exists(config) ? File.ReadAllText(config) : File.Exists(manifest) ? File.ReadAllText(manifest) : null;
            if (text == null) continue;

            string? Attr(string name) => Regex.Match(text, name + "=\"([^\"]+)\"").Groups[1].Value is { Length: > 0 } v ? v : null;
            var icon = Resolve(dir, Attr("Square150x150Logo") ?? Attr("Square480x480Logo") ?? Attr("Square44x44Logo") ?? Attr("StoreLogo"));
            var hero = Resolve(dir, Attr("SplashScreenImage") ?? Regex.Match(text, "SplashScreen[^>]*Image=\"([^\"]+)\"").Groups[1].Value);
            if (icon == null && hero == null) return false;
            if (icon != null) SaveAs(icon, Path.Combine(folder, "icon.png"), ImageFormat.Png);
            if (hero != null)
            {
                SaveAs(hero, Path.Combine(folder, "hero.jpg"), ImageFormat.Jpeg);
                SaveAs(hero, Path.Combine(folder, "capsule.jpg"), ImageFormat.Jpeg);
            }
            return true;
        }
        return false;
    }

    /// <summary>Ruta real de una imagen del manifiesto: puede estar con calificadores (logo.scale-200.png…).</summary>
    private static string? Resolve(string dir, string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return null;
        var full = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full)) return full;
        var parent = Path.GetDirectoryName(full)!;
        if (!Directory.Exists(parent)) return null;
        // La de más resolución entre las variantes (escala o tamaño)
        return Directory.EnumerateFiles(parent, Path.GetFileNameWithoutExtension(full) + ".*" + Path.GetExtension(full))
            .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
    }

    private static void SaveAs(string source, string target, ImageFormat format)
    {
        using var image = Image.FromFile(source);
        image.Save(target, format);
    }

    // ---------------------------------------------------------------------
    // Icono del propio ejecutable (juegos de fuera de Steam)
    // ---------------------------------------------------------------------

    private static void FromExe(string folder, string path)
    {
        if (!File.Exists(path)) return;
        using var icon = Icon.ExtractAssociatedIcon(path);
        if (icon == null) return;
        using var bitmap = icon.ToBitmap();
        bitmap.Save(Path.Combine(folder, "icon.png"), ImageFormat.Png);
    }

    private static string Key(string exe) =>
        Regex.Replace(Path.GetFileNameWithoutExtension(exe).ToLowerInvariant(), "[^a-z0-9_.-]", "_");
}
