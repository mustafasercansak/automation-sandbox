param(
    [Parameter(Mandatory)][string] $Root,
    [Parameter(Mandatory)][int] $Port
)

# Minimal static file server for the verify script's fixture site. Run as its own process so the
# caller can end it with Stop-Process: a blocked GetContext() cannot be interrupted from a job.
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
while ($listener.IsListening) {
    $context = $listener.GetContext()
    $relative = $context.Request.Url.AbsolutePath.TrimStart('/')
    if ($relative.EndsWith('/') -or $relative -eq '') { $relative += 'index.html' }
    $path = [System.IO.Path]::GetFullPath((Join-Path $Root $relative))
    if ($path.StartsWith($Root) -and (Test-Path -LiteralPath $path -PathType Leaf)) {
        $bytes = [System.IO.File]::ReadAllBytes($path)
        $context.Response.ContentType = 'text/html; charset=utf-8'
        $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    } else {
        $context.Response.StatusCode = 404
    }
    $context.Response.Close()
}
