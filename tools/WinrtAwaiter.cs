using System;
using System.Threading.Tasks;
using System.Threading;
using Windows.Foundation;

// Await WinRT operations with a timeout; references the OS metadata directly.
// This allows the read-only probe to build without installing a development SDK.
public static class WinrtAwaiter
{
    public static async Task<T> ToTask<T>(this IAsyncOperation<T> operation, CancellationToken token, int timeout)
    {
        try
        {
            DateTime deadline = timeout > 0 ? DateTime.UtcNow.AddMilliseconds(timeout) : DateTime.MaxValue;
            while (operation.Status == AsyncStatus.Started)
            {
                if (token.IsCancellationRequested || DateTime.UtcNow >= deadline)
                {
                    operation.Cancel();
                    token.ThrowIfCancellationRequested();
                    throw new TimeoutException("Bluetooth operation timed out.");
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            if (operation.Status == AsyncStatus.Error) throw operation.ErrorCode;
            if (operation.Status == AsyncStatus.Canceled) throw new OperationCanceledException();
            return operation.GetResults();
        }
        finally { operation.Close(); }
    }
    public static async Task<T> ToTask<T>(this IAsyncOperation<T> operation)
    {
        try
        {
            await Wait(operation).ConfigureAwait(false);
            return operation.GetResults();
        }
        finally { operation.Close(); }
    }

    public static async Task ToTask(this IAsyncAction operation)
    {
        try
        {
            await Wait(operation).ConfigureAwait(false);
            operation.GetResults();
        }
        finally { operation.Close(); }
    }

    private static async Task Wait(IAsyncInfo operation)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (operation.Status == AsyncStatus.Started)
        {
            if (DateTime.UtcNow >= deadline)
            {
                operation.Cancel();
                throw new TimeoutException("WinRT Bluetooth operation exceeded 10 seconds.");
            }
            await Task.Delay(20).ConfigureAwait(false);
        }
        if (operation.Status == AsyncStatus.Error) throw operation.ErrorCode;
        if (operation.Status == AsyncStatus.Canceled) throw new OperationCanceledException();
    }
}
