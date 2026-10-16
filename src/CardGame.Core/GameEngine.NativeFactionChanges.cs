namespace CardGame.Core;

public sealed record NativeEffectiveFactionChangedEvent(long ChangeId, int TurnNumber,
    int ActualTurnOwnerSeat, int TargetSeat, string? PreviousFactionId, string CurrentFactionId,
    long? ParentFrameId, CardConversionSource? Source, string Cause) : IGameEvent;

public sealed partial class CharacterState
{
    internal string? NativeEffectiveFactionOverrideId { get; set; }
    internal bool NativeEffectiveFactionWasDeclared { get; set; }
}

public sealed partial class GameEngine
{
    private sealed record NativeFactionAvatar(PrivateGeneralLibrarySource Source,
        string GeneralId, string? SkillId, string? SelectedFactionId);
    private sealed record NativeFactionObservation(string? FactionId, NativeFactionAvatar? Avatar);
    private readonly Dictionary<int, NativeFactionObservation> _nativeFactionObservations = [];
    private static readonly IReadOnlyList<string> NativeEffectiveFactionIds =
        Array.AsReadOnly(new[] { "wei", "shu", "wu", "qun", "jin" });
    private long _nativeFactionRevision;
    private long _nativeFactionQualificationStamp;
    private (long Base, long Revision) _nativeFactionQualificationDependencies;
    private bool _synchronizingNativeFactions;

    private bool TracksNativeEffectiveFactionChanges => _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.ResolveGoudeActualTurnEnd);

    private IReadOnlyList<string> AvailableEffectiveFactionIds() =>
        IsNationalWarMode ? Array.Empty<string>() : NativeEffectiveFactionIds;

    private bool HasNativeEffectiveFactionDeclaration(CharacterState player) =>
        TracksNativeEffectiveFactionChanges && player.NativeEffectiveFactionWasDeclared;

    private NativeFactionAvatar? CaptureNativeFactionAvatar(CharacterState player) =>
        player.GeneralSelected && !IsNationalWarMode && EffectiveGeneralLibrary(player) is { } library
            ? new(library.Source, library.RevealedGeneralId!, library.DeclaredSkillId, library.SelectedFactionId)
            : null;

    private string? CaptureNativeAvatarFactionBeforeDeclaration(CharacterState player) =>
        TracksNativeEffectiveFactionChanges ? GetEffectiveFactionId(player) : null;

    private void CompleteNativeAvatarFactionDeclaration(ProgramSkillFrame frame,
        CharacterState player, string? previous)
    {
        if (!TracksNativeEffectiveFactionChanges || IsNationalWarMode ||
            frame.GeneralLibraryDraft?.Source != EffectiveGeneralLibrary(player)?.Source) return;
        // A real declaration replaces an earlier explicit change even when the
        // same avatar/skill is selected. Acquiring an unused avatar does not.
        player.NativeEffectiveFactionOverrideId = null;
        var current = GetEffectiveFactionId(player);
        _nativeFactionObservations[player.Seat] = new(current, CaptureNativeFactionAvatar(player));
        if (string.Equals(previous, current, StringComparison.Ordinal) || current is null) return;
        _nativeFactionRevision++;
        if (!_setupComplete || _turnNumber < 1) return;
        player.NativeEffectiveFactionWasDeclared = true;
        AdvanceEventRulesAndQueueFact(new NativeEffectiveFactionChangedEvent(++_resolutionSequence,
            _turnNumber, _turnProgression.OwnerSeat, player.Seat, previous, current, frame.Id,
            new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId), "avatar-state"));
    }

    private void ChangeNativeEffectiveFaction(ProgramSkillFrame frame, string faction)
    {
        if (!TracksNativeEffectiveFactionChanges || _resolutionStack.LastOrDefault()?.Id != frame.Id ||
            GetPausedPrivateOfferEffect(frame)?.Op != SkillProgramEffectOp.ResolveGoudeActualTurnEnd ||
            frame.WindowContext?.Window != SkillProgramTriggerWindow.AfterTurnEnded ||
            !IsValidPlayerSeat(frame.OwnerSeat) || !_players[frame.OwnerSeat].IsAlive ||
            !AvailableEffectiveFactionIds().Contains(faction, StringComparer.Ordinal))
            throw new InvalidOperationException("A faction change requires its actual living native program owner and supported faction.");
        SynchronizeNativeEffectiveFactions();
        var player = _players[frame.OwnerSeat];
        var previous = GetEffectiveFactionId(player);
        if (string.Equals(previous, faction, StringComparison.Ordinal))
            throw new InvalidOperationException("A faction change cannot select the current effective faction.");
        var previousAvatar = CaptureNativeFactionAvatar(player);
        player.NativeEffectiveFactionOverrideId = faction;
        player.NativeEffectiveFactionWasDeclared = true;
        _nativeFactionObservations[player.Seat] = new(faction, previousAvatar);
        _nativeFactionRevision++;
        AdvanceEventRulesAndQueueFact(new NativeEffectiveFactionChangedEvent(++_resolutionSequence,
            _turnNumber, _turnProgression.OwnerSeat, player.Seat, previous, faction, frame.Id,
            new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId), "program"));
        AddLog("FactionChanged", $"{player.Name} 将势力变更为{GetFactionName(faction)}。", player.Seat);
    }

    // This observes completed native mutations. The cache contains no pending
    // resolution or entitlement; every actual change is retained as a scalar fact.
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 35)]
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 15)]
    private void SynchronizeNativeEffectiveFactions()
    {
        if (!TracksNativeEffectiveFactionChanges || _synchronizingNativeFactions) return;
        _synchronizingNativeFactions = true;
        try
        {
            var changes = new List<(CharacterState Player, string? Previous, string Current, string Cause)>();
            foreach (var player in _players)
            {
                var avatar = CaptureNativeFactionAvatar(player);
                var known = _nativeFactionObservations.TryGetValue(player.Seat, out var previous);
                if (known && previous!.Avatar != avatar)
                    player.NativeEffectiveFactionOverrideId = null;
                var current = player.GeneralSelected ? GetEffectiveFactionId(player) : null;
                _nativeFactionObservations[player.Seat] = new(current, avatar);
                if (!known || string.Equals(previous!.FactionId, current, StringComparison.Ordinal) ||
                    !_setupComplete || _turnNumber < 1 || current is null) continue;
                player.NativeEffectiveFactionWasDeclared = true;
                _nativeFactionRevision++;
                changes.Add((player, previous.FactionId, current,
                    previous.Avatar != avatar ? "avatar-state" : "native-state"));
            }
            // Refresh every observation before recursive rule delivery.
            foreach (var change in changes)
                AdvanceEventRulesAndQueueFact(new NativeEffectiveFactionChangedEvent(++_resolutionSequence,
                    _turnNumber, _turnProgression.OwnerSeat, change.Player.Seat, change.Previous,
                    change.Current, _resolutionStack.LastOrDefault()?.Id, null, change.Cause));
        }
        finally { _synchronizingNativeFactions = false; }
    }

    private long CaptureNativeFactionQualificationStamp(long underlying)
    {
        if (!TracksNativeEffectiveFactionChanges) return underlying;
        var current = (underlying, _nativeFactionRevision);
        if (_nativeFactionQualificationStamp == 0 || current != _nativeFactionQualificationDependencies)
        {
            _nativeFactionQualificationDependencies = current;
            _nativeFactionQualificationStamp++;
        }
        return long.MinValue / 2 + _nativeFactionQualificationStamp;
    }
}
