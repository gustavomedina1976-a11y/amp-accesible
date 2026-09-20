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

    // Python 3.12 queda fijado porque NAM 0.13 requiere Python >= 3.10
    // y evita depender del "latest" de Miniconda, que puede cambiar de Python.
    // Se usan dos endpoints oficiales de Anaconda para tolerar HTTP 403
    // o bloqueos temporales de uno de los servidores.
    private static readonly string[] MinicondaUrls =
    {
        "https://pro.anaconda.com/miniconda/Miniconda3-py312_26.7.1-0-Windows-x86_64.exe",
        "https://repo.anaconda.com/miniconda/Miniconda3-py312_26.7.1-0-Windows-x86_64.exe"
    };

    private const string MinicondaSha256 =
        "741c068509955d6fe0171613e3cdb76e12e74c87ed8236672197f01f227cd022";

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
            progress?.Report("Descargando Miniconda oficial Python 3.12 para el entrenador NAM.");
            string installer = Path.Combine(Root, "Miniconda3-py312-Windows-x86_64.exe");
            await DownloadMinicondaAsync(installer, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report("Instalando Miniconda Python 3.12 de forma local para Amp Accessible.");
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
        {
            string[] lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string userErrorMarker = lines.LastOrDefault(line =>
                line.StartsWith("GDM_USER_ERROR=", StringComparison.Ordinal)) ?? string.Empty;
            if (userErrorMarker.Length > "GDM_USER_ERROR=".Length)
                throw new InvalidOperationException(userErrorMarker["GDM_USER_ERROR=".Length..].Trim());

            throw new InvalidOperationException("El entrenador NAM terminó con error. " + result.Output);
        }

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

    private static async Task DownloadMinicondaAsync(
        string destination, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        foreach (string url in MinicondaUrls)
        {
            try
            {
                string host = new Uri(url).Host;
                progress?.Report($"Intentando descarga oficial de Miniconda desde {host}.");
                if (File.Exists(destination)) File.Delete(destination);

                await DownloadAsync(url, destination, progress, cancellationToken).ConfigureAwait(false);
                VerifyMinicondaInstaller(destination);

                progress?.Report($"Miniconda descargado y verificado desde {host}.");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                try
                {
                    if (File.Exists(destination)) File.Delete(destination);
                }
                catch { }

                string host;
                try { host = new Uri(url).Host; }
                catch { host = "servidor oficial"; }

                progress?.Report(
                    $"No se pudo descargar Miniconda desde {host}: {ex.Message}. Probando servidor alternativo.");
            }
        }

        throw new InvalidOperationException(
            "No se pudo descargar Miniconda desde ninguno de los dos servidores oficiales de Anaconda. " +
            "Revise firewall, antivirus o acceso de red a pro.anaconda.com y repo.anaconda.com. " +
            (lastError is null ? string.Empty : $"Último error: {lastError.Message}"),
            lastError);
    }

    private static void VerifyMinicondaInstaller(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 80L * 1024L * 1024L)
            throw new InvalidDataException(
                $"La descarga de Miniconda está incompleta. Tamaño recibido: {(info.Exists ? info.Length : 0)} bytes.");

        using (var stream = File.OpenRead(path))
        {
            int first = stream.ReadByte();
            int second = stream.ReadByte();
            if (first != 'M' || second != 'Z')
                throw new InvalidDataException("El archivo descargado no es un ejecutable Windows válido.");
        }

        using var sha = System.Security.Cryptography.SHA256.Create();
        using var input = File.OpenRead(path);
        string actual = Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant();
        if (!actual.Equals(MinicondaSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"SHA-256 de Miniconda incorrecto. Esperado {MinicondaSha256}; recibido {actual}.");
    }

    private static async Task DownloadAsync(
        string url, string destination, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression =
                System.Net.DecompressionMethods.GZip |
                System.Net.DecompressionMethods.Deflate |
                System.Net.DecompressionMethods.Brotli
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(45)
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AmpAccessible/2.41.88 NAMTrainer");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "es-AR,es;q=0.9,en;q=0.8");

        using HttpResponseMessage response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}) al descargar desde {new Uri(url).Host}.");
        }

        long? total = response.Content.Headers.ContentLength;
        await using Stream source =
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, true);

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

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
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
import numpy as np
from nam.train import core
from nam.models.metadata import UserMetadata

input_path = sys.argv[1]
output_path = sys.argv[2]
destination = sys.argv[3]
model_name = sys.argv[4]
epochs = int(sys.argv[5])

def dbfs(value):
    value = float(abs(value))
    if value <= 1.0e-12:
        return -120.0
    return 20.0 * np.log10(value)

def user_fail(message):
    print("GDM_USER_ERROR=" + message, flush=True)
    raise RuntimeError(message)

