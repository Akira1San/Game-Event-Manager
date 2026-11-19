using System.Collections.Generic;

namespace Game
{
    /// <summary>What a <c>bind</c> line keeps in sync: a label's text or a progress bar's fill.</summary>
    public enum BindKind
    {
        Text,
        Progress,
    }

    /// <summary>
    /// A parsed <c>bind</c> directive: <c>bind &lt;label&gt; to &lt;variable&gt;</c>,
    /// <c>bind progress of &lt;thing&gt; to &lt;variable&gt;</c>, and the
    /// <c>format "&lt;template&gt;"</c> form that can watch several variables.
    /// </summary>
    public class BindSpec
    {
        public int Line;
        public string Thing = "";
        public BindKind Kind = BindKind.Text;
        public List<VariableValue> Variables = new List<VariableValue>();
        /// <summary>Template for the <c>format</c> form (placeholders <c>{0}</c>, <c>{1}</c>, ...).</summary>
        public string Template = "";
        public bool HasTemplate;
    }

    /// <summary>
    /// Expands a <c>bind</c> line into the ordinary events it stands for — one
    /// <c>when &lt;variable&gt; changes</c> event per watched variable, whose body is a
    /// <c>set text of</c> / <c>set progress of</c> statement. Nothing new runs at runtime:
    /// the sugar disappears at parse time.
    /// </summary>
    public static class Bindings
    {
        public static List<EventNode> Expand(BindSpec spec)
        {
            var events = new List<EventNode>();
            if (spec == null || spec.Thing.Length == 0 || spec.Variables.Count == 0)
                return events;

            foreach (var variable in spec.Variables)
            {
                var ev = new EventNode { Line = spec.Line };
                if (variable.Owner == "my")
                    ev.Trigger.Add(Word("my", spec.Line));
                ev.Trigger.Add(Word(variable.Name, spec.Line));
                ev.Trigger.Add(Word("changes", spec.Line));

                StatementNode statement;
                if (spec.Kind == BindKind.Progress)
                {
                    statement = new SetProgressNode
                    {
                        Line = spec.Line,
                        Thing = spec.Thing,
                        Value = CopyOf(variable),
                        Reveal = false,
                    };
                }
                else
                {
                    statement = new SetTextNode
                    {
                        Line = spec.Line,
                        Target = spec.Thing,
                        Value = BoundValue(spec),
                        Reveal = false,
                    };
                }

                ev.Body.Add(new BodyLineNode { Line = spec.Line, Statement = statement });
                events.Add(ev);
            }
            return events;
        }

        /// <summary>The value a text bind writes: the variable itself, or the formatted template.</summary>
        private static ValueNode BoundValue(BindSpec spec)
        {
            if (!spec.HasTemplate)
                return CopyOf(spec.Variables[0]);

            var format = new FormatValue { Line = spec.Line, Template = spec.Template };
            foreach (var variable in spec.Variables)
                format.Args.Add(CopyOf(variable));
            return format;
        }

        private static VariableValue CopyOf(VariableValue variable)
        {
            return new VariableValue { Line = variable.Line, Owner = variable.Owner, Name = variable.Name };
        }

        private static Token Word(string text, int line)
        {
            return new Token { Type = TokenType.Word, Text = text, Line = line };
        }
    }
}
