using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Parful.AndX.Aip;
using Xunit;

namespace Parful.AndX.Tests
{
    public sealed class ExhibitRuntimeTests
    {
        [Fact]
        public async Task Start_Playing_Stop_reports_monotonic_state_and_deduplicates_command()
        {
            var adapter = new FakeAdapter();
            var publisher = new RecordingPublisher();
            var runtime = Runtime(adapter, publisher);

            var start = await runtime.HandleCommandAsync(Command("start", "idem-start-1"));
            Assert.True(start.Ok);
            Assert.Equal("STARTING", runtime.Snapshot.State);
            Assert.Equal(1, adapter.StartCalls);

            var duplicate = await runtime.HandleCommandAsync(Command("start", "idem-start-1"));
            Assert.True(duplicate.Ok);
            Assert.True((bool)duplicate.Data["duplicate"]);
            Assert.Equal(1, adapter.StartCalls);

            Assert.True(await runtime.NotifyPlayingAsync("A001", "exe-1"));
            Assert.Equal("PLAYING", runtime.Snapshot.State);

            var stop = await runtime.HandleCommandAsync(Command("safety.stop", "idem-stop-1"));
            Assert.True(stop.Ok);
            Assert.Equal("IDLE", runtime.Snapshot.State);
            Assert.Equal(1, adapter.StopCalls);
            Assert.Equal(new[] { "STARTING", "PLAYING", "IDLE" }, publisher.States);
            Assert.Equal(new[] { 1, 2, 3 }, publisher.Revisions);
            runtime.Dispose();
        }

        [Fact]
        public async Task Safety_stop_cancels_an_inflight_start_before_waiting_for_command_gate()
        {
            var adapter = new FakeAdapter
            {
                StartHandler = async (_, cancellationToken) => await Task.Delay(Timeout.Infinite, cancellationToken)
            };
            var publisher = new RecordingPublisher();
            var runtime = Runtime(adapter, publisher);

            var startTask = runtime.HandleCommandAsync(Command("start", "idem-start-cancel"));
            await WaitUntil(() => adapter.StartEntered, TimeSpan.FromSeconds(2));
            var stop = await runtime.HandleCommandAsync(Command("safety.stop", "idem-stop-cancel"));
            var start = await startTask;

            Assert.False(start.Ok);
            Assert.True(stop.Ok);
            Assert.True(adapter.StartCanceled);
            Assert.True(adapter.StopCalls >= 1);
            Assert.Equal("IDLE", runtime.Snapshot.State);
            runtime.Dispose();
        }

        [Fact]
        public async Task Startup_timeout_attempts_safe_stop_and_marks_error()
        {
            var adapter = new FakeAdapter();
            var publisher = new RecordingPublisher();
            var runtime = Runtime(adapter, publisher, TimeSpan.FromMilliseconds(50));

            var start = await runtime.HandleCommandAsync(Command("start", "idem-start-timeout"));
            Assert.True(start.Ok);
            await WaitUntil(() => runtime.Snapshot.State == "ERROR", TimeSpan.FromSeconds(2));

            Assert.Equal("ERROR", runtime.Snapshot.State);
            Assert.Equal("EXHIBIT_START_TIMEOUT", runtime.Snapshot.ErrorCode);
            Assert.Equal(1, adapter.StopCalls);
            Assert.Contains("ERROR", publisher.States);
            runtime.Dispose();
        }

