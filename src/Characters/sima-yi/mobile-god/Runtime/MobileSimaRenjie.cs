using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MobileSimaRenjieReturnKind { Counterspell, FailedAttack, BorrowedSword, Qizheng }
public sealed record MobileSimaResponseRequirement(long Id, long UseFrameId, int ResponderSeat,
    CardKind RequiredKind, int TargetIndex, int SuccessfulResponses, bool Settled = false);
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileSimaResponseRequirement? MobileSimaResponse { get; init; } }
[JsonDerivedType(typeof(MobileSimaRenjieFrame), "mobile-sima-renjie")]
public abstract partial record ResolutionFrame;
public sealed record MobileSimaRenjieFrame(long Id, long ParentFrameId, long UseFrameId, CardConversionSource Source,
    string ContentHash, MobileSimaRenjieReturnKind ReturnKind, string OccurrenceKey, int RoundNumber,
    int CandidateIndex = -1, int ChainDepth = -1, long RequirementId = 0, bool Applied = false,
    ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.MobileSimaRenjie, Step), IProgramChildResolutionFrame;
public sealed record MobileSimaResponseRequiredEvent(MobileSimaResponseRequirement Requirement) : IGameEvent;
public sealed record MobileSimaRenjieStartedEvent(long FrameId, long ParentFrameId, long UseFrameId, CardConversionSource Source,
    MobileSimaRenjieReturnKind ReturnKind, string OccurrenceKey, int RoundNumber) : IGameEvent;
public sealed record MobileSimaRenjieAppliedEvent(long FrameId, int OwnerSeat, int RoundNumber, bool Gained) : IGameEvent;

