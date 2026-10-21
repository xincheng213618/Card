using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed partial record CardUseFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public RoundOnceCardlessBasicUseReceipt? RoundOnceCardlessBasicUse{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public RoundOnceCardlessBasicUseReceipt? RoundOnceCardlessBasicResponse{get;init;}
}
public sealed partial record ProgramSkillFrame
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public RoundOnceCardlessBasicUseReceipt? RoundOnceCardlessBasicResponse{get;init;}
}
public sealed partial class GameEngine
{
 private IEnumerable<(CardConversionSource Source,SkillProgramViewAs Rule)> RoundOnceCardlessRules(CharacterState actor,CardKind? output=null,bool forResponse=false)
 {
  foreach(var instance in GetSkillBindingShard(actor).ProgramInstances)
  foreach(var rule in instance.Program.ViewAs)
   if(rule.RoundOnceCardlessBasic is not null&&(output is null||rule.OutputKind==output)&&rule.Condition.Evaluate(CreateSkillContext(actor))&&CanUseRoundOnceCardlessBasic(actor,instance,rule,forResponse)&&!IsCardUseForbidden(actor.Seat,rule.OutputKind,CardActionType.Use))yield return(new(instance.SkillId,rule.Id,actor.Seat,instance.SkillInstanceId),rule);
 }
 private IReadOnlyList<LegalAction> RoundOnceCardlessPlayActions(CharacterState actor)
 {
  if(!actor.IsAlive||(_phase!=TurnPhase.Play||actor.Seat!=_currentSeat)&&!IsKeBiNengPingduanBasicQuery(actor) && !IsMouKrLiwenUseQuery(actor) && !IsMouLuSuUseQuery(actor))return [];
  var result=new List<LegalAction>();
  foreach(var(source,rule)in RoundOnceCardlessRules(actor,CardKind.Slash))
  {
   var card=new Card(0,CardKind.Slash,Suit.None,0);var targets=GetFangtianOrderedSlashTargets(actor,card,source,effectiveKind:CardKind.Slash);
   foreach(var target in targets)result.Add(new(LegalActionKind.Slash,0,target.Seat,DescribeConversion(source,$"视为使用【杀】对 {target.Name} 使用"),CardKind.Slash){ConversionSource=source});
   AddProgramTargetCountSlashActions(result,actor,card,targets,card.DisplayName,CardKind.Slash,source,null);
  }
  AddNextCardTargetAdjustmentActions(result,actor);AddNextActualUseAdjustmentActions(result,actor);return Array.AsReadOnly(result.Select(action=>action with{RoundOnceCardlessBasicUse=ViewAsRule(action.ConversionSource!)!.RoundOnceCardlessBasic}).ToArray());
 }
 private bool TrySubmitRoundOnceCardlessPlay(PlayCardCommand command,out CommandResult result)
 {
  result=null!;if(command.CardId!=0||command.ConversionSource is not {} source||ViewAsRule(source)?.RoundOnceCardlessBasic is null)return false;
  var actor=_players[command.ActorSeat];var action=RoundOnceCardlessPlayActions(actor).SingleOrDefault(a=>a.ConversionSource==source&&a.PlayedCardKind==command.PlayedCardKind&&a.TargetCardId==command.TargetCardId&&a.TargetSeats.SequenceEqual(command.TargetSeats??[]));
  if(action is null||command.AdditionalConversionSources is {Count:>0}){result=Reject(CommandErrorCode.IllegalAction,"Cardless basic changed its exact published source, ordinary Slash or ordered native targets.");return true;}
  result=Accept(()=>{ClearPendingDecision();SelectUseConversion(action);ExecuteRoundOnceCardlessPlay(actor,action);AdvanceRulesAndPublishState();if(_options.AdvanceAfterHumanCommands)AdvanceToHumanBoundary();});return true;
 }
 private bool TryExecuteRoundOnceCardlessPlay(CharacterState actor,LegalAction action)
 {
  if(action.CardId!=0||action.ConversionSource is not {} source||ViewAsRule(source)?.RoundOnceCardlessBasic is null)return false;
  if(!RoundOnceCardlessPlayActions(actor).Any(a=>a.Kind==action.Kind&&a.ConversionSource==source&&a.PlayedCardKind==action.PlayedCardKind&&a.TargetCardId==action.TargetCardId&&a.TargetSeats.SequenceEqual(action.TargetSeats)))throw new InvalidOperationException("Cardless Slash lost its exact published action before payment.");
  ExecuteRoundOnceCardlessPlay(actor,action);return true;
 }
 private void ExecuteRoundOnceCardlessPlay(CharacterState actor,LegalAction action)
 {
  var source=action.ConversionSource!;if(action.PlayedCardKind!=CardKind.Slash)throw new InvalidOperationException("Cardless Play requires ordinary Slash.");
  if(IsTargetAdjustmentAction(actor,action))_selectedNextCardTargetSeats=action.TargetSeats;
  ConsumeRoundOnceCardlessBasic(source,false);var card=new Card(0,CardKind.Slash,Suit.None,0);
  var id=BeginCardUse(card,actor.Seat,action.TargetSeats,CardKind.Slash,physicalCardIds:[],conversionSource:source);IssueRoundOnceCardlessNativeUse(id,source);
  var targets=LifecycleCardUse(id)!.TargetSeats;if(targets.Count==0)throw new InvalidOperationException("Cardless Slash requires its actual selected targets.");
  var attack=new CardAttackHandle(this,id,actor.Seat,targets[0],card:null,damageAmount:1+actor.AlcoholDamageBonus,playedCardKind:CardKind.Slash,ignoresArmor:HasCardArmorBypass(actor,_players[targets[0]],CardKind.Slash));
  CaptureProgramAlcoholConsumption(id,actor);actor.HasAlcoholEffect=false;ActiveCardAttack=attack;CaptureProgramAdjustedSlashBaseDamage(attack);
  if(!IgnoresProgramSlashLimit(actor,source)&&LifecycleCardUse(id)?.UnlimitedUse!=true)RecordSlashUseDebit(id,actor.Seat);MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,CardKind.Slash);
  AdvanceEventRulesAndQueueFact(new CardUsedEvent(0,CardKind.Slash,actor.Seat,targets[0]));TryMarkProgramUseCommitted(id);
  if(!TryBeginProgramCardWindow(attack,LifecycleCardUse(id)!.Action!,SkillProgramTriggerWindow.CardUseCommitted,targets,ProgramCardContinuation.CommittedSlash))BeginSlashTargetResolution(attack);
 }
 private void IssueRoundOnceCardlessNativeUse(long id,CardConversionSource source)
 {
  var use=LifecycleCardUse(id)??throw new InvalidOperationException("Cardless native use has no actual owning frame.");var action=use.Action??throw new InvalidOperationException("Cardless native use has no allocated action.");
  var receipt=IssueRoundOnceCardlessBasicUse(use,action,source);ReplaceRuntimeFrame(id,use with{RoundOnceCardlessBasicUse=receipt});
 }
 private bool IsRoundOnceCardlessBasicUse(long id)
 {
  var use=LifecycleCardUse(id);if(use?.RoundOnceCardlessBasicUse is not {} receipt||use.CardId!=0||use.PhysicalCardIds is not {Count:0}||use.Action is not {} action)return false;
  var original=action;
  if(use.CurrentSlashFirePolicy is {} fire)
  {if(fire.OriginalAction.ActionId!=receipt.CardActionId||fire.OriginalAction.Type!=CardActionType.Use||fire.OriginalAction.EffectiveKind!=receipt.EffectiveKind)return false;AssertCurrentSlashFirePolicy(use);original=fire.OriginalAction;}
  if(!IsIssuedRoundOnceCardlessBasicUse(receipt,use,original))
  {
   var declared=CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Where(e=>e.Action.ActionId==receipt.CardActionId&&e.Action.ActorSeat==receipt.ActorSeat&&e.Action.EffectiveKind==receipt.EffectiveKind).Select(e=>e.Action).FirstOrDefault();
   if(declared is null||!IsIssuedRoundOnceCardlessBasicUse(receipt,use,declared)||!MatchesDeclaredActualDamageUse(use,action.ActorSeat,receipt.ProviderSeat))return false;
  }
  return use.SourceSeat==action.ActorSeat&&action.ProviderSeat==receipt.ProviderSeat&&(use.CardKind==receipt.EffectiveKind||use.CurrentSlashFirePolicy is not null);
 }
 private Card RoundOnceCardlessNativeRepresentation(long id)
 {var use=LifecycleCardUse(id);if(use is null||!IsRoundOnceCardlessBasicUse(id))throw new InvalidOperationException("Logical cardless Slash lacks its exact native accepted source and paid issuance.");return new(0,use.CardKind,Suit.None,0);}
    private IReadOnlyList<PromptChoice> RoundOnceCardlessForcedSlashChoices(CharacterState actor, CharacterState target,
        long originalId, bool qinglong)
    {
        if (qinglong ? ActiveQinglongCrescentBlade is not { } blade || blade.Attack.ResolutionId != originalId ||
                blade.Attack.SourceSeat != actor.Seat || blade.Attack.TargetSeat != target.Seat || !SameAttackOwner(ActiveCardAttack, blade.Attack) :
            ActiveBorrowedSword is not { } borrowed || borrowed.ResolutionId != originalId || borrowed.WeaponOwnerSeat != actor.Seat ||
                borrowed.SlashTargetSeat != target.Seat || borrowed.ActiveAttack is not null || GetWeapon(actor) is null)
            return [];
        var result = new List<PromptChoice>();
        foreach (var (source, rule) in RoundOnceCardlessForcedSlashRules(actor, target, qinglong))
        {
            var kind = rule.OutputKind;
            var parameters = new Dictionary<string, string> { [qinglong ? "action" : "response"] = "round-once-cardless-forced-slash",
                ["output-kind"] = kind.ToString(), ["original-frame-id"] = originalId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["forced-use"] = qinglong ? "qinglong" : "borrowed-sword" };
            AddConversionParameters(parameters, source);
            result.Add(new(new ChoiceId($"round-cardless.forced-{parameters["forced-use"]}.{originalId}.{source.BindingId}.{source.SkillInstanceId}"),
                $"发动【{ProgramConversionName(source)}】，视为使用【{CardCatalog.Get(kind).DisplayName}】攻击 {target.Name}。", [], [target.Seat], parameters));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private IEnumerable<(CardConversionSource Source, SkillProgramViewAs Rule)> RoundOnceCardlessForcedSlashRules(
        CharacterState actor, CharacterState target, bool qinglong) => RoundOnceCardlessRules(actor).Where(r => IsSlashCard(r.Rule.OutputKind) &&
        (qinglong ? CanUseQinglongCrescentBladeTarget(actor, target, r.Rule.OutputKind, null, false, Suit.None, 0) :
            IsLegalBorrowedSwordSlashTarget(actor, target, r.Rule.OutputKind, Suit.None, false, null, 0, [])) &&
        !HasBeneficiarySuitShield(actor.Seat, target.Seat, Suit.None) && !IsCardTargetProhibited(target, r.Rule.OutputKind, Suit.None));

    private bool TryResolveRoundOnceCardlessForcedSlash(PromptChoice selected, bool advance)
    {
        if (selected.Parameters.GetValueOrDefault("action")!="round-once-cardless-forced-slash" && selected.Parameters.GetValueOrDefault("response")!="round-once-cardless-forced-slash") return false;
        if (selected.Parameters.GetValueOrDefault("forced-use") is not ("borrowed-sword" or "qinglong")) return false;
        var qinglong = selected.Parameters["forced-use"] == "qinglong";
        if (_pendingDecision is not { } prompt || prompt.Kind != (qinglong ? DecisionKind.QinglongCrescentBlade : DecisionKind.RespondSlash) ||
            !prompt.Choices.Any(c => AssistedChoicesEqual([c], [selected])) || selected.Cards.Count != 0 || selected.Targets is not [var targetSeat] ||
            !long.TryParse(selected.Parameters.GetValueOrDefault("original-frame-id"), out var originalId) ||
            !Enum.TryParse<CardKind>(selected.Parameters.GetValueOrDefault("output-kind"), out var kind))
            throw new InvalidOperationException("A zero forced Slash changed its exact published producer, target or name.");
        var actor = _players[prompt.PlayerSeat]; var target = _players[targetSeat];
        if (!RoundOnceCardlessForcedSlashChoices(actor, target, originalId, qinglong).Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("A forced zero Slash lost its original legal same-target use and shared allowance.");
        var conversion = RequireConversionSource(selected); CaptureSelectedResponseConversion(selected);
        if (qinglong)
        {
            var old = ActiveQinglongCrescentBlade!.Attack;
            ActiveQinglongCrescentBlade = null; ClearPendingDecision();
            SetCardUseStep(old.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(old.ResolutionId, actor.Seat, target.Seat,
                Used: true, SlashCardIds: [], EffectiveSlashKind: kind));
            SuspendContinuationForQinglongFollowup(old.ResolutionId);
            BeginRoundOnceCardlessForcedSlash(actor, target, kind, conversion, null, false);
        }
        else
        {
            var original = ActiveBorrowedSword!;
            PopResponseWindow(original.ResolutionId); ClearPendingDecision();
            SetCardUseStep(original.ResolutionId, ResolutionFrameStep.ResolvingEffect);
            original.AwaitingSlashChoice = false; original.SlashCardId = 0; original.EffectiveSlashKind = kind;
            BeginRoundOnceCardlessForcedSlash(actor, target, kind, conversion, original, true);
        }
        AdvanceRulesAndPublishState(); if (advance) AdvanceToHumanBoundary(); return true;
    }

    private void BeginRoundOnceCardlessForcedSlash(CharacterState actor, CharacterState target, CardKind kind,
        CardConversionSource source, BorrowedSwordHandle? borrowed, bool countsTowardLimit)
    {
        ConsumeRoundOnceCardlessBasic(source,false);
        var card = new Card(0, kind, Suit.None, 0);
        var id = BeginCardUse(card, actor.Seat, [target.Seat], kind,
            ignoresArmor: HasCardArmorBypass(actor, target, kind), physicalCardIds: [], conversionSource: source);
        IssueRoundOnceCardlessNativeUse(id,source);
        var attack = new CardAttackHandle(this, id, actor.Seat, target.Seat, card: null,
            damageAmount: 1 + actor.AlcoholDamageBonus, playedCardKind: kind,
            ignoresArmor: HasCardArmorBypass(actor, target, kind));
        if (borrowed is not null) borrowed.ActiveAttack = attack;
        ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack);
        if (countsTowardLimit && _phase == TurnPhase.Play && actor.Seat == _currentSeat &&
            LifecycleCardUse(id)?.UnlimitedUse != true && !IgnoresProgramSlashLimit(actor, source)) RecordSlashUseDebit(id, actor.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, kind);
        CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, kind, actor.Seat, target.Seat, IgnoresArmor: attack.IgnoresArmor));
        NotifyAiOfSlash(actor, target);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!,
            SkillProgramTriggerWindow.CardUseCommitted, [target.Seat], ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }

}
