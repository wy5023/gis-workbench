namespace Workbench;

public record TodoItem(
    int Id,
    string Title,
    string? Notes,
    int Priority,
    string? DueDate,
    bool Done,
    string CreatedAt);

public record TodoInput(string? Title, string? Notes, int Priority, string? DueDate);

public record Course(
    int Id,
    string Name,
    string? Teacher,
    string? Location,
    int Weekday,
    string StartTime,
    string EndTime,
    string? Weeks,
    string Color);

public record CourseInput(
    string? Name,
    string? Teacher,
    string? Location,
    int Weekday,
    string? StartTime,
    string? EndTime,
    string? Weeks,
    string? Color);

public record LinkItem(int Id, string Title, string Url, string? Category);

public record LinkInput(string? Title, string? Url, string? Category);

public record LoginInput(string? Password);

public record TodoDoneInput(bool Done);
