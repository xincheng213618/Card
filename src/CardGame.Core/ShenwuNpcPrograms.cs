namespace CardGame.Core;

internal interface IShenwuNpcProgramHost
{
    SkillProgramStepOutcome ExecuteShenwuNpc(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class ShenwuMingwanCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "shenwu:mingwan";
    public void ValidateTrigger(SkillProgramTrigger t)
    {
        if (t.Id != "draw-for-later-real-use" || t.Window != SkillProgramTriggerWindow.CardUseCommitted ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || t.Optional || !t.IncludeResponseUses ||
            t.Subject is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.Priority != 0 ||
            t.UsageScope is not null || t.UsageLimit is not null || t.DynamicUsageLimit is not null ||
            t.NamedUsageGroup is not null || t.MarkerCost is not null || t.ChoiceGroup is not null || t.OrderGroup is not null ||
            t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 ||
            t.Suits.Count != 0 || t.DamageCardKinds.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 ||
            t.ExcludedReasons.Count != 0 || t.JudgmentReasons.Count != 0 || t.JudgmentSource is not null ||
            t.DamageOccurrence is not null || t.MovementOccurrence is not null || t.RequireDamageSource is not null ||
            t.RequireNoCardConversion is not null || t.GainPhaseQualification is not null || t.AllowNoEventTarget ||
            t.NoDyingAtActivation || t.DeferredTurnEndOnly || t.OnlyDesignatedCardTargets || t.AllowOwnDiscardPhaseEnded ||
            t.EvaluateConditionAtResolution || t.IgnoreOwnSkillMovements || t.MovementDiscardOnly || t.SingleActionInstance ||
            t.Effects is not [{ Op: SkillProgramEffectOp.DrawShenwuMingwanUse, Target: SkillProgramEffectTarget.Owner,
                Amount: 0, Condition.Kind: SkillProgramConditionKind.Always, NumberExpression: null, SourceBind: null,
                ResultBind: null, ActorReference: null, TargetReference: null }])
            throw new InvalidOperationException("Mingwan requires one exact forced actor committed-Use binding, including actual response Uses, with native own-turn qualification.");
    }
}

internal abstract class ShenwuNpcOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new ShenwuNpcEffectHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
}
internal abstract class ShenwuNpcBindingDescriptor : ShenwuNpcOperationDescriptor
{
    protected abstract IReadOnlyList<string> Choices { get; }
    protected abstract SkillProgramTriggerWindow Window { get; }
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "resultBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), resultBind: r.RequiredIdentifier("resultBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Window), new SelectSingleTarget(), new CreateChoiceResult(effect.ResultBind!, Choices)];
}
internal sealed class DrawShenwuWuzangReplacementDescriptor : ShenwuNpcOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawShenwuWuzangReplacement;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting)];
}
internal sealed class DrawShenwuCangmuReplacementDescriptor : ShenwuNpcOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawShenwuCangmuReplacement;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting)];
}
internal sealed class ResolveShenwuYinzeiDescriptor : ShenwuNpcOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShenwuYinzei;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class BindShenwuOtherDamageSourceDescriptor : ShenwuNpcBindingDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindShenwuOtherDamageSource;
    protected override IReadOnlyList<string> Choices => ["eligible", "other"];
    protected override SkillProgramTriggerWindow Window => SkillProgramTriggerWindow.AfterDamageApplied;
}
internal sealed class BindShenwuUndamagingTurnOwnerDescriptor : ShenwuNpcBindingDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindShenwuUndamagingTurnOwner;
    protected override IReadOnlyList<string> Choices => ["eligible", "other"];
    protected override SkillProgramTriggerWindow Window => SkillProgramTriggerWindow.AfterTurnEnded;
}
internal sealed class RecordShenwuMingwanVictimDescriptor : ShenwuNpcOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordShenwuMingwanVictim;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class DrawShenwuMingwanUseDescriptor : ShenwuNpcOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawShenwuMingwanUse;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)];
}
internal sealed class PreventShenwuNitaiOwnTurnDamageDescriptor : ShenwuNpcOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PreventShenwuNitaiOwnTurnDamage;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}
internal sealed class LoseShenwuTanyuHpDescriptor : ShenwuNpcOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.LoseShenwuTanyuHp;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class BindShenwuRecoveryBeneficiaryDescriptor : ShenwuNpcBindingDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindShenwuRecoveryBeneficiary;
    protected override IReadOnlyList<string> Choices => ["eligible", "other"];
    protected override SkillProgramTriggerWindow Window => SkillProgramTriggerWindow.AfterHpRecovered;
}
internal sealed class BindShenwuDeathBenefitDescriptor : ShenwuNpcBindingDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindShenwuDeathBenefit;
    protected override IReadOnlyList<string> Choices => ["kill-minion", "fallen-god", "fallen-player", "none"];
    protected override SkillProgramTriggerWindow Window => SkillProgramTriggerWindow.CharacterDied;
}
internal sealed class ShenwuNpcEffectHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IShenwuNpcProgramHost>().ExecuteShenwuNpc(frame, effect);
}

