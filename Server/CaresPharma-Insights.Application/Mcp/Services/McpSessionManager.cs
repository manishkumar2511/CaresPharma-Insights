using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using CaresPharma_Insights.Domain.Models;

namespace CaresPharma_Insights.Application.Mcp.Services
{
    public interface IMcpSessionManager
    {
        ChannelReader<string> CreateSession(string sessionId, TenantSecurityContext? tenantContext = null);
        void SetSessionTenantContext(string sessionId, TenantSecurityContext tenantContext);
        TenantSecurityContext? GetSessionTenantContext(string sessionId);
        bool TryPostMessage(string sessionId, string message);
        void RemoveSession(string sessionId);
    }

    /// <summary>
    /// Thread-safe high-throughput session manager maintaining SSE message streams for connected MCP clients.
    /// Associates active client sessions with their verified TenantSecurityContext.
    /// </summary>
    public sealed class McpSessionManager : IMcpSessionManager
    {
        private readonly ConcurrentDictionary<string, Channel<string>> _sessions = new();
        private readonly ConcurrentDictionary<string, TenantSecurityContext> _tenantContexts = new();

        public ChannelReader<string> CreateSession(string sessionId, TenantSecurityContext? tenantContext = null)
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

            if (tenantContext != null)
            {
                _tenantContexts[sessionId] = tenantContext;
            }

            return channel.Reader;
        }

        public void SetSessionTenantContext(string sessionId, TenantSecurityContext tenantContext)
        {
            _tenantContexts[sessionId] = tenantContext;
        }

        public TenantSecurityContext? GetSessionTenantContext(string sessionId)
        {
            _tenantContexts.TryGetValue(sessionId, out var context);
            return context;
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

            _tenantContexts.TryRemove(sessionId, out _);
        }
    }
}
