using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record XianlueRecordedEvent(long FrameId, int OwnerSeat, string SkillInstanceId, CardKind Kind) : IGameEvent;
public sealed record XianlueRewardIssuedEvent(long FrameId, int OwnerSeat, string SkillId, string SkillInstanceId, string GameplayHash, int TurnNumber, long WindowId, long CardUseId, long CardActionId) : IGameEvent;
public sealed record XianlueMovementIssuedEvent(long FrameId, XianlueStage Stage, long Before, long After, int? GiftCardId, int? GiftTargetSeat) : IGameEvent;
public sealed record ZaowangGrantedEvent(long FrameId, int OwnerSeat, int TargetSeat, string SkillId, string SkillInstanceId, string GameplayHash, int HpBeforeRecovery, int GrantedMaximumHp) : IGameEvent;
public sealed record IdentityExchangedEvent(long DeathFrameId, int FormerLordSeat, int SuccessorSeat) : IGameEvent;
internal sealed record ZaowangIdentityGrant(long FrameId, int OwnerSeat, int TargetSeat, Role OriginalRole, string SkillInstanceId, string GameplayHash);
public enum XianlueStage { Record, DrawChildren, Distribute, GiftChildren }
public sealed record XianluePendingState(XianlueStage Stage, int CardIndex, string SkillId = "", string SkillInstanceId = "", string GameplayHash = "", long WindowId = 0, long UseId = 0, long ActionId = 0,
    long DrawBefore = 0, long DrawAfter = 0, long GiftBefore = 0, long GiftAfter = 0, int? GiftCardId = null, int? GiftTargetSeat = null);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ZaowangPaidTargetSeat { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public XianluePendingState? XianluePending { get; init; }
}
public sealed partial class GameEngine
{
    private const string XianlueDrawBind = "xianlue-exact-drawn";
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 30)]
    private bool CanOfferDongZhao(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w)
    {
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.XianlueRecord))
            return _modeDefinition.ModeKind == ContentModeKind.Identity && w.SourceSeat is { } lord &&
                IsValidPlayerSeat(lord) && _players[lord].Role == Role.Lord && _players[lord].IsAlive;
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.XianlueRedistribute)) return true;
        var recorded = CompleteProgramEventHistory().OfType<XianlueRecordedEvent>().LastOrDefault(e =>
            e.OwnerSeat == c.OwnerSeat && e.SkillInstanceId == c.SkillInstanceId);
        return recorded is not null && w.CardUse is { } use && use.ActorSeat != c.OwnerSeat &&
            use.EffectiveKind == recorded.Kind && !CompleteProgramEventHistory().OfType<XianlueRewardIssuedEvent>().Any(e =>
                e.OwnerSeat == c.OwnerSeat && e.SkillInstanceId == c.SkillInstanceId && e.TurnNumber == _turnNumber);
    }
    private SkillProgramStepOutcome BeginXianlueRecord(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { XianluePending = new(XianlueStage.Record, 0) });
        PublishXianluePrompt(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private SkillProgramStepOutcome BeginXianlueRedistribute(ProgramSkillFrame f)
    {
        f = GetActiveProgramFrame(f.Id);
        if (f.WindowContext?.CardUse is not { } use) throw new InvalidOperationException("Xianlue lost its actual completed card use.");
        ReplaceRuntimeTop(f = f with { XianluePending = new(XianlueStage.DrawChildren, 0, f.SkillId, f.SkillInstanceId, f.GameplayHash, f.WindowContext.ParentFrameId, use.ParentCardUseFrameId, use.CardActionId, _movementSequence, _movementSequence), PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new XianlueRewardIssuedEvent(f.Id, f.OwnerSeat, f.SkillId, f.SkillInstanceId, f.GameplayHash, _turnNumber, f.WindowContext.ParentFrameId, use.ParentCardUseFrameId, use.CardActionId));
        var drawn = DrawCards(_players[f.OwnerSeat], 2, true, new("program.xianlue.draw"));
        SetProgramCardSet(f.Id, XianlueDrawBind, drawn, SkillProgramCardSetVisibility.Private);
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { XianluePending = f.XianluePending! with { DrawAfter = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new XianlueMovementIssuedEvent(f.Id, XianlueStage.DrawChildren, f.XianluePending!.DrawBefore, _movementSequence, null, null));
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private void PublishXianluePrompt(ProgramSkillFrame f)
    {
        var pending = f.XianluePending!;
        var choices = new List<PromptChoice>();
        PromptChoice Choice(string suffix, string label, IReadOnlyList<int> cards, IReadOnlyList<int> targets, string value) =>
            new(new ChoiceId($"xianlue.{f.Id}.{suffix}"), label, cards, targets,
                new Dictionary<string,string> { ["program-action"] = "xianlue", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["value"] = value });
        if (pending.Stage == XianlueStage.Record)
        {
            foreach (var kind in Enum.GetValues<CardKind>().Where(IsOrdinaryTrick).Distinct())
                choices.Add(Choice(kind.ToString(), $"记录【{CardCatalog.Get(kind).DisplayName}】", [], [], kind.ToString()));
        }
        else
        {
            var binding = GetProgramCardSet(f, XianlueDrawBind);
            var cardId = binding.CardIds[pending.CardIndex];
            foreach (var player in _players.Where(p => p.IsAlive))
                choices.Add(Choice($"give.{cardId}.{player.Seat}", $"将此牌分配给 {player.Name}", [cardId], [player.Seat], player.Seat.ToString(CultureInfo.InvariantCulture)));
        }
        var skill = _contentRegistry!.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, pending.Stage == XianlueStage.Record ? "先略：记录普通锦囊牌" : "先略：分配此次摸到的牌", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = pending.Stage != XianlueStage.Record, Choices = choices.AsReadOnly(),
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("xianlue")]
    private void ResolveDongZhaoChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Xianlue lost its frame.");
        if (f.XianluePending is not { } r || choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture) ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat) throw new InvalidOperationException("Xianlue choice belongs to a different frame.");
        ClearPendingDecision();
        if (r.Stage == XianlueStage.Record)
        {
            var kind = Enum.Parse<CardKind>(choice.Parameters["value"]);
            if (!IsOrdinaryTrick(kind)) throw new InvalidOperationException("Xianlue can only record an ordinary trick.");
            AdvanceEventRulesAndQueueFact(new XianlueRecordedEvent(f.Id, f.OwnerSeat, f.SkillInstanceId, kind));
            ReplaceRuntimeTop(f with { XianluePending = null }); AdvanceRuntimeProgram(f.Id); return;
        }
        if (r.Stage != XianlueStage.Distribute) throw new InvalidOperationException("Xianlue cannot repay a completed gift.");
        var id = GetProgramCardSet(f, XianlueDrawBind).CardIds[r.CardIndex];
        var target = int.Parse(choice.Parameters["value"], CultureInfo.InvariantCulture);
        if (!IsValidPlayerSeat(target) || !_players[target].IsAlive || choice.Cards is not [var chosen] || chosen != id ||
            _cardZones.GetLocation(id) != CardLocation.Hand(f.OwnerSeat)) throw new InvalidOperationException("Xianlue gift lost its actual drawn entity.");
        ReplaceRuntimeTop(f = f with { XianluePending = r with { Stage = XianlueStage.GiftChildren, CardIndex = r.CardIndex + 1, GiftBefore = _movementSequence, GiftAfter = _movementSequence, GiftCardId = id, GiftTargetSeat = target }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (target != f.OwnerSeat) MoveCards([GetHand(_players[f.OwnerSeat]).Single(c => c.Id == id)], CardLocation.Hand(f.OwnerSeat), CardLocation.Hand(target), new("program.xianlue.gift"));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { XianluePending = f.XianluePending! with { GiftAfter = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new XianlueMovementIssuedEvent(f.Id, XianlueStage.GiftChildren, f.XianluePending!.GiftBefore, _movementSequence, id, target));
        AdvanceRuntimeProgram(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 21900)]
    private bool ResumeDongZhao(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { XianluePending: { } r } f || f.Id != id) return false;
        if (r.Stage is XianlueStage.Record or XianlueStage.Distribute) { if (_pendingDecision is null) PublishXianluePrompt(f); return true; }
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return true;
        f = GetActiveProgramFrame(id); r = f.XianluePending!;
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive)
        { ReplaceRuntimeTop(f with { XianluePending = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(id); return true; }
        var cards = GetProgramCardSet(f, XianlueDrawBind).CardIds;
        var index = r.CardIndex;
        while (index < cards.Count && _cardZones.GetLocation(cards[index]) != CardLocation.Hand(f.OwnerSeat)) index++;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null,
            XianluePending = r with { Stage = index < cards.Count ? XianlueStage.Distribute : XianlueStage.Record, CardIndex = index } });
        PublishXianluePrompt(f); return true;
    }
    private SkillProgramStepOutcome ArmZaowang(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.SelectedTargetSeats is not [var seat] || !_players[seat].IsAlive) throw new InvalidOperationException("Zaowang needs one living recipient.");
        var target = _players[seat];
        if (f.ZaowangPaidTargetSeat is not null) throw new InvalidOperationException("Zaowang cannot pay twice.");
        ReplaceRuntimeTop(f = f with { ZaowangPaidTargetSeat = seat });
        if (_modeDefinition.ModeKind == ContentModeKind.Identity)
            target.AddZaowangGrant(new(f.Id, f.OwnerSeat, seat, target.Role, f.SkillInstanceId, f.GameplayHash));
        target.MaxHp = checked(target.MaxHp + 1);
        AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(seat, 1, target.MaxHp, f.SkillId));
        AdvanceEventRulesAndQueueFact(new ZaowangGrantedEvent(f.Id, f.OwnerSeat, seat, f.SkillId, f.SkillInstanceId, f.GameplayHash, target.Hp, target.MaxHp));
        return SkillProgramStepOutcome.Continue;
    }
    private bool CanContinueIssuedDongZhao(ProgramSkillFrame f) => ValidIssuedXianlue(f) || ValidIssuedZaowang(f);
    private bool ValidIssuedXianlue(ProgramSkillFrame f)
    {
        if(f.XianluePending is not {} r || f.InstructionIndex!=1 || r.SkillId != f.SkillId || r.SkillInstanceId != f.SkillInstanceId || r.GameplayHash != f.GameplayHash ||
            f.TriggerId is null || GetProgramTrigger(f).Effects is not [{ Op: SkillProgramEffectOp.XianlueRedistribute }] ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: {} u } w || w.ParentFrameId != r.WindowId || u.ParentCardUseFrameId != r.UseId || u.CardActionId != r.ActionId || u.ActorSeat == f.OwnerSeat ||
            r.DrawBefore<0 || r.DrawAfter<r.DrawBefore || r.DrawAfter>_movementSequence || r.CardIndex<0) return false;
        var facts=CompleteProgramEventHistory().OfType<XianlueRewardIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(facts is not [var fact] || fact.SkillId!=r.SkillId || fact.SkillInstanceId!=r.SkillInstanceId || fact.GameplayHash!=r.GameplayHash || fact.WindowId!=r.WindowId || fact.CardUseId!=r.UseId || fact.CardActionId!=r.ActionId) return false;
        var index=_resolutionStack.FindIndex(p=>p.Id==f.Id);
        if(index<1 || _resolutionStack[index-1] is not ProgramCardTriggerWindowFrame window || window.Id!=r.WindowId || window.ParentFrameId!=r.UseId ||
            window.Action.ActionId!=r.ActionId || window.CandidateIndex<0 || window.CandidateIndex>=window.Candidates.Count ||
            !MountObserverCandidateMatches(f,ToSharedCandidate(window.Candidates[window.CandidateIndex])) ||
            LifecycleCardUse(r.UseId) is not {Step:ResolutionFrameStep.Completed,Action:{} action} || action.ActionId!=r.ActionId || action.ActorSeat!=u.ActorSeat)return false;
        var b=f.CardSetBindings.SingleOrDefault(b=>b.Name==XianlueDrawBind);
        if(b is null || r.CardIndex>b.CardIds.Count || b.CardIds.Any(id=>_cardMovements.Count(m=>m.Sequence>r.DrawBefore && m.Sequence<=r.DrawAfter && m.CardId==id && m.To==CardLocation.Hand(f.OwnerSeat) && m.Reason.Value=="program.xianlue.draw")!=1))return false;
        return CompleteProgramEventHistory().OfType<XianlueMovementIssuedEvent>().Any(e=>e.FrameId==f.Id && e.Stage==XianlueStage.DrawChildren && e.Before==r.DrawBefore && e.After==r.DrawAfter) &&
            (r.Stage!=XianlueStage.GiftChildren || r.GiftCardId is {} gift && r.GiftTargetSeat is {} target && b.CardIds.Contains(gift) &&
             CompleteProgramEventHistory().OfType<XianlueMovementIssuedEvent>().Any(e=>e.FrameId==f.Id && e.Stage==r.Stage && e.Before==r.GiftBefore && e.After==r.GiftAfter && e.GiftCardId==gift && e.GiftTargetSeat==target) &&
             (target==f.OwnerSeat ? r.GiftBefore==r.GiftAfter : _cardMovements.Count(m=>m.Sequence>r.GiftBefore && m.Sequence<=r.GiftAfter && m.CardId==gift && m.From==CardLocation.Hand(f.OwnerSeat) && m.To==CardLocation.Hand(target) && m.Reason.Value=="program.xianlue.gift")==1));
    }
    private bool IsDongZhaoMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => e?.Op==SkillProgramEffectOp.XianlueRedistribute && p.SubjectSeat==f.OwnerSeat && p.BeforeCount==0 && p.CoverageResultBind is null && ValidIssuedXianlue(f);
    private bool DongZhaoFirstChild(ProgramSkillFrame root, ResolutionFrame child)
    {
        if(ValidIssuedZaowang(root))return ZaowangFirstChild(root,child);
        if(!ValidIssuedXianlue(root) || root.XianluePending is not {} r || child is not CardsMovedTriggerWindowFrame moved)return false;
        return moved.Batch.ParentFrameId==root.Id && moved.Batch.AwaitingProgramFrameId==root.Id && moved.ResumeProgramFrameId is null &&
            moved.Batch.OriginOwnerSeat==root.OwnerSeat && moved.Batch.OriginSkillId==root.SkillId && moved.Batch.OriginSkillInstanceId==root.SkillInstanceId && moved.Batch.Movements.Count>0 &&
            moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && (r.Stage==XianlueStage.DrawChildren ? m.Sequence>r.DrawBefore && m.Sequence<=r.DrawAfter && m.Reason.Value=="program.xianlue.draw" :
                r.Stage==XianlueStage.GiftChildren && m.Sequence>r.GiftBefore && m.Sequence<=r.GiftAfter && m.CardId==r.GiftCardId && m.Reason.Value=="program.xianlue.gift"));
    }
    private void ApplyZaowangSuccession(long deathId, CharacterState victim)
    {
        if (_modeDefinition.ModeKind != ContentModeKind.Identity || victim.Role != Role.Lord) return;
        // Multiple eligible heirs resolve in seat order, the normal trigger order.
        var successor = _players.Where(p => p.IsAlive && p.Role == Role.Loyalist && p.ZaowangGrants.Any(g => g.OriginalRole == Role.Loyalist))
            .OrderBy(p => p.Seat).FirstOrDefault();
        if (successor is null) return;
        victim.ExchangeIdentityWith(successor); successor.RoleRevealed = true;
        AdvanceEventRulesAndQueueFact(new IdentityExchangedEvent(deathId, victim.Seat, successor.Seat));
        AdvanceEventRulesAndQueueFact(new RoleRevealedEvent(victim.Seat, victim.Role));
        AdvanceEventRulesAndQueueFact(new RoleRevealedEvent(successor.Seat, successor.Role));
    }
    private bool ZaowangRebelDeathWins(CharacterState victim, CharacterState? killer) =>
        _modeDefinition.ModeKind == ContentModeKind.Identity && victim.Role == Role.Rebel &&
        victim.ZaowangGrants.Any(g => g.OriginalRole == Role.Rebel) && killer is { Role: Role.Lord or Role.Loyalist };
    [ProgramCapability]
    private sealed class DongZhaoProgramCapability(GameEngine engine) : IDongZhaoProgramHost
    {
        public SkillProgramStepOutcome XianlueRecord(ProgramSkillFrame f) => engine.BeginXianlueRecord(f);
        public SkillProgramStepOutcome XianlueRedistribute(ProgramSkillFrame f) => engine.BeginXianlueRedistribute(f);
        public SkillProgramStepOutcome ZaowangResolve(ProgramSkillFrame f) => engine.ArmZaowang(f);
    }
}
