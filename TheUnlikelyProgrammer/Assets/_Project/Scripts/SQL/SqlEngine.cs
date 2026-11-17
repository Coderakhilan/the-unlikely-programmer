using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Unlikely.SQL
{
    /// <summary>An in-memory table. Cells are double, string or null.</summary>
    public sealed class Table
    {
        public readonly string Name;
        public readonly string[] Columns;
        public readonly List<object[]> Rows = new List<object[]>();

        /// <summary>Other names the table answers to in "x.column" (its alias). Joined tables: alias -> column prefix.</summary>
        public readonly Dictionary<string, string> Prefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>True for the result of a JOIN: every column is stored as "prefix.column".</summary>
        public bool Qualified;

        public Table(string name, string[] columns)
        {
            Name = name;
            Columns = columns;
        }

        /// <summary>Column position, or -1. Accepts "column" and "table.column" / "alias.column".</summary>
        public int IndexOf(string column)
        {
            if (column == null) return -1;
            for (int i = 0; i < Columns.Length; i++)
                if (string.Equals(Columns[i], column, StringComparison.OrdinalIgnoreCase)) return i;

            int dot = column.IndexOf('.');
            if (dot > 0)
            {
                string prefix = column.Substring(0, dot), rest = column.Substring(dot + 1);
                if (!Qualified)
                    return Prefixes.ContainsKey(prefix) || string.Equals(prefix, Name, StringComparison.OrdinalIgnoreCase) ? IndexOf(rest) : -1;
                if (Prefixes.TryGetValue(prefix, out var real) && !string.Equals(real, prefix, StringComparison.OrdinalIgnoreCase))
                    return IndexOf(real + "." + rest);
                return -1;
            }

            if (!Qualified) return -1;
            // a bare name in a joined table: fine if exactly one of the tables has it
            int found = -1;
            for (int i = 0; i < Columns.Length; i++)
            {
                if (!Columns[i].EndsWith("." + column, StringComparison.OrdinalIgnoreCase)) continue;
                if (found >= 0)
                    throw new SqlException($"'{column}' is in more than one of the joined tables. Say which one, like {Columns[found]} or {Columns[i]}.");
                found = i;
            }
            return found;
        }

        /// <summary>How a column is labelled in results (joined tables drop the table prefix).</summary>
        public string Display(int index)
        {
            string c = Columns[index];
            int dot = Qualified ? c.IndexOf('.') : -1;
            return dot >= 0 ? c.Substring(dot + 1) : c;
        }
    }

    public sealed class Database
    {
        private readonly Dictionary<string, Table> tables = new Dictionary<string, Table>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> TableNames
        {
            get { foreach (var t in tables.Values) yield return t.Name; }
        }

        public IEnumerable<Table> Tables => tables.Values;

        public void Add(Table table) => tables[table.Name] = table;

        public Table Find(string name) => tables.TryGetValue(name, out var t) ? t : null;
    }

    public sealed class QueryResult
    {
        public readonly string[] Columns;
        public readonly List<object[]> Rows;

        public QueryResult(string[] columns, List<object[]> rows)
        {
            Columns = columns;
            Rows = rows;
        }

        public static string Format(object value)
        {
            if (value == null) return "NULL";
            if (value is double d)
            {
                if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15) return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
                return d.ToString("0.##", CultureInfo.InvariantCulture);
            }
            return value.ToString();
        }
    }

    /// <summary>
    /// Runs parsed queries against an in-memory Database: JOIN / LEFT JOIN ... ON, WHERE (AND/OR/NOT/LIKE/IN), GROUP BY with
    /// COUNT/SUM/AVG/MIN/MAX, ORDER BY, LIMIT. Text comparison ignores upper/lower case so beginners
    /// are not punished for typing 'reception'.
    /// </summary>
    public static class SqlEngine
    {
        private sealed class OutRow
        {
            public object[] Cells;
            public List<object[]> Source;  // the table row, or the whole group, it came from
        }

        public static QueryResult Execute(Database db, string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new SqlException("Type a query first.");

            string first = SqlParser.FirstWord(sql);
            if (first == "INSERT") return Insert(db, SqlParser.ParseInsert(sql));
            if (first == "DELETE") return Delete(db, SqlParser.ParseDelete(sql));
            return Select(db, SqlParser.Parse(sql));
        }

        /// <summary>True for statements that change the data (INSERT, DELETE) rather than just reading it.</summary>
        public static bool Changes(string sql)
        {
            string w = SqlParser.FirstWord(sql ?? "");
            return w == "INSERT" || w == "DELETE";
        }

        private static QueryResult WholeTable(Table t) => new QueryResult((string[])t.Columns.Clone(), new List<object[]>(t.Rows));

        private static Table Target(Database db, string name)
        {
            var t = db.Find(name);
            if (t == null) throw new SqlException($"There is no table called '{name}'. Tables here: {string.Join(", ", db.TableNames)}.");
            return t;
        }

        /// <summary>Adds rows, then shows the whole table so you can see them.</summary>
        private static QueryResult Insert(Database db, InsertStatement s)
        {
            var t = Target(db, s.Table);
            var map = new int[s.Columns.Count > 0 ? s.Columns.Count : t.Columns.Length];
            for (int i = 0; i < map.Length; i++)
            {
                map[i] = s.Columns.Count > 0 ? t.IndexOf(s.Columns[i]) : i;
                if (map[i] < 0) throw ColumnError(t, s.Columns[i], false);
            }
            var rows = new List<object[]>();
            if (s.Select != null)
            {
                var res = Select(db, s.Select);
                if (res.Columns.Length != map.Length)
                    throw new SqlException($"The SELECT gives {res.Columns.Length} column(s), but '{t.Name}' needs {map.Length} here. Pick matching columns.");
                rows.AddRange(res.Rows);
            }
            else
            {
                foreach (var v in s.Values)
                {
                    if (v.Length != map.Length)
                        throw new SqlException($"That row has {v.Length} value(s), but '{t.Name}' needs {map.Length}" +
                                               (s.Columns.Count == 0 ? $" ({string.Join(", ", t.Columns)})." : "."));
                    rows.Add(v);
                }
            }
            foreach (var v in rows)
            {
                var row = new object[t.Columns.Length];
                for (int i = 0; i < map.Length; i++) row[map[i]] = v[i];
                t.Rows.Add(row);
            }
            return WholeTable(t);
        }

        /// <summary>Removes the matching rows (all of them without WHERE), then shows what's left.</summary>
        private static QueryResult Delete(Database db, DeleteStatement s)
        {
            var t = Target(db, s.Table);
            if (s.Where != null) Validate(s.Where, t);
            t.Rows.RemoveAll(row => s.Where == null || Evaluate(s.Where, t, row));
            return WholeTable(t);
        }

        private static QueryResult Select(Database db, SelectStatement s)
        {

            Table table = db.Find(s.Table);
            if (table == null)
                throw new SqlException($"There is no table called '{s.Table}'. Tables here: {string.Join(", ", db.TableNames)}.");
            if (s.Joins.Count > 0) table = Join(db, table, s);
            else if (s.TableAlias != null)
            {
                var aliased = new Table(table.Name, table.Columns);
                aliased.Rows.AddRange(table.Rows);
                aliased.Prefixes[s.TableAlias] = table.Name;
                table = aliased;
            }

            // what to show
            var items = new List<SelectItem>();
            if (s.SelectAll) foreach (var c in table.Columns) items.Add(new SelectItem { Column = c });
            else items.AddRange(s.Items);
            foreach (var it in items) CheckItem(table, it);
            foreach (var g in s.GroupBy) if (table.IndexOf(g) < 0) throw ColumnError(table, g, false);
            if (s.Where != null) Validate(s.Where, table);

            // which rows
            var source = new List<object[]>();
            foreach (var row in table.Rows)
                if (s.Where == null || Evaluate(s.Where, table, row)) source.Add(row);

            bool grouped = s.GroupBy.Count > 0 || items.Exists(i => i.IsAggregate);
            var names = new string[items.Count];
            for (int i = 0; i < items.Count; i++)
                names[i] = items[i].IsAggregate || items[i].Alias != null ? items[i].Label : table.Display(table.IndexOf(items[i].Column));

            var output = new List<OutRow>();
            if (grouped)
            {
                foreach (var it in items)
                {
                    if (it.IsAggregate || s.GroupBy.Exists(g => table.IndexOf(g) == table.IndexOf(it.Column))) continue;
                    var agg = items.Find(x => x.IsAggregate);
                    throw new SqlException(s.GroupBy.Count == 0
                        ? $"'{it.Column}' can't sit next to {agg.Label} without GROUP BY {it.Column}. Add GROUP BY {it.Column}, or only select the count."
                        : $"'{it.Column}' isn't in the GROUP BY. With GROUP BY you can only show the grouped columns and counts/totals.");
                }

                var groups = new List<List<object[]>>();
                var index = new Dictionary<string, List<object[]>>();
                if (s.GroupBy.Count == 0) groups.Add(source);
                else
                {
                    foreach (var row in source)
                    {
                        var parts = new List<string>();
                        foreach (var g in s.GroupBy) parts.Add(QueryResult.Format(row[table.IndexOf(g)]).ToLowerInvariant());
                        string key = string.Join("\u001f", parts);
                        if (!index.TryGetValue(key, out var list)) { list = new List<object[]>(); index[key] = list; groups.Add(list); }
                        list.Add(row);
                    }
                }

                foreach (var g in groups)
                {
                    var cells = new object[items.Count];
                    for (int i = 0; i < items.Count; i++) cells[i] = ItemValue(items[i], table, g);
                    output.Add(new OutRow { Cells = cells, Source = g });
                }
            }
            else
            {
                foreach (var row in source)
                {
                    var cells = new object[items.Count];
                    for (int i = 0; i < items.Count; i++) cells[i] = row[table.IndexOf(items[i].Column)];
                    output.Add(new OutRow { Cells = cells, Source = new List<object[]> { row } });
                }
            }

            if (s.OrderBy.Count > 0)
            {
                var keys = new List<Func<OutRow, object>>();
                foreach (var o in s.OrderBy) keys.Add(OrderKey(o.Key, items, table, grouped, s));
                var order = new List<int>();
                for (int i = 0; i < output.Count; i++) order.Add(i);
                order.Sort((a, b) =>
                {
                    for (int k = 0; k < keys.Count; k++)
                    {
                        int c = CompareForSort(keys[k](output[a]), keys[k](output[b]));
                        if (s.OrderBy[k].Descending) c = -c;
                        if (c != 0) return c;
                    }
                    return a.CompareTo(b);   // keep the original order for ties
                });
                output = order.ConvertAll(i => output[i]);
            }

            var rows = new List<object[]>();
            foreach (var o in output)
            {
                if (s.Limit.HasValue && rows.Count >= s.Limit.Value) break;
                rows.Add(o.Cells);
            }
            return new QueryResult(names, rows);
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static bool SameColumn(Table table, string a, string b)
        {
            try { int i = table.IndexOf(a); return i >= 0 && i == table.IndexOf(b); }
            catch (SqlException) { return false; }
        }

        /// <summary>FROM a JOIN b ON ... [JOIN c ON ...]: one wide table whose columns are "alias.column".</summary>
        private static Table Join(Database db, Table first, SelectStatement s)
        {
            string firstPrefix = s.TableAlias ?? first.Name;
            var cols = new List<string>();
            foreach (var c in first.Columns) cols.Add(firstPrefix + "." + c);
            var current = new Table(first.Name, cols.ToArray()) { Qualified = true };
            current.Prefixes[firstPrefix] = firstPrefix;
            if (s.TableAlias != null) current.Prefixes[first.Name] = firstPrefix;
            foreach (var r in first.Rows) current.Rows.Add(r);

            foreach (var j in s.Joins)
            {
                var right = db.Find(j.Table);
                if (right == null)
                    throw new SqlException($"There is no table called '{j.Table}'. Tables here: {string.Join(", ", db.TableNames)}.");
                string prefix = j.Alias ?? right.Name;
                if (current.Prefixes.ContainsKey(prefix))
                    throw new SqlException($"Two tables are both called '{prefix}'. Give one a short name: JOIN {j.Table} AS x");
                var wide = new List<string>(current.Columns);
                foreach (var c in right.Columns) wide.Add(prefix + "." + c);
                var next = new Table(current.Name + "+" + right.Name, wide.ToArray()) { Qualified = true };
                foreach (var kv in current.Prefixes) next.Prefixes[kv.Key] = kv.Value;
                next.Prefixes[prefix] = prefix;
                if (j.Alias != null && !next.Prefixes.ContainsKey(right.Name)) next.Prefixes[right.Name] = prefix;

                Validate(j.On, next);
                foreach (var left in current.Rows)
                {
                    bool matched = false;
                    foreach (var r in right.Rows)
                    {
                        var row = new object[wide.Count];
                        left.CopyTo(row, 0);
                        r.CopyTo(row, left.Length);
                        if (!Evaluate(j.On, next, row)) continue;
                        next.Rows.Add(row);
                        matched = true;
                    }
                    if (!matched && j.Left)
                    {
                        var row = new object[wide.Count];
                        left.CopyTo(row, 0);   // the right-hand columns stay NULL
                        next.Rows.Add(row);
                    }
                }
                current = next;
            }
            return current;
        }

        private static void CheckItem(Table table, SelectItem it)
        {
            if (!it.IsAggregate) { if (table.IndexOf(it.Column) < 0) throw ColumnError(table, it.Column, false); return; }
            if (it.AggArg != "*" && table.IndexOf(it.AggArg) < 0) throw ColumnError(table, it.AggArg, false);
        }

        private static object ItemValue(SelectItem it, Table table, List<object[]> rows)
        {
            if (!it.IsAggregate) return rows.Count > 0 ? rows[0][table.IndexOf(it.Column)] : null;
            if (it.Aggregate == "COUNT")
            {
                if (it.AggArg == "*") return (double)rows.Count;
                int idx = table.IndexOf(it.AggArg), n = 0;
                foreach (var r in rows) if (r[idx] != null) n++;
                return (double)n;
            }
            int ci = table.IndexOf(it.AggArg);
            var values = new List<object>();
            foreach (var r in rows) if (r[ci] != null) values.Add(r[ci]);
            if (values.Count == 0) return null;
            if (it.Aggregate == "SUM" || it.Aggregate == "AVG")
            {
                double sum = 0;
                foreach (var v in values)
                {
                    if (!(v is double d)) throw new SqlException($"{it.Aggregate} only works on numbers, but '{it.AggArg}' holds text.");
                    sum += d;
                }
                return it.Aggregate == "SUM" ? sum : sum / values.Count;
            }
            object best = values[0];
            foreach (var v in values)
            {
                int c = CompareForSort(v, best);
                if (it.Aggregate == "MIN" ? c < 0 : c > 0) best = v;
            }
            return best;
        }

        private static Func<OutRow, object> OrderKey(SelectItem key, List<SelectItem> items, Table table, bool grouped, SelectStatement s)
        {
            // 1) something already shown: a name given with AS, a shown column, or the same aggregate
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                bool match = key.IsAggregate
                    ? it.IsAggregate && it.Aggregate == key.Aggregate && Same(it.AggArg, key.AggArg)
                    : Same(it.Alias, key.Column) || (!it.IsAggregate && it.Alias == null && (Same(it.Column, key.Column) || SameColumn(table, it.Column, key.Column)));
                if (match) { int idx = i; return o => o.Cells[idx]; }
            }
            // 2) an aggregate that isn't shown
            if (key.IsAggregate)
            {
                CheckItem(table, key);
                return o => ItemValue(key, table, o.Source);
            }
            // 3) a column of the table (in grouped queries it must be grouped)
            int col = table.IndexOf(key.Column);
            if (col < 0)
                throw new SqlException($"I can't sort by '{key.Column}': it isn't a column of '{table.Name}' or a name you gave with AS.");
            if (grouped && !s.GroupBy.Exists(g => Same(g, key.Column)))
                throw new SqlException($"With GROUP BY you can only sort by the grouped columns or by a count/total. '{key.Column}' isn't grouped.");
            return o => o.Source.Count > 0 ? o.Source[0][col] : null;
        }

        private static int CompareForSort(object a, object b)
        {
            if (a == null) return b == null ? 0 : -1;
            if (b == null) return 1;
            if (a is double da && b is double db) return da.CompareTo(db);
            return string.Compare(QueryResult.Format(a), QueryResult.Format(b), StringComparison.OrdinalIgnoreCase);
        }

        private static SqlException ColumnError(Table table, string column, bool inWhere)
        {
            string msg = $"There is no column called '{column}' in table '{table.Name}'. Columns: {string.Join(", ", table.Columns)}.";
            if (inWhere) msg += $" If '{column}' is text you are comparing against, put it in quotes, like '{column}'.";
            return new SqlException(msg);
        }

        private static void Validate(Expr e, Table table)
        {
            switch (e)
            {
                case LogicExpr l:
                    Validate(l.Left, table);
                    Validate(l.Right, table);
                    break;
                case NotExpr n:
                    Validate(n.Inner, table);
                    break;
                case CompareExpr c:
                    foreach (var o in new[] { c.Left, c.Right })
                        if (o.IsColumn && table.IndexOf(o.Column) < 0) throw ColumnError(table, o.Column, true);
                    break;
                case InExpr i:
                    if (i.Left.IsColumn && table.IndexOf(i.Left.Column) < 0) throw ColumnError(table, i.Left.Column, true);
                    foreach (var o in i.Values)
                        if (o.IsColumn && table.IndexOf(o.Column) < 0) throw ColumnError(table, o.Column, true);
                    break;
            }
        }

        private static bool Evaluate(Expr e, Table table, object[] row)
        {
            switch (e)
            {
                case LogicExpr l:
                    return l.IsAnd ? Evaluate(l.Left, table, row) && Evaluate(l.Right, table, row)
                                   : Evaluate(l.Left, table, row) || Evaluate(l.Right, table, row);
                case NotExpr n:
                    return !Evaluate(n.Inner, table, row);
                case InExpr i:
                {
                    var v = Value(i.Left, table, row);
                    foreach (var o in i.Values) if (Compare(v, "=", Value(o, table, row))) return true;
                    return false;
                }
                default:
                {
                    var c = (CompareExpr)e;
                    if (c.Op == "LIKE") return Like(Value(c.Left, table, row), QueryResult.Format(c.Right.Literal));
                    return Compare(Value(c.Left, table, row), c.Op, Value(c.Right, table, row));
                }
            }
        }

        /// <summary>SQL LIKE: % = any run of characters, _ = exactly one. Case-insensitive.</summary>
        public static bool Like(object value, string pattern)
        {
            if (value == null) return false;
            var re = new StringBuilder("^");
            foreach (char ch in pattern)
            {
                if (ch == '%') re.Append(".*");
                else if (ch == '_') re.Append('.');
                else re.Append(Regex.Escape(ch.ToString()));
            }
            re.Append('$');
            return Regex.IsMatch(QueryResult.Format(value), re.ToString(), RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        private static object Value(Operand o, Table table, object[] row) =>
            o.IsColumn ? row[table.IndexOf(o.Column)] : o.Literal;

        private static bool Compare(object a, string op, object b)
        {
            if (a == null || b == null) return false; // NULL never matches, like real SQL

            int cmp;
            if (a is double da && b is double db) cmp = da.CompareTo(db);
            else cmp = string.Compare(QueryResult.Format(a), QueryResult.Format(b), StringComparison.OrdinalIgnoreCase);

            switch (op)
            {
                case "=": return cmp == 0;
                case "!=":
                case "<>": return cmp != 0;
                case "<": return cmp < 0;
                case ">": return cmp > 0;
                case "<=": return cmp <= 0;
                case ">=": return cmp >= 0;
                default: return false;
            }
        }
    }
}
