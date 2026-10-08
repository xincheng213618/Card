using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string DrawFundedBasicChoiceMarker = "draw-funded-basic";
    private const string DrawFundedBasicDrawReason = "skill-program.draw-funded-distinct-basic.draw";
    private sealed record DrawFundedBasicNeed(DrawFundedDistinctBasicIntent Intent, long? ParentFrameId,
        long? RequestFrameId, long? ParentActionId, int Cursor, int? FixedTargetSeat);

    private bool DrawFundedDistinctBasicQualified(CharacterState actor)
    {
        if (!actor.IsAlive || _winner != Winner.None) return false;
        var hand = GetHand(actor);
        if (hand.Count == 0 || hand.Count != actor.Hp) return false;
        var color = SuitColor(EffectiveSuit(actor, hand[0]));
        return color is not null && hand.All(card => SuitColor(EffectiveSuit(actor, card)) == color);
    }

    private bool DrawFundedDistinctBasicNameAvailable(int actorSeat, string ledger, CardKind kind) =>
        !CompleteProgramEventHistory().OfType<DrawFundedDistinctBasicIssuedEvent>().Any(e =>
            e.ActorSeat == actorSeat && e.MethodLedgerId == ledger && e.ActualTurnNumber == _turnNumber &&
            e.ActualTurnOwnerSeat == _turnProgression.OwnerSeat && e.NormalizedName == ProgramBasicCardName(kind)) &&
        !_resolutionStack.OfType<DrawFundedDistinctBasicFrame>().Any(f => f.Payment.Source.OwnerSeat == actorSeat &&
            f.Payment.MethodLedgerId == ledger && f.Payment.ActualTurnNumber == _turnNumber &&
            f.Payment.ActualTurnOwnerSeat == _turnProgression.OwnerSeat && f.Payment.NormalizedName == ProgramBasicCardName(kind));

    private IEnumerable<(CardConversionSource Source, SkillProgramViewAs Rule, string Hash)> DrawFundedDistinctBasicRules(
        CharacterState actor, CardKind? output = null, bool responseUse = false, bool dyingUse = false)
    {
        if (!DrawFundedDistinctBasicQualified(actor)) yield break;
        foreach (var instance in GetSkillBindingShard(actor).ProgramInstances)
        foreach (var rule in instance.Program.ViewAs.Where(r => r.DrawFundedDistinctBasic is not null))
            if ((output is null || rule.OutputKind == output) && (dyingUse ? rule.ForPlay && !rule.NoDying : responseUse ? rule.ForResponse : rule.ForPlay) &&
                rule.InputCount == 0 && rule.UseOnly && rule.Condition.Evaluate(CreateSkillContext(actor)) &&
                DrawFundedDistinctBasicNameAvailable(actor.Seat, rule.DrawFundedDistinctBasic!.MethodLedgerId, rule.OutputKind) &&
                !IsCardUseForbidden(actor.Seat, rule.OutputKind, CardActionType.Use))
                yield return (new(instance.SkillId, rule.Id, actor.Seat, instance.SkillInstanceId), rule, instance.Program.GameplayHash);
    }

    private IReadOnlyList<LegalAction> DrawFundedDistinctBasicPlayActions(CharacterState actor)
    {
        if (_phase != TurnPhase.Play || actor.Seat != _currentSeat || _resolutionStack.Count != 0 || !actor.IsAlive) return [];
        var actions = new List<LegalAction>();
        foreach (var (source, rule, _) in DrawFundedDistinctBasicRules(actor))
            actions.AddRange(DrawFundedDistinctBasicNativePlayActions(actor, source, rule.OutputKind));
        return Array.AsReadOnly(actions.ToArray());
    }

    // Also used after the draw. It deliberately asks only current native target/quota legality;
    // the paid source and hand qualification are already frozen, not recharged or revoked.
    private IReadOnlyList<LegalAction> DrawFundedDistinctBasicNativePlayActions(CharacterState actor, CardConversionSource source, CardKind kind)
    {
        var result = new List<LegalAction>();
        if (!actor.IsAlive || _phase != TurnPhase.Play || _currentSeat != actor.Seat || IsCardUseForbidden(actor.Seat, kind, CardActionType.Use)) return result;
        var card = new Card(0, kind, Suit.None, 0);
        if (IsSlashCard(kind))
        {
            var targets = GetFangtianOrderedSlashTargets(actor, card, source, effectiveKind: kind);
            foreach (var target in targets)
                result.Add(new(LegalActionKind.Slash, 0, target.Seat, DescribeConversion(source, $"摸一张牌，视为使用【{card.DisplayName}】攻击 {target.Name}"), kind) { ConversionSource = source });
            AddProgramTargetCountSlashActions(result, actor, card, targets, card.DisplayName, kind, source, null);
        }
        else if (kind == CardKind.Peach && actor.Hp < actor.MaxHp && !HasSelfCardTargetProhibition(actor.Seat) && !HasBeneficiarySuitShield(actor.Seat, actor.Seat, Suit.None))
            result.Add(new(LegalActionKind.Peach, 0, null, DescribeConversion(source, "摸一张牌，视为使用【桃】"), kind) { ConversionSource = source });
        else if (kind == CardKind.Alcohol && !actor.HasAlcoholEffect && !HasSelfCardTargetProhibition(actor.Seat) &&
            (!actor.UsedPlayPhaseAlcoholThisTurn || HasTargetCardQuotaAllowance(actor.Seat, actor.Seat) || HasNextUnlimitedCard(actor) ||
                HasCardPolicy(actor, SkillProgramCardPolicyKind.UnlimitedAlcoholUse, CardKind.Alcohol)) && !HasBeneficiarySuitShield(actor.Seat, actor.Seat, Suit.None))
            result.Add(new(LegalActionKind.Alcohol, 0, null, DescribeConversion(source, "摸一张牌，视为使用【酒】"), kind) { ConversionSource = source });
        return Array.AsReadOnly(result.Select(a => a with { DrawFundedDistinctBasicUse = ViewAsRule(source)!.DrawFundedDistinctBasic }).ToArray());
    }

    private bool TrySubmitDrawFundedDistinctBasicPlay(PlayCardCommand command, out CommandResult result)
    {
        result = null!;
        if (command.CardId != 0 || command.ConversionSource is not { } source || ViewAsRule(source)?.DrawFundedDistinctBasic is null) return false;
        var actor = _players[command.ActorSeat];
        var action = DrawFundedDistinctBasicPlayActions(actor).SingleOrDefault(a => a.ConversionSource == source &&
            a.PlayedCardKind == command.PlayedCardKind && a.TargetCardId == command.TargetCardId && a.TargetSeats.SequenceEqual(command.TargetSeats ?? []));
        if (action is null || command.AdditionalConversionSources is { Count: > 0 } || _pendingDecision is not { Kind: DecisionKind.PlayCard } original)
        { result = Reject(CommandErrorCode.IllegalAction, "The draw-funded use lost its exact published source, qualification, name or native targets."); return true; }
        result = Accept(() =>
        {
            BeginDrawFundedDistinctBasic(actor, source, action.PlayedCardKind!.Value, action.TargetSeats, original,
                new(DrawFundedDistinctBasicIntent.Play, null, null, null, 0, action.TargetSeat));
            AdvanceRulesAndPublishState(); if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary();
        }); return true;
    }

    private bool TryExecuteDrawFundedDistinctBasicPlay(CharacterState actor, LegalAction action)
    {
        if (action.CardId != 0 || action.ConversionSource is not { } source || ViewAsRule(source)?.DrawFundedDistinctBasic is null) return false;
        if (!DrawFundedDistinctBasicPlayActions(actor).Any(a => a.Kind == action.Kind && a.ConversionSource == source &&
            a.PlayedCardKind == action.PlayedCardKind && a.TargetSeats.SequenceEqual(action.TargetSeats)))
            throw new InvalidOperationException("An AI draw-funded use changed its published action before acceptance.");
        var original = _pendingDecision ?? new PendingDecision(DecisionKind.PlayCard, actor.Seat, "已发布合法出牌动作", [], [])
        { PromptId = CreatePromptId(), Revision = Revision };
        BeginDrawFundedDistinctBasic(actor, source, action.PlayedCardKind!.Value, action.TargetSeats, original,
            new(DrawFundedDistinctBasicIntent.Play, null, null, null, 0, action.TargetSeat)); return true;
    }

    private DrawFundedBasicNeed? DrawFundedDistinctBasicContext(PendingDecision prompt, ResolutionFrame? suppliedTop = null)
    {
        var top = suppliedTop ?? _resolutionStack.LastOrDefault();
        if (top is null || top is DrawFundedDistinctBasicFrame or RequestedDeckBasicFrame) return null;
        var ownerId = top is ResponseWindowFrame response ? response.ParentFrameId : top.Id;
        var parentAction = LifecycleCardUse(ownerId)?.Action?.ActionId;
        if (prompt.Kind == DecisionKind.RespondDodge && ActiveFactionDefense is null && top is ResponseWindowFrame &&
            ActiveCardAttack is { } attack && attack.TargetSeat == prompt.PlayerSeat && IsProgramResponseCardUse(_players[prompt.PlayerSeat], CardKind.Dodge) && ownerId == attack.ResolutionId)
            return new(DrawFundedDistinctBasicIntent.OwnSlashDodge, ownerId, top.Id, parentAction, attack.SuccessfulDodgeResponses, attack.SourceSeat);
        if (prompt.Kind == DecisionKind.RescueDying && ActiveDying is { } dying && top.Id == dying.FrameId && dying.ResponderSeat == prompt.PlayerSeat)
            return new(DrawFundedDistinctBasicIntent.Dying, dying.FrameId, top.Id, null, dying.ResponderIndex, dying.VictimSeat);
        if (prompt.Kind == DecisionKind.RespondSlash && ActiveBorrowedSword is { AwaitingSlashChoice: true, ActiveAttack: null } borrowed &&
            borrowed.WeaponOwnerSeat == prompt.PlayerSeat && borrowed.ResolutionId == ownerId && GetWeapon(_players[prompt.PlayerSeat]) is not null)
            return new(DrawFundedDistinctBasicIntent.BorrowedSword, ownerId, top.Id, parentAction, 0, borrowed.SlashTargetSeat);
        if (prompt.Kind == DecisionKind.QinglongCrescentBlade && ActiveQinglongCrescentBlade is { } blade &&
            blade.Attack.SourceSeat == prompt.PlayerSeat && SameAttackOwner(ActiveCardAttack, blade.Attack) && blade.Attack.ResolutionId == ownerId)
            return new(DrawFundedDistinctBasicIntent.Qinglong, ownerId, top.Id, parentAction, 0, blade.Attack.TargetSeat);
        if (prompt.Kind != DecisionKind.ProgramTrigger || top is not ProgramSkillFrame program) return null;
        var paused = ProgramInstructionResolver.Default.Resolve(program, _contentRegistry.GetSkill(program.SkillId).Program!).GetPausedInstruction(program.InstructionIndex).Effect;
        if (paused.Op == SkillProgramEffectOp.RequestSlashByTarget && program.SelectedTargetSeats.SequenceEqual([prompt.PlayerSeat]) &&
            prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "request-slash-decline"))
            return new(DrawFundedDistinctBasicIntent.ProgramSlash, program.Id, program.Id, null, program.InstructionIndex, program.OwnerSeat);
        if (paused.Op == SkillProgramEffectOp.RequestSlashByNearest && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "request-slash-nearest-decline"))
            return new(DrawFundedDistinctBasicIntent.ProgramNearestSlash, program.Id, program.Id, null, program.InstructionIndex, null);
        if (paused.Op == SkillProgramEffectOp.RequestSlashAgainstChosenTarget && program.AssistedSlashRequest is { TargetSeat: { } target } assisted && assisted.ActorSeat == prompt.PlayerSeat)
            return new(DrawFundedDistinctBasicIntent.AssistedSlash, program.Id, program.Id, null, program.InstructionIndex, target);
        if (paused.Op == SkillProgramEffectOp.RequestLegalSlashByNearest && program.NearestLegalSlashRequest is { AwaitingFaction: false } nearest && nearest.ActorSeat == prompt.PlayerSeat)
            return new(DrawFundedDistinctBasicIntent.NearestLegalSlash, program.Id, program.Id, null, program.InstructionIndex, null);
        return null;
    }

    private bool DrawFundedDistinctBasicNativeResponseLegal(CharacterState actor, DrawFundedBasicNeed need, CardKind kind, int? targetSeat)
    {
        if (!actor.IsAlive || _winner != Winner.None || IsCardUseForbidden(actor.Seat, kind, CardActionType.Use)) return false;
        if (need.Intent == DrawFundedDistinctBasicIntent.OwnSlashDodge)
            return kind == CardKind.Dodge && IsProgramResponseCardUse(actor, kind) && ActiveCardAttack is { } a && a.ResolutionId == need.ParentFrameId &&
                a.SuccessfulDodgeResponses == need.Cursor && a.SourceSeat == targetSeat && ActiveFactionDefense is null;
        if (targetSeat is not { } seat || !IsValidPlayerSeat(seat) || !_players[seat].IsAlive || HasBeneficiarySuitShield(actor.Seat, seat, Suit.None)) return false;
        if (need.Intent == DrawFundedDistinctBasicIntent.Dying)
            return ActiveDying is { } d && d.FrameId == need.ParentFrameId && d.ResponderIndex == need.Cursor && d.ResponderSeat == actor.Seat &&
                d.VictimSeat == seat && _players[seat].Hp <= 0 && (kind == CardKind.Peach && CanUsePeachToRescue(actor.Seat, seat) ||
                    kind == CardKind.Alcohol && actor.Seat == seat && !HasSelfCardTargetProhibition(actor.Seat));
        if (!IsSlashCard(kind) || IsCardTargetProhibited(_players[seat], kind, Suit.None)) return false;
        if (need.Intent == DrawFundedDistinctBasicIntent.BorrowedSword)
            return ActiveBorrowedSword is { AwaitingSlashChoice: true, ActiveAttack: null } b && b.ResolutionId == need.ParentFrameId && b.WeaponOwnerSeat == actor.Seat &&
                b.SlashTargetSeat == seat && GetWeapon(actor) is not null && IsLegalBorrowedSwordSlashTarget(actor, _players[seat], kind, Suit.None, false, null, 0, []);
        if (need.Intent == DrawFundedDistinctBasicIntent.Qinglong)
            return ActiveQinglongCrescentBlade is { } q && q.Attack.ResolutionId == need.ParentFrameId && q.Attack.SourceSeat == actor.Seat &&
                q.Attack.TargetSeat == seat && SameAttackOwner(ActiveCardAttack, q.Attack) && CanUseQinglongCrescentBladeTarget(actor, _players[seat], kind, null, false, Suit.None, 0);
        if (_resolutionStack.SingleOrDefault(f => f.Id == need.ParentFrameId) is not ProgramSkillFrame program || program.InstructionIndex != need.Cursor ||
            !_players[program.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[program.OwnerSeat], program.SkillId, program.SkillInstanceId)) return false;
        if (need.FixedTargetSeat is { } fixedTarget && fixedTarget != seat ||
            need.Intent is DrawFundedDistinctBasicIntent.ProgramNearestSlash or DrawFundedDistinctBasicIntent.NearestLegalSlash && !GetNearestLivingCharacterSeats(actor.Seat).Contains(seat)) return false;
        return CanUseSlashTarget(actor, _players[seat], new(0, kind, Suit.None, 0), effectiveKind: kind, noEffectiveRank: true, physicalCardIds: []);
    }

    private IEnumerable<(CardConversionSource Source, SkillProgramViewAs Rule, string Hash)> DrawFundedDistinctBasicForcedSlashRules(CharacterState actor, CharacterState target, bool qinglong) =>
        DrawFundedDistinctBasicRules(actor).Where(r => IsSlashCard(r.Rule.OutputKind) && !HasBeneficiarySuitShield(actor.Seat, target.Seat, Suit.None) &&
            !IsCardTargetProhibited(target, r.Rule.OutputKind, Suit.None) && (qinglong ? CanUseQinglongCrescentBladeTarget(actor, target, r.Rule.OutputKind, null, false, Suit.None, 0) :
                IsLegalBorrowedSwordSlashTarget(actor, target, r.Rule.OutputKind, Suit.None, false, null, 0, [])));

    private bool HasDrawFundedDistinctBasicProgramSlash(CharacterState actor, IEnumerable<int> targets) => DrawFundedDistinctBasicRules(actor)
        .Any(r => IsSlashCard(r.Rule.OutputKind) && targets.Any(s => IsValidPlayerSeat(s) && _players[s].IsAlive && !HasBeneficiarySuitShield(actor.Seat, s, Suit.None) &&
            CanUseSlashTarget(actor, _players[s], new(0, r.Rule.OutputKind, Suit.None, 0), effectiveKind: r.Rule.OutputKind, noEffectiveRank: true, physicalCardIds: [])));

    private IReadOnlyList<PromptChoice> DrawFundedDistinctBasicResponseChoices(PendingDecision prompt, DrawFundedBasicNeed need)
    {
        var actor = _players[prompt.PlayerSeat]; var choices = new List<PromptChoice>();
        foreach (var (source, rule, _) in DrawFundedDistinctBasicRules(actor, responseUse: need.Intent == DrawFundedDistinctBasicIntent.OwnSlashDodge, dyingUse: need.Intent == DrawFundedDistinctBasicIntent.Dying))
        {
            var targets = need.FixedTargetSeat is { } target ? new[] { target } : prompt.ValidTargetSeats.ToArray();
            foreach (var seat in targets.Where(seat => DrawFundedDistinctBasicNativeResponseLegal(actor, need, rule.OutputKind, seat)))
            {
                var p = new Dictionary<string, string> { [DrawFundedBasicChoiceMarker] = need.Intent.ToString(), ["output-kind"] = rule.OutputKind.ToString(),
                    ["original-frame-id"] = need.ParentFrameId!.Value.ToString(CultureInfo.InvariantCulture), ["native-target-seat"] = seat.ToString(CultureInfo.InvariantCulture) };
                AddConversionParameters(p, source);
                choices.Add(new(new($"draw-funded.{need.Intent}.{need.ParentFrameId}.{source.BindingId}.{source.SkillInstanceId}.{seat}"),
                    $"发动【{ProgramConversionName(source)}】，摸一张牌，视为使用【{CardCatalog.Get(rule.OutputKind).DisplayName}】。", [],
                    (need.Intent is DrawFundedDistinctBasicIntent.OwnSlashDodge or DrawFundedDistinctBasicIntent.Dying) ? [] : [seat], p));
            }
        }
        return Array.AsReadOnly(choices.ToArray());
    }

    private PendingDecision? AddDrawFundedDistinctBasicChoices(PendingDecision? prompt)
    {
        if (prompt is null || !_started || prompt.Choices.Any(IsDrawFundedDistinctBasicAnswer) || DrawFundedDistinctBasicContext(prompt) is not { } need) return prompt;
        var choices = DrawFundedDistinctBasicResponseChoices(prompt, need);
        return choices.Count == 0 ? prompt : prompt with { Choices = Array.AsReadOnly(prompt.Choices.Concat(choices).ToArray()) };
    }
    private static bool IsDrawFundedDistinctBasicAnswer(PromptChoice choice) => choice.Parameters.ContainsKey(DrawFundedBasicChoiceMarker);
    private static PendingDecision DrawFundedDistinctBasicNativeDecision(PendingDecision prompt) => prompt with
    { Choices = Array.AsReadOnly(prompt.Choices.Where(c => !IsDrawFundedDistinctBasicAnswer(c)).ToArray()) };
    private bool HasDrawFundedDistinctBasicDodge(CharacterState actor) => IsProgramResponseCardUse(actor, CardKind.Dodge) && DrawFundedDistinctBasicRules(actor, CardKind.Dodge, true).Any();
    private bool HasDrawFundedDistinctBasicDyingResponse(CharacterState actor) => ActiveDying is { } d &&
        DrawFundedDistinctBasicRules(actor, dyingUse: true).Any(r => DrawFundedDistinctBasicNativeResponseLegal(actor,
            new(DrawFundedDistinctBasicIntent.Dying, d.FrameId, d.FrameId, null, d.ResponderIndex, d.VictimSeat), r.Rule.OutputKind, d.VictimSeat));
}
