using System;
using System.Collections.Generic;

namespace Game
{
    public enum TriggerKind
    {
        Unknown,
        GameStart,
        SceneStart,
        Appears,
        KeyHeld,
        KeyPressed,
        KeyReleased,
        WheelScrollUp,
        WheelScrollDown,
        Touches,
        Overlaps,
        Near,
        Clicked,
        RightClicked,
        MiddleClicked,
        DoubleClicked,
        Hovered,
        // UI text input (Task 15 Part C)
        TextChanged,
        Focused,
        LostFocus,
        Submitted,
        ButtonPressed,
        ButtonReleased,
        Arrives,
        Sees,
        StopsSeeing,
        // Hearing (Task 16 Part B)
        Hears,
        // Weapons & ammo (Task 16 Part C)
        ReloadComplete,
        Equipped,
        Unequipped,
        Dropped,
        Every,
        WatchBecomes,
        WatchBecomesNot,
        WatchChanges,
        Message,
        AnimEnd,
        /// <summary><c>when music ends</c> / <c>when music "&lt;name&gt;" ends</c> (Task 31).</summary>
        MusicEnd,
        StateIs,
        ParticleEnd,
        TweenEnd,
        // RTS selection (Task 18 Part A)
        Selected,
        Deselected,
        SelectionChanged,
        ControlGroupPressed,
        ControlGroupHeld,
        // RTS resource economy (Task 18 Part B)
        ResourceChanges,
        // RTS construction & base building (Task 18 Part C)
        BuildingPlaced,
        ConstructionCompleted,
        BuildingDestroyed,
        // RTS production queues & unit training (Task 18 Part D)
        TrainingStarted,
        TrainingFinished,
        ProductionQueueEmpty,
        // RTS orders & attack-move (Task 18 Part E)
        OrderCompletes,
        OrderStarts,
        PathBlocked,
        // RTS fog of war & visibility (Task 18 Part F)
        AreaRevealed,
        AreaHidden,
        EntersFog,
        // RTS damage & per-unit health (Task 18 Part G)
        HealthLow,
        // RTS target-pick mode (Task 27 Part A)
        AimPicked,
        // Buffs and debuffs (Task 27 Part C)
        BuffStarts,
        BuffEnds,
        // Local multiplayer (Task 16 Part F)
        PlayerJoins,
        PlayerDies,
        PlayerScoreChanges,
        PlayerKills,
        AllPlayersDead,
        // Per-player input bindings (Task 16 input follow-ups)
        BindingChanges,
        // Level progression (Task 16 Part G)
        LevelComplete,
        AllEnemiesDead,
    }

    public class TriggerDescriptor
    {
        public TriggerKind Kind = TriggerKind.Unknown;
        public string Key = "";
        public string RequireName = "";
        /// <summary>For <c>when &lt;building&gt; finishes training &lt;unit&gt;</c>: the trained
        /// unit's name, matched against the signal's <c>Name</c>.</summary>
        public string RequireUnit = "";
        /// <summary>For <c>when me touches &lt;material&gt;</c>: the physics-material name the
        /// touched actor's collider must carry (matched in addition to, or instead of, a name).</summary>
        public string RequireMaterial = "";
        /// <summary>For <c>when message "damage &lt;amount&gt;" arrives</c>: the quoted text is a
        /// template whose <c>&lt;slot&gt;</c> parts capture a number from the message
        /// (see <see cref="MessagePattern"/>).</summary>
        public bool MessagePattern;
        public double Distance;
        /// <summary>For `by my SightDistance`: the sheet actor's own variable to read the
        /// range from each poll (typed-enemy stats, Task 16 Part D).</summary>
        public string DistanceVariable = "";
        public double Seconds;
        public string WatchOwner = "";
        public string WatchName = "";
        public ValueNode WatchTarget;
        /// <summary>Additional conditions parsed from "and ..." clauses in trigger lines
        /// (e.g. "me is moving" in "when me touches Grass and me is moving").</summary>
        public List<ConditionNode> ExtraConditions = new List<ConditionNode>();
        /// <summary>For <c>when my Health is lower than &lt;n&gt;</c>: the value the stat drops below.</summary>
        public double Threshold;
        /// <summary>The player a <c>when player &lt;n&gt; ...</c> trigger listens to.</summary>
        public int Player;
        /// <summary>The victim of <c>when player &lt;n&gt; kills player &lt;m&gt;</c>.</summary>
        public int Player2;
    }

