using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record QigongFactionRequest(long FrameId,int Actor,int Target,FactionResponsePolicySource Policy);
public sealed record QigongDeclarationRequest(int Actor,int CardId,CardConversionSource Conversion,CardKind Kind,long Before);
public sealed record QigongNativeReceipt(long ProgramId,CardConversionSource Source,string GameplayHash,int Instruction,int Turn,long OriginalUseId,long OriginalActionId,int Actor,int Target,long UseId,long ActionId,CardActionContext OriginalAction,long Before,long After,bool Paid=false)
{
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public RoundOnceCardlessBasicPaymentReceipt? RoundOncePayment{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public DrawFundedDistinctBasicPayment? DrawFundedPayment{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ChainedStateBasicPayment? ChainedPayment{get;init;}
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public DeclaredCardPayment? DeclarationPayment{get;init;}
}
public sealed record QigongNativeIssuedEvent(QigongNativeReceipt Receipt):IGameEvent;
public sealed record QigongNativePaidEvent(QigongNativeReceipt Receipt):IGameEvent;
public sealed record QigongNativeReturnedEvent(long ProgramId,long UseId,long ActionId):IGameEvent;
internal sealed class ResolveQigongActualDodgedSlashRequestDescriptor():LvKuangLvXiangDescriptor(SkillProgramEffectOp.ResolveQigongActualDodgedSlashRequest){public override IReadOnlyList<ProgramResourceOperation>Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.SlashFullyDodged)];}
