using System.Text.Json.Serialization;

namespace CardGame.Core;

// The issuing card retains its own entity and target cursor. The ordinary
// program executor owns the nested request, every payment and native Slash.
public sealed record ShenwuTransplantProgramReturn(long CardUseFrameId, long ActionId,
    int TargetIndex, int ActorSeat);
public sealed record ShenwuTransplantResolution(long ProgramFrameId, long ActionId,
    int TargetIndex, int ActorSeat);
public sealed record ShenwuTransplantResolvedEvent(long CardUseFrameId, int SourceSeat,
    int TargetSeat, bool UsedSlash, bool Completed) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShenwuTransplantProgramReturn? ShenwuTransplantReturn { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShenwuTransplantResolution? ShenwuTransplant { get; init; }
}

public sealed partial class GameEngine
{
    private const string ShenwuTransplantSkill = "shenwu:transplant-wood-resolution";
    private static string ShenwuTransplantGrant(long id) => $"card-use:shenwu-transplant:{id}";
    private string ShenwuTransplantSource(long id) => $"turn:{_turnNumber}:{_currentSeat}:card-use:shenwu-transplant:{id}";

    private void AddShenwuTrickActions(List<LegalAction> actions, CharacterState actor,
        IEnumerable<Card> playableCards)
    {
        foreach (var card in playableCards.Where(card => card.Kind is CardKind.SowBeans or CardKind.TransplantWood))
        {
            if (IsCardUseForbidden(actor.Seat, card.Kind, CardActionType.Use)) continue;
            if (card.Kind == CardKind.SowBeans)
            {
                if (!IsDirectedCardTargetProhibited(actor.Seat, actor.Seat, card.Kind) &&
                    !IsCardTargetProhibited(actor, card.Kind, card.Suit, SuitColor(EffectiveSuit(actor, card))))
                    actions.Add(new(LegalActionKind.SowBeans, card.Id, null, "使用【撒豆成兵】"));
                continue;
            }
            foreach (var target in _players.Where(target => target.IsAlive && target.Seat != actor.Seat &&
                GetHand(target).Count + GetEquipment(target).Count != 0 &&
                !IsDirectedCardTargetProhibited(actor.Seat, target.Seat, card.Kind) &&
                !IsCardTargetProhibited(target, card.Kind, card.Suit, SuitColor(EffectiveSuit(actor, card))) &&
                !HasBeneficiarySuitShield(actor.Seat, target.Seat, EffectiveSuit(actor, card))))
                actions.Add(new(LegalActionKind.TransplantWood, card.Id, target.Seat,
                    $"对 {target.Name} 使用【移花接木】"));
        }
    }

    private void ResolveShenwuSowBeans(CharacterState source, Card card)
    {
        RequireShenwuTrickAction(source, card, LegalActionKind.SowBeans, null);
        var id = BeginCardUse(card, source.Seat, [source.Seat]);
        MoveCard(card, FindOwnedCardLocation(source, card), CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        BeginJizhiOrNullificationWindow(id, card, source.Seat, [source.Seat], LegalActionKind.SowBeans);
    }
    private void ResolveShenwuTransplantWood(CharacterState source, CharacterState target, Card card)
    {
        RequireShenwuTrickAction(source, card, LegalActionKind.TransplantWood, target.Seat);
        var id = BeginCardUse(card, source.Seat, [target.Seat]);
        MoveCard(card, FindOwnedCardLocation(source, card), CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        BeginJizhiOrNullificationWindow(id, card, source.Seat, [target.Seat], LegalActionKind.TransplantWood);
    }
    private void RequireShenwuTrickAction(CharacterState source, Card card, LegalActionKind kind, int? target)
    {
        if (!BuildLegalActions(source, selectedPhysicalCardId: card.Id).Any(action =>
                action.Kind == kind && action.CardId == card.Id && action.TargetSeat == target))
            throw new InvalidOperationException("A Shenwu trick lost its legal original entity and target.");
    }
    private void ResolveShenwuSowBeansEffect(NullificationWindowFrame pending)
    {
        var use = LifecycleCardUse(pending.ParentFrameId) ?? throw new InvalidOperationException("Sow beans lost its native owner.");
        SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        var seat = GetNullificationTargetSeat(pending) ?? use.SourceSeat;
        var target = _players[seat];
        if (target.IsAlive)
        {
            var limit = Math.Min(5, target.MaxHp);
            var count = GetEffectiveFactionId(target) == "god" ? limit : Math.Max(0, limit - GetHand(target).Count);
            DrawCards(target, count, log: true);
        }
        FinishCardUse(use.Id, GetNullificationEffectCard(pending), CardKind.SowBeans);
    }
    private void ResolveShenwuTransplantWoodEffect(NullificationWindowFrame pending)
    {
        var use = LifecycleCardUse(pending.ParentFrameId) ?? throw new InvalidOperationException("Transplant wood lost its native owner.");
        var actor = GetNullificationTargetSeat(pending) ?? throw new InvalidOperationException("Transplant wood lost its exact target.");
        if (!IsValidPlayerSeat(actor) || actor == use.SourceSeat || use.ShenwuTransplant is not null || use.Action is null)
            throw new InvalidOperationException("Transplant wood changed its native target or issuance.");
        SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        if (!_players[actor].IsAlive || !_players[use.SourceSeat].IsAlive ||
            GetHand(_players[actor]).Count + GetEquipment(_players[actor]).Count == 0)
        { FinishCardUse(use.Id, GetNullificationEffectCard(pending), CardKind.TransplantWood); return; }
        var program = _contentRegistry.GetSkill(ShenwuTransplantSkill).Program ??
            throw new InvalidOperationException("Transplant wood requires its registered request program.");
        var grant = ShenwuTransplantGrant(use.Id);
        _players[use.SourceSeat].SkillGrants.Grant(new(grant, ShenwuTransplantSkill, grant, ShenwuTransplantSource(use.Id),
            TurnExpiry: new(_turnNumber, _currentSeat)));
        var frameId = ++_resolutionSequence;
        UpdateLifecycleCardUse(use.Id, current => current with
        { ShenwuTransplant = new(frameId, use.Action.ActionId, use.TargetIndex, actor) });
        var frame = new ProgramSkillFrame(frameId, use.SourceSeat, ShenwuTransplantSkill,
            "card-effect", program.GameplayHash, 0, [], Array.AsReadOnly(new[] { actor }))
        {
            SkillInstanceId = grant,
            ShenwuTransplantReturn = new(use.Id, use.Action.ActionId, use.TargetIndex, actor)
        };
        PushRuntimeFrame(frame);
        AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.ActivationId));
        AdvanceRuntimeProgram(frame.Id);
    }

