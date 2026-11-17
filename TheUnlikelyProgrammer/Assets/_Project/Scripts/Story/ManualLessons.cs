using System.Collections.Generic;
using Unlikely.Core;

namespace Unlikely.Story
{
    /// <summary>
    /// The Manual app's lessons. A lesson is unlocked by the GameState flag "lesson_{id}"
    /// (set by the floor's employee terminal tutorial). Floor 0's terminal teaches SELECT, FROM, WHERE, AND, OR.
    /// Later floors' lessons are added when the SQL engine supports them.
    /// </summary>
    public static class ManualLessons
    {
        public sealed class Lesson
        {
            public string id, title, body, example;
            public int floor;
            public Lesson(string id, int floor, string title, string body, string example)
            {
                this.id = id; this.floor = floor; this.title = title; this.body = body; this.example = example;
            }
            public bool Unlocked => (GameState.DevGiveManual && floor == 0) || GameState.Has("lesson_" + id);
        }

        public static readonly Lesson[] All =
        {
            new Lesson("tables", 0, "Tables",
                "A database is a pile of tables. A table is rows and columns, like a spreadsheet that never lets you colour anything in.\nEvery computer here lists its tables at the top of the screen.",
                "employees (id, name, department, floor)"),
            new Lesson("select", 0, "SELECT ... FROM",
                "SELECT picks the columns you want. FROM says which table they live in.\nUse * for every column. Separate several columns with commas.",
                "SELECT name, department FROM employees"),
            new Lesson("where", 0, "WHERE",
                "WHERE keeps only the rows that match a condition. Text goes in single quotes. Numbers don't.\nCompare with =  <>  <  >  <=  >=",
                "SELECT name FROM employees WHERE floor = 1"),
            new Lesson("and", 0, "AND",
                "AND joins two conditions. A row must pass both to stay.",
                "SELECT name FROM employees\nWHERE department = 'Reception' AND floor = 0"),
            new Lesson("or", 0, "OR",
                "OR joins two conditions. A row passes if either one is true.\nMixing AND with OR? Use brackets so the computer reads it the way you meant.",
                "SELECT name FROM employees\nWHERE (floor = 1 OR floor = 2) AND department = 'Administration'"),

            // taught by the Floor 1 terminal, for Human Resources
            new Lesson("like", 1, "LIKE",
                "LIKE matches a pattern instead of exact text.\n%  stands for anything (or nothing).\n_  stands for exactly one character.",
                "SELECT name FROM staff WHERE name LIKE 'S%'\nSELECT name FROM staff WHERE role LIKE '%Officer'"),
            new Lesson("in", 1, "IN",
                "IN checks a value against a list. It's a tidy way to write several ORs.",
                "SELECT name FROM staff\nWHERE department IN ('Security', 'People & Facilities')"),
            new Lesson("not", 1, "NOT",
                "NOT flips a condition: rows that DON'T match. Works in front of a condition, or with LIKE and IN.",
                "WHERE NOT floor = 2\nWHERE name NOT LIKE 'S%'\nWHERE department NOT IN ('Security')"),
            new Lesson("orderby", 1, "ORDER BY",
                "ORDER BY sorts the results, smallest/A first. Add DESC for biggest/Z first. It goes after WHERE.",
                "SELECT name, start_year FROM staff\nWHERE floor = 2\nORDER BY start_year DESC"),
            new Lesson("groupby", 1, "GROUP BY + COUNT",
                "GROUP BY squashes rows that share a value into one row. COUNT(*) says how many rows were squashed together.\nOnly show the grouped column and the count.",
                "SELECT department, COUNT(*) FROM staff\nGROUP BY department"),

            // taught by the Floor 2 terminal, for the Executive floor
            new Lesson("keys", 2, "Primary & foreign keys",
                "A PRIMARY KEY is a column that's different for every row (an id). A FOREIGN KEY is a column in another table that holds one of those ids: a pointer.\nemployees.dept_id points at departments.id.",
                "employees (id, name, dept_id)\ndepartments (id, dept_name, floor)"),
            new Lesson("join", 2, "JOIN ... ON",
                "JOIN glues two tables into one, wherever ON is true, usually the foreign key matching the primary key.\nWrite table.column when both tables have that column.",
                "SELECT employees.name, departments.dept_name\nFROM employees\nJOIN departments ON employees.dept_id = departments.id"),
            new Lesson("alias", 2, "Table aliases",
                "Give a table a short name right after it, then use that name everywhere in the query.",
                "SELECT e.name FROM employees e\nJOIN departments d ON e.dept_id = d.id\nWHERE d.floor = 3"),
            new Lesson("leftjoin", 2, "LEFT JOIN",
                "JOIN drops rows that have no match. LEFT JOIN keeps every row of the first table; the other table's columns are NULL where nothing matched.",
                "SELECT d.dept_name, e.name\nFROM departments d\nLEFT JOIN employees e ON e.dept_id = d.id"),
            new Lesson("limit", 2, "LIMIT",
                "LIMIT n keeps only the first n rows. After ORDER BY it means 'the top n'. It always goes last.",
                "SELECT name, start_year FROM staff\nORDER BY start_year DESC\nLIMIT 3"),

            // taught by the Floor 3 terminal (reception desk), for the server room
            new Lesson("insert", 3, "INSERT INTO ... VALUES",
                "INSERT adds new rows. Name the table, the columns in brackets, then VALUES with one bracket per row, in the same order. Text in quotes, numbers without.",
                "INSERT INTO visitors (id, name, host)\nVALUES (7, 'Sam Ortiz', 'Reception')"),
            new Lesson("insertselect", 3, "INSERT INTO ... SELECT",
                "Instead of typing the values, a SELECT can find them: every row it returns is copied into the table. The columns have to line up.",
                "INSERT INTO archive (id, name)\nSELECT id, name FROM visitors\nWHERE status = 'left'"),
            new Lesson("delete", 3, "DELETE FROM",
                "DELETE removes every row its WHERE matches. There is no undo.\nWithout a WHERE it removes EVERY row in the table. Do not run it unless you know exactly what you're doing.",
                "DELETE FROM visitors\nWHERE id = 7"),
        };

        public static List<Lesson> Unlocked()
        {
            var list = new List<Lesson>();
            foreach (var l in All) if (l.Unlocked) list.Add(l);
            return list;
        }
    }
}
