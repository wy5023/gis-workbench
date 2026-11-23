using Microsoft.Data.Sqlite;

namespace Workbench.Data;

public sealed class Store
{
    private readonly string _connectionString;

    public Store(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        Initialize();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private void Initialize()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS todos (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                notes TEXT,
                priority INTEGER NOT NULL DEFAULT 1,
                due_date TEXT,
                done INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS courses (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                teacher TEXT,
                location TEXT,
                weekday INTEGER NOT NULL,
                start_time TEXT NOT NULL,
                end_time TEXT NOT NULL,
                weeks TEXT,
                color TEXT NOT NULL DEFAULT '#6C8CFF'
            );
            CREATE TABLE IF NOT EXISTS links (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                url TEXT NOT NULL,
                category TEXT,
                sort_order REAL NOT NULL DEFAULT 0
            );
            """;
        cmd.ExecuteNonQuery();

        if (Scalar(conn, "SELECT COUNT(*) FROM todos") == 0 &&
            Scalar(conn, "SELECT COUNT(*) FROM courses") == 0 &&
            Scalar(conn, "SELECT COUNT(*) FROM links") == 0)
        {
            Seed(conn);
        }
    }

    private static long Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static void Seed(SqliteConnection conn)
    {
        var today = DateTime.Today;
        var seeds = new (string Title, string Notes, int Priority, string? Due, bool Done)[]
        {
            ("完成《遥感影像处理》课程作业", "第三章：影像融合与分类，提交到学习通", 2, today.ToString("yyyy-MM-dd"), false),
            ("提交本周实习周报", "记录 Web GIS 模块的进展与问题", 1, today.ToString("yyyy-MM-dd"), false),
            ("整理个人工作台需求文档", "包含待办、课程表、常用链接三个模块", 0, null, true),
            ("预约图书馆研讨间", "周五下午小组讨论使用", 1, today.AddDays(2).ToString("yyyy-MM-dd"), false),
            ("复习空间数据库索引", "重点：R 树与网格索引", 0, today.AddDays(4).ToString("yyyy-MM-dd"), false),
        };
        foreach (var s in seeds)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO todos (title, notes, priority, due_date, done, created_at)
                VALUES ($t, $n, $p, $d, $done, $c);
                """;
            cmd.Parameters.AddWithValue("$t", s.Title);
            cmd.Parameters.AddWithValue("$n", (object?)s.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$p", s.Priority);
            cmd.Parameters.AddWithValue("$d", (object?)s.Due ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$done", s.Done ? 1 : 0);
            cmd.Parameters.AddWithValue("$c", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        var courses = new (string Name, string Teacher, string Location, int Weekday, string Start, string End, string Weeks, string Color)[]
        {
            ("遥感原理与应用", "王立新", "教三-204", 1, "08:00", "09:40", "1-16周", "#5B8DEF"),
            ("GIS 空间分析", "李雪", "教一-108", 2, "10:00", "11:40", "1-16周", "#8C7BF0"),
            ("数据库原理", "陈明", "教二-305", 3, "14:00", "15:40", "1-12周", "#22B8A6"),
            ("学术英语", "Sarah", "教三-101", 3, "16:00", "17:40", "1-16周", "#F0913A"),
            ("Web GIS 开发", "赵乾", "实验楼-402", 4, "08:00", "09:40", "3-16周", "#E0587A"),
            ("自然地理学", "周慧", "教三-101", 5, "10:00", "11:40", "1-16周", "#4FAE6F"),
        };
        foreach (var c in courses)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO courses (name, teacher, location, weekday, start_time, end_time, weeks, color)
                VALUES ($n, $t, $l, $w, $s, $e, $k, $c);
                """;
            cmd.Parameters.AddWithValue("$n", c.Name);
            cmd.Parameters.AddWithValue("$t", c.Teacher);
            cmd.Parameters.AddWithValue("$l", c.Location);
            cmd.Parameters.AddWithValue("$w", c.Weekday);
            cmd.Parameters.AddWithValue("$s", c.Start);
            cmd.Parameters.AddWithValue("$e", c.End);
            cmd.Parameters.AddWithValue("$k", c.Weeks);
            cmd.Parameters.AddWithValue("$c", c.Color);
            cmd.ExecuteNonQuery();
        }

        var links = new (string Title, string Url, string Category)[]
        {
            ("学习通", "https://i.chaoxing.com", "学习"),
            ("中国知网", "https://www.cnki.net", "学习"),
            ("学校教务系统", "https://jwxt.example.edu.cn", "校园"),
            ("GitHub", "https://github.com", "工具"),
            ("B 站", "https://www.bilibili.com", "娱乐"),
            ("中央气象台", "https://weather.cma.cn", "生活"),
        };
        var order = 0.0;
        foreach (var l in links)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO links (title, url, category, sort_order) VALUES ($t, $u, $c, $o);";
            cmd.Parameters.AddWithValue("$t", l.Title);
            cmd.Parameters.AddWithValue("$u", l.Url);
            cmd.Parameters.AddWithValue("$c", l.Category);
            cmd.Parameters.AddWithValue("$o", order);
            cmd.ExecuteNonQuery();
            order += 1;
        }
    }

    // ---------------- Todos ----------------

    public List<TodoItem> ListTodos()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, title, notes, priority, due_date, done, created_at
            FROM todos
            ORDER BY done ASC,
                     CASE WHEN due_date IS NULL OR due_date = '' THEN 1 ELSE 0 END,
                     due_date ASC,
                     priority DESC,
                     id DESC;
            """;
        using var reader = cmd.ExecuteReader();
        var list = new List<TodoItem>();
        while (reader.Read())
        {
            list.Add(new TodoItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5) == 1,
                reader.GetString(6)));
        }
        return list;
    }

    public TodoItem AddTodo(TodoInput input)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO todos (title, notes, priority, due_date, done, created_at)
            VALUES ($t, $n, $p, $d, 0, $c);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$t", input.Title!.Trim());
        cmd.Parameters.AddWithValue("$n", Nullable(input.Notes));
        cmd.Parameters.AddWithValue("$p", Math.Clamp(input.Priority, 0, 2));
        cmd.Parameters.AddWithValue("$d", Nullable(input.DueDate));
        cmd.Parameters.AddWithValue("$c", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return GetTodo(conn, id)!;
    }

    public TodoItem? UpdateTodo(int id, TodoInput input)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE todos
            SET title = $t, notes = $n, priority = $p, due_date = $d
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$t", input.Title!.Trim());
        cmd.Parameters.AddWithValue("$n", Nullable(input.Notes));
        cmd.Parameters.AddWithValue("$p", Math.Clamp(input.Priority, 0, 2));
        cmd.Parameters.AddWithValue("$d", Nullable(input.DueDate));
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 0 ? null : GetTodo(conn, id);
    }

    public TodoItem? SetTodoDone(int id, bool done)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE todos SET done = $d WHERE id = $id;";
        cmd.Parameters.AddWithValue("$d", done ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 0 ? null : GetTodo(conn, id);
    }

    public bool DeleteTodo(int id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM todos WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static TodoItem? GetTodo(SqliteConnection conn, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, notes, priority, due_date, done, created_at FROM todos WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new TodoItem(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt32(5) == 1,
            reader.GetString(6));
    }

    // ---------------- Courses ----------------

    public List<Course> ListCourses()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, teacher, location, weekday, start_time, end_time, weeks, color
            FROM courses ORDER BY weekday ASC, start_time ASC;
            """;
        using var reader = cmd.ExecuteReader();
        var list = new List<Course>();
        while (reader.Read())
        {
            list.Add(new Course(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8)));
        }
        return list;
    }

    public Course AddCourse(CourseInput input)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO courses (name, teacher, location, weekday, start_time, end_time, weeks, color)
            VALUES ($n, $t, $l, $w, $s, $e, $k, $c);
            SELECT last_insert_rowid();
            """;
        BindCourse(cmd, input);
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return GetCourse(conn, id)!;
    }

    public Course? UpdateCourse(int id, CourseInput input)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE courses
            SET name = $n, teacher = $t, location = $l, weekday = $w,
                start_time = $s, end_time = $e, weeks = $k, color = $c
            WHERE id = $id;
            """;
        BindCourse(cmd, input);
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 0 ? null : GetCourse(conn, id);
    }

    public bool DeleteCourse(int id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM courses WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static void BindCourse(SqliteCommand cmd, CourseInput input)
    {
        var start = string.IsNullOrWhiteSpace(input.StartTime) ? "08:00" : input.StartTime.Trim();
        var end = string.IsNullOrWhiteSpace(input.EndTime) ? "09:40" : input.EndTime.Trim();
        cmd.Parameters.AddWithValue("$n", input.Name!.Trim());
        cmd.Parameters.AddWithValue("$t", Nullable(input.Teacher));
        cmd.Parameters.AddWithValue("$l", Nullable(input.Location));
        cmd.Parameters.AddWithValue("$w", Math.Clamp(input.Weekday, 1, 7));
        cmd.Parameters.AddWithValue("$s", start);
        cmd.Parameters.AddWithValue("$e", end);
        cmd.Parameters.AddWithValue("$k", Nullable(input.Weeks));
        var color = string.IsNullOrWhiteSpace(input.Color) ? "#5B8DEF" : input.Color.Trim();
        if (color.Length != 7 || color[0] != '#' || !color.Skip(1).All(char.IsAsciiHexDigit))
        {
            color = "#5B8DEF";
        }
        cmd.Parameters.AddWithValue("$c", color);
    }

    private static Course? GetCourse(SqliteConnection conn, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, teacher, location, weekday, start_time, end_time, weeks, color
            FROM courses WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new Course(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt32(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8));
    }

    // ---------------- Links ----------------

    public List<LinkItem> ListLinks()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, url, category FROM links ORDER BY sort_order ASC, id ASC;";
        using var reader = cmd.ExecuteReader();
        var list = new List<LinkItem>();
        while (reader.Read())
        {
            list.Add(new LinkItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return list;
    }

    public LinkItem AddLink(LinkInput input)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO links (title, url, category, sort_order)
            VALUES ($t, $u, $c, (SELECT COALESCE(MAX(sort_order), 0) + 1 FROM links));
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$t", input.Title!.Trim());
        cmd.Parameters.AddWithValue("$u", input.Url!.Trim());
        cmd.Parameters.AddWithValue("$c", Nullable(input.Category));
        var id = Convert.ToInt32(cmd.ExecuteScalar());
        return GetLink(conn, id)!;
    }

    public LinkItem? UpdateLink(int id, LinkInput input)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE links SET title = $t, url = $u, category = $c WHERE id = $id;";
        cmd.Parameters.AddWithValue("$t", input.Title!.Trim());
        cmd.Parameters.AddWithValue("$u", input.Url!.Trim());
        cmd.Parameters.AddWithValue("$c", Nullable(input.Category));
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 0 ? null : GetLink(conn, id);
    }

    public bool DeleteLink(int id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM links WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static LinkItem? GetLink(SqliteConnection conn, int id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, url, category FROM links WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new LinkItem(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static object Nullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
}
