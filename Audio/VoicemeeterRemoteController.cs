using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GDMAmpAccessible.Audio;

/// <summary>
/// Integración mínima con la API remota oficial de VB-Audio VoiceMeeter.
/// La DLL NO se distribuye con Amp Accessible: se carga la copia instalada por VoiceMeeter,
/// tal como recomienda VB-Audio para mantener compatibilidad con la versión instalada.
/// </summary>
internal sealed class VoicemeeterRemoteController : IDisposable
{
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}";

    private IntPtr _module;
    private bool _loggedIn;
    private LoginDelegate? _login;
    private LogoutDelegate? _logout;
    private RunVoicemeeterDelegate? _runVoicemeeter;
    private GetVoicemeeterTypeDelegate? _getVoicemeeterType;
    private IsParametersDirtyDelegate? _isParametersDirty;
    private GetParameterFloatDelegate? _getParameterFloat;
    private SetParameterFloatDelegate? _setParameterFloat;
    private GetLevelDelegate? _getLevel;

    public string LastStatus { get; private set; } = "VoiceMeeter todavía no comprobado.";
    public int ConnectedType { get; private set; }
    public string ConnectedEdition => EditionName(ConnectedType);
    public bool IsInstalled => FindInstallFolder() is not null;
    public bool IsConnected => ConnectedType is >= 1 and <= 3;

    public VoicemeeterSetupResult PreparePrimaryVirtualInputForVideoCall(bool monoCenteredVoice = false, float gainDb = 0f)
    {
        if (!EnsureApiLoaded(out string loadMessage))
            return Fail(loadMessage);

        if (!EnsureServerRunning(out int type, out string serverMessage))
            return Fail(serverMessage);

        ConnectedType = type;
        int strip = PrimaryVirtualStripIndex(type);
        int b1Bus = PrimaryB1BusIndex(type);
        if (strip < 0 || b1Bus < 0)
            return Fail($"Edición de VoiceMeeter no reconocida, tipo {type}.");

        try
        {
            // Amp Accessible ya realiza el monitoreo local por la Focusrite. Por eso quitamos
            // los buses A de la entrada virtual y dejamos exclusivamente B1 para la app de llamada.
            SetRequired($"Strip[{strip}].Mute", 0f);
            // En Modo Voz forzamos MONO en la entrada virtual de VoiceMeeter. Esto
            // toma la voz que pudiera llegar por un solo lado y la entrega centrada a B1.
            // Al volver a clase de guitarra se desactiva Mono para conservar el estéreo
            // de chorus, delay, reverb y demás efectos de guitarra.
            SetRequired($"Strip[{strip}].Mono", monoCenteredVoice ? 1f : 0f);
            SetRequired($"Strip[{strip}].Pan_x", 0f);
            SetRequired($"Strip[{strip}].Gain", Math.Clamp(gainDb, -60f, 12f));
            SetRequired($"Strip[{strip}].A1", 0f);
            if (type >= 2)
            {
                SetRequired($"Strip[{strip}].A2", 0f);
                SetRequired($"Strip[{strip}].A3", 0f);
            }
            if (type >= 3)
            {
                SetRequired($"Strip[{strip}].A4", 0f);
                SetRequired($"Strip[{strip}].A5", 0f);
            }

            SetRequired($"Strip[{strip}].B1", 1f);
            if (type >= 2) SetRequired($"Strip[{strip}].B2", 0f);
            if (type >= 3) SetRequired($"Strip[{strip}].B3", 0f);

            // 2.40.13: no alcanza con asignar el strip a B1. El BUS B1 también puede
            // conservar Mute, ganancia o un modo especial de una configuración anterior.
            // Como la interfaz visual de VoiceMeeter no es accesible con JAWS, dejamos
            // este bus en un estado determinista para videollamadas.
            SetRequired($"Bus[{b1Bus}].Mute", 0f);
            SetRequired($"Bus[{b1Bus}].Gain", 0f);
            SetRequired($"Bus[{b1Bus}].Mono", monoCenteredVoice ? 1f : 0f);
            SetRequired($"Bus[{b1Bus}].mode.normal", 1f);

            // La API mantiene una caché local de parámetros. La documentación oficial
            // recomienda consultar IsParametersDirty para sincronizarla antes de leer.
            try { _isParametersDirty?.Invoke(); } catch { }
            float b1 = 0f;
            float busMute = 1f;
            float busGain = -60f;
            int verify = _getParameterFloat!($"Strip[{strip}].B1", out b1);
            int muteVerify = _getParameterFloat!($"Bus[{b1Bus}].Mute", out busMute);
            int gainVerify = _getParameterFloat!($"Bus[{b1Bus}].Gain", out busGain);
            bool verified = verify == 0 && b1 >= 0.5f
                && muteVerify == 0 && busMute < 0.5f
                && gainVerify == 0 && busGain > -1.0f;
            string channelMode = monoCenteredVoice ? "mono centrado" : "estéreo";
            string gainText = Math.Abs(gainDb) < 0.05f ? "0 dB" : $"{gainDb:+0.0;-0.0;0.0} dB";
            LastStatus = verified
                ? $"VoiceMeeter {EditionName(type)} conectado. Strip a B1 activo; bus B1 sin mute y a 0 dB; {channelMode}; ganancia de entrada {gainText}; monitoreo A desactivado."
                : $"VoiceMeeter {EditionName(type)} configurado, pero la verificación completa de B1 falló. Strip={verify}/{b1:0.0}; bus mute={muteVerify}/{busMute:0.0}; bus ganancia={gainVerify}/{busGain:0.0}.";

            return new VoicemeeterSetupResult(verified, LastStatus, type, strip, verified);
        }
        catch (Exception ex)
        {
            return Fail($"No se pudo configurar VoiceMeeter: {ex.Message}");
        }
    }

    public string GetStatusSummary()
    {
        if (!EnsureApiLoaded(out string loadMessage))
            return loadMessage;

        int result = _getVoicemeeterType!(out int type);
        if (result != 0 || type is < 1 or > 3)
        {
            ConnectedType = 0;
            LastStatus = "VoiceMeeter instalado, motor no conectado.";
            return LastStatus;
        }

        ConnectedType = type;
        int strip = PrimaryVirtualStripIndex(type);
        int b1Bus = PrimaryB1BusIndex(type);
        try { _isParametersDirty?.Invoke(); } catch { }
        float b1 = 0f;
        float mono = 0f;
        float gain = 0f;
        float busMute = 0f;
        float busGain = 0f;
        float busMono = 0f;
        int getResult = strip >= 0 ? _getParameterFloat!($"Strip[{strip}].B1", out b1) : -1;
        int monoResult = strip >= 0 ? _getParameterFloat!($"Strip[{strip}].Mono", out mono) : -1;
        int gainResult = strip >= 0 ? _getParameterFloat!($"Strip[{strip}].Gain", out gain) : -1;
        int busMuteResult = b1Bus >= 0 ? _getParameterFloat!($"Bus[{b1Bus}].Mute", out busMute) : -1;
        int busGainResult = b1Bus >= 0 ? _getParameterFloat!($"Bus[{b1Bus}].Gain", out busGain) : -1;
        int busMonoResult = b1Bus >= 0 ? _getParameterFloat!($"Bus[{b1Bus}].Mono", out busMono) : -1;
        string route = getResult == 0 ? (b1 >= 0.5f ? "strip B1 activo" : "strip B1 inactivo") : "strip B1 no verificable";
        string channelMode = monoResult == 0 ? (mono >= 0.5f ? "entrada mono" : "entrada estéreo") : "modo de entrada no verificable";
        string gainText = gainResult == 0 ? $"entrada {gain:+0.0;-0.0;0.0} dB" : "ganancia de entrada no verificable";
        string busState = busMuteResult == 0
            ? (busMute >= 0.5f ? "BUS B1 SILENCIADO" : "bus B1 sin mute")
            : "mute de bus B1 no verificable";
        string busGainText = busGainResult == 0 ? $"bus B1 {busGain:+0.0;-0.0;0.0} dB" : "ganancia de bus B1 no verificable";
        string busMonoText = busMonoResult == 0 ? (busMono >= 0.5f ? "bus B1 mono" : "bus B1 estéreo") : "modo de bus B1 no verificable";
        string meters = GetLevelSummary(type);
        LastStatus = $"VoiceMeeter {EditionName(type)} conectado; {route}; {busState}; {busGainText}; {busMonoText}; {channelMode}; {gainText}; {meters}.";
        return LastStatus;
    }

    private bool EnsureApiLoaded(out string message)
    {
        if (_module != IntPtr.Zero && _loggedIn && _getVoicemeeterType is not null && _setParameterFloat is not null)
        {
            message = LastStatus;
            return true;
        }

        string? folder = FindInstallFolder();
        if (folder is null)
        {
            message = "VoiceMeeter no está instalado o no se encontró su registro de instalación.";
            LastStatus = message;
            return false;
        }

        string dllName = Environment.Is64BitProcess ? "VoicemeeterRemote64.dll" : "VoicemeeterRemote.dll";
        string dllPath = Path.Combine(folder, dllName);
        if (!File.Exists(dllPath))
        {
            message = $"VoiceMeeter está instalado, pero falta {dllName} en su carpeta.";
            LastStatus = message;
            return false;
        }

        try
        {
            _module = NativeLibrary.Load(dllPath);
            _login = GetDelegate<LoginDelegate>("VBVMR_Login");
            _logout = GetDelegate<LogoutDelegate>("VBVMR_Logout");
            _runVoicemeeter = GetDelegate<RunVoicemeeterDelegate>("VBVMR_RunVoicemeeter");
            _getVoicemeeterType = GetDelegate<GetVoicemeeterTypeDelegate>("VBVMR_GetVoicemeeterType");
            _isParametersDirty = GetDelegate<IsParametersDirtyDelegate>("VBVMR_IsParametersDirty");
            _getParameterFloat = GetDelegate<GetParameterFloatDelegate>("VBVMR_GetParameterFloat");
            _setParameterFloat = GetDelegate<SetParameterFloatDelegate>("VBVMR_SetParameterFloat");
            _getLevel = GetDelegate<GetLevelDelegate>("VBVMR_GetLevel");

            int loginResult = _login();
            if (loginResult < 0)
            {
                message = $"La API de VoiceMeeter respondió error {loginResult} al iniciar la conexión.";
                LastStatus = message;
                ReleaseNativeLibrary();
                return false;
            }

            _loggedIn = true;
            // Inicializa/actualiza la caché de parámetros tras el Login, tal como
            // indica la documentación del SDK de VoiceMeeter.
            try { _isParametersDirty!(); } catch { }
            message = loginResult == 1
                ? "API de VoiceMeeter conectada; el motor todavía no estaba abierto."
                : "API de VoiceMeeter conectada.";
            LastStatus = message;
            return true;
        }
        catch (Exception ex)
        {
            message = $"No se pudo cargar la API de VoiceMeeter: {ex.Message}";
            LastStatus = message;
            ReleaseNativeLibrary();
            return false;
        }
    }

    private bool EnsureServerRunning(out int type, out string message)
    {
        type = 0;
        int typeResult = _getVoicemeeterType!(out type);
        if (typeResult == 0 && type is >= 1 and <= 3)
        {
            message = $"VoiceMeeter {EditionName(type)} ya estaba activo.";
            return true;
        }

        // Para una videollamada Amp Accessible sólo necesita el VAIO principal y B1.
        // Intentamos las ediciones oficiales en orden, porque el usuario puede tener instalada
        // Standard, Banana o Potato. El tipo 6 corresponde al ejecutable x64 de Potato;
        // GetVoicemeeterType devuelve 3 una vez que Potato está en marcha.
        int lastRunResult = -1;
        foreach (int requestedType in new[] { 1, 2, 3, 6 })
        {
            lastRunResult = _runVoicemeeter!(requestedType);
            if (lastRunResult < 0)
                continue;

            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 3000)
            {
                Thread.Sleep(100);
                typeResult = _getVoicemeeterType(out type);
                if (typeResult == 0 && type is >= 1 and <= 3)
                {
                    message = $"VoiceMeeter {EditionName(type)} iniciado automáticamente.";
                    return true;
                }
            }
        }

        message = $"VoiceMeeter está instalado, pero no pudo iniciarse automáticamente. Último código {lastRunResult}.";
        return false;
    }

    private void SetRequired(string parameter, float value)
    {
        int result = _setParameterFloat!(parameter, value);
        if (result != 0)
            throw new InvalidOperationException($"{parameter} devolvió código {result}");
    }

    private T GetDelegate<T>(string exportName) where T : Delegate
    {
        IntPtr address = NativeLibrary.GetExport(_module, exportName);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static int PrimaryVirtualStripIndex(int type) => type switch
    {
        1 => 2, // VoiceMeeter: 2 entradas físicas + VAIO.
        2 => 3, // Banana: 3 entradas físicas + VAIO + AUX.
        3 => 5, // Potato: 5 entradas físicas + VAIO + AUX + VAIO3.
        _ => -1
    };

    private static int PrimaryB1BusIndex(int type) => type switch
    {
        1 => 1, // Standard: A=0, B=1.
        2 => 3, // Banana: A1=0, A2=1, A3=2, B1=3.
        3 => 5, // Potato: A1..A5=0..4, B1=5.
        _ => -1
    };

    private static int PrimaryVirtualInputLevelBase(int type) => type switch
    {
        1 => 4,
        2 => 6,
        3 => 10,
        _ => -1
    };

    private static int PrimaryB1OutputLevelBase(int type) => type switch
    {
        1 => 8,
        2 => 24,
        3 => 40,
        _ => -1
    };

    private string GetLevelSummary(int type)
    {
        if (_getLevel is null) return "medidores no disponibles";
        int inputBase = PrimaryVirtualInputLevelBase(type);
        int outputBase = PrimaryB1OutputLevelBase(type);
        if (inputBase < 0 || outputBase < 0) return "medidores no disponibles";

        float input = ReadMaxLevel(0, inputBase, 2);
        float output = ReadMaxLevel(3, outputBase, 2);
        return $"nivel instantáneo entrada virtual {FormatLevelDb(input)}, salida B1 {FormatLevelDb(output)}";
    }

    private float ReadMaxLevel(int levelType, int firstChannel, int count)
    {
        if (_getLevel is null) return float.NaN;
        float maximum = 0f;
        bool any = false;
        for (int i = 0; i < count; i++)
        {
            int result = _getLevel(levelType, firstChannel + i, out float value);
            if (result == 0 && float.IsFinite(value))
            {
                maximum = Math.Max(maximum, Math.Max(0f, value));
                any = true;
            }
        }
        return any ? maximum : float.NaN;
    }

    private static string FormatLevelDb(float linear)
    {
        if (!float.IsFinite(linear)) return "no disponible";
        if (linear <= 0.000001f) return "menor a -120 dB";
        double db = 20.0 * Math.Log10(linear);
        return $"{Math.Max(-120.0, db):0.0} dB";
    }

    private static string EditionName(int type) => type switch
    {
        1 => "Standard",
        2 => "Banana",
        3 => "Potato",
        _ => "desconocido"
    };

    private static string? FindInstallFolder()
    {
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? key = baseKey.OpenSubKey(UninstallKeyPath, writable: false);
                string? uninstall = key?.GetValue("UninstallString") as string;
                string? folder = FolderFromUninstallString(uninstall);
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                    return folder;
            }
            catch { }
        }

        // Respaldo para instalaciones antiguas o registros dañados.
        string[] roots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        foreach (string root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string candidate = Path.Combine(root, "VB", "Voicemeeter");
            if (File.Exists(Path.Combine(candidate, Environment.Is64BitProcess ? "VoicemeeterRemote64.dll" : "VoicemeeterRemote.dll")))
                return candidate;
        }
        return null;
    }

    private static string? FolderFromUninstallString(string? uninstall)
    {
        if (string.IsNullOrWhiteSpace(uninstall)) return null;
        string value = uninstall.Trim();
        string executable;
        if (value.Length > 0 && value[0] == '"')
        {
            int end = value.IndexOf('"', 1);
            executable = end > 1 ? value[1..end] : value.Trim('"');
        }
        else
        {
            int exeEnd = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            executable = exeEnd >= 0 ? value[..(exeEnd + 4)] : value;
        }
        return Path.GetDirectoryName(executable);
    }

    private VoicemeeterSetupResult Fail(string message)
    {
        LastStatus = message;
        return new VoicemeeterSetupResult(false, message, ConnectedType, -1, false);
    }

    public void Dispose()
    {
        if (_loggedIn)
        {
            try { _logout?.Invoke(); } catch { }
        }
        _loggedIn = false;
        ReleaseNativeLibrary();
    }

    private void ReleaseNativeLibrary()
    {
        if (_module != IntPtr.Zero)
        {
            try { NativeLibrary.Free(_module); } catch { }
        }
        _module = IntPtr.Zero;
        _login = null;
        _logout = null;
        _runVoicemeeter = null;
        _getVoicemeeterType = null;
        _isParametersDirty = null;
        _getParameterFloat = null;
        _setParameterFloat = null;
        _getLevel = null;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int LoginDelegate();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int LogoutDelegate();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int RunVoicemeeterDelegate(int type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetVoicemeeterTypeDelegate(out int type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int IsParametersDirtyDelegate();
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int GetParameterFloatDelegate([MarshalAs(UnmanagedType.LPStr)] string parameter, out float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int SetParameterFloatDelegate([MarshalAs(UnmanagedType.LPStr)] string parameter, float value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetLevelDelegate(int levelType, int channelIndex, out float value);
}

internal sealed record VoicemeeterSetupResult(bool Success, string Message, int Type, int VirtualStripIndex, bool B1Verified);
