using System;

namespace Game
{
    public delegate ValueBox VariableResolver(VariableValue variable);

    /// <summary>Reads one <c>field "…" of "…" in "…"</c> (Task 29 Part C). The runtime supplies
    /// this so a lookup can warn and suggest; evaluation itself stays pure.</summary>
    public delegate ValueBox FieldResolver(FieldValue field);

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
        public static ValueBox Evaluate(ValueNode node, VariableResolver resolve, FieldResolver fieldLookup = null)
        {
            switch (node)
            {
                case NumberValue number:
                    return ValueBox.OfNumber(number.Value);
                case TextValue text:
                    return ValueBox.OfText(text.Text);
                case BooleanValue boolean:
                    return ValueBox.OfBoolean(boolean.Value);
                case FieldValue field:
                    return fieldLookup == null ? new ValueBox() : fieldLookup(field);
                case VariableValue variable:
                {
                    if (resolve == null)
                        return new ValueBox();
                    return resolve(variable);
                }
                case RandomRangeValue range:
                {
                    double min = Evaluate(range.Min, resolve, fieldLookup).AsNumber();
                    double max = Evaluate(range.Max, resolve, fieldLookup).AsNumber();
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
                    double left = Evaluate(math.Left, resolve, fieldLookup).AsNumber();
                    double right = Evaluate(math.Right, resolve, fieldLookup).AsNumber();
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
                case ExpressionValue expression:
                {
                    ValueBox box = Expressions.Fold(expression, part => Evaluate(part, resolve, fieldLookup));
                    return box;
                }
                case HelperValue helper:
                    return EvaluateHelper(helper, resolve, fieldLookup);
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
                                text.Append(Evaluate(format.Args[argIndex], resolve, fieldLookup).AsText());
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

        /// <summary>
        /// The numeric helpers (Task 29 Part B). <c>clamp</c> takes its bounds either way round, so
        /// writing them high-to-low still clamps instead of swallowing every value; <c>round</c>
        /// rounds halves away from zero (2.5 reads 3, -2.5 reads -3) because an author rounding gold
        /// or damage expects the bigger number; and <c>10% of X</c> is plain X times 0.1, left
        /// unrounded so a caller can decide with <c>round</c>.
        /// </summary>
        private static ValueBox EvaluateHelper(HelperValue helper, VariableResolver resolve, FieldResolver fieldLookup)
        {
            double first = Evaluate(helper.First, resolve, fieldLookup).AsNumber();
            switch (helper.Kind)
            {
                case ValueHelper.Clamp:
                {
                    double low = Evaluate(helper.Second, resolve, fieldLookup).AsNumber();
                    double high = Evaluate(helper.Third, resolve, fieldLookup).AsNumber();
                    if (low > high)
                    {
                        double swap = low;
                        low = high;
                        high = swap;
                    }
                    if (first < low)
                        return ValueBox.OfNumber(low);
                    if (first > high)
                        return ValueBox.OfNumber(high);
                    return ValueBox.OfNumber(first);
                }
                case ValueHelper.Min:
                {
                    double other = Evaluate(helper.Second, resolve, fieldLookup).AsNumber();
                    return ValueBox.OfNumber(first < other ? first : other);
                }
                case ValueHelper.Max:
                {
                    double other = Evaluate(helper.Second, resolve, fieldLookup).AsNumber();
                    return ValueBox.OfNumber(first > other ? first : other);
                }
                case ValueHelper.Round:
                    return ValueBox.OfNumber(Math.Round(first, MidpointRounding.AwayFromZero));
                case ValueHelper.Floor:
                    return ValueBox.OfNumber(Math.Floor(first));
                case ValueHelper.Ceil:
                    return ValueBox.OfNumber(Math.Ceiling(first));
                case ValueHelper.Abs:
                    return ValueBox.OfNumber(Math.Abs(first));
                case ValueHelper.PercentOf:
                    return ValueBox.OfNumber(first * Evaluate(helper.Second, resolve, fieldLookup).AsNumber() / 100.0);
                default:
                    return ValueBox.OfNumber(0);
            }
        }
    }
}