    public static class TriggerClassifier
    {
        public static TriggerDescriptor Classify(EventNode ev)
        {
            var trigger = ev.Trigger;
            var words = new string[trigger.Count];
            for (int i = 0; i < trigger.Count; i++)
                words[i] = trigger[i].Type == TokenType.Word ? trigger[i].Text : trigger[i].Text;



            if (words.Length >= 2 && words[0] == "every")
                return EveryDescriptor(trigger, ev);
            if (words.Length >= 2 && words[0] == "wheel")
                return new TriggerDescriptor
                {
                    Kind = words[words.Length - 1] == "up" ? TriggerKind.WheelScrollUp : TriggerKind.WheelScrollDown,
                };
            // `when binding changes` — a key was captured by the options menu (Task 16 input
            // follow-ups). Sits before the generic `<var> changes` watcher.
            if (words.Length == 2 && words[0] == "binding" && words[1] == "changes")
                return new TriggerDescriptor { Kind = TriggerKind.BindingChanges };
            // ---- local multiplayer (Task 16 Part F) ----
            // `when all players dead` and the four per-player edges. They sit before the generic
            // `<key> is pressed` / `changes` shapes so `player` is never read as an input key.
            if (words.Length == 3 && words[0] == "all" && words[1] == "players" && words[2] == "dead")
                return new TriggerDescriptor { Kind = TriggerKind.AllPlayersDead };
            if (Players.TryReadReference(words, 0, out int playerNumber, out int playerUsed))
            {
                int rest = playerUsed;
                if (words.Length == rest + 1 && words[rest] == "joins")
                    return new TriggerDescriptor { Kind = TriggerKind.PlayerJoins, Player = playerNumber };
                if (words.Length == rest + 1 && words[rest] == "dies")
                    return new TriggerDescriptor { Kind = TriggerKind.PlayerDies, Player = playerNumber };
                if (words.Length == rest + 2 && words[rest] == "score" && words[rest + 1] == "changes")
                    return new TriggerDescriptor { Kind = TriggerKind.PlayerScoreChanges, Player = playerNumber };
                if (words.Length >= rest + 3 && words[rest] == "kills" &&
                    Players.TryReadReference(words, rest + 1, out int victimNumber, out int victimUsed) &&
                    rest + 1 + victimUsed == words.Length)
                    return new TriggerDescriptor
                    {
                        Kind = TriggerKind.PlayerKills,
                        Player = playerNumber,
                        Player2 = victimNumber,
                    };
            }
            // `when reload completes` — the current weapon's reload timer ran out (Task 16 Part C).
            if (words.Length >= 2 && words[0] == "reload" && words[1] == "completes")
                return new TriggerDescriptor { Kind = TriggerKind.ReloadComplete };
            // `when buff "stim" starts` / `when buff "stim" ends` (Task 27 Part C) — the buff'ed
            // actor's own sheet hears it, with the buff name matched against the signal.
            if (words.Length == 3 && words[0] == "buff" &&
                (words[2] == "starts" || words[2] == "ends"))
                return new TriggerDescriptor
                {
                    Kind = words[2] == "starts" ? TriggerKind.BuffStarts : TriggerKind.BuffEnds,
                    RequireName = trigger[1].Text,
                };
            // RTS selection (Task 18 Part A). Call these before the generic key shape:
            // `when Marine is selected` would otherwise read as the key "Marine" being held.
            if (words.Length >= 3 && words[words.Length - 2] == "is" && words[words.Length - 1] == "selected")
                return new TriggerDescriptor
                {
                    Kind = TriggerKind.Selected,
                    RequireName = Join(trigger, 0, words.Length - 2),
                };
            if (words.Length >= 3 && words[words.Length - 2] == "is" && words[words.Length - 1] == "deselected")
                return new TriggerDescriptor
                {
                    Kind = TriggerKind.Deselected,
                    RequireName = Join(trigger, 0, words.Length - 2),
                };
            // RTS construction (Task 18 Part C). These sit before the generic key shape so
            // `when Barracks is placed` is not read as a key called "Barracks".
            if (words.Length >= 3 && words[words.Length - 2] == "is" && words[words.Length - 1] == "placed")
                return new TriggerDescriptor
                {
                    Kind = TriggerKind.BuildingPlaced,
                    RequireName = Join(trigger, 0, words.Length - 2),
                };
            if (words.Length >= 2 && words[words.Length - 2] == "construction" && words[words.Length - 1] == "completes")
                return new TriggerDescriptor
                {
                    Kind = TriggerKind.ConstructionCompleted,
                    RequireName = Join(trigger, 0, words.Length - 2),
                };
            if (words.Length >= 3 && words[words.Length - 2] == "is" && words[words.Length - 1] == "destroyed")
                return new TriggerDescriptor
                {
                    Kind = TriggerKind.BuildingDestroyed,
                    RequireName = Join(trigger, 0, words.Length - 2),
                };
            // RTS production (Task 18 Part D). `when Barracks finishes training Marine` and
            // `when Barracks starts producing Marine` carry both the building and the unit.
            int finishesIndex = IndexOf(words, "finishes");
            if (finishesIndex > 0 && finishesIndex + 2 < words.Length && words[finishesIndex + 1] == "training")
                return new TriggerDescriptor
                {
                    Kind = TriggerKind.TrainingFinished,
                    RequireName = Join(trigger, 0, finishesIndex),
                    RequireUnit = Join(trigger, finishesIndex + 2, words.Length),
                };
            int startsIndex = IndexOf(words, "starts");
            if (startsIndex > 0 && startsIndex + 2 < words.Length &&
                (words[startsIndex + 1] == "producing" || words[startsIndex + 1] == "training"))
                return new TriggerDescriptor
                {
                    Kind = TriggerKind.TrainingStarted,
                    RequireName = Join(trigger, 0, startsIndex),
                    RequireUnit = Join(trigger, startsIndex + 2, words.Length),
                };
            // `when production queue is empty` (any building) and the named form
            // `when Barracks production queue is empty`.
            if (words.Length >= 4 && words[words.Length - 4] == "production" &&
                words[words.Length - 3] == "queue" && words[words.Length - 2] == "is" &&
                words[words.Length - 1] == "empty")
                return new TriggerDescriptor
                {
                    Kind = TriggerKind.ProductionQueueEmpty,
                    RequireName = Join(trigger, 0, words.Length - 4),
                };
            // RTS orders & attack-move (Task 18 Part E). The bare forms listen to the
            // acting unit; the optional `<order> order completes` form filters by order name.
            if (words.Length == 2 && words[0] == "order" && words[1] == "completes")
                return new TriggerDescriptor { Kind = TriggerKind.OrderCompletes };
            if (words.Length == 3 && words[0] == "queued" && words[1] == "order" && words[2] == "starts")
                return new TriggerDescriptor { Kind = TriggerKind.OrderStarts };
            if (words.Length == 2 && words[0] == "path" && words[1] == "blocked")
                return new TriggerDescriptor { Kind = TriggerKind.PathBlocked };
            // `when my aim picks a target` (Task 27 Part A) — the click that resolved `aim`.
            // Sits before the generic `my`/`changes` shapes so `aim` is never read as a variable.
            int picksIndex = IndexOf(words, "picks");
            if (picksIndex >= 1 && words[picksIndex - 1] == "aim" && picksIndex + 2 < words.Length &&
                words[picksIndex + 1] == "a" && words[picksIndex + 2] == "target")
                return new TriggerDescriptor { Kind = TriggerKind.AimPicked };
            if (words.Length >= 3 && words[words.Length - 2] == "order" &&
                (words[words.Length - 1] == "completes" || words[words.Length - 1] == "starts"))
                return new TriggerDescriptor
                {
                    Kind = words[words.Length - 1] == "completes"
                        ? TriggerKind.OrderCompletes
                        : TriggerKind.OrderStarts,
                    RequireName = Join(trigger, 0, words.Length - 2),
                };
            // RTS fog of war & visibility (Task 18 Part F). `when area becomes revealed` /
            // `when area becomes hidden` listen to the local player's vision edges, and
            // `when <unit> enters fog` names the unit that just left that vision.
            if (words.Length == 3 && words[0] == "area" && words[1] == "becomes" &&
                (words[2] == "revealed" || words[2] == "hidden"))
                return new TriggerDescriptor
                {
                    Kind = words[2] == "revealed" ? TriggerKind.AreaRevealed : TriggerKind.AreaHidden,
                };
            if (words.Length >= 3 && words[words.Length - 2] == "enters" && words[words.Length - 1] == "fog")
                return new TriggerDescriptor
                {
                    Kind = TriggerKind.EntersFog,
                    RequireName = Join(trigger, 0, words.Length - 2),
                };
            // RTS damage & per-unit health (Task 18 Part G). `when my Health is lower than 30`
            // is a polled threshold edge on a per-unit health stat; it sits before the generic
            // `<var> is <value>` shapes so the stat is never read as a global variable.
            int lowerIndex = IndexOf(words, "lower");
            if (lowerIndex >= 2 && words[lowerIndex - 1] == "is" && lowerIndex + 2 < words.Length &&
                words[lowerIndex + 1] == "than" && trigger[lowerIndex + 2].Type == TokenType.Number)
            {
                int subjectFrom = words[0] == "my" ? 1 : 0;
                string subject = Join(trigger, subjectFrom, lowerIndex - 1);
                if (HealthManager.TryStat(subject, out string stat))
                    return new TriggerDescriptor
                    {
                        Kind = TriggerKind.HealthLow,
                        WatchOwner = "my",
                        WatchName = stat,
                        Threshold = trigger[lowerIndex + 2].Number,
                    };
            }
            if (words.Length == 2 && words[0] == "selection" && words[1] == "changes")
                return new TriggerDescriptor { Kind = TriggerKind.SelectionChanged };
            if (words.Length == 5 && words[0] == "control" && words[1] == "group" &&
                words[3] == "is" && (words[4] == "pressed" || words[4] == "held") &&
                trigger[2].Type == TokenType.Number)
            {
                int group = (int)trigger[2].Number;
                return new TriggerDescriptor
                {
                    Kind = words[4] == "pressed" ? TriggerKind.ControlGroupPressed : TriggerKind.ControlGroupHeld,
                    RequireName = group.ToString(),
                    Key = Selection.KeyForGroup(group),
                };
            }
            // ---- level progression (Task 16 Part G) ----
            // A level is a scene, so `when level starts` is the same edge as `when scene
            // starts`; `when level completes` has its own signal, raised by `next level`.
            if (words.Length == 2 && words[0] == "level" && words[1] == "starts")
                return new TriggerDescriptor { Kind = TriggerKind.SceneStart };
            if (words.Length == 2 && words[0] == "level" && words[1] == "completes")
                return new TriggerDescriptor { Kind = TriggerKind.LevelComplete };
            // `when all enemies dead` — polled once a tick (every enemy type is `my Type`-tagged).
            if (words.Length == 3 && words[0] == "all" && words[1] == "enemies" && words[2] == "dead")
                return new TriggerDescriptor { Kind = TriggerKind.AllEnemiesDead };
            if (words.Length >= 2 && words[0] == "game" && words[words.Length - 1] == "starts")
                return new TriggerDescriptor { Kind = TriggerKind.GameStart };
            if (words.Length >= 2 && words[0] == "scene" && words[words.Length - 1] == "starts")
                return new TriggerDescriptor { Kind = TriggerKind.SceneStart };
            if (words.Length >= 3 && words[0] == "state" && words[1] == "is")
                return new TriggerDescriptor { Kind = TriggerKind.StateIs, RequireName = Join(trigger, 2, words.Length) };
            // `when <thing> is double-clicked` / `right-clicked` / `middle-clicked` — hyphenated words
            // are a single lexed token, so the suffix sits in the last word.
            if (words.Length >= 3 && words[words.Length - 2] == "is")
            {
                string suffix = words[words.Length - 1];
                if (suffix == "double-clicked" || suffix == "right-clicked" || suffix == "middle-clicked")
                {
                    return new TriggerDescriptor
                    {
                        Kind = suffix == "double-clicked" ? TriggerKind.DoubleClicked :
                               suffix == "right-clicked" ? TriggerKind.RightClicked :
                               TriggerKind.MiddleClicked,
                        RequireName = Join(trigger, 0, words.Length - 2),
                    };
                }
            }
            // `when <thing> text changes` / `when <thing> loses focus` / `when <thing> submits` /
            // `when <thing> is focused` — UI text input (Task 15 Part C). These must come before the
            // generic key shape (`<name> is <word>`) and the `changes` watcher below.
            if (words.Length >= 3 && words[words.Length - 2] == "text" && words[words.Length - 1] == "changes")
                return new TriggerDescriptor { Kind = TriggerKind.TextChanged, RequireName = Join(trigger, 0, words.Length - 2) };
            if (words.Length >= 3 && words[words.Length - 2] == "loses" && words[words.Length - 1] == "focus")
                return new TriggerDescriptor { Kind = TriggerKind.LostFocus, RequireName = Join(trigger, 0, words.Length - 2) };
            if (words.Length >= 2 && words[words.Length - 1] == "submits")
                return new TriggerDescriptor { Kind = TriggerKind.Submitted, RequireName = Join(trigger, 0, words.Length - 1) };
            if (words.Length >= 3 && words[words.Length - 2] == "is" && words[words.Length - 1] == "focused")
                return new TriggerDescriptor { Kind = TriggerKind.Focused, RequireName = Join(trigger, 0, words.Length - 2) };
            // `when <thing> is pressed` / `when <thing> is released` — button edge triggers.
            // `me`/`other` are allowed (`when me is pressed` = this sheet's own button). Only when
            // <thing> is NOT a key name: `when W is pressed` stays a key press.
            if (words.Length >= 3 && words[words.Length - 2] == "is" &&
                (words[words.Length - 1] == "pressed" || words[words.Length - 1] == "released") &&
                !IsKeyName(Join(trigger, 0, words.Length - 2)))
            {
                return new TriggerDescriptor
                {
                    Kind = words[words.Length - 1] == "pressed" ? TriggerKind.ButtonPressed : TriggerKind.ButtonReleased,
                    RequireName = Join(trigger, 0, words.Length - 2),
                };
            }
            if (words.Length >= 3 && words[words.Length - 2] == "is" && words[words.Length - 1] != "clicked" && words[0] != "me" && words[0] != "other")
                return KeyDescriptor(words, trigger);
            if (words.Length >= 4 && words[words.Length - 3] == "is" && words[words.Length - 2] == "right" && words[words.Length - 1] == "clicked")
                return new TriggerDescriptor { Kind = TriggerKind.RightClicked, RequireName = Join(trigger, 0, words.Length - 3) };
            if (words.Length >= 4 && words[words.Length - 3] == "is" && words[words.Length - 2] == "middle" && words[words.Length - 1] == "clicked")
                return new TriggerDescriptor { Kind = TriggerKind.MiddleClicked, RequireName = Join(trigger, 0, words.Length - 3) };
            if (words.Length >= 3 && words[words.Length - 2] == "is" && words[words.Length - 1] == "clicked")
                return new TriggerDescriptor { Kind = TriggerKind.Clicked, RequireName = Join(trigger, 0, words.Length - 2) };
            if (words.Length >= 3 && words[0] == "message")
            {
                // `when message "damage <amount>" arrives` — the quoted text is a template whose
                // `<slot>` parts capture a number from the message (Task 18 Part G). A slot-less
                // text stays an exact name match, so old sheets are unchanged.
                string messageName = trigger[1].Text;
                return new TriggerDescriptor
                {
                    Kind = TriggerKind.Message,
                    RequireName = messageName,
                    MessagePattern = MessagePattern.IsPattern(messageName),
                };
            }
            if (words.Length >= 3 && words[0] == "anim")
                return new TriggerDescriptor { Kind = TriggerKind.AnimEnd, RequireName = trigger[1].Text };
            // `when music ends` — the one music player stopped. The optional quoted name only
            // matches that track, so a menu can hear its own jingle end (Task 31).
            if (words.Length >= 2 && words[0] == "music" && words[words.Length - 1] == "ends")
                return new TriggerDescriptor
                {
                    Kind = TriggerKind.MusicEnd,
                    RequireName = words.Length >= 3 ? trigger[1].Text : "",
                };
            if (words.Length >= 3 && words[0] == "particle" && words[words.Length - 1] == "ends")
                return new TriggerDescriptor { Kind = TriggerKind.ParticleEnd, RequireName = trigger[1].Text };
            if (words.Length >= 3 && words[0] == "tween" && words[words.Length - 1] == "ends")
                return new TriggerDescriptor { Kind = TriggerKind.TweenEnd, RequireName = Join(trigger, 1, words.Length - 1) };
            if (words.Length >= 2 && (words[0] == "me" || words[0] == "other"))
                return PronounDescriptor(trigger, ev);
            int becomesIndex = IndexOf(words, "becomes");
            if (becomesIndex >= 0)
                return WatchDescriptor(trigger, ev, becomesIndex, becomesNot: false);
            int notIndex = IndexOf(words, "not");
            if (notIndex > 0 && IndexOf(words, "becomes") > 0 && IndexOf(words, "becomes") < notIndex)
                return WatchDescriptor(trigger, ev, IndexOf(words, "becomes"), becomesNot: true);
            if (words.Length >= 2 && (words[0] == "resources" || words[0] == "resource") &&
                (words[words.Length - 1] == "change" || words[words.Length - 1] == "changes"))
                return new TriggerDescriptor { Kind = TriggerKind.ResourceChanges };
            if (words.Length == 1 && string.Equals(words[0], "ResourcesChange", StringComparison.OrdinalIgnoreCase))
                return new TriggerDescriptor { Kind = TriggerKind.ResourceChanges };
            if (words.Length >= 2 && words[words.Length - 1] == "changes")
            {
                var changes = new TriggerDescriptor { Kind = TriggerKind.WatchChanges };
                SetWatchName(changes, trigger, IndexOf(words, "changes"));
                return changes;
            }
            return new TriggerDescriptor { Kind = TriggerKind.Unknown };
        }

