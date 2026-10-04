using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string DamageTargetObtainReason = "skill-program.damage-target-obtain.obtain";
    private const string DamageTargetBenefitReason = "skill-program.damage-target-obtain.benefit-draw";
    private bool CanOfferDamageTargetObtain(int owner, ProgramSkillWindowContext c) =>
        c.TargetSeat is { } victim && victim != owner && IsValidPlayerSeat(victim) &&
        _players[owner].IsAlive && _players[victim].IsAlive && ActualDamageTargetMountUse(owner, c) is not null &&
        DamageTargetMountZones.Any(z => _cardZones.CardsAt(new(z, victim)).Count > 0);
    private IReadOnlyList<PromptChoice> DamageTargetObtainChoices(ProgramSkillFrame f) =>
        BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.DamageTargetObtain!.VictimSeat, DamageTargetMountZones, OwnedCardMoveIntent.Obtain)
            .Select(c => c with { Parameters = new Dictionary<string,string>(c.Parameters) { ["program-action"] = "damage-target-obtain" } }).ToArray();
    private SkillProgramStepOutcome BeginDamageTargetObtain(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        var c = f.WindowContext ?? throw new InvalidOperationException("Obtain lost its damage context.");
        if (!CanOfferDamageTargetObtain(f.OwnerSeat, c)) return SkillProgramStepOutcome.Continue;
        var use = ActualDamageTargetMountUse(f.OwnerSeat, c)!;
        if (f.DamageTargetObtain is not null) throw new InvalidOperationException("Obtain cannot pay twice.");
        f = f with { DamageTargetObtain = new(f.InstructionIndex, f.OwnerSeat, c.TargetSeat!.Value,
            c.ParentFrameId, c.DamageFrameId!.Value, use.Id, use.Action!.ActionId,
            f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash) };
        ReplaceRuntimeTop(f);
        var choices = DamageTargetObtainChoices(f);
        PublishDamageTargetObtainPrompt(f, f.OwnerSeat, "请选择伤害目标区域里的一张牌获得。", choices, true);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private void PublishDamageTargetObtainPrompt(ProgramSkillFrame f, int chooser, string text, IReadOnlyList<PromptChoice> choices, bool privateCards)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, chooser, text,
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = privateCards, TargetSeat = f.DamageTargetObtain!.VictimSeat,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description), Choices = Array.AsReadOnly(choices.ToArray()) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveDamageTargetObtainChoice(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Obtain choice lost its parent.");
        var d = f.DamageTargetObtain ?? throw new InvalidOperationException("Obtain choice lost its draft.");
        var duel = d.Stage == ProgramDamageTargetObtainStage.SelectingDuelTarget;
        var choices = duel ? DamageTargetDuelChoices(f) : DamageTargetObtainChoices(f);
        if (!IsValidDamageTargetObtain(f) || _pendingDecision?.PlayerSeat != (duel ? d.VictimSeat : d.OwnerSeat) ||
            !choices.Any(c => c.Id == selected.Id && c.Cards.SequenceEqual(selected.Cards) && c.Targets.SequenceEqual(selected.Targets) &&
                c.Parameters.OrderBy(x => x.Key).SequenceEqual(selected.Parameters.OrderBy(x => x.Key))))
            throw new InvalidOperationException("Obtain choice must name its exact current published slot or legal target.");
        ClearPendingDecision();
        if (duel) { IssueDamageTargetDuel(f, selected.Targets.Single()); return; }
        if (d.Stage != ProgramDamageTargetObtainStage.SelectingCard || d.Receipt is not null)
            throw new InvalidOperationException("Obtain payment was already resolved.");
        if (!HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || !CanOfferDamageTargetObtain(f.OwnerSeat, f.WindowContext!))
        { CancelProgramBindingAndCleanup(f, "获得前技能来源或参与者已失效。"); return; }
        var from = new CardLocation(Enum.Parse<CardZoneKind>(selected.Parameters["source-zone"]), d.VictimSeat);
        var card = _cardZones.CardsAt(from)[int.Parse(selected.Parameters["slot-index"], CultureInfo.InvariantCulture)];
        var to = card.IsGeneralWeapon && from.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.Hand(f.OwnerSeat);
        ReplaceRuntimeTop(f with { DamageTargetObtain = d with { Stage = ProgramDamageTargetObtainStage.ObtainChildren }, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
        MoveCard(card, from, to, new(DamageTargetObtainReason), record =>
        {
            var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { DamageTargetObtain = current.DamageTargetObtain! with
            { ObtainMovementSequence = record.Sequence, Receipt = record.To == CardLocation.Hand(d.OwnerSeat) ?
                new(record.Sequence,record.CardId,record.CardKind,record.From,record.To,record.Reason.Value,EquipmentCatalog.IsEquipment(record.CardKind)) : null } });
        });
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(f.Id);
    }
    private bool IsValidDamageTargetObtain(ProgramSkillFrame f)
    {
        if (f.DamageTargetObtain is not { } d || f.TriggerId is null || f.InstructionIndex != 1 || d.InstructionIndex != f.InstructionIndex ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0 } c ||
            d.OwnerSeat != f.OwnerSeat || d.VictimSeat == d.OwnerSeat || !IsValidPlayerSeat(d.VictimSeat) || c.TargetSeat != d.VictimSeat || c.SourceSeat != d.OwnerSeat ||
            c.ParentFrameId != d.DamageWindowId || c.DamageFrameId != d.DamageFrameId || d.SkillId != f.SkillId ||
            d.BindingId != GetProgramBindingId(f) || d.SkillInstanceId != f.SkillInstanceId || d.GameplayHash != f.GameplayHash ||
            ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Instructions is not [{Op:SkillProgramEffectOp.ObtainDamageTargetCardAndResolveCategory}] ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w => w.Id == d.DamageWindowId) is not { } window || window.ParentFrameId != d.DamageFrameId || window.SourceSeat != d.OwnerSeat || window.TargetSeat != d.VictimSeat ||
            _resolutionStack.OfType<DamageFrame>().SingleOrDefault(x=>x.Id==d.DamageFrameId) is not { } damage || damage.ParentFrameId!=d.OuterCardUseFrameId || damage.SourceSeat!=d.OwnerSeat || damage.TargetSeat!=d.VictimSeat || damage.Amount<=0 ||
            LifecycleCardUse(d.OuterCardUseFrameId)?.Action is not {Type:CardActionType.Use} a || a.ActionId != d.OuterActionId || a.ActorSeat != d.OwnerSeat || !IsSlashCard(a.EffectiveKind)) return false;
        if (d.Stage == ProgramDamageTargetObtainStage.SelectingCard) return d.Receipt is null && d.ObtainMovementSequence is null;
        if (d.ObtainMovementSequence is not { } sequence || _cardMovements.SingleOrDefault(m=>m.Sequence==sequence) is not { } movement ||
            movement.From.OwnerSeat!=d.VictimSeat || !DamageTargetMountZones.Contains(movement.From.Zone) || movement.Reason.Value!=DamageTargetObtainReason) return false;
        if (d.Receipt is not { } r) return (d.Stage is ProgramDamageTargetObtainStage.ObtainChildren or ProgramDamageTargetObtainStage.Complete) &&
            movement.CardKind==CardKind.GeneralWeapon && movement.From.Zone==CardZoneKind.Equipment && movement.To==CardLocation.OutsideGame;
        if (r.MovementSequence!=sequence)return false;
        return d.Stage != ProgramDamageTargetObtainStage.SelectingCard && r.Reason == DamageTargetObtainReason &&
            r.From.OwnerSeat == d.VictimSeat && DamageTargetMountZones.Contains(r.From.Zone) && r.To == CardLocation.Hand(d.OwnerSeat) &&
            r.IsEquipment == EquipmentCatalog.IsEquipment(r.PrintedKind) && _cardMovements.Any(m => m.Sequence == r.MovementSequence &&
                m.CardId == r.CardId && m.CardKind == r.PrintedKind && m.From == r.From && m.To == r.To && m.Reason.Value == r.Reason);
    }
    private bool ResumeDamageTargetObtain(long id)
    {
        var f = GetActiveProgramFrame(id);
        if (f.DamageTargetObtain is not { } d || d.Stage is ProgramDamageTargetObtainStage.SelectingCard or ProgramDamageTargetObtainStage.SelectingDuelTarget or ProgramDamageTargetObtainStage.DuelIssued or ProgramDamageTargetObtainStage.Complete) return false;
        if (!IsValidDamageTargetObtain(f)) throw new InvalidOperationException("Obtain tail lost its exact actual receipt.");
        if (f.PendingMovementContinuation is not null)
        {
            if (TryBeginCardsMovedProgramWindow()) return true;
            ReplaceRuntimeTop(f with { PendingMovementContinuation = null }); f = GetActiveProgramFrame(id);
        }
        if (d.Stage == ProgramDamageTargetObtainStage.DrawChildren || d.Receipt is null || !_players[d.VictimSeat].IsAlive || _winner != Winner.None)
        { FinishDamageTargetObtain(f); return true; }
        if (!d.Receipt.IsEquipment)
        {
            ReplaceRuntimeTop(f with { DamageTargetObtain = d with { Stage = ProgramDamageTargetObtainStage.DrawChildren }, PendingMovementContinuation = new(d.OwnerSeat,0,null) });
            DrawCards(_players[d.VictimSeat], 1, log:true, reason:new(DamageTargetBenefitReason));
            if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(id);
            return true;
        }
        if (!_players[d.OwnerSeat].IsAlive) { FinishDamageTargetObtain(f); return true; }
        f = f with { DamageTargetObtain = d with { Stage = ProgramDamageTargetObtainStage.SelectingDuelTarget } };
        ReplaceRuntimeTop(f);
        var choices = DamageTargetDuelChoices(f);
        if (choices.Count == 0) { FinishDamageTargetObtain(f); return true; }
        PublishDamageTargetObtainPrompt(f, d.VictimSeat, "利驭：请指定吕布使用【决斗】的另一名角色。", choices, false);
        return true;
    }
    private void FinishDamageTargetObtain(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f with { DamageTargetObtain = f.DamageTargetObtain! with { Stage = ProgramDamageTargetObtainStage.Complete }, PendingMovementContinuation = null });
        AdvanceRuntimeProgram(f.Id);
    }
    private bool CanIssueDamageTargetDuel(ProgramSkillFrame f, int target)
    {
        var d=f.DamageTargetObtain!;
        return _winner==Winner.None && _players[d.VictimSeat].IsAlive && target!=d.VictimSeat && target!=d.OwnerSeat &&
            IsValidPlayerSeat(target) && _players[target].IsAlive && _players[d.OwnerSeat].IsAlive &&
            !IsCardUseForbidden(d.OwnerSeat,CardKind.Duel,CardActionType.Use) &&
            !HasTurnCardTargetRestriction(d.OwnerSeat,SkillProgramCardTargetRestriction.SelfOnly) &&
            !IsCardTargetProhibited(_players[target],CardKind.Duel,Suit.None) && !IsDirectedCardTargetProhibited(d.OwnerSeat,target,CardKind.Duel);
    }
    private IReadOnlyList<PromptChoice> DamageTargetDuelChoices(ProgramSkillFrame f) => _players.Where(p => CanIssueDamageTargetDuel(f,p.Seat))
        .Select(p => new PromptChoice(new ChoiceId($"damage-target-duel.frame-{f.Id}.seat-{p.Seat}"),$"指定 {p.Name}。",[],[p.Seat],
            new Dictionary<string,string> { ["program-action"]="damage-target-duel", ["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray();
    private void IssueDamageTargetDuel(ProgramSkillFrame f,int target)
    {
        var d=f.DamageTargetObtain!;
        if(d is not {Stage:ProgramDamageTargetObtainStage.SelectingDuelTarget,Receipt:{IsEquipment:true} r} || !IsValidDamageTargetObtain(f) || !CanIssueDamageTargetDuel(f,target))
            throw new InvalidOperationException("Duel issuance lost its paid exact legal producer.");
        ReplaceRuntimeTop(f with { DamageTargetObtain=d with {Stage=ProgramDamageTargetObtainStage.DuelIssued} });
        // Suspend the actual outer attack on its own owner. No duplicate pending attack or use-ID sidecar.
        UpdateCardAttackState(d.OuterCardUseFrameId,s => s! with {Active=false});
        var origin=new ProgramDamageTargetDuelOrigin(f.Id,f.InstructionIndex,r.MovementSequence,d.OuterCardUseFrameId,d.DamageWindowId,d.DamageFrameId,d.OuterActionId,
            d.OwnerSeat,d.VictimSeat,target,f.SkillId,d.BindingId,f.SkillInstanceId,f.GameplayHash,_turnNumber);
        var card=new Card(0,CardKind.Duel,Suit.None,0);
        var use=BeginCardUse(card,d.OwnerSeat,[target],CardKind.Duel,physicalCardIds:[],damageTargetDuelOrigin:origin);
        BeginJizhiOrNullificationWindow(use,card,d.OwnerSeat,[target],LegalActionKind.Duel,playedCardKind:CardKind.Duel);
    }
    private bool IsDamageTargetDuelUse(long id) => LifecycleCardUse(id) is {DamageTargetDuelOrigin:not null,SelectedActorDuelOrigin:null,CardId:0,CardKind:CardKind.Duel,PhysicalCardIds.Count:0};
    private bool IsIssuedZeroEntityDuel(long id) => IsSelectedActorDuelUse(id) || IsDamageTargetDuelUse(id) || IsDualColorDuelUse(id) || IsConditionalDiscardDuelUse(id);
    private IReadOnlyList<int> DamageTargetDuelOuterProcessing(long useId)
    {
        var use=LifecycleCardUse(useId);
        if(use?.DamageTargetDuelOrigin is not { } o || !IsDamageTargetDuelUse(useId) ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==o.ParentProgramFrameId) is not { } parent ||
            !IsValidDamageTargetObtain(parent) || parent.DamageTargetObtain is not {Stage:ProgramDamageTargetObtainStage.DuelIssued,Receipt:{ } r} d ||
            r.MovementSequence!=o.ReceiptSequence || d.OuterCardUseFrameId!=o.OuterCardUseFrameId ||
            LifecycleCardUse(o.OuterCardUseFrameId) is not {CardAttack.Active:false} outer)
            throw new InvalidOperationException("Nested Duel processing lost its exact suspended outer Use.");
        return (outer.PhysicalCardIds ?? []).Where(id=>_cardZones.GetLocation(id)==CardLocation.Processing).ToArray();
    }
    private void ReturnDamageTargetDuel(CardUseFrame use)
    {
        if(use.DamageTargetDuelOrigin is not {AttackStarted:false} o)return;
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id!=o.ParentProgramFrameId || f.DamageTargetObtain?.Stage!=ProgramDamageTargetObtainStage.DuelIssued || !MatchesDamageTargetDuelParent(f,o))
            throw new InvalidOperationException("Nullified Duel lost its typed paid parent.");
        CompleteDamageTargetDuelReturn(f);
    }
    private bool MatchesDamageTargetDuelParent(ProgramSkillFrame f,ProgramDamageTargetDuelOrigin o) =>
        IsValidDamageTargetObtain(f) && f.DamageTargetObtain is {Stage:ProgramDamageTargetObtainStage.DuelIssued,Receipt:{IsEquipment:true} r} d &&
        o.ParentProgramFrameId==f.Id && o.InstructionIndex==f.InstructionIndex && o.ReceiptSequence==r.MovementSequence &&
        o.OuterCardUseFrameId==d.OuterCardUseFrameId && o.OuterActionId==d.OuterActionId && o.DamageWindowId==d.DamageWindowId && o.DamageFrameId==d.DamageFrameId &&
        o.InitialActorSeat==d.OwnerSeat && o.VictimChooserSeat==d.VictimSeat && IsValidPlayerSeat(o.InitialTargetSeat) && o.InitialTargetSeat!=d.OwnerSeat && o.InitialTargetSeat!=d.VictimSeat &&
        o.SkillId==f.SkillId && o.BindingId==d.BindingId && o.SkillInstanceId==f.SkillInstanceId && o.GameplayHash==f.GameplayHash && o.TurnNumber==_turnNumber;
    private void CompleteDamageTargetDuelReturn(ProgramSkillFrame f)
    {
        if(!IsValidDamageTargetObtain(f) || f.DamageTargetObtain is not {Stage:ProgramDamageTargetObtainStage.DuelIssued} d || LifecycleCardUse(d.OuterCardUseFrameId)?.CardAttack is not {Active:false})
            throw new InvalidOperationException("Completed Duel lost its suspended outer attack.");
        UpdateCardAttackState(d.OuterCardUseFrameId,s => s! with {Active=true});
        FinishDamageTargetObtain(f);
    }
    private void AssertDamageTargetObtainState(ProgramSkillFrame f)
    {
        if(f.DamageTargetObtain is not { } d)return;
        if(!IsValidDamageTargetObtain(f))throw new InvalidOperationException("Obtain state lost its actual producer or receipt.");
        if(d.Stage is ProgramDamageTargetObtainStage.SelectingCard or ProgramDamageTargetObtainStage.SelectingDuelTarget &&
            (_resolutionStack.LastOrDefault()?.Id!=f.Id || _pendingDecision is not {Kind:DecisionKind.ProgramTrigger} p ||
                p.PlayerSeat!=(d.Stage==ProgramDamageTargetObtainStage.SelectingCard?d.OwnerSeat:d.VictimSeat)))
            throw new InvalidOperationException("Obtain selection lost its actual chooser.");
    }
    private void AssertDamageTargetDuels()
    {
        foreach(var use in _resolutionStack.OfType<CardUseFrame>().Where(u=>u.DamageTargetDuelOrigin is not null))
        {
            var o=use.DamageTargetDuelOrigin!;var index=_resolutionStack.FindIndex(x=>x.Id==use.Id);
            if(index<1 || _resolutionStack[index-1] is not ProgramSkillFrame f || !IsValidDamageTargetObtain(f) || f.DamageTargetObtain is not {Stage:ProgramDamageTargetObtainStage.DuelIssued,Receipt:{IsEquipment:true} r} d ||
                o.ParentProgramFrameId!=f.Id || o.InstructionIndex!=f.InstructionIndex || o.ReceiptSequence!=r.MovementSequence || o.OuterCardUseFrameId!=d.OuterCardUseFrameId ||
                o.DamageWindowId!=d.DamageWindowId || o.DamageFrameId!=d.DamageFrameId || o.OuterActionId!=d.OuterActionId ||
                o.InitialActorSeat!=d.OwnerSeat || o.VictimChooserSeat!=d.VictimSeat || o.InitialTargetSeat==d.OwnerSeat || o.InitialTargetSeat==d.VictimSeat || !IsValidPlayerSeat(o.InitialTargetSeat) ||
                o.SkillId!=f.SkillId || o.BindingId!=d.BindingId || o.SkillInstanceId!=f.SkillInstanceId || o.GameplayHash!=f.GameplayHash || o.TurnNumber!=_turnNumber ||
                use.SelectedActorDuelOrigin is not null || use.CardId!=0 || use.CardKind!=CardKind.Duel || use.PhysicalCardIds?.Count!=0 ||
                use.Action is not {Type:CardActionType.Use,EffectiveKind:CardKind.Duel,PhysicalCards.Count:0,ConversionChain.Count:0,EffectiveSuit:Suit.None,EffectiveIsRed:false} ||
                LifecycleCardUse(d.OuterCardUseFrameId)?.CardAttack is not {Active:false})
                throw new InvalidOperationException("Issued damage-target Duel lost its exact paid origin.");
        }
    }
    private PromptChoice SelectAiDamageTargetObtain(PendingDecision decision, ProgramSkillFrame f)
    {
        if(f.DamageTargetObtain?.Stage!=ProgramDamageTargetObtainStage.SelectingDuelTarget)return SelectAiProgramOtherOwnedCardDiscard(decision,f);
        // The victim is choosing a harmful target; use that chooser's filtered public relationship model.
        var view=CreateSnapshot(decision.PlayerSeat);var hint=new SkillProgramAiHint(0,0,0,0,0,1,false,false);
        return decision.Choices.OrderByDescending(c=>_aiBrains[decision.PlayerSeat].ScoreProgramTarget(view,c.Targets.Single(),hint)).ThenBy(c=>c.Targets.Single()).First();
    }
    private sealed partial class ProgramSkillHost : IDamageTargetObtainProgramHost
    {
        public SkillProgramStepOutcome ObtainDamageTargetCardAndResolveCategory(ProgramSkillFrame f)=>engine.BeginDamageTargetObtain(f);
        public bool CanContinuePaidDamageTargetObtain(ProgramSkillFrame f)=>f.DamageTargetObtain is {Receipt:not null} && engine.IsValidDamageTargetObtain(f) &&
            (engine._players[f.OwnerSeat].IsAlive || f.DamageTargetObtain is {Receipt.IsEquipment:false,Stage:ProgramDamageTargetObtainStage.DrawChildren or ProgramDamageTargetObtainStage.Complete});
    }
}
