using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Avalonia.Layout.Pipeline;

/// <summary>
/// A self-scaling worker queue for short, latency-sensitive work items.
///
/// A worker drains the queue in a tight loop with <em>no synchronisation per item</em>: once
/// awake it keeps pulling items until the queue is empty, so a burst is consumed as a single
/// batch rather than paying a semaphore wait per item. When the queue empties it briefly spins
/// (to catch items that land microseconds later) before parking on the wake signal, and only
/// then does it truly sleep.
///
/// The queue favours running items on a single worker thread for as long as that thread can keep
/// up, which avoids the cache/context-switch overhead of spreading tiny items across cores. Only
/// when the backlog grows past the number of active threads does it spin up another worker, one
/// at a time, up to <see cref="MaxThreads"/>. Workers that sit idle longer than the idle timeout
/// retire themselves, shrinking the pool back down (to an optional warm minimum).
/// </summary>
internal sealed class ElasticWorkerQueue : IDisposable
{
    private readonly ConcurrentQueue<long> _items = new();

    /// <summary>
    /// Wake signal for <em>parked</em> workers only. It is not released per item — a running
    /// worker drains the queue directly — so it does not track the item count.
    /// </summary>
    private readonly SemaphoreSlim _signal = new(0);

    /// <summary>Guards <see cref="_threadCount"/> and the grow/retire decisions.</summary>
    private readonly object _gate = new();

    private readonly int _maxThreads;
    private readonly int _minThreads;
    private readonly TimeSpan _idleTimeout;

    /// <summary>Number of live worker threads. Mutated only under <see cref="_gate"/>.</summary>
    private int _threadCount;

    /// <summary>
    /// Workers in their idle phase (spinning or parked) — i.e. spare capacity that will grab new
    /// work without a new thread. Mutated via <see cref="Interlocked"/>.
    /// </summary>
    private int _idleWorkers;

    /// <summary>Workers actually parked on <see cref="_signal"/> and needing a release to wake.</summary>
    private int _blockedWorkers;

    /// <summary>Items enqueued but not yet finished executing. Mutated via <see cref="Interlocked"/>.</summary>
    private int _outstanding;


    // Instrumentation (best-effort counters for observability / the demo).
    private long _processed;
    private long _parks;
    private long _maxDrainBatch;

    private volatile bool _disposed;

    /// <summary>
    /// Raised when a work item throws. If no handler is attached the exception is swallowed
    /// so that one bad item cannot tear down a worker thread. The handler runs on the worker thread.
    /// </summary>
    public event Action<Exception>? WorkItemFailed;

    /// <param name="maxThreads">
    /// Upper bound on concurrent worker threads. Defaults to <see cref="Environment.ProcessorCount"/>.
    /// </param>
    /// <param name="minThreads">
    /// Minimum number of warm worker threads to keep alive once started. Defaults to 0
    /// (the pool shrinks all the way to zero when idle). A value of 1 keeps one thread hot
    /// so the first item after a quiet period skips thread-start latency.
    /// </param>
    /// <param name="idleTimeout">
    /// How long a worker waits with no work before retiring. Use
    /// <see cref="Timeout.InfiniteTimeSpan"/> to disable down-scaling. Defaults to 10 seconds.
    /// </param>
    public ElasticWorkerQueue(
        int maxThreads = 0,
        int minThreads = 0,
        TimeSpan? idleTimeout = null)
    {
        _maxThreads = maxThreads > 0 ? maxThreads : Environment.ProcessorCount;

        if (minThreads < 0 || minThreads > _maxThreads)
            throw new ArgumentOutOfRangeException(nameof(minThreads),
                $"minThreads must be between 0 and maxThreads ({_maxThreads}).");
        _minThreads = minThreads;

        var timeout = idleTimeout ?? TimeSpan.FromSeconds(10);
        if (timeout != Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleTimeout),
                "idleTimeout must be positive or Timeout.InfiniteTimeSpan.");
        _idleTimeout = timeout;

