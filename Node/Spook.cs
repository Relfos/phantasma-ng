using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Nethereum.Hex.HexConvertors.Extensions;
using Phantasma.Business;
using Phantasma.Core;
using Phantasma.Infrastructure;
using Phantasma.Infrastructure.Chains;
using Phantasma.Shared;
using Phantasma.Spook.Chains;
using Phantasma.Spook.Command;
using Phantasma.Spook.Converters;
using Phantasma.Spook.Interop;
using Phantasma.Spook.Oracles;
using Phantasma.Spook.Shell;
using Phantasma.Spook.Utils;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Tendermint.Abci;
using EthAccount = Nethereum.Web3.Accounts.Account;
using NeoAPI = Phantasma.Spook.Chains.NeoAPI;

namespace Phantasma.Spook
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ModuleAttribute : Attribute
    {
        public readonly string Name;

        public ModuleAttribute(string name)
        {
            Name = name;
        }
    }

    public class Spook : Runnable
    {
        public static string Version { get; private set; }
        public static string TxIdentifier => $"SPK{Version}";

        private PhantasmaKeys _nodeKeys;
        private bool _nodeReady = false;
        private List<string> _seeds = new List<string>();
        private NeoAPI _neoAPI;
        private EthAPI _ethAPI;
        private CommandDispatcher _commandDispatcher;
        private string _cryptoCompareAPIKey = null;
        private Thread _tokenSwapperThread;
        private Process _tendermintProcess;

        public NeoAPI NeoAPI { get { return _neoAPI; } }
        public EthAPI EthAPI { get { return _ethAPI; } }
        public string CryptoCompareAPIKey  { get { return _cryptoCompareAPIKey; } }
        public PhantasmaKeys NodeKeys { get { return _nodeKeys; } }
        public ABCIConnector ABCIConnector { get; private set; }

        public Spook()
        {
            this.ABCIConnector = new ABCIConnector();
        }

        protected override void OnStart()
        {
            Log.Information($"Starting Tendermint Engine");

            SetupTendermint();

            Log.Information($"Starting ABCI Application");

            var server = new Server()
            {
                Ports = { new ServerPort("127.0.0.1", 26658, ServerCredentials.Insecure) },
                Services = { ABCIApplication.BindService(this.ABCIConnector) },
            };

            server.Start();

            Log.Information($"Server is up & running ()");

            //ShutdownAwaiter();

            //_logger.LogInformation("Shutting down...");
            //server.ShutdownAsync().Wait();
            //_logger.LogInformation("Done shutting down...");
            //
            // NEW NEW NEW NEW NEW 

            Console.CancelKeyPress += delegate
            {
                this.Terminate();
            };

            ValidateConfig();

            Version = Assembly.GetAssembly(typeof(Spook)).GetVersion();

            _nodeKeys = SetupNodeKeys();

            if (!SetupNexus())
            {
                this.OnStop();
                return;
            }

            SetupOracleApis();

            if (Settings.Default.Node.HasMempool && !Settings.Default.Node.Readonly)
            {
                SetupMempool();
            }

            SetupNexusApi();

            _commandDispatcher = SetupCommandDispatcher();

            MakeReady(_commandDispatcher);

            if (!string.IsNullOrEmpty(Settings.Default.Oracle.Swaps))
            {
                StartTokenSwapper();
            }
        }

        public TokenSwapper StartTokenSwapper()
        {
            var platforms = Settings.Default.Oracle.Swaps.Split(',');
            var minimumFee = Settings.Default.Node.MinimumFee;
            var oracleSettings = Settings.Default.Oracle;
            var tokenSwapper = new TokenSwapper(this, _nodeKeys, _neoAPI, _ethAPI, minimumFee, platforms);
            NexusAPI.TokenSwapper = tokenSwapper;

            _tokenSwapperThread = new Thread(() =>
            {
                Log.Information("Running token swapping service...");
                while (Running)
                {
                    Log.Debug("Update TokenSwapper now");
                    Task.Delay(5000).Wait();
                    if (_nodeReady)
                    {
                        tokenSwapper.Update();
                    }
                }
            });

            _tokenSwapperThread.Start();

            return tokenSwapper;
        }

        private CommandDispatcher SetupCommandDispatcher()
        {
            Log.Information("Initializing command dispatcher...");
            var dispatcher = new CommandDispatcher(this);
            Log.Information("Command dispatcher initialized successfully.");
            return dispatcher;
        }


        private String prompt { get; set; } = "spook> ";

        private CommandDispatcher _dispatcher;

        public void MakeReady(CommandDispatcher dispatcher)
        {
            var nodeMode = Settings.Default.Node.Mode;
            Log.Information($"Node is now running in {nodeMode.ToString().ToLower()} mode!");
            _nodeReady = true;
        }

        private string PromptGenerator()
        {
            var height = this.ExecuteAPIR("getBlockHeight", new string[] { "main" });
            return string.Format(prompt, height.Trim(new char[] { '"' }));
        }

        private void SetupOracleApis()
        {
            var neoScanURL = Settings.Default.Oracle.NeoscanUrl;

            var neoRpcList = Settings.Default.Oracle.NeoRpcNodes;
            this._neoAPI = new RemoteRPCNode(neoScanURL, neoRpcList.ToArray());
            this._neoAPI.SetLogger((s) => Log.Information(s));

            var ethRpcList = Settings.Default.Oracle.EthRpcNodes;
            
            var ethWIF = Settings.Default.GetInteropWif(_nodeKeys, EthereumWallet.EthereumPlatform);
            var ethKeys = PhantasmaKeys.FromWIF("L4GcHJVrUPz6nW2EKJJGV2yxfa5UoaG8nfnaTAgzmWyuAmt3BYKg");

            this._ethAPI = new EthAPI(new EthAccount(ethKeys.PrivateKey.ToHex()));
            this._cryptoCompareAPIKey = Settings.Default.Oracle.CryptoCompareAPIKey;
            if (!string.IsNullOrEmpty(this._cryptoCompareAPIKey))
            {
                Log.Information($"CryptoCompare API enabled.");
            }
            else
            {
                Log.Warning($"CryptoCompare API key missing, oracles won't work properly...");
            }
        }

        private PhantasmaKeys SetupNodeKeys()
        {
            PhantasmaKeys nodeKeys = PhantasmaKeys.FromWIF(Settings.Default.Node.NodeWif);;
            //TODO wallet module?

            return nodeKeys;
        }

        private bool SetupTendermint()
        {
            // TODO: Platform-specific path
            var tendermintPath = "tendermint";

            if (!File.Exists(tendermintPath))
            {
                Log.Error("Could not find Tendermint binary, make sure its next to Phantasma");
                return false;
            }

            var launchArgs = new[]
            {
                "start",
                "--abci grpc",
                 "--proxy_app 127.0.0.1:44900"
            };

            _tendermintProcess = new Process();
            _tendermintProcess.StartInfo = new ProcessStartInfo
            {
                FileName = tendermintPath,
                Arguments = string.Join(' ', launchArgs),
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            _tendermintProcess.OutputDataReceived += (sender, a) =>
                Console.WriteLine(a.Data);
            _tendermintProcess.Start();
            _tendermintProcess.BeginOutputReadLine();

            return true;
        }

        private bool SetupNexus()
        {
            Log.Information("Setting up nexus...");
            var storagePath = Settings.Default.Node.StoragePath;
            var oraclePath = Settings.Default.Node.OraclePath;
            var nexusName = Settings.Default.Node.NexusName;

            switch (Settings.Default.Node.StorageBackend)
            {
                case StorageBackendType.CSV:
                    Log.Information("Setting CSV nexus...");
                    NexusAPI.Nexus = new Nexus(nexusName, (name) => new BasicDiskStore(storagePath + name + ".csv"));
                    break;

                case StorageBackendType.RocksDB:
                    Log.Information("Setting RocksDB nexus...");
                    NexusAPI.Nexus = new Nexus(nexusName, (name) => new DBPartition(storagePath + name));
                    break;
                default:
                    throw new Exception("Backend has to be set to either \"db\" or \"file\"");
            }

            Log.Information("Nexus is set");

            NexusAPI.Nexus.SetOracleReader(new SpookOracle(this, NexusAPI.Nexus));

            return true;
        }

        private void SetupMempool()
        {
            var mempool = new Mempool(NexusAPI.Nexus
                    , Settings.Default.Node.BlockTime
                    , Settings.Default.Node.MinimumFee
                    , System.Text.Encoding.UTF8.GetBytes(TxIdentifier)
                    , 0
                    , Settings.Default.Node.ProfilerPath
                    );

            if (Settings.Default.Node.MempoolLog)
            {
                mempool.OnTransactionFailed += Mempool_OnTransactionFailed;
                mempool.OnTransactionAdded += (hash) => Log.Information($"Received transaction {hash}");
                mempool.OnTransactionCommitted += (hash) => Log.Information($"Commited transaction {hash}");
                mempool.OnTransactionDiscarded += (hash) => Log.Information($"Discarded transaction {hash}");
            }
            if (Settings.Default.App.NodeStart)
            {
                mempool.StartInThread(ThreadPriority.AboveNormal);
            }
            
            NexusAPI.Mempool = mempool;
        }

        private void Mempool_OnTransactionFailed(Hash hash)
        {
            if (!Running || NexusAPI.Mempool == null)
            {
                return;
            }

            var status = NexusAPI.GetMempool().GetTransactionStatus(hash, out string reason);
            Log.Warning($"Rejected transaction {hash} => " + reason);
        }

        private void SetupNexusApi()
        {
            Log.Information($"Initializing nexus API...");

            var readOnlyMode = Settings.Default.Node.Readonly;

            NexusAPI.ApiLog = Settings.Default.Node.ApiLog;

            if (readOnlyMode)
            {
                Log.Warning($"Node will be running in read-only mode.");
            }
        }

        private static JsonSerializerOptions GetDefaultSerializerOptions()
        {
            return new JsonSerializerOptions
            {
                IncludeFields = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters = { new EnumerableJsonConverterFactory() }
            };
        }

        private void ValidateConfig()
        {
            if (Settings.Default.Node.ApiProxyUrl != null)
            {
                if (!Settings.Default.Node.ApiCache)
                {
                    throw new Exception("A proxy node must have api cache enabled.");
                }

                // TEMP commented for now, "Normal" node needs a proxy url to relay transactions to the BPs
                //if (Settings.Node.Mode != NodeMode.Proxy)
                //{
                //    throw new Exception($"A {Settings.Node.Mode.ToString().ToLower()} node cannot have a proxy url specified.");
                //}

                if (!Settings.Default.Node.HasRpc && !Settings.Default.Node.HasRest)
                {
                    throw new Exception("API proxy must have REST or RPC enabled.");
                }
            }
            else
            {
                if (Settings.Default.Node.Mode == NodeMode.Proxy)
                {
                    throw new Exception($"A {Settings.Default.Node.Mode.ToString().ToLower()} node must have a proxy url specified.");
                }
            }

            if (!Settings.Default.Node.IsValidator && !string.IsNullOrEmpty(Settings.Default.Oracle.Swaps))
            {
                    throw new Exception("Non-validator nodes cannot have swaps enabled");
            }


            // TODO to be continued...
        }


        protected override bool Run()
        {

            if (Settings.Default.App.UseShell)
            {
                _dispatcher = new CommandDispatcher(this);

                List<string> completionList = new List<string>();

                if (!string.IsNullOrEmpty(Settings.Default.App.Prompt))
                {
                    prompt = Settings.Default.App.Prompt;
                }

                var startupMsg = "Spook shell " + Version + "\nLogs are stored in " + Settings.Default.Log.LogPath + "\nTo exit use <ctrl-c> or \"exit\"!\n";

                Prompt.Run(
                    ((command, listCmd, list) =>
                    {
                        string command_main = command.Trim().Split(new char[] { ' ' }).First();

                        if (!_dispatcher.OnCommand(command))
                        {
                            Console.WriteLine("error: Command not found");
                        }

                        return "";
                    }), prompt, PromptGenerator, startupMsg, Path.GetTempPath() + Settings.Default.App.History, _dispatcher.Verbs);
            }
            else
            {
                // Do nothing in this thread...
                Thread.Sleep(1000);
            }

            return this.Running;
        }

        protected override void OnStop()
        {
            Log.Information("Termination started...");

            if (NexusAPI.Mempool != null && NexusAPI.Mempool.IsRunning)
            {
                Log.Information("Stopping mempool...");
                NexusAPI.Mempool.Stop();
            }

            //if (_node != null && _node.IsRunning)
            //{
            //    Logger.Message("Stopping node...");
            //    _node.Stop();
            //}
        }

        public void Terminate()
        {
            if (!Running)
            {
                Log.Information("Termination already in progress...");
            }

            if (Prompt.running)
            {
                Prompt.running = false;
            }

            this.OnStop();

            //Thread.Sleep(3000);
            if (Prompt.running)
            {
                Prompt.running = false;
            }

            Log.Information("Termination complete...");
            Environment.Exit(0);
        }

        public string ExecuteAPIR(string name, string[] args)
        {
            // TODO fix
            /*var result = _nexusApi.Execute(name, args);
            if (result == null)
            {
                return "";
            }

            return result;*/
            return null;
        }

        public void ExecuteAPI(string name, string[] args)
        {
            // TODO fix
            /*
            var result = _nexusApi.Execute(name, args);
            if (result == null)
            {
                Logger.Warning("API returned null value...");
                return;
            }

            Logger.Information(result);*/
        }
    }
}