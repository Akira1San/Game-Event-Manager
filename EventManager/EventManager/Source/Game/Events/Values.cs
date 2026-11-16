using System.Collections.Generic;
using System.Globalization;

namespace Game
{
    public abstract class ValueNode
    {
        public int Line;
    }

    public class NumberValue : ValueNode
    {
        public double Value;
    }

    public class TextValue : ValueNode
    {
        public string Text = "";
    }

    public class BooleanValue : ValueNode
    {
        public bool Value;
    }

    public class VariableValue : ValueNode
    {
        public string Owner = "";
        public string Name = "";
        /// <summary>Written with a leading minus (<c>-my DamageTaken</c>): the value is read
        /// and then negated, so one action can spend a captured amount — like
        /// <c>add -my DamageTaken to Health</c> (Task 18 Part G).</summary>
        public bool Negated;
    }

    /// <summary>Which numeric helper a <see cref="HelperValue"/> is (Task 29 Part B).</summary>
    public enum ValueHelper
    {
        Clamp,
        Min,
        Max,
        Round,
        Floor,
        Ceil,
        Abs,
        PercentOf
    }

    /// <summary>
    /// One numeric helper: <c>clamp &lt;v&gt; to &lt;min&gt; and &lt;max&gt;</c>,
    /// <c>min &lt;a&gt; and &lt;b&gt;</c>, <c>max &lt;a&gt; and &lt;b&gt;</c>,
    /// <c>round</c>/<c>floor</c>/<c>ceil</c>/<c>abs &lt;v&gt;</c> and <c>10% of &lt;v&gt;</c>.
    /// <see cref="First"/> is the value being worked on and <see cref="Second"/> / <see cref="Third"/>
    /// are the extra operands the two- and three-argument helpers take, so every helper is one node
    /// type and the evaluator switch stays in one place.
    /// </summary>
    public class HelperValue : ValueNode
    {
        public ValueHelper Kind;
        public ValueNode First = new NumberValue();
        public ValueNode Second = new NumberValue();
        public ValueNode Third = new NumberValue();
    }

    public class RandomRangeValue : ValueNode
    {
        public ValueNode Min = new NumberValue();
        public ValueNode Max = new NumberValue();
    }

    public class MathValue : ValueNode
    {
        public ValueNode Left = new NumberValue();
        public char Op;
        public ValueNode Right = new NumberValue();
    }

    /// <summary>
    /// Two or three operators on one line — <c>my Strength * 2 + 5</c> (Task 29 Part A).
    /// <see cref="Parts"/> holds the operands (one more than <see cref="Ops"/>) and
    /// <see cref="Ops"/> the operators between them, in the order they were written. It stays
    /// flat instead of nested so the runtime can apply precedence without the parser rebuilding
    /// a tree; a single-operator line still uses <see cref="SetNode.Op"/> and
    /// <see cref="SetNode.Right"/>, so nothing that already parsed changes shape.
    /// </summary>
    public class ExpressionValue : ValueNode
    {
        public List<ValueNode> Parts = new List<ValueNode>();
        public List<char> Ops = new List<char>();
    }

    /// <summary>
    /// <c>format "Health: {0}/{1}"</c> — a template whose <c>{0}</c>, <c>{1}</c>, ...
    /// placeholders are filled from <see cref="Args"/>. Used by <c>bind ... format</c>.
    /// </summary>
    public class FormatValue : ValueNode
    {
        public string Template = "";
        public List<ValueNode> Args = new List<ValueNode>();
    }

    public static class WordDistance
    {
        public static int Between(string a, string b)
        {
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++)
                d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++)
                d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                    int del = d[i - 1, j] + 1;
                    int ins = d[i, j - 1] + 1;
                    int sub = d[i - 1, j - 1] + cost;
                    int best = del < ins ? del : ins;
                    if (sub < best)
                        best = sub;
                    d[i, j] = best;
                }
            }
            return d[a.Length, b.Length];
        }
    }

    public static class Values
    {
        /// <summary>The word a helper is written with — also used in its friendly errors.</summary>
        public static string HelperWord(ValueHelper kind)
        {
            switch (kind)
            {
                case ValueHelper.Clamp: return "clamp";
                case ValueHelper.Min: return "min";
                case ValueHelper.Max: return "max";
                case ValueHelper.Round: return "round";
                case ValueHelper.Floor: return "floor";
                case ValueHelper.Ceil: return "ceil";
                case ValueHelper.Abs: return "abs";
                default: return "percent";
            }
        }

        private static string DescribeHelper(HelperValue helper)
        {
            switch (helper.Kind)
            {
                case ValueHelper.Clamp:
                    return $"clamp {Describe(helper.First)} to {Describe(helper.Second)} and {Describe(helper.Third)}";
                case ValueHelper.Min:
                    return $"min {Describe(helper.First)} and {Describe(helper.Second)}";
                case ValueHelper.Max:
                    return $"max {Describe(helper.First)} and {Describe(helper.Second)}";
                case ValueHelper.PercentOf:
                    return $"{Describe(helper.First)}% of {Describe(helper.Second)}";
                default:
                    return $"{HelperWord(helper.Kind)} {Describe(helper.First)}";
            }
        }

        public static string Describe(ValueNode value)
        {
            switch (value)
            {
                case NumberValue n:
                    return n.Value.ToString("R", CultureInfo.InvariantCulture);
                case TextValue t:
                    return $"\"{t.Text}\"";
                case BooleanValue b:
                    return b.Value ? "true" : "false";
                case VariableValue var:
                    return var.Owner.Length == 0 ? var.Name : $"{var.Owner} {var.Name}";
                case RandomRangeValue r:
                    return $"random {Describe(r.Min)} to {Describe(r.Max)}";
                case MathValue m:
                    return $"{Describe(m.Left)} {m.Op} {Describe(m.Right)}";
                case HelperValue h:
                    return DescribeHelper(h);
                case ExpressionValue e:
                {
                    var text = new System.Text.StringBuilder();
                    for (int i = 0; i < e.Parts.Count; i++)
                    {
                        if (i > 0)
                            text.Append(' ').Append(e.Ops[i - 1]).Append(' ');
                        text.Append(Describe(e.Parts[i]));
                    }
                    return text.ToString();
                }
                case FormatValue f:
                    return $"format \"{f.Template}\"";
                default:
                    return "a value";
            }
        }
    }
}
