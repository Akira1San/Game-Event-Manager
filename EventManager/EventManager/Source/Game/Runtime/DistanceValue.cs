using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// A distance written as a qualifier — `by 40`, `by my Reach` or `by Reach` (Task 37 F3).
    ///
    /// A number is fixed; `my &lt;name&gt;` is the acting sheet actor's own variable; a bare name is a
    /// global. The value is read **live** each time it is needed, so a weapon sheet can say
    /// <c>me is near Enemy by my Reach</c> and have it follow the weapon it equipped, instead of
    /// copying the number into every block.
    /// </summary>
    public static class DistanceValue
    {
        /// <summary>One reading of a `by …` qualifier.</summary>
        public struct Reading
        {
            /// <summary>The literal distance, or 0 when a variable names it.</summary>
            public double Number;
            /// <summary><c>""</c> for a literal, <c>my</c> for the actor's own variable,
            /// <c>global</c> for a game variable.</summary>
            public string Owner;
            /// <summary>The variable named, empty for a literal.</summary>
            public string Name;
            public bool IsVariable => !string.IsNullOrEmpty(Owner);
        }

        /// <summary>
        /// Reads the tokens after <paramref name="byIndex"/>. Anything that is not a number or a
        /// word leaves the literal at 0, which is the same "unset" the old number-only reader
        /// produced, so a mistyped qualifier still reads as no range rather than throwing.
        /// </summary>
        public static Reading Read(List<Token> tokens, int byIndex)
        {
            var reading = new Reading { Owner = "", Name = "" };
            if (tokens == null || byIndex < 0 || byIndex + 1 >= tokens.Count)
                return reading;
            Token next = tokens[byIndex + 1];
            if (next.Type == TokenType.Number)
            {
                reading.Number = next.Number;
                return reading;
            }
            if (next.Type != TokenType.Word)
                return reading;
            if (next.Text == "my" && byIndex + 2 < tokens.Count && tokens[byIndex + 2].Type == TokenType.Word)
            {
                reading.Owner = "my";
                reading.Name = tokens[byIndex + 2].Text;
                return reading;
            }
            reading.Owner = "global";
            reading.Name = next.Text;
            return reading;
        }

        /// <summary>The live number a reading stands for. An unset variable reads 0.</summary>
        public static double Resolve(string owner, string name, EventVariables variables, object actor)
        {
            if (string.IsNullOrEmpty(owner) || variables == null || string.IsNullOrEmpty(name))
                return 0;
            if (owner == "my" && actor != null)
                return variables.HasLocal(actor, name) ? variables.GetLocal(actor, name).AsNumber() : 0;
            if (owner == "my")
                return 0;
            return variables.GetGlobal(name).AsNumber();
        }
    }
}
