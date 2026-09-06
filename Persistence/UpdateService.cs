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
        sb.AppendLine($"$app = '{Ps(appDir)}'");
        sb.AppendLine($"$payload = '{Ps(payload)}'");
        sb.AppendLine($"$exe = '{Ps(exe)}'");
        sb.AppendLine("$logDir = Join-Path $env:LOCALAPPDATA 'GDM Amp Accessible\\Logs'");
        sb.AppendLine("New-Item -ItemType Directory -Force -Path $logDir | Out-Null");
        sb.AppendLine("$log = Join-Path $logDir ('actualizacion_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.log')");
        sb.AppendLine("function Log([string]$message) { Add-Content -LiteralPath $log -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff') + ' ' + $message) -Encoding UTF8 }");
        sb.AppendLine("function Wait-Or-Stop([int]$processId) {");
        sb.AppendLine("  if ($processId -le 0) { return }");
        sb.AppendLine("  $deadline = (Get-Date).AddSeconds(12)");
        sb.AppendLine("  while ((Get-Process -Id $processId -ErrorAction SilentlyContinue) -and ((Get-Date) -lt $deadline)) { Start-Sleep -Milliseconds 250 }");
        sb.AppendLine("  if (Get-Process -Id $processId -ErrorAction SilentlyContinue) {");
        sb.AppendLine("    Log ('El proceso anterior no termino a tiempo. Se fuerza cierre. PID ' + $processId)");
        sb.AppendLine("    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue");
        sb.AppendLine("    Start-Sleep -Milliseconds 900");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine("function Copy-WithRetry([string]$source, [string]$destination) {");
        sb.AppendLine("  for ($attempt = 1; $attempt -le 20; $attempt++) {");
        sb.AppendLine("    try { Copy-Item -LiteralPath $source -Destination $destination -Force -ErrorAction Stop; return }");
        sb.AppendLine("    catch { if ($attempt -eq 20) { throw }; Start-Sleep -Milliseconds 300 }");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine("try {");
        sb.AppendLine("  Log ('Inicio de actualizacion. Esperando PID ' + $PidToWait)");
        sb.AppendLine("  Wait-Or-Stop $PidToWait");
        sb.AppendLine("  Start-Sleep -Milliseconds 400");
        foreach (UpdateFileEntry file in manifest.Files)
        {
            string rel = NormalizeRelativePath(file.Path).Replace('\\', '/');
            sb.AppendLine($"  $src = Join-Path $payload '{Ps(rel)}'");
            sb.AppendLine($"  $dst = Join-Path $app '{Ps(rel)}'");
            sb.AppendLine("  New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null");
            sb.AppendLine("  Copy-WithRetry $src $dst");
            sb.AppendLine($"  Log 'Archivo actualizado: {Ps(rel)}'");
        }
        foreach (string delete in manifest.Delete ?? new())
        {
            string rel = NormalizeRelativePath(delete).Replace('\\', '/');
            sb.AppendLine($"  $old = Join-Path $app '{Ps(rel)}'");
            sb.AppendLine("  if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force -Recurse -ErrorAction Stop }");
            sb.AppendLine($"  Log 'Archivo eliminado: {Ps(rel)}'");
        }
        sb.AppendLine("  if (-not (Test-Path -LiteralPath $exe)) { throw ('No existe el ejecutable para reiniciar: ' + $exe) }");
        sb.AppendLine("  Log ('Iniciando Amp Accessible: ' + $exe)");
        sb.AppendLine("  $newProcess = Start-Process -FilePath $exe -WorkingDirectory $app -PassThru -ErrorAction Stop");
        sb.AppendLine("  Start-Sleep -Seconds 3");
        sb.AppendLine("  $newProcess.Refresh()");
        sb.AppendLine("  if ($newProcess.HasExited) { throw ('Amp Accessible se cerro durante el reinicio. Codigo ' + $newProcess.ExitCode) }");
        sb.AppendLine("  Log ('Reinicio confirmado. PID ' + $newProcess.Id)");
        sb.AppendLine("  Start-Sleep -Milliseconds 500");
        sb.AppendLine("  Remove-Item -LiteralPath $PSScriptRoot -Force -Recurse -ErrorAction SilentlyContinue");
        sb.AppendLine("  exit 0");
        sb.AppendLine("}");
        sb.AppendLine("catch {");
        sb.AppendLine("  try { Log ('ERROR: ' + using System.Diagnostics;
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
        string exe = Environment.ProcessPath ?? Path.Combine(appDir, "AmpAccessible.exe");
        var sb = new StringBuilder();
        sb.AppendLine("param([int]$PidToWait)");
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine("try { Wait-Process -Id $PidToWait -ErrorAction SilentlyContinue } catch {}");
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
        sb.AppendLine($"Start-Process -FilePath '{Ps(exe)}'");
        sb.AppendLine("Start-Sleep -Seconds 2");
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
.Exception.Message) } catch {}");
        sb.AppendLine("  try { Start-Process -FilePath 'notepad.exe' -ArgumentList ('\"' + $log + '\"') } catch {}");
        sb.AppendLine("  exit 1");
        sb.AppendLine("}");
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
