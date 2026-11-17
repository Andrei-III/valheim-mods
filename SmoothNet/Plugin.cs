using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Utils;
using Steamworks;
using UnityEngine;

namespace SmoothNet
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Every change is local to the machine running it: how much it sends, and how it draws objects
    // it doesn't control. Mixed installs work; each side just gets its own improvement.
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class SmoothNetPlugin : BaseUnityPlugin
    {
        public const string Guid = "andrei.valheim.smoothnet";
        public const string Name = "SmoothNet";
        public const string Version = "1.0.0";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static ConfigEntry<int> QueueSizeKB;
        internal static ConfigEntry<int> SendRateKB;
        internal static ConfigEntry<float> SnapDistance;
        internal static ConfigEntry<bool> DebugLogging;

        private Harmony _harmony;
        private float _nextReport;

        private void Awake()
        {
            Log = Logger;
            QueueSizeKB = Config.Bind("1 - Network", "UpdateBudgetKB", 64,
                new ConfigDescription(
                    "How much world-update data may be waiting to go out to each player before the game stops adding more " +
                    "(vanilla: 10). Higher = more creatures/objects updated each round in busy fights. " +
                    "Applies to whatever this machine sends (server -> players, player -> server).",
                    new AcceptableValueRange<int>(10, 128)));
            SendRateKB = Config.Bind("1 - Network", "SendRateKB", 2048,
                new ConfigDescription(
                    "Steam send rate in KB/s per connection (vanilla: 150). Must be well above UpdateBudgetKB x 20. " +
                    "Fine to keep high on a LAN; over the internet keep it below your upload speed. Applied when the game starts.",
                    new AcceptableValueRange<int>(150, 8192)));
            SnapDistance = Config.Bind("2 - Smoothing", "SnapDistance", 20f,
                new ConfigDescription(
                    "When an object someone else controls is this many meters off from its latest known position, it jumps " +
                    "there instantly; closer than this, it glides there quickly (vanilla: 5). Only changes what you see.",
                    new AcceptableValueRange<float>(5f, 50f)));
            DebugLogging = Config.Bind("3 - Debug", "DebugLogging", false,
                "Every 10 s, log per-connection pending send data and world updates sent/received per second.");

            QueueSizeKB.SettingChanged += (_, __) => NetPatches.RefreshBudget();
            NetPatches.RefreshBudget();
            SendRateKB.SettingChanged += (_, __) => NetPatches.ApplySendRate();

            _harmony = Harmony.CreateAndPatchAll(typeof(NetPatches), Guid);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        private void Update()
        {
            if (!DebugLogging.Value || Time.time < _nextReport) return;
            _nextReport = Time.time + 10f;
            NetPatches.Report();
        }
    }

    [HarmonyPatch]
    internal static class NetPatches
    {
        private const int VanillaBudget = 10240;
        private const float VanillaSnap = 5f;
        private static int _budget = VanillaBudget;
        private static bool _steamReady;

        internal static void RefreshBudget() => _budget = SmoothNetPlugin.QueueSizeKB.Value * 1024;

        public static int Budget() => _budget;
        public static float Snap() => SmoothNetPlugin.SnapDistance.Value;

        // ---- Update budget: ZDOMan.SendZDOs stops when the socket has more than 10240 bytes pending,
        // and fills a package up to (10240 - pending). Replace both constants.
        [HarmonyPatch(typeof(ZDOMan), "SendZDOs")]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> SendZDOs_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.Method(typeof(NetPatches), nameof(Budget));
            int replaced = 0;
            foreach (var ins in instructions)
            {
                if (ins.opcode == OpCodes.Ldc_I4 && ins.operand is int v && v == VanillaBudget)
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, getter).MoveLabelsFrom(ins).MoveBlocksFrom(ins);
                }
                else yield return ins;
            }
            if (replaced != 2)
                SmoothNetPlugin.Log.LogWarning($"SendZDOs: expected 2 budget constants, replaced {replaced}. Game code changed?");
        }

        // ---- Snap distance: ZSyncTransform.SyncPosition jumps straight to the target when it is 5 m or more
        // away, otherwise lerps 20 % per frame. Only the non-kinematic path (characters, items, projectiles).
        [HarmonyPatch(typeof(ZSyncTransform), "SyncPosition")]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> SyncPosition_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.Method(typeof(NetPatches), nameof(Snap));
            int replaced = 0;
            foreach (var ins in instructions)
            {
                if (ins.opcode == OpCodes.Ldc_R4 && ins.operand is float f && f == VanillaSnap)
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, getter).MoveLabelsFrom(ins).MoveBlocksFrom(ins);
                }
                else yield return ins;
            }
            if (replaced != 1)
                SmoothNetPlugin.Log.LogWarning($"SyncPosition: expected 1 snap constant, replaced {replaced}. Game code changed?");
        }

        // ---- Send rate: vanilla sets Steam's global SendRateMin/Max to 150 KB/s here, once.
        [HarmonyPatch(typeof(ZSteamSocket), "RegisterGlobalCallbacks")]
        [HarmonyPostfix]
        private static void RegisterGlobalCallbacks_Postfix()
        {
            _steamReady = true;
            ApplySendRate();
        }

        internal static void ApplySendRate()
        {
            if (!_steamReady) return;
            int rate = SmoothNetPlugin.SendRateKB.Value * 1024;
            SetInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, rate);
            SetInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, rate);
            SmoothNetPlugin.Log.LogInfo(
                $"Steam send rate ({(IsServer ? "game server" : "client")}): min {GetInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin) / 1024} KB/s, " +
                $"max {GetInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax) / 1024} KB/s; update budget {_budget / 1024} KB");
        }

        // A dedicated server runs Steam's game-server API; the client API isn't initialised there.
        private static bool IsServer => Application.isBatchMode;

        private static void SetInt(ESteamNetworkingConfigValue key, int value)
        {
            var h = GCHandle.Alloc(value, GCHandleType.Pinned);
            try
            {
                bool ok = IsServer
                    ? SteamGameServerNetworkingUtils.SetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, h.AddrOfPinnedObject())
                    : SteamNetworkingUtils.SetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, h.AddrOfPinnedObject());
                if (!ok) SmoothNetPlugin.Log.LogWarning($"Steam refused {key} = {value}");
            }
            catch (Exception e) { SmoothNetPlugin.Log.LogWarning($"Could not set {key}: {e.Message}"); }
            finally { h.Free(); }
        }

        private static int GetInt(ESteamNetworkingConfigValue key)
        {
            var buf = new byte[4];
            var h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                ulong size = 4;
                if (IsServer)
                    SteamGameServerNetworkingUtils.GetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero, out _, h.AddrOfPinnedObject(), ref size);
                else
                    SteamNetworkingUtils.GetConfigValue(key, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                        IntPtr.Zero, out _, h.AddrOfPinnedObject(), ref size);
            }
            catch { return -1; }
            finally { h.Free(); }
            return BitConverter.ToInt32(buf, 0);
        }

        internal static void Report()
        {
            if (ZNet.instance == null || ZDOMan.instance == null) return;
            var zdo = Traverse.Create(ZDOMan.instance);
            var sb = new StringBuilder();
            sb.Append($"updates/s sent {zdo.Field<int>("m_zdosSentLastSec").Value}, recv {zdo.Field<int>("m_zdosRecvLastSec").Value}");
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer?.m_socket == null) continue;
                string who = string.IsNullOrEmpty(peer.m_playerName) ? peer.m_socket.GetHostName() : peer.m_playerName;
                sb.Append($" | {who}: pending {peer.m_socket.GetSendQueueSize() / 1024f:0.0} KB");
            }
            SmoothNetPlugin.Log.LogInfo(sb.ToString());
        }
    }
}
