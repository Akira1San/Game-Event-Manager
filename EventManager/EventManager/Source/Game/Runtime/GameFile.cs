using System;

namespace Game
{
    /// <summary>
    /// Which <c>.events</c> file is the scene-wide global sheet (Task 23). The engine used to
    /// look for a file named exactly <c>Game</c>; the name now comes from the <c>GameFile</c>
    /// global, so a genre project can call its sheet <c>rpg_game.events</c>,
    /// <c>adventure_game.events</c> or <c>rts_game.events</c> and leave <c>Game</c> as the
    /// default. The <c>.events</c> suffix is optional when the name is written.
    /// </summary>
    public static class GameFile
    {
        /// <summary>The global that holds the configured file name.</summary>
        public const string Variable = "GameFile";

        /// <summary>The name used when nothing was ever set.</summary>
        public const string Default = "Game";

        private const string Extension = ".events";

        /// <summary>The name without its optional <c>.events</c> suffix; blank reads as the default.</summary>
        public static string Normalize(string written)
        {
            string name = (written ?? "").Trim().Trim('"').Trim();
            if (name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - Extension.Length).Trim();
            return name.Length == 0 ? Default : name;
        }

        /// <summary>The configured global sheet name, or <see cref="Default"/> when unset.</summary>
        public static string NameFrom(EventVariables variables)
        {
            if (variables == null || !variables.HasGlobal(Variable))
                return Default;
            return Normalize(variables.GetGlobal(Variable).AsText());
        }

        /// <summary>True when this file name is the configured global sheet.</summary>
        public static bool IsGlobalSheet(string fileName, EventVariables variables)
        {
            return string.Equals(Normalize(fileName), NameFrom(variables), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when an actor is named after the configured global file. The bridge skips such an
        /// actor when it attaches spawned sheets, exactly as it used to skip a hardcoded "Game".
        /// </summary>
        public static bool IsGlobalActorName(string actorName, EventVariables variables)
        {
            return string.Equals(Normalize(actorName), NameFrom(variables), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True when the variable name is the one this setting lives in.</summary>
        public static bool IsVariableName(string name)
        {
            return string.Equals(name, Variable, StringComparison.OrdinalIgnoreCase);
        }
    }
}
