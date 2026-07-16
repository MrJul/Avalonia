// using System;
// using System.Collections.Concurrent;
// using System.Runtime.ExceptionServices;
// using System.Threading;
//
// namespace Avalonia.Layout.Pipeline;
//
/// <summary>
/// Packs a layout work item — a contiguous range of node indices into the snapshot the current
/// stage runs against — into a single 64-bit value, so the work queue stays allocation-free.
/// Ranges let the pipeline batch several small sibling subtrees into one item: it keeps the
/// per-item overhead small compared to the work an item carries, and one worker handling a
/// contiguous run avoids false sharing on the node-indexed output arrays.
/// </summary>
internal static class LayoutWorkItem
{
    public static long Pack(int firstNode, int count) => ((long)count << 32) | (uint)firstNode;

    public static int GetFirstNode(long item) => (int)item;

    public static int GetCount(long item) => (int)(item >> 32);
}

/// <summary>
/// Processes the layout work items.
/// </summary>
internal interface ILayoutWorkProcessor
{
    void Process(long item);
}
//
// /// <summary>
// /// A small pool of dedicated worker threads executing layout work items pushed by the
// /// <see cref="LayoutPipeline"/> measure and arrange stages. The threads are created lazily on
// /// first use, are shared by the whole process, and park between stages, so an idle application
// /// costs nothing beyond the parked threads.
// /// </summary>
// /// <remarks>
// /// Workers are woken once per stage and spin-poll the queue while the stage runs, so scheduling
// /// an item costs an interlocked increment and a queue push — never a kernel transition. The
// /// calling (UI) thread participates in the work while the stage runs: it would otherwise be an
// /// idle core, since it blocks on the result anyway. A stage completes when every scheduled item
// /// has been processed; tree completion is driven by the dependency counters in the snapshot
// /// rather than blocking joins, so a fixed number of workers cannot deadlock. The first
// /// exception thrown by an item is latched, the remaining items are skipped while the queue
// /// drains, and the exception is rethrown on the calling thread.
// /// </remarks>
// internal sealed class LayoutWorkerPool
// {
//     public static LayoutWorkerPool Instance { get; } = new();
//
//     private readonly object _executionLock = new();
//     private readonly ConcurrentQueue<long> _queue = new();
//     private readonly ManualResetEventSlim _stageRunning = new(false);
//     private ILayoutWorkProcessor? _processor;
//     private volatile bool _running;
//     private int _activeItems;
//     private Exception? _exception;
//     private bool _started;
//
//     public int WorkerCount { get; } = 2; //Math.Max(1, Environment.ProcessorCount - 1);
//
//     /// <summary>
//     /// Runs a stage: enqueues the initial item, participates in the processing until every
//     /// item — including the ones scheduled by other items — has completed, then rethrows the
//     /// first exception encountered, if any. Concurrent stages (e.g. two UI threads) serialize.
//     /// </summary>
//     public void Execute(ILayoutWorkProcessor processor, long initialItem)
//     {
//         lock (_executionLock)
//         {
//             EnsureStarted();
//
//             _processor = processor;
//             _exception = null;
//
//             Enqueue(initialItem);
//
//             // One kernel wake-up per stage: the workers then stay hot, polling the queue.
//             _running = true;
//             _stageRunning.Set();
//
//             // Participate until the stage drains. SpinOnce is prevented from degrading to
//             // Sleep(1), which can stall for a whole timer quantum.
//             var spinWait = new SpinWait();
//
//             while (Volatile.Read(ref _activeItems) > 0)
//             {
//                 if (_queue.TryDequeue(out var item))
//                 {
//                     RunItem(item);
//                     spinWait.Reset();
//                 }
//                 else
//                 {
//                     spinWait.SpinOnce(sleep1Threshold: -1);
//                 }
//             }
//
//             _running = false;
//             _stageRunning.Reset();
//             _processor = null;
//
//             if (_exception is { } exception)
//                 ExceptionDispatchInfo.Capture(exception).Throw();
//         }
//     }
//
//     /// <summary>
//     /// Schedules a work item on the pool. Only valid while a stage is executing (from the
//     /// item processing it originates from).
//     /// </summary>
//     public void Enqueue(long item)
//     {
//         Interlocked.Increment(ref _activeItems);
//         _queue.Enqueue(item);
//     }
//
//     private void RunItem(long item)
//     {
//         try
//         {
//             if (Volatile.Read(ref _exception) is null)
//                 _processor!.Process(item);
//         }
//         catch (Exception exception)
//         {
//             Interlocked.CompareExchange(ref _exception, exception, null);
//         }
//         finally
//         {
//             Interlocked.Decrement(ref _activeItems);
//         }
//     }
//
//     private void EnsureStarted()
//     {
//         if (_started)
//             return;
//
//         _started = true;
//
//         for (var i = 0; i < WorkerCount; i++)
//         {
//             new Thread(WorkerLoop)
//             {
//                 Name = $"Avalonia Layout Worker #{i + 1}",
//                 IsBackground = true,
//             }.Start();
//         }
//     }
//
//     private void WorkerLoop()
//     {
//         var spinWait = new SpinWait();
//
//         while (true)
//         {
//             _stageRunning.Wait();
//             spinWait.Reset();
//
//             while (_running)
//             {
//                 if (_queue.TryDequeue(out var item))
//                 {
//                     RunItem(item);
//                     spinWait.Reset();
//                 }
//                 else
//                 {
//                     spinWait.SpinOnce(sleep1Threshold: -1);
//                 }
//             }
//         }
//     }
// }
