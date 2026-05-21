using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AudioABTester.Audio;
using AudioABTester.Commands;
using AudioABTester.Services;

namespace AudioABTester.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AudioEngine _audioEngine;
    private readonly IFileDialogService _fileDialogService;
    private readonly DispatcherTimer _transportTimer;
    private readonly Random _random = new();

    private bool _isInternalPositionUpdate;
    private bool _hasTestingModeChoice;
    private bool _isBlindTestSelected;
    private bool _isBlindAssignmentPrepared;
    private bool _isBlindMappingRevealed;
    private bool _isFirstCandidateAssignedToA;

    private string? _blindCandidatePath1;
    private string? _blindCandidatePath2;

    private string _fileAName = "No file selected";
    private string _fileBName = "No file selected";

    private string? _selectedOutputDeviceId;
    private string _outputDeviceStatus = "Output: Not selected";

    private double _positionSeconds;
    private double _durationSeconds = 1d;
    private string _playbackTime = "00:00 / 00:00";

    public MainViewModel()
        : this(new AudioEngine(new VolumeMatchService()), new FileDialogService())
    {
    }

    public MainViewModel(AudioEngine audioEngine, IFileDialogService fileDialogService)
    {
        _audioEngine = audioEngine;
        _fileDialogService = fileDialogService;

        SelectBlindYesCommand = new RelayCommand(SelectBlindYes);
        SelectBlindNoCommand = new RelayCommand(SelectBlindNo);

        LoadFileACommand = new RelayCommand(LoadFileA, () => ShowMainWorkflow);
        LoadFileBCommand = new RelayCommand(LoadFileB, () => ShowMainWorkflow);

        StartCommand = new RelayCommand(StartPlayback, () => _audioEngine.CanStart);
        PauseCommand = new RelayCommand(PausePlayback, () => _audioEngine.CanStart);
        StopCommand = new RelayCommand(StopPlayback, () => _audioEngine.CanStart);

        ListenACommand = new RelayCommand(() => ListenTo(PlaybackSource.A), () => _audioEngine.CanStart);
        ListenBCommand = new RelayCommand(() => ListenTo(PlaybackSource.B), () => _audioEngine.CanStart);
        RevealBlindMappingCommand = new RelayCommand(RevealBlindMapping, () => ShowRevealButton);
        ResetBlindRoundCommand = new RelayCommand(ResetBlindRound, () => IsBlindTestSelected && ShowMainWorkflow);

        RefreshOutputDevicesCommand = new RelayCommand(RefreshOutputDevices);
        TogglePlayPauseCommand = new RelayCommand(TogglePlayPause, () => _audioEngine.CanStart);
        ToggleSourceCommand = new RelayCommand(ToggleSource, () => _audioEngine.CanStart);
        SeekBackwardCommand = new RelayCommand(() => SeekBy(TimeSpan.FromSeconds(-5)), () => _audioEngine.CanStart);
        SeekForwardCommand = new RelayCommand(() => SeekBy(TimeSpan.FromSeconds(5)), () => _audioEngine.CanStart);

        _transportTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _transportTimer.Tick += (_, _) => RefreshTransport();
        _transportTimer.Start();

        RefreshOutputDevices();
        RefreshTransport();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand SelectBlindYesCommand { get; }

    public RelayCommand SelectBlindNoCommand { get; }

    public RelayCommand LoadFileACommand { get; }

    public RelayCommand LoadFileBCommand { get; }

    public RelayCommand StartCommand { get; }

    public RelayCommand PauseCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand ListenACommand { get; }

    public RelayCommand ListenBCommand { get; }

    public RelayCommand RevealBlindMappingCommand { get; }

    public RelayCommand ResetBlindRoundCommand { get; }

    public RelayCommand RefreshOutputDevicesCommand { get; }

    public RelayCommand TogglePlayPauseCommand { get; }

    public RelayCommand ToggleSourceCommand { get; }

    public RelayCommand SeekBackwardCommand { get; }

    public RelayCommand SeekForwardCommand { get; }

    public bool ShowBlindChoiceQuestion => !_hasTestingModeChoice;

    public bool ShowMainWorkflow => _hasTestingModeChoice;

    public bool IsBlindTestSelected => _isBlindTestSelected;

    public bool IsStandardTestSelected => _hasTestingModeChoice && !_isBlindTestSelected;

    public string LoadFileAButtonText
    {
        get
        {
            if (!IsBlindTestSelected)
            {
                return "Load A";
            }

            return _isBlindMappingRevealed ? "Load A" : "Load File 1";
        }
    }

    public string LoadFileBButtonText
    {
        get
        {
            if (!IsBlindTestSelected)
            {
                return "Load B";
            }

            return _isBlindMappingRevealed ? "Load B" : "Load File 2";
        }
    }

    public string ComparisonPanelTitle => IsBlindTestSelected ? "Blind Comparison" : "Comparison";

    public string FileAName
    {
        get => _fileAName;
        private set
        {
            if (SetProperty(ref _fileAName, value))
            {
                OnPropertyChanged(nameof(FileANameDisplay));
            }
        }
    }

    public string FileBName
    {
        get => _fileBName;
        private set
        {
            if (SetProperty(ref _fileBName, value))
            {
                OnPropertyChanged(nameof(FileBNameDisplay));
            }
        }
    }

    public string FileANameDisplay => ShouldHideBlindFileNames ? "Hidden for blind test" : FileAName;

    public string FileBNameDisplay => ShouldHideBlindFileNames ? "Hidden for blind test" : FileBName;

    public bool ShowRevealButton => IsBlindTestSelected && _isBlindAssignmentPrepared && !_isBlindMappingRevealed;

    public string BlindStatusText
    {
        get
        {
            if (ShowBlindChoiceQuestion)
            {
                return "Choose whether this round should be blind before loading files.";
            }

            if (!IsBlindTestSelected)
            {
                return "Standard comparison mode selected. Load A/B and press play.";
            }

            if (!_isBlindAssignmentPrepared)
            {
                return "Blind mode selected. Load File 1 and File 2 to randomize A/B.";
            }

            if (!_isBlindMappingRevealed)
            {
                return "Randomizing A and B files .... complete. Okay, time for you to press play.";
            }

            return $"Reveal: A = {FileAName}, B = {FileBName}.";
        }
    }

    public string CurrentSourceText => $"Current Source: {_audioEngine.CurrentSource}";

    public bool IsListeningToA => _audioEngine.CurrentSource == PlaybackSource.A;

    public bool IsListeningToB => _audioEngine.CurrentSource == PlaybackSource.B;

    public string ToggleHintText => "Space: Play/Pause   X: Toggle A/B";

    public ObservableCollection<AudioOutputDevice> OutputDevices { get; } = new();

    public string? SelectedOutputDeviceId
    {
        get => _selectedOutputDeviceId;
        set
        {
            if (!SetProperty(ref _selectedOutputDeviceId, value) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (string.Equals(value, _audioEngine.CurrentOutputDeviceId, StringComparison.Ordinal))
            {
                return;
            }

            _audioEngine.SetOutputDevice(value);
            OutputDeviceStatus = $"Output: {_audioEngine.CurrentOutputDeviceName}";
        }
    }

    public string OutputDeviceStatus
    {
        get => _outputDeviceStatus;
        private set => SetProperty(ref _outputDeviceStatus, value);
    }

    public double PositionSeconds
    {
        get => _positionSeconds;
        set
        {
            if (!SetProperty(ref _positionSeconds, value) || _isInternalPositionUpdate)
            {
                return;
            }

            _audioEngine.Seek(TimeSpan.FromSeconds(value));
            UpdatePlaybackTime();
        }
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        private set => SetProperty(ref _durationSeconds, value);
    }

    public string PlaybackTime
    {
        get => _playbackTime;
        private set => SetProperty(ref _playbackTime, value);
    }

    public string PlayPauseLabel => _audioEngine.IsPlaying ? "Pause" : "Play";

    public void Dispose()
    {
        _transportTimer.Stop();
        _audioEngine.Dispose();
    }

    private bool ShouldHideBlindFileNames => IsBlindTestSelected && !_isBlindMappingRevealed;

    private void SelectBlindYes()
    {
        _hasTestingModeChoice = true;
        _isBlindTestSelected = true;
        ResetRoundState();
        NotifyModeChanged();
        RefreshCommands();
    }

    private void SelectBlindNo()
    {
        _hasTestingModeChoice = true;
        _isBlindTestSelected = false;
        ResetRoundState();
        NotifyModeChanged();
        RefreshCommands();
    }

    private void ResetRoundState()
    {
        _audioEngine.ClearTracks();

        _blindCandidatePath1 = null;
        _blindCandidatePath2 = null;

        _isBlindAssignmentPrepared = false;
        _isBlindMappingRevealed = false;
        _isFirstCandidateAssignedToA = false;

        FileAName = "No file selected";
        FileBName = "No file selected";
    }

    private void LoadFileA()
    {
        var path = _fileDialogService.PickAudioFile();
        if (path is null)
        {
            return;
        }

        if (!IsBlindTestSelected)
        {
            _audioEngine.Stop();
            var track = _audioEngine.LoadTrackA(path);
            FileAName = track.DisplayName;
            RefreshTransport();
            RefreshCommands();
            return;
        }

        if (_isBlindAssignmentPrepared && _isBlindMappingRevealed)
        {
            _audioEngine.Stop();
            var track = _audioEngine.LoadTrackA(path);
            FileAName = track.DisplayName;

            if (_isFirstCandidateAssignedToA)
            {
                _blindCandidatePath1 = path;
            }
            else
            {
                _blindCandidatePath2 = path;
            }

            NotifyBlindStateChanged();
            RefreshTransport();
            RefreshCommands();
            return;
        }

        _blindCandidatePath1 = path;
        _isBlindAssignmentPrepared = false;
        _isBlindMappingRevealed = false;
        TryPrepareBlindAssignment();
    }

    private void LoadFileB()
    {
        var path = _fileDialogService.PickAudioFile();
        if (path is null)
        {
            return;
        }

        if (!IsBlindTestSelected)
        {
            _audioEngine.Stop();
            var track = _audioEngine.LoadTrackB(path);
            FileBName = track.DisplayName;
            RefreshTransport();
            RefreshCommands();
            return;
        }

        if (_isBlindAssignmentPrepared && _isBlindMappingRevealed)
        {
            _audioEngine.Stop();
            var track = _audioEngine.LoadTrackB(path);
            FileBName = track.DisplayName;

            if (_isFirstCandidateAssignedToA)
            {
                _blindCandidatePath2 = path;
            }
            else
            {
                _blindCandidatePath1 = path;
            }

            NotifyBlindStateChanged();
            RefreshTransport();
            RefreshCommands();
            return;
        }

        _blindCandidatePath2 = path;
        _isBlindAssignmentPrepared = false;
        _isBlindMappingRevealed = false;
        TryPrepareBlindAssignment();
    }

    private void TryPrepareBlindAssignment()
    {
        if (_blindCandidatePath1 is null || _blindCandidatePath2 is null)
        {
            NotifyBlindStateChanged();
            RefreshCommands();
            return;
        }

        _audioEngine.Stop();

        _isFirstCandidateAssignedToA = _random.Next(0, 2) == 0;
        var pathForA = _isFirstCandidateAssignedToA ? _blindCandidatePath1 : _blindCandidatePath2;
        var pathForB = _isFirstCandidateAssignedToA ? _blindCandidatePath2 : _blindCandidatePath1;

        var trackA = _audioEngine.LoadTrackA(pathForA);
        var trackB = _audioEngine.LoadTrackB(pathForB);

        FileAName = trackA.DisplayName;
        FileBName = trackB.DisplayName;

        _isBlindAssignmentPrepared = true;
        _isBlindMappingRevealed = false;

        NotifyBlindStateChanged();
        RefreshTransport();
        RefreshCommands();
    }

    private void RevealBlindMapping()
    {
        if (!ShowRevealButton)
        {
            return;
        }

        _isBlindMappingRevealed = true;
        NotifyBlindStateChanged();
        RefreshCommands();
    }

    private void ResetBlindRound()
    {
        if (!IsBlindTestSelected)
        {
            return;
        }

        ResetRoundState();

        RefreshTransport();
        NotifyBlindStateChanged();
        RefreshCommands();
    }

    private void StartPlayback()
    {
        _audioEngine.Start();
        RefreshTransport();
        RefreshCommands();
    }

    private void PausePlayback()
    {
        _audioEngine.Pause();
        RefreshTransport();
        RefreshCommands();
    }

    private void StopPlayback()
    {
        _audioEngine.Stop();
        RefreshTransport();
        RefreshCommands();
    }

    private void TogglePlayPause()
    {
        if (_audioEngine.IsPlaying)
        {
            _audioEngine.Pause();
        }
        else
        {
            _audioEngine.Start();
        }

        RefreshTransport();
        RefreshCommands();
    }

    private void ListenTo(PlaybackSource source)
    {
        _audioEngine.ListenTo(source);
        RefreshTransport();
    }

    private void ToggleSource()
    {
        _audioEngine.ToggleSource();
        RefreshTransport();
    }

    private void RefreshOutputDevices()
    {
        var devices = _audioEngine.GetOutputDevices();
        OutputDevices.Clear();
        foreach (var device in devices)
        {
            OutputDevices.Add(device);
        }

        var currentId = _audioEngine.CurrentOutputDeviceId;
        if (OutputDevices.Any(d => d.Id == currentId))
        {
            _selectedOutputDeviceId = currentId;
            OnPropertyChanged(nameof(SelectedOutputDeviceId));
        }
        else if (OutputDevices.Count > 0)
        {
            SelectedOutputDeviceId = OutputDevices[0].Id;
        }

        OutputDeviceStatus = $"Output: {_audioEngine.CurrentOutputDeviceName}";
    }

    private void SeekBy(TimeSpan amount)
    {
        _audioEngine.SeekBy(amount);
        RefreshTransport();
    }

    private void RefreshTransport()
    {
        if (_audioEngine.IsPlaying && _audioEngine.IsAtEnd)
        {
            _audioEngine.Stop();
        }

        _isInternalPositionUpdate = true;
        PositionSeconds = _audioEngine.CurrentPosition.TotalSeconds;
        DurationSeconds = Math.Max(1d, _audioEngine.TotalDuration.TotalSeconds);
        _isInternalPositionUpdate = false;

        UpdatePlaybackTime();
        OnPropertyChanged(nameof(CurrentSourceText));
        OnPropertyChanged(nameof(IsListeningToA));
        OnPropertyChanged(nameof(IsListeningToB));
        OnPropertyChanged(nameof(ToggleHintText));
        OnPropertyChanged(nameof(PlayPauseLabel));
    }

    private void UpdatePlaybackTime()
    {
        var current = TimeSpan.FromSeconds(PositionSeconds);
        var total = TimeSpan.FromSeconds(DurationSeconds);
        PlaybackTime = $"{current:mm\\:ss} / {total:mm\\:ss}";
    }

    private void RefreshCommands()
    {
        LoadFileACommand.RaiseCanExecuteChanged();
        LoadFileBCommand.RaiseCanExecuteChanged();

        StartCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();

        ListenACommand.RaiseCanExecuteChanged();
        ListenBCommand.RaiseCanExecuteChanged();
        RevealBlindMappingCommand.RaiseCanExecuteChanged();
        ResetBlindRoundCommand.RaiseCanExecuteChanged();

        TogglePlayPauseCommand.RaiseCanExecuteChanged();
        ToggleSourceCommand.RaiseCanExecuteChanged();
        SeekBackwardCommand.RaiseCanExecuteChanged();
        SeekForwardCommand.RaiseCanExecuteChanged();
    }

    private void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(ShowBlindChoiceQuestion));
        OnPropertyChanged(nameof(ShowMainWorkflow));
        OnPropertyChanged(nameof(IsBlindTestSelected));
        OnPropertyChanged(nameof(IsStandardTestSelected));
        OnPropertyChanged(nameof(LoadFileAButtonText));
        OnPropertyChanged(nameof(LoadFileBButtonText));
        OnPropertyChanged(nameof(ComparisonPanelTitle));
        NotifyBlindStateChanged();
    }

    private void NotifyBlindStateChanged()
    {
        OnPropertyChanged(nameof(FileANameDisplay));
        OnPropertyChanged(nameof(FileBNameDisplay));
        OnPropertyChanged(nameof(LoadFileAButtonText));
        OnPropertyChanged(nameof(LoadFileBButtonText));
        OnPropertyChanged(nameof(BlindStatusText));
        OnPropertyChanged(nameof(ShowRevealButton));
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