        private static TriggerDescriptor EveryDescriptor(List<Token> trigger, EventNode ev)
        {
            double seconds = 1;
            if (trigger.Count >= 2 && trigger[1].Type == TokenType.Number)
                seconds = trigger[1].Number;
            return new TriggerDescriptor { Kind = TriggerKind.Every, Seconds = seconds };
        }

        private static TriggerDescriptor KeyDescriptor(string[] words, List<Token> trigger)
        {
            int isIndex = words.Length - 2;
            var keyName = Join(trigger, 0, isIndex);
            // `when 1 is pressed` — people write the top-row number keys as digits, while the
            // input layer reports them as `D1`. Keep the written name for matching, but poll
            // with the engine name.
            var key = new TriggerDescriptor { Key = ActionWords.NormalizeKeyName(keyName), RequireName = keyName };
            switch (words[words.Length - 1])
            {
                case "pressed":
                    key.Kind = TriggerKind.KeyPressed;
                    break;
                case "released":
                    key.Kind = TriggerKind.KeyReleased;
                    break;
                default:
                    key.Kind = TriggerKind.KeyHeld;
                    break;
            }
            return key;
        }

        private static TriggerDescriptor PronounDescriptor(List<Token> trigger, EventNode ev)
        {
            var words = new string[trigger.Count];
            for (int i = 0; i < trigger.Count; i++)
                words[i] = trigger[i].Text;
            var descriptor = new TriggerDescriptor();
            if (words.Length >= 3 && words[1] == "touches")
            {
                descriptor.Kind = TriggerKind.Touches;
                int andIndex = IndexOf(words, "and");
                if (andIndex > 2)
                {
                    descriptor.RequireName = Join(trigger, 2, andIndex);
                    descriptor.RequireMaterial = descriptor.RequireName;
                    var extraTokens = new List<Token>();
                    for (int i = andIndex + 1; i < trigger.Count; i++)
                        extraTokens.Add(trigger[i]);
                    var cond = Conditions.Parse(extraTokens);
                    if (cond.Kind != ConditionKind.Unknown)
                        descriptor.ExtraConditions.Add(cond);
                }
                else
                {
                    descriptor.RequireName = Join(trigger, 2, words.Length);
                    descriptor.RequireMaterial = descriptor.RequireName;
                }
                return descriptor;
            }
            if (words.Length >= 3 && words[1] == "overlaps")
            {
                descriptor.Kind = TriggerKind.Overlaps;
                int overlapsAndIndex = IndexOf(words, "and");
                if (overlapsAndIndex > 2)
                {
                    descriptor.RequireName = Join(trigger, 2, overlapsAndIndex);
                    var extraTokens = new List<Token>();
                    for (int i = overlapsAndIndex + 1; i < trigger.Count; i++)
                        extraTokens.Add(trigger[i]);
                    var cond = Conditions.Parse(extraTokens);
                    if (cond.Kind != ConditionKind.Unknown)
                        descriptor.ExtraConditions.Add(cond);
                }
                else
                {
                    descriptor.RequireName = Join(trigger, 2, words.Length);
                }
                return descriptor;
            }
            if (words.Length >= 4 && words[1] == "arrives" && words[2] == "at")
            {
                descriptor.Kind = TriggerKind.Arrives;
                descriptor.RequireName = Join(trigger, 3, words.Length);
                return descriptor;
            }
            // `when me hears "gunshot" by 12` — a sound on the bus (Task 16 Part B). The name is
            // a quoted sound and `by` is optional: without it the sound's own radius decides.
            if (words.Length >= 3 && words[1] == "hears")
            {
                descriptor.Kind = TriggerKind.Hears;
                descriptor.RequireName = trigger[2].Text;
                descriptor.Distance = DistanceBy(trigger);
                descriptor.DistanceVariable = DistanceNameBy(trigger);
                return descriptor;
            }
            if (words.Length >= 3 && words[1] == "sees")
            {
                descriptor.Kind = TriggerKind.Sees;
                descriptor.RequireName = words[2];
                descriptor.Distance = DistanceBy(trigger);
                descriptor.DistanceVariable = DistanceNameBy(trigger);
                return descriptor;
            }
            if (words.Length >= 4 && words[1] == "stops" && words[2] == "seeing")
            {
                descriptor.Kind = TriggerKind.StopsSeeing;
                descriptor.RequireName = words[3];
                descriptor.Distance = DistanceBy(trigger);
                descriptor.DistanceVariable = DistanceNameBy(trigger);
                return descriptor;
            }
            if (words.Length >= 3 && words[1] == "gets")
            {
                descriptor.Kind = words[2] switch
                {
                    "equipped" => TriggerKind.Equipped,
                    "unequipped" => TriggerKind.Unequipped,
                    _ => TriggerKind.Dropped,
                };
                return descriptor;
            }
            if (words.Length >= 3 && words[1] == "is" && words[2] == "hovered")
            {
                descriptor.Kind = TriggerKind.Hovered;
                return descriptor;
            }
            if (words.Length >= 4 && words[1] == "is" && words[2] == "near")
            {
                descriptor.Kind = TriggerKind.Near;
                descriptor.RequireName = trigger[3].Text;
                descriptor.Distance = DistanceBy(trigger);
                return descriptor;
            }
            if (words.Length >= 2 && words[1] == "appears")
            {
                descriptor.Kind = TriggerKind.Appears;
                return descriptor;
            }
            return new TriggerDescriptor { Kind = TriggerKind.Unknown };
        }

