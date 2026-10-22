using System.Globalization;

namespace Game
{
    public enum ValueKind
    {
        None,
        Number,
        Text,
        Boolean,
    }

    public class ValueBox
    {
        public ValueKind Kind = ValueKind.None;
        public string Text = "";
        public double Number;
        public bool Boolean;

        public static ValueBox OfNumber(double value)
            => new ValueBox { Kind = ValueKind.Number, Number = value };

        public static ValueBox OfText(string value)
            => new ValueBox { Kind = ValueKind.Text, Text = value };

        public static ValueBox OfBoolean(bool value)
            => new ValueBox { Kind = ValueKind.Boolean, Boolean = value };

        public static ValueBox Copy(ValueBox value)
        {
            if (value == null)
                return new ValueBox();
            return new ValueBox { Kind = value.Kind, Text = value.Text, Number = value.Number, Boolean = value.Boolean };
        }

        public double AsNumber()
        {
            switch (Kind)
            {
                case ValueKind.Number:
                    return this.Number;
                case ValueKind.Text:
                    return double.TryParse(this.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;
                case ValueKind.Boolean:
                    return this.Boolean ? 1 : 0;
                default:
                    return 0;
            }
        }

        public string AsText()
        {
            switch (Kind)
            {
                case ValueKind.Number:
                    return this.Number.ToString("0.################", CultureInfo.InvariantCulture);
                case ValueKind.Text:
                    return this.Text;
                case ValueKind.Boolean:
                    return this.Boolean ? "true" : "false";
                default:
                    return "";
            }
        }

        public bool AsBoolean()
        {
            switch (Kind)
            {
                case ValueKind.Boolean:
                    return this.Boolean;
                case ValueKind.Number:
                    return this.Number != 0;
                case ValueKind.Text:
                    return this.Text.Length > 0;
                default:
                    return false;
            }
        }

        public bool IsNone()
        {
            return Kind == ValueKind.None;
        }
    }
}
