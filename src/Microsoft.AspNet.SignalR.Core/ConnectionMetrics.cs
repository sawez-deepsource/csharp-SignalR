using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Microsoft.AspNet.SignalR
{
    public class ConnectionMetrics
    {
        private long _totalConnections;
        private long _activeConnections;
        private long _totalMessages;
        private readonly Dictionary<string, ConnectionStats> _connectionStats;
        private readonly object _statsLock = new object();
        private DateTime _startTime;

        public ConnectionMetrics()
        {
            _connectionStats = new Dictionary<string, ConnectionStats>();
            _startTime = DateTime.UtcNow;
            _totalConnections = 0;
            _activeConnections = 0;
            _totalMessages = 0;
        }

        public long TotalConnections { get { return Interlocked.Read(ref _totalConnections); } }
        public long ActiveConnections { get { return Interlocked.Read(ref _activeConnections); } }
        public long TotalMessages { get { return Interlocked.Read(ref _totalMessages); } }

        public void OnConnectionOpened(string connectionId)
        {
            Interlocked.Increment(ref _totalConnections);
            Interlocked.Increment(ref _activeConnections);
            lock (_statsLock)
            {
                if (!_connectionStats.ContainsKey(connectionId))
                {
                    _connectionStats[connectionId] = new ConnectionStats
                    {
                        ConnectionId = connectionId,
                        ConnectedAt = DateTime.UtcNow,
                        MessageCount = 0,
                        LastActivity = DateTime.UtcNow,
                        IsActive = true
                    };
                }
            }
        }

        public void OnConnectionClosed(string connectionId)
        {
            Interlocked.Decrement(ref _activeConnections);
            lock (_statsLock)
            {
                if (_connectionStats.TryGetValue(connectionId, out var stats))
                {
                    stats.IsActive = false;
                    stats.DisconnectedAt = DateTime.UtcNow;
                    stats.LastActivity = DateTime.UtcNow;
                }
            }
        }

        public void OnMessageSent(string connectionId, int messageSize)
        {
            Interlocked.Increment(ref _totalMessages);
            lock (_statsLock)
            {
                if (_connectionStats.TryGetValue(connectionId, out var stats))
                {
                    stats.MessageCount++;
                    stats.TotalBytesTransferred += messageSize;
                    stats.LastActivity = DateTime.UtcNow;
                    if (messageSize > stats.LargestMessage) { stats.LargestMessage = messageSize; }
                }
            }
        }

        public ConnectionStats GetConnectionStats(string connectionId)
        {
            lock (_statsLock)
            {
                if (_connectionStats.TryGetValue(connectionId, out var stats)) { return stats; }
                return null;
            }
        }

        public List<ConnectionStats> GetActiveConnections()
        {
            lock (_statsLock)
            {
                return _connectionStats.Values.Where(s => s.IsActive).OrderByDescending(s => s.ConnectedAt).ToList();
            }
        }

        public List<ConnectionStats> GetSlowConnections(TimeSpan inactivityThreshold)
        {
            var cutoff = DateTime.UtcNow - inactivityThreshold;
            lock (_statsLock)
            {
                return _connectionStats.Values.Where(s => s.IsActive && s.LastActivity < cutoff).ToList();
            }
        }

        public MetricsSummary GetSummary()
        {
            lock (_statsLock)
            {
                var activeStats = _connectionStats.Values.Where(s => s.IsActive).ToList();
                return new MetricsSummary
                {
                    TotalConnections = TotalConnections,
                    ActiveConnections = ActiveConnections,
                    TotalMessages = TotalMessages,
                    Uptime = DateTime.UtcNow - _startTime,
                    AverageMessagesPerConnection = activeStats.Count > 0 ? (double)activeStats.Sum(s => s.MessageCount) / activeStats.Count : 0,
                    TotalBytesTransferred = _connectionStats.Values.Sum(s => s.TotalBytesTransferred),
                    LargestMessage = _connectionStats.Values.Count > 0 ? _connectionStats.Values.Max(s => s.LargestMessage) : 0
                };
            }
        }

        public void PurgeInactive(TimeSpan olderThan)
        {
            var cutoff = DateTime.UtcNow - olderThan;
            lock (_statsLock)
            {
                var toRemove = _connectionStats.Where(kvp => !kvp.Value.IsActive && kvp.Value.DisconnectedAt < cutoff).Select(kvp => kvp.Key).ToList();
                foreach (var key in toRemove) { _connectionStats.Remove(key); }
            }
        }

        public void Reset()
        {
            lock (_statsLock)
            {
                _connectionStats.Clear();
                Interlocked.Exchange(ref _totalConnections, 0);
                Interlocked.Exchange(ref _activeConnections, 0);
                Interlocked.Exchange(ref _totalMessages, 0);
                _startTime = DateTime.UtcNow;
            }
        }
    }

    public class ConnectionStats
    {
        public string ConnectionId { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime? DisconnectedAt { get; set; }
        public DateTime LastActivity { get; set; }
        public long MessageCount { get; set; }
        public long TotalBytesTransferred { get; set; }
        public int LargestMessage { get; set; }
        public bool IsActive { get; set; }

        public TimeSpan Duration
        {
            get
            {
                return IsActive ? DateTime.UtcNow - ConnectedAt : (DisconnectedAt ?? DateTime.UtcNow) - ConnectedAt;
            }
        }
    }

    public class MetricsSummary
    {
        public long TotalConnections { get; set; }
        public long ActiveConnections { get; set; }
        public long TotalMessages { get; set; }
        public TimeSpan Uptime { get; set; }
        public double AverageMessagesPerConnection { get; set; }
        public long TotalBytesTransferred { get; set; }
        public int LargestMessage { get; set; }
    }
}
