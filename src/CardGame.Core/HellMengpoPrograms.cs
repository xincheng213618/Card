using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IHellMengpoProgramHost
{
    SkillProgramStepOutcome Forget(ProgramSkillFrame frame);
    SkillProgramStepOutcome BanCategory(ProgramSkillFrame frame);
    SkillProgramStepOutcome Restore(ProgramSkillFrame frame);
}

internal abstract class HellMengpoOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override ISkillProgramEffectHandler Handler => new HellMengpoEffectHandler(Op);
}
internal sealed class ResolveHellWanghunLossDescriptor : HellMengpoOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveHellWanghunLoss;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Death;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.OwnerDied)];

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
}
internal sealed class BanHellWangshiCategoryDescriptor : HellMengpoOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BanHellWangshiCategory;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
internal sealed class RestoreHellWanghunSkillsDescriptor : HellMengpoOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RestoreHellWanghunSkills;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget()];
}
internal sealed class HellMengpoEffectHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => op switch
    {
        SkillProgramEffectOp.ResolveHellWanghunLoss => host.GetCapability<IHellMengpoProgramHost>().Forget(frame),
        SkillProgramEffectOp.BanHellWangshiCategory => host.GetCapability<IHellMengpoProgramHost>().BanCategory(frame),
        SkillProgramEffectOp.RestoreHellWanghunSkills => host.GetCapability<IHellMengpoProgramHost>().Restore(frame),
        _ => throw new InvalidOperationException("Unknown Mengpo operation.")
    };
}

public enum HellWanghunStage { LossChildren, InjectionChildren }
public sealed record HellWanghunReceipt(int InstructionIndex, long DeathFrameId, long DeathWindowId, HellWanghunStage Stage,
    int LostSkillCount, int LostGrantCount, int InjectedCards = 0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HellWanghunReceipt? HellWanghun { get; init; }
}

// Every removed grant has its own immutable fact. These are durable lost
// ownership receipts, not an engine-side pending restore list.
public sealed record HellWanghunTargetChosenEvent(long FrameId, int MengpoSeat, int TargetSeat, string? SkillId) : IGameEvent;
public sealed record HellWanghunGrantLostEvent(long FrameId, int MengpoSeat, int TargetSeat,
    string GeneralId, string? SecondaryGeneralId, SkillGrant Grant) : IGameEvent;
public sealed record HellWanghunIssuedEvent(long FrameId, long DeathFrameId, int MengpoSeat,
    int LostSkillCount, int LostGrantCount) : IGameEvent;
public sealed record HellReturnSoulInjectedEvent(long FrameId, int MengpoSeat, int CardId) : IGameEvent;
public sealed record HellWanghunCompletedEvent(long FrameId, int LostSkillCount, int InjectedCards) : IGameEvent;
public sealed record HellWangshiCategoryBanEvent(long FrameId, int MengpoSeat, int TurnNumber,
    int TurnOwnerSeat, string Category) : IGameEvent;

public sealed record HellShiyouRedirectReceipt(ActualDiscardRecoveryPhaseKey Phase, CardConversionSource Source,
    string GameplayHash, int CardId, CardKind CardKind, Suit EffectiveSuit,
    CardLocation OriginalSource, CardLocation ActualFrom, CardLocation Destination, CardMoveReason Reason);
