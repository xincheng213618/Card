using System.Globalization;
using System.Security.Cryptography;
using System.Text;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasWangYunTrickCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial);
    private CardKind[] WangYunRandomTrickPool()=>_contentRegistry.Cards.Values.Where(c=>c.LegacyKind is{}kind && CardCatalog.Get(kind).CategoryName=="锦囊牌" && kind!=CardKind.Nullification &&
        (kind==CardKind.Lightning || ActivityRuntimeProgram(_contentRegistry.GetSkill("ol:jingong").Program!).ViewAs.Any(r=>r.OutputKind==kind))).Select(c=>c.LegacyKind!.Value).Distinct().Order().ToArray();
    private bool IsWangYunRandomConversionAvailable(CharacterState owner,Card card,CardConversionSource source,bool forResponse)
    {
        if(!HasWangYunTrickCapability || !_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial) ||
            !_contentRegistry.Skills.TryGetValue(source.SkillId,out var definition) || definition.Program is not{}p ||
            !p.Activations.Any(a=>ProgramInstructionResolver.Default.Features(a).HasOperation(SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial)))return true;
        return !forResponse && source.OwnerSeat==owner.Seat && WangYunMaterial(card) &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame{WangYunTrick:{Stage:"choice" or "issuing-trick"}r}f && f.OwnerSeat==owner.Seat && f.SkillId==source.SkillId &&
            f.SkillInstanceId==source.SkillInstanceId && r.CardId==card.Id && r.From==_cardZones.GetLocation(card.Id) &&
            ActivityRuntimeProgram(p).ViewAs.SingleOrDefault(v=>v.Id==source.BindingId) is{}rule && r.Samples.Contains(rule.OutputKind);
    }
    private static string WangYunSamplesFingerprint(IReadOnlyList<CardKind> samples)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',',samples.Select(k=>(int)k)))));
    private SkillProgramStepOutcome UseThreeRandomTricksFromSelectedMaterial(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.WangYunTrick is not null || !WangYunActivationMatches(f,SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial) || f.SelectedCardIds is not[var id] || f.SelectedTargetSeats.Count!=0)
            throw new InvalidOperationException("Random tricks lost the original actual material activation.");
        var from=_cardZones.GetLocation(id);var card=_cardZones.CardsAt(from).Single(c=>c.Id==id);var pool=WangYunRandomTrickPool().ToList();
        if(from.OwnerSeat!=f.OwnerSeat || from.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment) || !WangYunMaterial(card) || pool.Count<3)
            throw new InvalidOperationException("Random tricks require one real Slash/equipment entity and at least three real proactive native trick templates.");
        var samples=new List<CardKind>();for(var i=0;i<3;i++){var at=_random.Next(pool.Count);samples.Add(pool[at]);pool.RemoveAt(at);}
        var r=new WangYunTrickReceipt{Source=WangYunSource(f),GameplayHash=f.GameplayHash,Turn=_turnNumber,PhaseInstance=_cardUseDebitPhaseInstanceId,InstructionIndex=1,
            CardId=id,PrintedKind=card.Kind,From=from,Samples=samples,Stage="choice"};ReplaceRuntimeTop(f=f with{WangYunTrick=r});
        AdvanceEventRulesAndQueueFact(new WangYunRandomTricksStartedEvent(f.Id,r.Source,r.GameplayHash,r.Turn,r.PhaseInstance,id,card.Kind,from,WangYunSamplesFingerprint(samples)));
        PublishWangYun(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<LegalAction> WangYunTrickActions(ProgramSkillFrame f)=>BuildLegalActions(_players[f.OwnerSeat],includeProgramActions:false,selectedPhysicalCardId:f.WangYunTrick!.CardId)
        .Where(a=>a.ConversionSource is{}s && s.SkillId==f.SkillId && s.SkillInstanceId==f.SkillInstanceId && a.PlayedCardKind is{}k && f.WangYunTrick.Samples.Contains(k)).ToArray();
    private bool CanWangYunLightning(ProgramSkillFrame f)
    {
        var r=f.WangYunTrick!;var owner=_players[f.OwnerSeat];var card=_cardZones.CardsAt(r.From).SingleOrDefault(c=>c.Id==r.CardId);
        return r.Samples.Contains(CardKind.Lightning) && owner.IsAlive && card is not null && WangYunMaterial(card) && !owner.JudgmentAreaAbolished && !HasJudgmentEffectiveCard(owner,CardKind.Lightning) &&
            !IsCardUseForbidden(owner.Seat,CardKind.Lightning,CardActionType.Use) && !IsSelfTargetForbiddenAction(owner,CardKind.Lightning,[owner.Seat]) &&
            !IsTurnPhysicalUseForbidden(owner.Seat,[card.Id]) && !IsTurnHandCardRestricted(owner,card) && !IsResponseEntityRestricted(owner.Seat,card.Id) &&
            !IsCardTargetProhibited(owner,CardKind.Lightning,EffectiveSuit(owner,card),SuitColor(EffectiveSuit(owner,card)));
    }
    private IReadOnlyList<PromptChoice> WangYunTrickChoices(ProgramSkillFrame f)
    {
        var result=new List<PromptChoice>();var index=0;
        foreach(var a in WangYunTrickActions(f))
        {
            var args=WangYunArgs(f,"trick");args["choice-index"]=(index++).ToString(CultureInfo.InvariantCulture);args["effective-kind"]=a.PlayedCardKind!.Value.ToString();
            result.Add(new(new($"wang-yun.{f.Id}.trick-{index}"),a.Description,[f.WangYunTrick!.CardId],a.TargetSeats,args));
        }
        if(CanWangYunLightning(f)){var args=WangYunArgs(f,"lightning");args["effective-kind"]=CardKind.Lightning.ToString();result.Add(new(new($"wang-yun.{f.Id}.lightning"),"将本次材料当【闪电】对自己使用",[f.WangYunTrick!.CardId],[f.OwnerSeat],args));}
        return Array.AsReadOnly(result.ToArray());
    }
    private void IssueWangYunTrick(ProgramSkillFrame f,PromptChoice selected)
    {
        var r=f.WangYunTrick!;var kind=Enum.Parse<CardKind>(selected.Parameters["effective-kind"]);var owner=_players[f.OwnerSeat];var card=_cardZones.CardsAt(r.From).Single(c=>c.Id==r.CardId);
        LegalAction? action=null;if(kind!=CardKind.Lightning)action=WangYunTrickActions(f)[int.Parse(selected.Parameters["choice-index"],CultureInfo.InvariantCulture)];
        ReplaceRuntimeTop(f=f with{WangYunTrick=r with{Stage="issuing-trick",SelectedKind=kind}});
        if(action is not null){ExecuteAction(owner,action);return;}
        if(!CanWangYunLightning(f))throw new InvalidOperationException("The sampled Lightning lost its actual native self-placement legality.");
        var id=BeginCardUse(card,owner.Seat,[owner.Seat],CardKind.Lightning,conversionSource:r.Source);MoveCard(card,r.From,CardLocation.Processing,CardMoveReasons.Use);CompleteWangYunNativePayment(id);
        AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(id,f.SkillId,f.ActivationId,f.OwnerSeat,[card.Id],CardKind.Lightning,true,[owner.Seat]));
        if(!HasSoleTargetProtectionCapability && TryBeginDelayedCardUsePrograms(id))return;
        BeginJizhiOrNullificationWindow(id,card,owner.Seat,[owner.Seat],LegalActionKind.Lightning,playedCardKind:CardKind.Lightning);
    }

    [ProgramCardLegality(ProgramCardLegalityStage.ConversionSourceBan, 30)]
    private bool WangYunConversionUnavailable(ProgramViewAsSourceQuery query) =>
        !IsWangYunRandomConversionAvailable(query.Owner, query.Card, query.Source, query.ForResponse);
}
