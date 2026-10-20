using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string ChanshuangDiscardReason="skill-program.li-yi.chanshuang.discard";
 private bool HasLiYiCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveChanshuangSimultaneousChoices)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveChanshuangEnding);
 private bool HasLiYiVirtualStoneAxe(CharacterState player,int? excludedEquipmentId=null,EquipmentSlot? excludedEquipmentSlot=null)=>player.IsAlive&&HasCardPolicy(player,SkillProgramCardPolicyKind.EmptyWeaponStoneAxe)&&!GetEquipment(player).Any(c=>c.Id!=excludedEquipmentId&&(excludedEquipmentSlot is null||EquipmentCatalog.Get(c.Kind).Slot!=excludedEquipmentSlot)&&EquipmentCatalog.Get(c.Kind).Slot==EquipmentSlot.Weapon);
 private CardConversionSource ChanshuangSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
 private static string ChanshuangHash<T>(T value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
 private IGameEvent[] ChanshuangActualTurnHistory()
 {
  if(!HasLiYiCapability||_turnNumber<1||_turnProgression.TurnNumber!=_turnNumber)return [];
  var history=CompleteProgramEventHistory().ToArray();var boundary=Array.FindLastIndex(history,e=>e is ActualTurnCursorReturnedEvent);
  if(boundary>=0&&history[boundary]is ActualTurnCursorReturnedEvent end&&end.ActualTurn==_turnNumber&&end.OwnerSeat==_turnProgression.OwnerSeat)return [];
  return history.Skip(boundary+1).ToArray();
 }
 private int CurrentChanshuangMask(int actor)
 {
  var history=ChanshuangActualTurnHistory();var mask=0;
  if(history.OfType<NativeRecastInvocationRecordedEvent>().Any(e=>e.TurnNumber==_turnNumber&&e.ActualTurnOwnerSeat==_turnProgression.OwnerSeat&&e.ExecutorSeat==actor&&e.ActualRecastCount==1))mask|=1;
  if(history.OfType<NativeLogicalDiscardInvocationRecordedEvent>().Any(e=>e.TurnNumber==_turnNumber&&e.ActualTurnOwnerSeat==_turnProgression.OwnerSeat&&e.ExecutorSeat==actor&&e.ActualCount==2))mask|=4;
  foreach(var group in history.OfType<CardUseDeclaredEvent>().Where(e=>IsSlashCard(e.CardKind)).GroupBy(e=>e.ResolutionId))
  {
   var declared=group.First();if(group.Any(e=>e!=declared))throw new InvalidOperationException("Chanshuang requires one consistent native Slash declaration.");var seat=declared.SourceSeat;int? provider=null;
   foreach(var change in history.OfType<ProgramCardUseActorReplacedEvent>().Where(e=>e.CardUseFrameId==declared.ResolutionId))
   {if(change.PreviousActorSeat!=seat||change.OwnerSeat!=seat||!IsValidPlayerSeat(change.ActorSeat)||!IsValidPlayerSeat(change.ProviderSeat)||provider is{}previous&&previous!=change.ProviderSeat)throw new InvalidOperationException("Chanshuang lost the final actual Slash actor's native replacement chain.");seat=change.ActorSeat;provider=change.ProviderSeat;}
   if(seat==actor){mask|=2;break;}
  }
  return mask;
 }
 private static int ChanshuangItemCount(ChanshuangReceipt r)=>r.Operation==SkillProgramEffectOp.ResolveChanshuangEnding?System.Numerics.BitOperations.PopCount((uint)r.FrozenMask):2;
 private static ChanshuangVote ChanshuangItem(ProgramSkillFrame f)
 {var r=f.Chanshuang!;return r.Operation==SkillProgramEffectOp.ResolveChanshuangEnding?new(f.OwnerSeat,(ChanshuangOption)(r.Cursor+1)):r.Votes[r.Cursor];}
 private int ChanshuangChooser(ProgramSkillFrame f)=>f.Chanshuang is{Stage:ChanshuangStage.Votes,Votes.Count:1}r?r.PeerSeat:f.Chanshuang!.Stage==ChanshuangStage.Votes?f.OwnerSeat:ChanshuangItem(f).Seat;
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27481)]
 private bool CanOfferChanshuangEnding(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>t.Effects is not[{Op:SkillProgramEffectOp.ResolveChanshuangEnding}]||context.Window==SkillProgramTriggerWindow.TurnEnding&&context.OwnerSeat==c.OwnerSeat&&context.SourceSeat==c.OwnerSeat&&_currentSeat==c.OwnerSeat&&_turnProgression.OwnerSeat==c.OwnerSeat&&CurrentChanshuangMask(c.OwnerSeat)!=0;
 private bool TryGetChanshuangSlashRequest(ProgramSkillFrame f,out int actorSeat,out IReadOnlyList<int> targets)
 {
  actorSeat=-1;targets=[];
  if(f.Chanshuang is not{SlashTarget:{}target}r||r.Stage is not(ChanshuangStage.SlashChoice or ChanshuangStage.SlashIssuing or ChanshuangStage.NativeUse or ChanshuangStage.NativeChildren)||r.Cursor>=ChanshuangItemCount(r)||ChanshuangItem(f)is not{Option:ChanshuangOption.Slash}item||r.Source!=ChanshuangSource(f)||r.GameplayHash!=f.GameplayHash||f.InstructionIndex!=1||ProgramEventHistory<ChanshuangStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Operation==r.Operation&&e.ActualTurn==r.ActualTurn&&e.ActualTurnOwner==r.ActualTurnOwner&&e.PeerSeat==r.PeerSeat&&e.FrozenMask==r.FrozenMask)!=1||ProgramEventHistory<ChanshuangSlashSelectedEvent>().Count(e=>e.FrameId==f.Id&&e.Cursor==r.Cursor&&e.ActorSeat==item.Seat&&e.TargetSeat==target)!=1)return false;
  actorSeat=item.Seat;targets=Array.AsReadOnly(new[]{target});return true;
 }
 private bool IsChanshuangSlashRequest(ProgramSkillFrame f)=>f.Chanshuang?.Stage==ChanshuangStage.SlashChoice&&TryGetChanshuangSlashRequest(f,out _,out _);
 private bool IsChanshuangSlashQuery(CharacterState actor)=>_resolutionStack.LastOrDefault()is ProgramSkillFrame{Chanshuang:{}r}f&&r.Stage is ChanshuangStage.SlashTarget or ChanshuangStage.SlashChoice or ChanshuangStage.SlashIssuing&&r.Cursor<ChanshuangItemCount(r)&&ChanshuangItem(f)is{Option:ChanshuangOption.Slash}item&&item.Seat==actor.Seat&&r.Source==ChanshuangSource(f)&&r.GameplayHash==f.GameplayHash;

    [ProgramDistanceRule(ProgramDistanceStage.AttackRangeBaseTerms, 10)]
    private void AddLiYiAttackRangeBaseTerms(ProgramAttackRangeQuery query, List<RuleQueryBaseTerm> terms)
    {
        if (HasLiYiVirtualStoneAxe(query.Player, query.ExcludedEquipmentId, query.ExcludedEquipmentSlot) && terms[0].Value < 3)
            terms[0] = new RuleQueryBaseTerm($"skill:{query.Player.Seat}:zhanjin:attack-range", 3);
    }

}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 60)]
    private bool ChanshuangSlashQuotaBeforeLimit(ProgramSlashQuotaQuery query) =>
        IsChanshuangSlashQuery(query.Actor);
}