internal sealed record HellShiyouTransferPlan
{
    internal long PlanId { get; init; }
    internal IReadOnlyList<CardTransfer> Transfers { get; init; } = Array.AsReadOnly(Array.Empty<CardTransfer>());
    internal IReadOnlyList<HellShiyouRedirectReceipt> Redirects { get; init; } = Array.AsReadOnly(Array.Empty<HellShiyouRedirectReceipt>());
}
public sealed record HellShiyouInterceptedEvent(long PlanId, long MovementSequence,
    HellShiyouRedirectReceipt Receipt) : IGameEvent;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class HellMengpoProgramHost(GameEngine engine) : IHellMengpoProgramHost
    {
        public SkillProgramStepOutcome Forget(ProgramSkillFrame frame) => engine.BeginHellWanghun(frame);
        public SkillProgramStepOutcome BanCategory(ProgramSkillFrame frame) => engine.IssueHellWangshiBan(frame);
        public SkillProgramStepOutcome Restore(ProgramSkillFrame frame) => engine.BeginHellSoulRestoration(frame);
    }

    private bool HellEnemies(int first, int second) => IsValidPlayerSeat(first) && IsValidPlayerSeat(second) &&
        _players[first].TeamId is { } team && _players[second].TeamId is { } other && team != other;

    private IReadOnlyList<SkillGrant> HellWanghunEligibleGrants(CharacterState player) => Array.AsReadOnly(
        GetSkillBindingShard(player).ActiveGrants.Where(grant => grant.IsEnabled && !grant.IsModeRule &&
            grant.SkillId is not ("hell:player-tier" or "hell:boss-tier" or "hell:return-soul-resolution") &&
            !grant.SourceId.StartsWith("equipment:", StringComparison.Ordinal) &&
            grant.LordProjection is null && grant.GeneralLibraryProjection is null &&
            !_contentRegistry.GetSkill(grant.SkillId).Tags.HasFlag(SkillTag.Awakening))
            .OrderBy(grant => grant.SkillId, StringComparer.Ordinal).ThenBy(grant => grant.GrantId, StringComparer.Ordinal).ToArray());

    private SkillProgramStepOutcome BeginHellWanghun(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.SkillId != "hell:wanghun" || frame.TriggerId != "forget-two-enemies" || frame.InstructionIndex != 1 ||
            frame.HellWanghun is not null || frame.WindowContext is not { Window: SkillProgramTriggerWindow.OwnerDied } context ||
            _players[frame.OwnerSeat].IsAlive || _resolutionStack.OfType<DeathFrame>().LastOrDefault() is not { } death ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not ProgramDeathTriggerWindowFrame window ||
            death.VictimSeat != frame.OwnerSeat || context.ParentFrameId != window.Id || window.DeathFrameId != death.Id ||
            window.OwnerSeat != frame.OwnerSeat ||
            ProgramEventHistory<HellWanghunIssuedEvent>().Any(e => e.FrameId == frame.Id))
            throw new InvalidOperationException("Wanghun requires its exact real Mengpo death and once-issued instruction.");
        var targets = _players.Where(p => p.IsAlive && HellEnemies(frame.OwnerSeat, p.Seat)).Select(p => p.Seat).ToList();
        _random.Shuffle(targets);
        var picked = targets.Take(2).Select(seat =>
        {
            var grants = HellWanghunEligibleGrants(_players[seat]);
            var ids = grants.Select(g => g.SkillId).Distinct(StringComparer.Ordinal).ToArray();
            var skill = ids.Length == 0 ? null : ids[_random.Next(ids.Length)];
            return (Seat: seat, Skill: skill, Grants: grants.Where(g => g.SkillId == skill).ToArray());
        }).ToArray();
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        var skills = picked.Count(p => p.Skill is not null);
        var removed = picked.Sum(p => p.Grants.Length);
        ReplaceRuntimeTop(frame = frame with { HellWanghun = new(frame.InstructionIndex, death.Id, window.Id,
            HellWanghunStage.LossChildren, skills, removed) });
        AdvanceEventRulesAndQueueFact(new HellWanghunIssuedEvent(frame.Id, death.Id, frame.OwnerSeat, skills, removed));
        foreach (var choice in picked)
        {
            var target = _players[choice.Seat];
            AdvanceEventRulesAndQueueFact(new HellWanghunTargetChosenEvent(frame.Id, frame.OwnerSeat, target.Seat, choice.Skill));
            foreach (var grant in choice.Grants)
            {
                if (!target.SkillGrants.RemoveGrant(grant.GrantId))
                    throw new InvalidOperationException("Wanghun changed a frozen original grant before loss.");
                AdvanceEventRulesAndQueueFact(new HellWanghunGrantLostEvent(frame.Id, frame.OwnerSeat, target.Seat,
                    target.General.Id, target.SecondaryGeneral?.Id, grant));
            }
        }
        ContinueHellWanghun(GetActiveProgramFrame(frame.Id));
        return SkillProgramStepOutcome.AwaitChild;
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 40051)]
    private bool ResumeHellWanghun(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { HellWanghun: not null } frame || frame.Id != id) return false;
        ContinueHellWanghun(frame); return true;
    }
    private void ContinueHellWanghun(ProgramSkillFrame frame)
    {
        AssertHellWanghun(frame);
        if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.Program) ||
            TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.Program) ||
            TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id)) return;
        frame = GetActiveProgramFrame(frame.Id);
        var receipt = frame.HellWanghun!;
        if (receipt.Stage == HellWanghunStage.LossChildren)
        {
            ReplaceRuntimeTop(frame = frame with { HellWanghun = receipt with { Stage = HellWanghunStage.InjectionChildren } });
            var oldOrder = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().Select(c => c.Id).ToList();
            for (var ordinal = 0; ordinal < 2; ordinal++)
            {
                var id = _cardZones.CreateDiagnostics().Select(c => c.CardId).DefaultIfEmpty().Max() + 1;
                var card = new Card(id, CardKind.ReturnSoul, Suit.None, 0);
                _cardZones.AddGeneratedCard(card); _generatedPhysicalCardIds.Add(id);
                MoveCard(card, CardLocation.OutsideGame, CardLocation.DrawPile, new("activity.hell.return-soul-injected"));
                oldOrder.Insert(_random.Next(oldOrder.Count + 1), id);
                frame = GetActiveProgramFrame(frame.Id);
                ReplaceRuntimeTop(frame = frame with { HellWanghun = frame.HellWanghun! with { InjectedCards = ordinal + 1 } });
                AdvanceEventRulesAndQueueFact(new HellReturnSoulInjectedEvent(frame.Id, frame.OwnerSeat, id));
            }
            var currentOrder = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().Select(c => c.Id).ToArray();
            _cardZones.ReorderDrawPileTop(currentOrder, oldOrder, []);
            ContinueHellWanghun(GetActiveProgramFrame(frame.Id)); return;
        }
        AdvanceEventRulesAndQueueFact(new HellWanghunCompletedEvent(frame.Id, receipt.LostSkillCount, receipt.InjectedCards));
        ReplaceRuntimeTop(frame = frame with { HellWanghun = null });
        FinishProgramSkill(frame, true);
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 40051)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 40051)]
    private bool CanContinueHellWanghun(ProgramSkillFrame frame)
    {
        if (frame.HellWanghun is null) return false;
        AssertHellWanghun(frame); return true;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 40051)]
    private void AssertHellWanghun(ProgramSkillFrame frame)
    {
        if (frame.HellWanghun is not { } receipt) return;
        if (frame.SkillId != "hell:wanghun" || frame.TriggerId != "forget-two-enemies" || frame.InstructionIndex != receipt.InstructionIndex ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.OwnerDied } context || context.ParentFrameId != receipt.DeathWindowId ||
            !_resolutionStack.OfType<ProgramDeathTriggerWindowFrame>().Any(window => window.Id == receipt.DeathWindowId &&
                window.DeathFrameId == receipt.DeathFrameId && window.OwnerSeat == frame.OwnerSeat) ||
            !Enum.IsDefined(receipt.Stage) || receipt.LostSkillCount is < 0 or > 2 || receipt.LostGrantCount < receipt.LostSkillCount ||
            receipt.InjectedCards is < 0 or > 2 || ProgramEventHistory<HellWanghunIssuedEvent>().Count(e =>
                e.FrameId == frame.Id && e.DeathFrameId == receipt.DeathFrameId && e.MengpoSeat == frame.OwnerSeat &&
                e.LostSkillCount == receipt.LostSkillCount && e.LostGrantCount == receipt.LostGrantCount) != 1)
            throw new InvalidOperationException("Wanghun lost its exact death, frozen loss or injection receipt.");
    }

    private SkillProgramStepOutcome IssueHellWangshiBan(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.SkillId != "hell:wangshi" || frame.TriggerId != "forbid-enemy-category" || frame.InstructionIndex != 1 ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SourceSeat: { } seat } context ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().LastOrDefault() is not { } parent || parent.Id != context.ParentFrameId ||
            parent.Window != context.Window || parent.OwnerSeat != seat || seat != _currentSeat ||
            _turnProgression.OwnerSeat != seat || _turnProgression.TurnNumber != _turnNumber)
            throw new InvalidOperationException("Wangshi requires the exact real turn-start owner.");
        if (!_players[frame.OwnerSeat].IsAlive || !_players[seat].IsAlive || !HellEnemies(frame.OwnerSeat, seat))
            return SkillProgramStepOutcome.Continue;
        if (ProgramEventHistory<HellWangshiCategoryBanEvent>().Any(e => e.FrameId == frame.Id))
            throw new InvalidOperationException("Wangshi cannot choose a different category after its actual issuance.");
        var category = new[] { "basic", "trick", "equipment" }[_random.Next(3)];
        AdvanceEventRulesAndQueueFact(new HellWangshiCategoryBanEvent(frame.Id, frame.OwnerSeat, _turnNumber, seat, category));
        var categoryName = category switch { "basic" => "基本牌", "trick" => "锦囊牌", _ => "装备牌" };
        AddLog("SkillTriggered", $"{_players[frame.OwnerSeat].General.Name}发动【往事】：{_players[seat].Name}本回合不能使用或打出{categoryName}。", frame.OwnerSeat, seat);
        return SkillProgramStepOutcome.Continue;
    }
    private bool IsHellWangshiActionForbidden(int actorSeat, CardKind kind, CardActionType type)
    {
        if (type is not (CardActionType.Use or CardActionType.Response) ||
            !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.BanHellWangshiCategory)) return false;
        var category = CardUseCategoryCatalog.Get(kind) switch
        { CardUseCategories.Basic => "basic", CardUseCategories.Equipment => "equipment", _ => "trick" };
        var history = CompleteProgramEventHistory().ToArray();
        return history.OfType<HellWangshiCategoryBanEvent>().Any(e => e.TurnNumber == _turnNumber &&
            e.TurnOwnerSeat == actorSeat && e.TurnOwnerSeat == _turnProgression.OwnerSeat && e.Category == category &&
            !history.OfType<TurnEndedEvent>().Any(end => end.TurnNumber == e.TurnNumber && end.ActorSeat == e.TurnOwnerSeat));
    }

    private bool HasHellShiyouCapability => _contentRegistry.ProgramDependencies.HasCardPolicy(SkillProgramCardPolicyKind.HellShiyouIntercept);
    private CardLocation? HellShiyouOriginalSource(CardTransfer transfer, CardMoveReason reason)
    {
        if (transfer.To != CardLocation.DiscardPile) return null;
        if (IsOwnedDiscardSource(transfer.From)) return IsProgramDiscardOriginReason(reason) ? transfer.From : null;
        if (transfer.From != CardLocation.Processing) return null;
        var entry = _cardMovements.LastOrDefault(m => m.CardId == transfer.CardId);
        if (entry is null || entry.To != CardLocation.Processing || !IsOwnedDiscardSource(entry.From) || !IsProgramDiscardOriginReason(entry.Reason)) return null;
        return reason == entry.Reason || reason.Value == entry.Reason.Value + "-finished" || reason.Value == entry.Reason.Value + "-discard" ? entry.From : null;
    }
    private HellShiyouTransferPlan PrepareHellShiyouDiscardTransfers(IReadOnlyList<CardTransfer> input, CardMoveReason reason)
    {
        var transfers = input.ToArray();
        if (!HasHellShiyouCapability || !_setupComplete || _winner != Winner.None ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Discard } phase)
            return new() { Transfers = Array.AsReadOnly(transfers) };
        var redirects = new List<HellShiyouRedirectReceipt>();
        foreach (var owner in _players.Where(p => p.IsAlive && !p.IsHuman && p.Seat != phase.ActorSeat)
            .OrderBy(p => RelativeTableOrder(_currentSeat, p.Seat)))
        {
            var policy = CardPolicies(owner, SkillProgramCardPolicyKind.HellShiyouIntercept).FirstOrDefault();
            if (policy.Source is null) continue;
            var eligible = transfers.Select((transfer, index) => (Transfer: transfer, Index: index,
                    Original: HellShiyouOriginalSource(transfer, reason)))
                .Where(p => p.Original is { OwnerSeat: var source } && source == phase.ActorSeat)
                .Select(p => (p.Transfer, p.Index, Original: p.Original!.Value,
                    Card: GetAttackCard(p.Transfer.CardId)))
                .GroupBy(p => EffectiveSuit(_players[phase.ActorSeat], p.Card))
                .Select(g => g.OrderByDescending(p => CardCatalog.Get(p.Card.Kind).HandKeepValue)
                    .ThenBy(p => p.Card.Id).First()).OrderBy(p => p.Index).ToArray();
            foreach (var item in eligible)
            {
                var destination = CardLocation.Hand(owner.Seat);
                redirects.Add(new(phase, new(policy.Source.SkillId, policy.Policy.Id, owner.Seat, policy.Source.SkillInstanceId),
                    policy.Source.Program.GameplayHash, item.Card.Id, item.Card.Kind, EffectiveSuit(_players[phase.ActorSeat], item.Card),
                    item.Original, item.Transfer.From, destination, reason));
                transfers[item.Index] = item.Transfer with { To = destination };
            }
        }
        return new() { PlanId = redirects.Count == 0 ? 0 : ++_resolutionSequence,
            Transfers = Array.AsReadOnly(transfers), Redirects = Array.AsReadOnly(redirects.ToArray()) };
    }
    private void CompleteHellShiyouDiscardTransfers(HellShiyouTransferPlan plan, IReadOnlyList<CardMovementRecord> movements)
    {
        foreach (var receipt in plan.Redirects)
        {
            var actual = movements.Where(m => m.CardId == receipt.CardId && m.CardKind == receipt.CardKind &&
                m.From == receipt.ActualFrom && m.To == receipt.Destination && m.Reason == receipt.Reason).ToArray();
            if (plan.PlanId <= 0 || actual is not [var move] || receipt.Destination != CardLocation.Hand(receipt.Source.OwnerSeat) ||
                receipt.OriginalSource.OwnerSeat != receipt.Phase.ActorSeat ||
                ProgramEventHistory<HellShiyouInterceptedEvent>().Any(e => e.PlanId == plan.PlanId && e.Receipt.CardId == receipt.CardId))
                throw new InvalidOperationException("Shiyou lost its exact pre-discard redirection and native movement.");
            AdvanceEventRulesAndQueueFact(new HellShiyouInterceptedEvent(plan.PlanId, move.Sequence, receipt));
        }
    }
    private bool IsHellShiyouRedirectedDiscard(CardMovementRecord move) =>
        ProgramEventHistory<HellShiyouInterceptedEvent>().Any(e => e.MovementSequence == move.Sequence &&
            e.Receipt.CardId == move.CardId && e.Receipt.CardKind == move.CardKind && e.Receipt.ActualFrom == move.From &&
            e.Receipt.Destination == move.To && e.Receipt.Reason == move.Reason &&
            move.To == CardLocation.Hand(e.Receipt.Source.OwnerSeat) && e.Receipt.OriginalSource.OwnerSeat == e.Receipt.Phase.ActorSeat &&
            e.Receipt.Phase.Kind == ActualDiscardRecoveryPhaseKind.Discard && IsRecordedActualDiscardRecoveryPhase(e.Receipt.Phase));
    // Payment and actual discard are deliberately different queries. A paid
    // cost still left its original source, but Shiyou's gained material never
    // contributes to logical discard counts or discard-pile opportunities.
    private bool IsHellShiyouPaidDiscardMovement(CardMovementRecord move, CardLocation expectedSource, CardMoveReason expectedReason) =>
        move.Reason == expectedReason && IsHellShiyouRedirectedDiscard(move) &&
        ProgramEventHistory<HellShiyouInterceptedEvent>().Any(e => e.MovementSequence == move.Sequence &&
            e.Receipt.CardId == move.CardId && e.Receipt.OriginalSource == expectedSource && e.Receipt.Reason == expectedReason);

    [ProgramCardLegality(ProgramCardLegalityStage.BeforeTurnUseBan, 30)]
    private bool HellWangshiCardUseForbidden(ProgramCardUseQuery query) =>
        IsHellWangshiActionForbidden(query.ActorSeat, query.Kind, query.ActionType);
}
