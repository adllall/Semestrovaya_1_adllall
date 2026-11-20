using System.Net;
using System.Net.Sockets;
using System.Text;
using Semestrovaya.Server;

namespace Semestrovaya.Tests;

public class HttpServerTests
{
    [Fact]
    public async Task HeadReturnsGetHeadersWithoutBody()
    {
        var router = new Router();
        router.Map("GET", "/", context => context.ViewAsync("error", new { ErrorCode = 200, ErrorMessage = "Works" }));
        var (server, shutdown, running, client) = Start(router);
        using (server)
        using (shutdown)
        using (client)
        {
            try
            {
                foreach (string path in new[] { "/", "/css/gallery.css" })
                {
                    using var get = await client.GetAsync(path);
                    using var request = new HttpRequestMessage(HttpMethod.Head, path);
                    using var head = await client.SendAsync(request);

                    Assert.Equal(HttpStatusCode.OK, get.StatusCode);
                    Assert.Equal(HttpStatusCode.OK, head.StatusCode);
                    Assert.Equal(get.Content.Headers.ContentType, head.Content.Headers.ContentType);
                    Assert.True(head.Content.Headers.ContentLength > 0);
                    Assert.Equal(get.Content.Headers.ContentLength, head.Content.Headers.ContentLength);
                    Assert.Empty(await head.Content.ReadAsByteArrayAsync());
                }
            }
            finally
            {
                await shutdown.CancelAsync();
                await running;
            }
        }
    }

    [Fact]
    public async Task ReturnsUsefulErrorsAndContinuesAfterHandlerFailure()
    {
        var router = new Router();
        router.Map("GET", "/", context => context.JsonAsync(new { ready = true }));
        router.Map("GET", "/api/failure", _ => throw new InvalidOperationException("Internal details"));
        router.Map("POST", "/api/forms", async context => await context.JsonAsync(await context.ReadJsonAsync<Person>()));
        var (server, shutdown, running, client) = Start(router);
        using (server)
        using (shutdown)
        using (client)
        {
            try
            {
                using var missing = await client.GetAsync("/missing");
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
                Assert.Contains("href=\"/\"", await missing.Content.ReadAsStringAsync());

                using var method = await client.GetAsync("/api/forms");
                Assert.Equal(HttpStatusCode.MethodNotAllowed, method.StatusCode);
                Assert.Contains("POST", method.Content.Headers.Allow);

                using var body = new StringContent("{", System.Text.Encoding.UTF8, "application/json");
                using var invalid = await client.PostAsync("/api/forms", body);
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

                using var failed = await client.GetAsync("/api/failure");
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                Assert.DoesNotContain("Internal details", await failed.Content.ReadAsStringAsync());

                using var ready = await client.GetAsync("/");
                Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            }
            finally
            {
                await shutdown.CancelAsync();
                await running;
            }
        }
    }

    [Theory]
    [InlineData("name=%D0%A2%D0%B5%D1%81%D1%82+%2B", 200)]
    [InlineData("name=one&name=two", 400)]
    [InlineData("name=%ZZ", 400)]
    [InlineData("name=%FF", 400)]
    [InlineData("not-a-key-value-pair", 400)]
    public async Task ValidatesFormEncoding(string body, int expected)
    {
        var router = new Router();
        router.Map("POST", "/api/form", async context => await context.JsonAsync(await context.ReadFormAsync()));
        var (server, shutdown, running, client) = Start(router);
        using (server)
        using (shutdown)
        using (client)
        {
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
                using var result = await client.PostAsync("/api/form", content);
                Assert.Equal(expected, (int)result.StatusCode);
                if (expected == 200)
                {
                    using var json = System.Text.Json.JsonDocument.Parse(await result.Content.ReadAsStringAsync());
                    Assert.Equal("Тест +", json.RootElement.GetProperty("name").GetString());
                }
            }
            finally
            {
                await shutdown.CancelAsync();
                await running;
            }
        }
    }

    [Fact]
    public async Task RejectsOversizedBodyAndStillAcceptsRequests()
    {
        var router = new Router();
        router.Map("POST", "/api/forms", async context => await context.JsonAsync(await context.ReadJsonAsync<Person>()));
        var (server, shutdown, running, client) = Start(router);
        using (server)
        using (shutdown)
        using (client)
        {
            try
            {
                using var oversized = new StringContent(new string('a', 32_769), Encoding.UTF8, "application/json");
                using var denied = await client.PostAsync("/api/forms", oversized);
                Assert.Equal(HttpStatusCode.RequestEntityTooLarge, denied.StatusCode);
                Assert.True(denied.Headers.ConnectionClose);

                using var small = new StringContent("""{"name":"Тест"}""", Encoding.UTF8, "application/json");
                using var accepted = await client.PostAsync("/api/forms", small);
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            }
            finally
            {
                await shutdown.CancelAsync();
                await running;
            }
        }
    }

    [Fact]
    public async Task BusyServerRespondsAndRecoversWhenSlotIsReleased()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var router = new Router();
        router.Map("GET", "/", async context =>
        {
            entered.TrySetResult();
            await release.Task;
            await context.JsonAsync(new { ready = true });
        });
        var (server, shutdown, running, client) = Start(router, maxConcurrentRequests: 1);
        using (server)
        using (shutdown)
        using (client)
        {
            try
            {
                var ongoing = client.GetAsync("/");
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                using var busy = await client.GetAsync("/");
                Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
                Assert.NotNull(busy.Headers.RetryAfter);
                release.TrySetResult();
                using var finished = await ongoing;
                Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
                using var next = await client.GetAsync("/");
                Assert.Equal(HttpStatusCode.OK, next.StatusCode);
            }
            finally
            {
                release.TrySetResult();
                await shutdown.CancelAsync();
                await running;
            }
        }
    }

    private static (HttpServer Server, CancellationTokenSource Shutdown, Task Running, HttpClient Client) Start(
        Router router, int maxConcurrentRequests = 48)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();

        var prefix = $"http://localhost:{port}/";
        var shutdown = new CancellationTokenSource();
        var server = new HttpServer(router, prefix, maxConcurrentRequests);
        return (server, shutdown, server.StartAsync(shutdown.Token),
            new HttpClient { BaseAddress = new Uri(prefix), Timeout = TimeSpan.FromSeconds(5) });
    }

    private sealed record Person(string Name);
}
