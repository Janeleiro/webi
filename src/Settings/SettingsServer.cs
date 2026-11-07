using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Webi.Settings;

/// <summary>
/// The settings page: a small HTTP server on localhost, started from the menu and stopped after a while unused.
/// Only this machine can reach it (http.sys rejects any host but localhost), and every API call must carry the
/// random token the menu opened the page with, so other sites open in the browser can't touch the settings.
/// Requests are served on background threads; whatever touches the app runs on the main thread through
/// <see cref="Tick"/>.
/// </summary>
public sealed class SettingsServer : IDisposable
{
    public const int PreferredPort = 47812;
    /// <summary>Stops by itself after this long with no request (the open page pings every minute).</summary>
    static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);
    const int MaxBody = 1 << 20;

    readonly ConcurrentQueue<Action> _main = new();
    HttpListener? _listener;
    long _lastRequest;
    static byte[]? _page;

    // Hooks into the app, all called on the main thread.
    /// <summary>What the page shows: config, autostart, version, changelog...</summary>
    public required Func<JsonObject> State { get; init; }
    /// <summary>Applies and saves a config from the page (and the autostart choice, when sent).</summary>
    public required Action<Config, bool?> Save { get; init; }
    /// <summary>Reloads config.json from disk; returns its error, if any.</summary>
    public required Func<string?> Reload { get; init; }
    public required Action OpenConfigFile { get; init; }
    /// <summary>Fingerprint of config.json on disk (see <see cref="Config.FileStamp"/>).</summary>
    public required Func<string> Stamp { get; init; }
    public Action<string>? Log { get; init; }

    public int Port { get; private set; }
    public string Token { get; private set; } = "";
    public bool Running => _listener?.IsListening == true;
    public string Url => $"http://localhost:{Port}/?t={Token}";

    /// <summary>Starts listening (on the usual port, or a free one if it's taken). False if nothing worked.</summary>
    public bool Start()
    {
        if (Running)
        {
            Touch();
            return true;
        }
        Token = NewToken();
        for (int attempt = 0; attempt < 12; attempt++)
        {
            int port = attempt == 0 ? PreferredPort : Random.Shared.Next(49152, 65535);
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
            }
            catch (Exception e) when (e is HttpListenerException or InvalidOperationException)
            {
                listener.Close();
                if (attempt == 0) Log?.Invoke($"porta {port} ocupada ({e.Message}), tentando outra");
                continue;
            }
            _listener = listener;
            Port = port;
            Touch();
            _ = AcceptLoop(listener);
            Log?.Invoke($"configurações em http://localhost:{port}/");
            return true;
        }
        Log?.Invoke("não consegui abrir nenhuma porta para as configurações");
        return false;
    }

    public void Stop()
    {
        var listener = _listener;
        _listener = null;
        try { listener?.Close(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Main thread, every frame: runs what the requests left for it, and stops the server when idle.</summary>
    public void Tick()
    {
        while (_main.TryDequeue(out var action)) action();
        if (Running && DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastRequest) > IdleTimeout.Ticks)
        {
            Log?.Invoke("configurações fechadas por falta de uso");
            Stop();
        }
    }

    void Touch() => Interlocked.Exchange(ref _lastRequest, DateTime.UtcNow.Ticks);

    Task<T> OnMain<T>(Func<T> f)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _main.Enqueue(() =>
        {
            try { done.SetResult(f()); }
            catch (Exception e) { done.SetException(e); }
        });
        return done.Task;
    }

    Task OnMain(Action a) => OnMain(() => { a(); return true; });

    async Task AcceptLoop(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().ConfigureAwait(false); }
            catch { return; }
            _ = Task.Run(() => Handle(context));
        }
    }

    // ------------------------------------------------------------------ routing

    async Task Handle(HttpListenerContext ctx)
    {
        Touch();
        var req = ctx.Request;
        var res = ctx.Response;
        res.Headers["Cache-Control"] = "no-store";
        res.Headers["X-Content-Type-Options"] = "nosniff";
        // The token is in the page's URL: it must not leak to the sites the page links to.
        res.Headers["Referrer-Policy"] = "no-referrer";
        // The page is self-contained: nothing loads from elsewhere, it only talks to this server, and no other site
        // may frame it. Should anything ever slip past the escaping, it has nowhere to send what it reads.
        res.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; " +
            "img-src data:; connect-src 'self'; form-action 'none'; frame-ancestors 'none'; base-uri 'none'";
        res.Headers["X-Frame-Options"] = "DENY";
        try
        {
            var path = req.Url?.AbsolutePath ?? "/";
            var method = req.HttpMethod;
            if (method == "GET" && (path == "/" || path == "/index.html")) await Send(res, 200, "text/html; charset=utf-8", Page());
            else if (path.StartsWith("/api/"))
            {
                if (!Authorized(req)) await Json(res, 403, Error("acesso negado: abra as configurações pelo menu da Webi"));
                else await Api(method, path, req, res);
            }
            else await Send(res, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("não encontrado"));
        }
        catch (JsonException e)
        {
            // A body that isn't JSON at all: the request's fault, not the server's.
            try { await Json(res, 400, Error($"JSON inválido: {e.Message}")); }
            catch { /* the connection is gone */ }
        }
        catch (Exception e)
        {
            Log?.Invoke($"configurações: {req.HttpMethod} {req.Url?.AbsolutePath}: {e.Message}");
            try { await Json(res, 500, Error(e.Message)); }
            catch { /* the connection is gone */ }
        }
    }

    bool Authorized(HttpListenerRequest req)
    {
        var origin = req.Headers["Origin"];
        if (origin != null && origin != $"http://localhost:{Port}") return false;
        var token = req.Headers["X-Webi-Token"] ?? "";
        return token.Length == Token.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(token), Encoding.ASCII.GetBytes(Token));
    }

    async Task Api(string method, string path, HttpListenerRequest req, HttpListenerResponse res)
    {
        switch (method, path)
        {
            case ("GET", "/api/ping"):
                await Json(res, 200, new JsonObject { ["ok"] = true });
                break;
            case ("GET", "/api/state"):
                await Json(res, 200, await StateAsync());
                break;
            case ("PUT", "/api/state"):
                await SaveState(await Body(req), res);
                break;
            case ("POST", "/api/reload"):
                var error = await OnMain(Reload);
                var state = await StateAsync();
                if (error != null) state["reload_error"] = error;
                await Json(res, 200, state);
                break;
            case ("POST", "/api/open-config"):
                await OnMain(OpenConfigFile);
                await Json(res, 200, new JsonObject { ["ok"] = true });
                break;
            default:
                await Json(res, 404, Error("rota desconhecida"));
                break;
        }
    }

    /// <summary>Body: <c>{ "config": {...}, "autostart": bool }</c>.</summary>
    async Task SaveState(JsonNode? body, HttpListenerResponse res)
    {
        if (body?["config"] is not JsonObject configNode)
        {
            await Json(res, 400, Error("faltou a configuração"));
            return;
        }
        Config config;
        try
        {
            config = Config.FromJson(configNode.ToJsonString());
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            await Json(res, 400, Error($"configuração inválida: {e.Message}"));
            return;
        }
        // The page sends the fingerprint of the file it was loaded from: if the file changed since (edited by hand),
        // saving would put the page's older copy over those edits.
        var sent = body["stamp"] is JsonValue sv && sv.TryGetValue<string>(out var st) ? st : null;
        if (sent != await OnMain(Stamp))
        {
            await Json(res, 409, Error("o config.json foi alterado fora desta página. Clique em \"Recarregar do arquivo\" (as alterações não salvas daqui se perdem) e faça de novo"));
            return;
        }
        bool? autostart = body["autostart"] is JsonValue a && a.TryGetValue<bool>(out bool on) ? on : null;
        await OnMain(() => Save(config, autostart));
        await Json(res, 200, await StateAsync());
    }

    /// <summary>The app's state plus the fingerprint of config.json, which the page sends back when saving.</summary>
    async Task<JsonObject> StateAsync()
    {
        var state = await OnMain(State);
        state["config_stamp"] = await OnMain(Stamp);
        return state;
    }

    // ------------------------------------------------------------------ plumbing

    static byte[] Page()
    {
        if (_page != null) return _page;
        using var stream = typeof(SettingsServer).Assembly.GetManifestResourceStream("Webi.settings.html")
            ?? throw new InvalidOperationException("página de configurações não embutida no executável");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return _page = memory.ToArray();
    }

    /// <summary>The JSON body, read up to <see cref="MaxBody"/> bytes (also when the size isn't announced: chunked).</summary>
    static async Task<JsonNode?> Body(HttpListenerRequest req)
    {
        if (req.ContentLength64 > MaxBody) throw new InvalidOperationException("requisição grande demais");
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await req.InputStream.ReadAsync(buffer)) > 0)
        {
            if (memory.Length + read > MaxBody) throw new InvalidOperationException("requisição grande demais");
            memory.Write(buffer, 0, read);
        }
        var text = Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    static JsonObject Error(string message) => new() { ["ok"] = false, ["error"] = message };

    static Task Json(HttpListenerResponse res, int status, JsonNode node) =>
        Send(res, status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(node.ToJsonString()));

    static async Task Send(HttpListenerResponse res, int status, string type, byte[] body)
    {
        res.StatusCode = status;
        res.ContentType = type;
        res.ContentLength64 = body.Length;
        await res.OutputStream.WriteAsync(body);
        res.Close();
    }

    static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public void Dispose() => Stop();
}
