using System.Text.Json;
namespace CardGame.Core;
public sealed record ProgramRoundOnceCardlessBasicPolicy(string UsageGroup,int UsesPerRound);
public sealed record RoundOnceCardlessBasicPaymentReceipt(CardConversionSource Source,string GameplayHash,string UsageGroup,int RoundNumber,int ActualTurnNumber,int ActualTurnOwnerSeat,int PhaseInstanceId);
public sealed record RoundOnceCardlessBasicUseReceipt(RoundOnceCardlessBasicPaymentReceipt Payment,long OwnerFrameId,long CardActionId,int ActorSeat,int ProviderSeat,int? RequesterSeat,int? ResponderSeat,int? OpponentSeat,CardKind EffectiveKind,CardActionType ActionType,long? ParentActionId);
public sealed record RoundOnceCardlessBasicQuotaPaidEvent(RoundOnceCardlessBasicPaymentReceipt Receipt):IGameEvent;
public sealed record RoundOnceCardlessBasicUseIssuedEvent(RoundOnceCardlessBasicUseReceipt Receipt):IGameEvent;
internal static class RoundOnceCardlessBasicSchema
{
 internal static ProgramRoundOnceCardlessBasicPolicy Parse(JsonElement node,string path)
 {
  if(node.ValueKind!=JsonValueKind.Object||node.EnumerateObject().Count()!=2||node.EnumerateObject().Any(p=>p.Name is not ("usageGroup" or "usesPerRound"))||!node.TryGetProperty("usageGroup",out var group)||group.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(group.GetString())||group.GetString()!.Length>128||!System.Text.RegularExpressions.Regex.IsMatch(group.GetString()!,@"^[A-Za-z0-9][A-Za-z0-9_.:-]*$")||!node.TryGetProperty("usesPerRound",out var uses)||!uses.TryGetInt32(out var count)||count!=1)throw new InvalidOperationException(path+": requires one bounded usageGroup and usesPerRound exactly one.");
  return new(group.GetString()!,count);
 }
 internal static void Validate(SkillProgramViewAs rule,string path)
 {
  if(rule.RoundOnceCardlessBasic is not {} p)return;
  if(string.IsNullOrWhiteSpace(p.UsageGroup)||p.UsageGroup.Length>128||!System.Text.RegularExpressions.Regex.IsMatch(p.UsageGroup,@"^[A-Za-z0-9][A-Za-z0-9_.:-]*$")||p.UsesPerRound!=1||rule.InputCount!=0||rule.SourceZones.Count!=0||rule.InputKinds.Count!=0||rule.InputSuits.Count!=0||rule.InputCategories.Count!=0||rule.OutputKind is not (CardKind.Slash or CardKind.Dodge)||rule.ForResponse!=(rule.OutputKind==CardKind.Dodge)||rule.ForPlay!=(rule.OutputKind==CardKind.Slash)||rule.UseOnly||rule.VariableInputCount||rule.ExtendedUse||rule.DeclaredEntity||rule.DeclarationValidation is not null||rule.TieredRoundConversion is not null||rule.DrawFundedDistinctBasic is not null||rule.RoundDistinctBasicUse is not null||rule.ChainedStateCost is not null||rule.ConversionStateId is not null||rule.MinimumTier!=0||rule.MaximumTier!=2||rule.ActivationUsageGroup is not null||rule.UsesPerPhase is not null||rule.UsageGroup is not null||rule.CostDestination is not null||rule.AllowSameKind||rule.AllowChainedInput||rule.SameSuit||rule.InheritPreviousPlaySuit||rule.UnusedOutputThisTurn||rule.UnusedOutputNameThisGame||rule.NameLedgerId is not null||rule.ExcludeOwnerEffects||rule.DistanceUnlimited||rule.SingleCardTrickUse||rule.NoDying||rule.AllHandCardsOfSelectedSuit||rule.NonBasicUnrespondableSlash||rule.UseEffectiveInputSuit is not null||rule.DisableSkillUntilTurnEndIfNoDamage is not null||rule.LastInSourceZone is not null||rule.DamageBonus!=0||rule.RecoveryBonus!=0||rule.Condition.Kind!=SkillProgramConditionKind.Always)throw new InvalidOperationException(path+": cardless round basic requires only zero materials, ordinary Slash/Dodge, native play/response direction and one shared round quota without another cost/conversion policy.");
 }
}