        for (var i = 0; i < maxThreads; ++i)
            StartWorker();
    }

    /// <summary>Maximum number of worker threads this queue may run.</summary>
    public int MaxThreads => _maxThreads;

    /// <summary>Current number of live worker threads (approximate; changes concurrently).</summary>
    public int WorkerCount => Volatile.Read(ref _threadCount);

    /// <summary>Number of items queued but not yet picked up by a worker (approximate).</summary>
    public int PendingCount => _items.Count;

    /// <summary>Total work items executed since construction.</summary>
    public long ProcessedCount => Interlocked.Read(ref _processed);

    /// <summary>
    /// Number of times a worker had to park on the wake signal. Every item beyond these is one that
    /// a worker pulled straight off the queue with no wait — so a small number here relative to
    /// <see cref="ProcessedCount"/> means most items were consumed without waiting.
    /// </summary>
    public long ParkCount => Interlocked.Read(ref _parks);

    /// <summary>Largest number of items a single worker drained back-to-back without waiting.</summary>
    public long MaxDrainBatch => Interlocked.Read(ref _maxDrainBatch);

    /// <summary>Items enqueued but not yet finished executing (queued or in flight).</summary>
    public int OutstandingCount => Volatile.Read(ref _outstanding);

    public ILayoutWorkProcessor? Processor { get; set; }

    /// <summary>Queues a work item for execution.</summary>
    public void Enqueue(long item)
    {
        //ObjectDisposedException.ThrowIf(_disposed, this);

        PublishItem(item);

        // Guard against the item slipping in just as we were disposed on another thread.
        //ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>Places an item on the queue and either wakes a parked worker or grows the pool.</summary>
    private void PublishItem(long item)
    {
        Interlocked.Increment(ref _outstanding); // matched by the completion accounting on drain
        _items.Enqueue(item);

        // Make the enqueued item visible before we read the worker counters, so we never conclude
        // "nobody to wake / no backlog" while a worker is in the middle of parking (a StoreLoad fence).
        Interlocked.MemoryBarrier();

        if (Volatile.Read(ref _blockedWorkers) > 0)
            _signal.Release();   // a worker is asleep — wake exactly one to drain the queue
        else
            MaybeGrow(); // running workers will pull it off directly; add capacity only on real backlog
    }

    /// <summary>
    /// Enqueues a single work item and blocks the calling thread until that item — and any further
    /// work it enqueues, transitively — has finished. Rather than parking, the caller joins in as a
    /// temporary worker, draining and running queued items exactly as a pool thread would, which
    /// both speeds up completion and lets a caller flush work synchronously without tying up a pool
    /// thread.
    /// </summary>
    /// <remarks>
    /// This assumes the queue is driven solely by this call: the item (and its descendants) may add
    /// more work, but no other producer enqueues concurrently. Completion is then simply the point
    /// at which no work remains outstanding — nothing queued and nothing still executing.
    /// </remarks>
    public void EnqueueAndWait(long work)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PublishItem(work);

        if (_disposed)
            throw new ObjectDisposedException(nameof(ElasticWorkerQueue));

        // Participate: run items as if we were a worker until no work remains outstanding.
        var spinner = new SpinWait();
        while (Volatile.Read(ref _outstanding) > 0)
        {
            if (_items.TryDequeue(out var item))
            {
                // Accounted per item rather than batched: this loop's exit condition is the counter
                // reaching zero, so it has to stay current. Trees here are small, so contention is not
                // a concern.
                try { Execute(item); }
                finally { CompleteItems(1); }
                spinner.Reset(); // made progress — reset back-off for the next empty stretch
            }
            else
            {
                // Queue is drained but items are still running (and may enqueue more); back off
                // without hogging the core (SpinWait escalates to yielding/sleeping on its own).
                spinner.SpinOnce(sleep1Threshold: -1);
            }
        }
    }

    /// <summary>
    /// Adds a worker only if the current threads genuinely can't service the backlog:
    /// no worker is idle, and there are more pending items than live threads. This keeps a
    /// single thread that is keeping up from ever being disturbed.
    /// </summary>
    /// <remarks>
    /// This runs on every enqueue, so each check must be O(1) and the common cases must not touch
    /// the lock — under sustained load we are at max threads with a backlog, and taking
    /// <see cref="_gate"/> there would serialise every producer against the workers' retire path.
    /// </remarks>
    private void MaybeGrow()
    {
        // Already at capacity: nothing to decide. Checked first because it is the steady state
        // under load, and it keeps us off the lock entirely.
        if (Volatile.Read(ref _threadCount) >= _maxThreads)
            return;

        // An idle worker (spinning or parked) will grab this item — no new thread needed.
        if (Volatile.Read(ref _idleWorkers) > 0)
            return;

        // Backlog still fits the current threads (one item of buffer per thread) — let them drain it.
        // Count is only reached while we are below max, where the queue is short and this is cheap.
        if (_items.Count <= Volatile.Read(ref _threadCount))
            return;

        lock (_gate)
        {
            if (_disposed) return;
            if (Volatile.Read(ref _idleWorkers) > 0) return;
            if (_threadCount >= _maxThreads) return;
            if (_items.Count <= _threadCount) return;

            _threadCount++;
            StartWorker();
        }
    }

    private void StartWorker()
    {
        var thread = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = $"{nameof(ElasticWorkerQueue)}-{Guid.NewGuid():N}"[..24],
        };
        thread.Start();
    }

    private void WorkerLoop()
    {
        var retired = false;
        try
        {
            while (!_disposed)
            {
                // Hot path: drain everything currently queued with zero synchronisation per item.
                if (!_items.IsEmpty)
                {
                    DrainAvailable();
                    continue; // re-check for more before we consider going idle
                }

                // Idle phase: nothing to do right now. Count as spare capacity so a burst wakes/uses
                // us instead of spawning another thread.
                Interlocked.Increment(ref _idleWorkers);
                bool gotWork;
                try
                {
                    gotWork = SpinForWork() || WaitForWork();
                }
                finally
                {
                    Interlocked.Decrement(ref _idleWorkers);
                }

                // Nothing arrived within the idle timeout: retire if we're truly not needed.
                if (!gotWork && !_disposed && TryRetire())
                {
                    retired = true;
                    return;
                }
            }
        }
        finally
        {
            // Any exit that wasn't a clean retire (dispose, unexpected throw) still frees its slot.
            if (!retired)
                lock (_gate) { _threadCount--; }
        }
    }

    /// <summary>Pulls and runs items until the queue is empty, recording the batch size.</summary>
    private void DrainAvailable()
    {
        var batch = 0;
        try
        {
            while (_items.TryDequeue(out var work))
            {
                Execute(work);
                batch++;
                if (_disposed) break;
            }
        }
        finally
        {
            CompleteItems(batch);
            RecordBatch(batch);
        }
    }

    /// <summary>
    /// Accounts for <paramref name="count"/> finished items in one shot.
    /// </summary>
    /// <remarks>
    /// Deliberately batched. These counters are the hottest shared cache lines in the system — the
    /// producer writes them on every enqueue and every worker writes them on every completion — so
    /// updating them per item makes each atomic contend across all cores and throttles the producer
    /// badly (measured: 20ns -> 343ns per enqueue at 24 workers). One update per drain instead of
    /// one per item keeps the lines effectively private to the producer.
    /// </remarks>
    private void CompleteItems(int count)
    {
        if (count <= 0)
            return;

        Interlocked.Add(ref _outstanding, -count); // matched by the increment in PublishItem
        Interlocked.Add(ref _processed, count);
    }

    /// <summary>
    /// Briefly spins (pure CPU, no OS yield) hoping more work lands almost immediately, so a steady
    /// stream is absorbed without ever parking. Returns true as soon as an item appears.
    /// </summary>
    private bool SpinForWork()
    {
        var spinner = new SpinWait();
        while (!spinner.NextSpinWillYield)
        {
            if (_disposed) return false;
            if (!_items.IsEmpty) return true;
            spinner.SpinOnce(sleep1Threshold: -1);
        }
        return !_disposed && !_items.IsEmpty;
    }

    /// <summary>
    /// Parks on the wake signal until an item arrives (a producer releases the signal) or the idle
    /// timeout elapses. Registers as a blocked worker first, then re-checks the queue, closing the
    /// lost-wakeup race with <see cref="Enqueue"/>.
    /// </summary>
    private bool WaitForWork()
    {
        Interlocked.Increment(ref _blockedWorkers);
        try
        {
            // An item enqueued between the spin ending and us registering as blocked would set no
            // signal (we weren't counted yet); this re-check catches it.
            if (!_items.IsEmpty)
                return true;

            Interlocked.Increment(ref _parks);
            return _signal.Wait(_idleTimeout);
        }
        finally
        {
            Interlocked.Decrement(ref _blockedWorkers);
        }
    }

    /// <summary>
    /// Decides, under the lock, whether an idle worker may leave. Returning true decrements the
    /// live thread count in the same critical section that <see cref="MaybeGrow"/> reads, so an
    /// item enqueued around the moment of retirement is either seen here (we stay) or triggers a
    /// fresh worker there (we leave) — it can never be stranded.
    /// </summary>
    private bool TryRetire()
    {
        lock (_gate)
        {
            if (!_disposed && _threadCount <= _minThreads)
                return false; // keep the warm minimum alive
            if (!_items.IsEmpty)
                return false; // work showed up just now — keep going
            _threadCount--;
            return true;
        }
    }

    private void Execute(long item)
    {
        try
        {
            Processor!.Process(item);
        }
        catch (Exception ex)
        {
            WorkItemFailed?.Invoke(ex);
        }
    }

    private void RecordBatch(long batch)
    {
        long current;
        while (batch > (current = Interlocked.Read(ref _maxDrainBatch)))
        {
            if (Interlocked.CompareExchange(ref _maxDrainBatch, batch, current) == current)
                return;
        }
    }

    /// <summary>
    /// Stops accepting work and wakes every worker so they observe the shutdown and exit.
    /// In-flight and already-queued items are not guaranteed to run. Worker threads are
    /// background threads, so they never block process exit.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        // Wake any and all parked workers so they observe _disposed and exit.
        _signal.Release(_maxThreads);
    }
}
