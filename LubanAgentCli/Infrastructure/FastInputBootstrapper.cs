#if !PUBLISH_AOT
using System.Reflection;
using System.Threading;

namespace LubanAgentCli.Infrastructure;

/// <summary>
/// 通过反射替换 Terminal.Gui 默认输入线程（20ms 轮询）为更高频的轮询线程，
/// 降低输入反显延迟。仅在 dotnet 驱动（NetInput）下生效。
/// </summary>
internal sealed class FastInputBootstrapper : IDisposable
{
    private const int DefaultPollIntervalMs = 2;

    private CancellationTokenSource? _cts;
    private Thread? _thread;
    private bool _enabled;

    public bool IsEnabled => _enabled;

    public bool TryEnable(IApplication application, int pollIntervalMs = DefaultPollIntervalMs)
    {
        if (_enabled)
        {
            return true;
        }

        try
        {
            var (coordinator, coordType) = GetCoordinator(application);
            if (coordinator == null || coordType == null)
            {
                return false;
            }

            var inputQueue = GetInputQueue(coordType, coordinator);
            if (inputQueue == null)
            {
                return false;
            }

            var inputField = coordType.GetField("_input", BindingFlags.NonPublic | BindingFlags.Instance);
            if (inputField == null)
            {
                return false;
            }

            var oldInput = inputField.GetValue(coordinator);
            if (oldInput == null)
            {
                return false;
            }

            var inputType = oldInput.GetType();
            if (inputType.Name != "NetInput")
            {
                return false;
            }

            var extCtsProp = inputType.GetProperty("ExternalCancellationTokenSource",
                BindingFlags.Public | BindingFlags.Instance);
            if (extCtsProp == null)
            {
                return false;
            }

            var inputTaskField = coordType.GetField("_inputTask", BindingFlags.NonPublic | BindingFlags.Instance);
            if (inputTaskField == null)
            {
                return false;
            }

            var extCts = new CancellationTokenSource();
            extCtsProp.SetValue(oldInput, extCts);
            extCts.Cancel();

            var oldTask = (Task?)inputTaskField.GetValue(coordinator);
            if (oldTask != null)
            {
                try
                {
                    oldTask.Wait(3000);
                }
                catch (AggregateException)
                {
                }
            }

            var newInput = Activator.CreateInstance(inputType);
            if (newInput == null)
            {
                return false;
            }

            var initMethod = inputType.GetMethod("Initialize",
                BindingFlags.Public | BindingFlags.Instance);
            if (initMethod == null)
            {
                return false;
            }

            initMethod.Invoke(newInput, new[] { inputQueue });
            inputField.SetValue(coordinator, newInput);

            UpdateInputProcessorInputImpl(coordType, coordinator, newInput);

            var peekMethod = inputType.GetMethod("Peek",
                BindingFlags.Public | BindingFlags.Instance);
            var readMethod = inputType.GetMethod("Read",
                BindingFlags.Public | BindingFlags.Instance);
            var enqueueMethod = inputQueue.GetType().GetMethod("Enqueue",
                BindingFlags.Public | BindingFlags.Instance);

            if (peekMethod == null || readMethod == null || enqueueMethod == null)
            {
                return false;
            }

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            _thread = new Thread(() => RunFastPollLoop(newInput, inputQueue, peekMethod, readMethod, enqueueMethod, pollIntervalMs, ct))
            {
                IsBackground = true,
                Name = "FastInputPoller",
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();

            _enabled = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            _cts?.Cancel();
            _thread?.Join(2000);
        }
        catch
        {
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _thread = null;
            _enabled = false;
        }
    }

    private static (object? Coordinator, Type? Type) GetCoordinator(IApplication application)
    {
        var appType = application.GetType();
        var coordProp = appType.GetProperty("Coordinator",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (coordProp == null)
        {
            return (null, null);
        }

        var coordinator = coordProp.GetValue(application);
        return (coordinator, coordinator?.GetType());
    }

    private static object? GetInputQueue(Type coordType, object coordinator)
    {
        var prop = coordType.GetProperty("InputQueue",
            BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(coordinator);
    }

    private static void UpdateInputProcessorInputImpl(Type coordType, object coordinator, object newInput)
    {
        try
        {
            var procProp = coordType.GetProperty("InputProcessor",
                BindingFlags.Public | BindingFlags.Instance);
            if (procProp == null)
            {
                return;
            }

            var processor = procProp.GetValue(coordinator);
            if (processor == null)
            {
                return;
            }

            var implProp = processor.GetType().GetProperty("InputImpl",
                BindingFlags.Public | BindingFlags.Instance);
            implProp?.SetValue(processor, newInput);
        }
        catch
        {
        }
    }

    private static void RunFastPollLoop(
        object input,
        object inputQueue,
        MethodInfo peekMethod,
        MethodInfo readMethod,
        MethodInfo enqueueMethod,
        int pollIntervalMs,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (peekMethod.Invoke(input, null) is true)
                {
                    if (readMethod.Invoke(input, null) is System.Collections.IEnumerable records)
                    {
                        foreach (var rec in records)
                        {
                            enqueueMethod.Invoke(inputQueue, new[] { rec });
                        }
                    }

                    continue;
                }
            }
            catch
            {
            }

            try
            {
                Thread.Sleep(pollIntervalMs);
            }
            catch (ThreadInterruptedException)
            {
            }
        }
    }
}
#endif
