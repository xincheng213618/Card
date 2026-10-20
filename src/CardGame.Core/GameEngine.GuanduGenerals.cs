using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record GuanduGeneralIssuedEvent(long FrameId, string SkillId, SkillProgramEffectOp Operation,
    int OwnerSeat, int ActualTurn, long? WindowId) : IGameEvent;
public sealed record GuanduGeneralCompletedEvent(long FrameId, string SkillId, bool Completed) : IGameEvent;
public sealed record GuanduGeneralDrawEvent(long FrameId, int Ordinal, int RecipientSeat, int Requested,
    int Actual, long Before, long After, long? AttemptId, bool Replaced) : IGameEvent;
public sealed record GuanduShicaiObtainedEvent(long FrameId, int OwnerSeat, string InstanceId, int CardId, int MovementSequence) : IGameEvent;
public sealed record GuanduDiscardFactEvent(long BatchId, int ActualTurn, int PhaseActor, long PhaseToken,
    int CardId, int MovementSequence, CardLocation From) : IGameEvent;
public sealed record GuanduJieliangPaidEvent(long FrameId, int OwnerSeat, int TargetSeat, int ActualTurn) : IGameEvent;
public sealed record GuanduHengjiangPaidEvent(long FrameId, int OwnerSeat, string InstanceId, int TargetSeat,
    int ActualTurn, long DamageFrameId, int OccurrenceIndex) : IGameEvent;
public sealed record GuanduBeizhanArmedEvent(long FrameId, int OwnerSeat, int TargetSeat, int CreatedTurn,
    CardUseEffectSource Source) : IGameEvent;
public sealed record GuanduBeizhanConsumedEvent(long ProducerFrameId, int TargetSeat, int ActualTurn, bool Restricted) : IGameEvent;
public sealed record GuanduMoushiGiftedEvent(long FrameId, int OwnerSeat, string InstanceId, int RecipientSeat,
    int CardId, int CreatedTurn) : IGameEvent;
public sealed record GuanduMoushiDrawnEvent(long ProducerFrameId, int OwnerSeat, int ActualTurn, int PhaseInstance,
    int VictimSeat, long DamageFrameId, long ActualPlayToken = 0) : IGameEvent;

