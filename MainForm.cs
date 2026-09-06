using System.Diagnostics;
using System.Media;
using System.Text;
using GDMAmpAccessible.Audio;
using GDMAmpAccessible.Models;
using GDMAmpAccessible.Persistence;
using NAudio.Midi;

namespace GDMAmpAccessible;

public sealed class MainForm : Form, IMessageFilter
{
    private readonly string? _startupNamPath;
    private readonly bool _startupNamImportOnly;
    private readonly AudioEngine _engine = new();
    private readonly VoicemeeterRemoteController _voicemeeter = new();
    private readonly AudioDiagnosticsLog _audioDiagnostics = new();
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _parameterUpdateTimer = new() { Interval = 45 };

    private readonly ComboBox _driverCombo = new();
    private readonly ComboBox _inputCombo = new();
    private readonly ComboBox _bufferCombo = new();
    private readonly NumericUpDown _masterVolume = CreateDecimalControl(0, 100, 100, 1m, decimals: 0);
    private readonly Panel _sectionHost = new();
    private readonly List<Control> _mainSections = new();
    private readonly Button _applyBufferButton = new();
    private readonly Label _actualBufferLabel = new();
    private readonly Label _diagnosticsLabel = new();
    private readonly TextBox _audioDiagnosticReport = new();
    private readonly Label _audioDiagnosticHealth = new();
    private readonly Button _refreshAudioDiagnosticButton = new();
    private readonly Button _readAudioDiagnosticButton = new();
    private readonly Button _copyAudioDiagnosticButton = new();
    private readonly Button _saveAudioDiagnosticButton = new();
    private readonly Button _openAudioDiagnosticsFolderButton = new();
    private readonly ComboBox _channelCombo = new();
    private readonly CheckBox _simulationEnabled = new();
    private readonly Button _refreshDriversButton = new();
    private readonly Button _asioPanelButton = new();
    private readonly Button _startStopButton = new();
    private readonly Label _statusLabel = new();
    private readonly Button _checkUpdatesButton = new();
    private readonly Button _installUpdatePackageButton = new();
    private readonly Button _repairInstallationButton = new();
    private readonly Button _uninstallApplicationButton = new();

    private readonly ComboBox _sceneCombo = new();
    private readonly TextBox _sceneName = new();
    private readonly Button _loadSceneButton = new();
    private readonly Button _saveSceneButton = new();
    private readonly Button _renameSceneButton = new();
    private readonly Button _nextSceneButton = new();
    private SceneLibrary _sceneLibrary = SceneStore.Load();
    private PianoProgressionLibrary _pianoProgressionLibrary = PianoProgressionStore.Load();

    private readonly ComboBox _factoryPresetCombo = new();
    private readonly Button _loadFactoryPresetButton = new();
    private readonly Button _duplicateFactoryPresetButton = new();
    private readonly ComboBox _presetCombo = new();
    private readonly TextBox _presetName = new();
    private readonly Button _savePresetButton = new();
    private readonly Button _loadPresetButton = new();
    private readonly Button _deletePresetButton = new();
    private readonly Button _openPresetFolderButton = new();
    private readonly Button _exportBackupButton = new();
    private readonly Button _restoreBackupButton = new();
    private readonly Button _openBackupFolderButton = new();
    private UserPresetLibrary _presetLibrary = PresetStore.Load();
    private bool _loadingPresetList;
    private bool _loadingFactoryPreset;

    // MIDI: entrada física opcional, MIDI Learn y simulador interno para probar sin pedalera.
    private readonly ComboBox _midiInputCombo = new();
    private readonly Button _midiRefreshButton = new();
    private readonly Button _midiConnectButton = new();
    private readonly Label _midiConnectionStatus = new();
    private readonly CheckBox _midiProgramBanks = new();
    private readonly ComboBox _midiLearnActionCombo = new();
    private readonly Button _midiLearnButton = new();
    private readonly Button _midiCancelLearnButton = new();
    private readonly ListBox _midiBindingsList = new();
    private readonly Button _midiDeleteBindingButton = new();
    private readonly ComboBox _midiSimTypeCombo = new();
    private readonly NumericUpDown _midiSimChannel = CreateDecimalControl(1, 16, 1, 1m, decimals: 0);
    private readonly NumericUpDown _midiSimNumber = CreateDecimalControl(0, 127, 0, 1m, decimals: 0);
    private readonly NumericUpDown _midiSimValue = CreateDecimalControl(0, 127, 127, 1m, decimals: 0);
    private readonly Button _midiSimSendButton = new();
    private readonly Label _midiLastMessageLabel = new();
    private MidiSettings _midiSettings = MidiSettingsStore.Load();
    private MidiIn? _midiInput;
    private bool _midiLearnArmed;
    private int _lastMidiWahAnnouncementBucket = -1;
    private bool _loadingMidiDevices;

    private readonly ListBox _preChainList = new();
    private readonly Button _movePreEffectUpButton = new();
    private readonly Button _movePreEffectDownButton = new();
    private List<PreEffectSlot> _preEffectOrder = UserPreset.DefaultOrder();
    private AudioPreferences _audioPreferences = AudioSettingsStore.Load();
    private NamLibrary _namLibrary = NamLibraryStore.Load();

    // Bajos, medios, agudos y presencia se recuerdan por separado para los tres canales.
    // Índice 0: limpio; 1: crunch; 2: lead. Segunda dimensión: B, M, T, P.
    private readonly float[,] _channelEq = new float[9, 4];
    private int _rememberedChannelIndex;
    private bool _loadingChannelEq;
    private bool _stallReported;

    private bool _loadingScene;
    private int _activeSceneIndex = -1;
    private string? _loadedIrPath;
    private string? _loadedIrPathB;

    private readonly NumericUpDown _gain = CreateDecimalControl(0, 10, 3, 0.1m);
    private readonly NumericUpDown _bass = CreateDecimalControl(0, 10, 5, 0.1m);
    private readonly NumericUpDown _middle = CreateDecimalControl(0, 10, 4, 0.1m);
    private readonly NumericUpDown _treble = CreateDecimalControl(0, 10, 6, 0.1m);
    private readonly NumericUpDown _presence = CreateDecimalControl(0, 10, 5, 0.1m);
    private readonly NumericUpDown _output = CreateDecimalControl(0, 100, 25, 1m, decimals: 0);

    // Cadena de voz separada: entrada física 1 de la interfaz ASIO cuando está disponible.
    private readonly CheckBox _voiceEnabled = new();
    private readonly CheckBox _voiceOnlyMode = new();
    private readonly Button _voiceClassPresetButton = new();
    private readonly Button _guitarClassPresetButton = new();
    private readonly Label _classProfileStatusLabel = new();
    private bool _applyingClassProfile;

    // 2.41.0: perfil experimental de dos guitarras con cadenas DSP separadas.
    private readonly CheckBox _twoGuitarMode = new();
    private readonly ComboBox _dualEditGuitarCombo = new();

    // 2.41.22: selector NAM rápido común al contexto de edición de Alt+O. Permite
    // elegir/cambiar el NAM de Guitarra 1 o Guitarra 2 sin abandonar Dos Guitarras.
    private readonly CheckBox _dualQuickNamEnabled = new();
    private readonly ComboBox _dualQuickNamCombo = new();
    private readonly Button _dualQuickNamLoadButton = new();
    private readonly Button _dualQuickNamPreviousButton = new();
    private readonly Button _dualQuickNamNextButton = new();
    private readonly TextBox _dualQuickNamCurrent = new();
    private bool _updatingDualQuickNam;

    // 2.41.18: bancos de fábrica y personales por guitarra. La misma interfaz muestra
    // únicamente los bancos de la guitarra elegida en "Guitarra a editar".
    private readonly ComboBox _dualFactoryBankCombo = new();
    private readonly Button _dualFactoryBankLoadButton = new();
    private readonly ComboBox _dualBankCombo = new();
    private readonly TextBox _dualBankName = new();
    private readonly Button _dualBankSaveButton = new();
    private readonly Button _dualBankLoadButton = new();
    private readonly Button _dualBankDeleteButton = new();
    private DualGuitarBankLibrary _dualGuitarBankLibrary = DualGuitarBankStore.Load();
    private bool _loadingDualBankList;

    // 2.41.24: escenas completas del modo Dos Guitarras. Cada escena captura las dos
    // cadenas completas más master y acompañamiento, sin sustituir bancos individuales.
    private readonly ComboBox _dualSceneCombo = new();
    private readonly TextBox _dualSceneName = new();
    private readonly Button _dualSceneSaveButton = new();
    private readonly Button _dualSceneLoadButton = new();
    private readonly Button _dualSceneDeleteButton = new();
    private readonly Button _dualSceneNextButton = new();
    private DualGuitarSceneLibrary _dualGuitarSceneLibrary = DualGuitarSceneStore.Load();
    private bool _loadingDualSceneList;
    private bool _loadingDualScene;

    private readonly CheckBox _guitar1ProcessingEnabled = new();
    private readonly CheckBox _guitar1UseRigEffects = new();
    private ScenePreset? _guitar1EffectMemory;
    private ScenePreset? _guitar2EffectMemory;
    private bool _dualEffectMemoriesInitialized;
    private bool _loadingDualEffectMemory;
    private int _dualLastEditedGuitar;
    private readonly ComboBox _guitar1AmpCombo = new();
    private readonly NumericUpDown _guitar1Gain = CreateDecimalControl(0, 10, 3, 0.1m);
    private readonly NumericUpDown _guitar1Output = CreateDecimalControl(0, 100, 50, 1m, decimals: 0);
    private readonly NumericUpDown _guitar1Mix = CreateDecimalControl(0, 100, 70, 1m, decimals: 0);
    private readonly NumericUpDown _guitar1Pan = CreateDecimalControl(-100, 100, 0, 5m, decimals: 0);
    private readonly CheckBox _guitar1Mute = new();

    // 2.41.21: NAM completamente independiente para Guitarra 1 / Input 1. La biblioteca
    // de capturas se comparte como catálogo, pero el modelo cargado vive en el segundo
    // AudioProcessor y puede procesar simultáneamente con el NAM de Guitarra 2.
    private readonly CheckBox _guitar1NamEnabled = new();
    private readonly CheckBox _guitar1NamIncludesCabinet = new();
    private readonly ComboBox _guitar1NamBankCombo = new();
    private readonly Button _guitar1NamLoadBankButton = new();
    private readonly Button _guitar1NamPreviousButton = new();
    private readonly Button _guitar1NamNextButton = new();
    private readonly Button _guitar1NamLoadFileButton = new();
    private readonly Button _guitar1NamClearButton = new();
    private readonly TextBox _guitar1NamPath = new();
    private readonly NumericUpDown _guitar1NamInputTrim = CreateDecimalControl(-24, 24, 0, 0.5m);
    private readonly NumericUpDown _guitar1NamOutputTrim = CreateDecimalControl(-24, 24, 0, 0.5m);
    private readonly CheckBox _guitar1NamAutoLevel = new();
    private readonly NumericUpDown _guitar1NamAutoLevelDb = CreateDecimalControl(-12, 12, 0, 0.1m);
    private string? _guitar1ActiveNamBankId;
    private bool _loadingGuitar1NamBank;

    // 2.41.16: IR A completamente independiente para Guitarra 1 / Input 1.
    private string? _guitar1LoadedIrPath;
    private readonly TextBox _guitar1IrPath = new();
    private readonly TextBox _guitar1IrBrowserStatus = new();
    private readonly Button _guitar1LoadIrButton = new();
    private readonly Button _guitar1ClearIrButton = new();
    private readonly Button _guitar1SelectIrFolderButton = new();
    private readonly Button _guitar1PreviousIrButton = new();
    private readonly Button _guitar1NextIrButton = new();
    private readonly List<string> _guitar1IrBrowserFiles = new();
    private int _guitar1IrBrowserIndex = -1;

    private readonly NumericUpDown _guitar2Mix = CreateDecimalControl(0, 100, 70, 1m, decimals: 0);
    private readonly NumericUpDown _guitar2Pan = CreateDecimalControl(-100, 100, 0, 5m, decimals: 0);
    private readonly CheckBox _guitar2Mute = new();
    private readonly Label _dualGuitarStatusLabel = new();
    private readonly CheckBox _voiceSuppressorEnabled = new();
    private readonly NumericUpDown _voiceThreshold = CreateDecimalControl(-75, -20, -48, 1m, decimals: 0);
    private readonly NumericUpDown _voiceReduction = CreateDecimalControl(0, 60, 30, 1m, decimals: 0);
    private readonly NumericUpDown _voiceRelease = CreateDecimalControl(40, 1000, 220, 10m, decimals: 0);
    private readonly NumericUpDown _voiceHighPass = CreateDecimalControl(50, 180, 90, 5m, decimals: 0);
    private readonly NumericUpDown _voiceBass = CreateDecimalControl(-12, 12, 0, 0.5m);
    private readonly NumericUpDown _voiceMid = CreateDecimalControl(-12, 12, 0, 0.5m);
    private readonly NumericUpDown _voiceTreble = CreateDecimalControl(-12, 12, 0, 0.5m);
    private readonly NumericUpDown _voiceLevel = CreateDecimalControl(0, 150, 100, 1m, decimals: 0);
    private readonly NumericUpDown _voiceMonitorLevel = CreateDecimalControl(0, 150, 70, 1m, decimals: 0);
    private readonly CheckBox _meetOutputEnabled = new();
    private readonly ComboBox _meetOutputCombo = new();
    private readonly Button _refreshMeetOutputsButton = new();
    private readonly Button _applyMeetOutputButton = new();
    private readonly Button _prepareVoicemeeterButton = new();
    private readonly Label _voicemeeterStatusLabel = new();
    private readonly Label _recommendedConferenceMicLabel = new();
    private readonly Button _checkConferenceMicButton = new();
    private readonly Button _openVbCablePageButton = new();
    private readonly CheckBox _checkFocusriteLoopbackButton = new();
    private bool _guitarClassLoopbackSelected;
    private readonly Label _focusriteLoopbackStatusLabel = new();
    private readonly NumericUpDown _meetGuitarLevel = CreateDecimalControl(0, 150, 100, 1m, decimals: 0);
    private readonly NumericUpDown _meetVoiceLevel = CreateDecimalControl(0, 150, 125, 1m, decimals: 0);
    private readonly List<MeetOutputDevice> _meetOutputDevices = new();

    private readonly ComboBox _tunerGuitarCombo = new();
    private readonly CheckBox _tunerEnabled = new();
    private readonly CheckBox _tunerMuteOutput = new();
    private readonly CheckBox _tunerSoundGuide = new();
    private readonly NumericUpDown _tunerReferenceA = CreateDecimalControl(430, 450, 440, 1m, decimals: 0);
    private readonly NumericUpDown _tunerGuideVolume = CreateDecimalControl(0, 100, 18, 1m, decimals: 0);
    private readonly TextBox _tunerStatus = new();
    private readonly Button _readTunerButton = new();
    private readonly ComboBox _referenceToneCombo = new();
    private readonly Button _playReferenceToneButton = new();

    private readonly CheckBox _octaverEnabled = new();
    private readonly ComboBox _octaverCharacterCombo = new();
    private readonly NumericUpDown _octaverDry = CreateDecimalControl(0, 100, 70, 1m, decimals: 0);
    private readonly NumericUpDown _octaverDown = CreateDecimalControl(0, 100, 55, 1m, decimals: 0);
    private readonly NumericUpDown _octaverUp = CreateDecimalControl(0, 100, 55, 1m, decimals: 0);
    private readonly NumericUpDown _octaverTone = CreateDecimalControl(0, 100, 50, 1m, decimals: 0);
    private readonly NumericUpDown _octaverLevel = CreateDecimalControl(0, 125, 85, 1m, decimals: 0);
    private readonly CheckBox _gateEnabled = new();
    private readonly NumericUpDown _gateThreshold = CreateDecimalControl(-80, -20, -58, 1m, decimals: 0);
    private readonly NumericUpDown _gateRelease = CreateDecimalControl(10, 1000, 180, 10m, decimals: 0);

    private readonly CheckBox _compressorEnabled = new();
    private readonly ComboBox _compressorCharacterCombo = new();
    private readonly NumericUpDown _compressorSustain = CreateDecimalControl(0, 10, 5, 0.1m);
    private readonly NumericUpDown _compressorAttack = CreateDecimalControl(2, 80, 18, 1m, decimals: 0);
    private readonly NumericUpDown _compressorLevel = CreateDecimalControl(0, 10, 5, 0.1m);

    private readonly CheckBox _autoWahEnabled = new();
    private readonly ComboBox _autoWahModeCombo = new();
    private readonly ComboBox _autoWahCharacterCombo = new();
    private readonly NumericUpDown _autoWahSensitivity = CreateDecimalControl(0, 10, 5, 0.1m);
    private readonly NumericUpDown _autoWahRange = CreateDecimalControl(0, 10, 6, 0.1m);
    private readonly NumericUpDown _autoWahResonance = CreateDecimalControl(0, 10, 6, 0.1m);
    private readonly NumericUpDown _autoWahManualPosition = CreateDecimalControl(0, 100, 50, 1m, decimals: 0);

    private readonly ComboBox _effectSelector = new();
    private readonly Panel _effectPanelHost = new();
    private readonly List<Control> _effectPanels = new();

    private readonly CheckBox _overdriveEnabled = new();
    private readonly ComboBox _distortionCharacterCombo = new();
    private readonly NumericUpDown _overdriveGain = CreateDecimalControl(0, 10, 4.0m, 0.1m);
    private readonly NumericUpDown _overdriveTone = CreateDecimalControl(0, 10, 5, 0.1m);
    private readonly NumericUpDown _overdriveLevel = CreateDecimalControl(0, 10, 5, 0.1m);

    private readonly CheckBox _ts9Enabled = new();
    private readonly ComboBox _driveCharacterCombo = new();
    private readonly NumericUpDown _ts9Gain = CreateDecimalControl(0, 10, 3, 0.1m);
    private readonly NumericUpDown _ts9Tone = CreateDecimalControl(0, 10, 5, 0.1m);
    private readonly NumericUpDown _ts9Level = CreateDecimalControl(0, 10, 5, 0.1m);

    private readonly CheckBox _od1Enabled = new();
    private readonly ComboBox _od1CharacterCombo = new();
    private readonly NumericUpDown _od1Drive = CreateDecimalControl(0, 10, 3.5m, 0.1m);
    private readonly NumericUpDown _od1Tone = CreateDecimalControl(0, 10, 5m, 0.1m);
    private readonly NumericUpDown _od1Level = CreateDecimalControl(0, 10, 5, 0.1m);

    private readonly CheckBox _fuzzEnabled = new();
    private readonly ComboBox _fuzzCharacterCombo = new();
    private readonly NumericUpDown _fuzzGain = CreateDecimalControl(0, 10, 5, 0.1m);
    private readonly NumericUpDown _fuzzTone = CreateDecimalControl(0, 10, 5, 0.1m);
    private readonly NumericUpDown _fuzzLevel = CreateDecimalControl(0, 10, 5, 0.1m);

    private readonly CheckBox _eq5Enabled = new();
    private readonly ComboBox _eq5PlacementCombo = new();
    private readonly NumericUpDown _eq5Band100 = CreateDecimalControl(-12, 12, 0, 0.5m);
    private readonly NumericUpDown _eq5Band250 = CreateDecimalControl(-12, 12, 0, 0.5m);
    private readonly NumericUpDown _eq5Band800 = CreateDecimalControl(-12, 12, 0, 0.5m);
    private readonly NumericUpDown _eq5Band2500 = CreateDecimalControl(-12, 12, 0, 0.5m);
    private readonly NumericUpDown _eq5Band6400 = CreateDecimalControl(-12, 12, 0, 0.5m);
    private readonly NumericUpDown _eq5Output = CreateDecimalControl(-12, 12, 0, 0.5m);

    private readonly CheckBox _boosterEnabled = new();
    private readonly ComboBox _boosterCharacterCombo = new();
    private readonly NumericUpDown _boosterDb = CreateDecimalControl(0, 20, 6, 0.5m);

    private readonly CheckBox _externalIrEnabled = new();
    private readonly TextBox _irPath = new();
    private readonly Button _loadIrButton = new();
    private readonly Button _clearIrButton = new();
    private readonly CheckBox _externalIrBEnabled = new();
    private readonly TextBox _irPathB = new();
    private readonly Button _loadIrBButton = new();
    private readonly Button _clearIrBButton = new();
    private readonly TextBox _irBrowserStatus = new();
    private readonly Button _selectIrFolderButton = new();
    private readonly Button _previousIrButton = new();
    private readonly Button _nextIrButton = new();
    private readonly List<string> _irBrowserFiles = new();
    private int _irBrowserIndex = -1;
    private readonly NumericUpDown _irMix = CreateDecimalControl(0, 100, 0, 1m, decimals: 0);
    private readonly CheckBox _irBPhaseInvert = new();
    private readonly NumericUpDown _irLowCut = CreateDecimalControl(20, 250, 20, 5m, decimals: 0);
    private readonly NumericUpDown _irHighCut = CreateDecimalControl(3000, 20000, 20000, 250m, decimals: 0);

    // Quinta sección: Neural Amp Modeler. El motor nativo es opcional.
    // 2.41.23: Alt+N concentra la configuración avanzada y permite elegir
    // explícitamente qué guitarra NAM se está ajustando. Alt+O queda para carga rápida.
    private readonly ComboBox _namEditGuitarCombo = new();
    private readonly Panel _namAdvancedHost = new();
    private readonly GroupBox _namGuitar1AdvancedGroup = new();
    private readonly GroupBox _namGuitar2AdvancedGroup = new();
    private readonly CheckBox _namEnabled = new();
    private readonly CheckBox _namIncludesCabinet = new();
    private readonly TextBox _namPath = new();
    private readonly TextBox _namStatus = new();
    private readonly Button _loadNamButton = new();
    private readonly Button _clearNamButton = new();
    private readonly Button _checkNamButton = new();
    private readonly Button _namGuideButton = new();
    private readonly TextBox _namSearchQuery = new();
    private readonly Button _namSearchButton = new();
    private readonly Button _loadLatestNamButton = new();
    private readonly ComboBox _namBankCombo = new();
    private readonly TextBox _namBankFilterText = new();
    private readonly ComboBox _namCategoryFilter = new();
    private readonly CheckBox _namFavoritesOnly = new();
    private readonly Button _clearNamFiltersButton = new();
    private readonly TextBox _namBankFilterStatus = new();
    private readonly TextBox _namDisplayName = new();
    private readonly ComboBox _namCategoryEdit = new();
    private readonly CheckBox _namFavorite = new();
    private readonly Button _saveNamMetadataButton = new();
    private readonly Button _loadNamBankButton = new();
    private readonly Button _previousNamButton = new();
    private readonly Button _nextNamButton = new();
    private readonly Button _removeNamBankButton = new();
    private readonly Button _openNamBankFolderButton = new();
    private string? _activeNamBankId;
    private bool _loadingNamBank;
    private bool _loadingNamFilters;
    private readonly NumericUpDown _namInputTrim = CreateDecimalControl(-24, 24, 0, 0.5m);
    private readonly NumericUpDown _namOutputTrim = CreateDecimalControl(-24, 24, 0, 0.5m);
    private readonly CheckBox _namAutoLevel = new();
    private readonly NumericUpDown _namAutoLevelDb = CreateDecimalControl(-12, 12, 0, 0.1m);
    private readonly Button _calibrateNamLevelButton = new();
    private readonly TextBox _namLevelStatus = new();

    // Quinta pestaña Herramientas: afinador, metrónomo, batería, bajo, piano y grabador. Sale por la misma ruta ASIO.
    private readonly CheckBox _metronomeEnabled = new();
    private readonly NumericUpDown _metronomeBpm = CreateDecimalControl(40, 240, 80, 1m, decimals: 0);
    private readonly ComboBox _metronomeMeter = new();
    private readonly NumericUpDown _metronomeVolume = CreateDecimalControl(0, 100, 25, 1m, decimals: 0);
    private readonly CheckBox _metronomeAccent = new();
    private readonly Button _tapTempoButton = new();
    private readonly Button _restartMetronomeButton = new();
    private readonly CheckBox _drumsEnabled = new();
    private readonly ComboBox _drumPattern = new();
    private readonly NumericUpDown _drumVolume = CreateDecimalControl(0, 100, 35, 1m, decimals: 0);
    private readonly CheckBox _backingBassEnabled = new();
    private readonly ComboBox _backingBassKey = new();
    private readonly ComboBox _backingBassMode = new();
    private readonly ComboBox _backingBassLine = new();
    private readonly NumericUpDown _backingBassVolume = CreateDecimalControl(0, 100, 28, 1m, decimals: 0);
    private readonly CheckBox _pianoEnabled = new();
    private readonly ComboBox _pianoSound = new();
    private readonly ComboBox _pianoKey = new();
    private readonly ComboBox _pianoProgression = new();
    private readonly TextBox _pianoCustomProgression = new();
    private readonly TextBox _pianoCustomProgressionName = new();
    private readonly ComboBox _pianoSavedProgression = new();
    private readonly Button _pianoApplyCustomProgression = new();
    private readonly Button _pianoSaveCustomProgression = new();
    private readonly Button _pianoLoadCustomProgression = new();
    private readonly Button _pianoDeleteCustomProgression = new();
    private readonly ComboBox _pianoStyle = new();
    private readonly NumericUpDown _pianoVolume = CreateDecimalControl(0, 100, 24, 1m, decimals: 0);
    private bool _updatingBackingPairShortcut;
    // F12 es quien arma explícitamente el seguimiento por guitarra. Los controles
    // individuales de batería/bajo/piano conservan el comportamiento continuo.
    private bool _backingBandFollowGuitarArmed;
    private readonly ComboBox _practiceRecordingDuration = new();
    private readonly Button _practiceRecordButton = new();
    private readonly Button _practiceStopButton = new();
    private readonly Button _practiceOpenFolderButton = new();
    private readonly TextBox _practiceRecordingStatus = new();
    private readonly CheckBox _loopSyncTempo = new();
    private readonly ComboBox _loopSourceCombo = new();
    private readonly ComboBox _loopBars = new();
    private readonly Button _loopRecordButton = new();
    private readonly Button _loopOverdubButton = new();
    private readonly Button _loopUndoButton = new();
    private readonly Button _loopPlayButton = new();
    private readonly Button _loopSaveButton = new();
    private readonly Button _loopClearButton = new();
    private readonly TextBox _loopStatus = new();
    private readonly List<long> _tapTempoTicks = new(5);
    private bool _practiceSaveInProgress;
    private bool _loopSaveInProgress;
    private bool _loopAutoCompletionAnnounced;
    private bool _loopTempoCompletionAnnounced;

    private readonly CheckBox _fxLoopEnabled = new();
    private readonly NumericUpDown _fxLoopSend = CreateDecimalControl(0, 100, 100, 1m, decimals: 0);
    private readonly NumericUpDown _fxLoopReturn = CreateDecimalControl(0, 100, 100, 1m, decimals: 0);
    private readonly Button _resetDelayButton = new();

    private readonly CheckBox _phaserEnabled = new();
    private readonly NumericUpDown _phaserRate = CreateDecimalControl(0.05m, 4m, 0.55m, 0.05m);
    private readonly NumericUpDown _phaserDepth = CreateDecimalControl(0, 100, 68, 1m, decimals: 0);
    private readonly NumericUpDown _phaserFeedback = CreateDecimalControl(0, 70, 18, 1m, decimals: 0);
    private readonly NumericUpDown _phaserMix = CreateDecimalControl(0, 85, 45, 1m, decimals: 0);

    private readonly CheckBox _flangerEnabled = new();
    private readonly ComboBox _flangerCharacterCombo = new();
    private readonly NumericUpDown _flangerRate = CreateDecimalControl(0.05m, 5m, 0.35m, 0.05m);
    private readonly NumericUpDown _flangerDepth = CreateDecimalControl(0, 100, 62, 1m, decimals: 0);
    private readonly NumericUpDown _flangerFeedback = CreateDecimalControl(-70, 70, 28, 1m, decimals: 0);
    private readonly NumericUpDown _flangerMix = CreateDecimalControl(0, 85, 42, 1m, decimals: 0);

    private readonly CheckBox _chorusEnabled = new();
    private readonly ComboBox _chorusPlacementCombo = new();
    private readonly ComboBox _chorusCharacterCombo = new();
    private readonly NumericUpDown _chorusRate = CreateDecimalControl(0.1m, 5m, 0.8m, 0.1m);
    private readonly NumericUpDown _chorusDepth = CreateDecimalControl(0, 10, 7.5m, 0.1m);
    private readonly NumericUpDown _chorusMix = CreateDecimalControl(0, 100, 45, 1m, decimals: 0);

    // Segundo chorus: carácter analógico ancho, inspirado en pedales MXR clásicos.
    private readonly CheckBox _analogChorusEnabled = new();
    private readonly ComboBox _analogChorusPlacementCombo = new();
    private readonly NumericUpDown _analogChorusRate = CreateDecimalControl(0.05m, 3m, 0.65m, 0.05m);
    private readonly NumericUpDown _analogChorusDepth = CreateDecimalControl(0, 10, 6.5m, 0.1m);
    private readonly NumericUpDown _analogChorusMix = CreateDecimalControl(0, 100, 42, 1m, decimals: 0);
    private readonly NumericUpDown _analogChorusLow = CreateDecimalControl(0, 10, 5, 0.5m);
    private readonly NumericUpDown _analogChorusHigh = CreateDecimalControl(0, 10, 5, 0.5m);

    // MicroPitch 80s / Harmonizer: ancho estéreo sin el vaivén de un chorus.
    private readonly CheckBox _microPitchEnabled = new();
    private readonly NumericUpDown _microPitchDetune = CreateDecimalControl(2, 24, 9, 1m, decimals: 0);
    private readonly NumericUpDown _microPitchDelay = CreateDecimalControl(3, 24, 10, 1m, decimals: 0);
    private readonly NumericUpDown _microPitchMix = CreateDecimalControl(0, 75, 38, 1m, decimals: 0);

    private readonly CheckBox _rotaryEnabled = new();
    private readonly CheckBox _rotarySync = new();
    private readonly ComboBox _rotaryDivision = new();
    private readonly CheckBox _rotaryFast = new();
    private readonly NumericUpDown _rotaryDepth = CreateDecimalControl(0,100,70,1m,decimals:0);
    private readonly NumericUpDown _rotaryMix = CreateDecimalControl(0,85,45,1m,decimals:0);

    private readonly CheckBox _tremoloEnabled = new();
    private readonly CheckBox _tremoloSync = new();
    private readonly ComboBox _tremoloDivision = new();
    private readonly NumericUpDown _tremoloRate = CreateDecimalControl(0.1m, 12m, 4m, 0.1m);
    private readonly NumericUpDown _tremoloDepth = CreateDecimalControl(0, 95, 45, 1m, decimals: 0);

    private readonly CheckBox _delayEnabled = new();
    private readonly CheckBox _delaySync = new();
    private readonly ComboBox _delayDivision = new();
    private readonly ComboBox _delayCharacterCombo = new();
    private readonly NumericUpDown _delayTime = CreateDecimalControl(1, 1000, 380, 1m, decimals: 0);
    private readonly NumericUpDown _delayFeedback = CreateDecimalControl(0, 60, 25, 1m, decimals: 0);
    private readonly NumericUpDown _delayMix = CreateDecimalControl(0, 60, 20, 1m, decimals: 0);

    private readonly CheckBox _reverbEnabled = new();
    private readonly ComboBox _reverbCharacterCombo = new();
    private readonly NumericUpDown _reverbMix = CreateDecimalControl(0, 65, 24, 1m, decimals: 0);
    private readonly NumericUpDown _reverbDecay = CreateDecimalControl(0, 100, 48, 1m, decimals: 0);
    private readonly NumericUpDown _reverbTone = CreateDecimalControl(0, 100, 55, 1m, decimals: 0);
    private readonly NumericUpDown _reverbPreDelay = CreateDecimalControl(0, 150, 18, 1m, decimals: 0);
    private readonly NumericUpDown _reverbDamping = CreateDecimalControl(0, 100, 45, 1m, decimals: 0);
    private readonly NumericUpDown _reverbDiffusion = CreateDecimalControl(0, 100, 60, 1m, decimals: 0);

    private int _revision;
    private int _lastDelayAutomaticResetCount;
    private int _diagnosticsPollCounter;
    private TunerReading _lastTunerReading = TunerReading.NoSignal;
    private bool _audioRequested;
    private bool _loadingAudioDeviceSelection;

    private static readonly (string Name, float Frequency)[] ReferenceTones =
    {
        ("Mi grave, sexta cuerda, 82,4 hercios", 82.4069f),
        ("La, quinta cuerda, 110 hercios", 110.0f),
        ("Re, cuarta cuerda, 146,8 hercios", 146.8324f),
        ("Sol, tercera cuerda, 196 hercios", 196.0f),
        ("Si, segunda cuerda, 246,9 hercios", 246.9417f),
        ("Mi agudo, primera cuerda, 329,6 hercios", 329.6276f),
        ("La de referencia, 440 hercios", 440.0f)
    };

    public MainForm(string? startupNamPath = null, bool startupNamImportOnly = false)
    {
        _startupNamPath = startupNamPath;
        _startupNamImportOnly = startupNamImportOnly;
        Text = AppInfo.WindowTitle;
        // JAWS: el título de la ventana ya identifica la aplicación. No duplicarlo como
        // AccessibleName/Description del formulario porque algunos lectores lo anuncian
        // antes de cada control al recorrer con Tab.
        AccessibleName = string.Empty;
        AccessibleDescription = string.Empty;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 640);
        ClientSize = new Size(900, 780);
        AutoScroll = true;
        KeyPreview = true;
        // Segunda capa de teclado: un filtro de mensajes captura Shift+F1..F7
        // antes de que controles estándar (listas, combos, botones, etc.) puedan
        // interpretar esas combinaciones como comandos propios.
        Application.AddMessageFilter(this);
        FontFamily uiFontFamily = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
        Font = new Font(uiFontFamily, 10f);

        BuildInterface();
        LoadMasterVolumePreference();
        LoadChannelEqPreferences();
        LoadVoicePreferences();
        LoadDualGuitarPreferences();
        LoadTunerPreference();
        LoadNamPreferences();
        LoadIrBrowserPreference();
        LoadMetronomePreferences();
        PopulatePianoProgressionLibrary();
        LoadMeetOutputDevices(announce: false);
        WireEvents();
        UpdateDynamicEffectAccessibleNames();
        PopulateSceneList();
        PopulatePresetList();
        PopulatePreChainList();
        PopulateMidiActions();
        RefreshMidiBindingsList();
        RefreshMidiDevices(announce: false);
        LoadDrivers(announce: false);
        RecallScene(_sceneLibrary.LastSelectedIndex, announce: false);
        InitializeDualEffectMemories();
        RefreshDualGuitarBankList();
        RefreshDualGuitarSceneList();

        _parameterUpdateTimer.Tick += (_, _) =>
        {
            _parameterUpdateTimer.Stop();
            UpdateParameters();
        };
        Shown += (_, _) => BeginInvoke((Action)(() =>
        {
            TryAutoConnectMidi();
            if (!string.IsNullOrWhiteSpace(_startupNamPath))
            {
                ImportNamModel(_startupNamPath, announce: true, loadAfterImport: !_startupNamImportOnly);
                ShowSection(4, _namEditGuitarCombo);
            }
            else
            {
                AnnounceSelectedAsioDevice();
                _driverCombo.Focus();
            }
        }));

        _statusTimer.Tick += (_, _) => PollAudioErrors();
        _statusTimer.Start();

        // No se reinicia ASIO automáticamente desde otro hilo. En versiones anteriores,
        // dos vigilantes podían coincidir y convertir un microcorte en una detención larga.
        // Si el callback deja de avanzar, se informa y el usuario conserva el control con F4.
    }

    private void BuildInterface()
    {
        AutoScroll = false;

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Controls.Add(main);

        var heading = new Label
        {
            Text = "Amp Accessible",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            AccessibleName = string.Empty,
            AccessibleDescription = string.Empty,
            AccessibleRole = AccessibleRole.None,
            TabStop = false
        };
        main.Controls.Add(heading, 0, 0);

        var instructions = new Label
        {
            Text = "Use Alt+C Configuración, Alt+E Equipo, Alt+F Efectos, Alt+B Bancos, Alt+N NAM, Alt+H Herramientas, Alt+M Micrófono y videollamadas, Alt+O Dos guitarras, Alt+I MIDI y Alt+D Diagnóstico. F4 inicia o detiene. F1 muestra todos los atajos.",
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            AccessibleName = string.Empty,
            AccessibleDescription = string.Empty,
            AccessibleRole = AccessibleRole.None,
            TabStop = false
        };
        main.Controls.Add(instructions, 0, 1);

        _sectionHost.Dock = DockStyle.Fill;
        _sectionHost.TabStop = false;
        _sectionHost.AccessibleName = string.Empty;
        _sectionHost.AccessibleDescription = string.Empty;

        var generalTab = CreateMainSection("1. Configuración",
            "Configuración: controlador ASIO, entrada de guitarra, buffer y estado");
        var equipmentTab = CreateMainSection("2. Equipos",
            "Equipos: amplificador, ecualización, gabinete e impulsos IR");
        var effectsTab = CreateMainSection("3. Efectos",
            "Efectos: pedales, loop, modulaciones, chorus, delay y reverb");
        var scenesTab = CreateMainSection("4. Escenas y presets",
            "Escenas y presets: guardar, recuperar y organizar sonidos completos");
        var namTab = CreateMainSection("5. NAM",
            "Neural Amp Modeler: elegir qué guitarra se configura, ajustar nivel, gabinete, Auto Level y organizar la biblioteca compartida");
        var toolsTab = CreateMainSection("6. Herramientas",
            "Herramientas: afinador cromático, metrónomo, batería, bajo, piano de acompañamiento y grabación de práctica");
        var microphoneTab = CreateMainSection("7. Micrófono",
            "Micrófono: procesamiento independiente de la entrada 1, supresor, ecualización y nivel de voz");

        var dualGuitarTab = CreateMainSection("8. Dos guitarras",
            "Dos guitarras: operación rápida de dos cadenas independientes, bancos, IR y selección/carga NAM por guitarra");
        var midiTab = CreateMainSection("9. MIDI",
            "MIDI: seleccionar controlador, aprender asignaciones y simular mensajes sin hardware");
        var diagnosticTab = CreateMainSection("10. Diagnóstico",
            "Diagnóstico accesible: estado ASIO, buffer real, carga DSP, callbacks, errores, NAM, videollamadas y memoria");

        var generalContent = CreateTabContent();
        generalContent.Controls.Add(BuildAudioGroup());

        generalContent.Controls.Add(BuildMaintenanceGroup());

        var statusGroup = new GroupBox
        {
            Text = "Estado",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            AccessibleName = string.Empty,
            AccessibleDescription = string.Empty,
            TabStop = false,
            Padding = new Padding(10),
            Margin = new Padding(0, 8, 0, 0)
        };
        _statusLabel.Text = "Detenido. Amp Accessible detectará automáticamente los controladores ASIO instalados.";
        _statusLabel.AutoSize = true;
        _statusLabel.MaximumSize = new Size(790, 0);
        _statusLabel.Padding = new Padding(8);
        _statusLabel.AccessibleName = "Estado: detenido";
        statusGroup.Controls.Add(_statusLabel);
        generalContent.Controls.Add(statusGroup);
        generalTab.Controls.Add(generalContent);

        var equipmentContent = CreateTabContent();
        equipmentContent.Controls.Add(BuildAmpGroup());
        equipmentContent.Controls.Add(BuildIrGroup());
        equipmentTab.Controls.Add(equipmentContent);

        var effectsContent = CreateTabContent();
        effectsContent.Controls.Add(BuildEffectsSelectorGroup());
        effectsContent.Controls.Add(BuildPreChainGroup());
        effectsTab.Controls.Add(effectsContent);

        var scenesContent = CreateTabContent();
        scenesContent.Controls.Add(BuildScenesGroup());
        scenesContent.Controls.Add(BuildPresetsGroup());
        scenesContent.Controls.Add(BuildBackupGroup());
        scenesTab.Controls.Add(scenesContent);

        var namContent = CreateTabContent();
        namContent.Controls.Add(BuildNamGroup());
        namTab.Controls.Add(namContent);

        var toolsContent = CreateTabContent();
        toolsContent.Controls.Add(BuildTunerGroup());
        toolsContent.Controls.Add(BuildMetronomeGroup());
        toolsTab.Controls.Add(toolsContent);

        var microphoneContent = CreateTabContent();
        microphoneContent.Controls.Add(BuildVoiceGroup());
        microphoneTab.Controls.Add(microphoneContent);

        var dualGuitarContent = CreateTabContent();
        dualGuitarContent.Controls.Add(BuildDualGuitarGroup());
        dualGuitarTab.Controls.Add(dualGuitarContent);

        var midiContent = CreateTabContent();
        midiContent.Controls.Add(BuildMidiGroup());
        midiTab.Controls.Add(midiContent);

        var diagnosticContent = CreateTabContent();
        diagnosticContent.Controls.Add(BuildAudioDiagnosticsGroup());
        diagnosticTab.Controls.Add(diagnosticContent);

        _mainSections.AddRange(new Control[] { generalTab, equipmentTab, effectsTab, scenesTab, namTab, toolsTab, microphoneTab, dualGuitarTab, midiTab, diagnosticTab });
        foreach (Control section in _mainSections)
        {
            section.Dock = DockStyle.Fill;
            section.Visible = false;
            _sectionHost.Controls.Add(section);
        }
        generalTab.Visible = true;

        var sectionMenu = new MenuStrip { Dock = DockStyle.Top, ShowItemToolTips = true };
        sectionMenu.AccessibleName = "Menú de secciones";
        sectionMenu.Items.Add(CreateSectionMenuItem("&Configuración", Keys.Alt | Keys.C, 0, _driverCombo));
        sectionMenu.Items.Add(CreateSectionMenuItem("&Equipo", Keys.Alt | Keys.E, 1, _channelCombo));
        sectionMenu.Items.Add(CreateSectionMenuItem("E&fectos", Keys.Alt | Keys.F, 2, _effectSelector));
        sectionMenu.Items.Add(CreateSectionMenuItem("&Bancos", Keys.Alt | Keys.B, 3, _factoryPresetCombo));
        sectionMenu.Items.Add(CreateSectionMenuItem("&NAM", Keys.Alt | Keys.N, 4, _namEditGuitarCombo));
        sectionMenu.Items.Add(CreateSectionMenuItem("&Herramientas", Keys.Alt | Keys.H, 5, _tunerEnabled));
        sectionMenu.Items.Add(CreateSectionMenuItem("&Micrófono y videollamadas", Keys.Alt | Keys.M, 6, _voiceEnabled));
        sectionMenu.Items.Add(CreateSectionMenuItem("D&os guitarras", Keys.Alt | Keys.O, 7, _twoGuitarMode));
        sectionMenu.Items.Add(CreateSectionMenuItem("M&IDI", Keys.Alt | Keys.I, 8, _midiInputCombo));
        sectionMenu.Items.Add(CreateSectionMenuItem("&Diagnóstico", Keys.Alt | Keys.D, 9, _audioDiagnosticReport));
        MainMenuStrip = sectionMenu;
        Controls.Add(sectionMenu);
        sectionMenu.BringToFront();

        main.Controls.Add(_sectionHost, 0, 2);
    }

    private ToolStripMenuItem CreateSectionMenuItem(string text, Keys shortcut, int sectionIndex, Control firstControl)
    {
        var item = new ToolStripMenuItem(text)
        {
            ShortcutKeys = shortcut,
            ShowShortcutKeys = true,
            AccessibleName = text.Replace("&", string.Empty)
        };
        item.Click += (_, _) => ShowSection(sectionIndex, firstControl);
        return item;
    }

    private void ShowSection(int index, Control? firstControl)
    {
        if (index < 0 || index >= _mainSections.Count) return;

        _sectionHost.SuspendLayout();
        try
        {
            for (int i = 0; i < _mainSections.Count; i++)
                _mainSections[i].Visible = i == index;
            _mainSections[index].BringToFront();
        }
        finally
        {
            _sectionHost.ResumeLayout(true);
        }

        if (index == 4)
        {
            int wantedNamGuitar = _dualEditGuitarCombo.SelectedIndex == 1 ? 1 : 0;
            if (_namEditGuitarCombo.Items.Count >= 2) _namEditGuitarCombo.SelectedIndex = wantedNamGuitar;
            UpdateNamSettingsGuitarContext();
        }

        string section = index switch
        {
            0 => "Configuración",
            1 => "Equipo",
            2 => "Efectos",
            3 => "Bancos y escenas",
            4 => "NAM",
            5 => "Herramientas",
            6 => "Micrófono y videollamadas",
            7 => "Dos guitarras",
            8 => "MIDI",
            9 => "Diagnóstico",
            _ => "Sección"
        };
        SetStatus($"Sección {section}.");
        BeginInvoke((Action)(() => firstControl?.Focus()));
    }

    private static Panel CreateMainSection(string text, string accessibleDescription)
    {
        return new Panel
        {
            AccessibleName = string.Empty,
            AccessibleDescription = accessibleDescription,
            AccessibleRole = AccessibleRole.Pane,
            TabStop = false,
            AutoScroll = true,
            Padding = new Padding(8)
        };
    }

    private static TableLayoutPanel CreateTabContent()
    {
        return new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(2),
            AccessibleName = string.Empty,
            AccessibleDescription = string.Empty,
            AccessibleRole = AccessibleRole.None,
            TabStop = false
        };
    }

    private Control BuildDualGuitarGroup()
    {
        var group = CreateGroup("Modo Dos Guitarras");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        _twoGuitarMode.Text = "&Activar modo Dos Guitarras";
        _twoGuitarMode.AccessibleName = "Activar modo Dos Guitarras";
        _twoGuitarMode.AccessibleDescription = "Usa Input 1 para Guitarra 1 con un DSP independiente e Input 2 para Guitarra 2 con el rig principal. En este modo el micrófono de voz queda desactivado.";
        AddLabeledControl(table, "Modo:", _twoGuitarMode);

        ConfigureCombo(_dualEditGuitarCombo, "Guitarra a editar", "Seleccione Guitarra 1 o Guitarra 2. JAWS anunciará qué cadena corresponde a cada una.");
        _dualEditGuitarCombo.Items.AddRange(new object[]
        {
            "Guitarra 1 - Input 1 - efectos, IR y NAM independientes",
            "Guitarra 2 - Input 2 - efectos, IR y NAM independientes, rig principal"
        });
        _dualEditGuitarCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Guitarra a &editar:", _dualEditGuitarCombo);

        _dualQuickNamEnabled.Text = "Activar NAM de la guitarra seleccionada";
        _dualQuickNamEnabled.AccessibleName = "Activar NAM de la guitarra seleccionada";
        _dualQuickNamEnabled.AccessibleDescription = "Control rápido ligado a Guitarra a editar. Alt+O sólo reúne la operación inmediata de NAM; los trims, gabinete, Auto Level y demás ajustes técnicos están en Alt+N.";
        AddLabeledControl(table, "NAM rápido, estado:", _dualQuickNamEnabled);

        ConfigureCombo(_dualQuickNamCombo, "NAM rápido de la guitarra seleccionada", "Lista completa de la biblioteca NAM. Use flechas para elegir y Enter para cargar la captura en la guitarra seleccionada, sin ir a la sección NAM.");
        AddLabeledControl(table, "NAM rápido, captura:", _dualQuickNamCombo);

        var dualQuickNamButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _dualQuickNamLoadButton.Text = "Cargar NAM";
        _dualQuickNamLoadButton.AccessibleName = "Cargar NAM en la guitarra seleccionada";
        _dualQuickNamPreviousButton.Text = "NAM anterior";
        _dualQuickNamPreviousButton.AccessibleName = "NAM anterior de la guitarra seleccionada";
        _dualQuickNamNextButton.Text = "NAM siguiente";
        _dualQuickNamNextButton.AccessibleName = "NAM siguiente de la guitarra seleccionada";
        dualQuickNamButtons.Controls.AddRange(new Control[] { _dualQuickNamLoadButton, _dualQuickNamPreviousButton, _dualQuickNamNextButton });
        AddLabeledControl(table, "NAM rápido, acciones:", dualQuickNamButtons);

        _dualQuickNamCurrent.ReadOnly = true;
        _dualQuickNamCurrent.Dock = DockStyle.Fill;
        _dualQuickNamCurrent.AccessibleName = "NAM actualmente cargado en la guitarra seleccionada";
        _dualQuickNamCurrent.Text = "Sin modelo NAM cargado.";
        AddLabeledControl(table, "NAM rápido, actual:", _dualQuickNamCurrent);

        ConfigureCombo(_dualFactoryBankCombo, "Banco de fábrica para la guitarra seleccionada", "Los treinta bancos de fábrica pueden cargarse sólo en la guitarra elegida. No modifican la otra guitarra ni se mezclan con los bancos personales.");
        foreach (UserPreset factoryPreset in FactoryPresetBank.Presets) _dualFactoryBankCombo.Items.Add(factoryPreset.Name);
        if (_dualFactoryBankCombo.Items.Count > 0) _dualFactoryBankCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Banco de fábrica para la guitarra seleccionada:", _dualFactoryBankCombo);

        _dualFactoryBankLoadButton.Text = "Cargar fábrica";
        _dualFactoryBankLoadButton.AccessibleName = "Cargar banco de fábrica en la guitarra seleccionada";
        _dualFactoryBankLoadButton.AccessibleDescription = "Aplica el banco de fábrica elegido solamente a la guitarra seleccionada en Modo Dos Guitarras.";
        AddLabeledControl(table, "Acción de fábrica:", _dualFactoryBankLoadButton);

        ConfigureCombo(_dualBankCombo, "Banco personal de la guitarra seleccionada", "Lista de bancos personales guardados para la guitarra elegida. Guitarra 1 y Guitarra 2 nunca comparten esta lista.");
        AddLabeledControl(table, "Banco personal de la guitarra seleccionada:", _dualBankCombo);

        _dualBankName.MaxLength = 60;
        _dualBankName.Width = 360;
        _dualBankName.AccessibleName = "Nombre del banco de la guitarra seleccionada";
        _dualBankName.AccessibleDescription = "Escriba un nombre para guardar o actualizar un banco de la guitarra actualmente seleccionada.";
        AddLabeledControl(table, "Nombre del banco:", _dualBankName);

        var dualBankButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _dualBankSaveButton.Text = "Guardar banco";
        _dualBankSaveButton.AccessibleName = "Guardar banco de la guitarra seleccionada";
        _dualBankLoadButton.Text = "Cargar banco";
        _dualBankLoadButton.AccessibleName = "Cargar banco de la guitarra seleccionada";
        _dualBankDeleteButton.Text = "Eliminar banco";
        _dualBankDeleteButton.AccessibleName = "Eliminar banco de la guitarra seleccionada";
        dualBankButtons.Controls.AddRange(new Control[] { _dualBankSaveButton, _dualBankLoadButton, _dualBankDeleteButton });
        AddLabeledControl(table, "Acciones de banco:", dualBankButtons);

        ConfigureCombo(_dualSceneCombo, "Escena completa de Dos Guitarras", "Cada escena guarda simultáneamente ambas guitarras completas, sus NAM e IR, efectos, mezclas, paneos, volumen master y acompañamiento. Use flechas para elegir y Cargar para recuperar todo el conjunto.");
        AddLabeledControl(table, "Escena completa:", _dualSceneCombo);

        _dualSceneName.MaxLength = 60;
        _dualSceneName.Width = 360;
        _dualSceneName.AccessibleName = "Nombre de la escena completa de Dos Guitarras";
        _dualSceneName.AccessibleDescription = "Escriba un nombre descriptivo, por ejemplo Worship limpio o Vai más ritmo, y pulse Guardar escena completa.";
        AddLabeledControl(table, "Nombre de escena completa:", _dualSceneName);

        var dualSceneButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _dualSceneSaveButton.Text = "Guardar escena completa";
        _dualSceneSaveButton.AccessibleName = "Guardar escena completa de Dos Guitarras";
        _dualSceneLoadButton.Text = "Cargar escena completa";
        _dualSceneLoadButton.AccessibleName = "Cargar escena completa de Dos Guitarras";
        _dualSceneDeleteButton.Text = "Eliminar escena";
        _dualSceneDeleteButton.AccessibleName = "Eliminar escena completa de Dos Guitarras";
        _dualSceneNextButton.Text = "Escena siguiente";
        _dualSceneNextButton.AccessibleName = "Cargar escena completa siguiente";
        dualSceneButtons.Controls.AddRange(new Control[] { _dualSceneSaveButton, _dualSceneLoadButton, _dualSceneDeleteButton, _dualSceneNextButton });
        AddLabeledControl(table, "Acciones de escena completa:", dualSceneButtons);

        _guitar1ProcessingEnabled.Text = "&Procesar Guitarra 1 / Input 1";
        _guitar1ProcessingEnabled.AccessibleName = "Procesar Guitarra 1, Input 1";
        _guitar1ProcessingEnabled.AccessibleDescription = "Activado fuerza Input 1 a cien por ciento DSP y cero por ciento señal seca interna. Desactívelo sólo para comparar con la guitarra cruda.";
        _guitar1ProcessingEnabled.Checked = true;
        AddLabeledControl(table, "Guitarra 1, procesamiento:", _guitar1ProcessingEnabled);

        _guitar1UseRigEffects.Text = "&Activar efectos de Guitarra 1";
        _guitar1UseRigEffects.AccessibleName = "Activar efectos de Guitarra 1";
        _guitar1UseRigEffects.AccessibleDescription = "Activado: Guitarra 1 usa su memoria independiente de efectos. Seleccione Guitarra 1 en Guitarra a editar y luego cambie la sección Efectos. Guitarra 1 también puede usar su propio NAM e IR A independientes.";
        _guitar1UseRigEffects.Checked = true;
        AddLabeledControl(table, "Guitarra 1, efectos:", _guitar1UseRigEffects);

        ConfigureCombo(_guitar1AmpCombo, "Amplificador de Guitarra 1", "Amplificador interno independiente para la guitarra conectada en Input 1.");
        _guitar1AmpCombo.Items.AddRange(new object[]
        {
            "Limpio americano tipo Twin Reverb",
            "Crunch británico tipo Marshall",
            "Lead valvular cálido, alta ganancia",
            "Limpio Lone Star Style",
            "Limpio Class A brillante",
            "Crunch Plexi clásico",
            "Crunch Class A abierto",
            "Lead moderno apretado",
            "Lead Legacy cantado"
        });
        _guitar1AmpCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Guitarra 1, &amplificador:", _guitar1AmpCombo);
        AddLabeledControl(table, "Guitarra 1, &ganancia 0 a 10:", ConfigureNumeric(_guitar1Gain, "Ganancia de Guitarra 1"));
        AddLabeledControl(table, "Guitarra 1, &volumen DSP:", ConfigureNumeric(_guitar1Output, "Volumen de salida del amplificador DSP de Guitarra 1"));
        AddLabeledControl(table, "Guitarra 1, &nivel en mezcla:", ConfigureNumeric(_guitar1Mix, "Nivel de Guitarra 1 en la mezcla final"));
        ConfigureNumeric(_guitar1Pan, "Paneo de Guitarra 1");
        _guitar1Pan.AccessibleDescription = "Menos cien es totalmente izquierda, cero es centro y más cien es totalmente derecha. En centro el estéreo queda idéntico a la versión 2.41.26.";
        AddLabeledControl(table, "Guitarra 1, &paneo estéreo:", _guitar1Pan);
        _guitar1Mute.Text = "Silenciar Guitarra &1";
        _guitar1Mute.AccessibleName = "Silenciar Guitarra 1";
        AddLabeledControl(table, "Guitarra 1:", _guitar1Mute);

        _guitar1IrPath.ReadOnly = true;
        _guitar1IrPath.Width = 520;
        _guitar1IrPath.Text = "Guitarra 1: gabinete interno.";
        _guitar1IrPath.AccessibleName = "IR A de Guitarra 1";
        _guitar1IrPath.AccessibleDescription = "Archivo IR externo usado solamente por Guitarra 1. Si está vacío se usa el gabinete interno.";
        AddLabeledControl(table, "Guitarra 1, IR A:", _guitar1IrPath);

        var g1IrFileButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _guitar1LoadIrButton.Text = "Cargar IR Guitarra 1";
        _guitar1LoadIrButton.AccessibleName = "Cargar archivo IR A de Guitarra 1";
        _guitar1ClearIrButton.Text = "Quitar IR Guitarra 1";
        _guitar1ClearIrButton.AccessibleName = "Quitar IR externo de Guitarra 1 y volver al gabinete interno";
        _guitar1ClearIrButton.Enabled = false;
        g1IrFileButtons.Controls.AddRange(new Control[] { _guitar1LoadIrButton, _guitar1ClearIrButton });
        AddLabeledControl(table, "Guitarra 1, acciones IR:", g1IrFileButtons);

        _guitar1IrBrowserStatus.ReadOnly = true;
        _guitar1IrBrowserStatus.Width = 520;
        _guitar1IrBrowserStatus.Text = "Carpeta IR de Guitarra 1 no seleccionada.";
        _guitar1IrBrowserStatus.AccessibleName = "Explorador de carpeta IR de Guitarra 1";
        AddLabeledControl(table, "Guitarra 1, carpeta IR:", _guitar1IrBrowserStatus);

        var g1IrBrowserButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _guitar1SelectIrFolderButton.Text = "Elegir carpeta IR Guitarra 1";
        _guitar1SelectIrFolderButton.AccessibleName = "Elegir carpeta de IR para Guitarra 1";
        _guitar1PreviousIrButton.Text = "IR anterior Guitarra 1";
        _guitar1PreviousIrButton.AccessibleName = "Cargar IR anterior de Guitarra 1";
        _guitar1NextIrButton.Text = "IR siguiente Guitarra 1";
        _guitar1NextIrButton.AccessibleName = "Cargar IR siguiente de Guitarra 1";
        _guitar1PreviousIrButton.Enabled = false;
        _guitar1NextIrButton.Enabled = false;
        g1IrBrowserButtons.Controls.AddRange(new Control[] { _guitar1SelectIrFolderButton, _guitar1PreviousIrButton, _guitar1NextIrButton });
        AddLabeledControl(table, "Guitarra 1, recorrer IR:", g1IrBrowserButtons);

        var guitar2Info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Text = "Guitarra 2 usa el rig principal actual. Si NAM está activo, Guitarra 2 usa NAM; si NAM está apagado, usa el amplificador seleccionado en Equipo. La sección Efectos edita solamente la guitarra elegida en Guitarra a editar.",
            AccessibleName = "Cadena de Guitarra 2",
            AccessibleDescription = "Guitarra 2 usa el rig principal y NAM actual. Cada guitarra conserva su propia memoria de efectos."
        };
        AddLabeledControl(table, "Guitarra 2, cadena:", guitar2Info);
        AddLabeledControl(table, "Guitarra 2, &nivel en mezcla:", ConfigureNumeric(_guitar2Mix, "Nivel de Guitarra 2 en la mezcla final"));
        ConfigureNumeric(_guitar2Pan, "Paneo de Guitarra 2");
        _guitar2Pan.AccessibleDescription = "Menos cien es totalmente izquierda, cero es centro y más cien es totalmente derecha. En centro el estéreo queda idéntico a la versión 2.41.26.";
        AddLabeledControl(table, "Guitarra 2, p&aneo estéreo:", _guitar2Pan);
        _guitar2Mute.Text = "Silenciar Guitarra &2";
        _guitar2Mute.AccessibleName = "Silenciar Guitarra 2";
        AddLabeledControl(table, "Guitarra 2:", _guitar2Mute);

        _dualGuitarStatusLabel.AutoSize = true;
        _dualGuitarStatusLabel.MaximumSize = new Size(760, 0);
        _dualGuitarStatusLabel.Text = "Modo Dos Guitarras desactivado. Los perfiles de clase funcionan exactamente como en 2.40.19.";
        _dualGuitarStatusLabel.AccessibleName = _dualGuitarStatusLabel.Text;
        AddLabeledControl(table, "Estado:", _dualGuitarStatusLabel);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Text = "Dos DSP separados, dos memorias de efectos, dos rutas de IR, dos instancias NAM, paneo estéreo independiente, bancos independientes y escenas completas. Una escena completa recupera ambas guitarras, sus mezclas, paneos, volumen master y acompañamiento con una sola acción. Alt+O queda orientado al trabajo rápido; los ajustes técnicos del NAM se concentran en Alt+N.",
            AccessibleName = "Alcance y organización del modo Dos Guitarras"
        };
        AddLabeledControl(table, "Información:", note);
        return group;
    }

    private Control BuildScenesGroup()
    {
        var group = CreateGroup("Escenas y sonidos guardados");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        ConfigureCombo(_sceneCombo, "Selector de escena",
            "Tres escenas persistentes. Al cambiar la selección se carga inmediatamente la escena elegida.");
        AddLabeledControl(table, "&Escena:", _sceneCombo);

        _sceneName.MaxLength = 40;
        _sceneName.Width = 360;
        _sceneName.AccessibleName = "Nombre de la escena seleccionada";
        _sceneName.AccessibleDescription = "Escriba un nombre y pulse Renombrar escena. El nombre no cambia el sonido hasta que guarde o cargue una escena.";
        AddLabeledControl(table, "&Nombre:", _sceneName);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _loadSceneButton.Text = "&Cargar escena";
        _loadSceneButton.AccessibleName = "Cargar escena seleccionada";
        _saveSceneButton.Text = "&Guardar configuración en escena";
        _saveSceneButton.AccessibleName = "Guardar la configuración actual en la escena seleccionada";
        _renameSceneButton.Text = "&Renombrar escena";
        _renameSceneButton.AccessibleName = "Renombrar escena seleccionada";
        _nextSceneButton.Text = "Escena &siguiente";
        _nextSceneButton.AccessibleName = "Cargar escena siguiente";
        buttons.Controls.AddRange(new Control[]
        {
            _loadSceneButton, _saveSceneButton, _renameSceneButton, _nextSceneButton
        });
        AddLabeledControl(table, "Acciones:", buttons);

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(790, 0),
            Text = "Cada escena guarda canal, la ecualización de ese canal, volumen, puerta, ambos overdrives, booster, gabinete o IR, loop, ambos chorus, delay y reverb. Además, limpio, crunch y lead recuerdan su propia ecualización al cambiar manualmente de canal. El bypass general, el afinador, el controlador ASIO y el modelo NAM quedan independientes.",
            AccessibleName = "Contenido de las escenas",
            AccessibleDescription = "Cada escena guarda el sonido completo, pero no modifica el afinador, el controlador ASIO, la entrada de guitarra ni el modelo NAM."
        };
        AddLabeledControl(table, "Información:", explanation);
        return group;
    }

    private Control BuildPresetsGroup()
    {
        var group = CreateGroup("Presets completos, ilimitados");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        ConfigureCombo(_factoryPresetCombo, "Banco de fábrica", "Treinta presets de fábrica de sólo lectura, listos para usar.");
        foreach (UserPreset factoryPreset in FactoryPresetBank.Presets) _factoryPresetCombo.Items.Add(factoryPreset.Name);
        if (_factoryPresetCombo.Items.Count > 0) _factoryPresetCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Preset de &fábrica:", _factoryPresetCombo);

        var factoryButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _loadFactoryPresetButton.Text = "&Cargar fábrica";
        _loadFactoryPresetButton.AccessibleName = "Cargar preset de fábrica seleccionado";
        _duplicateFactoryPresetButton.Text = "&Duplicar al banco de usuario";
        _duplicateFactoryPresetButton.AccessibleName = "Duplicar preset de fábrica seleccionado al banco de usuario";
        factoryButtons.Controls.AddRange(new Control[] { _loadFactoryPresetButton, _duplicateFactoryPresetButton });
        AddLabeledControl(table, "Acciones de fábrica:", factoryButtons);

        ConfigureCombo(_presetCombo, "Banco de usuario", "Presets ilimitados editables guardados en Documentos. Cada preset recuerda el sonido, el NAM y el orden de los pedales previos.");
        AddLabeledControl(table, "Preset de &usuario:", _presetCombo);

        _presetName.MaxLength = 60;
        _presetName.Width = 360;
        _presetName.AccessibleName = "Nombre del preset";
        AddLabeledControl(table, "N&ombre del preset:", _presetName);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _savePresetButton.Text = "&Guardar nuevo o reemplazar";
        _savePresetButton.AccessibleName = "Guardar preset completo";
        _loadPresetButton.Text = "&Cargar preset";
        _loadPresetButton.AccessibleName = "Cargar preset completo seleccionado";
        _deletePresetButton.Text = "&Eliminar preset";
        _deletePresetButton.AccessibleName = "Eliminar preset seleccionado";
        _openPresetFolderButton.Text = "Abrir &carpeta de presets";
        _openPresetFolderButton.AccessibleName = "Abrir carpeta de presets";
        buttons.Controls.AddRange(new Control[] { _savePresetButton, _loadPresetButton, _deletePresetButton, _openPresetFolderButton });
        AddLabeledControl(table, "Acciones:", buttons);

        var info = new Label
        {
            AutoSize = true, MaximumSize = new Size(790, 0),
            Text = "El banco de fábrica contiene 30 sonidos de sólo lectura. Puede cargarlos directamente o duplicarlos al banco de usuario para editarlos. Los presets de usuario son ilimitados y guardan canal, EQ, pedales, IR, acompañamiento, modelo NAM, ajustes NAM y el orden de la cadena previa.",
            AccessibleName = "Contenido de los presets completos"
        };
        AddLabeledControl(table, "Información:", info);
        return group;
    }

    private Control BuildBackupGroup()
    {
        var group = CreateGroup("Copia de seguridad y traslado");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _exportBackupButton.Text = "&Exportar copia completa";
        _exportBackupButton.AccessibleName = "Exportar copia de seguridad completa de Amp Accessible";
        _restoreBackupButton.Text = "&Restaurar copia";
        _restoreBackupButton.AccessibleName = "Restaurar copia de seguridad de Amp Accessible";
        _openBackupFolderButton.Text = "Abrir &carpeta de copias";
        _openBackupFolderButton.AccessibleName = "Abrir carpeta de copias de seguridad";
        buttons.Controls.AddRange(new Control[] { _exportBackupButton, _restoreBackupButton, _openBackupFolderButton });
        AddLabeledControl(table, "Seguridad:", buttons);

        var info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(790, 0),
            Text = "La copia completa guarda escenas, presets de usuario, bancos y escenas completas de Dos Guitarras, configuración de audio y voz, biblioteca NAM con sus archivos y los IR externos que existan en el momento de exportar. La restauración recompone rutas locales nuevas para NAM e IR, por lo que sirve también para trasladar Amp Accessible a otra PC.",
            AccessibleName = "Contenido de la copia de seguridad completa"
        };
        AddLabeledControl(table, "Información:", info);
        return group;
    }

    private Control BuildPreChainGroup()
    {
        var group = CreateGroup("Orden de la cadena previa al amplificador");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        _preChainList.Height = 125;
        _preChainList.Width = 420;
        _preChainList.AccessibleName = "Orden de pedales antes del amplificador o NAM";
        _preChainList.AccessibleDescription = "Use Control Alt flecha arriba o abajo para mover el pedal seleccionado. El primer elemento procesa primero.";
        AddLabeledControl(table, "&Cadena previa:", _preChainList);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _movePreEffectUpButton.Text = "Mover &arriba";
        _movePreEffectDownButton.Text = "Mover a&bajo";
        buttons.Controls.AddRange(new Control[] { _movePreEffectUpButton, _movePreEffectDownButton });
        AddLabeledControl(table, "Orden:", buttons);

        var info = new Label
        {
            AutoSize = true, MaximumSize = new Size(790, 0),
            Text = "El orden es real en el DSP: Booster, Compresor, Auto Wah, Supresor, EQ de 5 bandas, Boss OD-1, Overdrive, Distorsiones y Fuzz pueden moverse libremente. Si el EQ se configura después del amplificador, su posición en esta lista previa se ignora. Amplificador o NAM, gabinete/IR, loop y reverb permanecen después de esta cadena.",
            AccessibleName = "Explicación del orden de efectos"
        };
        AddLabeledControl(table, "Información:", info);
        return group;
    }

    private Control BuildMaintenanceGroup()
    {
        var group = CreateGroup("Instalación, reparación y actualizaciones");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _checkUpdatesButton.Text = "&Buscar actualizaciones";
        _checkUpdatesButton.AccessibleName = "Buscar actualizaciones de Amp Accessible";
        _installUpdatePackageButton.Text = "Instalar paquete &local";
        _installUpdatePackageButton.AccessibleName = "Instalar paquete incremental punto gdmupdate";
        _repairInstallationButton.Text = "&Reparar instalación";
        _repairInstallationButton.AccessibleName = "Reparar instalación de Amp Accessible";
        _uninstallApplicationButton.Text = "&Desinstalar";
        _uninstallApplicationButton.AccessibleName = "Desinstalar Amp Accessible";
        buttons.Controls.AddRange(new Control[] { _checkUpdatesButton, _installUpdatePackageButton, _repairInstallationButton, _uninstallApplicationButton });
        AddLabeledControl(table, "Mantenimiento:", buttons);

        var info = new Label
        {
            AutoSize = true, MaximumSize = new Size(790, 0),
            Text = "El instalador único registra Amp Accessible en Aplicaciones instaladas de Windows. Reparar vuelve a instalar los archivos del programa sin borrar NAM, presets, escenas ni copias. Las actualizaciones incrementales usan paquetes .gdmupdate y reemplazan solamente los archivos modificados.",
            AccessibleName = "Información de mantenimiento e instalación"
        };
        AddLabeledControl(table, "Información:", info);
        return group;
    }

    private Control BuildAudioDiagnosticsGroup()
    {
        var group = CreateGroup("Diagnóstico de audio accesible");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        _audioDiagnosticHealth.AutoSize = true;
        _audioDiagnosticHealth.MaximumSize = new Size(790, 0);
        _audioDiagnosticHealth.Text = "Estado general: audio detenido.";
        _audioDiagnosticHealth.AccessibleName = "Estado general del audio: detenido";
        AddLabeledControl(table, "Estado general:", _audioDiagnosticHealth);

        _audioDiagnosticReport.Multiline = true;
        _audioDiagnosticReport.ReadOnly = true;
        _audioDiagnosticReport.ScrollBars = ScrollBars.Vertical;
        _audioDiagnosticReport.WordWrap = true;
        _audioDiagnosticReport.Height = 330;
        _audioDiagnosticReport.Dock = DockStyle.Fill;
        _audioDiagnosticReport.TabStop = true;
        _audioDiagnosticReport.AccessibleName = "Informe completo de diagnóstico de audio";
        _audioDiagnosticReport.AccessibleDescription = "Texto de sólo lectura. Use flechas con JAWS para revisar cada línea del estado ASIO, buffer, carga DSP, NAM y errores.";
        AddLabeledControl(table, "Informe:", _audioDiagnosticReport);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _refreshAudioDiagnosticButton.Text = "&Actualizar diagnóstico";
        _refreshAudioDiagnosticButton.AccessibleName = "Actualizar diagnóstico de audio";
        _readAudioDiagnosticButton.Text = "&Leer resumen";
        _readAudioDiagnosticButton.AccessibleName = "Leer resumen del diagnóstico con JAWS";
        _copyAudioDiagnosticButton.Text = "&Copiar informe";
        _copyAudioDiagnosticButton.AccessibleName = "Copiar informe de diagnóstico al portapapeles";
        _saveAudioDiagnosticButton.Text = "&Guardar informe";
        _saveAudioDiagnosticButton.AccessibleName = "Guardar informe de diagnóstico detallado";
        _openAudioDiagnosticsFolderButton.Text = "Abrir &carpeta de diagnósticos";
        _openAudioDiagnosticsFolderButton.AccessibleName = "Abrir carpeta de diagnósticos de audio";
        buttons.Controls.AddRange(new Control[]
        {
            _refreshAudioDiagnosticButton, _readAudioDiagnosticButton, _copyAudioDiagnosticButton,
            _saveAudioDiagnosticButton, _openAudioDiagnosticsFolderButton
        });
        AddLabeledControl(table, "Acciones:", buttons);

        var info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(790, 0),
            Text = "El diagnóstico no modifica el sonido ni escribe continuamente en disco. Los contadores se reinician al iniciar una nueva sesión de audio. Si aparece un problema, use Guardar informe para conservar la telemetría reciente y compartirla sin tener que copiar mensajes de JAWS.",
            AccessibleName = "Información del diagnóstico de audio"
        };
        AddLabeledControl(table, "Información:", info);
        UpdateAudioDiagnosticPanel();
        return group;
    }

    private Control BuildAudioGroup()
    {
        var group = CreateGroup("Audio ASIO e interfaz de audio");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        ConfigureCombo(_driverCombo, "Controlador ASIO", "Lista de controladores ASIO detectados automáticamente. Amp Accessible recuerda el controlador usado anteriormente.");
        AddLabeledControl(table, "&Controlador ASIO:", _driverCombo);

        ConfigureCombo(_inputCombo, "Entrada de guitarra", "Entrada física donde está conectada la guitarra, normalmente 1 o 2.");
        AddLabeledControl(table, "&Entrada de guitarra:", _inputCombo);

        _masterVolume.AccessibleName = "Volumen master de Amp Accessible, porcentaje";
        _masterVolume.AccessibleDescription = "Volumen final del software. Baja conjuntamente guitarras, voz, acompañamientos, looper, Playback 1-2 y Loopback, sin modificar la ganancia de la interfaz ni los archivos grabados.";
        AddLabeledControl(table, "&Volumen master de Amp Accessible:", _masterVolume);

        ConfigureCombo(_bufferCombo, "Buffer de referencia",
            "Este valor sirve para comparar. No cambia el driver. El buffer real se modifica en el panel ASIO oficial de la interfaz seleccionada.");
        _bufferCombo.Items.AddRange(new object[] { "64 muestras, referencia", "128 muestras, referencia", "256 muestras, referencia", "512 muestras, referencia" });
        _bufferCombo.SelectedIndex = _audioPreferences.BufferSize switch
        {
            64 => 0,
            128 => 1,
            256 => 2,
            512 => 3,
            _ => 2
        };
        AddLabeledControl(table, "&Buffer de referencia:", _bufferCombo);

        _actualBufferLabel.AutoSize = true;
        _actualBufferLabel.Text = "Buffer efectivo: todavía no iniciado.";
        _actualBufferLabel.AccessibleName = "Buffer ASIO efectivo: todavía no iniciado";
        AddLabeledControl(table, "Buffer efectivo del driver:", _actualBufferLabel);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _refreshDriversButton.Text = "&Actualizar controladores";
        _refreshDriversButton.AccessibleName = "Actualizar controladores ASIO";
        _asioPanelButton.Text = "&Panel ASIO";
        _asioPanelButton.AccessibleName = "Abrir panel del controlador ASIO";
        _applyBufferButton.Text = "&Configurar buffer en panel ASIO";
        _applyBufferButton.AccessibleName = "Abrir panel ASIO de la interfaz seleccionada para cambiar el buffer real";
        _startStopButton.Text = "&Iniciar audio";
        _startStopButton.AccessibleName = "Iniciar audio";
        buttons.Controls.AddRange(new Control[]
        {
            _refreshDriversButton, _asioPanelButton, _applyBufferButton, _startStopButton
        });
        AddLabeledControl(table, "Acciones:", buttons);

        _diagnosticsLabel.AutoSize = true;
        _diagnosticsLabel.MaximumSize = new Size(790, 0);
        _diagnosticsLabel.Text = "Diagnóstico: audio detenido.";
        _diagnosticsLabel.AccessibleName = "Diagnóstico de audio: detenido";
        AddLabeledControl(table, "Diagnóstico:", _diagnosticsLabel);

        return group;
    }

    private void LoadMeetOutputDevices(bool announce)
    {
        string? selectedId = _meetOutputCombo.SelectedIndex >= 0 && _meetOutputCombo.SelectedIndex < _meetOutputDevices.Count
            ? _meetOutputDevices[_meetOutputCombo.SelectedIndex].Id
            : (string.IsNullOrWhiteSpace(_audioPreferences.MeetOutputDeviceId) ? null : _audioPreferences.MeetOutputDeviceId);
        _meetOutputDevices.Clear();
        _meetOutputCombo.Items.Clear();
        try
        {
            foreach (MeetOutputDevice device in AudioEngine.GetMeetOutputDevices())
            {
                _meetOutputDevices.Add(device);
                _meetOutputCombo.Items.Add(device.Name);
            }
            int selected = selectedId is null ? -1 : _meetOutputDevices.FindIndex(d => d.Id == selectedId);
            if (selected < 0)
                selected = _meetOutputDevices.FindIndex(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)
                    || d.Name.Contains("VoiceMeeter Input", StringComparison.OrdinalIgnoreCase));
            if (selected < 0 && _meetOutputDevices.Count > 0) selected = 0;
            _meetOutputCombo.SelectedIndex = selected;
            if (announce) SetStatus($"{_meetOutputDevices.Count} dispositivos de salida encontrados para videollamadas.");
        }
        catch (Exception ex)
        {
            if (announce) SetStatus($"No se pudieron enumerar las salidas para videollamadas: {ex.Message}", true);
        }
    }

    private bool SelectedMeetOutputIsVbCable()
    {
        return _meetOutputCombo.SelectedIndex >= 0
            && _meetOutputCombo.SelectedIndex < _meetOutputDevices.Count
            && _meetOutputDevices[_meetOutputCombo.SelectedIndex].Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase);
    }

    private int FindVbCableOutputIndex()
    {
        return _meetOutputDevices.FindIndex(d =>
            d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
    }

    private int FindFocusriteOutputIndex()
    {
        return _meetOutputDevices.FindIndex(d =>
            d.Name.Contains("Focusrite", StringComparison.OrdinalIgnoreCase)
            || d.Name.Contains("Scarlett", StringComparison.OrdinalIgnoreCase));
    }

    private bool SelectedMeetOutputIsFocusrite()
    {
        return _meetOutputCombo.SelectedIndex >= 0
            && _meetOutputCombo.SelectedIndex < _meetOutputDevices.Count
            && (_meetOutputDevices[_meetOutputCombo.SelectedIndex].Name.Contains("Focusrite", StringComparison.OrdinalIgnoreCase)
                || _meetOutputDevices[_meetOutputCombo.SelectedIndex].Name.Contains("Scarlett", StringComparison.OrdinalIgnoreCase));
    }

    private void CheckFocusriteLoopback(bool announce = true)
    {
        MeetInputDevice? loopback;
        try { loopback = AudioEngine.GetPreferredFocusriteLoopbackInputDevice(); }
        catch (Exception ex)
        {
            string fail = $"No se pudo consultar Loopback Focusrite: {ex.Message}. Mantenga Focusrite como micrófono en Zoom/Meet.";
            _focusriteLoopbackStatusLabel.Text = fail;
            _focusriteLoopbackStatusLabel.AccessibleName = fail;
            if (announce) SetStatus(fail, true);
            return;
        }

        if (loopback is null)
        {
            string missing = "Loopback Focusrite no expuesto. En Focusrite Notifier abra Expose/Hide Windows Channels y marque Loopback L + R. Hasta entonces mantenga Focusrite como micrófono en Zoom/Meet.";
            _focusriteLoopbackStatusLabel.Text = missing;
            _focusriteLoopbackStatusLabel.AccessibleName = missing;
            if (announce) SetStatus(missing, true);
            return;
        }

        bool hasMeter = AudioEngine.TryGetCapturePeak(loopback.Id, out float peak);
        double db = peak <= 0.000001f ? -120.0 : Math.Max(-120.0, 20.0 * Math.Log10(peak));
        string signal = !hasMeter ? "medidor no disponible"
            : peak >= 0.001f ? $"señal detectada, pico {db:0.0} dB"
            : "sin señal instantánea";
        string ok = $"Loopback Focusrite encontrado: {loopback.Name}; {signal}. Para evitar eco, no use la Focusrite como altavoz de Zoom/Meet mientras envía la llamada por Loopback.";
        _focusriteLoopbackStatusLabel.Text = ok;
        _focusriteLoopbackStatusLabel.AccessibleName = ok;
        if (announce) SetStatus(ok, hasMeter && peak < 0.001f);
    }

    private void RefreshFocusriteLoopbackToggleState()
    {
        bool active = (_guitarClassLoopbackSelected && IsGuitarClassDirectFocusriteLoopback()) ||
            (_meetOutputEnabled.Checked && SelectedMeetOutputIsFocusrite());

        _checkFocusriteLoopbackButton.Checked = active;
        _checkFocusriteLoopbackButton.Text = active
            ? "&Loopback Focusrite: activado"
            : "&Loopback Focusrite: desactivado";
        _checkFocusriteLoopbackButton.AccessibleName = active
            ? "Loopback Focusrite activado"
            : "Loopback Focusrite desactivado";
    }

    private bool PrepareFocusriteLoopbackOutput(string profileName, string conferencingApp)
    {
        MeetInputDevice? loopback;
        try { loopback = AudioEngine.GetPreferredFocusriteLoopbackInputDevice(); }
        catch { loopback = null; }
        if (loopback is null)
        {
            CheckFocusriteLoopback(announce: false);
            return false;
        }

        int focusriteIndex = FindFocusriteOutputIndex();
        if (focusriteIndex < 0)
        {
            LoadMeetOutputDevices(announce: false);
            focusriteIndex = FindFocusriteOutputIndex();
        }
        if (focusriteIndex < 0)
        {
            SetStatus($"{profileName}: Loopback L + R existe, pero no se encontró una salida de reproducción Focusrite. MODO SEGURO: mantenga Focusrite como micrófono en {conferencingApp}.", true);
            return false;
        }

        // En la 2i2 el Loopback recibe Playback 1-2. Enviamos la mezcla de videollamada
        // procesada a la salida Focusrite sólo cuando el usuario prepara esta ruta.
        _meetOutputCombo.SelectedIndex = focusriteIndex;
        if (!_meetOutputEnabled.Checked) _meetOutputEnabled.Checked = true;
        else ApplyMeetOutput();
        RefreshRecommendedConferenceMic(announce: false);
        CheckFocusriteLoopback(announce: false);
        RefreshFocusriteLoopbackToggleState();

        bool running = !_engine.IsRunning || _engine.IsMeetOutputRunning;
        string warning = $"{profileName}: ruta Focusrite Loopback preparada. En {conferencingApp} use {loopback.Name} como micrófono. IMPORTANTE: seleccione como altavoz una salida distinta de Focusrite para evitar que la voz de la otra persona vuelva al Loopback.";
        SetStatus(warning, !running);
        return running;
    }

    private MeetInputDevice? GetRecommendedConferenceMic()
    {
        try
        {
            if (SelectedMeetOutputIsFocusrite())
                return AudioEngine.GetPreferredFocusriteLoopbackInputDevice();
            if (SelectedMeetOutputIsVbCable())
                return AudioEngine.GetPreferredVbCableInputDevice();
            if (SelectedMeetOutputIsVoicemeeter())
                return AudioEngine.GetPreferredVoicemeeterB1InputDevice();

            // Prioridad 2.40.18: Loopback Focusrite si está expuesto; después VB-CABLE.
            return AudioEngine.GetPreferredFocusriteLoopbackInputDevice()
                ?? AudioEngine.GetPreferredVbCableInputDevice();
        }
        catch { return null; }
    }

    private string GetRecommendedConferenceMicName()
    {
        MeetInputDevice? device = GetRecommendedConferenceMic();
        return device?.Name ?? "micrófono virtual no encontrado";
    }

    private void RefreshRecommendedConferenceMic(bool announce)
    {
        MeetInputDevice? device = GetRecommendedConferenceMic();
        string text;
        bool error;
        if (device is null)
        {
            text = "No se encontró un micrófono virtual utilizable. MODO SEGURO: mantenga Focusrite como micrófono en Zoom/Meet.";
            error = true;
        }
        else if (IsGuitarClassDirectFocusriteLoopback())
        {
            text = $"Clase de Guitarra lista por Focusrite Loopback. Micrófono para Meet: {device.Name}. Playback 1-2 lleva guitarra procesada estéreo más voz procesada sin salida virtual duplicada. No use Focusrite como altavoz de la llamada para evitar eco.";
            error = false;
        }
        else if (SelectedMeetOutputIsFocusrite())
        {
            bool prepared = _meetOutputEnabled.Checked && (!_engine.IsRunning || _engine.IsMeetOutputRunning);
            text = prepared
                ? $"Ruta Focusrite Loopback preparada. Micrófono para Zoom/Meet: {device.Name}. No use Focusrite como altavoz de la llamada para evitar eco."
                : $"Loopback Focusrite disponible como {device.Name}, pero la ruta procesada todavía NO está preparada. Active Loopback Focusrite antes de cambiar el micrófono de Zoom/Meet.";
            error = !prepared;
        }
        else if (SelectedMeetOutputIsVbCable())
        {
            text = $"Ruta directa VB-CABLE lista. Micrófono para Zoom/Meet: {device.Name}.";
            error = false;
        }
        else if (device.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
        {
            text = $"Loopback Focusrite disponible como {device.Name}, pero la ruta procesada todavía NO está preparada. Active Loopback Focusrite antes de cambiar el micrófono de Zoom/Meet.";
            error = true;
        }
        else
        {
            text = $"Ruta VoiceMeeter avanzada. Micrófono para Zoom/Meet: {device.Name}. Compruebe señal antes de seleccionarlo.";
            error = false;
        }

        _recommendedConferenceMicLabel.Text = text;
        _recommendedConferenceMicLabel.AccessibleName = text;
        if (announce) SetStatus(text, error);
    }

    private void CheckConferenceMicSignal()
    {
        MeetInputDevice? device = GetRecommendedConferenceMic();
        if (device is null)
        {
            RefreshRecommendedConferenceMic(announce: true);
            return;
        }

        if (!AudioEngine.TryGetCapturePeak(device.Id, out float peak))
        {
            SetStatus($"{device.Name} existe, pero Windows no permitió leer su medidor. Mantenga Focusrite como micrófono hasta comprobar la ruta virtual.", true);
            return;
        }

        double db = peak <= 0.000001f ? -120.0 : Math.Max(-120.0, 20.0 * Math.Log10(peak));
        bool focusriteLoopback = device.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase);
        bool routePrepared = !focusriteLoopback || IsGuitarClassDirectFocusriteLoopback() ||
            (_meetOutputEnabled.Checked && (!_engine.IsRunning || _engine.IsMeetOutputRunning));
        if (peak >= 0.001f && routePrepared)
            SetStatus($"Micrófono virtual confirmado: {device.Name}. Señal detectada, pico instantáneo {db:0.0} dB y ruta de Amp Accessible preparada. Ya puede seleccionarlo en Zoom o Meet.");
        else if (peak >= 0.001f && focusriteLoopback)
            SetStatus($"{device.Name} muestra señal a {db:0.0} dB, pero la ruta procesada de Amp Accessible NO está preparada. Ese pico puede venir de otra fuente. Active Loopback Focusrite antes de usarlo en Zoom o Meet.", true);
        else
            SetStatus($"{device.Name} existe pero ahora no detecta voz. NO cambie Zoom/Meet desde la Focusrite hasta que esta prueba detecte señal y la ruta esté preparada.", true);
    }

    private void OpenVbCableOfficialPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://vb-audio.com/Cable/",
                UseShellExecute = true
            });
            SetStatus("Página oficial de VB-CABLE abierta en el navegador.");
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo abrir la página oficial de VB-CABLE: {ex.Message}", true);
        }
    }

    private int FindVoicemeeterOutputIndex()
    {
        return _meetOutputDevices.FindIndex(d =>
            d.Name.Contains("Voicemeeter Input", StringComparison.OrdinalIgnoreCase)
            || d.Name.Contains("VoiceMeeter Input", StringComparison.OrdinalIgnoreCase));
    }

    private bool SelectedMeetOutputIsVoicemeeter()
    {
        return _meetOutputCombo.SelectedIndex >= 0
            && _meetOutputCombo.SelectedIndex < _meetOutputDevices.Count
            && (_meetOutputDevices[_meetOutputCombo.SelectedIndex].Name.Contains("Voicemeeter Input", StringComparison.OrdinalIgnoreCase)
                || _meetOutputDevices[_meetOutputCombo.SelectedIndex].Name.Contains("VoiceMeeter Input", StringComparison.OrdinalIgnoreCase));
    }

    private VoicemeeterSetupResult PrepareVoicemeeterRouting(bool announce)
    {
        // En clases de inglés la salida virtual debe ser mono centrada. La compensación
        // de +3 dB se aplica sólo en VoiceMeeter, después del centrado, por lo que no
        // aumenta el retorno local. En clase de guitarra se conserva el estéreo y 0 dB.
        bool monoCenteredVoice = _voiceOnlyMode.Checked;
        float voicemeeterGainDb = monoCenteredVoice ? 3f : 0f;
        VoicemeeterSetupResult result = _voicemeeter.PreparePrimaryVirtualInputForVideoCall(monoCenteredVoice, voicemeeterGainDb);
        _voicemeeterStatusLabel.Text = result.Message;
        _voicemeeterStatusLabel.AccessibleName = $"Estado de VoiceMeeter: {result.Message}";
        if (announce)
        {
            string message = result.Success
                ? $"{result.Message} No necesita usar la interfaz de VoiceMeeter. En Zoom/Meet seleccione exactamente {GetRecommendedConferenceMicName()} como micrófono."
                : result.Message;
            SetStatus(message, !result.Success);
        }
        return result;
    }

    private void PrepareVoicemeeterForVideoCall()
    {
        int index = FindVoicemeeterOutputIndex();
        if (index < 0)
        {
            LoadMeetOutputDevices(announce: false);
            index = FindVoicemeeterOutputIndex();
        }
        if (index < 0)
        {
            SetStatus("No se encontró VoiceMeeter Input entre los dispositivos de salida de Windows.", true);
            return;
        }

        _meetOutputCombo.SelectedIndex = index;
        if (!_meetOutputEnabled.Checked)
            _meetOutputEnabled.Checked = true; // El evento aplica la salida.
        else
            ApplyMeetOutput();
        RefreshRecommendedConferenceMic(announce: false);
    }

    private void ApplyMeetOutput()
    {
        string? id = _meetOutputCombo.SelectedIndex >= 0 && _meetOutputCombo.SelectedIndex < _meetOutputDevices.Count
            ? _meetOutputDevices[_meetOutputCombo.SelectedIndex].Id : null;
        if (_meetOutputEnabled.Checked && id is null)
        {
            SetStatus("Seleccione un dispositivo de salida virtual para videollamadas.", true);
            return;
        }
        _audioPreferences.MeetOutputEnabled = _meetOutputEnabled.Checked;
        _audioPreferences.MeetOutputDeviceId = id ?? string.Empty;
        string message = _engine.ConfigureMeetOutput(id, _meetOutputEnabled.Checked);
        string? voicemeeterMessage = null;
        bool voicemeeterError = false;
        if (_meetOutputEnabled.Checked && SelectedMeetOutputIsVoicemeeter())
        {
            VoicemeeterSetupResult setup = PrepareVoicemeeterRouting(announce: false);
            voicemeeterMessage = setup.Message;
            voicemeeterError = !setup.Success;
        }
        UpdateParameters();
        SaveAudioPreferences();
        RefreshFocusriteLoopbackToggleState();
        string finalMessage = string.IsNullOrWhiteSpace(voicemeeterMessage) ? message : $"{message} {voicemeeterMessage}";
        SetStatus(finalMessage, voicemeeterError || (_meetOutputEnabled.Checked && !_engine.IsMeetOutputRunning && _engine.IsRunning));
    }

    private void ApplyVoiceClassPreset()
    {
        if (_twoGuitarMode.Checked) _twoGuitarMode.Checked = false;
        // Antes de silenciar el retorno para inglés, conservamos el último retorno
        // usado en clases de guitarra. Así volver a guitarra restaura exactamente
        // el valor que el usuario venía usando.
        if (!_voiceOnlyMode.Checked)
            _audioPreferences.GuitarClassMonitorPercent = (float)_voiceMonitorLevel.Value;

        // Si la 2.40.15 quedó guardada ya en Perfil Inglés con NAM activo, también
        // capturamos ese estado al aplicar por primera vez la 2.40.16. Repetir el
        // perfil Inglés después de apagar NAM no pisa el estado guardado.
        if (_namEnabled.Checked && _engine.Processor.HasNamModel)
            _audioPreferences.GuitarClassNamEnabled = true;
        else if (_audioPreferences.LastClassProfile != "Inglés")
            _audioPreferences.GuitarClassNamEnabled = false;

        _applyingClassProfile = true;
        try
        {
            _audioPreferences.LastClassProfile = "Inglés";
            _voiceOnlyMode.Checked = true;
            _voiceEnabled.Checked = true;
            _voiceSuppressorEnabled.Checked = true;
            SetNumeric(_voiceThreshold, -48f);
            SetNumeric(_voiceReduction, 24f);
            SetNumeric(_voiceRelease, 300f);
            SetNumeric(_voiceHighPass, 90f);
            SetNumeric(_voiceBass, 0f);
            SetNumeric(_voiceMid, 1.5f);
            SetNumeric(_voiceTreble, 2.0f);
            SetNumeric(_voiceLevel, 115f);
            SetNumeric(_voiceMonitorLevel, 0f);
            SetNumeric(_meetGuitarLevel, 0f);
            // 150 % llevaba el Loopback a aproximadamente -0,6 dBFS. 95 % es
            // 3,96 dB menos y deja margen seguro sin cambiar EQ, compresor ni puerta.
            SetNumeric(_meetVoiceLevel, 95f);

            // Inglés es voz pura: NAM queda apagado aunque haya un modelo cargado.
            // Su estado previo quedó guardado para restaurarlo al volver a Guitarra.
            if (_namEnabled.Checked) _namEnabled.Checked = false;
            UpdateClassProfileStatus();
        }
        finally
        {
            _applyingClassProfile = false;
        }

        ScheduleParameterUpdate();
        SaveAudioPreferences();
        PrepareClassVideoOutput("Clase de Inglés", "Zoom");
    }

    private bool IsGuitarClassDirectFocusriteLoopback()
    {
        if (_twoGuitarMode.Checked || _audioPreferences.LastClassProfile != "Guitarra" || _voiceOnlyMode.Checked || _meetOutputEnabled.Checked)
            return false;
        try { return AudioEngine.GetPreferredFocusriteLoopbackInputDevice() is not null; }
        catch { return false; }
    }

    private bool PrepareGuitarClassFocusriteLoopback(bool announce = true)
    {
        MeetInputDevice? loopback;
        try { loopback = AudioEngine.GetPreferredFocusriteLoopbackInputDevice(); }
        catch { loopback = null; }

        if (loopback is null)
        {
            _guitarClassLoopbackSelected = false;
            if (_meetOutputEnabled.Checked) _meetOutputEnabled.Checked = false;
            _audioPreferences.MeetOutputEnabled = false;
            _audioPreferences.MeetOutputDeviceId = string.Empty;
            _engine.ConfigureMeetOutput(null, false);
            SaveAudioPreferences();
            CheckFocusriteLoopback(announce: false);
            if (announce)
                SetStatus("Clase de Guitarra: Loopback L + R no está expuesto. MODO SEGURO: mantenga Focusrite como micrófono en Meet hasta exponer Loopback.", true);
            return false;
        }

        // Scarlett 2i2 4th Gen: el Loopback puede recibir la mezcla de Direct Monitor,
        // que incluye Playback 1-2. El audio ASIO principal de Amp Accessible ya sale
        // por Playback 1-2, por lo que NO abrimos una segunda salida WASAPI a la
        // Focusrite: eso duplicaría guitarra/voz en auriculares y en el Loopback.
        if (_meetOutputEnabled.Checked) _meetOutputEnabled.Checked = false;
        _audioPreferences.MeetOutputEnabled = false;
        _audioPreferences.MeetOutputDeviceId = string.Empty;
        _engine.ConfigureMeetOutput(null, false);
        _meetOutputCombo.SelectedIndex = -1;
        SaveAudioPreferences();
        _guitarClassLoopbackSelected = true;
        CheckFocusriteLoopback(announce: false);
        RefreshRecommendedConferenceMic(announce: false);
        RefreshFocusriteLoopbackToggleState();

        string monitor = $"{_voiceMonitorLevel.Value:0} por ciento";
        string message = $"Clase de Guitarra preparada para Focusrite Loopback. En Meet use {loopback.Name} como micrófono. La mezcla enviada por Playback 1-2 contiene guitarra procesada en estéreo y voz procesada con retorno {monitor}. No se abre una salida virtual adicional, para evitar doble monitoreo. En Focusrite Control 2, para recibir sólo el audio procesado, deje Playback 1-2 en la mezcla enviada a Loopback y silencie Analogue 1 y Analogue 2. El altavoz de Meet debe ser distinto de Focusrite para evitar eco de retorno.";
        if (announce) SetStatus(message);
        return true;
    }

    private void ApplyGuitarClassPreset()
    {
        if (_twoGuitarMode.Checked) _twoGuitarMode.Checked = false;
        _applyingClassProfile = true;
        try
        {
            _audioPreferences.LastClassProfile = "Guitarra";
            _voiceOnlyMode.Checked = false;
            _voiceEnabled.Checked = true;
            _voiceSuppressorEnabled.Checked = true;

            // Un poco más de reducción de ambiente que en inglés, pero con cierre
            // suficientemente lento para no comerse finales de palabras.
            SetNumeric(_voiceThreshold, -46f);
            SetNumeric(_voiceReduction, 30f);
            SetNumeric(_voiceRelease, 350f);
            SetNumeric(_voiceHighPass, 90f);
            SetNumeric(_voiceBass, 0f);
            SetNumeric(_voiceMid, 1.0f);
            SetNumeric(_voiceTreble, 1.5f);
            SetNumeric(_voiceLevel, 110f);
            SetNumeric(_voiceMonitorLevel, _audioPreferences.GuitarClassMonitorPercent);
            SetNumeric(_meetGuitarLevel, 100f);
            SetNumeric(_meetVoiceLevel, 145f);

            // Restaura NAM sólo si estaba activo antes de entrar en Inglés y el
            // modelo sigue cargado. Así el perfil de voz no altera el rig de guitarra.
            bool restoreNam = _audioPreferences.GuitarClassNamEnabled && _engine.Processor.HasNamModel;
            if (_namEnabled.Checked != restoreNam) _namEnabled.Checked = restoreNam;
            UpdateClassProfileStatus();
        }
        finally
        {
            _applyingClassProfile = false;
        }

        ScheduleParameterUpdate();
        SaveAudioPreferences();
        PrepareGuitarClassFocusriteLoopback(announce: true);
    }

    private void PrepareClassVideoOutput(string profileName, string conferencingApp)
    {
        _guitarClassLoopbackSelected = false;
        RefreshFocusriteLoopbackToggleState();
        // 2.40.18: Focusrite Loopback es la ruta integrada preferida, pero no se
        // fuerza si Windows no expone Loopback L + R. La seguridad de la clase tiene
        // prioridad: si no está disponible, se conserva el micrófono físico Focusrite.
        MeetInputDevice? loopback = null;
        try { loopback = AudioEngine.GetPreferredFocusriteLoopbackInputDevice(); } catch { }
        if (loopback is not null)
        {
            // No activamos automáticamente la ruta porque en Scarlett 2i2 Loopback
            // recibe Playback 1-2. Si Zoom también reproduce por Focusrite, la voz de
            // la profesora puede regresar a la llamada. El botón Comprobar Loopback
            // prepara la ruta de forma explícita y JAWS recuerda cambiar el altavoz.
            if (_meetOutputEnabled.Checked) _meetOutputEnabled.Checked = false;
            _meetOutputCombo.SelectedIndex = FindFocusriteOutputIndex();
            _audioPreferences.MeetOutputEnabled = false;
            SaveAudioPreferences();
            RefreshRecommendedConferenceMic(announce: false);
            CheckFocusriteLoopback(announce: false);
            SetStatus($"{profileName} aplicado. Loopback Focusrite disponible como {loopback.Name}, pero permanece en MODO SEGURO. Mantenga Focusrite como micrófono en {conferencingApp} hasta activar Loopback Focusrite y luego Comprobar micrófono de videollamada para confirmar señal. La ruta Loopback requiere que el altavoz de la llamada NO sea Focusrite.");
            return;
        }

        // Si el Loopback todavía no está expuesto, no arriesgamos la clase con otra
        // ruta automática. VB-CABLE y VoiceMeeter siguen disponibles manualmente.
        if (_meetOutputEnabled.Checked) _meetOutputEnabled.Checked = false;
        _meetOutputCombo.SelectedIndex = -1;
        _audioPreferences.MeetOutputEnabled = false;
        SaveAudioPreferences();
        RefreshRecommendedConferenceMic(announce: false);
        CheckFocusriteLoopback(announce: false);
        SetStatus($"{profileName} aplicado. Loopback Focusrite no está expuesto. MODO SEGURO: mantenga Focusrite como micrófono en {conferencingApp}. En Focusrite Notifier marque Loopback L + R y luego active Loopback Focusrite.", true);
    }

    private void UpdateClassProfileStatus()
    {
        string profile = _audioPreferences.LastClassProfile;
        string monitor = $"{_voiceMonitorLevel.Value:0} por ciento";
        _classProfileStatusLabel.Text = $"Perfil actual: {profile}. Retorno local de voz: {monitor}.";
        _classProfileStatusLabel.AccessibleName = $"Perfil de clase actual: {profile}. Retorno local de voz: {monitor}.";
    }

    private Control BuildVoiceGroup()
    {
        var group = CreateGroup("Voz, entrada 1 de la interfaz");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        _voiceEnabled.Text = "&Procesar y mezclar micrófono de entrada 1";
        _voiceEnabled.AccessibleName = "Procesar micrófono de la entrada 1";
        _voiceEnabled.AccessibleDescription = "Activa la cadena de voz independiente. Para guitarra y voz simultáneas seleccione la entrada 2 como entrada de guitarra.";
        AddLabeledControl(table, "Micrófono:", _voiceEnabled);

        _voiceOnlyMode.Text = "&Modo Voz / videollamadas: usar sólo el micrófono";
        _voiceOnlyMode.AccessibleName = "Modo Voz para videollamadas";
        _voiceOnlyMode.AccessibleDescription = "Silencia por completo guitarra, NAM, efectos, metrónomo, batería, bajo y piano. Sólo procesa la entrada 1 como micrófono para Zoom, Meet, Teams u otra aplicación.";
        AddLabeledControl(table, "Modo de uso:", _voiceOnlyMode);

        _voiceClassPresetButton.Text = "Modo &Clase de Inglés";
        _voiceClassPresetButton.AccessibleName = "Activar perfil Clase de Inglés";
        _voiceClassPresetButton.AccessibleDescription = "Usa sólo el micrófono, apaga el retorno local y NAM, reduce cerca de 4 dB la salida de voz al Loopback y silencia guitarra. Detecta Loopback Focusrite, pero lo deja en modo seguro hasta comprobarlo para no arriesgar la clase.";

        _guitarClassPresetButton.Text = "Modo Clase de &Guitarra";
        _guitarClassPresetButton.AccessibleName = "Activar perfil Clase de Guitarra";
        _guitarClassPresetButton.AccessibleDescription = "Restaura guitarra y micrófono, recupera el último retorno local de voz y el estado NAM anterior a Inglés. Si Loopback Focusrite está disponible, usa directamente Playback 1-2 para enviar guitarra procesada estéreo y voz procesada, sin abrir una segunda salida que duplique el monitoreo.";

        var classButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        classButtons.Controls.AddRange(new Control[] { _voiceClassPresetButton, _guitarClassPresetButton });
        AddLabeledControl(table, "Perfiles de clase:", classButtons);

        _classProfileStatusLabel.AutoSize = true;
        _classProfileStatusLabel.MaximumSize = new Size(790, 0);
        UpdateClassProfileStatus();
        AddLabeledControl(table, "Perfil actual:", _classProfileStatusLabel);

        _voiceSuppressorEnabled.Text = "Activar &supresor de ruido de voz";
        _voiceSuppressorEnabled.AccessibleName = "Activar supresor de ruido de voz";
        _voiceSuppressorEnabled.AccessibleDescription = "Expander suave: atenúa el ruido ambiente cuando usted no habla sin cortar bruscamente las palabras.";
        AddLabeledControl(table, "Supresor:", _voiceSuppressorEnabled);

        AddLabeledControl(table, "Umbral del supresor, menos 75 a menos 20 decibeles:",
            ConfigureNumeric(_voiceThreshold, "Umbral del supresor de voz en decibeles"));
        AddLabeledControl(table, "Reducción cuando está cerrado, 0 a 60 decibeles:",
            ConfigureNumeric(_voiceReduction, "Reducción de ruido de voz en decibeles"));
        AddLabeledControl(table, "Cierre suave, 40 a 1000 milisegundos:",
            ConfigureNumeric(_voiceRelease, "Tiempo de cierre del supresor de voz"));

        AddLabeledControl(table, "Filtro de graves, 50 a 180 hercios:",
            ConfigureNumeric(_voiceHighPass, "Filtro pasa altos de la voz en hercios"));
        AddLabeledControl(table, "EQ bajos, menos 12 a más 12 decibeles:",
            ConfigureNumeric(_voiceBass, "Ecualización de bajos de la voz"));
        AddLabeledControl(table, "EQ medios, menos 12 a más 12 decibeles:",
            ConfigureNumeric(_voiceMid, "Ecualización de medios de la voz"));
        AddLabeledControl(table, "EQ agudos, menos 12 a más 12 decibeles:",
            ConfigureNumeric(_voiceTreble, "Ecualización de agudos de la voz"));
        AddLabeledControl(table, "Nivel de voz procesada, 0 a 150 por ciento:",
            ConfigureNumeric(_voiceLevel, "Nivel interno del micrófono procesado"));
        AddLabeledControl(table, "Voz en mis auriculares, 0 a 150 por ciento:",
            ConfigureNumeric(_voiceMonitorLevel, "Nivel de voz en el monitoreo local"));

        _meetOutputEnabled.Text = "Activar &salida virtual para videollamadas";
        _meetOutputEnabled.AccessibleName = "Activar salida virtual para videollamadas";
        AddLabeledControl(table, "Salida para videollamadas:", _meetOutputEnabled);

        ConfigureCombo(_meetOutputCombo, "Dispositivo de salida para videollamadas",
            "Focusrite Loopback es la ruta integrada preferida cuando Loopback L + R está expuesto. VB-CABLE y VoiceMeeter quedan como alternativas manuales.");
        AddLabeledControl(table, "Dispositivo virtual:", _meetOutputCombo);

        var meetButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _refreshMeetOutputsButton.Text = "&Actualizar dispositivos";
        _refreshMeetOutputsButton.AccessibleName = "Actualizar dispositivos de salida para videollamadas";
        _applyMeetOutputButton.Text = "A&plicar salida para videollamadas";
        _applyMeetOutputButton.AccessibleName = "Aplicar dispositivo virtual para videollamadas";
        _prepareVoicemeeterButton.Text = "Preparar &VoiceMeeter automáticamente";
        _prepareVoicemeeterButton.AccessibleName = "Preparar VoiceMeeter automáticamente para Zoom";
        _prepareVoicemeeterButton.AccessibleDescription = "Configura VoiceMeeter por su API oficial. Envía la entrada virtual principal a B1, desactiva los buses A y deja el propio bus B1 sin mute, a 0 dB y en modo normal. En Modo Voz centra el micrófono en mono y aplica una compensación de 3 dB; en modo guitarra conserva el estéreo. No es necesario usar la interfaz visual de VoiceMeeter.";
        _checkConferenceMicButton.Text = "&Comprobar micrófono de videollamada";
        _checkConferenceMicButton.AccessibleName = "Comprobar micrófono de videollamada antes de entrar a Zoom o Meet";
        _checkConferenceMicButton.AccessibleDescription = "Hable mientras pulsa este botón. Amp Accessible verifica Loopback Focusrite, CABLE Output o VoiceMeeter B1 según la ruta seleccionada.";
        _checkFocusriteLoopbackButton.Appearance = Appearance.Button;
        _checkFocusriteLoopbackButton.AutoSize = true;
        _checkFocusriteLoopbackButton.Text = "&Loopback Focusrite: desactivado";
        _checkFocusriteLoopbackButton.AccessibleName = "Loopback Focusrite desactivado";
        _checkFocusriteLoopbackButton.AccessibleDescription = "Interruptor de Loopback Focusrite. Un toque lo deja activado y pulsado; el siguiente toque lo desactiva y lo libera. En Zoom o Meet use una salida de altavoces distinta de Focusrite para evitar eco.";
        _openVbCablePageButton.Text = "Abrir página oficial &VB-CABLE";
        _openVbCablePageButton.AccessibleName = "Abrir página oficial de VB-CABLE";
        _openVbCablePageButton.AccessibleDescription = "Abre en el navegador la página oficial de VB-Audio para descargar VB-CABLE. Amp Accessible no instala controladores de terceros automáticamente.";
        meetButtons.Controls.AddRange(new Control[] { _refreshMeetOutputsButton, _applyMeetOutputButton, _checkFocusriteLoopbackButton, _checkConferenceMicButton, _openVbCablePageButton, _prepareVoicemeeterButton });
        AddLabeledControl(table, "Acciones de videollamada:", meetButtons);

        _focusriteLoopbackStatusLabel.AutoSize = true;
        _focusriteLoopbackStatusLabel.MaximumSize = new Size(790, 0);
        _focusriteLoopbackStatusLabel.Text = "Loopback Focusrite: todavía no comprobado.";
        _focusriteLoopbackStatusLabel.AccessibleName = "Estado de Loopback Focusrite: todavía no comprobado";
        AddLabeledControl(table, "Loopback Focusrite:", _focusriteLoopbackStatusLabel);
        CheckFocusriteLoopback(announce: false);

        _voicemeeterStatusLabel.AutoSize = true;
        _voicemeeterStatusLabel.MaximumSize = new Size(790, 0);
        _voicemeeterStatusLabel.Text = "VoiceMeeter: todavía no comprobado.";
        _voicemeeterStatusLabel.AccessibleName = "Estado de VoiceMeeter: todavía no comprobado";
        AddLabeledControl(table, "VoiceMeeter accesible:", _voicemeeterStatusLabel);

        _recommendedConferenceMicLabel.AutoSize = true;
        _recommendedConferenceMicLabel.MaximumSize = new Size(790, 0);
        _recommendedConferenceMicLabel.Text = "Micrófono para Zoom/Meet: todavía no comprobado.";
        _recommendedConferenceMicLabel.AccessibleName = "Micrófono recomendado para Zoom o Meet: todavía no comprobado";
        AddLabeledControl(table, "Micrófono para Zoom / Meet:", _recommendedConferenceMicLabel);
        RefreshRecommendedConferenceMic(announce: false);

        AddLabeledControl(table, "Guitarra enviada a videollamada, 0 a 150 por ciento:",
            ConfigureNumeric(_meetGuitarLevel, "Nivel de guitarra procesada enviado a videollamada"));
        AddLabeledControl(table, "Micrófono enviado a videollamada, 0 a 150 por ciento:",
            ConfigureNumeric(_meetVoiceLevel, "Nivel de micrófono procesado enviado a videollamada"));

        var info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(790, 0),
            Text = "La voz no pasa por efectos de guitarra. En Clase de Guitarra, Scarlett 2i2 4th Gen usa directamente Playback 1-2 hacia Loopback L + R, sin una segunda salida que duplique el monitoreo. Para enviar sólo el audio procesado, en Focusrite Control 2 deje Playback 1-2 en la mezcla enviada a Loopback y silencie Analogue 1 y Analogue 2. El altavoz de Zoom/Meet debe ser una salida distinta de Focusrite para evitar eco. En Inglés se conserva el comportamiento estable de la versión anterior. VB-CABLE y VoiceMeeter quedan como alternativas manuales.",
            AccessibleName = "Información de la cadena de voz",
            AccessibleDescription = "La voz es independiente de la guitarra y permanece activa incluso durante el afinador. En Modo Voz se silencian todas las rutas de guitarra y acompañamiento. Use auriculares y no duplique el monitoreo directo."
        };
        AddLabeledControl(table, "Importante:", info);
        return group;
    }

    private Control BuildTunerGroup()
    {
        var group = CreateGroup("Afinador cromático accesible");
        group.Controls.Add(CreateTunerPanel());
        return group;
    }

    private Control CreateTunerPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;

        ConfigureCombo(_tunerGuitarCombo, "Guitarra a afinar",
            "En Modo Dos Guitarras elija Input 1 o Input 2. F9 lee sólo la guitarra elegida y Control F9 alterna rápidamente entre ambas. Fuera del modo dual se usa la entrada normal del rig principal.");
        _tunerGuitarCombo.Items.AddRange(new object[]
        {
            "Guitarra 1 / Input 1",
            "Guitarra 2 / Input 2"
        });
        _tunerGuitarCombo.SelectedIndex = 1;
        AddLabeledControl(table, "Guitarra a &afinar:", _tunerGuitarCombo);

        _tunerEnabled.Text = "Activar &afinador";
        _tunerEnabled.AccessibleName = "Activar afinador cromático";
        _tunerEnabled.AccessibleDescription = "Analiza la guitarra seleccionada antes del amplificador y de todos los efectos. En Modo Dos Guitarras la otra guitarra permanece independiente.";
        AddLabeledControl(table, "Estado:", _tunerEnabled);

        _tunerMuteOutput.Text = "&Silenciar amplificador mientras afino";
        _tunerMuteOutput.Checked = true;
        _tunerMuteOutput.AccessibleName = "Silenciar solamente la guitarra seleccionada mientras se usa el afinador";
        _tunerMuteOutput.AccessibleDescription = "En Modo Dos Guitarras silencia sólo la guitarra que se está afinando. La otra guitarra y el acompañamiento global continúan sonando.";
        AddLabeledControl(table, "Escucha de guitarra:", _tunerMuteOutput);

        _tunerSoundGuide.Text = "Activar &guía sonora";
        _tunerSoundGuide.AccessibleName = "Activar guía sonora del afinador";
        _tunerSoundGuide.AccessibleDescription =
            "Tono grave si la cuerda está baja, tono agudo si está alta y dos pulsos si está afinada.";
        AddLabeledControl(table, "Orientación por sonido:", _tunerSoundGuide);

        AddLabeledControl(table, "Referencia de La, de 430 a 450 hercios:",
            ConfigureNumeric(_tunerReferenceA, "Frecuencia de referencia para la nota La"));
        AddLabeledControl(table, "Volumen de guía sonora, de 0 a 100:",
            ConfigureNumeric(_tunerGuideVolume, "Volumen de la guía sonora y tonos de referencia"));

        _tunerStatus.ReadOnly = true;
        _tunerStatus.Multiline = true;
        _tunerStatus.ScrollBars = ScrollBars.Vertical;
        _tunerStatus.Height = 62;
        _tunerStatus.Dock = DockStyle.Fill;
        _tunerStatus.Text = "Afinador desactivado. Active el afinador y toque una cuerda.";
        _tunerStatus.AccessibleName = "Lectura del afinador: afinador desactivado";
        _tunerStatus.AccessibleDescription =
            "Muestra guitarra seleccionada, nota, octava, frecuencia, cents y si debe subir o bajar la afinación. Pulse F9 para leer y Control F9 para cambiar de guitarra en modo dual.";
        AddLabeledControl(table, "Lectura para JAWS:", _tunerStatus);

        _readTunerButton.Text = "&Leer afinación con JAWS, F9";
        _readTunerButton.AccessibleName = "Leer afinación con JAWS";
        _readTunerButton.AccessibleDescription =
            "Lleva el foco a la lectura actual para que JAWS la anuncie.";
        AddLabeledControl(table, "Lectura manual:", _readTunerButton);

        ConfigureCombo(_referenceToneCombo, "Tono de referencia para guitarra",
            "Seleccione una cuerda de guitarra o La 440 y pulse reproducir tono. La salida dura aproximadamente un segundo y siete décimas.");
        foreach (var tone in ReferenceTones)
        {
            _referenceToneCombo.Items.Add(tone.Name);
        }
        _referenceToneCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Tono de referencia:", _referenceToneCombo);

        _playReferenceToneButton.Text = "&Reproducir tono, F10";
        _playReferenceToneButton.AccessibleName = "Reproducir tono de referencia seleccionado durante cinco segundos";
        AddLabeledControl(table, "Acción sonora:", _playReferenceToneButton);
        return table;
    }

    private Control BuildAmpGroup()
    {
        var group = CreateGroup("Amplificador");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        _simulationEnabled.Text = "&Activar simulación completa, Control más B";
        _simulationEnabled.Checked = true;
        _simulationEnabled.AccessibleName = "Activar o desactivar toda la simulación";
        _simulationEnabled.AccessibleDescription = "Cuando se desactiva se escucha la guitarra directa con el volumen maestro, sin puerta, pedales, amplificador, gabinete ni efectos.";
        AddLabeledControl(table, "Bypass general:", _simulationEnabled);

        ConfigureCombo(_channelCombo, "Canal de amplificador", "Nueve modelos: tres limpios, tres crunch y tres lead.");
        _channelCombo.Items.AddRange(new object[]
        {
            "Canal 1: Limpio americano tipo Twin Reverb",
            "Canal 2: Crunch británico tipo Marshall",
            "Canal 3: Lead valvular cálido, alta ganancia",
            "Canal 4: Limpio Lone Star Style, cálido y con cuerpo",
            "Canal 5: Limpio Class A brillante",
            "Canal 6: Crunch Plexi clásico",
            "Canal 7: Crunch Class A abierto",
            "Canal 8: Lead moderno apretado",
            "Canal 9: Lead Legacy cantado"
        });
        _channelCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Modelo de &canal:", _channelCombo);

        var eqInfo = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Text = "Bajos, medios, agudos y presencia se recuerdan de forma independiente para cada canal.",
            AccessibleName = "Ecualización independiente por canal",
            AccessibleDescription = "Al cambiar entre limpio, crunch y lead se recuperan automáticamente los cuatro controles de ecualización de ese canal."
        };
        AddLabeledControl(table, "Ecualización por canal:", eqInfo);

        AddLabeledControl(table, "&Ganancia, de 0 a 10:", ConfigureNumeric(_gain, "Ganancia del amplificador"));
        AddLabeledControl(table, "&Bajos, de 0 a 10:", ConfigureNumeric(_bass, "Bajos"));
        AddLabeledControl(table, "&Medios, de 0 a 10:", ConfigureNumeric(_middle, "Medios"));
        AddLabeledControl(table, "&Agudos, de 0 a 10:", ConfigureNumeric(_treble, "Agudos"));
        AddLabeledControl(table, "P&resencia, de 0 a 10:", ConfigureNumeric(_presence, "Presencia"));
        AddLabeledControl(table, "Nivel de &salida seguro, de 0 a 100:", ConfigureNumeric(_output, "Nivel general de salida seguro. Valor inicial 25 por ciento"));
        return group;
    }

    private Control BuildIrGroup()
    {
        var group = CreateGroup("Gabinete e IR profesional, A y B");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        _externalIrEnabled.Text = "Usar &IR A externa cargada";
        _externalIrEnabled.Enabled = false;
        _externalIrEnabled.AccessibleName = "Usar IR A externa";
        _externalIrEnabled.AccessibleDescription = "Cuando está desactivada, la ruta A usa el gabinete interno estilo V30.";
        AddLabeledControl(table, "Gabinete A:", _externalIrEnabled);

        _irPath.ReadOnly = true;
        _irPath.Text = "IR A: interno estilo V30.";
        _irPath.AccessibleName = "Archivo IR A";
        _irPath.Dock = DockStyle.Fill;
        AddLabeledControl(table, "Archivo IR A:", _irPath);

        var buttonsA = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _loadIrButton.Text = "&Cargar IR A";
        _loadIrButton.AccessibleName = "Cargar archivo IR A";
        _clearIrButton.Text = "&Quitar IR A";
        _clearIrButton.AccessibleName = "Quitar IR A y volver al gabinete interno estilo V30";
        _clearIrButton.Enabled = false;
        buttonsA.Controls.AddRange(new Control[] { _loadIrButton, _clearIrButton });
        AddLabeledControl(table, "Acciones IR A:", buttonsA);

        _irBrowserStatus.ReadOnly = true;
        _irBrowserStatus.Text = "Carpeta IR no seleccionada.";
        _irBrowserStatus.AccessibleName = "Explorador de carpeta IR";
        _irBrowserStatus.AccessibleDescription = "Muestra la carpeta de respuestas impulsionales y la posición del IR actual dentro de la lista.";
        _irBrowserStatus.Dock = DockStyle.Fill;
        AddLabeledControl(table, "Explorador de carpeta IR:", _irBrowserStatus);

        var browserButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _selectIrFolderButton.Text = "Elegir carpeta de IR";
        _selectIrFolderButton.AccessibleName = "Elegir carpeta de respuestas impulsionales";
        _selectIrFolderButton.AccessibleDescription = "Indexa los archivos WAV, WAVE, AIF y AIFF de una carpeta para recorrerlos sin volver al Explorador.";
        _previousIrButton.Text = "IR anterior";
        _previousIrButton.AccessibleName = "Cargar IR anterior de la carpeta";
        _previousIrButton.AccessibleDescription = "Carga en IR A el archivo anterior de la carpeta y JAWS anuncia su nombre y posición.";
        _nextIrButton.Text = "IR siguiente";
        _nextIrButton.AccessibleName = "Cargar IR siguiente de la carpeta";
        _nextIrButton.AccessibleDescription = "Carga en IR A el archivo siguiente de la carpeta y JAWS anuncia su nombre y posición.";
        _previousIrButton.Enabled = false;
        _nextIrButton.Enabled = false;
        browserButtons.Controls.AddRange(new Control[] { _selectIrFolderButton, _previousIrButton, _nextIrButton });
        AddLabeledControl(table, "Recorrer IR A:", browserButtons);

        _externalIrBEnabled.Text = "Usar IR &B externa cargada";
        _externalIrBEnabled.Enabled = false;
        _externalIrBEnabled.AccessibleName = "Usar IR B externa";
        _externalIrBEnabled.AccessibleDescription = "Activa el segundo IR para mezclarlo en paralelo con el gabinete A.";
        AddLabeledControl(table, "Gabinete B:", _externalIrBEnabled);

        _irPathB.ReadOnly = true;
        _irPathB.Text = "Ningún IR B cargado.";
        _irPathB.AccessibleName = "Archivo IR B";
        _irPathB.Dock = DockStyle.Fill;
        AddLabeledControl(table, "Archivo IR B:", _irPathB);

        var buttonsB = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _loadIrBButton.Text = "Cargar IR &B";
        _loadIrBButton.AccessibleName = "Cargar archivo IR B";
        _clearIrBButton.Text = "Quitar I&R B";
        _clearIrBButton.AccessibleName = "Quitar archivo IR B";
        _clearIrBButton.Enabled = false;
        buttonsB.Controls.AddRange(new Control[] { _loadIrBButton, _clearIrBButton });
        AddLabeledControl(table, "Acciones IR B:", buttonsB);

        AddLabeledControl(table, "&Mezcla IR B, 0 a 100 por ciento:", ConfigureNumeric(_irMix, "Mezcla del IR B. Cero usa solamente A; cien usa solamente B"));
        _irBPhaseInvert.Text = "Invertir &fase del IR B";
        _irBPhaseInvert.AccessibleName = "Invertir fase del IR B";
        _irBPhaseInvert.AccessibleDescription = "Útil para corregir cancelaciones cuando se mezclan dos respuestas impulsionales.";
        AddLabeledControl(table, "Fase:", _irBPhaseInvert);
        AddLabeledControl(table, "Low-&cut de gabinete, hercios:", ConfigureNumeric(_irLowCut, "Low cut del gabinete en hercios"));
        AddLabeledControl(table, "High-c&ut de gabinete, hercios:", ConfigureNumeric(_irHighCut, "High cut del gabinete en hercios"));

        var info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Text = "IR A puede ser externo o el V30 interno. El explorador de carpeta permite recorrer muchas tomas con IR anterior y siguiente sin abrir el Explorador. IR B es opcional. La mezcla, fase y filtros se aplican antes del loop de efectos.",
            AccessibleName = "Información de IR dual y explorador de carpeta"
        };
        AddLabeledControl(table, "Funcionamiento:", info);
        return group;
    }

    private Control BuildNamGroup()
    {
        var group = CreateGroup("Neural Amp Modeler, archivos .nam");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Text = "Alt+O queda para elegir y cargar rápidamente el NAM de Guitarra 1 o Guitarra 2. Alt+N concentra los ajustes avanzados y la organización de la biblioteca. Elija primero qué guitarra NAM desea editar; sólo se muestran los controles técnicos de esa guitarra.",
            AccessibleName = "Organización del modo NAM"
        };
        AddLabeledControl(table, "Funcionamiento:", explanation);

        ConfigureCombo(_namEditGuitarCombo, "Guitarra NAM a editar", "Elija Guitarra 1 o Guitarra 2. Alt+N mostrará sólo los ajustes avanzados del NAM de esa guitarra. La carga rápida de capturas permanece en Alt+O.");
        _namEditGuitarCombo.Items.AddRange(new object[]
        {
            "Guitarra 1 - Input 1 - NAM independiente",
            "Guitarra 2 - Input 2 - NAM del rig principal"
        });
        _namEditGuitarCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Guitarra NAM a &editar:", _namEditGuitarCombo);

        _namAdvancedHost.AutoSize = true;
        _namAdvancedHost.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _namAdvancedHost.Dock = DockStyle.Fill;
        _namAdvancedHost.TabStop = false;

        _namGuitar1AdvancedGroup.Text = "Ajustes NAM de Guitarra 1";
        _namGuitar1AdvancedGroup.AutoSize = true;
        _namGuitar1AdvancedGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _namGuitar1AdvancedGroup.Dock = DockStyle.Top;
        _namGuitar1AdvancedGroup.Padding = new Padding(8);
        _namGuitar1AdvancedGroup.TabStop = false;
        var g1NamAdvancedTable = CreateTwoColumnTable();
        _namGuitar1AdvancedGroup.Controls.Add(g1NamAdvancedTable);

        _guitar1NamPath.ReadOnly = true;
        _guitar1NamPath.Dock = DockStyle.Fill;
        _guitar1NamPath.Text = "Guitarra 1: ningún modelo NAM cargado.";
        _guitar1NamPath.AccessibleName = "Archivo NAM cargado en Guitarra 1";
        AddLabeledControl(g1NamAdvancedTable, "Modelo actual:", _guitar1NamPath);

        _guitar1NamIncludesCabinet.Text = "El NAM de Guitarra 1 ya incluye &gabinete y micrófono";
        _guitar1NamIncludesCabinet.AccessibleName = "Modelo NAM de Guitarra 1 incluye gabinete";
        _guitar1NamIncludesCabinet.AccessibleDescription = "Marcado: Guitarra 1 omite su IR externo después del NAM. Desmarcado: usa el IR o gabinete de Guitarra 1.";
        AddLabeledControl(g1NamAdvancedTable, "Tipo de captura:", _guitar1NamIncludesCabinet);
        AddLabeledControl(g1NamAdvancedTable, "Ajuste de &entrada NAM, dB:", ConfigureNumeric(_guitar1NamInputTrim, "Ajuste de entrada NAM de Guitarra 1 en decibeles"));
        AddLabeledControl(g1NamAdvancedTable, "Ajuste de &salida NAM, dB:", ConfigureNumeric(_guitar1NamOutputTrim, "Ajuste manual de salida NAM de Guitarra 1. Cero conserva el margen seguro automático."));
        _guitar1NamAutoLevel.Text = "Activar nivelación &automática NAM de Guitarra 1";
        _guitar1NamAutoLevel.AccessibleName = "Activar Auto Level NAM de Guitarra 1";
        AddLabeledControl(g1NamAdvancedTable, "Auto Level:", _guitar1NamAutoLevel);
        AddLabeledControl(g1NamAdvancedTable, "Compensación automática, dB:", ConfigureNumeric(_guitar1NamAutoLevelDb, "Compensación Auto Level NAM de Guitarra 1 en decibeles"));

        _namGuitar2AdvancedGroup.Text = "Ajustes NAM de Guitarra 2";
        _namGuitar2AdvancedGroup.AutoSize = true;
        _namGuitar2AdvancedGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _namGuitar2AdvancedGroup.Dock = DockStyle.Top;
        _namGuitar2AdvancedGroup.Padding = new Padding(8);
        _namGuitar2AdvancedGroup.TabStop = false;
        var g2NamAdvancedTable = CreateTwoColumnTable();
        _namGuitar2AdvancedGroup.Controls.Add(g2NamAdvancedTable);

        _namPath.ReadOnly = true;
        _namPath.Text = "Ningún modelo NAM cargado.";
        _namPath.Dock = DockStyle.Fill;
        _namPath.AccessibleName = "Archivo NAM cargado en Guitarra 2";
        AddLabeledControl(g2NamAdvancedTable, "Modelo actual:", _namPath);

        _namIncludesCabinet.Text = "El modelo NAM ya &incluye gabinete y micrófono";
        _namIncludesCabinet.AccessibleName = "Modelo NAM de Guitarra 2 incluye gabinete";
        _namIncludesCabinet.AccessibleDescription = "Marcado: se omite el gabinete interno y cualquier IR externo de Guitarra 2. Desmarcado: después del NAM se usa el gabinete o IR actual.";
        AddLabeledControl(g2NamAdvancedTable, "Tipo de captura:", _namIncludesCabinet);
        AddLabeledControl(g2NamAdvancedTable, "Ajuste de &entrada NAM, dB:", ConfigureNumeric(_namInputTrim, "Ajuste adicional de entrada al NAM de Guitarra 2 en decibeles"));
        AddLabeledControl(g2NamAdvancedTable, "Ajuste de &salida NAM, dB:", ConfigureNumeric(_namOutputTrim, "Ajuste manual de salida NAM de Guitarra 2. Cero conserva el margen seguro automático."));

        _namAutoLevel.Text = "Activar nivelación &automática NAM de Guitarra 2";
        _namAutoLevel.AccessibleName = "Activar Auto Level NAM de Guitarra 2";
        _namAutoLevel.AccessibleDescription = "Aplica solamente una compensación de volumen guardada para esta captura. No comprime ni cambia el carácter del modelo.";
        AddLabeledControl(g2NamAdvancedTable, "Auto Level:", _namAutoLevel);
        _namAutoLevelDb.ReadOnly = true;
        AddLabeledControl(g2NamAdvancedTable, "Compensación automática, dB:", ConfigureNumeric(_namAutoLevelDb, "Compensación Auto Level NAM de Guitarra 2 en decibeles"));

        _calibrateNamLevelButton.Text = "&Calibrar nivel NAM de Guitarra 2";
        _calibrateNamLevelButton.AccessibleName = "Calibrar nivel automático del NAM de Guitarra 2";
        _calibrateNamLevelButton.AccessibleDescription = "Toque la guitarra normalmente durante cuatro segundos. Amp Accessible medirá la salida de Guitarra 2 y guardará una compensación segura para esta captura.";
        AddLabeledControl(g2NamAdvancedTable, "Calibración:", _calibrateNamLevelButton);
        _namLevelStatus.ReadOnly = true;
        _namLevelStatus.Text = "Auto Level sin calibrar.";
        _namLevelStatus.AccessibleName = "Estado de Auto Level NAM de Guitarra 2";
        AddLabeledControl(g2NamAdvancedTable, "Nivel NAM:", _namLevelStatus);

        _namAdvancedHost.Controls.Add(_namGuitar2AdvancedGroup);
        _namAdvancedHost.Controls.Add(_namGuitar1AdvancedGroup);
        AddLabeledControl(table, "Ajustes NAM:", _namAdvancedHost);
        UpdateNamSettingsGuitarContext();

        _namBankFilterText.Dock = DockStyle.Fill;
        _namBankFilterText.AccessibleName = "Filtrar banco NAM por nombre o categoría";
        _namBankFilterText.AccessibleDescription = "Escriba una parte del nombre o de la categoría. La lista se actualiza mientras escribe.";
        AddLabeledControl(table, "&Filtrar banco NAM:", _namBankFilterText);

        _namCategoryFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _namCategoryFilter.Dock = DockStyle.Fill;
        _namCategoryFilter.AccessibleName = "Filtrar banco NAM por categoría";
        AddLabeledControl(table, "Categoría a &mostrar:", _namCategoryFilter);

        var filterActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _namFavoritesOnly.Text = "Mostrar sólo &favoritas";
        _namFavoritesOnly.AccessibleName = "Mostrar solamente capturas NAM favoritas";
        _clearNamFiltersButton.Text = "&Limpiar filtros";
        _clearNamFiltersButton.AccessibleName = "Limpiar filtros del banco NAM";
        filterActions.Controls.AddRange(new Control[] { _namFavoritesOnly, _clearNamFiltersButton });
        AddLabeledControl(table, "Filtros:", filterActions);

        _namBankFilterStatus.ReadOnly = true;
        _namBankFilterStatus.Dock = DockStyle.Fill;
        _namBankFilterStatus.AccessibleName = "Cantidad de capturas NAM visibles";
        AddLabeledControl(table, "Resultado del filtro:", _namBankFilterStatus);

        _namBankCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _namBankCombo.Dock = DockStyle.Fill;
        _namBankCombo.AccessibleName = "Biblioteca compartida de capturas NAM";
        _namBankCombo.AccessibleDescription = "Lista persistente y filtrable compartida por ambas guitarras. La guitarra elegida arriba determina el destino de Cargar seleccionada, anterior, siguiente y Enter.";
        AddLabeledControl(table, "Banco de &capturas NAM:", _namBankCombo);

        _namDisplayName.MaxLength = 80;
        _namDisplayName.Dock = DockStyle.Fill;
        _namDisplayName.AccessibleName = "Nombre de la captura NAM seleccionada";
        AddLabeledControl(table, "Nombre en &biblioteca:", _namDisplayName);

        _namCategoryEdit.DropDownStyle = ComboBoxStyle.DropDown;
        _namCategoryEdit.Dock = DockStyle.Fill;
        _namCategoryEdit.MaxLength = 40;
        _namCategoryEdit.AccessibleName = "Categoría de la captura NAM seleccionada";
        _namCategoryEdit.AccessibleDescription = "Puede elegir una categoría sugerida o escribir una propia.";
        _namCategoryEdit.Items.AddRange(new object[]
        {
            "Clean", "Crunch", "Lead", "High Gain", "Vintage", "Boutique",
            "Fender", "Marshall", "Mesa", "Vox", "Pedales", "Bajo", "Otros"
        });
        AddLabeledControl(table, "Categoría de la &captura:", _namCategoryEdit);

        var metadataActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _namFavorite.Text = "Marcar como &favorita";
        _namFavorite.AccessibleName = "Marcar captura NAM seleccionada como favorita";
        _saveNamMetadataButton.Text = "&Guardar nombre y categoría";
        _saveNamMetadataButton.AccessibleName = "Guardar nombre, categoría y estado favorito de la captura NAM";
        metadataActions.Controls.AddRange(new Control[] { _namFavorite, _saveNamMetadataButton });
        AddLabeledControl(table, "Organización:", metadataActions);

        var bankActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _loadNamBankButton.Text = "&Cargar seleccionada";
        _loadNamBankButton.AccessibleName = "Cargar captura NAM seleccionada del banco";
        _previousNamButton.Text = "NAM a&nterior";
        _previousNamButton.AccessibleName = "Cargar captura NAM anterior del banco";
        _nextNamButton.Text = "NAM &siguiente";
        _nextNamButton.AccessibleName = "Cargar captura NAM siguiente del banco";
        _removeNamBankButton.Text = "Quitar del &banco";
        _removeNamBankButton.AccessibleName = "Quitar la captura seleccionada del banco NAM";
        _openNamBankFolderButton.Text = "Abrir &carpeta";
        _openNamBankFolderButton.AccessibleName = "Abrir carpeta del banco de capturas NAM";
        bankActions.Controls.AddRange(new Control[] { _loadNamBankButton, _previousNamButton, _nextNamButton, _removeNamBankButton, _openNamBankFolderButton });
        AddLabeledControl(table, "Cambiar captura:", bankActions);

        _namSearchQuery.Dock = DockStyle.Fill;
        _namSearchQuery.AccessibleName = "Buscar capturas NAM en TONE3000";
        _namSearchQuery.AccessibleDescription = "Escriba por ejemplo Marshall JCM800, Fender Twin, Mesa Rectifier o el nombre del equipo que quiere buscar.";
        AddLabeledControl(table, "&Buscar captura:", _namSearchQuery);

        var searchActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _namSearchButton.Text = "Buscar en &TONE3000";
        _namSearchButton.AccessibleName = "Buscar capturas NAM en TONE3000";
        _namSearchButton.AccessibleDescription = "Abre el catálogo oficial TONE3000 ya filtrado a NAM y con el texto escrito en el buscador.";
        _loadLatestNamButton.Text = "Cargar última &descarga NAM";
        _loadLatestNamButton.AccessibleName = "Cargar la captura NAM descargada más recientemente";
        _loadLatestNamButton.AccessibleDescription = "Busca el último archivo punto NAM o ZIP con modelos NAM en la carpeta Descargas y lo prepara para cargar.";
        searchActions.Controls.AddRange(new Control[] { _namSearchButton, _loadLatestNamButton });
        AddLabeledControl(table, "Buscador de capturas:", searchActions);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _loadNamButton.Text = "&Cargar archivo NAM";
        _loadNamButton.AccessibleName = "Cargar archivo punto NAM";
        _clearNamButton.Text = "&Quitar NAM";
        _clearNamButton.AccessibleName = "Quitar modelo NAM y volver al amplificador interno";
        _checkNamButton.Text = "Comprobar &motor NAM";
        _checkNamButton.AccessibleName = "Comprobar disponibilidad del motor NAM nativo";
        _namGuideButton.Text = "Abrir &guía del motor NAM";
        _namGuideButton.AccessibleName = "Abrir instrucciones para instalar el motor NAM";
        actions.Controls.AddRange(new Control[] { _loadNamButton, _clearNamButton, _checkNamButton, _namGuideButton });
        AddLabeledControl(table, "Acciones:", actions);

        _namStatus.ReadOnly = true;
        _namStatus.Multiline = true;
        _namStatus.ScrollBars = ScrollBars.Vertical;
        _namStatus.Height = 95;
        _namStatus.Dock = DockStyle.Fill;
        _namStatus.AccessibleName = "Estado del motor y modelo NAM";
        AddLabeledControl(table, "Información:", _namStatus);

        RefreshNamCategoryFilter();
        RefreshNamBankCombo();
        RefreshNamStatus();
        return group;
    }

    private Control BuildMetronomeGroup()
    {
        var group = CreateGroup("Metrónomo, batería, bajo, piano y órgano de acompañamiento");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        var explanation = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Text = "El metrónomo, la batería, el bajo y el teclado de acompañamiento (piano u órgano) salen por la misma salida ASIO que la guitarra y el micrófono. Todos comparten BPM. F8 sirve para tap tempo. F12 arma o desarma juntos batería, bajo y teclado: cuando quedan armados esperan a que empiece la guitarra, arrancan desde el tiempo 1 y se detienen al dejar de tocar. Shift F8 controla sólo la batería, Shift F9 sólo el bajo, Control F12 controla sólo el teclado y Shift F12 cambia la variante de batería/bajo.",
            AccessibleName = "Explicación del metrónomo"
        };
        AddLabeledControl(table, "Funcionamiento:", explanation);

        _metronomeEnabled.Text = "&Activar metrónomo";
        _metronomeEnabled.AccessibleName = "Activar o desactivar metrónomo";
        _metronomeEnabled.AccessibleDescription = "El metrónomo se escucha cuando el audio ASIO está iniciado con F4.";
        AddLabeledControl(table, "Estado:", _metronomeEnabled);

        AddLabeledControl(table, "&Tempo, 40 a 240 BPM:",
            ConfigureNumeric(_metronomeBpm, "Tempo del metrónomo en pulsos por minuto"));

        ConfigureCombo(_metronomeMeter, "Compás del metrónomo",
            "Seleccione 2 por 4, 3 por 4, 4 por 4 o 6 por 8. El primer pulso puede llevar acento.");
        _metronomeMeter.Items.AddRange(new object[] { "2/4", "3/4", "4/4", "6/8" });
        _metronomeMeter.SelectedIndex = 2;
        AddLabeledControl(table, "&Compás:", _metronomeMeter);

        _metronomeAccent.Text = "&Acentuar el primer tiempo";
        _metronomeAccent.AccessibleName = "Acentuar primer tiempo del compás";
        AddLabeledControl(table, "Acento:", _metronomeAccent);

        AddLabeledControl(table, "&Volumen del click, 0 a 100 por ciento:",
            ConfigureNumeric(_metronomeVolume, "Volumen del click del metrónomo"));

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _tapTempoButton.Text = "&Tap tempo, F8";
        _tapTempoButton.AccessibleName = "Tap tempo; pulse varias veces al ritmo deseado";
        _restartMetronomeButton.Text = "Reiniciar en &tiempo 1";
        _restartMetronomeButton.AccessibleName = "Reiniciar metrónomo, batería, bajo y piano en el primer tiempo";
        actions.Controls.AddRange(new Control[] { _tapTempoButton, _restartMetronomeButton });
        AddLabeledControl(table, "Acciones:", actions);

        _drumsEnabled.Text = "Activar &batería de acompañamiento";
        _drumsEnabled.AccessibleName = "Activar o desactivar batería de acompañamiento";
        _drumsEnabled.AccessibleDescription = "La batería usa el mismo tempo BPM del metrónomo y sale por la misma salida ASIO.";
        AddLabeledControl(table, "Batería:", _drumsEnabled);

        ConfigureCombo(_drumPattern, "Patrón de batería",
            "Seleccione Rock 4 por 4, Worship 4 por 4, Pop 4 por 4, Balada 4 por 4, Blues Shuffle o Worship 6 por 8.");
        _drumPattern.Items.AddRange(new object[]
        {
            "Rock 4/4",
            "Worship 4/4",
            "Pop 4/4",
            "Balada 4/4",
            "Blues Shuffle",
            "Worship 6/8"
        });
        _drumPattern.SelectedIndex = 1;
        AddLabeledControl(table, "&Patrón de batería:", _drumPattern);

        AddLabeledControl(table, "Volumen de &batería, 0 a 100 por ciento:",
            ConfigureNumeric(_drumVolume, "Volumen de la batería de acompañamiento"));

        _backingBassEnabled.Text = "Activar ba&jo de acompañamiento";
        _backingBassEnabled.AccessibleName = "Activar o desactivar bajo de acompañamiento";
        _backingBassEnabled.AccessibleDescription = "El bajo comparte el BPM y el patrón rítmico de la batería, con tonalidad, modo, línea y volumen independientes.";
        AddLabeledControl(table, "Bajo:", _backingBassEnabled);

        ConfigureCombo(_backingBassKey, "Tonalidad del bajo",
            "Seleccione la tonalidad para el bajo de acompañamiento.");
        _backingBassKey.Items.AddRange(new object[]
        {
            "Do (C)", "Do sostenido (C#)", "Re (D)", "Mi bemol (Eb)",
            "Mi (E)", "Fa (F)", "Fa sostenido (F#)", "Sol (G)",
            "La bemol (Ab)", "La (A)", "Si bemol (Bb)", "Si (B)"
        });
        _backingBassKey.SelectedIndex = 7;
        AddLabeledControl(table, "&Tonalidad del bajo:", _backingBassKey);

        ConfigureCombo(_backingBassMode, "Modo mayor o menor del bajo",
            "El modo afecta especialmente a la línea melódica simple.");
        _backingBassMode.Items.AddRange(new object[] { "Mayor", "Menor" });
        _backingBassMode.SelectedIndex = 0;
        AddLabeledControl(table, "&Modo:", _backingBassMode);

        ConfigureCombo(_backingBassLine, "Tipo de línea de bajo",
            "Seleccione raíz, raíz quinta, octavas, línea melódica simple o Slap contundente con thumb y pop bien marcados.");
        _backingBassLine.Items.AddRange(new object[]
        {
            "Raíz",
            "Raíz - quinta",
            "Octavas",
            "Línea melódica simple",
            "Slap contundente"
        });
        _backingBassLine.SelectedIndex = 1;
        AddLabeledControl(table, "&Línea de bajo:", _backingBassLine);

        AddLabeledControl(table, "Volumen del ba&jo, 0 a 100 por ciento:",
            ConfigureNumeric(_backingBassVolume, "Volumen del bajo de acompañamiento"));

        _pianoEnabled.Text = "Activar &piano u órgano de acompañamiento";
        _pianoEnabled.AccessibleName = "Activar o desactivar piano u órgano de acompañamiento";
        _pianoEnabled.AccessibleDescription = "El teclado comparte el tempo global y puede usar piano u órgano con tonalidad, progresión, estilo, sonido y volumen propios.";
        AddLabeledControl(table, "Piano u órgano:", _pianoEnabled);

        ConfigureCombo(_pianoKey, "Tonalidad del piano",
            "Seleccione tonalidad mayor o menor. Las tonalidades menores cambian realmente la armonía de la progresión, no sólo el nombre.");
        _pianoKey.Items.AddRange(new object[]
        {
            "Do mayor (C)", "Do sostenido mayor (C#)", "Re mayor (D)", "Mi bemol mayor (Eb)",
            "Mi mayor (E)", "Fa mayor (F)", "Fa sostenido mayor (F#)", "Sol mayor (G)",
            "La bemol mayor (Ab)", "La mayor (A)", "Si bemol mayor (Bb)", "Si mayor (B)",
            "Do menor (Cm)", "Do sostenido menor (C#m)", "Re menor (Dm)", "Mi bemol menor (Ebm)",
            "Mi menor (Em)", "Fa menor (Fm)", "Fa sostenido menor (F#m)", "Sol menor (Gm)",
            "Sol sostenido menor (G#m)", "La menor (Am)", "Si bemol menor (Bbm)", "Si menor (Bm)"
        });
        _pianoKey.SelectedIndex = 7;
        AddLabeledControl(table, "Tonalidad del pia&no:", _pianoKey);

        // 2.41.52: por pedido del usuario, el tipo de piano/órgano queda inmediatamente
        // después de Tonalidad para que el recorrido con JAWS siga un orden musical lógico.
        ConfigureCombo(_pianoSound, "Tipo de piano u órgano",
            "Seleccione Piano acústico, Rhodes, Piano Worship, Concert Grand u Órgano Hammond Worship. El Hammond incluye drawbars, percusión suave, vibrato chorus leve y Leslie lento o rápido.");
        _pianoSound.Items.AddRange(new object[]
        {
            "Piano acústico",
            "Piano eléctrico Rhodes",
            "Piano Worship Alabanza",
            "Piano de cola brillante / Concert Grand",
            "Órgano Hammond Worship - Leslie lento",
            "Órgano Hammond Worship - Leslie rápido"
        });
        _pianoSound.SelectedIndex = 0;
        AddLabeledControl(table, "&Tipo de piano u órgano:", _pianoSound);

        ConfigureCombo(_pianoProgression, "Progresión de acordes del piano",
            "Las cinco primeras opciones son progresiones de fábrica. Personalizada permite escribir los grados en el orden exacto. Use por ejemplo I, V, V, vi, IV o I x2, V, vi x2, IV. Mayúscula significa acorde mayor y minúscula acorde menor.");
        _pianoProgression.Items.AddRange(new object[]
        {
            "I-V-vi-IV / i-v-VI-iv",
            "vi-IV-I-V / VI-iv-i-v",
            "I-IV-V-IV / i-iv-v-iv",
            "I-vi-IV-V / i-VI-iv-v",
            "i-VI-III-VII (menor worship)",
            "Personalizada: grados definidos por el usuario"
        });
        _pianoProgression.SelectedIndex = 0;
        AddLabeledControl(table, "Pro&gresión:", _pianoProgression);

        _pianoCustomProgression.Text = "I, V, vi, IV";
        _pianoCustomProgression.AccessibleName = "Grados de la progresión personalizada";
        _pianoCustomProgression.AccessibleDescription = "Escriba grados separados por coma. Ejemplo I, V, V, vi, IV. Para mantener un acorde varios compases use x2, x3 y así hasta x32. Para repetir un bloque completo use paréntesis y x cantidad, por ejemplo (I, V, vi, IV) x7. Puede combinar bloques y grados sueltos. Se admiten bemol o sostenido antes del grado, por ejemplo bVII; acordes disminuidos; y séptimas reales como V7, ii7, Imaj7 o vii°7.";
        AddLabeledControl(table, "&Grados personalizados:", _pianoCustomProgression);

        var customProgressionActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _pianoApplyCustomProgression.Text = "&Aplicar grados";
        _pianoApplyCustomProgression.AccessibleName = "Aplicar progresión personalizada";
        _pianoCustomProgressionName.Width = 150;
        _pianoCustomProgressionName.AccessibleName = "Nombre para guardar la progresión personalizada";
        _pianoCustomProgressionName.PlaceholderText = "Nombre";
        _pianoSaveCustomProgression.Text = "&Guardar";
        _pianoSaveCustomProgression.AccessibleName = "Guardar progresión personalizada";
        customProgressionActions.Controls.AddRange(new Control[] { _pianoApplyCustomProgression, _pianoCustomProgressionName, _pianoSaveCustomProgression });
        AddLabeledControl(table, "Editar progresión:", customProgressionActions);

        ConfigureCombo(_pianoSavedProgression, "Progresiones personalizadas guardadas",
            "Seleccione una progresión guardada y use Cargar. Eliminar borra solamente esta progresión de usuario.");
        var savedProgressionActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _pianoSavedProgression.Width = 220;
        _pianoLoadCustomProgression.Text = "&Cargar";
        _pianoLoadCustomProgression.AccessibleName = "Cargar progresión personalizada guardada";
        _pianoDeleteCustomProgression.Text = "&Eliminar";
        _pianoDeleteCustomProgression.AccessibleName = "Eliminar progresión personalizada guardada";
        savedProgressionActions.Controls.AddRange(new Control[] { _pianoSavedProgression, _pianoLoadCustomProgression, _pianoDeleteCustomProgression });
        AddLabeledControl(table, "Progresiones guardadas:", savedProgressionActions);

        ConfigureCombo(_pianoStyle, "Estilo de interpretación del piano",
            "Seleccione un estilo libre o uno equivalente al patrón de batería. Seguir patrón de batería adapta automáticamente el piano a Rock, Worship, Pop, Balada, Blues Shuffle o Worship 6 por 8.");
        _pianoStyle.Items.AddRange(new object[]
        {
            "Acordes sostenidos",
            "Arpegio",
            "Balada 4/4",
            "Worship 4/4",
            "Seguir patrón de batería",
            "Rock 4/4",
            "Pop 4/4",
            "Blues Shuffle",
            "Worship 6/8"
        });
        _pianoStyle.SelectedIndex = 3;
        AddLabeledControl(table, "Est&ilo del piano:", _pianoStyle);

        AddLabeledControl(table, "Volumen del p&iano, 0 a 100 por ciento:",
            ConfigureNumeric(_pianoVolume, "Volumen del piano de acompañamiento"));

        ConfigureCombo(_practiceRecordingDuration, "Duración de la grabación de práctica",
            "La grabación rápida dura como máximo cinco minutos.");
        _practiceRecordingDuration.Items.Add("5 minutos");
        _practiceRecordingDuration.SelectedIndex = 0;
        _practiceRecordingDuration.Enabled = false;
        AddLabeledControl(table, "Duración de &grabación:", _practiceRecordingDuration);

        var recordingActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _practiceRecordButton.Text = "&Grabar práctica";
        _practiceRecordButton.AccessibleName = "Iniciar grabación de práctica";
        _practiceRecordButton.AccessibleDescription = "Graba hasta cinco minutos de la mezcla final que escucha: guitarra procesada, voz, metrónomo, batería, bajo, piano y looper.";
        _practiceStopButton.Text = "&Detener y guardar";
        _practiceStopButton.AccessibleName = "Detener y guardar grabación de práctica";
        _practiceStopButton.Enabled = false;
        _practiceOpenFolderButton.Text = "Abrir &grabaciones";
        _practiceOpenFolderButton.AccessibleName = "Abrir carpeta de grabaciones";
        recordingActions.Controls.AddRange(new Control[] { _practiceRecordButton, _practiceStopButton, _practiceOpenFolderButton });
        AddLabeledControl(table, "Grabación rápida:", recordingActions);

        _practiceRecordingStatus.ReadOnly = true;
        _practiceRecordingStatus.Dock = DockStyle.Fill;
        _practiceRecordingStatus.Text = "Sin grabación en curso.";
        _practiceRecordingStatus.AccessibleName = "Estado de la grabación de práctica";
        AddLabeledControl(table, "Estado de grabación:", _practiceRecordingStatus);

        ConfigureCombo(_loopSourceCombo, "Fuente de captura del looper",
            "En Modo Dos Guitarras elija Guitarra 1, Guitarra 2 o Ambas. La fuente elegida se usa tanto en la primera vuelta como en cada overdub. Durante una captura activa el selector queda bloqueado para evitar cambios a mitad de vuelta. Fuera del modo dual el looper conserva el rig principal histórico.");
        _loopSourceCombo.Items.AddRange(new object[]
        {
            "Guitarra 1 / Input 1",
            "Guitarra 2 / Input 2",
            "Ambas guitarras"
        });
        _loopSourceCombo.SelectedIndex = 2;
        AddLabeledControl(table, "&Fuente del looper:", _loopSourceCombo);

        _loopSyncTempo.Text = "Sincronizar looper con &tempo";
        _loopSyncTempo.AccessibleName = "Sincronizar looper con tempo maestro";
        _loopSyncTempo.AccessibleDescription = "Desactivado conserva el looper manual. Activado cierra automáticamente la primera vuelta al completar la cantidad de compases elegida.";
        AddLabeledControl(table, "Sincronización del looper:", _loopSyncTempo);

        ConfigureCombo(_loopBars, "Cantidad de compases del looper sincronizado",
            "Seleccione uno, dos, cuatro u ocho compases. La primera vuelta se cerrará automáticamente.");
        _loopBars.Items.AddRange(new object[] { "1 compás", "2 compases", "4 compases", "8 compases" });
        _loopBars.SelectedIndex = 2;
        _loopBars.Enabled = false;
        AddLabeledControl(table, "&Compases del loop:", _loopBars);

        var looperActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _loopRecordButton.Text = "Grabar &loop";
        _loopRecordButton.AccessibleName = "Grabar primera vuelta del looper";
        _loopOverdubButton.Text = "&Overdub";
        _loopOverdubButton.AccessibleName = "Activar o desactivar overdub del looper";
        _loopUndoButton.Text = "Des&hacer último overdub";
        _loopUndoButton.AccessibleName = "Deshacer último overdub del looper";
        _loopUndoButton.AccessibleDescription = "Restaura el loop al estado que tenía justo antes de iniciar la última sesión de overdub.";
        _loopUndoButton.Enabled = false;
        _loopPlayButton.Text = "&Reproducir o detener";
        _loopPlayButton.AccessibleName = "Reproducir o detener looper";
        _loopSaveButton.Text = "&Guardar loop";
        _loopSaveButton.AccessibleName = "Guardar loop como archivo WAV";
        _loopClearButton.Text = "&Borrar loop";
        _loopClearButton.AccessibleName = "Borrar loop actual";
        looperActions.Controls.AddRange(new Control[] { _loopRecordButton, _loopOverdubButton, _loopUndoButton, _loopPlayButton, _loopSaveButton, _loopClearButton });
        AddLabeledControl(table, "Looper:", looperActions);

        _loopStatus.ReadOnly = true;
        _loopStatus.Dock = DockStyle.Fill;
        _loopStatus.Text = "Looper vacío.";
        _loopStatus.AccessibleName = "Estado del looper";
        AddLabeledControl(table, "Estado del looper:", _loopStatus);

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Text = "En Modo Dos Guitarras, Fuente del looper permite imprimir Guitarra 1, Guitarra 2 o ambas. Puede grabar una primera vuelta con una guitarra, cambiar la fuente mientras el loop reproduce y luego hacer overdub con la otra. El acompañamiento global nunca se imprime. F8 hace tap tempo. F12 arma o desarma batería, bajo y piano; al estar armados, cualquiera de las dos guitarras puede disparar el acompañamiento desde el tiempo 1 y el bus se detiene al dejar de tocar. Control F12 conserva el control individual del piano. Shift F2 controla la grabadora; Shift F3 inicia o cierra la primera vuelta manual; Shift F4 overdub; Shift F5 reproducir o detener; Shift F6 guardar y Shift F7 borrar. Con sincronización activada, Shift F3 inicia una vuelta de 1, 2, 4 u 8 compases que se cierra sola por compases. El botón Deshacer último overdub restaura la mezcla anterior. El looper admite hasta dos minutos.",
            AccessibleName = "Ayuda de herramientas y looper"
        };
        AddLabeledControl(table, "Ayuda:", help);

        return group;
    }

    private Control BuildEffectsSelectorGroup()
    {
        var group = CreateGroup("Efectos de guitarra, selector único");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        ConfigureCombo(_effectSelector, "Efecto a configurar",
            "Seleccione un efecto y solamente se mostrarán sus controles. El afinador ahora está en la pestaña Herramientas.");
        _effectSelector.Items.AddRange(new object[]
        {
            "Boosters",
            "Compresores",
            "Octavadores / Pitch",
            "Auto wah por dinámica",
            "Supresor de ruidos",
            "Overdrives",
            "OD-1 / Fulltone OCD",
            "Distorsiones",
            "Fuzz",
            "Ecualizador gráfico de 5 bandas",
            "Loop de efectos",
            "Phaser",
            "Flanger",
            "Chorus estéreo",
            "Chorus analógico tipo MXR",
            "MicroPitch 80s / Harmonizer",
            "Rotary Speaker / Leslie",
            "Tremolo",
            "Delay",
            "Reverbs"
        });
        AddLabeledControl(table, "&Efecto a configurar:", _effectSelector);

        _effectPanelHost.AutoSize = true;
        _effectPanelHost.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _effectPanelHost.Dock = DockStyle.Fill;
        _effectPanelHost.AccessibleName = string.Empty;
        _effectPanelHost.Padding = new Padding(2);

        _effectPanels.Add(CreateBoosterPanel());
        _effectPanels.Add(CreateCompressorPanel());
        _effectPanels.Add(CreateOctaverPanel());
        _effectPanels.Add(CreateAutoWahPanel());
        _effectPanels.Add(CreateGatePanel());
        _effectPanels.Add(CreateTs9Panel());
        _effectPanels.Add(CreateOd1Panel());
        _effectPanels.Add(CreateDs1Panel());
        _effectPanels.Add(CreateFuzzPanel());
        _effectPanels.Add(CreateEq5Panel());
        _effectPanels.Add(CreateEffectsLoopPanel());
        _effectPanels.Add(CreatePhaserPanel());
        _effectPanels.Add(CreateFlangerPanel());
        _effectPanels.Add(CreateChorusPanel());
        _effectPanels.Add(CreateAnalogChorusPanel());
        _effectPanels.Add(CreateMicroPitchPanel());
        _effectPanels.Add(CreateRotaryPanel());
        _effectPanels.Add(CreateTremoloPanel());
        _effectPanels.Add(CreateDelayPanel());
        _effectPanels.Add(CreateReverbPanel());

        foreach (Control panel in _effectPanels)
        {
            panel.Visible = false;
            panel.Dock = DockStyle.Top;
        }

        AddLabeledControl(table, "Parámetros:", _effectPanelHost);
        _effectSelector.SelectedIndex = 0;
        ShowSelectedEffectPanel();
        return group;
    }

    private Control CreateOctaverPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _octaverEnabled.Text = "&Activar octavador";
        _octaverEnabled.AccessibleName = "Activar octavador y pitch";
        AddLabeledControl(table, "Estado:", _octaverEnabled);
        ConfigureCombo(_octaverCharacterCombo, "Tipo de octavador", "Octava abajo, octava arriba, dual, sub octava u organ.");
        _octaverCharacterCombo.Items.AddRange(new object[] { "Octava abajo", "Octava arriba", "Dual Octave", "Sub Octave", "Organ Pitch" });
        _octaverCharacterCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Tipo:", _octaverCharacterCombo);
        AddLabeledControl(table, "Señal directa, porcentaje:", ConfigureNumeric(_octaverDry, "Nivel dry del octavador"));
        AddLabeledControl(table, "Octava abajo, porcentaje:", ConfigureNumeric(_octaverDown, "Nivel de octava abajo"));
        AddLabeledControl(table, "Octava arriba, porcentaje:", ConfigureNumeric(_octaverUp, "Nivel de octava arriba"));
        AddLabeledControl(table, "Tono, porcentaje:", ConfigureNumeric(_octaverTone, "Tono del octavador"));
        AddLabeledControl(table, "Nivel general, porcentaje:", ConfigureNumeric(_octaverLevel, "Nivel general del octavador"));
        var help = new TextBox { ReadOnly=true, Multiline=true, AutoSize=false, Height=58,
            Text="El tracking está optimizado para notas individuales y solos. El octavador forma parte de la cadena previa y puede moverse antes o después de otros pedales.",
            AccessibleName="Ayuda del octavador" };
        AddLabeledControl(table, "Ayuda:", help);
        return table;
    }

    private Control CreateGatePanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _gateEnabled.Text = "&Activar puerta de ruidos";
        _gateEnabled.Checked = true;
        _gateEnabled.AccessibleName = "Activar puerta de ruidos";
        AddLabeledControl(table, "Estado:", _gateEnabled);
        AddLabeledControl(table, "Umbral en decibeles:",
            ConfigureNumeric(_gateThreshold, "Umbral de la puerta de ruidos en decibeles"));
        AddLabeledControl(table, "Liberación en milisegundos:",
            ConfigureNumeric(_gateRelease, "Tiempo de liberación de la puerta en milisegundos"));
        return table;
    }

    private Control CreateCompressorPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _compressorEnabled.Text = "Activar &compresor";
        _compressorEnabled.AccessibleName = "Activar compresor limpio";
        _compressorEnabled.AccessibleDescription =
            "Compresor previo al amplificador, pensado especialmente para sonidos limpios. Aumenta sustain y empareja la dinámica sin cambiar el carácter del amplificador.";
        AddLabeledControl(table, "Estado:", _compressorEnabled);
        ConfigureCombo(_compressorCharacterCombo,"Tipo de compresor","Studio Clean, Dyna, Optical o Sustainer."); _compressorCharacterCombo.Items.AddRange(new object[]{"Studio Clean","Dyna Comp","Optical","Sustainer"}); _compressorCharacterCombo.SelectedIndex=0; AddLabeledControl(table,"Tipo de compresor:",_compressorCharacterCombo);
        AddLabeledControl(table, "Sustain, de 0 a 10:", ConfigureNumeric(_compressorSustain, "Sustain del compresor"));
        AddLabeledControl(table, "Ataque, de 2 a 80 milisegundos:", ConfigureNumeric(_compressorAttack, "Ataque del compresor en milisegundos"));
        AddLabeledControl(table, "Nivel, de 0 a 10:", ConfigureNumeric(_compressorLevel, "Nivel de salida del compresor"));
        return table;
    }

    private Control CreateAutoWahPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _autoWahEnabled.Text = "Activar &wah";
        _autoWahEnabled.AccessibleName = "Activar wah automático o manual";
        _autoWahEnabled.AccessibleDescription =
            "Wah previo a las distorsiones. Puede seguir la dinámica de la guitarra o una posición manual controlable por pedal de expresión MIDI.";
        AddLabeledControl(table, "Estado:", _autoWahEnabled);

        ConfigureCombo(_autoWahModeCombo, "Modo del wah",
            "Auto por dinámica o Manual para pedal de expresión.");
        _autoWahModeCombo.Items.AddRange(new object[]
        {
            "Auto por dinámica",
            "Manual / pedal de expresión"
        });
        _autoWahModeCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Modo:", _autoWahModeCombo);

        ConfigureCombo(_autoWahCharacterCombo, "Carácter del wah",
            "Clásico, estilo Vai Bad Horsie o estilo Satriani Big Bad.");
        _autoWahCharacterCombo.Items.AddRange(new object[]
        {
            "Clásico",
            "Vai / Bad Horsie",
            "Satriani / Big Bad"
        });
        _autoWahCharacterCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Carácter:", _autoWahCharacterCombo);

        AddLabeledControl(table, "Sensibilidad, de 0 a 10:",
            ConfigureNumeric(_autoWahSensitivity, "Sensibilidad del auto wah a la dinámica"));
        AddLabeledControl(table, "Rango, de 0 a 10:",
            ConfigureNumeric(_autoWahRange, "Amplitud y extensión del barrido del wah"));
        AddLabeledControl(table, "Resonancia, de 0 a 10:",
            ConfigureNumeric(_autoWahResonance, "Resonancia y carácter vocal del wah"));
        AddLabeledControl(table, "Posición manual, de 0 a 100 por ciento:",
            ConfigureNumeric(_autoWahManualPosition, "Posición manual del wah para teclado, simulador MIDI o pedal de expresión"));
        UpdateAutoWahModeUi();
        return table;
    }

    private void UpdateAutoWahModeUi()
    {
        bool manual = _autoWahModeCombo.SelectedIndex == 1;
        _autoWahSensitivity.Enabled = !manual;
        _autoWahManualPosition.Enabled = manual;

        string character = _autoWahCharacterCombo.SelectedIndex switch
        {
            1 => "Vai / Bad Horsie, barrido amplio, vocal y más abierto",
            2 => "Satriani / Big Bad, voz más agresiva, vocal y con empuje de solo",
            _ => "Clásico, respuesta equilibrada de envelope filter"
        };
        _autoWahCharacterCombo.AccessibleName = $"Carácter del wah: {character}";
        _autoWahModeCombo.AccessibleName = manual
            ? "Modo del wah: Manual o pedal de expresión"
            : "Modo del wah: Auto por dinámica";
        _autoWahManualPosition.AccessibleDescription = manual
            ? "Posición actual del pedal. Cero es talón abajo y cien es punta abajo. Puede asignarse con MIDI Learn."
            : "Disponible al elegir modo Manual o pedal de expresión.";
    }

    private Control CreateDs1Panel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _overdriveEnabled.Text = "Activar &distorsiones";
        _overdriveEnabled.AccessibleName = "Activar distorsiones";
        _overdriveEnabled.AccessibleDescription =
            "Distorsión previa al amplificador, inspirada en el carácter clásico del DS-1, con recorte simétrico, tono y nivel.";
        AddLabeledControl(table, "Estado:", _overdriveEnabled);
        ConfigureCombo(_distortionCharacterCombo,"Tipo de distorsión","DS-1, Guv'nor británica, RAT o Modern."); _distortionCharacterCombo.Items.AddRange(new object[]{"DS-1 clásico","Guv'nor británica","RAT agresiva","Modern apretada"}); _distortionCharacterCombo.SelectedIndex=0; AddLabeledControl(table,"Tipo de distorsión:",_distortionCharacterCombo);
        AddLabeledControl(table, "Distorsión, de 0 a 10:", ConfigureNumeric(_overdriveGain, "Cantidad de distorsión del DS-1"));
        AddLabeledControl(table, "Tono, de 0 a 10:", ConfigureNumeric(_overdriveTone, "Tono del DS-1"));
        AddLabeledControl(table, "Nivel, de 0 a 10:", ConfigureNumeric(_overdriveLevel, "Nivel del DS-1"));
        return table;
    }

    private Control CreateTs9Panel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _ts9Enabled.Text = "Activar &overdrive seleccionable";
        _ts9Enabled.AccessibleName = "Activar overdrive seleccionable";
        _ts9Enabled.AccessibleDescription = "Familia de overdrives con cinco voces: TS808, TS9, Klon, Marshall británico y DOD 250.";
        AddLabeledControl(table, "Estado:", _ts9Enabled);
        ConfigureCombo(_driveCharacterCombo, "Tipo de overdrive", "Seleccione TS808, TS9, Klon, Marshall británico o DOD 250.");
        _driveCharacterCombo.Items.AddRange(new object[] { "TS808 cálido", "TS9 clásico", "Klon transparente", "Marshall británico", "DOD 250" });
        _driveCharacterCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Tipo de overdrive:", _driveCharacterCombo);
        AddLabeledControl(table, "Ganancia, de 0 a 10:", ConfigureNumeric(_ts9Gain, "Ganancia del overdrive seleccionado"));
        AddLabeledControl(table, "Tono, de 0 a 10:", ConfigureNumeric(_ts9Tone, "Tono del overdrive seleccionado"));
        AddLabeledControl(table, "Nivel, de 0 a 10:", ConfigureNumeric(_ts9Level, "Nivel del overdrive seleccionado"));
        return table;
    }

    private Control CreateOd1Panel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _od1Enabled.Text = "Activar &OD-1 / Fulltone OCD";
        _od1Enabled.AccessibleName = "Activar overdrive OD-1 o Fulltone OCD";
        _od1Enabled.AccessibleDescription =
            "Familia agrupada de overdrives: Boss OD-1 Vintage 1977, OD-1 Late 4558 y Fulltone OCD en modos LP o HP. El tono se utiliza en OCD; los OD-1 conservan su voicing original.";
        AddLabeledControl(table, "Estado:", _od1Enabled);
        ConfigureCombo(_od1CharacterCombo, "Modelo de overdrive OD-1 u OCD",
            "Seleccione OD-1 Vintage 1977, OD-1 Late 4558, Fulltone OCD LP o Fulltone OCD HP.");
        _od1CharacterCombo.Items.AddRange(new object[]
        {
            "OD-1 Vintage 1977",
            "OD-1 Late 4558",
            "Fulltone OCD modo LP",
            "Fulltone OCD modo HP"
        });
        _od1CharacterCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Modelo:", _od1CharacterCombo);
        AddLabeledControl(table, "Drive, de 0 a 10:", ConfigureNumeric(_od1Drive, "Drive del OD-1 o Fulltone OCD"));
        AddLabeledControl(table, "Tono, de 0 a 10:", ConfigureNumeric(_od1Tone, "Tono del Fulltone OCD. En OD-1 se conserva el tono original del pedal"));
        AddLabeledControl(table, "Nivel, de 0 a 10:", ConfigureNumeric(_od1Level, "Nivel de salida del OD-1 o Fulltone OCD"));
        return table;
    }

    private Control CreateFuzzPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _fuzzEnabled.Text = "Activar &fuzz";
        _fuzzEnabled.AccessibleName = "Activar fuzz";
        _fuzzEnabled.AccessibleDescription =
            "Fuzz previo al amplificador con dos caracteres: Fuzz Face vintage, más dinámico, y Big Muff grueso, comprimido y sostenido.";
        AddLabeledControl(table, "Estado:", _fuzzEnabled);
        ConfigureCombo(_fuzzCharacterCombo, "Tipo de fuzz", "Seleccione Fuzz Face vintage o Big Muff grueso.");
        _fuzzCharacterCombo.Items.AddRange(new object[] { "Fuzz Face vintage", "Big Muff grueso" });
        _fuzzCharacterCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Tipo de fuzz:", _fuzzCharacterCombo);
        AddLabeledControl(table, "Ganancia, de 0 a 10:", ConfigureNumeric(_fuzzGain, "Ganancia del fuzz"));
        AddLabeledControl(table, "Tono, de 0 a 10:", ConfigureNumeric(_fuzzTone, "Tono del fuzz"));
        AddLabeledControl(table, "Nivel, de 0 a 10:", ConfigureNumeric(_fuzzLevel, "Nivel de salida del fuzz"));
        return table;
    }

    private Control CreateEq5Panel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _eq5Enabled.Text = "Activar &EQ de 5 bandas";
        _eq5Enabled.AccessibleName = "Activar ecualizador gráfico de 5 bandas";
        _eq5Enabled.AccessibleDescription =
            "Ecualizador gráfico para guitarra. Cada banda trabaja de menos 12 a más 12 decibeles. Puede ubicarse antes o después del amplificador o NAM.";
        AddLabeledControl(table, "Estado:", _eq5Enabled);
        ConfigureCombo(_eq5PlacementCombo, "Ubicación del EQ", "Antes del amplificador o NAM, o después del amplificador y gabinete.");
        _eq5PlacementCombo.Items.AddRange(new object[] { "Antes del amplificador o NAM", "Después del amplificador y gabinete" });
        _eq5PlacementCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Ubicación:", _eq5PlacementCombo);
        AddLabeledControl(table, "100 Hz, de -12 a +12 dB:", ConfigureNumeric(_eq5Band100, "EQ 100 hercios, graves"));
        AddLabeledControl(table, "250 Hz, de -12 a +12 dB:", ConfigureNumeric(_eq5Band250, "EQ 250 hercios, graves medios"));
        AddLabeledControl(table, "800 Hz, de -12 a +12 dB:", ConfigureNumeric(_eq5Band800, "EQ 800 hercios, medios"));
        AddLabeledControl(table, "2,5 kHz, de -12 a +12 dB:", ConfigureNumeric(_eq5Band2500, "EQ 2 coma 5 kilohercios, presencia"));
        AddLabeledControl(table, "6,4 kHz, de -12 a +12 dB:", ConfigureNumeric(_eq5Band6400, "EQ 6 coma 4 kilohercios, brillo"));
        AddLabeledControl(table, "Nivel general, de -12 a +12 dB:", ConfigureNumeric(_eq5Output, "Nivel general del EQ de 5 bandas"));
        return table;
    }

    private Control CreateBoosterPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _boosterEnabled.Text = "Activar &booster limpio";
        _boosterEnabled.AccessibleName = "Activar booster limpio tipo CAE MXR";
        _boosterEnabled.AccessibleDescription =
            "Booster de una perilla situado antes del amplificador. Aumenta la señal sin agregar distorsión intencional.";
        AddLabeledControl(table, "Estado:", _boosterEnabled);
        ConfigureCombo(_boosterCharacterCombo, "Tipo de booster", "CAE Line Driver, EP Style o Treble Booster.");
        _boosterCharacterCombo.Items.AddRange(new object[] { "CAE Line Driver", "EP Style Booster", "Treble Booster" });
        _boosterCharacterCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Tipo de booster:", _boosterCharacterCombo);
        AddLabeledControl(table, "Aumento, de 0 a 20 decibeles:",
            ConfigureNumeric(_boosterDb, "Aumento del booster limpio en decibeles"));
        return table;
    }

    private Control CreateEffectsLoopPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _fxLoopEnabled.Text = "Activar &loop de efectos";
        _fxLoopEnabled.Checked = true;
        _fxLoopEnabled.AccessibleName = "Activar loop de efectos";
        _fxLoopEnabled.AccessibleDescription =
            "Phaser, flanger, los chorus configurados en Loop, MicroPitch 80s y delay se procesan aquí. Cada chorus puede enviarse de forma independiente al Input del amplificador o NAM. El reverb queda después del loop.";
        AddLabeledControl(table, "Estado:", _fxLoopEnabled);
        AddLabeledControl(table, "Nivel de envío, de 0 a 100:",
            ConfigureNumeric(_fxLoopSend, "Nivel de envío al loop de efectos en porcentaje"));
        AddLabeledControl(table, "Nivel de retorno, de 0 a 100:",
            ConfigureNumeric(_fxLoopReturn, "Nivel de retorno del loop en porcentaje"));
        _resetDelayButton.Text = "&Limpiar memoria del delay";
        _resetDelayButton.AccessibleName = "Limpiar memoria del delay";
        _resetDelayButton.AccessibleDescription = "Reinicia solamente el delay sin detener el audio.";
        AddLabeledControl(table, "Recuperación:", _resetDelayButton);
        return table;
    }

    private Control CreatePhaserPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _phaserEnabled.Text = "Activar &phaser";
        _phaserEnabled.AccessibleName = "Activar phaser dentro del loop";
        _phaserEnabled.AccessibleDescription =
            "Phaser de cuatro etapas situado después del amplificador y antes de flanger, de los chorus ubicados en Loop y del delay.";
        AddLabeledControl(table, "Estado:", _phaserEnabled);
        AddLabeledControl(table, "Velocidad en hercios:", ConfigureNumeric(_phaserRate, "Velocidad del phaser"));
        AddLabeledControl(table, "Profundidad en porcentaje:", ConfigureNumeric(_phaserDepth, "Profundidad del phaser"));
        AddLabeledControl(table, "Realimentación en porcentaje:", ConfigureNumeric(_phaserFeedback, "Realimentación del phaser"));
        AddLabeledControl(table, "Mezcla en porcentaje:", ConfigureNumeric(_phaserMix, "Mezcla del phaser"));
        return table;
    }

    private Control CreateFlangerPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _flangerEnabled.Text = "Activar &flanger";
        _flangerEnabled.AccessibleName = "Activar flanger dentro del loop";
        _flangerEnabled.AccessibleDescription =
            "Flanger de retardo corto situado después del phaser y antes de los chorus ubicados en Loop y del delay.";
        AddLabeledControl(table, "Estado:", _flangerEnabled);
        ConfigureCombo(_flangerCharacterCombo, "Tipo de flanger", "Classic, Jet o Tape Zero Point.");
        _flangerCharacterCombo.Items.AddRange(new object[] { "Flanger clásico", "Jet Flanger", "Tape Zero Point" });
        _flangerCharacterCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Tipo de flanger:", _flangerCharacterCombo);
        AddLabeledControl(table, "Velocidad en hercios:", ConfigureNumeric(_flangerRate, "Velocidad del flanger"));
        AddLabeledControl(table, "Profundidad en porcentaje:", ConfigureNumeric(_flangerDepth, "Profundidad del flanger"));
        AddLabeledControl(table, "Realimentación, de menos 70 a 70 por ciento:", ConfigureNumeric(_flangerFeedback, "Realimentación del flanger"));
        AddLabeledControl(table, "Mezcla en porcentaje:", ConfigureNumeric(_flangerMix, "Mezcla del flanger"));
        return table;
    }

    private Control CreateChorusPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _chorusEnabled.Text = "A&ctivar chorus";
        _chorusEnabled.AccessibleName = "Activar chorus estéreo";
        AddLabeledControl(table, "Estado:", _chorusEnabled);
        ConfigureCombo(_chorusCharacterCombo, "Tipo de chorus estéreo", "Ensemble ancho o Dimension sutil y espacial.");
        _chorusCharacterCombo.Items.AddRange(new object[] { "Stereo Ensemble", "Dimension estéreo" });
        _chorusCharacterCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Tipo de chorus:", _chorusCharacterCombo);
        ConfigureCombo(_chorusPlacementCombo, "Ubicación del chorus", "Loop o Input. Input lo coloca en la cadena previa al amplificador o NAM.");
        _chorusPlacementCombo.Items.AddRange(new object[] { "Loop de efectos", "Input del amplificador" });
        _chorusPlacementCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Ubicación:", _chorusPlacementCombo);
        AddLabeledControl(table, "Velocidad en hercios:", ConfigureNumeric(_chorusRate, "Velocidad del chorus"));
        AddLabeledControl(table, "Profundidad en milisegundos:", ConfigureNumeric(_chorusDepth, "Profundidad del chorus"));
        AddLabeledControl(table, "Mezcla en porcentaje:", ConfigureNumeric(_chorusMix, "Mezcla del chorus"));
        return table;
    }

    private Control CreateAnalogChorusPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _analogChorusEnabled.Text = "Activar chorus analógico tipo &MXR";
        _analogChorusEnabled.AccessibleName = "Activar chorus analógico tipo MXR";
        _analogChorusEnabled.AccessibleDescription =
            "Segundo chorus de carácter analógico, más cálido, ancho y oscuro que el chorus ensemble. Puede ubicarse de forma independiente en el Input o en el Loop.";
        AddLabeledControl(table, "Estado:", _analogChorusEnabled);
        ConfigureCombo(_analogChorusPlacementCombo, "Ubicación del chorus analógico", "Loop o Input. Input lo coloca antes del amplificador interno o NAM.");
        _analogChorusPlacementCombo.Items.AddRange(new object[] { "Loop de efectos", "Input del amplificador" });
        _analogChorusPlacementCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Ubicación:", _analogChorusPlacementCombo);
        AddLabeledControl(table, "Velocidad en hercios:", ConfigureNumeric(_analogChorusRate, "Velocidad del chorus analógico"));
        AddLabeledControl(table, "Profundidad, de 0 a 10:", ConfigureNumeric(_analogChorusDepth, "Profundidad del chorus analógico"));
        AddLabeledControl(table, "Mezcla en porcentaje:", ConfigureNumeric(_analogChorusMix, "Mezcla del chorus analógico"));
        AddLabeledControl(table, "Graves, de 0 a 10:", ConfigureNumeric(_analogChorusLow, "Ecualización de graves del chorus analógico"));
        AddLabeledControl(table, "Agudos, de 0 a 10:", ConfigureNumeric(_analogChorusHigh, "Ecualización de agudos del chorus analógico"));
        return table;
    }

    private static void ConfigureTempoDivisionCombo(ComboBox combo, string accessibleName)
    {
        ConfigureCombo(combo, accessibleName, "Negra, corchea, corchea con puntillo, tresillo o semicorchea.");
        combo.Items.Clear();
        combo.Items.AddRange(new object[] { "Negra", "Corchea", "Corchea con puntillo", "Tresillo", "Semicorchea" });
        if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;
    }

    private static float TempoDivisionFactor(int index) => index switch
    {
        1 => 0.5f,
        2 => 0.75f,
        3 => 2f / 3f,
        4 => 0.25f,
        _ => 1f
    };

    private float SyncedDelayMilliseconds() => Math.Clamp((60000f / (float)_metronomeBpm.Value) * TempoDivisionFactor(_delayDivision.SelectedIndex), 1f, 1000f);
    private float SyncedTremoloHz() => Math.Clamp(((float)_metronomeBpm.Value / 60f) / TempoDivisionFactor(_tremoloDivision.SelectedIndex), 0.1f, 12f);
    private float SyncedRotaryHz() => Math.Clamp(((float)_metronomeBpm.Value / 60f) / TempoDivisionFactor(_rotaryDivision.SelectedIndex), 0.2f, 8f);

    private Control CreateMicroPitchPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _microPitchEnabled.Text = "Activar &MicroPitch 80s / Harmonizer";
        _microPitchEnabled.AccessibleName = "Activar MicroPitch 80s tipo Eventide Harmonizer";
        _microPitchEnabled.AccessibleDescription =
            "Ensanchador estéreo post amplificador. Crea dos voces apenas desafinadas y con retardos distintos a izquierda y derecha. Agranda la guitarra sin el barrido evidente de un chorus.";
        AddLabeledControl(table, "Estado:", _microPitchEnabled);
        AddLabeledControl(table, "Desafinación, cents:",
            ConfigureNumeric(_microPitchDetune, "Desafinación simétrica del MicroPitch en cents. Nueve es el valor inicial estilo rack de los 80."));
        AddLabeledControl(table, "Retardo base, milisegundos:",
            ConfigureNumeric(_microPitchDelay, "Retardo corto base de las voces MicroPitch en milisegundos"));
        AddLabeledControl(table, "Mezcla, porcentaje:",
            ConfigureNumeric(_microPitchMix, "Cantidad de voces MicroPitch mezcladas con la guitarra directa"));
        var help = new TextBox
        {
            ReadOnly = true, Multiline = true, AutoSize = false, Height = 68,
            Text = "Pensado para el efecto de guitarra grande de fines de los 70 y los 80: dos voces microafinadas y retardadas en estéreo. Pruebe 9 cents, 10 ms y 38 % como punto de partida.",
            AccessibleName = "Ayuda de MicroPitch 80s"
        };
        AddLabeledControl(table, "Ayuda:", help);
        return table;
    }

    private Control CreateRotaryPanel()
    {
        var table=CreateTwoColumnTable(); table.AccessibleName=string.Empty;
        _rotaryEnabled.Text="Activar &Rotary Speaker"; _rotaryEnabled.AccessibleName="Activar efecto Leslie o Rotary Speaker";
        _rotaryFast.Text="Velocidad &rápida"; _rotaryFast.AccessibleName="Leslie velocidad rápida. Desmarcado es lento";
        _rotarySync.Text="Sincronizar con tempo maestro"; _rotarySync.AccessibleName="Sincronizar Leslie con tempo maestro";
        ConfigureTempoDivisionCombo(_rotaryDivision, "Subdivisión del Leslie");
        AddLabeledControl(table,"Estado:",_rotaryEnabled); AddLabeledControl(table,"Slow o Fast:",_rotaryFast);
        AddLabeledControl(table,"Sincronización:",_rotarySync); AddLabeledControl(table,"Subdivisión:",_rotaryDivision);
        AddLabeledControl(table,"Profundidad:",ConfigureNumeric(_rotaryDepth,"Profundidad del Leslie")); AddLabeledControl(table,"Mezcla:",ConfigureNumeric(_rotaryMix,"Mezcla del Leslie")); return table;
    }

    private Control CreateTremoloPanel()
    {
        var table = CreateTwoColumnTable(); table.AccessibleName = string.Empty;
        _tremoloEnabled.Text = "Activar &tremolo"; _tremoloEnabled.AccessibleName = "Activar tremolo dentro del loop";
        AddLabeledControl(table, "Estado:", _tremoloEnabled);
        _tremoloSync.Text = "Sincronizar con tempo maestro"; _tremoloSync.AccessibleName = "Sincronizar tremolo con tempo maestro";
        ConfigureTempoDivisionCombo(_tremoloDivision, "Subdivisión del tremolo");
        AddLabeledControl(table, "Sincronización:", _tremoloSync);
        AddLabeledControl(table, "Subdivisión:", _tremoloDivision);
        AddLabeledControl(table, "Velocidad manual en hercios:", ConfigureNumeric(_tremoloRate, "Velocidad manual del tremolo"));
        AddLabeledControl(table, "Profundidad en porcentaje:", ConfigureNumeric(_tremoloDepth, "Profundidad del tremolo"));
        return table;
    }

    private Control CreateDelayPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _delayEnabled.Text = "Activar &delay";
        _delayEnabled.AccessibleName = "Activar delay dentro del loop";
        AddLabeledControl(table, "Estado:", _delayEnabled);
        ConfigureCombo(_delayCharacterCombo, "Carácter del delay",
            "Digital limpio, analógico BBD, cinta o reverse ambiental.");
        _delayCharacterCombo.Items.AddRange(new object[] { "Digital limpio", "Analógico oscuro tipo BBD", "Tape cinta", "Reverse ambiental" });
        _delayCharacterCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Carácter del delay:", _delayCharacterCombo);
        _delaySync.Text = "Sincronizar con tempo maestro"; _delaySync.AccessibleName = "Sincronizar delay con tempo maestro";
        ConfigureTempoDivisionCombo(_delayDivision, "Subdivisión del delay");
        _delayDivision.SelectedIndex = 2;
        AddLabeledControl(table, "Sincronización:", _delaySync);
        AddLabeledControl(table, "Subdivisión:", _delayDivision);
        AddLabeledControl(table, "Tiempo manual, de 1 a 1000 milisegundos:",
            ConfigureNumeric(_delayTime, "Tiempo del delay en milisegundos. Flechas arriba y abajo cambian 10 milisegundos. Flechas izquierda y derecha cambian 1 milisegundo."));
        AddLabeledControl(table, "Repeticiones, de 0 a 60 por ciento:",
            ConfigureNumeric(_delayFeedback, "Realimentación del delay"));
        AddLabeledControl(table, "Mezcla, de 0 a 60 por ciento:",
            ConfigureNumeric(_delayMix, "Mezcla del delay"));
        return table;
    }

    private Control CreateReverbPanel()
    {
        var table = CreateTwoColumnTable();
        table.AccessibleName = string.Empty;
        _reverbEnabled.Text = "Activar &reverb";
        _reverbEnabled.Checked = true;
        _reverbEnabled.AccessibleName = "Activar reverb tipo resorte";
        AddLabeledControl(table, "Estado:", _reverbEnabled);
        ConfigureCombo(_reverbCharacterCombo, "Tipo de reverb", "Spring con rebote metálico de tanque y drip dependiente del ataque, Plate brillante, Room corta, Hall amplia, Church oscura, Shimmer con octava presente o Cathedral enorme y muy ancha. Al cambiar de tipo, la cola anterior se limpia automáticamente con un fundido corto.");
        _reverbCharacterCombo.Items.AddRange(new object[] { "Spring", "Plate", "Room", "Hall", "Church", "Shimmer", "Cathedral" });
        _reverbCharacterCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Tipo de reverb:", _reverbCharacterCombo);
        AddLabeledControl(table, "Mezcla en porcentaje:", ConfigureNumeric(_reverbMix, "Mezcla del reverb"));
        AddLabeledControl(table, "Duración en porcentaje:", ConfigureNumeric(_reverbDecay, "Duración del reverb"));
        AddLabeledControl(table, "Brillo en porcentaje:", ConfigureNumeric(_reverbTone, "Brillo del reverb"));
        return table;
    }

    private void UpdateDynamicEffectAccessibleNames()
    {
        string distortion = _distortionCharacterCombo.SelectedIndex switch
        {
            1 => "Guv'nor",
            2 => "RAT",
            3 => "Modern Distortion",
            _ => "DS-1"
        };
        _overdriveEnabled.AccessibleName = $"Activar distorsión {distortion}";
        _overdriveGain.AccessibleName = $"Ganancia de {distortion}";
        _overdriveTone.AccessibleName = $"Tono de {distortion}";
        _overdriveLevel.AccessibleName = $"Nivel de {distortion}";

        string drive = _driveCharacterCombo.SelectedIndex switch
        {
            1 => "TS9",
            2 => "Klon",
            3 => "Marshall",
            4 => "DOD 250",
            _ => "TS808"
        };
        _ts9Enabled.AccessibleName = $"Activar overdrive {drive}";
        _ts9Gain.AccessibleName = $"Ganancia de {drive}";
        _ts9Tone.AccessibleName = $"Tono de {drive}";
        _ts9Level.AccessibleName = $"Nivel de {drive}";

        string odFamily = _od1CharacterCombo.SelectedIndex switch
        {
            1 => "Boss OD-1 Late 4558",
            2 => "Fulltone OCD LP",
            3 => "Fulltone OCD HP",
            _ => "Boss OD-1 Vintage 1977"
        };
        _od1Enabled.AccessibleName = $"Activar {odFamily}";
        _od1Drive.AccessibleName = $"Drive de {odFamily}";
        _od1Tone.AccessibleName = _od1CharacterCombo.SelectedIndex >= 2
            ? $"Tono de {odFamily}"
            : $"Tono reservado para OCD. {odFamily} conserva su voicing original";
        _od1Level.AccessibleName = $"Nivel de {odFamily}";

        string booster = _boosterCharacterCombo.SelectedIndex switch
        {
            1 => "EP Style Booster",
            2 => "Treble Booster",
            _ => "CAE Line Driver"
        };
        _boosterEnabled.AccessibleName = $"Activar {booster}";
        _boosterDb.AccessibleName = $"Aumento de {booster} en decibeles";

        string compressor = _compressorCharacterCombo.SelectedIndex switch
        {
            1 => "Dyna Comp",
            2 => "Optical",
            3 => "Sustainer",
            _ => "Studio Clean"
        };
        _compressorEnabled.AccessibleName = $"Activar compresor {compressor}";
        _compressorSustain.AccessibleName = $"Sustain de {compressor}";
        _compressorAttack.AccessibleName = $"Ataque de {compressor} en milisegundos";
        _compressorLevel.AccessibleName = $"Nivel de {compressor}";

        string flanger = _flangerCharacterCombo.SelectedIndex switch
        {
            1 => "Jet Flanger",
            2 => "Tape Zero Point",
            _ => "Flanger clásico"
        };
        _flangerEnabled.AccessibleName = $"Activar {flanger}";
        _flangerRate.AccessibleName = $"Velocidad de {flanger}";
        _flangerDepth.AccessibleName = $"Profundidad de {flanger}";
        _flangerFeedback.AccessibleName = $"Realimentación de {flanger}";
        _flangerMix.AccessibleName = $"Mezcla de {flanger}";

        string delay = _delayCharacterCombo.SelectedIndex switch
        {
            1 => "Delay analógico BBD",
            2 => "Tape Delay",
            3 => "Reverse Delay",
            _ => "Delay digital"
        };
        _delayEnabled.AccessibleName = $"Activar {delay}";
        _delayTime.AccessibleName = $"Tiempo de {delay} en milisegundos";
        _delayFeedback.AccessibleName = $"Realimentación de {delay}";
        _delayMix.AccessibleName = $"Mezcla de {delay}";

        string reverb = _reverbCharacterCombo.SelectedIndex switch
        {
            1 => "Plate",
            2 => "Room",
            3 => "Hall",
            4 => "Church",
            5 => "Shimmer",
            6 => "Cathedral",
            _ => "Spring"
        };
        string reverbDetail = _reverbCharacterCombo.SelectedIndex switch
        {
            1 => "placa brillante, cola densa",
            2 => "habitación corta, reflexiones tempranas",
            3 => "sala amplia, cola suave",
            4 => "iglesia oscura, predelay largo y cola solemne",
            5 => "ambiente etéreo con octava brillante más presente",
            6 => "catedral máxima, cola enorme y estéreo muy ancho",
            _ => "resorte, ataque metálico y drip más marcado"
        };
        _reverbCharacterCombo.AccessibleName = $"Tipo de reverb: {reverb}; {reverbDetail}";
        _reverbCharacterCombo.AccessibleDescription = reverbDetail;
        _reverbEnabled.AccessibleName = $"Activar reverb {reverb}";
        _reverbMix.AccessibleName = $"Mezcla de reverb {reverb}";
        _reverbDecay.AccessibleName = $"Duración de reverb {reverb}";
        _reverbTone.AccessibleName = $"Brillo de reverb {reverb}";
        _reverbPreDelay.AccessibleName = $"Pre delay de reverb {reverb} en milisegundos";
        _reverbDamping.AccessibleName = $"Damping de reverb {reverb}";
        _reverbDiffusion.AccessibleName = $"Difusión de reverb {reverb}";

        string chorus = _chorusCharacterCombo.SelectedIndex == 1 ? "Dimension estéreo" : "Stereo Ensemble";
        _chorusEnabled.AccessibleName = $"Activar chorus {chorus}";
        _chorusRate.AccessibleName = $"Velocidad de chorus {chorus}";
        _chorusDepth.AccessibleName = $"Profundidad de chorus {chorus}";
        _chorusMix.AccessibleName = $"Mezcla de chorus {chorus}";

        string octaver = _octaverCharacterCombo.SelectedIndex switch
        {
            1 => "Octava arriba",
            2 => "Dual Octave",
            3 => "Sub Octave",
            4 => "Organ Pitch",
            _ => "Octava abajo"
        };
        _octaverEnabled.AccessibleName = $"Activar {octaver}";
        _octaverDry.AccessibleName = $"Señal directa de {octaver}";
        _octaverDown.AccessibleName = $"Octava abajo de {octaver}";
        _octaverUp.AccessibleName = $"Octava arriba de {octaver}";
        _octaverTone.AccessibleName = $"Tono de {octaver}";
        _octaverLevel.AccessibleName = $"Nivel general de {octaver}";
    }

    private Control BuildMidiGroup()
    {
        var group = CreateGroup("Control MIDI y MIDI Learn");
        var table = CreateTwoColumnTable();
        group.Controls.Add(table);

        ConfigureCombo(_midiInputCombo, "Dispositivo de entrada MIDI",
            "Seleccione una pedalera o controlador MIDI conectado por USB o por una interfaz MIDI.");
        _midiInputCombo.Width = 430;
        AddLabeledControl(table, "&Entrada MIDI:", _midiInputCombo);

        var deviceButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _midiRefreshButton.Text = "&Actualizar dispositivos";
        _midiRefreshButton.AccessibleName = "Actualizar dispositivos MIDI";
        _midiConnectButton.Text = "&Conectar MIDI";
        _midiConnectButton.AccessibleName = "Conectar o desconectar entrada MIDI";
        deviceButtons.Controls.AddRange(new Control[] { _midiRefreshButton, _midiConnectButton });
        AddLabeledControl(table, "Conexión:", deviceButtons);

        _midiConnectionStatus.AutoSize = true;
        _midiConnectionStatus.MaximumSize = new Size(720, 0);
        _midiConnectionStatus.Text = "MIDI desconectado. El simulador interno funciona aunque no haya una pedalera conectada.";
        _midiConnectionStatus.AccessibleName = _midiConnectionStatus.Text;
        AddLabeledControl(table, "Estado:", _midiConnectionStatus);

        _midiProgramBanks.Text = "Program Change 0 a 29 carga directamente bancos F01 a F30";
        _midiProgramBanks.AccessibleName = "Program Change cero a veintinueve carga bancos F01 a F30";
        _midiProgramBanks.AccessibleDescription =
            "Activado por defecto. Program Change 0 carga F01, 1 carga F02 y así hasta 29 que carga F30. Una asignación aprendida tiene prioridad.";
        _midiProgramBanks.Checked = _midiSettings.ProgramChangesLoadFactoryBanks;
        AddLabeledControl(table, "Bancos:", _midiProgramBanks);

        ConfigureCombo(_midiLearnActionCombo, "Función para MIDI Learn",
            "Elija una función de Amp Accessible, pulse Iniciar MIDI Learn y luego pise o mueva el control que quiera asignar.");
        _midiLearnActionCombo.Width = 430;
        AddLabeledControl(table, "&Función a aprender:", _midiLearnActionCombo);

        var learnButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        _midiLearnButton.Text = "Iniciar MIDI &Learn";
        _midiLearnButton.AccessibleName = "Iniciar MIDI Learn";
        _midiCancelLearnButton.Text = "&Cancelar Learn";
        _midiCancelLearnButton.AccessibleName = "Cancelar MIDI Learn";
        _midiCancelLearnButton.Enabled = false;
        learnButtons.Controls.AddRange(new Control[] { _midiLearnButton, _midiCancelLearnButton });
        AddLabeledControl(table, "Aprender:", learnButtons);

        _midiBindingsList.Width = 620;
        _midiBindingsList.Height = 150;
        _midiBindingsList.AccessibleName = "Asignaciones MIDI guardadas";
        _midiBindingsList.AccessibleDescription = "Lista de controles MIDI aprendidos y la función que ejecuta cada uno.";
        AddLabeledControl(table, "Asignaciones:", _midiBindingsList);

        _midiDeleteBindingButton.Text = "&Borrar asignación seleccionada";
        _midiDeleteBindingButton.AccessibleName = "Borrar asignación MIDI seleccionada";
        AddLabeledControl(table, "Editar:", _midiDeleteBindingButton);

        var separator = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            Text = "Simulador MIDI interno: permite probar las mismas rutas que usará una pedalera real. Para un pulsador CC use normalmente valor 127. Para Program Change el valor no se utiliza.",
            AccessibleName = "Simulador MIDI interno. Permite probar MIDI sin controlador físico."
        };
        AddLabeledControl(table, "Simulador:", separator);

        ConfigureCombo(_midiSimTypeCombo, "Tipo de mensaje MIDI simulado",
            "Seleccione Control Change o Program Change.");
        _midiSimTypeCombo.Items.AddRange(new object[] { "Control Change, CC", "Program Change, PC" });
        _midiSimTypeCombo.SelectedIndex = 0;
        AddLabeledControl(table, "Tipo:", _midiSimTypeCombo);

        _midiSimChannel.AccessibleName = "Canal MIDI simulado, 1 a 16";
        AddLabeledControl(table, "Canal:", _midiSimChannel);
        _midiSimNumber.AccessibleName = "Número de controlador CC o número de programa, 0 a 127";
        AddLabeledControl(table, "Número:", _midiSimNumber);
        _midiSimValue.AccessibleName = "Valor MIDI simulado, 0 a 127";
        AddLabeledControl(table, "Valor:", _midiSimValue);

        _midiSimSendButton.Text = "&Enviar mensaje simulado";
        _midiSimSendButton.AccessibleName = "Enviar mensaje MIDI simulado";
        AddLabeledControl(table, "Prueba:", _midiSimSendButton);

        _midiLastMessageLabel.AutoSize = true;
        _midiLastMessageLabel.MaximumSize = new Size(720, 0);
        _midiLastMessageLabel.Text = "Todavía no se recibió ni simuló ningún mensaje MIDI.";
        _midiLastMessageLabel.AccessibleName = _midiLastMessageLabel.Text;
        AddLabeledControl(table, "Último MIDI:", _midiLastMessageLabel);

        return group;
    }

    private void ShowSelectedEffectPanel()
    {
        if (_effectPanels.Count == 0)
        {
            return;
        }

        int selected = Math.Clamp(_effectSelector.SelectedIndex, 0, _effectPanels.Count - 1);
        _effectPanelHost.SuspendLayout();
        try
        {
            _effectPanelHost.Controls.Clear();
            Control panel = _effectPanels[selected];
            panel.Visible = true;
            _effectPanelHost.Controls.Add(panel);
        }
        finally
        {
            _effectPanelHost.ResumeLayout(true);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        _checkUpdatesButton.Enabled = false;
        try
        {
            SetStatus("Comprobando actualizaciones...");
            OnlineUpdateResult result = await UpdateService.CheckAndDownloadOnlineAsync();
            SetStatus(result.Message, !result.Available);
            if (result.Available && !string.IsNullOrWhiteSpace(result.PackagePath))
            {
                DialogResult answer = MessageBox.Show(this,
                    result.Message + "\r\n\r\n¿Desea instalarla ahora? Amp Accessible se cerrará y volverá a abrir.",
                    "Actualización disponible", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer == DialogResult.Yes) PrepareAndLaunchUpdate(result.PackagePath);
            }
        }
        finally { _checkUpdatesButton.Enabled = true; }
    }

    private void InstallLocalUpdatePackage()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Seleccionar actualización de Amp Accessible",
            Filter = "Actualización de Amp Accessible (*.gdmupdate)|*.gdmupdate|Todos los archivos (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) PrepareAndLaunchUpdate(dialog.FileName);
    }

    private void PrepareAndLaunchUpdate(string packagePath)
    {
        try
        {
            PreparedUpdate prepared = UpdateService.PreparePackage(packagePath);
            DialogResult answer = MessageBox.Show(this,
                $"Actualización a Amp Accessible {prepared.TargetVersion} verificada.\r\n\r\nEl programa se cerrará, reemplazará solamente los archivos nuevos o modificados y volverá a iniciarse.",
                "Instalar actualización", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
            if (answer != DialogResult.OK) return;
            UpdateService.LaunchPreparedUpdate(prepared, Environment.ProcessId);
            Application.Exit();
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo preparar la actualización: " + ex.Message, true);
        }
    }

    private string? FindCachedInstaller()
    {
        string candidate = Path.Combine(AppContext.BaseDirectory, "Maintenance", "AmpAccessible_Setup.exe");
        if (File.Exists(candidate)) return candidate;
        candidate = Path.Combine(AppContext.BaseDirectory, "AmpAccessible_Setup.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private void LaunchRepairInstaller()
    {
        string? installer = FindCachedInstaller();
        if (installer is null)
        {
            SetStatus("No se encontró el instalador de mantenimiento. Esta copia probablemente se ejecuta desde una carpeta de desarrollo. Use el instalador único para disponer de Reparar y Desinstalar.", true);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = installer, UseShellExecute = true });
            SetStatus("Instalador de mantenimiento abierto. Elija Reparar o actualizar instalación.");
        }
        catch (Exception ex) { SetStatus("No se pudo abrir el instalador: " + ex.Message, true); }
    }

    private void LaunchUninstaller()
    {
        string uninstaller = Path.Combine(AppContext.BaseDirectory, "unins000.exe");
        if (!File.Exists(uninstaller))
        {
            SetStatus("No se encontró el desinstalador. Esta copia probablemente no fue instalada con el instalador único.", true);
            return;
        }
        if (MessageBox.Show(this, "¿Desea abrir el desinstalador de Amp Accessible? Sus NAM, presets, escenas y copias de seguridad se conservarán.",
            "Desinstalar Amp Accessible", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = uninstaller, UseShellExecute = true });
            Application.Exit();
        }
        catch (Exception ex) { SetStatus("No se pudo abrir el desinstalador: " + ex.Message, true); }
    }

    private void WireEvents()
    {
        _autoWahModeCombo.SelectedIndexChanged += (_, _) =>
        {
            UpdateAutoWahModeUi();
            ScheduleParameterUpdate();
        };
        _autoWahCharacterCombo.SelectedIndexChanged += (_, _) =>
        {
            UpdateAutoWahModeUi();
            ScheduleParameterUpdate();
        };
        _distortionCharacterCombo.SelectedIndexChanged += (_, _) => UpdateDynamicEffectAccessibleNames();
        _driveCharacterCombo.SelectedIndexChanged += (_, _) => UpdateDynamicEffectAccessibleNames();
        _od1CharacterCombo.SelectedIndexChanged += (_, _) => UpdateDynamicEffectAccessibleNames();
        _boosterCharacterCombo.SelectedIndexChanged += (_, _) => UpdateDynamicEffectAccessibleNames();
        _compressorCharacterCombo.SelectedIndexChanged += (_, _) => UpdateDynamicEffectAccessibleNames();
        _flangerCharacterCombo.SelectedIndexChanged += (_, _) => UpdateDynamicEffectAccessibleNames();
        _delayCharacterCombo.SelectedIndexChanged += (_, _) => UpdateDynamicEffectAccessibleNames();
        _reverbCharacterCombo.SelectedIndexChanged += (_, _) =>
        {
            UpdateDynamicEffectAccessibleNames();
            // El tipo de reverb se aplica de inmediato. Antes dependía sólo del temporizador
            // general de controles y podía parecer que el nuevo carácter recién entraba al
            // apagar y volver a activar el efecto.
            if (!_loadingScene && !_loadingDualEffectMemory) UpdateParameters();
        };
        _chorusCharacterCombo.SelectedIndexChanged += (_, _) => UpdateDynamicEffectAccessibleNames();
        _octaverCharacterCombo.SelectedIndexChanged += (_, _) => UpdateDynamicEffectAccessibleNames();

        _refreshMeetOutputsButton.Click += (_, _) => LoadMeetOutputDevices(announce: true);
        _applyMeetOutputButton.Click += (_, _) => ApplyMeetOutput();
        _prepareVoicemeeterButton.Click += (_, _) => PrepareVoicemeeterForVideoCall();
        _checkConferenceMicButton.Click += (_, _) => CheckConferenceMicSignal();
        _checkFocusriteLoopbackButton.Click += (_, _) =>
        {
            CheckFocusriteLoopback(announce: false);
            bool requestedActive = _checkFocusriteLoopbackButton.Checked;

            if (_audioPreferences.LastClassProfile == "Guitarra" && !_voiceOnlyMode.Checked)
            {
                if (requestedActive)
                {
                    bool ok = PrepareGuitarClassFocusriteLoopback(announce: true);
                    if (!ok) _checkFocusriteLoopbackButton.Checked = false;
                }
                else
                {
                    _guitarClassLoopbackSelected = false;
                    // En Clase de Guitarra la ruta física usa Playback 1-2 directo.
                    // Soltar este interruptor sólo desmarca su uso recomendado; no
                    // alteramos el audio principal ni abrimos una salida duplicada.
                    SetStatus("Loopback Focusrite desmarcado para Clase de Guitarra. El audio principal de Amp Accessible continúa por Playback 1-2; no se abre ninguna salida virtual adicional.");
                }
                RefreshFocusriteLoopbackToggleState();
                return;
            }

            string profile = string.IsNullOrWhiteSpace(_audioPreferences.LastClassProfile)
                ? "Perfil actual"
                : $"Clase de {_audioPreferences.LastClassProfile}";

            if (requestedActive)
            {
                bool ok = PrepareFocusriteLoopbackOutput(profile, "Zoom");
                if (!ok) _checkFocusriteLoopbackButton.Checked = false;
            }
            else
            {
                if (_meetOutputEnabled.Checked && SelectedMeetOutputIsFocusrite())
                    _meetOutputEnabled.Checked = false;
                RefreshRecommendedConferenceMic(announce: false);
                CheckFocusriteLoopback(announce: false);
                SetStatus($"{profile}: Loopback Focusrite desactivado. La salida procesada para videollamada queda detenida; vuelva a pulsar el interruptor para activarla.");
            }

            RefreshFocusriteLoopbackToggleState();
        };
        _openVbCablePageButton.Click += (_, _) => OpenVbCableOfficialPage();
        _meetOutputEnabled.CheckedChanged += (_, _) => ApplyMeetOutput();
        _voiceOnlyMode.CheckedChanged += (_, _) =>
        {
            if (_voiceOnlyMode.Checked && !_voiceEnabled.Checked) _voiceEnabled.Checked = true;
            if (!_applyingClassProfile)
            {
                _audioPreferences.LastClassProfile = "Personalizado";
                UpdateClassProfileStatus();
            }
            ScheduleParameterUpdate();
            SetStatus(_voiceOnlyMode.Checked
                ? "Modo Voz activado. Sólo se procesará el micrófono de la entrada 1; guitarra, NAM y acompañamiento quedan silenciados."
                : "Modo Voz desactivado. Se restaura el funcionamiento normal de guitarra y micrófono.");
        };
        _voiceClassPresetButton.Click += (_, _) => ApplyVoiceClassPreset();
        _guitarClassPresetButton.Click += (_, _) => ApplyGuitarClassPreset();
        _voiceMonitorLevel.ValueChanged += (_, _) =>
        {
            if (!_voiceOnlyMode.Checked && !_applyingClassProfile)
                _audioPreferences.GuitarClassMonitorPercent = (float)_voiceMonitorLevel.Value;
            UpdateClassProfileStatus();
        };

        _midiRefreshButton.Click += (_, _) => RefreshMidiDevices(announce: true);
        _midiConnectButton.Click += (_, _) => ToggleMidiConnection();
        _midiProgramBanks.CheckedChanged += (_, _) =>
        {
            _midiSettings.ProgramChangesLoadFactoryBanks = _midiProgramBanks.Checked;
            try { MidiSettingsStore.Save(_midiSettings); } catch { }
        };
        _midiLearnButton.Click += (_, _) => StartMidiLearn();
        _midiCancelLearnButton.Click += (_, _) => CancelMidiLearn();
        _midiDeleteBindingButton.Click += (_, _) => DeleteSelectedMidiBinding();
        _midiBindingsList.SelectedIndexChanged += (_, _) => _midiDeleteBindingButton.Enabled = _midiBindingsList.SelectedIndex >= 0;
        _midiSimTypeCombo.SelectedIndexChanged += (_, _) =>
            _midiSimValue.Enabled = _midiSimTypeCombo.SelectedIndex != 1;
        _midiSimSendButton.Click += (_, _) => SimulateMidiMessage();

        _checkUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync();
        _installUpdatePackageButton.Click += (_, _) => InstallLocalUpdatePackage();
        _repairInstallationButton.Click += (_, _) => LaunchRepairInstaller();
        _uninstallApplicationButton.Click += (_, _) => LaunchUninstaller();

        _refreshAudioDiagnosticButton.Click += (_, _) => UpdateAudioDiagnosticPanel(announce: true);
        _readAudioDiagnosticButton.Click += (_, _) => ReadAudioDiagnosticSummary();
        _copyAudioDiagnosticButton.Click += (_, _) => CopyAudioDiagnosticReport();
        _saveAudioDiagnosticButton.Click += (_, _) => SaveAudioDiagnosticReport();
        _openAudioDiagnosticsFolderButton.Click += (_, _) => OpenAudioDiagnosticsFolder();

        _refreshDriversButton.Click += (_, _) => LoadDrivers(announce: true);
        _driverCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingAudioDeviceSelection) LoadInputs(announce: true);
        };
        _inputCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingAudioDeviceSelection)
            {
                SaveAudioDevicePreference();
                if (_inputCombo.SelectedIndex >= 0) SetStatus($"Entrada de guitarra seleccionada: {_inputCombo.SelectedItem}.");
            }
        };
        _asioPanelButton.Click += (_, _) => OpenAsioPanel();
        _applyBufferButton.Click += (_, _) => ApplySelectedBuffer();
        _bufferCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingScene) SaveAudioPreferences();
        };
        _masterVolume.ValueChanged += (_, _) =>
        {
            _engine.ConfigureMasterVolume((float)_masterVolume.Value);
            if (!_loadingScene) SaveAudioPreferences();
        };
        _startStopButton.Click += (_, _) => ToggleAudio();
        _twoGuitarMode.CheckedChanged += (_, _) => HandleTwoGuitarModeChanged();
        _tunerGuitarCombo.SelectedIndexChanged += (_, _) => HandleTunerGuitarChanged();
        _dualEditGuitarCombo.SelectedIndexChanged += (_, _) => HandleDualEditGuitarChanged();
        _dualQuickNamEnabled.CheckedChanged += (_, _) => ApplyDualQuickNamEnabled();
        _dualQuickNamLoadButton.Click += (_, _) => LoadSelectedDualQuickNam();
        _dualQuickNamPreviousButton.Click += (_, _) => LoadAdjacentDualQuickNam(-1);
        _dualQuickNamNextButton.Click += (_, _) => LoadAdjacentDualQuickNam(1);
        _dualQuickNamCombo.KeyDown += DualQuickNamCombo_KeyDown;
        _dualFactoryBankLoadButton.Click += (_, _) => LoadSelectedDualFactoryBank();
        _dualBankCombo.SelectedIndexChanged += (_, _) => HandleDualBankSelectionChanged();
        _dualBankSaveButton.Click += (_, _) => SaveDualGuitarBank();
        _dualBankLoadButton.Click += (_, _) => LoadSelectedDualGuitarBank();
        _dualBankDeleteButton.Click += (_, _) => DeleteSelectedDualGuitarBank();
        _dualSceneCombo.SelectedIndexChanged += (_, _) => HandleDualSceneSelectionChanged();
        _dualSceneSaveButton.Click += (_, _) => SaveDualGuitarScene();
        _dualSceneLoadButton.Click += (_, _) => LoadSelectedDualGuitarScene();
        _dualSceneDeleteButton.Click += (_, _) => DeleteSelectedDualGuitarScene();
        _dualSceneNextButton.Click += (_, _) => LoadNextDualGuitarScene();
        _guitar1ProcessingEnabled.CheckedChanged += (_, _) => { UpdateDualGuitarStatus(); UpdateParameters(); };
        _guitar1UseRigEffects.CheckedChanged += (_, _) => { UpdateDualGuitarStatus(); UpdateParameters(); SaveAudioPreferences(); };
        _guitar1AmpCombo.SelectedIndexChanged += (_, _) => { UpdateDualGuitarStatus(); ScheduleParameterUpdate(); };
        _guitar1Gain.ValueChanged += (_, _) => ScheduleParameterUpdate();
        _guitar1Output.ValueChanged += (_, _) => { UpdateDualGuitarStatus(); ScheduleParameterUpdate(); };
        _guitar1Mix.ValueChanged += (_, _) => { UpdateDualGuitarStatus(); ScheduleParameterUpdate(); };
        _guitar1Pan.ValueChanged += (_, _) => { UpdatePanAccessibleNames(); UpdateDualGuitarStatus(); ScheduleParameterUpdate(); };
        _guitar1Mute.CheckedChanged += (_, _) => { UpdateDualGuitarStatus(); UpdateParameters(); };
        _guitar1NamEnabled.CheckedChanged += (_, _) => { UpdateDualGuitarStatus(); UpdateParameters(); SaveAudioPreferences(); SyncDualQuickNamState(); };
        _guitar1NamIncludesCabinet.CheckedChanged += (_, _) => { UpdateParameters(); SaveAudioPreferences(); };
        _guitar1NamInputTrim.ValueChanged += (_, _) => { ScheduleParameterUpdate(); SaveAudioPreferences(); };
        _guitar1NamOutputTrim.ValueChanged += (_, _) => { ScheduleParameterUpdate(); SaveAudioPreferences(); };
        _guitar1NamAutoLevel.CheckedChanged += (_, _) => { UpdateParameters(); SaveAudioPreferences(); };
        _guitar1NamAutoLevelDb.ValueChanged += (_, _) => { ScheduleParameterUpdate(); SaveAudioPreferences(); };
        _guitar1NamLoadBankButton.Click += (_, _) => LoadSelectedGuitar1NamFromBank();
        _guitar1NamPreviousButton.Click += (_, _) => LoadAdjacentGuitar1NamFromBank(-1);
        _guitar1NamNextButton.Click += (_, _) => LoadAdjacentGuitar1NamFromBank(1);
        _guitar1NamLoadFileButton.Click += (_, _) => LoadGuitar1NamModelFromDialog();
        _guitar1NamClearButton.Click += (_, _) => ClearGuitar1NamModel();
        _guitar2Mix.ValueChanged += (_, _) => { UpdateDualGuitarStatus(); ScheduleParameterUpdate(); };
        _guitar2Pan.ValueChanged += (_, _) => { UpdatePanAccessibleNames(); UpdateDualGuitarStatus(); ScheduleParameterUpdate(); };
        _guitar2Mute.CheckedChanged += (_, _) => { UpdateDualGuitarStatus(); UpdateParameters(); };
        _guitar1LoadIrButton.Click += (_, _) => LoadGuitar1ImpulseResponse();
        _guitar1ClearIrButton.Click += (_, _) => ClearGuitar1ImpulseResponse();
        _guitar1SelectIrFolderButton.Click += (_, _) => SelectGuitar1IrBrowserFolder();
        _guitar1PreviousIrButton.Click += (_, _) => LoadAdjacentGuitar1ImpulseResponse(-1);
        _guitar1NextIrButton.Click += (_, _) => LoadAdjacentGuitar1ImpulseResponse(1);
        _loadIrButton.Click += (_, _) => LoadImpulseResponse();
        _clearIrButton.Click += (_, _) => ClearImpulseResponse();
        _selectIrFolderButton.Click += (_, _) => SelectIrBrowserFolder();
        _previousIrButton.Click += (_, _) => LoadAdjacentImpulseResponse(-1);
        _nextIrButton.Click += (_, _) => LoadAdjacentImpulseResponse(1);
        _loadIrBButton.Click += (_, _) => LoadImpulseResponseB();
        _clearIrBButton.Click += (_, _) => ClearImpulseResponseB();
        _namEditGuitarCombo.SelectedIndexChanged += (_, _) => UpdateNamSettingsGuitarContext();
        _loadNamButton.Click += (_, _) => LoadNamModelFromDialogForSelectedGuitar();
        _clearNamButton.Click += (_, _) => ClearNamModelForSelectedGuitar();
        _checkNamButton.Click += (_, _) => RefreshNamStatus(announce: true);
        _namGuideButton.Click += (_, _) => OpenNamGuide();
        _namSearchButton.Click += (_, _) => SearchNamCaptures();
        _loadLatestNamButton.Click += (_, _) => LoadLatestDownloadedNam();
        _loadNamBankButton.Click += (_, _) => LoadSelectedNamFromBankForSelectedGuitar();
        _previousNamButton.Click += (_, _) => LoadAdjacentNamFromBankForSelectedGuitar(-1);
        _nextNamButton.Click += (_, _) => LoadAdjacentNamFromBankForSelectedGuitar(1);
        _removeNamBankButton.Click += (_, _) => RemoveSelectedNamFromBank();
        _openNamBankFolderButton.Click += (_, _) => OpenNamBankFolder();
        _saveNamMetadataButton.Click += (_, _) => SaveSelectedNamMetadata();
        _clearNamFiltersButton.Click += (_, _) => ClearNamFilters();
        _namBankFilterText.TextChanged += (_, _) => RefreshNamBankCombo();
        _namCategoryFilter.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingNamFilters) RefreshNamBankCombo();
        };
        _namFavoritesOnly.CheckedChanged += (_, _) => RefreshNamBankCombo();
        _namBankCombo.SelectedIndexChanged += (_, _) => UpdateNamMetadataEditorFromSelection();
        _calibrateNamLevelButton.Click += (_, _) => StartNamLevelCalibration();
        _namBankCombo.KeyDown += NamBankCombo_KeyDown;
        _namSearchQuery.KeyDown += NamSearchQuery_KeyDown;
        _namEnabled.CheckedChanged += (_, _) => { ValidateNamEnableState(); SyncDualQuickNamState(); };
        _resetDelayButton.Click += (_, _) => ResetDelayMemory();
        _readTunerButton.Click += (_, _) => ReadTunerWithJaws();
        _playReferenceToneButton.Click += (_, _) => PlaySelectedReferenceTone();
        _effectSelector.SelectedIndexChanged += (_, _) => ShowSelectedEffectPanel();
        _delayTime.KeyDown += DelayTime_KeyDown;
        _tapTempoButton.Click += (_, _) => TapTempo();
        _restartMetronomeButton.Click += (_, _) => RestartMetronome();
        _practiceRecordButton.Click += (_, _) => StartPracticeRecording();
        _practiceStopButton.Click += async (_, _) => await FinalizePracticeRecordingAsync(autoCompleted: false);
        _practiceOpenFolderButton.Click += (_, _) => OpenPracticeRecordingsFolder();
        _loopRecordButton.Click += (_, _) => ToggleLoopFirstPass();
        _loopOverdubButton.Click += (_, _) => ToggleLoopOverdub();
        _loopUndoButton.Click += (_, _) => UndoLastLoopOverdub();
        _loopPlayButton.Click += (_, _) => ToggleLoopPlayback();
        _loopSourceCombo.SelectedIndexChanged += (_, _) =>
        {
            _engine.ConfigureLoopCaptureSource(SelectedLoopCaptureSource);
            if (!_loadingScene)
            {
                SaveAudioPreferences();
                SetStatus($"Fuente del looper: {CurrentLoopCaptureSourceName}. La selección se aplicará a la próxima captura u overdub.");
            }
        };
        _loopSyncTempo.CheckedChanged += (_, _) =>
        {
            _loopBars.Enabled = _loopSyncTempo.Checked;
            if (!_loadingScene) SaveAudioPreferences();
        };
        _loopBars.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingScene) SaveAudioPreferences();
        };
        _loopSaveButton.Click += async (_, _) => await SaveLoopAsync();
        _loopClearButton.Click += (_, _) => ClearLoop();
        _metronomeEnabled.CheckedChanged += (_, _) =>
        {
            if (_metronomeEnabled.Checked && !_engine.IsRunning)
            {
                SetStatus("Metrónomo activado. Pulse F4 para iniciar el audio y escuchar el click.");
            }
        };
        _drumsEnabled.CheckedChanged += (_, _) =>
        {
            if (!_updatingBackingPairShortcut)
            {
                _backingBandFollowGuitarArmed = false;
                _engine.RequestMetronomeReset();
            }
            if (_drumsEnabled.Checked && !_engine.IsRunning && !_updatingBackingPairShortcut)
            {
                SetStatus("Batería activada. Pulse F4 para iniciar el audio. El tempo es el mismo BPM del metrónomo.");
            }
        };
        _drumPattern.SelectedIndexChanged += (_, _) => _engine.RequestMetronomeReset();
        _backingBassEnabled.CheckedChanged += (_, _) =>
        {
            if (!_updatingBackingPairShortcut)
            {
                _backingBandFollowGuitarArmed = false;
                _engine.RequestMetronomeReset();
            }
            if (_backingBassEnabled.Checked && !_engine.IsRunning && !_updatingBackingPairShortcut)
            {
                SetStatus("Bajo de acompañamiento activado. Pulse F4 para iniciar el audio.");
            }
        };
        _backingBassKey.SelectedIndexChanged += (_, _) => _engine.RequestMetronomeReset();
        _backingBassMode.SelectedIndexChanged += (_, _) => _engine.RequestMetronomeReset();
        _backingBassLine.SelectedIndexChanged += (_, _) => _engine.RequestMetronomeReset();
        _pianoEnabled.CheckedChanged += (_, _) =>
        {
            if (!_updatingBackingPairShortcut)
            {
                _backingBandFollowGuitarArmed = false;
                _engine.RequestMetronomeReset();
            }
            if (_pianoEnabled.Checked && !_engine.IsRunning && !_updatingBackingPairShortcut)
                SetStatus("Piano de acompañamiento activado. Pulse F4 para iniciar el audio.");
        };
        _pianoSound.SelectedIndexChanged += (_, _) => _engine.RequestMetronomeReset();
        _pianoKey.SelectedIndexChanged += (_, _) => _engine.RequestMetronomeReset();
        _pianoProgression.SelectedIndexChanged += (_, _) =>
        {
            _engine.RequestMetronomeReset();
            if (_pianoProgression.SelectedIndex == 5)
                SetStatus("Progresión personalizada seleccionada. Edite Grados personalizados y pulse Aplicar grados.");
        };
        _pianoApplyCustomProgression.Click += (_, _) => ApplyCustomPianoProgression();
        _pianoSaveCustomProgression.Click += (_, _) => SaveCustomPianoProgression();
        _pianoLoadCustomProgression.Click += (_, _) => LoadSelectedCustomPianoProgression();
        _pianoDeleteCustomProgression.Click += (_, _) => DeleteSelectedCustomPianoProgression();
        _pianoStyle.SelectedIndexChanged += (_, _) => _engine.RequestMetronomeReset();

        _channelCombo.SelectedIndexChanged += (_, _) => HandleChannelChanged();
        _bass.ValueChanged += (_, _) => RememberCurrentChannelEq();
        _middle.ValueChanged += (_, _) => RememberCurrentChannelEq();
        _treble.ValueChanged += (_, _) => RememberCurrentChannelEq();
        _presence.ValueChanged += (_, _) => RememberCurrentChannelEq();

        // Al incorporar o quitar pedales con chorus/delay activos, el loop se aparta
        // unos milisegundos para que la transición no compita con el controlador ASIO.

        _sceneCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingScene && _sceneCombo.SelectedIndex >= 0)
            {
                RecallScene(_sceneCombo.SelectedIndex, announce: true);
            }
        };
        _loadSceneButton.Click += (_, _) => RecallScene(_sceneCombo.SelectedIndex, announce: true);
        _saveSceneButton.Click += (_, _) => SaveCurrentScene(_sceneCombo.SelectedIndex);
        _renameSceneButton.Click += (_, _) => RenameSelectedScene();
        _nextSceneButton.Click += (_, _) => LoadNextScene();
        _presetCombo.SelectedIndexChanged += (_, _) => HandlePresetSelectionChanged();
        _factoryPresetCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingFactoryPreset && _factoryPresetCombo.SelectedIndex >= 0)
                LoadSelectedFactoryPreset(announce: true);
        };
        _loadFactoryPresetButton.Click += (_, _) => LoadSelectedFactoryPreset(announce: true);
        _duplicateFactoryPresetButton.Click += (_, _) => DuplicateSelectedFactoryPreset();
        _savePresetButton.Click += (_, _) => SaveCurrentPreset();
        _loadPresetButton.Click += (_, _) => LoadSelectedPreset();
        _deletePresetButton.Click += (_, _) => DeleteSelectedPreset();
        _openPresetFolderButton.Click += (_, _) => OpenPresetFolder();
        _exportBackupButton.Click += (_, _) => ExportFullBackup();
        _restoreBackupButton.Click += (_, _) => RestoreFullBackup();
        _openBackupFolderButton.Click += (_, _) => OpenBackupFolder();
        _movePreEffectUpButton.Click += (_, _) => MoveSelectedPreEffect(-1);
        _movePreEffectDownButton.Click += (_, _) => MoveSelectedPreEffect(1);

        foreach (Control control in GetParameterControls())
        {
            switch (control)
            {
                case NumericUpDown numeric:
                    numeric.ValueChanged += (_, _) => ScheduleParameterUpdate();
                    break;
                case CheckBox checkBox:
                    checkBox.CheckedChanged += (_, _) => UpdateParameters();
                    break;
                case ComboBox comboBox:
                    comboBox.SelectedIndexChanged += (_, _) => ScheduleParameterUpdate();
                    break;
            }
        }

        FormClosing += (_, _) =>
        {
            if (_audioRequested || _engine.HasActiveSession)
            {
                CaptureAudioTelemetry();
                ConsumeAudioFaultAndSave(out _);
                if (_audioDiagnostics.LastSavedPath is null)
                {
                    try { _audioDiagnostics.Save("Cierre de la aplicación"); } catch { }
                }
            }
            _audioRequested = false;
            if ((_engine.IsPracticeRecording || _engine.HasPendingPracticeRecording) && !_practiceSaveInProgress)
            {
                try { _engine.StopAndSavePracticeRecordingAsync().GetAwaiter().GetResult(); } catch { }
            }
            _parameterUpdateTimer.Stop();
            DisconnectMidiInput(announce: false);
            try { MidiSettingsStore.Save(_midiSettings); } catch { }
            Application.RemoveMessageFilter(this);
            TrySaveSceneLibrary();
            SaveAudioPreferences();
            _voicemeeter.Dispose();
            _engine.Dispose();
        };
        KeyDown += MainForm_KeyDown;
    }

    private IEnumerable<Control> GetParameterControls()
    {
        yield return _simulationEnabled;
        yield return _channelCombo;
        yield return _gain;
        yield return _bass;
        yield return _middle;
        yield return _treble;
        yield return _presence;
        yield return _output;
        yield return _voiceEnabled;
        yield return _voiceOnlyMode;
        yield return _voiceClassPresetButton;
        yield return _guitarClassPresetButton;
        yield return _voiceSuppressorEnabled;
        yield return _voiceThreshold;
        yield return _voiceReduction;
        yield return _voiceRelease;
        yield return _voiceHighPass;
        yield return _voiceBass;
        yield return _voiceMid;
        yield return _voiceTreble;
        yield return _voiceLevel;
        yield return _voiceMonitorLevel;
        yield return _meetGuitarLevel;
        yield return _meetVoiceLevel;
        yield return _tunerGuitarCombo;
        yield return _tunerEnabled;
        yield return _tunerMuteOutput;
        yield return _tunerSoundGuide;
        yield return _tunerReferenceA;
        yield return _tunerGuideVolume;
        yield return _octaverEnabled;
        yield return _octaverCharacterCombo;
        yield return _octaverDry;
        yield return _octaverDown;
        yield return _octaverUp;
        yield return _octaverTone;
        yield return _octaverLevel;
        yield return _gateEnabled;
        yield return _gateThreshold;
        yield return _gateRelease;
        yield return _compressorEnabled;
        yield return _compressorSustain;
        yield return _compressorAttack;
        yield return _compressorLevel;
        yield return _autoWahEnabled;
        yield return _autoWahModeCombo;
        yield return _autoWahCharacterCombo;
        yield return _autoWahSensitivity;
        yield return _autoWahRange;
        yield return _autoWahResonance;
        yield return _autoWahManualPosition;
        yield return _overdriveEnabled;
        yield return _overdriveGain;
        yield return _overdriveTone;
        yield return _overdriveLevel;
        yield return _ts9Enabled;
        yield return _ts9Gain;
        yield return _ts9Tone;
        yield return _ts9Level;
        yield return _od1Enabled;
        yield return _od1CharacterCombo;
        yield return _od1Drive;
        yield return _od1Tone;
        yield return _od1Level;
        yield return _fuzzEnabled;
        yield return _fuzzCharacterCombo;
        yield return _fuzzGain;
        yield return _fuzzTone;
        yield return _fuzzLevel;
        yield return _eq5Enabled;
        yield return _eq5PlacementCombo;
        yield return _eq5Band100;
        yield return _eq5Band250;
        yield return _eq5Band800;
        yield return _eq5Band2500;
        yield return _eq5Band6400;
        yield return _eq5Output;
        yield return _boosterEnabled;
        yield return _boosterDb;
        yield return _externalIrEnabled;
        yield return _externalIrBEnabled;
        yield return _irMix;
        yield return _irBPhaseInvert;
        yield return _irLowCut;
        yield return _irHighCut;
        yield return _namEnabled;
        yield return _namIncludesCabinet;
        yield return _namInputTrim;
        yield return _namOutputTrim;
        yield return _namAutoLevel;
        yield return _metronomeEnabled;
        yield return _metronomeBpm;
        yield return _metronomeMeter;
        yield return _metronomeVolume;
        yield return _metronomeAccent;
        yield return _drumsEnabled;
        yield return _drumPattern;
        yield return _drumVolume;
        yield return _backingBassEnabled;
        yield return _backingBassKey;
        yield return _backingBassMode;
        yield return _backingBassLine;
        yield return _backingBassVolume;
        yield return _pianoEnabled;
        yield return _pianoKey;
        yield return _pianoSound;
        yield return _pianoProgression;
        yield return _pianoCustomProgression;
        yield return _pianoCustomProgressionName;
        yield return _pianoSavedProgression;
        yield return _pianoStyle;
        yield return _pianoVolume;
        yield return _fxLoopEnabled;
        yield return _fxLoopSend;
        yield return _fxLoopReturn;
        yield return _phaserEnabled;
        yield return _phaserRate;
        yield return _phaserDepth;
        yield return _phaserFeedback;
        yield return _phaserMix;
        yield return _flangerEnabled;
        yield return _flangerRate;
        yield return _flangerDepth;
        yield return _flangerFeedback;
        yield return _flangerMix;
        yield return _chorusEnabled;
        yield return _chorusPlacementCombo;
        yield return _chorusRate;
        yield return _chorusDepth;
        yield return _chorusMix;
        yield return _analogChorusEnabled;
        yield return _analogChorusPlacementCombo;
        yield return _analogChorusRate;
        yield return _analogChorusDepth;
        yield return _analogChorusMix;
        yield return _analogChorusLow;
        yield return _analogChorusHigh;
        yield return _microPitchEnabled;
        yield return _microPitchDetune;
        yield return _microPitchDelay;
        yield return _microPitchMix;
        yield return _delayEnabled;
        yield return _delayCharacterCombo;
        yield return _delayTime;
        yield return _delayFeedback;
        yield return _delayMix;
        yield return _reverbEnabled;
        yield return _reverbMix;
        yield return _reverbDecay;
        yield return _reverbTone;
        yield return _reverbPreDelay;
        yield return _reverbDamping;
        yield return _reverbDiffusion;
    }

    private void DelayTime_KeyDown(object? sender, KeyEventArgs e)
    {
        decimal delta = e.KeyCode switch
        {
            Keys.Up => 10m,
            Keys.Down => -10m,
            Keys.Right => 1m,
            Keys.Left => -1m,
            _ => 0m
        };

        if (delta == 0m) return;

        decimal next = _delayTime.Value + delta;
        if (next < _delayTime.Minimum) next = _delayTime.Minimum;
        if (next > _delayTime.Maximum) next = _delayTime.Maximum;
        _delayTime.Value = next;
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private static string PreEffectName(PreEffectSlot slot) => slot switch
    {
        PreEffectSlot.Booster => "Booster",
        PreEffectSlot.Compressor => "Compresor",
        PreEffectSlot.AutoWah => "Auto Wah",
        PreEffectSlot.Gate => "Supresor de ruido",
        PreEffectSlot.Octaver => "Octavador / Pitch",
        PreEffectSlot.Eq5 => "EQ gráfico de 5 bandas",
        PreEffectSlot.Od1 => "OD-1 / Fulltone OCD",
        PreEffectSlot.Divine => "Overdrive",
        PreEffectSlot.Ds1 => "Distorsiones",
        PreEffectSlot.Fuzz => "Fuzz",
        PreEffectSlot.Chorus => "Chorus por Input",
        _ => slot.ToString()
    };

    private void PopulatePreChainList(int selectedIndex = 0)
    {
        _preEffectOrder = UserPresetLibrary.NormalizeOrder(_preEffectOrder);
        _preChainList.BeginUpdate();
        try
        {
            _preChainList.Items.Clear();
            for (int i = 0; i < _preEffectOrder.Count; i++)
                _preChainList.Items.Add($"{i + 1}. {PreEffectName(_preEffectOrder[i])}");
            if (_preChainList.Items.Count > 0)
                _preChainList.SelectedIndex = Math.Clamp(selectedIndex, 0, _preChainList.Items.Count - 1);
        }
        finally { _preChainList.EndUpdate(); }
    }

    private void MoveSelectedPreEffect(int direction)
    {
        int index = _preChainList.SelectedIndex;
        if (index < 0)
        {
            if (_preChainList.Items.Count > 0) _preChainList.SelectedIndex = 0;
            SetStatus("Seleccione un pedal de la cadena previa para moverlo.");
            return;
        }
        int target = index + (direction < 0 ? -1 : 1);
        if (target < 0 || target >= _preEffectOrder.Count)
        {
            SetStatus(direction < 0 ? "Ese pedal ya es el primero de la cadena." : "Ese pedal ya es el último de la cadena.");
            return;
        }
        PreEffectSlot moved = _preEffectOrder[index];
        (_preEffectOrder[index], _preEffectOrder[target]) = (_preEffectOrder[target], _preEffectOrder[index]);
        PopulatePreChainList(target);
        UpdateParameters();
        _preChainList.Focus();
        SetStatus($"{PreEffectName(moved)} movido a la posición {target + 1}. Cadena: {string.Join(", ", _preEffectOrder.Select(PreEffectName))}.");
    }

    private void PopulatePresetList(int selectedIndex = -1)
    {
        _presetLibrary.Normalize();
        _loadingPresetList = true;
        try
        {
            _presetCombo.Items.Clear();
            foreach (UserPreset preset in _presetLibrary.Presets) _presetCombo.Items.Add(preset.Name);
            if (_presetCombo.Items.Count > 0)
            {
                int index = selectedIndex >= 0 ? selectedIndex : _presetLibrary.LastSelectedIndex;
                _presetCombo.SelectedIndex = Math.Clamp(index, 0, _presetCombo.Items.Count - 1);
                _presetName.Text = _presetLibrary.Presets[_presetCombo.SelectedIndex].Name;
            }
            else
            {
                _presetCombo.SelectedIndex = -1;
                _presetName.Text = string.Empty;
            }
        }
        finally { _loadingPresetList = false; }
    }

    private void HandlePresetSelectionChanged()
    {
        if (_loadingPresetList) return;
        int index = _presetCombo.SelectedIndex;
        if (index >= 0 && index < _presetLibrary.Presets.Count)
        {
            _presetName.Text = _presetLibrary.Presets[index].Name;
            _presetLibrary.LastSelectedIndex = index;
            try { PresetStore.Save(_presetLibrary); } catch { }
        }
    }

    private static string NormalizePresetName(string? text)
    {
        string value = string.IsNullOrWhiteSpace(text) ? "Preset" : text.Trim();
        return value.Length <= 60 ? value : value[..60];
    }

    private void LoadSelectedFactoryPreset(bool announce = true)
    {
        if (_loadingFactoryPreset) return;

        int index = _factoryPresetCombo.SelectedIndex;
        if (index < 0 || index >= FactoryPresetBank.Presets.Count)
        {
            if (announce) SetStatus("No hay un preset de fábrica seleccionado.", true);
            return;
        }

        UserPreset preset = FactoryPresetBank.Presets[index];
        _loadingFactoryPreset = true;
        try
        {
            // Aplicar el rig completo como una sola operación: sonido, cadena, NAM e IR.
            _preEffectOrder = UserPresetLibrary.NormalizeOrder(preset.PreEffectOrder);
            ApplyPresetSound(preset.Sound);
            PopulatePreChainList();

            _namEnabled.Checked = false;
            _namIncludesCabinet.Checked = false;
            _engine.Processor.ClearNamModel();
            UpdateDynamicEffectAccessibleNames();
            UpdateParameters();
        }
        finally
        {
            _loadingFactoryPreset = false;
        }

        if (announce)
        {
            string code = index + 1 <= 99 ? $"F{index + 1:00}" : $"F{index + 1}";
            string ampName = _channelCombo.SelectedItem?.ToString() ?? "amplificador";
            string activeEffects = DescribeActiveFactoryEffects(preset.Sound);
            SetStatus($"{code} {preset.Name} cargado. Amplificador {ampName}. {activeEffects}");
        }
    }

    private static string DescribeActiveFactoryEffects(ScenePreset scene)
    {
        var effects = new List<string>();
        if (scene.CompressorEnabled) effects.Add("compresor");
        if (scene.BoosterEnabled) effects.Add("booster");
        if (scene.Ts9Enabled) effects.Add($"overdrive {scene.DriveCharacter}");
        if (scene.Od1Enabled) effects.Add(scene.Od1Character switch
        {
            Od1Character.Late4558 => "Boss OD-1 Late 4558",
            Od1Character.OcdLp => "Fulltone OCD LP",
            Od1Character.OcdHp => "Fulltone OCD HP",
            _ => "Boss OD-1 Vintage 1977"
        });
        if (scene.OverdriveEnabled) effects.Add($"distorsión {scene.DistortionCharacter}");
        if (scene.FuzzEnabled) effects.Add($"fuzz {scene.FuzzCharacter}");
        if (scene.Eq5Enabled) effects.Add("EQ de 5 bandas");
        if (scene.OctaverEnabled) effects.Add($"octavador {scene.OctaverCharacter}");
        if (scene.AutoWahEnabled) effects.Add("auto wah");
        if (scene.PhaserEnabled) effects.Add("phaser");
        if (scene.ChorusEnabled) effects.Add("chorus");
        if (scene.FlangerEnabled) effects.Add("flanger");
        if (scene.MicroPitchEnabled) effects.Add("MicroPitch 80s");
        if (scene.RotaryEnabled) effects.Add("Leslie");
        if (scene.TremoloEnabled) effects.Add("tremolo");
        if (scene.DelayEnabled) effects.Add($"delay {scene.DelayCharacter}");
        if (scene.ReverbEnabled) effects.Add($"reverb {scene.ReverbCharacter}");

        return effects.Count == 0
            ? "Sin efectos adicionales."
            : $"Efectos activos: {string.Join(", ", effects)}.";
    }

    private void DuplicateSelectedFactoryPreset()
    {
        int index = _factoryPresetCombo.SelectedIndex;
        if (index < 0 || index >= FactoryPresetBank.Presets.Count)
        {
            SetStatus("No hay un preset de fábrica seleccionado.", true);
            return;
        }

        UserPreset source = FactoryPresetBank.Presets[index];
        string baseName = $"Copia de {source.Name}";
        string name = baseName;
        int suffix = 2;
        while (_presetLibrary.Presets.Any(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            name = $"{baseName} {suffix++}";

        var copy = source with { Name = name };
        _presetLibrary.Presets.Add(copy);
        _presetLibrary.LastSelectedIndex = _presetLibrary.Presets.Count - 1;
        try
        {
            PresetStore.Save(_presetLibrary);
            PopulatePresetList(_presetLibrary.LastSelectedIndex);
            _presetName.Text = name;
            SetStatus($"{source.Name} duplicado como {name}. Ya puede editarlo y guardarlo en el banco de usuario.");
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo duplicar el preset de fábrica: {ex.Message}", true);
        }
    }

    private void SaveCurrentPreset()
    {
        string name = NormalizePresetName(_presetName.Text);
        var preset = new UserPreset
        {
            Name = name,
            Sound = CaptureCurrentScene(name),
            NamEnabled = _namEnabled.Checked && _engine.Processor.HasNamModel,
            NamIncludesCabinet = _namIncludesCabinet.Checked,
            NamPath = _engine.Processor.NamModelPath,
            NamInputTrimDb = (float)_namInputTrim.Value,
            NamOutputTrimDb = (float)_namOutputTrim.Value,
            NamAutoLevelEnabled = _namAutoLevel.Checked,
            NamAutoLevelDb = (float)_namAutoLevelDb.Value,
            PreEffectOrder = _preEffectOrder.ToList()
        };

        int existing = _presetLibrary.Presets.FindIndex(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        int selected;
        if (existing >= 0)
        {
            _presetLibrary.Presets[existing] = preset;
            selected = existing;
        }
        else
        {
            _presetLibrary.Presets.Add(preset);
            selected = _presetLibrary.Presets.Count - 1;
        }
        _presetLibrary.LastSelectedIndex = selected;
        try
        {
            PresetStore.Save(_presetLibrary);
            PopulatePresetList(selected);
            SetStatus(existing >= 0 ? $"Preset {name} actualizado con el rig completo." : $"Preset {name} guardado. Banco de presets: {_presetLibrary.Presets.Count}.");
        }
        catch (Exception ex) { SetStatus($"No se pudo guardar el preset: {ex.Message}", true); }
    }

    private void LoadSelectedPreset()
    {
        int index = _presetCombo.SelectedIndex;
        if (index < 0 || index >= _presetLibrary.Presets.Count)
        {
            SetStatus("No hay un preset seleccionado.", true);
            return;
        }
        UserPreset preset = _presetLibrary.Presets[index];
        ApplyPresetSound(preset.Sound);
        _preEffectOrder = UserPresetLibrary.NormalizeOrder(preset.PreEffectOrder);
        PopulatePreChainList();

        _namIncludesCabinet.Checked = preset.NamIncludesCabinet;
        SetNumeric(_namInputTrim, preset.NamInputTrimDb);
        SetNumeric(_namOutputTrim, preset.NamOutputTrimDb);
        _namAutoLevel.Checked = preset.NamAutoLevelEnabled;
        SetNumeric(_namAutoLevelDb, preset.NamAutoLevelDb);
        string namNotice = string.Empty;
        if (preset.NamEnabled)
        {
            if (!string.IsNullOrWhiteSpace(preset.NamPath) && File.Exists(preset.NamPath))
            {
                TryLoadNamModel(preset.NamPath, announce: false, enableAfterLoad: true, resumeAudioAfterLoad: true);
            }
            else
            {
                _namEnabled.Checked = false;
                namNotice = " El archivo NAM guardado ya no existe; se dejó NAM desactivado.";
            }
        }
        else _namEnabled.Checked = false;

        _presetLibrary.LastSelectedIndex = index;
        try { PresetStore.Save(_presetLibrary); } catch { }
        UpdateParameters();
        SetStatus($"Preset {preset.Name} cargado. Cadena: {string.Join(", ", _preEffectOrder.Select(PreEffectName))}.{namNotice}", !string.IsNullOrEmpty(namNotice));
    }

    private void DeleteSelectedPreset()
    {
        int index = _presetCombo.SelectedIndex;
        if (index < 0 || index >= _presetLibrary.Presets.Count)
        {
            SetStatus("No hay un preset seleccionado.", true);
            return;
        }
        string name = _presetLibrary.Presets[index].Name;
        _presetLibrary.Presets.RemoveAt(index);
        _presetLibrary.LastSelectedIndex = _presetLibrary.Presets.Count == 0 ? -1 : Math.Min(index, _presetLibrary.Presets.Count - 1);
        try
        {
            PresetStore.Save(_presetLibrary);
            PopulatePresetList(_presetLibrary.LastSelectedIndex);
            SetStatus($"Preset {name} eliminado.");
        }
        catch (Exception ex) { SetStatus($"No se pudo eliminar el preset: {ex.Message}", true); }
    }

    private void OpenPresetFolder()
    {
        try
        {
            string folder = Path.GetDirectoryName(PresetStore.FilePath)!;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            SetStatus("Carpeta de presets abierta.");
        }
        catch (Exception ex) { SetStatus($"No se pudo abrir la carpeta de presets: {ex.Message}", true); }
    }

    private static string BackupFolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "GDM Amp Accessible", "Copias de seguridad");

    private void ExportFullBackup()
    {
        try
        {
            UpdateActiveNamBankSettings();
            SaveAudioPreferences();
            SceneStore.Save(_sceneLibrary);
            PresetStore.Save(_presetLibrary);
            DualGuitarBankStore.Save(_dualGuitarBankLibrary);
            DualGuitarSceneStore.Save(_dualGuitarSceneLibrary);
            NamLibraryStore.Save(_namLibrary);
            MidiSettingsStore.Save(_midiSettings);
            Directory.CreateDirectory(BackupFolderPath);

            using var dialog = new SaveFileDialog
            {
                Title = "Exportar copia completa de Amp Accessible",
                Filter = "Copia Amp Accessible (*.gdmbackup)|*.gdmbackup|Todos los archivos (*.*)|*.*",
                DefaultExt = "gdmbackup",
                AddExtension = true,
                InitialDirectory = BackupFolderPath,
                FileName = $"AmpAccessible_Backup_{DateTime.Now:yyyy-MM-dd_HHmm}.gdmbackup",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            string version = typeof(MainForm).Assembly.GetName().Version?.ToString() ?? "2.40.0";
            BackupService.Export(dialog.FileName, _sceneLibrary, _presetLibrary, _dualGuitarBankLibrary, _dualGuitarSceneLibrary, _audioPreferences, _namLibrary, _midiSettings, version);
            SetStatus($"Copia de seguridad completa creada: {Path.GetFileName(dialog.FileName)}. Incluye {_presetLibrary.Presets.Count} presets, {_dualGuitarBankLibrary.Guitar1Banks.Count + _dualGuitarBankLibrary.Guitar2Banks.Count} bancos de dos guitarras, {_dualGuitarSceneLibrary.Scenes.Count} escenas completas duales, {_namLibrary.Items.Count} capturas NAM y {_midiSettings.Bindings.Count} asignaciones MIDI.");
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo crear la copia de seguridad: {ex.Message}", true);
        }
    }

    private void RestoreFullBackup()
    {
        if (_audioRequested || _engine.HasActiveSession)
        {
            SetStatus("Para restaurar una copia, detenga primero el audio con F4.", true);
            return;
        }

        try
        {
            Directory.CreateDirectory(BackupFolderPath);
            using var dialog = new OpenFileDialog
            {
                Title = "Restaurar copia de Amp Accessible",
                Filter = "Copia Amp Accessible (*.gdmbackup)|*.gdmbackup|Todos los archivos (*.*)|*.*",
                InitialDirectory = BackupFolderPath,
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            DialogResult answer = MessageBox.Show(
                this,
                "La restauración reemplazará escenas, presets de usuario, bancos independientes y escenas completas de dos guitarras, preferencias, asignaciones MIDI y Banco NAM actuales por los de la copia seleccionada. Los 30 bancos de fábrica no se modifican. ¿Continuar?",
                "Restaurar copia de seguridad",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            DisconnectMidiInput(announce: false);
            BackupRestoreResult restored = BackupService.Restore(dialog.FileName);
            _sceneLibrary = restored.Scenes;
            _presetLibrary = restored.Presets;
            _dualGuitarBankLibrary = restored.DualGuitarBanks;
            _dualGuitarSceneLibrary = restored.DualGuitarScenes;
            _audioPreferences = restored.Audio;
            _namLibrary = restored.NamLibrary;
            _midiSettings = restored.Midi;
            _activeNamBankId = null;
            _guitar1ActiveNamBankId = null;

            _engine.Processor.ClearNamModel();
            _engine.Guitar1Processor.ClearNamModel();
            _namEnabled.Checked = false;
            _clearNamButton.Enabled = false;
            _namPath.Text = "Ningún modelo NAM cargado.";
            _guitar1NamEnabled.Checked = false;
            _guitar1NamClearButton.Enabled = false;
            _guitar1NamPath.Text = "Guitarra 1: ningún modelo NAM cargado.";

            _loadingScene = true;
            try
            {
                _bufferCombo.SelectedIndex = _audioPreferences.BufferSize switch
                {
                    64 => 0,
                    128 => 1,
                    256 => 2,
                    _ => 3
                };
                LoadMasterVolumePreference();
                LoadChannelEqPreferences();
                LoadVoicePreferences();
                LoadMetronomePreferences();
            }
            finally
            {
                _loadingScene = false;
            }

            PopulateSceneList();
            PopulatePresetList();
            RefreshDualGuitarBankList();
            RefreshDualGuitarSceneList();
            PopulateMidiActions();
            _midiProgramBanks.Checked = _midiSettings.ProgramChangesLoadFactoryBanks;
            RefreshMidiBindingsList();
            RefreshMidiDevices(announce: false);
            _namBankFilterText.Clear();
            _namFavoritesOnly.Checked = false;
            RefreshNamCategoryFilter("Todas las categorías");
            RefreshNamBankCombo();
            _loadingAudioDeviceSelection = true;
            try { _driverCombo.SelectedIndex = -1; }
            finally { _loadingAudioDeviceSelection = false; }
            LoadDrivers(announce: false);
            LoadNamPreferences();
            LoadIrBrowserPreference();
            LoadDualGuitarPreferences();
            LoadTunerPreference();
            RecallScene(_sceneLibrary.LastSelectedIndex, announce: false);
            InitializeDualEffectMemories();
            RefreshDualGuitarBankList();
            RefreshDualGuitarSceneList();
            UpdateParameters();
            TryAutoConnectMidi();

            string warning = restored.MissingAssetCount > 0
                ? $" Faltaron {restored.MissingAssetCount} archivos dentro de la copia y se omitieron."
                : string.Empty;
            SetStatus($"Copia restaurada. Presets: {_presetLibrary.Presets.Count}. Bancos de dos guitarras: {_dualGuitarBankLibrary.Guitar1Banks.Count + _dualGuitarBankLibrary.Guitar2Banks.Count}. Escenas completas duales: {_dualGuitarSceneLibrary.Scenes.Count}. NAM restauradas: {restored.RestoredNamCount}. IR restaurados: {restored.RestoredIrCount}. Asignaciones MIDI: {_midiSettings.Bindings.Count}.{warning}", restored.MissingAssetCount > 0);
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo restaurar la copia de seguridad: {ex.Message}", true);
        }
    }

    private void OpenBackupFolder()
    {
        try
        {
            Directory.CreateDirectory(BackupFolderPath);
            Process.Start(new ProcessStartInfo { FileName = BackupFolderPath, UseShellExecute = true });
            SetStatus("Carpeta de copias de seguridad abierta.");
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo abrir la carpeta de copias: {ex.Message}", true);
        }
    }

    private void ApplyPresetSound(ScenePreset scene)
    {
        bool missingExternalIr = false;
        _loadingScene = true;
        try
        {
            StoreEqForChannel(0, scene.CleanBass, scene.CleanMiddle, scene.CleanTreble, scene.CleanPresence);
            StoreEqForChannel(1, scene.CrunchBass, scene.CrunchMiddle, scene.CrunchTreble, scene.CrunchPresence);
            StoreEqForChannel(2, scene.LeadBass, scene.LeadMiddle, scene.LeadTreble, scene.LeadPresence);
            int ch = Math.Clamp((int)scene.Channel, 0, 8);
            _channelCombo.SelectedIndex = ch;
            SetNumeric(_gain, scene.Gain);

            // La ecualización principal del preset pertenece al amplificador seleccionado.
            // Antes se leían valores comunes de _channelEq y se ignoraban Bass/Middle/Treble/Presence
            // del preset, haciendo que muchos bancos sonaran prácticamente iguales.
            StoreEqForChannel(ch, scene.Bass, scene.Middle, scene.Treble, scene.Presence);
            SetNumeric(_bass, scene.Bass);
            SetNumeric(_middle, scene.Middle);
            SetNumeric(_treble, scene.Treble);
            SetNumeric(_presence, scene.Presence);
            SetNumeric(_output, scene.OutputPercent);
            _octaverEnabled.Checked = scene.OctaverEnabled;
        _octaverCharacterCombo.SelectedIndex = Math.Clamp((int)scene.OctaverCharacter, 0, 4);
        SetNumeric(_octaverDry, scene.OctaverDryPercent);
        SetNumeric(_octaverDown, scene.OctaverDownPercent);
        SetNumeric(_octaverUp, scene.OctaverUpPercent);
        SetNumeric(_octaverTone, scene.OctaverTonePercent);
        SetNumeric(_octaverLevel, scene.OctaverLevelPercent);
        _gateEnabled.Checked = scene.GateEnabled; SetNumeric(_gateThreshold, scene.GateThresholdDb); SetNumeric(_gateRelease, scene.GateReleaseMs);
            _compressorEnabled.Checked = scene.CompressorEnabled;
        _compressorCharacterCombo.SelectedIndex=Math.Clamp((int)scene.CompressorCharacter,0,3); SetNumeric(_compressorSustain, scene.CompressorSustain); SetNumeric(_compressorAttack, scene.CompressorAttackMs); SetNumeric(_compressorLevel, scene.CompressorLevel);
            _autoWahEnabled.Checked = scene.AutoWahEnabled; _autoWahModeCombo.SelectedIndex = Math.Clamp((int)scene.AutoWahMode, 0, 1); _autoWahCharacterCombo.SelectedIndex = Math.Clamp((int)scene.AutoWahCharacter, 0, 2); SetNumeric(_autoWahSensitivity, scene.AutoWahSensitivity); SetNumeric(_autoWahRange, scene.AutoWahRange); SetNumeric(_autoWahResonance, scene.AutoWahResonance); SetNumeric(_autoWahManualPosition, scene.AutoWahManualPositionPercent); UpdateAutoWahModeUi();
            _overdriveEnabled.Checked = scene.OverdriveEnabled;
        _distortionCharacterCombo.SelectedIndex=Math.Clamp((int)scene.DistortionCharacter,0,3); SetNumeric(_overdriveGain, scene.OverdriveGain); SetNumeric(_overdriveTone, scene.OverdriveTone); SetNumeric(_overdriveLevel, scene.OverdriveLevel);
            _ts9Enabled.Checked = scene.Ts9Enabled;
        _driveCharacterCombo.SelectedIndex = Math.Clamp((int)scene.DriveCharacter, 0, 4); SetNumeric(_ts9Gain, scene.Ts9Gain); SetNumeric(_ts9Tone, scene.Ts9Tone); SetNumeric(_ts9Level, scene.Ts9Level);
            ApplyNewPedalControls(scene);
            _boosterEnabled.Checked = scene.BoosterEnabled;
        _boosterCharacterCombo.SelectedIndex = Math.Clamp((int)scene.BoosterCharacter, 0, 2); SetNumeric(_boosterDb, scene.BoosterDb);
            _preEffectOrder = UserPresetLibrary.NormalizeOrder(scene.PreEffectOrder);
            ApplySceneImpulseResponse(scene, out missingExternalIr);
            _fxLoopEnabled.Checked = scene.FxLoopEnabled; SetNumeric(_fxLoopSend, scene.FxLoopSendPercent); SetNumeric(_fxLoopReturn, scene.FxLoopReturnPercent);
            _phaserEnabled.Checked = scene.PhaserEnabled; SetNumeric(_phaserRate, scene.PhaserRateHz); SetNumeric(_phaserDepth, scene.PhaserDepthPercent); SetNumeric(_phaserFeedback, scene.PhaserFeedbackPercent); SetNumeric(_phaserMix, scene.PhaserMixPercent);
            _flangerEnabled.Checked = scene.FlangerEnabled;
        _flangerCharacterCombo.SelectedIndex = Math.Clamp((int)scene.FlangerCharacter, 0, 2); SetNumeric(_flangerRate, scene.FlangerRateHz); SetNumeric(_flangerDepth, scene.FlangerDepthPercent); SetNumeric(_flangerFeedback, scene.FlangerFeedbackPercent); SetNumeric(_flangerMix, scene.FlangerMixPercent);
            _chorusEnabled.Checked = scene.ChorusEnabled;
        _chorusPlacementCombo.SelectedIndex = Math.Clamp((int)scene.ChorusPlacement, 0, 1);
        _chorusCharacterCombo.SelectedIndex = Math.Clamp((int)scene.ChorusCharacter, 0, 1); SetNumeric(_chorusRate, scene.ChorusRateHz); SetNumeric(_chorusDepth, scene.ChorusDepthMs); SetNumeric(_chorusMix, scene.ChorusMixPercent);
            _analogChorusEnabled.Checked = scene.AnalogChorusEnabled; _analogChorusPlacementCombo.SelectedIndex = Math.Clamp((int)scene.AnalogChorusPlacement, 0, 1); SetNumeric(_analogChorusRate, scene.AnalogChorusRateHz); SetNumeric(_analogChorusDepth, scene.AnalogChorusDepth); SetNumeric(_analogChorusMix, scene.AnalogChorusMixPercent); SetNumeric(_analogChorusLow, scene.AnalogChorusLow); SetNumeric(_analogChorusHigh, scene.AnalogChorusHigh);
            _microPitchEnabled.Checked = scene.MicroPitchEnabled;
            SetNumeric(_microPitchDetune, scene.MicroPitchDetuneCents);
            SetNumeric(_microPitchDelay, scene.MicroPitchDelayMs);
            SetNumeric(_microPitchMix, scene.MicroPitchMixPercent);
            _rotaryEnabled.Checked = scene.RotaryEnabled;
        _rotarySync.Checked = scene.RotarySyncEnabled;
        _rotaryDivision.SelectedIndex = Math.Clamp((int)scene.RotaryDivision, 0, 4);
        _rotaryFast.Checked = scene.RotaryFast;
        SetNumeric(_rotaryDepth, scene.RotaryDepthPercent); SetNumeric(_rotaryMix, scene.RotaryMixPercent);
        _tremoloEnabled.Checked = scene.TremoloEnabled;
        _tremoloSync.Checked = scene.TremoloSyncEnabled;
        _tremoloDivision.SelectedIndex = Math.Clamp((int)scene.TremoloDivision, 0, 4);
        SetNumeric(_tremoloRate, scene.TremoloRateHz);
        SetNumeric(_tremoloDepth, scene.TremoloDepthPercent);
        _delayEnabled.Checked = scene.DelayEnabled;
        _delaySync.Checked = scene.DelaySyncEnabled;
        _delayDivision.SelectedIndex = Math.Clamp((int)scene.DelayDivision, 0, 4); _delayCharacterCombo.SelectedIndex = Math.Clamp((int)scene.DelayCharacter, 0, 3); SetNumeric(_delayTime, scene.DelayTimeMs); SetNumeric(_delayFeedback, scene.DelayFeedbackPercent); SetNumeric(_delayMix, scene.DelayMixPercent);
            _reverbEnabled.Checked = scene.ReverbEnabled;
        _reverbCharacterCombo.SelectedIndex = Math.Clamp((int)scene.ReverbCharacter, 0, 6); SetNumeric(_reverbMix, scene.ReverbMixPercent); SetNumeric(_reverbDecay, scene.ReverbDecayPercent); SetNumeric(_reverbTone, scene.ReverbTonePercent);
        SetNumeric(_reverbPreDelay, scene.ReverbPreDelayMs);
        SetNumeric(_reverbDamping, scene.ReverbDampingPercent);
        SetNumeric(_reverbDiffusion, scene.ReverbDiffusionPercent);
            if (scene.AccompanimentStored)
            {
                _metronomeEnabled.Checked = scene.SceneMetronomeEnabled; SetNumeric(_metronomeBpm, scene.SceneMetronomeBpm);
                _metronomeMeter.SelectedIndex = scene.SceneMetronomeBeatsPerBar switch { 2 => 0, 3 => 1, 6 => 3, _ => 2 };
                _metronomeAccent.Checked = scene.SceneMetronomeAccentFirstBeat; SetNumeric(_metronomeVolume, scene.SceneMetronomeVolumePercent);
                _drumsEnabled.Checked = scene.SceneDrumsEnabled; _drumPattern.SelectedIndex = Math.Clamp(scene.SceneDrumPattern, 0, 5); SetNumeric(_drumVolume, scene.SceneDrumVolumePercent);
                _backingBassEnabled.Checked = scene.SceneBackingBassEnabled; _backingBassKey.SelectedIndex = Math.Clamp(scene.SceneBackingBassKey, 0, 11); _backingBassMode.SelectedIndex = scene.SceneBackingBassMinor ? 1 : 0; _backingBassLine.SelectedIndex = Math.Clamp(scene.SceneBackingBassLine, 0, 4); SetNumeric(_backingBassVolume, scene.SceneBackingBassVolumePercent);
                _pianoEnabled.Checked = scene.ScenePianoEnabled; _pianoSound.SelectedIndex = Math.Clamp(scene.ScenePianoSound, 0, 5); _pianoKey.SelectedIndex = Math.Clamp(scene.ScenePianoKey, 0, 23); _pianoProgression.SelectedIndex = Math.Clamp(scene.ScenePianoProgression, 0, 5); _pianoCustomProgression.Text = string.IsNullOrWhiteSpace(scene.ScenePianoCustomProgression) ? "I, V, vi, IV" : scene.ScenePianoCustomProgression; _pianoStyle.SelectedIndex = Math.Clamp(scene.ScenePianoStyle, 0, 8); SetNumeric(_pianoVolume, scene.ScenePianoVolumePercent);
                _engine.RequestMetronomeReset();
            }
            _rememberedChannelIndex = _channelCombo.SelectedIndex;
        }
        finally { _loadingScene = false; }
        _engine.Processor.RequestDelayReset();
        UpdateParameters();
        if (missingExternalIr) SetStatus("El IR del preset no fue encontrado; se usó el gabinete interno V30.", true);
    }

    private void PopulateSceneList()
    {
        _sceneLibrary.Normalize();
        _loadingScene = true;
        try
        {
            _sceneCombo.BeginUpdate();
            _sceneCombo.Items.Clear();
            for (int index = 0; index < 3; index++)
            {
                _sceneCombo.Items.Add($"Escena {index + 1}: {_sceneLibrary.Scenes[index].Name}");
            }
            _sceneCombo.EndUpdate();
            _sceneCombo.SelectedIndex = Math.Clamp(_sceneLibrary.LastSelectedIndex, 0, 2);
            _sceneName.Text = _sceneLibrary.Scenes[_sceneCombo.SelectedIndex].Name;
        }
        finally
        {
            _loadingScene = false;
        }
    }

    private void RefreshSceneList(int selectedIndex)
    {
        _sceneLibrary.LastSelectedIndex = Math.Clamp(selectedIndex, 0, 2);
        PopulateSceneList();
        _sceneCombo.SelectedIndex = _sceneLibrary.LastSelectedIndex;
    }

    private void ApplyNewPedalControls(ScenePreset scene)
    {
        _od1Enabled.Checked = scene.Od1Enabled;
        _od1CharacterCombo.SelectedIndex = Math.Clamp((int)scene.Od1Character, 0, 3);
        SetNumeric(_od1Drive, scene.Od1Drive);
        SetNumeric(_od1Tone, scene.Od1Tone);
        SetNumeric(_od1Level, scene.Od1Level);
        _fuzzEnabled.Checked = scene.FuzzEnabled;
        _fuzzCharacterCombo.SelectedIndex = Math.Clamp((int)scene.FuzzCharacter, 0, 1);
        SetNumeric(_fuzzGain, scene.FuzzGain);
        SetNumeric(_fuzzTone, scene.FuzzTone);
        SetNumeric(_fuzzLevel, scene.FuzzLevel);
        _eq5Enabled.Checked = scene.Eq5Enabled;
        _eq5PlacementCombo.SelectedIndex = Math.Clamp((int)scene.Eq5Placement, 0, 1);
        SetNumeric(_eq5Band100, scene.Eq5Band100Db);
        SetNumeric(_eq5Band250, scene.Eq5Band250Db);
        SetNumeric(_eq5Band800, scene.Eq5Band800Db);
        SetNumeric(_eq5Band2500, scene.Eq5Band2500Db);
        SetNumeric(_eq5Band6400, scene.Eq5Band6400Db);
        SetNumeric(_eq5Output, scene.Eq5OutputDb);
    }

    private ScenePreset CaptureCurrentScene(string name)
    {
        return new ScenePreset
        {
            Name = NormalizeSceneName(name),
            Channel = (AmpChannel)Math.Max(0, _channelCombo.SelectedIndex),
            Gain = (float)_gain.Value,
            Bass = (float)_bass.Value,
            Middle = (float)_middle.Value,
            Treble = (float)_treble.Value,
            Presence = (float)_presence.Value,

            CleanBass = _channelEq[0, 0],
            CleanMiddle = _channelEq[0, 1],
            CleanTreble = _channelEq[0, 2],
            CleanPresence = _channelEq[0, 3],
            CrunchBass = _channelEq[1, 0],
            CrunchMiddle = _channelEq[1, 1],
            CrunchTreble = _channelEq[1, 2],
            CrunchPresence = _channelEq[1, 3],
            LeadBass = _channelEq[2, 0],
            LeadMiddle = _channelEq[2, 1],
            LeadTreble = _channelEq[2, 2],
            LeadPresence = _channelEq[2, 3],

            OutputPercent = (float)_output.Value,
            OctaverEnabled = _octaverEnabled.Checked,
            OctaverCharacter = (OctaverCharacter)Math.Max(0, _octaverCharacterCombo.SelectedIndex),
            OctaverDryPercent = (float)_octaverDry.Value,
            OctaverDownPercent = (float)_octaverDown.Value,
            OctaverUpPercent = (float)_octaverUp.Value,
            OctaverTonePercent = (float)_octaverTone.Value,
            OctaverLevelPercent = (float)_octaverLevel.Value,
            GateEnabled = _gateEnabled.Checked,
            GateThresholdDb = (float)_gateThreshold.Value,
            GateReleaseMs = (float)_gateRelease.Value,
            CompressorEnabled = _compressorEnabled.Checked,
            CompressorCharacter = (CompressorCharacter)Math.Max(0,_compressorCharacterCombo.SelectedIndex),
            CompressorSustain = (float)_compressorSustain.Value,
            CompressorAttackMs = (float)_compressorAttack.Value,
            CompressorLevel = (float)_compressorLevel.Value,
            AutoWahEnabled = _autoWahEnabled.Checked,
            AutoWahMode = (AutoWahMode)Math.Clamp(_autoWahModeCombo.SelectedIndex, 0, 1),
            AutoWahCharacter = (AutoWahCharacter)Math.Clamp(_autoWahCharacterCombo.SelectedIndex, 0, 2),
            AutoWahSensitivity = (float)_autoWahSensitivity.Value,
            AutoWahRange = (float)_autoWahRange.Value,
            AutoWahResonance = (float)_autoWahResonance.Value,
            AutoWahManualPositionPercent = (float)_autoWahManualPosition.Value,
            OverdriveEnabled = _overdriveEnabled.Checked,
            DistortionCharacter = (DistortionCharacter)Math.Max(0,_distortionCharacterCombo.SelectedIndex),
            OverdriveGain = (float)_overdriveGain.Value,
            OverdriveTone = (float)_overdriveTone.Value,
            OverdriveLevel = (float)_overdriveLevel.Value,
            Ts9Enabled = _ts9Enabled.Checked,
            DriveCharacter = (DriveCharacter)Math.Max(0, _driveCharacterCombo.SelectedIndex),
            Ts9Gain = (float)_ts9Gain.Value,
            Ts9Tone = (float)_ts9Tone.Value,
            Ts9Level = (float)_ts9Level.Value,
            Od1Enabled = _od1Enabled.Checked,
            Od1Character = (Od1Character)Math.Max(0, _od1CharacterCombo.SelectedIndex),
            Od1Drive = (float)_od1Drive.Value,
            Od1Tone = (float)_od1Tone.Value,
            Od1Level = (float)_od1Level.Value,
            FuzzEnabled = _fuzzEnabled.Checked,
            FuzzCharacter = (FuzzCharacter)Math.Max(0, _fuzzCharacterCombo.SelectedIndex),
            FuzzGain = (float)_fuzzGain.Value,
            FuzzTone = (float)_fuzzTone.Value,
            FuzzLevel = (float)_fuzzLevel.Value,
            Eq5Enabled = _eq5Enabled.Checked,
            Eq5Placement = (EqPlacement)Math.Max(0, _eq5PlacementCombo.SelectedIndex),
            Eq5Band100Db = (float)_eq5Band100.Value,
            Eq5Band250Db = (float)_eq5Band250.Value,
            Eq5Band800Db = (float)_eq5Band800.Value,
            Eq5Band2500Db = (float)_eq5Band2500.Value,
            Eq5Band6400Db = (float)_eq5Band6400.Value,
            Eq5OutputDb = (float)_eq5Output.Value,
            BoosterEnabled = _boosterEnabled.Checked,
            BoosterCharacter = (BoosterCharacter)Math.Max(0, _boosterCharacterCombo.SelectedIndex),
            BoosterDb = (float)_boosterDb.Value,
            PreEffectOrder = _preEffectOrder.ToArray(),
            ExternalIrEnabled = _externalIrEnabled.Checked && !string.IsNullOrWhiteSpace(_loadedIrPath),
            ExternalIrPath = _loadedIrPath,
            ExternalIrBEnabled = _externalIrBEnabled.Checked && !string.IsNullOrWhiteSpace(_loadedIrPathB),
            ExternalIrBPath = _loadedIrPathB,
            IrMixPercent = (float)_irMix.Value,
            IrBPhaseInvert = _irBPhaseInvert.Checked,
            IrLowCutHz = (float)_irLowCut.Value,
            IrHighCutHz = (float)_irHighCut.Value,
            FxLoopEnabled = _fxLoopEnabled.Checked,
            FxLoopSendPercent = (float)_fxLoopSend.Value,
            FxLoopReturnPercent = (float)_fxLoopReturn.Value,
            PhaserEnabled = _phaserEnabled.Checked,
            PhaserRateHz = (float)_phaserRate.Value,
            PhaserDepthPercent = (float)_phaserDepth.Value,
            PhaserFeedbackPercent = (float)_phaserFeedback.Value,
            PhaserMixPercent = (float)_phaserMix.Value,
            FlangerEnabled = _flangerEnabled.Checked,
            FlangerCharacter = (FlangerCharacter)Math.Max(0, _flangerCharacterCombo.SelectedIndex),
            FlangerRateHz = (float)_flangerRate.Value,
            FlangerDepthPercent = (float)_flangerDepth.Value,
            FlangerFeedbackPercent = (float)_flangerFeedback.Value,
            FlangerMixPercent = (float)_flangerMix.Value,
            ChorusEnabled = _chorusEnabled.Checked,
            ChorusPlacement = (ChorusPlacement)Math.Max(0, _chorusPlacementCombo.SelectedIndex),
            ChorusCharacter = (ChorusCharacter)Math.Max(0, _chorusCharacterCombo.SelectedIndex),
            ChorusRateHz = (float)_chorusRate.Value,
            ChorusDepthMs = (float)_chorusDepth.Value,
            ChorusMixPercent = (float)_chorusMix.Value,
            AnalogChorusEnabled = _analogChorusEnabled.Checked,
            AnalogChorusPlacement = (ChorusPlacement)Math.Max(0, _analogChorusPlacementCombo.SelectedIndex),
            AnalogChorusRateHz = (float)_analogChorusRate.Value,
            AnalogChorusDepth = (float)_analogChorusDepth.Value,
            AnalogChorusMixPercent = (float)_analogChorusMix.Value,
            AnalogChorusLow = (float)_analogChorusLow.Value,
            AnalogChorusHigh = (float)_analogChorusHigh.Value,
            MicroPitchEnabled = _microPitchEnabled.Checked,
            MicroPitchDetuneCents = (float)_microPitchDetune.Value,
            MicroPitchDelayMs = (float)_microPitchDelay.Value,
            MicroPitchMixPercent = (float)_microPitchMix.Value,
            RotaryEnabled = _rotaryEnabled.Checked,
            RotarySyncEnabled = _rotarySync.Checked,
            RotaryDivision = (TempoDivision)Math.Max(0, _rotaryDivision.SelectedIndex),
            RotaryRateHz = _rotarySync.Checked ? SyncedRotaryHz() : 0f,
            RotaryFast = _rotaryFast.Checked,
            RotaryDepthPercent = (float)_rotaryDepth.Value,
            RotaryMixPercent = (float)_rotaryMix.Value,
            TremoloEnabled = _tremoloEnabled.Checked,
            TremoloSyncEnabled = _tremoloSync.Checked,
            TremoloDivision = (TempoDivision)Math.Max(0, _tremoloDivision.SelectedIndex),
            TremoloRateHz = _tremoloSync.Checked ? SyncedTremoloHz() : (float)_tremoloRate.Value,
            TremoloDepthPercent = (float)_tremoloDepth.Value,
            DelayEnabled = _delayEnabled.Checked,
            DelaySyncEnabled = _delaySync.Checked,
            DelayDivision = (TempoDivision)Math.Max(0, _delayDivision.SelectedIndex),
            DelayCharacter = (DelayCharacter)Math.Max(0, _delayCharacterCombo.SelectedIndex),
            DelayTimeMs = _delaySync.Checked ? SyncedDelayMilliseconds() : (float)_delayTime.Value,
            DelayFeedbackPercent = (float)_delayFeedback.Value,
            DelayMixPercent = (float)_delayMix.Value,
            ReverbEnabled = _reverbEnabled.Checked,
            ReverbCharacter = (ReverbCharacter)Math.Max(0, _reverbCharacterCombo.SelectedIndex),
            ReverbMixPercent = (float)_reverbMix.Value,
            ReverbDecayPercent = (float)_reverbDecay.Value,
            ReverbTonePercent = (float)_reverbTone.Value,
            ReverbPreDelayMs = (float)_reverbPreDelay.Value,
            ReverbDampingPercent = (float)_reverbDamping.Value,
            ReverbDiffusionPercent = (float)_reverbDiffusion.Value,

            AccompanimentStored = true,
            SceneMetronomeEnabled = _metronomeEnabled.Checked,
            SceneMetronomeBpm = (float)_metronomeBpm.Value,
            SceneMetronomeBeatsPerBar = SelectedMetronomeBeatsPerBar,
            SceneMetronomeAccentFirstBeat = _metronomeAccent.Checked,
            SceneMetronomeVolumePercent = (float)_metronomeVolume.Value,
            SceneDrumsEnabled = _drumsEnabled.Checked,
            SceneDrumPattern = Math.Clamp(_drumPattern.SelectedIndex, 0, 5),
            SceneDrumVolumePercent = (float)_drumVolume.Value,
            SceneBackingBassEnabled = _backingBassEnabled.Checked,
            SceneBackingBassKey = Math.Clamp(_backingBassKey.SelectedIndex, 0, 11),
            SceneBackingBassMinor = _backingBassMode.SelectedIndex == 1,
            SceneBackingBassLine = Math.Clamp(_backingBassLine.SelectedIndex, 0, 4),
            SceneBackingBassVolumePercent = (float)_backingBassVolume.Value,
            ScenePianoEnabled = _pianoEnabled.Checked,
            ScenePianoSound = Math.Clamp(_pianoSound.SelectedIndex, 0, 5),
            ScenePianoKey = Math.Clamp(_pianoKey.SelectedIndex, 0, 23),
            ScenePianoProgression = Math.Clamp(_pianoProgression.SelectedIndex, 0, 5),
            ScenePianoCustomProgression = _pianoCustomProgression.Text.Trim(),
            ScenePianoStyle = Math.Clamp(_pianoStyle.SelectedIndex, 0, 8),
            ScenePianoVolumePercent = (float)_pianoVolume.Value
        };
    }

    private void SaveCurrentScene(int index, bool useNameField = true)
    {
        if (index < 0 || index >= 3)
        {
            SetStatus("Seleccione una escena antes de guardar.", true);
            return;
        }

        string name = useNameField && index == _sceneCombo.SelectedIndex
            ? NormalizeSceneName(_sceneName.Text)
            : _sceneLibrary.Scenes[index].Name;
        _sceneLibrary.Scenes[index] = CaptureCurrentScene(name);
        _sceneLibrary.LastSelectedIndex = index;
        _activeSceneIndex = index;

        if (!TrySaveSceneLibrary())
        {
            return;
        }

        RefreshSceneList(index);
        SetStatus($"Escena {index + 1}, {name}, guardada con la configuración actual.");
    }

    private void RenameSelectedScene()
    {
        int index = _sceneCombo.SelectedIndex;
        if (index < 0 || index >= 3)
        {
            SetStatus("Seleccione una escena antes de renombrarla.", true);
            return;
        }

        string name = NormalizeSceneName(_sceneName.Text);
        _sceneLibrary.Scenes[index] = _sceneLibrary.Scenes[index] with { Name = name };
        _sceneLibrary.LastSelectedIndex = index;
        if (!TrySaveSceneLibrary())
        {
            return;
        }

        RefreshSceneList(index);
        SetStatus($"Escena {index + 1} renombrada como {name}.");
    }

    private void RecallScene(int index, bool announce)
    {
        if (index < 0 || index >= 3)
        {
            return;
        }

        ScenePreset scene = _sceneLibrary.Scenes[index];
        bool missingExternalIr = false;
        _loadingScene = true;
        try
        {
            _sceneCombo.SelectedIndex = index;
            _sceneName.Text = scene.Name;

            // Primero restauramos las tres EQ de la escena y recién después cargamos
            // en pantalla la correspondiente al canal activo.
            StoreEqForChannel(0, scene.CleanBass, scene.CleanMiddle, scene.CleanTreble, scene.CleanPresence);
            StoreEqForChannel(1, scene.CrunchBass, scene.CrunchMiddle, scene.CrunchTreble, scene.CrunchPresence);
            StoreEqForChannel(2, scene.LeadBass, scene.LeadMiddle, scene.LeadTreble, scene.LeadPresence);

            int sceneChannel = Math.Clamp((int)scene.Channel, 0, 8);
            _channelCombo.SelectedIndex = sceneChannel;
            SetNumeric(_gain, scene.Gain);
            SetNumeric(_bass, _channelEq[sceneChannel, 0]);
            SetNumeric(_middle, _channelEq[sceneChannel, 1]);
            SetNumeric(_treble, _channelEq[sceneChannel, 2]);
            SetNumeric(_presence, _channelEq[sceneChannel, 3]);
            SetNumeric(_output, scene.OutputPercent);

            _octaverEnabled.Checked = scene.OctaverEnabled;
        _octaverCharacterCombo.SelectedIndex = Math.Clamp((int)scene.OctaverCharacter, 0, 4);
        SetNumeric(_octaverDry, scene.OctaverDryPercent);
        SetNumeric(_octaverDown, scene.OctaverDownPercent);
        SetNumeric(_octaverUp, scene.OctaverUpPercent);
        SetNumeric(_octaverTone, scene.OctaverTonePercent);
        SetNumeric(_octaverLevel, scene.OctaverLevelPercent);
        _gateEnabled.Checked = scene.GateEnabled;
            SetNumeric(_gateThreshold, scene.GateThresholdDb);
            SetNumeric(_gateRelease, scene.GateReleaseMs);
            _compressorEnabled.Checked = scene.CompressorEnabled;
        _compressorCharacterCombo.SelectedIndex=Math.Clamp((int)scene.CompressorCharacter,0,3);
            SetNumeric(_compressorSustain, scene.CompressorSustain);
            SetNumeric(_compressorAttack, scene.CompressorAttackMs);
            SetNumeric(_compressorLevel, scene.CompressorLevel);
            _autoWahEnabled.Checked = scene.AutoWahEnabled;
            _autoWahModeCombo.SelectedIndex = Math.Clamp((int)scene.AutoWahMode, 0, 1);
            _autoWahCharacterCombo.SelectedIndex = Math.Clamp((int)scene.AutoWahCharacter, 0, 2);
            SetNumeric(_autoWahSensitivity, scene.AutoWahSensitivity);
            SetNumeric(_autoWahRange, scene.AutoWahRange);
            SetNumeric(_autoWahResonance, scene.AutoWahResonance);
            SetNumeric(_autoWahManualPosition, scene.AutoWahManualPositionPercent);
            UpdateAutoWahModeUi();

            _overdriveEnabled.Checked = scene.OverdriveEnabled;
        _distortionCharacterCombo.SelectedIndex=Math.Clamp((int)scene.DistortionCharacter,0,3);
            SetNumeric(_overdriveGain, scene.OverdriveGain);
            SetNumeric(_overdriveTone, scene.OverdriveTone);
            SetNumeric(_overdriveLevel, scene.OverdriveLevel);
            _ts9Enabled.Checked = scene.Ts9Enabled;
        _driveCharacterCombo.SelectedIndex = Math.Clamp((int)scene.DriveCharacter, 0, 4);
            SetNumeric(_ts9Gain, scene.Ts9Gain);
            SetNumeric(_ts9Tone, scene.Ts9Tone);
            SetNumeric(_ts9Level, scene.Ts9Level);
            ApplyNewPedalControls(scene);
            _boosterEnabled.Checked = scene.BoosterEnabled;
        _boosterCharacterCombo.SelectedIndex = Math.Clamp((int)scene.BoosterCharacter, 0, 2);
            SetNumeric(_boosterDb, scene.BoosterDb);

            ApplySceneImpulseResponse(scene, out missingExternalIr);

            _fxLoopEnabled.Checked = scene.FxLoopEnabled;
            SetNumeric(_fxLoopSend, scene.FxLoopSendPercent);
            SetNumeric(_fxLoopReturn, scene.FxLoopReturnPercent);
            _phaserEnabled.Checked = scene.PhaserEnabled;
            SetNumeric(_phaserRate, scene.PhaserRateHz);
            SetNumeric(_phaserDepth, scene.PhaserDepthPercent);
            SetNumeric(_phaserFeedback, scene.PhaserFeedbackPercent);
            SetNumeric(_phaserMix, scene.PhaserMixPercent);
            _flangerEnabled.Checked = scene.FlangerEnabled;
        _flangerCharacterCombo.SelectedIndex = Math.Clamp((int)scene.FlangerCharacter, 0, 2);
            SetNumeric(_flangerRate, scene.FlangerRateHz);
            SetNumeric(_flangerDepth, scene.FlangerDepthPercent);
            SetNumeric(_flangerFeedback, scene.FlangerFeedbackPercent);
            SetNumeric(_flangerMix, scene.FlangerMixPercent);
            _chorusEnabled.Checked = scene.ChorusEnabled;
        _chorusPlacementCombo.SelectedIndex = Math.Clamp((int)scene.ChorusPlacement, 0, 1);
        _chorusCharacterCombo.SelectedIndex = Math.Clamp((int)scene.ChorusCharacter, 0, 1);
            SetNumeric(_chorusRate, scene.ChorusRateHz);
            SetNumeric(_chorusDepth, scene.ChorusDepthMs);
            SetNumeric(_chorusMix, scene.ChorusMixPercent);
            _analogChorusEnabled.Checked = scene.AnalogChorusEnabled;
            _analogChorusPlacementCombo.SelectedIndex = Math.Clamp((int)scene.AnalogChorusPlacement, 0, 1);
            SetNumeric(_analogChorusRate, scene.AnalogChorusRateHz);
            SetNumeric(_analogChorusDepth, scene.AnalogChorusDepth);
            SetNumeric(_analogChorusMix, scene.AnalogChorusMixPercent);
            SetNumeric(_analogChorusLow, scene.AnalogChorusLow);
            SetNumeric(_analogChorusHigh, scene.AnalogChorusHigh);
            _microPitchEnabled.Checked = scene.MicroPitchEnabled;
            SetNumeric(_microPitchDetune, scene.MicroPitchDetuneCents);
            SetNumeric(_microPitchDelay, scene.MicroPitchDelayMs);
            SetNumeric(_microPitchMix, scene.MicroPitchMixPercent);
            _rotaryEnabled.Checked = scene.RotaryEnabled;
        _rotarySync.Checked = scene.RotarySyncEnabled;
        _rotaryDivision.SelectedIndex = Math.Clamp((int)scene.RotaryDivision, 0, 4);
        _rotaryFast.Checked = scene.RotaryFast;
        SetNumeric(_rotaryDepth, scene.RotaryDepthPercent); SetNumeric(_rotaryMix, scene.RotaryMixPercent);
        _tremoloEnabled.Checked = scene.TremoloEnabled;
        _tremoloSync.Checked = scene.TremoloSyncEnabled;
        _tremoloDivision.SelectedIndex = Math.Clamp((int)scene.TremoloDivision, 0, 4);
        SetNumeric(_tremoloRate, scene.TremoloRateHz);
        SetNumeric(_tremoloDepth, scene.TremoloDepthPercent);
        _delayEnabled.Checked = scene.DelayEnabled;
        _delaySync.Checked = scene.DelaySyncEnabled;
        _delayDivision.SelectedIndex = Math.Clamp((int)scene.DelayDivision, 0, 4);
            _delayCharacterCombo.SelectedIndex = Math.Clamp((int)scene.DelayCharacter, 0, 3);
            SetNumeric(_delayTime, scene.DelayTimeMs);
            SetNumeric(_delayFeedback, scene.DelayFeedbackPercent);
            SetNumeric(_delayMix, scene.DelayMixPercent);
            _reverbEnabled.Checked = scene.ReverbEnabled;
        _reverbCharacterCombo.SelectedIndex = Math.Clamp((int)scene.ReverbCharacter, 0, 6);
            SetNumeric(_reverbMix, scene.ReverbMixPercent);
            SetNumeric(_reverbDecay, scene.ReverbDecayPercent);
            SetNumeric(_reverbTone, scene.ReverbTonePercent);
        SetNumeric(_reverbPreDelay, scene.ReverbPreDelayMs);
        SetNumeric(_reverbDamping, scene.ReverbDampingPercent);
        SetNumeric(_reverbDiffusion, scene.ReverbDiffusionPercent);

            if (scene.AccompanimentStored)
            {
                _metronomeEnabled.Checked = scene.SceneMetronomeEnabled;
                SetNumeric(_metronomeBpm, scene.SceneMetronomeBpm);
                _metronomeMeter.SelectedIndex = scene.SceneMetronomeBeatsPerBar switch
                {
                    2 => 0,
                    3 => 1,
                    6 => 3,
                    _ => 2
                };
                _metronomeAccent.Checked = scene.SceneMetronomeAccentFirstBeat;
                SetNumeric(_metronomeVolume, scene.SceneMetronomeVolumePercent);
                _drumsEnabled.Checked = scene.SceneDrumsEnabled;
                _drumPattern.SelectedIndex = Math.Clamp(scene.SceneDrumPattern, 0, 5);
                SetNumeric(_drumVolume, scene.SceneDrumVolumePercent);
                _backingBassEnabled.Checked = scene.SceneBackingBassEnabled;
                _backingBassKey.SelectedIndex = Math.Clamp(scene.SceneBackingBassKey, 0, 11);
                _backingBassMode.SelectedIndex = scene.SceneBackingBassMinor ? 1 : 0;
                _backingBassLine.SelectedIndex = Math.Clamp(scene.SceneBackingBassLine, 0, 4);
                SetNumeric(_backingBassVolume, scene.SceneBackingBassVolumePercent);
                _pianoEnabled.Checked = scene.ScenePianoEnabled;
                _pianoSound.SelectedIndex = Math.Clamp(scene.ScenePianoSound, 0, 5);
                _pianoKey.SelectedIndex = Math.Clamp(scene.ScenePianoKey, 0, 23);
                _pianoProgression.SelectedIndex = Math.Clamp(scene.ScenePianoProgression, 0, 5);
                _pianoCustomProgression.Text = string.IsNullOrWhiteSpace(scene.ScenePianoCustomProgression) ? "I, V, vi, IV" : scene.ScenePianoCustomProgression;
                _pianoStyle.SelectedIndex = Math.Clamp(scene.ScenePianoStyle, 0, 8);
                SetNumeric(_pianoVolume, scene.ScenePianoVolumePercent);
                _engine.RequestMetronomeReset();
            }

            _rememberedChannelIndex = _channelCombo.SelectedIndex;
        }
        finally
        {
            _loadingScene = false;
        }

        _activeSceneIndex = index;
        _sceneLibrary.LastSelectedIndex = index;
        TrySaveSceneLibrary(showError: false);
        _engine.Processor.RequestDelayReset();
        UpdateParameters();

        if (announce)
        {
            string irNotice = missingExternalIr
                ? " El IR externo no fue encontrado y se usó el gabinete interno V30."
                : string.Empty;
            SetStatus($"Cargada escena {index + 1}, {scene.Name}.{irNotice}", missingExternalIr);
        }
    }

    private void ApplySceneImpulseResponse(ScenePreset scene, out bool missingExternalIr)
    {
        missingExternalIr = false;

        if (!scene.ExternalIrEnabled || string.IsNullOrWhiteSpace(scene.ExternalIrPath))
        {
            _engine.Processor.ClearImpulseResponse();
            _loadedIrPath = null;
            _externalIrEnabled.Checked = false;
            _externalIrEnabled.Enabled = false;
            _clearIrButton.Enabled = false;
            _irPath.Text = "IR A: interno estilo V30.";
        }
        else if (!File.Exists(scene.ExternalIrPath))
        {
            _engine.Processor.ClearImpulseResponse();
            _loadedIrPath = null;
            _externalIrEnabled.Checked = false;
            _externalIrEnabled.Enabled = false;
            _clearIrButton.Enabled = false;
            _irPath.Text = $"IR A no encontrado: {scene.ExternalIrPath}. Se usa el V30 interno.";
            missingExternalIr = true;
        }
        else
        {
            try
            {
                if (!string.Equals(_loadedIrPath, scene.ExternalIrPath, StringComparison.OrdinalIgnoreCase) || !_engine.Processor.HasExternalImpulse)
                    _engine.Processor.LoadImpulseResponse(scene.ExternalIrPath);
                _loadedIrPath = scene.ExternalIrPath;
                _externalIrEnabled.Enabled = true;
                _externalIrEnabled.Checked = true;
                _clearIrButton.Enabled = true;
                _irPath.Text = scene.ExternalIrPath;
                SyncIrBrowserToLoadedPath(scene.ExternalIrPath);
            }
            catch
            {
                _engine.Processor.ClearImpulseResponse();
                _loadedIrPath = null;
                _externalIrEnabled.Checked = false;
                _externalIrEnabled.Enabled = false;
                _clearIrButton.Enabled = false;
                _irPath.Text = "El IR A guardado no pudo cargarse. Se usa el V30 interno.";
                missingExternalIr = true;
            }
        }

        if (!scene.ExternalIrBEnabled || string.IsNullOrWhiteSpace(scene.ExternalIrBPath))
        {
            _engine.Processor.ClearImpulseResponseB();
            _loadedIrPathB = null;
            _externalIrBEnabled.Checked = false;
            _externalIrBEnabled.Enabled = false;
            _clearIrBButton.Enabled = false;
            _irPathB.Text = "Ningún IR B cargado.";
        }
        else if (!File.Exists(scene.ExternalIrBPath))
        {
            _engine.Processor.ClearImpulseResponseB();
            _loadedIrPathB = null;
            _externalIrBEnabled.Checked = false;
            _externalIrBEnabled.Enabled = false;
            _clearIrBButton.Enabled = false;
            _irPathB.Text = $"IR B no encontrado: {scene.ExternalIrBPath}.";
            missingExternalIr = true;
        }
        else
        {
            try
            {
                if (!string.Equals(_loadedIrPathB, scene.ExternalIrBPath, StringComparison.OrdinalIgnoreCase) || !_engine.Processor.HasExternalImpulseB)
                    _engine.Processor.LoadImpulseResponseB(scene.ExternalIrBPath);
                _loadedIrPathB = scene.ExternalIrBPath;
                _externalIrBEnabled.Enabled = true;
                _externalIrBEnabled.Checked = true;
                _clearIrBButton.Enabled = true;
                _irPathB.Text = scene.ExternalIrBPath;
            }
            catch
            {
                _engine.Processor.ClearImpulseResponseB();
                _loadedIrPathB = null;
                _externalIrBEnabled.Checked = false;
                _externalIrBEnabled.Enabled = false;
                _clearIrBButton.Enabled = false;
                _irPathB.Text = "El IR B guardado no pudo cargarse.";
                missingExternalIr = true;
            }
        }

        SetNumeric(_irMix, scene.IrMixPercent);
        _irBPhaseInvert.Checked = scene.IrBPhaseInvert;
        SetNumeric(_irLowCut, scene.IrLowCutHz);
        SetNumeric(_irHighCut, scene.IrHighCutHz);
    }

    private void LoadNextScene()
    {
        int current = _activeSceneIndex >= 0 ? _activeSceneIndex : _sceneCombo.SelectedIndex;
        int next = (Math.Max(0, current) + 1) % 3;
        RecallScene(next, announce: true);
    }

    private bool TrySaveSceneLibrary(bool showError = true)
    {
        try
        {
            SceneStore.Save(_sceneLibrary);
            return true;
        }
        catch (Exception exception)
        {
            if (showError)
            {
                SetStatus($"No se pudieron guardar las escenas: {exception.Message}", true);
            }
            return false;
        }
    }

    private static string NormalizeSceneName(string? name)
    {
        string normalized = string.IsNullOrWhiteSpace(name) ? "Escena" : name.Trim();
        return normalized.Length <= 40 ? normalized : normalized[..40];
    }

    private static void SetNumeric(NumericUpDown control, float value)
    {
        decimal decimalValue = decimal.Round((decimal)value, control.DecimalPlaces);
        control.Value = Math.Clamp(decimalValue, control.Minimum, control.Maximum);
    }

    private void LoadDrivers(bool announce = true)
    {
        string? previous = _driverCombo.SelectedItem?.ToString();
        string preferred = !string.IsNullOrWhiteSpace(previous)
            ? previous
            : _audioPreferences.AsioDriverName ?? string.Empty;
        string[] drivers = AudioEngine.GetDriverNames();

        _loadingAudioDeviceSelection = true;
        try
        {
            _driverCombo.BeginUpdate();
            _driverCombo.Items.Clear();
            _driverCombo.Items.AddRange(drivers.Cast<object>().ToArray());
            _driverCombo.EndUpdate();

            if (drivers.Length == 0)
            {
                _inputCombo.Items.Clear();
                _driverCombo.AccessibleDescription = "No se encontró ningún controlador ASIO instalado.";
                SetStatus("No se encontró ningún controlador ASIO. Instale el driver ASIO oficial de su interfaz de audio y pulse Actualizar controladores.", true);
                return;
            }

            int index = Array.FindIndex(drivers, name => string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase));
            _driverCombo.SelectedIndex = index >= 0 ? index : 0;
        }
        finally
        {
            _loadingAudioDeviceSelection = false;
        }

        LoadInputs(announce);
    }

    private void LoadInputs(bool announce = true)
    {
        _inputCombo.Items.Clear();
        string? driver = _driverCombo.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(driver))
        {
            return;
        }

        try
        {
            AsioDeviceInfo info = AudioEngine.GetDeviceInfo(driver);
            _loadingAudioDeviceSelection = true;
            try
            {
                foreach (string input in info.InputChannels)
                {
                    _inputCombo.Items.Add(input);
                }

                if (_inputCombo.Items.Count > 0)
                {
                    int preferredInput = Math.Clamp(_audioPreferences.GuitarInputIndex, 0, _inputCombo.Items.Count - 1);
                    _inputCombo.SelectedIndex = preferredInput;
                }
            }
            finally
            {
                _loadingAudioDeviceSelection = false;
            }

            string summary = BuildAsioDeviceSummary(info);
            _driverCombo.AccessibleDescription = summary;
            _inputCombo.AccessibleDescription = info.InputCount > 0
                ? $"{info.InputCount} entradas disponibles en {driver}. Seleccione la entrada física de la guitarra."
                : $"{driver} no informa entradas de audio.";

            _audioPreferences.AsioDriverName = driver;
            if (_inputCombo.SelectedIndex >= 0) _audioPreferences.GuitarInputIndex = _inputCombo.SelectedIndex;
            SaveAudioDevicePreference();

            if (announce) SetStatus(summary);
        }
        catch (Exception exception)
        {
            _driverCombo.AccessibleDescription = $"Controlador ASIO {driver}. No se pudieron consultar sus canales.";
            SetStatus($"No se pudo consultar el controlador ASIO {driver}: {exception.Message}", true);
        }
    }

    private static string BuildAsioDeviceSummary(AsioDeviceInfo info)
    {
        string compatibility = info.Supports48Khz ? "48 kHz compatible" : "48 kHz no informado o no compatible";
        string outputNotice = info.OutputCount >= 2 ? string.Empty : ", atención: Amp Accessible necesita salida estéreo";
        return $"Se detectó {info.DriverName}: {info.InputCount} entradas, {info.OutputCount} salidas, {compatibility}{outputNotice}.";
    }

    private void AnnounceSelectedAsioDevice()
    {
        string? driver = _driverCombo.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(driver))
        {
            SetStatus("No hay controlador ASIO seleccionado. Instale el driver ASIO oficial de su interfaz de audio.", true);
            return;
        }

        try
        {
            AsioDeviceInfo info = AudioEngine.GetDeviceInfo(driver);
            string summary = BuildAsioDeviceSummary(info);
            _driverCombo.AccessibleDescription = summary;
            SetStatus(summary);
        }
        catch (Exception exception)
        {
            SetStatus($"Controlador ASIO seleccionado: {driver}. No se pudieron consultar sus canales: {exception.Message}", true);
        }
    }

    private void SaveAudioDevicePreference()
    {
        string? driver = _driverCombo.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(driver)) _audioPreferences.AsioDriverName = driver;
        if (_inputCombo.SelectedIndex >= 0) _audioPreferences.GuitarInputIndex = _inputCombo.SelectedIndex;
        try { AudioSettingsStore.Save(_audioPreferences); }
        catch { }
    }

    private void ToggleAudio()
    {
        if (_audioRequested || _engine.HasActiveSession)
        {
            CaptureAudioTelemetry();
            string? diagnosticPath;
            ConsumeAudioFaultAndSave(out diagnosticPath);

            if (diagnosticPath is null)
            {
                diagnosticPath = _audioDiagnostics.LastSavedPath;
            }

            if (diagnosticPath is null)
            {
                try
                {
                    diagnosticPath = _audioDiagnostics.Save(_stallReported ? "Detención manual después de cuelgue" : "Detención manual");
                }
                catch { }
            }

            string? recordingPath = SavePracticeRecordingBeforeAudioStop();
            string loopNotice = string.Empty;
            if (_engine.IsLoopRecording)
            {
                bool loopCreated = _engine.FinishLoopRecordingAndPlay();
                loopNotice = loopCreated
                    ? $" Primera vuelta del loop cerrada en {_engine.LoopSeconds:0.0} segundos."
                    : " La primera vuelta del loop era demasiado corta y fue descartada.";
            }
            if (_engine.IsLoopOverdubbing)
            {
                _engine.ToggleLoopOverdub();
                loopNotice += " Overdub detenido.";
            }
            _audioRequested = false;
            _stallReported = false;
            _engine.Stop();
            SetRunningState(false);
            UpdateActualBufferLabel();
            string recordingNotice = recordingPath is null ? string.Empty : $" Grabación guardada en {recordingPath}.";
            SetStatus((diagnosticPath is null ? "Audio detenido." : $"Audio detenido. Diagnóstico disponible en {diagnosticPath}.") + recordingNotice + loopNotice);
            return;
        }

        string? driver = _driverCombo.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(driver))
        {
            SetStatus("Seleccione un controlador ASIO.", true);
            _driverCombo.Focus();
            return;
        }

        if (_inputCombo.SelectedIndex < 0)
        {
            SetStatus(_twoGuitarMode.Checked
                ? "Modo Dos Guitarras necesita Input 1 e Input 2 disponibles en el controlador ASIO."
                : _voiceOnlyMode.Checked
                    ? "Seleccione una entrada del controlador. En Modo Voz se utilizará el micrófono de la entrada 1."
                    : "Seleccione la entrada donde está conectada la guitarra.", true);
            _inputCombo.Focus();
            return;
        }

        if (_twoGuitarMode.Checked && _inputCombo.Items.Count < 2)
        {
            SetStatus("Modo Dos Guitarras necesita una interfaz con al menos dos entradas ASIO.", true);
            return;
        }

        try
        {
            UpdateParameters();
            string message = _engine.Start(driver, _twoGuitarMode.Checked ? 1 : _inputCombo.SelectedIndex, SelectedBufferSize);
            if (_meetOutputEnabled.Checked)
            {
                string? videoId = _meetOutputCombo.SelectedIndex >= 0 && _meetOutputCombo.SelectedIndex < _meetOutputDevices.Count
                    ? _meetOutputDevices[_meetOutputCombo.SelectedIndex].Id : null;
                if (!string.IsNullOrWhiteSpace(videoId))
                {
                    message += " " + _engine.ConfigureMeetOutput(videoId, true);
                    if (SelectedMeetOutputIsVoicemeeter())
                    {
                        VoicemeeterSetupResult setup = PrepareVoicemeeterRouting(announce: false);
                        message += " " + setup.Message;
                    }
                }
                else
                    message += " La salida para videollamadas está activada, pero no hay un dispositivo virtual seleccionado.";
            }
            if (_twoGuitarMode.Checked)
            {
                message += " Dos DSP activos: Guitarra 1 por Input 1 y Guitarra 2 por Input 2. Voz desactivada en este perfil.";
            }
            else if (_voiceOnlyMode.Checked)
            {
                message += " Modo Voz activo: sólo se procesa el micrófono de la entrada 1; la ruta de guitarra y el acompañamiento están silenciados.";
            }
            else if (_voiceEnabled.Checked && _inputCombo.SelectedIndex == 0)
            {
                message += " El procesamiento de voz no se mezcla porque la guitarra está seleccionada en la entrada 1. Para micrófono 1 y guitarra 2, seleccione entrada 2 como guitarra.";
            }
            _audioDiagnostics.BeginSession(driver, _twoGuitarMode.Checked ? 1 : _inputCombo.SelectedIndex, SelectedBufferSize, _engine.ActualBufferSize);
            _audioRequested = true;
            _stallReported = false;
            SetRunningState(true);
            UpdateActualBufferLabel();
            SetStatus(message);
        }
        catch (Exception exception)
        {
            _audioRequested = false;
            _engine.Stop();
            SetRunningState(false);
            UpdateActualBufferLabel();
            SetStatus($"No se pudo iniciar el audio: {exception.Message}", true);
        }
    }

    private void SetRunningState(bool running)
    {
        _driverCombo.Enabled = !running;
        _inputCombo.Enabled = !running;
        _bufferCombo.Enabled = !running;
        _refreshDriversButton.Enabled = !running;
        _asioPanelButton.Enabled = !running;
        _applyBufferButton.Enabled = !running;
        _twoGuitarMode.Enabled = !running;
        _startStopButton.Text = running ? "&Detener audio" : "&Iniciar audio";
        _startStopButton.AccessibleName = running ? "Detener audio" : "Iniciar audio";
    }

    private int SelectedBufferSize => _bufferCombo.SelectedIndex switch
    {
        0 => 64,
        1 => 128,
        2 => 256,
        _ => 512
    };

    private void LoadMasterVolumePreference()
    {
        SetNumeric(_masterVolume, _audioPreferences.MasterVolumePercent);
        _engine.ConfigureMasterVolume((float)_masterVolume.Value);
    }

    private void LoadChannelEqPreferences()
    {
        StoreEqForChannel(0, _audioPreferences.CleanBass, _audioPreferences.CleanMiddle,
            _audioPreferences.CleanTreble, _audioPreferences.CleanPresence);
        StoreEqForChannel(1, _audioPreferences.CrunchBass, _audioPreferences.CrunchMiddle,
            _audioPreferences.CrunchTreble, _audioPreferences.CrunchPresence);
        StoreEqForChannel(2, _audioPreferences.LeadBass, _audioPreferences.LeadMiddle,
            _audioPreferences.LeadTreble, _audioPreferences.LeadPresence);
        StoreEqForChannel(3, 5.5f, 4.5f, 5.5f, 4.5f); StoreEqForChannel(4, 4.5f, 5f, 6.2f, 5.5f);
        StoreEqForChannel(5, 5f, 6.2f, 5.4f, 5.2f); StoreEqForChannel(6, 4.8f, 6f, 6f, 5.8f);
        StoreEqForChannel(7, 4.5f, 5.5f, 5.2f, 5.5f); StoreEqForChannel(8, 5f, 6.5f, 5f, 5.2f);
        _rememberedChannelIndex = Math.Clamp(_channelCombo.SelectedIndex, 0, 8);
    }

    private void StoreEqForChannel(int channelIndex, float bass, float middle, float treble, float presence)
    {
        channelIndex = Math.Clamp(channelIndex, 0, 8);
        _channelEq[channelIndex, 0] = Math.Clamp(bass, 0f, 10f);
        _channelEq[channelIndex, 1] = Math.Clamp(middle, 0f, 10f);
        _channelEq[channelIndex, 2] = Math.Clamp(treble, 0f, 10f);
        _channelEq[channelIndex, 3] = Math.Clamp(presence, 0f, 10f);
    }

    private void RememberCurrentChannelEq(bool force = false)
    {
        if (!force && (_loadingScene || _loadingChannelEq))
        {
            return;
        }

        int channelIndex = Math.Clamp(_channelCombo.SelectedIndex, 0, 8);
        StoreEqForChannel(channelIndex,
            (float)_bass.Value, (float)_middle.Value, (float)_treble.Value, (float)_presence.Value);
    }

    private void HandleChannelChanged()
    {
        int newChannelIndex = _channelCombo.SelectedIndex;
        if (newChannelIndex < 0)
        {
            return;
        }

        if (_loadingScene || _loadingChannelEq)
        {
            _rememberedChannelIndex = Math.Clamp(newChannelIndex, 0, 8);
            return;
        }

        // Antes de salir del canal actual se conserva su ecualización.
        int previousChannel = Math.Clamp(_rememberedChannelIndex, 0, 8);
        StoreEqForChannel(previousChannel,
            (float)_bass.Value, (float)_middle.Value, (float)_treble.Value, (float)_presence.Value);

        _loadingChannelEq = true;
        try
        {
            SetNumeric(_bass, _channelEq[newChannelIndex, 0]);
            SetNumeric(_middle, _channelEq[newChannelIndex, 1]);
            SetNumeric(_treble, _channelEq[newChannelIndex, 2]);
            SetNumeric(_presence, _channelEq[newChannelIndex, 3]);
        }
        finally
        {
            _loadingChannelEq = false;
        }

        _rememberedChannelIndex = newChannelIndex;
        ScheduleParameterUpdate();

        string channelName = _channelCombo.SelectedItem?.ToString() ?? $"Canal {newChannelIndex + 1}";
        SetStatus($"{channelName}. Ecualización propia cargada: bajos {_bass.Value:0.0}, medios {_middle.Value:0.0}, agudos {_treble.Value:0.0}, presencia {_presence.Value:0.0}.");
    }

    private void LoadTunerPreference()
    {
        _tunerGuitarCombo.SelectedIndex = Math.Clamp(_audioPreferences.TunerGuitarIndex, 0, 1);
        _tunerGuitarCombo.Enabled = _twoGuitarMode.Checked;
        _tunerGuitarCombo.TabStop = _twoGuitarMode.Checked;
        UpdateTunerAccessibleContext();
    }

    private int SelectedTunerGuitarIndex => Math.Clamp(_tunerGuitarCombo.SelectedIndex, 0, 1);

    private string SelectedTunerGuitarName => SelectedTunerGuitarIndex == 0
        ? "Guitarra 1 / Input 1"
        : "Guitarra 2 / Input 2";

    private void UpdateTunerAccessibleContext()
    {
        bool dual = _twoGuitarMode.Checked;
        _tunerGuitarCombo.Enabled = dual;
        _tunerGuitarCombo.TabStop = dual;
        string target = dual ? SelectedTunerGuitarName : "rig principal / entrada de guitarra seleccionada";
        _tunerStatus.AccessibleDescription = dual
            ? $"Afinador preparado para {target}. F9 lee la afinación y Control F9 cambia de guitarra."
            : "Afinador del rig principal. F9 lee la afinación actual.";
        _readTunerButton.AccessibleDescription = dual
            ? $"Lee con JAWS únicamente {target}. Control F9 alterna la guitarra a afinar."
            : "Lleva el foco a la lectura actual para que JAWS la anuncie.";
    }

    private void HandleTunerGuitarChanged()
    {
        if (_tunerGuitarCombo.SelectedIndex < 0) return;
        _lastTunerReading = TunerReading.NoSignal;
        _engine.Processor.SetTunerGuideDirection(TuningDirection.NoSignal);
        _engine.Guitar1Processor.SetTunerGuideDirection(TuningDirection.NoSignal);
        UpdateTunerAccessibleContext();
        UpdateParameters();
        if (!_loadingScene) SaveAudioPreferences();
        if (_twoGuitarMode.Checked)
            SetStatus($"Afinador preparado para {SelectedTunerGuitarName}. Pulse F9 para leer la afinación o Control F9 para cambiar de guitarra.");
    }

    private void ToggleTunerGuitarTarget()
    {
        if (!_twoGuitarMode.Checked)
        {
            SetStatus("Control F9 cambia de guitarra solamente cuando Modo Dos Guitarras está activo. En modo normal el afinador usa el rig principal.");
            return;
        }
        _tunerGuitarCombo.SelectedIndex = SelectedTunerGuitarIndex == 0 ? 1 : 0;
    }

    private void LoadDualGuitarPreferences()
    {
        _guitar1ProcessingEnabled.Checked = _audioPreferences.Guitar1ProcessingEnabled;
        _guitar1UseRigEffects.Checked = _audioPreferences.Guitar1UseRigEffects;
        _guitar1AmpCombo.SelectedIndex = Math.Clamp(_audioPreferences.Guitar1AmpChannel, 0, Math.Max(0, _guitar1AmpCombo.Items.Count - 1));
        SetNumeric(_guitar1Gain, _audioPreferences.Guitar1Gain);
        SetNumeric(_guitar1Output, _audioPreferences.Guitar1OutputPercent);
        SetNumeric(_guitar1Mix, _audioPreferences.Guitar1MixPercent);
        SetNumeric(_guitar1Pan, _audioPreferences.Guitar1PanPercent);
        _guitar1Mute.Checked = _audioPreferences.Guitar1Muted;
        SetNumeric(_guitar2Mix, _audioPreferences.Guitar2MixPercent);
        SetNumeric(_guitar2Pan, _audioPreferences.Guitar2PanPercent);
        _guitar2Mute.Checked = _audioPreferences.Guitar2Muted;
        UpdatePanAccessibleNames();
        LoadGuitar1NamPreferences();
        LoadGuitar1IrPreferences();
        _twoGuitarMode.Checked = _audioPreferences.TwoGuitarMode;
        if (_twoGuitarMode.Checked)
        {
            _voiceOnlyMode.Checked = false;
            _voiceEnabled.Checked = false;
        }
        UpdateDualGuitarStatus();
        RefreshDualQuickNamSelector();
        AnnounceDualGuitarSelection();
    }

    private void InitializeDualEffectMemories()
    {
        ScenePreset current = CaptureCurrentScene("Efectos actuales");
        _guitar1EffectMemory = _audioPreferences.Guitar1EffectMemory ?? (current with { Name = "Efectos Guitarra 1" });
        _guitar2EffectMemory = _audioPreferences.Guitar2EffectMemory ?? (current with { Name = "Efectos Guitarra 2" });
        _dualLastEditedGuitar = Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1);
        _dualEffectMemoriesInitialized = true;

        if (_twoGuitarMode.Checked)
        {
            LoadDualEffectMemoryIntoControls(_dualLastEditedGuitar == 0 ? _guitar1EffectMemory : _guitar2EffectMemory);
            UpdateParameters();
        }
    }

    private void StoreVisibleEffectsForGuitar(int guitarIndex)
    {
        if (!_dualEffectMemoriesInitialized || _loadingDualEffectMemory || _loadingScene)
            return;

        ScenePreset captured = CaptureCurrentScene(guitarIndex == 0 ? "Efectos Guitarra 1" : "Efectos Guitarra 2");
        if (guitarIndex == 0) _guitar1EffectMemory = captured;
        else _guitar2EffectMemory = captured;
    }

    private void HandleDualEditGuitarChanged()
    {
        int selected = Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1);
        if (_loadingDualEffectMemory)
            return;

        if (_twoGuitarMode.Checked && _dualEffectMemoriesInitialized)
        {
            StoreVisibleEffectsForGuitar(_dualLastEditedGuitar);
            _dualLastEditedGuitar = selected;
            ScenePreset memory = selected == 0
                ? (_guitar1EffectMemory ?? CaptureCurrentScene("Efectos Guitarra 1"))
                : (_guitar2EffectMemory ?? CaptureCurrentScene("Efectos Guitarra 2"));
            LoadDualEffectMemoryIntoControls(memory);
            SaveAudioPreferences();
            UpdateParameters();
        }
        else
        {
            _dualLastEditedGuitar = selected;
        }

        RefreshDualGuitarBankList();
        RefreshDualQuickNamSelector();
        AnnounceDualGuitarSelection();
    }

    private void RefreshDualQuickNamSelector(string? selectId = null)
    {
        if (_updatingDualQuickNam) return;
        _updatingDualQuickNam = true;
        try
        {
            int guitarIndex = Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1);
            string? wanted = selectId ?? (guitarIndex == 0 ? _guitar1ActiveNamBankId : _activeNamBankId);
            _dualQuickNamCombo.Items.Clear();
            foreach (NamLibraryItem item in _namLibrary.Items)
                _dualQuickNamCombo.Items.Add(item);

            int index = -1;
            if (!string.IsNullOrWhiteSpace(wanted))
            {
                for (int i = 0; i < _dualQuickNamCombo.Items.Count; i++)
                {
                    if (_dualQuickNamCombo.Items[i] is NamLibraryItem candidate &&
                        string.Equals(candidate.Id, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }
            if (index < 0 && _dualQuickNamCombo.Items.Count > 0) index = 0;
            _dualQuickNamCombo.SelectedIndex = index;

            bool hasItems = _dualQuickNamCombo.Items.Count > 0;
            _dualQuickNamLoadButton.Enabled = hasItems;
            _dualQuickNamPreviousButton.Enabled = hasItems;
            _dualQuickNamNextButton.Enabled = hasItems;

            bool hasModel = guitarIndex == 0 ? _engine.Guitar1Processor.HasNamModel : _engine.Processor.HasNamModel;
            bool enabled = guitarIndex == 0 ? _guitar1NamEnabled.Checked : _namEnabled.Checked;
            _dualQuickNamEnabled.Enabled = hasModel;
            _dualQuickNamEnabled.Checked = hasModel && enabled;
            string? path = guitarIndex == 0 ? _engine.Guitar1Processor.NamModelPath : _engine.Processor.NamModelPath;
            string guitarName = guitarIndex == 0 ? "Guitarra 1" : "Guitarra 2";
            _dualQuickNamCurrent.Text = hasModel && !string.IsNullOrWhiteSpace(path)
                ? $"{guitarName}: {Path.GetFileName(path)}; procesamiento {(enabled ? "ACTIVO" : "inactivo")}."
                : $"{guitarName}: sin modelo NAM cargado.";
        }
        finally
        {
            _updatingDualQuickNam = false;
        }
    }

    private void SyncDualQuickNamState()
    {
        if (_updatingDualQuickNam) return;
        RefreshDualQuickNamSelector();
    }

    private void ApplyDualQuickNamEnabled()
    {
        if (_updatingDualQuickNam) return;
        int guitarIndex = Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1);
        bool wanted = _dualQuickNamEnabled.Checked;
        if (guitarIndex == 0)
        {
            if (wanted && !_engine.Guitar1Processor.HasNamModel)
            {
                SetStatus("Guitarra 1 no tiene un NAM cargado. Elija una captura en NAM rápido y pulse Cargar NAM.", true);
                RefreshDualQuickNamSelector();
                return;
            }
            _guitar1NamEnabled.Checked = wanted;
            SetStatus($"NAM de Guitarra 1 {(wanted ? "activado" : "desactivado")} desde Alt+O.");
        }
        else
        {
            if (wanted && !_engine.Processor.HasNamModel)
            {
                SetStatus("Guitarra 2 no tiene un NAM cargado. Elija una captura en NAM rápido y pulse Cargar NAM.", true);
                RefreshDualQuickNamSelector();
                return;
            }
            _namEnabled.Checked = wanted;
            SetStatus($"NAM de Guitarra 2 {(wanted ? "activado" : "desactivado")} desde Alt+O.");
        }
        RefreshDualQuickNamSelector();
    }

    private void LoadSelectedDualQuickNam()
    {
        if (_dualQuickNamCombo.SelectedItem is not NamLibraryItem item)
        {
            SetStatus("La biblioteca NAM está vacía. Importe una captura primero.", true);
            return;
        }

        int guitarIndex = Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1);
        if (guitarIndex == 0)
            LoadGuitar1NamBankItem(item, announce: true);
        else
            LoadNamBankItem(item, announce: true);
        RefreshDualQuickNamSelector(item.Id);
    }

    private void LoadAdjacentDualQuickNam(int direction)
    {
        if (_dualQuickNamCombo.Items.Count == 0)
        {
            SetStatus("La biblioteca NAM está vacía.", true);
            return;
        }
        int index = _dualQuickNamCombo.SelectedIndex;
        if (index < 0) index = 0;
        index = (index + (direction < 0 ? -1 : 1) + _dualQuickNamCombo.Items.Count) % _dualQuickNamCombo.Items.Count;
        _dualQuickNamCombo.SelectedIndex = index;
        LoadSelectedDualQuickNam();
    }

    private void DualQuickNamCombo_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        e.Handled = true;
        LoadSelectedDualQuickNam();
    }

    private void LoadSelectedDualFactoryBank()
    {
        if (!_twoGuitarMode.Checked)
        {
            SetStatus("Active Modo Dos Guitarras antes de cargar un banco de fábrica por guitarra.", true);
            return;
        }

        int index = _dualFactoryBankCombo.SelectedIndex;
        if (index < 0 || index >= FactoryPresetBank.Presets.Count)
        {
            SetStatus("No hay un banco de fábrica seleccionado.", true);
            return;
        }

        int guitarIndex = Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1);
        string guitarName = guitarIndex == 0 ? "Guitarra 1" : "Guitarra 2";
        UserPreset preset = FactoryPresetBank.Presets[index];
        if (guitarIndex == 0)
            ApplyFactoryPresetToGuitar1(preset);
        else
            ApplyFactoryPresetToGuitar2(preset);

        SaveAudioPreferences();
        UpdateDualGuitarStatus();
        UpdateParameters();
        string activeEffects = DescribeActiveFactoryEffects(preset.Sound);
        SetStatus($"{guitarName}, {preset.Name} cargado como banco de fábrica independiente. {activeEffects} La otra guitarra permanece intacta.");
    }

    private void ApplyFactoryPresetToGuitar1(UserPreset preset)
    {
        ScenePreset sound = preset.Sound with
        {
            Name = "Efectos Guitarra 1",
            PreEffectOrder = UserPresetLibrary.NormalizeOrder(preset.PreEffectOrder).ToArray(),
            ExternalIrEnabled = false,
            ExternalIrPath = null,
            ExternalIrBEnabled = false,
            ExternalIrBPath = null,
            AccompanimentStored = false
        };

        _guitar1AmpCombo.SelectedIndex = Math.Clamp((int)sound.Channel, 0, Math.Max(0, _guitar1AmpCombo.Items.Count - 1));
        SetNumeric(_guitar1Gain, sound.Gain);
        SetNumeric(_guitar1Output, sound.OutputPercent);
        _guitar1UseRigEffects.Checked = true;
        _guitar1EffectMemory = sound;
        LoadDualEffectMemoryIntoControls(sound);

        // Los bancos de fábrica utilizan gabinete interno. El navegador de carpeta
        // permanece preparado, pero el IR externo de Guitarra 1 se desactiva.
        _engine.Guitar1Processor.ClearImpulseResponse();
        _guitar1LoadedIrPath = null;
        _audioPreferences.Guitar1IrPath = string.Empty;
        _guitar1IrPath.Text = "Guitarra 1: gabinete interno.";
        _guitar1ClearIrButton.Enabled = false;
        // Los bancos de fábrica son sonidos de amplificadores internos. El NAM de
        // Guitarra 1 queda cargado como preparado, pero desactivado, para volver luego.
        _guitar1NamEnabled.Checked = false;
        _engine.Guitar1Processor.RequestDelayReset();
    }

    private void ApplyFactoryPresetToGuitar2(UserPreset preset)
    {
        ScenePreset sound = preset.Sound with
        {
            Name = "Efectos Guitarra 2",
            PreEffectOrder = UserPresetLibrary.NormalizeOrder(preset.PreEffectOrder).ToArray(),
            AccompanimentStored = false
        };

        // Guitarra 2 es el rig principal. Se aplica el mismo banco de fábrica que
        // en Alt+B, pero sin tocar Guitarra 1 y sin cargar acompañamientos.
        _preEffectOrder = UserPresetLibrary.NormalizeOrder(preset.PreEffectOrder);
        ApplyPresetSound(sound);
        _namEnabled.Checked = false;
        _namIncludesCabinet.Checked = false;
        _engine.Processor.ClearNamModel();
        _guitar2EffectMemory = CaptureCurrentScene("Efectos Guitarra 2") with
        {
            PreEffectOrder = _preEffectOrder.ToArray(),
            AccompanimentStored = false
        };
        UpdateDynamicEffectAccessibleNames();
    }

    private static string NormalizeDualBankName(string? value)
    {
        string name = string.IsNullOrWhiteSpace(value) ? "Banco" : value.Trim();
        return name.Length <= 60 ? name : name[..60];
    }

    private void RefreshDualGuitarBankList(int selectedIndex = int.MinValue)
    {
        int guitarIndex = Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1);
        _dualGuitarBankLibrary.Normalize();
        List<DualGuitarBankPreset> banks = _dualGuitarBankLibrary.BanksFor(guitarIndex);
        int desired = selectedIndex == int.MinValue
            ? _dualGuitarBankLibrary.LastIndexFor(guitarIndex)
            : selectedIndex;

        _loadingDualBankList = true;
        try
        {
            _dualBankCombo.BeginUpdate();
            _dualBankCombo.Items.Clear();
            foreach (DualGuitarBankPreset bank in banks) _dualBankCombo.Items.Add(bank.Name);
            _dualBankCombo.EndUpdate();

            if (banks.Count == 0)
            {
                _dualBankCombo.SelectedIndex = -1;
                _dualBankName.Text = string.Empty;
            }
            else
            {
                desired = Math.Clamp(desired < 0 ? 0 : desired, 0, banks.Count - 1);
                _dualBankCombo.SelectedIndex = desired;
                _dualBankName.Text = banks[desired].Name;
                _dualGuitarBankLibrary.SetLastIndexFor(guitarIndex, desired);
            }

            string guitarName = guitarIndex == 0 ? "Guitarra 1" : "Guitarra 2";
            _dualFactoryBankCombo.AccessibleName = $"Bancos de fábrica para {guitarName}";
            _dualFactoryBankCombo.AccessibleDescription = $"Treinta bancos de fábrica disponibles para {guitarName}. Cargar uno modifica sólo esa guitarra.";
            _dualFactoryBankLoadButton.AccessibleName = $"Cargar banco de fábrica en {guitarName}";
            _dualBankCombo.AccessibleName = $"Bancos personales independientes de {guitarName}";
            _dualBankCombo.AccessibleDescription = banks.Count == 0
                ? $"{guitarName} todavía no tiene bancos personales guardados. Puede usar un banco de fábrica de la lista anterior o escribir un nombre y pulsar Guardar banco."
                : $"{guitarName} tiene {banks.Count} bancos personales. Cargar o guardar aquí no modifica los bancos de la otra guitarra.";
            _dualBankSaveButton.AccessibleName = $"Guardar banco de {guitarName}";
            _dualBankLoadButton.AccessibleName = $"Cargar banco de {guitarName}";
            _dualBankDeleteButton.AccessibleName = $"Eliminar banco de {guitarName}";
            _dualBankLoadButton.Enabled = banks.Count > 0;
            _dualBankDeleteButton.Enabled = banks.Count > 0;
        }
        finally
        {
            _loadingDualBankList = false;
        }
    }

    private void HandleDualBankSelectionChanged()
    {
        if (_loadingDualBankList) return;
        int guitarIndex = Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1);
        List<DualGuitarBankPreset> banks = _dualGuitarBankLibrary.BanksFor(guitarIndex);
        int index = _dualBankCombo.SelectedIndex;
        if (index < 0 || index >= banks.Count) return;
        _dualBankName.Text = banks[index].Name;
        _dualGuitarBankLibrary.SetLastIndexFor(guitarIndex, index);
        try { DualGuitarBankStore.Save(_dualGuitarBankLibrary); } catch { }
    }

    private DualGuitarBankPreset CaptureDualGuitarBank(int guitarIndex, string name, bool captureVisibleEffects = true)
    {
        if (captureVisibleEffects) StoreVisibleEffectsForGuitar(guitarIndex);
        if (guitarIndex == 0)
        {
            ScenePreset effects = _guitar1EffectMemory ?? CaptureCurrentScene("Efectos Guitarra 1");
            ScenePreset sound = effects with
            {
                Name = name,
                Channel = (AmpChannel)Math.Clamp(_guitar1AmpCombo.SelectedIndex, 0, 8),
                Gain = (float)_guitar1Gain.Value,
                OutputPercent = (float)_guitar1Output.Value,
                ExternalIrEnabled = !string.IsNullOrWhiteSpace(_guitar1LoadedIrPath) && _engine.Guitar1Processor.HasExternalImpulse,
                ExternalIrPath = _guitar1LoadedIrPath,
                ExternalIrBEnabled = false,
                ExternalIrBPath = null,
                AccompanimentStored = false
            };
            return new DualGuitarBankPreset
            {
                Name = name,
                Sound = sound,
                MixPercent = (float)_guitar1Mix.Value,
                PanPercent = (float)_guitar1Pan.Value,
                Muted = _guitar1Mute.Checked,
                ProcessingEnabled = _guitar1ProcessingEnabled.Checked,
                EffectsEnabled = _guitar1UseRigEffects.Checked,
                NamEnabled = _guitar1NamEnabled.Checked && _engine.Guitar1Processor.HasNamModel,
                NamIncludesCabinet = _guitar1NamIncludesCabinet.Checked,
                NamPath = _engine.Guitar1Processor.NamModelPath,
                NamInputTrimDb = (float)_guitar1NamInputTrim.Value,
                NamOutputTrimDb = (float)_guitar1NamOutputTrim.Value,
                NamAutoLevelEnabled = _guitar1NamAutoLevel.Checked,
                NamAutoLevelDb = (float)_guitar1NamAutoLevelDb.Value
            };
        }

        ScenePreset liveRig = CaptureCurrentScene(name);
        ScenePreset guitar2Effects = _guitar2EffectMemory ?? liveRig;
        ScenePreset guitar2Sound = captureVisibleEffects
            ? liveRig with { AccompanimentStored = false }
            : guitar2Effects with
            {
                Name = name,
                Channel = liveRig.Channel,
                Gain = liveRig.Gain,
                Bass = liveRig.Bass,
                Middle = liveRig.Middle,
                Treble = liveRig.Treble,
                Presence = liveRig.Presence,
                CleanBass = liveRig.CleanBass,
                CleanMiddle = liveRig.CleanMiddle,
                CleanTreble = liveRig.CleanTreble,
                CleanPresence = liveRig.CleanPresence,
                CrunchBass = liveRig.CrunchBass,
                CrunchMiddle = liveRig.CrunchMiddle,
                CrunchTreble = liveRig.CrunchTreble,
                CrunchPresence = liveRig.CrunchPresence,
                LeadBass = liveRig.LeadBass,
                LeadMiddle = liveRig.LeadMiddle,
                LeadTreble = liveRig.LeadTreble,
                LeadPresence = liveRig.LeadPresence,
                OutputPercent = liveRig.OutputPercent,
                ExternalIrEnabled = liveRig.ExternalIrEnabled,
                ExternalIrPath = liveRig.ExternalIrPath,
                ExternalIrBEnabled = liveRig.ExternalIrBEnabled,
                ExternalIrBPath = liveRig.ExternalIrBPath,
                IrMixPercent = liveRig.IrMixPercent,
                IrBPhaseInvert = liveRig.IrBPhaseInvert,
                IrLowCutHz = liveRig.IrLowCutHz,
                IrHighCutHz = liveRig.IrHighCutHz,
                AccompanimentStored = false
            };
        return new DualGuitarBankPreset
        {
            Name = name,
            Sound = guitar2Sound,
            MixPercent = (float)_guitar2Mix.Value,
            PanPercent = (float)_guitar2Pan.Value,
            Muted = _guitar2Mute.Checked,
            ProcessingEnabled = true,
            EffectsEnabled = true,
            NamEnabled = _namEnabled.Checked && _engine.Processor.HasNamModel,
            NamIncludesCabinet = _namIncludesCabinet.Checked,
            NamPath = _engine.Processor.NamModelPath,
            NamInputTrimDb = (float)_namInputTrim.Value,
            NamOutputTrimDb = (float)_namOutputTrim.Value,
            NamAutoLevelEnabled = _namAutoLevel.Checked,
            NamAutoLevelDb = (float)_namAutoLevelDb.Value
        };
    }

    private void SaveDualGuitarBank()
    {
        if (!_twoGuitarMode.Checked)
        {
            SetStatus("Active Modo Dos Guitarras antes de guardar un banco independiente.", true);
            return;
        }

        int guitarIndex = Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1);
        string guitarName = guitarIndex == 0 ? "Guitarra 1" : "Guitarra 2";
        string name = NormalizeDualBankName(_dualBankName.Text);
        DualGuitarBankPreset bank = CaptureDualGuitarBank(guitarIndex, name);
        List<DualGuitarBankPreset> banks = _dualGuitarBankLibrary.BanksFor(guitarIndex);
        int existing = banks.FindIndex(item => string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase));
        int selected;
        if (existing >= 0)
        {
            banks[existing] = bank;
            selected = existing;
        }
        else
        {
            banks.Add(bank);
            selected = banks.Count - 1;
        }
        _dualGuitarBankLibrary.SetLastIndexFor(guitarIndex, selected);
        try
        {
            DualGuitarBankStore.Save(_dualGuitarBankLibrary);
            RefreshDualGuitarBankList(selected);
            SetStatus(existing >= 0
                ? $"{guitarName}, banco {name} actualizado. La otra guitarra no fue modificada."
                : $"{guitarName}, banco {name} guardado. Total de bancos de {guitarName}: {banks.Count}.");
        }
        catch (Exception ex) { SetStatus($"No se pudo guardar el banco de {guitarName}: {ex.Message}", true); }
    }

    private void LoadSelectedDualGuitarBank()
    {
        if (!_twoGuitarMode.Checked)
        {
            SetStatus("Active Modo Dos Guitarras antes de cargar un banco independiente.", true);
            return;
        }

        int guitarIndex = Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1);
        string guitarName = guitarIndex == 0 ? "Guitarra 1" : "Guitarra 2";
        List<DualGuitarBankPreset> banks = _dualGuitarBankLibrary.BanksFor(guitarIndex);
        int index = _dualBankCombo.SelectedIndex;
        if (index < 0 || index >= banks.Count)
        {
            SetStatus($"{guitarName} no tiene un banco seleccionado.", true);
            return;
        }

        DualGuitarBankPreset bank = banks[index];
        string notice = guitarIndex == 0 ? ApplyGuitar1Bank(bank) : ApplyGuitar2Bank(bank);
        _dualGuitarBankLibrary.SetLastIndexFor(guitarIndex, index);
        try { DualGuitarBankStore.Save(_dualGuitarBankLibrary); } catch { }
        SaveAudioPreferences();
        UpdateDualGuitarStatus();
        UpdateParameters();
        SetStatus($"{guitarName}, banco {bank.Name} cargado. La otra guitarra permanece intacta.{notice}", !string.IsNullOrWhiteSpace(notice));
    }

    private string ApplyGuitar1Bank(DualGuitarBankPreset bank, bool resumeAudioAfterNamLoad = true)
    {
        ScenePreset sound = bank.Sound ?? new ScenePreset();
        _guitar1AmpCombo.SelectedIndex = Math.Clamp((int)sound.Channel, 0, Math.Max(0, _guitar1AmpCombo.Items.Count - 1));
        SetNumeric(_guitar1Gain, sound.Gain);
        SetNumeric(_guitar1Output, sound.OutputPercent);
        SetNumeric(_guitar1Mix, bank.MixPercent);
        SetNumeric(_guitar1Pan, bank.PanPercent);
        _guitar1Mute.Checked = bank.Muted;
        _guitar1ProcessingEnabled.Checked = bank.ProcessingEnabled;
        _guitar1UseRigEffects.Checked = bank.EffectsEnabled;
        _guitar1EffectMemory = sound with { Name = "Efectos Guitarra 1" };
        LoadDualEffectMemoryIntoControls(_guitar1EffectMemory);

        string notice = string.Empty;
        if (sound.ExternalIrEnabled && !string.IsNullOrWhiteSpace(sound.ExternalIrPath))
        {
            if (File.Exists(sound.ExternalIrPath))
            {
                try
                {
                    _engine.Guitar1Processor.LoadImpulseResponse(sound.ExternalIrPath);
                    _guitar1LoadedIrPath = sound.ExternalIrPath;
                    _audioPreferences.Guitar1IrPath = sound.ExternalIrPath;
                    _guitar1IrPath.Text = sound.ExternalIrPath;
                    _guitar1ClearIrButton.Enabled = true;
                    string? folder = Path.GetDirectoryName(sound.ExternalIrPath);
                    if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                        ScanGuitar1IrBrowserFolder(folder, sound.ExternalIrPath, announce: false, persist: false);
                }
                catch
                {
                    _engine.Guitar1Processor.ClearImpulseResponse();
                    _guitar1LoadedIrPath = null;
                    _guitar1IrPath.Text = "Guitarra 1: gabinete interno.";
                    _guitar1ClearIrButton.Enabled = false;
                    notice = " El IR guardado no pudo cargarse; Guitarra 1 usa gabinete interno.";
                }
            }
            else
            {
                _engine.Guitar1Processor.ClearImpulseResponse();
                _guitar1LoadedIrPath = null;
                _guitar1IrPath.Text = "Guitarra 1: gabinete interno.";
                _guitar1ClearIrButton.Enabled = false;
                notice = " El IR guardado ya no existe; Guitarra 1 usa gabinete interno.";
            }
        }
        else
        {
            _engine.Guitar1Processor.ClearImpulseResponse();
            _guitar1LoadedIrPath = null;
            _audioPreferences.Guitar1IrPath = string.Empty;
            _guitar1IrPath.Text = "Guitarra 1: gabinete interno.";
            _guitar1ClearIrButton.Enabled = false;
        }

        _guitar1NamIncludesCabinet.Checked = bank.NamIncludesCabinet;
        SetNumeric(_guitar1NamInputTrim, bank.NamInputTrimDb);
        SetNumeric(_guitar1NamOutputTrim, bank.NamOutputTrimDb);
        _guitar1NamAutoLevel.Checked = bank.NamAutoLevelEnabled;
        SetNumeric(_guitar1NamAutoLevelDb, bank.NamAutoLevelDb);
        if (!string.IsNullOrWhiteSpace(bank.NamPath) && File.Exists(bank.NamPath))
        {
            bool sameModel = _engine.Guitar1Processor.HasNamModel &&
                string.Equals(_engine.Guitar1Processor.NamModelPath, bank.NamPath, StringComparison.OrdinalIgnoreCase);
            if (sameModel) _guitar1NamEnabled.Checked = bank.NamEnabled;
            else TryLoadGuitar1NamModel(bank.NamPath, announce: false, enableAfterLoad: bank.NamEnabled, resumeAudioAfterLoad: resumeAudioAfterNamLoad);
        }
        else if (bank.NamEnabled)
        {
            _guitar1NamEnabled.Checked = false;
            notice += " El NAM guardado ya no existe; Guitarra 1 quedó con amplificador interno.";
        }
        else _guitar1NamEnabled.Checked = false;
        return notice;
    }

    private string ApplyGuitar2Bank(DualGuitarBankPreset bank, bool resumeAudioAfterNamLoad = true)
    {
        ScenePreset sound = bank.Sound ?? new ScenePreset();
        // La memoria se actualiza antes de tocar los controles para que el callback
        // nunca reciba un bloque con amplificador nuevo y efectos viejos.
        _guitar2EffectMemory = sound with { Name = "Efectos Guitarra 2" };
        ApplyPresetSound(sound);
        SetNumeric(_guitar2Mix, bank.MixPercent);
        SetNumeric(_guitar2Pan, bank.PanPercent);
        _guitar2Mute.Checked = bank.Muted;

        if (!string.IsNullOrWhiteSpace(_loadedIrPath) && File.Exists(_loadedIrPath))
        {
            string? folder = Path.GetDirectoryName(_loadedIrPath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                ScanIrBrowserFolder(folder, _loadedIrPath, announce: false, persist: false);
        }

        _namIncludesCabinet.Checked = bank.NamIncludesCabinet;
        SetNumeric(_namInputTrim, bank.NamInputTrimDb);
        SetNumeric(_namOutputTrim, bank.NamOutputTrimDb);
        _namAutoLevel.Checked = bank.NamAutoLevelEnabled;
        SetNumeric(_namAutoLevelDb, bank.NamAutoLevelDb);
        string notice = string.Empty;
        if (!string.IsNullOrWhiteSpace(bank.NamPath) && File.Exists(bank.NamPath))
        {
            bool sameModel = _engine.Processor.HasNamModel &&
                string.Equals(_engine.Processor.NamModelPath, bank.NamPath, StringComparison.OrdinalIgnoreCase);
            if (sameModel) _namEnabled.Checked = bank.NamEnabled;
            else TryLoadNamModel(bank.NamPath, announce: false, enableAfterLoad: bank.NamEnabled, resumeAudioAfterLoad: resumeAudioAfterNamLoad);
        }
        else if (bank.NamEnabled)
        {
            _namEnabled.Checked = false;
            notice = " El NAM guardado ya no existe; Guitarra 2 quedó con amplificador interno.";
        }
        else _namEnabled.Checked = false;
        return notice;
    }

    private void DeleteSelectedDualGuitarBank()
    {
        int guitarIndex = Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1);
        string guitarName = guitarIndex == 0 ? "Guitarra 1" : "Guitarra 2";
        List<DualGuitarBankPreset> banks = _dualGuitarBankLibrary.BanksFor(guitarIndex);
        int index = _dualBankCombo.SelectedIndex;
        if (index < 0 || index >= banks.Count)
        {
            SetStatus($"{guitarName} no tiene un banco seleccionado.", true);
            return;
        }
        string name = banks[index].Name;
        banks.RemoveAt(index);
        int next = banks.Count == 0 ? -1 : Math.Min(index, banks.Count - 1);
        _dualGuitarBankLibrary.SetLastIndexFor(guitarIndex, next);
        try
        {
            DualGuitarBankStore.Save(_dualGuitarBankLibrary);
            RefreshDualGuitarBankList(next);
            SetStatus($"{guitarName}, banco {name} eliminado. La otra biblioteca no fue modificada.");
        }
        catch (Exception ex) { SetStatus($"No se pudo eliminar el banco de {guitarName}: {ex.Message}", true); }
    }

    private void RefreshDualGuitarSceneList(int selectedIndex = int.MinValue)
    {
        _dualGuitarSceneLibrary.Normalize();
        int desired = selectedIndex == int.MinValue ? _dualGuitarSceneLibrary.LastSelectedIndex : selectedIndex;
        _loadingDualSceneList = true;
        try
        {
            _dualSceneCombo.BeginUpdate();
            _dualSceneCombo.Items.Clear();
            foreach (DualGuitarScenePreset scene in _dualGuitarSceneLibrary.Scenes) _dualSceneCombo.Items.Add(scene.Name);
            _dualSceneCombo.EndUpdate();

            if (_dualGuitarSceneLibrary.Scenes.Count == 0)
            {
                _dualSceneCombo.SelectedIndex = -1;
                _dualSceneName.Text = string.Empty;
                _dualSceneLoadButton.Enabled = false;
                _dualSceneDeleteButton.Enabled = false;
                _dualSceneNextButton.Enabled = false;
            }
            else
            {
                desired = Math.Clamp(desired < 0 ? 0 : desired, 0, _dualGuitarSceneLibrary.Scenes.Count - 1);
                _dualSceneCombo.SelectedIndex = desired;
                _dualSceneName.Text = _dualGuitarSceneLibrary.Scenes[desired].Name;
                _dualGuitarSceneLibrary.LastSelectedIndex = desired;
                _dualSceneLoadButton.Enabled = true;
                _dualSceneDeleteButton.Enabled = true;
                _dualSceneNextButton.Enabled = _dualGuitarSceneLibrary.Scenes.Count > 1;
            }
            _dualSceneCombo.AccessibleDescription = _dualGuitarSceneLibrary.Scenes.Count == 0
                ? "Todavía no hay escenas completas guardadas. Escriba un nombre y pulse Guardar escena completa."
                : $"Hay {_dualGuitarSceneLibrary.Scenes.Count} escenas completas. Cada una restaura simultáneamente ambas guitarras, paneos, master y acompañamiento.";
        }
        finally { _loadingDualSceneList = false; }
    }

    private void HandleDualSceneSelectionChanged()
    {
        if (_loadingDualSceneList) return;
        int index = _dualSceneCombo.SelectedIndex;
        if (index < 0 || index >= _dualGuitarSceneLibrary.Scenes.Count) return;
        _dualSceneName.Text = _dualGuitarSceneLibrary.Scenes[index].Name;
        _dualGuitarSceneLibrary.LastSelectedIndex = index;
        try { DualGuitarSceneStore.Save(_dualGuitarSceneLibrary); } catch { }
    }

    private DualGuitarScenePreset CaptureDualGuitarScene(string name)
    {
        int selectedGuitar = Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1);
        StoreVisibleEffectsForGuitar(selectedGuitar);
        DualGuitarBankPreset guitar1 = CaptureDualGuitarBank(0, "Guitarra 1", captureVisibleEffects: false);
        DualGuitarBankPreset guitar2 = CaptureDualGuitarBank(1, "Guitarra 2", captureVisibleEffects: false);
        return new DualGuitarScenePreset
        {
            Name = NormalizeDualSceneName(name),
            Guitar1 = guitar1,
            Guitar2 = guitar2,
            MasterVolumePercent = (float)_masterVolume.Value,
            SelectedGuitarIndex = selectedGuitar,
            MetronomeEnabled = _metronomeEnabled.Checked,
            MetronomeBpm = (float)_metronomeBpm.Value,
            MetronomeBeatsPerBar = SelectedMetronomeBeatsPerBar,
            MetronomeAccentFirstBeat = _metronomeAccent.Checked,
            MetronomeVolumePercent = (float)_metronomeVolume.Value,
            DrumsEnabled = _drumsEnabled.Checked,
            DrumPattern = Math.Clamp(_drumPattern.SelectedIndex, 0, 5),
            DrumVolumePercent = (float)_drumVolume.Value,
            BackingBassEnabled = _backingBassEnabled.Checked,
            BackingBassKey = Math.Clamp(_backingBassKey.SelectedIndex, 0, 11),
            BackingBassMinor = _backingBassMode.SelectedIndex == 1,
            BackingBassLine = Math.Clamp(_backingBassLine.SelectedIndex, 0, 4),
            BackingBassVolumePercent = (float)_backingBassVolume.Value,
            PianoEnabled = _pianoEnabled.Checked,
            PianoSound = Math.Clamp(_pianoSound.SelectedIndex, 0, 5),
            PianoKey = Math.Clamp(_pianoKey.SelectedIndex, 0, 23),
            PianoProgression = Math.Clamp(_pianoProgression.SelectedIndex, 0, 5),
            PianoCustomProgression = _pianoCustomProgression.Text.Trim(),
            PianoStyle = Math.Clamp(_pianoStyle.SelectedIndex, 0, 8),
            PianoVolumePercent = (float)_pianoVolume.Value
        };
    }

    private void SaveDualGuitarScene()
    {
        if (!_twoGuitarMode.Checked)
        {
            SetStatus("Active Modo Dos Guitarras antes de guardar una escena completa.", true);
            return;
        }
        string name = NormalizeDualSceneName(_dualSceneName.Text);
        DualGuitarScenePreset scene = CaptureDualGuitarScene(name);
        int existing = _dualGuitarSceneLibrary.Scenes.FindIndex(item => string.Equals(item.Name, name, StringComparison.CurrentCultureIgnoreCase));
        int selected;
        if (existing >= 0)
        {
            _dualGuitarSceneLibrary.Scenes[existing] = scene;
            selected = existing;
        }
        else
        {
            _dualGuitarSceneLibrary.Scenes.Add(scene);
            selected = _dualGuitarSceneLibrary.Scenes.Count - 1;
        }
        _dualGuitarSceneLibrary.LastSelectedIndex = selected;
        try
        {
            DualGuitarSceneStore.Save(_dualGuitarSceneLibrary);
            RefreshDualGuitarSceneList(selected);
            SetStatus(existing >= 0
                ? $"Escena completa {name} actualizada. Guarda las dos guitarras, paneos, master y acompañamiento."
                : $"Escena completa {name} guardada. Total: {_dualGuitarSceneLibrary.Scenes.Count}.");
        }
        catch (Exception ex) { SetStatus($"No se pudo guardar la escena completa: {ex.Message}", true); }
    }

    private void LoadSelectedDualGuitarScene()
    {
        int index = _dualSceneCombo.SelectedIndex;
        if (index < 0 || index >= _dualGuitarSceneLibrary.Scenes.Count)
        {
            SetStatus("No hay una escena completa seleccionada.", true);
            return;
        }

        DualGuitarScenePreset scene = _dualGuitarSceneLibrary.Scenes[index];
        bool wasRunning = _audioRequested || _engine.HasActiveSession;
        if (wasRunning) StopAudioForNamChange();
        if (!_twoGuitarMode.Checked) _twoGuitarMode.Checked = true;

        string notice = string.Empty;
        string? fatalError = null;
        _loadingDualScene = true;
        try
        {
            if (string.IsNullOrWhiteSpace(scene.Guitar1.NamPath) && _engine.Guitar1Processor.HasNamModel)
            {
                _guitar1NamEnabled.Checked = false;
                _engine.Guitar1Processor.ClearNamModel();
                _audioPreferences.Guitar1NamModelPath = string.Empty;
                _guitar1ActiveNamBankId = null;
                _guitar1NamPath.Text = "Guitarra 1: ningún modelo NAM cargado.";
                _guitar1NamClearButton.Enabled = false;
            }
            if (string.IsNullOrWhiteSpace(scene.Guitar2.NamPath) && _engine.Processor.HasNamModel)
            {
                _namEnabled.Checked = false;
                _engine.Processor.ClearNamModel();
                _audioPreferences.NamModelPath = string.Empty;
                _activeNamBankId = null;
                _namPath.Text = "Ningún modelo NAM cargado.";
                _clearNamButton.Enabled = false;
            }

            notice += ApplyGuitar1Bank(scene.Guitar1, resumeAudioAfterNamLoad: false);
            notice += ApplyGuitar2Bank(scene.Guitar2, resumeAudioAfterNamLoad: false);

            SetNumeric(_masterVolume, scene.MasterVolumePercent);
            _metronomeEnabled.Checked = scene.MetronomeEnabled;
            SetNumeric(_metronomeBpm, scene.MetronomeBpm);
            _metronomeMeter.SelectedIndex = scene.MetronomeBeatsPerBar switch { 2 => 0, 3 => 1, 6 => 3, _ => 2 };
            _metronomeAccent.Checked = scene.MetronomeAccentFirstBeat;
            SetNumeric(_metronomeVolume, scene.MetronomeVolumePercent);
            _drumsEnabled.Checked = scene.DrumsEnabled;
            _drumPattern.SelectedIndex = Math.Clamp(scene.DrumPattern, 0, 5);
            SetNumeric(_drumVolume, scene.DrumVolumePercent);
            _backingBassEnabled.Checked = scene.BackingBassEnabled;
            _backingBassKey.SelectedIndex = Math.Clamp(scene.BackingBassKey, 0, 11);
            _backingBassMode.SelectedIndex = scene.BackingBassMinor ? 1 : 0;
            _backingBassLine.SelectedIndex = Math.Clamp(scene.BackingBassLine, 0, 4);
            SetNumeric(_backingBassVolume, scene.BackingBassVolumePercent);
            _pianoEnabled.Checked = scene.PianoEnabled;
            _pianoSound.SelectedIndex = Math.Clamp(scene.PianoSound, 0, 5);
            _pianoKey.SelectedIndex = Math.Clamp(scene.PianoKey, 0, 23);
            _pianoProgression.SelectedIndex = Math.Clamp(scene.PianoProgression, 0, 5);
            _pianoCustomProgression.Text = string.IsNullOrWhiteSpace(scene.PianoCustomProgression) ? "I, V, vi, IV" : scene.PianoCustomProgression;
            _pianoStyle.SelectedIndex = Math.Clamp(scene.PianoStyle, 0, 8);
            SetNumeric(_pianoVolume, scene.PianoVolumePercent);

            int selectedGuitar = Math.Clamp(scene.SelectedGuitarIndex, 0, 1);
            _loadingDualEffectMemory = true;
            try
            {
                _dualEditGuitarCombo.SelectedIndex = selectedGuitar;
                _dualLastEditedGuitar = selectedGuitar;
            }
            finally { _loadingDualEffectMemory = false; }
            ScenePreset visibleMemory = selectedGuitar == 0
                ? (_guitar1EffectMemory ?? scene.Guitar1.Sound)
                : (_guitar2EffectMemory ?? scene.Guitar2.Sound);
            LoadDualEffectMemoryIntoControls(visibleMemory);

            UpdateParameters();
            RefreshNamStatus(announce: false);
            _engine.RequestMetronomeReset();
            SaveAudioPreferences();
            _dualGuitarSceneLibrary.LastSelectedIndex = index;
            try { DualGuitarSceneStore.Save(_dualGuitarSceneLibrary); } catch { }
        }
        catch (Exception ex)
        {
            fatalError = ex.Message;
        }
        finally
        {
            _loadingDualScene = false;
            if (wasRunning && !_engine.IsRunning)
            {
                try
                {
                    ToggleAudio();
                    if (_engine.IsRunning) _engine.RequestMetronomeReset();
                }
                catch (Exception ex)
                {
                    fatalError = string.IsNullOrWhiteSpace(fatalError) ? ex.Message : fatalError + "; " + ex.Message;
                }
            }
        }

        RefreshDualGuitarBankList();
        RefreshDualQuickNamSelector();
        UpdateDualGuitarStatus();
        if (!string.IsNullOrWhiteSpace(fatalError))
        {
            SetStatus($"La escena completa {scene.Name} no pudo restaurarse por completo: {fatalError}. Revise Alt+D.", true);
            return;
        }
        string audioText = wasRunning ? " Audio reiniciado una sola vez con ambos rigs." : string.Empty;
        SetStatus($"Escena completa {scene.Name} cargada: Guitarra 1, Guitarra 2, paneos, master y acompañamiento restaurados.{audioText}{notice}", !string.IsNullOrWhiteSpace(notice));
    }

    private void DeleteSelectedDualGuitarScene()
    {
        int index = _dualSceneCombo.SelectedIndex;
        if (index < 0 || index >= _dualGuitarSceneLibrary.Scenes.Count)
        {
            SetStatus("No hay una escena completa seleccionada.", true);
            return;
        }
        string name = _dualGuitarSceneLibrary.Scenes[index].Name;
        _dualGuitarSceneLibrary.Scenes.RemoveAt(index);
        int next = _dualGuitarSceneLibrary.Scenes.Count == 0 ? -1 : Math.Min(index, _dualGuitarSceneLibrary.Scenes.Count - 1);
        _dualGuitarSceneLibrary.LastSelectedIndex = next;
        try
        {
            DualGuitarSceneStore.Save(_dualGuitarSceneLibrary);
            RefreshDualGuitarSceneList(next);
            SetStatus($"Escena completa {name} eliminada. Los bancos individuales no fueron modificados.");
        }
        catch (Exception ex) { SetStatus($"No se pudo eliminar la escena completa: {ex.Message}", true); }
    }

    private void LoadNextDualGuitarScene()
    {
        int count = _dualGuitarSceneLibrary.Scenes.Count;
        if (count == 0)
        {
            SetStatus("No hay escenas completas de Dos Guitarras guardadas.", true);
            return;
        }
        int next = (_dualSceneCombo.SelectedIndex + 1 + count) % count;
        _dualSceneCombo.SelectedIndex = next;
        LoadSelectedDualGuitarScene();
    }

    private static string NormalizeDualSceneName(string? value)
    {
        string name = string.IsNullOrWhiteSpace(value) ? "Escena dual" : value.Trim();
        return name.Length <= 60 ? name : name[..60];
    }

    private void LoadDualEffectMemoryIntoControls(ScenePreset scene)
    {
        bool previousLoadingScene = _loadingScene;
        _loadingDualEffectMemory = true;
        _loadingScene = true;
        try
        {
            _octaverEnabled.Checked = scene.OctaverEnabled;
            _octaverCharacterCombo.SelectedIndex = Math.Clamp((int)scene.OctaverCharacter, 0, 4);
            SetNumeric(_octaverDry, scene.OctaverDryPercent);
            SetNumeric(_octaverDown, scene.OctaverDownPercent);
            SetNumeric(_octaverUp, scene.OctaverUpPercent);
            SetNumeric(_octaverTone, scene.OctaverTonePercent);
            SetNumeric(_octaverLevel, scene.OctaverLevelPercent);

            _gateEnabled.Checked = scene.GateEnabled;
            SetNumeric(_gateThreshold, scene.GateThresholdDb);
            SetNumeric(_gateRelease, scene.GateReleaseMs);

            _compressorEnabled.Checked = scene.CompressorEnabled;
            _compressorCharacterCombo.SelectedIndex = Math.Clamp((int)scene.CompressorCharacter, 0, 3);
            SetNumeric(_compressorSustain, scene.CompressorSustain);
            SetNumeric(_compressorAttack, scene.CompressorAttackMs);
            SetNumeric(_compressorLevel, scene.CompressorLevel);

            _autoWahEnabled.Checked = scene.AutoWahEnabled;
            _autoWahModeCombo.SelectedIndex = Math.Clamp((int)scene.AutoWahMode, 0, 1);
            _autoWahCharacterCombo.SelectedIndex = Math.Clamp((int)scene.AutoWahCharacter, 0, 2);
            SetNumeric(_autoWahSensitivity, scene.AutoWahSensitivity);
            SetNumeric(_autoWahRange, scene.AutoWahRange);
            SetNumeric(_autoWahResonance, scene.AutoWahResonance);
            SetNumeric(_autoWahManualPosition, scene.AutoWahManualPositionPercent);
            UpdateAutoWahModeUi();

            _overdriveEnabled.Checked = scene.OverdriveEnabled;
            _distortionCharacterCombo.SelectedIndex = Math.Clamp((int)scene.DistortionCharacter, 0, 3);
            SetNumeric(_overdriveGain, scene.OverdriveGain);
            SetNumeric(_overdriveTone, scene.OverdriveTone);
            SetNumeric(_overdriveLevel, scene.OverdriveLevel);

            _ts9Enabled.Checked = scene.Ts9Enabled;
            _driveCharacterCombo.SelectedIndex = Math.Clamp((int)scene.DriveCharacter, 0, 4);
            SetNumeric(_ts9Gain, scene.Ts9Gain);
            SetNumeric(_ts9Tone, scene.Ts9Tone);
            SetNumeric(_ts9Level, scene.Ts9Level);

            ApplyNewPedalControls(scene);

            _boosterEnabled.Checked = scene.BoosterEnabled;
            _boosterCharacterCombo.SelectedIndex = Math.Clamp((int)scene.BoosterCharacter, 0, 2);
            SetNumeric(_boosterDb, scene.BoosterDb);
            _preEffectOrder = UserPresetLibrary.NormalizeOrder(scene.PreEffectOrder);

            _fxLoopEnabled.Checked = scene.FxLoopEnabled;
            SetNumeric(_fxLoopSend, scene.FxLoopSendPercent);
            SetNumeric(_fxLoopReturn, scene.FxLoopReturnPercent);

            _phaserEnabled.Checked = scene.PhaserEnabled;
            SetNumeric(_phaserRate, scene.PhaserRateHz);
            SetNumeric(_phaserDepth, scene.PhaserDepthPercent);
            SetNumeric(_phaserFeedback, scene.PhaserFeedbackPercent);
            SetNumeric(_phaserMix, scene.PhaserMixPercent);

            _flangerEnabled.Checked = scene.FlangerEnabled;
            _flangerCharacterCombo.SelectedIndex = Math.Clamp((int)scene.FlangerCharacter, 0, 2);
            SetNumeric(_flangerRate, scene.FlangerRateHz);
            SetNumeric(_flangerDepth, scene.FlangerDepthPercent);
            SetNumeric(_flangerFeedback, scene.FlangerFeedbackPercent);
            SetNumeric(_flangerMix, scene.FlangerMixPercent);

            _chorusEnabled.Checked = scene.ChorusEnabled;
            _chorusPlacementCombo.SelectedIndex = Math.Clamp((int)scene.ChorusPlacement, 0, 1);
            _chorusCharacterCombo.SelectedIndex = Math.Clamp((int)scene.ChorusCharacter, 0, 1);
            SetNumeric(_chorusRate, scene.ChorusRateHz);
            SetNumeric(_chorusDepth, scene.ChorusDepthMs);
            SetNumeric(_chorusMix, scene.ChorusMixPercent);

            _analogChorusEnabled.Checked = scene.AnalogChorusEnabled;
            _analogChorusPlacementCombo.SelectedIndex = Math.Clamp((int)scene.AnalogChorusPlacement, 0, 1);
            SetNumeric(_analogChorusRate, scene.AnalogChorusRateHz);
            SetNumeric(_analogChorusDepth, scene.AnalogChorusDepth);
            SetNumeric(_analogChorusMix, scene.AnalogChorusMixPercent);
            SetNumeric(_analogChorusLow, scene.AnalogChorusLow);
            SetNumeric(_analogChorusHigh, scene.AnalogChorusHigh);
            _microPitchEnabled.Checked = scene.MicroPitchEnabled;
            SetNumeric(_microPitchDetune, scene.MicroPitchDetuneCents);
            SetNumeric(_microPitchDelay, scene.MicroPitchDelayMs);
            SetNumeric(_microPitchMix, scene.MicroPitchMixPercent);

            _rotaryEnabled.Checked = scene.RotaryEnabled;
            _rotarySync.Checked = scene.RotarySyncEnabled;
            _rotaryDivision.SelectedIndex = Math.Clamp((int)scene.RotaryDivision, 0, 4);
            _rotaryFast.Checked = scene.RotaryFast;
            SetNumeric(_rotaryDepth, scene.RotaryDepthPercent);
            SetNumeric(_rotaryMix, scene.RotaryMixPercent);

            _tremoloEnabled.Checked = scene.TremoloEnabled;
            _tremoloSync.Checked = scene.TremoloSyncEnabled;
            _tremoloDivision.SelectedIndex = Math.Clamp((int)scene.TremoloDivision, 0, 4);
            SetNumeric(_tremoloRate, scene.TremoloRateHz);
            SetNumeric(_tremoloDepth, scene.TremoloDepthPercent);

            _delayEnabled.Checked = scene.DelayEnabled;
            _delaySync.Checked = scene.DelaySyncEnabled;
            _delayDivision.SelectedIndex = Math.Clamp((int)scene.DelayDivision, 0, 4);
            _delayCharacterCombo.SelectedIndex = Math.Clamp((int)scene.DelayCharacter, 0, 3);
            SetNumeric(_delayTime, scene.DelayTimeMs);
            SetNumeric(_delayFeedback, scene.DelayFeedbackPercent);
            SetNumeric(_delayMix, scene.DelayMixPercent);

            _reverbEnabled.Checked = scene.ReverbEnabled;
            _reverbCharacterCombo.SelectedIndex = Math.Clamp((int)scene.ReverbCharacter, 0, 6);
            SetNumeric(_reverbMix, scene.ReverbMixPercent);
            SetNumeric(_reverbDecay, scene.ReverbDecayPercent);
            SetNumeric(_reverbTone, scene.ReverbTonePercent);
            SetNumeric(_reverbPreDelay, scene.ReverbPreDelayMs);
            SetNumeric(_reverbDamping, scene.ReverbDampingPercent);
            SetNumeric(_reverbDiffusion, scene.ReverbDiffusionPercent);
        }
        finally
        {
            _loadingScene = previousLoadingScene;
            _loadingDualEffectMemory = false;
        }

        PopulatePreChainList();
        UpdateDynamicEffectAccessibleNames();
    }

    private static DspParameters ApplyEffectMemoryToParameters(DspParameters target, ScenePreset source)
    {
        return target with
        {
            OctaverEnabled = source.OctaverEnabled,
            OctaverCharacter = source.OctaverCharacter,
            OctaverDryPercent = source.OctaverDryPercent,
            OctaverDownPercent = source.OctaverDownPercent,
            OctaverUpPercent = source.OctaverUpPercent,
            OctaverTonePercent = source.OctaverTonePercent,
            OctaverLevelPercent = source.OctaverLevelPercent,
            GateEnabled = source.GateEnabled,
            GateThresholdDb = source.GateThresholdDb,
            GateReleaseMs = source.GateReleaseMs,
            CompressorEnabled = source.CompressorEnabled,
            CompressorCharacter = source.CompressorCharacter,
            CompressorSustain = source.CompressorSustain,
            CompressorAttackMs = source.CompressorAttackMs,
            CompressorLevel = source.CompressorLevel,
            AutoWahEnabled = source.AutoWahEnabled,
            AutoWahMode = source.AutoWahMode,
            AutoWahCharacter = source.AutoWahCharacter,
            AutoWahSensitivity = source.AutoWahSensitivity,
            AutoWahRange = source.AutoWahRange,
            AutoWahResonance = source.AutoWahResonance,
            AutoWahManualPositionPercent = source.AutoWahManualPositionPercent,
            OverdriveEnabled = source.OverdriveEnabled,
            DistortionCharacter = source.DistortionCharacter,
            OverdriveGain = source.OverdriveGain,
            OverdriveTone = source.OverdriveTone,
            OverdriveLevel = source.OverdriveLevel,
            Ts9Enabled = source.Ts9Enabled,
            DriveCharacter = source.DriveCharacter,
            Ts9Gain = source.Ts9Gain,
            Ts9Tone = source.Ts9Tone,
            Ts9Level = source.Ts9Level,
            Od1Enabled = source.Od1Enabled,
            Od1Character = source.Od1Character,
            Od1Drive = source.Od1Drive,
            Od1Tone = source.Od1Tone,
            Od1Level = source.Od1Level,
            FuzzEnabled = source.FuzzEnabled,
            FuzzCharacter = source.FuzzCharacter,
            FuzzGain = source.FuzzGain,
            FuzzTone = source.FuzzTone,
            FuzzLevel = source.FuzzLevel,
            Eq5Enabled = source.Eq5Enabled,
            Eq5Placement = source.Eq5Placement,
            Eq5Band100Db = source.Eq5Band100Db,
            Eq5Band250Db = source.Eq5Band250Db,
            Eq5Band800Db = source.Eq5Band800Db,
            Eq5Band2500Db = source.Eq5Band2500Db,
            Eq5Band6400Db = source.Eq5Band6400Db,
            Eq5OutputDb = source.Eq5OutputDb,
            BoosterEnabled = source.BoosterEnabled,
            BoosterCharacter = source.BoosterCharacter,
            BoosterDb = source.BoosterDb,
            PreEffectOrder = UserPresetLibrary.NormalizeOrder(source.PreEffectOrder).ToArray(),
            FxLoopEnabled = source.FxLoopEnabled,
            FxLoopSendPercent = source.FxLoopSendPercent,
            FxLoopReturnPercent = source.FxLoopReturnPercent,
            PhaserEnabled = source.PhaserEnabled,
            PhaserRateHz = source.PhaserRateHz,
            PhaserDepthPercent = source.PhaserDepthPercent,
            PhaserFeedbackPercent = source.PhaserFeedbackPercent,
            PhaserMixPercent = source.PhaserMixPercent,
            FlangerEnabled = source.FlangerEnabled,
            FlangerCharacter = source.FlangerCharacter,
            FlangerRateHz = source.FlangerRateHz,
            FlangerDepthPercent = source.FlangerDepthPercent,
            FlangerFeedbackPercent = source.FlangerFeedbackPercent,
            FlangerMixPercent = source.FlangerMixPercent,
            ChorusEnabled = source.ChorusEnabled,
            ChorusPlacement = source.ChorusPlacement,
            ChorusCharacter = source.ChorusCharacter,
            ChorusRateHz = source.ChorusRateHz,
            ChorusDepthMs = source.ChorusDepthMs,
            ChorusMixPercent = source.ChorusMixPercent,
            AnalogChorusEnabled = source.AnalogChorusEnabled,
            AnalogChorusPlacement = source.AnalogChorusPlacement,
            AnalogChorusRateHz = source.AnalogChorusRateHz,
            AnalogChorusDepth = source.AnalogChorusDepth,
            AnalogChorusMixPercent = source.AnalogChorusMixPercent,
            AnalogChorusLow = source.AnalogChorusLow,
            AnalogChorusHigh = source.AnalogChorusHigh,
            MicroPitchEnabled = source.MicroPitchEnabled,
            MicroPitchDetuneCents = source.MicroPitchDetuneCents,
            MicroPitchDelayMs = source.MicroPitchDelayMs,
            MicroPitchMixPercent = source.MicroPitchMixPercent,
            RotaryEnabled = source.RotaryEnabled,
            RotarySyncEnabled = source.RotarySyncEnabled,
            RotaryDivision = source.RotaryDivision,
            RotaryRateHz = source.RotaryRateHz,
            RotaryFast = source.RotaryFast,
            RotaryDepthPercent = source.RotaryDepthPercent,
            RotaryMixPercent = source.RotaryMixPercent,
            TremoloEnabled = source.TremoloEnabled,
            TremoloSyncEnabled = source.TremoloSyncEnabled,
            TremoloDivision = source.TremoloDivision,
            TremoloRateHz = source.TremoloRateHz,
            TremoloDepthPercent = source.TremoloDepthPercent,
            DelayEnabled = source.DelayEnabled,
            DelaySyncEnabled = source.DelaySyncEnabled,
            DelayDivision = source.DelayDivision,
            DelayCharacter = source.DelayCharacter,
            DelayTimeMs = source.DelayTimeMs,
            DelayFeedbackPercent = source.DelayFeedbackPercent,
            DelayMixPercent = source.DelayMixPercent,
            ReverbEnabled = source.ReverbEnabled,
            ReverbCharacter = source.ReverbCharacter,
            ReverbMixPercent = source.ReverbMixPercent,
            ReverbDecayPercent = source.ReverbDecayPercent,
            ReverbTonePercent = source.ReverbTonePercent,
            ReverbPreDelayMs = source.ReverbPreDelayMs,
            ReverbDampingPercent = source.ReverbDampingPercent,
            ReverbDiffusionPercent = source.ReverbDiffusionPercent
        };
    }

    private void HandleTwoGuitarModeChanged()
    {
        if (_twoGuitarMode.Checked)
        {
            if (_inputCombo.Items.Count > 0 && _inputCombo.Items.Count < 2)
            {
                _twoGuitarMode.Checked = false;
                SetStatus("Modo Dos Guitarras necesita al menos dos entradas ASIO. El modo quedó desactivado.", true);
                return;
            }

            // Input 1 deja de ser micrófono y pasa a ser Guitarra 1. El rig principal
            // se fija en Input 2 para mantener el comportamiento conocido de la 2.40.19.
            if (_inputCombo.Items.Count >= 2) _inputCombo.SelectedIndex = 1;
            if (_voiceOnlyMode.Checked) _voiceOnlyMode.Checked = false;
            if (_voiceEnabled.Checked) _voiceEnabled.Checked = false;
            _simulationEnabled.Enabled = false;
            _simulationEnabled.TabStop = false;
            _simulationEnabled.AccessibleDescription = "Modo Dos Guitarras activo: ambas cadenas permanecen procesadas. El bypass general se bloquea para impedir que una guitarra quede cruda.";
            _audioPreferences.LastClassProfile = "Personalizado";

            if (_dualEffectMemoriesInitialized)
            {
                // Al entrar desde el modo normal, el estado visible pertenece al rig principal
                // y se conserva como memoria de Guitarra 2 antes de mostrar la guitarra elegida.
                if (!_audioPreferences.TwoGuitarMode)
                    _guitar2EffectMemory = CaptureCurrentScene("Efectos Guitarra 2");
                _dualLastEditedGuitar = Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1);
                LoadDualEffectMemoryIntoControls(_dualLastEditedGuitar == 0
                    ? (_guitar1EffectMemory ?? CaptureCurrentScene("Efectos Guitarra 1"))
                    : (_guitar2EffectMemory ?? CaptureCurrentScene("Efectos Guitarra 2")));
            }

            SetStatus("Modo Dos Guitarras activado. Ambas guitarras quedan procesadas simultáneamente y la sección Efectos edita solamente la guitarra elegida en Guitarra a editar.");
        }
        else
        {
            if (_dualEffectMemoriesInitialized)
            {
                StoreVisibleEffectsForGuitar(_dualLastEditedGuitar);
                if (_guitar2EffectMemory is not null)
                    LoadDualEffectMemoryIntoControls(_guitar2EffectMemory);
            }

            _simulationEnabled.Enabled = true;
            _simulationEnabled.TabStop = true;
            _simulationEnabled.AccessibleDescription = "Cuando se desactiva se escucha la guitarra directa con el volumen maestro, sin puerta, pedales, amplificador, gabinete ni efectos.";
            _effectSelector.AccessibleName = "Efecto a configurar";
            _effectSelector.AccessibleDescription = "Seleccione un efecto y solamente se mostrarán sus controles. El afinador está en Herramientas.";
            SetStatus("Modo Dos Guitarras desactivado. Vuelve a estar disponible el funcionamiento normal de guitarra y micrófono y el bypass general Control más B.");
        }

        UpdateDualGuitarStatus();
        UpdateParameters();
        UpdateLooperUi();
        SaveAudioPreferences();
        UpdateTunerAccessibleContext();
    }

    private void AnnounceDualGuitarSelection()
    {
        bool guitar1 = _dualEditGuitarCombo.SelectedIndex != 1;
        string selected = guitar1
            ? "Guitarra 1, Input 1"
            : "Guitarra 2, Input 2";

        // 2.41.14: el selector también determina qué memoria modifica la sección Efectos. El nombre accesible contiene
        // siempre la selección actual y los controles de la otra guitarra salen del orden
        // de tabulación. De esta forma JAWS no sigue presentando Guitarra 1 al elegir Guitarra 2.
        _dualEditGuitarCombo.AccessibleName = $"Guitarra a editar. Seleccionada: {selected}";
        if (_twoGuitarMode.Checked)
        {
            _effectSelector.AccessibleName = $"Efecto a configurar para {selected}";
            _effectSelector.AccessibleDescription = $"La sección Efectos modifica solamente {selected}. La otra guitarra conserva sus propios valores.";
        }
        _dualEditGuitarCombo.AccessibleDescription = guitar1
            ? "Guitarra 1 usa Input 1 con amplificador interno o NAM propio, IR y efectos independientes. La sección Efectos edita exclusivamente la memoria de Guitarra 1."
            : "Guitarra 2 usa Input 2 y el rig principal con su propio NAM e IR. La sección Efectos edita exclusivamente la memoria de Guitarra 2.";

        _guitar1UseRigEffects.TabStop = guitar1;
        _guitar1AmpCombo.TabStop = guitar1;
        _guitar1Gain.TabStop = guitar1;
        _guitar1Output.TabStop = guitar1;
        _guitar1Mix.TabStop = guitar1;
        _guitar1Pan.TabStop = guitar1;
        _guitar1Mute.TabStop = guitar1;
        _guitar1NamEnabled.TabStop = guitar1;
        _guitar1NamBankCombo.TabStop = guitar1;
        _guitar1NamLoadBankButton.TabStop = guitar1;
        _guitar1NamPreviousButton.TabStop = guitar1;
        _guitar1NamNextButton.TabStop = guitar1;
        _guitar1NamLoadFileButton.TabStop = guitar1;
        _guitar1NamClearButton.TabStop = guitar1;
        _guitar2Mix.TabStop = !guitar1;
        _guitar2Pan.TabStop = !guitar1;
        _guitar2Mute.TabStop = !guitar1;

        List<DualGuitarBankPreset> visibleBanks = _dualGuitarBankLibrary.BanksFor(guitar1 ? 0 : 1);
        string bankState = visibleBanks.Count == 0
            ? "sin bancos guardados"
            : $"{visibleBanks.Count} bancos; seleccionado {_dualBankCombo.SelectedItem?.ToString() ?? visibleBanks[0].Name}";
        if (guitar1)
            SetStatus($"Editando Guitarra 1, Input 1. Efectos, IR, NAM y bancos pertenecen solamente a Guitarra 1. Las escenas completas de Alt+O abarcan ambas guitarras. El selector NAM rápido controla esta guitarra; {bankState}.");
        else
            SetStatus($"Editando Guitarra 2, Input 2. Efectos, bancos y NAM pertenecen solamente a Guitarra 2. Las escenas completas de Alt+O abarcan ambas guitarras. Use el selector NAM rápido para cambiar capturas sin abrir la sección NAM; {bankState}.");
    }

    private void UpdateDualGuitarStatus()
    {
        string amp = _guitar1AmpCombo.SelectedItem?.ToString() ?? "amplificador interno";
        string state = _twoGuitarMode.Checked ? "ACTIVO" : "desactivado";
        string g1Mute = _guitar1Mute.Checked ? "silenciada" : "activa";
        string g2Mute = _guitar2Mute.Checked ? "silenciada" : "activa";
        string g1Processing = _guitar1ProcessingEnabled.Checked ? "procesamiento 100 por ciento DSP" : "bypass crudo";
        string g1Effects = _guitar1UseRigEffects.Checked ? "efectos independientes activos" : "sin efectos";
        string g1Nam = _guitar1NamEnabled.Checked && _engine.Guitar1Processor.HasNamModel
            ? $"NAM activo {Path.GetFileNameWithoutExtension(_engine.Guitar1Processor.NamModelPath)}"
            : _engine.Guitar1Processor.HasNamModel
                ? $"NAM preparado {Path.GetFileNameWithoutExtension(_engine.Guitar1Processor.NamModelPath)}"
                : "NAM sin modelo";
        string editing = _dualEditGuitarCombo.SelectedIndex == 1 ? "Guitarra 2" : "Guitarra 1";
        string g1Pan = FormatPanForSpeech(_guitar1Pan.Value);
        string g2Pan = FormatPanForSpeech(_guitar2Pan.Value);
        string text = $"Modo Dos Guitarras {state}. Editando efectos y bancos de {editing}. Bancos personales: Guitarra 1 {_dualGuitarBankLibrary.Guitar1Banks.Count}, Guitarra 2 {_dualGuitarBankLibrary.Guitar2Banks.Count}; 30 bancos de fábrica disponibles para cada guitarra; escenas completas {_dualGuitarSceneLibrary.Scenes.Count}. Guitarra 1: {g1Processing}, {g1Effects}, {g1Nam}, {amp}, ganancia {_guitar1Gain.Value:0.0}, volumen DSP {_guitar1Output.Value:0} %, mezcla {_guitar1Mix.Value:0} %, paneo {g1Pan}, {g1Mute}. Guitarra 2: efectos independientes, rig principal/NAM, mezcla {_guitar2Mix.Value:0} %, paneo {g2Pan}, {g2Mute}.";
        _dualGuitarStatusLabel.Text = text;
        _dualGuitarStatusLabel.AccessibleName = text;
    }

    private static string FormatPanForSpeech(decimal value)
    {
        decimal pan = Math.Clamp(value, -100m, 100m);
        if (pan == 0m) return "Centro";
        return pan < 0m
            ? $"{Math.Abs(pan):0} por ciento izquierda"
            : $"{pan:0} por ciento derecha";
    }

    private void UpdatePanAccessibleNames()
    {
        _guitar1Pan.AccessibleName = $"Guitarra 1, paneo, {FormatPanForSpeech(_guitar1Pan.Value)}";
        _guitar2Pan.AccessibleName = $"Guitarra 2, paneo, {FormatPanForSpeech(_guitar2Pan.Value)}";
    }

    private void LoadVoicePreferences()
    {
        _voiceOnlyMode.Checked = _audioPreferences.VoiceOnlyMode;
        _voiceEnabled.Checked = _audioPreferences.VoiceEnabled || _audioPreferences.VoiceOnlyMode;
        _voiceSuppressorEnabled.Checked = _audioPreferences.VoiceSuppressorEnabled;
        SetNumeric(_voiceThreshold, _audioPreferences.VoiceThresholdDb);
        SetNumeric(_voiceReduction, _audioPreferences.VoiceReductionDb);
        SetNumeric(_voiceRelease, _audioPreferences.VoiceReleaseMs);
        SetNumeric(_voiceHighPass, _audioPreferences.VoiceHighPassHz);
        SetNumeric(_voiceBass, _audioPreferences.VoiceBassDb);
        SetNumeric(_voiceMid, _audioPreferences.VoiceMidDb);
        SetNumeric(_voiceTreble, _audioPreferences.VoiceTrebleDb);
        SetNumeric(_voiceLevel, _audioPreferences.VoiceLevelPercent);
        SetNumeric(_voiceMonitorLevel, _audioPreferences.VoiceMonitorPercent);
        SetNumeric(_meetGuitarLevel, _audioPreferences.MeetGuitarPercent);
        SetNumeric(_meetVoiceLevel, _audioPreferences.MeetVoicePercent);
        _meetOutputEnabled.Checked = _audioPreferences.MeetOutputEnabled;
        UpdateClassProfileStatus();
        RefreshFocusriteLoopbackToggleState();
    }

    private void LoadGuitar1NamPreferences()
    {
        _guitar1NamEnabled.Checked = false;
        _guitar1NamIncludesCabinet.Checked = _audioPreferences.Guitar1NamIncludesCabinet;
        SetNumeric(_guitar1NamInputTrim, _audioPreferences.Guitar1NamInputTrimDb);
        SetNumeric(_guitar1NamOutputTrim, _audioPreferences.Guitar1NamOutputTrimDb);
        _guitar1NamAutoLevel.Checked = _audioPreferences.Guitar1NamAutoLevelEnabled;
        SetNumeric(_guitar1NamAutoLevelDb, _audioPreferences.Guitar1NamAutoLevelDb);
        RefreshGuitar1NamBankCombo();

        string savedPath = _audioPreferences.Guitar1NamModelPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
        {
            NamLibraryItem? item = NamLibraryStore.FindByPath(_namLibrary, savedPath);
            if (item is not null)
            {
                _guitar1ActiveNamBankId = item.Id;
                RefreshGuitar1NamBankCombo(item.Id);
            }
            TryLoadGuitar1NamModel(savedPath, announce: false, enableAfterLoad: _audioPreferences.Guitar1NamEnabled);
        }
        else
        {
            _guitar1NamPath.Text = string.IsNullOrWhiteSpace(savedPath)
                ? "Guitarra 1: ningún modelo NAM cargado."
                : $"Guitarra 1: modelo NAM guardado no encontrado: {savedPath}";
            _guitar1NamClearButton.Enabled = false;
        }
    }

    private void RefreshGuitar1NamBankCombo(string? selectId = null)
    {
        _loadingGuitar1NamBank = true;
        try
        {
            string? selectedId = (_guitar1NamBankCombo.SelectedItem as NamLibraryItem)?.Id;
            string? wanted = selectId ?? selectedId ?? _guitar1ActiveNamBankId;
            _guitar1NamBankCombo.Items.Clear();
            foreach (NamLibraryItem item in _namLibrary.Items)
                _guitar1NamBankCombo.Items.Add(item);

            int index = -1;
            if (!string.IsNullOrWhiteSpace(wanted))
            {
                for (int i = 0; i < _guitar1NamBankCombo.Items.Count; i++)
                {
                    if (_guitar1NamBankCombo.Items[i] is NamLibraryItem candidate &&
                        string.Equals(candidate.Id, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }
            if (index < 0 && _guitar1NamBankCombo.Items.Count > 0) index = 0;
            _guitar1NamBankCombo.SelectedIndex = index;
        }
        finally
        {
            _loadingGuitar1NamBank = false;
        }

        bool hasItems = _guitar1NamBankCombo.Items.Count > 0;
        _guitar1NamLoadBankButton.Enabled = hasItems;
        _guitar1NamPreviousButton.Enabled = hasItems;
        _guitar1NamNextButton.Enabled = hasItems;
    }

    private void ApplyGuitar1NamBankDefaults(NamLibraryItem item)
    {
        _guitar1NamIncludesCabinet.Checked = item.IncludesCabinet;
        SetNumeric(_guitar1NamInputTrim, item.InputTrimDb);
        SetNumeric(_guitar1NamOutputTrim, item.OutputTrimDb);
        _guitar1NamAutoLevel.Checked = item.AutoLevelEnabled;
        SetNumeric(_guitar1NamAutoLevelDb, item.AutoLevelDb);
    }

    private void LoadSelectedGuitar1NamFromBank()
    {
        if (_guitar1NamBankCombo.SelectedItem is not NamLibraryItem item)
        {
            SetStatus("El Banco NAM de Guitarra 1 está vacío. Cargue una captura NAM primero.", true);
            return;
        }
        LoadGuitar1NamBankItem(item, announce: true);
    }

    private void LoadAdjacentGuitar1NamFromBank(int direction)
    {
        if (_guitar1NamBankCombo.Items.Count == 0)
        {
            SetStatus("El Banco NAM de Guitarra 1 está vacío.", true);
            return;
        }

        int index = _guitar1NamBankCombo.SelectedIndex;
        if (index < 0) index = 0;
        index = (index + (direction < 0 ? -1 : 1) + _guitar1NamBankCombo.Items.Count) % _guitar1NamBankCombo.Items.Count;
        _guitar1NamBankCombo.SelectedIndex = index;
        LoadSelectedGuitar1NamFromBank();
    }

    private void LoadGuitar1NamBankItem(NamLibraryItem item, bool announce)
    {
        if (!File.Exists(item.Path))
        {
            SetStatus($"No se encontró la captura {item.Name} para Guitarra 1. Actualice el Banco NAM.", true);
            return;
        }

        _guitar1ActiveNamBankId = item.Id;
        ApplyGuitar1NamBankDefaults(item);
        RefreshGuitar1NamBankCombo(item.Id);
        TryLoadGuitar1NamModel(item.Path, announce, enableAfterLoad: true, resumeAudioAfterLoad: true);
    }

    private void LoadGuitar1NamModelFromDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Modelos Neural Amp Modeler (*.nam)|*.nam|Todos los archivos (*.*)|*.*",
            Title = "Cargar modelo NAM para Guitarra 1"
        };
        string? currentFolder = Path.GetDirectoryName(_engine.Guitar1Processor.NamModelPath ?? _audioPreferences.Guitar1NamModelPath);
        if (!string.IsNullOrWhiteSpace(currentFolder) && Directory.Exists(currentFolder))
            dialog.InitialDirectory = currentFolder;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            NamLibraryItem item = NamLibraryStore.Import(
                _namLibrary,
                dialog.FileName,
                _guitar1NamIncludesCabinet.Checked,
                (float)_guitar1NamInputTrim.Value,
                (float)_guitar1NamOutputTrim.Value);
            _guitar1ActiveNamBankId = item.Id;
            RefreshNamCategoryFilter();
            RefreshNamBankCombo();
            RefreshGuitar1NamBankCombo(item.Id);
            ApplyGuitar1NamBankDefaults(item);
            TryLoadGuitar1NamModel(item.Path, announce: true, enableAfterLoad: true, resumeAudioAfterLoad: true);
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo incorporar el NAM de Guitarra 1 al banco: {ex.Message}", true);
        }
    }

    private void TryLoadGuitar1NamModel(string path, bool announce, bool enableAfterLoad, bool resumeAudioAfterLoad = false)
    {
        bool wasRunning = resumeAudioAfterLoad && (_audioRequested || _engine.HasActiveSession);
        StopAudioForNamChange();
        try
        {
            var info = _engine.Guitar1Processor.LoadNamModel(path);
            _guitar1NamPath.Text = path;
            _audioPreferences.Guitar1NamModelPath = path;
            _guitar1NamClearButton.Enabled = true;
            _guitar1NamEnabled.Checked = enableAfterLoad;
            NamLibraryItem? item = NamLibraryStore.FindByPath(_namLibrary, path);
            if (item is not null)
            {
                _guitar1ActiveNamBankId = item.Id;
                RefreshGuitar1NamBankCombo(item.Id);
            }
            UpdateParameters();
            SaveAudioPreferences();
            if (wasRunning)
            {
                ToggleAudio();
                if (_engine.IsRunning && (_metronomeEnabled.Checked || _drumsEnabled.Checked || _backingBassEnabled.Checked || _pianoEnabled.Checked))
                    _engine.RequestMetronomeReset();
            }
            RefreshDualQuickNamSelector(_guitar1ActiveNamBankId);
            if (announce)
            {
                string simultaneous = _namEnabled.Checked && _engine.Processor.HasNamModel
                    ? " Dos NAM quedan preparados para procesar simultáneamente."
                    : string.Empty;
                SetStatus(wasRunning
                    ? $"Guitarra 1, NAM cargado: {info.FileName}. Audio reiniciado. Nivel seguro NAM menos nueve decibeles activo.{simultaneous}"
                    : $"Guitarra 1, NAM cargado: {info.FileName}. Nivel seguro NAM menos nueve decibeles activo.{simultaneous}");
            }
        }
        catch (Exception ex)
        {
            _guitar1NamEnabled.Checked = false;
            _guitar1NamClearButton.Enabled = _engine.Guitar1Processor.HasNamModel;
            _guitar1NamPath.Text = $"Guitarra 1: no se pudo cargar NAM: {ex.Message}";
            UpdateParameters();
            if (wasRunning && !_engine.IsRunning) ToggleAudio();
            if (announce) SetStatus($"No se pudo cargar el modelo NAM de Guitarra 1: {ex.Message}", true);
        }
    }

    private void ClearGuitar1NamModel()
    {
        bool wasRunning = _audioRequested || _engine.HasActiveSession;
        StopAudioForNamChange();
        _guitar1NamEnabled.Checked = false;
        _engine.Guitar1Processor.ClearNamModel();
        _audioPreferences.Guitar1NamModelPath = string.Empty;
        _guitar1ActiveNamBankId = null;
        _guitar1NamPath.Text = "Guitarra 1: ningún modelo NAM cargado.";
        _guitar1NamClearButton.Enabled = false;
        UpdateParameters();
        SaveAudioPreferences();
        if (wasRunning) ToggleAudio();
        RefreshDualQuickNamSelector();
        SetStatus("NAM de Guitarra 1 quitado. Guitarra 1 vuelve a usar su amplificador interno; Guitarra 2 no fue modificada.");
    }

    private void ToggleGuitar1NamQuick()
    {
        if (_engine.Guitar1Processor.HasNamModel)
        {
            _guitar1NamEnabled.Checked = !_guitar1NamEnabled.Checked;
            UpdateParameters();
            SaveAudioPreferences();
            string model = Path.GetFileName(_engine.Guitar1Processor.NamModelPath ?? string.Empty);
            SetStatus(_guitar1NamEnabled.Checked
                ? $"Guitarra 1, NAM activado: {model}."
                : $"Guitarra 1, NAM desactivado. Se usa su amplificador interno; {model} queda preparado.");
            return;
        }

        if (_guitar1NamBankCombo.Items.Count > 0)
        {
            if (_guitar1NamBankCombo.SelectedIndex < 0) _guitar1NamBankCombo.SelectedIndex = 0;
            LoadSelectedGuitar1NamFromBank();
            return;
        }

        string savedPath = _audioPreferences.Guitar1NamModelPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
        {
            TryLoadGuitar1NamModel(savedPath, announce: true, enableAfterLoad: true, resumeAudioAfterLoad: true);
            return;
        }

        SetStatus("Guitarra 1 no tiene capturas NAM disponibles. Cargue una captura desde Alt+O o desde la sección NAM.", true);
    }

    private void LoadNamPreferences()
    {
        _namEnabled.Checked = false;
        _namIncludesCabinet.Checked = _audioPreferences.NamIncludesCabinet;
        SetNumeric(_namInputTrim, _audioPreferences.NamInputTrimDb);
        SetNumeric(_namOutputTrim, _audioPreferences.NamOutputTrimDb);
        RefreshNamBankCombo();

        string savedPath = _audioPreferences.NamModelPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
        {
            try
            {
                NamLibraryItem item = NamLibraryStore.FindByPath(_namLibrary, savedPath)
                    ?? NamLibraryStore.Import(_namLibrary, savedPath, _audioPreferences.NamIncludesCabinet, _audioPreferences.NamInputTrimDb, _audioPreferences.NamOutputTrimDb);
                ApplyNamBankSettings(item);
                SelectNamBankItem(item.Id);
                TryLoadNamModel(item.Path, announce: false, enableAfterLoad: _audioPreferences.NamEnabled);
            }
            catch
            {
                TryLoadNamModel(savedPath, announce: false, enableAfterLoad: _audioPreferences.NamEnabled);
            }
        }
        else
        {
            _namPath.Text = string.IsNullOrWhiteSpace(savedPath)
                ? "Ningún modelo NAM cargado."
                : $"Modelo guardado no encontrado: {savedPath}";
            RefreshNamStatus();
        }
    }

    private void LoadMetronomePreferences()
    {
        _metronomeEnabled.Checked = _audioPreferences.MetronomeEnabled;
        SetNumeric(_metronomeBpm, _audioPreferences.MetronomeBpm);
        SetNumeric(_metronomeVolume, _audioPreferences.MetronomeVolumePercent);
        _metronomeAccent.Checked = _audioPreferences.MetronomeAccentFirstBeat;
        _metronomeMeter.SelectedIndex = _audioPreferences.MetronomeBeatsPerBar switch
        {
            2 => 0,
            3 => 1,
            6 => 3,
            _ => 2
        };
        _drumsEnabled.Checked = _audioPreferences.DrumsEnabled;
        _drumPattern.SelectedIndex = Math.Clamp(_audioPreferences.DrumPattern, 0, 5);
        SetNumeric(_drumVolume, _audioPreferences.DrumVolumePercent);
        _backingBassEnabled.Checked = _audioPreferences.BackingBassEnabled;
        _backingBassKey.SelectedIndex = Math.Clamp(_audioPreferences.BackingBassKey, 0, 11);
        _backingBassMode.SelectedIndex = _audioPreferences.BackingBassMinor ? 1 : 0;
        _backingBassLine.SelectedIndex = Math.Clamp(_audioPreferences.BackingBassLine, 0, 4);
        SetNumeric(_backingBassVolume, _audioPreferences.BackingBassVolumePercent);
        _pianoEnabled.Checked = _audioPreferences.PianoEnabled;
        _pianoSound.SelectedIndex = Math.Clamp(_audioPreferences.PianoSound, 0, 5);
        _pianoKey.SelectedIndex = Math.Clamp(_audioPreferences.PianoKey, 0, 23);
        _pianoProgression.SelectedIndex = Math.Clamp(_audioPreferences.PianoProgression, 0, 5);
        _pianoCustomProgression.Text = string.IsNullOrWhiteSpace(_audioPreferences.PianoCustomProgression) ? "I, V, vi, IV" : _audioPreferences.PianoCustomProgression;
        _pianoStyle.SelectedIndex = Math.Clamp(_audioPreferences.PianoStyle, 0, 8);
        SetNumeric(_pianoVolume, _audioPreferences.PianoVolumePercent);
        _loopSourceCombo.SelectedIndex = Math.Clamp(_audioPreferences.LoopCaptureSource, 0, 2);
        _engine.ConfigureLoopCaptureSource(SelectedLoopCaptureSource);
        _loopSyncTempo.Checked = _audioPreferences.LoopSyncTempo;
        _loopBars.SelectedIndex = _audioPreferences.LoopBars switch { 1 => 0, 2 => 1, 8 => 3, _ => 2 };
        _loopBars.Enabled = _loopSyncTempo.Checked;
        UpdateLooperUi();
    }

    private void SaveAudioPreferences()
    {
        RememberCurrentChannelEq(force: true);
        _audioPreferences.BufferSize = SelectedBufferSize;
        _audioPreferences.AsioDriverName = _driverCombo.SelectedItem?.ToString() ?? _audioPreferences.AsioDriverName ?? string.Empty;
        if (_inputCombo.SelectedIndex >= 0) _audioPreferences.GuitarInputIndex = _inputCombo.SelectedIndex;
        _audioPreferences.MasterVolumePercent = (float)_masterVolume.Value;
        _audioPreferences.TunerGuitarIndex = SelectedTunerGuitarIndex;

        _audioPreferences.CleanBass = _channelEq[0, 0];
        _audioPreferences.CleanMiddle = _channelEq[0, 1];
        _audioPreferences.CleanTreble = _channelEq[0, 2];
        _audioPreferences.CleanPresence = _channelEq[0, 3];

        _audioPreferences.CrunchBass = _channelEq[1, 0];
        _audioPreferences.CrunchMiddle = _channelEq[1, 1];
        _audioPreferences.CrunchTreble = _channelEq[1, 2];
        _audioPreferences.CrunchPresence = _channelEq[1, 3];

        _audioPreferences.LeadBass = _channelEq[2, 0];
        _audioPreferences.LeadMiddle = _channelEq[2, 1];
        _audioPreferences.LeadTreble = _channelEq[2, 2];
        _audioPreferences.LeadPresence = _channelEq[2, 3];

        _audioPreferences.VoiceEnabled = _voiceEnabled.Checked;
        _audioPreferences.VoiceOnlyMode = _voiceOnlyMode.Checked;
        _audioPreferences.VoiceSuppressorEnabled = _voiceSuppressorEnabled.Checked;
        _audioPreferences.VoiceThresholdDb = (float)_voiceThreshold.Value;
        _audioPreferences.VoiceReductionDb = (float)_voiceReduction.Value;
        _audioPreferences.VoiceReleaseMs = (float)_voiceRelease.Value;
        _audioPreferences.VoiceHighPassHz = (float)_voiceHighPass.Value;
        _audioPreferences.VoiceBassDb = (float)_voiceBass.Value;
        _audioPreferences.VoiceMidDb = (float)_voiceMid.Value;
        _audioPreferences.VoiceTrebleDb = (float)_voiceTreble.Value;
        _audioPreferences.VoiceLevelPercent = (float)_voiceLevel.Value;
        _audioPreferences.VoiceMonitorPercent = (float)_voiceMonitorLevel.Value;
        if (!_voiceOnlyMode.Checked)
            _audioPreferences.GuitarClassMonitorPercent = (float)_voiceMonitorLevel.Value;
        _audioPreferences.MeetGuitarPercent = (float)_meetGuitarLevel.Value;
        _audioPreferences.MeetVoicePercent = (float)_meetVoiceLevel.Value;
        _audioPreferences.MeetOutputEnabled = _meetOutputEnabled.Checked;
        _audioPreferences.MeetOutputDeviceId = _meetOutputCombo.SelectedIndex >= 0 && _meetOutputCombo.SelectedIndex < _meetOutputDevices.Count
            ? _meetOutputDevices[_meetOutputCombo.SelectedIndex].Id : _audioPreferences.MeetOutputDeviceId;

        if (_dualEffectMemoriesInitialized && !_loadingDualEffectMemory && !_loadingScene)
            StoreVisibleEffectsForGuitar(Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1));

        _audioPreferences.TwoGuitarMode = _twoGuitarMode.Checked;
        _audioPreferences.Guitar1ProcessingEnabled = _guitar1ProcessingEnabled.Checked;
        _audioPreferences.Guitar1UseRigEffects = _guitar1UseRigEffects.Checked;
        _audioPreferences.Guitar1AmpChannel = Math.Max(0, _guitar1AmpCombo.SelectedIndex);
        _audioPreferences.Guitar1Gain = (float)_guitar1Gain.Value;
        _audioPreferences.Guitar1OutputPercent = (float)_guitar1Output.Value;
        _audioPreferences.Guitar1MixPercent = (float)_guitar1Mix.Value;
        _audioPreferences.Guitar1PanPercent = (float)_guitar1Pan.Value;
        _audioPreferences.Guitar1Muted = _guitar1Mute.Checked;
        _audioPreferences.Guitar2MixPercent = (float)_guitar2Mix.Value;
        _audioPreferences.Guitar2PanPercent = (float)_guitar2Pan.Value;
        _audioPreferences.Guitar2Muted = _guitar2Mute.Checked;
        _audioPreferences.Guitar1EffectMemory = _guitar1EffectMemory;
        _audioPreferences.Guitar2EffectMemory = _guitar2EffectMemory;
        _audioPreferences.Guitar1IrPath = _guitar1LoadedIrPath ?? string.Empty;
        _audioPreferences.Guitar1NamModelPath = _engine.Guitar1Processor.NamModelPath ?? _audioPreferences.Guitar1NamModelPath ?? string.Empty;
        _audioPreferences.Guitar1NamEnabled = _guitar1NamEnabled.Checked && _engine.Guitar1Processor.HasNamModel;
        _audioPreferences.Guitar1NamIncludesCabinet = _guitar1NamIncludesCabinet.Checked;
        _audioPreferences.Guitar1NamInputTrimDb = (float)_guitar1NamInputTrim.Value;
        _audioPreferences.Guitar1NamOutputTrimDb = (float)_guitar1NamOutputTrim.Value;
        _audioPreferences.Guitar1NamAutoLevelEnabled = _guitar1NamAutoLevel.Checked;
        _audioPreferences.Guitar1NamAutoLevelDb = (float)_guitar1NamAutoLevelDb.Value;

        _audioPreferences.NamModelPath = _engine.Processor.NamModelPath ?? _audioPreferences.NamModelPath ?? string.Empty;
        _audioPreferences.NamEnabled = _namEnabled.Checked && _engine.Processor.HasNamModel;
        _audioPreferences.NamIncludesCabinet = _namIncludesCabinet.Checked;
        _audioPreferences.NamInputTrimDb = (float)_namInputTrim.Value;
        _audioPreferences.NamOutputTrimDb = (float)_namOutputTrim.Value;

        _audioPreferences.MetronomeEnabled = _metronomeEnabled.Checked;
        _audioPreferences.MetronomeBpm = (float)_metronomeBpm.Value;
        _audioPreferences.MetronomeBeatsPerBar = SelectedMetronomeBeatsPerBar;
        _audioPreferences.MetronomeAccentFirstBeat = _metronomeAccent.Checked;
        _audioPreferences.MetronomeVolumePercent = (float)_metronomeVolume.Value;
        _audioPreferences.DrumsEnabled = _drumsEnabled.Checked;
        _audioPreferences.DrumPattern = Math.Clamp(_drumPattern.SelectedIndex, 0, 5);
        _audioPreferences.DrumVolumePercent = (float)_drumVolume.Value;
        _audioPreferences.BackingBassEnabled = _backingBassEnabled.Checked;
        _audioPreferences.BackingBassKey = Math.Clamp(_backingBassKey.SelectedIndex, 0, 11);
        _audioPreferences.BackingBassMinor = _backingBassMode.SelectedIndex == 1;
        _audioPreferences.BackingBassLine = Math.Clamp(_backingBassLine.SelectedIndex, 0, 4);
        _audioPreferences.BackingBassVolumePercent = (float)_backingBassVolume.Value;
        _audioPreferences.PianoEnabled = _pianoEnabled.Checked;
        _audioPreferences.PianoSound = Math.Clamp(_pianoSound.SelectedIndex, 0, 5);
        _audioPreferences.PianoKey = Math.Clamp(_pianoKey.SelectedIndex, 0, 23);
        _audioPreferences.PianoProgression = Math.Clamp(_pianoProgression.SelectedIndex, 0, 5);
        _audioPreferences.PianoCustomProgression = _pianoCustomProgression.Text.Trim();
        _audioPreferences.PianoStyle = Math.Clamp(_pianoStyle.SelectedIndex, 0, 8);
        _audioPreferences.PianoVolumePercent = (float)_pianoVolume.Value;
        _audioPreferences.LoopSyncTempo = _loopSyncTempo.Checked;
        _audioPreferences.LoopBars = SelectedLoopBars;
        _audioPreferences.LoopCaptureSource = SelectedLoopCaptureSource;

        UpdateActiveNamBankSettings();
        try { AudioSettingsStore.Save(_audioPreferences); }
        catch { }
    }

    private void ApplySelectedBuffer()
    {
        SaveAudioPreferences();
        if (_audioRequested || _engine.HasActiveSession)
        {
            SavePracticeRecordingBeforeAudioStop();
            _audioRequested = false;
            _engine.Stop();
            SetRunningState(false);
            UpdateActualBufferLabel();
        }

        OpenAsioPanel();
        SetStatus($"Referencia elegida: {SelectedBufferSize}. El selector de Amp Accessible no cambia el driver. Configure ese valor en el panel ASIO de la interfaz seleccionada, cierre el panel y pulse F4; la aplicación informará el buffer efectivo.");
    }

    private void UpdateActualBufferLabel()
    {
        int actual = _engine.ActualBufferSize;
        string text;
        if (actual <= 0)
        {
            text = $"Buffer efectivo: todavía no iniciado. Referencia seleccionada: {SelectedBufferSize}.";
        }
        else if (actual == SelectedBufferSize)
        {
            text = $"Buffer efectivo: {actual} muestras; coincide con la referencia.";
        }
        else
        {
            text = $"Buffer efectivo: {actual} muestras. La referencia {SelectedBufferSize} no fue aplicada por el driver. Cambie el valor real en el panel ASIO de la interfaz seleccionada.";
        }

        _actualBufferLabel.Text = text;
        _actualBufferLabel.AccessibleName = text;
        _actualBufferLabel.AccessibleDescription = text;
    }

    private void UpdateAudioDiagnostics()
    {
        if (!_engine.HasActiveSession)
        {
            _diagnosticsLabel.Text = "Diagnóstico: audio detenido.";
            _diagnosticsLabel.AccessibleName = "Diagnóstico de audio: detenido";
            UpdateActualBufferLabel();
            UpdateAudioDiagnosticPanel();
            return;
        }

        long memoryMb = GC.GetTotalMemory(false) / (1024 * 1024);
        string text = $"Buffer de referencia {SelectedBufferSize}, efectivo del driver {_engine.ActualBufferSize}; memoria administrada {memoryMb} MB; " +
                      $"carga DSP actual {_engine.LastDspLoadPercent} por ciento, máxima {_engine.MaxDspLoadPercent} por ciento; " +
                      $"máxima por canal: limpio {_engine.MaxDspLoadClean}, crunch {_engine.MaxDspLoadCrunch}, lead {_engine.MaxDspLoadLead} por ciento; " +
                      $"plazos DSP excedidos {_engine.OutputUnderrunCount}; errores totales {_engine.TotalAudioErrorCount}: " +
                      $"buffer {_engine.BufferErrorCount}, entrada {_engine.InputReadErrorCount}, DSP {_engine.DspErrorCount}, salida {_engine.OutputWriteErrorCount}; " +
                      $"resets del driver {_engine.DriverResetRequestCount}; cambios de buffer {_engine.BufferChangeCount}; bloques ASIO {_engine.CallbackCount}.";
        _diagnosticsLabel.Text = text;
        // No se cambia el nombre accesible cuatro veces por segundo: JAWS puede gastar
        // tiempo procesando notificaciones que no fueron solicitadas.
        _diagnosticsLabel.AccessibleName = "Diagnóstico de audio";
        _diagnosticsLabel.AccessibleDescription = text;
        UpdateAudioDiagnosticPanel();
    }

    private static string FormatPeakDb(float peak)
    {
        if (peak <= 0.000001f) return "sin señal";
        return $"{Math.Max(-120.0, 20.0 * Math.Log10(peak)):0.0} dB";
    }

    private static void AppendHardPanDiagnostic(StringBuilder text, string guitarName, float panPercent, float leftPeak, float rightPeak)
    {
        if (MathF.Abs(panPercent) < 99.5f) return;

        bool hardLeft = panPercent < 0f;
        float wantedPeak = hardLeft ? leftPeak : rightPeak;
        float oppositePeak = hardLeft ? rightPeak : leftPeak;
        string wantedSide = hardLeft ? "izquierda" : "derecha";
        string oppositeSide = hardLeft ? "derecha" : "izquierda";

        if (wantedPeak <= 0.000001f)
        {
            text.AppendLine($"Prueba hard-pan {guitarName}: aún no se midió señal útil después del último cambio; toque esa guitarra y vuelva a actualizar Alt+D.");
            return;
        }

        if (oppositePeak <= 0.000001f)
        {
            text.AppendLine($"Prueba hard-pan {guitarName}: CORRECTO dentro de Amp Accessible; sale señal por {wantedSide} y cero digital por {oppositeSide}. Si todavía se oye en ambos auriculares, la duplicación ocurre después de Amp Accessible: revisar Direct Monitor / mezcla de hardware de Focusrite Control 2.");
        }
        else
        {
            text.AppendLine($"Prueba hard-pan {guitarName}: ADVERTENCIA; Amp Accessible todavía mide {FormatPeakDb(oppositePeak)} por {oppositeSide}. Esta fuga está dentro de la ruta digital y debe corregirse en el programa.");
        }
    }

    private string GetAudioDiagnosticHealth()
    {
        if (!_engine.HasActiveSession) return "Audio detenido. No hay una sesión ASIO activa.";
        if (_engine.CallbackAgeMilliseconds >= 1000)
            return $"CRÍTICO: el callback ASIO lleva {_engine.CallbackAgeMilliseconds:0} milisegundos sin avanzar.";
        if (_engine.TotalAudioErrorCount > 0 || _engine.DriverResetRequestCount > 0)
            return $"ATENCIÓN: se detectaron {_engine.TotalAudioErrorCount} errores de audio y {_engine.DriverResetRequestCount} solicitudes de reset del driver en esta sesión.";
        if (_engine.OutputUnderrunCount == 1 && _engine.TotalAudioErrorCount == 0 && _engine.CallbackCount >= 10000)
            return $"CORRECTO: audio estable; se registró 1 pico DSP aislado en {_engine.CallbackCount} callbacks. Carga DSP actual {_engine.LastDspLoadPercent} por ciento, máxima {_engine.MaxDspLoadPercent} por ciento.";
        if (_engine.OutputUnderrunCount > 0 || _engine.MaxDspLoadPercent >= 90)
            return $"ADVERTENCIA: hubo {_engine.OutputUnderrunCount} plazos DSP excedidos; carga DSP máxima {_engine.MaxDspLoadPercent} por ciento.";
        return $"CORRECTO: audio estable. Carga DSP actual {_engine.LastDspLoadPercent} por ciento, máxima {_engine.MaxDspLoadPercent} por ciento, sin errores detectados.";
    }

    private string BuildAudioDiagnosticReport()
    {
        var text = new StringBuilder(2048);
        string driver = _engine.SessionDriverName ?? _driverCombo.SelectedItem?.ToString() ?? "no seleccionado";
        string input = _inputCombo.SelectedItem?.ToString() ?? (_engine.SessionInputChannelIndex >= 0 ? $"Entrada {_engine.SessionInputChannelIndex + 1}" : "no seleccionada");
        int actualBuffer = _engine.ActualBufferSize;
        double bufferMs = actualBuffer > 0 ? actualBuffer * 1000.0 / AudioEngine.SampleRate : 0;
        int latencySamples = _engine.PlaybackLatencySamples;
        double latencyMs = latencySamples > 0 ? latencySamples * 1000.0 / AudioEngine.SampleRate : 0;
        bool namNative = _engine.Processor.IsNamNativeEngineAvailable(out string namNativeDescription);
        string? namPath = _engine.Processor.NamModelPath;
        string namModel = string.IsNullOrWhiteSpace(namPath) ? "ninguno" : Path.GetFileName(namPath);
        string irA = string.IsNullOrWhiteSpace(_loadedIrPath) ? "gabinete interno" : Path.GetFileName(_loadedIrPath);
        string irB = string.IsNullOrWhiteSpace(_loadedIrPathB) ? "sin IR B" : Path.GetFileName(_loadedIrPathB);
        long managedMb = GC.GetTotalMemory(false) / (1024 * 1024);
        long processMb = Environment.WorkingSet / (1024 * 1024);

        text.AppendLine(AppInfo.DiagnosticTitle);
        text.AppendLine($"Identificación de compilación: {AppInfo.Version}; {AppInfo.BuildName}");
        text.AppendLine($"Estado general: {GetAudioDiagnosticHealth()}");
        text.AppendLine($"Audio: {(_engine.IsRunning ? "activo" : _engine.HasActiveSession ? "sesión abierta" : "detenido")}");
        text.AppendLine($"Volumen master de Amp Accessible: {_masterVolume.Value:0} %; salida final medida después del master {FormatPeakDb(_engine.MasterOutputPeak)}; las grabaciones internas se conservan antes del master");
        text.AppendLine($"Controlador ASIO: {driver}");
        text.AppendLine(_twoGuitarMode.Checked
            ? "Entradas de guitarras: Guitarra 1 = Input 1; Guitarra 2 = Input 2"
            : _voiceOnlyMode.Checked
                ? "Micrófono activo: entrada 1; ruta de guitarra ignorada"
                : $"Entrada de guitarra: {input}");
        if (_engine.SessionDriverInputChannels > 0 || _engine.SessionDriverOutputChannels > 0)
            text.AppendLine($"Canales del driver: {_engine.SessionDriverInputChannels} entradas, {_engine.SessionDriverOutputChannels} salidas");
        text.AppendLine($"Frecuencia de trabajo: {AudioEngine.SampleRate} Hz");
        text.AppendLine($"Buffer de referencia: {SelectedBufferSize} muestras");
        text.AppendLine(actualBuffer > 0
            ? $"Buffer efectivo: {actualBuffer} muestras, {bufferMs:0.00} ms"
            : "Buffer efectivo: no disponible porque el audio está detenido");
        if (latencySamples > 0) text.AppendLine($"Latencia de salida informada por ASIO: {latencySamples} muestras, {latencyMs:0.00} ms");
        text.AppendLine($"Callbacks ASIO procesados: {_engine.CallbackCount}");
        text.AppendLine($"Edad del último callback: {_engine.CallbackAgeMilliseconds:0.0} ms");
        text.AppendLine($"Carga DSP: actual {_engine.LastDspLoadPercent} %, máxima {_engine.MaxDspLoadPercent} %");
        text.AppendLine($"Máximos por canal: limpio {_engine.MaxDspLoadClean} %, crunch {_engine.MaxDspLoadCrunch} %, lead {_engine.MaxDspLoadLead} %");
        text.AppendLine($"Plazos DSP excedidos: {_engine.OutputUnderrunCount}");
        text.AppendLine($"Errores de audio: total {_engine.TotalAudioErrorCount}; buffer {_engine.BufferErrorCount}; entrada {_engine.InputReadErrorCount}; DSP {_engine.DspErrorCount}; salida {_engine.OutputWriteErrorCount}");
        text.AppendLine($"Solicitudes de reset ASIO: {_engine.DriverResetRequestCount}; cambios de buffer: {_engine.BufferChangeCount}");
        text.AppendLine($"NAM nativo: {(namNative ? "disponible" : "no disponible")}. {namNativeDescription}");
        text.AppendLine($"NAM de Guitarra 2 cargado: {namModel}; procesamiento NAM {(_namEnabled.Checked && _engine.Processor.HasNamModel ? "activo" : "inactivo")}");
        if (_engine.Processor.HasNamModel)
            text.AppendLine($"Nivel NAM Guitarra 2: recomendado entrada {_engine.Processor.NamRecommendedInputDb:+0.0;-0.0;0.0} dB; recomendado salida original {_engine.Processor.NamRecommendedOutputDb:+0.0;-0.0;0.0} dB; recomendado salida aplicado {_engine.Processor.NamAppliedRecommendedOutputDb:+0.0;-0.0;0.0} dB; margen seguro {_engine.Processor.NamSafetyOutputPadDb:+0.0;-0.0;0.0} dB; trim usuario {_namOutputTrim.Value:+0.0;-0.0;0.0} dB.");
        if (!string.IsNullOrWhiteSpace(_engine.Processor.NamLastError)) text.AppendLine($"Último error NAM: {_engine.Processor.NamLastError}");
        text.AppendLine($"Gabinete / IR A: {irA}; IR B: {irB}");
        if (_irBrowserFiles.Count > 0)
        {
            string irBrowserPosition = _irBrowserIndex >= 0
                ? $"IR {_irBrowserIndex + 1} de {_irBrowserFiles.Count}: {Path.GetFileName(_irBrowserFiles[_irBrowserIndex])}"
                : $"{_irBrowserFiles.Count} IR indexados; ninguno seleccionado todavía";
            text.AppendLine($"Explorador de carpeta IR: {_audioPreferences.IrBrowserFolder}; {irBrowserPosition}; Control+F6 siguiente, Control+F7 anterior");
        }
        else
        {
            text.AppendLine("Explorador de carpeta IR: sin carpeta preparada");
        }
        text.AppendLine("Cambio de tipo de reverb: limpieza automática de la cola anterior y fundido corto sin clics; no es necesario apagar y volver a activar el efecto");
        string tunerTargetDiagnostic = _twoGuitarMode.Checked ? SelectedTunerGuitarName : "rig principal / entrada seleccionada";
        text.AppendLine($"Afinador: {(_tunerEnabled.Checked ? "ACTIVO" : "inactivo")}; fuente = {tunerTargetDiagnostic}; silenciar sólo fuente afinada {(_tunerMuteOutput.Checked ? "sí" : "no")}; guía sonora {(_tunerSoundGuide.Checked ? "activa" : "inactiva")}; F9 leer, Control+F9 cambiar guitarra en modo dual.");
        string wahMode = _autoWahModeCombo.SelectedIndex == 1 ? "MANUAL / PEDAL DE EXPRESIÓN" : "AUTO POR DINÁMICA";
        string wahCharacter = _autoWahCharacterCombo.SelectedIndex switch
        {
            1 => "Vai / Bad Horsie",
            2 => "Satriani / Big Bad",
            _ => "Clásico"
        };
        text.AppendLine($"Wah: {(_autoWahEnabled.Checked ? "ACTIVO" : "inactivo")}; modo {wahMode}; carácter {wahCharacter}; sensibilidad {_autoWahSensitivity.Value:0.0}; rango {_autoWahRange.Value:0.0}; resonancia {_autoWahResonance.Value:0.0}; posición manual {_autoWahManualPosition.Value:0} %; preparado para MIDI Learn CC continuo");
        if (_autoWahCharacterCombo.SelectedIndex == 1)
            text.AppendLine("Wah Vai 2.41.26: barrido revisado con apertura más temprana hacia medios/agudos, menos componente grave en la mezcla y presencia moderada; Clásico y Satriani permanecen sin cambios.");
        if (_reverbEnabled.Checked && _reverbCharacterCombo.SelectedIndex == 0)
            text.AppendLine("Reverb Spring 2.41.35: primer rebote conservado, graves sostenidos fuera del detector y resonancias más cortas para que la cola no quede cantando una nota metálica fija. Plate, Room, Hall, Church, Shimmer y Cathedral permanecen sin cambios.");
        if (_twoGuitarMode.Checked)
        {
            string g1Amp = _guitar1AmpCombo.SelectedItem?.ToString() ?? "amplificador interno";
            string g1Rig = _guitar1NamEnabled.Checked && _engine.Guitar1Processor.HasNamModel
                ? $"NAM {Path.GetFileName(_engine.Guitar1Processor.NamModelPath)}"
                : g1Amp;
            text.AppendLine($"Modo Dos Guitarras: ACTIVO; Guitarra 1 Input 1 = {g1Rig}, ganancia {_guitar1Gain.Value:0.0}, volumen DSP {_guitar1Output.Value:0} %, mezcla {_guitar1Mix.Value:0} %, paneo {FormatPanForSpeech(_guitar1Pan.Value)}, mute {(_guitar1Mute.Checked ? "sí" : "no")}; Guitarra 2 Input 2 = rig principal/NAM, mezcla {_guitar2Mix.Value:0} %, paneo {FormatPanForSpeech(_guitar2Pan.Value)}, mute {(_guitar2Mute.Checked ? "sí" : "no")}");
            text.AppendLine("Paneo estéreo 2.41.30: independiente por guitarra, post-DSP y pre-mezcla; centro conserva exactamente L/R de la cadena; en -100/+100 se fuerza a cero digital exacto el canal contrario; acompañamiento global permanece centrado.");
            text.AppendLine($"Procesar Guitarra 1 / Input 1: {(_guitar1ProcessingEnabled.Checked ? "ACTIVO; 100 % DSP, 0 % señal seca interna" : "DESACTIVADO; bypass crudo solicitado por el usuario")}");
            text.AppendLine($"Efectos de Guitarra 1: {(_guitar1UseRigEffects.Checked ? "ACTIVOS; memoria independiente" : "DESACTIVADOS; cadena mínima")}");
            string g1NamModel = _engine.Guitar1Processor.HasNamModel ? Path.GetFileName(_engine.Guitar1Processor.NamModelPath) : "ninguno";
            text.AppendLine($"NAM de Guitarra 1: {g1NamModel}; procesamiento {(_guitar1NamEnabled.Checked && _engine.Guitar1Processor.HasNamModel ? "ACTIVO" : "inactivo")}; incluye gabinete {(_guitar1NamIncludesCabinet.Checked ? "sí" : "no")}; trim entrada {_guitar1NamInputTrim.Value:+0.0;-0.0;0.0} dB; trim salida {_guitar1NamOutputTrim.Value:+0.0;-0.0;0.0} dB; Control+Alt+F5 on/off, Control+Alt+F6 siguiente, Control+Alt+F7 anterior");
            if (_engine.Guitar1Processor.HasNamModel)
                text.AppendLine($"Nivel NAM Guitarra 1: recomendado entrada {_engine.Guitar1Processor.NamRecommendedInputDb:+0.0;-0.0;0.0} dB; recomendado salida original {_engine.Guitar1Processor.NamRecommendedOutputDb:+0.0;-0.0;0.0} dB; recomendado salida aplicado {_engine.Guitar1Processor.NamAppliedRecommendedOutputDb:+0.0;-0.0;0.0} dB; margen seguro {_engine.Guitar1Processor.NamSafetyOutputPadDb:+0.0;-0.0;0.0} dB; trim usuario {_guitar1NamOutputTrim.Value:+0.0;-0.0;0.0} dB.");
            if (!string.IsNullOrWhiteSpace(_engine.Guitar1Processor.NamLastError)) text.AppendLine($"Último error NAM de Guitarra 1: {_engine.Guitar1Processor.NamLastError}");
            string g1IrState = string.IsNullOrWhiteSpace(_guitar1LoadedIrPath) || !_engine.Guitar1Processor.HasExternalImpulse
                ? "gabinete interno"
                : Path.GetFileName(_guitar1LoadedIrPath);
            string g1IrBrowser = _guitar1IrBrowserFiles.Count > 0 && _guitar1IrBrowserIndex >= 0
                ? $"IR {_guitar1IrBrowserIndex + 1} de {_guitar1IrBrowserFiles.Count}: {Path.GetFileName(_guitar1IrBrowserFiles[_guitar1IrBrowserIndex])}"
                : (_guitar1IrBrowserFiles.Count > 0 ? $"{_guitar1IrBrowserFiles.Count} IR disponibles" : "sin carpeta preparada");
            text.AppendLine($"IR de Guitarra 1: {g1IrState}; explorador {g1IrBrowser}; Control+Shift+F6 siguiente, Control+Shift+F7 anterior");
            int g1BankCount = _dualGuitarBankLibrary.Guitar1Banks.Count;
            int g2BankCount = _dualGuitarBankLibrary.Guitar2Banks.Count;
            string selectedBankGuitar = _dualEditGuitarCombo.SelectedIndex == 1 ? "Guitarra 2" : "Guitarra 1";
            string selectedBankName = _dualBankCombo.SelectedIndex >= 0 ? _dualBankCombo.SelectedItem?.ToString() ?? "sin nombre" : "ninguno";
            string selectedFactoryBank = _dualFactoryBankCombo.SelectedIndex >= 0 ? _dualFactoryBankCombo.SelectedItem?.ToString() ?? "sin nombre" : "ninguno";
            text.AppendLine($"Bancos por guitarra: fábrica disponibles = {FactoryPresetBank.Presets.Count} para cada guitarra; fábrica seleccionada = {selectedFactoryBank}; personales Guitarra 1 = {g1BankCount}; personales Guitarra 2 = {g2BankCount}; biblioteca personal visible = {selectedBankGuitar}; banco personal seleccionado = {selectedBankName}");
            text.AppendLine("Bancos 2.41.32: F31 Steve Vai Legacy Clean; F32 Steve Vai Legacy Lead.");
            string pianoProgressionDetail = _pianoProgression.SelectedIndex == 5 ? $"; grados {_pianoCustomProgression.Text}" : string.Empty;
            text.AppendLine($"Teclas 2.41.58: {(_pianoEnabled.Checked ? "ACTIVO" : "inactivo")}; sonido {_pianoSound.SelectedItem}; tonalidad {_pianoKey.SelectedItem}; progresión {_pianoProgression.SelectedItem}{pianoProgressionDetail}; estilo {_pianoStyle.SelectedItem}; volumen {_pianoVolume.Value:0} %; Control+F12 on/off.");
            text.AppendLine($"MicroPitch 80s: {(_microPitchEnabled.Checked ? "ACTIVO" : "inactivo")}; detune {_microPitchDetune.Value:0} cents; delay {_microPitchDelay.Value:0} ms; mix {_microPitchMix.Value:0} %.");
            text.AppendLine($"Seguimiento de guitarra del acompañamiento: {(_backingBandFollowGuitarArmed ? "ARMADO por F12; arranca con guitarra y para tras silencio" : "inactivo; controles individuales continuos")}.");
            string dualSceneName = _dualSceneCombo.SelectedIndex >= 0 ? _dualSceneCombo.SelectedItem?.ToString() ?? "sin nombre" : "ninguna";
            text.AppendLine($"Escenas completas de Dos Guitarras: {_dualGuitarSceneLibrary.Scenes.Count} guardadas; seleccionada = {dualSceneName}; cada escena restaura ambas cadenas, NAM/IR, efectos, mezclas, paneos, volumen master y acompañamiento; si el audio estaba activo se reinicia una sola vez.");
            text.AppendLine($"Edición de efectos: {(_dualEditGuitarCombo.SelectedIndex == 1 ? "Guitarra 2 / Input 2" : "Guitarra 1 / Input 1")}; la sección Efectos modifica sólo esa memoria y la otra permanece intacta");
            text.AppendLine("Cadenas duales: dos instancias DSP, dos memorias de efectos, dos rutas de gabinete, dos instancias NAM simultáneas, dos bibliotecas de bancos independientes y biblioteca de escenas completas; cargar o cambiar NAM/IR en una guitarra no modifica la otra.");
            text.AppendLine($"Afinador independiente por guitarra: fuente seleccionada = {SelectedTunerGuitarName}; sólo esa entrada se analiza y se silencia si corresponde; la otra guitarra permanece procesada. F9 lee, Control+F9 alterna Guitarra 1/Guitarra 2.");
            text.AppendLine($"Selector NAM rápido Alt+O: disponible para Guitarra 1 y Guitarra 2; guitarra seleccionada = {(_dualEditGuitarCombo.SelectedIndex == 1 ? "Guitarra 2" : "Guitarra 1")}; permite activar, elegir, cargar con Enter, anterior y siguiente sin abrir la sección NAM.");
            text.AppendLine($"Interfaz NAM ordenada: Alt+O concentra carga rápida; Alt+N concentra ajustes avanzados y biblioteca; Guitarra NAM a editar en Alt+N = {(_namEditGuitarCombo.SelectedIndex == 1 ? "Guitarra 2" : "Guitarra 1")}.");
            string accompanimentState = $"metrónomo {(_metronomeEnabled.Checked ? "ACTIVO" : "inactivo")}, batería {(_drumsEnabled.Checked ? "ACTIVA" : "inactiva")}, bajo {(_backingBassEnabled.Checked ? "ACTIVO" : "inactivo")}, piano {(_pianoEnabled.Checked ? "ACTIVO" : "inactivo")}";
            text.AppendLine($"Acompañamiento dual: bus global único e independiente de mute/mezcla de Guitarra 1 y Guitarra 2; {accompanimentState}; pico {FormatPeakDb(_engine.DualAccompanimentPeak)}; no se duplica al usar ambas guitarras y no se imprime dentro del looper.");
            text.AppendLine("Procesamiento dual: Guitarra 1 usa un control de procesamiento dedicado e independiente del antiguo estado de micrófono; Guitarra 2 permanece forzada al rig principal; bypass general Control+B bloqueado mientras Modo Dos Guitarras está activo.");
            text.AppendLine($"Medidores duales, pico desde inicio de audio: Guitarra 1 Input 1 cruda {FormatPeakDb(_engine.Guitar1RawPeak)}, salida DSP {FormatPeakDb(_engine.Guitar1ProcessedPeak)}; Guitarra 2 Input 2 cruda {FormatPeakDb(_engine.Guitar2RawPeak)}, salida DSP {FormatPeakDb(_engine.Guitar2ProcessedPeak)}");
            text.AppendLine($"Mezcla final dual, pico desde inicio de audio: aporte Guitarra 1 después de mezcla {FormatPeakDb(_engine.Guitar1MixedPeak)}; aporte Guitarra 2 después de mezcla {FormatPeakDb(_engine.Guitar2MixedPeak)}; mezcla antes del master {FormatPeakDb(_engine.DualFinalMixPeak)}; Playback 1-2 después del master {FormatPeakDb(_engine.MasterOutputPeak)}");
            text.AppendLine($"Verificación L/R posterior al paneo, desde el último cambio de paneo: Guitarra 1 izquierda {FormatPeakDb(_engine.Guitar1PostPanLeftPeak)}, derecha {FormatPeakDb(_engine.Guitar1PostPanRightPeak)}; Guitarra 2 izquierda {FormatPeakDb(_engine.Guitar2PostPanLeftPeak)}, derecha {FormatPeakDb(_engine.Guitar2PostPanRightPeak)}.");
            text.AppendLine($"Playback 1-2 posterior al master, por canal: izquierda {FormatPeakDb(_engine.MasterLeftPeak)}; derecha {FormatPeakDb(_engine.MasterRightPeak)}.");
            AppendHardPanDiagnostic(text, "Guitarra 1", (float)_guitar1Pan.Value, _engine.Guitar1PostPanLeftPeak, _engine.Guitar1PostPanRightPeak);
            AppendHardPanDiagnostic(text, "Guitarra 2", (float)_guitar2Pan.Value, _engine.Guitar2PostPanLeftPeak, _engine.Guitar2PostPanRightPeak);
            if (_engine.Guitar1RawPeak >= 0.99f) text.AppendLine("ADVERTENCIA Input 1: la señal cruda alcanzó 0,0 dB; reduzca la ganancia física de Input 1 para dejar margen antes del DSP.");
        }
        else
        {
            text.AppendLine("Modo Dos Guitarras: desactivado");
        }
        string classNamState = _voiceOnlyMode.Checked
            ? $"NAM forzado apagado; NAM guardado para guitarra {(_audioPreferences.GuitarClassNamEnabled ? "activo" : "inactivo")}"
            : $"NAM del rig {(_namEnabled.Checked && _engine.Processor.HasNamModel ? "activo" : "inactivo")}";
        if (_twoGuitarMode.Checked)
            text.AppendLine($"Perfil de trabajo: Dos Guitarras; voz desactivada; Guitarra 2 con {classNamState}");
        else if (IsGuitarClassDirectFocusriteLoopback())
            text.AppendLine($"Perfil de clase: Guitarra; retorno local de voz {_voiceMonitorLevel.Value:0} %; Focusrite Loopback recibe Playback 1-2 con guitarra procesada estéreo más voz procesada; nivel efectivo de voz en esa mezcla {_voiceMonitorLevel.Value:0} %; salida virtual adicional no usada; {classNamState}");
        else
            text.AppendLine($"Perfil de clase: {_audioPreferences.LastClassProfile}; retorno local de voz {_voiceMonitorLevel.Value:0} %; retorno guardado para guitarra {_audioPreferences.GuitarClassMonitorPercent:0} %; envío de voz a videollamada {_meetVoiceLevel.Value:0} %; {classNamState}");
        text.AppendLine($"Modo Voz: {(_twoGuitarMode.Checked ? "desactivado por Modo Dos Guitarras" : _voiceOnlyMode.Checked ? "activo; sólo micrófono" : "desactivado")}");
        if (!_twoGuitarMode.Checked && (_voiceEnabled.Checked || _voiceOnlyMode.Checked))
        {
            text.AppendLine($"Micrófono Input 1, medición interna desde inicio de audio: señal cruda {FormatPeakDb(_engine.VoiceRawPeak)}; voz después de supresor/EQ/compresión {FormatPeakDb(_engine.Processor.VoiceProcessedPeak)}; nivel de voz {_voiceLevel.Value:0} %; supresor {(_voiceSuppressorEnabled.Checked ? "activo" : "inactivo")}.");
            if (_voiceOnlyMode.Checked && !_meetOutputEnabled.Checked)
                text.AppendLine("Ruta de voz Inglés 2.41.30: ATENCIÓN, retorno local 0 % significa que no se oye la voz en Playback 1-2. La salida procesada a Loopback todavía NO está preparada; active Loopback Focusrite. Un pico del dispositivo Loopback por sí solo no demuestra que sea la voz de Amp Accessible.");
            else if (_voiceOnlyMode.Checked && _meetOutputEnabled.Checked)
                text.AppendLine($"Ruta de voz Inglés 2.41.30: salida procesada a videollamada {(_engine.IsMeetOutputRunning ? "ACTIVA" : "configurada pero no activa")}; retorno local {_voiceMonitorLevel.Value:0} %; envío de voz {_meetVoiceLevel.Value:0} %.");
        }
        if (_meetOutputEnabled.Checked)
            text.AppendLine($"Salida virtual adicional para videollamadas: {(_engine.IsMeetOutputRunning ? "activa" : "configurada pero no activa")}");
        else if (_twoGuitarMode.Checked)
            text.AppendLine("Salida virtual adicional para videollamadas: desactivada; Modo Dos Guitarras sale directamente por Playback 1-2 y puede ser recibido por Focusrite Loopback.");
        else if (_voiceOnlyMode.Checked)
            text.AppendLine("Salida virtual adicional para videollamadas: desactivada; Focusrite Loopback usa Playback 1-2 y no requiere esta salida adicional.");
        else if (IsGuitarClassDirectFocusriteLoopback())
            text.AppendLine("Salida virtual adicional para videollamadas: desactivada a propósito; Clase de Guitarra usa Playback 1-2 directo hacia Focusrite Loopback para evitar doble monitoreo.");
        else
            text.AppendLine("Salida virtual adicional para videollamadas: desactivada.");
        try
        {
            var captureStates = AudioEngine.GetCaptureDeviceStatuses();
            string cableState = string.Join("; ", captureStates
                .Where(d => d.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)
                    || d.Name.Contains("Voicemeeter", StringComparison.OrdinalIgnoreCase)
                    || d.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
                .Select(d => $"{d.Name}: {d.State}"));
            text.AppendLine($"Micrófonos de videollamada Windows: {(string.IsNullOrWhiteSpace(cableState) ? "ninguno de Loopback/VB-CABLE/VoiceMeeter detectado" : cableState)}");
        }
        catch (Exception ex)
        {
            text.AppendLine($"Micrófonos de videollamada Windows: no se pudieron enumerar ({ex.Message})");
        }
        try
        {
            MeetInputDevice? focusriteLoopback = AudioEngine.GetPreferredFocusriteLoopbackInputDevice();
            if (focusriteLoopback is null)
                text.AppendLine("Focusrite Loopback: NO EXPUESTO. En Focusrite Notifier marque Loopback L + R.");
            else
            {
                string loopPeak = AudioEngine.TryGetCapturePeak(focusriteLoopback.Id, out float fp)
                    ? (fp <= 0.000001f ? "sin señal instantánea" : $"pico {Math.Max(-120.0, 20.0 * Math.Log10(fp)):0.0} dB")
                    : "medidor no disponible";
                bool loopSignalDetected = AudioEngine.TryGetCapturePeak(focusriteLoopback.Id, out float loopReadyPeak) && loopReadyPeak >= 0.001f;
                bool englishLoopbackPrepared = _voiceOnlyMode.Checked && _meetOutputEnabled.Checked && (!_engine.IsRunning || _engine.IsMeetOutputRunning);
                string loopRouteState = _twoGuitarMode.Checked
                    ? (loopSignalDetected
                        ? "ACTIVO: señal detectada; mezcla de dos guitarras procesadas por Playback 1-2; estéreo L/R conservado"
                        : "Modo Dos Guitarras preparado por Playback 1-2, pero sin señal instantánea; toque cualquiera de las dos guitarras para comprobar")
                    : _voiceOnlyMode.Checked
                        ? (englishLoopbackPrepared
                            ? (loopSignalDetected
                                ? "ACTIVO: ruta procesada de Inglés preparada y señal detectada; listo para videollamada"
                                : "Ruta procesada de Inglés preparada, pero sin señal instantánea; hable para comprobar")
                            : (loopSignalDetected
                                ? "ATENCIÓN: Loopback muestra señal, pero la ruta procesada de Inglés NO está preparada; el pico puede pertenecer a otra fuente. Active Loopback Focusrite"
                                : "Loopback disponible, pero la ruta procesada de Inglés NO está preparada. Active Loopback Focusrite"))
                        : IsGuitarClassDirectFocusriteLoopback()
                            ? (loopSignalDetected
                                ? "ACTIVO: señal detectada; Clase de Guitarra por Playback 1-2; estéreo L/R conservado; listo para Meet"
                                : "Clase de Guitarra preparada por Playback 1-2, pero sin señal instantánea; hable o toque para comprobar")
                            : (loopSignalDetected
                                ? "señal detectada en Loopback; verifique además que la ruta procesada seleccionada esté activa"
                                : "disponible, pero sin señal instantánea; hable para comprobar la ruta");
                text.AppendLine($"Focusrite Loopback: {focusriteLoopback.Name}; {loopPeak}; {loopRouteState}");
                text.AppendLine("Seguridad Loopback: Zoom/Meet debe reproducir por una salida distinta de Focusrite para evitar eco de retorno.");
                if (IsGuitarClassDirectFocusriteLoopback())
                    text.AppendLine("Clase de Guitarra / Focusrite Control 2: para que Meet reciba sólo el audio procesado, Playback 1-2 debe estar en la mezcla enviada a Loopback y Analogue 1 / Analogue 2 deben quedar silenciados en esa mezcla.");
            }
        }
        catch (Exception ex)
        {
            text.AppendLine($"Focusrite Loopback: no se pudo consultar ({ex.Message})");
        }
        if (_meetOutputEnabled.Checked && _engine.IsMeetOutputRunning)
        {
            string virtualMode = _voiceOnlyMode.Checked
                ? "voz mono duplicada en todos los canales del dispositivo virtual"
                : "estéreo L/R conservado";
            text.AppendLine($"Formato de salida virtual: {_engine.MeetEndpointChannels} canal(es); {virtualMode}");
        }
        if (SelectedMeetOutputIsVoicemeeter() || _voicemeeter.IsInstalled)
            text.AppendLine($"VoiceMeeter: {_voicemeeter.GetStatusSummary()}; alternativa avanzada, no es la ruta principal cuando Focusrite Loopback está disponible.");

        MeetInputDevice? conferenceMic = GetRecommendedConferenceMic();
        if (conferenceMic is null)
        {
            text.AppendLine("Micrófono recomendado para Zoom/Meet: NO ENCONTRADO. Mantener Focusrite como micrófono hasta resolver la ruta de videollamada.");
        }
        else
        {
            string peakText = AudioEngine.TryGetCapturePeak(conferenceMic.Id, out float conferencePeak)
                ? (conferencePeak <= 0.000001f ? "sin señal instantánea" : $"pico {Math.Max(-120.0, 20.0 * Math.Log10(conferencePeak)):0.0} dB")
                : "medidor no disponible";
            bool conferenceIsLoopback = conferenceMic.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase);
            bool conferenceRoutePrepared = !conferenceIsLoopback || IsGuitarClassDirectFocusriteLoopback() ||
                (_meetOutputEnabled.Checked && (!_engine.IsRunning || _engine.IsMeetOutputRunning));
            string readyText = conferencePeak >= 0.001f && conferenceRoutePrepared
                ? "; señal presente y ruta preparada; listo"
                : conferencePeak >= 0.001f && conferenceIsLoopback
                    ? "; señal presente, pero ruta de Amp Accessible NO preparada"
                    : string.Empty;
            text.AppendLine($"Micrófono recomendado para Zoom/Meet: {conferenceMic.Name}; {peakText}{readyText}");
        }
        string loopSourceDiagnostic = _twoGuitarMode.Checked
            ? CurrentLoopCaptureSourceName
            : "rig principal / Guitarra 2; selector dual en espera";
        text.AppendLine($"Looper por guitarra 2.41.31: fuente de captura = {loopSourceDiagnostic}; la selección se aplica a primera vuelta y overdub; acompañamiento global no se imprime; durante una captura activa la fuente queda bloqueada para evitar cambios a mitad de vuelta.");
        text.AppendLine($"Memoria: administrada {managedMb} MB; proceso {processMb} MB; GC 0/1/2: {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
        string looperDiagnosticState = _engine.IsLoopRecording
            ? $"grabando primera vuelta desde {CurrentLoopCaptureSourceName}"
            : _engine.IsLoopOverdubbing
                ? $"overdub desde {CurrentLoopCaptureSourceName}"
                : _engine.IsLoopPlaying ? "reproduciendo" : "detenido";
        text.AppendLine($"Grabadora: {(_engine.IsPracticeRecording ? "grabando" : "detenida")}; looper: {looperDiagnosticState}");
        if (!string.IsNullOrWhiteSpace(_audioDiagnostics.LastSavedPath)) text.AppendLine($"Último diagnóstico guardado: {_audioDiagnostics.LastSavedPath}");
        return text.ToString().TrimEnd();
    }

    private void UpdateAudioDiagnosticPanel(bool announce = false)
    {
        if (_audioDiagnosticReport.IsDisposed) return;
        string health = GetAudioDiagnosticHealth();
        _audioDiagnosticHealth.Text = health;
        _audioDiagnosticHealth.AccessibleName = $"Estado general del audio: {health}";
        // Si el usuario está leyendo el informe con JAWS no reemplazamos el texto
        // periódicamente, porque movería el cursor de lectura. Se refresca al salir del
        // control o al pulsar explícitamente Actualizar diagnóstico.
        if (!_audioDiagnosticReport.Focused || announce)
        {
            string report = BuildAudioDiagnosticReport();
            if (!string.Equals(_audioDiagnosticReport.Text, report, StringComparison.Ordinal))
                _audioDiagnosticReport.Text = report;
        }
        if (announce) SetStatus(health, health.StartsWith("CRÍTICO", StringComparison.Ordinal));
    }

    private void ReadAudioDiagnosticSummary()
    {
        UpdateAudioDiagnosticPanel();
        string summary = GetAudioDiagnosticHealth();
        if (_engine.HasActiveSession)
            summary += $" Buffer {_engine.ActualBufferSize} muestras. Volumen master {_masterVolume.Value:0} por ciento. Carga DSP {_engine.LastDspLoadPercent} por ciento, máxima {_engine.MaxDspLoadPercent}. Plazos excedidos {_engine.OutputUnderrunCount}. Errores {_engine.TotalAudioErrorCount}. Dos Guitarras {(_twoGuitarMode.Checked ? "activo" : "desactivado")}. Modo Voz {(_voiceOnlyMode.Checked ? "activo" : "desactivado")}. NAM {(_namEnabled.Checked && _engine.Processor.HasNamModel ? "activo" : "inactivo")}.";
        SetStatus(summary, summary.StartsWith("CRÍTICO", StringComparison.Ordinal));
    }

    private void CopyAudioDiagnosticReport()
    {
        try
        {
            UpdateAudioDiagnosticPanel();
            Clipboard.SetText(_audioDiagnosticReport.Text);
            SetStatus("Informe de diagnóstico copiado al portapapeles.");
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo copiar el diagnóstico: {ex.Message}", true);
        }
    }

    private void SaveAudioDiagnosticReport()
    {
        try
        {
            CaptureAudioTelemetry();
            string path = _audioDiagnostics.Save("Informe manual solicitado desde Diagnóstico", BuildAudioDiagnosticReport());
            UpdateAudioDiagnosticPanel();
            SetStatus($"Diagnóstico guardado en {path}.");
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo guardar el diagnóstico: {ex.Message}", true);
        }
    }

    private static string GetAudioDiagnosticsFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GDM Amp Accessible", "Diagnosticos");

    private void OpenAudioDiagnosticsFolder()
    {
        try
        {
            string folder = GetAudioDiagnosticsFolder();
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            SetStatus("Carpeta de diagnósticos abierta.");
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo abrir la carpeta de diagnósticos: {ex.Message}", true);
        }
    }

    private void OpenAsioPanel()
    {
        string? driver = _driverCombo.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(driver))
        {
            SetStatus("Seleccione un controlador ASIO.", true);
            return;
        }

        try
        {
            AudioEngine.ShowControlPanel(driver);
            SetStatus($"Panel ASIO de {driver} abierto. Cambie allí el buffer real si lo desea. Referencia actual en Amp Accessible: {SelectedBufferSize}. El valor efectivo se comprobará al iniciar con F4.");
        }
        catch (Exception exception)
        {
            SetStatus($"No se pudo abrir el panel ASIO: {exception.Message}", true);
        }
    }

    private void LoadGuitar1IrPreferences()
    {
        _guitar1LoadedIrPath = null;
        _engine.Guitar1Processor.ClearImpulseResponse();
        string savedPath = _audioPreferences.Guitar1IrPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
        {
            try
            {
                _engine.Guitar1Processor.LoadImpulseResponse(savedPath);
                _guitar1LoadedIrPath = savedPath;
                _guitar1IrPath.Text = savedPath;
                _guitar1ClearIrButton.Enabled = true;
            }
            catch
            {
                _guitar1IrPath.Text = "Guitarra 1: no se pudo restaurar el IR; se usa gabinete interno.";
            }
        }
        else
        {
            _guitar1IrPath.Text = string.IsNullOrWhiteSpace(savedPath)
                ? "Guitarra 1: gabinete interno."
                : $"Guitarra 1: IR no encontrado: {savedPath}. Se usa gabinete interno.";
        }

        string folder = _audioPreferences.Guitar1IrBrowserFolder ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            ScanGuitar1IrBrowserFolder(folder, _audioPreferences.Guitar1IrBrowserLastFilePath, announce: false, persist: false);
        else
            UpdateGuitar1IrBrowserStatus();
    }

    private void SelectGuitar1IrBrowserFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Elija una carpeta de IR para Guitarra 1. Se indexarán WAV, WAVE, AIF y AIFF.",
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(_audioPreferences.Guitar1IrBrowserFolder)
                ? _audioPreferences.Guitar1IrBrowserFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath)) return;
        ScanGuitar1IrBrowserFolder(dialog.SelectedPath, preferredPath: null, announce: true, persist: true);
    }

    private void ScanGuitar1IrBrowserFolder(string folder, string? preferredPath, bool announce, bool persist)
    {
        _guitar1IrBrowserFiles.Clear();
        _guitar1IrBrowserIndex = -1;
        try
        {
            if (!Directory.Exists(folder))
            {
                _guitar1PreviousIrButton.Enabled = false;
                _guitar1NextIrButton.Enabled = false;
                _guitar1IrBrowserStatus.Text = "Carpeta IR de Guitarra 1 no encontrada.";
                _guitar1IrBrowserStatus.AccessibleName = _guitar1IrBrowserStatus.Text;
                if (announce) SetStatus(_guitar1IrBrowserStatus.Text, true);
                return;
            }

            _guitar1IrBrowserFiles.AddRange(Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(IsSupportedImpulseResponseFile)
                .OrderBy(path => path, Comparer<string>.Create(CompareIrPathsNaturally)));
            _audioPreferences.Guitar1IrBrowserFolder = folder;

            string?[] candidates = { preferredPath, _guitar1LoadedIrPath, _audioPreferences.Guitar1IrBrowserLastFilePath };
            foreach (string? candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                int index = _guitar1IrBrowserFiles.FindIndex(path => string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) { _guitar1IrBrowserIndex = index; break; }
            }

            _guitar1PreviousIrButton.Enabled = _guitar1IrBrowserFiles.Count > 0;
            _guitar1NextIrButton.Enabled = _guitar1IrBrowserFiles.Count > 0;
            UpdateGuitar1IrBrowserStatus();
            if (persist) SaveAudioPreferences();

            if (announce)
            {
                if (_guitar1IrBrowserFiles.Count == 0)
                    SetStatus($"Carpeta IR de Guitarra 1 seleccionada: {folder}. No contiene archivos compatibles.", true);
                else if (_guitar1IrBrowserIndex >= 0)
                    SetStatus($"Guitarra 1: carpeta IR preparada con {_guitar1IrBrowserFiles.Count} archivos. Seleccionado {_guitar1IrBrowserIndex + 1}: {Path.GetFileName(_guitar1IrBrowserFiles[_guitar1IrBrowserIndex])}.");
                else
                    SetStatus($"Guitarra 1: carpeta IR preparada con {_guitar1IrBrowserFiles.Count} archivos. Pulse IR siguiente de Guitarra 1 para cargar el primero.");
            }
        }
        catch (Exception ex)
        {
            _guitar1PreviousIrButton.Enabled = false;
            _guitar1NextIrButton.Enabled = false;
            _guitar1IrBrowserStatus.Text = $"No se pudo leer la carpeta IR de Guitarra 1: {ex.Message}";
            _guitar1IrBrowserStatus.AccessibleName = _guitar1IrBrowserStatus.Text;
            if (announce) SetStatus(_guitar1IrBrowserStatus.Text, true);
        }
    }

    private void UpdateGuitar1IrBrowserStatus()
    {
        if (_guitar1IrBrowserFiles.Count == 0)
        {
            string folder = _audioPreferences.Guitar1IrBrowserFolder ?? string.Empty;
            _guitar1IrBrowserStatus.Text = string.IsNullOrWhiteSpace(folder)
                ? "Carpeta IR de Guitarra 1 no seleccionada."
                : $"Carpeta IR de Guitarra 1 sin archivos compatibles: {folder}";
            _guitar1IrBrowserStatus.AccessibleName = _guitar1IrBrowserStatus.Text;
            return;
        }
        string folderName = Path.GetFileName((_audioPreferences.Guitar1IrBrowserFolder ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folderName)) folderName = _audioPreferences.Guitar1IrBrowserFolder;
        _guitar1IrBrowserStatus.Text = _guitar1IrBrowserIndex >= 0
            ? $"{folderName}: IR {_guitar1IrBrowserIndex + 1} de {_guitar1IrBrowserFiles.Count}: {Path.GetFileName(_guitar1IrBrowserFiles[_guitar1IrBrowserIndex])}"
            : $"{folderName}: {_guitar1IrBrowserFiles.Count} IR disponibles; todavía no se cargó uno.";
        _guitar1IrBrowserStatus.AccessibleName = _guitar1IrBrowserStatus.Text;
    }

    private void AdoptGuitar1IrBrowserFolderFromLoadedFile(string filePath)
    {
        string? folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        ScanGuitar1IrBrowserFolder(folder, filePath, announce: false, persist: false);
        _audioPreferences.Guitar1IrBrowserLastFilePath = filePath;
        SaveAudioPreferences();
        UpdateGuitar1IrBrowserStatus();
    }

    private void LoadAdjacentGuitar1ImpulseResponse(int direction)
    {
        if (_guitar1IrBrowserFiles.Count == 0)
        {
            string folder = _audioPreferences.Guitar1IrBrowserFolder ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                ScanGuitar1IrBrowserFolder(folder, preferredPath: null, announce: false, persist: false);
        }
        if (_guitar1IrBrowserFiles.Count == 0)
        {
            SetStatus("Guitarra 1 no tiene una carpeta IR preparada. Use Elegir carpeta IR Guitarra 1 o cargue un IR desde Alt+O.", true);
            return;
        }
        int step = direction < 0 ? -1 : 1;
        int nextIndex = _guitar1IrBrowserIndex < 0
            ? (step > 0 ? 0 : _guitar1IrBrowserFiles.Count - 1)
            : (_guitar1IrBrowserIndex + step + _guitar1IrBrowserFiles.Count) % _guitar1IrBrowserFiles.Count;
        LoadGuitar1IrBrowserItem(nextIndex, announce: true);
    }

    private void LoadGuitar1IrBrowserItem(int index, bool announce)
    {
        if (index < 0 || index >= _guitar1IrBrowserFiles.Count) return;
        string path = _guitar1IrBrowserFiles[index];
        if (!File.Exists(path))
        {
            ScanGuitar1IrBrowserFolder(_audioPreferences.Guitar1IrBrowserFolder ?? string.Empty, null, false, false);
            SetStatus($"El IR de Guitarra 1 ya no existe: {Path.GetFileName(path)}. La carpeta fue actualizada.", true);
            return;
        }
        try
        {
            int samples = _engine.Guitar1Processor.LoadImpulseResponse(path);
            _guitar1LoadedIrPath = path;
            _guitar1IrBrowserIndex = index;
            _audioPreferences.Guitar1IrPath = path;
            _audioPreferences.Guitar1IrBrowserLastFilePath = path;
            _guitar1IrPath.Text = path;
            _guitar1ClearIrButton.Enabled = true;
            UpdateParameters();
            SaveAudioPreferences();
            UpdateGuitar1IrBrowserStatus();
            if (announce)
            {
                double milliseconds = samples * 1000.0 / AudioEngine.SampleRate;
                SetStatus($"Guitarra 1, IR {_guitar1IrBrowserIndex + 1} de {_guitar1IrBrowserFiles.Count}: {Path.GetFileName(path)}; {samples} muestras, {milliseconds:0.0} milisegundos.");
            }
        }
        catch (Exception ex) { SetStatus($"No se pudo cargar el IR de Guitarra 1 {Path.GetFileName(path)}: {ex.Message}", true); }
    }

    private void LoadGuitar1ImpulseResponse()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Cargar respuesta impulsional IR A de Guitarra 1",
            Filter = "Respuestas impulsionales (*.wav;*.wave;*.aif;*.aiff)|*.wav;*.wave;*.aif;*.aiff|Todos los archivos (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            int samples = _engine.Guitar1Processor.LoadImpulseResponse(dialog.FileName);
            _guitar1LoadedIrPath = dialog.FileName;
            _audioPreferences.Guitar1IrPath = dialog.FileName;
            _guitar1IrPath.Text = dialog.FileName;
            _guitar1ClearIrButton.Enabled = true;
            AdoptGuitar1IrBrowserFolderFromLoadedFile(dialog.FileName);
            UpdateParameters();
            SaveAudioPreferences();
            double milliseconds = samples * 1000.0 / AudioEngine.SampleRate;
            SetStatus($"IR de Guitarra 1 cargado: {Path.GetFileName(dialog.FileName)}, {samples} muestras, {milliseconds:0.0} milisegundos. Su carpeta quedó preparada.");
        }
        catch (Exception ex) { SetStatus($"No se pudo cargar el IR de Guitarra 1: {ex.Message}", true); }
    }

    private void ClearGuitar1ImpulseResponse()
    {
        _engine.Guitar1Processor.ClearImpulseResponse();
        _guitar1LoadedIrPath = null;
        _audioPreferences.Guitar1IrPath = string.Empty;
        _guitar1IrPath.Text = "Guitarra 1: gabinete interno.";
        _guitar1ClearIrButton.Enabled = false;
        UpdateParameters();
        SaveAudioPreferences();
        SetStatus("IR externo de Guitarra 1 quitado. Guitarra 1 volvió al gabinete interno.");
    }

    private void LoadIrBrowserPreference()
    {
        string folder = _audioPreferences.IrBrowserFolder ?? string.Empty;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            UpdateIrBrowserStatus();
            return;
        }

        ScanIrBrowserFolder(folder, _audioPreferences.IrBrowserLastFilePath, announce: false, persist: false);
    }

    private void SelectIrBrowserFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Elija una carpeta con respuestas impulsionales IR. Amp Accessible indexará WAV, WAVE, AIF y AIFF para recorrerlos con IR anterior y siguiente.",
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(_audioPreferences.IrBrowserFolder)
                ? _audioPreferences.IrBrowserFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;

        ScanIrBrowserFolder(dialog.SelectedPath, preferredPath: null, announce: true, persist: true);
    }

    private void ScanIrBrowserFolder(string folder, string? preferredPath, bool announce, bool persist)
    {
        _irBrowserFiles.Clear();
        _irBrowserIndex = -1;

        try
        {
            if (!Directory.Exists(folder))
            {
                _previousIrButton.Enabled = false;
                _nextIrButton.Enabled = false;
                _irBrowserStatus.Text = "Carpeta IR no encontrada.";
                _irBrowserStatus.AccessibleName = _irBrowserStatus.Text;
                if (announce) SetStatus("La carpeta de IR seleccionada ya no existe.", true);
                return;
            }

            _irBrowserFiles.AddRange(Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(IsSupportedImpulseResponseFile)
                .OrderBy(path => path, Comparer<string>.Create(CompareIrPathsNaturally)));

            _audioPreferences.IrBrowserFolder = folder;

            string?[] selectionCandidates =
            {
                preferredPath,
                _loadedIrPath,
                _audioPreferences.IrBrowserLastFilePath
            };
            foreach (string? candidate in selectionCandidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                int candidateIndex = _irBrowserFiles.FindIndex(path =>
                    string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase));
                if (candidateIndex >= 0)
                {
                    _irBrowserIndex = candidateIndex;
                    break;
                }
            }

            _previousIrButton.Enabled = _irBrowserFiles.Count > 0;
            _nextIrButton.Enabled = _irBrowserFiles.Count > 0;
            UpdateIrBrowserStatus();

            if (persist) SaveAudioPreferences();

            if (announce)
            {
                if (_irBrowserFiles.Count == 0)
                    SetStatus($"Carpeta IR seleccionada: {folder}. No contiene archivos WAV, WAVE, AIF o AIFF.", true);
                else if (_irBrowserIndex >= 0)
                    SetStatus($"Carpeta IR preparada con {_irBrowserFiles.Count} archivos. Seleccionado {_irBrowserIndex + 1} de {_irBrowserFiles.Count}: {Path.GetFileName(_irBrowserFiles[_irBrowserIndex])}.");
                else
                    SetStatus($"Carpeta IR preparada con {_irBrowserFiles.Count} archivos. Pulse IR siguiente para cargar el primero o IR anterior para empezar por el último.");
            }
        }
        catch (Exception exception)
        {
            _previousIrButton.Enabled = false;
            _nextIrButton.Enabled = false;
            _irBrowserStatus.Text = $"No se pudo leer la carpeta IR: {exception.Message}";
            _irBrowserStatus.AccessibleName = _irBrowserStatus.Text;
            if (announce) SetStatus(_irBrowserStatus.Text, true);
        }
    }

    private void UpdateIrBrowserStatus()
    {
        if (_irBrowserFiles.Count == 0)
        {
            string folder = _audioPreferences.IrBrowserFolder ?? string.Empty;
            _irBrowserStatus.Text = string.IsNullOrWhiteSpace(folder)
                ? "Carpeta IR no seleccionada."
                : $"Carpeta IR sin archivos compatibles: {folder}";
            _irBrowserStatus.AccessibleName = _irBrowserStatus.Text;
            return;
        }

        string folderName = Path.GetFileName(_audioPreferences.IrBrowserFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folderName)) folderName = _audioPreferences.IrBrowserFolder;
        _irBrowserStatus.Text = _irBrowserIndex >= 0
            ? $"{folderName}: IR {_irBrowserIndex + 1} de {_irBrowserFiles.Count}: {Path.GetFileName(_irBrowserFiles[_irBrowserIndex])}"
            : $"{folderName}: {_irBrowserFiles.Count} IR disponibles; todavía no se cargó uno desde el explorador.";
        _irBrowserStatus.AccessibleName = _irBrowserStatus.Text;
    }

    private void AdoptIrBrowserFolderFromLoadedFile(string filePath)
    {
        string? folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        ScanIrBrowserFolder(folder, filePath, announce: false, persist: false);
        _audioPreferences.IrBrowserLastFilePath = filePath;
        SaveAudioPreferences();
        UpdateIrBrowserStatus();
    }

    private void SyncIrBrowserToLoadedPath(string filePath)
    {
        if (_irBrowserFiles.Count == 0 || string.IsNullOrWhiteSpace(_audioPreferences.IrBrowserFolder)) return;
        string? folder = Path.GetDirectoryName(filePath);
        if (!string.Equals(folder, _audioPreferences.IrBrowserFolder, StringComparison.OrdinalIgnoreCase)) return;

        int index = _irBrowserFiles.FindIndex(path => string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        _irBrowserIndex = index;
        _audioPreferences.IrBrowserLastFilePath = filePath;
        UpdateIrBrowserStatus();
    }

    private void LoadAdjacentImpulseResponse(int direction)
    {
        if (_irBrowserFiles.Count == 0)
        {
            string folder = _audioPreferences.IrBrowserFolder ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                ScanIrBrowserFolder(folder, preferredPath: null, announce: false, persist: false);
        }

        if (_irBrowserFiles.Count == 0)
        {
            SetStatus("No hay una carpeta IR preparada. Use Elegir carpeta de IR o cargue primero un IR A desde una carpeta.", true);
            return;
        }

        int step = direction < 0 ? -1 : 1;
        int nextIndex;
        if (_irBrowserIndex < 0)
            nextIndex = step > 0 ? 0 : _irBrowserFiles.Count - 1;
        else
            nextIndex = (_irBrowserIndex + step + _irBrowserFiles.Count) % _irBrowserFiles.Count;

        LoadIrBrowserItem(nextIndex, announce: true);
    }

    private void LoadIrBrowserItem(int index, bool announce)
    {
        if (index < 0 || index >= _irBrowserFiles.Count) return;
        string path = _irBrowserFiles[index];
        if (!File.Exists(path))
        {
            string folder = _audioPreferences.IrBrowserFolder ?? string.Empty;
            ScanIrBrowserFolder(folder, preferredPath: null, announce: false, persist: false);
            SetStatus($"El IR ya no existe: {Path.GetFileName(path)}. La carpeta fue actualizada.", true);
            return;
        }

        try
        {
            int samples = _engine.Processor.LoadImpulseResponse(path);
            _loadedIrPath = path;
            _irBrowserIndex = index;
            _audioPreferences.IrBrowserLastFilePath = path;
            _irPath.Text = path;
            _externalIrEnabled.Enabled = true;
            _externalIrEnabled.Checked = true;
            _clearIrButton.Enabled = true;
            UpdateParameters();
            SaveAudioPreferences();
            UpdateIrBrowserStatus();

            if (announce)
            {
                double milliseconds = samples * 1000.0 / AudioEngine.SampleRate;
                SetStatus($"IR {_irBrowserIndex + 1} de {_irBrowserFiles.Count}: {Path.GetFileName(path)}; {samples} muestras, {milliseconds:0.0} milisegundos. Cargado en IR A.");
            }
        }
        catch (Exception exception)
        {
            SetStatus($"No se pudo cargar {Path.GetFileName(path)}: {exception.Message}", true);
        }
    }

    private static bool IsSupportedImpulseResponseFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wave", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aiff", StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareIrPathsNaturally(string? leftPath, string? rightPath)
    {
        string left = Path.GetFileName(leftPath ?? string.Empty);
        string right = Path.GetFileName(rightPath ?? string.Empty);
        string[] leftParts = System.Text.RegularExpressions.Regex.Split(left, "([0-9]+)");
        string[] rightParts = System.Text.RegularExpressions.Regex.Split(right, "([0-9]+)");
        int count = Math.Min(leftParts.Length, rightParts.Length);
        for (int i = 0; i < count; i++)
        {
            bool leftNumber = long.TryParse(leftParts[i], out long leftValue);
            bool rightNumber = long.TryParse(rightParts[i], out long rightValue);
            int comparison;
            if (leftNumber && rightNumber)
                comparison = leftValue.CompareTo(rightValue);
            else
                comparison = StringComparer.CurrentCultureIgnoreCase.Compare(leftParts[i], rightParts[i]);
            if (comparison != 0) return comparison;
        }
        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private void LoadImpulseResponse()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Cargar respuesta impulsional de gabinete",
            Filter = "Archivos de audio WAV o AIFF|*.wav;*.wave;*.aif;*.aiff|Todos los archivos|*.*",
            InitialDirectory = Directory.Exists(_audioPreferences.IrBrowserFolder) ? _audioPreferences.IrBrowserFolder : string.Empty,
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            int samples = _engine.Processor.LoadImpulseResponse(dialog.FileName);
            _loadedIrPath = dialog.FileName;
            _irPath.Text = dialog.FileName;
            _externalIrEnabled.Enabled = true;
            _externalIrEnabled.Checked = true;
            _clearIrButton.Enabled = true;
            UpdateParameters();
            AdoptIrBrowserFolderFromLoadedFile(dialog.FileName);
            double milliseconds = samples * 1000.0 / AudioEngine.SampleRate;
            SetStatus($"IR cargado: {Path.GetFileName(dialog.FileName)}, {samples} muestras, {milliseconds:0.0} milisegundos procesados. La carpeta quedó preparada para IR anterior y siguiente.");
        }
        catch (Exception exception)
        {
            SetStatus($"No se pudo cargar el IR: {exception.Message}", true);
        }
    }

    private void LoadImpulseResponseB()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Cargar segunda respuesta impulsional de gabinete, IR B",
            Filter = "Archivos de audio WAV o AIFF|*.wav;*.wave;*.aif;*.aiff|Todos los archivos|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            int samples = _engine.Processor.LoadImpulseResponseB(dialog.FileName);
            _loadedIrPathB = dialog.FileName;
            _irPathB.Text = dialog.FileName;
            _externalIrBEnabled.Enabled = true;
            _externalIrBEnabled.Checked = true;
            _clearIrBButton.Enabled = true;
            if (_irMix.Value == 0) _irMix.Value = 35;
            UpdateParameters();
            double milliseconds = samples * 1000.0 / AudioEngine.SampleRate;
            SetStatus($"IR B cargado: {Path.GetFileName(dialog.FileName)}, {samples} muestras, {milliseconds:0.0} milisegundos. Mezcla B: {_irMix.Value:0} por ciento.");
        }
        catch (Exception exception)
        {
            SetStatus($"No se pudo cargar el IR B: {exception.Message}", true);
        }
    }

    private void LoadNamModelFromDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Cargar modelo Neural Amp Modeler",
            Filter = "Modelos Neural Amp Modeler (*.nam)|*.nam|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            RegisterAndLoadNamModel(dialog.FileName, announce: true);
        }
    }

    private void RegisterAndLoadNamModel(string sourcePath, bool announce)
    {
        ImportNamModel(sourcePath, announce, loadAfterImport: true);
    }

    private void ImportNamModel(string sourcePath, bool announce, bool loadAfterImport)
    {
        try
        {
            if (!string.Equals(Path.GetExtension(sourcePath), ".nam", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("El archivo seleccionado no tiene extensión .nam.", true);
                return;
            }

            NamLibraryItem item = NamLibraryStore.Import(
                _namLibrary,
                sourcePath,
                _namIncludesCabinet.Checked,
                (float)_namInputTrim.Value,
                (float)_namOutputTrim.Value);
            RefreshNamCategoryFilter();
            RefreshNamBankCombo(item.Id);

            if (loadAfterImport)
            {
                LoadNamBankItem(item, announce);
            }
            else
            {
                _activeNamBankId = item.Id;
                SetStatus($"Modelo NAM guardado en el Banco NAM: {item.Name}.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo incorporar la captura al banco NAM: {ex.Message}", true);
        }
    }

    private void RefreshNamCategoryFilter(string? preferredCategory = null)
    {
        _loadingNamFilters = true;
        try
        {
            string current = preferredCategory
                ?? _namCategoryFilter.SelectedItem?.ToString()
                ?? "Todas las categorías";

            _namCategoryFilter.Items.Clear();
            _namCategoryFilter.Items.Add("Todas las categorías");
            _namCategoryFilter.Items.Add("Sin categoría");

            foreach (string category in _namLibrary.Items
                         .Select(item => NamLibraryStore.NormalizeCategory(item.Category))
                         .Where(category => !string.IsNullOrWhiteSpace(category))
                         .Distinct(StringComparer.CurrentCultureIgnoreCase)
                         .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase))
            {
                _namCategoryFilter.Items.Add(category);
                if (!_namCategoryEdit.Items.Cast<object>().Any(item =>
                        string.Equals(item?.ToString(), category, StringComparison.CurrentCultureIgnoreCase)))
                    _namCategoryEdit.Items.Add(category);
            }

            int wantedIndex = -1;
            for (int i = 0; i < _namCategoryFilter.Items.Count; i++)
            {
                if (string.Equals(_namCategoryFilter.Items[i]?.ToString(), current, StringComparison.CurrentCultureIgnoreCase))
                {
                    wantedIndex = i;
                    break;
                }
            }
            _namCategoryFilter.SelectedIndex = wantedIndex >= 0 ? wantedIndex : 0;
        }
        finally
        {
            _loadingNamFilters = false;
        }
    }

    private IEnumerable<NamLibraryItem> FilteredNamItems()
    {
        IEnumerable<NamLibraryItem> items = _namLibrary.Items;
        string query = _namBankFilterText.Text.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            items = items.Where(item =>
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                (item.Category ?? string.Empty).Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        string category = _namCategoryFilter.SelectedItem?.ToString() ?? "Todas las categorías";
        if (string.Equals(category, "Sin categoría", StringComparison.CurrentCultureIgnoreCase))
            items = items.Where(item => string.IsNullOrWhiteSpace(item.Category));
        else if (!string.Equals(category, "Todas las categorías", StringComparison.CurrentCultureIgnoreCase))
            items = items.Where(item => string.Equals(item.Category, category, StringComparison.CurrentCultureIgnoreCase));

        if (_namFavoritesOnly.Checked) items = items.Where(item => item.Favorite);
        return items;
    }

    private void RefreshNamBankCombo(string? selectId = null)
    {
        _loadingNamBank = true;
        try
        {
            string? selectedId = (_namBankCombo.SelectedItem as NamLibraryItem)?.Id;
            string? wanted = selectId ?? selectedId ?? _activeNamBankId;
            List<NamLibraryItem> visible = FilteredNamItems().ToList();

            _namBankCombo.Items.Clear();
            foreach (NamLibraryItem item in visible) _namBankCombo.Items.Add(item);

            int index = -1;
            if (!string.IsNullOrWhiteSpace(wanted))
            {
                for (int i = 0; i < _namBankCombo.Items.Count; i++)
                {
                    if (_namBankCombo.Items[i] is NamLibraryItem candidate &&
                        string.Equals(candidate.Id, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }
            if (index < 0 && _namBankCombo.Items.Count > 0) index = 0;
            _namBankCombo.SelectedIndex = index;

            int total = _namLibrary.Items.Count;
            int favorites = _namLibrary.Items.Count(item => item.Favorite);
            _namBankFilterStatus.Text = $"Mostrando {visible.Count} de {total} capturas. Favoritas: {favorites}.";
        }
        finally
        {
            _loadingNamBank = false;
        }

        bool hasItems = _namBankCombo.Items.Count > 0;
        _loadNamBankButton.Enabled = hasItems;
        _previousNamButton.Enabled = hasItems;
        _nextNamButton.Enabled = hasItems;
        _removeNamBankButton.Enabled = hasItems;
        _saveNamMetadataButton.Enabled = hasItems;
        _namDisplayName.Enabled = hasItems;
        _namCategoryEdit.Enabled = hasItems;
        _namFavorite.Enabled = hasItems;
        UpdateNamMetadataEditorFromSelection();
        if (!_loadingGuitar1NamBank) RefreshGuitar1NamBankCombo(_guitar1ActiveNamBankId);
        if (!_updatingDualQuickNam) RefreshDualQuickNamSelector();
    }

    private void UpdateNamMetadataEditorFromSelection()
    {
        if (_loadingNamBank) return;
        if (_namBankCombo.SelectedItem is not NamLibraryItem item)
        {
            _namDisplayName.Text = string.Empty;
            _namCategoryEdit.Text = string.Empty;
            _namFavorite.Checked = false;
            return;
        }

        _namDisplayName.Text = item.Name;
        _namCategoryEdit.Text = item.Category ?? string.Empty;
        _namFavorite.Checked = item.Favorite;
    }

    private void SaveSelectedNamMetadata()
    {
        if (_namBankCombo.SelectedItem is not NamLibraryItem item)
        {
            SetStatus("No hay una captura NAM seleccionada para organizar.", true);
            return;
        }

        string name = string.IsNullOrWhiteSpace(_namDisplayName.Text)
            ? Path.GetFileNameWithoutExtension(item.Path)
            : _namDisplayName.Text.Trim();
        if (name.Length > 80) name = name[..80];

        bool duplicate = _namLibrary.Items.Any(candidate =>
            !string.Equals(candidate.Id, item.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (duplicate)
        {
            SetStatus($"Ya existe otra captura NAM llamada {name}. Use un nombre diferente.", true);
            _namDisplayName.Focus();
            return;
        }

        string category = NamLibraryStore.NormalizeCategory(_namCategoryEdit.Text);
        if (string.Equals(category, "Sin categoría", StringComparison.CurrentCultureIgnoreCase) ||
            string.Equals(category, "Todas las categorías", StringComparison.CurrentCultureIgnoreCase))
            category = string.Empty;
        item.Name = name;
        item.Category = category;
        item.Favorite = _namFavorite.Checked;
        try
        {
            NamLibraryStore.Save(_namLibrary);
            RefreshNamCategoryFilter();
            RefreshNamBankCombo(item.Id);
            string categoryText = string.IsNullOrWhiteSpace(category) ? "sin categoría" : $"categoría {category}";
            string favoriteText = item.Favorite ? ", favorita" : string.Empty;
            SetStatus($"Captura NAM {name} guardada: {categoryText}{favoriteText}.");
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo guardar la organización del Banco NAM: {ex.Message}", true);
        }
    }

    private void ClearNamFilters()
    {
        _namBankFilterText.Clear();
        _namFavoritesOnly.Checked = false;
        RefreshNamCategoryFilter("Todas las categorías");
        RefreshNamBankCombo(_activeNamBankId);
        SetStatus("Filtros del Banco NAM limpiados.");
    }

    private void SelectNamBankItem(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        _activeNamBankId = id;
        RefreshNamBankCombo(id);
    }

    private bool EditingGuitar1NamSettings => _namEditGuitarCombo.SelectedIndex != 1;

    private void UpdateNamSettingsGuitarContext()
    {
        bool guitar1 = EditingGuitar1NamSettings;
        _namGuitar1AdvancedGroup.Visible = guitar1;
        _namGuitar2AdvancedGroup.Visible = !guitar1;
        _namAdvancedHost.AccessibleName = guitar1 ? "Ajustes NAM avanzados de Guitarra 1" : "Ajustes NAM avanzados de Guitarra 2";
        _namBankCombo.AccessibleDescription = guitar1
            ? "Biblioteca compartida. Cargar seleccionada, anterior, siguiente o Enter aplican la captura a Guitarra 1 mientras esta guitarra esté elegida en Alt+N."
            : "Biblioteca compartida. Cargar seleccionada, anterior, siguiente o Enter aplican la captura a Guitarra 2 mientras esta guitarra esté elegida en Alt+N.";
        string guitarName = guitar1 ? "Guitarra 1" : "Guitarra 2";
        _loadNamBankButton.AccessibleName = $"Cargar captura NAM seleccionada en {guitarName}";
        _previousNamButton.AccessibleName = $"Cargar captura NAM anterior en {guitarName}";
        _nextNamButton.AccessibleName = $"Cargar captura NAM siguiente en {guitarName}";
        _loadNamButton.AccessibleName = $"Cargar archivo punto NAM en {guitarName}";
        _loadLatestNamButton.AccessibleName = $"Cargar la última descarga NAM en {guitarName}";
        _clearNamButton.AccessibleName = $"Quitar NAM de {guitarName}";

        _guitar1NamPath.TabStop = guitar1;
        _guitar1NamIncludesCabinet.TabStop = guitar1;
        _guitar1NamInputTrim.TabStop = guitar1;
        _guitar1NamOutputTrim.TabStop = guitar1;
        _guitar1NamAutoLevel.TabStop = guitar1;
        _guitar1NamAutoLevelDb.TabStop = guitar1;

        _namPath.TabStop = !guitar1;
        _namIncludesCabinet.TabStop = !guitar1;
        _namInputTrim.TabStop = !guitar1;
        _namOutputTrim.TabStop = !guitar1;
        _namAutoLevel.TabStop = !guitar1;
        _namAutoLevelDb.TabStop = !guitar1;
        _calibrateNamLevelButton.TabStop = !guitar1;
        _namLevelStatus.TabStop = !guitar1;
        RefreshNamStatus();
    }

    private void LoadNamModelFromDialogForSelectedGuitar()
    {
        if (EditingGuitar1NamSettings) LoadGuitar1NamModelFromDialog();
        else LoadNamModelFromDialog();
        RefreshNamStatus();
    }

    private void ClearNamModelForSelectedGuitar()
    {
        if (EditingGuitar1NamSettings) ClearGuitar1NamModel();
        else ClearNamModel();
        RefreshNamStatus();
    }

    private void RegisterAndLoadNamModelForSelectedGuitar(string sourcePath, bool announce)
    {
        if (!EditingGuitar1NamSettings)
        {
            RegisterAndLoadNamModel(sourcePath, announce);
            return;
        }

        try
        {
            NamLibraryItem item = NamLibraryStore.Import(
                _namLibrary,
                sourcePath,
                _guitar1NamIncludesCabinet.Checked,
                (float)_guitar1NamInputTrim.Value,
                (float)_guitar1NamOutputTrim.Value);
            _guitar1ActiveNamBankId = item.Id;
            RefreshNamCategoryFilter();
            RefreshNamBankCombo(item.Id);
            RefreshGuitar1NamBankCombo(item.Id);
            ApplyGuitar1NamBankDefaults(item);
            TryLoadGuitar1NamModel(item.Path, announce, enableAfterLoad: true, resumeAudioAfterLoad: true);
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo incorporar la captura NAM a Guitarra 1: {ex.Message}", true);
        }
    }

    private void LoadSelectedNamFromBankForSelectedGuitar()
    {
        if (_namBankCombo.SelectedItem is not NamLibraryItem item)
        {
            SetStatus("El banco NAM está vacío. Cargue o descargue una captura primero.", true);
            return;
        }

        if (EditingGuitar1NamSettings)
        {
            LoadGuitar1NamBankItem(item, announce: true);
            RefreshNamBankCombo(item.Id);
        }
        else
        {
            LoadNamBankItem(item, announce: true);
        }
        RefreshNamStatus();
    }

    private void LoadAdjacentNamFromBankForSelectedGuitar(int direction)
    {
        if (_namBankCombo.Items.Count == 0)
        {
            SetStatus("El banco NAM está vacío.", true);
            return;
        }

        int index = _namBankCombo.SelectedIndex;
        if (index < 0) index = 0;
        index = (index + (direction < 0 ? -1 : 1) + _namBankCombo.Items.Count) % _namBankCombo.Items.Count;
        _namBankCombo.SelectedIndex = index;
        LoadSelectedNamFromBankForSelectedGuitar();
    }

    private void LoadSelectedNamFromBank()
    {
        if (_namBankCombo.SelectedItem is not NamLibraryItem item)
        {
            SetStatus("El banco NAM está vacío. Cargue o descargue una captura primero.", true);
            return;
        }
        LoadNamBankItem(item, announce: true);
    }

    private void LoadAdjacentNamFromBank(int direction)
    {
        if (_namBankCombo.Items.Count == 0)
        {
            SetStatus("El banco NAM está vacío.", true);
            return;
        }

        int index = _namBankCombo.SelectedIndex;
        if (index < 0) index = 0;
        index = (index + (direction < 0 ? -1 : 1) + _namBankCombo.Items.Count) % _namBankCombo.Items.Count;
        _namBankCombo.SelectedIndex = index;
        LoadSelectedNamFromBank();
    }

    private void LoadNamBankItem(NamLibraryItem item, bool announce)
    {
        if (!File.Exists(item.Path))
        {
            SetStatus($"No se encontró la captura {item.Name}. Se quitará del banco.", true);
            NamLibraryStore.Remove(_namLibrary, item, deleteManagedFile: false);
            RefreshNamCategoryFilter();
            RefreshNamBankCombo();
            return;
        }

        UpdateActiveNamBankSettings();
        _activeNamBankId = item.Id;
        ApplyNamBankSettings(item);
        RefreshNamBankCombo(item.Id);
        TryLoadNamModel(item.Path, announce, enableAfterLoad: true, resumeAudioAfterLoad: true);
    }

    private void ApplyNamBankSettings(NamLibraryItem item)
    {
        _namIncludesCabinet.Checked = item.IncludesCabinet;
        SetNumeric(_namInputTrim, item.InputTrimDb);
        SetNumeric(_namOutputTrim, item.OutputTrimDb);
        _namAutoLevel.Checked = item.AutoLevelEnabled;
        SetNumeric(_namAutoLevelDb, item.AutoLevelDb);
        _namLevelStatus.Text = item.LastCalibrationRmsDb > -119f
            ? $"Calibrado. Nivel medido {item.LastCalibrationRmsDb:0.0} dBFS. Compensación {item.AutoLevelDb:+0.0;-0.0;0.0} dB."
            : "Auto Level sin calibrar.";
    }

    private void UpdateActiveNamBankSettings()
    {
        if (string.IsNullOrWhiteSpace(_activeNamBankId)) return;
        NamLibraryItem? item = _namLibrary.Items.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, _activeNamBankId, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        item.IncludesCabinet = _namIncludesCabinet.Checked;
        item.InputTrimDb = (float)_namInputTrim.Value;
        item.OutputTrimDb = (float)_namOutputTrim.Value;
        item.AutoLevelEnabled = _namAutoLevel.Checked;
        item.AutoLevelDb = (float)_namAutoLevelDb.Value;
        try { NamLibraryStore.Save(_namLibrary); } catch { }
    }

    private void StartNamLevelCalibration()
    {
        if (!_engine.IsRunning)
        {
            SetStatus("Inicie el audio con F4 antes de calibrar el nivel NAM.", true);
            return;
        }
        if (!_namEnabled.Checked || !_engine.Processor.HasNamModel)
        {
            SetStatus("Cargue y active un modelo NAM antes de calibrar.", true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_activeNamBankId))
        {
            SetStatus("La captura NAM activa no pertenece al Banco NAM.", true);
            return;
        }

        _namAutoLevel.Checked = false;
        SetNumeric(_namAutoLevelDb, 0f);
        UpdateParameters();
        _engine.Processor.StartNamLevelCalibration(4.0);
        _calibrateNamLevelButton.Enabled = false;
        _namLevelStatus.Text = "Calibrando durante 4 segundos. Toque acordes y notas con su intensidad normal.";
        SetStatus("Calibración NAM iniciada. Toque normalmente durante cuatro segundos.");
    }

    private void CompleteNamLevelCalibrationIfReady()
    {
        if (!_engine.Processor.TryConsumeNamLevelCalibration(out float rmsDb)) return;

        _calibrateNamLevelButton.Enabled = true;
        if (rmsDb <= -70f)
        {
            _namLevelStatus.Text = "Calibración inválida: nivel demasiado bajo. Vuelva a calibrar tocando la guitarra.";
            SetStatus("No se pudo calibrar Auto Level NAM porque la señal fue demasiado baja.", true);
            return;
        }

        const float targetRmsDb = -18.0f;
        float compensation = Math.Clamp(targetRmsDb - rmsDb, -12f, 12f);
        SetNumeric(_namAutoLevelDb, compensation);
        _namAutoLevel.Checked = true;

        NamLibraryItem? item = _namLibrary.Items.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, _activeNamBankId, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            item.AutoLevelEnabled = true;
            item.AutoLevelDb = compensation;
            item.LastCalibrationRmsDb = rmsDb;
            try { NamLibraryStore.Save(_namLibrary); } catch { }
        }

        UpdateParameters();
        _namLevelStatus.Text = $"Calibrado. Nivel medido {rmsDb:0.0} dBFS. Compensación {compensation:+0.0;-0.0;0.0} dB.";
        SetStatus($"Auto Level NAM calibrado. Compensación aplicada {compensation:+0.0;-0.0;0.0} decibeles.");
    }

    private void RemoveSelectedNamFromBank()
    {
        if (_namBankCombo.SelectedItem is not NamLibraryItem item) return;
        DialogResult answer = MessageBox.Show(
            this,
            $"¿Quitar {item.Name} del banco NAM? El archivo administrado también se eliminará de la biblioteca local.",
            "Quitar captura NAM",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        bool wasActive = string.Equals(item.Id, _activeNamBankId, StringComparison.OrdinalIgnoreCase);
        bool wasActiveGuitar1 = string.Equals(item.Id, _guitar1ActiveNamBankId, StringComparison.OrdinalIgnoreCase);
        if (wasActive)
        {
            ClearNamModel();
            _activeNamBankId = null;
        }
        if (wasActiveGuitar1)
        {
            // La biblioteca es un catálogo compartido, pero cada guitarra mantiene su
            // propia instancia nativa. Antes de borrar el archivo administrado debemos
            // descargar también la instancia de Guitarra 1 para no dejar una ruta huérfana.
            if (!wasActive) StopAudioForNamChange();
            _guitar1NamEnabled.Checked = false;
            _engine.Guitar1Processor.ClearNamModel();
            _audioPreferences.Guitar1NamModelPath = string.Empty;
            _guitar1ActiveNamBankId = null;
            _guitar1NamPath.Text = "Guitarra 1: ningún modelo NAM cargado.";
            _guitar1NamClearButton.Enabled = false;
        }
        NamLibraryStore.Remove(_namLibrary, item, deleteManagedFile: true);
        RefreshNamCategoryFilter();
        RefreshNamBankCombo();
        UpdateParameters();
        SaveAudioPreferences();
        string affected = wasActive && wasActiveGuitar1
            ? " Se descargó de Guitarra 1 y Guitarra 2."
            : wasActiveGuitar1
                ? " Se descargó de Guitarra 1; Guitarra 2 no fue modificada."
                : wasActive
                    ? " Se descargó de Guitarra 2; Guitarra 1 no fue modificada."
                    : string.Empty;
        SetStatus($"{item.Name} fue quitada del banco NAM.{affected}");
    }

    private void OpenNamBankFolder()
    {
        try
        {
            Directory.CreateDirectory(NamLibraryStore.ManagedModelsFolder);
            Process.Start(new ProcessStartInfo { FileName = NamLibraryStore.ManagedModelsFolder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo abrir la carpeta del banco NAM: {ex.Message}", true);
        }
    }

    private void TryLoadNamModel(string path, bool announce, bool enableAfterLoad, bool resumeAudioAfterLoad = false)
    {
        bool wasRunning = resumeAudioAfterLoad && (_audioRequested || _engine.HasActiveSession);
        StopAudioForNamChange();
        try
        {
            var info = _engine.Processor.LoadNamModel(path);
            _namPath.Text = path;
            _audioPreferences.NamModelPath = path;
            _clearNamButton.Enabled = true;
            _namEnabled.Checked = enableAfterLoad;
            string rate = info.SampleRate > 1000f ? $"{info.SampleRate:0} Hz" : "frecuencia no declarada";
            string loadMode = info.LoadMode switch
            {
                0 => "NeuralAudio interno",
                1 => "RTNeural",
                2 => "NAM Core",
                _ => $"modo {info.LoadMode}"
            };
            _namStatus.Text = $"Modelo cargado: {info.FileName}. {rate}. Ajuste recomendado de entrada {info.RecommendedInputDb:+0.0;-0.0;0.0} dB; salida original {info.RecommendedOutputDb:+0.0;-0.0;0.0} dB. Nivel seguro NAM: margen fijo -9,0 dB y refuerzo recomendado automático limitado a +3,0 dB. Motor: {loadMode}; {(info.IsStatic ? "implementación estática" : "implementación dinámica/Core")}.";
            NamLibraryItem? bankItem = NamLibraryStore.FindByPath(_namLibrary, path);
            if (bankItem is not null)
            {
                _activeNamBankId = bankItem.Id;
                RefreshNamBankCombo(bankItem.Id);
            }
            UpdateParameters();
            SaveAudioPreferences();
            if (wasRunning)
            {
                ToggleAudio();
                if (_engine.IsRunning && (_drumsEnabled.Checked || _backingBassEnabled.Checked || _pianoEnabled.Checked))
                {
                    // El precalentamiento del DSP recorre los generadores; al cargar NAM
                    // los reiniciamos después de arrancar ASIO para que batería, bajo y piano
                    // vuelvan juntos exactamente desde el primer tiempo.
                    _engine.RequestMetronomeReset();
                }
            }
            RefreshDualQuickNamSelector(_activeNamBankId);
            if (announce)
            {
                SetStatus(wasRunning
                    ? $"NAM cargado: {info.FileName}. Audio reiniciado con la nueva captura. Nivel seguro NAM menos nueve decibeles activo."
                    : $"NAM cargado: {info.FileName}. Nivel seguro NAM menos nueve decibeles activo. Pulse F4 para iniciar el audio.");
            }
        }
        catch (Exception ex)
        {
            _namEnabled.Checked = false;
            _clearNamButton.Enabled = _engine.Processor.HasNamModel;
            _namStatus.Text = $"No se pudo cargar NAM: {ex.Message}";
            if (announce) SetStatus($"No se pudo cargar el modelo NAM: {ex.Message}", true);
        }
    }

    private void ClearNamModel()
    {
        StopAudioForNamChange();
        _namEnabled.Checked = false;
        _engine.Processor.ClearNamModel();
        _audioPreferences.NamModelPath = string.Empty;
        _namPath.Text = "Ningún modelo NAM cargado.";
        _clearNamButton.Enabled = false;
        RefreshNamStatus();
        UpdateParameters();
        SaveAudioPreferences();
        RefreshDualQuickNamSelector();
        SetStatus("Modelo NAM quitado. Se vuelve a usar el amplificador interno.");
    }

    private void StopAudioForNamChange()
    {
        if (_audioRequested || _engine.HasActiveSession)
        {
            SavePracticeRecordingBeforeAudioStop();
            _audioRequested = false;
            _engine.Stop();
            SetRunningState(false);
            UpdateActualBufferLabel();
        }
    }

    private void ValidateNamEnableState()
    {
        if (_namEnabled.Checked && !_engine.Processor.HasNamModel)
        {
            _namEnabled.Checked = false;
            SetStatus("No se puede activar NAM porque no hay un modelo válido cargado.", true);
            return;
        }
        SaveAudioPreferences();
    }

    private void RefreshNamStatus(bool announce = false)
    {
        bool guitar1 = EditingGuitar1NamSettings;
        var processor = guitar1 ? _engine.Guitar1Processor : _engine.Processor;
        string guitarName = guitar1 ? "Guitarra 1" : "Guitarra 2";
        bool available = processor.IsNamNativeEngineAvailable(out string description);
        if (processor.HasNamModel)
        {
            string model = Path.GetFileName(processor.NamModelPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(model)) model = "modelo cargado";
            _namStatus.Text = $"{guitarName}: motor NAM disponible. Modelo cargado: {model}.";
            _clearNamButton.Enabled = true;
        }
        else
        {
            _namStatus.Text = $"{guitarName}: {description}";
            _clearNamButton.Enabled = false;
        }
        _loadNamButton.Enabled = available;
        if (announce) SetStatus(_namStatus.Text, !available);
    }

    private void OpenNamGuide()
    {
        string guide = Path.Combine(AppContext.BaseDirectory, "LEEME_NAM.txt");
        if (!File.Exists(guide))
        {
            guide = Path.Combine(Environment.CurrentDirectory, "LEEME_NAM.txt");
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = guide,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo abrir la guía NAM: {ex.Message}", true);
        }
    }

    private void NamBankCombo_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        e.Handled = true;
        LoadSelectedNamFromBankForSelectedGuitar();
    }

    private void NamSearchQuery_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        SearchNamCaptures();
    }

    private void SearchNamCaptures()
    {
        string query = _namSearchQuery.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            SetStatus("Escriba qué amplificador, pedal o captura NAM quiere buscar.", true);
            _namSearchQuery.Focus();
            return;
        }

        try
        {
            string url = $"https://www.tone3000.com/search?q={Uri.EscapeDataString(query)}&format=nam";
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            SetStatus($"Buscando capturas NAM de {query} en TONE3000. Cuando termine la descarga, vuelva a Amp Accessible y pulse Cargar última descarga NAM.");
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo abrir el buscador TONE3000: {ex.Message}", true);
        }
    }

    private void LoadLatestDownloadedNam()
    {
        try
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloads))
            {
                SetStatus("No se encontró la carpeta Descargas de Windows.", true);
                return;
            }

            FileInfo? latest = null;
            foreach (FileInfo candidate in new DirectoryInfo(downloads)
                         .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                         .Where(file => file.Extension.Equals(".nam", StringComparison.OrdinalIgnoreCase) ||
                                        file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(file => file.LastWriteTimeUtc)
                         .Take(30))
            {
                if (candidate.Extension.Equals(".nam", StringComparison.OrdinalIgnoreCase) || ZipContainsNam(candidate.FullName))
                {
                    latest = candidate;
                    break;
                }
            }

            if (latest is null)
            {
                SetStatus("No encontré archivos NAM ni ZIP con capturas en la carpeta Descargas.", true);
                return;
            }

            if (latest.Extension.Equals(".nam", StringComparison.OrdinalIgnoreCase))
            {
                RegisterAndLoadNamModelForSelectedGuitar(latest.FullName, announce: true);
                return;
            }

            LoadNamFromDownloadedZip(latest.FullName);
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo revisar la última descarga NAM: {ex.Message}", true);
        }
    }

    private static bool ZipContainsNam(string zipPath)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
            return archive.Entries.Any(entry => !string.IsNullOrWhiteSpace(entry.Name) &&
                                                entry.Name.EndsWith(".nam", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private void LoadNamFromDownloadedZip(string zipPath)
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string root = Path.Combine(documents, "GDM Amp Accessible", "Capturas NAM");
        Directory.CreateDirectory(root);
        string baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(zipPath));
        string destination = Path.Combine(root, $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(destination);

        var extracted = new List<string>();
        using (var archive = System.IO.Compression.ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) ||
                    !entry.Name.EndsWith(".nam", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string safeName = SanitizeFileName(entry.Name);
                string output = GetUniqueFilePath(destination, safeName);
                using (Stream input = entry.Open())
                using (FileStream outputStream = File.Create(output))
                {
                    input.CopyTo(outputStream);
                }
                extracted.Add(output);
            }
        }

        if (extracted.Count == 0)
        {
            SetStatus($"El ZIP {Path.GetFileName(zipPath)} no contiene archivos punto NAM.", true);
            return;
        }

        if (extracted.Count == 1)
        {
            RegisterAndLoadNamModelForSelectedGuitar(extracted[0], announce: true);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = $"El paquete contiene {extracted.Count} modelos NAM. Elija uno",
            Filter = "Modelos Neural Amp Modeler (*.nam)|*.nam",
            InitialDirectory = destination,
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            RegisterAndLoadNamModelForSelectedGuitar(dialog.FileName, announce: true);
        }
        else
        {
            SetStatus($"Se extrajeron {extracted.Count} capturas NAM en {destination}. No se cargó ninguna todavía.");
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "captura_nam" : name;
    }

    private static string GetUniqueFilePath(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return path;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int index = 2; index < 1000; index++)
        {
            path = Path.Combine(directory, $"{stem}_{index}{extension}");
            if (!File.Exists(path)) return path;
        }
        return Path.Combine(directory, $"{stem}_{Guid.NewGuid():N}{extension}");
    }

    private void ClearImpulseResponse()
    {
        _engine.Processor.ClearImpulseResponse();
        _loadedIrPath = null;
        _externalIrEnabled.Checked = false;
        _externalIrEnabled.Enabled = false;
        _clearIrButton.Enabled = false;
        _irPath.Text = "IR A: interno estilo V30.";
        UpdateParameters();
        SetStatus("IR A externo quitado. La ruta A volvió al gabinete interno estilo V30.");
    }

    private void ClearImpulseResponseB()
    {
        _engine.Processor.ClearImpulseResponseB();
        _loadedIrPathB = null;
        _externalIrBEnabled.Checked = false;
        _externalIrBEnabled.Enabled = false;
        _clearIrBButton.Enabled = false;
        _irPathB.Text = "Ningún IR B cargado.";
        UpdateParameters();
        SetStatus("IR B quitado. La mezcla queda solamente con el gabinete A.");
    }

    private void ScheduleParameterUpdate()
    {
        if (_loadingScene)
        {
            return;
        }

        _parameterUpdateTimer.Stop();
        _parameterUpdateTimer.Start();
    }

    private void UpdateParameters()
    {
        if (_loadingScene || _channelCombo.SelectedIndex < 0)
        {
            return;
        }

        // 2.41.14: la sección Efectos tiene una memoria por guitarra. Los controles
        // visibles pertenecen a la guitarra seleccionada en Alt+O. Antes de construir
        // los parámetros guardamos ese estado para que la otra cadena no sea pisada.
        if (_twoGuitarMode.Checked && _dualEffectMemoriesInitialized && !_loadingDualEffectMemory)
            StoreVisibleEffectsForGuitar(Math.Clamp(_dualEditGuitarCombo.SelectedIndex, 0, 1));

        // 2.41.47: analizar la progresión fuera del callback. Los bloques (I,V,vi,IV)xN
        // se expanden aquí y el DSP recibe únicamente códigos numéricos inmutables.
        if (!TryEncodeCustomPianoProgression(_pianoCustomProgression.Text,
            out ulong pianoPack1, out ulong pianoPack2, out ulong pianoPack3, out ulong pianoPack4,
            out ulong pianoPack5, out ulong pianoPack6, out ulong pianoPack7, out ulong pianoPack8,
            out ushort[] pianoCustomSequence, out ulong pianoCustomHash,
            out int pianoCustomCount, out _, out _, out _))
        {
            TryEncodeCustomPianoProgression("I, V, vi, IV",
                out pianoPack1, out pianoPack2, out pianoPack3, out pianoPack4,
                out pianoPack5, out pianoPack6, out pianoPack7, out pianoPack8,
                out pianoCustomSequence, out pianoCustomHash,
                out pianoCustomCount, out _, out _, out _);
        }

        var parameters = new DspParameters
        {
            Revision = Interlocked.Increment(ref _revision),
            SimulationEnabled = _twoGuitarMode.Checked || _simulationEnabled.Checked,
            Channel = (AmpChannel)_channelCombo.SelectedIndex,
            Gain = (float)_gain.Value,
            Bass = (float)_bass.Value,
            Middle = (float)_middle.Value,
            Treble = (float)_treble.Value,
            Presence = (float)_presence.Value,

            CleanBass = _channelEq[0, 0],
            CleanMiddle = _channelEq[0, 1],
            CleanTreble = _channelEq[0, 2],
            CleanPresence = _channelEq[0, 3],
            CrunchBass = _channelEq[1, 0],
            CrunchMiddle = _channelEq[1, 1],
            CrunchTreble = _channelEq[1, 2],
            CrunchPresence = _channelEq[1, 3],
            LeadBass = _channelEq[2, 0],
            LeadMiddle = _channelEq[2, 1],
            LeadTreble = _channelEq[2, 2],
            LeadPresence = _channelEq[2, 3],

            OutputPercent = (float)_output.Value,
            VoiceEnabled = !_twoGuitarMode.Checked && _voiceEnabled.Checked,
            VoiceOnlyMode = !_twoGuitarMode.Checked && _voiceOnlyMode.Checked,
            VoiceSuppressorEnabled = _voiceSuppressorEnabled.Checked,
            VoiceThresholdDb = (float)_voiceThreshold.Value,
            VoiceReductionDb = (float)_voiceReduction.Value,
            VoiceReleaseMs = (float)_voiceRelease.Value,
            VoiceHighPassHz = (float)_voiceHighPass.Value,
            VoiceBassDb = (float)_voiceBass.Value,
            VoiceMidDb = (float)_voiceMid.Value,
            VoiceTrebleDb = (float)_voiceTreble.Value,
            VoiceLevelPercent = (float)_voiceLevel.Value,
            VoiceMonitorPercent = (float)_voiceMonitorLevel.Value,
            MeetGuitarPercent = (float)_meetGuitarLevel.Value,
            MeetVoicePercent = (float)_meetVoiceLevel.Value,
            TunerEnabled = _tunerEnabled.Checked && (!_twoGuitarMode.Checked || SelectedTunerGuitarIndex == 1),
            TunerMuteOutput = _tunerMuteOutput.Checked,
            TunerSoundGuideEnabled = _tunerSoundGuide.Checked,
            TunerReferenceAHz = (float)_tunerReferenceA.Value,
            TunerGuideVolumePercent = (float)_tunerGuideVolume.Value,
            MetronomeEnabled = _metronomeEnabled.Checked,
            MetronomeBpm = (float)_metronomeBpm.Value,
            MetronomeBeatsPerBar = SelectedMetronomeBeatsPerBar,
            MetronomeAccentFirstBeat = _metronomeAccent.Checked,
            MetronomeVolumePercent = (float)_metronomeVolume.Value,
            DrumsEnabled = _drumsEnabled.Checked,
            DrumPattern = Math.Clamp(_drumPattern.SelectedIndex, 0, 5),
            DrumVolumePercent = (float)_drumVolume.Value,
            BackingBassEnabled = _backingBassEnabled.Checked,
            BackingBassKey = Math.Clamp(_backingBassKey.SelectedIndex, 0, 11),
            BackingBassMinor = _backingBassMode.SelectedIndex == 1,
            BackingBassLine = Math.Clamp(_backingBassLine.SelectedIndex, 0, 4),
            BackingBassVolumePercent = (float)_backingBassVolume.Value,
            PianoEnabled = _pianoEnabled.Checked,
            PianoSound = Math.Clamp(_pianoSound.SelectedIndex, 0, 5),
            PianoKey = Math.Clamp(_pianoKey.SelectedIndex, 0, 23),
            PianoProgression = Math.Clamp(_pianoProgression.SelectedIndex, 0, 5),
            PianoCustomProgressionPack1 = pianoPack1,
            PianoCustomProgressionPack2 = pianoPack2,
            PianoCustomProgressionPack3 = pianoPack3,
            PianoCustomProgressionPack4 = pianoPack4,
            PianoCustomProgressionPack5 = pianoPack5,
            PianoCustomProgressionPack6 = pianoPack6,
            PianoCustomProgressionPack7 = pianoPack7,
            PianoCustomProgressionPack8 = pianoPack8,
            PianoCustomProgressionCount = pianoCustomCount,
            PianoCustomProgressionSequence = pianoCustomSequence,
            PianoCustomProgressionHash = pianoCustomHash,
            PianoStyle = Math.Clamp(_pianoStyle.SelectedIndex, 0, 8),
            PianoVolumePercent = (float)_pianoVolume.Value,
            // F12 deja los tres generadores armados. Sólo cuando los tres están activos
            // el bus sigue automáticamente la actividad de la guitarra. Los controles
            // individuales conservan el comportamiento continuo histórico.
            AccompanimentFollowGuitar = _backingBandFollowGuitarArmed && _drumsEnabled.Checked && _backingBassEnabled.Checked && _pianoEnabled.Checked,
            OctaverEnabled = _octaverEnabled.Checked,
            OctaverCharacter = (OctaverCharacter)Math.Max(0, _octaverCharacterCombo.SelectedIndex),
            OctaverDryPercent = (float)_octaverDry.Value,
            OctaverDownPercent = (float)_octaverDown.Value,
            OctaverUpPercent = (float)_octaverUp.Value,
            OctaverTonePercent = (float)_octaverTone.Value,
            OctaverLevelPercent = (float)_octaverLevel.Value,
            GateEnabled = _gateEnabled.Checked,
            GateThresholdDb = (float)_gateThreshold.Value,
            GateReleaseMs = (float)_gateRelease.Value,
            CompressorEnabled = _compressorEnabled.Checked,
            CompressorCharacter = (CompressorCharacter)Math.Max(0,_compressorCharacterCombo.SelectedIndex),
            CompressorSustain = (float)_compressorSustain.Value,
            CompressorAttackMs = (float)_compressorAttack.Value,
            CompressorLevel = (float)_compressorLevel.Value,
            AutoWahEnabled = _autoWahEnabled.Checked,
            AutoWahMode = (AutoWahMode)Math.Clamp(_autoWahModeCombo.SelectedIndex, 0, 1),
            AutoWahCharacter = (AutoWahCharacter)Math.Clamp(_autoWahCharacterCombo.SelectedIndex, 0, 2),
            AutoWahSensitivity = (float)_autoWahSensitivity.Value,
            AutoWahRange = (float)_autoWahRange.Value,
            AutoWahResonance = (float)_autoWahResonance.Value,
            AutoWahManualPositionPercent = (float)_autoWahManualPosition.Value,
            OverdriveEnabled = _overdriveEnabled.Checked,
            DistortionCharacter = (DistortionCharacter)Math.Max(0,_distortionCharacterCombo.SelectedIndex),
            OverdriveGain = (float)_overdriveGain.Value,
            OverdriveTone = (float)_overdriveTone.Value,
            OverdriveLevel = (float)_overdriveLevel.Value,
            Ts9Enabled = _ts9Enabled.Checked,
            DriveCharacter = (DriveCharacter)Math.Max(0, _driveCharacterCombo.SelectedIndex),
            Ts9Gain = (float)_ts9Gain.Value,
            Ts9Tone = (float)_ts9Tone.Value,
            Ts9Level = (float)_ts9Level.Value,
            Od1Enabled = _od1Enabled.Checked,
            Od1Character = (Od1Character)Math.Max(0, _od1CharacterCombo.SelectedIndex),
            Od1Drive = (float)_od1Drive.Value,
            Od1Tone = (float)_od1Tone.Value,
            Od1Level = (float)_od1Level.Value,
            FuzzEnabled = _fuzzEnabled.Checked,
            FuzzCharacter = (FuzzCharacter)Math.Max(0, _fuzzCharacterCombo.SelectedIndex),
            FuzzGain = (float)_fuzzGain.Value,
            FuzzTone = (float)_fuzzTone.Value,
            FuzzLevel = (float)_fuzzLevel.Value,
            Eq5Enabled = _eq5Enabled.Checked,
            Eq5Placement = (EqPlacement)Math.Max(0, _eq5PlacementCombo.SelectedIndex),
            Eq5Band100Db = (float)_eq5Band100.Value,
            Eq5Band250Db = (float)_eq5Band250.Value,
            Eq5Band800Db = (float)_eq5Band800.Value,
            Eq5Band2500Db = (float)_eq5Band2500.Value,
            Eq5Band6400Db = (float)_eq5Band6400.Value,
            Eq5OutputDb = (float)_eq5Output.Value,
            BoosterEnabled = _boosterEnabled.Checked,
            BoosterCharacter = (BoosterCharacter)Math.Max(0, _boosterCharacterCombo.SelectedIndex),
            BoosterDb = (float)_boosterDb.Value,
            PreEffectOrder = _preEffectOrder.ToArray(),
            ExternalIrEnabled = _externalIrEnabled.Checked,
            ExternalIrBEnabled = _externalIrBEnabled.Checked,
            IrMixPercent = (float)_irMix.Value,
            IrBPhaseInvert = _irBPhaseInvert.Checked,
            IrLowCutHz = (float)_irLowCut.Value,
            IrHighCutHz = (float)_irHighCut.Value,
            NamEnabled = _namEnabled.Checked && _engine.Processor.HasNamModel,
            NamIncludesCabinet = _namIncludesCabinet.Checked,
            NamInputTrimDb = (float)_namInputTrim.Value,
            NamOutputTrimDb = (float)_namOutputTrim.Value,
            NamAutoLevelEnabled = _namAutoLevel.Checked,
            NamAutoLevelDb = (float)_namAutoLevelDb.Value,
            FxLoopEnabled = _fxLoopEnabled.Checked,
            FxLoopSendPercent = (float)_fxLoopSend.Value,
            FxLoopReturnPercent = (float)_fxLoopReturn.Value,
            PhaserEnabled = _phaserEnabled.Checked,
            PhaserRateHz = (float)_phaserRate.Value,
            PhaserDepthPercent = (float)_phaserDepth.Value,
            PhaserFeedbackPercent = (float)_phaserFeedback.Value,
            PhaserMixPercent = (float)_phaserMix.Value,
            FlangerEnabled = _flangerEnabled.Checked,
            FlangerCharacter = (FlangerCharacter)Math.Max(0, _flangerCharacterCombo.SelectedIndex),
            FlangerRateHz = (float)_flangerRate.Value,
            FlangerDepthPercent = (float)_flangerDepth.Value,
            FlangerFeedbackPercent = (float)_flangerFeedback.Value,
            FlangerMixPercent = (float)_flangerMix.Value,
            ChorusEnabled = _chorusEnabled.Checked,
            ChorusPlacement = (ChorusPlacement)Math.Max(0, _chorusPlacementCombo.SelectedIndex),
            ChorusCharacter = (ChorusCharacter)Math.Max(0, _chorusCharacterCombo.SelectedIndex),
            ChorusRateHz = (float)_chorusRate.Value,
            ChorusDepthMs = (float)_chorusDepth.Value,
            ChorusMixPercent = (float)_chorusMix.Value,
            AnalogChorusEnabled = _analogChorusEnabled.Checked,
            AnalogChorusPlacement = (ChorusPlacement)Math.Max(0, _analogChorusPlacementCombo.SelectedIndex),
            AnalogChorusRateHz = (float)_analogChorusRate.Value,
            AnalogChorusDepth = (float)_analogChorusDepth.Value,
            AnalogChorusMixPercent = (float)_analogChorusMix.Value,
            AnalogChorusLow = (float)_analogChorusLow.Value,
            AnalogChorusHigh = (float)_analogChorusHigh.Value,
            MicroPitchEnabled = _microPitchEnabled.Checked,
            MicroPitchDetuneCents = (float)_microPitchDetune.Value,
            MicroPitchDelayMs = (float)_microPitchDelay.Value,
            MicroPitchMixPercent = (float)_microPitchMix.Value,
            RotaryEnabled = _rotaryEnabled.Checked,
            RotarySyncEnabled = _rotarySync.Checked,
            RotaryDivision = (TempoDivision)Math.Max(0, _rotaryDivision.SelectedIndex),
            RotaryRateHz = _rotarySync.Checked ? SyncedRotaryHz() : 0f,
            RotaryFast = _rotaryFast.Checked,
            RotaryDepthPercent = (float)_rotaryDepth.Value,
            RotaryMixPercent = (float)_rotaryMix.Value,
            TremoloEnabled = _tremoloEnabled.Checked,
            TremoloSyncEnabled = _tremoloSync.Checked,
            TremoloDivision = (TempoDivision)Math.Max(0, _tremoloDivision.SelectedIndex),
            TremoloRateHz = _tremoloSync.Checked ? SyncedTremoloHz() : (float)_tremoloRate.Value,
            TremoloDepthPercent = (float)_tremoloDepth.Value,
            DelayEnabled = _delayEnabled.Checked,
            DelaySyncEnabled = _delaySync.Checked,
            DelayDivision = (TempoDivision)Math.Max(0, _delayDivision.SelectedIndex),
            DelayCharacter = (DelayCharacter)Math.Max(0, _delayCharacterCombo.SelectedIndex),
            DelayTimeMs = _delaySync.Checked ? SyncedDelayMilliseconds() : (float)_delayTime.Value,
            DelayFeedbackPercent = (float)_delayFeedback.Value,
            DelayMixPercent = (float)_delayMix.Value,
            ReverbEnabled = _reverbEnabled.Checked,
            ReverbCharacter = (ReverbCharacter)Math.Max(0, _reverbCharacterCombo.SelectedIndex),
            ReverbMixPercent = (float)_reverbMix.Value,
            ReverbDecayPercent = (float)_reverbDecay.Value,
            ReverbTonePercent = (float)_reverbTone.Value
        };

        // El rig principal pertenece a Guitarra 2. En modo dual sus efectos salen
        // siempre de su memoria, aunque la interfaz esté mostrando los de Guitarra 1.
        DspParameters guitar2Parameters = parameters;
        if (_twoGuitarMode.Checked && _dualEffectMemoriesInitialized && _guitar2EffectMemory is not null)
            guitar2Parameters = ApplyEffectMemoryToParameters(parameters, _guitar2EffectMemory);

        if (_twoGuitarMode.Checked)
        {
            // 2.41.19: metrónomo, batería, bajo y piano dejan de pertenecer a Guitarra 2.
            // El AudioEngine los genera una sola vez en un bus global posterior a ambas
            // cadenas, de modo que también funcionan con sólo Guitarra 1 y no se duplican.
            _engine.ConfigureDualAccompaniment(parameters);
            guitar2Parameters = guitar2Parameters with
            {
                MetronomeEnabled = false,
                DrumsEnabled = false,
                BackingBassEnabled = false,
                PianoEnabled = false
            };
        }
        else
        {
            // Fuera del modo dual el acompañamiento continúa exactamente en el procesador
            // principal, como en las versiones estables anteriores.
            _engine.ConfigureDualAccompaniment(parameters with
            {
                MetronomeEnabled = false,
                DrumsEnabled = false,
                BackingBassEnabled = false,
                PianoEnabled = false
            });
        }

        _engine.Processor.SetParameters(guitar2Parameters);
        _engine.ConfigureMasterVolume((float)_masterVolume.Value);

        // Guitarra 1 usa una segunda instancia DSP con su propia memoria de efectos.
        // Voz queda reservada al modo de clase. En modo dual, NAM, IR y efectos son independientes por guitarra; el acompañamiento sigue siendo global.
        DspParameters guitar1Parameters;
        if (_guitar1UseRigEffects.Checked)
        {
            guitar1Parameters = parameters with
            {
                Revision = Interlocked.Increment(ref _revision),
                SimulationEnabled = true,
                Channel = (AmpChannel)Math.Clamp(_guitar1AmpCombo.SelectedIndex, 0, 8),
                Gain = (float)_guitar1Gain.Value,
                Bass = 5.0f,
                Middle = 5.0f,
                Treble = 5.5f,
                Presence = 5.0f,
                OutputPercent = (float)_guitar1Output.Value,
                VoiceEnabled = false,
                VoiceOnlyMode = false,
                TunerEnabled = _twoGuitarMode.Checked && _tunerEnabled.Checked && SelectedTunerGuitarIndex == 0,
                TunerMuteOutput = _tunerMuteOutput.Checked,
                TunerSoundGuideEnabled = _tunerSoundGuide.Checked,
                TunerReferenceAHz = (float)_tunerReferenceA.Value,
                TunerGuideVolumePercent = (float)_tunerGuideVolume.Value,
                MetronomeEnabled = false,
                DrumsEnabled = false,
                BackingBassEnabled = false,
                PianoEnabled = false,
                ExternalIrEnabled = !string.IsNullOrWhiteSpace(_guitar1LoadedIrPath) && _engine.Guitar1Processor.HasExternalImpulse,
                ExternalIrBEnabled = false,
                NamEnabled = _guitar1NamEnabled.Checked && _engine.Guitar1Processor.HasNamModel,
                NamIncludesCabinet = _guitar1NamIncludesCabinet.Checked,
                NamInputTrimDb = (float)_guitar1NamInputTrim.Value,
                NamOutputTrimDb = (float)_guitar1NamOutputTrim.Value,
                NamAutoLevelEnabled = _guitar1NamAutoLevel.Checked,
                NamAutoLevelDb = (float)_guitar1NamAutoLevelDb.Value
            };

            if (_dualEffectMemoriesInitialized && _guitar1EffectMemory is not null)
                guitar1Parameters = ApplyEffectMemoryToParameters(guitar1Parameters, _guitar1EffectMemory);
        }
        else
        {
            guitar1Parameters = new DspParameters
            {
                Revision = Interlocked.Increment(ref _revision),
                SimulationEnabled = true,
                Channel = (AmpChannel)Math.Clamp(_guitar1AmpCombo.SelectedIndex, 0, 8),
                Gain = (float)_guitar1Gain.Value,
                Bass = 5.0f,
                Middle = 5.0f,
                Treble = 5.5f,
                Presence = 5.0f,
                OutputPercent = (float)_guitar1Output.Value,
                VoiceEnabled = false,
                VoiceOnlyMode = false,
                TunerEnabled = _twoGuitarMode.Checked && _tunerEnabled.Checked && SelectedTunerGuitarIndex == 0,
                TunerMuteOutput = _tunerMuteOutput.Checked,
                TunerSoundGuideEnabled = _tunerSoundGuide.Checked,
                TunerReferenceAHz = (float)_tunerReferenceA.Value,
                TunerGuideVolumePercent = (float)_tunerGuideVolume.Value,
                MetronomeEnabled = false,
                DrumsEnabled = false,
                BackingBassEnabled = false,
                PianoEnabled = false,
                GateEnabled = true,
                GateThresholdDb = -58f,
                GateReleaseMs = 180f,
                CompressorEnabled = false,
                AutoWahEnabled = false,
                OverdriveEnabled = false,
                Ts9Enabled = false,
                Od1Enabled = false,
                FuzzEnabled = false,
                Eq5Enabled = false,
                BoosterEnabled = false,
                OctaverEnabled = false,
                ExternalIrEnabled = !string.IsNullOrWhiteSpace(_guitar1LoadedIrPath) && _engine.Guitar1Processor.HasExternalImpulse,
                ExternalIrBEnabled = false,
                NamEnabled = _guitar1NamEnabled.Checked && _engine.Guitar1Processor.HasNamModel,
                NamIncludesCabinet = _guitar1NamIncludesCabinet.Checked,
                NamInputTrimDb = (float)_guitar1NamInputTrim.Value,
                NamOutputTrimDb = (float)_guitar1NamOutputTrim.Value,
                NamAutoLevelEnabled = _guitar1NamAutoLevel.Checked,
                NamAutoLevelDb = (float)_guitar1NamAutoLevelDb.Value,
                FxLoopEnabled = false,
                PhaserEnabled = false,
                FlangerEnabled = false,
                ChorusEnabled = false,
                AnalogChorusEnabled = false,
                RotaryEnabled = false,
                TremoloEnabled = false,
                DelayEnabled = false,
                ReverbEnabled = false
            };
        }
        _engine.Guitar1Processor.SetParameters(guitar1Parameters);
        if (_twoGuitarMode.Checked && _guitar1ProcessingEnabled.Checked)
            _engine.Guitar1Processor.ForceSimulationFullyOn();
        _engine.ConfigureTwoGuitarMode(_twoGuitarMode.Checked, _guitar1ProcessingEnabled.Checked, (float)_guitar1Mix.Value, (float)_guitar1Pan.Value, _guitar1Mute.Checked,
            (float)_guitar2Mix.Value, (float)_guitar2Pan.Value, _guitar2Mute.Checked);
    }

    private int SelectedMetronomeBeatsPerBar => _metronomeMeter.SelectedIndex switch
    {
        0 => 2,
        1 => 3,
        3 => 6,
        _ => 4
    };

    private int SelectedLoopBars => _loopBars.SelectedIndex switch
    {
        0 => 1,
        1 => 2,
        3 => 8,
        _ => 4
    };

    private int SelectedLoopCaptureSource => Math.Clamp(_loopSourceCombo.SelectedIndex, 0, 2);

    private string CurrentLoopCaptureSourceName => !_twoGuitarMode.Checked
        ? "rig principal / Guitarra 2"
        : SelectedLoopCaptureSource switch
        {
            0 => "Guitarra 1 / Input 1",
            1 => "Guitarra 2 / Input 2",
            _ => "ambas guitarras"
        };

    private void TapTempo()
    {
        long now = Stopwatch.GetTimestamp();
        long resetTicks = (long)(Stopwatch.Frequency * 2.5);
        if (_tapTempoTicks.Count > 0 && now - _tapTempoTicks[^1] > resetTicks)
        {
            _tapTempoTicks.Clear();
        }

        _tapTempoTicks.Add(now);
        while (_tapTempoTicks.Count > 5)
        {
            _tapTempoTicks.RemoveAt(0);
        }

        if (_tapTempoTicks.Count < 2)
        {
            SetStatus("Tap tempo: primer golpe registrado. Pulse F8 nuevamente siguiendo el pulso.");
            return;
        }

        double intervalSeconds = 0.0;
        for (int i = 1; i < _tapTempoTicks.Count; i++)
        {
            intervalSeconds += (_tapTempoTicks[i] - _tapTempoTicks[i - 1]) / (double)Stopwatch.Frequency;
        }
        intervalSeconds /= _tapTempoTicks.Count - 1;
        if (intervalSeconds <= 0.0)
        {
            return;
        }

        decimal bpm = (decimal)Math.Clamp(60.0 / intervalSeconds, 40.0, 240.0);
        _metronomeBpm.Value = decimal.Round(bpm, 0);
        _engine.RequestMetronomeReset();
        SetStatus($"Tap tempo: {_metronomeBpm.Value:0} BPM.");
    }

    private void RestartMetronome()
    {
        _engine.RequestMetronomeReset();
        SetStatus("Metrónomo, batería, bajo y piano reiniciados desde el primer tiempo.");
    }

    private void StartPracticeRecording()
    {
        if (_practiceSaveInProgress)
        {
            SetStatus("La grabación anterior todavía se está guardando.", true);
            return;
        }
        if (!_engine.IsRunning)
        {
            SetStatus("Para grabar, primero inicie el audio con F4.", true);
            return;
        }
        if (_engine.IsPracticeRecording)
        {
            SetStatus("Ya hay una grabación de práctica en curso.");
            return;
        }

        const int minutes = 5;
        try
        {
            string path = _engine.StartPracticeRecording(minutes);
            _practiceRecordButton.Enabled = false;
            _practiceStopButton.Enabled = true;
            _practiceRecordingDuration.Enabled = false;
            _practiceRecordingStatus.Text = $"Grabando {minutes} minutos. Archivo de destino: {path}";
            SetStatus($"Grabación de práctica iniciada por {minutes} minutos. Se detendrá y guardará automáticamente.");
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo iniciar la grabación: {ex.Message}", true);
        }
    }

    private async Task FinalizePracticeRecordingAsync(bool autoCompleted)
    {
        if (_practiceSaveInProgress) return;
        if (!_engine.IsPracticeRecording && !_engine.HasPendingPracticeRecording) return;

        _practiceSaveInProgress = true;
        _practiceRecordButton.Enabled = false;
        _practiceStopButton.Enabled = false;
        _practiceRecordingDuration.Enabled = false;
        _practiceRecordingStatus.Text = "Guardando archivo WAV...";
        try
        {
            string? path = await _engine.StopAndSavePracticeRecordingAsync();
            if (string.IsNullOrWhiteSpace(path))
            {
                _practiceRecordingStatus.Text = "No había audio grabado para guardar.";
                SetStatus("No había audio grabado para guardar.", true);
            }
            else
            {
                string reason = autoCompleted ? "Grabación completada automáticamente" : "Grabación detenida";
                _practiceRecordingStatus.Text = $"{reason}. Guardada en {path}";
                SetStatus($"{reason}. WAV guardado en {path}.");
            }
        }
        catch (Exception ex)
        {
            _practiceRecordingStatus.Text = $"Error al guardar: {ex.Message}";
            SetStatus($"No se pudo guardar la grabación: {ex.Message}", true);
        }
        finally
        {
            _practiceSaveInProgress = false;
            _practiceRecordButton.Enabled = true;
            _practiceStopButton.Enabled = false;
            _practiceRecordingDuration.Enabled = false;
        }
    }

    private string? SavePracticeRecordingBeforeAudioStop()
    {
        if (_practiceSaveInProgress || (!_engine.IsPracticeRecording && !_engine.HasPendingPracticeRecording))
        {
            return null;
        }

        _practiceSaveInProgress = true;
        try
        {
            string? path = _engine.StopAndSavePracticeRecordingAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(path))
            {
                _practiceRecordingStatus.Text = $"Grabación guardada en {path}";
            }
            return path;
        }
        catch (Exception ex)
        {
            _practiceRecordingStatus.Text = $"Error al guardar: {ex.Message}";
            return null;
        }
        finally
        {
            _practiceSaveInProgress = false;
            _practiceRecordButton.Enabled = true;
            _practiceStopButton.Enabled = false;
            _practiceRecordingDuration.Enabled = false;
        }
    }

    private void UpdatePracticeRecordingUi()
    {
        if (_engine.IsPracticeRecording)
        {
            TimeSpan elapsed = TimeSpan.FromSeconds(_engine.PracticeRecordingSeconds);
            TimeSpan remaining = TimeSpan.FromSeconds(_engine.PracticeRecordingRemainingSeconds);
            string elapsedText = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
            string remainingText = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
            _practiceRecordingStatus.Text = $"Grabando. Transcurrido {elapsedText}. Restante {remainingText}.";
            _practiceRecordButton.Enabled = false;
            _practiceStopButton.Enabled = true;
            _practiceRecordingDuration.Enabled = false;
        }
    }

    private sealed class MidiActionOption
    {
        public MidiActionOption(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
        public override string ToString() => Name;
    }

    private void PopulateMidiActions()
    {
        string? selectedId = (_midiLearnActionCombo.SelectedItem as MidiActionOption)?.Id;
        _midiLearnActionCombo.Items.Clear();

        void Add(string id, string name) => _midiLearnActionCombo.Items.Add(new MidiActionOption(id, name));

        Add("amp.toggle", "Amp Accessible: activar o desactivar audio, equivalente a F4");
        Add("nam.toggle", "NAM: activar o desactivar, equivalente a F5");
        Add("nam.next", "NAM: siguiente, equivalente a F6");
        Add("nam.previous", "NAM: anterior, equivalente a F7");
        Add("guitar1.nam.toggle", "Guitarra 1, NAM: activar o desactivar, equivalente a Control Alt F5");
        Add("guitar1.nam.next", "Guitarra 1, NAM: siguiente, equivalente a Control Alt F6");
        Add("guitar1.nam.previous", "Guitarra 1, NAM: anterior, equivalente a Control Alt F7");
        Add("ir.next", "Guitarra 2, IR A de carpeta: siguiente, equivalente a Control F6");
        Add("ir.previous", "Guitarra 2, IR A de carpeta: anterior, equivalente a Control F7");
        Add("guitar1.ir.next", "Guitarra 1, IR de carpeta: siguiente, equivalente a Control Shift F6");
        Add("guitar1.ir.previous", "Guitarra 1, IR de carpeta: anterior, equivalente a Control Shift F7");
        Add("backing.toggle", "Acompañamiento: batería, bajo y piano juntos, equivalente a F12");
        Add("recording.toggle", "Grabadora de práctica: iniciar o detener");
        Add("looper.first", "Looper: primera vuelta");
        Add("looper.overdub", "Looper: overdub");
        Add("looper.undo", "Looper: deshacer último overdub");
        Add("looper.play", "Looper: reproducir o detener");
        Add("looper.clear", "Looper: borrar");
        Add("effect.booster", "Efecto: Booster");
        Add("effect.od1", "Efecto: OD-1 / Fulltone OCD");
        Add("effect.overdrive", "Efecto: Overdrive");
        Add("effect.ds1", "Efecto: Boss DS-1");
        Add("effect.fuzz", "Efecto: Fuzz");
        Add("effect.eq5", "Efecto: EQ de 5 bandas");
        Add("effect.gate", "Efecto: puerta de ruidos");
        Add("effect.compressor", "Efecto: compresor");
        Add("effect.autowah", "Efecto: Wah automático o manual");
        Add("effect.chorus.stereo", "Efecto: Chorus estéreo");
        Add("effect.chorus.analog", "Efecto: Chorus analógico");
        Add("effect.phaser", "Efecto: Phaser");
        Add("effect.flanger", "Efecto: Flanger");
        Add("effect.delay", "Efecto: Delay");
        Add("effect.reverb", "Efecto: Reverb");
        Add("continuous.output", "Pedal de expresión: volumen general");
        Add("continuous.gain", "Pedal de expresión: ganancia del amplificador");
        Add("continuous.delaymix", "Pedal de expresión: mezcla de delay");
        Add("continuous.reverbmix", "Pedal de expresión: mezcla de reverb");
        Add("continuous.wahposition", "Pedal de expresión: posición del wah");

        for (int i = 0; i < FactoryPresetBank.Presets.Count; i++)
        {
            string code = $"F{i + 1:00}";
            Add($"factory.{i + 1:00}", $"Banco {code}: {FactoryPresetBank.Presets[i].Name}");
        }

        int selectedIndex = -1;
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            for (int i = 0; i < _midiLearnActionCombo.Items.Count; i++)
            {
                if (_midiLearnActionCombo.Items[i] is MidiActionOption option &&
                    string.Equals(option.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }
        }
        _midiLearnActionCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : (_midiLearnActionCombo.Items.Count > 0 ? 0 : -1);
    }

    private string MidiActionName(string actionId)
    {
        foreach (object item in _midiLearnActionCombo.Items)
            if (item is MidiActionOption option && string.Equals(option.Id, actionId, StringComparison.OrdinalIgnoreCase))
                return option.Name;
        return actionId;
    }

    private static bool IsContinuousMidiAction(string actionId) =>
        actionId.StartsWith("continuous.", StringComparison.OrdinalIgnoreCase);

    private void RefreshMidiBindingsList()
    {
        _midiSettings.Normalize();
        _midiBindingsList.Items.Clear();
        foreach (MidiBinding binding in _midiSettings.Bindings)
            _midiBindingsList.Items.Add(DescribeMidiBinding(binding));
        _midiDeleteBindingButton.Enabled = _midiBindingsList.SelectedIndex >= 0;
    }

    private string DescribeMidiBinding(MidiBinding binding)
    {
        string message = binding.MessageKind == MidiMessageKind.ProgramChange
            ? $"PC {binding.Number}"
            : $"CC {binding.Number}";
        return $"Canal {binding.Channel}, {message}: {MidiActionName(binding.ActionId)}";
    }

    private void RefreshMidiDevices(bool announce)
    {
        if (_midiInput is not null)
            DisconnectMidiInput(announce: false);

        _loadingMidiDevices = true;
        try
        {
            string preferred = _midiSettings.PreferredInputDevice ?? string.Empty;
            _midiInputCombo.Items.Clear();
            int preferredIndex = -1;
            int count = MidiIn.NumberOfDevices;
            for (int i = 0; i < count; i++)
            {
                string name = MidiIn.DeviceInfo(i).ProductName;
                _midiInputCombo.Items.Add(name);
                if (string.Equals(name, preferred, StringComparison.CurrentCultureIgnoreCase)) preferredIndex = i;
            }
            _midiInputCombo.SelectedIndex = preferredIndex >= 0 ? preferredIndex : (count > 0 ? 0 : -1);
        }
        catch (Exception ex)
        {
            _midiInputCombo.Items.Clear();
            _midiConnectionStatus.Text = $"No se pudieron enumerar dispositivos MIDI: {ex.Message}";
            _midiConnectionStatus.AccessibleName = _midiConnectionStatus.Text;
            if (announce) SetStatus(_midiConnectionStatus.Text, true);
            return;
        }
        finally
        {
            _loadingMidiDevices = false;
        }

        _midiConnectButton.Text = "&Conectar MIDI";
        if (_midiInputCombo.Items.Count == 0)
        {
            _midiConnectionStatus.Text = "No hay dispositivos MIDI conectados. El simulador interno está disponible.";
            _midiConnectionStatus.AccessibleName = _midiConnectionStatus.Text;
            if (announce) SetStatus("No se detectaron entradas MIDI. Puede usar el simulador interno para probar las asignaciones.");
        }
        else
        {
            _midiConnectionStatus.Text = $"MIDI desconectado. Dispositivos detectados: {_midiInputCombo.Items.Count}.";
            _midiConnectionStatus.AccessibleName = _midiConnectionStatus.Text;
            if (announce) SetStatus($"Se detectaron {_midiInputCombo.Items.Count} entradas MIDI.");
        }
    }

    private void TryAutoConnectMidi()
    {
        if (!_midiSettings.AutoConnect || string.IsNullOrWhiteSpace(_midiSettings.PreferredInputDevice)) return;
        if (_midiInputCombo.SelectedIndex < 0) return;
        if (!string.Equals(_midiInputCombo.SelectedItem?.ToString(), _midiSettings.PreferredInputDevice, StringComparison.CurrentCultureIgnoreCase)) return;
        ConnectSelectedMidiInput(announce: false);
    }

    private void ToggleMidiConnection()
    {
        if (_midiInput is not null)
        {
            DisconnectMidiInput(announce: true);
            return;
        }
        ConnectSelectedMidiInput(announce: true);
    }

    private void ConnectSelectedMidiInput(bool announce)
    {
        if (_loadingMidiDevices) return;
        int deviceIndex = _midiInputCombo.SelectedIndex;
        if (deviceIndex < 0 || deviceIndex >= MidiIn.NumberOfDevices)
        {
            if (announce) SetStatus("No hay una entrada MIDI seleccionada. Conecte una pedalera o use el simulador MIDI.", true);
            return;
        }

        DisconnectMidiInput(announce: false);
        try
        {
            var input = new MidiIn(deviceIndex);
            input.MessageReceived += MidiInput_MessageReceived;
            input.Start();
            _midiInput = input;
            string name = _midiInputCombo.SelectedItem?.ToString() ?? MidiIn.DeviceInfo(deviceIndex).ProductName;
            _midiSettings.PreferredInputDevice = name;
            _midiSettings.AutoConnect = true;
            MidiSettingsStore.Save(_midiSettings);
            _midiConnectButton.Text = "&Desconectar MIDI";
            _midiConnectionStatus.Text = $"MIDI conectado: {name}.";
            _midiConnectionStatus.AccessibleName = _midiConnectionStatus.Text;
            if (announce) SetStatus(_midiConnectionStatus.Text);
        }
        catch (Exception ex)
        {
            _midiInput = null;
            _midiConnectButton.Text = "&Conectar MIDI";
            _midiConnectionStatus.Text = $"No se pudo abrir la entrada MIDI: {ex.Message}";
            _midiConnectionStatus.AccessibleName = _midiConnectionStatus.Text;
            if (announce) SetStatus(_midiConnectionStatus.Text, true);
        }
    }

    private void DisconnectMidiInput(bool announce)
    {
        MidiIn? input = _midiInput;
        _midiInput = null;
        if (input is not null)
        {
            try { input.Stop(); } catch { }
            try { input.MessageReceived -= MidiInput_MessageReceived; } catch { }
            try { input.Dispose(); } catch { }
        }
        _midiConnectButton.Text = "&Conectar MIDI";
        string text = "MIDI desconectado. El simulador interno sigue disponible.";
        _midiConnectionStatus.Text = text;
        _midiConnectionStatus.AccessibleName = text;
        if (announce) SetStatus(text);
    }

    private void MidiInput_MessageReceived(object? sender, MidiInMessageEventArgs e)
    {
        int raw = e.RawMessage;
        if (IsDisposed || Disposing) return;
        try
        {
            BeginInvoke((Action)(() => ProcessMidiRawMessage(raw, simulated: false)));
        }
        catch
        {
            // La ventana puede estar cerrándose mientras llega el último mensaje MIDI.
        }
    }

    private void ProcessMidiRawMessage(int rawMessage, bool simulated)
    {
        int status = rawMessage & 0xFF;
        int command = status & 0xF0;
        int channel = (status & 0x0F) + 1;
        int data1 = (rawMessage >> 8) & 0x7F;
        int data2 = (rawMessage >> 16) & 0x7F;

        if (command == 0xB0)
            ProcessMidiMessage(MidiMessageKind.ControlChange, channel, data1, data2, simulated);
        else if (command == 0xC0)
            ProcessMidiMessage(MidiMessageKind.ProgramChange, channel, data1, 127, simulated);
    }

    private void SimulateMidiMessage()
    {
        MidiMessageKind kind = _midiSimTypeCombo.SelectedIndex == 1
            ? MidiMessageKind.ProgramChange
            : MidiMessageKind.ControlChange;
        ProcessMidiMessage(
            kind,
            (int)_midiSimChannel.Value,
            (int)_midiSimNumber.Value,
            (int)_midiSimValue.Value,
            simulated: true);
    }

    private void ProcessMidiMessage(MidiMessageKind kind, int channel, int number, int value, bool simulated)
    {
        channel = Math.Clamp(channel, 1, 16);
        number = Math.Clamp(number, 0, 127);
        value = Math.Clamp(value, 0, 127);
        string messageName = kind == MidiMessageKind.ProgramChange ? $"PC {number}" : $"CC {number}, valor {value}";
        string source = simulated ? "simulado" : "recibido";
        _midiLastMessageLabel.Text = $"MIDI {source}: canal {channel}, {messageName}.";
        _midiLastMessageLabel.AccessibleName = _midiLastMessageLabel.Text;

        if (_midiLearnArmed)
        {
            if (kind == MidiMessageKind.ControlChange && value == 0) return;
            CaptureMidiLearn(kind, channel, number);
            return;
        }

        MidiBinding? binding = _midiSettings.Bindings.FirstOrDefault(candidate =>
            candidate.MessageKind == kind && candidate.Channel == channel && candidate.Number == number);
        if (binding is not null)
        {
            if (kind == MidiMessageKind.ControlChange && !IsContinuousMidiAction(binding.ActionId) && value == 0)
                return;
            ExecuteMidiAction(binding.ActionId, value);
            return;
        }

        if (kind == MidiMessageKind.ProgramChange && _midiSettings.ProgramChangesLoadFactoryBanks && number < FactoryPresetBank.Presets.Count)
        {
            LoadFactoryPresetFromMidi(number);
            return;
        }

        if (simulated)
            SetStatus($"MIDI simulado canal {channel}, {messageName}: no tiene una asignación.");
    }

    private void StartMidiLearn()
    {
        if (_midiLearnActionCombo.SelectedItem is not MidiActionOption option)
        {
            SetStatus("Seleccione primero una función para MIDI Learn.", true);
            return;
        }

        _midiLearnArmed = true;
        _midiLearnButton.Enabled = false;
        _midiCancelLearnButton.Enabled = true;
        SetStatus($"MIDI Learn esperando un control para: {option.Name}. Pise el pulsador o mueva el pedal. También puede usar el simulador MIDI.");
    }

    private void CancelMidiLearn(bool announce = true)
    {
        _midiLearnArmed = false;
        _midiLearnButton.Enabled = true;
        _midiCancelLearnButton.Enabled = false;
        if (announce) SetStatus("MIDI Learn cancelado.");
    }

    private void CaptureMidiLearn(MidiMessageKind kind, int channel, int number)
    {
        if (_midiLearnActionCombo.SelectedItem is not MidiActionOption option)
        {
            CancelMidiLearn(announce: false);
            return;
        }

        if (kind == MidiMessageKind.ProgramChange && IsContinuousMidiAction(option.Id))
        {
            SetStatus("Esa función necesita un Control Change continuo. MIDI Learn sigue esperando un CC.", true);
            return;
        }

        _midiSettings.Bindings.RemoveAll(binding =>
            string.Equals(binding.ActionId, option.Id, StringComparison.OrdinalIgnoreCase) ||
            (binding.MessageKind == kind && binding.Channel == channel && binding.Number == number));
        _midiSettings.Bindings.Add(new MidiBinding
        {
            MessageKind = kind,
            Channel = channel,
            Number = number,
            ActionId = option.Id
        });
        MidiSettingsStore.Save(_midiSettings);
        CancelMidiLearn(announce: false);
        RefreshMidiBindingsList();
        string message = kind == MidiMessageKind.ProgramChange ? $"PC {number}" : $"CC {number}";
        SetStatus($"MIDI Learn guardado. Canal {channel}, {message}: {option.Name}.");
    }

    private void DeleteSelectedMidiBinding()
    {
        int index = _midiBindingsList.SelectedIndex;
        if (index < 0 || index >= _midiSettings.Bindings.Count)
        {
            SetStatus("Seleccione una asignación MIDI para borrarla.", true);
            return;
        }

        string description = DescribeMidiBinding(_midiSettings.Bindings[index]);
        _midiSettings.Bindings.RemoveAt(index);
        MidiSettingsStore.Save(_midiSettings);
        RefreshMidiBindingsList();
        SetStatus($"Asignación MIDI borrada: {description}.");
    }

    private void LoadFactoryPresetFromMidi(int index)
    {
        if (index < 0 || index >= FactoryPresetBank.Presets.Count) return;
        _loadingFactoryPreset = true;
        try { _factoryPresetCombo.SelectedIndex = index; }
        finally { _loadingFactoryPreset = false; }
        LoadSelectedFactoryPreset(announce: true);
    }

    private void ExecuteMidiAction(string actionId, int value)
    {
        if (actionId.StartsWith("factory.", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(actionId.AsSpan("factory.".Length), out int factoryNumber))
        {
            LoadFactoryPresetFromMidi(factoryNumber - 1);
            return;
        }

        if (IsContinuousMidiAction(actionId))
        {
            float normalized = Math.Clamp(value, 0, 127) / 127f;
            switch (actionId.ToLowerInvariant())
            {
                case "continuous.output":
                    SetNumeric(_output, normalized * 100f);
                    SetStatus($"MIDI: volumen general {_output.Value:0} por ciento.");
                    break;
                case "continuous.gain":
                    SetNumeric(_gain, normalized * 10f);
                    SetStatus($"MIDI: ganancia del amplificador {_gain.Value:0.0}.");
                    break;
                case "continuous.delaymix":
                    SetNumeric(_delayMix, normalized * 60f);
                    SetStatus($"MIDI: mezcla de delay {_delayMix.Value:0} por ciento.");
                    break;
                case "continuous.reverbmix":
                    SetNumeric(_reverbMix, normalized * 65f);
                    SetStatus($"MIDI: mezcla de reverb {_reverbMix.Value:0} por ciento.");
                    break;
                case "continuous.wahposition":
                    if (_autoWahModeCombo.SelectedIndex != 1) _autoWahModeCombo.SelectedIndex = 1;
                    SetNumeric(_autoWahManualPosition, normalized * 100f);
                    int wahBucket = value >= 127 ? 4 : Math.Clamp((int)(normalized * 4f), 0, 4);
                    if (wahBucket != _lastMidiWahAnnouncementBucket)
                    {
                        _lastMidiWahAnnouncementBucket = wahBucket;
                        SetStatus($"MIDI: posición del wah {_autoWahManualPosition.Value:0} por ciento; {(_autoWahEnabled.Checked ? "wah activo" : "wah desactivado")}.");
                    }
                    break;
            }
            return;
        }

        switch (actionId.ToLowerInvariant())
        {
            case "amp.toggle": ToggleAudio(); break;
            case "nam.toggle": ToggleNamQuick(); break;
            case "nam.next": LoadAdjacentNamFromBank(1); break;
            case "nam.previous": LoadAdjacentNamFromBank(-1); break;
            case "guitar1.nam.toggle": ToggleGuitar1NamQuick(); break;
            case "guitar1.nam.next": LoadAdjacentGuitar1NamFromBank(1); break;
            case "guitar1.nam.previous": LoadAdjacentGuitar1NamFromBank(-1); break;
            case "ir.next": LoadAdjacentImpulseResponse(1); break;
            case "ir.previous": LoadAdjacentImpulseResponse(-1); break;
            case "guitar1.ir.next": LoadAdjacentGuitar1ImpulseResponse(1); break;
            case "guitar1.ir.previous": LoadAdjacentGuitar1ImpulseResponse(-1); break;
            case "backing.toggle": ToggleBackingBandPair(); break;
            case "recording.toggle": TogglePracticeRecordingShortcut(); break;
            case "looper.first": ToggleLoopFirstPass(); break;
            case "looper.overdub": ToggleLoopOverdub(); break;
            case "looper.undo": UndoLastLoopOverdub(); break;
            case "looper.play": ToggleLoopPlayback(); break;
            case "looper.clear": ClearLoop(); break;
            case "effect.booster": ToggleMidiCheckBox(_boosterEnabled, "Booster"); break;
            case "effect.od1": ToggleMidiCheckBox(_od1Enabled, "OD-1 / Fulltone OCD"); break;
            case "effect.overdrive": ToggleMidiCheckBox(_ts9Enabled, "Overdrive"); break;
            case "effect.ds1": ToggleMidiCheckBox(_overdriveEnabled, "Boss DS-1"); break;
            case "effect.fuzz": ToggleMidiCheckBox(_fuzzEnabled, "Fuzz"); break;
            case "effect.eq5": ToggleMidiCheckBox(_eq5Enabled, "EQ de 5 bandas"); break;
            case "effect.gate": ToggleMidiCheckBox(_gateEnabled, "Puerta de ruidos"); break;
            case "effect.compressor": ToggleMidiCheckBox(_compressorEnabled, "Compresor"); break;
            case "effect.autowah": ToggleMidiCheckBox(_autoWahEnabled, "Wah"); break;
            case "effect.chorus.stereo": ToggleMidiCheckBox(_chorusEnabled, "Chorus estéreo"); break;
            case "effect.chorus.analog": ToggleMidiCheckBox(_analogChorusEnabled, "Chorus analógico"); break;
            case "effect.phaser": ToggleMidiCheckBox(_phaserEnabled, "Phaser"); break;
            case "effect.flanger": ToggleMidiCheckBox(_flangerEnabled, "Flanger"); break;
            case "effect.delay": ToggleMidiCheckBox(_delayEnabled, "Delay"); break;
            case "effect.reverb": ToggleMidiCheckBox(_reverbEnabled, "Reverb"); break;
        }
    }

    private void ToggleMidiCheckBox(CheckBox checkBox, string name)
    {
        checkBox.Checked = !checkBox.Checked;
        SetStatus($"MIDI: {name} {(checkBox.Checked ? "activado" : "desactivado")}.");
    }

    private void AnnounceGeneralStatus()
    {
        string bank = _factoryPresetCombo.SelectedItem?.ToString() ?? "sin banco de fábrica seleccionado";
        string amp = _channelCombo.SelectedItem?.ToString() ?? "amplificador sin seleccionar";
        string nam;
        if (_namEnabled.Checked && _engine.Processor.HasNamModel)
        {
            string model = Path.GetFileNameWithoutExtension(_engine.Processor.NamModelPath) ?? "modelo cargado";
            nam = $"NAM activo, {model}";
        }
        else if (_engine.Processor.HasNamModel)
        {
            string model = Path.GetFileNameWithoutExtension(_engine.Processor.NamModelPath) ?? "modelo cargado";
            nam = $"NAM apagado, {model} preparado";
        }
        else
        {
            nam = "NAM sin modelo cargado";
        }

        string ir = string.IsNullOrWhiteSpace(_loadedIrPath)
            ? "gabinete interno"
            : _irBrowserIndex >= 0 && _irBrowserFiles.Count > 0
                ? $"IR A {_irBrowserIndex + 1} de {_irBrowserFiles.Count}, {Path.GetFileNameWithoutExtension(_loadedIrPath)}"
                : $"IR A {Path.GetFileNameWithoutExtension(_loadedIrPath)}";
        string drums = _drumsEnabled.Checked ? "batería activa" : "batería apagada";
        string bass = _backingBassEnabled.Checked ? "bajo activo" : "bajo apagado";
        string piano = _pianoEnabled.Checked ? "piano activo" : "piano apagado";
        string key = _backingBassKey.SelectedItem?.ToString() ?? "tonalidad no definida";
        string recording = _engine.IsPracticeRecording
            ? $"grabando, {_engine.PracticeRecordingSeconds:0} segundos"
            : "grabadora detenida";
        string midi = _midiInput is null
            ? "MIDI desconectado"
            : $"MIDI conectado, {_midiInputCombo.SelectedItem}";
        string looper = _engine.IsLoopRecording
            ? (_engine.IsLoopTempoSyncedRecording
                ? $"loop sincronizado grabando {SelectedLoopBars} compases"
                : "loop grabando primera vuelta")
            : _engine.IsLoopOverdubbing
                ? $"loop en overdub, {_engine.LoopSeconds:0.0} segundos"
                : _engine.IsLoopPlaying
                    ? $"loop reproduciendo, {_engine.LoopSeconds:0.0} segundos"
                    : _engine.HasLoop
                        ? $"loop detenido, {_engine.LoopSeconds:0.0} segundos"
                        : "looper vacío";
        string tuner = _tunerEnabled.Checked
            ? $"afinador activo para {(_twoGuitarMode.Checked ? SelectedTunerGuitarName : "rig principal")}"
            : "afinador apagado";

        SetStatus($"Estado general. {bank}. {amp}. {nam}. {ir}. {tuner}. {_metronomeBpm.Value:0} BPM. Tonalidad {key}. {drums}. {bass}. {piano}. {recording}. {looper}. {midi}.");
    }

    private void TogglePracticeRecordingShortcut()
    {
        if (_practiceSaveInProgress)
        {
            SetStatus("La grabación anterior todavía se está guardando.", true);
            return;
        }

        if (_engine.IsPracticeRecording || _engine.HasPendingPracticeRecording)
        {
            _ = FinalizePracticeRecordingAsync(autoCompleted: false);
            return;
        }

        StartPracticeRecording();
    }

    private void ToggleLoopFirstPass()
    {
        if (!_engine.IsRunning)
        {
            SetStatus("Para grabar un loop, primero inicie Amp Accessible con F4.", true);
            return;
        }

        if (_engine.IsLoopRecording)
        {
            if (_engine.IsLoopTempoSyncedRecording)
            {
                SetStatus($"Loop sincronizado grabando. Se cerrará solo al completar {SelectedLoopBars} compases. Shift F7 permite cancelarlo y borrarlo.");
                return;
            }

            bool created = _engine.FinishLoopRecordingAndPlay();
            _loopAutoCompletionAnnounced = false;
            _loopTempoCompletionAnnounced = false;
            if (!created)
            {
                _loopStatus.Text = "Loop descartado por ser demasiado corto.";
                SetStatus("Loop demasiado corto. No se guardó ninguna vuelta.", true);
                return;
            }

            _loopStatus.Text = $"Loop creado, {_engine.LoopSeconds:0.0} segundos. Reproduciendo.";
            UpdateLooperUi();
            SetStatus($"Primera vuelta cerrada. Loop de {_engine.LoopSeconds:0.0} segundos reproduciendo. Puede cambiar Fuente del looper antes de iniciar un overdub.");
            return;
        }

        if (_engine.HasLoop)
        {
            SetStatus("Ya existe un loop. Pulse Shift F7 para borrarlo antes de grabar una primera vuelta nueva.", true);
            return;
        }

        try
        {
            _loopAutoCompletionAnnounced = false;
            _loopTempoCompletionAnnounced = false;

            if (_loopSyncTempo.Checked)
            {
                // Reiniciamos click, batería, bajo y piano para que el tiempo 1 y la captura
                // comiencen juntos en el siguiente callback ASIO.
                _engine.RequestMetronomeReset();
                double targetSeconds = _engine.StartLoopRecordingSynced(
                    (float)_metronomeBpm.Value,
                    SelectedMetronomeBeatsPerBar,
                    SelectedLoopBars);
                _loopStatus.Text = $"Grabando loop sincronizado desde {CurrentLoopCaptureSourceName}: {SelectedLoopBars} compases, objetivo {targetSeconds:0.0} segundos.";
                UpdateLooperUi();
                SetStatus($"Loop sincronizado iniciado desde {CurrentLoopCaptureSourceName}. {SelectedLoopBars} compases a {_metronomeBpm.Value:0} BPM. La vuelta se cerrará automáticamente.");
            }
            else
            {
                _engine.StartLoopRecording();
                _loopStatus.Text = $"Grabando primera vuelta desde {CurrentLoopCaptureSourceName}. Máximo {_engine.MaximumLoopSeconds} segundos.";
                UpdateLooperUi();
                SetStatus($"Loop grabando primera vuelta desde {CurrentLoopCaptureSourceName}. Pulse Shift F3 nuevamente para cerrar la vuelta y comenzar a reproducir.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo iniciar el looper: {ex.Message}", true);
        }
    }

    private void ToggleLoopOverdub()
    {
        if (!_engine.HasLoop)
        {
            SetStatus("El looper está vacío. Grabe primero una vuelta con Shift F3.", true);
            return;
        }
        if (_engine.IsLoopRecording)
        {
            SetStatus("Cierre primero la primera vuelta con Shift F3.", true);
            return;
        }

        bool enabled = _engine.ToggleLoopOverdub();
        UpdateLooperUi();
        _loopStatus.Text = enabled
            ? $"Overdub activo desde {CurrentLoopCaptureSourceName}. Loop {_engine.LoopSeconds:0.0} segundos."
            : $"Overdub detenido. Loop reproduciendo, {_engine.LoopSeconds:0.0} segundos.";
        SetStatus(enabled
            ? $"Overdub activo desde {CurrentLoopCaptureSourceName}. Se guardó el estado anterior para poder deshacer esta capa."
            : "Overdub detenido. El loop sigue reproduciendo. Puede cambiar Fuente del looper para la próxima capa y usar Deshacer último overdub si no quiere conservar esta capa.");
    }

    private void UndoLastLoopOverdub()
    {
        if (!_engine.HasLoop)
        {
            SetStatus("El looper está vacío.", true);
            return;
        }
        if (!_engine.CanUndoLoopOverdub)
        {
            SetStatus("No hay un overdub anterior disponible para deshacer.", true);
            return;
        }

        bool restored = _engine.UndoLastLoopOverdub();
        UpdateLooperUi();
        SetStatus(restored
            ? "Último overdub deshecho. La vuelta anterior fue restaurada y el loop continúa disponible."
            : "No se pudo restaurar el overdub anterior.", !restored);
    }

    private void ToggleLoopPlayback()
    {
        if (!_engine.HasLoop)
        {
            SetStatus("El looper está vacío. Grabe primero una vuelta con Shift F3.", true);
            return;
        }
        if (_engine.IsLoopRecording)
        {
            SetStatus("Cierre primero la primera vuelta con Shift F3.", true);
            return;
        }

        bool playing = _engine.ToggleLoopPlayback();
        _loopStatus.Text = playing
            ? $"Loop reproduciendo, {_engine.LoopSeconds:0.0} segundos."
            : $"Loop detenido, {_engine.LoopSeconds:0.0} segundos.";
        SetStatus(playing ? "Loop reproduciendo." : "Loop detenido.");
    }

    private async Task SaveLoopAsync()
    {
        if (_loopSaveInProgress)
        {
            SetStatus("El loop ya se está guardando.");
            return;
        }
        if (!_engine.HasLoop)
        {
            SetStatus("No hay ningún loop para guardar.", true);
            return;
        }

        _loopSaveInProgress = true;
        _loopSaveButton.Enabled = false;
        try
        {
            string? path = await _engine.SaveLoopAsync();
            if (string.IsNullOrWhiteSpace(path))
            {
                SetStatus("No había audio de loop para guardar.", true);
                return;
            }
            _loopStatus.Text = $"Loop guardado en {path}";
            SetStatus($"Loop guardado como WAV en {path}.");
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo guardar el loop: {ex.Message}", true);
        }
        finally
        {
            _loopSaveInProgress = false;
            _loopSaveButton.Enabled = true;
        }
    }

    private void ClearLoop()
    {
        _engine.ClearLoop();
        _loopAutoCompletionAnnounced = false;
        _loopTempoCompletionAnnounced = false;
        _loopStatus.Text = "Looper vacío.";
        SetStatus("Loop borrado.");
    }

    private void ToggleDrumsShortcut()
    {
        _drumsEnabled.Checked = !_drumsEnabled.Checked;
        string pattern = _drumPattern.SelectedItem?.ToString() ?? "patrón de batería";
        SetStatus($"Batería {(_drumsEnabled.Checked ? "activada" : "desactivada")}. {pattern}, {_metronomeBpm.Value:0} BPM.");
    }

    private void ToggleBackingBassShortcut()
    {
        _backingBassEnabled.Checked = !_backingBassEnabled.Checked;
        string key = _backingBassKey.SelectedItem?.ToString() ?? "tonalidad actual";
        string mode = _backingBassMode.SelectedItem?.ToString() ?? "modo actual";
        SetStatus($"Bajo de acompañamiento {(_backingBassEnabled.Checked ? "activado" : "desactivado")}. {key}, {mode}, {_metronomeBpm.Value:0} BPM.");
    }

    private void CycleBackingPattern()
    {
        if (_drumPattern.Items.Count > 0)
        {
            _drumPattern.SelectedIndex = (_drumPattern.SelectedIndex + 1 + _drumPattern.Items.Count) % _drumPattern.Items.Count;
        }
        if (_backingBassLine.Items.Count > 0)
        {
            _backingBassLine.SelectedIndex = (_backingBassLine.SelectedIndex + 1 + _backingBassLine.Items.Count) % _backingBassLine.Items.Count;
        }
        _engine.RequestMetronomeReset();
        string drums = _drumPattern.SelectedItem?.ToString() ?? "patrón actual";
        string bass = _backingBassLine.SelectedItem?.ToString() ?? "línea actual";
        SetStatus($"Variante de acompañamiento: batería {drums}; bajo {bass}; {_metronomeBpm.Value:0} BPM.");
    }

    private void UpdateLooperUi()
    {
        bool captureActive = _engine.IsLoopRecording || _engine.IsLoopOverdubbing;
        _loopSourceCombo.Enabled = _twoGuitarMode.Checked && !captureActive;
        _loopSourceCombo.AccessibleDescription = _twoGuitarMode.Checked
            ? (captureActive
                ? $"Fuente actual {CurrentLoopCaptureSourceName}. El selector está bloqueado mientras se captura audio; detenga la primera vuelta u overdub para cambiarla."
                : "Elija qué guitarra se imprimirá en la próxima primera vuelta u overdub. El acompañamiento global queda siempre fuera del looper.")
            : "Disponible en Modo Dos Guitarras. Fuera del modo dual el looper captura el rig principal histórico.";
        _loopUndoButton.Enabled = _engine.CanUndoLoopOverdub && !_engine.IsLoopRecording;
        if (_engine.IsLoopRecording)
        {
            _loopStatus.Text = _engine.IsLoopTempoSyncedRecording
                ? $"Grabando loop sincronizado desde {CurrentLoopCaptureSourceName}: {SelectedLoopBars} compases a {_metronomeBpm.Value:0} BPM."
                : $"Grabando primera vuelta desde {CurrentLoopCaptureSourceName}. Máximo {_engine.MaximumLoopSeconds} segundos.";
            return;
        }
        if (_engine.IsLoopOverdubbing)
        {
            _loopStatus.Text = $"Overdub activo desde {CurrentLoopCaptureSourceName}. Loop {_engine.LoopSeconds:0.0} segundos.";
            return;
        }
        if (_engine.IsLoopPlaying)
        {
            _loopStatus.Text = $"Loop reproduciendo, {_engine.LoopSeconds:0.0} segundos.";
            return;
        }
        if (_engine.HasLoop)
        {
            _loopStatus.Text = $"Loop detenido, {_engine.LoopSeconds:0.0} segundos.";
            return;
        }
        _loopStatus.Text = "Looper vacío.";
    }

    private void OpenPracticeRecordingsFolder()
    {
        try
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folder = Path.Combine(documents, "GDM Amp Accessible", "Grabaciones");
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo abrir la carpeta de grabaciones: {ex.Message}", true);
        }
    }

    private void PollAudioErrors()
    {
        if (_audioRequested || _engine.HasActiveSession)
        {
            CaptureAudioTelemetry();
        }

        if (_engine.PracticeRecordingAutoCompleted && !_practiceSaveInProgress)
        {
            _ = FinalizePracticeRecordingAsync(autoCompleted: true);
        }
        UpdatePracticeRecordingUi();
        if (_engine.LoopAutoCompleted && !_loopAutoCompletionAnnounced)
        {
            _loopAutoCompletionAnnounced = true;
            SetStatus($"El looper alcanzó el máximo de {_engine.MaximumLoopSeconds} segundos. Primera vuelta cerrada y reproducción iniciada.");
        }
        if (_engine.LoopTempoCompleted && !_loopTempoCompletionAnnounced)
        {
            _loopTempoCompletionAnnounced = true;
            SetStatus($"Loop sincronizado completado. {SelectedLoopBars} compases, {_engine.LoopSeconds:0.0} segundos. Reproducción iniciada desde el tiempo 1.");
        }
        UpdateLooperUi();
        CompleteNamLevelCalibrationIfReady();

        AudioFaultReport? fault = ConsumeAudioFaultAndSave(out string? faultPath);
        if (fault is not null)
        {
            string pathNotice = faultPath is null ? string.Empty : $" Registro guardado en {faultPath}.";
            SetStatus(fault.UserMessage + pathNotice, true);
        }

        int automaticResets = _engine.Processor.DelayAutomaticResetCount;
        if (automaticResets > _lastDelayAutomaticResetCount)
        {
            _lastDelayAutomaticResetCount = automaticResets;
            SetStatus("Se detectó una falla interna y se limpió solamente la memoria del delay.", true);
        }

        // El camino ASIO directo no apaga efectos automáticamente.
        // Los errores por etapa quedan en el diagnóstico y sólo se anuncia el primero
        // de cada incidente, para no saturar a JAWS con cientos de mensajes iguales.

        _diagnosticsPollCounter++;
        if (_diagnosticsPollCounter >= 10)
        {
            _diagnosticsPollCounter = 0;
            UpdateAudioDiagnostics();
            UpdateActualBufferLabel();
        }

        bool stalled = _audioRequested && _engine.NeedsRestart(callbackThresholdMilliseconds: 2200);
        if (stalled && !_stallReported)
        {
            _stallReported = true;
            string? path = null;
            try { path = _audioDiagnostics.Save("Callback ASIO detenido", $"Edad del último callback: {_engine.CallbackAgeMilliseconds:0} ms"); } catch { }
            string pathNotice = path is null ? string.Empty : $" Registro guardado automáticamente en {path}.";
            SetStatus("El callback ASIO dejó de avanzar. La aplicación no fuerza un reinicio automático para evitar un cuelgue largo. Pulse F4 para detener y vuelva a pulsar F4 para iniciar." + pathNotice, true);
        }
        else if (!stalled)
        {
            _stallReported = false;
        }

        UpdateTunerDisplay();
    }

    private AudioFaultReport? ConsumeAudioFaultAndSave(out string? path)
    {
        path = null;
        AudioFaultReport? fault = _engine.ConsumeAudioFault();
        if (fault is null)
        {
            return null;
        }

        CaptureAudioTelemetry();
        try
        {
            path = _audioDiagnostics.SaveIncident(
                fault.Sequence,
                $"Falla de audio #{fault.Sequence}",
                fault.DiagnosticDetail);
        }
        catch
        {
            // El audio nunca se detiene porque falle la escritura del diagnóstico.
        }

        return fault;
    }

    private void CaptureAudioTelemetry()
    {
        int flags = 0;
        if (_overdriveEnabled.Checked) flags |= 1;
        if (_ts9Enabled.Checked) flags |= 2;
        if (_boosterEnabled.Checked) flags |= 4;
        if (_chorusEnabled.Checked) flags |= 8;
        if (_delayEnabled.Checked) flags |= 16;
        if (_reverbEnabled.Checked) flags |= 32;
        if (_gateEnabled.Checked) flags |= 64;
        if (_tunerEnabled.Checked) flags |= 128;
        if (_compressorEnabled.Checked) flags |= 256;
        if (_autoWahEnabled.Checked) flags |= 512;
        if (_phaserEnabled.Checked) flags |= 1024;
        if (_flangerEnabled.Checked) flags |= 2048;
        if (_voiceEnabled.Checked) flags |= 4096;
        if (_voiceEnabled.Checked && _voiceSuppressorEnabled.Checked) flags |= 8192;
        if (_namEnabled.Checked && _engine.Processor.HasNamModel) flags |= 16384;
        if (_analogChorusEnabled.Checked) flags |= 32768;
        if (_microPitchEnabled.Checked) flags |= 65536;

        AmpChannel channel = _channelCombo.SelectedIndex switch
        {
            1 => AmpChannel.CrunchBritish,
            2 => AmpChannel.LeadJcm800,
            _ => AmpChannel.CleanTwin
        };
        _audioDiagnostics.Capture(_engine, channel, flags);
    }

    private void UpdateTunerDisplay()
    {
        bool dual = _twoGuitarMode.Checked;
        string target = dual ? SelectedTunerGuitarName : "Guitarra / rig principal";

        if (!_tunerEnabled.Checked)
        {
            _lastTunerReading = TunerReading.NoSignal;
            _engine.Processor.SetTunerGuideDirection(TuningDirection.NoSignal);
            _engine.Guitar1Processor.SetTunerGuideDirection(TuningDirection.NoSignal);
            _tunerStatus.Text = $"{target}: afinador desactivado. Active el afinador y toque una cuerda.";
            _tunerStatus.AccessibleName = $"Lectura del afinador, {target}: afinador desactivado";
            return;
        }

        if (!_engine.IsRunning)
        {
            _lastTunerReading = TunerReading.NoSignal;
            _engine.Processor.SetTunerGuideDirection(TuningDirection.NoSignal);
            _engine.Guitar1Processor.SetTunerGuideDirection(TuningDirection.NoSignal);
            _tunerStatus.Text = $"{target}: el afinador está activado, pero el audio está detenido. Pulse F4 para iniciar.";
            _tunerStatus.AccessibleName = $"Lectura del afinador, {target}: audio detenido";
            return;
        }

        TunerReading reading;
        if (dual && SelectedTunerGuitarIndex == 0)
        {
            reading = _engine.Guitar1Processor.GetTunerReading((float)_tunerReferenceA.Value);
            _engine.Guitar1Processor.SetTunerGuideDirection(reading.Direction);
            _engine.Processor.SetTunerGuideDirection(TuningDirection.NoSignal);
        }
        else
        {
            reading = _engine.Processor.GetTunerReading((float)_tunerReferenceA.Value);
            _engine.Processor.SetTunerGuideDirection(reading.Direction);
            _engine.Guitar1Processor.SetTunerGuideDirection(TuningDirection.NoSignal);
        }

        _lastTunerReading = reading;
        _tunerStatus.Text = $"{target}: {reading.DisplayText}";
        _tunerStatus.AccessibleName = $"Lectura del afinador, {target}: {reading.DisplayText}";
    }

    private void ReadTunerWithJaws()
    {
        UpdateTunerDisplay();
        _tunerStatus.Focus();
        _tunerStatus.SelectAll();
        SetStatus($"Lectura del afinador: {_tunerStatus.Text}");
    }

    private void PlaySelectedReferenceTone()
    {
        if (!_engine.IsRunning)
        {
            SetStatus("Inicie el audio con F4 antes de reproducir el tono de referencia.", true);
            return;
        }

        int index = Math.Clamp(_referenceToneCombo.SelectedIndex, 0, ReferenceTones.Length - 1);
        (string name, float frequency) = ReferenceTones[index];
        if (_twoGuitarMode.Checked && SelectedTunerGuitarIndex == 0)
            _engine.Guitar1Processor.PlayTunerReferenceTone(frequency);
        else
            _engine.Processor.PlayTunerReferenceTone(frequency);
        string target = _twoGuitarMode.Checked ? SelectedTunerGuitarName : "rig principal";
        SetStatus($"Reproduciendo {name} para {target} durante aproximadamente 5 segundos.");
    }

    private void ResetDelayMemory()
    {
        _engine.Processor.RequestDelayReset();
        SetStatus("Memoria del delay limpiada. El audio, chorus y reverb continúan activos.");
    }

    private void ToggleCurrentDelay()
    {
        _delayEnabled.Checked = !_delayEnabled.Checked;
        string character = _delayCharacterCombo.SelectedItem?.ToString() ?? "carácter seleccionado";
        SetStatus($"Delay {character} {(_delayEnabled.Checked ? "activado" : "desactivado")}.");
    }

    private void ToggleDelayCharacter(DelayCharacter character)
    {
        int targetIndex = character == DelayCharacter.AnalogDark ? 1 : 0;
        bool sameCharacter = _delayCharacterCombo.SelectedIndex == targetIndex;

        if (_delayEnabled.Checked && sameCharacter)
        {
            _delayEnabled.Checked = false;
            SetStatus($"Delay {(character == DelayCharacter.AnalogDark ? "analógico" : "digital")} desactivado.");
            return;
        }

        _delayCharacterCombo.SelectedIndex = targetIndex;
        _delayEnabled.Checked = true;
        SetStatus($"Delay {(character == DelayCharacter.AnalogDark ? "analógico" : "digital")} activado.");
    }

    private bool HandleShiftFunctionKey(Keys keyCode)
    {
        // Segunda capa de funciones: se maneja desde ProcessCmdKey para que
        // funcione aunque el foco esté en listas, botones o controles numéricos.
        // Shift+F10 queda deliberadamente libre para Windows y JAWS.
        switch (keyCode)
        {
            case Keys.F1:
                AnnounceGeneralStatus();
                return true;
            case Keys.F2:
                TogglePracticeRecordingShortcut();
                return true;
            case Keys.F3:
                ToggleLoopFirstPass();
                return true;
            case Keys.F4:
                ToggleLoopOverdub();
                return true;
            case Keys.F5:
                ToggleLoopPlayback();
                return true;
            case Keys.F6:
                _ = SaveLoopAsync();
                return true;
            case Keys.F7:
                ClearLoop();
                return true;
            case Keys.F8:
                ToggleDrumsShortcut();
                return true;
            case Keys.F9:
                ToggleBackingBassShortcut();
                return true;
            case Keys.F11:
                SaveCurrentScene(_sceneCombo.SelectedIndex);
                return true;
            case Keys.F12:
                CycleBackingPattern();
                return true;
            default:
                return false;
        }
    }


    public bool PreFilterMessage(ref Message m)
    {
        // WM_KEYDOWN / WM_SYSKEYDOWN. Esta ruta se usa deliberadamente para
        // Shift+F1..F7 porque algunos controles de WinForms consumen esas teclas
        // antes de ProcessCmdKey. Shift+F8 en adelante ya funciona correctamente
        // por la ruta existente y se deja intacto para minimizar regresiones.
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;
        if (m.Msg != WM_KEYDOWN && m.Msg != WM_SYSKEYDOWN)
            return false;

        if (Form.ActiveForm != this && !ContainsFocus)
            return false;

        Keys keyCode = (Keys)(int)m.WParam & Keys.KeyCode;
        if (keyCode < Keys.F1 || keyCode > Keys.F7)
            return false;

        Keys modifiers = Control.ModifierKeys;
        if (modifiers != Keys.Shift)
            return false;

        // Evita que la repetición automática de una tecla mantenida active y
        // desactive grabadora/looper varias veces. Bit 30 indica que la tecla
        // ya estaba pulsada en el mensaje anterior.
        bool isAutoRepeat = (m.LParam.ToInt64() & (1L << 30)) != 0;
        if (isAutoRepeat)
            return true;

        return HandleShiftFunctionKey(keyCode);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Las combinaciones Shift+F deben interceptarse antes de que WinForms
        // o el control con foco consuman la tecla como comando.
        Keys modifiers = keyData & Keys.Modifiers;
        Keys keyCode = keyData & Keys.KeyCode;
        if (modifiers == Keys.Shift && HandleShiftFunctionKey(keyCode))
        {
            return true;
        }

        // Las teclas de función principales también se capturan aquí para que
        // funcionen de forma global aunque el foco esté en un ComboBox, lista,
        // cuadro de texto o cualquier control de la sección NAM.
        if (modifiers == Keys.None)
        {
            switch (keyCode)
            {
                case Keys.F1:
                    ShowKeyboardHelp();
                    return true;
                case Keys.F4:
                    ToggleAudio();
                    return true;
                case Keys.F5:
                    ToggleNamQuick();
                    return true;
                case Keys.F6:
                    LoadAdjacentNamFromBank(1);
                    return true;
                case Keys.F7:
                    LoadAdjacentNamFromBank(-1);
                    return true;
                case Keys.F8:
                    TapTempo();
                    return true;
                case Keys.F9:
                    ReadTunerWithJaws();
                    return true;
                case Keys.F10:
                    PlaySelectedReferenceTone();
                    return true;
                case Keys.F12:
                    ToggleBackingBandPair();
                    return true;
            }
        }

        if (keyData == (Keys.Control | Keys.F12))
        {
            TogglePianoShortcut();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Alt | Keys.F5))
        {
            ToggleGuitar1NamQuick();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Alt | Keys.F6))
        {
            LoadAdjacentGuitar1NamFromBank(1);
            return true;
        }
        if (keyData == (Keys.Control | Keys.Alt | Keys.F7))
        {
            LoadAdjacentGuitar1NamFromBank(-1);
            return true;
        }

        if (keyData == (Keys.Control | Keys.Shift | Keys.F6))
        {
            LoadAdjacentGuitar1ImpulseResponse(1);
            return true;
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.F7))
        {
            LoadAdjacentGuitar1ImpulseResponse(-1);
            return true;
        }

        if (keyData == (Keys.Control | Keys.F6))
        {
            LoadAdjacentImpulseResponse(1);
            return true;
        }
        if (keyData == (Keys.Control | Keys.F7))
        {
            LoadAdjacentImpulseResponse(-1);
            return true;
        }

        if (keyData == (Keys.Control | Keys.F9))
        {
            ToggleTunerGuitarTarget();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Tab) ||
            keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            SetStatus("No hay pestañas. Use Alt más la letra de la sección.");
            return true;
        }

        if (keyData == (Keys.Control | Keys.Alt | Keys.Up))
        {
            MoveSelectedPreEffect(-1);
            return true;
        }
        if (keyData == (Keys.Control | Keys.Alt | Keys.Down))
        {
            MoveSelectedPreEffect(1);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ToggleNamQuick()
    {
        if (_engine.Processor.HasNamModel)
        {
            _namEnabled.Checked = !_namEnabled.Checked;
            UpdateParameters();
            if (_engine.IsRunning && (_drumsEnabled.Checked || _backingBassEnabled.Checked || _pianoEnabled.Checked))
            {
                _engine.RequestMetronomeReset();
            }
            string model = Path.GetFileName(_engine.Processor.NamModelPath ?? string.Empty);
            SetStatus(_namEnabled.Checked
                ? $"NAM activado: {model}."
                : $"NAM desactivado. Se usa el amplificador interno; {model} queda cargado para volver con F5.");
            return;
        }

        if (_namBankCombo.Items.Count > 0)
        {
            if (_namBankCombo.SelectedIndex < 0) _namBankCombo.SelectedIndex = 0;
            LoadSelectedNamFromBank();
            return;
        }

        string savedPath = _audioPreferences.NamModelPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
        {
            TryLoadNamModel(savedPath, announce: true, enableAfterLoad: true, resumeAudioAfterLoad: true);
            return;
        }

        SetStatus("No hay capturas NAM disponibles. Abra la sección NAM y cargue una captura primero.", true);
    }

    private void PopulatePianoProgressionLibrary(string? selectName = null)
    {
        string previous = selectName ?? _pianoSavedProgression.SelectedItem?.ToString() ?? string.Empty;
        _pianoSavedProgression.BeginUpdate();
        try
        {
            _pianoSavedProgression.Items.Clear();
            _pianoProgressionLibrary.Normalize();
            foreach (PianoProgressionItem item in _pianoProgressionLibrary.Items)
                _pianoSavedProgression.Items.Add(item.Name);
        }
        finally
        {
            _pianoSavedProgression.EndUpdate();
        }

        if (_pianoSavedProgression.Items.Count == 0)
        {
            _pianoSavedProgression.SelectedIndex = -1;
            return;
        }

        int index = -1;
        for (int i = 0; i < _pianoSavedProgression.Items.Count; i++)
        {
            if (string.Equals(_pianoSavedProgression.Items[i]?.ToString(), previous, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }
        _pianoSavedProgression.SelectedIndex = index >= 0 ? index : 0;
    }

    private void ApplyCustomPianoProgression()
    {
        if (!TryEncodeCustomPianoProgression(_pianoCustomProgression.Text,
            out _, out _, out _, out _, out _, out _, out _, out _,
            out int count, out int totalBars, out string normalized, out string error))
        {
            SetStatus($"Progresión personalizada inválida. {error}", true);
            _pianoCustomProgression.Focus();
            return;
        }

        _pianoCustomProgression.Text = normalized;
        _pianoProgression.SelectedIndex = 5;
        UpdateParameters();
        _engine.RequestMetronomeReset();
        SetStatus($"Progresión personalizada aplicada: {normalized}. {count} posiciones, {totalBars} compases antes de repetir.");
    }

    private void SaveCustomPianoProgression()
    {
        string name = _pianoCustomProgressionName.Text.Trim();
        if (name.Length == 0)
        {
            SetStatus("Escriba un nombre para guardar la progresión personalizada.", true);
            _pianoCustomProgressionName.Focus();
            return;
        }

        if (!TryEncodeCustomPianoProgression(_pianoCustomProgression.Text,
            out _, out _, out _, out _, out _, out _, out _, out _,
            out int count, out int totalBars, out string normalized, out string error))
        {
            SetStatus($"No se pudo guardar. {error}", true);
            _pianoCustomProgression.Focus();
            return;
        }

        PianoProgressionItem? existing = _pianoProgressionLibrary.Items.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            _pianoProgressionLibrary.Items.Add(new PianoProgressionItem { Name = name, Sequence = normalized });
        else
            existing.Sequence = normalized;

        try
        {
            PianoProgressionStore.Save(_pianoProgressionLibrary);
            _pianoCustomProgression.Text = normalized;
            _pianoProgression.SelectedIndex = 5;
            PopulatePianoProgressionLibrary(name);
            UpdateParameters();
            _engine.RequestMetronomeReset();
            SetStatus($"Progresión {name} guardada. {count} posiciones, {totalBars} compases: {normalized}.");
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo guardar la progresión: {ex.Message}", true);
        }
    }

    private void LoadSelectedCustomPianoProgression()
    {
        string name = _pianoSavedProgression.SelectedItem?.ToString() ?? string.Empty;
        if (name.Length == 0)
        {
            SetStatus("No hay una progresión personalizada guardada seleccionada.", true);
            return;
        }

        PianoProgressionItem? item = _pianoProgressionLibrary.Items.FirstOrDefault(entry =>
            string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            SetStatus("La progresión seleccionada ya no existe.", true);
            PopulatePianoProgressionLibrary();
            return;
        }

        _pianoCustomProgression.Text = item.Sequence;
        _pianoCustomProgressionName.Text = item.Name;
        _pianoProgression.SelectedIndex = 5;
        ApplyCustomPianoProgression();
        SetStatus($"Progresión {item.Name} cargada: {item.Sequence}.");
    }

    private void DeleteSelectedCustomPianoProgression()
    {
        string name = _pianoSavedProgression.SelectedItem?.ToString() ?? string.Empty;
        if (name.Length == 0)
        {
            SetStatus("No hay una progresión personalizada seleccionada para eliminar.", true);
            return;
        }

        int removed = _pianoProgressionLibrary.Items.RemoveAll(entry =>
            string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            SetStatus("La progresión seleccionada ya no existe.", true);
            return;
        }

        try
        {
            PianoProgressionStore.Save(_pianoProgressionLibrary);
            PopulatePianoProgressionLibrary();
            SetStatus($"Progresión personalizada {name} eliminada.");
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo actualizar la biblioteca de progresiones: {ex.Message}", true);
        }
    }

    private static bool TryEncodeCustomPianoProgression(string text,
        out ulong pack1, out ulong pack2, out ulong pack3, out ulong pack4,
        out ulong pack5, out ulong pack6, out ulong pack7, out ulong pack8,
        out int count, out int totalBars, out string normalized, out string error)
    {
        return TryEncodeCustomPianoProgression(text,
            out pack1, out pack2, out pack3, out pack4,
            out pack5, out pack6, out pack7, out pack8,
            out _, out _, out count, out totalBars, out normalized, out error);
    }

    private static bool TryEncodeCustomPianoProgression(string text,
        out ulong pack1, out ulong pack2, out ulong pack3, out ulong pack4,
        out ulong pack5, out ulong pack6, out ulong pack7, out ulong pack8,
        out ushort[] expandedSequence, out ulong sequenceHash,
        out int count, out int totalBars, out string normalized, out string error)
    {
        pack1 = pack2 = pack3 = pack4 = pack5 = pack6 = pack7 = pack8 = 0;
        expandedSequence = Array.Empty<ushort>();
        sequenceHash = 0;
        count = 0;
        totalBars = 0;
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Escriba al menos un grado, por ejemplo I, V, vi, IV.";
            return false;
        }

        const int maxExpandedPositions = 65536; // Más de un mes de compases a tempos normales; límite sólo de seguridad de memoria.
        var sequence = new List<ushort>();
        var normalizedItems = new List<string>();

        if (!TrySplitProgressionTopLevel(text, out List<string> items, out error))
            return false;

        foreach (string rawItem in items)
        {
            string item = rawItem.Trim();
            if (item.Length == 0) continue;

            if (item.StartsWith("(", StringComparison.Ordinal))
            {
                int close = item.LastIndexOf(')');
                if (close <= 0)
                {
                    error = $"Falta cerrar el paréntesis en: {item}.";
                    return false;
                }

                string inner = item.Substring(1, close - 1);
                string suffix = item[(close + 1)..].Trim();
                int repetitions = 1;
                if (suffix.Length > 0)
                {
                    var repeatMatch = System.Text.RegularExpressions.Regex.Match(suffix, @"^[xX]\s*(\d+)$",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                    if (!repeatMatch.Success || !int.TryParse(repeatMatch.Groups[1].Value, out repetitions) || repetitions < 1)
                    {
                        error = $"Multiplicador de bloque inválido: {suffix}. Use por ejemplo (I, V, vi, IV) x7.";
                        return false;
                    }
                }

                if (!TrySplitProgressionTopLevel(inner, out List<string> blockItems, out error))
                    return false;
                if (blockItems.Count == 0)
                {
                    error = "El bloque entre paréntesis no puede estar vacío.";
                    return false;
                }

                var blockCodes = new List<ushort>();
                var blockNormalized = new List<string>();
                int blockBars = 0;
                foreach (string blockRaw in blockItems)
                {
                    string chordText = blockRaw.Trim();
                    if (chordText.Contains('(') || chordText.Contains(')'))
                    {
                        error = "No se admiten paréntesis anidados. Use bloques separados.";
                        return false;
                    }
                    if (!TryParsePianoDegree(chordText, out ushort code, out int bars, out string chordNormalized, out error))
                        return false;
                    blockCodes.Add(code);
                    blockNormalized.Add(chordNormalized);
                    blockBars += bars;
                }

                long newCount = (long)sequence.Count + (long)blockCodes.Count * repetitions;
                if (newCount > maxExpandedPositions)
                {
                    error = $"La expansión produciría {newCount:N0} posiciones. Reduzca el multiplicador del bloque; el límite de seguridad es {maxExpandedPositions:N0} posiciones.";
                    return false;
                }

                for (int r = 0; r < repetitions; r++)
                    sequence.AddRange(blockCodes);
                totalBars += blockBars * repetitions;

                string normalizedBlock = $"({string.Join(", ", blockNormalized)})";
                if (repetitions > 1) normalizedBlock += $" x{repetitions}";
                normalizedItems.Add(normalizedBlock);
            }
            else
            {
                if (item.Contains(')'))
                {
                    error = $"Paréntesis de cierre sin apertura en: {item}.";
                    return false;
                }
                if (!TryParsePianoDegree(item, out ushort code, out int bars, out string chordNormalized, out error))
                    return false;
                if (sequence.Count >= maxExpandedPositions)
                {
                    error = $"La progresión supera el límite de seguridad de {maxExpandedPositions:N0} posiciones.";
                    return false;
                }
                sequence.Add(code);
                totalBars += bars;
                normalizedItems.Add(chordNormalized);
            }
        }

        if (sequence.Count == 0)
        {
            error = "No se reconocieron grados romanos.";
            return false;
        }

        expandedSequence = sequence.ToArray();
        count = expandedSequence.Length;
        normalized = string.Join(", ", normalizedItems);

        // Se conservan los ocho packs históricos con las primeras 32 posiciones para compatibilidad.
        for (int i = 0; i < Math.Min(32, expandedSequence.Length); i++)
        {
            ushort code = expandedSequence[i];
            int shift = (i & 3) * 16;
            ulong shifted = ((ulong)code) << shift;
            switch (i >> 2)
            {
                case 0: pack1 |= shifted; break;
                case 1: pack2 |= shifted; break;
                case 2: pack3 |= shifted; break;
                case 3: pack4 |= shifted; break;
                case 4: pack5 |= shifted; break;
                case 5: pack6 |= shifted; break;
                case 6: pack7 |= shifted; break;
                default: pack8 |= shifted; break;
            }
        }

        // FNV-1a de 64 bits: identifica el contenido sin comparar arrays dentro del callback.
        ulong hash = 14695981039346656037UL;
        foreach (ushort code in expandedSequence)
        {
            hash ^= (byte)(code & 0xFF);
            hash *= 1099511628211UL;
            hash ^= (byte)(code >> 8);
            hash *= 1099511628211UL;
        }
        hash ^= (ulong)expandedSequence.Length;
        hash *= 1099511628211UL;
        sequenceHash = hash;
        return true;
    }

    private static bool TrySplitProgressionTopLevel(string text, out List<string> items, out string error)
    {
        items = new List<string>();
        error = string.Empty;
        int depth = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(')
            {
                depth++;
                if (depth > 1)
                {
                    error = "No se admiten paréntesis anidados. Use bloques separados.";
                    return false;
                }
            }
            else if (c == ')')
            {
                depth--;
                if (depth < 0)
                {
                    error = "Hay un paréntesis de cierre sin apertura.";
                    return false;
                }
            }
            else if (depth == 0 && (c == ',' || c == ';' || c == '|' || c == '>'))
            {
                items.Add(text.Substring(start, i - start));
                start = i + 1;
            }
        }
        if (depth != 0)
        {
            error = "Falta cerrar un paréntesis.";
            return false;
        }
        items.Add(text[start..]);
        items.RemoveAll(value => string.IsNullOrWhiteSpace(value));
        return true;
    }

    private static bool TryParsePianoDegree(string text, out ushort code, out int bars, out string normalized, out string error)
    {
        code = 0;
        bars = 1;
        normalized = string.Empty;
        error = string.Empty;
        const string pattern = @"^(?<acc>[b#]?)(?<roman>VII|VI|IV|V|III|II|I|vii|vi|iv|v|iii|ii|i)(?<dim>°|dim)?(?<sev>maj7|Maj7|MAJ7|M7|7)?(?:\s*[xX]\s*(?<bars>\d+))?$";
        var match = System.Text.RegularExpressions.Regex.Match(text.Trim(), pattern,
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            error = $"No pude interpretar el grado: {text}. Use por ejemplo V, vi, V7, ii7, Imaj7, vii°7, bVII o vi x2.";
            return false;
        }

        string roman = match.Groups["roman"].Value;
        int degree = roman.ToUpperInvariant() switch
        {
            "I" => 0, "II" => 1, "III" => 2, "IV" => 3, "V" => 4, "VI" => 5, "VII" => 6, _ => -1
        };
        if (degree < 0)
        {
            error = $"Grado no reconocido: {roman}.";
            return false;
        }

        if (match.Groups["bars"].Success && (!int.TryParse(match.Groups["bars"].Value, out bars) || bars < 1 || bars > 32))
        {
            error = "La duración de un acorde puede ser de 1 a 32 compases. Para repetir varios acordes juntos use (grados) x cantidad.";
            return false;
        }

        bool diminished = match.Groups["dim"].Success;
        bool allLower = roman.All(char.IsLower);
        int quality = diminished ? 2 : allLower ? 1 : 0;
        string seventhText = match.Groups["sev"].Value;
        int seventh = 0; // 0 sin séptima; 1 séptima menor; 2 séptima mayor; 3 séptima disminuida.
        if (!string.IsNullOrEmpty(seventhText))
        {
            bool majorSeventh = seventhText.Equals("maj7", StringComparison.OrdinalIgnoreCase) || seventhText == "M7";
            seventh = majorSeventh ? 2 : diminished ? 3 : 1;
        }
        string accidentalText = match.Groups["acc"].Value;
        int accidentalCode = accidentalText == "b" ? 1 : accidentalText == "#" ? 2 : 0;
        code = (ushort)(degree | ((bars - 1) << 3) | (quality << 8) | (accidentalCode << 10) | (seventh << 12));

        var builder = new StringBuilder();
        builder.Append(accidentalText).Append(roman);
        if (diminished) builder.Append('°');
        if (seventh == 2) builder.Append("maj7");
        else if (seventh != 0) builder.Append('7');
        if (bars > 1) builder.Append(" x").Append(bars);
        normalized = builder.ToString();
        return true;
    }

    private void TogglePianoShortcut()
    {
        _pianoEnabled.Checked = !_pianoEnabled.Checked;
        UpdateParameters();
        _engine.RequestMetronomeReset();
        string sound = _pianoSound.SelectedItem?.ToString() ?? "sonido actual";
        string key = _pianoKey.SelectedItem?.ToString() ?? "tonalidad actual";
        string progression = _pianoProgression.SelectedIndex == 5
            ? $"personalizada {_pianoCustomProgression.Text}"
            : (_pianoProgression.SelectedItem?.ToString() ?? "progresión actual");
        string style = _pianoStyle.SelectedItem?.ToString() ?? "estilo actual";
        string startNotice = _pianoEnabled.Checked && !_engine.IsRunning ? " Pulse F4 para iniciar el audio." : string.Empty;
        SetStatus($"Piano de acompañamiento {(_pianoEnabled.Checked ? "activado" : "desactivado")}. {sound}; {key}; {progression}; {style}; {_metronomeBpm.Value:0} BPM.{startNotice}");
    }

    private void ToggleBackingBandPair()
    {
        // F12 es el control práctico del acompañamiento completo. La primera pulsación
        // arma el seguimiento por guitarra y garantiza que los tres generadores queden
        // encendidos. La siguiente pulsación los apaga. Si el usuario había encendido
        // los tres manualmente, F12 primero los arma en vez de apagarlos.
        bool allEnabled = _drumsEnabled.Checked && _backingBassEnabled.Checked && _pianoEnabled.Checked;
        bool enable = !(_backingBandFollowGuitarArmed && allEnabled);
        _backingBandFollowGuitarArmed = enable;

        _updatingBackingPairShortcut = true;
        try
        {
            _drumsEnabled.Checked = enable;
            _backingBassEnabled.Checked = enable;
            _pianoEnabled.Checked = enable;
        }
        finally
        {
            _updatingBackingPairShortcut = false;
        }

        // Fuerza el estado final de los tres generadores al DSP en una sola
        // revisión para que arranquen desde el mismo pulso musical.
        UpdateParameters();
        _engine.RequestMetronomeReset();

        string pattern = _drumPattern.SelectedItem?.ToString() ?? "patrón actual";
        string key = _backingBassKey.SelectedItem?.ToString() ?? "tonalidad actual";
        string mode = _backingBassMode.SelectedItem?.ToString() ?? "modo actual";
        string piano = _pianoSound.SelectedItem?.ToString() ?? "piano actual";

        if (enable)
        {
            string startNotice = _engine.IsRunning ? string.Empty : " Pulse F4 para iniciar el audio.";
            SetStatus($"Acompañamiento armado: batería, bajo y piano. Empezará desde el tiempo 1 cuando detecte la guitarra y se detendrá después de una pausa. {pattern}; bajo en {key} {mode}; {piano}; {_metronomeBpm.Value:0} BPM.{startNotice}");
        }
        else
        {
            SetStatus("Acompañamiento desarmado: batería, bajo y piano apagados.");
        }
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Alt && !e.Control && !e.Shift)
        {
            int section = e.KeyCode switch
            {
                Keys.C => 0,
                Keys.E => 1,
                Keys.F => 2,
                Keys.B => 3,
                Keys.N => 4,
                Keys.H => 5,
                Keys.M => 6,
                Keys.O => 7,
                Keys.I => 8,
                Keys.G => 5,
                Keys.D => 9,
                _ => -1
            };
            if (section >= 0)
            {
                Control focus = section switch
                {
                    0 => _driverCombo,
                    1 => _channelCombo,
                    2 => _effectSelector,
                    3 => _factoryPresetCombo,
                    4 => _namBankCombo,
                    5 => e.KeyCode == Keys.G ? _practiceRecordButton : _tunerEnabled,
                    6 => _voiceEnabled,
                    7 => _twoGuitarMode,
                    8 => _midiInputCombo,
                    9 => _audioDiagnosticReport,
                    _ => _driverCombo
                };
                ShowSection(section, focus);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
        }

        // Respaldo para controles que sí entreguen Shift+F a KeyDown.
        // La ruta principal está en ProcessCmdKey, que se ejecuta antes.
        if (e.Shift && !e.Control && !e.Alt && HandleShiftFunctionKey(e.KeyCode))
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.F1)
        {
            ShowKeyboardHelp();
            e.Handled = true;
            return;
        }

        if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.F4)
        {
            ToggleAudio();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.F5)
        {
            ToggleNamQuick();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.F6)
        {
            LoadAdjacentNamFromBank(1);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.F7)
        {
            LoadAdjacentNamFromBank(-1);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.F8)
        {
            TapTempo();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.F9)
        {
            ReadTunerWithJaws();
            e.Handled = true;
            return;
        }

        if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.F10)
        {
            PlaySelectedReferenceTone();
            e.Handled = true;
            return;
        }

        if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.F12)
        {
            ToggleBackingBandPair();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        int keyCodeValue = (int)e.KeyCode;
        if (e.Alt && keyCodeValue >= (int)Keys.D1 && keyCodeValue <= (int)Keys.D3)
        {
            int sceneIndex = keyCodeValue - (int)Keys.D1;
            RecallScene(sceneIndex, announce: true);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.Shift && keyCodeValue >= (int)Keys.D1 && keyCodeValue <= (int)Keys.D3)
        {
            int sceneIndex = keyCodeValue - (int)Keys.D1;
            SaveCurrentScene(sceneIndex, useNameField: false);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.S)
        {
            SaveCurrentScene(_sceneCombo.SelectedIndex);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && e.Alt && keyCodeValue >= (int)Keys.D1 && keyCodeValue <= (int)Keys.D9)
        {
            int channel = keyCodeValue - (int)Keys.D1;
            if (channel < _channelCombo.Items.Count)
            {
                _channelCombo.SelectedIndex = channel;
                SetStatus($"Amplificador {channel + 1}: {_channelCombo.SelectedItem}.");
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && !e.Alt && keyCodeValue >= (int)Keys.D1 && keyCodeValue <= (int)Keys.D3)
        {
            int channel = keyCodeValue - (int)Keys.D1;
            _channelCombo.SelectedIndex = channel;
            SetStatus($"Seleccionado {_channelCombo.SelectedItem}.");
            e.Handled = true;
            return;
        }

        if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.F9)
        {
            ToggleTunerGuitarTarget();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.B)
        {
            if (_twoGuitarMode.Checked)
            {
                SetStatus("Modo Dos Guitarras: procesamiento activo en ambas guitarras. El bypass general está bloqueado para evitar que una cadena quede cruda.");
            }
            else
            {
                _simulationEnabled.Checked = !_simulationEnabled.Checked;
                SetStatus($"Simulación completa {(_simulationEnabled.Checked ? "activada" : "desactivada; guitarra directa")}.");
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.T)
        {
            _tunerEnabled.Checked = !_tunerEnabled.Checked;
            string tunerTarget = _twoGuitarMode.Checked ? SelectedTunerGuitarName : "rig principal";
            SetStatus($"Afinador {(_tunerEnabled.Checked ? "activado" : "desactivado")} para {tunerTarget}.");
            e.Handled = true;
        }
        else if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.M)
        {
            _metronomeEnabled.Checked = !_metronomeEnabled.Checked;
            SetStatus($"Metrónomo {(_metronomeEnabled.Checked ? "activado" : "desactivado")}. Tempo {_metronomeBpm.Value:0} BPM.");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.G)
        {
            _gateEnabled.Checked = !_gateEnabled.Checked;
            SetStatus($"Puerta de ruidos {(_gateEnabled.Checked ? "activada" : "desactivada")}.");
            e.Handled = true;
        }
        else if (e.Control && !e.Shift && e.KeyCode == Keys.O)
        {
            _overdriveEnabled.Checked = !_overdriveEnabled.Checked;
            SetStatus($"Distorsión estilo DS-1 {(_overdriveEnabled.Checked ? "activada" : "desactivada")}.");
            e.Handled = true;
        }
        else if (e.Control && e.Shift && e.KeyCode == Keys.O)
        {
            _ts9Enabled.Checked = !_ts9Enabled.Checked;
            SetStatus($"Overdrive seleccionable {(_ts9Enabled.Checked ? "activado" : "desactivado")}.");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.Shift && e.KeyCode == Keys.B)
        {
            _boosterEnabled.Checked = !_boosterEnabled.Checked;
            SetStatus($"Booster limpio {(_boosterEnabled.Checked ? "activado" : "desactivado")}.");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.L)
        {
            _fxLoopEnabled.Checked = !_fxLoopEnabled.Checked;
            SetStatus($"Loop de efectos {(_fxLoopEnabled.Checked ? "activado" : "desactivado")}.");
            e.Handled = true;
        }
        else if (e.Control && e.Shift && e.KeyCode == Keys.C)
        {
            _compressorEnabled.Checked = !_compressorEnabled.Checked;
            SetStatus($"Compresor limpio {(_compressorEnabled.Checked ? "activado" : "desactivado")}.");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.Alt && !e.Shift && e.KeyCode == Keys.C)
        {
            _analogChorusEnabled.Checked = !_analogChorusEnabled.Checked;
            SetStatus($"Chorus analógico tipo MXR {(_analogChorusEnabled.Checked ? "activado" : "desactivado")}.");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.C)
        {
            _chorusEnabled.Checked = !_chorusEnabled.Checked;
            SetStatus($"Chorus ensemble {(_chorusEnabled.Checked ? "activado" : "desactivado")}.");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && !e.Shift && e.KeyCode == Keys.W)
        {
            _autoWahEnabled.Checked = !_autoWahEnabled.Checked;
            SetStatus($"Wah {(_autoWahEnabled.Checked ? "activado" : "desactivado")}.");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && !e.Shift && e.KeyCode == Keys.P)
        {
            _phaserEnabled.Checked = !_phaserEnabled.Checked;
            SetStatus($"Phaser {(_phaserEnabled.Checked ? "activado" : "desactivado")}.");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.F)
        {
            _flangerEnabled.Checked = !_flangerEnabled.Checked;
            SetStatus($"Flanger {(_flangerEnabled.Checked ? "activado" : "desactivado")}.");
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && !e.Shift && !e.Alt && e.KeyCode == Keys.D)
        {
            ToggleCurrentDelay();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.R)
        {
            _reverbEnabled.Checked = !_reverbEnabled.Checked;
            SetStatus($"Reverb {(_reverbEnabled.Checked ? "activado" : "desactivado")}.");
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.I)
        {
            LoadImpulseResponse();
            e.Handled = true;
        }
    }

    private void ShowKeyboardHelp()
    {
        const string help =
            "Atajos de teclado:\r\n\r\n" +
            "CAPA PRINCIPAL\r\n" +
            "F1: ayuda completa.\r\n" +
            "F4: iniciar o detener Amp Accessible y el audio.\r\n" +
            "F5: activar o desactivar NAM de Guitarra 2 o rig principal.\r\n" +
            "F6: cargar la captura NAM siguiente de Guitarra 2 o rig principal.\r\n" +
            "F7: cargar la captura NAM anterior de Guitarra 2 o rig principal.\r\n" +
            "Control Alt F5: activar o desactivar NAM de Guitarra 1.\r\n" +
            "Control Alt F6: cargar la captura NAM siguiente de Guitarra 1.\r\n" +
            "Control Alt F7: cargar la captura NAM anterior de Guitarra 1.\r\n" +
            "Control F6: cargar el IR A de Guitarra 2 siguiente de su carpeta.\r\n" +
            "Control F7: cargar el IR A de Guitarra 2 anterior de su carpeta.\r\n" +
            "Control Shift F6: cargar el IR de Guitarra 1 siguiente de su carpeta.\r\n" +
            "Control Shift F7: cargar el IR de Guitarra 1 anterior de su carpeta.\r\n" +
            "F8: tap tempo.\r\n" +
            "F9: leer con JAWS la afinación de la guitarra seleccionada.\r\n" +
            "Control F9: en Modo Dos Guitarras, alternar afinador entre Guitarra 1 e Input 1 y Guitarra 2 e Input 2.\r\n" +
            "F10: reproducir tono de referencia por la ruta de la guitarra seleccionada.\r\n" +
            "F12: armar o desarmar juntos batería, bajo y piano. Armados, esperan la guitarra, arrancan desde el tiempo 1 y paran tras una pausa.\r\n" +
            "En Acompañamiento, Progresión personalizada permite escribir grados como I, V, V, vi, IV o I x2, V, vi x2, IV; se pueden guardar y cargar con nombre.\r\n" +
            "Control F12: activar o desactivar sólo el piano de acompañamiento.\r\n\r\n" +
            "SEGUNDA CAPA, SHIFT MÁS F\r\n" +
            "Shift F1: anunciar estado general del rig, acompañamiento, grabadora y looper.\r\n" +
            "Shift F2: iniciar o detener y guardar la grabadora de práctica, máximo 5 minutos.\r\n" +
            "Shift F3: iniciar o cerrar la primera vuelta manual del looper; si la sincronización está activa, inicia la vuelta que se cierra sola por compases.\r\n" +
            "Shift F4: activar o desactivar overdub. El botón Deshacer último overdub recupera la mezcla anterior.\r\n" +
            "Shift F5: reproducir o detener el loop.\r\n" +
            "Shift F6: guardar el loop como WAV.\r\n" +
            "Shift F7: borrar el loop.\r\n" +
            "Shift F8: activar o desactivar sólo batería.\r\n" +
            "Shift F9: activar o desactivar sólo bajo.\r\n" +
            "Shift F10: reservado para el menú contextual estándar de Windows y JAWS.\r\n" +
            "Shift F11: guardar la configuración actual en la escena seleccionada.\r\n" +
            "Shift F12: cambiar la variante de acompañamiento, batería y línea de bajo.\r\n\r\n" +
            "EFECTOS Y ESCENAS\r\n" +
            "Control B: activar o desactivar toda la simulación; al apagarla queda guitarra directa.\r\n" +
            "Alt 1, 2 o 3: cargar una de las tres escenas.\r\n" +
            "Control Shift 1, 2 o 3: guardar el sonido actual en esa escena.\r\n" +
            "Control S: guardar el sonido actual en la escena seleccionada.\r\n" +
            "Control T: activar o desactivar afinador. En modo dual sólo analiza la guitarra elegida en Herramientas.\r\n" +
            "Control M: activar o desactivar metrónomo.\r\n" +
            "Control 1, 2 o 3: seleccionar los tres primeros amplificadores internos.\r\n" +
            "Control Alt 1 a 9: seleccionar cualquiera de los nueve amplificadores.\r\n" +
            "Control G: puerta de ruidos.\r\n" +
            "Control Shift C: compresor limpio.\r\n" +
            "Control W: auto wah.\r\n" +
            "Control O: DS-1.\r\n" +
            "Control Shift O: overdrive seleccionable.\r\n" +
            "Control Shift B: booster limpio.\r\n" +
            "Control L: loop de efectos.\r\n" +
            "Control P: phaser.\r\n" +
            "Control F: flanger.\r\n" +
            "Control C: chorus ensemble.\r\n" +
            "Control Alt C: chorus analógico.\r\n" +
            "Control D: activar o desactivar el delay actualmente seleccionado, sea digital, analógico, tape o reverse.\r\n" +
            "Control R: reverb.\r\n" +
            "Control I: cargar archivo IR; su carpeta queda preparada automáticamente para recorrer las demás tomas.\r\n" +
            "En Equipo, Elegir carpeta de IR indexa WAV, WAVE, AIF y AIFF; IR anterior y siguiente recorren circularmente la carpeta.\r\n\r\n" +
            "DOS GUITARRAS\r\n" +
            "Alt O: abrir la sección Dos guitarras. Guitarra 1 usa Input 1 y Guitarra 2 usa Input 2.\r\n" +
            "Guitarra 1 y Guitarra 2 conservan memorias de efectos, IR y NAM independientes. En Alt O, el selector NAM rápido permite activar, elegir, cargar con Enter, anterior y siguiente para cualquiera de las dos guitarras. Para trims, gabinete, Auto Level y configuración técnica use Alt N y elija Guitarra NAM a editar. Ambos NAM pueden procesarse simultáneamente. El afinador de Alt H permite elegir Guitarra 1 o Guitarra 2; F9 lee y Control F9 alterna la fuente sin afectar la otra guitarra.\r\n\r\n" +
            "MIDI\r\n" +
            "Alt I: abrir la sección MIDI.\r\n" +
            "Program Change 0 a 29: cargar F01 a F30 cuando el mapeo directo está activado.\r\n" +
            "MIDI Learn: permite asignar CC o Program Change a funciones, efectos, NAM, looper y bancos.\r\n" +
            "Para usar un pedal de expresión como wah, seleccione Pedal de expresión: posición del wah y mueva el pedal durante MIDI Learn. El wah cambia automáticamente a modo Manual.\r\n" +
            "El simulador MIDI interno permite probar todo sin una pedalera física.\r\n\r\n" +
            "DIAGNÓSTICO\r\n" +
            "Alt D: abrir diagnóstico de audio accesible.\r\n" +
            "Incluye driver ASIO, frecuencia, buffer efectivo, carga DSP, callbacks, errores, NAM, videollamadas y memoria.\r\n" +
            "Puede copiar o guardar el informe completo para revisar una falla.\r\n\r\n" +
            "NAVEGACIÓN\r\n" +
            "Alt C Configuración, Alt E Equipo, Alt F Efectos, Alt B Bancos, Alt N NAM, Alt H Herramientas, Alt M Micrófono y videollamadas, Alt O Dos guitarras, Alt I MIDI y Alt D Diagnóstico.\r\n" +
            "Tab y Shift Tab recorren controles. Flechas modifican listas y valores. Control Tab no cambia de sección; use Alt más la letra indicada.";
        MessageBox.Show(this, help, "Ayuda de teclado", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SetStatus(string message, bool error = false)
    {
        _statusLabel.Text = message;
        _statusLabel.AccessibleName = $"Estado: {message}";
        if (error && !_engine.IsRunning)
        {
            // Evita abrir un sonido del sistema sobre la misma interfaz mientras ASIO está activo.
            SystemSounds.Exclamation.Play();
        }
    }

    private static GroupBox CreateGroup(string text)
    {
        return new GroupBox
        {
            Text = text,
            // El texto sigue visible como título del grupo, pero no se expone como
            // nombre accesible del contenedor para que JAWS no lo repita en cada Tab.
            AccessibleName = string.Empty,
            AccessibleDescription = string.Empty,
            AccessibleRole = AccessibleRole.None,
            TabStop = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(10),
            Margin = new Padding(0, 8, 0, 0)
        };
    }

    private static TableLayoutPanel CreateTwoColumnTable()
    {
        var table = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 0,
            Padding = new Padding(4),
            AccessibleName = string.Empty,
            AccessibleDescription = string.Empty,
            AccessibleRole = AccessibleRole.None,
            TabStop = false
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        return table;
    }

    private static void AddLabeledControl(TableLayoutPanel table, string labelText, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 7, 12, 0),
            UseMnemonic = true,
            AccessibleName = string.Empty,
            AccessibleDescription = string.Empty,
            AccessibleRole = AccessibleRole.None,
            TabStop = false
        };
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(3, 3, 3, 6);
        table.Controls.Add(label, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static void ConfigureCombo(ComboBox combo, string accessibleName, string description)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.AccessibleName = accessibleName;
        combo.AccessibleDescription = description;
        combo.Width = 520;
    }

    private static NumericUpDown ConfigureNumeric(NumericUpDown control, string accessibleName)
    {
        control.AccessibleName = accessibleName;
        control.AccessibleRole = AccessibleRole.SpinButton;
        control.Width = 180;
        return control;
    }

    private static NumericUpDown CreateDecimalControl(decimal minimum, decimal maximum, decimal value,
        decimal increment, int decimals = 1)
    {
        return new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Increment = increment,
            DecimalPlaces = decimals,
            ThousandsSeparator = false
        };
    }
}
