using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Parful.AndX.Aip
{
    /// <summary>测试和短生命周期宿主使用；生产 Unity 应使用持久化实现。</summary>
    public sealed class InMemoryExhibitCommandStore : IExhibitCommandStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, ExhibitCommandRecord> _records = new Dictionary<string, ExhibitCommandRecord>(StringComparer.Ordinal);
        private readonly TimeSpan _retention;
        private readonly int _maxEntries;

        public InMemoryExhibitCommandStore(TimeSpan? retention = null, int maxEntries = 100)
        {
            _retention = retention ?? TimeSpan.FromDays(7);
            _maxEntries = Math.Max(1, maxEntries);
        }

        public Task<ExhibitCommandRecord> GetAsync(string key, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                ExhibitCommandRecord record;
                return Task.FromResult(_records.TryGetValue(key ?? string.Empty, out record) ? record.Clone() : null);
            }
        }

        public Task SaveAsync(ExhibitCommandRecord record, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.Key)) throw new ArgumentException("Command record key is required.", nameof(record));
            lock (_gate)
            {
                _records[record.Key] = record.Clone();
                Prune(_records, _retention, _maxEntries);
            }
            return Task.CompletedTask;
        }

        internal static void Prune(Dictionary<string, ExhibitCommandRecord> records, TimeSpan retention, int maxEntries)
        {
            var cutoff = DateTimeOffset.UtcNow.Subtract(retention);
            foreach (var pair in records.ToList())
            {
                if (RecordTime(pair.Value) < cutoff) records.Remove(pair.Key);
            }
            var keep = records.Values
                .OrderByDescending(RecordTime)
                .Take(Math.Max(1, maxEntries))
                .Select(item => item.Key)
                .ToList();
            var keepSet = new HashSet<string>(keep, StringComparer.Ordinal);
            foreach (var key in records.Keys.Where(key => !keepSet.Contains(key)).ToList())
            {
                records.Remove(key);
            }
        }

        private static DateTimeOffset RecordTime(ExhibitCommandRecord record)
        {
            DateTimeOffset value;
            if (DateTimeOffset.TryParse(record.CompletedAt ?? record.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
            {
                return value;
            }
            return DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// 文件持久化命令去重库。每条记录在实体动作前先写 pending，避免崩溃后自动重放。
    /// 默认保留 7 天且最多 100 条，路径由宿主指定（Unity 使用 persistentDataPath）。
    /// </summary>
    public sealed class FileExhibitCommandStore : IExhibitCommandStore, IDisposable
    {
        private readonly string _path;
        private readonly TimeSpan _retention;
        private readonly int _maxEntries;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private Dictionary<string, ExhibitCommandRecord> _records;
        private bool _loaded;
        private bool _disposed;

        public FileExhibitCommandStore(string path, TimeSpan? retention = null, int maxEntries = 100)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Command store path is required.", nameof(path));
            _path = path;
            _retention = retention ?? TimeSpan.FromDays(7);
            _maxEntries = Math.Max(1, maxEntries);
        }

        public async Task<ExhibitCommandRecord> GetAsync(string key, CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureLoaded();
                ExhibitCommandRecord record;
                return _records.TryGetValue(key ?? string.Empty, out record) ? record.Clone() : null;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task SaveAsync(ExhibitCommandRecord record, CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(record.Key)) throw new ArgumentException("Command record key is required.", nameof(record));
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                EnsureLoaded();
                _records[record.Key] = record.Clone();
                InMemoryExhibitCommandStore.Prune(_records, _retention, _maxEntries);
                WriteAtomic();
            }
            finally
            {
                _gate.Release();
            }
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            if (!File.Exists(_path))
            {
                _records = new Dictionary<string, ExhibitCommandRecord>(StringComparer.Ordinal);
                return;
            }
            try
            {
                var json = File.ReadAllText(_path, Encoding.UTF8);
                _records = JsonConvert.DeserializeObject<Dictionary<string, ExhibitCommandRecord>>(json)
                    ?? new Dictionary<string, ExhibitCommandRecord>(StringComparer.Ordinal);
                InMemoryExhibitCommandStore.Prune(_records, _retention, _maxEntries);
            }
            catch (IOException)
            {
                _records = new Dictionary<string, ExhibitCommandRecord>(StringComparer.Ordinal);
            }
            catch (UnauthorizedAccessException)
            {
                _records = new Dictionary<string, ExhibitCommandRecord>(StringComparer.Ordinal);
            }
            catch (JsonException)
            {
                _records = new Dictionary<string, ExhibitCommandRecord>(StringComparer.Ordinal);
            }
        }

        private void WriteAtomic()
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var json = JsonConvert.SerializeObject(_records, Formatting.None);
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(_path))
                {
                    try
                    {
                        File.Replace(temp, _path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Delete(_path);
                        File.Move(temp, _path);
                    }
                    catch (IOException)
                    {
                        File.Delete(_path);
                        File.Move(temp, _path);
                    }
                }
                else
                {
                    File.Move(temp, _path);
                }
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FileExhibitCommandStore));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _gate.Dispose();
        }
    }
}