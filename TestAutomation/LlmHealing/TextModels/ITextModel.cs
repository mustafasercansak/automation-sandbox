using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutomationSandbox.LlmHealing
{
    /// <summary>One bounded "send this text, get text back" call to a language model, for tasks that are not
    /// locator healing: drafting a document, triaging an issue, reviewing a draft. Unlike
    /// <see cref="ILlmHealingProvider" />, nothing about the request is shaped around picking a candidate.</summary>
    public interface ITextModel
    {
        /// <summary>Name that identifies the model in reports and in label-based routing. Unique within a
        /// <see cref="TextModelRegistry" />.</summary>
        string Name { get; }

        /// <summary>Whether the local configuration the model needs (a key) is present. It does not check that the
        /// service is reachable or that quota remains.</summary>
        bool IsAvailable { get; }

        /// <summary>Sends <paramref name="request" /> and returns the answer. A failure of any kind is returned in
        /// the result and never thrown, except for cancellation by <paramref name="cancellationToken" />.</summary>
        Task<TextCompletionResult> CompleteAsync(TextCompletionRequest request, CancellationToken cancellationToken = default);
    }

    /// <summary>What to ask a <see cref="ITextModel" />.</summary>
    public sealed class TextCompletionRequest
    {
        /// <summary>Default for <see cref="MaxOutputTokens" />.</summary>
        public const int DefaultMaxOutputTokens = 2048;

        /// <summary>Creates a request. <paramref name="maxOutputTokens" /> must be positive.</summary>
        public TextCompletionRequest(string prompt, int maxOutputTokens = DefaultMaxOutputTokens, bool expectJson = false)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("prompt must not be null or empty.", nameof(prompt));
            }

            if (maxOutputTokens < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxOutputTokens), "maxOutputTokens must be positive.");
            }

            Prompt = prompt;
            MaxOutputTokens = maxOutputTokens;
            ExpectJson = expectJson;
        }

        /// <summary>The text to send. It is passed through the model's text sanitiser before it leaves the process.</summary>
        public string Prompt { get; }

        /// <summary>Upper bound on the answer length, in tokens. Not every service accepts the limit; see each model.</summary>
        public int MaxOutputTokens { get; }

        /// <summary>When true the caller will parse the answer as a JSON object, and models that can ask the
        /// service for JSON output do so. The answer is still untrusted text and must be validated.</summary>
        public bool ExpectJson { get; }
    }

    /// <summary>The outcome of one <see cref="ITextModel.CompleteAsync" /> call.</summary>
    public sealed class TextCompletionResult
    {
        /// <summary>Name of the model that produced this result.</summary>
        public string ModelName { get; set; } = "";

        /// <summary>True when the model returned a non-empty answer.</summary>
        public bool Success { get; set; }

        /// <summary>The answer text; empty when <see cref="Success" /> is false.</summary>
        public string Text { get; set; } = "";

        /// <summary>Failure diagnostic, or null on success.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Wall-clock time of the whole call, including retries.</summary>
        public TimeSpan Elapsed { get; set; }

        /// <summary>HTTP sends made, including retries; 0 when the model was unavailable.</summary>
        public int AttemptCount { get; set; }
    }
}
