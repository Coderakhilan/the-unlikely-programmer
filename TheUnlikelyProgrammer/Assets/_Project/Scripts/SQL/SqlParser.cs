using System;
using System.Collections.Generic;

namespace Unlikely.SQL
{
    /// <summary>One thing after SELECT: a column, or an aggregate like COUNT(*) / SUM(amount), optionally "AS name".</summary>
    public sealed class SelectItem
    {
        public string Column;      // plain column (null for aggregates)
        public string Aggregate;   // COUNT, SUM, AVG, MIN, MAX (upper case) or null
        public string AggArg;      // column name, or "*" for COUNT(*)
        public string Alias;

        public bool IsAggregate => Aggregate != null;
        public string Label => Alias ?? (IsAggregate ? $"{Aggregate}({AggArg})" : Column);
    }

    public sealed class OrderItem
    {
        public SelectItem Key;     // a column/alias name (Column set) or an aggregate
        public bool Descending;
    }

    public sealed class SelectStatement
    {
        public bool SelectAll;
        public readonly List<SelectItem> Items = new List<SelectItem>();
        public string Table;
        public string TableAlias;
        public readonly List<JoinClause> Joins = new List<JoinClause>();
        public Expr Where;
        public readonly List<string> GroupBy = new List<string>();
        public readonly List<OrderItem> OrderBy = new List<OrderItem>();
        public int? Limit;

        /// <summary>Plain column names (kept for older callers).</summary>
        public List<string> Columns
        {
            get { var l = new List<string>(); foreach (var i in Items) if (!i.IsAggregate) l.Add(i.Column); return l; }
        }
    }

    /// <summary>JOIN other_table [alias] ON condition  (LEFT JOIN keeps left rows with no match).</summary>
    public sealed class JoinClause
    {
        public string Table;
        public string Alias;
        public bool Left;
        public Expr On;
    }

    /// <summary>INSERT INTO t [(cols)] VALUES (...), (...)   or   INSERT INTO t [(cols)] SELECT ...</summary>
    public sealed class InsertStatement
    {
        public string Table;
        public readonly List<string> Columns = new List<string>();   // empty = every column, in table order
        public readonly List<object[]> Values = new List<object[]>();
        public SelectStatement Select;                              // set for INSERT ... SELECT
    }

    /// <summary>DELETE FROM t [WHERE ...]</summary>
    public sealed class DeleteStatement
    {
        public string Table;
        public Expr Where;
    }

    public abstract class Expr { }

    public sealed class LogicExpr : Expr
    {
        public bool IsAnd;
        public Expr Left;
        public Expr Right;
    }

    public sealed class NotExpr : Expr
    {
        public Expr Inner;
    }

    public sealed class CompareExpr : Expr
    {
        public Operand Left;
        public string Op;          // = != <> < > <= >=  or LIKE
        public Operand Right;
    }

    public sealed class InExpr : Expr
    {
        public Operand Left;
        public readonly List<Operand> Values = new List<Operand>();
    }

    public sealed class Operand
    {
        public string Column;   // set when this operand is a column name
        public object Literal;  // double or string when it is a value
        public bool IsColumn => Column != null;
    }

