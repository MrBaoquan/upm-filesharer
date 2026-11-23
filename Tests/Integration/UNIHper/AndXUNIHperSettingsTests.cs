using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Parful.AndX;
using Parful.AndX.Core;
using Parful.AndX.Integration.UNIHper;
using NUnit.Framework;
using UNIHper;

namespace Parful.AndX.Tests
{
    public sealed class AndXUNIHperSettingsTests
    {
        [Test]
        public void Defaults_use_auto_initialization_and_auto_mode()
        {
            var settings = new AndXUNIHperSettings();

            Assert.IsTrue(settings.AutoInitialize);
            Assert.AreEqual("Auto", settings.AccessMode);
            Assert.AreEqual(30f, settings.TimeoutSeconds);
            Assert.AreEqual(2, settings.MaxRetries);
            Assert.AreEqual(8L * 1024 * 1024, settings.ChunkSize);
            Assert.IsFalse(settings.AllowInsecureHttp);
        }

        [Test]
        public void Config_path_uses_UNIHper_AppPath_Auto_on_editor_and_desktop()
        {
            var attribute = (SerializedAt)Attribute.GetCustomAttribute(
                typeof(AndXUNIHperSettings),
                typeof(SerializedAt)
            );

            Assert.IsNotNull(attribute);
#if UNITY_WEBGL && !UNITY_EDITOR
            Assert.AreEqual(AppPath.PersistentDir, attribute.RootDir);
#else
            Assert.AreEqual(AppPath.Auto, attribute.RootDir);
#endif
            Assert.AreEqual("Configs", attribute.SubDir);
            Assert.AreEqual("AndX.xml", attribute.FileName);
        }

        [Test]
        public void UNIHper_scanner_discovers_settings_type()
        {
            AssemblyConfig.Refresh();

            var configTypes = AssemblyConfig.GetSubClasses(typeof(UConfig));

            CollectionAssert.Contains(configTypes, typeof(AndXUNIHperSettings));
        }
        [Test]
        public void OnLoaded_auto_initializes_from_settings_lifecycle()
        {
            var previousFactory = AndXTransportProvider.Factory;
            AndXTransportProvider.Factory = () => new NullTransport();
            global::AndX.Config.Reset();
            try
            {
                var settings = new AndXUNIHperSettings
                {
                    AutoInitialize = true,
                    AccessMode = "Edge",
                };

                typeof(UConfig)
                    .GetMethod("Loaded", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(settings, null);

                Assert.IsTrue(global::AndX.Config.IsConfigured);
                Assert.AreEqual(AndXAccessMode.Edge, global::AndX.Config.ActiveMode);
            }
            finally
            {
                global::AndX.Config.Reset();
                AndXTransportProvider.Factory = previousFactory;
            }
        }

        [Test]
        public void OnLoaded_respects_auto_initialize_false()
        {
            global::AndX.Config.Reset();
            var settings = new AndXUNIHperSettings
            {
                AutoInitialize = false,
                AccessMode = "Edge",
            };

            typeof(UConfig)
                .GetMethod("Loaded", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(settings, null);

            Assert.IsFalse(global::AndX.Config.IsConfigured);
        }

        [Test]
        public void CreateOptions_maps_supported_fields_and_trims_text()
        {
            var settings = new AndXUNIHperSettings
            {
                AccessMode = " edge ",
                Endpoint = " http://127.0.0.1:6699 ",
                ExhibitId = " 1024 ",
                NodeId = " DEV-001 ",
                NodeSecret = " node-secret-for-test ",
                EdgeKey = " edge-key-for-test ",
                AccessToken = " access-token-for-test ",
                EnrollmentToken = " enrollment-token-for-test ",
                TimeoutSeconds = 12f,
                MaxRetries = 4,
                ChunkSize = 1024L,
                AllowInsecureHttp = true,
            };

            var options = settings.CreateOptions();

            Assert.AreEqual(AndXAccessMode.Edge, options.AccessMode);
            Assert.AreEqual("http://127.0.0.1:6699", options.Endpoint);
            Assert.AreEqual("1024", options.ExhibitId);
            Assert.AreEqual("DEV-001", options.NodeId);
            Assert.IsNotNull(options.NodeCredential);
            Assert.AreEqual("DEV-001", options.NodeCredential.NodeId);
            Assert.AreEqual("node-secret-for-test", options.NodeCredential.Secret);
            Assert.AreEqual("edge-key-for-test", options.EdgeKey);
            Assert.AreEqual("access-token-for-test", options.AccessToken);
            Assert.AreEqual("enrollment-token-for-test", options.EnrollmentToken);
            Assert.AreEqual(TimeSpan.FromSeconds(12), options.Timeout);
            Assert.AreEqual(4, options.MaxRetries);
            Assert.AreEqual(1024L, options.ChunkSize);
            Assert.IsTrue(options.AllowInsecureHttp);
        }

        [Test]
        public void CreateOptions_rejects_node_secret_without_node_id()
        {
            var settings = new AndXUNIHperSettings
            {
                NodeSecret = "node-secret-for-test",
            };

            var ex = Assert.Throws<AndXException>(() => settings.CreateOptions());

            Assert.AreEqual(AndXContract.SdkErrorCodes.Configuration, ex.Code);
            StringAssert.Contains("NodeId", ex.Message);
        }

        [Test]
        public void CreateOptions_leaves_node_credential_empty_when_secret_not_configured()
        {
            var settings = new AndXUNIHperSettings { NodeId = "DEV-001" };

            var options = settings.CreateOptions();

            Assert.IsNull(options.NodeCredential);
            Assert.AreEqual("DEV-001", options.NodeId);
        }

        [Test]
        public void CreateOptions_rejects_unknown_access_mode()
        {
            var settings = new AndXUNIHperSettings { AccessMode = "unsupported" };

            var ex = Assert.Throws<AndXException>(() => settings.CreateOptions());

            Assert.AreEqual(AndXContract.SdkErrorCodes.Configuration, ex.Code);
            StringAssert.Contains("AccessMode", ex.Message);
        }

        [Test]
        public void CreateOptions_normalizes_unsafe_numeric_values()
        {
            var settings = new AndXUNIHperSettings
            {
                TimeoutSeconds = 0f,
                MaxRetries = -1,
                ChunkSize = 0L,
            };

            var options = settings.CreateOptions();

            Assert.AreEqual(TimeSpan.FromSeconds(30), options.Timeout);
            Assert.AreEqual(0, options.MaxRetries);
            Assert.AreEqual(8L * 1024 * 1024, options.ChunkSize);
        }
        private sealed class NullTransport : IAndXTransport
        {
            public Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new TransportResponse { StatusCode = 200, Text = "{\"code\":0,\"data\":null}" });
            }
        }

    }
}