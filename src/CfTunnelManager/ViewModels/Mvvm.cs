using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace CfTunnelManager.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            OnChanged(name);
        }
    }

    protected void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        _running = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await execute();
        }
        finally
        {
            _running = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
