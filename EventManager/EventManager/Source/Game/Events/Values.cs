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
                default:
                    return "a value";
            }
        }
    }
}
