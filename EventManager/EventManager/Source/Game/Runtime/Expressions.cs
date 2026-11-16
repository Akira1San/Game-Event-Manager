using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// Folding for a multi-operator value (Task 29 Part A). The parser keeps the line flat —
    /// operands and the operators between them — so this is the only place precedence lives:
    /// <c>*</c> and <c>/</c> bind tighter than <c>+</c> and <c>-</c>, and the rest is one
    /// left-to-right pass. It follows the same arithmetic the one-operator <c>set</c> already
    /// used: division by zero reads 0, and a <c>+</c> with text on either side joins the two
    /// texts (so <c>set Total to "Gold: " + my Gold + "!"</c> builds a string instead of
    /// collapsing to a number).
    /// </summary>
    public static class Expressions
    {
        public static ValueBox Fold(ExpressionValue expression, Func<ValueNode, ValueBox> evaluate)
        {
            var values = new List<ValueBox>(expression.Parts.Count);
            for (int i = 0; i < expression.Parts.Count; i++)
                values.Add(evaluate(expression.Parts[i]));

            var ops = new List<char>(expression.Ops);

            // Tight pass: each `*` or `/` collapses into the value on its left.
            for (int i = 0; i < ops.Count; )
            {
                if (ops[i] == '*' || ops[i] == '/')
                {
                    values[i] = Combine(values[i], ops[i], values[i + 1]);
                    values.RemoveAt(i + 1);
                    ops.RemoveAt(i);
                }
                else
                {
                    i++;
                }
            }

            // Loose pass: `+` and `-` left to right, which is all that is left by now.
            for (int i = 0; i < ops.Count; i++)
                values[i + 1] = Combine(values[i], ops[i], values[i + 1]);

            return values[values.Count - 1];
        }

        public static ValueBox Combine(ValueBox left, char op, ValueBox right)
        {
            if (op == '+' && (left.Kind == ValueKind.Text || right.Kind == ValueKind.Text))
                return ValueBox.OfText(left.AsText() + right.AsText());

            double a = left.AsNumber();
            double b = right.AsNumber();
            switch (op)
            {
                case '+': return ValueBox.OfNumber(a + b);
                case '-': return ValueBox.OfNumber(a - b);
                case '*': return ValueBox.OfNumber(a * b);
                case '/': return ValueBox.OfNumber(b == 0 ? 0 : a / b);
                default: return ValueBox.OfNumber(0);
            }
        }
    }
}
