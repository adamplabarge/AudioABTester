using Microsoft.Win32;

namespace AudioABTester.Services;

public interface IFileDialogService
{
    string? PickAudioFile();
}

public sealed class FileDialogService : IFileDialogService
{
    public string? PickAudioFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select an audio file",
            Filter = "Audio Files|*.wav;*.mp3;*.flac;*.aiff;*.aif",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}