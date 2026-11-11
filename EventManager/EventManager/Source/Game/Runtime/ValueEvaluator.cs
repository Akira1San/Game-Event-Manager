using System;

namespace Game
{
    public delegate ValueBox VariableResolver(VariableValue variable);

    public static class Rng
    {
        private static Random random = new Random();

        public static void Seed(int seed)
        {
            random = new Random(seed);
        }

        public static double NextDouble()
        {
            return random.NextDouble();
        }
    }

    public static class ValueEvaluator
    {
        public static ValueBox Evaluate(ValueNode node, VariableResolver resolve)
        {
            switch (node)
            {
                case NumberValue number:
                    return ValueBox.OfNumber(number.Value);
                case TextValue text:
                    return ValueBox.OfText(text.Text);
                case BooleanValue boolean:
                    return ValueBox.OfBoolean(boolean.Value);
                case VariableValue variable:
                {
                    if (resolve == null)
                        return new ValueBox();
                    return resolve(variable);
                }
                case RandomRangeValue range:
                {
                    double min = Evaluate(range.Min, resolve).AsNumber();
                    double max = Evaluate(range.Max, resolve).AsNumber();
                    if (min > max)
                    {
                        double swap = min;
                        min = max;
                        max = swap;
                    }
                    if (min == Math.Floor(min) && max == Math.Floor(max))
                    {
                        long low = (long)min;
                        long span = (long)max - low + 1;
                        if (span > 0)
                            return ValueBox.OfNumber(low + (long)(Rng.NextDouble() * span));
                        return ValueBox.OfNumber(min);
                    }
                    return ValueBox.OfNumber(min + Rng.NextDouble() * (max - min));
                }
                case MathValue math:
                {
                    double left = Evaluate(math.Left, resolve).AsNumber();
                    double right = Evaluate(math.Right, resolve).AsNumber();
                    switch (math.Op)
                    {
                        case '+':
                            return ValueBox.OfNumber(left + right);
                        case '-':
                            return ValueBox.OfNumber(left - right);
                        case '*':
                            return ValueBox.OfNumber(left * right);
                        case '/':
                            return right == 0 ? ValueBox.OfNumber(0) : ValueBox.OfNumber(left / right);
                        default:
                            return ValueBox.OfNumber(0);
                    }
                }
                case FormatValue format:
                {
                    // `format "Health: {0}/{1}"` — numbered placeholders filled from the args.
                    var text = new System.Text.StringBuilder(format.Template.Length);
                    for (int i = 0; i < format.Template.Length; i++)
                    {
                        char c = format.Template[i];
                        if (c == '{')
                        {
                            int end = format.Template.IndexOf('}', i + 1);
                            if (end > i + 1 && int.TryParse(format.Template.Substring(i + 1, end - i - 1), out int argIndex) &&
                                argIndex >= 0 && argIndex < format.Args.Count)
                            {
                                text.Append(Evaluate(format.Args[argIndex], resolve).AsText());
                                i = end;
                                continue;
                            }
                        }
                        text.Append(c);
                    }
                    return ValueBox.OfText(text.ToString());
                }
                default:
                    return new ValueBox();
            }
        }
    }
}
