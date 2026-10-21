using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CardGame.Core;

internal sealed record ParticipantPromptHostKey(long FrameId, PromptId PromptId,
    CardConversionSource Source, string GameplayHash);
internal sealed record ParticipantPromptHostState(ParticipantPromptHostKey Key, int RecipientSeat,
    IReadOnlyList<int> PendingNpcSeats, bool CanAcceptInput);
internal sealed record ParticipantGiftWork(ParticipantPromptHostKey Key, int ActorSeat,
    long Revision, Func<ChoiceId> Choose);

/// <summary>
/// Transient, opt-in participant input host. Engine access belongs exclusively
/// to the supplied owner context; workers receive detached decision inputs only.
/// Attach after replay, never while replaying an accepted command prefix.
/// </summary>
public sealed class ParticipantPromptHost : IDisposable, IAsyncDisposable
{
    private sealed class Job(ParticipantGiftWork work, CancellationTokenSource cancellation)
    {
        internal readonly ParticipantGiftWork Work = work;
        internal readonly CancellationTokenSource Cancellation = cancellation;
        internal void Cancel() { try { Cancellation.Cancel(); } catch (ObjectDisposedException) { } }
    }
    private sealed record Ready(Job Job, ChoiceId Choice, Exception? Error);
    private sealed class OwnerInbox
    {
        private readonly Channel<Action> _queue = Channel.CreateUnbounded<Action>(
            new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
        internal readonly Task Completion;
        internal OwnerInbox() => Completion = Task.Run(async () =>
        {
            await foreach (var action in _queue.Reader.ReadAllAsync().ConfigureAwait(false)) action();
        });
        internal void Post(Action action)
        {
            if (!_queue.Writer.TryWrite(action)) throw new ObjectDisposedException(nameof(ParticipantPromptHost));
        }
        internal void Complete() => _queue.Writer.TryComplete();
    }

    private readonly GameEngine _game;
    private readonly Action<Action> _post;
    private readonly Func<GameCommand, CommandResult> _submit;
    private readonly Func<bool> _canAutoSubmit;
    private readonly OwnerInbox? _inbox;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<int, Job> _jobs = [];
    private readonly List<Task> _workers = [];
    private readonly ConcurrentQueue<Ready> _ready = new();
    private ParticipantPromptHostState? _state;
    private TaskCompletionSource<bool> _changed = NewSignal();
    private int _posted;
    private int _stopRequested;
    private bool _stopped;
    private long _seenRevision = -1;
    private int _seenReadyCount = -1;

    private ParticipantPromptHost(GameEngine game, Action<Action> post,
        Func<GameCommand, CommandResult> submit, Func<bool> canAutoSubmit, OwnerInbox? inbox)
    {
        _game = game; _post = post; _submit = submit; _canAutoSubmit = canAutoSubmit; _inbox = inbox;
        game.StateChanged += OnStateChanged;
        RequestProgress();
    }

    /// <summary>post must enqueue, never invoke inline. Call owner-only members on that context.</summary>
    public static ParticipantPromptHost Attach(GameEngine game, Action<Action> post,
        Func<GameCommand, CommandResult> submit, Func<bool>? canAutoSubmit = null) =>
        new(game, post, submit, canAutoSubmit ?? (() => true), null);

    /// <summary>After this call, access this engine through the host's async methods only.</summary>
    public static ParticipantPromptHost CreateHeadless(GameEngine game)
    {
        var inbox = new OwnerInbox();
        return new(game, inbox.Post, command => game.Submit(command), () => true, inbox);
    }

    public event Action? Updated;
    public event Action<Exception>? Failed;
    public Exception? Failure { get; private set; }
    // These properties are for the owner context, not player/network payloads.
    public bool HasActiveInvitation => _state is not null;
    public bool IsAcceptingInput => _state is { CanAcceptInput: true } state && _game.IsParticipantInputBoundary(state.Key);
    public bool HasReadyResponse => !_ready.IsEmpty;

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void OnStateChanged(GameSnapshot _) => RequestProgress();