        /// <summary>
        /// `by my SightDistance` — a range held in the sheet actor's own variable. A plain
        /// `by 12` stays a fixed distance (<see cref="DistanceBy"/> reads it); this only
        /// answers the variable form, so typed enemies sight as far as their type says.
        /// </summary>
        private static string DistanceNameBy(List<Token> trigger)
        {
            for (int i = trigger.Count - 2; i >= 0; i--)
            {
                if (trigger[i].Type != TokenType.Word || trigger[i].Text != "by")
                    continue;
                if (trigger[i + 1].Type == TokenType.Word && trigger[i + 1].Text == "my" && i + 2 < trigger.Count)
                    return trigger[i + 2].Text;
                return "";
            }
            return "";
        }

        private static double DistanceBy(List<Token> trigger)
        {
            for (int i = 0; i < trigger.Count - 1; i++)
                if (trigger[i].Text == "by" && trigger[i + 1].Type == TokenType.Number)
                    return trigger[i + 1].Number;
            return -1;
        }

        private static TriggerDescriptor WatchDescriptor(List<Token> trigger, EventNode ev, int becomesIndex, bool becomesNot)
        {
            var descriptor = new TriggerDescriptor();
            if (trigger.Count > becomesIndex && trigger[becomesIndex + 1].Text == "not")
                descriptor.Kind = TriggerKind.WatchBecomesNot;
            else if (becomesNot)
                descriptor.Kind = TriggerKind.WatchBecomesNot;
            else
                descriptor.Kind = TriggerKind.WatchBecomes;
            int nameIndex = becomesIndex - 1;
            if (nameIndex - 1 >= 0 && trigger[nameIndex - 1].Text == "my")
            {
                descriptor.WatchOwner = "my";
                descriptor.WatchName = trigger[nameIndex].Text;
            }
            else
            {
                // Resource names and variables can be several words (`Supply Used`,
                // `Supply Max`); join everything before `becomes`.
                SetWatchName(descriptor, trigger, becomesIndex);
            }
            descriptor.WatchTarget = ValueFromToken(trigger[trigger.Count - 1]);
            return descriptor;
        }

