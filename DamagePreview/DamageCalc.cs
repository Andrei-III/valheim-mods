using System;
using HarmonyLib;
using UnityEngine;

namespace DamagePreview
{
    // What the next shot would do to the creature under the crosshair.
    internal struct Prediction
    {
        public Character Target;
        public float Min;   // direct damage on the lowest roll
        public float Max;   // direct damage on the highest roll
        public float DotMin; // fire + poison + spirit, applied over time (not drawn)
        public float DotMax;
    }

    // Mirrors the vanilla bow/crossbow damage pipeline without firing anything:
    //   Attack.ProjectileAttackTriggered: weapon + ammo damage, * random skill factor (* draw %), ModifyDamage, SEMan.ModifyAttack
    //   Character.RPC_Damage:             backstab, stagger x2, resistances (weak spot aware), world-level armor, DoT split
    //   Character.ApplyDamage:            multiplayer difficulty scale, world "player damage" modifier
    // The game's own HitData methods are used wherever possible so patches and other mods' hooks stay in effect.
    internal static class DamageCalc
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private static int _mask;
        private static readonly System.Collections.Generic.IComparer<RaycastHit> HitDistance =
            System.Collections.Generic.Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

        private static readonly AccessTools.FieldRef<Character, float> BackstabTime =
            AccessTools.FieldRefAccess<Character, float>("m_backstabTime");

        // Raycast from the camera along the crosshair, against the same layers arrows collide with.
        public static bool TryFindTarget(Player player, float range, out Character target, out Collider collider)
        {
            target = null;
            collider = null;
            var cam = GameCamera.instance;
            if (cam == null) return false;

            if (_mask == 0)
                _mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain",
                    "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");