public sealed partial class GameEngine
{
    private const string MobileSimaRenjie = "mobile-god:renjie";
    private bool HasMobileSimaRenjie => _contentRegistry.Skills.ContainsKey(MobileSimaRenjie);
    private int MobileSimaSuccessfulResponses(CardAttackHandle attack, CardKind kind) => kind == CardKind.Dodge
        ? attack.SuccessfulDodgeResponses : ActiveDuel is { } duel && duel.ResolutionId == attack.ResolutionId
            ? duel.SuccessfulSlashResponses : 0;
    private void MarkMobileSimaRenjieResponse(CardAttackHandle attack, int responderSeat, CardKind requiredKind)
    {
        if (!HasMobileSimaRenjie || !HasRuntimeSkill(_players[responderSeat], MobileSimaRenjie) ||
            LifecycleCardUse(attack.ResolutionId) is not { } use || use.SourceSeat == responderSeat || !_players[responderSeat].IsAlive) return;
        var progress = MobileSimaSuccessfulResponses(attack, requiredKind);
        if (use.MobileSimaResponse is { Settled: false } old && old.ResponderSeat == responderSeat && old.RequiredKind == requiredKind &&
            old.TargetIndex == use.TargetIndex && old.SuccessfulResponses == progress) return;
        var r = new MobileSimaResponseRequirement(++_resolutionSequence, use.Id, responderSeat, requiredKind, use.TargetIndex, progress);
        UpdateLifecycleCardUse(use.Id, current => current with { MobileSimaResponse = r });
        AdvanceEventRulesAndQueueFact(new MobileSimaResponseRequiredEvent(r));
    }
    private void ClearMobileSimaRenjieResponse(CardAttackHandle attack)
    {
        if (HasMobileSimaRenjie && LifecycleCardUse(attack.ResolutionId)?.MobileSimaResponse is not null)
            UpdateLifecycleCardUse(attack.ResolutionId, use => use with { MobileSimaResponse = null });
    }
    private bool TryBeginMobileSimaRenjieFailedAttack(CardAttackHandle attack)
    {
        if (!HasMobileSimaRenjie || attack.IsChainPropagation || LifecycleCardUse(attack.ResolutionId) is not { MobileSimaResponse: { Settled: false } r } use ||
            r.ResponderSeat != attack.TargetSeat || r.TargetIndex != use.TargetIndex ||
            r.SuccessfulResponses != MobileSimaSuccessfulResponses(attack, r.RequiredKind)) return false;
        if (_resolutionStack.LastOrDefault() is not { } parent || parent.Id != use.Id &&
            (parent is not ResponseWindowFrame response || response.ParentFrameId != use.Id))
            throw new InvalidOperationException("An unanswered native attack lost its original response host.");
        if (!CanBeginMobileSimaRenjie(r.ResponderSeat))
        { UpdateLifecycleCardUse(use.Id, current => current with { MobileSimaResponse = r with { Settled = true } }); return false; }
        return BeginMobileSimaRenjie(parent, use.Id, r.ResponderSeat, MobileSimaRenjieReturnKind.FailedAttack,
            "required:" + r.Id, requirement: r.Id);
    }
    private bool TryBeginMobileSimaRenjieNullification(NullificationWindowFrame supplied, bool explicitPass)
    {
        if (!HasMobileSimaRenjie || _resolutionStack.LastOrDefault() is not NullificationWindowFrame f || f.Id != supplied.Id ||
            f.CandidateIndex < 0 || f.CandidateIndex >= f.CandidateSeats.Count) return false;
        var actor = f.CandidateSeats[f.CandidateIndex];
        var source = f.ChainDepth == 0 ? f.SourceSeat : ProgramEventHistory<NullificationRespondedEvent>()
            .LastOrDefault(e => e.ResolutionId == f.ParentFrameId && e.ChainDepth == f.ChainDepth)?.ResponderSeat ?? f.SourceSeat;
        if (f.DelayedReturn is null && source == actor || !CanBeginMobileSimaRenjie(actor) ||
            IsWenjiNullificationResponseForbidden(f, actor) || WushuangFuqiNullificationBanned(actor) ||
            f.ChainDepth == 0 && IsNearbyTargetResponseProhibited(f.SourceSeat, actor, f.EffectCardKind, f.TargetSeats)) return false;
        if (explicitPass && _pendingDecision is { } p && (p.Kind != DecisionKind.Nullification || p.PlayerSeat != actor))
            throw new InvalidOperationException("Renjie cannot replace another accepted response decision.");
        ClearPendingDecision();
        return BeginMobileSimaRenjie(f, f.ParentFrameId, actor, MobileSimaRenjieReturnKind.Counterspell,
            $"counterspell:{f.Id}:{f.CandidateIndex}:{f.ChainDepth}", f.CandidateIndex, f.ChainDepth);
    }
    private bool TryBeginMobileSimaRenjieBorrowedSword(BorrowedSwordHandle pending)
    {
        var owner = _players[pending.WeaponOwnerSeat];
        if (!HasMobileSimaRenjie || !owner.IsAlive || pending.SourceSeat == owner.Seat || !CanBeginMobileSimaRenjie(owner.Seat) ||
            IsIssuedCardUnrespondable(pending.ResolutionId) || IsYingboUnrespondable(pending.ResolutionId) ||
            IsWenjiResponseForbidden(pending.ResolutionId, owner.Seat) ||
            !IsLegalBorrowedSwordSlashTarget(owner, _players[pending.SlashTargetSeat]) ||
            !GetEquipment(owner).Any(c => EquipmentCatalog.Get(c.Kind).Slot == EquipmentSlot.Weapon) ||
            LifecycleCardUse(pending.ResolutionId) is not { } use) return false;
        var key = $"borrowed:{use.Id}:{use.TargetIndex}:{NativeExtraEffectRoundOrdinal(use.Id)}";
        if (ProgramEventHistory<MobileSimaRenjieStartedEvent>().Any(e => e.OccurrenceKey == key)) return false;
        if (_resolutionStack.LastOrDefault()?.Id != use.Id) throw new InvalidOperationException("Borrowed Sword's missing use lost its native owner.");
        ClearPendingDecision();
        return BeginMobileSimaRenjie(use, use.Id, owner.Seat, MobileSimaRenjieReturnKind.BorrowedSword, key);
    }
    private bool CanBeginMobileSimaRenjie(int actor) => _winner == Winner.None && _players[actor].IsAlive &&
        HasRuntimeSkill(_players[actor], MobileSimaRenjie) &&
        ProgramEventHistory<MobileSimaRenjieStartedEvent>().Count(e => e.Source.OwnerSeat == actor && e.RoundNumber == _roundNumber) < 4;
    private bool TryBeginMobileSimaRenjieQizheng(CardUseFrame use)
    {
        if (!HasMobileSimaRenjie || use.Qizheng is not { Stage: QizhengStage.Response, ResponseKind: null } q ||
            use.SourceSeat == q.Target || !CanBeginMobileSimaRenjie(q.Target) || IsWenjiResponseForbidden(use.Id, q.Target) ||
            IsIssuedCardUnrespondable(use.Id) || IsYingboUnrespondable(use.Id)) return false;
        var key = $"qizheng:{use.Id}:{q.NullificationId}:{q.TargetIndex}:{q.EffectOrdinal}";
        if (ProgramEventHistory<MobileSimaRenjieStartedEvent>().Any(e => e.OccurrenceKey == key)) return false;
        if (_resolutionStack.LastOrDefault()?.Id != use.Id) throw new InvalidOperationException("Qizheng's unanswered response lost its original owner.");
        ClearPendingDecision();
        return BeginMobileSimaRenjie(use, use.Id, q.Target, MobileSimaRenjieReturnKind.Qizheng, key);
    }
    private bool BeginMobileSimaRenjie(ResolutionFrame parent, long useId, int actor, MobileSimaRenjieReturnKind kind,
        string key, int candidate = -1, int depth = -1, long requirement = 0)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault()?.Id != parent.Id)
            throw new InvalidOperationException("Renjie requires one accepted, unanswered native occurrence.");
        var grant = _players[actor].SkillGrants.Grants.First(g => g.SkillId == MobileSimaRenjie && g.IsEnabled);
        var f = new MobileSimaRenjieFrame(++_resolutionSequence, parent.Id, useId,
            new(MobileSimaRenjie, "unanswered-native", actor, grant.SkillInstanceId), _contentRegistry.ContentHash,
            kind, key, _roundNumber, candidate, depth, requirement);
        PushRuntimeFrame(f);
        AdvanceEventRulesAndQueueFact(new MobileSimaRenjieStartedEvent(f.Id, parent.Id, useId, f.Source, kind, key, f.RoundNumber));
        AdvanceRuntimeFrame(f.Id); return true;
    }
    [NativeFrameRoute(typeof(MobileSimaRenjieFrame), 40425)]
    private void ContinueMobileSimaRenjie(MobileSimaRenjieFrame supplied)
    {
        if (_resolutionStack.LastOrDefault() is not MobileSimaRenjieFrame f || f.Id != supplied.Id || _pendingDecision is not null) return;
        AssertMobileSimaRenjie(f);
        if (!f.Applied)
        {
            if (TryBeginSkillInvocation(f.Id, f.Source.OwnerSeat, f.Source.SkillId, f.OccurrenceKey, f.Source.SkillInstanceId)) return;
            f = (MobileSimaRenjieFrame)_resolutionStack[^1]; var owner = _players[f.Source.OwnerSeat];
            var gained = owner.IsAlive && _winner == Winner.None;
            ReplaceRuntimeTop(f = f with { Applied = true });
            if (gained)
            {
                owner.MarkerSourceCounts[(PlayerMarkerKind.Ren, owner.Seat)] = owner.MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.Ren, owner.Seat)) + 1;
                owner.Markers[PlayerMarkerKind.Ren] = owner.Markers.GetValueOrDefault(PlayerMarkerKind.Ren) + 1;
                AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id, owner.Seat, PlayerMarkerKind.Ren, 1,
                    owner.Markers[PlayerMarkerKind.Ren], owner.Seat, "skill.mobile-god.renjie.unanswered"));
            }
            AdvanceEventRulesAndQueueFact(new MobileSimaRenjieAppliedEvent(f.Id, owner.Seat, f.RoundNumber, gained));
        }
        AssertMobileSimaRenjie(f);
        PopResolutionFrame(f.Id, ResolutionFrameKind.MobileSimaRenjie);
        if (f.ReturnKind == MobileSimaRenjieReturnKind.Counterspell)
        {
            var parent = (NullificationWindowFrame)_resolutionStack[^1];
            ContinueNullificationWindow(ReplaceNullificationWindowFrame(parent with { CandidateIndex = parent.CandidateIndex + 1 })); return;
        }
        if (f.ReturnKind == MobileSimaRenjieReturnKind.Qizheng)
        { ResolveQizhengAfterResponse(f.UseFrameId); return; }
        if (f.ReturnKind == MobileSimaRenjieReturnKind.BorrowedSword)
        { CompleteBorrowedSwordWithoutSlash(new BorrowedSwordHandle(this, f.UseFrameId), transferWeapon: true); return; }
        var use = LifecycleCardUse(f.UseFrameId)!;
        UpdateLifecycleCardUse(use.Id, current => current with { MobileSimaResponse = current.MobileSimaResponse! with { Settled = true } });
        var attack = ActiveCardAttack ?? throw new InvalidOperationException("Renjie lost its exact native attack return.");
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack);
    }
    private bool MobileSimaRenjieParentMatches(ResolutionFrame parent, MobileSimaRenjieFrame f)
    {
        if (f.ParentFrameId != parent.Id) return false;
        if (f.ReturnKind == MobileSimaRenjieReturnKind.Counterspell)
            return parent is NullificationWindowFrame n && HasNullificationEffectOwner(n) && n.ParentFrameId == f.UseFrameId && n.CandidateIndex == f.CandidateIndex && n.ChainDepth == f.ChainDepth &&
                n.CandidateSeats.ElementAtOrDefault(n.CandidateIndex) == f.Source.OwnerSeat && f.OccurrenceKey == $"counterspell:{n.Id}:{n.CandidateIndex}:{n.ChainDepth}";
        if (LifecycleCardUse(f.UseFrameId) is not { } use) return false;
        if (f.ReturnKind == MobileSimaRenjieReturnKind.Qizheng)
            return parent.Id == use.Id && use.Qizheng is { Stage: QizhengStage.Response, ResponseKind: null } q && q.Target == f.Source.OwnerSeat &&
                f.OccurrenceKey == $"qizheng:{use.Id}:{q.NullificationId}:{q.TargetIndex}:{q.EffectOrdinal}";
        if (f.ReturnKind == MobileSimaRenjieReturnKind.BorrowedSword)
            return parent.Id == use.Id && ActiveBorrowedSword is { } sword && sword.ResolutionId == use.Id && sword.WeaponOwnerSeat == f.Source.OwnerSeat &&
                f.OccurrenceKey == $"borrowed:{use.Id}:{use.TargetIndex}:{NativeExtraEffectRoundOrdinal(use.Id)}";
        return (parent.Id == use.Id || parent is ResponseWindowFrame response && response.ParentFrameId == use.Id) &&
            use.MobileSimaResponse is { Settled: false } r && r.Id == f.RequirementId && r.ResponderSeat == f.Source.OwnerSeat && r.TargetIndex == use.TargetIndex &&
            ActiveCardAttack is { } attack && attack.ResolutionId == use.Id && attack.TargetSeat == r.ResponderSeat &&
            r.SuccessfulResponses == MobileSimaSuccessfulResponses(attack, r.RequiredKind) && f.OccurrenceKey == "required:" + r.Id &&
            ProgramEventHistory<MobileSimaResponseRequiredEvent>().Count(e => e.Requirement == r) == 1;
    }
    private void AssertMobileSimaRenjie(MobileSimaRenjieFrame f)
    {
        var index = _resolutionStack.FindIndex(p => p.Id == f.Id);
        if (index < 1 || f.Source.SkillId != MobileSimaRenjie || f.ContentHash != _contentRegistry.ContentHash ||
            f.RoundNumber != _roundNumber || !MobileSimaRenjieParentMatches(_resolutionStack[index - 1], f) ||
            ProgramEventHistory<MobileSimaRenjieStartedEvent>().Count(e => e == new MobileSimaRenjieStartedEvent(f.Id, f.ParentFrameId, f.UseFrameId,
                f.Source, f.ReturnKind, f.OccurrenceKey, f.RoundNumber)) != 1 ||
            ProgramEventHistory<MobileSimaRenjieAppliedEvent>().Count(e => e.FrameId == f.Id) != (f.Applied ? 1 : 0))
            throw new InvalidOperationException("Renjie lost its exact unanswered native cursor, real invocation or once-only marker result.");
    }
    private bool MobileSimaRenjieStructuralEdge(ResolutionFrame parent, ResolutionFrame child) =>
        child is MobileSimaRenjieFrame f && MobileSimaRenjieParentMatches(parent, f) &&
        ProgramEventHistory<MobileSimaRenjieStartedEvent>().Count(e => e.FrameId == f.Id && e.ParentFrameId == parent.Id && e.OccurrenceKey == f.OccurrenceKey) == 1;
    private bool HasMobileSimaRenjieNativeSubtree(long useId)
    {
        var index = _resolutionStack.FindLastIndex(p => p is MobileSimaRenjieFrame f && f.UseFrameId == useId);
        return index > 0 && MobileSimaRenjieStructuralEdge(_resolutionStack[index - 1], _resolutionStack[index]) &&
            (index == _resolutionStack.Count - 1 || _resolutionStack[index + 1] is SkillInvocationFrame invocation &&
                SkillInvocationStructuralEdge(_resolutionStack[index], invocation) && HasSkillInvocationObserverSuffix(index + 1));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeResponseHealth, 40425)]
    private bool TryAdvanceMobileSimaRenjieSubtree()
    {
        if (_pendingDecision is not null || _resolutionStack.OfType<MobileSimaRenjieFrame>().LastOrDefault() is not { } root ||
            !HasMobileSimaRenjieNativeSubtree(root.UseFrameId)) return false;
        if (_resolutionStack.LastOrDefault()?.Id == root.Id) { AdvanceRuntimeFrame(root.Id); return true; }
        return TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 40425)]
    private void AssertMobileSimaRenjieFrames()
    { foreach (var f in _resolutionStack.OfType<MobileSimaRenjieFrame>()) AssertMobileSimaRenjie(f); }
}
