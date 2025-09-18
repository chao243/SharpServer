using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SharpServer.Common.ServiceRegistry;

namespace SharpServer.Common.LoadBalancing;

public class RoundRobinLoadBalancer : ILoadBalancer
{
    private readonly ConcurrentDictionary<string, CounterState> _counters = new();
    private readonly ConcurrentDictionary<string, ServiceHealth> _health = new();

    private static readonly TimeSpan EvaluationWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan OpenCircuitDuration = TimeSpan.FromSeconds(30);
    private const double FailureThreshold = 0.5;
    private const int MinimumSampleSize = 5;

    public Task<ServiceInfo?> SelectServiceAsync(string serviceName, IReadOnlyList<ServiceInfo> services, string? affinityKey = null, CancellationToken cancellationToken = default)
    {
        if (services.Count == 0)
        {
            return Task.FromResult<ServiceInfo?>(null);
        }

        var upServices = services.Where(s => s.Status == ServiceStatus.Up).ToList();
        if (upServices.Count == 0)
        {
            return Task.FromResult<ServiceInfo?>(null);
        }

        var healthy = upServices.Where(s => IsHealthy(s.ServiceId)).ToList();
        var candidates = healthy.Count > 0 ? healthy : upServices;

        var counter = _counters.GetOrAdd(serviceName, _ => new CounterState());
        var index = (int)(counter.Next() % (uint)candidates.Count);
        var selected = candidates[index];
        return Task.FromResult<ServiceInfo?>(selected);
    }

    public void RecordSuccess(string serviceId)
    {
        var health = _health.GetOrAdd(serviceId, _ => new ServiceHealth());
        health.RecordSuccess();
    }

    public void RecordFailure(string serviceId, Exception? exception = null)
    {
        var health = _health.GetOrAdd(serviceId, _ => new ServiceHealth());
        health.RecordFailure();
    }

    private bool IsHealthy(string serviceId)
    {
        if (!_health.TryGetValue(serviceId, out var health))
        {
            return true;
        }

        return health.IsHealthy();
    }

    private sealed class CounterState
    {
        private int _value = -1;

        public uint Next()
        {
            return (uint)Interlocked.Increment(ref _value);
        }
    }

    private sealed class ServiceHealth
    {
        private double _successes;
        private double _failures;
        private DateTime _lastSample = DateTime.UtcNow;
        private DateTime _circuitOpenUntil = DateTime.MinValue;
        private readonly object _lock = new();

        public void RecordSuccess()
        {
            lock (_lock)
            {
                ApplyDecay();
                _successes += 1d;
                _circuitOpenUntil = DateTime.MinValue;
            }
        }

        public void RecordFailure()
        {
            lock (_lock)
            {
                ApplyDecay();
                _failures += 1d;
                EvaluateCircuit();
            }
        }

        public bool IsHealthy()
        {
            lock (_lock)
            {
                ApplyDecay();

                if (DateTime.UtcNow < _circuitOpenUntil)
                {
                    return false;
                }

                var total = _successes + _failures;
                if (total < MinimumSampleSize)
                {
                    return true;
                }

                var failureRate = _failures / total;
                return failureRate <= FailureThreshold;
            }
        }

        private void ApplyDecay()
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastSample;
            if (elapsed <= TimeSpan.Zero)
            {
                return;
            }

            var decayFactor = Math.Exp(-elapsed.TotalSeconds / EvaluationWindow.TotalSeconds);
            _successes *= decayFactor;
            _failures *= decayFactor;
            _lastSample = now;
        }

        private void EvaluateCircuit()
        {
            var total = _successes + _failures;
            if (total < MinimumSampleSize)
            {
                return;
            }

            var failureRate = _failures / total;
            if (failureRate > FailureThreshold)
            {
                _circuitOpenUntil = DateTime.UtcNow.Add(OpenCircuitDuration);
            }
        }
    }
}
