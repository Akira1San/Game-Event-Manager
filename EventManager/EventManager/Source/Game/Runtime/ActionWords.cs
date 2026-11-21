using System;
using System.Collections.Generic;

namespace Game
{
    public enum ActionVerb
    {
        Unknown,
        Move,
        StopMoving,
        /// <summary><c>jump</c> / <c>jump strength &lt;n&gt;</c> (Task 7 G4 phase 3).</summary>
        Jump,
        /// <summary><c>climb &lt;thing&gt;</c> / <c>climb down &lt;thing&gt;</c> (Task 7 G4 phase 4).</summary>
        Climb,
        /// <summary><c>stop climbing</c> — let go of the ladder.</summary>
        StopClimbing,
        Turn,
        LookAt,
        Face,
        Teleport,
        /// <summary><c>save checkpoint</c> — remembers the acting actor's own transform in four
        /// per-actor variables (Task 36 Part F).</summary>
        SaveCheckpoint,
        /// <summary><c>respawn at &lt;thing&gt;</c> — warps to a checkpoint actor through the same
        /// reset <c>teleport</c> uses (Task 36 Part F).</summary>
        Respawn,
        Push,
        Goto,
        Parent,
        Unparent,
        Scale,
        Destroy,
        Spawn,
        /// <summary><c>cast "&lt;spell&gt;"</c> / <c>cast "&lt;spell&gt;" at &lt;thing&gt;</c> (Task 9 E3).</summary>
        Cast,
        PlaySound,
        StopSound,
        /// <summary><c>play music "&lt;name&gt;"</c> (Task 31).</summary>
        PlayMusic,
        /// <summary><c>loop music "&lt;name&gt;"</c> — the same player, repeating (Task 31).</summary>
        LoopMusic,
        /// <summary><c>stop music</c>.</summary>
        StopMusic,
        /// <summary><c>fade music to &lt;n&gt; in &lt;s&gt;</c>.</summary>
        FadeMusic,
        /// <summary><c>duck music by &lt;n&gt; for &lt;s&gt;</c> — lower the music, then restore it.</summary>
        DuckMusic,
        SetMusicVolume,
        SetSoundVolume,
        SetMasterVolume,
        PlayAnim,
        LoopAnim,
        StopAnim,
        /// <summary><c>set anim speed to &lt;n&gt;</c> — per-sheet playback speed (Task 33 Part A).</summary>
        SetAnimSpeed,
        PlayParticle,
        StopParticle,
        SetLightIntensity,
        SetLightColor,
        SetLightRange,
        EnableLight,
        DisableLight,
        ToggleLight,
        CameraFollow,
        CameraLooksAt,
        CameraAt,
        CameraSwitch,
        CameraFree,
        // RTS camera controls (Task 26)
        ZoomCamera,
        PanCamera,
        Hide,
        Show,
        ShowText,
        HideText,
        RestartScene,
        LoadScene,
        QuitGame,
        Print,
        // RTS selection (Task 18 Part A)
        Select,
        Deselect,
        // UI text input focus (Task 15 Part C)
        Focus,
        Unfocus,
        // Keyboard/gamepad focus navigation (Task 15 Part E)
        Navigate,
        // Tween helpers (Task 15 Part D)
        Tween,
        // Moving platforms (Task 15 Part H)
        MovePlatform,
        // Global time scale (Task 17 Part C pause/resume)
        SetTimeScale,
        // Prefab / scene spawning (Task 17 Part D)
        SpawnPrefab,
        SpawnScene,
        // Scene lifecycle (TASK_FIX_SCENES Parts B/C)
        AddScene,
        UnloadScene,
        // RTS construction (Task 18 Part C)
        PlaceBuilding,
        CancelBuild,
        // RTS production queues & unit training (Task 18 Part D)
        Train,
        CancelTraining,
        // RTS orders & attack-move (Task 18 Part E)
        AttackMove,
        AttackOrder,
        /// <summary><c>gather &lt;node&gt;</c> — walk a worker to a resource node (Task 29 Part C).</summary>
        Gather,
        /// <summary><c>repair &lt;target&gt;</c> — walk to a damaged friendly (Task 29 Part C).</summary>
        Repair,
        StopOrder,
        HoldPosition,
        AddOrderToQueue,
        ClearOrders,
        SetRallyPoint,
        // RTS fog of war & visibility (Task 18 Part F)
        RevealArea,
        HideArea,
        SetVisibility,
        // RTS terrain passability (Task 22 Part B)
        MarkTerrain,
        /// <summary><c>fly to &lt;spot&gt; at &lt;height&gt;</c> (Task 28 Part B).</summary>
        Fly,
        /// <summary><c>lift off</c> — a building rises to its <c>my Altitude</c> (Task 28 Part C).</summary>
        LiftOff,
        /// <summary><c>land</c> — the building comes back down (Task 28 Part C).</summary>
        Land,
        // Random-angle wander and still mode (Task 16 Part A)
        Wander,
        Still,
        // Projectile behaviour (Task 16 Part C.5)
        Bounce,
        Explode,
        // RTS target-pick mode & area damage (Task 27 Part A)
        Aim,
        CancelAim,
        DamageArea,
        // Buffs and debuffs (Task 27 Part C)
        AddBuff,
        RemoveBuff,
        // RTS research queues & upgrades (Task 27 Part D)
        Research,
        CancelResearch,
        // Shared cooldowns (Task 27 Part E)
        PutOnCooldown,
    }

    public class ActionSpec
    {
        public ActionVerb Verb;
        public string Thing = "";
        public string To = "";
        public string Dir = "";
        public double Number;
        public bool HasNumber;
        /// <summary><c>facing forward</c>: turn the body onto the heading it walks. A <c>goto</c>
        /// qualifier, and since the G2 follow-up also a <c>move toward</c> one.</summary>
        public bool FaceForward;
        public ValueNode AmountNode;
        public List<Token> Leftover = new List<Token>();
        /// <summary><c>select all &lt;unit type&gt;</c> / <c>deselect all</c>.</summary>
        public bool All;
        /// <summary><c>play anim "&lt;name&gt;" upper body</c> (and the <c>loop</c> / <c>stop</c> forms):
        /// the clip goes into the base Anim Graph's <c>UpperBody</c> Animation Slot, behind a Blend With
        /// Mask, instead of replacing the whole pose (Task 33 Part C).</summary>
        public bool UpperBody;
        /// <summary><c>select units near me by &lt;dist&gt;</c>.</summary>
        public bool Near;
        /// <summary>Radius for the <see cref="Near"/> form of <c>select</c>.</summary>
        public double Distance;
        /// <summary><c>tween</c>: which property animates, its target, and the duration/easing.</summary>
        public bool HasTween;
        public TweenKind TweenProperty;
        public string TweenTarget = "";
        public string TweenColor = "";
        public ValueNode TweenValue;
        /// <summary>Tween length, or how long a <c>push ... for &lt;n&gt; seconds</c> keeps shoving.
        /// Zero on a push means the historical single-tick nudge.</summary>
        public double Duration;
        public TweenEasing Easing = TweenEasing.Linear;
        /// <summary><c>play music random from "a, b, c"</c>: the clip names to pick between
        /// (Task 31 follow-up). Empty for the plain named forms.</summary>
        public List<string> Choices = new List<string>();
        /// <summary><c>place &lt;building&gt; at &lt;target&gt; on grid</c>: snap to a cell
        /// and reserve its footprint. Free-form placement leaves this false.</summary>
        public bool OnGrid;
        /// <summary>Explicit grid cell size from a trailing <c>by &lt;n&gt;</c>.</summary>
        public double GridSize;
        public bool HasGridSize;
        /// <summary><c>spawn &lt;thing&gt; at &lt;spot&gt; offset &lt;n&gt;</c>: distance along the
        /// spot's forward axis (flattened to XZ) where the clone appears.</summary>
        public double SpawnOffset;
        /// <summary>True when the spawn wrote an <c>offset</c> at all.</summary>
        public bool HasSpawnOffset;
        /// <summary><c>move toward &lt;thing&gt; ... around walls</c>: corner-sample and steer
        /// around obstacles instead of walking into them (Task 7 Part G2). Opt-in, so every
        /// existing sheet keeps the plain straight line.</summary>
        public bool AroundWalls;
        /// <summary><c>cast "&lt;spell&gt;"</c>: the spell's name (also kept in
        /// <see cref="Thing"/> so warnings read the same as other actions).</summary>
        public string Spell = "";
        /// <summary>
        /// <c>selected units ...</c> (Task 29 Part D): the action runs once for every selected
        /// actor in this scope instead of only for <c>me</c>. A bare <c>selected ...</c> is
        /// <see cref="SelectionOrderScope.All"/>, so one card button can order a mixed selection.
        /// </summary>
        public bool HasSelectionScope;
        public SelectionOrderScope SelectionScope = SelectionOrderScope.All;
        /// <summary><c>spawn Ring at me rarity "Rare"</c>: roll affixes on the clone (Task 9 E7).</summary>
        public string Rarity = "";
        public bool HasRarity;
        /// <summary><c>... level 12</c>: the item level the affixes roll for.</summary>
        public double ItemLevel = 1;
        public bool HasItemLevel;
        /// <summary><c>aim "&lt;ability&gt;"</c>: the ability name (also kept in
        /// <see cref="Thing"/> so warnings read the same as other actions).</summary>
        public string Ability = "";
        /// <summary>What that aim accepts — a point, a unit, the caster, or any of them.</summary>
        public TargetAccepts Accepts = TargetAccepts.Point | TargetAccepts.Unit;
        /// <summary><c>damage area around &lt;spot&gt; by &lt;n&gt; for radius &lt;r&gt;</c>:
        /// the blast radius, as a literal (<see cref="Radius"/>) or a value.</summary>
        public double Radius;
        public ValueNode RadiusNode;
        /// <summary><c>add buff "&lt;name&gt;"</c> / <c>remove buff "&lt;name&gt;"</c> (Task 27 Part C).</summary>
        public string BuffName = "";
        /// <summary>What a second copy of that buff does; refresh unless the line says otherwise.</summary>
        public BuffRule BuffRule = BuffRule.Refresh;
        /// <summary>The stat changes the buff makes, in the order they were written.</summary>
        public List<BuffModifierWord> BuffModifiers = new List<BuffModifierWord>();
    }

    public static class ActionWords
    {
        public static string[] DirectionWords = { "forward", "back", "backward", "left", "right", "up", "down" };

        public static string[] KeyNamesForScanning = BuildKeyNames();