            int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, Hits, range, _mask,
                QueryTriggerInteraction.Ignore);
            Array.Sort(Hits, 0, n, HitDistance);

            for (int i = 0; i < n; i++)
            {
                var col = Hits[i].collider;
                if (col == null) continue;
                // Skip our own body (the camera sits behind the player).
                if (col.attachedRigidbody && col.attachedRigidbody.gameObject == player.gameObject) continue;
                if (col.transform.IsChildOf(player.transform)) continue;

                // First thing hit decides: a wall/tree/ground blocks the shot.
                return TryResolveTarget(col, out target, out collider);
            }
            return false;
        }

        // Target from ArrowArc's predicted impact instead of the crosshair. Returns false (no preview) when the
        // arc lands on something that isn't a valid creature or beyond MaxRange.
        public static bool TryTargetFromArc(Player player, Collider hit, float range, out Character target, out Collider collider)
        {
            target = null;
            collider = null;
            if (hit == null) return false;
            // Resolve first: the arc usually lands on terrain/rocks/buildings, and ClosestPoint is only supported on
            // primitive and convex mesh colliders (anything else logs a warning every frame).
            if (!TryResolveTarget(hit, out target, out collider)) return false;
            var from = player.transform.position;
            var near = SupportsClosestPoint(hit) ? hit.ClosestPoint(from) : target.transform.position;
            if (Vector3.Distance(from, near) <= range) return true;
            target = null;
            collider = null;
            return false;
        }

        private static bool SupportsClosestPoint(Collider c) =>
            c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider m && m.convex);

        private static bool TryResolveTarget(Collider col, out Character target, out Collider collider)
        {
            target = null;
            collider = null;
            var c = col.attachedRigidbody ? col.attachedRigidbody.GetComponent<Character>() : null;
            if (c == null) c = col.GetComponentInParent<Character>();
            if (c == null || c.IsDead() || c.IsPlayer() || c.IsTamed()) return false;
            target = c;
            collider = col;
            return true;
        }

        public static bool TryPredict(Player player, Character target, Collider hitCollider, out Prediction result)
        {
            result = default;
            var weapon = player.GetCurrentWeapon();
            if (weapon == null) return false;
            var atk = weapon.m_shared.m_attack;
            if (atk == null || atk.m_attackType != Attack.AttackType.Projectile) return false;

            float draw = 1f;
            if (atk.m_bowDraw)
            {
                draw = player.GetAttackDrawPercentage();
                if (draw <= 0f) return false; // only while drawing
            }
            else if (atk.m_requiresReload)
            {
                if (!DamagePreviewPlugin.IncludeCrossbows.Value || !player.IsWeaponLoaded()) return false;
            }
            else
            {
                return false; // staffs and other projectile weapons: out of scope
            }

            var ammo = FindAmmo(player, weapon);
            if (!string.IsNullOrWhiteSpace(weapon.m_shared.m_ammoType) && ammo == null) return false;

            // Same random roll as Skills.GetRandomSkillFactor: centre lerp(0.4, 1, skill), +-0.15.
            var skill = weapon.m_shared.m_skillType;
            float centre = Mathf.Lerp(0.4f, 1f, player.GetSkillFactor(skill));
            float lo = Mathf.Clamp01(centre - 0.15f);
            float hi = Mathf.Clamp01(centre + 0.15f);
            if (atk.m_bowDraw)
            {
                lo *= draw;
                hi *= draw;
            }

            var hitMin = BuildHit(player, weapon, ammo, atk, skill, lo);
            var hitMax = BuildHit(player, weapon, ammo, atk, skill, hi);
            result.Target = target;
            result.Min = ApplyTargetSide(hitMin, player, target, hitCollider, out result.DotMin);
            result.Max = ApplyTargetSide(hitMax, player, target, hitCollider, out result.DotMax);
            return true;
        }

        // Same selection as Attack.UseAmmo, without consuming anything.
        private static ItemDrop.ItemData FindAmmo(Player player, ItemDrop.ItemData weapon)
        {
            string type = weapon.m_shared.m_ammoType;
            if (string.IsNullOrWhiteSpace(type)) return null;
            var inv = player.GetInventory();
            var ammo = player.GetAmmoItem();
            if (ammo != null && (!inv.ContainsItem(ammo) || ammo.m_shared.m_ammoType != type)) ammo = null;
            return ammo ?? inv.GetAmmoItem(type);
        }

        private static HitData BuildHit(Player player, ItemDrop.ItemData weapon, ItemDrop.ItemData ammo, Attack atk,
            Skills.SkillType skill, float factor)
        {
            var hit = new HitData();
            hit.m_damage.Add(weapon.GetDamage());
            if (ammo != null) hit.m_damage.Add(ammo.GetDamage());
            hit.m_backstabBonus = weapon.m_shared.m_backstabBonus;
            hit.m_skill = skill;
            hit.m_hitType = HitData.HitType.PlayerHit;
            hit.SetAttacker(player);

            // Attack.ModifyDamage
            if (atk.m_damageMultiplier != 1f) hit.m_damage.Modify(atk.m_damageMultiplier);
            hit.m_damage.Modify(factor);
            hit.m_damage.Modify(1f + Mathf.Max(0, player.GetLevel() - 1) * 0.5f);
            if (atk.m_damageMultiplierPerMissingHP > 0f)
                hit.m_damage.Modify(1f + (player.GetMaxHealth() - player.GetHealth()) * atk.m_damageMultiplierPerMissingHP);
            if (atk.m_damageMultiplierByTotalHealthMissing > 0f)
                hit.m_damage.Modify(1f + (1f - player.GetHealthPercentage()) * atk.m_damageMultiplierByTotalHealthMissing);

            // Meads, set bonuses, etc.
            player.GetSEMan().ModifyAttack(skill, ref hit);
            return hit;
        }

        private static float ApplyTargetSide(HitData hit, Player player, Character target, Collider hitCollider, out float dot)
        {
            var ai = target.GetBaseAI();
            if (ai != null && !ai.IsAlerted() && hit.m_backstabBonus > 1f && Time.time - BackstabTime(target) > 300f &&
                (!ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs) || !ai.CanSeeTarget(player)))
                hit.ApplyModifier(hit.m_backstabBonus);

            if (target.IsStaggering()) hit.ApplyModifier(2f);

            hit.ApplyResistance(target.GetDamageModifiers(FindWeakSpot(target, hitCollider)), out _);

            if (Game.m_worldLevel > 0)
                hit.ApplyArmor(Game.m_worldLevel * Game.instance.m_worldLevelEnemyBaseAC);

            // Fire, poison and spirit are taken out and applied as damage over time.
            dot = hit.m_damage.m_fire + hit.m_damage.m_poison + hit.m_damage.m_spirit;
            hit.m_damage.m_fire = 0f;
            hit.m_damage.m_poison = 0f;
            hit.m_damage.m_spirit = 0f;

            // Character.ApplyDamage
            hit.ApplyModifier(Game.instance.GetDifficultyDamageScaleEnemy(target.transform.position));
            hit.ApplyModifier(Game.m_playerDamageRate);
            return hit.GetTotalDamage();
        }

        private static WeakSpot FindWeakSpot(Character target, Collider c)
        {
            var spots = target.m_weakSpots;
            if (c == null || spots == null) return null;
            foreach (var ws in spots)
                if (ws != null && ws.m_collider == c) return ws;
            return null;
        }
    }
}
