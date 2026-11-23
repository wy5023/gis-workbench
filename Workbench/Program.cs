using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Workbench;
using Workbench.Data;

var builder = WebApplication.CreateBuilder(args);

// 容器平台（Render 等）注入 PORT 环境变量；生产环境监听该端口，回退 10000
if (!builder.Environment.IsDevelopment())
{
    var port = Environment.GetEnvironmentVariable("PORT");
    if (string.IsNullOrWhiteSpace(port) || !int.TryParse(port, out var p))
    {
        p = 10000;
    }
    builder.WebHost.UseUrls($"http://0.0.0.0:{p}");
}

// 数据目录：部署时通过 DATA_DIR 指向持久化磁盘，本地默认 App_Data
var dataDir = Environment.GetEnvironmentVariable("DATA_DIR");
if (string.IsNullOrWhiteSpace(dataDir))
{
    dataDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
}
Directory.CreateDirectory(dataDir);

builder.Services.AddSingleton(new Store(Path.Combine(dataDir, "workbench.db")));

var password = Environment.GetEnvironmentVariable("WORKBENCH_PASSWORD")
               ?? builder.Configuration["Workbench:Password"];
if (string.IsNullOrWhiteSpace(password))
{
    password = "admin123";
}
var usingDefaultPassword = password == "admin123";

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "workbench.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect("/");
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// ---------------- 登录限流 ----------------
var gate = new LoginGate();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.MapGet("/api/auth/me", (HttpContext ctx) => Results.Ok(new
{
    authenticated = ctx.User.Identity?.IsAuthenticated == true,
    defaultPassword = usingDefaultPassword
})).AllowAnonymous();

app.MapPost("/api/auth/login", async (LoginInput input, HttpContext ctx) =>
{
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!gate.CanAttempt(ip))
    {
        return Results.Json(new { message = "尝试次数过多，请稍后再试" }, statusCode: StatusCodes.Status429TooManyRequests);
    }
    if (!PasswordMatches(input.Password ?? string.Empty, password))
    {
        gate.RecordFailure(ip);
        return Results.Json(new { message = "密码不正确" }, statusCode: StatusCodes.Status401Unauthorized);
    }
    gate.Reset(ip);
    var identity = new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "owner")],
        CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = true });
    return Results.Ok(new { ok = true });
}).AllowAnonymous();

app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { ok = true });
}).RequireAuthorization();

var api = app.MapGroup("/api").RequireAuthorization();

// ---------------- 待办 ----------------
api.MapGet("/todos", (Store store) => store.ListTodos());

api.MapPost("/todos", (TodoInput input, Store store) =>
{
    var error = ValidateTodo(input);
    return error is null
        ? Results.Ok(store.AddTodo(input))
        : Results.BadRequest(new { message = error });
});

api.MapPut("/todos/{id:int}", (int id, TodoInput input, Store store) =>
{
    var error = ValidateTodo(input);
    if (error is not null) return Results.BadRequest(new { message = error });
    var item = store.UpdateTodo(id, input);
    return item is null ? Results.NotFound(new { message = "待办不存在" }) : Results.Ok(item);
});

api.MapPatch("/todos/{id:int}/done", (int id, TodoDoneInput input, Store store) =>
{
    var item = store.SetTodoDone(id, input.Done);
    return item is null ? Results.NotFound(new { message = "待办不存在" }) : Results.Ok(item);
});

api.MapDelete("/todos/{id:int}", (int id, Store store) =>
    store.DeleteTodo(id) ? Results.Ok(new { ok = true }) : Results.NotFound(new { message = "待办不存在" }));

// ---------------- 课程 ----------------
api.MapGet("/courses", (Store store) => store.ListCourses());

api.MapPost("/courses", (CourseInput input, Store store) =>
{
    var error = ValidateCourse(input);
    return error is null
        ? Results.Ok(store.AddCourse(input))
        : Results.BadRequest(new { message = error });
});

api.MapPut("/courses/{id:int}", (int id, CourseInput input, Store store) =>
{
    var error = ValidateCourse(input);
    if (error is not null) return Results.BadRequest(new { message = error });
    var item = store.UpdateCourse(id, input);
    return item is null ? Results.NotFound(new { message = "课程不存在" }) : Results.Ok(item);
});

