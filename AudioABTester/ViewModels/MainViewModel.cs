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

    private bool _isInternalPositionUpdate;
    private string _fileAName = "No file selected";
    private string _fileBName = "No file selected";
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

        LoadFileACommand = new RelayCommand(LoadFileA);
        LoadFileBCommand = new RelayCommand(LoadFileB);
        StartCommand = new RelayCommand(StartPlayback, () => _audioEngine.CanStart);
        PauseCommand = new RelayCommand(PausePlayback, () => _audioEngine.CanStart);
        StopCommand = new RelayCommand(StopPlayback, () => _audioEngine.CanStart);
        ListenACommand = new RelayCommand(() => ListenTo(PlaybackSource.A), () => _audioEngine.CanStart);
        ListenBCommand = new RelayCommand(() => ListenTo(PlaybackSource.B), () => _audioEngine.CanStart);
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

        RefreshTransport();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RelayCommand LoadFileACommand { get; }

    public RelayCommand LoadFileBCommand { get; }

    public RelayCommand StartCommand { get; }

    public RelayCommand PauseCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand ListenACommand { get; }

    public RelayCommand ListenBCommand { get; }

    public RelayCommand TogglePlayPauseCommand { get; }

    public RelayCommand ToggleSourceCommand { get; }

    public RelayCommand SeekBackwardCommand { get; }

    public RelayCommand SeekForwardCommand { get; }

    public string FileAName
    {
        get => _fileAName;
        private set => SetProperty(ref _fileAName, value);
    }

    public string FileBName
    {
        get => _fileBName;
        private set => SetProperty(ref _fileBName, value);
    }

    public string CurrentSourceText => $"Current Source: {_audioEngine.CurrentSource}";

    public bool IsListeningToA => _audioEngine.CurrentSource == PlaybackSource.A;

    public bool IsListeningToB => _audioEngine.CurrentSource == PlaybackSource.B;

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

    private void LoadFileA()
    {
        var path = _fileDialogService.PickAudioFile();
        if (path is null)
        {
            return;
        }

        _audioEngine.Stop();
        var track = _audioEngine.LoadTrackA(path);
        FileAName = track.DisplayName;
        RefreshTransport();
        RefreshCommands();
    }

    private void LoadFileB()
    {
        var path = _fileDialogService.PickAudioFile();
        if (path is null)
        {
            return;
        }

        _audioEngine.Stop();
        var track = _audioEngine.LoadTrackB(path);
        FileBName = track.DisplayName;
        RefreshTransport();
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
        StartCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        ListenACommand.RaiseCanExecuteChanged();
        ListenBCommand.RaiseCanExecuteChanged();
        TogglePlayPauseCommand.RaiseCanExecuteChanged();
        ToggleSourceCommand.RaiseCanExecuteChanged();
        SeekBackwardCommand.RaiseCanExecuteChanged();
        SeekForwardCommand.RaiseCanExecuteChanged();
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