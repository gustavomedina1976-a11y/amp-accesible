using System.Diagnostics;
using System.Text;

namespace GDMAmpAccessible;

internal sealed record NamTrainerInfo(bool Installed, string PythonPath, string Version, bool CudaAvailable);

internal sealed class NamTrainerService
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GDMAmpAccessible", "NAMTrainer");
    private static readonly string MinicondaRoot = Path.Combine(Root, "Miniconda3");
    public static string PythonPath => Path.Combine(MinicondaRoot, "python.exe");
    public static bool IsInstalled => File.Exists(PythonPath);

    private const string MinicondaUrl =
        "https://repo.anaconda.com/miniconda/Miniconda3-latest-Windows-x86_64.exe";

    public async Task<NamTrainerInfo> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
            return new NamTrainerInfo(false, PythonPath, "no instalado", false);

        ProcessResult check = await RunProcessAsync(
            PythonPath,
            new[] { "-c", "import nam,torch; print(getattr(nam,'__version__','desconocida')); print('CUDA='+str(torch.cuda.is_available()))" },
            null,
            cancellationToken).ConfigureAwait(false);

        if (check.ExitCode != 0)
            return new NamTrainerInfo(false, PythonPath, "instalación incompleta", false);

        string[] lines = check.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        string version = lines.FirstOrDefault(line => !line.StartsWith("CUDA=", StringComparison.OrdinalIgnoreCase))
            ?? "desconocida";
        bool cuda = lines.Any(line => line.Trim().Equals("CUDA=True", StringComparison.OrdinalIgnoreCase));
        return new NamTrainerInfo(true, PythonPath, version.Trim(), cuda);
    }

    public async Task<NamTrainerInfo> InstallOrUpdateAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Root);

        if (!File.Exists(PythonPath))
        {
            progress?.Report("Descargando Miniconda oficial para el entrenador NAM.");
            string installer = Path.Combine(Root, "Miniconda3-latest-Windows-x86_64.exe");
            await DownloadAsync(MinicondaUrl, installer, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report("Instalando Miniconda de forma local para Amp Accessible.");
            ProcessResult install = await RunProcessAsync(
                installer,
                new[] { "/InstallationType=JustMe", "/RegisterPython=0", "/AddToPath=0", "/S", "/D=" + MinicondaRoot },
                progress,
                cancellationToken).ConfigureAwait(false);

            if (install.ExitCode != 0 || !File.Exists(PythonPath))
                throw new InvalidOperationException(
                    $"No se pudo instalar Miniconda para NAM. Código {install.ExitCode}. {install.Output}");
        }

        progress?.Report("Actualizando pip del entrenador NAM.");
        ProcessResult pip = await RunProcessAsync(
            PythonPath,
            new[] { "-m", "pip", "install", "--upgrade", "pip" },
            progress,
            cancellationToken).ConfigureAwait(false);
        if (pip.ExitCode != 0)
            throw new InvalidOperationException("No se pudo actualizar pip para NAM. " + pip.Output);

        progress?.Report("Instalando o actualizando Neural Amp Modeler oficial.");
        ProcessResult nam = await RunProcessAsync(
            PythonPath,
            new[] { "-m", "pip", "install", "--upgrade", "neural-amp-modeler" },
            progress,
            cancellationToken).ConfigureAwait(false);
        if (nam.ExitCode != 0)
            throw new InvalidOperationException("No se pudo instalar Neural Amp Modeler. " + nam.Output);

        NamTrainerInfo info = await CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!info.Installed)
            throw new InvalidOperationException("NAM terminó de instalarse pero la comprobación final falló.");
        return info;
    }

    public async Task<string> TrainAsync(
        string captureFolder,
        string modelName,
        int epochs,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!IsInstalled)
            throw new InvalidOperationException("El entrenador NAM oficial todavía no está instalado.");

        string input = Path.Combine(captureFolder, "input_original.wav");
        string output = Path.Combine(captureFolder, "output_capturado.wav");
        if (!File.Exists(input))
            throw new FileNotFoundException("La carpeta no contiene input_original.wav.", input);
        if (!File.Exists(output))
            throw new FileNotFoundException("La carpeta no contiene output_capturado.wav.", output);

        string safeName = SafeName(modelName);
        string destination = Path.Combine(captureFolder, "Modelo_NAM");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(Root);

        string script = Path.Combine(Root, "gdm_train_nam.py");
        await File.WriteAllTextAsync(script, PythonScript, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);

        progress?.Report($"Entrenamiento NAM iniciado: {safeName}. Épocas: {epochs}.");

        ProcessResult result = await RunProcessAsync(
            PythonPath,
            new[]
            {
                script, input, output, destination, safeName,
                Math.Clamp(epochs, 10, 300).ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            progress,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException("El entrenador NAM terminó con error. " + result.Output);

        string marker = result.Output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.StartsWith("GDM_NAM_RESULT=", StringComparison.Ordinal))
            ?? string.Empty;

        string path = marker.StartsWith("GDM_NAM_RESULT=", StringComparison.Ordinal)
            ? marker["GDM_NAM_RESULT=".Length..].Trim()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            path = Directory.EnumerateFiles(destination, "*.nam", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException("El entrenamiento terminó pero no se encontró el archivo .nam generado.");

        progress?.Report($"Modelo NAM generado: {path}");
        return path;
    }

    private static async Task DownloadAsync(
        string url, string destination, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using HttpResponseMessage response = await client.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);

        byte[] buffer = new byte[1024 * 128];
        long written = 0;
        int lastPercent = -1;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0) break;
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;
            if (total is > 0)
            {
                int percent = (int)Math.Clamp(written * 100L / total.Value, 0, 100);
                if (percent >= lastPercent + 10)
                {
                    lastPercent = percent;
                    progress?.Report($"Descarga Miniconda: {percent} por ciento.");
                }
            }
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName, IEnumerable<string> arguments, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Root
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var output = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (output) output.AppendLine(e.Data);
            if (e.Data.Contains("Epoch", StringComparison.OrdinalIgnoreCase) ||
                e.Data.Contains("training", StringComparison.OrdinalIgnoreCase) ||
                e.Data.Contains("checks", StringComparison.OrdinalIgnoreCase) ||
                e.Data.StartsWith("GDM_", StringComparison.Ordinal))
                progress?.Report(e.Data.Trim());
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (output) output.AppendLine(e.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException($"No se pudo iniciar {fileName}.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        string text;
        lock (output) text = output.ToString();
        return new ProcessResult(process.ExitCode, text);
    }

    private static string SafeName(string? value)
    {
        string text = string.IsNullOrWhiteSpace(value) ? "Modelo_NAM" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars()) text = text.Replace(invalid, '_');
        return text.Length <= 60 ? text : text[..60];
    }

    private sealed record ProcessResult(int ExitCode, string Output);

    private const string PythonScript = """
import sys
from pathlib import Path
from nam.train import core
from nam.models.metadata import UserMetadata

input_path = sys.argv[1]
output_path = sys.argv[2]
destination = sys.argv[3]
model_name = sys.argv[4]
epochs = int(sys.argv[5])

Path(destination).mkdir(parents=True, exist_ok=True)
print("GDM_STATUS=Validando entrada y salida")

result = core.train(
    input_path,
    output_path,
    destination,
    epochs=epochs,
    latency=None,
    silent=True,
    save_plot=False,
    modelname=model_name,
    ignore_checks=False,
    local=False,
)

if result is None or result.model is None:
    raise RuntimeError("NAM no produjo un modelo. Revise las validaciones de la captura.")

print("GDM_STATUS=Exportando modelo NAM")
user_metadata = UserMetadata()
result.model.net.export(
    destination,
    basename=model_name,
    user_metadata=user_metadata,
    other_metadata={"training": result.metadata.model_dump()},
)

files = sorted(Path(destination).rglob("*.nam"), key=lambda p: p.stat().st_mtime, reverse=True)
if not files:
    raise RuntimeError("El entrenamiento finalizó pero no apareció un archivo .nam.")

print("GDM_NAM_RESULT=" + str(files[0]))
""";
}
