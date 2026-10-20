using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum LangdaoOption { Damage, ExtraTarget, NoResponse }
public sealed record ZhangYanLangdaoUse(long ProgramFrameId,long WindowId,long ActionId,CardConversionSource Source,string GameplayHash,int OriginalActor,int OriginalTarget,int DamageBonus,int ExtraCount,bool NoResponse,bool Killed=false,bool Settled=false)
{
 private IReadOnlyList<int> _original=[],_added=[];private IReadOnlyList<LangdaoOption> _selected=[];
 public IReadOnlyList<int> OriginalTargets {get=>_original;init=>_original=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<int> AddedTargets {get=>_added;init=>_added=Array.AsReadOnly(value.ToArray());}
 public IReadOnlyList<LangdaoOption> SelectedOptions {get=>_selected;init=>_selected=Array.AsReadOnly(value.ToArray());}
}
public sealed record ZhangYanLangdaoOptionFrozenEvent(long FrameId,int Index,LangdaoOption Option):IGameEvent;
public sealed record ZhangYanLangdaoVoteRevealedEvent(long FrameId,int Index,int Seat,LangdaoOption Option):IGameEvent;
public sealed record ZhangYanLangdaoIssuedEvent(long UseId,long ActionId,long FrameId,CardConversionSource Source,string GameplayHash,int OriginalActor,int OriginalTarget,int DamageBonus,int ExtraCount,bool NoResponse):IGameEvent;
public sealed record ZhangYanLangdaoTargetAddedEvent(long FrameId,long UseId,long ActionId,int Target,int Index):IGameEvent;
public sealed record ZhangYanLangdaoRedirectedEvent(long ProgramFrameId,long UseId,long ActionId,CardConversionSource Source,string GameplayHash,int InstructionIndex,int TargetIndex,int OriginalTarget,int NewTarget):IGameEvent;
public sealed record ZhangYanLangdaoKilledEvent(long UseId,long ActionId,long DyingId,long DamageId,int Victim):IGameEvent;
public sealed record ZhangYanLangdaoSettledEvent(long UseId,long ActionId,CardConversionSource Source,bool Killed):IGameEvent;
public sealed record ZhangYanLangdaoOptionRemovedEvent(long UseId,long ActionId,CardConversionSource Source,LangdaoOption Option):IGameEvent;
internal sealed class ResolveLangdaoSimultaneousSlashDescriptor():ZhangYanDescriptor(SkillProgramEffectOp.ResolveLangdaoSimultaneousSlash)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];}
