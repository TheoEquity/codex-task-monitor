using System.Security.Principal;

namespace CodexTaskMonitor.Windows.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activationSignal;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task listener;
    private readonly TaskCompletionSource<bool> ownership = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object activationSync = new();
    private EventHandler? activationRequested;
    private bool activationPending;
    private int disposed;

    public bool IsOwner { get; }

    public event EventHandler? ActivationRequested
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);
            var dispatchPending = false;
            lock (activationSync)
            {
                activationRequested += value;
                if (activationPending)
                {
                    activationPending = false;
                    dispatchPending = true;
                }
            }

            if (dispatchPending)
                QueueActivation(value);
        }
        remove
        {
            lock (activationSync)
            {
                activationRequested -= value;
            }
        }
    }

    private SingleInstanceService(Mutex mutex, EventWaitHandle activationSignal)
    {
        this.mutex = mutex;
        this.activationSignal = activationSignal;
        listener = Task.Run(Listen);
        IsOwner = ownership.Task.GetAwaiter().GetResult();
    }

    public static SingleInstanceService TryAcquire(string name)
    {
        var objectName = BuildObjectName(name, CurrentUserSid());
        var mutex = new Mutex(initiallyOwned: false, objectName);
        EventWaitHandle? activationSignal = null;
        try
        {
            activationSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $"{objectName}.Activate");
            return new SingleInstanceService(mutex, activationSignal);
        }
        catch
        {
            activationSignal?.Dispose();
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        lifetime.Cancel();
        activationSignal.Set();

        try
        {
            listener.GetAwaiter().GetResult();
        }
        finally
        {
            try
            {
                activationSignal.Dispose();
            }
            finally
            {
                try
                {
                    mutex.Dispose();
                }
                finally
                {
                    lifetime.Dispose();
                }
            }
        }
    }

    private void Listen()
    {
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            ownership.TrySetResult(ownsMutex);
            if (!ownsMutex)
            {
                activationSignal.Set();
                return;
            }

            var handles = new WaitHandle[] { activationSignal, lifetime.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                if (lifetime.IsCancellationRequested)
                    return;

                OnActivationSignal();
            }
        }
        catch (Exception error)
        {
            ownership.TrySetException(error);
            throw;
        }
        finally
        {
            if (ownsMutex)
                mutex.ReleaseMutex();
        }
    }

    internal static string BuildObjectName(string name, string userSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(userSid);
        return $"Global\\{name}.{userSid}";
    }

    private static string CurrentUserSid()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrWhiteSpace(sid))
            throw new InvalidOperationException("The current Windows user does not have a security identifier.");

        return sid;
    }

    private void OnActivationSignal()
    {
        EventHandler? handlers;
        lock (activationSync)
        {
            handlers = activationRequested;
            if (handlers is null)
            {
                activationPending = true;
                return;
            }
        }

        QueueActivation(handlers);
    }

    private void QueueActivation(EventHandler handlers)
    {
        _ = Task.Run(() => NotifyActivation(handlers));
    }

    private void NotifyActivation(EventHandler handlers)
    {
        try
        {
            handlers(this, EventArgs.Empty);
        }
        catch
        {
            // A consumer callback must not terminate the cross-process activation listener.
        }
    }
}
