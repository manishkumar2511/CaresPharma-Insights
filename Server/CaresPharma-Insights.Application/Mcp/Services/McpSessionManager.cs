using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;

namespace CaresPharma_Insights.Application.Mcp.Services
{
    public interface IMcpSessionManager
    {
        ChannelReader<string> CreateSession(string sessionId);
        bool TryPostMessage(string sessionId, string message);
        void RemoveSession(string sessionId);
    }

    /// <summary>
    /// Thread-safe high-throughput session manager maintaining SSE message streams for connected MCP clients.
    /// Uses bounded asynchronous channels to prevent memory leaks while optimizing throughput.
    /// </summary>
    public sealed class McpSessionManager : IMcpSessionManager
    {
        private readonly ConcurrentDictionary<string, Channel<string>> _sessions = new();

        public ChannelReader<string> CreateSession(string sessionId)
        {
            var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

            _sessions.AddOrUpdate(sessionId, channel, (_, oldChannel) =>
            {
                oldChannel.Writer.TryComplete();
                return channel;
            });

            return channel.Reader;
        }

        public bool TryPostMessage(string sessionId, string message)
        {
            if (_sessions.TryGetValue(sessionId, out var channel))
            {
                return channel.Writer.TryWrite(message);
            }

            return false;
        }

        public void RemoveSession(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }
}
