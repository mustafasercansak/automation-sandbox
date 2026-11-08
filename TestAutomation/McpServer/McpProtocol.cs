using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.Mcp
{
    /// <summary>What a tool returned: text for the model, and whether the call failed. A failed call is a result the model can
    /// read, not a protocol error.</summary>
    internal sealed class McpToolResult
    {
        public McpToolResult(string text, bool isError)
        {
            Text = text;
            IsError = isError;
        }

        public string Text { get; }

        public bool IsError { get; }

        public static McpToolResult Ok(string text) => new McpToolResult(text, false);

        public static McpToolResult Error(string text) => new McpToolResult(text, true);
    }

    /// <summary>One tool the server offers. <see cref="InputSchema" /> is a JSON Schema object; the arguments are untrusted and
    /// each handler validates them itself.</summary>
    internal sealed class McpTool
    {
        public McpTool(string name, string description, string inputSchema, Func<JsonElement, CancellationToken, Task<McpToolResult>> handler)
        {
            Name = name;
            Description = description;
            InputSchema = inputSchema;
            Handler = handler;
        }

        public string Name { get; }

        public string Description { get; }

        public string InputSchema { get; }

        public Func<JsonElement, CancellationToken, Task<McpToolResult>> Handler { get; }
    }

    /// <summary>A minimal Model Context Protocol server over a pair of text streams: one JSON-RPC message per line (the stdio
    /// transport), tools only. It never writes anything but protocol messages to the output, bounds every incoming message and
    /// every tool answer, and answers a malformed or unknown request with a JSON-RPC error instead of stopping.
    /// It serves two protocol eras, which the revision allows a server to do (#674). A request that carries the per-request
    /// <c>io.modelcontextprotocol/protocolVersion</c> in <c>_meta</c> is served statelessly under 2026-07-28: no handshake, a
    /// <c>resultType</c> on every result. A process that was opened with <c>initialize</c> is served under the older revisions
    /// (2025-06-18 and before) exactly as before, so a client that has not moved yet keeps working. A request that is neither
    /// is malformed.</summary>
    internal static class McpProtocol
    {
        /// <summary>Largest accepted message, in characters. A longer line is discarded and answered with an error.</summary>
        public const int MaxMessageLength = 1000000;

        /// <summary>Longest tool answer, in characters; a longer one is cut and says so.</summary>
        public const int MaxToolOutputLength = 60000;

        /// <summary>The newest protocol revision this server speaks: no handshake, every request carries its own version.</summary>
        public const string ProtocolVersion = "2026-07-28";

        /// <summary>The newest older revision, spoken to a client that opens with <c>initialize</c>.</summary>
        public const string LegacyProtocolVersion = "2025-06-18";

        private const string VersionKey = "io.modelcontextprotocol/protocolVersion";
        private const string CapabilitiesKey = "io.modelcontextprotocol/clientCapabilities";
        private const string ServerInfoKey = "io.modelcontextprotocol/serverInfo";

        // How long a client may keep the tool list and the discover answer. Both are fixed for the life of the process.
        private const int CacheMilliseconds = 3600000;

        private static readonly string[] LegacyVersions = { "2025-06-18", "2025-03-26", "2024-11-05" };

        private static readonly string[] AllVersions = { "2026-07-28", "2025-06-18", "2025-03-26", "2024-11-05" };

        // Which era a stdio process is in is decided by its first message, and only an initialize request moves it to the older one.
        private sealed class Session
        {
            public bool Legacy { get; set; }
        }

        private const string Instructions = "Read-only tools of the Automation Sandbox engine. No tool writes a label, card, file or issue, and none takes a credential as an argument.";

        public static async Task RunAsync(TextReader input, TextWriter output, IReadOnlyList<McpTool> tools, string serverVersion, CancellationToken cancellationToken)
        {
            var reader = new BoundedLineReader(input, MaxMessageLength);
            var session = new Session();
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line.EndOfInput)
                {
                    return;
                }

                string? response;
                if (line.TooLong)
                {
                    response = ErrorResponse("null", -32600, "The message is longer than " + MaxMessageLength + " characters and was discarded.");
                }
                else if (line.Text!.Trim().Length == 0)
                {
                    continue;
                }
                else
                {
                    response = await HandleAsync(line.Text, tools, serverVersion, session, cancellationToken).ConfigureAwait(false);
                }

                if (response != null)
                {
                    await output.WriteLineAsync(response).ConfigureAwait(false);
                    await output.FlushAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>Handles one message and returns the response line, or null for a notification (which never gets one).</summary>
        private static async Task<string?> HandleAsync(string message, IReadOnlyList<McpTool> tools, string serverVersion, Session session, CancellationToken cancellationToken)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(message);
            }
            catch (JsonException)
            {
                return ErrorResponse("null", -32700, "The message is not valid JSON.");
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return ErrorResponse("null", -32600, "A request must be one JSON object (batches are not supported).");
                }

                var hasId = root.TryGetProperty("id", out var idElement);
                var id = "null";
                if (hasId)
                {
                    if (idElement.ValueKind != JsonValueKind.String && idElement.ValueKind != JsonValueKind.Number)
                    {
                        return ErrorResponse("null", -32600, "The request id must be a string or a number.");
                    }

                    id = idElement.GetRawText();
                }

                // A response from the client (no method) is not something this server asked for; ignore it.
                if (!root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
                {
                    return hasId ? ErrorResponse(id, -32600, "The request has no method.") : null;
                }

                var method = methodElement.GetString()!;
                if (!hasId)
                {
                    return null;
                }

                var parameters = root.TryGetProperty("params", out var p) ? p : default;

                // The older handshake: kept so a client that has not moved to 2026-07-28 still works.
                if (method == "initialize")
                {
                    session.Legacy = true;
                    return ResultResponse(id, Initialize(parameters, serverVersion));
                }

                if (HasModernVersion(parameters))
                {
                    return await HandleModernAsync(id, method, parameters, tools, serverVersion, cancellationToken).ConfigureAwait(false);
                }

                if (session.Legacy)
                {
                    switch (method)
                    {
                        case "ping":
                            return ResultResponse(id, "{}");
                        case "tools/list":
                            return ResultResponse(id, ListTools(tools, false));
                        case "tools/call":
                            return await CallToolAsync(id, parameters, tools, false, cancellationToken).ConfigureAwait(false);
                        default:
                            return ErrorResponse(id, -32601, "Method not found.");
                    }
                }

                // Neither a handshake nor a per-request version: the request is malformed under the current revision.
                return ErrorResponse(id, -32602, "The request is missing params._meta[\"" + VersionKey + "\"] and params._meta[\"" + CapabilitiesKey + "\"], which every request needs.");
            }
        }

        private static bool HasModernVersion(JsonElement parameters) =>
            parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("_meta", out var meta)
            && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty(VersionKey, out _);

        // 2026-07-28: every request carries its version and capabilities, and nothing is remembered between requests.
        private static async Task<string> HandleModernAsync(string id, string method, JsonElement parameters, IReadOnlyList<McpTool> tools, string serverVersion, CancellationToken cancellationToken)
        {
            var meta = parameters.GetProperty("_meta");
            var requested = meta.GetProperty(VersionKey);
            if (requested.ValueKind != JsonValueKind.String)
            {
                return ErrorResponse(id, -32602, "_meta[\"" + VersionKey + "\"] must be a string.");
            }

            if (!meta.TryGetProperty(CapabilitiesKey, out var capabilities) || capabilities.ValueKind != JsonValueKind.Object)
            {
                return ErrorResponse(id, -32602, "The request is missing params._meta[\"" + CapabilitiesKey + "\"], which every request needs.");
            }

            var version = requested.GetString()!;
            if (version != ProtocolVersion)
            {
                // The versions this server speaks, newest first, so the client can pick one and retry.
                return "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":" + JsonSerializer.Serialize(new
                {
                    code = -32022,
                    message = "Unsupported protocol version",
                    data = new { supported = AllVersions, requested = version },
                }) + "}";
            }

            switch (method)
            {
                case "server/discover":
                    return ResultResponse(id, Discover(serverVersion));
                case "tools/list":
                    return ResultResponse(id, ListTools(tools, true));
                case "tools/call":
                    return await CallToolAsync(id, parameters, tools, true, cancellationToken).ConfigureAwait(false);
                default:
                    // ping and the initialize notification are not part of this revision.
                    return ErrorResponse(id, -32601, "Method not found.");
            }
        }

        private static string Discover(string serverVersion) =>
            JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["resultType"] = "complete",
                ["supportedVersions"] = AllVersions,
                ["capabilities"] = new { tools = new { } },
                ["_meta"] = new Dictionary<string, object> { [ServerInfoKey] = new { name = "automation-sandbox-mcp", version = serverVersion } },
                ["instructions"] = Instructions,
                ["ttlMs"] = CacheMilliseconds,
                ["cacheScope"] = "public",
            });

        private static string Initialize(JsonElement parameters, string serverVersion)
        {
            var version = LegacyProtocolVersion;
            if (parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("protocolVersion", out var requested) && requested.ValueKind == JsonValueKind.String)
            {
                var asked = requested.GetString();
                if (LegacyVersions.Contains(asked))
                {
                    version = asked!;
                }
            }

            return JsonSerializer.Serialize(new
            {
                protocolVersion = version,
                capabilities = new { tools = new { } },
                serverInfo = new { name = "automation-sandbox-mcp", version = serverVersion },
                instructions = Instructions,
            });
        }

        private static string ListTools(IReadOnlyList<McpTool> tools, bool modern)
        {
            var items = tools.Select(t => new Dictionary<string, object>
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = JsonDocument.Parse(t.InputSchema).RootElement.Clone(),
            }).ToList();
            if (!modern)
            {
                return JsonSerializer.Serialize(new { tools = items });
            }

            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["resultType"] = "complete",
                ["tools"] = items,
                ["ttlMs"] = CacheMilliseconds,
                ["cacheScope"] = "public",
            });
        }

        private static async Task<string> CallToolAsync(string id, JsonElement parameters, IReadOnlyList<McpTool> tools, bool modern, CancellationToken cancellationToken)
        {
            if (parameters.ValueKind != JsonValueKind.Object || !parameters.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
            {
                return ErrorResponse(id, -32602, "tools/call needs a tool name.");
            }

            var tool = tools.FirstOrDefault(t => string.Equals(t.Name, nameElement.GetString(), StringComparison.Ordinal));
            if (tool == null)
            {
                return ErrorResponse(id, -32602, "Unknown tool.");
            }

            JsonElement arguments;
            if (parameters.TryGetProperty("arguments", out var given))
            {
                if (given.ValueKind != JsonValueKind.Object)
                {
                    return ErrorResponse(id, -32602, "The arguments must be a JSON object.");
                }

                arguments = given;
            }
            else
            {
                using var empty = JsonDocument.Parse("{}");
                arguments = empty.RootElement.Clone();
            }

            McpToolResult result;
            try
            {
                result = await tool.Handler(arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Only the type: an exception message could carry a URL or text from a response.
                result = McpToolResult.Error("The tool failed unexpectedly (" + ex.GetType().Name + ").");
            }

            var content = new[] { new { type = "text", text = Cut(result.Text) } };
            return ResultResponse(id, modern
                ? JsonSerializer.Serialize(new { resultType = "complete", content, isError = result.IsError })
                : JsonSerializer.Serialize(new { content, isError = result.IsError }));
        }

        private static string Cut(string text)
        {
            if (text.Length <= MaxToolOutputLength)
            {
                return text;
            }

            return text.Substring(0, MaxToolOutputLength) + "\n[output cut at " + MaxToolOutputLength + " characters]";
        }

        private static string ResultResponse(string id, string resultJson) => "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"result\":" + resultJson + "}";

        private static string ErrorResponse(string id, int code, string message) =>
            "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"error\":" + JsonSerializer.Serialize(new { code, message }) + "}";

        private readonly struct Line
        {
            public Line(string? text, bool tooLong, bool endOfInput)
            {
                Text = text;
                TooLong = tooLong;
                EndOfInput = endOfInput;
            }

            public string? Text { get; }

            public bool TooLong { get; }

            public bool EndOfInput { get; }
        }

        // TextReader.ReadLine has no limit, so one endless line would grow without bound. This reads at most the limit,
        // then throws the rest of an over-long line away.
        private sealed class BoundedLineReader
        {
            private readonly TextReader _input;
            private readonly int _limit;
            private readonly char[] _buffer = new char[4096];
            private int _position;
            private int _length;

            public BoundedLineReader(TextReader input, int limit)
            {
                _input = input;
                _limit = limit;
            }

            public async Task<Line> ReadLineAsync()
            {
                var builder = new StringBuilder();
                var tooLong = false;
                var sawAny = false;
                while (true)
                {
                    if (_position >= _length)
                    {
                        _length = await _input.ReadAsync(_buffer, 0, _buffer.Length).ConfigureAwait(false);
                        _position = 0;
                        if (_length == 0)
                        {
                            return sawAny ? new Line(tooLong ? null : builder.ToString(), tooLong, false) : new Line(null, false, true);
                        }
                    }

                    var c = _buffer[_position++];
                    sawAny = true;
                    if (c == '\n')
                    {
                        return new Line(tooLong ? null : builder.ToString().TrimEnd('\r'), tooLong, false);
                    }

                    if (!tooLong)
                    {
                        if (builder.Length >= _limit)
                        {
                            tooLong = true;
                            builder.Clear();
                        }
                        else
                        {
                            builder.Append(c);
                        }
                    }
                }
            }
        }
    }
}
