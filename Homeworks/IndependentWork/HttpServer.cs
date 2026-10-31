using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IndependentWork;

public class HttpServer
{
    private HttpListener server = new HttpListener();
    private string path = "/";
    private string folder = Path.Combine(AppContext.BaseDirectory, "wwwroot");

    private List<Ticket> tickets = new List<Ticket>();
    private int nextId = 1;

    public void Start()
    {
        string settingsFile = Path.Combine(AppContext.BaseDirectory, "settings.json");
        string json = File.ReadAllText(settingsFile);

        using JsonDocument document = JsonDocument.Parse(json);
        var settings = document.RootElement.GetProperty("Server");

        string host = settings.GetProperty("host").GetString()!;
        string port = settings.GetProperty("port").GetString()!;
        path = "/" + settings.GetProperty("path").GetString()!.Trim('/') + "/";
        if (path == "//") path = "/";

        string address = $"http://{host}:{port}{path}";
        server.Prefixes.Add(address);
        server.Start();
        Console.WriteLine($"Сервер запущен: {address}");
    }

    public async Task ListenAsync()
    {
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await server.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                break;
            }

            try
            {
                await HandleRequestAsync(context);
            }
            catch (Exception error)
            {
                Console.WriteLine(error.Message);
                await SendResponseAsync(context.Response, 500, "Внутренняя ошибка сервера.");
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    public void Stop()
    {
        server.Stop();
        Console.WriteLine("Сервер остановлен.");
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        string url = context.Request.Url!.AbsolutePath;
        string route = url;

        if (path != "/" && url.StartsWith(path))
            route = "/" + url.Substring(path.Length);

        route = route.TrimEnd('/');
        if (route == "") route = "/";

        string method = context.Request.HttpMethod;

        if (route == "/tickets" && method == "GET")
            await GetTicketsAsync(context);
        else if (route == "/tickets/details" && method == "GET")
            await GetTicketDetailsAsync(context);
        else if (route == "/tickets/create" && method == "POST")
            await CreateTicketAsync(context);
        else if (route == "/tickets/update" && method == "PUT")
            await UpdateTicketAsync(context);
        else if (route.StartsWith("/tickets"))
            await SendResponseAsync(context.Response, 404, "Маршрут не найден.");
        else
            await SendFileAsync(context);
    }

    // GET /tickets?category=network&status=open
    private async Task GetTicketsAsync(HttpListenerContext context)
    {
        string? category = context.Request.QueryString["category"];
        string? status = context.Request.QueryString["status"];

        if (category != null && category != "" && !IsCategoryValid(category))
        {
            await SendResponseAsync(context.Response, 400, "Некорректная категория.");
            return;
        }

        if (status != null && status != "" && !IsStatusValid(status))
        {
            await SendResponseAsync(context.Response, 400, "Некорректный статус.");
            return;
        }

        IEnumerable<Ticket> result = tickets;
        if (!string.IsNullOrEmpty(category))
            result = result.Where(ticket => ticket.Category == category);
        if (!string.IsNullOrEmpty(status))
            result = result.Where(ticket => ticket.Status == status);

        string html = "<h1>Обращения в поддержку</h1>";
        html += $"<form method='get' action='{path}tickets'>Категория: " +
                "<select name='category'><option value=''>Все</option><option>network</option>" +
                "<option>software</option><option>hardware</option></select> Статус: " +
                "<select name='status'><option value=''>Все</option><option>open</option>" +
                "<option>closed</option></select> <button>Фильтровать</button></form>";
        html += "<h2>Список обращений</h2>";

        html += "<table border='1'>";
        html += "<tr>";
        html += "<th>Id</th>";
        html += "<th>Тема</th>";
        html += "<th>Категория</th>";
        html += "<th>Статус</th>";
        html += "</tr>";

        foreach (Ticket ticket in result)
        {
            html += "<tr>";
            html += $"<td>{ticket.Id}</td>";
            html += $"<td><a href='{path}tickets/details?id={ticket.Id}'>{Encode(ticket.Subject)}</a></td>";
            html += $"<td>{ticket.Category}</td>";
            html += $"<td>{ticket.Status}</td>";
            html += "</tr>";
        }

        html += "</table>";

        html += $"</table><h2>Создать обращение</h2><form method='post' action='{path}tickets/create'>" +
                "Тема: <input name='subject' minlength='5' maxlength='100' required> " +
                "Категория: <select name='category'><option>network</option><option>software</option>" +
                "<option>hardware</option></select> <button>Создать</button></form>";

        await SendResponseAsync(context.Response, 200, Page(html), "text/html; charset=utf-8");
    }

    // GET /tickets/details?id=1
    private async Task GetTicketDetailsAsync(HttpListenerContext context)
    {
        if (!int.TryParse(context.Request.QueryString["id"], out int id) || id <= 0)
        {
            await SendResponseAsync(context.Response, 400, "Некорректный Id.");
            return;
        }

        Ticket? ticket = tickets.FirstOrDefault(item => item.Id == id);
        if (ticket == null)
        {
            await SendResponseAsync(context.Response, 404, "Обращение не найдено.");
            return;
        }

        string html = $"<h1>Обращение №{ticket.Id}</h1>" +
                      $"<p>Тема: {Encode(ticket.Subject)}</p><p>Категория: {ticket.Category}</p>" +
                      $"<p>Статус: {ticket.Status}</p><a href='{path}tickets'>Назад к списку</a>";
        await SendResponseAsync(context.Response, 200, Page(html), "text/html; charset=utf-8");
    }

    // POST /tickets/create
    private async Task CreateTicketAsync(HttpListenerContext context)
    {
        Dictionary<string, string> form = await ReadFormAsync(context.Request);
        string subject = form.GetValueOrDefault("subject", "").Trim();
        string category = form.GetValueOrDefault("category", "");

        if (subject.Length < 5 || subject.Length > 100 || !IsCategoryValid(category))
        {
            await SendResponseAsync(context.Response, 400, "Проверьте тему (5-100 символов) и категорию.");
            return;
        }

        Ticket ticket = new Ticket
        {
            Id = nextId++,
            Subject = subject,
            Category = category,
            Status = "open"
        };
        tickets.Add(ticket);

        context.Response.StatusCode = 303;
        context.Response.RedirectLocation = $"{path}tickets/details?id={ticket.Id}";
        context.Response.ContentLength64 = 0;
    }

    // PUT /tickets/update?id=1
    private async Task UpdateTicketAsync(HttpListenerContext context)
    {
        if (!int.TryParse(context.Request.QueryString["id"], out int id) || id <= 0)
        {
            await SendResponseAsync(context.Response, 400, "Некорректный Id.");
            return;
        }

        Ticket? ticket = tickets.FirstOrDefault(item => item.Id == id);
        if (ticket == null)
        {
            await SendResponseAsync(context.Response, 404, "Обращение не найдено.");
            return;
        }

        Dictionary<string, string> form = await ReadFormAsync(context.Request);
        string subject = form.GetValueOrDefault("subject", "").Trim();
        string category = form.GetValueOrDefault("category", "");
        string status = form.GetValueOrDefault("status", "");

        if (subject.Length < 5 || subject.Length > 100 ||
            !IsCategoryValid(category) || !IsStatusValid(status))
        {
            await SendResponseAsync(context.Response, 400, "Проверьте тему, категорию и статус.");
            return;
        }

        ticket.Subject = subject;
        ticket.Category = category;
        ticket.Status = status;

        context.Response.StatusCode = 204;
        context.Response.ContentLength64 = 0;
    }

    private static async Task<Dictionary<string, string>> ReadFormAsync(HttpListenerRequest request)
    {
        using StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding);
        string body = await reader.ReadToEndAsync();
        Dictionary<string, string> form = new Dictionary<string, string>();

        foreach (string part in body.Split('&'))
        {
            string[] pair = part.Split('=', 2);
            string key = WebUtility.UrlDecode(pair[0]);
            string value = pair.Length > 1 ? WebUtility.UrlDecode(pair[1]) : "";
            form[key] = value;
        }
        return form;
    }

