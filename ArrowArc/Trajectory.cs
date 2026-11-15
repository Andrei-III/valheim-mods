using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArrowArc
{
    // The predicted flight of the next shot.
    internal class Flight
    {
        public readonly List<Vector3> Points = new List<Vector3>(256);
        public bool Hit;              // ended on something (false: ran out of time/distance in mid-air)
        public Vector3 HitPoint;
        public Vector3 HitNormal;
        public Collider HitCollider;  // for weak-spot lookups (DamagePreview)
        public Character HitCharacter;
        public float SpreadDegrees;   // +- random deviation the game adds to the aim, per axis
        public float Speed;           // launch speed, m/s
    }

    // Mirrors the vanilla shot without firing anything:
    //   Attack.GetProjectileSpawnPoint: origin joint + height/range/offset, Humanoid.GetAimDir
    //   Attack.FireProjectileBurst:     weapon + ammo velocity and accuracy, scaled by draw %, launch angle
    //   Projectile.FixedUpdate:         gravity, then v^2 drag, then move; raycast each step against the same layers
    internal static class Trajectory
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[32];
        private static readonly IComparer<RaycastHit> HitDistance =
            Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));
        private static int _mask;

        // Last launch values, only for debug logging.
        private static string _lastLog;

        public static bool TryPredict(Player player, Flight flight)
        {
            flight.Points.Clear();
            flight.Hit = false;
            flight.HitCharacter = null;
            flight.HitCollider = null;

            var weapon = player.GetCurrentWeapon();
            if (weapon == null) return false;
            var atk = weapon.m_shared.m_attack;
            if (atk == null || atk.m_attackType != Attack.AttackType.Projectile) return false;

            float draw;
            if (atk.m_bowDraw)
            {
                draw = player.GetAttackDrawPercentage();
                if (draw <= 0f) return false; // only while drawing
                if (ArrowArcPlugin.OnlyAtFullDraw.Value && draw < 1f) return false;
            }
            else if (atk.m_requiresReload)
            {
                if (!player.IsWeaponLoaded()) return false;
                draw = 1f;
            }
            else
            {
                return false; // staffs and other projectile weapons: out of scope
            }

            var ammo = FindAmmo(player, weapon);
            if (!string.IsNullOrWhiteSpace(weapon.m_shared.m_ammoType) && ammo == null) return false;

            // Attack.FireProjectileBurst: ammo with its own projectile adds to the weapon's values.
            GameObject prefab = atk.m_attackProjectile;
            float vel = atk.m_projectileVel;
            float velMin = atk.m_projectileVelMin;
            float acc = atk.m_projectileAccuracy;
            float accMin = atk.m_projectileAccuracyMin;
            AnimationCurve curve = atk.m_drawVelocityCurve;
            if (ammo != null && ammo.m_shared.m_attack.m_attackProjectile)
            {
                var a = ammo.m_shared.m_attack;
                prefab = a.m_attackProjectile;
                vel += a.m_projectileVel;
                velMin += a.m_projectileVelMin;
                acc += a.m_projectileAccuracy;
                accMin += a.m_projectileAccuracyMin;
                curve = a.m_drawVelocityCurve;
            }
            if (prefab == null) return false;
            var proj = prefab.GetComponent<Projectile>();
            if (proj == null) return false;
            var grapple = proj.m_spawnOnHit != null ? proj.m_spawnOnHit.GetComponent<GrapplingPoint>() : null;
            if (!IsShownFor(atk, grapple)) return false;

            if (atk.m_bowDraw)
            {
                acc = Mathf.Lerp(accMin, acc, Mathf.Pow(draw, 0.5f));
                vel = Mathf.Lerp(velMin, vel, curve != null ? curve.Evaluate(draw) : draw);
            }
            else if (atk.m_skillAccuracy)
            {
                acc = Mathf.Lerp(accMin, acc, player.GetSkillFactor(weapon.m_shared.m_skillType));
            }
            // Random-velocity weapons (non-bow) roll between min and max; we show the fastest.

            GetSpawnPoint(player, atk, out var pos, out var dir);
            if (atk.m_launchAngle != 0f)
                dir = Quaternion.AngleAxis(atk.m_launchAngle, Vector3.Cross(Vector3.up, dir)) * dir;

            flight.SpreadDegrees = acc;
            flight.Speed = vel;

            if (ArrowArcPlugin.DebugLogging.Value)
            {
                string msg = $"{weapon.m_shared.m_name} + {ammo?.m_shared.m_name ?? "-"}: draw {draw:0.00} speed {vel:0.0} m/s, " +
                             $"gravity {proj.m_gravity:0.00}, drag {proj.m_drag:0.0000}, ttl {proj.m_ttl:0.0}s, spread +-{acc:0.00} deg";
                if (msg != _lastLog) ArrowArcPlugin.Debug(_lastLog = msg);
            }

            // GrapplingPoint.Update breaks the rope as soon as the player is further than m_maxLength from the hook,
            // so a landing past that is useless: end the arc where it leaves the rope's reach.
            float rope = grapple != null && grapple.m_maxLength > 0f ? grapple.m_maxLength : 0f;
            Simulate(player, proj, pos, dir * vel, rope, flight);
            return flight.Points.Count > 1;
        }

        // Per-weapon switches. The grappling hook is a reload weapon like the crossbow; it is told apart by its
        // projectile, which spawns a GrapplingPoint where it lands (Projectile.SpawnOnHit).
        private static bool IsShownFor(Attack atk, GrapplingPoint grapple)
        {
            if (grapple != null) return ArrowArcPlugin.ShowForGrapplingHook.Value;
            return atk.m_bowDraw ? ArrowArcPlugin.ShowForBows.Value : ArrowArcPlugin.ShowForCrossbows.Value;
        }

        // Same as Attack.GetProjectileSpawnPoint for a player (no BaseAI target).
        private static void GetSpawnPoint(Player player, Attack atk, out Vector3 spawn, out Vector3 aim)
        {
            Transform origin = player.transform;
            if (!string.IsNullOrEmpty(atk.m_attackOriginJoint) && player.GetVisual() != null)
            {
                var joint = Utils.FindChild(player.GetVisual().transform, atk.m_attackOriginJoint);
                if (joint != null) origin = joint;
            }
            var t = player.transform;
            spawn = origin.position + t.up * atk.m_attackHeight + t.forward * atk.m_attackRange + t.right * atk.m_attackOffset;
            aim = player.GetAimDir(spawn);
            if (atk.m_useCharacterFacing)
            {
                var f = Vector3.forward;
                if (atk.m_useCharacterFacingYAim) f.y = aim.y;
                aim = t.TransformDirection(f);
            }
        }

        private static void Simulate(Player player, Projectile proj, Vector3 pos, Vector3 vel, float rope, Flight flight)
        {
            if (_mask == 0)
                _mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain",
                    "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");

            float dt = Time.fixedDeltaTime;
            float ttl = proj.m_ttl > 0f ? proj.m_ttl : 10f;
            int steps = Mathf.Clamp(Mathf.CeilToInt(ttl / dt), 1, 2000);
            float maxDist = ArrowArcPlugin.MaxDistance.Value;
            float travelled = 0f;
            Vector3 anchor = player.transform.position; // the rope length is measured from here

            flight.Points.Add(pos);
            for (int i = 0; i < steps; i++)
            {
                vel += Vector3.down * (proj.m_gravity * dt);
                float drag = vel.sqrMagnitude * proj.m_drag * dt;
                vel += drag * -vel.normalized;
                Vector3 next = pos + vel * dt;

                // Out of rope reach during this step: stop there, with no landing ring.
                if (rope > 0f && (next - anchor).sqrMagnitude > rope * rope)
                {
                    float f = RopeCut(pos - anchor, next - pos, rope);
                    next = pos + (next - pos) * f;
                    if (TryHitSegment(player, pos, next, out var early))
                    {
                        RecordHit(flight, early);
                        return;
                    }
                    flight.Points.Add(next);
                    return;
                }

                if (TryHitSegment(player, pos, next, out var hit))
                {
                    RecordHit(flight, hit);
                    return;
                }

                if (proj.m_canHitWater)
                {
                    float water = Floating.GetLiquidLevel(next);
                    if (next.y < water && pos.y >= water)
                    {
                        float f = Mathf.InverseLerp(pos.y, next.y, water);
                        var p = Vector3.Lerp(pos, next, f);
                        flight.Points.Add(p);
                        flight.Hit = true;
                        flight.HitPoint = p;
                        flight.HitNormal = Vector3.up;
                        return;
                    }
                }

                travelled += Vector3.Distance(pos, next);
                pos = next;
                flight.Points.Add(pos);
                if (travelled >= maxDist) return;
            }
        }

        private static bool TryHitSegment(Player player, Vector3 from, Vector3 to, out RaycastHit hit)
        {
            Vector3 seg = to - from;
            float len = seg.magnitude;
            hit = default;
            return len > 0f && TryHit(player, from, seg / len, len, out hit);
        }

        private static void RecordHit(Flight flight, RaycastHit hit)
        {
            flight.Points.Add(hit.point);
            flight.Hit = true;
            flight.HitPoint = hit.point;
            flight.HitNormal = hit.normal;
            flight.HitCollider = hit.collider;
            flight.HitCharacter = CharacterOf(hit.collider);
        }

        // Fraction f in [0, 1] of step d (starting at offset p from the anchor) where |p + f*d| reaches r.
        private static float RopeCut(Vector3 p, Vector3 d, float r)
        {
            float a = d.sqrMagnitude;
            if (a <= 0f) return 0f;
            float b = 2f * Vector3.Dot(p, d);
            float c = p.sqrMagnitude - r * r;
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return 0f;
            return Mathf.Clamp01((-b + Mathf.Sqrt(disc)) / (2f * a));
        }

        private static bool TryHit(Player player, Vector3 from, Vector3 dir, float len, out RaycastHit result)
        {
            result = default;
            int n = Physics.RaycastNonAlloc(from, dir, Hits, len, _mask, QueryTriggerInteraction.Ignore);
            if (n == 0) return false;
            Array.Sort(Hits, 0, n, HitDistance);
            for (int i = 0; i < n; i++)
            {
                var col = Hits[i].collider;
                if (col == null) continue;
                // The arrow leaves from inside our own body/bow; vanilla ignores the owner too.
                if (col.attachedRigidbody && col.attachedRigidbody.gameObject == player.gameObject) continue;
                if (col.transform.IsChildOf(player.transform)) continue;
                result = Hits[i];
                return true;
            }
            return false;
        }

        private static Character CharacterOf(Collider col)
        {
            if (col == null) return null;
            var c = col.attachedRigidbody ? col.attachedRigidbody.GetComponent<Character>() : null;
            return c != null ? c : col.GetComponentInParent<Character>();
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
    }
}