public sealed record GuanduGeneralReceipt(SkillProgramEffectOp Operation, int InstructionIndex, int ActualTurn,
    long? WindowId, string Stage, int TargetSeat = -1, int CardId = 0, int Index = 0, int Count = 0,
    int DrawOrdinal = 0, int DrawRecipient = -1, int DrawRequested = 0, string? NextStage = null,
    NativeDrawOutcome? Draw = null, IReadOnlyList<int>? Seats = null, CardKind? FrozenKind = null,
    int MovementBefore = 0, int MovementAfter = 0, bool Used = false);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GuanduGeneralReceipt? GuanduGeneral { get; init; }
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class GuanduGeneralHost(GameEngine engine) : IGuanduGeneralHost
    {
        public SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation) => engine.BeginGuanduGeneral(frame, operation);
    }

    private bool GuanduSkillEnabled(CharacterState owner, string skill) => owner.IsAlive &&
        _contentRegistry.Skills.ContainsKey(skill) && EnabledSkillPrograms(owner).Any(p => p.Id == skill);
    private bool GuanduGeneralGrantQualified(CharacterState owner, SkillGrant grant)
    {
        if (grant.SkillId is not ("guandu:zezhu" or "guandu:chenggong")) return true;
        if (!IsGuanduActivity || !owner.IsAlive || !owner.SkillGrants.Grants.Any(g => g.SkillId == "guandu:fushi" &&
            g.IsEnabled && LibraryBaseGrantLive(owner, g) && IsCurrentTurnSkillGrantQualified(owner, g))) return false;
        var qun = _players.Count(p => p.IsAlive && GetEffectiveFactionId(p) == "qun");
        var wei = _players.Count(p => p.IsAlive && GetEffectiveFactionId(p) == "wei");
        return grant.SkillId == "guandu:zezhu" ? qun > wei : wei > qun;
    }
    [ProgramViewContributor(ProgramViewContribution.PrivateCards, 20)]
    private IEnumerable<CardSnapshot> GuanduShicaiPrivatelyViewedCards(int viewerSeat) => IsGuanduActivity &&
        viewerSeat == _currentSeat && _phase == TurnPhase.Play && GuanduSkillEnabled(_players[viewerSeat], "guandu:shicai") &&
        _cardZones.CardsAt(CardLocation.DrawPile).LastOrDefault() is { } top ? new[] { ToSnapshot(top) } : [];
    private bool GuanduShicaiReady(int ownerSeat, string instance)
    {
        var acquired = ProgramEventHistory<GuanduShicaiObtainedEvent>().LastOrDefault(e => e.OwnerSeat == ownerSeat && e.InstanceId == instance);
        return acquired is null || _cardMovements.Any(m => m.CardId == acquired.CardId && m.Sequence > acquired.MovementSequence &&
            m.From == CardLocation.Hand(ownerSeat) && m.To != m.From);
    }
    private IEnumerable<RuleQueryContribution> GuanduGeneralRuleContributions(CharacterState player, SkillRuleQuery query)
    {
        if (!IsGuanduActivity || query != SkillRuleQuery.DrawCount) yield break;
        var sources = _players.Count(p => p.IsAlive && GuanduAllies(p.Seat, player.Seat) &&
            p.Markers.GetValueOrDefault(PlayerMarkerKind.GuanduGrain) > 0 && GuanduSkillEnabled(p, "guandu:liangying"));
        if (sources != 0) yield return new FiniteRuleQueryContribution("guandu:liangying", SkillRuleOperation.Add, sources);
    }
    private void CaptureGuanduDiscardBatch(CardMovementBatchContext batch)
    {
        if (!IsGuanduActivity || batch.DiscardRecoveryPhase is not { Kind: ActualDiscardRecoveryPhaseKind.Discard } phase) return;
        foreach (var movement in batch.Movements)
            if (GetProgramDiscardSource(movement) is { OwnerSeat: { } owner } from && owner == phase.ActorSeat &&
                !ProgramEventHistory<GuanduDiscardFactEvent>().Any(e => e.MovementSequence == movement.Sequence))
                AdvanceEventRulesAndQueueFact(new GuanduDiscardFactEvent(batch.Id, phase.TurnNumber, owner, phase.Token,
                    movement.CardId, movement.Sequence, from));
    }
    private bool GuanduTurnDiscarded(int seat) => ProgramEventHistory<GuanduDiscardFactEvent>().Any(e => e.ActualTurn == _turnNumber && e.PhaseActor == seat);
    private Card[] GuanduOwnedCards(int seat, bool judgment = false) => (judgment
        ? GetHand(_players[seat]).Concat(GetEquipment(_players[seat])).Concat(_cardZones.CardsAt(CardLocation.Judgment(seat)))
        : GetHand(_players[seat]).Concat(GetEquipment(_players[seat]))).ToArray();
    private Card[] GuanduDiscardable(int chooser, int owner) => GuanduOwnedCards(owner).Where(c =>
        !IsSelfHandCategoryDiscardForbidden(owner, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard)).ToArray();
    private static IReadOnlyList<int> FreezeGuanduSeats(IEnumerable<int> seats) => Array.AsReadOnly(seats.ToArray());

    private SkillProgramStepOutcome BeginGuanduGeneral(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!IsGuanduActivity || f.GuanduGeneral is not null || f.InstructionIndex != 1 ||
            GetPausedPrivateOfferEffect(f)?.Op != operation)
            throw new InvalidOperationException("An activity general requires its exact unpaid owning instruction.");
        var r = new GuanduGeneralReceipt(operation, f.InstructionIndex, _turnNumber, f.WindowContext?.ParentFrameId,
            "begin", Seats: FreezeGuanduSeats([]));
        ReplaceRuntimeTop(f = f with { GuanduGeneral = r });
        AdvanceEventRulesAndQueueFact(new GuanduGeneralIssuedEvent(f.Id, f.SkillId, operation, f.OwnerSeat, _turnNumber, r.WindowId));
        RunGuanduGeneral(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool DrainGuanduGeneralChildren(long id) =>
        TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 44000)]
    private bool RunGuanduGeneral(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { GuanduGeneral: { } r } f || f.Id != id) return false;
        AssertGuanduGeneral(f);
        if (r.Stage == "draw-attempt" || r.Stage == "native-use") return true;
        if (DrainGuanduGeneralChildren(id)) return true;
        f = GetActiveProgramFrame(id); r = f.GuanduGeneral!;
        if (r.Stage == "finish") { FinishGuanduGeneral(f); return true; }
        switch (r.Operation)
        {
            case SkillProgramEffectOp.ResolveGuanduShicai: ContinueGuanduShicai(f); break;
            case SkillProgramEffectOp.ResolveGuanduZezhu: ContinueGuanduZezhu(f); break;
            case SkillProgramEffectOp.ResolveGuanduJieliang: ContinueGuanduJieliang(f); break;
            case SkillProgramEffectOp.ResolveGuanduJieliangClaim: ContinueGuanduJieliangClaim(f); break;
            case SkillProgramEffectOp.ResolveGuanduCangchu: ContinueGuanduCangchu(f); break;
            case SkillProgramEffectOp.ResolveGuanduSushou: ContinueGuanduSushou(f); break;
            case SkillProgramEffectOp.ResolveGuanduPolu: ContinueGuanduPolu(f); break;
            case SkillProgramEffectOp.ResolveGuanduThunderChariot: ContinueGuanduThunderChariot(f); break;
            case SkillProgramEffectOp.ResolveGuanduChoulue: ContinueGuanduChoulue(f); break;
            case SkillProgramEffectOp.ResolveGuanduHengjiang: ContinueGuanduHengjiang(f); break;
            case SkillProgramEffectOp.ResolveGuanduHengjiangReward: ContinueGuanduHengjiangReward(f); break;
            case SkillProgramEffectOp.ResolveGuanduXiying: ContinueGuanduXiying(f); break;
            case SkillProgramEffectOp.ResolveGuanduBeizhan: ContinueGuanduBeizhan(f); break;
            case SkillProgramEffectOp.ResolveGuanduYuanlve: ContinueGuanduYuanlve(f); break;
            case SkillProgramEffectOp.ResolveGuanduMoushi: ContinueGuanduMoushi(f); break;
            case SkillProgramEffectOp.ResolveGuanduZhongjie: ContinueGuanduZhongjie(f); break;
            case SkillProgramEffectOp.ResolveGuanduSuishiDying: DrawGuanduGeneral(f, f.OwnerSeat, 1, "finish"); break;
            case SkillProgramEffectOp.ResolveGuanduSuishiDeath:
                GuanduStage(f, "finish"); new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, f.OwnerSeat, 1); RunGuanduGeneral(id); break;
            case SkillProgramEffectOp.ResolveGuanduFuyuan:
                DrawGuanduGeneral(f, GetHand(_players[_currentSeat]).Count < GetHand(_players[f.OwnerSeat]).Count ? _currentSeat : f.OwnerSeat, 1, "finish"); break;
            default: throw new InvalidOperationException("Unknown activity general producer.");
        }
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 44000)]
    private bool ReturnGuanduGeneralMovement(ProgramSkillFrame f) => f.GuanduGeneral is not null && RunGuanduGeneral(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 44000)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 44000)]
    private bool ContinueIssuedGuanduGeneral(ProgramSkillFrame f) => f.GuanduGeneral is not null;

    private ProgramSkillFrame GuanduStage(ProgramSkillFrame f, string stage) => GuanduUpdate(f, f.GuanduGeneral! with { Stage = stage });
    private ProgramSkillFrame GuanduUpdate(ProgramSkillFrame f, GuanduGeneralReceipt r)
    { ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { GuanduGeneral = r }); return f; }
    private void DrawGuanduGeneral(ProgramSkillFrame f, int recipient, int count, string next)
    {
        f = GuanduUpdate(f, f.GuanduGeneral! with { Stage = "draw-attempt", DrawOrdinal = f.GuanduGeneral.DrawOrdinal + 1,
            DrawRecipient = recipient, DrawRequested = count, NextStage = next, Draw = null });
        BeginOperationProgramDraw(f, recipient, count, new("activity.guandu." + f.SkillId + ".draw"));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveGuanduZezhu, SkillProgramEffectOp.ResolveGuanduCangchu,
        SkillProgramEffectOp.ResolveGuanduSushou, SkillProgramEffectOp.ResolveGuanduPolu,
        SkillProgramEffectOp.ResolveGuanduHengjiangReward, SkillProgramEffectOp.ResolveGuanduBeizhan,
        SkillProgramEffectOp.ResolveGuanduYuanlve, SkillProgramEffectOp.ResolveGuanduMoushi,
        SkillProgramEffectOp.ResolveGuanduZhongjie, SkillProgramEffectOp.ResolveGuanduSuishiDying,
        SkillProgramEffectOp.ResolveGuanduFuyuan)]
    private void ReturnGuanduGeneralDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        var r = f.GuanduGeneral ?? throw new InvalidOperationException("Activity draw lost its producer.");
        if (r.Stage != "draw-attempt" || outcome.RecipientSeat != r.DrawRecipient || outcome.RequestedCount != r.DrawRequested ||
            ProgramEventHistory<GuanduGeneralDrawEvent>().Any(e => e.FrameId == f.Id && e.Ordinal == r.DrawOrdinal))
            throw new InvalidOperationException("Activity draw changed its once-paid recipient or count.");
        AdvanceEventRulesAndQueueFact(new GuanduGeneralDrawEvent(f.Id, r.DrawOrdinal, outcome.RecipientSeat,
            outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.SequenceBefore, outcome.SequenceAfter,
            outcome.AttemptFrameId, outcome.Replacement is not null));
        GuanduUpdate(f, r with { Stage = r.NextStage!, NextStage = null, Draw = outcome });
        RunGuanduGeneral(f.Id);
    }
    private void FinishGuanduGeneral(ProgramSkillFrame f, bool completed = true)
    {
        var operation = f.GuanduGeneral!.Operation;
        AdvanceEventRulesAndQueueFact(new GuanduGeneralCompletedEvent(f.Id, f.SkillId, completed));
        if (operation == SkillProgramEffectOp.ResolveGuanduChoulue && !f.ChoiceBindings.Any(b => b.Name == "choulue-kind"))
            f = f with { ChoiceBindings = Array.AsReadOnly(f.ChoiceBindings.Append(new ProgramChoiceResultBinding("choulue-kind", "none", f.OwnerSeat)).ToArray()) };
        ReplaceRuntimeTop(f = f with { GuanduGeneral = null });
        if (operation == SkillProgramEffectOp.ResolveGuanduChoulue) AdvanceRuntimeProgram(f.Id);
        else FinishProgramSkill(f, completed);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 44000)]
    private void AssertGuanduGeneral(ProgramSkillFrame f)
    {
        if (f.GuanduGeneral is not { } r) return;
        if (!IsGuanduActivity || r.InstructionIndex != f.InstructionIndex || r.InstructionIndex != 1 ||
            r.ActualTurn != _turnNumber || r.WindowId != f.WindowContext?.ParentFrameId ||
            GetPausedPrivateOfferEffect(f)?.Op != r.Operation ||
            ProgramEventHistory<GuanduGeneralIssuedEvent>().Count(e => e == new GuanduGeneralIssuedEvent(f.Id,
                f.SkillId, r.Operation, f.OwnerSeat, r.ActualTurn, r.WindowId)) != 1 ||
            r.Seats is null || r.Seats.Any(s => !IsValidPlayerSeat(s)) || r.Seats.Distinct().Count() != r.Seats.Count)
            throw new InvalidOperationException("Activity receipt lost its exact instruction, turn, participant snapshot or issuance.");
        if (r.Stage == "draw-attempt" && (r.NextStage is null || r.Draw is not null || r.DrawRequested < 0 || !IsValidPlayerSeat(r.DrawRecipient)))
            throw new InvalidOperationException("Activity draw lost its owned awaiting receipt.");
    }
}
