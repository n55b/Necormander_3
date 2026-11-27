#if UNITY_EDITOR || LEVEL_WORKSHOP
using System;
using System.Globalization;

// Arithmetic typed into number boxes: + - * / (also × ÷), parentheses, signs and decimals.
// "-2.5+1.9" → -0.6, "(3+1)/2" → 2. Anything else, or an unfinished expression, is rejected.
internal static class WorkshopMath
{
    internal static bool TryEvaluate(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var reader = new Reader(text);
        try
        {
            value = reader.Expression();
            reader.SkipSpaces();
            return reader.AtEnd && !double.IsNaN(value) && !double.IsInfinity(value);
        }
        catch (FormatException) { return false; }
    }

    // True when the text is more than a plain number, so the result is worth showing.
    internal static bool IsFormula(string text) =>
        !string.IsNullOrWhiteSpace(text) && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private sealed class Reader
    {
        private readonly string text;
        private int at;
        internal Reader(string text) { this.text = text; }
        internal bool AtEnd => at >= text.Length;

        internal void SkipSpaces() { while (at < text.Length && char.IsWhiteSpace(text[at])) at++; }

        private bool Take(char c)
        {
            SkipSpaces();
            if (at < text.Length && text[at] == c) { at++; return true; }
            return false;
        }

        internal double Expression()
        {
            double result = Term();
            while (true)
            {
                if (Take('+')) result += Term();
                else if (Take('-')) result -= Term();
                else return result;
            }
        }

        private double Term()
        {
            double result = Factor();
            while (true)
            {
                if (Take('*') || Take('×')) result *= Factor();
                else if (Take('/') || Take('÷'))
                {
                    double divisor = Factor();
                    if (divisor == 0) throw new FormatException("0으로 나눌 수 없습니다.");
                    result /= divisor;
                }
                else return result;
            }
        }

        private double Factor()
        {
            if (Take('+')) return Factor();
            if (Take('-')) return -Factor();
            if (Take('('))
            {
                double inner = Expression();
                if (!Take(')')) throw new FormatException("괄호가 닫히지 않았습니다.");
                return inner;
            }
            SkipSpaces();
            int start = at;
            while (at < text.Length && (char.IsDigit(text[at]) || text[at] == '.')) at++;
            if (start == at) throw new FormatException("숫자가 필요합니다.");
            if (!double.TryParse(text.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                throw new FormatException("숫자를 읽을 수 없습니다.");
            return number;
        }
    }
}
#endif
