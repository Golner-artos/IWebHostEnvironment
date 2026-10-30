using System.Collections.Concurrent;
using System.Net;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddSingleton<IUserRepository, UserRepository>();

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/users"));

app.MapGet("/users", (IUserRepository repo) =>
{
    var rows = string.Concat(repo.GetAll().Select(u =>
        $"<tr><td>{H(u.Name)}</td><td>{u.Age}</td><td>" +
        $"<a href='/users/{u.Id}'>Подробнее</a> <a href='/users/{u.Id}/edit'>Редактировать</a> " +
        $"<form method='post' action='/users/{u.Id}/delete' style='display:inline'><button>Удалить</button></form>" +
        "</td></tr>"));

    if (rows == "") rows = "<tr><td colspan='3'>Пользователей пока нет</td></tr>";

    return Page("Пользователи",
        "<p><a href='/users/add'>+ Добавить пользователя</a></p>" +
        $"<table><tr><th>Имя</th><th>Возраст</th><th>Действия</th></tr>{rows}</table>");
});

app.MapGet("/users/add", () => Page("Добавить пользователя", Form("/users/add", "", "", null, "Добавить")));

app.MapPost("/users/add", async (HttpRequest req, IUserRepository repo) =>
{
    var f = await req.ReadFormAsync();
    var name = f["name"].ToString().Trim();
    var ageText = f["age"].ToString();

    if (!Validate(name, ageText, out var age, out var error))
        return Page("Добавить пользователя", Form("/users/add", name, ageText, error, "Добавить"), 400);

    repo.Add(new User { Name = name, Age = age });
    return Results.Redirect("/users");
});

app.MapGet("/users/{id}", (string id, IUserRepository repo) =>
{
    var problem = Resolve(id, repo, out var user);
    if (problem != null) return problem;

    return Page("Пользователь",
        $"<p><b>Id:</b> {user!.Id}</p><p><b>Имя:</b> {H(user.Name)}</p><p><b>Возраст:</b> {user.Age}</p>" +
        $"<a href='/users/{user.Id}/edit'>Редактировать</a> | <a href='/users'>← К списку</a>");
});

app.MapGet("/users/{id}/edit", (string id, IUserRepository repo) =>
{
    var problem = Resolve(id, repo, out var user);
    if (problem != null) return problem;

    return Page("Редактировать пользователя",
        Form($"/users/{user!.Id}/edit", user.Name, user.Age.ToString(), null, "Сохранить"));
});

app.MapPost("/users/{id}/edit", async (string id, HttpRequest req, IUserRepository repo) =>
{
    var problem = Resolve(id, repo, out var user);
    if (problem != null) return problem;

    var f = await req.ReadFormAsync();
    var name = f["name"].ToString().Trim();
    var ageText = f["age"].ToString();

    if (!Validate(name, ageText, out var age, out var error))
        return Page("Редактировать пользователя", Form($"/users/{user!.Id}/edit", name, ageText, error, "Сохранить"), 400);

    user!.Name = name;
    user.Age = age;
    repo.Update(user);
    return Results.Redirect("/users");
});

app.MapPost("/users/{id}/delete", (string id, IUserRepository repo) =>
{
    var problem = Resolve(id, repo, out var user);
    if (problem != null) return problem;

    repo.Delete(user!.Id);
    return Results.Redirect("/users");
});

app.Run();


static IResult? Resolve(string rawId, IUserRepository repo, out User? user)
{
    user = null;
    if (!Guid.TryParse(rawId, out var id))
        return Error(400, $"Неверный формат Id: '{rawId}'. Нужен GUID.");

    user = repo.GetById(id);
    return user is null ? Error(404, $"Пользователь с Id {id} не найден.") : null;
}

static bool Validate(string name, string ageText, out int age, out string? error)
{
    age = 0;
    error = null;
    if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        error = "Имя не может быть пустым (до 100 символов).";
    else if (!int.TryParse(ageText, out age) || age < 0 || age > 150)
        error = "Возраст должен быть числом от 0 до 150.";
    return error is null;
}

static string H(string? s) => WebUtility.HtmlEncode(s ?? ""); 

static IResult Error(int code, string message) =>
    Page("Ошибка", $"<p class='err'>{H(message)}</p><a href='/users'>← К списку</a>", code);

static string Form(string action, string name, string age, string? error, string button) =>
    (error is null ? "" : $"<p class='err'>{H(error)}</p>") +
    $"<form method='post' action='{action}'>" +
    $"<p>Имя: <input name='name' value='{H(name)}'></p>" +
    $"<p>Возраст: <input name='age' type='number' value='{H(age)}'></p>" +
    $"<button>{button}</button> <a href='/users'>Отмена</a></form>";

static IResult Page(string title, string body, int status = 200) =>
    Results.Content(
        "<!DOCTYPE html><html lang='ru'><head><meta charset='utf-8'>" +
        $"<title>{H(title)}</title>" +
        "<style>body{font-family:Arial;max-width:700px;margin:30px auto}" +
        "table{width:100%;border-collapse:collapse}td,th{border:1px solid #ccc;padding:6px;text-align:left}" +
        ".err{color:#b00}</style></head>" +
        $"<body><h2>{H(title)}</h2>{body}</body></html>",
        "text/html; charset=utf-8", statusCode: status);


public class User
{
    public Guid Id { get; init; } = Guid.NewGuid(); 
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

public interface IUserRepository
{
    void Add(User user);              
    bool Delete(Guid id);             
    User? GetById(Guid id);         
    bool Update(User user);           
    IEnumerable<User> GetAll();       
}

public class UserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<Guid, User> _users = new();

    public void Add(User user) => _users[user.Id] = user;
    public bool Delete(Guid id) => _users.TryRemove(id, out _);
    public User? GetById(Guid id) => _users.TryGetValue(id, out var u) ? u : null;
    public bool Update(User user) => _users.ContainsKey(user.Id) && (_users[user.Id] = user) != null;
    public IEnumerable<User> GetAll() => _users.Values.OrderBy(u => u.Name);
}