using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>One bindable control: an action word pair, its per-player keys and its gamepad button.</summary>
    public class InputAction
    {
        /// <summary>Compact id used in settings variables (<c>Player1 ShootKey</c>).</summary>
        public string Id;
        /// <summary>What an event writes: <c>when turn left is held</c>, <c>when shoot is pressed</c>.</summary>
        public string Name;
        /// <summary>The default key per player, written the way events write keys.</summary>
        public string[] DefaultKeys;
        /// <summary>Flax's <c>GamepadButton</c> name for this action (see <see cref="PlayerInput.ButtonFor"/>).</summary>
        public string Button;
    }

    /// <summary>
    /// Per-player input bindings for local two-player (Task 16 "input" follow-ups).
    ///
    /// Player 1 answers to the arrows, Space, Shift, R and 1-3; player 2 to WASD, Ctrl, Right
    /// Shift, Q and 4-6. A player's sheet can therefore be written once — <c>when Left Arrow is
    /// held</c> means "that player's turn-left key", so Player2 turns with A without a second
    /// sheet. Actions also work by name (<c>when turn left is held</c>), which is what the
    /// options menu binds.
    ///
    /// The map is engine-free: keys and buttons are plain names, so the routing rules are
    /// unit-testable. <c>save bindings</c> writes one global per action (<c>Player1 ShootKey</c>),
    /// and <c>load bindings</c> reads them back — the same globals a UI label can bind to.
    /// </summary>
    public class PlayerInput
    {
        public const int MaxPlayers = 2;

        public static readonly InputAction[] Actions =
        {
            new InputAction { Id = "TurnLeft", Name = "Turn Left", DefaultKeys = new[] { "Left Arrow", "A" }, Button = "LeftStickLeft" },
            new InputAction { Id = "TurnRight", Name = "Turn Right", DefaultKeys = new[] { "Right Arrow", "D" }, Button = "LeftStickRight" },
            new InputAction { Id = "Forward", Name = "Move Forward", DefaultKeys = new[] { "Up Arrow", "W" }, Button = "LeftStickUp" },
            new InputAction { Id = "Back", Name = "Move Backward", DefaultKeys = new[] { "Down Arrow", "S" }, Button = "LeftStickDown" },
            new InputAction { Id = "Shoot", Name = "Shoot", DefaultKeys = new[] { "Space", "Left Ctrl" }, Button = "A" },
            // Flax maps both shifts to one `Shift` key, so player 2 sprints with Alt instead —
            // two players can hold sprint at the same time.
            new InputAction { Id = "Sprint", Name = "Sprint", DefaultKeys = new[] { "Left Shift", "Left Alt" }, Button = "B" },
            new InputAction { Id = "Reload", Name = "Reload", DefaultKeys = new[] { "R", "Q" }, Button = "X" },
            new InputAction { Id = "Weapon1", Name = "Weapon 1", DefaultKeys = new[] { "1", "4" }, Button = "DPadLeft" },
            new InputAction { Id = "Weapon2", Name = "Weapon 2", DefaultKeys = new[] { "2", "5" }, Button = "DPadUp" },
            new InputAction { Id = "Weapon3", Name = "Weapon 3", DefaultKeys = new[] { "3", "6" }, Button = "DPadRight" },
        };

        /// <summary>Words that mean an action, including the short forms people write.</summary>
        private static readonly Dictionary<string, string> ActionAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "turn left", "Turn Left" },
            { "turn right", "Turn Right" },
            { "move forward", "Move Forward" },
            { "forward", "Move Forward" },
            { "move backward", "Move Backward" },
            { "move back", "Move Backward" },
            { "backward", "Move Backward" },
            { "shoot", "Shoot" },
            { "fire", "Shoot" },
            { "sprint", "Sprint" },
            { "run", "Sprint" },
            { "reload", "Reload" },
            { "weapon 1", "Weapon 1" },
            { "weapon 2", "Weapon 2" },
            { "weapon 3", "Weapon 3" },
        };

        /// <summary>player → action → the key it answers to.</summary>
        private readonly Dictionary<int, Dictionary<string, string>> keys = new Dictionary<int, Dictionary<string, string>>();
        /// <summary>player → action → gamepad button, when the options menu captured one.</summary>
        private readonly Dictionary<int, Dictionary<string, string>> buttons = new Dictionary<int, Dictionary<string, string>>();

        public PlayerInput()
        {
            for (int player = 1; player <= MaxPlayers; player++)
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (InputAction action in Actions)
                    map[action.Name] = action.DefaultKeys[player - 1];
                keys[player] = map;
            }
        }

        public static InputAction Find(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return null;
            string wanted = action.Trim();
            foreach (InputAction entry in Actions)
                if (string.Equals(entry.Name, wanted, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.Id, wanted, StringComparison.OrdinalIgnoreCase))
                    return entry;
            return null;
        }

        /// <summary>Reads an action from what an event wrote: a name ("turn left"), a short form
        /// ("forward"), or an id ("TurnLeft").</summary>
        public static bool TryReadAction(string written, out string action)
        {
            action = "";
            if (string.IsNullOrWhiteSpace(written))
                return false;
            InputAction found = Find(written);
            if (found != null)
            {
                action = found.Name;
                return true;
            }
            string squeezed = written.Trim().Replace("  ", " ");
            if (ActionAliases.TryGetValue(squeezed, out string alias))
            {
                action = alias;
                return true;
            }
            return false;
        }

        /// <summary>The global that stores a binding, e.g. "Player1 ShootKey".</summary>
        public static string SettingName(int player, string action)
        {
            InputAction entry = Find(action);
            return Players.ActorName(player) + " " + (entry == null ? action : entry.Id) + "Key";
        }

        /// <summary>True when a saved setting name is one this map writes (<c>Player2 ShootKey</c>).</summary>
        public bool HasSetting(string name)
        {
            for (int player = 1; player <= MaxPlayers; player++)
                foreach (InputAction entry in Actions)
                    if (string.Equals(SettingName(player, entry.Name), name, StringComparison.OrdinalIgnoreCase))
                        return true;
            return false;
        }

        /// <summary>Reads "Player1" + "ShootKey" back into player and action.</summary>
        public static bool TryReadSettingName(string first, string second, out int player, out string action)
        {
            player = 0;
            action = "";
            if (!Players.TryReadNumber(first, out int number) || string.IsNullOrWhiteSpace(second))
                return false;
            string id = second.Trim();
            if (!id.EndsWith("Key", StringComparison.OrdinalIgnoreCase) || id.Length <= "Key".Length)
                return false;
            InputAction entry = Find(id.Substring(0, id.Length - "Key".Length));
            if (entry == null)
                return false;
            player = number;
            action = entry.Name;
            return true;
        }

        public string KeyFor(int player, string action)
        {
            InputAction entry = Find(action);
            if (entry == null)
                return "";
            if (!keys.TryGetValue(player, out var map))
                return entry.DefaultKeys[Math.Max(0, Math.Min(player - 1, entry.DefaultKeys.Length - 1))];
            return map.TryGetValue(entry.Name, out string key) ? key : entry.DefaultKeys[0];
        }

        /// <summary>Binds <paramref name="action"/> to <paramref name="key"/> for one player.</summary>
        public void SetBinding(int player, string action, string key)
        {
            InputAction entry = Find(action);
            if (entry == null || player < 1 || player > MaxPlayers || string.IsNullOrWhiteSpace(key))
                return;
            if (!keys.TryGetValue(player, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                keys[player] = map;
            }
            map[entry.Name] = key.Trim();
        }

        /// <summary>
        /// Turns what an event wrote into the physical key this player listens to. A written
        /// action (<c>when shoot is pressed</c>) reads that player's binding; a written key
        /// (<c>when Left Arrow is held</c>) reads the binding of the action it is the default
        /// for. Anything else (P, Escape, …) stays itself and behaves globally.
        /// </summary>
        public string Resolve(int player, string written)
        {
            if (player < 1 || player > MaxPlayers || string.IsNullOrWhiteSpace(written))
                return written;
            if (TryReadAction(written, out string action))
                return KeyFor(player, action);
            foreach (InputAction entry in Actions)
                if (string.Equals(entry.DefaultKeys[0], written.Trim(), StringComparison.OrdinalIgnoreCase))
                    return KeyFor(player, entry.Name);
            return written;
        }

        /// <summary>
        /// Reads the action a written key trigger stands for: the action's name / alias, or the
        /// key that is its default for player 1 (<c>when Left Arrow is held</c> → Turn Left).
        /// </summary>
        public static bool TryActionFor(string written, out string action)
        {
            if (TryReadAction(written, out action))
                return true;
            if (!string.IsNullOrWhiteSpace(written))
            {
                foreach (InputAction entry in Actions)
                {
                    if (!string.Equals(entry.DefaultKeys[0], written.Trim(), StringComparison.OrdinalIgnoreCase))
                        continue;
                    action = entry.Name;
                    return true;
                }
            }
            action = "";
            return false;
        }

        /// <summary>True when <paramref name="written"/> is an action or an action's default key.</summary>
        public static bool NamesAction(string written)
        {
            return TryActionFor(written, out _);
        }

        /// <summary>
        /// Which player owns a physical key, or 0 when nobody does (global keys such as P or
        /// Escape). The lowest player wins when both bind the same key.
        /// </summary>
        public int OwnerOf(string physicalKey)
        {
            if (string.IsNullOrWhiteSpace(physicalKey))
                return 0;
            // The bridge scans `D4` for the 4 key; bindings are written `4`.
            string wanted = ActionWords.NormalizeKeyName(physicalKey.Trim());
            for (int player = 1; player <= MaxPlayers; player++)
                foreach (InputAction entry in Actions)
                    if (string.Equals(ActionWords.NormalizeKeyName(KeyFor(player, entry.Name)), wanted, StringComparison.OrdinalIgnoreCase))
                        return player;
            return 0;
        }

        /// <summary>The gamepad index a player reads (player 1 → pad 0, player 2 → pad 1).</summary>
        public static int GamepadFor(int player)
        {
            return Math.Max(0, player - 1);
        }

        /// <summary>The gamepad button for an action, or "" when the action has none.</summary>
        public static string DefaultButtonFor(string action)
        {
            InputAction entry = Find(action);
            return entry == null ? "" : entry.Button;
        }

        /// <summary>The button this player actually uses: a captured one, else the action's default.</summary>
        public string ButtonFor(int player, string action)
        {
            InputAction entry = Find(action);
            if (entry == null)
                return "";
            if (buttons.TryGetValue(player, out var map) && map.TryGetValue(entry.Name, out string button))
                return button;
            return entry.Button;
        }

        /// <summary>Binds an action to a gamepad button (captured from the options menu).</summary>
        public void SetButton(int player, string action, string button)
        {
            InputAction entry = Find(action);
            if (entry == null || player < 1 || player > MaxPlayers || string.IsNullOrWhiteSpace(button))
                return;
            if (!buttons.TryGetValue(player, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                buttons[player] = map;
            }
            map[entry.Name] = button.Trim();
        }

        /// <summary>The action names, for a warning that lists what can be bound.</summary>
        public static string ActionNames()
        {
            var parts = new List<string>();
            foreach (InputAction entry in Actions)
                parts.Add(entry.Name);
            return string.Join(", ", parts);
        }

        /// <summary>Writes every binding as a global (<c>Player1 ShootKey</c>), so UI labels can bind to it.</summary>
        public void SaveTo(EventVariables variables)
        {
            if (variables == null)
                return;
            for (int player = 1; player <= MaxPlayers; player++)
                foreach (InputAction entry in Actions)
                    variables.SetGlobal(SettingName(player, entry.Name), ValueBox.OfText(KeyFor(player, entry.Name)));
        }

        /// <summary>Reads bindings back from the globals a previous save wrote. Missing ones stay at their default.</summary>
        public void LoadFrom(EventVariables variables)
        {
            if (variables == null)
                return;
            for (int player = 1; player <= MaxPlayers; player++)
                foreach (InputAction entry in Actions)
                {
                    string name = SettingName(player, entry.Name);
                    if (variables.HasGlobal(name))
                        SetBinding(player, entry.Name, variables.GetGlobal(name).AsText());
                }
        }

        /// <summary>Restores the factory bindings for one player (or both with 0).</summary>
        public void Reset(int player)
        {
            for (int number = 1; number <= MaxPlayers; number++)
            {
                if (player != 0 && player != number)
                    continue;
                foreach (InputAction entry in Actions)
                    SetBinding(number, entry.Name, entry.DefaultKeys[number - 1]);
            }
        }
    }
}