/// <summary>Public actual-turn evidence; the target restriction is reconstructed from rule history.</summary>
public sealed record ShenwuMingwanVictimRecordedEvent(int ActualTurn, int OwnerSeat, int VictimSeat, long DamageFrameId, long CardUseFrameId) : IGameEvent;
public sealed record ShenwuYinzeiDiscardedEvent(long ProgramFrameId, int OwnerSeat, int SourceSeat, int CardId) : IGameEvent;

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 30)]
    private bool ShenwuMingwanDirectedTargetProhibited(ProgramDirectedTargetQuery query) =>
        ShenwuMingwanForbidsTarget(query.ActorSeat, query.TargetSeat);

    [ProgramCapability]
    private sealed class ShenwuNpcProgramHost(GameEngine engine) : IShenwuNpcProgramHost
    {
        public SkillProgramStepOutcome ExecuteShenwuNpc(ProgramSkillFrame frame, SkillProgramEffect effect) => engine.ExecuteShenwuNpc(frame, effect);
    }

    private SkillProgramStepOutcome ExecuteShenwuNpc(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var owner = _players[frame.OwnerSeat];
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        switch (effect.Op)
        {
            case SkillProgramEffectOp.DrawShenwuWuzangReplacement:
                DrawProgramCards(frame.Id, owner.Seat, Math.Max(5, owner.Hp / 2) + ShenwuReplacementDrawBonus(owner.Seat), null, null, SkillProgramCardSetVisibility.Private, reason);
                break;
            case SkillProgramEffectOp.DrawShenwuCangmuReplacement:
                DrawProgramCards(frame.Id, owner.Seat, _players.Count(p => p.IsAlive) + ShenwuReplacementDrawBonus(owner.Seat), null, null, SkillProgramCardSetVisibility.Private, reason);
                break;
            case SkillProgramEffectOp.ResolveShenwuYinzei:
            {
                var attack = ShenwuAppliedDamage(frame);
                if (GetHand(owner).Count != 0 || attack.IsSourceLess || attack.SourceSeat == owner.Seat || !_players[attack.SourceSeat].IsAlive) break;
                var source = _players[attack.SourceSeat];
                var cards = GetHand(source).Concat(GetEquipment(source)).Where(c =>
                    !IsSelfHandCategoryDiscardForbidden(source.Seat, c, FindOwnedCardLocation(source, c), OwnedCardMoveIntent.Discard)).ToArray();
                if (cards.Length == 0) break;
                var card = cards[_random.Next(cards.Length)];
                MoveLogicalDiscard(owner.Seat, source.Seat, card, FindOwnedCardLocation(source, card), reason, parentFrameId: frame.Id);
                AdvanceEventRulesAndQueueFact(new ShenwuYinzeiDiscardedEvent(frame.Id, owner.Seat, source.Seat, card.Id));
                break;
            }
            case SkillProgramEffectOp.BindShenwuOtherDamageSource:
            {
                var attack = ShenwuAppliedDamage(frame);
                var eligible = !attack.IsSourceLess && attack.SourceSeat != owner.Seat && _players[attack.SourceSeat].IsAlive;
                BindShenwuParticipant(frame, effect, eligible ? attack.SourceSeat : null, eligible ? "eligible" : "other");
                break;
            }
            case SkillProgramEffectOp.BindShenwuUndamagingTurnOwner:
            {
                var turn = _resolutionStack.OfType<DeferredTurnEndFrame>().LastOrDefault();
                if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterTurnEnded } context || turn is null ||
                    context.ParentFrameId != turn.Id || !IsActualAfterTurnEndedParent(turn))
                    throw new InvalidOperationException("Yandu requires the exact completed actual turn.");
                var history = CompleteProgramEventHistory().ToArray();
                var end = Array.FindLastIndex(history, e => e is TurnEndedEvent t && t.TurnNumber == turn.TurnNumber && t.ActorSeat == turn.OwnerSeat);
                var start = end < 0 ? -1 : Array.FindLastIndex(history, end, e => e is TurnStartedEvent t && t.TurnNumber == turn.TurnNumber && t.ActorSeat == turn.OwnerSeat);
                var eligible = start >= 0 && owner.Seat != turn.OwnerSeat && _players[turn.OwnerSeat].IsAlive &&
                    !history.Skip(start + 1).Take(end - start - 1).OfType<DamageAppliedEvent>().Any(e => !e.SourceLess && e.SourceSeat == turn.OwnerSeat && e.Amount > 0);
                BindShenwuParticipant(frame, effect, eligible ? turn.OwnerSeat : null, eligible ? "eligible" : "other");
                break;
            }
            case SkillProgramEffectOp.RecordShenwuMingwanVictim:
            {
                var attack = ShenwuAppliedDamage(frame);
                if (!ShenwuOwnActualTurn(owner.Seat) || attack.IsSourceLess || attack.SourceSeat != owner.Seat || attack.TargetSeat == owner.Seat ||
                    LifecycleCardUse(attack.ResolutionId) is not { } use || use.SourceSeat != owner.Seat || use.CardKind != attack.EffectiveCardKind) break;
                if (!ShenwuMingwanVictims(owner.Seat).Contains(attack.TargetSeat))
                    AdvanceEventRulesAndQueueFact(new ShenwuMingwanVictimRecordedEvent(_turnNumber, owner.Seat, attack.TargetSeat, frame.WindowContext!.DamageFrameId!.Value, use.Id));
                break;
            }
            case SkillProgramEffectOp.DrawShenwuMingwanUse:
                if (ShenwuOwnActualTurn(owner.Seat) && frame.WindowContext?.CardUse is { ActorSeat: var actor } && actor == owner.Seat && ShenwuMingwanVictims(owner.Seat).Count != 0)
                    DrawProgramCards(frame.Id, owner.Seat, 1, null, null, SkillProgramCardSetVisibility.Private, reason);
                break;
            case SkillProgramEffectOp.PreventShenwuNitaiOwnTurnDamage:
                if (frame.WindowContext?.Window != SkillProgramTriggerWindow.BeforeDamageApplied ||
                    _resolutionStack.ElementAtOrDefault(_resolutionStack.Count - 2) is not BeforeDamageProgramWindowFrame damage ||
                    damage.Id != frame.WindowContext.ParentFrameId || damage.TargetSeat != owner.Seat)
                    throw new InvalidOperationException("Nitai requires its native before-damage window.");
                if ((frame.SkillId == "shenwu:zhaohuo" ? GetDamageNature(CurrentDamageAttempt!) == DamageNature.Fire : ShenwuOwnActualTurn(owner.Seat)) && !damage.Prevented)
                    PreventProgramCurrentDamage(frame);
                break;
            case SkillProgramEffectOp.LoseShenwuTanyuHp:
                if (_players.Where(p => p.IsAlive).All(p => GetHand(p).Count <= GetHand(owner).Count))
                    return new ProgramSkillHost(this).LoseHp(frame.Id, frame.SkillId, owner.Seat, 1);
                break;
            case SkillProgramEffectOp.BindShenwuRecoveryBeneficiary:
                if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterHpRecovered, HpChange: { Kind: HpChangeKind.Recovery } hp } || hp.Amount <= 0)
                    throw new InvalidOperationException("Jicai requires a real positive recovery occurrence.");
                BindShenwuParticipant(frame, effect, _players[hp.TargetSeat].IsAlive ? hp.TargetSeat : null, _players[hp.TargetSeat].IsAlive ? "eligible" : "other");
                break;
            case SkillProgramEffectOp.BindShenwuDeathBenefit:
            {
                if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.CharacterDied, TargetSeat: { } victim } context || _players[victim].IsAlive)
                    throw new InvalidOperationException("Coalition supply requires an actual death window.");
                var choice = "none";
                if (owner.IsAlive && owner.TeamId is { } team)
                {
                    if (context.SourceSeat == owner.Seat && _players[victim].TeamId != team &&
                        _players[victim].General.Id is "shenwu:zhuyin-left" or "shenwu:zhuyin-right") choice = "kill-minion";
                    else if (victim != owner.Seat && _players[victim].TeamId == team)
                        choice = GetEffectiveFactionId(_players[victim]) == "god" ? "fallen-god" : "fallen-player";
                }
                BindShenwuParticipant(frame, effect, null, choice);
                break;
            }
            default: throw new InvalidOperationException("Unsupported Shenwu NPC operation.");
        }
        return SkillProgramStepOutcome.Continue;
    }

    private IDamageAttempt ShenwuAppliedDamage(ProgramSkillFrame frame)
    {
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } context ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().LastOrDefault(w => w.Id == context.ParentFrameId) is not { } window)
            throw new InvalidOperationException("Shenwu requires its actual applied-damage parent.");
        return GetDamageTriggerAttack(window);
    }
    private void BindShenwuParticipant(ProgramSkillFrame frame, SkillProgramEffect effect, int? seat, string choice) =>
        ReplaceRuntimeTop(frame with { SelectedTargetSeats = seat is { } actual ? Array.AsReadOnly(new[] { actual }) : [],
            ChoiceBindings = Array.AsReadOnly(frame.ChoiceBindings.Append(new ProgramChoiceResultBinding(effect.ResultBind!, choice, frame.OwnerSeat)).ToArray()) });

    private bool IsShenwuChoiceResult(ProgramSkillFrame frame, ProgramChoiceResultBinding binding)
    {
        if (binding.ChooserSeat != frame.OwnerSeat || frame.WindowContext is not { } context) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        var producer = plan.Instructions.Take(frame.InstructionIndex).SingleOrDefault(effect => effect.ResultBind == binding.Name &&
            effect.Op is SkillProgramEffectOp.BindShenwuOtherDamageSource or SkillProgramEffectOp.BindShenwuUndamagingTurnOwner or
                SkillProgramEffectOp.BindShenwuRecoveryBeneficiary or SkillProgramEffectOp.BindShenwuDeathBenefit);
        if (producer is null) return false;
        int? participant = producer.Op switch
        {
            SkillProgramEffectOp.BindShenwuOtherDamageSource when context.Window == SkillProgramTriggerWindow.AfterDamageApplied => context.SourceSeat,
            SkillProgramEffectOp.BindShenwuUndamagingTurnOwner when context.Window == SkillProgramTriggerWindow.AfterTurnEnded => context.TargetSeat,
            SkillProgramEffectOp.BindShenwuRecoveryBeneficiary when context is { Window: SkillProgramTriggerWindow.AfterHpRecovered,
                HpChange: { Kind: HpChangeKind.Recovery, Amount: > 0 } hp } => hp.TargetSeat,
            _ => null
        };
        if (producer.Op == SkillProgramEffectOp.BindShenwuDeathBenefit)
            return context.Window == SkillProgramTriggerWindow.CharacterDied && frame.SelectedTargetSeats.Count == 0 &&
                binding.OptionId is "kill-minion" or "fallen-god" or "fallen-player" or "none";
        return participant is { } seat && IsValidPlayerSeat(seat) &&
            (binding.OptionId == "eligible" && frame.SelectedTargetSeats.SequenceEqual([seat]) ||
                binding.OptionId == "other" && frame.SelectedTargetSeats.Count == 0);
    }

    private bool HasExactShenwuBoundParticipantSelection(ProgramSkillFrame frame)
    {
        if (frame.SelectedTargetSeats is not [var participant] || frame.WindowContext is not { } context ||
            !frame.ChoiceBindings.Any(binding => binding.OptionId == "eligible" && IsShenwuChoiceResult(frame, binding))) return false;
        if (context.Window == SkillProgramTriggerWindow.AfterTurnEnded)
            return _resolutionStack.OfType<DeferredTurnEndFrame>().LastOrDefault(p => p.Id == context.ParentFrameId) is { } ending &&
                context.SourceSeat == participant && context.TargetSeat == participant && ending.OwnerSeat == participant &&
                MatchesAfterTurnEndedChild(ending, frame, context);
        if (context.Window == SkillProgramTriggerWindow.AfterDamageApplied)
            return _resolutionStack.OfType<DamageTriggerWindowFrame>().LastOrDefault(p => p.Id == context.ParentFrameId) is { } damage &&
                GetDamageTriggerAttack(damage) is { IsSourceLess: false } attack && attack.SourceSeat == participant && context.SourceSeat == participant;
        if (context is { Window: SkillProgramTriggerWindow.AfterHpRecovered, HpChange: { } hp })
            return participant == hp.TargetSeat && _resolutionStack.OfType<HpChangedTriggerWindowFrame>().LastOrDefault(p => p.Id == hp.Id) is { } parent &&
                parent.Change == hp && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
                parent.Candidates[parent.CandidateIndex] is { } candidate && candidate.OwnerSeat == frame.OwnerSeat &&
                candidate.SkillId == frame.SkillId && candidate.SkillInstanceId == frame.SkillInstanceId && candidate.BindingId == frame.TriggerId &&
                IsShenwuRecoveryCandidate(candidate, context);
        return false;
    }

    private bool ShenwuEnabled(CharacterState player, string skillId) => player.IsAlive && _contentRegistry.Skills.ContainsKey(skillId) && EnabledSkillPrograms(player).Any(p => p.Id == skillId);
    private bool ShenwuOwnActualTurn(int seat) => _currentSeat == seat && _phase is not (TurnPhase.NotStarted or TurnPhase.Finished);
    private IReadOnlyList<int> ShenwuMingwanVictims(int ownerSeat) => CompleteProgramEventHistory().OfType<ShenwuMingwanVictimRecordedEvent>()
        .Where(e => e.ActualTurn == _turnNumber && e.OwnerSeat == ownerSeat).Select(e => e.VictimSeat).Distinct().ToArray();
    private bool ShenwuMingwanForbidsTarget(int actorSeat, int targetSeat) => ShenwuOwnActualTurn(actorSeat) && actorSeat != targetSeat &&
        ShenwuEnabled(_players[actorSeat], "shenwu:mingwan") && ShenwuMingwanVictims(actorSeat) is { Count: > 0 } victims && !victims.Contains(targetSeat);
    private bool ShenwuFutaiForbidsPeach(int actorSeat, CardKind kind, CardActionType type) => kind == CardKind.Peach && type == CardActionType.Use &&
        _players.Any(p => p.Seat != actorSeat && !ShenwuOwnActualTurn(p.Seat) && ShenwuEnabled(p, "shenwu:futai"));
    private bool ShenwuCannotTurnOver(CharacterState target) => ShenwuEnabled(target, "shenwu:xiongshou");
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 230)]
    private int ShenwuNpcDamageBonus(IDamageAttempt attack)
    {
        var target = _players[attack.TargetSeat]; var bonus = 0;
        if (!attack.IsSourceLess && !attack.IsChainPropagation && attack.CardUserSeat == attack.SourceSeat &&
            attack.EffectiveCardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash &&
            ShenwuEnabled(_players[attack.SourceSeat], "shenwu:xiongshou") && target.Hp < _players[attack.SourceSeat].Hp) bonus++;
        if (!attack.IsSourceLess && attack.SourceSeat != attack.TargetSeat &&
            HasCardPolicy(target, SkillProgramCardPolicyKind.ShenwuArmedSourceDamageBonus) &&
            GetEquipment(_players[attack.SourceSeat]).Any(c => EquipmentCatalog.Get(c.Kind).Slot == EquipmentSlot.Weapon)) bonus++;
        if (!ShenwuOwnActualTurn(target.Seat) && GetDamageNature(attack) == DamageNature.Fire && ShenwuEnabled(target, "shenwu:nitai")) bonus++;
        return bonus;
    }

    private static bool IsShenwuJicaiTrigger(SkillProgramTrigger trigger) => trigger.Window == SkillProgramTriggerWindow.AfterHpRecovered &&
        trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.BindShenwuRecoveryBeneficiary);
    private IEnumerable<ProgramTriggerCandidate> CollectShenwuRecoveryCandidates(HpChangeContext change) =>
        change.Kind != HpChangeKind.Recovery || change.Amount <= 0 ? [] : _players.Where(p => p.IsAlive)
            .SelectMany(p => CollectProgramTriggerCandidates(p, SkillProgramTriggerWindow.AfterHpRecovered))
            .Where(c => IsShenwuJicaiTrigger(GetProgramTrigger(c)));
    private ProgramSkillWindowContext CreateShenwuRecoveryContext(HpChangeContext change, ProgramTriggerCandidate candidate) =>
        new(SkillProgramTriggerWindow.AfterHpRecovered, change.Id, candidate.OwnerSeat, SourceSeat: change.SourceSeat,
            TargetSeat: change.TargetSeat, Amount: change.Amount, OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]) with { HpChangeAmount = change.Amount, HpBeforeChange = change.HpBefore, HpAfterChange = change.HpAfter }, HpChange: change);
    private bool IsShenwuRecoveryCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        IsShenwuJicaiTrigger(GetProgramTrigger(candidate)) && context.HpChange is { Kind: HpChangeKind.Recovery, Amount: > 0 } hp &&
        context.OwnerSeat == candidate.OwnerSeat && context.ParentFrameId == hp.Id && context.TargetSeat == hp.TargetSeat &&
        _resolutionStack.OfType<HpChangedTriggerWindowFrame>().LastOrDefault() is { } parent && parent.Id == hp.Id && parent.Change == hp &&
        parent.Candidates.ElementAtOrDefault(parent.CandidateIndex) == candidate;

    [ProgramCardLegality(ProgramCardLegalityStage.BeforeTurnUseBan, 50)]
    private bool ShenwuFutaiCardUseForbidden(ProgramCardUseQuery query) =>
        ShenwuFutaiForbidsPeach(query.ActorSeat, query.Kind, query.ActionType);
}