        /// <summary>Reads the watched variable name ending just before <paramref name="until"/>,
        /// canonicalising RTS resources so `Supply`, `Supply Used` and `SupplyUsed` share one slot.</summary>
        private static void SetWatchName(TriggerDescriptor descriptor, List<Token> trigger, int until)
        {
            int from = 0;
            descriptor.WatchOwner = "";
            if (until > 0 && trigger[0].Text == "my")
            {
                descriptor.WatchOwner = "my";
                from = 1;
            }
            string name = Join(trigger, from, until);
            descriptor.WatchName = descriptor.WatchOwner.Length == 0
                ? ResourceManager.CanonicalOrName(name)
                : name;
        }

        private static ValueNode ValueFromToken(Token token)
        {
            switch (token.Type)
            {
                case TokenType.Number:
                    return new NumberValue { Line = token.Line, Value = token.Number };
                case TokenType.Str:
                    return new TextValue { Line = token.Line, Text = token.Text };
                case TokenType.Word:
                    if (token.Text == "true" || token.Text == "false")
                        return new BooleanValue { Line = token.Line, Value = token.Text == "true" };
                    return new VariableValue { Line = token.Line, Name = token.Text };
                default:
                    return new NumberValue { Line = token.Line };
            }
        }

        private static int IndexOf(string[] words, string word)
        {
            for (int i = 0; i < words.Length; i++)
                if (words[i] == word)
                    return i;
            return -1;
        }