        /// <summary>
        /// Maps the way people write number keys (<c>1</c>, <c>0</c>) to Flax's key names
        /// (<c>D1</c>, <c>D0</c>). Every other name is returned unchanged.
        /// </summary>
        public static string NormalizeKeyName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length != 1)
                return name;
            char c = name[0];
            return c >= '0' && c <= '9' ? "D" + c : name;
        }

        /// <summary>
        /// Flax's own <c>KeyboardKeys</c> names, which differ from the words people write:
        /// <c>Space</c> is <c>Spacebar</c>, <c>1</c> is <c>Alpha1</c>, <c>Up</c> is
        /// <c>ArrowUp</c>, and there is one <c>Shift</c>/<c>Control</c>/<c>Alt</c> per side.
        /// Each alias names exactly one key: a name we don't know must fail, never fall back
        /// to a different key (that used to make every arrow/digit read as a held Space).
        /// </summary>
        private static readonly Dictionary<string, string> KeyEngineNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Space", "Spacebar" },
            { "Enter", "Return" },
            { "Return", "Return" },
            { "Up", "ArrowUp" },
            { "Down", "ArrowDown" },
            { "Left", "ArrowLeft" },
            { "Right", "ArrowRight" },
            { "Shift", "Shift" },
            { "Left Shift", "Shift" },
            { "Right Shift", "Shift" },
            { "LeftShift", "Shift" },
            { "RightShift", "Shift" },
            { "Ctrl", "Control" },
            { "Control", "Control" },
            { "Left Ctrl", "Control" },
            { "Right Ctrl", "Control" },
            { "LeftCtrl", "Control" },
            { "RightCtrl", "Control" },
            { "Alt", "Alt" },
            { "Left Alt", "Alt" },
            { "Right Alt", "Alt" },
            { "LeftAlt", "Alt" },
            { "RightAlt", "Alt" },
            // TK4 drives the player with the arrow keys, and people write them as
            // "Left Arrow" far more often than bare "Left". Every spelling of one arrow
            // resolves to that arrow; nothing falls back to a different key.
            { "Left Arrow", "ArrowLeft" },
            { "LeftArrow", "ArrowLeft" },
            { "Arrow Left", "ArrowLeft" },
            { "ArrowLeft", "ArrowLeft" },
            { "Right Arrow", "ArrowRight" },
            { "RightArrow", "ArrowRight" },
            { "Arrow Right", "ArrowRight" },
            { "ArrowRight", "ArrowRight" },
            { "Up Arrow", "ArrowUp" },
            { "UpArrow", "ArrowUp" },
            { "Arrow Up", "ArrowUp" },
            { "ArrowUp", "ArrowUp" },
            { "Down Arrow", "ArrowDown" },
            { "DownArrow", "ArrowDown" },
            { "Arrow Down", "ArrowDown" },
            { "ArrowDown", "ArrowDown" },
        };

        /// <summary>The engine name to look a written key up with, or the trimmed name when it
        /// already is an engine name (A–Z, Tab, Escape, Backspace).</summary>
        public static string EngineKeyName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            string trimmed = name.Trim();
            if (KeyEngineNames.TryGetValue(trimmed, out string alias))
                return alias;
            string compact = trimmed.Replace(" ", "");
            if (KeyEngineNames.TryGetValue(compact, out alias))
                return alias;
            // 0–9, and the `D1` form NormalizeKeyName produces, are Flax's `Alpha1`.
            if (compact.Length == 1 && compact[0] >= '0' && compact[0] <= '9')
                return "Alpha" + compact;
            if (compact.Length == 2 && (compact[0] == 'D' || compact[0] == 'd') && compact[1] >= '0' && compact[1] <= '9')
                return "Alpha" + compact[1];
            return trimmed;
        }

        private static string[] BuildKeyNames()
        {
            var names = new List<string>();
            names.AddRange(new[] { "Space", "Enter", "Escape", "Tab", "Backspace", "Up", "Down", "Left", "Right" });
            // Both ways of naming an arrow key are scanned (and are valid trigger names), so
            // `when Left Arrow is held` and `when Arrow Left is held` both reach the same key.
            names.AddRange(new[]
            {
                "Left Arrow", "Right Arrow", "Up Arrow", "Down Arrow",
                "Arrow Left", "Arrow Right", "Arrow Up", "Arrow Down",
            });
            names.AddRange(new[] { "Left Shift", "Right Shift", "Left Ctrl", "Right Ctrl", "Left Alt", "Right Alt" });
            names.AddRange(new[] { "Left Mouse", "Right Mouse", "Middle Mouse" });
            for (char letter = 'A'; letter <= 'Z'; letter++)
                names.Add(letter.ToString());
            for (int number = 0; number <= 9; number++)
                names.Add("D" + number);
            return names.ToArray();
        }

        public static ActionSpec Parse(List<Token> words, Action<string> warn)
        {
            // `selected units move there` (Task 29 Part D): the order applies to the whole
            // selection. Checked first so every existing shape below is untouched — the prefix
            // is stripped and the rest of the line is parsed exactly as it always was.
            if (words.Count > 0 && words[0].Type == TokenType.Word && words[0].Text == "selected")
                return ParseSelected(words, warn);
            var spec = new ActionSpec();
            if (words.Count == 0)
                return Unknown(spec, words, "", warn);

            string verb = words[0].Text;
            // `add order to queue` is an RTS order action (Task 18 Part E). Checked before
            // the switch so `add scene "HUD"` still reaches its own generic handler below.
            if (verb == "add" && words.Count >= 4 && words[1].Text == "order" &&
                words[2].Text == "to" && words[3].Text == "queue")
                return Finish(spec, ActionVerb.AddOrderToQueue, words, 4);
            // `add buff "stim" to me for 8 seconds` (Task 27 Part C). Checked before the switch
            // so the generic `add <value> to <var>` shape never sees it.
            if (verb == "add" && words.Count >= 2 && words[1].Text == "buff")
                return AddBuff(spec, words, warn);
            if (verb == "remove" && words.Count >= 2 && words[1].Text == "buff")
                return RemoveBuff(spec, words, warn);
            switch (verb)
            {
                case "stop":
                    if (words.Count == 1)
                        return Finish(spec, ActionVerb.StopOrder, words, 1);
                    if (words.Count >= 2 && words[1].Text == "moving")
                        return Finish(spec, ActionVerb.StopMoving, words, 2);
                    if (words.Count >= 2 && words[1].Text == "climbing")
                        return Finish(spec, ActionVerb.StopClimbing, words, 2);
                    if (words.Count >= 2 && words[1].Text == "sound")
                        return Finish(spec, ActionVerb.StopSound, words, 2);
                    if (words.Count >= 2 && words[1].Text == "music")
                        return Finish(spec, ActionVerb.StopMusic, words, 2);
                    // `stop anim upper body` (Task 33 Part C): only the layer, so the legs keep
                    // walking. Checked before the whole-body `stop anim`.
                    if (words.Count >= 4 && words[1].Text == "anim" &&
                        words[2].Text == "upper" && words[3].Text == "body")
                    {
                        spec.UpperBody = true;
                        return Finish(spec, ActionVerb.StopAnim, words, 4);
                    }
                    if (words.Count >= 2 && words[1].Text == "anim")
                        return Finish(spec, ActionVerb.StopAnim, words, 2);
                    if (words.Count >= 3 && words[1].Text == "particle" && words[2].Type == TokenType.Str)
                    {
                        spec.Thing = words[2].Text;
                        return Finish(spec, ActionVerb.StopParticle, words, 3);
                    }
                    if (words.Count >= 2 && words[1].Text == "particle")
                        return Unknown(spec, words, "\u201Cstop particle\u201D needs a name in quotes, like \u201Cstop particle \"smoke\"\u201D", warn);
                    return Unknown(spec, words, "yes, a \u201Cstop\u201D line — I only know \u201Cstop sound\u201D, \u201Cstop music\u201D, \u201Cstop anim\u201D, \u201Cstop moving\u201D, \u201Cstop climbing\u201D, \u201Cstop particle\u201D, and \u201Cstop this event\u201D", warn);

                case "wander":
                {
                    // `wander` / `wander speed 5` / `wander speed my Speed` / `wander speed 5 duration 2`
                    int wanderIndex = 1;
                    if (words.Count >= wanderIndex + 2 && words[wanderIndex].Text == "speed")
                    {
                        // `my <name>` is two tokens, so the amount reports how many it used.
                        spec.AmountNode = ValueNodeAt(words, wanderIndex + 1, out int wanderUsed);
                        spec.HasNumber = words[wanderIndex + 1].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[wanderIndex + 1].Number;
                        wanderIndex += 1 + wanderUsed;
                    }
                    else
                    {
                        spec.Number = 5;
                        spec.HasNumber = true;
                    }
                    if (words.Count >= wanderIndex + 2 && words[wanderIndex].Text == "duration")
                    {
                        if (words[wanderIndex + 1].Type != TokenType.Number)
                            return Unknown(spec, words, "\u201Cwander duration\u201D needs seconds, like \u201Cwander speed 5 duration 2\u201D", warn);
                        spec.Duration = words[wanderIndex + 1].Number;
                        wanderIndex += 2;
                    }
                    return Finish(spec, ActionVerb.Wander, words, wanderIndex);
                }

                case "still":
                    return Finish(spec, ActionVerb.Still, words, 1);

                case "fade":
                {
                    // `fade music to 0.4 in 2` — a volume ramp the bridge advances each tick
                    // (Task 31). The target is the music level, 0-1.
                    if (words.Count >= 6 && words[1].Text == "music" && words[2].Text == "to" &&
                        words[3].Type == TokenType.Number && words[4].Text == "in" &&
                        words[5].Type == TokenType.Number)
                    {
                        spec.Number = words[3].Number;
                        spec.HasNumber = true;
                        spec.Duration = words[5].Number;
                        return Finish(spec, ActionVerb.FadeMusic, words, 6);
                    }
                    return Unknown(spec, words, "\u201Cfade\u201D looks like this: \u201Cfade music to 0 in 2\u201D", warn);
                }

                case "duck":
                {
                    // `duck music by 0.6 for 4` — lower the music under dialogue, then restore
                    // it (Task 31). The amount is the share to remove: 0.6 = 60% quieter.
                    if (words.Count >= 6 && words[1].Text == "music" && words[2].Text == "by" &&
                        words[3].Type == TokenType.Number && words[4].Text == "for" &&
                        words[5].Type == TokenType.Number)
                    {
                        spec.Number = words[3].Number;
                        spec.HasNumber = true;
                        spec.Duration = words[5].Number;
                        return Finish(spec, ActionVerb.DuckMusic, words, 6);
                    }
                    return Unknown(spec, words, "\u201Cduck\u201D looks like this: \u201Cduck music by 0.6 for 4\u201D", warn);
                }

                case "bounce":
                    // `bounce` — a projectile reflects off the wall it just hit (Task 16 Part C.5).
                    return Finish(spec, ActionVerb.Bounce, words, 1);

                case "aim":
                {
                    // `aim "psi storm"` enters target-pick mode (Task 27 Part A). The optional
                    // qualifiers narrow what a click accepts and how long the mode stays armed:
                    // `aim "heal" at me`, `aim "psi storm" at a point for 6 seconds`.
                    if (words.Count >= 2 && words[1].Type == TokenType.Str)
                    {
                        spec.Thing = words[1].Text;
                        spec.Ability = words[1].Text;
                        int aimIndex = 2;
                        if (words.Count >= aimIndex + 3 && words[aimIndex].Text == "at" &&
                            words[aimIndex + 1].Text == "a" &&
                            (words[aimIndex + 2].Text == "point" || words[aimIndex + 2].Text == "unit"))
                        {
                            spec.Accepts = words[aimIndex + 2].Text == "point"
                                ? TargetAccepts.Point
                                : TargetAccepts.Unit;
                            aimIndex += 3;
                        }
                        else if (words.Count >= aimIndex + 2 && words[aimIndex].Text == "at" &&
                                 words[aimIndex + 1].Text == "me")
                        {
                            spec.Accepts = TargetAccepts.Me;
                            aimIndex += 2;
                        }
                        else if (words.Count >= aimIndex + 2 && words[aimIndex].Text == "at")
                        {
                            return Unknown(spec, words, "after \u201Cat\u201D an aim takes \u201Ca point\u201D, \u201Ca unit\u201D or \u201Cme\u201D", warn);
                        }
                        if (words.Count >= aimIndex + 3 && words[aimIndex].Text == "for" &&
                            words[aimIndex + 2].Text == "seconds")
                        {
                            if (words[aimIndex + 1].Type != TokenType.Number)
                                return Unknown(spec, words, "an aim timeout is seconds, like \u201Cfor 6 seconds\u201D", warn);
                            spec.Duration = words[aimIndex + 1].Number;
                            aimIndex += 3;
                        }
                        return Finish(spec, ActionVerb.Aim, words, aimIndex);
                    }
                    return Unknown(spec, words, "\u201Caim\u201D needs an ability name in quotes, like \u201Caim \"psi storm\"\u201D", warn);
                }

                case "damage":
                {
                    // `damage area around <spot> by <n> for radius <r>` (Task 27 Part A) — AoE
                    // without a projectile, so a storm never destroys its caster. The spot is
                    // an actor, `there`, or the pick an `aim` just made.
                    if (words.Count >= 4 && words[1].Text == "area" && words[2].Text == "around")
                    {
                        int damageIndex = 3;
                        string spot;
                        int spotUsed;
                        if (words.Count >= damageIndex + 3 && words[damageIndex].Text == "my" &&
                            words[damageIndex + 1].Text == "aim" && words[damageIndex + 2].Text == "target")
                        {
                            spot = "my aim target";
                            spotUsed = 3;
                        }
                        else if (words.Count >= damageIndex + 2 && words[damageIndex].Text == "my" &&
                                 words[damageIndex + 1].Text == "aim")
                        {
                            spot = "my aim";
                            spotUsed = 2;
                        }
                        else
                        {
                            spot = TargetAt(words, damageIndex, out spotUsed);
                        }
                        if (spot != null)
                        {
                            spec.Thing = spot;
                            damageIndex += spotUsed;
                            if (words.Count >= damageIndex + 2 && words[damageIndex].Text == "by")
                            {
                                spec.AmountNode = ValueNodeAt(words, damageIndex + 1, out int damageUsed);
                                spec.HasNumber = words[damageIndex + 1].Type == TokenType.Number;
                                if (spec.HasNumber)
                                    spec.Number = words[damageIndex + 1].Number;
                                damageIndex += 1 + damageUsed;
                                if (words.Count >= damageIndex + 3 && words[damageIndex].Text == "for" &&
                                    words[damageIndex + 1].Text == "radius")
                                {
                                    spec.RadiusNode = ValueNodeAt(words, damageIndex + 2, out int radiusUsed);
                                    if (words[damageIndex + 2].Type == TokenType.Number)
                                        spec.Radius = words[damageIndex + 2].Number;
                                    damageIndex += 2 + radiusUsed;
                                }
                                return Finish(spec, ActionVerb.DamageArea, words, damageIndex);
                            }
                        }
                    }
                    return Unknown(spec, words, "\u201Cdamage area\u201D looks like \u201Cdamage area around Mouse by 30 for radius 180\u201D", warn);
                }

                case "explode":
                case "explosive":
                {
                    // `explode` / `explode radius 320` — blast where the projectile stopped.
                    int blastIndex = 1;
                    if (words.Count >= blastIndex + 2 && words[blastIndex].Text == "radius")
                    {
                        spec.AmountNode = ValueNodeAt(words, blastIndex + 1, out int blastUsed);
                        spec.HasNumber = words[blastIndex + 1].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[blastIndex + 1].Number;
                        blastIndex += 1 + blastUsed;
                    }
                    return Finish(spec, ActionVerb.Explode, words, blastIndex);
                }

                case "move":
                    return Move(spec, words, warn);

                case "select":
                    return SelectAction(spec, words, warn);

                case "deselect":
                    return DeselectAction(spec, words, warn);

                case "tween":
                    return TweenAction(spec, words, warn);

                case "moveplatform":
                    return MovePlatformAction(spec, words, warn);

                case "focus":
                case "unfocus":
                {
                    string target = ThingAt(words, 1, out int used);
                    if (target != null)
                    {
                        spec.Thing = target;
                        return Finish(spec, verb == "focus" ? ActionVerb.Focus : ActionVerb.Unfocus, words, 1 + used);
                    }
                    return Unknown(spec, words, $"\u201C{verb}\u201D needs a thing, like \u201C{verb} NameBox\u201D", warn);
                }

                case "navigate":
                {
                    FocusDirection? direction = words.Count >= 2 ? FocusNavigation.ParseDirection(words[1].Text) : null;
                    if (direction.HasValue)
                    {
                        spec.Dir = FocusNavigation.DirectionName(direction.Value);
                        return Finish(spec, ActionVerb.Navigate, words, 2);
                    }
                    return Unknown(spec, words, "\u201Cnavigate\u201D needs a direction, like \u201Cnavigate down\u201D (up, down, left or right)", warn);
                }

                case "mark":
                {
                    // `mark water around Mouse by 300` / `mark land at Mouse` — paints the
                    // terrain grid (Task 22 Part B).
                    UnitTypes.Ground? markGround = words.Count >= 2 ? UnitTypes.GroundFrom(words[1].Text) : null;
                    if (markGround == null)
                        return Unknown(spec, words, "\u201Cmark\u201D marks land, water or air, like \u201Cmark water around Mouse by 300\u201D", warn);
                    spec.Thing = UnitTypes.GroundName(markGround.Value);
                    int markIndex = 2;
                    bool markAround = false;
                    if (markIndex < words.Count && words[markIndex].Text == "around")
                    {
                        markAround = true;
                        markIndex++;
                    }
                    else if (markIndex < words.Count && words[markIndex].Text == "at")
                        markIndex++;
                    else
                        return Unknown(spec, words, "\u201Cmark\u201D goes \u201Cat <spot>\u201D or \u201Caround <spot> by <size>\u201D, like \u201Cmark water around Mouse by 300\u201D", warn);

                    string markTarget = ThingAt(words, markIndex, out int markUsed);
                    if (markTarget == null)
                        return Unknown(spec, words, "a terrain mark needs a place, like \u201Cmark water around Mouse by 300\u201D", warn);
                    spec.To = markTarget;
                    markIndex += markUsed;
                    if (markAround)
                    {
                        if (markIndex + 1 >= words.Count || words[markIndex].Text != "by")
                            return Unknown(spec, words, "\u201Cmark water around Mouse\u201D needs a size, like \u201Cby 300\u201D", warn);
                        spec.AmountNode = ValueNodeAt(words, markIndex + 1, out int markRadiusUsed);
                        spec.HasNumber = words[markIndex + 1].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[markIndex + 1].Number;
                        markIndex += 1 + markRadiusUsed;
                    }
                    return Finish(spec, ActionVerb.MarkTerrain, words, markIndex);
                }

                case "attack-move":
                {
                    // `attack-move to Enemy` — walk toward the target, ready to fight.
                    int attackIndex = 1;
                    if (words.Count >= 2 && words[1].Text == "to")
                        attackIndex = 2;
                    string attackTarget = ThingAt(words, attackIndex, out int attackUsed);
                    if (attackTarget != null)
                    {
                        spec.Thing = attackTarget;
                        return Finish(spec, ActionVerb.AttackMove, words, attackIndex + attackUsed);
                    }
                    return Unknown(spec, words, "\u201Cattack-move\u201D looks like this: \u201Cattack-move to Enemy\u201D", warn);
                }

                case "attack":
                {
                    string attackTarget = ThingAt(words, 1, out int attackUsed);
                    if (attackTarget != null)
                    {
                        spec.Thing = attackTarget;
                        return Finish(spec, ActionVerb.AttackOrder, words, 1 + attackUsed);
                    }
                    return Unknown(spec, words, "\u201Cattack\u201D needs a target, like \u201Cattack Enemy\u201D", warn);
                }

                case "gather":
                {
                    // `gather MineralField` — the order is the walk; the node's sheet mines.
                    string gatherTarget = ThingAt(words, 1, out int gatherUsed);
                    if (gatherTarget != null)
                    {
                        spec.Thing = gatherTarget;
                        return Finish(spec, ActionVerb.Gather, words, 1 + gatherUsed);
                    }
                    return Unknown(spec, words, "\u201Cgather\u201D needs a node, like \u201Cgather MineralField\u201D", warn);
                }

                case "repair":
                {
                    string repairTarget = ThingAt(words, 1, out int repairUsed);
                    if (repairTarget != null)
                    {
                        spec.Thing = repairTarget;
                        return Finish(spec, ActionVerb.Repair, words, 1 + repairUsed);
                    }
                    return Unknown(spec, words, "\u201Crepair\u201D needs a target, like \u201Crepair Tank\u201D", warn);
                }


                case "hold":
                    if (words.Count >= 2 && words[1].Text == "position")
                        return Finish(spec, ActionVerb.HoldPosition, words, 2);
                    return Unknown(spec, words, "\u201Chold\u201D looks like this: \u201Chold position\u201D", warn);

                case "clear":
                    if (words.Count >= 2 && words[1].Text == "orders")
                        return Finish(spec, ActionVerb.ClearOrders, words, 2);
                    return Unknown(spec, words, "\u201Cclear\u201D looks like this: \u201Cclear orders\u201D", warn);

                case "lift":
                    if (words.Count >= 2 && words[1].Text == "off")
                        return Finish(spec, ActionVerb.LiftOff, words, 2);
                    return Unknown(spec, words, "\u201Clift\u201D looks like this: \u201Clift off\u201D", warn);

                case "land":
                    return Finish(spec, ActionVerb.Land, words, 1);

                case "fly":
                {
                    // `fly to LZ at 120` — raise to that height and fly the straight line there.
                    int flyIndex = 1;
                    if (flyIndex < words.Count && words[flyIndex].Text == "to")
                        flyIndex++;
                    string flySpot = ThingAt(words, flyIndex, out int flyUsed);
                    if (flySpot != null && flyIndex + flyUsed < words.Count && words[flyIndex + flyUsed].Text == "at")
                    {
                        int heightIndex = flyIndex + flyUsed + 1;
                        if (heightIndex < words.Count &&
                            (words[heightIndex].Type == TokenType.Number || words[heightIndex].Type == TokenType.Word))
                        {
                            spec.Thing = flySpot;
                            spec.AmountNode = ValueNodeAt(words, heightIndex, out int flyHeightUsed);
                            spec.HasNumber = words[heightIndex].Type == TokenType.Number;
                            if (spec.HasNumber)
                                spec.Number = words[heightIndex].Number;
                            return Finish(spec, ActionVerb.Fly, words, heightIndex + flyHeightUsed);
                        }
                    }
                    return Unknown(spec, words, "\u201Cfly\u201D looks like this: \u201Cfly to LZ at 120\u201D", warn);
                }

                case "goto":
                {
                    string gotoThing = ThingAt(words, 1, out int gotoUsed);
                    if (gotoThing != null)
                    {
                        spec.Thing = gotoThing;
                        int afterThing = 1 + gotoUsed;
                        if (words.Count >= afterThing + 2 && words[afterThing].Text == "speed")
                        {
                            // `speed 100` or `speed my Speed` — a `my` amount is two tokens.
                            spec.AmountNode = ValueNodeAt(words, afterThing + 1, out int gotoSpeedUsed);
                            spec.HasNumber = words[afterThing + 1].Type == TokenType.Number;
                            if (spec.HasNumber)
                                spec.Number = words[afterThing + 1].Number;
                            afterThing += 1 + gotoSpeedUsed;
                        }
                        else
                        {
                            spec.Number = 5;
                            spec.HasNumber = true;
                        }
                        if (words.Count >= afterThing + 2 && words[afterThing].Text == "facing" && words[afterThing + 1].Text == "forward")
                        {
                            spec.FaceForward = true;
                            afterThing += 2;
                        }
                        return Finish(spec, ActionVerb.Goto, words, afterThing);
                    }
                    return Unknown(spec, words, "\u201Cgoto\u201D needs a thing, like \u201Cgoto SpawnPoint\u201D", warn);
                }

                case "turn":
                    if (words.Count >= 2 && IsDirection(words[1].Text) && words.Count >= 4 && words[2].Text == "degrees")
                    {
                        spec.Dir = words[1].Text;
                        spec.AmountNode = ValueNodeAt(words, words.Count - 1);
                        spec.HasNumber = words[words.Count - 1].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[words.Count - 1].Number;
                        return Finish(spec, ActionVerb.Turn, words, 4);
                    }
                    return Unknown(spec, words, "\u201Cturn\u201D looks like this: \u201Cturn left degrees 90\u201D", warn);

                case "look":
                {
                    if (words.Count >= 3 && words[1].Text == "at")
                    {
                        string lookThing = ThingAt(words, 2, out int lookUsed);
                        if (lookThing != null)
                        {
                            spec.Thing = lookThing;
                            return Finish(spec, ActionVerb.LookAt, words, 2 + lookUsed);
                        }
                    }
                    return Unknown(spec, words, "\u201Clook\u201D looks like this: \u201Clook at Player\u201D (or \u201Clook at nearest Enemy\u201D)", warn);
                }

                case "zoom":
                {
                    // `zoom camera by 0.2` / `zoom in` / `zoom out` (Task 26 Part A).
                    int zoomIndex = 1;
                    if (zoomIndex < words.Count && words[zoomIndex].Text == "camera")
                        zoomIndex++;
                    if (zoomIndex < words.Count &&
                        (words[zoomIndex].Text == "in" || words[zoomIndex].Text == "out"))
                    {
                        spec.Dir = words[zoomIndex].Text;
                        return Finish(spec, ActionVerb.ZoomCamera, words, zoomIndex + 1);
                    }
                    if (zoomIndex + 1 < words.Count && words[zoomIndex].Text == "by")
                    {
                        spec.AmountNode = ValueNodeAt(words, zoomIndex + 1, out int zoomUsed);
                        spec.HasNumber = words[zoomIndex + 1].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[zoomIndex + 1].Number;
                        return Finish(spec, ActionVerb.ZoomCamera, words, zoomIndex + 1 + zoomUsed);
                    }
                    return Unknown(spec, words, "\u201Czoom\u201D looks like \u201Czoom camera by 0.2\u201D, \u201Czoom in\u201D or \u201Czoom out\u201D", warn);
                }

                case "pan":
                {
                    // `pan camera left by 100` — a free-pan nudge (Task 26 Part B/C).
                    int panIndex = 1;
                    if (panIndex < words.Count && words[panIndex].Text == "camera")
                        panIndex++;
                    if (panIndex < words.Count &&
                        CameraRig.TryDirection(words[panIndex].Text, out _, out _))
                    {
                        spec.Dir = words[panIndex].Text;
                        panIndex++;
                        if (panIndex + 1 < words.Count && words[panIndex].Text == "by")
                        {
                            spec.AmountNode = ValueNodeAt(words, panIndex + 1, out int panUsed);
                            spec.HasNumber = words[panIndex + 1].Type == TokenType.Number;
                            if (spec.HasNumber)
                                spec.Number = words[panIndex + 1].Number;
                            panIndex += 1 + panUsed;
                        }
                        return Finish(spec, ActionVerb.PanCamera, words, panIndex);
                    }
                    return Unknown(spec, words, "\u201Cpan\u201D goes left, right, forward or backward, like \u201Cpan camera left by 100\u201D", warn);
                }

                case "camera":
                {
                    if (words.Count >= 3 && words[1].Text == "follows")
                    {
                        string followThing = ThingAt(words, 2, out int followUsed);
                        if (followThing != null)
                        {
                            spec.Thing = followThing;
                            return Finish(spec, ActionVerb.CameraFollow, words, 2 + followUsed);
                        }
                    }
                    if (words.Count >= 3 && words[1].Text == "looks" && words[2].Text == "at")
                    {
                        string lookThing = ThingAt(words, 3, out int lookUsed);
                        if (lookThing != null)
                        {
                            spec.Thing = lookThing;
                            return Finish(spec, ActionVerb.CameraLooksAt, words, 3 + lookUsed);
                        }
                    }
                    if (words.Count >= 3 && words[1].Text == "at")
                    {
                        string atThing = ThingAt(words, 2, out int atUsed);
                        if (atThing != null)
                        {
                            spec.Thing = atThing;
                            return Finish(spec, ActionVerb.CameraAt, words, 2 + atUsed);
                        }
                    }
                    if (words.Count >= 3 && words[1].Text == "switch")
                    {
                        string switchThing = ThingAt(words, 2, out int switchUsed);
                        if (switchThing != null)
                        {
                            spec.Thing = switchThing;
                            return Finish(spec, ActionVerb.CameraSwitch, words, 2 + switchUsed);
                        }
                    }
                    if (words.Count >= 2 && words[1].Text == "free")
                    {
                        return Finish(spec, ActionVerb.CameraFree, words, 2);
                    }
                    // `camera zoom in` / `camera zoom out` / `camera zoom by 0.2`.
                    if (words.Count >= 3 && words[1].Text == "zoom")
                    {
                        if (words[2].Text == "in" || words[2].Text == "out")
                        {
                            spec.Dir = words[2].Text;
                            return Finish(spec, ActionVerb.ZoomCamera, words, 3);
                        }
                        if (words.Count >= 5 && words[2].Text == "by")
                        {
                            spec.AmountNode = ValueNodeAt(words, 3, out int zoomUsed);
                            spec.HasNumber = words[3].Type == TokenType.Number;
                            if (spec.HasNumber)
                                spec.Number = words[3].Number;
                            return Finish(spec, ActionVerb.ZoomCamera, words, 3 + zoomUsed);
                        }
                    }
                    return Unknown(spec, words, "\u201Ccamera\u201D looks like this: \u201Ccamera follows Player\u201D, \u201Ccamera looks at Chest\u201D, \u201Ccamera at CameraDoor\u201D, \u201Ccamera switch CameraDoor\u201D, or \u201Ccamera free\u201D", warn);
                }

                case "face":
                {
                    string faceThing = ThingAt(words, 1, out int faceUsed);
                    if (faceThing != null)
                    {
                        spec.Thing = faceThing;
                        return Finish(spec, ActionVerb.Face, words, 1 + faceUsed);
                    }
                    return Unknown(spec, words, "\u201Cface\u201D needs a thing, like \u201Cface Player\u201D", warn);
                }

                case "save":
                    // `save checkpoint` remembers this actor's own transform (Task 36 Part F).
                    // `save stats` / `save bindings` never reach here: the parser makes them
                    // statement lines and the runtime consumes them first.
                    if (words.Count >= 2 && words[1].Text == "checkpoint")
                        return Finish(spec, ActionVerb.SaveCheckpoint, words, 2);
                    return Unknown(spec, words, "\u201Csave\u201D on its own is either \u201Csave checkpoint\u201D, \u201Csave stats\u201D or \u201Csave bindings\u201D", warn);

                case "teleport":
                    if (words.Count >= 4 && IsThing(words[1]) && words[2].Text == "to" && IsThing(words[3]))
                    {
                        spec.Thing = words[1].Text;
                        spec.To = words[3].Text;
                        return Finish(spec, ActionVerb.Teleport, words, 4);
                    }
                    return Unknown(spec, words, "\u201Cteleport\u201D looks like this: \u201Cteleport me to SpawnPoint\u201D", warn);

                case "respawn":
                {
                    // `respawn at Checkpoint2`: come back at a checkpoint actor, through the
                    // same reset `teleport` uses (Task 36 Part F). The target may carry a
                    // qualifier or Flax's duplicate number, so it reads like every other target.
                    if (words.Count >= 3 && words[1].Text == "at")
                    {
                        string respawnTarget = TargetAt(words, 2, out int respawnUsed);
                        if (respawnTarget != null)
                        {
                            spec.To = respawnTarget;
                            return Finish(spec, ActionVerb.Respawn, words, 2 + respawnUsed);
                        }
                    }
                    return Unknown(spec, words, "\u201Crespawn\u201D looks like this: \u201Crespawn at Checkpoint2\u201D", warn);
                }

                case "push":
                    if (words.Count >= 3 && IsThing(words[1]) && IsDirection(words[2].Text))
                    {
                        spec.Thing = words[1].Text;
                        spec.Dir = words[2].Text;
                        int pushIndex = 3;
                        if (words.Count >= pushIndex + 2 && words[pushIndex].Text == "strength")
                        {
                            spec.AmountNode = ValueNodeAt(words, pushIndex + 1, out int pushUsed);
                            spec.HasNumber = words[pushIndex + 1].Type == TokenType.Number;
                            if (spec.HasNumber)
                                spec.Number = words[pushIndex + 1].Number;
                            pushIndex += 1 + pushUsed;
                        }
                        else
                        {
                            spec.Number = 5;
                            spec.HasNumber = true;
                        }
                        // `push other back strength 100 for 0.2 seconds` keeps shoving for that
                        // long; without it the push is the historical single tick.
                        if (words.Count >= pushIndex + 3 && words[pushIndex].Text == "for" &&
                            words[pushIndex + 2].Text == "seconds")
                        {
                            if (words[pushIndex + 1].Type == TokenType.Number)
                                spec.Duration = words[pushIndex + 1].Number;
                            pushIndex += 3;
                        }
                        return Finish(spec, ActionVerb.Push, words, pushIndex);
                    }
                    return Unknown(spec, words, "\u201Cpush\u201D looks like this: \u201Cpush other up strength 10\u201D or \u201Cpush other back strength 100 for 0.2 seconds\u201D", warn);

                case "climb":
                {
                    // `climb Ladder` / `climb Ladder speed 120` / `climb down Ladder`. No speed
                    // means the default pace; `my ClimbSpeed` works like every other amount.
                    int climbIndex = 1;
                    if (words.Count >= 2 && words[1].Text == "down")
                    {
                        spec.Dir = "down";
                        climbIndex = 2;
                    }
                    if (climbIndex < words.Count && IsThing(words[climbIndex]))
                    {
                        spec.Thing = words[climbIndex].Text;
                        climbIndex++;
                        if (words.Count >= climbIndex + 2 && words[climbIndex].Text == "speed")
                        {
                            spec.AmountNode = ValueNodeAt(words, climbIndex + 1, out int climbUsed);
                            spec.HasNumber = words[climbIndex + 1].Type == TokenType.Number;
                            if (spec.HasNumber)
                                spec.Number = words[climbIndex + 1].Number;
                            climbIndex += 1 + climbUsed;
                        }
                        else
                        {
                            spec.Number = PhysicsMovement.DefaultClimbSpeed;
                            spec.HasNumber = true;
                        }
                        return Finish(spec, ActionVerb.Climb, words, climbIndex);
                    }
                    return Unknown(spec, words, "\u201Cclimb\u201D looks like this: \u201Cclimb Ladder\u201D or \u201Cclimb down Ladder speed 120\u201D", warn);
                }

                case "jump":
                {
                    // `jump` / `jump strength 350`: a vertical take-off, refused in mid-air by
                    // the bridge. No strength means the default arc; `my JumpPower` works too.
                    int jumpIndex = 1;
                    if (words.Count >= 3 && words[1].Text == "strength")
                    {
                        spec.AmountNode = ValueNodeAt(words, 2, out int strengthUsed);
                        spec.HasNumber = words[2].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[2].Number;
                        jumpIndex = 2 + strengthUsed;
                    }
                    if (!spec.HasNumber && spec.AmountNode == null)
                    {
                        spec.Number = PhysicsMovement.DefaultJumpStrength;
                        spec.HasNumber = true;
                    }
                    return Finish(spec, ActionVerb.Jump, words, jumpIndex);
                }

                case "parent":
                    if (words.Count >= 4 && IsThing(words[1]) && words[2].Text == "to" && IsThing(words[3]))
                    {
                        spec.Thing = words[1].Text;
                        spec.To = words[3].Text;
                        return Finish(spec, ActionVerb.Parent, words, 4);
                    }
                    return Unknown(spec, words, "\u201Cparent\u201D looks like this: \u201Cparent Box to me\u201D", warn);

                case "unparent":
                    if (words.Count >= 2 && IsThing(words[1]))
                    {
                        spec.Thing = words[1].Text;
                        return Finish(spec, ActionVerb.Unparent, words, 2);
                    }
                    return Unknown(spec, words, "\u201Cunparent\u201D needs a thing, like \u201Cunparent Box\u201D", warn);

                case "scale":
                    if (words.Count >= 3 && IsThing(words[1]))
                    {
                        spec.Thing = words[1].Text;
                        int numIndex = 2;
                        if (words.Count >= 4 && words[2].Text == "to")
                            numIndex = 3;
                        if (numIndex < words.Count)
                        {
                            spec.AmountNode = ValueNodeAt(words, numIndex);
                            spec.HasNumber = words[numIndex].Type == TokenType.Number;
                            if (spec.HasNumber)
                                spec.Number = words[numIndex].Number;
                            return Finish(spec, ActionVerb.Scale, words, numIndex + 1);
                        }
                    }
                    return Unknown(spec, words, "\u201Cscale\u201D looks like this: \u201Cscale Box 2\u201D or \u201Cscale Box to 2\u201D", warn);

                case "destroy":
                    if (words.Count >= 2 && IsThing(words[1]))
                    {
                        spec.Thing = words[1].Text;
                        return Finish(spec, ActionVerb.Destroy, words, 2);
                    }
                    return Unknown(spec, words, "\u201Cdestroy\u201D needs a thing, like \u201Cdestroy other\u201D", warn);

                case "cast":
                {
                    // `cast "Fireball"` / `cast "Heal" at player` — the spell name is quoted, so
                    // a spell called `Heal` can never be mistaken for a target.
                    if (words.Count >= 2 && words[1].Type == TokenType.Str)
                    {
                        spec.Thing = words[1].Text;
                        spec.Spell = words[1].Text;
                        int index = 2;
                        if (words.Count >= index + 2 && words[index].Text == "at")
                        {
                            string castTarget = TargetAt(words, index + 1, out int castUsed);
                            if (castTarget == null)
                                return Unknown(spec, words, "\u201Ccast\u201D looks like this: \u201Ccast \"Fireball\"\u201D or \u201Ccast \"Heal\" at player\u201D", warn);
                            spec.To = castTarget;
                            index += 1 + castUsed;
                        }
                        return Finish(spec, ActionVerb.Cast, words, index);
                    }
                    return Unknown(spec, words, "\u201Ccast\u201D needs a spell name in quotes, like \u201Ccast \"Fireball\"\u201D", warn);
                }
                case "spawn":
                    if (words.Count >= 2 && words[1].Text == "prefab")
                    {
                        // `spawn prefab "Hero" at SpawnPoint` / `... at SpawnPoint 1`
                        if (words.Count >= 5 && words[2].Type == TokenType.Str && words[3].Text == "at")
                        {
                            string prefabSpot = TargetAt(words, 4, out int prefabSpotUsed);
                            if (prefabSpot != null)
                            {
                                spec.Thing = words[2].Text;
                                spec.To = prefabSpot;
                                return Finish(spec, ActionVerb.SpawnPrefab, words, 4 + prefabSpotUsed);
                            }
                        }
                        return Unknown(spec, words, "\u201Cspawn prefab\u201D looks like this: \u201Cspawn prefab \"Hero\" at SpawnPoint\u201D", warn);
                    }
                    if (words.Count >= 2 && words[1].Text == "scene")
                    {
                        // `spawn scene "HeroScene" at SpawnPoint` / `... at SpawnPoint 1`
                        if (words.Count >= 5 && words[2].Type == TokenType.Str && words[3].Text == "at")
                        {
                            string sceneSpot = TargetAt(words, 4, out int sceneSpotUsed);
                            if (sceneSpot != null)
                            {
                                spec.Thing = words[2].Text;
                                spec.To = sceneSpot;
                                return Finish(spec, ActionVerb.SpawnScene, words, 4 + sceneSpotUsed);
                            }
                        }
                        return Unknown(spec, words, "\u201Cspawn scene\u201D looks like this: \u201Cspawn scene \"HeroScene\" at SpawnPoint\u201D", warn);
                    }
                    if (words.Count >= 4 && IsThing(words[1]) && words[2].Text == "at")
                    {
                        string cloneSpot = TargetAt(words, 3, out int cloneSpotUsed);
                        if (cloneSpot != null)
                        {
                            spec.Thing = words[1].Text;
                            spec.To = cloneSpot;
                            int consumed = 3 + cloneSpotUsed;
                            // Qualifiers, in any order after the spot:
                            //   `offset 30`  — 30 units along the spot's forward axis
                            //   `rarity "Rare"` / `level 12` — roll an affixed item (Task 9 E7)
                            while (consumed < words.Count)
                            {
                                string qualifier = words[consumed].Text;
                                if (qualifier == "offset" && !spec.HasSpawnOffset && consumed + 1 < words.Count)
                                {
                                    spec.AmountNode = ValueNodeAt(words, consumed + 1);
                                    spec.HasSpawnOffset = true;
                                    if (words[consumed + 1].Type == TokenType.Number)
                                        spec.SpawnOffset = words[consumed + 1].Number;
                                    consumed += 2;
                                    continue;
                                }
                                if (qualifier == "rarity" && !spec.HasRarity &&
                                    consumed + 1 < words.Count && words[consumed + 1].Type == TokenType.Str)
                                {
                                    spec.Rarity = words[consumed + 1].Text;
                                    spec.HasRarity = true;
                                    consumed += 2;
                                    continue;
                                }
                                if (qualifier == "level" && !spec.HasItemLevel &&
                                    consumed + 1 < words.Count && words[consumed + 1].Type == TokenType.Number)
                                {
                                    spec.ItemLevel = words[consumed + 1].Number;
                                    spec.HasItemLevel = true;
                                    consumed += 2;
                                    continue;
                                }
                                break;
                            }
                            return Finish(spec, ActionVerb.Spawn, words, consumed);
                        }
                    }
                    return Unknown(spec, words, "\u201Cspawn\u201D looks like this: \u201Cspawn Bullet at me\u201D, \u201Cspawn Bullet at me offset 30\u201D, \u201Cspawn Ring at me rarity \"Rare\" level 12\u201D, \u201Cspawn prefab \"Hero\" at SpawnPoint\u201D or \u201Cspawn scene \"HeroScene\" at SpawnPoint\u201D", warn);
                case "put":
                {
                    // `put "stim" on cooldown for 10 seconds` — start a named per-actor timer.
                    // The name is quoted like a spell, so a two-word timer stays one name.
                    if (words.Count >= 6 && words[1].Type == TokenType.Str &&
                        words[2].Text == "on" && words[3].Text == "cooldown" &&
                        words[4].Text == "for" && words[5].Type == TokenType.Number)
                    {
                        spec.Thing = words[1].Text;
                        spec.Duration = Math.Max(0, words[5].Number);
                        int index = 6;
                        if (words.Count >= index + 1 && words[index].Text == "seconds")
                            index++;
                        return Finish(spec, ActionVerb.PutOnCooldown, words, index);
                    }
                    return Unknown(spec, words, "\u201Cput\u201D looks like this: \u201Cput \"stim\" on cooldown for 10 seconds\u201D", warn);
                }
                case "research":
                {
                    // `research "Stim Pack" at Barracks` — queue an upgrade on that building.
                    // `research "Stim Pack"` queues on the acting building (its own sheet).
                    // The name is quoted like a spell, so a two-word upgrade stays one name.
                    string upgrade = null;
                    int upgradeUsed = 0;
                    if (words.Count >= 2 && words[1].Type == TokenType.Str)
                    {
                        upgrade = words[1].Text;
                        upgradeUsed = 1;
                    }
                    else if (words.Count >= 2)
                    {
                        upgrade = ThingAt(words, 1, out upgradeUsed);
                    }
                    if (!string.IsNullOrEmpty(upgrade))
                    {
                        spec.Thing = upgrade;
                        int index = 1 + upgradeUsed;
                        if (words.Count >= index + 2 && words[index].Text == "at")
                        {
                            string building = TargetAt(words, index + 1, out int buildingUsed);
                            if (building != null)
                            {
                                spec.To = building;
                                index += 1 + buildingUsed;
                            }
                        }
                        return Finish(spec, ActionVerb.Research, words, index);
                    }
                    return Unknown(spec, words, "\u201Cresearch\u201D looks like this: \u201Cresearch \"Stim Pack\" at Barracks\u201D", warn);
                }
                case "train":
                {
                    // `train Marine at Barracks` — add a unit to that building's queue.
                    // `train Marine` queues on the acting building (on its own sheet).
                    string unit = ThingAt(words, 1, out int unitUsed);
                    if (unit != null)
                    {
                        spec.Thing = unit;
                        int index = 1 + unitUsed;
                        if (words.Count >= index + 2 && words[index].Text == "at")
                        {
                            string building = TargetAt(words, index + 1, out int buildingUsed);
                            if (building != null)
                            {
                                spec.To = building;
                                index += 1 + buildingUsed;
                            }
                        }
                        return Finish(spec, ActionVerb.Train, words, index);
                    }
                    return Unknown(spec, words, "\u201Ctrain\u201D looks like this: \u201Ctrain Marine at Barracks\u201D", warn);
                }
                case "place":
                {
                    // `place Barracks at Mouse` / `place Barracks at Mouse on grid [by 32]`
                    if (words.Count >= 4 && IsThing(words[1]) && words[2].Text == "at" && IsThing(words[3]))
                    {
                        spec.Thing = words[1].Text;
                        spec.To = words[3].Text;
                        int index = 4;
                        if (words.Count >= index + 2 && words[index].Text == "on" && words[index + 1].Text == "grid")
                        {
                            spec.OnGrid = true;
                            index += 2;
                            if (words.Count >= index + 2 && words[index].Text == "by" && words[index + 1].Type == TokenType.Number)
                            {
                                spec.GridSize = words[index + 1].Number;
                                spec.HasGridSize = true;
                                index += 2;
                            }
                        }
                        return Finish(spec, ActionVerb.PlaceBuilding, words, index);
                    }
                    return Unknown(spec, words, "\u201Cplace\u201D looks like this: \u201Cplace Barracks at Mouse\u201D or \u201Cplace Barracks at Mouse on grid by 64\u201D", warn);
                }
                case "cancel":
                {
                    // `cancel aim` leaves target-pick mode (Task 27 Part A).
                    if (words.Count >= 2 && words[1].Text == "aim")
                        return Finish(spec, ActionVerb.CancelAim, words, 2);
                    if (words.Count >= 3 && words[1].Text == "research")
                    {
                        // `cancel research "Stim Pack"` refunds it; `at Barracks` names the building.
                        string upgrade = words[2].Type == TokenType.Str
                            ? words[2].Text
                            : ThingAt(words, 2, out _);
                        if (!string.IsNullOrEmpty(upgrade))
                        {
                            spec.Thing = upgrade;
                            int index = 3;
                            if (words.Count >= index + 2 && words[index].Text == "at")
                            {
                                string building = TargetAt(words, index + 1, out int buildingUsed);
                                if (building != null)
                                {
                                    spec.To = building;
                                    index += 1 + buildingUsed;
                                }
                            }
                            return Finish(spec, ActionVerb.CancelResearch, words, index);
                        }
                    }
                    if (words.Count >= 3 && words[1].Text == "training")
                    {
                        string unit = ThingAt(words, 2, out int unitUsed);
                        if (unit != null)
                        {
                            spec.Thing = unit;
                            int index = 2 + unitUsed;
                            if (words.Count >= index + 2 && words[index].Text == "at")
                            {
                                string building = TargetAt(words, index + 1, out int buildingUsed);
                                if (building != null)
                                {
                                    spec.To = building;
                                    index += 1 + buildingUsed;
                                }
                            }
                            return Finish(spec, ActionVerb.CancelTraining, words, index);
                        }
                    }
                    if (words.Count >= 2 && words[1].Text == "build")
                    {
                        int index = 2;
                        if (words.Count >= 3 && IsThing(words[2]))
                        {
                            spec.Thing = words[2].Text;
                            index = 3;
                        }
                        return Finish(spec, ActionVerb.CancelBuild, words, index);
                    }
                    return Unknown(spec, words, "\u201Ccancel\u201D looks like this: \u201Ccancel build\u201D, \u201Ccancel training Marine\u201D or \u201Ccancel research \"Stim Pack\"\u201D", warn);
                }
                case "set":
                {
                    // `set anim speed to <n>` — the acting actor's playback speed (Task 33 Part A).
                    // Its own action word, not a global variable called "anim speed".
                    if (words.Count >= 5 && words[1].Text == "anim" && words[2].Text == "speed" && words[3].Text == "to")
                    {
                        spec.AmountNode = ValueNodeAt(words, 4, out int animSpeedUsed);
                        spec.HasNumber = words[4].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[4].Number;
                        return Finish(spec, ActionVerb.SetAnimSpeed, words, 4 + animSpeedUsed);
                    }
                    if (words.Count >= 3 && words[1].Text == "anim" && words[2].Text == "speed")
                        return Unknown(spec, words, "\u201Cset anim speed\u201D looks like this: \u201Cset anim speed to 1.5\u201D", warn);
                    // Volume groups (Task 31): `set music volume to 0.4`, `set sound volume to
                    // 0.7`, `set master volume to 0.8`. They write the globals a settings UI
                    // binds, and the bridge applies them to every source it owns.
                    if (words.Count >= 5 && words[2].Text == "volume" && words[3].Text == "to" &&
                        (words[1].Text == "music" || words[1].Text == "sound" || words[1].Text == "master"))
                    {
                        ActionVerb volumeVerb = words[1].Text == "music" ? ActionVerb.SetMusicVolume
                            : words[1].Text == "sound" ? ActionVerb.SetSoundVolume
                            : ActionVerb.SetMasterVolume;
                        spec.AmountNode = ValueNodeAt(words, 4, out int volumeUsed);
                        spec.HasNumber = words[4].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[4].Number;
                        return Finish(spec, volumeVerb, words, 4 + volumeUsed);
                    }
                    if (words.Count >= 5 && words[1].Text == "light" && words[2].Text == "intensity" && words[3].Text == "to")
                    {
                        spec.AmountNode = ValueNodeAt(words, 4);
                        spec.HasNumber = words[4].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[4].Number;
                        return Finish(spec, ActionVerb.SetLightIntensity, words, 5);
                    }
                    if (words.Count >= 5 && words[1].Text == "light" && words[2].Text == "color" && words[3].Text == "to" && words[4].Type == TokenType.Str)
                    {
                        spec.Thing = words[4].Text;
                        return Finish(spec, ActionVerb.SetLightColor, words, 5);
                    }
                    if (words.Count >= 5 && words[1].Text == "light" && words[2].Text == "range" && words[3].Text == "to")
                    {
                        spec.AmountNode = ValueNodeAt(words, 4);
                        spec.HasNumber = words[4].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[4].Number;
                        return Finish(spec, ActionVerb.SetLightRange, words, 5);
                    }
                    if (words.Count >= 2 && words[1].Text == "light")
                        return Unknown(spec, words, "\u201Cset light\u201D looks like this: \u201Cset light intensity to 2\u201D, \u201Cset light color to \"1 0 0\"\u201D, or \u201Cset light range to 10\u201D", warn);
                    if (words.Count >= 5 && words[1].Text == "time" && words[2].Text == "scale" && words[3].Text == "to")
                    {
                        spec.AmountNode = ValueNodeAt(words, 4);
                        spec.HasNumber = words[4].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[4].Number;
                        return Finish(spec, ActionVerb.SetTimeScale, words, 5);
                    }
                    if (words.Count >= 5 && words[1].Text == "rally" && words[2].Text == "point" && words[3].Text == "to")
                    {
                        string rallyTarget = ThingAt(words, 4, out int rallyUsed);
                        if (rallyTarget != null)
                        {
                            spec.Thing = rallyTarget;
                            return Finish(spec, ActionVerb.SetRallyPoint, words, 4 + rallyUsed);
                        }
                        return Unknown(spec, words, "\u201Cset rally point\u201D needs a target, like \u201Cset rally point to Barracks\u201D", warn);
                    }
                    // `set visibility to <radius>` — RTS fog of war (Task 18 Part F).
                    if (words.Count >= 4 && words[1].Text == "visibility" && words[2].Text == "to")
                    {
                        spec.AmountNode = ValueNodeAt(words, 3, out int visUsed);
                        spec.HasNumber = words[3].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[3].Number;
                        return Finish(spec, ActionVerb.SetVisibility, words, 3 + visUsed);
                    }
                    return Unknown(spec, words, "yes, a \u201Cset\u201D line — I only know \u201Cset <variable> to <value>\u201D, \u201Cset text of\u201D, \u201Cset state\u201D, \u201Cset my control group\u201D, \u201Cset light\u201D, \u201Cset music/sound/master volume to\u201D, \u201Cset time scale\u201D, \u201Cset rally point\u201D, and \u201Cset visibility\u201D", warn);
                }
                case "enable":
                    if (words.Count >= 2 && words[1].Text == "light")
                        return Finish(spec, ActionVerb.EnableLight, words, 2);
                    return Unknown(spec, words, "\u201Cenable\u201D looks like this: \u201Cenable light\u201D", warn);
                case "disable":
                    if (words.Count >= 2 && words[1].Text == "light")
                        return Finish(spec, ActionVerb.DisableLight, words, 2);
                    return Unknown(spec, words, "\u201Cdisable\u201D looks like this: \u201Cdisable light\u201D", warn);
                case "toggle":
                    if (words.Count >= 2 && words[1].Text == "light")
                        return Finish(spec, ActionVerb.ToggleLight, words, 2);
                    return Unknown(spec, words, "\u201Ctoggle\u201D looks like this: \u201Ctoggle light\u201D", warn);
            }

            if (words.Count >= 3 && words[0].Text == "play" && words[1].Text == "sound" && words[2].Type == TokenType.Str)
            {
                spec.Thing = words[2].Text;
                int soundIndex = 3;
                // `play sound "shot" at Enemy` — a positional source on that actor (Task 31).
                // Without `at` the sound stays non-positional, exactly as before.
                if (words.Count >= soundIndex + 2 && words[soundIndex].Text == "at")
                {
                    string soundTarget = TargetAt(words, soundIndex + 1, out int soundUsed);
                    if (soundTarget == null)
                        return Unknown(spec, words, "\u201Cplay sound \u2026 at\u201D needs a place, like \u201Cplay sound \"shot\" at Enemy\u201D", warn);
                    spec.To = soundTarget;
                    soundIndex += 1 + soundUsed;
                }
                return Finish(spec, ActionVerb.PlaySound, words, soundIndex);
            }
            if (words.Count >= 2 && words[0].Text == "play" && words[1].Text == "sound")
                return Unknown(spec, words, "\u201Cplay sound\u201D needs a file in quotes, like \u201Cplay sound \"Boing.wav\"\u201D", warn);
            if (words.Count >= 5 && words[0].Text == "play" && words[1].Text == "music" &&
                words[2].Text == "random" && words[3].Text == "from" && words[4].Type == TokenType.Str)
            {
                // `play music random from "a, b, c"` — one clip out of a quoted list, so a
                // level can roll its own track without a chain of if-blocks.
                spec.Choices.AddRange(AudioMix.SplitChoices(words[4].Text));
                if (spec.Choices.Count == 0)
                    return Unknown(spec, words, "\u201Cplay music random from\u201D needs at least one clip name, like \u201Cplay music random from \"menu, level1\"\u201D", warn);
                return Finish(spec, ActionVerb.PlayMusic, words, 5);
            }
            if (words.Count >= 3 && words[0].Text == "play" && words[1].Text == "music" && words[2].Type == TokenType.Str)
            {
                spec.Thing = words[2].Text;
                return Finish(spec, ActionVerb.PlayMusic, words, 3);
            }
            if (words.Count >= 2 && words[0].Text == "play" && words[1].Text == "music")
                return Unknown(spec, words, "\u201Cplay music\u201D needs a clip name in quotes, like \u201Cplay music \"level1\"\u201D, or a list: \u201Cplay music random from \"level1, level2\"\u201D", warn);
            if (words.Count >= 5 && words[0].Text == "loop" && words[1].Text == "music" &&
                words[2].Text == "random" && words[3].Text == "from" && words[4].Type == TokenType.Str)
            {
                spec.Choices.AddRange(AudioMix.SplitChoices(words[4].Text));
                if (spec.Choices.Count == 0)
                    return Unknown(spec, words, "\u201Cloop music random from\u201D needs at least one clip name, like \u201Cloop music random from \"menu, level1\"\u201D", warn);
                return Finish(spec, ActionVerb.LoopMusic, words, 5);
            }
            if (words.Count >= 3 && words[0].Text == "loop" && words[1].Text == "music" && words[2].Type == TokenType.Str)
            {
                spec.Thing = words[2].Text;
                return Finish(spec, ActionVerb.LoopMusic, words, 3);
            }
            if (words.Count >= 2 && words[0].Text == "loop" && words[1].Text == "music")
                return Unknown(spec, words, "\u201Cloop music\u201D needs a clip name in quotes, like \u201Cloop music \"menu\"\u201D, or a list: \u201Cloop music random from \"menu, level1\"\u201D", warn);
            // `play anim "<name>" upper body` (Task 33 Part C): into the slot, not the whole pose.
            if (words.Count >= 5 && words[0].Text == "play" && words[1].Text == "anim" &&
                words[2].Type == TokenType.Str && words[3].Text == "upper" && words[4].Text == "body")
            {
                spec.Thing = words[2].Text;
                spec.UpperBody = true;
                return Finish(spec, ActionVerb.PlayAnim, words, 5);
            }
            if (words.Count >= 3 && words[0].Text == "play" && words[1].Text == "anim" && words[2].Type == TokenType.Str)
            {
                spec.Thing = words[2].Text;
                return Finish(spec, ActionVerb.PlayAnim, words, 3);
            }
            if (words.Count >= 2 && words[0].Text == "play" && words[1].Text == "anim")
                return Unknown(spec, words, "\u201Cplay anim\u201D needs a name in quotes, like \u201Cplay anim \"Attack\"\u201D", warn);
            if (words.Count >= 3 && words[0].Text == "play" && words[1].Text == "particle" && words[2].Type == TokenType.Str)
            {
                spec.Thing = words[2].Text;
                return Finish(spec, ActionVerb.PlayParticle, words, 3);
            }
            if (words.Count >= 2 && words[0].Text == "play" && words[1].Text == "particle")
                return Unknown(spec, words, "\u201Cplay particle\u201D needs a name in quotes, like \u201Cplay particle \"smoke\"\u201D", warn);
            if (words.Count >= 5 && words[0].Text == "loop" && words[1].Text == "anim" &&
                words[2].Type == TokenType.Str && words[3].Text == "upper" && words[4].Text == "body")
            {
                spec.Thing = words[2].Text;
                spec.UpperBody = true;
                return Finish(spec, ActionVerb.LoopAnim, words, 5);
            }
            if (words.Count >= 3 && words[0].Text == "loop" && words[1].Text == "anim" && words[2].Type == TokenType.Str)
            {
                spec.Thing = words[2].Text;
                return Finish(spec, ActionVerb.LoopAnim, words, 3);
            }
            if (words.Count >= 2 && words[0].Text == "loop" && words[1].Text == "anim")
                return Unknown(spec, words, "\u201Cloop anim\u201D needs a name in quotes, like \u201Cloop anim \"Walk\"\u201D", warn);
            if (words.Count >= 2 && (words[0].Text == "show" || words[0].Text == "hide") && words[1].Text == "text")
            {
                bool showText = words[0].Text == "show";
                if (showText && words.Count >= 3 && words[2].Type == TokenType.Str)
                {
                    spec.Thing = words[2].Text;
                    return Finish(spec, ActionVerb.ShowText, words, 3);
                }
                if (!showText && words.Count == 2)
                    return Finish(spec, ActionVerb.HideText, words, 2);
                return Unknown(spec, words, showText
                    ? "\u201Cshow text\u201D displays quoted text, like \u201Cshow text \"Game Over\"\""
                    : "\u201Chide text\u201D takes nothing after it", warn);
            }
            // `reveal area around me by <radius>` / `hide area around me by <radius>` — RTS
            // fog of war (Task 18 Part F). Checked before the plain `hide <thing>` form so
            // "area" is never treated as an object to hide.
            if (words.Count >= 6 && (words[0].Text == "reveal" || words[0].Text == "hide") &&
                words[1].Text == "area" && words[2].Text == "around" && words[3].Text == "me" &&
                words[4].Text == "by")
            {
                spec.AmountNode = ValueNodeAt(words, 5, out int fogUsed);
                spec.HasNumber = words[5].Type == TokenType.Number;
                if (spec.HasNumber)
                    spec.Number = words[5].Number;
                return Finish(spec, words[0].Text == "reveal" ? ActionVerb.RevealArea : ActionVerb.HideArea, words, 5 + fogUsed);
            }
            if (words.Count >= 2 && (words[0].Text == "reveal" || words[0].Text == "hide") &&
                words[1].Text == "area")
                return Unknown(spec, words, "fog areas look like this: \u201Creveal area around me by 200\u201D or \u201Chide area around me by 200\u201D", warn);
            if (words.Count >= 2 && (words[0].Text == "hide" || words[0].Text == "show") && IsThing(words[1]))
            {
                spec.Thing = words[1].Text;
                return Finish(spec, words[0].Text == "hide" ? ActionVerb.Hide : ActionVerb.Show, words, 2);
            }
            if (words.Count >= 2 && words[0].Text == "restart" && words[1].Text == "scene")
                return Finish(spec, ActionVerb.RestartScene, words, 2);
            if (words.Count >= 3 && words[0].Text == "load" && words[1].Text == "scene" && words[2].Type == TokenType.Str)
            {
                spec.Thing = words[2].Text;
                return Finish(spec, ActionVerb.LoadScene, words, 3);
            }
            if (words.Count >= 2 && words[0].Text == "add" && words[1].Text == "scene")
            {
                if (words.Count >= 3 && words[2].Type == TokenType.Str)
                {
                    spec.Thing = words[2].Text;
                    return Finish(spec, ActionVerb.AddScene, words, 3);
                }
                return Unknown(spec, words, "\u201Cadd scene\u201D needs the scene name in quotes, like \u201Cadd scene \"HUD\"\u201D", warn);
            }
            if (words.Count >= 2 && words[0].Text == "unload" && words[1].Text == "scene")
            {
                if (words.Count >= 3 && words[2].Type == TokenType.Str)
                {
                    spec.Thing = words[2].Text;
                    return Finish(spec, ActionVerb.UnloadScene, words, 3);
                }
                return Unknown(spec, words, "\u201Cunload scene\u201D needs the scene name in quotes, like \u201Cunload scene \"HUD\"\u201D", warn);
            }
            if (words.Count >= 2 && words[0].Text == "quit" && words[1].Text == "game")
                return Finish(spec, ActionVerb.QuitGame, words, 2);
            if (words.Count >= 2 && (words[0].Text == "print" || words[0].Text == "debug"))
            {
                if (words[1].Type == TokenType.Str)
                {
                    spec.Thing = words[1].Text;
                    return Finish(spec, ActionVerb.Print, words, 2);
                }
                var parts = new System.Text.StringBuilder();
                for (int i = 1; i < words.Count; i++)
                {
                    if (parts.Length > 0) parts.Append(' ');
                    parts.Append(words[i].Text);
                }
                spec.Thing = parts.ToString();
                return Finish(spec, ActionVerb.Print, words, words.Count);
            }
            return Unknown(spec, words, "", warn);
        }

        /// <summary>
        /// <c>tween &lt;thing&gt; position to &lt;target&gt; in &lt;time&gt;</c>, plus alpha/scale/color
        /// and an optional trailing <c>easing linear|ease in|ease out|ease in out</c>.
        /// </summary>
        private static ActionSpec TweenAction(ActionSpec spec, List<Token> words, Action<string> warn)
        {
            int index = 1;
            string subject = ThingAt(words, index, out int subjectUsed);
            if (subject == null)
                return Unknown(spec, words, "\u201Ctween\u201D needs a thing to animate, like \u201Ctween PausePanel alpha to 0 in 0.3\u201D", warn);
            spec.Thing = subject;
            index += subjectUsed;

            if (index >= words.Count)
                return Unknown(spec, words, "\u201Ctween\u201D needs position, alpha, scale, or color after the thing", warn);
            switch (words[index].Text)
            {
                case "position":
                    spec.TweenProperty = TweenKind.Position;
                    break;
                case "alpha":
                    spec.TweenProperty = TweenKind.Alpha;
                    break;
                case "scale":
                    spec.TweenProperty = TweenKind.Scale;
                    break;
                case "color":
                    spec.TweenProperty = TweenKind.Color;
                    break;
                default:
                    return Unknown(spec, words, "\u201Ctween\u201D knows position, alpha, scale, and color", warn);
            }
            index++;

            if (index >= words.Count || words[index].Text != "to")
                return Unknown(spec, words, "\u201Ctween\u201D joins the property and its target with \u201Cto\u201D", warn);
            index++;
            if (index >= words.Count)
                return Unknown(spec, words, "\u201Ctween\u201D is missing the target value after \u201Cto\u201D", warn);

            if (spec.TweenProperty == TweenKind.Position)
            {
                string target = ThingAt(words, index, out int targetUsed);
                if (target == null)
                    return Unknown(spec, words, "\u201Ctween position\u201D needs an actor to move to, like \u201Ctween PausePanel position to CenterPoint in 0.5\u201D", warn);
                spec.TweenTarget = target;
                index += targetUsed;
            }
            else if (spec.TweenProperty == TweenKind.Color)
            {
                if (words[index].Type != TokenType.Str)
                    return Unknown(spec, words, "\u201Ctween color\u201D needs a color in quotes, like \u201Ctween PausePanel color to \"1 0 0\" in 0.5\u201D", warn);
                spec.TweenColor = words[index].Text;
                index++;
            }
            else
            {
                spec.TweenValue = ValueNodeAt(words, index);
                spec.HasNumber = words[index].Type == TokenType.Number;
                if (spec.HasNumber)
                    spec.Number = words[index].Number;
                index++;
            }

            if (index + 1 >= words.Count || words[index].Text != "in" || words[index + 1].Type != TokenType.Number)
                return Unknown(spec, words, "\u201Ctween\u201D needs a time in seconds, like \u201C... in 0.5\u201D", warn);
            spec.Duration = words[index + 1].Number;
            index += 2;

            if (index < words.Count)
            {
                if (words[index].Text != "easing")
                    return Unknown(spec, words, "after the time, \u201Ctween\u201D only takes \u201Ceasing ...\u201D", warn);
                index++;
                if (index < words.Count && words[index].Text == "linear")
                {
                    spec.Easing = TweenEasing.Linear;
                    index++;
                }
                else if (index < words.Count && words[index].Text == "ease")
                {
                    index++;
                    bool easeIn = index < words.Count && words[index].Text == "in";
                    if (easeIn) index++;
                    bool easeOut = index < words.Count && words[index].Text == "out";
                    if (easeOut) index++;
                    if (!easeIn && !easeOut)
                        return Unknown(spec, words, "easing looks like \u201Ceasing linear\u201D, \u201Ceasing ease in\u201D, \u201Ceasing ease out\u201D, or \u201Ceasing ease in out\u201D", warn);
                    spec.Easing = easeIn && easeOut ? TweenEasing.EaseInOut : easeIn ? TweenEasing.EaseIn : TweenEasing.EaseOut;
                }
                else
                {
                    return Unknown(spec, words, "easing looks like \u201Ceasing linear\u201D, \u201Ceasing ease in\u201D, \u201Ceasing ease out\u201D, or \u201Ceasing ease in out\u201D", warn);
                }
            }

            spec.HasTween = true;
            return Finish(spec, ActionVerb.Tween, words, index);
        }

        /// <summary>
        /// <c>moveplatform &lt;thing&gt; &lt;axis&gt; &lt;distance&gt; in &lt;time&gt; [easing linear|ease in|ease out|ease in out]</c>.
        /// The subject sweeps back and forth along a world axis (x/y/z) by the signed distance.
        /// </summary>
        private static ActionSpec MovePlatformAction(ActionSpec spec, List<Token> words, Action<string> warn)
        {
            int index = 1;
            string subject = ThingAt(words, index, out int subjectUsed);
            if (subject == null)
                return Unknown(spec, words, "\u201Cmoveplatform\u201D needs a thing to move, like \u201Cmoveplatform Plat1 x 200 in 2\u201D", warn);
            spec.Thing = subject;
            index += subjectUsed;

            if (index >= words.Count)
                return Unknown(spec, words, "\u201Cmoveplatform\u201D needs an axis (x, y or z) after the thing", warn);
            string axis = words[index].Text.ToLowerInvariant();
            if (axis != "x" && axis != "y" && axis != "z")
                return Unknown(spec, words, "\u201Cmoveplatform\u201D only knows axes x, y and z", warn);
            spec.Dir = axis;
            index++;

            if (index >= words.Count || words[index].Type != TokenType.Number)
                return Unknown(spec, words, "\u201Cmoveplatform\u201D needs a distance number after the axis, like \u201Cmoveplatform Plat1 x 200 in 2\u201D", warn);
            spec.Number = words[index].Number;
            spec.HasNumber = true;
            index++;

            if (index + 1 >= words.Count || words[index].Text != "in" || words[index + 1].Type != TokenType.Number)
                return Unknown(spec, words, "\u201Cmoveplatform\u201D needs a time in seconds, like \u201C... in 2\u201D", warn);
            spec.Duration = words[index + 1].Number;
            index += 2;

            if (index < words.Count)
            {
                if (words[index].Text != "easing")
                    return Unknown(spec, words, "after the time, \u201Cmoveplatform\u201D only takes \u201Ceasing ...\u201D", warn);
                index++;
                if (index < words.Count && words[index].Text == "linear")
                {
                    spec.Easing = TweenEasing.Linear;
                    index++;
                }
                else if (index < words.Count && words[index].Text == "ease")
                {
                    index++;
                    bool easeIn = index < words.Count && words[index].Text == "in";
                    if (easeIn) index++;
                    bool easeOut = index < words.Count && words[index].Text == "out";
                    if (easeOut) index++;
                    if (!easeIn && !easeOut)
                        return Unknown(spec, words, "easing looks like \u201Ceasing linear\u201D, \u201Ceasing ease in\u201D, \u201Ceasing ease out\u201D, or \u201Ceasing ease in out\u201D", warn);
                    spec.Easing = easeIn && easeOut ? TweenEasing.EaseInOut : easeIn ? TweenEasing.EaseIn : TweenEasing.EaseOut;
                }
                else
                {
                    return Unknown(spec, words, "easing looks like \u201Ceasing linear\u201D, \u201Ceasing ease in\u201D, \u201Ceasing ease out\u201D, or \u201Ceasing ease in out\u201D", warn);
                }
            }

            return Finish(spec, ActionVerb.MovePlatform, words, index);
        }

        private static ActionSpec SelectAction(ActionSpec spec, List<Token> words, Action<string> warn)
        {
            // `select units near me by 5` — box-select everything in a radius.
            if (words.Count >= 4 && words[1].Text == "units" && words[2].Text == "near")
            {
                string anchor = ThingAt(words, 3, out int nearUsed);
                if (anchor != null)
                {
                    spec.Thing = anchor;
                    spec.Near = true;
                    spec.Distance = 5;
                    for (int i = 3 + nearUsed; i < words.Count - 1; i++)
                    {
                        if (words[i].Text != "by" || words[i + 1].Type != TokenType.Number)
                            continue;
                        spec.Distance = words[i + 1].Number;
                        spec.HasNumber = true;
                        spec.Number = spec.Distance;
                    }
                    return Finish(spec, ActionVerb.Select, words, words.Count);
                }
            }
            // `select all Marine` — pick every actor of that type.
            if (words.Count >= 3 && words[1].Text == "all")
            {
                string unit = ThingAt(words, 2, out int allUsed);
                if (unit != null)
                {
                    spec.All = true;
                    spec.Thing = unit;
                    return Finish(spec, ActionVerb.Select, words, 2 + allUsed);
                }
                return Unknown(spec, words, "\u201Cselect all\u201D needs a unit type, like \u201Cselect all Marine\u201D", warn);
            }
            // `select me` / `select nearest Enemy` / `select Marine`.
            {
                string thing = ThingAt(words, 1, out int used);
                if (thing != null)
                {
                    spec.Thing = thing;
                    return Finish(spec, ActionVerb.Select, words, 1 + used);
                }
            }
            return Unknown(spec, words, "\u201Cselect\u201D looks like this: \u201Cselect me\u201D, \u201Cselect all Marine\u201D, or \u201Cselect units near me by 5\u201D", warn);
        }

        private static ActionSpec DeselectAction(ActionSpec spec, List<Token> words, Action<string> warn)
        {
            if (words.Count == 2 && words[1].Text == "all")
            {
                spec.All = true;
                return Finish(spec, ActionVerb.Deselect, words, 2);
            }
            string thing = ThingAt(words, 1, out int used);
            if (thing != null)
            {
                spec.Thing = thing;
                return Finish(spec, ActionVerb.Deselect, words, 1 + used);
            }
            return Unknown(spec, words, "\u201Cdeselect\u201D looks like this: \u201Cdeselect me\u201D or \u201Cdeselect all\u201D", warn);
        }

        private static ActionSpec Move(ActionSpec spec, List<Token> words, Action<string> warn)
        {
            if (words.Count >= 2 && words[1].Text == "away" && words.Count >= 4 && words[2].Text == "from" && IsThing(words[3]))
            {
                spec.Thing = words[3].Text;
                spec.Dir = "away";
                if (words.Count >= 6 && words[words.Count - 2].Text == "speed")
                {
                    spec.AmountNode = TrailingValue(words);
                    spec.HasNumber = words[words.Count - 1].Type == TokenType.Number;
                    if (spec.HasNumber)
                        spec.Number = words[words.Count - 1].Number;
                }
                else
                {
                    spec.Number = 5;
                    spec.HasNumber = true;
                }
                return Finish(spec, ActionVerb.Move, words, words.Count);
            }
            if (words.Count >= 2 && words[1].Text == "toward")
            {
                string target = ThingAt(words, 2, out int used);
                if (target != null)
                {
                    spec.Thing = target;
                    int afterThing = 2 + used;
                    if (words.Count >= afterThing + 2 && words[afterThing].Text == "speed")
                    {
                        // `speed 60` or `speed my Speed` — a `my` amount is two tokens, and the
                        // value no longer has to be the last thing on the line (`around walls`).
                        spec.AmountNode = ValueNodeAt(words, afterThing + 1, out int speedUsed);
                        spec.HasNumber = words[afterThing + 1].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[afterThing + 1].Number;
                        afterThing += 1 + speedUsed;
                    }
                    else
                    {
                        spec.Number = 5;
                        spec.HasNumber = true;
                    }
                    // `around walls` (Task 7 G2): the bridge corner-samples this chase instead of
                    // walking straight into the obstacle. Absent = today's straight line.
                    if (words.Count >= afterThing + 2 &&
                        words[afterThing].Text == "around" && words[afterThing + 1].Text == "walls")
                    {
                        spec.AroundWalls = true;
                        afterThing += 2;
                    }
                    // `facing forward` (Task 7 G2 follow-up): turn the body onto the heading this
                    // chase actually walks, so a chaser never slides sideways at the player. Same
                    // qualifier `goto` uses; the order is `move toward X [speed n] [around walls]
                    // [facing forward]`.
                    if (words.Count >= afterThing + 2 &&
                        words[afterThing].Text == "facing" && words[afterThing + 1].Text == "forward")
                    {
                        spec.FaceForward = true;
                        afterThing += 2;
                    }
                    return Finish(spec, ActionVerb.Move, words, afterThing);
                }
                return Unknown(spec, words, "\"move toward\" needs a target, like \"move toward Player speed 2\"", warn);
            }
            if (words.Count >= 2 && IsDirection(words[1].Text))
            {
                spec.Dir = words[1].Text == "back" ? "backward" : words[1].Text;
                if (words.Count >= 4 && words[2].Text == "speed")
                {
                    spec.AmountNode = TrailingValue(words);
                    spec.HasNumber = words[words.Count - 1].Type == TokenType.Number;
                    if (spec.HasNumber)
                        spec.Number = words[words.Count - 1].Number;
                }
                else
                {
                    spec.Number = 5;
                    spec.HasNumber = true;
                }
                return Finish(spec, ActionVerb.Move, words, words.Count);
            }
            return Unknown(spec, words, "\u201Cmove\u201D looks like this: \u201Cmove forward speed 5\u201D", warn);
        }

        private static ValueNode ValueNodeAt(List<Token> words, int index)
        {
            return ValueNodeAt(words, index, out _);
        }

        /// <summary>
        /// Reads the amount at <paramref name="index"/> — a number, a global, or <c>my &lt;name&gt;</c>
        /// (which is two tokens, so <paramref name="used"/> says how many the caller just spent).
        /// Bullet speeds, blast radii and knockbacks all come from the spawned actor's own
        /// variables, so the per-object form has to work wherever an amount is accepted.
        /// </summary>
        private static ValueNode ValueNodeAt(List<Token> words, int index, out int used)
        {
            used = 1;
            if (index + 1 < words.Count && words[index].Text == "my" &&
                words[index + 1].Type == TokenType.Word)
            {
                used = 2;
                return new VariableValue { Owner = "my", Name = words[index + 1].Text };
            }
            if (words[index].Type == TokenType.Number)
                return new NumberValue { Value = words[index].Number };
            return new VariableValue { Owner = "global", Name = words[index].Text };
        }

        /// <summary>The value that ends the line, so <c>speed 5</c> and <c>speed my Speed</c>
        /// both read their amount.</summary>
        private static ValueNode TrailingValue(List<Token> words)
        {
            int index = words.Count - 1;
            if (index >= 1 && words[index - 1].Text == "my" && words[index].Type == TokenType.Word)
                index--;
            return ValueNodeAt(words, index);
        }

        private static bool IsThing(Token token)
        {
            return token.Type == TokenType.Word;
        }

        private static bool IsQualifier(string word)
        {
            return word == "nearest" || word == "random";
        }

        private static string ThingAt(List<Token> words, int index, out int used)
        {
            used = 1;
            if (index >= words.Count || words[index].Type != TokenType.Word)
                return null;
            string first = words[index].Text;
            if (IsQualifier(first) && index + 1 < words.Count && words[index + 1].Type == TokenType.Word)
            {
                used = 2;
                return first + " " + words[index + 1].Text;
            }
            return first;
        }

        /// <summary>Reads the actor name after <c>at</c>: a word (plus a <c>nearest</c>/<c>random</c>
        /// qualifier), followed by any number words — Flax names duplicates `SpawnPoint 1`, and those
        /// have to stay one name.</summary>
        /// <summary>
        /// <c>add buff "&lt;name&gt;" [to &lt;target&gt;] for &lt;n&gt; seconds [refresh|stack|ignore]
        /// [and] [adding &lt;value&gt; to my &lt;stat&gt;] [and multiplying my &lt;stat&gt; by &lt;value&gt;]</c>
        /// (Task 27 Part C). Without <c>to</c> the buff lands on the acting sheet.
        /// </summary>
        private static ActionSpec AddBuff(ActionSpec spec, List<Token> words, Action<string> warn)
        {
            const string shape = "\u201Cadd buff\u201D looks like \u201Cadd buff \"stim\" to me for 8 seconds adding 40 to my Speed\u201D";
            int index = 2;
            if (index >= words.Count || words[index].Type != TokenType.Str)
                return Unknown(spec, words, shape, warn);
            spec.BuffName = words[index].Text;
            index++;
            if (index < words.Count && words[index].Text == "to")
            {
                string target = TargetAt(words, index + 1, out int targetUsed);
                if (target == null)
                    return Unknown(spec, words, shape, warn);
                spec.To = target;
                index += 1 + targetUsed;
            }
            else
                spec.To = "me";
            if (index + 2 >= words.Count || words[index].Text != "for" ||
                words[index + 1].Type != TokenType.Number || words[index + 2].Text != "seconds")
                return Unknown(spec, words, "an \u201Cadd buff\u201D line needs a duration, like \u201Cfor 8 seconds\u201D", warn);
            spec.Duration = words[index + 1].Number;
            index += 3;
            if (index < words.Count && words[index].Type == TokenType.Word &&
                (words[index].Text == "refresh" || words[index].Text == "stack" || words[index].Text == "ignore"))
            {
                spec.BuffRule = words[index].Text == "stack" ? BuffRule.Stack
                    : words[index].Text == "ignore" ? BuffRule.Ignore
                    : BuffRule.Refresh;
                index++;
            }
            while (index < words.Count)
            {
                if (words[index].Text == "and" && index + 1 < words.Count)
                    index++;
                if (words[index].Text == "adding")
                {
                    var word = new BuffModifierWord { Multiply = false };
                    word.Value = ValueNodeAt(words, index + 1, out int valueUsed);
                    if (word.Value == null)
                        return Unknown(spec, words, shape, warn);
                    if (words[index + 1].Type == TokenType.Number)
                        word.Number = words[index + 1].Number;
                    int toIndex = index + 1 + valueUsed;
                    if (toIndex + 1 >= words.Count || words[toIndex].Text != "to" ||
                        words[toIndex + 1].Text != "my" ||
                        !TryReadMyStat(words, toIndex + 1, out string stat, out int statUsed))
                        return Unknown(spec, words, "an \u201Cadding\u201D clause reads \u201Cadding 40 to my Speed\u201D", warn);
                    word.Stat = stat;
                    spec.BuffModifiers.Add(word);
                    index = toIndex + 1 + statUsed;
                    continue;
                }
                if (words[index].Text == "multiplying")
                {
                    var word = new BuffModifierWord { Multiply = true };
                    if (!TryReadMyStat(words, index + 1, out string stat, out int statUsed))
                        return Unknown(spec, words, "a \u201Cmultiplying\u201D clause reads \u201Cmultiplying my Speed by 1.5\u201D", warn);
                    int byIndex = index + 1 + statUsed;
                    if (byIndex + 1 >= words.Count || words[byIndex].Text != "by")
                        return Unknown(spec, words, "a \u201Cmultiplying\u201D clause reads \u201Cmultiplying my Speed by 1.5\u201D", warn);
                    word.Stat = stat;
                    word.Value = ValueNodeAt(words, byIndex + 1, out int factorUsed);
                    if (word.Value == null)
                        return Unknown(spec, words, "a \u201Cmultiplying\u201D clause reads \u201Cmultiplying my Speed by 1.5\u201D", warn);
                    if (words[byIndex + 1].Type == TokenType.Number)
                        word.Number = words[byIndex + 1].Number;
                    spec.BuffModifiers.Add(word);
                    index = byIndex + 1 + factorUsed;
                    continue;
                }
                return Unknown(spec, words,
                    $"I don't know \u201C{words[index].Text}\u201D in an \u201Cadd buff\u201D line \u2014 it takes a duration, \u201Crefresh\u201D/\u201Cstack\u201D/\u201Cignore\u201D, and \u201Cadding <n> to my <stat>\u201D or \u201Cmultiplying my <stat> by <n>\u201D",
                    warn);
            }
            return Finish(spec, ActionVerb.AddBuff, words, index);
        }

        /// <summary><c>remove buff "&lt;name&gt;" [from &lt;target&gt;]</c> — ends a buff early (Task 27 Part C).</summary>
        private static ActionSpec RemoveBuff(ActionSpec spec, List<Token> words, Action<string> warn)
        {
            const string shape = "\u201Cremove buff\u201D looks like \u201Cremove buff \"stim\" from me\u201D";
            int index = 2;
            if (index >= words.Count || words[index].Type != TokenType.Str)
                return Unknown(spec, words, shape, warn);
            spec.BuffName = words[index].Text;
            index++;
            if (index < words.Count && words[index].Text == "from")
            {
                string target = TargetAt(words, index + 1, out int targetUsed);
                if (target == null)
                    return Unknown(spec, words, shape, warn);
                spec.To = target;
                index += 1 + targetUsed;
            }
            else
                spec.To = "me";
            if (index < words.Count)
                return Unknown(spec, words, shape, warn);
            return Finish(spec, ActionVerb.RemoveBuff, words, index);
        }

        /// <summary><c>my &lt;stat&gt;</c> inside a buff clause, including the two-word health stats.</summary>
        private static bool TryReadMyStat(List<Token> words, int index, out string stat, out int used)
        {
            stat = "";
            used = 0;
            if (index + 1 >= words.Count || words[index].Text != "my" ||
                words[index + 1].Type != TokenType.Word)
                return false;
            if (index + 2 < words.Count && words[index + 2].Type == TokenType.Word &&
                HealthManager.TryStat(words[index + 1].Text + " " + words[index + 2].Text, out string pair))
            {
                stat = pair;
                used = 3;
                return true;
            }
            stat = words[index + 1].Text;
            used = 2;
            return true;
        }

        private static string TargetAt(List<Token> words, int index, out int used)
        {
            used = 0;
            string name = ThingAt(words, index, out used);
            if (name == null)
                return null;
            while (index + used < words.Count && words[index + used].Type == TokenType.Number)
            {
                name += " " + words[index + used].Text;
                used++;
            }
            return name;
        }

        private static bool IsDirection(string word)
        {
            foreach (string direction in DirectionWords)
                if (word == direction)
                    return true;
            return false;
        }

        /// <summary>
        /// <c>selected &lt;order&gt;</c> / <c>selected units &lt;order&gt;</c> /
        /// <c>selected buildings &lt;order&gt;</c> (Task 29 Part D). The order itself is parsed by
        /// <see cref="Parse"/> unchanged, so `selected units train Marine` is the same spec as
        /// `train Marine` plus the scope the bridge fans out on.
        /// </summary>
        private static ActionSpec ParseSelected(List<Token> words, Action<string> warn)
        {
            var scope = SelectionOrderScope.All;
            int index = 1;
            if (index < words.Count && words[index].Type == TokenType.Word &&
                SelectionInfo.TryScopeWord(words[index].Text, out SelectionOrderScope readScope))
            {
                scope = readScope;
                index++;
            }
            var spec = new ActionSpec();
            if (index >= words.Count)
            {
                return Unknown(spec, words,
                    "\u201Cselected\u201D needs an order after it \u2014 like \u201Cselected units stop\u201D or \u201Cselected buildings train Marine\u201D",
                    warn);
            }
            var rest = words.GetRange(index, words.Count - index);
            ActionSpec order = Parse(rest, warn);
            if (!SelectionInfo.AllowsOrderScope(order.Verb))
            {
                order.Verb = ActionVerb.Unknown;
                if (warn != null)
                    warn("\u201Cselected\u201D only goes with orders \u2014 \u201Cselected units goto there\u201D, \u201Cselected units stop\u201D, \u201Cselected units attack Enemy\u201D, \u201Cselected units gather GoldMine\u201D, \u201Cselected buildings train Marine\u201D, \u201Cselected units set rally point to there\u201D");
                return order;
            }
            order.HasSelectionScope = true;
            order.SelectionScope = scope;
            return order;
        }

        private static ActionSpec Finish(ActionSpec spec, ActionVerb verb, List<Token> words, int consumed)
        {
            spec.Verb = verb;
            for (int i = consumed; i < words.Count; i++)
                spec.Leftover.Add(words[i]);
            return spec;
        }

        private static ActionSpec Unknown(ActionSpec spec, List<Token> words, string hint, Action<string> warn)
        {
            spec.Verb = ActionVerb.Unknown;
            foreach (var word in words)
                spec.Leftover.Add(word);
            if (warn != null)
            {
                var parts = new System.Text.StringBuilder();
                for (int i = 0; i < words.Count; i++)
                {
                    if (parts.Length > 0)
                        parts.Append(' ');
                    parts.Append(words[i].Text);
                }
                string way = parts.ToString();
                warn(string.IsNullOrEmpty(hint)
                    ? $"I don't know the action \u201C{way}\u201D yet — the line was skipped."
                    : $"I don't know the action \u201C{way}\u201D yet. {hint}");
            }
            return spec;
        }
    }
}