    // This internal activation cannot be voluntarily invoked, even if a foreign
    // skill-copy effect were to expose its definition or a checkpoint is forged.
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 27800,
        SkillProgramEffectOp.RequestSlashAgainstChosenTarget)]
    private bool AllowsShenwuTransplantActivation(ProgramActivationLegalityContext gate) =>
        gate.SkillId != ShenwuTransplantSkill;

    private bool IsShenwuTransplantProgram(ProgramSkillFrame frame)
    {
        if (frame.ShenwuTransplantReturn is not { } receipt || frame.SkillId != ShenwuTransplantSkill ||
            frame.TriggerId is not null || frame.ActivationId != "card-effect" ||
            frame.SelectedCardIds.Count != 0 || !frame.SelectedTargetSeats.SequenceEqual(new[] { receipt.ActorSeat }) ||
            frame.SkillInstanceId != ShenwuTransplantGrant(receipt.CardUseFrameId) ||
            LifecycleCardUse(receipt.CardUseFrameId) is not { Step: ResolutionFrameStep.ResolvingEffect,
                ShenwuTransplant: { } state, Action: { } action } use || LeChengyanNativeEffectKind(use) != CardKind.TransplantWood ||
            state.ProgramFrameId != frame.Id || state.ActionId != action.ActionId || state.ActionId != receipt.ActionId ||
            state.TargetIndex != receipt.TargetIndex || use.TargetIndex != receipt.TargetIndex ||
            state.ActorSeat != receipt.ActorSeat || use.TargetIndex < 0 || use.TargetIndex >= LeChengyanNativeTargets(use).Count ||
            LeChengyanNativeTargets(use)[use.TargetIndex] != receipt.ActorSeat || frame.OwnerSeat != use.SourceSeat ||
            frame.GameplayHash != _contentRegistry.GetSkill(ShenwuTransplantSkill).Program!.GameplayHash)
            return false;
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        return index > 0 && _resolutionStack[index - 1].Id == use.Id &&
            _players[frame.OwnerSeat].SkillGrants.Grants.Any(grant => grant.GrantId == frame.SkillInstanceId &&
                grant.SkillId == frame.SkillId && grant.SkillInstanceId == frame.SkillInstanceId &&
                grant.SourceId == ShenwuTransplantSource(receipt.CardUseFrameId) &&
                grant.TurnExpiry == new SkillGrantTurnExpiry(_turnNumber, _currentSeat));
    }
    private bool TryCompleteShenwuTransplantProgram(ProgramSkillFrame frame, bool completed)
    {
        if (frame.ShenwuTransplantReturn is not { } receipt) return false;
        if (_resolutionStack.LastOrDefault()?.Id != frame.Id || !IsShenwuTransplantProgram(frame))
            throw new InvalidOperationException("Transplant wood lost its exact issued program return.");
        var usedSlash = frame.ChoiceBindings.SingleOrDefault(binding => binding.Name == "slash-result")?.OptionId == "used-slash";
        AdvanceEventRulesAndQueueFact(new ProgramSkillResolvedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.ActivationId, completed));
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
        _players[frame.OwnerSeat].SkillGrants.RemoveGrant(frame.SkillInstanceId);
        var use = LifecycleCardUse(receipt.CardUseFrameId)!;
        UpdateLifecycleCardUse(use.Id, current => current with { ShenwuTransplant = null });
        AdvanceEventRulesAndQueueFact(new ShenwuTransplantResolvedEvent(use.Id, use.SourceSeat,
            receipt.ActorSeat, usedSlash, completed));
        FinishCardUse(use.Id, GetTrickRepresentation(use.Id, use.CardId, requireProcessing: true), CardKind.TransplantWood);
        return true;
    }
    private bool ShenwuTrickFrameRidesOn(ResolutionFrame ride, ResolutionFrame beneath) =>
        ride is ProgramSkillFrame { ShenwuTransplantReturn: { } receipt } frame &&
        receipt.CardUseFrameId == beneath.Id && IsShenwuTransplantProgram(frame);
    private bool HasShenwuTrickPrompt() => _resolutionStack.OfType<ProgramSkillFrame>()
        .Any(frame => frame.ShenwuTransplantReturn is not null && IsShenwuTransplantProgram(frame));

    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 27800)]
    private bool TryAdvanceShenwuTransplantAi()
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AssistedSlashRequest: { } request } frame ||
            !IsShenwuTransplantProgram(frame) || _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != request.ActorSeat || _players[decision.PlayerSeat].IsHuman) return false;
        var actor = _players[request.ActorSeat];
        bool Enemy(int seat) => actor.TeamId is { } team && _players[seat].TeamId is { } other && team != other;
        PromptChoice selected;
        if (request.TargetSeat is null)
            selected = decision.Choices.Where(choice => choice.Parameters.GetValueOrDefault("request-option") == "target")
                .OrderBy(choice => Enemy(choice.Targets.Single()) ? 0 : 1)
                .ThenBy(choice => _players[choice.Targets.Single()].Hp).ThenBy(choice => choice.Targets.Single()).First();
        else if (actor.TeamId is not null && !Enemy(request.TargetSeat.Value))
            selected = decision.Choices.Single(choice => choice.Parameters.GetValueOrDefault("request-option") == "decline");
        else selected = decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("request-option") != "decline") ??
            decision.Choices.Single(choice => choice.Parameters.GetValueOrDefault("request-option") == "decline");
        ProgramInteractions.Resolve(selected);
        AdvanceRulesAndPublishState();
        return true;
    }

    private bool IsShenwuTransplantAttackConsistent(CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        var frame = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(frame =>
            frame.ShenwuTransplantReturn is not null && frame.Id == attack.ProgramSkillCardUseFrameId);
        if (frame is null || !IsShenwuTransplantProgram(frame) ||
            LifecycleCardUse(attack.ResolutionId) is not { Action: { Type: CardActionType.Use } action } child ||
            !IsSlashCard(action.EffectiveKind) || action.ActorSeat != frame.SelectedTargetSeats[0] ||
            child.SourceSeat != action.ActorSeat || attack.CardUserSeat != action.ActorSeat ||
            action.ParentActionId != LifecycleCardUse(frame.ShenwuTransplantReturn!.CardUseFrameId)!.Action!.ActionId)
            return false;
        var parent = LifecycleCardUse(frame.ShenwuTransplantReturn!.CardUseFrameId)!;
        var parentIds = parent.Action!.PhysicalCards.Select(card => card.CardId).ToHashSet();
        if (!processing.Any(card => parentIds.Contains(card.Id))) return false;
        // The pre-existing checker validates the same physical or zero-entity
        // child against every native response/transfer path after removing only
        // the exact retained parent trick entity from its expected Processing.
        // Its absence is also the recursion sentinel; a previously claimed
        // parent needs no adjustment and follows the unmodified native checker.
        return IsActiveAttackCardConsistent(attack, processing.Where(card => !parentIds.Contains(card.Id)).ToArray());
    }

    private void AssertShenwuCardState()
    {
        foreach (var frame in _resolutionStack.OfType<ProgramSkillFrame>().Where(frame => frame.ShenwuTransplantReturn is not null))
            if (!IsShenwuTransplantProgram(frame))
                throw new InvalidOperationException("Transplant wood changed its exact native program owner.");
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(use => use.ShenwuTransplant is not null))
        {
            _ = GetTrickRepresentation(use.Id, use.CardId, requireProcessing: true);
            if (use.Action is null || use.Action.PhysicalCards.Any(card =>
                    !IsClaimedUseCardEntity(use.Id, card.CardId) &&
                    !(IsHuiyunOutsideGeneralWeaponMaterial(use.Id, card.CardId) || IsSiruiOutsideGeneralWeaponMaterial(use.Id, card.CardId)) && !IsGuanduDestroyedChariotMaterial(use.Id, card.CardId) &&
                    _cardZones.GetLocation(card.CardId) != CardLocation.Processing) ||
                !_resolutionStack.OfType<ProgramSkillFrame>().Any(frame =>
                    frame.Id == use.ShenwuTransplant!.ProgramFrameId && IsShenwuTransplantProgram(frame)))
                throw new InvalidOperationException("Transplant wood lost its frozen native request child.");
        }
    }
}
