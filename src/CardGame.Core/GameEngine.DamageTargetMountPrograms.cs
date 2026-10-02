using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static readonly CardZoneKind[] DamageTargetMountZones = [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment];
    private static bool IsPrintedMount(CardKind kind) => EquipmentCatalog.IsEquipment(kind) &&
        EquipmentCatalog.Get(kind).Slot is EquipmentSlot.OffensiveHorse or EquipmentSlot.DefensiveHorse;
    private CardUseFrame? ActualDamageTargetMountUse(int ownerSeat, ProgramSkillWindowContext context)
    {
        if (context is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, SourceSeat: { } source, TargetSeat: { } target } ||
            source != ownerSeat || ActiveDamageTrigger is not { } damage || damage.Id != context.ParentFrameId ||
            damage.ParentFrameId != context.DamageFrameId) return null;
        var attack = GetDamageTriggerAttack(damage);
        if (attack.IsSourceLess || !attack.DamageWasApplied || attack.SourceSeat != ownerSeat || attack.TargetSeat != target ||
            attack.EffectiveCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)) return null;
        // The real action is stored on the owning use (also for prepared multi-target/chain attacks).
        return _resolutionStack.OfType<CardUseFrame>().LastOrDefault(u => u.Id == attack.ResolutionId &&
            u.Action is { Type: CardActionType.Use } a && a.ActorSeat == ownerSeat &&
            a.EffectiveKind == attack.EffectiveCardKind);
    }
    private IReadOnlyList<PromptChoice> DamageTargetMountChoices(long frameId, int ownerSeat, int targetSeat)
    {
        if (ownerSeat != targetSeat)
            return BuildOtherOwnedCardDiscardChoices(frameId, ownerSeat, DamageTargetMountZones,
                new CardMoveReason("skill-program.damage-target-mount.discard"))
                .Where(c => c.Targets.SequenceEqual([targetSeat]))
                .Select(c => c with { Parameters = new Dictionary<string,string>(c.Parameters) { ["program-action"] = "damage-target-mount" } }).ToArray();
        // The current text permits the actual victim to be this same source through chain/transfer.
        // Keep the mature only-other helper unchanged; this opt-in alone adds self-owned slots.
        var result = new List<PromptChoice>();
        if (!_players[targetSeat].IsAlive) return result;
        foreach (var zone in DamageTargetMountZones)
        {
            var cards = _cardZones.CardsAt(new(zone,targetSeat));
            for (var slot=0;slot<cards.Count;slot++)
            {
                var card=cards[slot]; var hidden=zone==CardZoneKind.Hand;
                result.Add(new(new ChoiceId($"damage-target-mount.frame-{frameId}.self.{zone}.{slot}"),
                    hidden ? $"弃置自己的第{slot+1}个手牌牌位。" : $"弃置自己的【{card.DisplayName}】。",
                    hidden?[]:[card.Id],[targetSeat],new Dictionary<string,string>
                    { ["program-action"]="damage-target-mount",["frame-id"]=frameId.ToString(CultureInfo.InvariantCulture),
                      ["card-owner-seat"]=targetSeat.ToString(CultureInfo.InvariantCulture),["source-zone"]=zone.ToString(),
                      ["slot-index"]=slot.ToString(CultureInfo.InvariantCulture),["move-reason"]="skill-program.damage-target-mount.discard" }));
            }
        }
        return result;
    }
    private bool CanOfferDamageTargetMount(int ownerSeat, ProgramSkillWindowContext context) =>
        ActualDamageTargetMountUse(ownerSeat, context) is not null && context.TargetSeat is { } seat &&
        IsValidPlayerSeat(seat) && _players[seat].IsAlive && DamageTargetMountChoices(1, ownerSeat, seat).Count > 0;
    private SkillProgramStepOutcome BeginDamageTargetMount(ProgramSkillFrame f)
    {
        var active = GetActiveProgramFrame(f.Id);
        var context = active.WindowContext ?? throw new InvalidOperationException("Mount discard lost its damage window.");
        var use = ActualDamageTargetMountUse(f.OwnerSeat, context);
        if (use?.Action is not { } action || !CanOfferDamageTargetMount(f.OwnerSeat, context)) return SkillProgramStepOutcome.Continue;
        if (active.DamageTargetMount is not null) throw new InvalidOperationException("Mount discard cannot pay twice.");
        var target = context.TargetSeat!.Value;
        ReplaceRuntimeTop(active with { DamageTargetMount = new(active.InstructionIndex, active.OwnerSeat, target,
            context.ParentFrameId, context.DamageFrameId!.Value, use.Id, action.ActionId,
            active.SkillId, GetProgramBindingId(active), active.SkillInstanceId, active.GameplayHash) });
        var choices = DamageTargetMountChoices(f.Id, f.OwnerSeat, target);
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, f.OwnerSeat,
            $"【{skill.Name}】弃置 {_players[target].Name} 区域里的一张牌；若为坐骑牌，你获得之。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [target], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = target,
          SkillPrompt = new(f.SkillId, skill.Name, skill.Name + " · 弃置牌", skill.Description),
          Choices = Array.AsReadOnly(choices.ToArray()) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private void ResolveDamageTargetMount(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Mount discard lost its program.");
        var draft = f.DamageTargetMount ?? throw new InvalidOperationException("Mount discard lost its payment draft.");
        if (draft.Receipt is not null || _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } p || p.PlayerSeat != f.OwnerSeat ||
            choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture) ||
            !DamageTargetMountChoices(f.Id,f.OwnerSeat,draft.TargetSeat).Any(c => c.Id == choice.Id &&
                c.Cards.SequenceEqual(choice.Cards) && c.Targets.SequenceEqual(choice.Targets) && c.Parameters.OrderBy(x=>x.Key).SequenceEqual(choice.Parameters.OrderBy(x=>x.Key))))
            throw new InvalidOperationException("Mount discard must name its exact published opaque slot.");
        ClearPendingDecision();
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId) ||
            !CanOfferDamageTargetMount(f.OwnerSeat,f.WindowContext!))
        { CancelProgramBindingAndCleanup(f,"付款前技能来源或伤害目标已失效。"); return; }
        var source = new CardLocation(Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]), draft.TargetSeat);
        var card = _cardZones.CardsAt(source)[int.Parse(choice.Parameters["slot-index"],CultureInfo.InvariantCulture)];
        var destination = card.IsGeneralWeapon && source.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.DiscardPile;
        var reason = new CardMoveReason("skill-program.damage-target-mount.discard");
        ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat,0,null) });
        var batch = BeginCardMovementBatch([source],[destination]);
        var records = new List<CardMovementRecord>(1); var committed = false;
        try
        {
            _cardZones.Move(card.Id,source,destination);
            records.Add(RecordMovement(card,source,destination,reason, record =>
            {
                var current = GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(current with { DamageTargetMount = draft with { Receipt =
                    new(record.Sequence,record.CardId,record.CardKind,record.From,record.To,record.Reason.Value,IsPrintedMount(card.Kind)) } });
            }));
            ResolveEquipmentSkillGrant(card,source,destination);
            ClearJudgmentEffectiveKindAfterMove(card,source,destination);
            ResolveSilverLionRemoval(card,source,reason);
            ResolveWoodenOxMove(card,source,destination);
            CollectDiscardPhaseHandDiscard(card,source,destination);
            committed = true;
        }
        finally { CompleteCardMovementBatch(batch,records,committed); }
        if (!TryBeginCardsMovedProgramWindow())
        { var current=GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(current with { PendingMovementContinuation=null }); AdvanceRuntimeProgram(f.Id); }
    }
    private bool IsValidDamageTargetMount(ProgramSkillFrame f)
    {
        if (f.DamageTargetMount is not { } d || f.WindowContext is not { Window:SkillProgramTriggerWindow.AfterDamageApplied,Amount:>0 } c ||
            d.InstructionIndex != f.InstructionIndex || d.InstructionIndex < 1 || d.OwnerSeat != f.OwnerSeat || d.TargetSeat != c.TargetSeat ||
            d.DamageWindowId != c.ParentFrameId || d.DamageFrameId != c.DamageFrameId || c.SourceSeat != f.OwnerSeat ||
            !IsValidPlayerSeat(d.TargetSeat) || d.SkillId != f.SkillId || d.BindingId != GetProgramBindingId(f) ||
            d.SkillInstanceId != f.SkillInstanceId || d.GameplayHash != f.GameplayHash) return false;
        var plan=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!);
        if (plan.Instructions.Count != 1 || plan.Instructions[0].Op != SkillProgramEffectOp.DiscardDamageTargetAndClaimMount ||
            plan.Trigger?.Subject != SkillProgramTriggerSubject.DamageSource || plan.Trigger.Optional != true) return false;
        var use=_resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u=>u.Id==d.CardUseFrameId);
        if (use?.Action is not {Type:CardActionType.Use} action || action.ActionId!=d.CardActionId || action.ActorSeat!=d.OwnerSeat ||
            !IsSlashCard(action.EffectiveKind)) return false;
        if(d.Receipt is not { } r) return !d.ClaimIssued && d.ClaimMovementSequence is null;
        var actual=_cardMovements.SingleOrDefault(m=>m.Sequence==r.MovementSequence);
        return actual is not null && actual.CardId==r.CardId && actual.CardKind==r.PrintedKind && actual.From==r.From && actual.To==r.To &&
            actual.Reason.Value==r.Reason && r.Reason=="skill-program.damage-target-mount.discard" &&
            r.From.OwnerSeat==d.TargetSeat && DamageTargetMountZones.Contains(r.From.Zone) &&
            r.To==(r.PrintedKind==CardKind.GeneralWeapon && r.From.Zone==CardZoneKind.Equipment?CardLocation.OutsideGame:CardLocation.DiscardPile) &&
            r.IsMount==IsPrintedMount(r.PrintedKind) &&
            (d.ClaimMovementSequence is null ? !d.ClaimIssued : d.ClaimIssued && _cardMovements.Any(m=>
                m.Sequence==d.ClaimMovementSequence && m.CardId==r.CardId && m.CardKind==r.PrintedKind &&
                m.From==r.To && m.To==CardLocation.Hand(d.OwnerSeat) && m.Reason.Value=="skill-program.damage-target-mount.claim"));
    }
    private bool ResumeDamageTargetMount(long id)
    {
        var f=GetActiveProgramFrame(id);
        if(f.DamageTargetMount is not {Receipt:not null} d)return false;
        if(!IsValidDamageTargetMount(f))throw new InvalidOperationException("Mount claim lost its exact paid producer.");
        if(f.PendingMovementContinuation is not null)
        { if(TryBeginCardsMovedProgramWindow())return true; ReplaceRuntimeTop(f with {PendingMovementContinuation=null}); f=GetActiveProgramFrame(id); }
        if(!d.ClaimIssued && _players[f.OwnerSeat].IsAlive && d.Receipt.IsMount && d.Receipt.To==CardLocation.DiscardPile &&
            _cardZones.GetLocation(d.Receipt.CardId)==d.Receipt.To)
        {
            ReplaceRuntimeTop(f with {DamageTargetMount=d with {ClaimIssued=true},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            var card=_cardZones.CardsAt(d.Receipt.To).Single(c=>c.Id==d.Receipt.CardId);
            MoveCard(card,d.Receipt.To,CardLocation.Hand(f.OwnerSeat),new CardMoveReason("skill-program.damage-target-mount.claim"), record=>
            {var current=GetActiveProgramFrame(id);ReplaceRuntimeTop(current with{DamageTargetMount=current.DamageTargetMount! with{ClaimMovementSequence=record.Sequence}});});
            if(TryBeginCardsMovedProgramWindow())return true;
            f=GetActiveProgramFrame(id);ReplaceRuntimeTop(f with {PendingMovementContinuation=null});
            return ResumeDamageTargetMount(id);
        }
        f=GetActiveProgramFrame(id);ReplaceRuntimeTop(f with {DamageTargetMount=null});
        AdvanceRuntimeProgram(id); return true;
    }
    private void AssertDamageTargetMountState(ProgramSkillFrame f)
    {
        if(f.DamageTargetMount is not { } d)return;
        if(!IsValidDamageTargetMount(f))throw new InvalidOperationException("Mount payment lost its exact identity or actual record.");
        if(d.Receipt is null && (f.PendingMovementContinuation is not null || !ReferenceEquals(f,_resolutionStack.LastOrDefault()) ||
            _pendingDecision is not {Kind:DecisionKind.ProgramTrigger} p || p.PlayerSeat!=f.OwnerSeat || p.Choices.Count==0 ||
            p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")!="damage-target-mount")))
            throw new InvalidOperationException("Unpaid mount draft lost its owning prompt.");
    }
    private sealed partial class ProgramSkillHost : IDamageTargetMountProgramHost
    {
        public SkillProgramStepOutcome DiscardDamageTargetAndClaimMount(ProgramSkillFrame f)=>engine.BeginDamageTargetMount(f);
        public bool CanContinuePaidDamageTargetMount(ProgramSkillFrame f)=>f.DamageTargetMount?.Receipt is not null &&
            engine._players[f.OwnerSeat].IsAlive && engine.IsValidDamageTargetMount(f);
    }
}
