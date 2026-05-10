using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoFlow.App.Models;

public sealed class ScriptDefinition : INotifyPropertyChanged
{
    private bool _isRunning;

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning == value)
            {
                return;
            }

            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => IsRunning ? "运行中" : "空闲";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
