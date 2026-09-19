using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

// Minimal loopback-only static file server for the sample's fixture app. A real HTTP origin
// is required here, not a file:// page: Playwright's storage state does not carry localStorage
// for file:// origins (confirmed empirically while building PlaywrightWebSession's storage-state
// support, #450) - the whole point of this sample's step 2 is a real storage-state round-trip.
internal sealed class LoopbackFileServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _rootDirectory;
    private readonly CancellationTokenSource _cts = new();

    public string BaseUrl { get; }

    public LoopbackFileServer(string rootDirectory)
    {
        _rootDirectory = rootDirectory;
        var port = GetFreeTcpPort();
        BaseUrl = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUrl);
        _listener.Start();
        _ = ServeLoopAsync();
    }

    private async Task ServeLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            var relativePath = context.Request.Url?.AbsolutePath.TrimStart('/') ?? "";
            var filePath = Path.Combine(_rootDirectory, relativePath);

            if (File.Exists(filePath))
            {
                var bytes = File.ReadAllBytes(filePath);
                context.Response.ContentType = ContentTypeFor(filePath);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
            else
            {
                context.Response.StatusCode = 404;
            }

            context.Response.OutputStream.Close();
        }
    }

    private static string ContentTypeFor(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html",
            ".png" => "image/png",
            _ => "application/octet-stream",
        };
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
    }
}