    public void RequestProgress()
    {
        if (Volatile.Read(ref _stopRequested) != 0 || Interlocked.Exchange(ref _posted, 1) != 0) return;
        try
        {
            _post(() =>
            {
                Interlocked.Exchange(ref _posted, 0);
                if (_stopped || Volatile.Read(ref _stopRequested) != 0) return;
                try
                {
                    Synchronize();
                    if (Failure is null && _canAutoSubmit()) PumpCore();
                }
                catch (Exception error) { Fail(error); }
            });
        }
        catch (ObjectDisposedException) { Interlocked.Exchange(ref _posted, 0); }
    }

    private void Notify()
    {
        var signal = _changed;
        _changed = NewSignal();
        signal.TrySetResult(true);
        Updated?.Invoke();
    }

    private void NotifyIfChanged(bool stateChanged)
    {
        var readyCount = _ready.Count;
        if (!stateChanged && _seenRevision == _game.Revision && _seenReadyCount == readyCount) return;
        _seenRevision = _game.Revision; _seenReadyCount = readyCount;
        Notify();
    }

    private void ClearJobs()
    {
        foreach (var job in _jobs.Values) job.Cancel();
        _jobs.Clear();
        _ready.Clear();
    }

    private void Synchronize()
    {
        if (_stopped || Failure is not null) return;
        var next = _game.CaptureParticipantHostState();
        if (next is null && _game.HasParticipantInputBoundary)
            throw new InvalidOperationException("The participant input boundary has no host-state provider.");
        var stateChanged = next?.Key != _state?.Key || next?.CanAcceptInput != _state?.CanAcceptInput ||
            (next is not null && _state is not null && !next.PendingNpcSeats.SequenceEqual(_state.PendingNpcSeats));
        if (next?.Key != _state?.Key) ClearJobs();
        _state = next;
        if (next is null) { NotifyIfChanged(stateChanged); return; }
        foreach (var actor in _jobs.Keys.Where(actor => !next.PendingNpcSeats.Contains(actor)).ToArray())
        { _jobs[actor].Cancel(); _jobs.Remove(actor); }
        if (!next.CanAcceptInput) { NotifyIfChanged(stateChanged); return; }

        _workers.RemoveAll(task => task.IsCompleted);
        var batch = new List<Job>();
        foreach (var actor in next.PendingNpcSeats)
        {
            if (_jobs.ContainsKey(actor)) continue;
            var work = _game.CaptureParticipantGiftWork(next, actor) ??
                throw new InvalidOperationException("A pending participant has no current private gift decision.");
            var job = new Job(work, CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token));
            _jobs.Add(actor, job); batch.Add(job);
        }
        // No NPC begins its actual decision until all this batch's inputs exist.
        var start = NewSignal();
        foreach (var job in batch) _workers.Add(RunDecision(job, start.Task));
        start.TrySetResult(true);
        NotifyIfChanged(stateChanged);
    }

