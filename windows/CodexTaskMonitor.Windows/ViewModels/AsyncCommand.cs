using System.Windows.Input;

namespace CodexTaskMonitor.Windows.ViewModels;

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null, Action? onError = null) : ICommand
{
    private bool running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(); }
        catch (OperationCanceledException) { }
        catch
        {
            try { onError?.Invoke(); }
            catch { }
        }
        finally
        {
            running = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
