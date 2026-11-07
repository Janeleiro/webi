using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Webi.Settings;
using Xunit;

namespace Webi.Tests;

/// <summary>Starts the real server on localhost and talks to it, with a loop standing in for the main thread.</summary>
public sealed class SettingsServerTests : IDisposable
{
    readonly SettingsServer _server;
    readonly CancellationTokenSource _stop = new();
    readonly HttpClient _http = new();
    Config _config = new();
    bool? _autostart;
    int _opened;

    public SettingsServerTests()
    {
        _server = new SettingsServer
        {
            State = () => new JsonObject { ["config"] = JsonNode.Parse(_config.ToJson()) },
            Save = (c, a) => { _config = c; _autostart = a; },
            Reload = () => null,
            OpenConfigFile = () => _opened++,
        };
        Assert.True(_server.Start());
        // The "main thread".
        Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                _server.Tick();
                await Task.Delay(5);
            }
        });
    }

    string Base => $"http://localhost:{_server.Port}";

    HttpRequestMessage Api(HttpMethod method, string path, string? body = null, string? token = null)
    {
        var req = new HttpRequestMessage(method, Base + path);
        req.Headers.Add("X-Webi-Token", token ?? _server.Token);
        if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return req;
    }

    [Fact]
    public async Task Serves_the_embedded_page()
    {
        var res = await _http.GetAsync(Base + "/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Webi · Configurações", await res.Content.ReadAsStringAsync());
        Assert.Equal("no-referrer", string.Join("", res.Headers.GetValues("Referrer-Policy")));
        var csp = string.Join("", res.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("connect-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Equal("DENY", string.Join("", res.Headers.GetValues("X-Frame-Options")));
        Assert.StartsWith($"http://localhost:{_server.Port}/?t=", _server.Url);
    }

    [Fact]
    public async Task The_api_needs_the_token_and_a_local_origin()
    {
        var none = await _http.SendAsync(Api(HttpMethod.Get, "/api/state", token: ""));
        Assert.Equal(HttpStatusCode.Forbidden, none.StatusCode);
        var wrong = await _http.SendAsync(Api(HttpMethod.Get, "/api/state", token: new string('0', _server.Token.Length)));
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);

        var foreign = Api(HttpMethod.Get, "/api/state");
        foreign.Headers.Add("Origin", "https://site-malicioso.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(foreign)).StatusCode);

        var ok = await _http.SendAsync(Api(HttpMethod.Get, "/api/state"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var state = JsonNode.Parse(await ok.Content.ReadAsStringAsync())!;
        Assert.Equal(45, (int)state["config"]!["water_minutes"]!);
    }

    [Fact]
    public async Task Saving_validates_the_config()
    {
        var body = """{ "config": { "water_minutes": 0, "sounds": true, "max_webs": 999, "fur_color": "xadrez" }, "autostart": true }""";
        var res = await _http.SendAsync(Api(HttpMethod.Put, "/api/state", body));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(1, _config.WaterMinutes);
        Assert.Equal(50, _config.MaxWebs);
        Assert.Equal("marrom", _config.FurColor);
        Assert.True(_config.Sounds);
        Assert.True(_autostart);

        var bad = await _http.SendAsync(Api(HttpMethod.Put, "/api/state", """{ "config": { "scale": "grande" } }"""));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var missing = await _http.SendAsync(Api(HttpMethod.Put, "/api/state", """{ "autostart": false }"""));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task A_huge_body_is_refused_even_without_a_length()
    {
        var req = Api(HttpMethod.Put, "/api/state");
        // Chunked: no Content-Length up front.
        req.Content = new StreamContent(new System.IO.MemoryStream(new byte[3 << 20]));
        req.Headers.TransferEncodingChunked = true;
        var res = await _http.SendAsync(req);
        Assert.False(res.IsSuccessStatusCode);
        Assert.Contains("grande demais", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Opening_the_file_runs_on_the_main_thread()
    {
        var res = await _http.SendAsync(Api(HttpMethod.Post, "/api/open-config"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(1, _opened);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _server.Dispose();
        _http.Dispose();
    }
}
