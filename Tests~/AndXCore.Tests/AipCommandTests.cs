using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Parful.AndX.Aip;
using Xunit;

namespace Parful.AndX.Tests
{
    public sealed class AipCommandTests
    {
        [Fact]
        public async Task Dispatcher_Preserves_Action_Args_And_Lease()
        {
            AipCommand captured = null;
            var dispatcher = new AipCommandDispatcher(command =>
            {
                captured = command;
                return Task.FromResult(AipCommandResult.Success(new JObject { ["accepted"] = true }));
            });
            var request = new AipServerRequest
            {
                Name = "command.invoke",
                SessionId = "session-1",
                Raw = new JObject { ["idempotencyKey"] = "cmd-1" },
                Payload = new JObject
                {
                    ["action"] = "exhibit.control.set",
                    ["leaseId"] = "lease-1",
                    ["args"] = new JObject { ["channel"] = "DO_16", ["value"] = 1 }
                }
            };

            var result = await dispatcher.DispatchAsync(request);

            Assert.True(result.Ok);
            Assert.Equal("exhibit.control.set", captured.Action);
            Assert.Equal("DO_16", (string)captured.Args["channel"]);
            Assert.Equal(1, (int)captured.Args["value"]);
            Assert.Equal("lease-1", captured.LeaseId);
            Assert.Equal("session-1", captured.SessionId);
            Assert.Equal("cmd-1", captured.IdempotencyKey);
        }

        [Fact]
        public async Task Dispatcher_Maps_Safety_Stop_To_Host_Command()
        {
            AipCommand captured = null;
            var dispatcher = new AipCommandDispatcher(command =>
            {
                captured = command;
                return Task.FromResult(AipCommandResult.Success(new JObject { ["dispatched"] = true }));
            });

            var result = await dispatcher.DispatchAsync(new AipServerRequest
            {
                Name = "safety.stop",
                Payload = new JObject { ["reason"] = "focus lost" },
                Raw = new JObject { ["idempotencyKey"] = "safety-1" }
            });

            Assert.True(result.Ok);
            Assert.Equal("safety.stop", captured.Action);
            Assert.Equal("safety-1", captured.IdempotencyKey);
        }

        [Fact]
        public async Task Dispatcher_Rejects_Unknown_Or_Missing_Request()
        {
            var calls = 0;
            var dispatcher = new AipCommandDispatcher(_ =>
            {
                calls++;
                return Task.FromResult(AipCommandResult.Success());
            });

            var unknown = await dispatcher.DispatchAsync(new AipServerRequest { Name = "raw.io.write", Payload = new JObject() });
            var missing = await dispatcher.DispatchAsync(new AipServerRequest { Name = "command.invoke", Payload = new JObject() });

            Assert.False(unknown.Ok);
            Assert.Equal("AIP_UNSUPPORTED_PROTOCOL", unknown.ErrorCode);
            Assert.False(missing.Ok);
            Assert.Equal("AIP_INVALID_REQUEST", missing.ErrorCode);
            Assert.Equal(0, calls);
        }

        [Fact]
        public void ClientOptions_Require_Secure_Or_Test_Credentials()
        {
            var options = new AipClientOptions { Endpoint = "ws://127.0.0.1:19132/api/aip", DeviceId = "unity-test", TestMode = true };
            options.Validate();
            options.TestMode = false;
            Assert.Throws<ArgumentException>(() => options.Validate());
        }
    }
}