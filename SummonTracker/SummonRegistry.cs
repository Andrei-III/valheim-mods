using System.Collections.Generic;
using UnityEngine;

namespace SummonTracker
{
    internal class TrackedSummon
    {
        public ZDOID Id;
        public string DefaultName;   // localized creature name, used when it hasn't been renamed
        public Sprite Icon;
        public float BaseMaxHealth;  // fallback when the ZDO has no max health yet
        public float Lifetime;       // seconds, 0 = no timed death on this prefab
        public float SpawnTime;      // Time.time when we started tracking (corrected for creatures already alive)

        // Refreshed every frame from the ZDO
        public string Name;
        public int Level;
        public float Health;
        public float MaxHealth;
        public float Distance;
        public bool Loaded;          // instance exists on this machine (close enough for live updates)
        public float Remaining => Lifetime > 0f ? Mathf.Max(0f, Lifetime - (Time.time - SpawnTime)) : -1f;
    }

    /// <summary>
    /// Keeps the list of the local player's summons. Everything is read from synced ZDO data, so a summon
    /// that's far away (not instantiated locally) still shows its last known health and position.
    /// </summary>
    internal static class SummonRegistry
    {
        internal static readonly List<TrackedSummon> Summons = new List<TrackedSummon>();
        private static readonly Dictionary<ZDOID, TrackedSummon> s_byId = new Dictionary<ZDOID, TrackedSummon>();
        private static readonly Dictionary<int, Sprite> s_iconCache = new Dictionary<int, Sprite>();
        private static float s_nextScan;

        internal static void TrackNewSummon(Character character, ItemDrop.ItemData weapon)
        {
            if (!IsFollowerSummon(character))
            {
                SummonTrackerPlugin.Debug($"Ignoring non-follower summon {character.name}");
                return;
            }
            var nview = character.GetComponent<ZNetView>();
            if (!nview || !nview.IsValid()) return;
            Add(character, nview.GetZDO(), weapon?.GetIcon(), 0f);
        }

        /// <summary>
        /// Summons that follow you around (Dead Raiser skeletons). Roots (Staff of the Wild) and the hostile Trollstav troll have no
        /// Tameable / follow behavior and are left out, since they're cheap and stay where they spawned.
        /// </summary>
        private static bool IsFollowerSummon(Character c)
        {
            var tameable = c.GetComponent<Tameable>();
            if (!tameable || !c.GetComponent<MonsterAI>()) return false;
            return tameable.m_commandable || tameable.m_unsummonDistance > 0f || tameable.m_unsummonOnOwnerLogoutSeconds > 0f;
        }

        private static void Add(Character character, ZDO zdo, Sprite weaponIcon, float alreadyAlive)
        {
            if (s_byId.ContainsKey(zdo.m_uid)) return;

            var prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
            var timed = prefab ? prefab.GetComponent<CharacterTimedDestruction>() : null;

            var s = new TrackedSummon
            {
                Id = zdo.m_uid,
                DefaultName = Localization.instance.Localize(character.m_name),
                BaseMaxHealth = character.GetMaxHealth(),
                Icon = FindIcon(zdo.GetPrefab(), prefab, weaponIcon),
                // The timeout is rolled randomly between min and max; show the earliest it can happen.
                Lifetime = timed && timed.m_triggerOnAwake ? timed.m_timeoutMin : 0f,
                SpawnTime = Time.time - alreadyAlive,
            };
            s_byId[s.Id] = s;
            Summons.Add(s);
            SummonTrackerPlugin.Debug($"Tracking {prefab?.name} {s.Id} (lifetime {s.Lifetime}s)");
        }

        internal static void Update()
        {
            // Left the world: drop everything. (Dying keeps the list; summons outlive their owner's death.)
            if (!ZNet.instance || ZDOMan.instance == null)
            {
                if (Summons.Count > 0) Clear();
                return;
            }
            var player = Player.m_localPlayer;
            if (!player) return;

            if (Time.time >= s_nextScan)
            {
                s_nextScan = Time.time + 1f;
                DiscoverFollowers(player);
            }

            Vector3 playerPos = player.transform.position;
            for (int i = Summons.Count - 1; i >= 0; i--)
            {
                var s = Summons[i];
                var zdo = ZDOMan.instance.GetZDO(s.Id);
                float health = zdo != null ? zdo.GetFloat(ZDOVars.s_health, float.MaxValue) : 0f;
                if (zdo == null || health <= 0f)
                {
                    SummonTrackerPlugin.Debug($"Untracking {s.Id} ({(zdo == null ? "gone" : "dead")})");
                    s_byId.Remove(s.Id);
                    Summons.RemoveAt(i);
                    continue;
                }

                s.MaxHealth = zdo.GetFloat(ZDOVars.s_maxHealth, s.BaseMaxHealth);
                s.Health = health == float.MaxValue ? s.MaxHealth : health; // no health key = full health
                s.Level = Mathf.Max(1, zdo.GetInt(ZDOVars.s_level, 1));
                string tamedName = zdo.GetString(ZDOVars.s_tamedName);
                s.Name = string.IsNullOrEmpty(tamedName) ? s.DefaultName : tamedName;
                s.Distance = Vector3.Distance(playerPos, zdo.GetPosition());
                s.Loaded = ZNetScene.instance.FindInstance(s.Id) != null;
            }
        }

        /// <summary>
        /// Fallback for summons we didn't see being cast (e.g. after reloading the mod): any loaded summon-type
        /// creature that's following the local player by name.
        /// </summary>
        private static void DiscoverFollowers(Player player)
        {
            string myName = player.GetPlayerName();
            foreach (var c in Character.GetAllCharacters())
            {
                if (!c || c.IsPlayer() || !c.IsTamed()) continue;
                var tameable = c.GetComponent<Tameable>();
                if (!tameable || (tameable.m_unsummonDistance <= 0f && tameable.m_unsummonOnOwnerLogoutSeconds <= 0f))
                    continue; // regular tamed animal (wolf, lox...), not a summon
                var nview = c.GetComponent<ZNetView>();
                if (!nview || !nview.IsValid()) continue;
                var zdo = nview.GetZDO();
                if (s_byId.ContainsKey(zdo.m_uid) || zdo.GetString(ZDOVars.s_follow) != myName) continue;
                var ai = c.GetBaseAI();
                Add(c, zdo, null, ai ? (float)ai.GetTimeSinceSpawned().TotalSeconds : 0f);
            }
        }

        private static Sprite FindIcon(int prefabHash, GameObject prefab, Sprite weaponIcon)
        {
            if (s_iconCache.TryGetValue(prefabHash, out var cached) && cached) return cached;

            Sprite icon = null;
            // Prefer the creature's trophy (e.g. Skeleton_Friendly -> TrophySkeleton), it reads as "that creature".
            if (prefab && ObjectDB.instance)
            {
                string baseName = prefab.name.Replace("_Friendly", "").Replace("_Summoned", "").Replace("_summoned", "");
                var trophy = ObjectDB.instance.GetItemPrefab("Trophy" + baseName);
                icon = trophy ? trophy.GetComponent<ItemDrop>()?.m_itemData.GetIcon() : null;
            }
            icon = icon ? icon : weaponIcon;
            if (!icon && ObjectDB.instance)
            {
                var staff = ObjectDB.instance.GetItemPrefab("StaffSkeleton");
                icon = staff ? staff.GetComponent<ItemDrop>()?.m_itemData.GetIcon() : null;
            }
            if (icon) s_iconCache[prefabHash] = icon;
            return icon;
        }

        private static void Clear()
        {
            Summons.Clear();
            s_byId.Clear();
        }
    }
}
