using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unlikely.SQL
{
    /// <summary>A mistake in the player's query. The message is written to be read by a beginner.</summary>
    public class SqlException : Exception
    {
        public SqlException(string message) : base(message) { }
    }

    public enum TokenType { Word, Number, String, Symbol, End }

    public readonly struct Token
    {
        public readonly TokenType Type;
        public readonly string Text;
        public readonly object Value; // double for numbers, string for strings

        public Token(TokenType type, string text, object value = null)
        {
            Type = type;
            Text = text;
            Value = value;
        }

        public string Upper => Text.ToUpperInvariant();
    }

    /// <summary>Splits a query string into tokens. Pure C#, no Unity dependencies.</summary>
    public static class SqlTokenizer
    {
        public static List<Token> Tokenize(string sql)
        {
            var tokens = new List<Token>();
            int i = 0;
            while (i < sql.Length)
            {
                char c = sql[i];

                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                    tokens.Add(new Token(TokenType.Word, sql.Substring(start, i - start)));
                    continue;
                }

                bool negativeNumber = c == '-' && i + 1 < sql.Length && char.IsDigit(sql[i + 1]) && PreviousAllowsNegative(tokens);
                if (char.IsDigit(c) || negativeNumber)
                {
                    int start = i;
                    i++;
                    while (i < sql.Length && (char.IsDigit(sql[i]) || sql[i] == '.')) i++;
                    string text = sql.Substring(start, i - start);
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                        throw new SqlException($"'{text}' doesn't look like a valid number.");
                    tokens.Add(new Token(TokenType.Number, text, number));
                    continue;
                }

                if (c == '\'' || c == '"')
                {
                    char quote = c;
                    i++;
                    var sb = new StringBuilder();
                    bool closed = false;
                    while (i < sql.Length)
                    {
                        if (sql[i] == quote)
                        {
                            if (i + 1 < sql.Length && sql[i + 1] == quote) { sb.Append(quote); i += 2; continue; }
                            closed = true;
                            i++;
                            break;
                        }
                        sb.Append(sql[i]);
                        i++;
                    }
                    if (!closed) throw new SqlException("You opened a quote but never closed it. Text values need a quote on both sides, like 'Reception'.");
                    tokens.Add(new Token(TokenType.String, sb.ToString(), sb.ToString()));
                    continue;
                }

                if (i + 1 < sql.Length)
                {
                    string two = sql.Substring(i, 2);
                    if (two == "<=" || two == ">=" || two == "!=" || two == "<>")
                    {
                        tokens.Add(new Token(TokenType.Symbol, two));
                        i += 2;
                        continue;
                    }
                    if (two == "==")
                        throw new SqlException("In SQL, one equals sign (=) compares two things. Use = instead of ==.");
                }

                if (",;()*=<>.".IndexOf(c) >= 0)
                {
                    tokens.Add(new Token(TokenType.Symbol, c.ToString()));
                    i++;
                    continue;
                }

                throw new SqlException($"I don't understand the character '{c}'.");
            }

            tokens.Add(new Token(TokenType.End, ""));
            return tokens;
        }

        private static bool PreviousAllowsNegative(List<Token> tokens)
        {
            if (tokens.Count == 0) return true;
            var prev = tokens[tokens.Count - 1];
            return prev.Type == TokenType.Symbol && prev.Text != ")";
        }
    }
}