def estimate_v3_latency_accessible(output_path):
    y = core._wav_to_np(output_path)
    if len(y) < 562000:
        user_fail(
            "La captura NAM es demasiado corta para medir los pulsos de calibración V3. "
            "Repita la captura completa."
        )

    expected = (504000, 552000)
    noise_start, noise_end = 492000, 498000
    lookahead = 1000
    lookback = 10000

    noise = np.abs(np.asarray(y[noise_start:noise_end], dtype=np.float64))
    background_peak = float(np.max(noise)) if len(noise) else 0.0
    background_rms = float(np.sqrt(np.mean(np.square(noise)))) if len(noise) else 0.0
    global_peak = float(np.max(np.abs(y))) if len(y) else 0.0
    margin = max(1.0e-5, background_peak * 0.05)
    threshold = max(background_peak + margin, background_rms * 6.0)

    delays = []
    pulse_peaks = []
    scans = []

    for position in expected:
        start = max(0, position - lookahead)
        stop = min(len(y), position + lookback)
        signed = np.asarray(y[start:stop], dtype=np.float64)
        window = np.abs(signed)
        scans.append(signed)
        if len(window) == 0:
            user_fail("No se pudo analizar la ventana de pulsos de calibración NAM.")

        pulse_peak = float(np.max(window))
        pulse_peaks.append(pulse_peak)
        triggered = np.where(window > threshold)[0]
        if len(triggered) == 0:
            user_fail(
                "No se detectó el frente de uno de los pulsos V3 por encima del ruido. "
                f"Umbral {dbfs(threshold):.1f} dBFS; pico del pulso {dbfs(pulse_peak):.1f} dBFS; "
                f"ruido RMS {dbfs(background_rms):.1f} dBFS. "
                "Repita la captura completa con atenuación digital 0 dB y ajuste el nivel con Output físico o caja de reamp."
            )
        first = int(triggered[0])
        delays.append(first - lookahead)

    spread = max(delays) - min(delays)
    weakest_peak = min(pulse_peaks)
    snr_ratio = weakest_peak / max(background_rms, 1.0e-12)
    snr_db = 20.0 * np.log10(max(snr_ratio, 1.0e-12))

    if global_peak >= 0.999:
        user_fail("La captura presenta clipping. Repita la captura bajando la ganancia de retorno.")

    if spread > 20:
        average_scan = np.mean(np.stack(scans), axis=0)
        average_triggered = np.where(np.abs(average_scan) > threshold)[0]
        average_delay = None if len(average_triggered) == 0 else int(average_triggered[0]) - lookahead
        extra = "" if average_delay is None else f"; promedio detectado {average_delay} muestras"
        user_fail(
            "Los primeros frentes de los dos pulsos V3 no coinciden con suficiente precisión. "
            f"Se midieron {delays[0]} y {delays[1]} muestras; diferencia {spread} muestras{extra}. "
            "No se forzará una latencia. Repita la captura completa con 0 dB digital y sin modificar controles durante la toma."
        )

    print(
        "GDM_STATUS=Calibración accesible V3 por primer frente: "
        f"latencias {delays[0]} y {delays[1]} muestras; diferencia {spread}; "
        f"umbral {dbfs(threshold):.1f} dBFS; ruido RMS {dbfs(background_rms):.1f} dBFS; "
        f"relación {snr_db:.1f} dB.",
        flush=True,
    )

    latency = int(round(sum(delays) / len(delays))) - 1
    return latency

Path(destination).mkdir(parents=True, exist_ok=True)
print("GDM_STATUS=Validando entrada y salida", flush=True)

input_version, strong_match = core._detect_input_version(input_path)

latency_analysis = core._analyze_latency(
    None,
    input_version,
    input_path,
    output_path,
    silent=True,
    _override_suppress_plots=True,
)

latency = latency_analysis.calibration.recommended

if latency is not None:
    print(
        f"GDM_STATUS=Latencia NAM detectada por el método oficial: {latency} muestras.",
        flush=True,
    )
elif input_version.major == 3:
    print(
        "GDM_STATUS=El método oficial no detectó los pulsos. "
        "Probando medición accesible V3.",
        flush=True,
    )
    latency = estimate_v3_latency_accessible(output_path)
    print(
        f"GDM_STATUS=Latencia accesible validada: {latency} muestras. "
        "No se abrirán gráficos externos.",
        flush=True,
    )
else:
    user_fail(
        "NAM no pudo detectar automáticamente la latencia y esta versión de input "
        "todavía no tiene medición accesible alternativa. Repita la captura con mayor nivel de retorno."
    )

print(
    f"GDM_STATUS=Entrenamiento NAM iniciado: {model_name}. "
    f"Épocas: {epochs}. Latencia: {latency} muestras.",
    flush=True,
)

result = core.train(
    input_path,
    output_path,
    destination,
    epochs=epochs,
    latency=latency,
    silent=True,
    save_plot=False,
    modelname=model_name,
    ignore_checks=False,
    local=False,
)

if result is None or result.model is None:
    user_fail(
        "NAM rechazó la captura durante sus controles de calidad. "
        "Revise nivel, ruido y que no haya gate, chorus, delay o reverb en la cadena capturada."
    )

print("GDM_STATUS=Exportando modelo NAM", flush=True)
user_metadata = UserMetadata()
result.model.net.export(
    destination,
    basename=model_name,
    user_metadata=user_metadata,
    other_metadata={"training": result.metadata.model_dump()},
)

files = sorted(
    Path(destination).rglob("*.nam"),
    key=lambda p: p.stat().st_mtime,
    reverse=True,
)

if not files:
    user_fail("El entrenamiento finalizó pero no apareció un archivo punto NAM.")

print("GDM_NAM_RESULT=" + str(files[0]), flush=True)
""";
}