api.MapDelete("/courses/{id:int}", (int id, Store store) =>
    store.DeleteCourse(id) ? Results.Ok(new { ok = true }) : Results.NotFound(new { message = "课程不存在" }));

// ---------------- 常用链接 ----------------
api.MapGet("/links", (Store store) => store.ListLinks());

api.MapPost("/links", (LinkInput input, Store store) =>
{
    var error = ValidateLink(input);
    if (error is not null) return Results.BadRequest(new { message = error });
    input = NormalizeLink(input);
    return Results.Ok(store.AddLink(input));
});

api.MapPut("/links/{id:int}", (int id, LinkInput input, Store store) =>
{
    var error = ValidateLink(input);
    if (error is not null) return Results.BadRequest(new { message = error });
    input = NormalizeLink(input);
    var item = store.UpdateLink(id, input);
    return item is null ? Results.NotFound(new { message = "链接不存在" }) : Results.Ok(item);
});

api.MapDelete("/links/{id:int}", (int id, Store store) =>
    store.DeleteLink(id) ? Results.Ok(new { ok = true }) : Results.NotFound(new { message = "链接不存在" }));

app.Run();

static string? ValidateTodo(TodoInput input)
{
    if (string.IsNullOrWhiteSpace(input.Title)) return "请填写待办标题";
    if (input.Title.Length > 120) return "标题不能超过 120 个字符";
    if (input.Priority is < 0 or > 2) return "优先级无效";
    if (!string.IsNullOrWhiteSpace(input.DueDate) &&
        !DateOnly.TryParseExact(input.DueDate, "yyyy-MM-dd", out _))
    {
        return "日期格式应为 yyyy-MM-dd";
    }
    return null;
}

static string? ValidateCourse(CourseInput input)
{
    if (string.IsNullOrWhiteSpace(input.Name)) return "请填写课程名称";
    if (input.Weekday is < 1 or > 7) return "星期无效";
    if (!TimeOnly.TryParseExact(input.StartTime, "HH:mm", out _) ||
        !TimeOnly.TryParseExact(input.EndTime, "HH:mm", out _))
    {
        return "时间格式应为 HH:mm";
    }
    if (string.CompareOrdinal(input.StartTime, input.EndTime) >= 0) return "结束时间需晚于开始时间";
    return null;
}

static string? ValidateLink(LinkInput input)
{
    if (string.IsNullOrWhiteSpace(input.Title)) return "请填写链接名称";
    if (string.IsNullOrWhiteSpace(input.Url)) return "请填写链接地址";
    var url = input.Url.Trim();
    if (!Uri.TryCreate(url.Contains("://") ? url : "https://" + url, UriKind.Absolute, out var uri) ||
        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
        return "链接地址格式不正确";
    }
    return null;
}

static LinkInput NormalizeLink(LinkInput input)
{
    var url = input.Url!.Trim();
    if (!url.Contains("://")) url = "https://" + url;
    return input with { Url = url };
}

static bool PasswordMatches(string candidate, string expected)
{
    var a = SHA256.HashData(Encoding.UTF8.GetBytes(candidate));
    var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    return CryptographicOperations.FixedTimeEquals(a, b);
}

internal sealed class LoginGate
{
    private const int MaxFailures = 8;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, (int Failures, DateTime Start)> _state = new();
    private readonly Lock _lock = new();

    public bool CanAttempt(string key)
    {
        lock (_lock)
        {
            if (!_state.TryGetValue(key, out var entry)) return true;
            if (DateTime.UtcNow - entry.Start > Window)
            {
                _state.Remove(key);
                return true;
            }
            return entry.Failures < MaxFailures;
        }
    }

    public void RecordFailure(string key)
    {
        lock (_lock)
        {
            if (!_state.TryGetValue(key, out var entry) || DateTime.UtcNow - entry.Start > Window)
            {
                _state[key] = (1, DateTime.UtcNow);
                return;
            }
            _state[key] = (entry.Failures + 1, entry.Start);
        }
    }

    public void Reset(string key)
    {
        lock (_lock)
        {
            _state.Remove(key);
        }
    }
}
