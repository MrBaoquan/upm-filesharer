using System;
using System.Threading;
using System.Threading.Tasks;

namespace Parful.AndX.Aip
{
    /// <summary>
    /// Maintains the AIP WebSocket connection with bounded exponential backoff.
    /// Call Start after configuring ExhibitRuntime and before expecting payment commands.
    /// </summary>
    public sealed class AipConnectionSupervisor : IDisposable
    {
        private readonly AipClient _client;
        private readonly TimeSpan _initialRetryDelay;
        private readonly TimeSpan _maxRetryDelay;
        private CancellationTokenSource _lifetime;
        private Task _loop;
        private bool _disposed;

        public event Action<Exception> ReconnectFailed;

        public AipConnectionSupervisor(
            AipClient client,
            TimeSpan? initialRetryDelay = null,
            TimeSpan? maxRetryDelay = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _initialRetryDelay = initialRetryDelay ?? TimeSpan.FromSeconds(1);
            _maxRetryDelay = maxRetryDelay ?? TimeSpan.FromSeconds(30);
            if (_initialRetryDelay <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(initialRetryDelay));
            if (_maxRetryDelay < _initialRetryDelay)
                throw new ArgumentOutOfRangeException(nameof(maxRetryDelay));
        }

        public bool IsRunning
        {
            get { return _loop != null && !_loop.IsCompleted; }
        }

        public void Start()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AipConnectionSupervisor));
            if (IsRunning) return;
            _lifetime = new CancellationTokenSource();
            _loop = RunAsync(_lifetime.Token);
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var delay = _initialRetryDelay;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!_client.IsConnected)
                        await _client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    if (_client.IsConnected)
                        delay = _initialRetryDelay;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    ReconnectFailed?.Invoke(exception);
                }

                if (!_client.IsConnected)
                {
                    var nextDelayMs = Math.Min(delay.TotalMilliseconds * 2, _maxRetryDelay.TotalMilliseconds);
                    delay = TimeSpan.FromMilliseconds(nextDelayMs);
                }

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _lifetime?.Cancel(); } catch { }
            _lifetime?.Dispose();
            _lifetime = null;
            _loop = null;
        }
    }
}
