using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class FanJiangZhangDaChecks
{
    private const string Skill = "ol:juesheng", Armor = "ol:yuanchou", Owner = "fixture:fjzd-owner", Driver = "fixture:fjzd-driver", Quiet = "fixture:fjzd-quiet",
        Watch = "fixture:fjzd-watch", Health = "fixture:fjzd-health", Mode = "identity:classic-fan-jiang-zhang-da-fixture";
    public static void YuanchouEffectiveBlackNativeArmorAndPrivacyCold()
    {
        foreach (var scene in new[] { "black", "red", "effective-red", "actor-black" })
        {
            var (g, r) = Start(scene); Draw(g);
            Native(g, a => a.Kind == LegalActionKind.Equip && V(g, 0).Hand.Any(c => c.Id == a.CardId && c.Kind == CardKind.Tengjia)); Play(g);
            Use(g, "equip-peer", [1]); Reach(g, () => Action(g, "select-and-move-owned-card"));
            Answer(g, c => c.Cards is [var id] && V(g, 0).Hand.Any(card => card.Id == id && card.Kind == CardKind.Tengjia)); Play(g);
            Need(V(g, 0).Equipment.Any(c => c.Kind == CardKind.Tengjia) && V(g, 1).Equipment.Any(c => c.Kind == CardKind.Tengjia), "Both native armor directions use actual equipped Tengjia entities.");
            var hp = V(g, 1).Hp;
            Native(g, a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
            if (scene == "actor-black")
            {
                Reach(g, () => Action(g, "select-target")); Answer(g, c => c.Targets.SequenceEqual([3])); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
                var changed = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardKind == CardKind.Slash);
                Need(changed.SourceSeat == 3 && changed.Action is { ActorSeat: 3, ProviderSeat: 0, EffectiveIsRed: false } &&
                    E<ProgramCardUseActorReplacedEvent>(g).Any(e => e.CardUseFrameId == changed.Id && e.PreviousActorSeat == 0 && e.ActorSeat == 3 && e.ProviderSeat == 0),
                    "An isolated Yuanchou catalog preserves the real black appearance through native actor rewriting before target armor is evaluated.");
                g = Cold(g, r); Continue(g);
            }
            Play(g);
            var use = E<CardUseDeclaredEvent>(g).Last(e => e.SourceSeat == 0 && e.CardKind == CardKind.Slash);
            Need(scene is "black" or "actor-black" ? V(g, 1).Hp == hp - 1 && !E<ArmorEffectAppliedEvent>(g).Any(e => e.ResolutionId == use.ResolutionId) :
                V(g, 1).Hp == hp && E<ArmorEffectAppliedEvent>(g).Any(e => e.ResolutionId == use.ResolutionId && e.ArmorCard == CardKind.Tengjia),
                "Yuanchou checks the genuine effective black color: a printed black card changed to red cannot bypass real native Vine armor.");
            if (scene != "black") { _ = Cold(g, r); continue; }
            Use(g, "equip-peer", [1]); Reach(g, () => Action(g, "select-and-move-owned-card")); Answer(g, c => c.Cards is [var id] && V(g, 0).Hand.Any(card => card.Id == id && card.Kind == CardKind.Crossbow)); Play(g);
            var ownerHp = V(g, 0).Hp; Native(g, a => a.Kind == LegalActionKind.BorrowedSword && a.TargetSeats.SequenceEqual([1, 0]));
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
            var incoming = g.ResolutionStack.OfType<CardUseFrame>().Last(u => u.CardKind == CardKind.Slash);
            Need(incoming.SourceSeat == 1 && incoming.Action?.EffectiveIsRed == false && incoming.CardAttack?.IgnoresArmor == true &&
                Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).Players[0].Hand.Count == 0),
                "A genuine Borrowed Sword black Slash also bypasses the Yuanchou target's armor while private Hand entities stay concealed.");
            g = Cold(g, r); Answer(g, c => c.Cards.Count == 0); Play(g);
            Need(V(g, 0).Hp == ownerHp - 1, "The incoming direction uses native Slash response and damage rather than a synthetic damage instruction.");
            Incoming(g, 1); Play(g);
            var zero = E<CardUseDeclaredEvent>(g).Last(e => e.SourceSeat == 1 && e.CardKind == CardKind.Slash);
            Need(zero.CardId == 0 && E<ArmorEffectAppliedEvent>(g).Any(e => e.ResolutionId == zero.ResolutionId && e.ArmorCard == CardKind.Tengjia),
                "Colorless zero-entity Slash remains colorless and cannot borrow the black-Slash armor policy."); _ = Cold(g, r);
        }
    }
    public static void JueshengLifetimeSlashAndBothDuelDirectionsCold()
    {
        foreach (var scene in new[] { "forward", "reverse" })
        {
            var (g, r) = Start(scene); Draw(g); Incoming(g, 1); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); Answer(g, c => c.Cards.Count == 1); Play(g);
            Incoming(g, 1); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); Answer(g, c => c.Cards.Count == 1); Play(g);
            Need(!HasSkill(g, 1, Skill) && E<CardUseDeclaredEvent>(g).Count(e => e.SourceSeat == 1 && e.CardKind == CardKind.Slash) == 2,
                "The target's two actual zero-material Slash Uses occur before it ever acquires Juesheng; defensive Dodge is a different real card use.");
            if (scene == "reverse") { Use(g, "wound-peer", [1]); Play(g); }
            var hp0 = V(g, 0).Hp; var hp1 = V(g, 1).Hp; Juesheng(g, 1);
            if (scene == "reverse") { Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }); g = Cold(g, r); Answer(g, c => c.Cards.Count == 0); }
            Play(g); var frozen = E<JueshengTargetDamageFrozenEvent>(g).Single(); var origin = E<JueshengDuelIssuedEvent>(g).Single().Origin;
            Need(frozen.SlashCount == 2 && frozen.Amount == 2 && (scene == "forward" ? V(g, 1).Hp == hp1 - 2 : V(g, 0).Hp == hp0 - 2) &&
                E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == origin.UseId && e.CardId == 0 && e.CardKind == CardKind.Duel) == 1,
                "The true native zero-entity Duel freezes max(1, lifetime Slash Uses), with the same target-based X when the target wins the Slash exchange.");
            if (scene == "reverse") Need(E<CardActionAcceptedEvent>(g).Any(e => e.Action.ActorSeat == 1 && e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Slash) &&
                E<CardUseDeclaredEvent>(g).Count(e => e.SourceSeat == 1 && e.CardKind == CardKind.Slash) == 2,
                "Purely playing a physical/conversion Slash during Duel adds no native Slash Use and does not become a lifetime count.");
            Need(E<JueshengSkillGrantedEvent>(g) is [{ RecipientSeat: 1 }] && E<JueshengCompletedEvent>(g).Count(e => e.UseId == origin.UseId) == 1,
                "The independent then-clause grants the skill after the entire real Duel returns exactly once."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("actor"); Native(g, a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1])); Reach(g, () => Action(g, "select-target"));
            Answer(g, c => c.Targets.SequenceEqual([3])); Play(g);
            Need(E<ProgramCardUseActorReplacedEvent>(g).Any(e => e.PreviousActorSeat == 0 && e.ActorSeat == 3 && e.ProviderSeat == 0), "The native Use actually changes actor while physical provider zero stays fixed.");
            Juesheng(g, 3); Play(g); Need(E<JueshengTargetDamageFrozenEvent>(g) is [{ TargetSeat: 3, SlashCount: 1, Amount: 1 }],
                "Lifetime Slash ownership follows the real final actor chain, rather than the original physical provider or accepted-window count."); _ = Cold(g, r);
        }
    }
    public static void JueshengWholeMultiTargetExtraEffectsAndTemporaryExpiryCold()
    {
        {
            var (g, r) = Start("multi"); Use(g, "flip-peer", [1]); Play(g); Juesheng(g, 1);
            Reach(g, () => Optional(g, "ol:fengzi")); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == "ol:fengzi" && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cost"));
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "cost"); Reach(g, () => BingTargets(g));
            Answer(g, c => c.Targets.SequenceEqual([2])); if (BingTargets(g)) Answer(g, c => c.Targets.Count == 0);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            Need(E<JueshengSkillGrantedEvent>(g).Length == 0 && E<JueshengTargetDamageFrozenEvent>(g).Length == 1,
                "The first real target's damage child cannot grant early or prematurely complete a multi-target Duel.");
            g = Cold(g, r); Continue(g); Play(g); var origin = E<JueshengDuelIssuedEvent>(g).Single().Origin;
            Need(E<JueshengTargetDamageFrozenEvent>(g).Select(e => e.TargetSeat).SequenceEqual([1, 2]) && E<ExtraCardEffectStartedEvent>(g).Any(e => e.CardUseFrameId == origin.UseId) &&
                E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat is 1 or 2) == 4 && E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == origin.UseId) == 1 &&
                E<JueshengSkillGrantedEvent>(g).Select(e => e.RecipientSeat).SequenceEqual([1, 2]),
                "All native target slots and both extra-effect rounds resolve before each final recipient gains one exact independent temporary source.");
            var grants = E<JueshengSkillGrantedEvent>(g); End(g); Reach(g, () => E<JueshengSkillExpiredEvent>(g).Length == 2);
            Need(grants.All(e => E<JueshengSkillExpiredEvent>(g).Count(x => x.GrantId == e.GrantId && x.RecipientSeat == e.RecipientSeat) == 1) &&
                E<TurnStartedEvent>(g).Any(e => e.ActorSeat == 1) && E<ActualTurnCursorReturnedEvent>(g).Any(e => e.OwnerSeat == 1) &&
                HasSkill(g, 1, Skill) && HasSkill(g, 2, Skill),
                "The recipient's next actual turn expires only the matching transferred source, including a face-down skipped turn, and preserves printed Juesheng grants."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("nullified"); Draw(g); Juesheng(g, 1);
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.Nullification });
            g = Cold(g, r); Answer(g, c => c.Cards.Count == 1); Play(g);
            var origin = E<JueshengDuelIssuedEvent>(g).Single().Origin;
            var proof = JsonSerializer.Serialize(new { Responses = E<NullificationRespondedEvent>(g), Resolved = E<NullificationResolvedEvent>(g), Damage = E<JueshengTargetDamageFrozenEvent>(g), Granted = E<JueshengSkillGrantedEvent>(g), Returned = E<JueshengDuelReturnedEvent>(g) });
            Need(E<NullificationRespondedEvent>(g).Any(e => e.ResolutionId == origin.UseId && e.ResponderSeat == 0), "The real human actually pays a native Nullification instead of depending on an AI choosing to cancel: " + proof);
            Need(E<NullificationResolvedEvent>(g).Any(e => e.ResolutionId == origin.UseId && e.EffectNullified) && E<JueshengTargetDamageFrozenEvent>(g).Length == 0,
                "The exact whole native Duel really ends nullified before any Duel pair or damage amount is issued: " + proof);
            Need(E<JueshengSkillGrantedEvent>(g) is [{ RecipientSeat: 1 }] && E<JueshengDuelReturnedEvent>(g).Single().UseId == origin.UseId,
                "An actually nullified whole Duel still executes the current text's independent then-clause; the historical damage-conditioned version is not substituted: " + proof); _ = Cold(g, r);
        }
    }
    public static void JueshengPaidArmorChildrenSourceLossDeathAndPermanentSpendCold()
    {
        foreach (var scene in new[] { "lion-loss", "lion-death", "ox" })
        {
            var (g, r) = Start(scene); Draw(g); Native(g, a => a.Kind == LegalActionKind.Equip); Play(g);
            var gear = scene == "ox" ? V(g, 0).Equipment.Single(c => c.Kind == CardKind.WoodenOx).Id : V(g, 0).Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
            if (scene == "ox")
            {
                var card = V(g, 0).Hand.First().Id;
                Accept(g, new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [card], [], g.Revision, P(g)!.PromptId)); Play(g);
                Need(V(g, 0).WoodenOxGrain is { Count: 1 } grain && grain[0].Id == card && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).Players[0].WoodenOxGrain?.Count is null or 0),
                    "Native Wooden Ox grain remains private to its actual owner before an equipment response payment.");
            }
            else { Use(g, "hurt"); Play(g); }
            Use(g, "wound-peer", [1]); Play(g);
            Juesheng(g, 1); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
            var p = P(g)!; var selected = p.Choices.First(c => c.Cards.SequenceEqual([gear])); var state = State(g);
            Need(!g.Submit(new AnswerPromptCommand(1, p.PromptId, selected.Id, g.Revision)).Accepted && State(g) == state,
                "Another seat cannot pay or observe the private actual equipment-as-Slash response.");
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([gear]));
            if (scene != "ox")
            {
                Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health); var live = E<JueshengDuelIssuedEvent>(g).Single().Origin;
                Need(E<JueshengSkillGrantedEvent>(g).Length == 0 && E<JueshengCompletedEvent>(g).Length == 0 &&
                    g.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == live.UseId && u.JueshengDuel == live) &&
                    E<CardResponseCompletionStartedEvent>(g).Any(e => e.ParentFrameId == live.UseId),
                    "The genuine paid response completion drains its Lion recovery and health child while the original Duel stays live, before the then-clause: " + Diagnostic(g));
                g = Cold(g, r); Continue(g);
            }
            Reach(g, () => E<JueshengCompletedEvent>(g).Length == 1 && (scene != "lion-death" || !V(g, 0).IsAlive)); var issued = E<JueshengDuelIssuedEvent>(g).Single().Origin;
            Need(g.CardMovements.Count(m => m.CardId == gear && m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1 &&
                E<JueshengSpentEvent>(g).Length == 1 && E<JueshengSkillGrantedEvent>(g) is [{ RecipientSeat: 1 }] && E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == issued.UseId) == 1,
                "The true paid Lion/Ox response, native recovery/death subtree and then-clause return exactly once even when the original skill source disappears.");
            if (scene == "lion-loss") Need(V(g, 0).IsAlive && !HasSkill(g, 0, Skill), "The real health child removes the HP-conditional source without undoing its paid Duel.");
            if (scene == "lion-death")
            {
                var proof = JsonSerializer.Serialize(new { Scene = scene, V(g, 0).Hp, V(g, 0).MaxHp, V(g, 0).IsAlive,
                    Recoveries = E<RecoveryAppliedEvent>(g), Losses = E<ProgramSkillHpLostEvent>(g), Dying = E<PlayerDyingEvent>(g), Deaths = E<PlayerDiedEvent>(g),
                    HealthBindings = E<ProgramBindingResolvedEvent>(g).Where(e => e.SkillId == Health), Granted = E<JueshengSkillGrantedEvent>(g), State = Diagnostic(g) });
                Need(E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == 0 && e.Amount == 1), "The actual Lion payment recovers its real original owner before the death child: " + proof);
                Need(E<ProgramSkillHpLostEvent>(g).Any(e => e.SkillId == Health && e.TargetSeat == 0 && e.RemainingHp == 0), "The real recovery observer executes its lethal original-owner loss: " + proof);
                Need(!V(g, 0).IsAlive, "Actual native recovery children kill the already paid issuer before its complete return: " + proof);
                Need(E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 0) && HasSkill(g, 1, Skill), "Actual native issuer death is recorded and its living recipient still receives the then-clause: " + proof);
                var events = g.Events.ToArray();
                Need(Array.FindIndex(events, e => e.Payload is PlayerDiedEvent { VictimSeat: 0 }) < Array.FindIndex(events, e => e.Payload is JueshengSkillGrantedEvent),
                    "The true dying/death child finishes before the whole Duel's independent grant; waiting for a late death cannot conceal an early then-clause: " + proof);
            }
            if (scene == "ox") { Play(g); Use(g, "regrant"); Play(g); Need(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.UseProgramSkill && a.SkillId == Skill),
                    "Permanent limited expenditure belongs to the character and survives a new acquisition/source instance."); }
            _ = Cold(g, r);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static bool HasSkill(GameEngine g, int seat, string skill) { var skills = V(g, seat).Skills; Need(skills is not null, "A character's own public skill snapshot must exist."); return skills!.Any(s => s.Id == skill); }
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Action(GameEngine g, string action) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action) == true;
    private static bool Optional(GameEngine g, string skill) => P(g)?.SkillPrompt?.SkillId == skill && Action(g, "activate");
    private static bool BingTargets(GameEngine g) => P(g)?.SkillPrompt?.SkillId == "ol:bingzhong" && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "bingzhong-target");
    private static void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected genuine Fan Jiang Zhang Da command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; var choice = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && choice is not null, "Missing actual human choice: " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(0, p.PromptId, choice!.Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string id, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Juesheng(GameEngine g, int target) => Accept(g, new UseProgramSkillCommand(0, Skill, "limited-duel", [], [target], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Draw(GameEngine g) { Use(g, "draw"); Play(g); }
    private static void Incoming(GameEngine g, int actor) { Use(g, "incoming", [actor]); Reach(g, () => P(g) is { PlayerSeat: 0 } && Action(g, "virtual-slash-offer") && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([0]))); Answer(g, c => c.Targets.SequenceEqual([0])); }
    private static void Native(GameEngine g, Func<LegalAction, bool> predicate) { var a = g.GetHumanLegalActions().FirstOrDefault(predicate); Need(a is not null, "Missing native card action: " + Diagnostic(g)); Accept(g, new PlayCardCommand(0, a!.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources ?? [] }); }
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done, [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0)
    {
        for (var i = 0; i < 320; i++)
        {
            if (done()) return; Need(g.State.Winner == Winner.None, $"Ended before {caller}:{line}: " + Diagnostic(g)); var p = P(g);
            if (p is null || p.PlayerSeat != 0) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p.Kind == DecisionKind.PlayCard) End(g);
            else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash) Answer(g, c => c.Cards.Count == 0);
            else if (p.Kind == DecisionKind.Nullification) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else if (p.Kind == DecisionKind.RescueDying) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass");
            else if (p.SkillPrompt?.SkillId is Watch or Health) Continue(g);
            else if (Action(g, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "decline")) Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "decline");
            else throw new InvalidOperationException($"Unexpected {caller}:{line}: " + Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing bounded {caller}:{line}: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase, Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).HandCount, V(g, s).IsAlive }), Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(6).Select(c => new { c.Cards, c.Targets, c.Parameters }) } : null, Frames = g.ResolutionStack.TakeLast(5).Select(f => new { f.Id, Type = f.GetType().Name }), Facts = g.Events.TakeLast(5).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(copy), "Cold recovery preserves every permitted private view, permanent spend, lifetime facts, whole Duel and independently owned temporary grant."); return copy; }
    private static (GameEngine, ContentRegistry) Start(string scene)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene)); var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "lion-death" ? Role.Rebel : Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-fan-jiang-zhang-da", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:fan-jiang-zhang-da");
            if (scene == "multi") { CharacterFixtureContent.Register(b, "ol:yang-fu"); CharacterFixtureContent.Register(b, "ol:wang-rong"); }
            if (scene == "effective-red") CharacterFixtureContent.Register(b, "classic:xiao-qiao");
            var armor = scene is "black" or "red" or "effective-red" or "actor-black"; var material = scene.StartsWith("lion", StringComparison.Ordinal) ? "silverLion" : scene == "ox" ? "woodenOx" : scene == "multi" ? "duel" : "crossbow";
            var defs = new List<string> { $$"""
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":50,"priority":0},{"id":"range","query":"attackRange","operation":"set","value":4,"priority":0},{"id":"quota","query":"slashLimit","operation":"add","value":4,"priority":0}],"viewAs":[
             {"id":"slash","inputKinds":[],"inputSuits":[],"sourceZones":["hand","equipment"],"outputKind":"slash","forPlay":true,"forResponse":{{(scene.StartsWith("lion",StringComparison.Ordinal)||scene is "ox" or "reverse"?"true":"false")}},"allowSameKind":true},
             {"id":"dodge","inputKinds":[],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true,"allowSameKind":true},
             {"id":"borrowed","inputKinds":[],"inputSuits":[],"outputKind":"borrowedSword","forPlay":true,"forResponse":false,"singleCardTrickUse":true,"allowSameKind":true}],"activations":[
             {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
             {"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
             {"id":"wound-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
             {"id":"regrant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:juesheng"]}]},
             {"id":"flip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"selectedTarget"}]},
             {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]},
             {"id":"equip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment","awaitMovementTriggers":true}]}]}
            """, $$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
                $$"""{"id":"fixture:fjzd-response","revision":1,"modifiers":[{"id":"range","query":"attackRange","operation":"set","value":4,"priority":0}],"viewAs":[{"id":"actual-slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":{{(scene is "reverse" or "lion-loss" or "lion-death" or "ox" ? "true" : "false")}},"allowSameKind":true}]}""" };
            if (scene is "actor" or "actor-black") defs.Add("""{"id":"fixture:fjzd-actor","revision":1,"triggers":[{"id":"actual-actor","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"}]}]}""");
            if (scene == "actor-black") defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-rewritten-black","window":"cardUseBeforeTargetEffects","ownerRelation":"observer","cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"rewritten","options":[{"id":"continue"}]}]}]}""");
            if (scene == "multi") defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"real-native-damage","window":"afterDamageApplied","subject":"damageSource","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"damage","options":[{"id":"continue"}]}]}]}""");
            if (scene == "nullified") defs.Add("""{"id":"fixture:fjzd-nullification","revision":1,"viewAs":[{"id":"real-nullification","inputKinds":["crossbow"],"inputSuits":[],"outputKind":"nullification","forPlay":false,"forResponse":true}]}""");
            if (scene == "lion-loss") defs.Add("""{"id":"fixture:fjzd-conditional","revision":1,"hpSkillGrants":[{"id":"healthy-source","skillId":"ol:juesheng","minimumHp":12}]}""");
            if (scene.StartsWith("lion", StringComparison.Ordinal)) defs.Add($$"""{"id":"{{Health}}","revision":1,"triggers":[{"id":"real-lion-health","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":{{(scene=="lion-death"?20:2)}}}]}]}""");
            var names = defs.Select(d => JsonNode.Parse(d)!).ToDictionary(d => d["id"]!.GetValue<string>(), d => new { name = d["id"]!.GetValue<string>(), description = "真实决生原生夹具", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => d[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""", JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "实际原生指令") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:fjzd-pick", "实际本人", "固定公开选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000d) });
            var own = new List<string> { Driver, Armor, scene == "lion-loss" ? "fixture:fjzd-conditional" : Skill };
            if (scene == "effective-red") own.Add("classic:hongyan"); if (scene is "actor" or "actor-black") own.Add("fixture:fjzd-actor"); if (scene == "actor-black") own.Add(Watch);
            if (scene == "nullified") own.Add("fixture:fjzd-nullification");
            if (scene == "multi") own.AddRange(["ol:bingzhong", "ol:fengzi", Watch]); if (scene.StartsWith("lion", StringComparison.Ordinal)) own.Add(Health);
            b.AddGeneral(new(Owner, "实际范疆张达", "ol-fan-jiang-zhang-da", "fixture:fjzd-pick", "wu", 12, own, GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:fjzd-peer-" + i).ToArray();
            foreach (var peer in peers) { var skills = new List<string> { "fixture:fjzd-response" }; if (scene == "actor-black") skills.Add(Armor); if (scene == "multi") skills.Add(Skill); b.AddGeneral(new(peer, "实际其他角色", "supporter", Quiet, "qun", 12, skills)); }
            var kind = Enum.Parse<CardKind>(material, true); var kinds = armor ? new[] { CardKind.Crossbow, CardKind.Tengjia } : [kind];
            foreach (var actual in kinds) b.AddCard(new("fixture:fjzd-card-" + actual, "实际" + actual, CardCatalog.Get(actual).CategoryName, "原生实体", actual));
            b.AddDeck(new("fixture:fjzd-deck", "固定有界原生牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard("fixture:fjzd-card-" + kinds[i % kinds.Length], scene == "red" ? Suit.Heart : Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "范疆张达实际原生小局面", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:fjzd-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
