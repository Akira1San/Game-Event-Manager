using System;
using System.Collections.Generic;

namespace Game
{
    public enum ActionVerb
    {
        Unknown,
        Move,
        StopMoving,
        Turn,
        LookAt,
        Face,
        Teleport,
        Push,
        Goto,
        Parent,
        Unparent,
        Scale,
        Destroy,
        Spawn,
        PlaySound,
        StopSound,
        PlayAnim,
        LoopAnim,
        StopAnim,
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
        // Random-angle wander and still mode (Task 16 Part A)
        Wander,
        Still,
    }

    public class ActionSpec
    {
        public ActionVerb Verb;
        public string Thing = "";
        public string To = "";
        public string Dir = "";
        public double Number;
        public bool HasNumber;
        public bool FaceForward;
        public ValueNode AmountNode;
        public List<Token> Leftover = new List<Token>();
        /// <summary><c>select all &lt;unit type&gt;</c> / <c>deselect all</c>.</summary>
        public bool All;
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
            var spec = new ActionSpec();
            if (words.Count == 0)
                return Unknown(spec, words, "", warn);

            string verb = words[0].Text;
            switch (verb)
            {
                case "stop":
                    if (words.Count >= 2 && words[1].Text == "moving")
                        return Finish(spec, ActionVerb.StopMoving, words, 2);
                    if (words.Count >= 2 && words[1].Text == "sound")
                        return Finish(spec, ActionVerb.StopSound, words, 2);
                    if (words.Count >= 2 && words[1].Text == "anim")
                        return Finish(spec, ActionVerb.StopAnim, words, 2);
                    if (words.Count >= 3 && words[1].Text == "particle" && words[2].Type == TokenType.Str)
                    {
                        spec.Thing = words[2].Text;
                        return Finish(spec, ActionVerb.StopParticle, words, 3);
                    }
                    if (words.Count >= 2 && words[1].Text == "particle")
                        return Unknown(spec, words, "\u201Cstop particle\u201D needs a name in quotes, like \u201Cstop particle \"smoke\"\u201D", warn);
                    return Unknown(spec, words, "yes, a \u201Cstop\u201D line — I only know \u201Cstop sound\u201D, \u201Cstop anim\u201D, \u201Cstop moving\u201D, \u201Cstop particle\u201D, and \u201Cstop this event\u201D", warn);

                case "wander":
                {
                    // `wander` / `wander speed 5` / `wander speed 5 duration 2`
                    int wanderIndex = 1;
                    if (words.Count >= wanderIndex + 2 && words[wanderIndex].Text == "speed")
                    {
                        spec.AmountNode = ValueNodeAt(words, wanderIndex + 1);
                        spec.HasNumber = words[wanderIndex + 1].Type == TokenType.Number;
                        if (spec.HasNumber)
                            spec.Number = words[wanderIndex + 1].Number;
                        wanderIndex += 2;
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

                case "goto":
                {
                    string gotoThing = ThingAt(words, 1, out int gotoUsed);
                    if (gotoThing != null)
                    {
                        spec.Thing = gotoThing;
                        int afterThing = 1 + gotoUsed;
                        if (words.Count >= afterThing + 2 && words[afterThing].Text == "speed")
                        {
                            spec.AmountNode = ValueNodeAt(words, afterThing + 1);
                            spec.HasNumber = words[afterThing + 1].Type == TokenType.Number;
                            if (spec.HasNumber)
                                spec.Number = words[afterThing + 1].Number;
                            afterThing += 2;
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

                case "teleport":
                    if (words.Count >= 4 && IsThing(words[1]) && words[2].Text == "to" && IsThing(words[3]))
                    {
                        spec.Thing = words[1].Text;
                        spec.To = words[3].Text;
                        return Finish(spec, ActionVerb.Teleport, words, 4);
                    }
                    return Unknown(spec, words, "\u201Cteleport\u201D looks like this: \u201Cteleport me to SpawnPoint\u201D", warn);

                case "push":
                    if (words.Count >= 3 && IsThing(words[1]) && IsDirection(words[2].Text))
                    {
                        spec.Thing = words[1].Text;
                        spec.Dir = words[2].Text;
                        int pushIndex = 3;
                        if (words.Count >= pushIndex + 2 && words[pushIndex].Text == "strength")
                        {
                            spec.AmountNode = ValueNodeAt(words, pushIndex + 1);
                            spec.HasNumber = words[pushIndex + 1].Type == TokenType.Number;
                            if (spec.HasNumber)
                                spec.Number = words[pushIndex + 1].Number;
                            pushIndex += 2;
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
                            // `spawn Bullet at me offset 30`: the clone appears 30 units along
                            // the spot's own forward axis (TK4's muzzle offset).
                            if (words.Count >= consumed + 2 && words[consumed].Text == "offset")
                            {
                                spec.AmountNode = ValueNodeAt(words, consumed + 1);
                                spec.HasSpawnOffset = true;
                                if (words[consumed + 1].Type == TokenType.Number)
                                    spec.SpawnOffset = words[consumed + 1].Number;
                                consumed += 2;
                            }
                            return Finish(spec, ActionVerb.Spawn, words, consumed);
                        }
                    }
                    return Unknown(spec, words, "\u201Cspawn\u201D looks like this: \u201Cspawn Bullet at me\u201D, \u201Cspawn Bullet at me offset 30\u201D, \u201Cspawn prefab \"Hero\" at SpawnPoint\u201D or \u201Cspawn scene \"HeroScene\" at SpawnPoint\u201D", warn);
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
                    return Unknown(spec, words, "\u201Ccancel\u201D looks like this: \u201Ccancel build\u201D or \u201Ccancel training Marine\u201D", warn);
                }
                case "set":
                {
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
                    return Unknown(spec, words, "yes, a \u201Cset\u201D line — I only know \u201Cset <variable> to <value>\u201D, \u201Cset text of\u201D, \u201Cset state\u201D, \u201Cset my control group\u201D, \u201Cset light\u201D, and \u201Cset time scale\u201D", warn);
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
                return Finish(spec, ActionVerb.PlaySound, words, 3);
            }
            if (words.Count >= 2 && words[0].Text == "play" && words[1].Text == "sound")
                return Unknown(spec, words, "\u201Cplay sound\u201D needs a file in quotes, like \u201Cplay sound \"Boing.wav\"\u201D", warn);
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
                    spec.AmountNode = ValueNodeAt(words, words.Count - 1);
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
                        spec.AmountNode = ValueNodeAt(words, words.Count - 1);
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
                return Unknown(spec, words, "\"move toward\" needs a target, like \"move toward Player speed 2\"", warn);
            }
            if (words.Count >= 2 && IsDirection(words[1].Text))
            {
                spec.Dir = words[1].Text == "back" ? "backward" : words[1].Text;
                if (words.Count >= 4 && words[2].Text == "speed")
                {
                    spec.AmountNode = ValueNodeAt(words, words.Count - 1);
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
            if (words[index].Type == TokenType.Number)
                return new NumberValue { Value = words[index].Number };
            return new VariableValue { Owner = "global", Name = words[index].Text };
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