    private static bool IsCategoryValid(string value)
    {
        return value == "network" || value == "software" || value == "hardware";
    }

    private static bool IsStatusValid(string value)
    {
        return value == "open" || value == "closed";
    }

    private static string Encode(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private static string Page(string content)
    {
        return "<!DOCTYPE html><html lang='ru'><head><meta charset='UTF-8'><title>Поддержка</title></head>" +
               "<body style='font-family:Arial;margin:30px'>" + content + "</body></html>";
    }

    private static async Task SendResponseAsync(HttpListenerResponse response, int status, string text,
        string contentType = "text/plain; charset=utf-8")
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        response.StatusCode = status;
        response.ContentType = contentType;
        response.ContentLength64 = data.Length;
        await response.OutputStream.WriteAsync(data, 0, data.Length);
    }

    // Если запрос не относится к обращениям, отдаём файл из wwwroot.
    private async Task SendFileAsync(HttpListenerContext context)
    {
        if (context.Request.HttpMethod != "GET")
        {
            await SendResponseAsync(context.Response, 404, "Страница не найдена.");
            return;
        }

        string fileName = context.Request.Url!.AbsolutePath;
        if (path != "/" && fileName.StartsWith(path))
            fileName = fileName.Substring(path.Length);
        else
            fileName = fileName.TrimStart('/');

        fileName = Uri.UnescapeDataString(fileName);
        if (fileName == "") fileName = "search-engine.html";

        string fullFolder = Path.GetFullPath(folder);
        string filePath = Path.GetFullPath(Path.Combine(fullFolder, fileName));
        if (!filePath.StartsWith(fullFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(filePath))
        {
            await SendResponseAsync(context.Response, 404, "Файл не найден.");
            return;
        }

        byte[] data = await File.ReadAllBytesAsync(filePath);
        string type = Path.GetExtension(filePath) == ".svg" ? "image/svg+xml" : "text/html; charset=utf-8";
        await SendResponseAsync(context.Response, 200, Encoding.UTF8.GetString(data), type);
    }
}
