using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyHttpServer;

public class HttpServer
{
    private HttpListener server = new HttpListener();
    private string path = "";

    private string folder = Path.Combine(
        AppContext.BaseDirectory, "wwwroot");

    public void Start()
    {
        // Читаем настройки из файла.
        string settingsFile = Path.Combine(
            AppContext.BaseDirectory, "settings.json");

        string json = File.ReadAllText(settingsFile);

        using JsonDocument document = JsonDocument.Parse(json);
        var settings = document.RootElement.GetProperty("Server");

        string host = settings.GetProperty("host").GetString()!;
        string port = settings.GetProperty("port").GetString()!;
        path = "/" + settings.GetProperty("path").GetString()!.Trim('/') + "/";

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
            catch (HttpListenerException) when (!server.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException) when (!server.IsListening)
            {
                break;
            }

            var response = context.Response;

            try
            {
                // Например, из /catalog/labels/GPT_label.svg
                // получаем labels/GPT_label.svg.
                string fileName = context.Request.Url!.AbsolutePath
                    .Substring(path.Length);

                fileName = Uri.UnescapeDataString(fileName);

                // Если запрошена главная страница.
                if (fileName == "")
                {
                    fileName = "search-engine.html";
                }

                string filePath = Path.GetFullPath(
                    Path.Combine(folder, fileName));

                // Отдаём только файлы внутри wwwroot.
                if (!filePath.StartsWith(
                        folder + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(filePath))
                {
                    response.StatusCode = 404;
                    response.ContentLength64 = 0;
                    continue;
                }

                if (Path.GetExtension(filePath) == ".svg")
                {
                    response.ContentType = "image/svg+xml";
                }
                else
                {
                    response.ContentType = "text/html; charset=utf-8";
                }

                byte[] data = File.ReadAllBytes(filePath);

                response.ContentLength64 = data.Length;
                response.OutputStream.Write(data, 0, data.Length);
            }
            catch (Exception error)
            {
                Console.WriteLine($"Ошибка: {error.Message}");
            }
            finally
            {
                // Завершаем ответ, чтобы браузер не ждал продолжения.
                response.Close();
            }
        }
    }

    public void Stop()
    {
        server.Stop();
        Console.WriteLine("Сервер остановлен.");
    }
}