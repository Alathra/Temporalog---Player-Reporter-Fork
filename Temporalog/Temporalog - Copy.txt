using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HarmonyLib;
using Temporalog.Config;
using Temporalog.InfluxDB;
using Vintagestory.GameContent;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;

using static HarmonyLib.Code;



namespace Temporalog;

internal class Temporalog : ModSystem
{
    private readonly Harmony _harmony;

    private long _writeDataListenerId;

    public static Temporalog? Instance { get; private set; }

    private const string HarmonyPatchKey = "Temporalog.Patch";

    private InfluxDbClient? _client;

    private ICoreServerAPI _sapi = null!;

    //private ModSystemBlockReinforcement bre; // Block reinforcement modsystem


    public static TermporalogConfig Config = null!;

    private ServerMain _server = null!;

    private Process? _vsProcess;

    private List<PointData> _data;
    private List<PointData>? _dataOnline;
    
    private readonly string _configFile = "TemporalogConfig.json";

    public Temporalog()
    {
        _harmony = new Harmony(HarmonyPatchKey);
        _data = new List<PointData>();
        Instance = this;
    }
    
    public override void StartServerSide(ICoreServerAPI sapi)
    {
        _sapi = sapi;

        try
        {
            Config = _sapi.LoadModConfig<TermporalogConfig>(_configFile);

            if (Config == null)
            {
                Config = new TermporalogConfig();
                _sapi.StoreModConfig(Config, _configFile);

                _sapi.Server.LogWarning(
                    $"Config file {_configFile} was missing created new one at {Path.Combine(GamePaths.ModConfig, _configFile)}");
                _sapi.Server.LogWarning("Mod disabled");
                return;
            }
        }
        catch (Exception e)
        {
            _sapi.Logger.Error(e);
            _sapi.Server.LogWarning("Mod disabled");
            return;
        }

        _server = (ServerMain)_sapi.World;
        //bre = sapi.ModLoader.GetModSystem<ModSystemBlockReinforcement>();

        _vsProcess = Process.GetCurrentProcess();

        _client = new InfluxDbClient(Config, sapi);
        if (!_client.HasConnection())
        {
            _client.TryReconnect();
        }

        _server.ModEventManager.TriggerGameWorldBeingSaved();
        
        // var original = typeof(ServerSystemAutoSaveGame).GetMethod("doAutoSave", BindingFlags.NonPublic | BindingFlags.Instance);
        // var prefix =
        //     new HarmonyMethod(typeof(PatchServerEventManager).GetMethod(nameof(PatchServerEventManager.Prefix)));
        // var postfix =
        //     new HarmonyMethod(typeof(PatchServerEventManager).GetMethod(nameof(PatchServerEventManager.Postfix)));
        // _harmony.Patch(original, prefix: prefix, postfix: postfix);
        
        PatchFrameProfilerUtil.Patch(_harmony);
        PatchAdminLogging.Patch(_harmony);

        _sapi.Logger.EntryAdded += LogEntryAdded;
        _sapi.Event.DidPlaceBlock += OnDidPlaceBlock;
        _sapi.Event.DidBreakBlock += OnDidBreakBlock;
        _sapi.Event.PlayerDeath += PlayerDeath;

        _writeDataListenerId = _sapi.Event.RegisterGameTickListener(WriteOnline, 10000);
        _writeDataListenerId = _sapi.Event.RegisterGameTickListener(WriteData, Config.DataCollectInterval);
    }

    // public class PatchServerEventManager
    // {
    //     public static void Prefix(out Stopwatch __state)
    //     {
    //         Console.WriteLine("Start Saving");
    //         __state = new Stopwatch(); // assign your own state
    //         __state.Start();
    //     }
    //
    //     public static void Postfix(Stopwatch __state)
    //     {
    //         __state.Stop();
    //         Console.WriteLine($"Saving took {__state.Elapsed.ToString()}");
    //     }
    // }

