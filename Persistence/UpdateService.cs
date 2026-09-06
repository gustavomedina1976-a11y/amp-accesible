using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GDMAmpAccessible.Persistence;

internal sealed class UpdateChannelConfig
{
    public string ManifestUrl { get; set; } = string.Empty;
}

internal sealed class UpdateChannelManifest
{
    public string LatestVersion { get; set; } = string.Empty;
    public List<UpdatePackageLink> Packages { get; set; } = new();
}

internal sealed class UpdatePackageLink
{
    public string FromVersion { get; set; } = string.Empty;
    public string ToVersion { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed class UpdatePackageManifest
{
    public string FromVersion { get; set; } = string.Empty;
    public string ToVersion { get; set; } = string.Empty;
    public List<UpdateFileEntry> Files { get; set; } = new();
    public List<string> Delete { get; set; } = new();
}

internal sealed class UpdateFileEntry
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed record OnlineUpdateResult(bool Available, string Message, string? PackagePath = null);
internal sealed record PreparedUpdate(string StagingFolder, string ScriptPath, string TargetVersion);

internal static class UpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static string ChannelConfigPath => Path.Combine(AppContext.BaseDirectory, "update-channel.json");

    public static async Task<OnlineUpdateResult> CheckAndDownloadOnlineAsync(CancellationToken cancellationToken = default)
    {
        UpdateChannelConfig? config = null;
        try
        {
            if (File.Exists(ChannelConfigPath))
                config = JsonSerializer.Deserialize<UpdateChannelConfig>(await File.ReadAllTextAsync(ChannelConfigPath, cancellationToken), JsonOptions);
        }
        catch (Exception ex)
        {
            return new(false, $"No se pudo leer la configuración de actualizaciones: {ex.Message}");
        }

        if (config is null || string.IsNullOrWhiteSpace(config.ManifestUrl))
            return new(false, "El sistema de actualización incremental está instalado, pero todavía no hay un servidor de actualizaciones configurado. Puede instalar un paquete .gdmupdate manualmente.");

        if (!Uri.TryCreate(config.ManifestUrl, UriKind.Absolute, out Uri? manifestUri) || manifestUri.Scheme != Uri.UriSchemeHttps)
            return new(false, "La dirección del canal de actualización no es HTTPS o no es válida.");

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            string json = await client.GetStringAsync(manifestUri, cancellationToken);
            UpdateChannelManifest? manifest = JsonSerializer.Deserialize<UpdateChannelManifest>(json, JsonOptions);
            if (manifest is null) return new(false, "El manifiesto de actualización no es válido.");

            if (!Version.TryParse(CurrentVersion, out Version? current)) current = new Version(0, 0, 0);
            if (!Version.TryParse(manifest.LatestVersion, out Version? latest)) return new(false, "El servidor no indicó una versión válida.");
            if (latest <= current) return new(false, $"Amp Accessible {CurrentVersion} ya está actualizado.");

            UpdatePackageLink? package = manifest.Packages.FirstOrDefault(p =>
                string.Equals(NormalizeVersion(p.FromVersion), NormalizeVersion(CurrentVersion), StringComparison.OrdinalIgnoreCase));
            if (package is null || string.IsNullOrWhiteSpace(package.Url))
                return new(false, $"Existe Amp Accessible {manifest.LatestVersion}, pero no hay un paquete incremental directo desde {CurrentVersion}. Use el instalador completo.");

            if (!Uri.TryCreate(package.Url, UriKind.Absolute, out Uri? packageUri) || packageUri.Scheme != Uri.UriSchemeHttps)
                return new(false, "La dirección del paquete de actualización no es HTTPS o no es válida.");

            string tempPath = Path.Combine(Path.GetTempPath(), $"AmpAccessible_{package.ToVersion}_{Guid.NewGuid():N}.gdmupdate");
            byte[] bytes = await client.GetByteArrayAsync(packageUri, cancellationToken);
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);

            string actual = ComputeSha256(tempPath);
            if (!string.IsNullOrWhiteSpace(package.Sha256) && !actual.Equals(package.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempPath);
                return new(false, "La descarga no pasó la verificación SHA-256. No se instaló nada.");
            }

            return new(true, $"Actualización {package.ToVersion} descargada. Sólo se descargaron los archivos modificados del paquete incremental.", tempPath);
        }
        catch (Exception ex)
        {
            return new(false, $"No se pudo comprobar la actualización: {ex.Message}");
        }
    }

    public static PreparedUpdate PreparePackage(string packagePath)
    {
        if (!File.Exists(packagePath)) throw new FileNotFoundException("No se encontró el paquete de actualización.", packagePath);

        string root = Path.Combine(Path.GetTempPath(), "GDM_Amp_Update_" + Guid.NewGuid().ToString("N"));
        string payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);

        UpdatePackageManifest manifest;
        using (ZipArchive archive = ZipFile.OpenRead(packagePath))
        {
            ZipArchiveEntry? manifestEntry = archive.GetEntry("update.json");
            if (manifestEntry is null) throw new InvalidDataException("El paquete no contiene update.json.");
            using var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8, true);
            manifest = JsonSerializer.Deserialize<UpdatePackageManifest>(reader.ReadToEnd(), JsonOptions)
                ?? throw new InvalidDataException("update.json no es válido.");

            if (!string.Equals(NormalizeVersion(manifest.FromVersion), NormalizeVersion(CurrentVersion), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Este paquete actualiza desde {manifest.FromVersion}, pero está ejecutando {CurrentVersion}.");
            if (!Version.TryParse(manifest.ToVersion, out Version? target) ||
                !Version.TryParse(CurrentVersion, out Version? current) || target <= current)
                throw new InvalidDataException("La versión de destino del paquete no es válida o no es más nueva.");

            foreach (UpdateFileEntry file in manifest.Files)
            {
                string rel = NormalizeRelativePath(file.Path);
                ZipArchiveEntry? entry = archive.GetEntry("payload/" + rel.Replace('\\', '/'));
                if (entry is null) throw new InvalidDataException($"Falta el archivo {rel} en el paquete.");
                string dest = SafeCombine(payload, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, true);
                string hash = ComputeSha256(dest);
                if (string.IsNullOrWhiteSpace(file.Sha256) || !hash.Equals(file.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"El archivo {rel} no pasó la verificación SHA-256.");
            }
        }

        string script = Path.Combine(root, "aplicar_actualizacion.ps1");
        File.WriteAllText(script, BuildApplyScript(payload, manifest), new UTF8Encoding(false));
        return new(root, script, manifest.ToVersion);
    }

    public static void LaunchPreparedUpdate(PreparedUpdate prepared, int processId)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{prepared.ScriptPath}\" -PidToWait {processId}",
            UseShellExecute = true,
            WorkingDirectory = prepared.StagingFolder
        };
        Process.Start(psi);
    }

    private static string BuildApplyScript(string payload, UpdatePackageManifest manifest)
    {
        string appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string exe = Path.Combine(appDir, "AmpAccessible.exe");
        var sb = new StringBuilder();
        sb.AppendLine("param([int]$PidToWait)");
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("$deadline = (Get-Date).AddSeconds(12)");
        sb.AppendLine("while ((Get-Process -Id $PidToWait -ErrorAction SilentlyContinue) -and ((Get-Date) -lt $deadline)) { Start-Sleep -Milliseconds 250 }");
        sb.AppendLine("if (Get-Process -Id $PidToWait -ErrorAction SilentlyContinue) { Stop-Process -Id $PidToWait -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 800 }");
        sb.AppendLine("Start-Sleep -Milliseconds 600");
        sb.AppendLine($"$app = '{Ps(appDir)}'");
        sb.AppendLine($"$payload = '{Ps(payload)}'");
        foreach (UpdateFileEntry file in manifest.Files)
        {
            string rel = NormalizeRelativePath(file.Path).Replace('\\', '/');
            sb.AppendLine($"$src = Join-Path $payload '{Ps(rel)}'");
            sb.AppendLine($"$dst = Join-Path $app '{Ps(rel)}'");
            sb.AppendLine("New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null");
            sb.AppendLine("Copy-Item -LiteralPath $src -Destination $dst -Force");
        }
        foreach (string delete in manifest.Delete ?? new())
        {
            string rel = NormalizeRelativePath(delete).Replace('\\', '/');
            sb.AppendLine($"$old = Join-Path $app '{Ps(rel)}'");
            sb.AppendLine("if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force -Recurse }");
        }
        sb.AppendLine($"$newProcess = Start-Process -FilePath '{Ps(exe)}' -WorkingDirectory '{Ps(appDir)}' -PassThru");
        sb.AppendLine("Start-Sleep -Seconds 3");
        sb.AppendLine("$newProcess.Refresh()");
        sb.AppendLine("if ($newProcess.HasExited) { throw ('Amp Accessible se cerro durante el reinicio. Codigo ' + $newProcess.ExitCode) }");
        sb.AppendLine("Remove-Item -LiteralPath $PSScriptRoot -Force -Recurse -ErrorAction SilentlyContinue");
        return sb.ToString();
    }

    private static string NormalizeVersion(string value)
    {
        if (Version.TryParse(value, out Version? version)) return version.ToString(3);
        return value?.Trim() ?? string.Empty;
    }

    private static string NormalizeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Ruta vacía en paquete de actualización.");
        string rel = value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(rel) || rel.Split(Path.DirectorySeparatorChar).Any(p => p == ".."))
            throw new InvalidDataException("El paquete contiene una ruta no permitida.");
        return rel;
    }

    private static string SafeCombine(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Ruta fuera del paquete.");
        return full;
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Ps(string value) => value.Replace("'", "''");
}
