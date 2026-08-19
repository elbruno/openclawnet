using System.Runtime.CompilerServices;
using System.Threading.Channels;
using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace OpenClawNet.Models.GitHubCopilot;

/// <summary>
/// Adapts the GitHub Copilot SDK (<see cref="CopilotClient"/> + <see cref="CopilotSession"/>)
/// to the <see cref="IChatClient"/> contract expected by <c>IAgentProvider.CreateChatClient</c>.
/// Each request creates a disposable session; the underlying <see cref="CopilotClient"/> is
/// shared and managed by <see cref="GitHubCopilotAgentProvider"/>.
/// </summary>
internal sealed class CopilotChatClient : IChatClient
{
    private readonly GitHubCopilotAgentProvider _provider;
    private readonly string? _model;
    private readonly string? _systemMessage;

    internal CopilotChatClient(
        GitHubCopilotAgentProvider provider,
        string? model,
        string? systemMessage)
    {
        _provider = provider;
        _model = model;
        _systemMessage = systemMessage;
    }

    public ChatClientMetadata Metadata { get; } =
        new("github-copilot", new Uri("https://github.com/features/copilot"));

    // ── Non-streaming ────────────────────────────────────────────────
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var client = await _provider.GetClientAsync().ConfigureAwait(false);

        await using var session = await client.CreateSessionAsync(
            CreateSessionConfig(_model, _systemMessage, streaming: false)).ConfigureAwait(false);

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? responseText = null;

        session.On<SessionEvent>(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent msg:
                    responseText = msg.Data.Content;
                    break;
                case SessionIdleEvent:
                    done.TrySetResult();
                    break;
                case SessionErrorEvent err:
                    done.TrySetException(
                        new InvalidOperationException($"Copilot session error: {err.Data.Message}"));
                    break;
            }
        });

        var prompt = BuildPrompt(chatMessages);
        await session.SendAsync(new MessageOptions { Prompt = prompt }).ConfigureAwait(false);
        await done.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        return new ChatResponse(
            new ChatMessage(ChatRole.Assistant, responseText ?? string.Empty));
    }

    // ── Streaming ────────────────────────────────────────────────────
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = await _provider.GetClientAsync().ConfigureAwait(false);

        await using var session = await client.CreateSessionAsync(
            CreateSessionConfig(_model, _systemMessage, streaming: true)).ConfigureAwait(false);

        var channel = Channel.CreateUnbounded<ChatResponseUpdate>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        session.On<SessionEvent>(evt =>
        {
            switch (evt)
            {
                // Delta chunks — the incremental text while streaming
                case AssistantMessageDeltaEvent delta:
                    channel.Writer.TryWrite(new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(delta.Data.DeltaContent)],
                    });
                    break;

                // Session finished processing — close the channel
                case SessionIdleEvent:
                    channel.Writer.TryComplete();
                    break;

                case SessionErrorEvent err:
                    channel.Writer.TryComplete(
                        new InvalidOperationException($"Copilot session error: {err.Data.Message}"));
                    break;

                // Ignore AssistantMessageEvent (final full text) during streaming
                // to avoid duplicating content already emitted via deltas.
            }
        });

        var prompt = BuildPrompt(chatMessages);
        await session.SendAsync(new MessageOptions { Prompt = prompt }).ConfigureAwait(false);

        await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    public void Dispose() { /* nothing to dispose — CopilotClient is owned by the provider */ }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ChatClientMetadata))
            return Metadata;
        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the last user message text from the chat history.
    /// The <see cref="CopilotSession"/> manages its own context per session,
    /// so we only need to forward the final user turn.
    /// </summary>
    private static string BuildPrompt(IEnumerable<ChatMessage> messages)
    {
        var last = messages.LastOrDefault(m => m.Role == ChatRole.User);
        return last?.Text ?? string.Empty;
    }

    internal static SessionConfig CreateSessionConfig(
        string? model,
        string? systemMessage,
        bool streaming)
    {
        return new SessionConfig
        {
            Model = model,
            SystemMessage = systemMessage is not null
                ? new SystemMessageConfig { Content = systemMessage }
                : null,
            Streaming = streaming ? true : null,
            EnableManagedSettings = false,
            OnPermissionRequest = PermissionHandler.ApproveAll,
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
        };
    }
}