    private void OnDidBreakBlock(IServerPlayer byPlayer, int oldBlockId, BlockSelection blockSel)
    {
        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) return;
        var pointData = PointData.Measurement("playerlogcrbreak").Tag("player", byPlayer.PlayerName.ToLower())
            .Tag("playerUID", byPlayer.PlayerUID).Tag("position", blockSel.Position.ToString()).Field("value",
                $"{_sapi.World.Blocks[oldBlockId].Code} {blockSel.Position}");
        WritePoint(pointData, WritePrecision.Ms);
    }

    private void OnDidPlaceBlock(IServerPlayer byPlayer, int oldBlockId, BlockSelection blockSel,
        ItemStack withItemStack)
    {
        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) return;
        var pointData = PointData.Measurement("playerlogcrplace").Tag("player", byPlayer.PlayerName.ToLower())
            .Tag("playerUID", byPlayer.PlayerUID).Tag("position", blockSel.Position.ToString()).Field("value",
                $"{withItemStack.Collectible?.Code} {blockSel.Position}");
        WritePoint(pointData, WritePrecision.Ms);
    }

    private void LogEntryAdded(EnumLogType logType, string message, object[] args)
    {
        switch (logType)
        {
            case EnumLogType.Chat:
                break;
            case EnumLogType.Event:
                break;
            case EnumLogType.StoryEvent:
                break;
            case EnumLogType.Build:
                break;
            case EnumLogType.VerboseDebug:
                break;
            case EnumLogType.Debug:
                break;
            case EnumLogType.Notification:
                break;
            case EnumLogType.Warning:
            {
                var msg = string.Format(message, args);
                if (msg.Contains("Server overloaded"))
                {
                    WritePoint(PointData.Measurement("overloadwarnings").Field("value", msg));
                }
                else
                {
                    WritePoint(PointData.Measurement("warnings").Field("value", msg));
                }

                break;
            }
            case EnumLogType.Error:
            case EnumLogType.Fatal:
            {
                WritePoint(PointData.Measurement("errors").Field("value", string.Format(message, args)));
                break;
            }
            case EnumLogType.Audit:
            {
                //PLAYER REPORTER : DATA TO INFLUX DB
                WritePoint(PointData.Measurement("audit").Field("value", string.Format(message, args)));
                break;
            }
        }
    }

    private void WriteData(float t1)
    {
        _data = new List<PointData>();

        var activeEntities =
            _sapi.World.LoadedEntities.Count(loadedEntity => loadedEntity.Value.State != EnumEntityState.Inactive);
        _data.Add(PointData.Measurement("entitiesActive").Field("value", activeEntities));

        var statsCollection =
            _server.StatsCollector[GameMath.Mod(_server.StatsCollectorIndex - 1, _server.StatsCollector.Length)];
        if (statsCollection.ticksTotal > 0)
        {
            _data.Add(PointData.Measurement("l2avgticktime").Field("value",
                (double)statsCollection.tickTimeTotal / statsCollection.ticksTotal));
            _data.Add(PointData.Measurement("l2stickspersec").Field("value", statsCollection.ticksTotal / 2.0));
        }

        _data.Add(PointData.Measurement("packetspresec").Field("value", statsCollection.statTotalPackets / 2.0));
        _data.Add(PointData.Measurement("kilobytespersec").Field("value",
            decimal.Round((decimal)(statsCollection.statTotalPacketsLength / 2048.0), 2,
                MidpointRounding.AwayFromZero)));

        _vsProcess?.Refresh();
        var totalMemory = _vsProcess?.PrivateMemorySize64 / 1048576;
        var managedMemory = GC.GetTotalMemory(false) / 1048576;
        
        _data.Add(PointData.Measurement("memory").Field("value", totalMemory ?? 0));
        _data.Add(PointData.Measurement("memoryManaged").Field("value", managedMemory));

        _data.Add(PointData.Measurement("threads").Field("value", _vsProcess?.Threads.Count ?? 0));

        _data.Add(PointData.Measurement("chunks").Field("value", _sapi.World.LoadedChunkIndices.Length));

        _data.Add(PointData.Measurement("entities").Field("value", _sapi.World.LoadedEntities.Count));

        _data.Add(PointData.Measurement("generatingChunks")
            .Field("value", _sapi.WorldManager.CurrentGeneratingChunkCount));
        WritePoints(_data);
    }   

    private void WriteOnline(float t1)
    {
        _dataOnline = new List<PointData>();

        foreach (var player in _sapi.World.AllOnlinePlayers.Cast<IServerPlayer>())
        {
            if (player.ConnectionState == EnumClientState.Playing)
            {
                _dataOnline.Add(PointData.Measurement("online").Tag("player", player.PlayerName.ToLower())
                    .Field("value", player.Ping));
            }
        }

        _dataOnline.Add(PointData.Measurement("clients").Field("value", _server.Clients.Count));
        WritePoints(_dataOnline);
    }

    public void WritePoints(List<PointData> data, WritePrecision? precision = null)
    {
        _client?.WritePoints(data, precision);
    }

    public void WritePoint(PointData data, WritePrecision? precision = null)
    {
        _client?.WritePoint(data, precision);
    }

    private void PlayerDeath(IServerPlayer byplayer, DamageSource damagesource)
    {
        var causeEntity = damagesource.GetCauseEntity();
        var playerName = causeEntity is EntityPlayer pl ? $"({pl.Player.PlayerName})" : causeEntity?.GetName();
        var source = $"{causeEntity?.GetType().Name} {playerName} [{damagesource.Type}] : {damagesource.Source}";
        WritePoint(PointData.Measurement("deaths").Tag("player", byplayer.PlayerName)
            .Field("value", source));
    }

    /// <summary>
    /// PLAYER REPORTER ADDITIONS.
    /// </summary>

    private void onUseEntity(Entity entity, IPlayer byPlayer, ItemSlot item, Vec3d hitPos, int mode, ref EnumHandling handling)
    {
        string click = mode > 0 ? "Right Click" : "Left Click";
        string itemName = item.GetStackName() != null ? item.GetStackName() : "Hand";
        _sapi.Logger.Audit("[{0}] {1} at {2} used {3} on {4} at {5}, mode {6}", EnumPlayerReport.EntityUse, byPlayer.PlayerName, byPlayer.Entity.Pos.XYZInt, itemName, entity.GetName(), entity.Pos.XYZInt, click);
        return;
    }

    private void onUseBlock(IServerPlayer byPlayer, BlockSelection blockSel) //This function is called by our event listener when a block breaks
    {
        BlockPos pos = blockSel.Position; //get the pos of the block
        string position = pos.ToString(); //Convert the pos into a readable string
        string itemUsedName = "None";
        string playerName = byPlayer.PlayerName;
        string blockName = _sapi.World.BlockAccessor.GetBlock(pos).GetPlacedBlockName(_sapi.World, pos); //Get the block name

        itemUsedName = byPlayer.InventoryManager.ActiveHotbarSlot?.GetStackName() ?? "Hand";
        _sapi.Logger.Audit("[{0}] Player: {1} used {2} on {3} at pos {4}",EnumPlayerReport.BlockUse, playerName, itemUsedName, blockName, position);

    }
    private void onBreakBlock(IServerPlayer byPlayer, BlockSelection blockSel, ref float dropQuantityMultiplier, ref EnumHandling handling) //This function is called by our event listener when a block breaks
    {
        //BlockData bdata = new BlockData(); //Initialize our BlockData class to hold our player and block info
        BlockPos pos = blockSel.Position; //get the pos of the block
        string position = pos.ToString(); //Convert the pos into a readable string
        string playerName = byPlayer.PlayerName; //Get the player's name
        string block = _sapi.World.GetBlock(blockSel.Block.BlockId).GetPlacedBlockName(_sapi.World, pos); //Get the block name
        //bdata.reinforced = bre.IsReinforced(pos);
        /*if (bdata.reinforced)
        {
            BlockReinforcement brdata = bre.GetReinforcment(pos);
            if (brdata.LastPlayername != null)
                bdata.reinforcePlayer = brdata.LastPlayername.ToString();
            else bdata.reinforcePlayer = "null";
            if (brdata.LastGroupname != null)
                bdata.reinforceGroup = brdata.LastGroupname.ToString();
            else bdata.reinforceGroup = "null";
            _sapi.Logger.Audit("[{0}] Player: {1} broke {2} at pos {3}. [RE] owner_player: {4}, owner_group: {5}, str: {6}", EnumPlayerReport.ReinforcedBlockBreak, bdata.player, bdata.block, position, bdata.reinforcePlayer, bdata.reinforceGroup, brdata.Strength);
            return;
        }*/
        _sapi.Logger.Audit("[{0}] Player: {1} broke {2} at pos {3}", EnumPlayerReport.BlockBreak, playerName, block, position);
    }



    public override void Dispose()
    {
        if (_client != null)
        {
            _sapi.Logger.EntryAdded -= LogEntryAdded;

            _sapi.Event.UnregisterGameTickListener(_writeDataListenerId);

            _client.Dispose();
        }

        _harmony.UnpatchAll(HarmonyPatchKey);
    }
}