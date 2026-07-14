using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Avalonia.Layout.Pipeline;

/// <summary>
/// Processes the layout work items scheduled on the <see cref="LayoutWorkerPool"/>.
/// An item is a node index into the snapshot the current stage runs against.
/// </summary>
internal interface ILayoutWorkProcessor
{
    void Process(int item);
}

/// <summary>
/// A small pool of dedicated worker threads executing layout work items — node indices into
/// the current <see cref="LayoutTreeSnapshot"/> — pushed by the <see cref="LayoutPipeline"/>
/// measure and arrange stages. The threads are created lazily on first use, are shared by the
/// whole process, and park on a semaphore between stages, so an idle application costs
/// nothing beyond the parked threads.
/// </summary>
/// <remarks>
/// The calling (UI) thread participates in the work while a stage runs — it would otherwise
/// be an idle core, since it blocks on the result anyway. A stage completes when every
/// scheduled item has been processed: tree completion is driven by the dependency counters in
/// the snapshot rather than blocking joins, so a fixed number of workers cannot deadlock.
/// The first exception thrown by an item is latched, the remaining items are skipped while
/// the queue drains, and the exception is rethrown on the calling thread.
/// </remarks>
internal sealed class LayoutWorkerPool
{
    public static LayoutWorkerPool Instance { get; } = new();

    private readonly object _executionLock = new();
    private readonly ConcurrentQueue<int> _queue = new();
    private readonly SemaphoreSlim _workAvailable = new(0);
    private ILayoutWorkProcessor? _processor;
    private int _activeItems;
    private Exception? _exception;
    private bool _started;

    public int WorkerCount { get; } = Math.Max(1, Environment.ProcessorCount - 1);

    /// <summary>
    /// Runs a stage: enqueues the initial item, participates in the processing until every
    /// item — including the ones scheduled by other items — has completed, then rethrows the
    /// first exception encountered, if any. Concurrent stages (e.g. two UI threads) serialize.
    /// </summary>
    public void Execute(ILayoutWorkProcessor processor, int initialItem)
    {
        lock (_executionLock)
        {
            EnsureStarted();

            _processor = processor;
            _exception = null;

            Enqueue(initialItem);

            var spinWait = new SpinWait();

            while (Volatile.Read(ref _activeItems) > 0)
            {
                if (_queue.TryDequeue(out var item))
                {
                    RunItem(item);
                    spinWait.Reset();
                }
                else
                {
                    spinWait.SpinOnce();
                }
            }

            _processor = null;

            if (_exception is { } exception)
                ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    /// <summary>
    /// Schedules a work item on the pool. Only valid while a stage is executing (from the
    /// item processing it originates from).
    /// </summary>
    public void Enqueue(int item)
    {
        Interlocked.Increment(ref _activeItems);
        _queue.Enqueue(item);
        _workAvailable.Release();
    }

    private void RunItem(int item)
    {
        try
        {
            if (Volatile.Read(ref _exception) is null)
                _processor!.Process(item);
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(ref _exception, exception, null);
        }
        finally
        {
            Interlocked.Decrement(ref _activeItems);
        }
    }

    private void EnsureStarted()
    {
        if (_started)
            return;

        _started = true;

        for (var i = 0; i < WorkerCount; i++)
        {
            new Thread(WorkerLoop)
            {
                Name = $"Avalonia Layout Worker #{i}",
                IsBackground = true,
            }.Start();
        }
    }

    private void WorkerLoop()
    {
        while (true)
        {
            _workAvailable.Wait();

            if (_queue.TryDequeue(out var item))
                RunItem(item);
        }
    }
}
