using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IWushuangCombatHost
{
    SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class WushuangCombatDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangCombatTactic;
    public override ISkillProgramEffectHandler Handler { get; } = new WushuangCombatHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "tactic", "sourceBind", "condition");
        var tactic = r.RequiredInt("tactic");
        if (tactic is not (2019 or 2020 or 2039 or 2040 or 2104 or 2105 or 2106 or 2116 or 2120 or 2121 or 2122 or
            2130 or 2131 or 2143 or 2174 or 2175 or 2185 or 2198 or 2245 or 2246 or 2247 or 2337 or 2365 or 2367))
            throw new InvalidOperationException($"{r.Path}: unknown combat tactic operation.");
        var bind = r.OptionalIdentifier("sourceBind");
        if ((tactic is 2039 or 2040 or 2365) != (bind is not null))
            throw new InvalidOperationException($"{r.Path}: recasts require their selected physical-card binding.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), tactic, r.Condition(), sourceBind: bind);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => e.SourceBind is not { } bind ? [] :
        e.Amount == 2365 ? [new RequireActivationEntry(), new ReadCardSet(bind), new MoveCardSet(bind, null, SkillProgramCardDestination.DiscardPile)] :
        [new RequireOwnedCardSet(bind, SkillProgramEffectTarget.Owner, e.Amount == 2040 ? 2 : 1, [CardZoneKind.Hand, CardZoneKind.Equipment]),
            new ReadCardSet(bind), new MoveCardSet(bind, null, SkillProgramCardDestination.DiscardPile)];
}
internal sealed class WushuangCombatHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangCombatTactic;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        h.GetCapability<IWushuangCombatHost>().Execute(f, e);
}