    private async Task RunDecision(Job job, Task start)
    {
        var token = job.Cancellation.Token;
        try
        {
            await Task.Run(async () =>
            {
                await start.WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                // Actual relationship/material evaluation occurs here, off the owner.
                var choice = job.Work.Choose();
                token.ThrowIfCancellationRequested();
                _ready.Enqueue(new(job, choice, null));
                RequestProgress();
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            { _ready.Enqueue(new(job, default, error)); RequestProgress(); }
        }
        finally { job.Cancellation.Dispose(); }
    }

    /// <summary>Owner-context manual step; consumes at most one actual command.</summary>
    public bool PumpOne()
    {
        if (_inbox is not null) throw new InvalidOperationException("Use the headless host's async methods.");
        try { return PumpCore(); }
        catch (Exception error) { Fail(error); throw; }
    }

    private bool PumpCore()
    {
        Synchronize();
        if (_stopped || Failure is not null || _state is not { CanAcceptInput: true } state) return false;
        var discarded = false;
        while (_ready.TryDequeue(out var ready))
        {
            var work = ready.Job.Work;
            if (!_jobs.TryGetValue(work.ActorSeat, out var active) || !ReferenceEquals(active, ready.Job)) continue;
            _jobs.Remove(work.ActorSeat); ready.Job.Cancel();
            if (ready.Error is { } error) { Fail(error); return false; }
            var view = _game.CreateSnapshot(work.ActorSeat);
            var prompt = view.PendingDecision;
            if (state.Key != work.Key || view.Revision != work.Revision ||
                prompt?.ParticipantFrameId != work.Key.FrameId || prompt.PromptId != work.Key.PromptId ||
                !prompt.Choices.Any(choice => choice.Id == ready.Choice))
            { discarded = true; continue; }

            var result = _submit(new AnswerPromptCommand(work.ActorSeat, work.Key.PromptId, ready.Choice, work.Revision));
            if (!result.Accepted && _game.Revision == work.Revision)
                throw new InvalidOperationException("A current participant decision was rejected: " + result.Error?.Message);
            Synchronize();
            if (!_ready.IsEmpty && IsAcceptingInput && _canAutoSubmit()) RequestProgress();
            return result.Accepted;
        }
        if (discarded) Synchronize(); // Fresh views cause new real decisions, never revision rebasing.
        return false;
    }

    private void Fail(Exception error)
    {
        if (Failure is not null || _stopped) return;
        Failure = error; ClearJobs();
        try { Notify(); Failed?.Invoke(error); }
        catch { /* A host observer cannot turn a failed decision into a game command. */ }
    }

    private Task<T> InvokeAsync<T>(Func<T> action, CancellationToken token)
    {
        if (Volatile.Read(ref _stopRequested) != 0)
            return Task.FromException<T>(new ObjectDisposedException(nameof(ParticipantPromptHost)));
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _post(() =>
            {
                if (token.IsCancellationRequested) { completion.TrySetCanceled(token); return; }
                try
                {
                    if (_stopped) throw new ObjectDisposedException(nameof(ParticipantPromptHost));
                    if (Failure is { } error) throw new InvalidOperationException("Participant host failed.", error);
                    completion.TrySetResult(action());
                }
                catch (Exception error) { completion.TrySetException(error); }
            });
        }
        catch (Exception error) { completion.TrySetException(error); }
        return completion.Task.WaitAsync(token);
    }

    /// <summary>Trusted-host result; do not forward its configured-human snapshot to another actor.</summary>
    public Task<CommandResult> SubmitAsync(GameCommand command, CancellationToken cancellationToken = default) =>
        InvokeAsync(() => { var result = _submit(command); Synchronize(); return result; }, cancellationToken);

    public Task<GameSnapshot> CreateSnapshotAsync(int viewerSeat, CancellationToken cancellationToken = default) =>
        InvokeAsync(() => _game.CreateSnapshot(viewerSeat), cancellationToken);

    /// <summary>
    /// Drives ordinary synchronous steps and waits for real participant work.
    /// A human boundary returns without stopping the owner's participant inbox.
    /// </summary>
    public async Task<GameSnapshot> RunToBoundaryAsync(CancellationToken cancellationToken = default)
    {
        if (_inbox is null) throw new InvalidOperationException("Use the desktop owner pump for an attached host.");
        while (true)
        {
            var boundary = await InvokeAsync(() =>
            {
                Synchronize();
                var view = _game.State;
                if (view.Status == EngineStatus.Completed || view.PendingDecision is not null)
                    return (View: view, Done: true, Wait: Task.CompletedTask);
                if (_state is null || !_state.CanAcceptInput)
                {
                    var result = _submit(new AdvanceCommand(_game.Revision));
                    if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message);
                    Synchronize(); view = _game.State;
                }
                RequestProgress();
                if (_state is { CanAcceptInput: true, PendingNpcSeats.Count: 0 } &&
                    view.PendingDecision is null)
                    throw new InvalidOperationException("An open participant window has no available actor.");
                return (View: view, Done: view.Status == EngineStatus.Completed || view.PendingDecision is not null,
                    Wait: _state is { CanAcceptInput: true } ? (Task)_changed.Task : Task.CompletedTask);
            }, cancellationToken).ConfigureAwait(false);
            if (boundary.Done) return boundary.View;
            await boundary.Wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void StopCore()
    {
        if (_stopped) return;
        _stopped = true;
        _game.StateChanged -= OnStateChanged;
        _lifetime.Cancel(); ClearJobs(); _state = null;
        _changed.TrySetResult(true);
        _lifetime.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopRequested, 1) != 0) return;
        if (_inbox is null) StopCore();
        else _inbox.Post(() => { StopCore(); _inbox.Complete(); });
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_inbox is not null) await _inbox.Completion.ConfigureAwait(false);
        await Task.WhenAll(_workers.ToArray()).ConfigureAwait(false);
    }
}