    /// <summary>
    /// Recursive-descent parser for the SQL taught in the game:
    /// SELECT cols | * | COUNT(*) ... [AS alias] FROM table
    /// [WHERE conditions with AND / OR / NOT / brackets, =, &lt;&gt;, &lt;, &gt;, LIKE, IN (...)]
    /// [JOIN table [alias] ON a.col = b.col ...] (also INNER JOIN / LEFT JOIN; columns may be written table.column)
    /// [GROUP BY cols] [ORDER BY col [ASC|DESC], ...] [LIMIT n]
    /// </summary>
    public sealed class SqlParser
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "LIKE", "IN", "GROUP", "ORDER", "BY", "ASC", "DESC", "LIMIT", "AS",
            "JOIN", "ON", "INNER", "LEFT", "OUTER", "INSERT", "INTO", "VALUES", "DELETE"
        };

        private static readonly HashSet<string> Aggregates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "COUNT", "SUM", "AVG", "MIN", "MAX"
        };

        private static readonly HashSet<string> CompareOps = new HashSet<string> { "=", "!=", "<>", "<", ">", "<=", ">=" };

        private readonly List<Token> t;
        private int p;

        private SqlParser(List<Token> tokens) { t = tokens; }

        public static SelectStatement Parse(string sql) => new SqlParser(SqlTokenizer.Tokenize(sql)).ParseSelect();

        /// <summary>The first word of a statement, upper case ("SELECT", "INSERT", "DELETE"...), or "".</summary>
        public static string FirstWord(string sql)
        {
            var toks = SqlTokenizer.Tokenize(sql);
            return toks.Count > 0 && toks[0].Type == TokenType.Word ? toks[0].Upper : "";
        }

        public static InsertStatement ParseInsert(string sql) => new SqlParser(SqlTokenizer.Tokenize(sql)).DoInsert();

        public static DeleteStatement ParseDelete(string sql) => new SqlParser(SqlTokenizer.Tokenize(sql)).DoDelete();

        private InsertStatement DoInsert()
        {
            p++;   // INSERT
            if (!IsWord("INTO")) throw new SqlException("It's INSERT INTO, then the table. Example: INSERT INTO visitors VALUES (4, 'Sam')");
            p++;
            if (!IsName(Cur)) throw new SqlException("After INSERT INTO, give the name of the table.");
            var s = new InsertStatement { Table = Cur.Text };
            p++;
            if (IsSymbol("("))
            {
                p++;
                while (true)
                {
                    if (!IsName(Cur)) throw new SqlException($"Inside the brackets after the table, list column names. I found '{Describe(Cur)}'.");
                    s.Columns.Add(Cur.Text);
                    p++;
                    if (IsSymbol(",")) { p++; continue; }
                    break;
                }
                if (!IsSymbol(")")) throw new SqlException("The column list was opened with ( but never closed with ).");
                p++;
            }
            if (IsWord("SELECT"))
            {
                s.Select = ParseSelect();
                return s;
            }
            if (!IsWord("VALUES")) throw new SqlException("After the table, write VALUES (...) with the new row, or a SELECT that finds the rows to copy.");
            p++;
            while (true)
            {
                if (!IsSymbol("(")) throw new SqlException("Each new row goes in brackets: VALUES (1, 'text', 2)");
                p++;
                var row = new List<object>();
                while (true)
                {
                    if (Cur.Type == TokenType.Number || Cur.Type == TokenType.String) { row.Add(Cur.Value); p++; }
                    else if (IsWord("NULL")) { row.Add(null); p++; }
                    else throw new SqlException($"Values are numbers or 'text in quotes'. I found '{Describe(Cur)}'.");
                    if (IsSymbol(",")) { p++; continue; }
                    break;
                }
                if (!IsSymbol(")")) throw new SqlException("A row of VALUES was opened with ( but never closed with ). Is a comma missing?");
                p++;
                s.Values.Add(row.ToArray());
                if (IsSymbol(",")) { p++; continue; }
                break;
            }
            if (IsSymbol(";")) p++;
            if (Cur.Type != TokenType.End) throw new SqlException($"I didn't expect '{Describe(Cur)}' after the VALUES.");
            return s;
        }

        private DeleteStatement DoDelete()
        {
            p++;   // DELETE
            if (!IsWord("FROM")) throw new SqlException("It's DELETE FROM, then the table. Example: DELETE FROM visitors WHERE id = 3");
            p++;
            if (!IsName(Cur)) throw new SqlException("After DELETE FROM, give the name of the table.");
            var s = new DeleteStatement { Table = Cur.Text };
            p++;
            if (IsWord("WHERE")) { p++; s.Where = ParseOr(); }
            if (IsSymbol(";")) p++;
            if (Cur.Type != TokenType.End) throw new SqlException($"I didn't expect '{Describe(Cur)}' here. DELETE FROM table WHERE condition.");
            return s;
        }

        private Token Cur => t[p];
        private Token Next => p + 1 < t.Count ? t[p + 1] : t[t.Count - 1];
        private bool IsWord(string keyword) => Cur.Type == TokenType.Word && Cur.Upper == keyword;
        private bool IsSymbol(string s) => Cur.Type == TokenType.Symbol && Cur.Text == s;
        private static bool IsName(Token tok) => tok.Type == TokenType.Word && !Keywords.Contains(tok.Text);

        private static string Describe(Token tok) => tok.Type == TokenType.End ? "the end of the query" : tok.Text;

        private SelectStatement ParseSelect()
        {
            if (!IsWord("SELECT"))
                throw new SqlException("A query starts with SELECT. Try: SELECT * FROM tablename");
            p++;

            var s = new SelectStatement();
            if (IsSymbol("*"))
            {
                s.SelectAll = true;
                p++;
            }
            else
            {
                if (IsWord("FROM") || Cur.Type == TokenType.End)
                    throw new SqlException("After SELECT, say which columns you want (or use * for all of them).");
                while (true)
                {
                    s.Items.Add(ParseSelectItem(true));
                    if (IsSymbol(",")) { p++; continue; }
                    break;
                }
            }

            if (!IsWord("FROM"))
            {
                if (!s.SelectAll && IsName(Cur))
                    throw new SqlException($"Did you forget a comma between column names? I found '{t[p - 1].Text}' followed by '{Cur.Text}'.");
                throw new SqlException("I expected FROM next, to say which table to read. Example: SELECT name FROM employees");
            }
            p++;

            if (!IsName(Cur))
                throw new SqlException("After FROM, give me the name of a table.");
            s.Table = Cur.Text;
            p++;
            s.TableAlias = ParseAlias();

            while (IsWord("JOIN") || IsWord("INNER") || IsWord("LEFT"))
            {
                var j = new JoinClause();
                if (IsWord("LEFT")) { j.Left = true; p++; if (IsWord("OUTER")) p++; }
                else if (IsWord("INNER")) p++;
                if (!IsWord("JOIN")) throw new SqlException("Expected JOIN here. Example: FROM staff JOIN departments ON staff.dept_id = departments.id");
                p++;
                if (!IsName(Cur)) throw new SqlException("After JOIN, give the name of the table to join.");
                j.Table = Cur.Text;
                p++;
                j.Alias = ParseAlias();
                if (!IsWord("ON"))
                    throw new SqlException($"A JOIN needs ON to say how the rows match up. Example: JOIN {j.Table} ON {s.TableAlias ?? s.Table}.some_id = {j.Alias ?? j.Table}.id");
                p++;
                j.On = ParseOr();
                s.Joins.Add(j);
            }

            if (IsWord("WHERE"))
            {
                p++;
                s.Where = ParseOr();
            }

            if (IsWord("GROUP"))
            {
                p++;
                if (!IsWord("BY")) throw new SqlException("It's GROUP BY, two words. Example: GROUP BY department");
                p++;
                while (true)
                {
                    if (!IsName(Cur)) throw new SqlException($"After GROUP BY, name the column to group on. I found '{Describe(Cur)}'.");
                    s.GroupBy.Add(ParseName());
                    if (IsSymbol(",")) { p++; continue; }
                    break;
                }
            }

            if (IsWord("ORDER"))
            {
                p++;
                if (!IsWord("BY")) throw new SqlException("It's ORDER BY, two words. Example: ORDER BY name");
                p++;
                while (true)
                {
                    var key = ParseSelectItem(false);
                    bool desc = false;
                    if (IsWord("DESC")) { desc = true; p++; }
                    else if (IsWord("ASC")) p++;
                    s.OrderBy.Add(new OrderItem { Key = key, Descending = desc });
                    if (IsSymbol(",")) { p++; continue; }
                    break;
                }
            }

            if (IsWord("LIMIT"))
            {
                p++;
                if (Cur.Type != TokenType.Number) throw new SqlException("LIMIT needs a number, like LIMIT 5.");
                s.Limit = Math.Max(0, (int)(double)Cur.Value);
                p++;
            }

            if (IsSymbol(";")) p++;
            if (Cur.Type != TokenType.End)
            {
                if (IsWord("WHERE") || IsWord("GROUP") || IsWord("ORDER") || IsWord("LIMIT") || IsWord("JOIN") || IsWord("LEFT") || IsWord("INNER"))
                    throw new SqlException($"'{Cur.Text}' is in the wrong place. The order is: SELECT ... FROM ... JOIN ... ON ... WHERE ... GROUP BY ... ORDER BY ... LIMIT.");
                throw new SqlException($"I didn't expect '{Describe(Cur)}' here. Check your spelling, or whether something is missing before it.");
            }
            return s;
        }

        private SelectItem ParseSelectItem(bool allowAlias)
        {
            SelectItem item;
            if (Cur.Type == TokenType.Word && Aggregates.Contains(Cur.Text) && Next.Type == TokenType.Symbol && Next.Text == "(")
            {
                string fn = Cur.Upper;
                p += 2;
                string arg;
                if (IsSymbol("*"))
                {
                    if (fn != "COUNT") throw new SqlException($"{fn}(*) doesn't work. Give {fn} a column, like {fn}(amount).");
                    arg = "*";
                    p++;
                }
                else if (IsName(Cur)) arg = ParseName();
                else throw new SqlException($"Inside {fn}( ) put a column name{(fn == "COUNT" ? " or *" : "")}.");
                if (!IsSymbol(")")) throw new SqlException($"{fn}( was opened but never closed with ).");
                p++;
                item = new SelectItem { Aggregate = fn, AggArg = arg };
            }
            else if (IsName(Cur))
            {
                item = new SelectItem { Column = ParseName() };
            }
            else throw new SqlException($"I expected a column name here but found '{Describe(Cur)}'.");

            if (allowAlias)
            {
                if (IsWord("AS"))
                {
                    p++;
                    if (!IsName(Cur)) throw new SqlException("After AS, give the new column name, like AS total.");
                    item.Alias = Cur.Text;
                    p++;
                }
            }
            return item;
        }

        private Expr ParseOr()
        {
            Expr left = ParseAnd();
            while (IsWord("OR"))
            {
                p++;
                left = new LogicExpr { IsAnd = false, Left = left, Right = ParseAnd() };
            }
            return left;
        }

        private Expr ParseAnd()
        {
            Expr left = ParseNot();
            while (IsWord("AND"))
            {
                p++;
                left = new LogicExpr { IsAnd = true, Left = left, Right = ParseNot() };
            }
            return left;
        }

        private Expr ParseNot()
        {
            if (IsWord("NOT"))
            {
                p++;
                return new NotExpr { Inner = ParseNot() };
            }
            return ParsePrimary();
        }

        private Expr ParsePrimary()
        {
            if (IsSymbol("("))
            {
                p++;
                Expr inner = ParseOr();
                if (!IsSymbol(")"))
                    throw new SqlException("A bracket ( was opened but never closed with ).");
                p++;
                return inner;
            }

            Operand left = ParseOperand();

            bool negate = false;
            if (IsWord("NOT")) { negate = true; p++; }

            Expr e;
            if (IsWord("LIKE"))
            {
                p++;
                var pattern = ParseOperand();
                if (pattern.IsColumn) throw new SqlException("LIKE needs a text pattern in quotes, like LIKE 'A%'.");
                e = new CompareExpr { Left = left, Op = "LIKE", Right = pattern };
            }
            else if (IsWord("IN"))
            {
                p++;
                if (!IsSymbol("(")) throw new SqlException("IN needs a list in brackets, like IN ('Accounts', 'Records').");
                p++;
                var inExpr = new InExpr { Left = left };
                while (true)
                {
                    inExpr.Values.Add(ParseOperand());
                    if (IsSymbol(",")) { p++; continue; }
                    break;
                }
                if (!IsSymbol(")")) throw new SqlException("The IN list was opened with ( but never closed with ).");
                p++;
                e = inExpr;
            }
            else
            {
                if (negate) throw new SqlException("NOT goes before a whole condition (NOT floor = 1) or before LIKE / IN (name NOT LIKE 'A%').");
                if (!(Cur.Type == TokenType.Symbol && CompareOps.Contains(Cur.Text)))
                    throw new SqlException("A condition needs a comparison such as = or > between two things. Example: WHERE department = 'Reception'");
                string op = Cur.Text;
                p++;
                Operand right = ParseOperand();
                return new CompareExpr { Left = left, Op = op, Right = right };
            }
            return negate ? new NotExpr { Inner = e } : e;
        }

        /// <summary>A column name, possibly written table.column (or alias.column).</summary>
        private string ParseName()
        {
            string name = Cur.Text;
            p++;
            if (IsSymbol("."))
            {
                p++;
                if (!IsName(Cur)) throw new SqlException($"After '{name}.' put a column name, like {name}.id");
                name += "." + Cur.Text;
                p++;
            }
            return name;
        }

        /// <summary>Optional short name for a table: FROM staff s, or FROM staff AS s.</summary>
        private string ParseAlias()
        {
            if (IsWord("AS"))
            {
                p++;
                if (!IsName(Cur)) throw new SqlException("After AS, give the table a short name, like AS s.");
                return t[p++].Text;
            }
            if (IsName(Cur)) return t[p++].Text;
            return null;
        }

        private Operand ParseOperand()
        {
            var tok = Cur;
            if (IsName(tok)) return new Operand { Column = ParseName() };
            if (tok.Type == TokenType.Number || tok.Type == TokenType.String) { p++; return new Operand { Literal = tok.Value }; }
            throw new SqlException($"I expected a column name or a value here but found '{Describe(tok)}'.");
        }
    }
}