public sealed record WushuangCombatReceipt(int TacticId, int InstructionIndex, int TargetSeat, int Amount,
    DamageNature Nature, int Stage = 0, int Index = 0, int DrawCount = 0)
{
    private readonly IReadOnlyList<int> _seats = Array.Empty<int>();
    public IReadOnlyList<int> Seats { get => _seats; init => _seats = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WushuangCombatReceipt? WushuangCombat { get; init; }
}
public sealed record WushuangCombatIssuedEvent(long ProgramFrameId, int TacticId, int OwnerSeat, int InstructionIndex) : IGameEvent;
public sealed record WushuangCombatDamageFactEvent(long DamageFrameId, int Turn, int Round, int SourceSeat, int TargetSeat,
    int Amount, CardKind? Kind, DamageNature Nature, bool SourceLess) : IGameEvent;
public sealed record WushuangCombatUseFactEvent(long CardUseFrameId, int Turn, int Round, int OwnerSeat,
    CardKind Kind, Suit Suit, int SlashOrdinal, bool Drunken) : IGameEvent;
public sealed record WushuangLuckyNumberEvent(int OwnerSeat, int Number) : IGameEvent;
public sealed record WushuangIncomingReductionIssuedEvent(long AttackFrameId, int TargetSeat, int Round, int Ordinal) : IGameEvent;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class WushuangCombatHost(GameEngine engine) : IWushuangCombatHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffect e) => engine.ExecuteWushuangCombat(f, e); }

    private SkillProgramStepOutcome ExecuteWushuangCombat(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!IsWushuangMode || f.SkillId != $"wushuang:tactic-{effect.Amount}" || f.WushuangCombat is not null ||
            ProgramEventHistory<WushuangCombatIssuedEvent>().Any(e => e.ProgramFrameId == f.Id && e.InstructionIndex == f.InstructionIndex))
            throw new InvalidOperationException("A combat tactic requires its exact unpaid owner instruction.");
        var id = effect.Amount; var target = f.WindowContext?.SourceSeat ?? f.OwnerSeat;
        var amount = 0; var nature = DamageNature.Normal; int[] seats = [];
        if (id is 2105 or 2106) { amount = id == 2105 ? 1 : 2; nature = DamageNature.Thunder; seats = _players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray(); }
        if (id is 2174 or 2175) { amount = id == 2174 ? 1 : 2; seats = _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).Select(p => p.Seat).ToArray(); }
        if (id == 2116) { amount = 1; seats = [target]; }
        if (id is 2104 or 2143)
        {
            var usage = $"draw-{id}";
            _skillRuntimeState.TryConsumeUsage(f.OwnerSeat, f.SkillId, usage, SkillUsageScope.Game, int.MaxValue);
            if (_skillRuntimeState.GetUsage(f.OwnerSeat, f.SkillId, usage, SkillUsageScope.Game) % (id == 2104 ? 9 : 6) == 0)
            { var enemies = _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).ToArray(); if (enemies.Length > 0) { target = enemies[_random.Next(enemies.Length)].Seat; amount = 1; seats = [target]; } }
        }
        if (id == 2131)
        {
            var context = f.WindowContext!;
            var parent = _resolutionStack.OfType<DamageTriggerWindowFrame>().Single(w => w.Id == context.ParentFrameId);
            var attack = GetDamageTriggerAttack(parent);
            if (attack.IsSourceLess || context.DamageFrameId != parent.ParentFrameId || context.SourceSeat != attack.SourceSeat || context.Amount != attack.DamageAmount)
                throw new InvalidOperationException("Reflected damage lost its original paid damage segment.");
            target = attack.SourceSeat; amount = attack.DamageAmount; nature = GetDamageNature(attack); seats = [target];
        }
        var draw = id == 2130 || id == 2367 && f.WindowContext?.Window != SkillProgramTriggerWindow.SkillsChanged ? 1 : 0;
        ReplaceRuntimeTop(f = f with { WushuangCombat = new(id, f.InstructionIndex, target, amount, nature, DrawCount: draw) { Seats = seats } });
        AdvanceEventRulesAndQueueFact(new WushuangCombatIssuedEvent(f.Id, id, f.OwnerSeat, f.InstructionIndex));
        if (id == 2367 && f.WindowContext?.Window == SkillProgramTriggerWindow.SkillsChanged)
        {
            var lucky = 1 + _random.Next(13);
            AdvanceEventRulesAndQueueFact(new WushuangLuckyNumberEvent(f.OwnerSeat, lucky));
            AddLog("WushuangLuckyNumber", $"{_players[f.OwnerSeat].Name} 的幸运数字为 {lucky}。", f.OwnerSeat);
        }
        if (id == 2019)
        {
            ReplaceRuntimeTop(f = f with { WushuangCombat = f.WushuangCombat! with { Stage = 10 } });
            _ = BeginJudgment(null, _currentSeat, "闪电", f.Id, CardKind.Lightning, JudgmentContinuationKind.ProgramSkill,
                sourceSeat: f.OwnerSeat, programResultBind: "wushuang-lightning", programResultVisibility: SkillProgramCardSetVisibility.Public);
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (id is 2039 or 2040 or 2365)
        {
            var cards = GetProgramCardSet(f, effect.SourceBind!);
            if (cards.CardIds.Count is < 1 or > 2 || cards.CardIds.Count > (id == 2040 ? 2 : 1) ||
                cards.SourceLocations.Any(location => location.OwnerSeat != f.OwnerSeat || location.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
                id == 2365 && cards.CardIds.Any(card => GetAttackCard(card).Kind is not (CardKind.PeachGarden or CardKind.ArrowBarrage or CardKind.Lightning)) ||
                cards.CardIds.Where((card, index) => _cardZones.GetLocation(card) != cards.SourceLocations[index]).Any())
                throw new InvalidOperationException("Recast materials left their exact owning selection.");
            f = BeginFrameNativeRecastInvocation(f, f.OwnerSeat, cards.CardIds);
            ReplaceRuntimeTop(f = f with { WushuangCombat = f.WushuangCombat! with { Stage = 1, DrawCount = cards.CardIds.Count } });
            foreach (var card in cards.CardIds)
            { var location = _cardZones.GetLocation(card); MoveCard(_cardZones.CardsAt(location).Single(c => c.Id == card), location, CardLocation.DiscardPile, CardMoveReasons.RecastDiscard); }
        }
        if (id == 2020) ChangeProgramMaximumHp(f, 7 - _players[f.OwnerSeat].MaxHp);
        if (id == 2185)
            foreach (var enemy in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat))
                ChangeWushuangTargetMaximumHp(enemy, -1, f.SkillId);
        if (id is 2120 or 2121 or 2122)
        {
            var enemy = _players[target];
            var candidates = GetHand(enemy).Concat(GetEquipment(enemy)).Where(c => !IsForeignEquipmentDiscardPrevented(f.OwnerSeat, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard) &&
                !IsSelfHandCategoryDiscardForbidden(enemy.Seat, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard)).ToArray();
            if (candidates.Length > 0) { var card = candidates[_random.Next(candidates.Length)]; MoveLogicalDiscard(f.OwnerSeat, enemy.Seat, card, _cardZones.GetLocation(card.Id), new("wushuang.emergency-discard")); }
        }
        if (id == 2198)
        {
            var enemies = _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && GetHand(p).Count > 0).ToArray();
            if (enemies.Length > 0)
            { var enemy = enemies[_random.Next(enemies.Length)]; var cards = GetHand(enemy); var card = cards[_random.Next(cards.Count)]; MoveCard(card, CardLocation.Hand(enemy.Seat), CardLocation.Hand(f.OwnerSeat), new("wushuang.transport")); }
        }
        if (id is 2245 or 2246 or 2247)
            new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, id == 2246 ? 3 : 2, null, null);
        if (id == 2337)
        {
            if (f.WindowContext is not { CardUse: { } context } || LifecycleCardUse(context.ParentCardUseFrameId) is not { Action: { } action, ExtraCardEffect: null } use ||
                action.ActionId != context.CardActionId || use.SourceSeat != f.OwnerSeat || use.CardKind != CardKind.Snatch)
                throw new InvalidOperationException("Repeated Snatch requires its exact accepted original effect.");
            var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.Id == f.WindowContext.ParentFrameId);
            var source = new CardConversionSource(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
            ReplaceRuntimeFrame(use.Id, use with { ExtraCardEffect = new() { Source = source, ProgramFrameId = f.Id,
                ActionId = action.ActionId, AutomaticGameplayHash = f.GameplayHash, Trick = window.TrickContinuation } });
            AdvanceEventRulesAndQueueFact(new AutomaticExtraCardEffectIssuedEvent(f.Id, source, use.Id, action.ActionId, f.GameplayHash));
        }
        return ContinueWushuangCombat(f.Id) ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.Continue;
    }

    private void ChangeWushuangTargetMaximumHp(CharacterState target, int delta, string skill)
    {
        var before = target.MaxHp; var hpBefore = target.Hp;
        target.MaxHp = HasWushuangTactic(target.Seat, 2020) ? 7 : Math.Max(1, checked(before + delta));
        target.Hp = Math.Min(target.Hp, target.MaxHp);
        AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(target.Seat, target.MaxHp - before, target.MaxHp, skill) { HpBeforeClamp = hpBefore });
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 61400)]
    private bool ContinueWushuangCombat(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WushuangCombat: { } r } f || f.Id != frameId) return false;
        if (f.InstructionIndex != r.InstructionIndex || r.Stage == 10 ||
            GetPausedPrivateOfferEffect(f) is not { Op: SkillProgramEffectOp.ResolveWushuangCombatTactic } effect || effect.Amount != r.TacticId)
            throw new InvalidOperationException("A combat tactic lost its exact owning instruction.");
        if (TryBeginQueuedRecoveryReplacement(frameId, PostEventContinuation.Program) ||
            TryBeginCharacterStateProgramWindow(frameId, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frameId, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(frameId)) return true;
        f = GetActiveProgramFrame(frameId); r = f.WushuangCombat!;
        if (r.DrawCount > 0 && r.Stage < 2)
        {
            ReplaceRuntimeTop(f = f with { WushuangCombat = r = r with { Stage = 2 } });
            BeginOperationProgramDraw(f, f.OwnerSeat, r.DrawCount, r.TacticId is 2039 or 2040 or 2365 ? CardMoveReasons.RecastDraw : new("wushuang.combat.draw"));
            return true;
        }
        while (r.Index < r.Seats.Count && _winner == Winner.None)
        {
            var seat = r.Seats[r.Index];
            ReplaceRuntimeTop(f = f with { WushuangCombat = r = r with { Stage = 3, Index = r.Index + 1 } });
            if (!_players[seat].IsAlive) continue;
            BeginProgramSkillDamage(f, seat, r.Amount, nature: r.Nature, sourceLess: r.TacticId == 2019);
            return true;
        }
        ReplaceRuntimeTop(f with { WushuangCombat = null });
        return false;
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveWushuangCombatTactic)]
    private void ReturnWushuangCombatDraw(ProgramSkillFrame frame, NativeDrawOutcome outcome)
    {
        if (frame.WushuangCombat is not { Stage: 2 } r || outcome.RecipientSeat != frame.OwnerSeat || outcome.RequestedCount != r.DrawCount)
            throw new InvalidOperationException("Combat draw lost its exact paid instruction.");
        var reason = r.TacticId is 2039 or 2040 or 2365 ? CardMoveReasons.RecastDraw : new CardMoveReason("wushuang.combat.draw");
        CompleteProgramDrawCards(frame.Id, frame.OwnerSeat, null, SkillProgramCardSetVisibility.Private, reason, 0, outcome.ActualDrawIds);
        if (r.TacticId is 2039 or 2040 or 2365)
        {
            var effect = GetPausedPrivateOfferEffect(frame)!; var set = GetProgramCardSet(frame, effect.SourceBind!);
            foreach (var id in set.CardIds) { var card = _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c => c.Id == id); AdvanceEventRulesAndQueueFact(new CardRecastEvent(frame.OwnerSeat, id, card.Kind, outcome.ActualDrawIds.Count)); }
            CompleteFrameNativeRecastInvocation(GetActiveProgramFrame(frame.Id), outcome.ActualDrawIds.Count);
        }
        AdvanceRuntimeProgram(frame.Id);
    }

    private bool IsWushuangLightningJudgment(ProgramSkillFrame f, int target, string reason, string bind, SkillProgramCardSetVisibility visibility) =>
        IsWushuangMode && f.WushuangCombat is { TacticId: 2019, Stage: 10 } && target == _currentSeat && reason == "闪电" && bind == "wushuang-lightning" && visibility == SkillProgramCardSetVisibility.Public;

    private bool TryReturnWushuangLightningJudgment(JudgmentFrame judgment)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WushuangCombat: { TacticId: 2019, Stage: 10 } r } f || f.Id != judgment.ParentFrameId) return false;
        if (!IsWushuangLightningJudgment(f, judgment.TargetSeat, judgment.Reason, judgment.ProgramResultBind!, judgment.ProgramResultVisibility!.Value))
            throw new InvalidOperationException("Lightning tactic lost its exact judgment return.");
        var card = GetJudgmentCard(judgment);
        var hit = card is not null && EffectiveZhenyiJudgmentSuit(judgment, EffectiveSuit(_players[judgment.TargetSeat], card)) == Suit.Spade && card.Rank is >= 2 and <= 9;
        ReplaceRuntimeTop(f = f with { WushuangCombat = r with { Stage = 0, TargetSeat = judgment.TargetSeat, Amount = hit ? 3 : 0, Nature = DamageNature.Thunder, Seats = hit ? [judgment.TargetSeat] : [] } });
        if (card is not null && _cardZones.GetLocation(card.Id) is { Zone: CardZoneKind.Judgment or CardZoneKind.Processing } location)
            MoveCard(card, location, CardLocation.DiscardPile, CardMoveReasons.JudgmentFinish);
        AdvanceRuntimeProgram(f.Id); return true;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 61400)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 61400)]
    private bool RetainWushuangCombat(ProgramSkillFrame f) => f.WushuangCombat is not null;
}
