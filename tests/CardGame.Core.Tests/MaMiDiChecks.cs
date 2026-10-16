using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MaMiDiChecks
{
    private const string Bing = "ol:bingjie", Zheng = "ol:zhengding", Owner = "fixture:mmd-owner", Driver = "fixture:mmd-driver", Quiet = "fixture:mmd-quiet", Shield = "fixture:mmd-shield", Health = "fixture:mmd-health", Cost = "fixture:mmd-cost", Conditional = "fixture:mmd-conditional", Extra = "fixture:mmd-extra", Skip = "fixture:mmd-skip", Swap = "fixture:mmd-swap", Death = "fixture:mmd-death", Mode = "identity:classic-ma-mi-di-fixture";
    private const string DiscardReason = "skill-program.ma-mi-di.bingjie.discard";
    public static void BingjieNativeTargetsPrivatePaymentsBorrowedSwordAndRepeatCold()
    {
        {
            var (g, r) = Start("aoe"); Need(V(g).Hp == 5 && V(g).MaxHp == 7, "The real 4/6 body receives the native Lord +1/+1 before paying any maximum HP.");
            Activate(g, Bing); Play(g); Need(V(g).Hp == 5 && V(g).MaxHp == 6 && E<BingjieGrantedEvent>(g).Length == 1, "Actual Play pays one maximum HP without an artificial opening wound or recovery.");
            Native(g, CardKind.ArrowBarrage); Reach(g, () => Current(g) is { Stage: MaMiDiStage.TargetChoice });
            var prompt = P(g)!; var root = Frame(g); var targets = root.MaMiDi!.Targets.ToArray();
            Need(targets.SequenceEqual([1, 2, 3]) && prompt.PlayerSeat == 1 && prompt.IsPrivate && g.CreateSnapshot(0).PendingDecision is null && g.CreateSnapshot(2).PendingDecision is null,
                "The true multi-target card excludes its actor and lets each actual recipient privately choose its own HE payment.");
            var before = State(g); Need(!g.Submit(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, g.Revision)).Accepted && State(g) == before, "The source cannot submit another participant's hidden discard choice.");
            g = Cold(g, r); Play(g);
            Need(E<MaMiDiDiscardIssuedEvent>(g).Where(e => e.FrameId == root.Id).Select(e => e.TargetSeat).SequenceEqual(targets) && E<MaMiDiCompletedEvent>(g).Count(e => e.FrameId == root.Id) == 1 &&
                E<DamageAppliedEvent>(g).Length == 3, "All three native target payments finish once before the original unmodified card resolves."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("borrowed"); Activate(g, Bing); Play(g); Use(g, "equip", [1]); Play(g);
            Native(g, CardKind.BorrowedSword, [1, 3]); Play(g);
            var binding = E<MaMiDiStartedEvent>(g).Single(e => e.Op == SkillProgramEffectOp.ResolveBingjieIssuedTargetDiscards);
            Need(E<MaMiDiFrozenTargetEvent>(g).Where(e => e.FrameId == binding.FrameId).Select(e => e.TargetSeat).SequenceEqual([1]) && E<MaMiDiDiscardIssuedEvent>(g).Single().TargetSeat == 1,
                "Borrowed Sword follows the native main-target roles: its weapon holder pays, while its requested Slash victim is not a second trick target."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("add-target"); Activate(g, Bing); Play(g); Native(g, CardKind.Duel, [1]); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Swap);
            g = Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([2])); Play(g);
            Need(E<ProgramCardUseTargetAddedEvent>(g).Any(e => e.TargetSeat == 2) && E<MaMiDiDiscardIssuedEvent>(g).Select(e => e.TargetSeat).SequenceEqual([1, 2]),
                "The native finalized-use target addition happens before Bingjie freezes its real target list, including the genuinely added second target."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("repeat"); Activate(g, Bing); Play(g); var hp = V(g, 1).Hp; Native(g, CardKind.Slash, [1]);
            Reach(g, () => Optional(g, "ol:fengzi")); Activate(g, "ol:fengzi"); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "extra-effects-rank-guess") == true);
            g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "cost"); Play(g);
            Need(V(g, 1).Hp == hp - 2 && E<MaMiDiDiscardIssuedEvent>(g).Length == 1 && E<BingjieUseBoundEvent>(g).Length == 1,
                "A genuine Fengzi Slash effect repeat remains one accepted Use/designation and never reissues Bingjie's payment."); _ = Cold(g, r);
        }
    }
    public static void BingjieActualPlayStackingSourceLossSkippedPhaseAndExpiryCold()
    {
        {
            var (g, r) = Start("extra"); Activate(g, Bing); Play(g); var turn = g.State.TurnNumber; Native(g, CardKind.Slash, [1]); Play(g); End(g);
            Reach(g, () => Optional(g, Bing)); Need(g.State.TurnNumber == turn && E<BingjieGrantedEvent>(g).Length == 1, "The inserted actual Play and subsequent normal Play are distinct starts in the same real turn.");
            g = Cold(g, r); Activate(g, Bing); Play(g); Use(g, "lose-bingjie"); Play(g); Need(V(g).Skills!.All(s => s.ContentId != Bing), "The original source is really removed after both maximum-HP payments.");
            Native(g, CardKind.ArrowBarrage); Play(g);
            Need(E<BingjieGrantedEvent>(g).Length == 2 && E<MaMiDiDiscardIssuedEvent>(g).Length == 7 && E<MaMiDiStartedEvent>(g).Last(e => e.Op == SkillProgramEffectOp.ResolveBingjieIssuedTargetDiscards).GrantCount == 2,
                "Both independently paid same-turn grants survive source loss and force every actual final target to discard twice.");
            var paid = E<MaMiDiDiscardIssuedEvent>(g).Length; End(g); Reach(g, () => g.State.TurnNumber > turn && P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            Native(g, CardKind.Slash, [1]); Play(g); Need(E<MaMiDiDiscardIssuedEvent>(g).Length == paid, "Expired issued effects cannot leak into the next real turn after the source was lost."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("actor"); Activate(g, Bing); Play(g); Native(g, CardKind.Duel, [1]); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Swap);
            g = Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([2])); Play(g);
            Need(E<ProgramCardUseActorReplacedEvent>(g).Any(e => e.ActorSeat == 2) && E<BingjieUseBoundEvent>(g).Length == 0 && E<MaMiDiDiscardIssuedEvent>(g).Length == 0,
                "A real replacement of the actor before the designation trigger prevents the old actor's paid grants from attaching to somebody else's actual use."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("skip"); Play(g);
            Need(E<BingjieGrantedEvent>(g).Length == 0 && !E<MaMiDiStartedEvent>(g).Any(e => e.Op == SkillProgramEffectOp.PayBingjieTurnMaximumHp) && g.State.TurnNumber > 1,
                "A genuinely skipped first Play has no Bingjie start/payment; the later optional start can be declined normally."); _ = Cold(g, r);
        }
    }
    public static void ZhengdingActualDodgePureResponsesEffectiveColorsAndCounterspellCold()
    {
        foreach (var scenario in new[] { "response-black", "response-red", "response-opposite", "response-duel", "response-group" })
        {
            var (g, r) = Start(scenario); var expectedKind = scenario == "response-duel" || scenario == "response-group" ? DecisionKind.RespondSlash : DecisionKind.RespondDodge;
            Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.Kind == expectedKind); var maximum = V(g).MaxHp; var hp = V(g).Hp;
            g = Cold(g, r); Answer(g, c => c.Cards.Count == 1); Reach(g, () => E<CardResponseCompletedEvent>(g).Any(e => e.ActorSeat == 0) && !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MaMiDi?.Op == SkillProgramEffectOp.GrowZhengdingMatchingResponse));
            var link = E<MaMiDiResponseLinkedEvent>(g).Last(e => e.ResponseActor == 0); var gain = scenario != "response-opposite";
            Need(link.TurnOwner != 0 && link.IncomingActor != 0 && link.Provider == 0 && link.ResponseIsRed is not null && link.IncomingIsRed is not null &&
                V(g).MaxHp == maximum + (gain ? 1 : 0) && V(g).Hp == hp,
                "A true off-turn own response compares frozen effective colors, not printed suit, and increases only maximum HP: " + scenario + " " + Diagnostic(g));
            if (scenario == "response-duel" || scenario == "response-group") Need(link.ResponseKind == CardKind.Slash && E<CardRespondedEvent>(g).Any(e => e.ResponderSeat == 0), "Genuine pure Slash responses qualify without inventing a CardActionType.Use."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("response-counterspell"); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.Nullification }); var maximum = V(g).MaxHp;
            Answer(g, c => c.Cards.Count == 1); Reach(g, () => E<MaMiDiMaximumIssuedEvent>(g).Any(e => e.Op == SkillProgramEffectOp.GrowZhengdingMatchingResponse)); g = Cold(g, r);
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.Nullification }); Answer(g, c => c.Cards.Count == 1);
            Reach(g, () => E<MaMiDiResponseLinkedEvent>(g).Count(e => e.ResponseActor == 0) >= 2 && !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MaMiDi is not null));
            Need(V(g).MaxHp == maximum + 2 && E<MaMiDiResponseLinkedEvent>(g).Where(e => e.ResponseActor == 0).Last().IncomingKind == CardKind.Nullification,
                "The next actual counterspell directly answers the previous other actor's counterspell, rather than reusing the root trick's owner/color."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("own-response"); SkipOptional(g); Play(g); var maximum = V(g).MaxHp;
            Use(g, "incoming", [1]); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])) == true); Answer(g, c => c.Targets.SequenceEqual([0]));
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); Answer(g, c => c.Cards.Count == 1); Play(g);
            Need(V(g).MaxHp == maximum && E<MaMiDiResponseLinkedEvent>(g).Last(e => e.ResponseActor == 0).IncomingIsRed is null && !E<MaMiDiMaximumIssuedEvent>(g).Any(e => e.Op == SkillProgramEffectOp.GrowZhengdingMatchingResponse),
                "A genuine colorless native incoming Slash and an own-turn response cannot manufacture Zhengding, even if the stored legacy boolean happens to be false.");
            Use(g, "cardless", [1]); Play(g); Need(!E<MaMiDiMaximumIssuedEvent>(g).Any(e => e.Op == SkillProgramEffectOp.GrowZhengdingMatchingResponse), "Cardless skill damage is never a response to another character's card."); _ = Cold(g, r);
        }
    }
    public static void BingjieNativeMaximumHealthLionOxDeathAndPaidColdReturns()
    {
        {
            var (g, r) = Start("maximum-zero"); Need(V(g).Hp == 2 && V(g).MaxHp == 2, "The small non-Lord fixture begins at a genuine native two maximum HP, before either actual Play payment.");
            Activate(g, Bing); Play(g); Need(V(g).Hp == 1 && V(g).MaxHp == 1 && E<MaMiDiMaximumIssuedEvent>(g).Single().MaximumAfter == 1, "The first actual inserted-Play cost genuinely clamps both HP and maximum HP to one.");
            End(g); Reach(g, () => Optional(g, Bing)); g = Cold(g, r); Activate(g, Bing); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Death);
            var invoice = E<MaMiDiMaximumIssuedEvent>(g).Last(); var death = g.ResolutionStack.OfType<DeathFrame>().Single();
            Need(invoice.MaximumBefore == 1 && invoice.MaximumAfter == 0 && V(g).MaxHp == 0 && V(g).Hp == 0 && !V(g).IsAlive && death.ReturnKind == DeathReturnKind.ProgramSkill && death.ParentFrameId == invoice.FrameId && death.KillerSeat is null && !g.ResolutionStack.OfType<DyingFrame>().Any() && E<PlayerDyingEvent>(g).Length == 0,
                "The second genuine same-turn Play pays one-to-zero maximum HP and directly enters native owner death, without a rescueable Dying attempt or fabricated HP loss.");
            g = Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, () => E<MaMiDiCompletedEvent>(g).Any(e => e.FrameId == invoice.FrameId));
            Need(E<MaMiDiMaximumIssuedEvent>(g).Length == 2 && E<BingjieGrantedEvent>(g).Length == 1 && E<MaMiDiCompletedEvent>(g).Count(e => e.FrameId == invoice.FrameId) == 1 && g.State.Winner == Winner.None,
                "Native direct-death cleanup returns to the original paid instruction once; the dead source cannot receive a second usable turn entitlement."); _ = Cold(g, r);
        }
        foreach (var scenario in new[] { "maximum-loss", "maximum-death" })
        {
            var (g, r) = Start(scenario); Activate(g, Bing); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Health);
            Need(Current(g) is { Stage: MaMiDiStage.MaximumChildren } && E<MaMiDiMaximumIssuedEvent>(g).Length == 1, "The genuine maximum-HP observation pauses below its paid owning instruction.");
            g = Cold(g, r); Continue(g);
            if (scenario == "maximum-loss")
            {
                Play(g); Need(V(g).Skills!.All(s => s.ContentId != Bing) && E<BingjieGrantedEvent>(g).Length == 1, "A real clamp removes the conditional source before the HP child returns, without canceling the completed cost.");
                Native(g, CardKind.Slash, [1]); Play(g); Need(E<MaMiDiDiscardIssuedEvent>(g).Length == 1, "The issued effect remains usable after its source vanished inside payment children.");
            }
            else
            {
                Reach(g, () => g.ResolutionStack.OfType<DyingFrame>().Any()); g = Cold(g, r); Reach(g, () => !V(g).IsAlive && E<MaMiDiCompletedEvent>(g).Length == 1);
                Need(g.State.Winner == Winner.None && E<MaMiDiMaximumIssuedEvent>(g).Length == 1 && E<BingjieGrantedEvent>(g).Length == 0, "Real non-Lord death drains the paid maximum-HP child and completes the original receipt once without a second cost or a new usable post-death entitlement.");
            }
            _ = Cold(g, r);
        }
        foreach (var scenario in new[] { "lion", "ox", "target-death" })
        {
            var (g, r) = Start(scenario); Activate(g, Bing); Play(g); Use(g, "equip", [1]); Play(g); var equipment = V(g, 1).Equipment.Single().Id;
            Use(g, "wound-peer", [1]); Play(g); TrimPeer(g, 1);
            Native(g, scenario == "target-death" ? CardKind.ArrowBarrage : CardKind.Slash, scenario == "target-death" ? null : [1]);
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == (scenario == "lion" ? Health : Cost));
            Need(E<MaMiDiDiscardIssuedEvent>(g).Single().CardId == equipment && Current(g) is { Stage: MaMiDiStage.TargetChildren }, "The target's only legal equipment is truly discarded before its native removal/movement child pauses.");
            g = Cold(g, r); Continue(g);
            if (scenario == "target-death") { Reach(g, () => g.ResolutionStack.OfType<DyingFrame>().Any()); g = Cold(g, r); }
            Play(g);
            if (scenario == "lion") Need(E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == 1 && e.Amount == 1), "The actual foreign Silver Lion heals through the exact original paid-discard recovery parent.");
            if (scenario == "target-death") Need(!V(g, 1).IsAlive && E<MaMiDiDiscardIssuedEvent>(g).Select(e => e.TargetSeat).SequenceEqual([1, 2, 3]), "A real target death does not replay its paid cost or cancel later frozen target discards.");
            Need(E<MaMiDiDiscardIssuedEvent>(g).Count(e => e.CardId == equipment) == 1, "Native child returns never discard the original equipment twice."); _ = Cold(g, r);
        }
        {
            var (g, r) = Start("human-ox"); SkipOptional(g); Play(g); Native(g, CardKind.WoodenOx); Play(g); var ox = V(g).Equipment.Single(c => c.Kind == CardKind.WoodenOx).Id;
            var store = g.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.UseEquipmentEffect && a.EquipmentKind == CardKind.WoodenOx); var grain = store.SelectableCardIds.First();
            Accept(g, new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], g.Revision, P(g)!.PromptId)); Play(g); Need(V(g).WoodenOxGrain!.Single().Id == grain, "A real human equipment command stores the original hand entity before the foreign turn.");
            End(g); Reach(g, () => Current(g) is { Stage: MaMiDiStage.TargetChoice } && P(g) is { PlayerSeat: 0 });
            Need(Frame(g).OwnerSeat != 0 && g.CreateSnapshot(Frame(g).OwnerSeat).PendingDecision is null, "The actual foreign paid Bingjie owner cannot see the human recipient's hand/equipment choice.");
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([ox])); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Cost); g = Cold(g, r); Continue(g);
            Reach(g, () => E<MaMiDiDiscardIssuedEvent>(g).Any(e => e.CardId == ox) && !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MaMiDi is not null));
            Need(!V(g).Equipment.Any(c => c.Id == ox) && (V(g).WoodenOxGrain is null or { Count: 0 }) && g.CardMovements.Count(m => m.CardId == grain && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1,
                "Removing the actual stocked Wooden Ox spills its one original grain through the exact paid native movement subtree, once across cold returns."); _ = Cold(g, r);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame Frame(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.MaMiDi is not null);
    private static MaMiDiReceipt? Current(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MaMiDi is not null)?.MaMiDi;
    private static bool Optional(GameEngine g, string skill) => P(g)?.SkillPrompt?.SkillId == skill && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real Ma Mi Di command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Need(V(g, p.PlayerSeat).IsHuman, "Never submit an AI player's answer."); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void Activate(GameEngine g, string skill) { Reach(g, () => Optional(g, skill)); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
    private static void SkipOptional(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Continue(GameEngine g) { if (V(g, P(g)!.PlayerSeat).IsHuman) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); else Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    private static void Use(GameEngine g, string activation, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Native(GameEngine g, CardKind kind, int[]? targets = null)
    { var a = g.GetHumanLegalActions().First(a => (a.PlayedCardKind ?? V(g).Hand.FirstOrDefault(c => c.Id == a.CardId)?.Kind) == kind && (targets is null || a.TargetSeats.SequenceEqual(targets))); Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource }); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void TrimPeer(GameEngine g, int seat)
    { while (V(g, seat).HandCount > 0) { Use(g, "trim-peer", [seat]); Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true); Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == "0"); Play(g); } }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 400; step++)
        {
            if (done()) return; if (g.State.Status == EngineStatus.Completed) throw new InvalidOperationException("Ma Mi Di completed before expected real boundary: " + Diagnostic(g)); var p = P(g);
            if (p is not null && !V(g, p.PlayerSeat).IsHuman) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) SkipOptional(g);
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue") == true) Continue(g);
            else if (p is not null) throw new InvalidOperationException("Unexpected Ma Mi Di prompt: " + Diagnostic(g)); else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Missing Ma Mi Di boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase, Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(3).Select(c => c.Parameters) } : null, Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).MaxHp, V(g, s).HandCount, V(g, s).IsAlive }), Tail = g.Events.TakeLast(10).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(cold), "Cold restore preserves exact maximum-HP invoices, frozen issued participants, private recipient payments and true response colors without repeating costs."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario)); var response = scenario.StartsWith("response-", StringComparison.Ordinal); var role = response || scenario is "maximum-death" or "maximum-zero" ? Role.Renegade : Role.Lord;
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = role, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral }); Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        if (!response && scenario != "skip") Reach(g, () => Optional(g, Bing)); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ma-mi-di", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:ma-mi-di"); if (scenario == "repeat") CharacterFixtureContent.Register(b, "ol:wang-rong"); if (scenario == "response-opposite") CharacterFixtureContent.Register(b, "classic:xiao-qiao");
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],"viewAs":[],"activations":[
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"trim-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"wound-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"lose-bingjie","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Bing}}"],"sourceBind":"standard:none"}]},
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]},
                {"id":"cardless","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Shield}}","revision":1,"cardPolicies":[{"id":"only-real-human-is-legal","kind":"prohibitTarget","cardKinds":["slash","fireSlash","thunderSlash","duel","barbarianAssault","arrowBarrage"]}]},
              {"id":"{{Extra}}","revision":1,"triggers":[{"id":"true-extra-play","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
              {"id":"{{Skip}}","revision":1,"triggers":[{"id":"true-skipped-play","window":"afterNormalDraw","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"real-maximum-clamp","skillId":"{{Bing}}","minimumHp":9}]},
              {"id":"{{Health}}","revision":1,"triggers":[{"id":"real-health-child","window":"afterHealthChanged","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]},
              {"id":"{{Cost}}","revision":1,"triggers":[{"id":"true-equipment-payment","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch","movementReasons":["{{DiscardReason}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            if (scenario == "lion") rules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Health)!["triggers"]![0]!["window"] = "afterHpRecovered";
            rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Death}}","revision":1,"triggers":[{"id":"real-zero-maximum-death","window":"ownerDied","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"draw","target":"selectedTarget","amount":1}]}]}"""));
            rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Swap}}","revision":1,"triggers":[{"id":"real-native-role-change","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["duel"],"priority":100,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"{{(scenario == "add-target" ? "addCurrentCardUseTarget" : "replaceCurrentCardUseActor")}}","target":"selectedTarget"}]}]}"""));
            var views = rules["skills"]![0]!["viewAs"]!.AsArray();
            foreach (var kind in new[] { CardKind.Slash, CardKind.ArrowBarrage, CardKind.BorrowedSword, CardKind.Duel })
            {
                var node = JsonSerializer.SerializeToNode(new { id = "native-" + kind, inputKinds = Array.Empty<string>(), inputSuits = Array.Empty<string>(), outputKind = char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..], forPlay = true, forResponse = kind == CardKind.Slash, allowSameKind = true });
                if (kind is CardKind.ArrowBarrage or CardKind.BorrowedSword or CardKind.Duel) node!["singleCardTrickUse"] = true; views.Add(node);
            }
            views.Add(JsonSerializer.SerializeToNode(new { id = "response-dodge", inputKinds = Array.Empty<string>(), inputSuits = Array.Empty<string>(), outputKind = "dodge", forPlay = false, forResponse = true, allowSameKind = true }));
            views.Add(JsonSerializer.SerializeToNode(new { id = "response-counterspell", inputKinds = Array.Empty<string>(), inputSuits = Array.Empty<string>(), outputKind = "nullification", forPlay = false, forResponse = true, allowSameKind = true }));
            if (scenario == "maximum-death") rules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Health)!["triggers"]![0]!["effects"]!.AsArray().Add(JsonNode.Parse("{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}"));
            if (scenario == "target-death") rules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Cost)!["triggers"]![0]!["effects"]!.AsArray().Add(JsonNode.Parse("{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}"));
            var labels = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "马日磾真实原生边界", optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })); foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "真实原生边界") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            var response = scenario.StartsWith("response-", StringComparison.Ordinal); var role = response || scenario is "maximum-death" or "maximum-zero" ? Role.Renegade : Role.Lord;
            b.AddSkill(new("fixture:mmd-pick", "本人公开评分", "固定本人") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == role ? 100000d : -100000d) });
            var own = new List<string> { Driver, Zheng, scenario == "maximum-loss" ? Conditional : Bing }; if (scenario.StartsWith("maximum-", StringComparison.Ordinal)) own.Add(Health); if (scenario == "repeat") own.Add("ol:fengzi"); if (scenario == "extra") own.Add(Extra); if (scenario == "skip") own.Add(Skip); if (scenario == "response-opposite") own.Add("classic:hongyan"); if (scenario is "actor" or "add-target") own.Add(Swap); if (scenario == "human-ox") own.Add(Cost);
            if (scenario == "maximum-zero") { own.Remove(Health); own.Add(Extra); own.Add(Death); }
            b.AddGeneral(new(Owner, "实际马日磾", "ol-ma-mi-di", "fixture:mmd-pick", "qun", scenario == "maximum-zero" ? 2 : scenario.StartsWith("maximum-", StringComparison.Ordinal) ? 8 : 6, own) { InitialHp = scenario == "maximum-zero" ? 2 : scenario.StartsWith("maximum-", StringComparison.Ordinal) ? 8 : 4 });
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:mmd-peer-" + i).ToArray();
            foreach (var (peer, index) in peers.Select((peer, index) => (peer, index)))
            {
                var pick = "fixture:mmd-pick-" + index; b.AddSkill(new(pick, "其他公开评分", "固定顺序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == role ? -100000d : 100003d - index) });
                var skills = response ? new List<string> { Shield } : new List<string> { Quiet }; if (scenario == "response-counterspell") skills.Add(Driver); if (scenario == "lion") skills.Add(Health); if (scenario is "ox" or "target-death") skills.Add(Cost);
                if (scenario == "human-ox") { if (index == 0) { skills.Remove(Quiet); skills.Add(Driver); skills.Add(Bing); } else skills.Add(Shield); }
                b.AddGeneral(new(peer, "实际其他角色", "supporter", pick, "wei", 8, skills));
            }
            var deckKind = scenario switch { "borrowed" => CardKind.QilinBow, "lion" => CardKind.SilverLion, "ox" or "human-ox" => CardKind.WoodenOx, "target-death" => CardKind.Crossbow, "response-duel" => CardKind.Duel, "response-group" => CardKind.BarbarianAssault, "response-counterspell" => CardKind.Nullification, _ => CardKind.Slash };
            b.AddCard(new("fixture:mmd-card", "原生固定实体", deckKind is CardKind.QilinBow or CardKind.SilverLion or CardKind.WoodenOx or CardKind.Crossbow ? "装备牌" : deckKind is CardKind.Slash ? "基本牌" : "锦囊牌", "固定真实实体", deckKind));
            b.AddDeck(new("fixture:mmd-deck", "小固定牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("fixture:mmd-card", scenario == "response-red" ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "马日磾实际规则", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:mmd-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
