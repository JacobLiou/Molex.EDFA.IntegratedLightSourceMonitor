using System.Windows;
using Serilog;

namespace LightSourceMonitor.Helpers;

public static class AsyncHelper
{
    /// <summary>
    /// Safely runs an async Task in fire-and-forget mode, logging any exceptions via Serilog.
    /// Use this instead of <c>_ = SomeAsync()</c> to prevent silent exception swallowing.
    /// </summary>
    public static async void SafeFireAndForget(this Task task, string context = "")
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unhandled exception in fire-and-forget task [{Context}]", context);
        }
    }

    /// <summary>
    /// Safely invokes an action on the WPF Dispatcher, swallowing exceptions during shutdown.
    /// </summary>
    public static void SafeDispatcherInvoke(Action action)
    {
        try
        {
            var app = Application.Current;
            if (app == null) return;

            var dispatcher = app.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            dispatcher.Invoke(action);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception in Dispatcher.Invoke");
        }
    }

    /// <summary>
    /// Safely invokes an event, catching subscriber exceptions.
    /// </summary>
    public static void SafeInvoke<T>(this Action<T>? handler, T arg, string eventName = "")
    {
        if (handler == null) return;

        foreach (var d in handler.GetInvocationList())
        {
            try
            {
                ((Action<T>)d)(arg);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Subscriber exception in event [{Event}]", eventName);
            }
        }
    }
}