        /// <summary>True when <paramref name="name"/> is a scanned input key (W, Space, Left Mouse, ...),
        /// so `&lt;name&gt; is pressed` is treated as a key press rather than a button press.</summary>
        private static bool IsKeyName(string name)
        {
            // A single digit is a top-row number key, even though the scan list spells it `D1`.
            if (ActionWords.NormalizeKeyName(name) != name)
                return true;
            foreach (string key in ActionWords.KeyNamesForScanning)
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static string Join(List<Token> trigger, int from, int until)
        {
            var parts = new System.Text.StringBuilder();
            for (int i = from; i < until; i++)
            {
                if (parts.Length > 0)
                    parts.Append(' ');
                parts.Append(trigger[i].Text);
            }
            return parts.ToString();
        }

    }

    public class PushSignal
    {
        public TriggerKind Kind;
        public object Other;
        /// <summary>The player a join/die/score signal is about, and the killer of a kill signal.</summary>
        public int PlayerA;
        /// <summary>The victim of a <c>when player &lt;n&gt; kills player &lt;m&gt;</c> signal.</summary>
        public int PlayerB;
        /// <summary>If set, the pushed event runs with this actor as `me` (used by `equip`:
        /// an item's `gets equipped`/`gets unequipped` event runs with the wearer as `me`).</summary>
        public object MeOverride;
        public string Name = "";
        /// <summary>Physics-material name of the touched/colliding actor (contact triggers only).</summary>
        public string Material = "";
    }
}