        [Fact]
        public async Task Pending_record_from_previous_process_requires_manual_review()
        {
            var directory = Path.Combine(Path.GetTempPath(), "andx-runtime-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "commands.json");
            Directory.CreateDirectory(directory);
            try
            {
                var writer = new FileExhibitCommandStore(path);
                await writer.SaveAsync(new ExhibitCommandRecord
                {
                    Key = "idem-crash",
                    Action = "start",
                    CommandId = "exe-crash",
                    OrderNo = "A001",
                    CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                    Pending = true
                });
                writer.Dispose();
                var store = new FileExhibitCommandStore(path);
                var adapter = new FakeAdapter();
                var runtime = Runtime(adapter, new RecordingPublisher(), commandStore: store);

                var result = await runtime.HandleCommandAsync(Command("start", "idem-crash"));

                Assert.False(result.Ok);
                Assert.Equal("AIP_COMMAND_PENDING_REVIEW", result.ErrorCode);
                Assert.Equal(0, adapter.StartCalls);
                runtime.Dispose();
                store.Dispose();
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Fact]
        public async Task Capability_handler_runs_only_in_playing_and_deduplicates_command()
        {
            var adapter = new FakeAdapter();
            var publisher = new RecordingPublisher();
            var handler = new RecordingCapabilityHandler("qiuqian.draw");
            var runtime = Runtime(adapter, publisher, capabilityHandlers: new IExhibitCapabilityHandler[] { handler });

            Assert.True((await runtime.HandleCommandAsync(Command("start", "idem-cap-start"))).Ok);
            var beforePlaying = await runtime.HandleCommandAsync(Command("qiuqian.draw", "idem-cap-early"));
            Assert.False(beforePlaying.Ok);
            Assert.Equal("EXHIBIT_BUSY", beforePlaying.ErrorCode);

            Assert.True(await runtime.NotifyPlayingAsync());
            var draw = await runtime.HandleCommandAsync(Command("qiuqian.draw", "idem-cap-draw"));
            Assert.True(draw.Ok);
            Assert.Equal(1, handler.Calls);

            var duplicate = await runtime.HandleCommandAsync(Command("qiuqian.draw", "idem-cap-draw"));
            Assert.True(duplicate.Ok);
            Assert.True((bool)duplicate.Data["duplicate"]);
            Assert.Equal(1, handler.Calls);
            runtime.Dispose();
        }

        [Fact]
        public async Task Reusing_idempotency_key_for_another_action_is_rejected()
        {
            var adapter = new FakeAdapter();
            var runtime = Runtime(adapter, new RecordingPublisher());
            Assert.True((await runtime.HandleCommandAsync(Command("start", "idem-conflict"))).Ok);

            var conflict = await runtime.HandleCommandAsync(Command("safety.stop", "idem-conflict"));

            Assert.False(conflict.Ok);
            Assert.Equal("EXHIBIT_COMMAND_CONFLICT", conflict.ErrorCode);
            runtime.Dispose();
        }

        private static ExhibitRuntime Runtime(
            FakeAdapter adapter,
            RecordingPublisher publisher,
            TimeSpan? timeout = null,
            IExhibitCommandStore commandStore = null,
            IExhibitCapabilityHandler[] capabilityHandlers = null)
        {
            var runtime = new ExhibitRuntime();
            runtime.Configure(new ExhibitRuntimeOptions
            {
                ExhibitId = "exhibit-qiuqianji",
                Adapter = adapter,
                StatePublisher = publisher,
                CommandStore = commandStore ?? new InMemoryExhibitCommandStore(),
                CapabilityHandlers = capabilityHandlers ?? new IExhibitCapabilityHandler[0],
                StartConfirmationTimeout = timeout ?? TimeSpan.FromSeconds(5)
            });
            return runtime;
        }

        private static AipCommand Command(string action, string idempotencyKey)
        {
            return new AipCommand
            {
                Action = action,
                SessionId = "session-1",
                IdempotencyKey = idempotencyKey,
                Args = new JObject
                {
                    ["orderNo"] = "A001",
                    ["commandId"] = "exe-1"
                },
                Request = new JObject
                {
                    ["id"] = "request-" + idempotencyKey,
                    ["meta"] = new JObject
                    {
                        ["orderNo"] = "A001",
                        ["commandId"] = "exe-1",
                        ["capabilityKey"] = action
                    }
                }
            };
        }

        private static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout)
        {
            var started = DateTimeOffset.UtcNow;
            while (!predicate())
            {
                if (DateTimeOffset.UtcNow - started > timeout) throw new TimeoutException();
                await Task.Delay(10);
            }
        }

        private sealed class FakeAdapter : IExhibitRuntimeAdapter
        {
            public int StartCalls;
            public int StopCalls;
            public bool StartCanceled;
            public bool StartEntered;
            public Func<ExhibitRuntimeCommand, CancellationToken, Task> StartHandler;

            public async Task StartAsync(ExhibitRuntimeCommand command, CancellationToken cancellationToken)
            {
                StartCalls++;
                StartEntered = true;
                if (StartHandler == null) return;
                try
                {
                    await StartHandler(command, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    StartCanceled = true;
                    throw;
                }
            }

            public Task StopAsync(ExhibitRuntimeCommand command, CancellationToken cancellationToken)
            {
                StopCalls++;
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingCapabilityHandler : IExhibitCapabilityHandler
        {
            public RecordingCapabilityHandler(string capability)
            {
                Capability = capability;
            }

            public string Capability { get; }
            public int Calls;

            public Task<AipCommandResult> ExecuteAsync(
                ExhibitRuntimeCommand command,
                CancellationToken cancellationToken)
            {
                Calls += 1;
                return Task.FromResult(AipCommandResult.Success(new JObject
                {
                    ["capability"] = command.Action,
                    ["orderNo"] = command.OrderNo
                }));
            }
        }

        private sealed class RecordingPublisher : IExhibitStatePublisher
        {
            public readonly List<string> States = new List<string>();
            public readonly List<int> Revisions = new List<int>();

            public Task PublishAsync(ExhibitRuntimeSnapshot snapshot, CancellationToken cancellationToken)
            {
                States.Add(snapshot.State);
                Revisions.Add(snapshot.Revision);
                return Task.CompletedTask;
            }
        }
    }
}