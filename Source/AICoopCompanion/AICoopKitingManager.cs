using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AICoopCompanion
{
    // Test-only combat helper. It is deliberately independent from the AI command protocol.
    internal static class AICoopKitingManager
    {
        private const int UpdateIntervalTicks = 15;
        private const int ReissueDelayTicks = 45;
        private const int OrbitDelayTicks = 180;
        private const float RangedSafetyMargin = 0.5f;
        private static readonly FieldInfo TicksToNextBurstShotField = typeof(Verb).GetField("ticksToNextBurstShot", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ProjectileTicksToImpactField = typeof(Projectile).GetField("ticksToImpact", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo ProjectileDestinationCellProperty = typeof(Projectile).GetProperty("DestinationCell", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo ProjectileStartingTicksProperty = typeof(Projectile).GetProperty("StartingTicksToImpact", BindingFlags.Instance | BindingFlags.NonPublic);
        private const int ProjectileUrgentTicks = 90;
        private const float ProjectileExtraRadius = 1.5f;
        // RimWorld movement is cell based. Keep projectile dodges to a short
        // one or two cell hop before falling back to the normal escape search.
        private const float ProjectileMicroDodgeRadius = 2.05f;
        private const int ProjectileMicroDodgeCooldownTicks = 6;
        private const float MeleeOrbitMinRadius = 7f;
        private const float MeleeOrbitMaxRadius = 12f;
        private const int ManualTurretEnemyThreshold = 10;
        private const float CompressedRaidPointThreshold = 700f;
        private const float OriginalRaidPointThreshold = 2100f;
        private sealed class KitingState
        {
            public int TargetId;
            public IntVec3 AnchorCell;
            public bool HasAnchor;
            public float AnchorRange;
            public int AnchorId;
            public int LastAnchorSearchTick = -999999;
            public IntVec3 Destination;
            public bool HasDestination;
            public bool NeedsRetreat;
            public bool MeleeRetreating;
            public IntVec3 CoverCell;
            public bool HasCover;
            public int LastCommandTick = -999999;
            public int NextOrbitTick;
            public int OrbitIndex;
            public int LastProjectileDodgeTick = -999999;
            public IntVec3 StrategicDestination;
            public bool HasStrategicDestination;
            public bool ProjectileDodgeMove;
        }

        private static readonly Dictionary<int, bool> PreviousDrafted = new Dictionary<int, bool>();
        private static readonly Dictionary<int, KitingState> Active = new Dictionary<int, KitingState>();
        private static readonly Dictionary<int, int> ManualTurretAssignments = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> LastTauntTicks = new Dictionary<int, int>();
        private static readonly Dictionary<int, MeleeLureRecord> MeleeLureRecords = new Dictionary<int, MeleeLureRecord>();
        private static readonly HashSet<int> AIDrafted = new HashSet<int>();
        private static readonly Dictionary<int, bool> AIDraftIntents = new Dictionary<int, bool>();
        private static readonly Dictionary<int, RaidPointSnapshot> RaidPointCache = new Dictionary<int, RaidPointSnapshot>();

        private sealed class RaidPointSnapshot
        {
            public float Original;
            public float Adjusted;
            public int Tick;
        }

        private sealed class MeleeLureRecord
        {
            public int LuredMeleeId;
            public bool VanillaRetargeted;
            public bool LeftAttractionRange;
        }

        public static void Reset()
        {
            PreviousDrafted.Clear();
            Active.Clear();
            ManualTurretAssignments.Clear();
            LastTauntTicks.Clear();
            MeleeLureRecords.Clear();
            AIDrafted.Clear();
            AIDraftIntents.Clear();
            RaidPointCache.Clear();
        }

        internal static void MarkAIDraftIntent(Pawn pawn, bool drafted)
        {
            if (pawn != null) AIDraftIntents[pawn.thingIDNumber] = drafted;
        }

        internal static void YieldToPlayer(Pawn pawn)
        {
            if (pawn == null) return;
            int id = pawn.thingIDNumber;
            Active.Remove(id);
            AIDrafted.Remove(id);
            AIDraftIntents.Remove(id);
            ManualTurretAssignments.Remove(id);
            PreviousDrafted[id] = false;
        }

        // Called from the draft setter so the test feature also works while the game is paused.
        public static void NotifyDraftChanged(Pawn pawn, bool drafted)
        {
            if (pawn == null) return;
            AICoopGameComponent ownership = AICoopGameComponent.Current;
            // Normal operation only kites AI-owned colonists.  The temporary
            // player-draft test switch intentionally allows player-owned
            // colonists through the same path.
            if (ownership == null || !ownership.CanAIControl(pawn))
            {
                PreviousDrafted.Remove(pawn.thingIDNumber);
                Active.Remove(pawn.thingIDNumber);
                ManualTurretAssignments.Remove(pawn.thingIDNumber);
                AIDrafted.Remove(pawn.thingIDNumber);
                AIDraftIntents.Remove(pawn.thingIDNumber);
                return;
            }
            int id = pawn.thingIDNumber;
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            if (AICoopAgentRuntime.IsGameLoading)
            {
                PreviousDrafted[id] = drafted;
                Active.Remove(id);
                return;
            }
            if (!drafted)
            {
                PreviousDrafted[id] = false;
                Active.Remove(id);
                AIDrafted.Remove(id);
                AIDraftIntents.Remove(id);
                return;
            }

            bool aiIntent;
            bool hasAIDraftIntent = AIDraftIntents.TryGetValue(id, out aiIntent) && aiIntent;
            if (!hasAIDraftIntent)
            {
                PreviousDrafted[id] = drafted;
                Active.Remove(id);
                ManualTurretAssignments.Remove(id);
                AIDrafted.Remove(id);
                AIDraftIntents.Remove(id);
                return;
            }
            AIDraftIntents.Remove(id);
            AIDrafted.Add(id);

            bool wasDrafted;
            PreviousDrafted.TryGetValue(id, out wasDrafted);
            PreviousDrafted[id] = true;
            if (wasDrafted) return;
            Active[id] = new KitingState { NextOrbitTick = tick };
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component != null && HasRangedWeapon(pawn)) component.AddLog("[自动溜怪] " + pawn.LabelShort + " 已启动。");
            if (pawn.Map != null && pawn.Spawned && HasRangedWeapon(pawn)) UpdatePawn(pawn, Active[id], tick);
        }

        public static void Tick()
        {
            if (Find.TickManager == null || Find.TickManager.TicksGame % UpdateIntervalTicks != 0) return;

            int tick = Find.TickManager.TicksGame;
            ManageManualTurrets(tick);
            HashSet<int> seen = new HashSet<int>();
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn == null || pawn.Map == null || !pawn.Spawned || pawn.Dead) continue;
                AICoopGameComponent ownership = AICoopGameComponent.Current;
                if (ownership == null || !ownership.CanAIControl(pawn))
                {
                    PreviousDrafted.Remove(pawn.thingIDNumber);
                    Active.Remove(pawn.thingIDNumber);
                    ManualTurretAssignments.Remove(pawn.thingIDNumber);
                    AIDrafted.Remove(pawn.thingIDNumber);
                    continue;
                }
                int id = pawn.thingIDNumber;
                seen.Add(id);
                bool drafted = pawn.Drafted;
                bool wasDrafted;
                PreviousDrafted.TryGetValue(id, out wasDrafted);

                // The setter callback can run while a save is still loading. In that
                // case it records the value but intentionally does not activate kiting.
                // Retry until a drafted pawn is actually registered as active.
                if (drafted != wasDrafted || (drafted && !AIDrafted.Contains(id))) NotifyDraftChanged(pawn, drafted);
                else PreviousDrafted[id] = drafted;

                if (!drafted || !AIDrafted.Contains(id))
                {
                    continue;
                }
                KitingState state;
                if (!Active.TryGetValue(id, out state))
                {
                    state = new KitingState { NextOrbitTick = tick };
                    Active[id] = state;
                }
                UpdatePawn(pawn, state, tick);
            }

            foreach (int id in PreviousDrafted.Keys.Where(id => !seen.Contains(id)).ToList()) PreviousDrafted.Remove(id);
            foreach (int id in Active.Keys.Where(id => !seen.Contains(id)).ToList()) Active.Remove(id);
            foreach (int id in ManualTurretAssignments.Keys.Where(id => !seen.Contains(id)).ToList()) ManualTurretAssignments.Remove(id);
            foreach (int id in AIDrafted.Where(id => !seen.Contains(id)).ToList()) AIDrafted.Remove(id);
        }

        // Drafting can happen while the game is paused, so GameComponentTick may not
        // run. Polling from Root.Update provides a main-thread fallback for the Harmony
        // setter patch and also repairs state that was captured during game loading.
        public static void PollDraftStates()
        {
            if (AICoopAgentRuntime.IsGameLoading || Find.Maps == null || AICoopGameComponent.Current == null) return;
            foreach (Map map in Find.Maps)
            {
                if (map == null || map.mapPawns == null) continue;
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn == null || pawn.drafter == null || pawn.Dead || !pawn.Spawned) continue;
                    int id = pawn.thingIDNumber;
                    bool drafted = pawn.drafter.Drafted;
                    bool previous;
                    bool known = PreviousDrafted.TryGetValue(id, out previous);
                    if (!known || drafted != previous || (drafted && !AIDrafted.Contains(id)))
                    {
                        NotifyDraftChanged(pawn, drafted);
                    }
                }
            }
        }

        private static void ManageManualTurrets(int tick)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null) return;
            foreach (Map map in Find.Maps)
            {
                if (map == null || map.mapPawns == null) continue;
                List<Pawn> enemies = map.mapPawns.AllPawnsSpawned.Where(IsHostileTarget).ToList();
                bool highThreat = enemies.Count >= ManualTurretEnemyThreshold && IsHighRaidThreat(map, tick);
                List<Pawn> aiPawns = map.mapPawns.FreeColonistsSpawned
                    .Where(pawn => pawn != null && component.CanAIControl(pawn) && pawn.Drafted && !pawn.Downed && pawn.jobs != null)
                    .ToList();
                if (highThreat)
                {
                    List<Building_TurretGun> turrets = map.listerThings.AllThings.OfType<Building_TurretGun>()
                        .Where(turret => turret.Spawned && turret.Faction == Faction.OfPlayer && turret.IsMannable &&
                            turret.TryGetComp<CompMannable>() != null && !turret.TryGetComp<CompMannable>().MannedNow &&
                            turret.def != null && turret.def.building != null && turret.def.building.turretGunDef != null)
                        .OrderBy(turret => turret.Position.DistanceToSquared(map.Center)).ToList();
                    for (int i = aiPawns.Count - 1; i >= 0 && turrets.Count > 0; i--)
                    {
                        Pawn pawn = aiPawns[i];
                        if (ManualTurretAssignments.ContainsKey(pawn.thingIDNumber) ||
                            (pawn.CurJob != null && pawn.CurJob.def == JobDefOf.ManTurret)) continue;
                        Building_TurretGun turret = turrets.FirstOrDefault(candidate => pawn.CanReserveAndReach(candidate, PathEndMode.InteractionCell, Danger.Deadly));
                        if (turret == null) continue;
                        Job job = JobMaker.MakeJob(JobDefOf.ManTurret, turret);
                        if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc)) continue;
                        ManualTurretAssignments[pawn.thingIDNumber] = turret.thingIDNumber;
                        turrets.Remove(turret);
                        Active.Remove(pawn.thingIDNumber);
                    }
                }
                else if (!highThreat || enemies.Count < ManualTurretEnemyThreshold)
                {
                    foreach (Pawn pawn in aiPawns)
                    {
                        if (!ManualTurretAssignments.ContainsKey(pawn.thingIDNumber)) continue;
                        if (pawn.CurJob == null || pawn.CurJob.def != JobDefOf.ManTurret) continue;
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        ManualTurretAssignments.Remove(pawn.thingIDNumber);
                    }
                }
            }
        }

        private static bool IsHighRaidThreat(Map map, int tick)
        {
            RaidPointSnapshot snapshot;
            if (!RaidPointCache.TryGetValue(map.uniqueID, out snapshot) || tick - snapshot.Tick > 600)
            {
                float original, adjusted;
                if (TryReadEliteRaidPoints(map, out original, out adjusted))
                {
                    snapshot = new RaidPointSnapshot { Original = original, Adjusted = adjusted, Tick = tick };
                    RaidPointCache[map.uniqueID] = snapshot;
                }
                else
                {
                    float fallback = StorytellerUtility.DefaultThreatPointsNow(map);
                    snapshot = new RaidPointSnapshot { Original = fallback, Adjusted = fallback, Tick = tick };
                    RaidPointCache[map.uniqueID] = snapshot;
                }
            }
            return snapshot.Adjusted > CompressedRaidPointThreshold || snapshot.Original > OriginalRaidPointThreshold;
        }

        internal static bool TryGetRaidPoints(Map map, out float original, out float adjusted)
        {
            original = adjusted = 0f;
            if (map == null) return false;
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            RaidPointSnapshot snapshot;
            if (RaidPointCache.TryGetValue(map.uniqueID, out snapshot) && tick - snapshot.Tick <= 600)
            {
                original = snapshot.Original;
                adjusted = snapshot.Adjusted;
                return true;
            }
            if (TryReadEliteRaidPoints(map, out original, out adjusted))
            {
                RaidPointCache[map.uniqueID] = new RaidPointSnapshot { Original = original, Adjusted = adjusted, Tick = tick };
                return true;
            }
            original = adjusted = StorytellerUtility.DefaultThreatPointsNow(map);
            RaidPointCache[map.uniqueID] = new RaidPointSnapshot { Original = original, Adjusted = adjusted, Tick = tick };
            return true;
        }

        private static bool TryReadEliteRaidPoints(Map map, out float original, out float adjusted)
        {
            original = adjusted = 0f;
            try
            {
                Type markerType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("EliteRaidPlus.Core.Data.RaidMarkerMapComponent", false))
                    .FirstOrDefault(type => type != null);
                if (markerType == null || map.components == null) return false;
                object component = map.components.FirstOrDefault(item => item != null && markerType.IsInstanceOfType(item));
                if (component == null) return false;
                FieldInfo markersField = markerType.GetField("raidMarkers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                object markers = markersField == null ? null : markersField.GetValue(component);
                System.Collections.IEnumerable enumerable = markers as System.Collections.IEnumerable;
                if (enumerable == null) return false;
                int latestTick = -1;
                foreach (object marker in enumerable)
                {
                    if (marker == null) continue;
                    Type type = marker.GetType();
                    FieldInfo factionField = type.GetField("faction", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    Faction markerFaction = factionField == null ? null : factionField.GetValue(marker) as Faction;
                    if (markerFaction != null && !markerFaction.HostileTo(Faction.OfPlayer)) continue;
                    int start = ReadIntMember(type, marker, "raidStartTick", -1);
                    float candidateOriginal = ReadFloatMember(type, marker, "originalPoints", 0f);
                    float candidateAdjusted = ReadFloatMember(type, marker, "adjustedPoints", 0f);
                    if (candidateOriginal <= 0f && candidateAdjusted <= 0f) continue;
                    if (start < latestTick) continue;
                    original = candidateOriginal;
                    adjusted = candidateAdjusted > 0f ? candidateAdjusted : candidateOriginal;
                    latestTick = start;
                }
                return latestTick >= 0 && (original > 0f || adjusted > 0f);
            }
            catch
            {
                return false;
            }
        }

        private static int ReadIntMember(Type type, object instance, string name, int fallback)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            object value = field == null ? null : field.GetValue(instance);
            int result;
            return value != null && Int32.TryParse(value.ToString(), out result) ? result : fallback;
        }

        private static float ReadFloatMember(Type type, object instance, string name, float fallback)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            object value = field == null ? null : field.GetValue(instance);
            float result;
            return value != null && Single.TryParse(value.ToString(), out result) ? result : fallback;
        }

        private static bool HasRangedWeapon(Pawn pawn)
        {
            return pawn != null && pawn.equipment != null && pawn.equipment.Primary != null &&
                pawn.equipment.Primary.def != null && pawn.equipment.Primary.def.IsRangedWeapon;
        }

        private static bool HasMeleeWeapon(Pawn pawn)
        {
            return pawn != null && pawn.equipment != null && pawn.equipment.Primary != null &&
                pawn.equipment.Primary.def != null && pawn.equipment.Primary.def.IsMeleeWeapon;
        }

        private static void RefreshAnchor(Pawn pawn, Pawn target, KitingState state, int tick)
        {
            if (pawn == null || target == null || state == null) return;
            Thing anchor = null;
            if (tick - state.LastAnchorSearchTick >= 90)
            {
                anchor = FindDefenseAnchor(pawn.Map, target);
                state.LastAnchorSearchTick = tick;
            }
            else if (state.HasAnchor && state.AnchorId != 0)
            {
                anchor = pawn.Map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == state.AnchorId && IsTacticalAnchor(thing));
            }
            bool changed = anchor == null ? state.HasAnchor : !state.HasAnchor || state.AnchorCell != anchor.Position;
            if (!changed) return;
            state.HasAnchor = anchor != null;
            state.AnchorCell = anchor == null ? IntVec3.Invalid : anchor.Position;
            state.AnchorRange = anchor == null ? 0f : DefenseRange(anchor);
            state.AnchorId = anchor == null ? 0 : anchor.thingIDNumber;
            state.HasDestination = false;
            state.HasStrategicDestination = false;
            state.ProjectileDodgeMove = false;
            state.OrbitIndex = 0;
            state.NextOrbitTick = tick;
        }

        private static bool IssueMove(Pawn pawn, IntVec3 destination, KitingState state, int tick)
        {
            if (pawn == null || state == null || !destination.IsValid || destination == pawn.Position || pawn.jobs == null) return false;
            if (pawn.CurJob != null && pawn.CurJob.def == JobDefOf.Goto && pawn.CurJob.targetA.Cell == destination) return true;
            Job move = JobMaker.MakeJob(JobDefOf.Goto, new LocalTargetInfo(destination));
            if (!pawn.jobs.TryTakeOrderedJob(move)) return false;
            state.Destination = destination;
            state.HasDestination = true;
            state.StrategicDestination = destination;
            state.HasStrategicDestination = true;
            state.ProjectileDodgeMove = false;
            state.NeedsRetreat = false;
            state.LastCommandTick = tick;
            state.NextOrbitTick = tick + OrbitDelayTicks;
            return true;
        }

        private static bool IssueProjectileDodgeMove(Pawn pawn, IntVec3 destination, KitingState state, int tick)
        {
            if (pawn == null || state == null || !destination.IsValid || destination == pawn.Position || pawn.jobs == null) return false;
            if (state.HasDestination && !state.ProjectileDodgeMove && state.Destination != pawn.Position)
            {
                state.StrategicDestination = state.Destination;
                state.HasStrategicDestination = true;
            }
            if (pawn.CurJob != null && pawn.CurJob.def == JobDefOf.Goto && pawn.CurJob.targetA.Cell == destination)
            {
                state.Destination = destination;
                state.HasDestination = true;
                state.ProjectileDodgeMove = true;
                return true;
            }
            Job move = JobMaker.MakeJob(JobDefOf.Goto, new LocalTargetInfo(destination));
            if (!pawn.jobs.TryTakeOrderedJob(move)) return false;
            state.Destination = destination;
            state.HasDestination = true;
            state.ProjectileDodgeMove = true;
            state.NeedsRetreat = false;
            state.LastCommandTick = tick;
            return true;
        }

        private static bool RestoreStrategicMove(Pawn pawn, KitingState state, int tick)
        {
            if (pawn == null || state == null || !state.ProjectileDodgeMove) return false;
            if (IsCurrentKitingMove(pawn, state)) return true;
            // Do not cancel a vanilla combat, rescue, or other urgent job that
            // replaced the temporary dodge move. Resume the saved route after
            // that job ends instead.
            if (pawn.CurJob != null) return false;
            state.ProjectileDodgeMove = false;
            state.HasDestination = false;
            if (state.HasStrategicDestination && state.StrategicDestination.IsValid &&
                state.StrategicDestination != pawn.Position)
            {
                IntVec3 strategic = state.StrategicDestination;
                state.HasStrategicDestination = false;
                IssueMove(pawn, strategic, state, tick);
                return true;
            }
            state.HasStrategicDestination = false;
            return false;
        }

        private static void UpdateMeleePawn(Pawn pawn, KitingState state, int tick)
        {
            if (pawn == null || state == null || pawn.Map == null) return;

            bool residenceRestricted = IsResidenceRestricted(pawn);
            if (residenceRestricted && !IsResidenceCell(pawn, pawn.Position))
            {
                IntVec3 residence = FindResidenceCell(pawn);
                if (residence.IsValid) IssueMove(pawn, residence, state, tick);
                return;
            }

            // A melee pawn is a decoy first. While an enemy has selected it as the
            // target, keep a safe distance from that enemy while continuing to lure
            // enemies that have not selected a pawn yet.
            if (!residenceRestricted || HasEnemyInAttackRange(pawn)) RedirectHostilesToMeleePawn(pawn, tick);
            if (residenceRestricted && !HasEnemyInAttackRange(pawn)) return;
            List<Pawn> currentAttackers = FindHostilesTargeting(pawn);
            Pawn currentAttacker = currentAttackers
                .OrderBy(enemy => enemy.Position.DistanceToSquared(pawn.Position))
                .FirstOrDefault();
            state.MeleeRetreating = currentAttackers.Count > 0;

            if (state.HasDestination && pawn.Position == state.Destination && pawn.CurJob == null)
            {
                state.HasDestination = false;
                if (state.ProjectileDodgeMove) RestoreStrategicMove(pawn, state, tick);
                if (!state.ProjectileDodgeMove) state.HasStrategicDestination = false;
            }
            if (IsCurrentKitingMove(pawn, state)) return;

            Pawn threatenedRanged = FindRangedPawnUnderMeleeAttack(pawn.Map, pawn);
            Pawn supportTarget = FindMeleeAttacker(pawn.Map, threatenedRanged);
            if (supportTarget != null)
            {
                // If this decoy is already being chased by another enemy, do not
                // abandon its safety route just to approach the ranged ally's attacker.
                // It may still strike from its current cell when that attacker is the
                // same enemy already targeting it.
                if (currentAttacker != null && supportTarget != currentAttacker)
                {
                    Verb supportFromHere = pawn.TryGetAttackVerb(supportTarget, false);
                    if (supportFromHere == null || supportFromHere.verbProps == null ||
                        !supportFromHere.verbProps.IsMeleeAttack ||
                        !supportFromHere.CanHitTargetFrom(pawn.Position, new LocalTargetInfo(supportTarget)))
                        supportTarget = null;
                }
            }
            if (supportTarget != null)
            {
                Verb supportVerb = pawn.TryGetAttackVerb(supportTarget, false);
                if (supportVerb == null || supportVerb.verbProps == null || !supportVerb.verbProps.IsMeleeAttack) return;
                LocalTargetInfo supportInfo = new LocalTargetInfo(supportTarget);
                if (supportVerb.CanHitTargetFrom(pawn.Position, supportInfo))
                {
                    if (!IsMeleeAttackJobForTarget(pawn.CurJob, supportTarget))
                    {
                        Job attack = JobMaker.MakeJob(JobDefOf.AttackMelee, supportInfo);
                        attack.maxNumStaticAttacks = 1;
                        pawn.jobs.TryTakeOrderedJob(attack);
                    }
                    return;
                }
                IntVec3 approach = FindAdjacentCell(pawn, supportTarget);
                if (approach.IsValid) IssueMove(pawn, approach, state, tick);
                return;
            }

            if (state.MeleeRetreating)
            {
                Pawn lureTarget = FindMeleeLureTarget(pawn, currentAttackers);
                if (lureTarget == null) lureTarget = currentAttacker;
                RefreshAnchor(pawn, lureTarget, state, tick);
                IntVec3 escape = FindMeleeLureDestination(pawn, currentAttackers, lureTarget, state);
                if (!escape.IsValid)
                {
                    float retreatDistance = EnemyAttackRange(currentAttacker, pawn) + RangedSafetyMargin;
                    escape = FindEscapeDestination(pawn, currentAttacker, retreatDistance);
                }
                if (escape.IsValid && escape != pawn.Position) IssueMove(pawn, escape, state, tick);
                return;
            }

            Pawn target = FindMeleeLureTarget(pawn, Enumerable.Empty<Pawn>());
            if (target == null) return;
            RefreshAnchor(pawn, target, state, tick);
            float distance = pawn.Position.DistanceTo(target.Position);
            IntVec3 lure = FindLureDestination(pawn, target, state);
            if (lure.IsValid && (distance > 8f || distance < 5f || tick >= state.NextOrbitTick)) IssueMove(pawn, lure, state, tick);
        }

        private static Pawn FindMeleeLureTarget(Pawn pawn, IEnumerable<Pawn> excluded)
        {
            if (pawn == null || pawn.Map == null) return null;
            HashSet<int> excludedIds = new HashSet<int>((excluded ?? Enumerable.Empty<Pawn>())
                .Where(enemy => enemy != null).Select(enemy => enemy.thingIDNumber));
            List<Pawn> rangedAllies = pawn.Map.mapPawns.FreeColonistsSpawned
                .Where(candidate => candidate != null && candidate != pawn && !candidate.Downed && candidate.Drafted && HasRangedWeapon(candidate))
                .ToList();
            return pawn.Map.mapPawns.AllPawnsSpawned.Where(IsHostileTarget)
                .Where(enemy => !excludedIds.Contains(enemy.thingIDNumber))
                .Where(enemy => enemy.CurJob == null || !enemy.CurJob.targetA.HasThing || !(enemy.CurJob.targetA.Thing is Pawn))
                .OrderBy(enemy => rangedAllies.Count == 0
                    ? enemy.Position.DistanceToSquared(pawn.Position)
                    : rangedAllies.Min(ally => enemy.Position.DistanceToSquared(ally.Position)))
                .ThenBy(enemy => enemy.Position.DistanceToSquared(pawn.Position))
                .FirstOrDefault();
        }

        private static List<Pawn> FindHostilesTargeting(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return new List<Pawn>();
            return pawn.Map.mapPawns.AllPawnsSpawned
                .Where(IsHostileTarget)
                .Where(enemy => enemy.CurJob != null && enemy.CurJob.targetA.Thing == pawn)
                .ToList();
        }

        private static void RedirectHostilesToMeleePawn(Pawn meleePawn, int tick)
        {
            if (meleePawn == null || meleePawn.Map == null || meleePawn.jobs == null) return;
            List<Pawn> activeMeleePawns = meleePawn.Map.mapPawns.FreeColonistsSpawned
                .Where(candidate => candidate != null && candidate.Drafted && !candidate.Downed &&
                    HasMeleeWeapon(candidate) && !HasRangedWeapon(candidate) && Active.ContainsKey(candidate.thingIDNumber))
                .ToList();
            if (!activeMeleePawns.Contains(meleePawn)) return;
            StatDef sightDef = DefDatabase<StatDef>.GetNamedSilentFail("SightRadius");
            foreach (Pawn enemy in meleePawn.Map.mapPawns.AllPawnsSpawned.Where(IsHostileTarget))
            {
                if (enemy == null || enemy.jobs == null) continue;
                Pawn nearestMelee = activeMeleePawns
                    .OrderBy(candidate => candidate.Position.DistanceToSquared(enemy.Position))
                    .ThenBy(candidate => candidate.thingIDNumber)
                    .FirstOrDefault();
                if (nearestMelee != meleePawn) continue;
                if (IsEnemyTargetingMelee(enemy, meleePawn))
                {
                    // The lure is already active. Do not recreate the attack job
                    // every polling tick, even if its bookkeeping record was lost.
                    if (!MeleeLureRecords.ContainsKey(enemy.thingIDNumber))
                    {
                        MeleeLureRecords[enemy.thingIDNumber] = new MeleeLureRecord
                        {
                            LuredMeleeId = meleePawn.thingIDNumber,
                            VanillaRetargeted = false,
                            LeftAttractionRange = false
                        };
                    }
                    continue;
                }
                float aggroRange = sightDef == null ? 24f : enemy.GetStatValue(sightDef);
                aggroRange = Math.Max(12f, Math.Min(40f, aggroRange));
                bool insideAttractionRange = enemy.Position.DistanceTo(meleePawn.Position) <= aggroRange &&
                    GenSight.LineOfSight(enemy.Position, meleePawn.Position, meleePawn.Map);

                MeleeLureRecord record;
                if (MeleeLureRecords.TryGetValue(enemy.thingIDNumber, out record))
                {
                    bool recordedMeleeActive = activeMeleePawns.Any(candidate => candidate.thingIDNumber == record.LuredMeleeId);
                    if (!recordedMeleeActive)
                    {
                        MeleeLureRecords.Remove(enemy.thingIDNumber);
                        record = null;
                    }
                    else if (IsEnemyTargetingMelee(enemy, record.LuredMeleeId))
                    {
                        // The original lure is still active; do not replace its job.
                        continue;
                    }
                    else if (HasExplicitEnemyTarget(enemy))
                    {
                        // Vanilla combat selected another pawn, building, or cell
                        // after the lure. Respect that target until the enemy has
                        // left and then re-entered the attraction range.
                        // Respect that target until the enemy has left and then
                        // re-entered this lure's normal attraction range.
                        if (!record.VanillaRetargeted) LastTauntTicks.Remove(enemy.thingIDNumber);
                        record.VanillaRetargeted = true;
                    }

                    if (record != null && record.VanillaRetargeted)
                    {
                        if (!insideAttractionRange)
                        {
                            record.LeftAttractionRange = true;
                            continue;
                        }
                        if (!record.LeftAttractionRange) continue;
                    }
                }

                if (!insideAttractionRange) continue;

                Verb enemyVerb = enemy.TryGetAttackVerb(meleePawn, false);
                if (enemyVerb == null || enemyVerb.verbProps == null) continue;
                int lastTauntTick;
                if (LastTauntTicks.TryGetValue(enemy.thingIDNumber, out lastTauntTick) && tick - lastTauntTick < 90) continue;
                Job attack = JobMaker.MakeJob(enemyVerb.verbProps.IsMeleeAttack ? JobDefOf.AttackMelee : JobDefOf.AttackStatic,
                    new LocalTargetInfo(meleePawn));
                if (!enemyVerb.verbProps.IsMeleeAttack) attack.maxNumStaticAttacks = 1;
                if (enemy.jobs.TryTakeOrderedJob(attack, JobTag.Misc))
                {
                    LastTauntTicks[enemy.thingIDNumber] = tick;
                    MeleeLureRecords[enemy.thingIDNumber] = new MeleeLureRecord
                    {
                        LuredMeleeId = meleePawn.thingIDNumber,
                        VanillaRetargeted = false,
                        LeftAttractionRange = false
                    };
                }
            }
        }

        private static bool IsEnemyTargetingMelee(Pawn enemy, Pawn meleePawn)
        {
            return enemy != null && meleePawn != null && enemy.CurJob != null &&
                enemy.CurJob.targetA.HasThing && enemy.CurJob.targetA.Thing == meleePawn;
        }

        private static bool IsEnemyTargetingMelee(Pawn enemy, int meleePawnId)
        {
            return enemy != null && enemy.CurJob != null && enemy.CurJob.targetA.HasThing &&
                enemy.CurJob.targetA.Thing is Pawn && enemy.CurJob.targetA.Thing.thingIDNumber == meleePawnId;
        }

        private static bool HasExplicitEnemyTarget(Pawn enemy)
        {
            return enemy != null && enemy.CurJob != null && enemy.CurJob.targetA.IsValid;
        }

        private static Pawn FindRangedPawnUnderMeleeAttack(Map map, Pawn excluding)
        {
            if (map == null) return null;
            return map.mapPawns.AllPawnsSpawned
                .Where(candidate => candidate != excluding && HasRangedWeapon(candidate) && !candidate.Downed && candidate.Spawned)
                .Where(candidate => FindMeleeAttacker(map, candidate) != null)
                .OrderBy(candidate => candidate.Position.DistanceToSquared(excluding.Position))
                .FirstOrDefault();
        }

        private static Pawn FindMeleeAttacker(Map map, Pawn victim)
        {
            if (map == null || victim == null) return null;
            return map.mapPawns.AllPawnsSpawned.Where(IsHostileTarget)
                .Where(enemy =>
                {
                    Verb attack = enemy.TryGetAttackVerb(victim, false);
                    if (attack == null || attack.verbProps == null || !attack.verbProps.IsMeleeAttack) return false;
                    bool targetsVictim = enemy.CurJob != null && enemy.CurJob.targetA.Thing == victim;
                    return targetsVictim || enemy.Position.DistanceTo(victim.Position) <= attack.EffectiveRange + 1.5f;
                })
                .OrderBy(enemy => enemy.Position.DistanceToSquared(victim.Position))
                .FirstOrDefault();
        }

        private static IntVec3 FindAdjacentCell(Pawn pawn, Pawn target)
        {
            if (pawn == null || target == null || pawn.Map == null) return IntVec3.Invalid;
            return GenRadial.RadialCellsAround(target.Position, 1.5f, true)
                .Where(cell => cell.InBounds(pawn.Map) && cell.Walkable(pawn.Map) && cell != pawn.Position)
                .Where(cell => IsAllowedKitingCell(pawn, cell))
                .Where(cell => cell.GetThingList(pawn.Map).All(thing => !(thing is Pawn) || thing == pawn))
                .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .OrderBy(cell => cell.DistanceToSquared(pawn.Position))
                .DefaultIfEmpty(IntVec3.Invalid).First();
        }

        private static IntVec3 FindLureDestination(Pawn pawn, Pawn target, KitingState state)
        {
            if (pawn == null || target == null || pawn.Map == null) return IntVec3.Invalid;
            IEnumerable<IntVec3> ring = state != null && state.HasAnchor
                ? GenRadial.RadialCellsAround(state.AnchorCell, MeleeOrbitMaxRadius, true)
                : GenRadial.RadialCellsAround(target.Position, MeleeOrbitMaxRadius, true);
            List<IntVec3> candidates = ring
                .Where(cell => cell.InBounds(pawn.Map) && cell.Walkable(pawn.Map) && cell != pawn.Position)
                .Where(cell => IsAllowedKitingCell(pawn, cell))
                .Where(cell => cell.DistanceTo(target.Position) >= MeleeOrbitMinRadius && cell.DistanceTo(target.Position) <= MeleeOrbitMaxRadius)
                .Where(cell => cell.GetThingList(pawn.Map).All(thing => !(thing is Pawn) || thing == pawn))
                .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .Where(cell => !IsProjectileCellThreat(cell, pawn.Map, null))
                .OrderByDescending(cell => LureCellScore(pawn, target, cell, state))
                .ToList();
            if (candidates.Count == 0) return IntVec3.Invalid;
            IntVec3 current = pawn.Position;
            return candidates
                .OrderBy(cell => OrbitTurnDistance(current, cell, target.Position))
                .ThenBy(cell => cell.DistanceToSquared(current))
                .First();
        }

        private static IntVec3 FindMeleeLureDestination(Pawn pawn, IEnumerable<Pawn> safeTargets, Pawn lureTarget, KitingState state)
        {
            if (pawn == null || pawn.Map == null || lureTarget == null) return IntVec3.Invalid;
            IEnumerable<IntVec3> ring = state != null && state.HasAnchor
                ? GenRadial.RadialCellsAround(state.AnchorCell, MeleeOrbitMaxRadius, true)
                : GenRadial.RadialCellsAround(lureTarget.Position, MeleeOrbitMaxRadius, true);
            List<IntVec3> candidates = ring
                .Where(cell => cell.InBounds(pawn.Map) && cell.Walkable(pawn.Map) && cell != pawn.Position)
                .Where(cell => IsAllowedKitingCell(pawn, cell))
                .Where(cell => cell.DistanceTo(lureTarget.Position) >= MeleeOrbitMinRadius && cell.DistanceTo(lureTarget.Position) <= MeleeOrbitMaxRadius)
                .Where(cell => cell.GetThingList(pawn.Map).All(thing => !(thing is Pawn) || thing == pawn))
                .Where(cell => IsSafeFromEnemies(pawn, safeTargets, cell))
                .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .Where(cell => !IsProjectileCellThreat(cell, pawn.Map, null))
                .OrderByDescending(cell => LureCellScore(pawn, lureTarget, cell, state))
                .ToList();
            if (candidates.Count == 0) return IntVec3.Invalid;
            IntVec3 current = pawn.Position;
            return candidates
                .OrderBy(cell => OrbitTurnDistance(current, cell, lureTarget.Position))
                .ThenBy(cell => cell.DistanceToSquared(current))
                .First();
        }

        private static bool IsSafeFromEnemies(Pawn pawn, IEnumerable<Pawn> enemies, IntVec3 cell)
        {
            if (pawn == null || enemies == null) return true;
            foreach (Pawn enemy in enemies)
            {
                if (enemy == null || !IsHostileTarget(enemy)) continue;
                float requiredDistance = EnemyAttackRange(enemy, pawn) + RangedSafetyMargin;
                if (!IsSafeFromSpecificEnemy(pawn, enemy, requiredDistance, cell)) return false;
            }
            return true;
        }

        private static float OrbitTurnDistance(IntVec3 from, IntVec3 to, IntVec3 center)
        {
            double fromAngle = Math.Atan2(from.z - center.z, from.x - center.x);
            double toAngle = Math.Atan2(to.z - center.z, to.x - center.x);
            double delta = Math.Abs(toAngle - fromAngle);
            while (delta > Math.PI) delta = Math.Abs(delta - Math.PI * 2.0);
            return (float)delta;
        }

        private static bool IsSafeFromSpecificEnemy(Pawn pawn, Pawn enemy, float requiredDistance, IntVec3 cell)
        {
            if (pawn == null || enemy == null || !cell.IsValid) return false;
            if (IsDirectMoveToward(pawn.Position, cell, enemy.Position)) return false;
            return cell.DistanceTo(enemy.Position) + 0.01f >= requiredDistance;
        }

        private static bool IsResidenceRestricted(Pawn pawn)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (pawn == null || pawn.Map == null || component == null || !component.HasCompletedDefensePreset(pawn.Map)) return false;
            int minX, minZ, maxX, maxZ;
            return component.TryGetResidenceBounds(pawn.Map, out minX, out minZ, out maxX, out maxZ);
        }

        private static bool IsResidenceCell(Pawn pawn, IntVec3 cell)
        {
            if (pawn == null || pawn.Map == null || !cell.IsValid) return false;
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null || !component.HasCompletedDefensePreset(pawn.Map)) return true;
            int minX, minZ, maxX, maxZ;
            if (!component.TryGetResidenceBounds(pawn.Map, out minX, out minZ, out maxX, out maxZ)) return true;
            return cell.x >= minX && cell.x <= maxX && cell.z >= minZ && cell.z <= maxZ;
        }

        private static bool IsAllowedKitingCell(Pawn pawn, IntVec3 cell)
        {
            return !IsResidenceRestricted(pawn) || IsResidenceCell(pawn, cell);
        }

        private static bool HasEnemyInAttackRange(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return false;
            foreach (Pawn enemy in pawn.Map.mapPawns.AllPawnsSpawned.Where(IsHostileTarget))
            {
                Verb verb = pawn.TryGetAttackVerb(enemy, false);
                if (verb == null || verb.verbProps == null) continue;
                if (enemy.Position.DistanceTo(pawn.Position) <= Math.Max(1.5f, verb.EffectiveRange)) return true;
            }
            return false;
        }

        private static IntVec3 FindResidenceCell(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return IntVec3.Invalid;
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null) return IntVec3.Invalid;
            int minX, minZ, maxX, maxZ;
            if (!component.TryGetResidenceBounds(pawn.Map, out minX, out minZ, out maxX, out maxZ)) return IntVec3.Invalid;
            return CellRect.FromLimits(minX, minZ, maxX, maxZ).Cells
                .Where(cell => cell.InBounds(pawn.Map) && cell.Walkable(pawn.Map) && cell != pawn.Position)
                .Where(cell => cell.GetThingList(pawn.Map).All(thing => !(thing is Pawn) || thing == pawn))
                .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .OrderBy(cell => cell.DistanceToSquared(pawn.Position))
                .DefaultIfEmpty(IntVec3.Invalid).First();
        }

        private static float LureCellScore(Pawn pawn, Pawn target, IntVec3 cell, KitingState state)
        {
            if (pawn == null || target == null || pawn.Map == null) return 0f;
            float distance = cell.DistanceTo(target.Position);
            float radiusScore = -Math.Abs(distance - (MeleeOrbitMinRadius + MeleeOrbitMaxRadius) * 0.5f) * 2f;
            float cover = CoverScore(target, cell, pawn.Map);
            float obstacle = GenRadial.RadialCellsAround(cell, 1.5f, true)
                .Where(adjacent => adjacent.InBounds(pawn.Map))
                .SelectMany(adjacent => adjacent.GetThingList(pawn.Map))
                .Count(IsHardCoverThing);
            float anchor = 0f;
            if (state != null && state.HasAnchor)
            {
                float anchorDistance = cell.DistanceTo(state.AnchorCell);
                anchor = -Math.Abs(anchorDistance - 5f) * 2f;
            }
            return radiusScore + cover * 18f + obstacle * 8f + anchor - pawn.Position.DistanceToSquared(cell) * 0.01f;
        }

        private static void UpdatePawn(Pawn pawn, KitingState state, int tick)
        {
            if (pawn == null || pawn.Downed || pawn.InMentalState || pawn.jobs == null || pawn.equipment == null || pawn.equipment.Primary == null) return;
            if (pawn.CurJob != null && pawn.CurJob.def == JobDefOf.ManTurret) return;
            bool residenceRestricted = IsResidenceRestricted(pawn);

            if (state.HasDestination && pawn.Position == state.Destination && pawn.CurJob == null)
            {
                state.HasDestination = false;
                if (state.ProjectileDodgeMove && RestoreStrategicMove(pawn, state, tick)) return;
                if (!state.ProjectileDodgeMove) state.HasStrategicDestination = false;
            }

            // Projectile avoidance is checked before ordinary combat so grenades, EMP and
            // incendiary rounds cannot be ignored while a pawn is lining up a shot.
            Projectile projectile;
            if (TryFindProjectileThreat(pawn, out projectile))
            {
                if (state.ProjectileDodgeMove && IsCurrentKitingMove(pawn, state)) return;
                IntVec3 microEscape = FindProjectileMicroEscape(pawn, projectile, state, tick);
                if (microEscape.IsValid && microEscape != pawn.Position)
                {
                    IssueProjectileDodgeMove(pawn, microEscape, state, tick);
                    return;
                }
                IntVec3 escape = FindProjectileEscapeFromProjectiles(pawn, projectile, state);
                if (escape.IsValid && escape != pawn.Position) IssueProjectileDodgeMove(pawn, escape, state, tick);
                return;
            }

            if (residenceRestricted && !IsResidenceCell(pawn, pawn.Position))
            {
                IntVec3 residence = FindResidenceCell(pawn);
                if (residence.IsValid) IssueMove(pawn, residence, state, tick);
                return;
            }

            if (RestoreStrategicMove(pawn, state, tick)) return;

            if (HasMeleeWeapon(pawn) && !HasRangedWeapon(pawn))
            {
                UpdateMeleePawn(pawn, state, tick);
                return;
            }
            if (!HasRangedWeapon(pawn)) return;

            Pawn target = FindTarget(pawn, state.TargetId, residenceRestricted);
            if (target == null)
            {
                state.TargetId = 0;
                state.HasDestination = false;
                state.HasStrategicDestination = false;
                state.ProjectileDodgeMove = false;
                state.NeedsRetreat = false;
                state.HasAnchor = false;
                state.HasCover = false;
                state.CoverCell = IntVec3.Invalid;
                return;
            }

            Verb verb = pawn.TryGetAttackVerb(target, false);
            if (verb == null || verb.verbProps == null || verb.verbProps.IsMeleeAttack) return;
            if (state.TargetId != target.thingIDNumber)
            {
                state.TargetId = target.thingIDNumber;
                state.HasDestination = false;
                state.HasStrategicDestination = false;
                state.ProjectileDodgeMove = false;
                state.NeedsRetreat = false;
                state.HasCover = false;
                state.CoverCell = IntVec3.Invalid;
                state.NextOrbitTick = tick;
            }

            RefreshAnchor(pawn, target, state, tick);
            LocalTargetInfo targetInfo = new LocalTargetInfo(target);
            float ownRange = Math.Max(1.5f, verb.EffectiveRange);
            Pawn nearestRangedThreat = FindNearestRangedThreat(pawn);
            Pawn nearestMeleeThreat = FindNearestMeleeThreat(pawn);
            float rangedSafeDistance = nearestRangedThreat == null
                ? 0f
                : EnemyAttackRange(nearestRangedThreat, pawn) + RangedSafetyMargin;
            bool rangedUnsafe = nearestRangedThreat != null &&
                pawn.Position.DistanceTo(nearestRangedThreat.Position) < rangedSafeDistance;
            bool meleeUnsafe = nearestMeleeThreat != null &&
                pawn.Position.DistanceTo(nearestMeleeThreat.Position) < ownRange * 0.5f;
            float requiredSafeDistance = Math.Max(rangedUnsafe ? rangedSafeDistance : 0f,
                meleeUnsafe ? ownRange * 0.5f + RangedSafetyMargin : 0f);
            Pawn retreatThreat = rangedUnsafe ? nearestRangedThreat : (meleeUnsafe ? nearestMeleeThreat : target);
            bool currentSafe = !rangedUnsafe && !meleeUnsafe;
            bool ownCanHit = verb.CanHitTargetFrom(pawn.Position, targetInfo);
            bool weaponReady = WeaponReady(pawn, verb);

            // Movement is mandatory as soon as the enemy reaches the danger distance. Do
            // this before checking the weapon, preventing a pawn from trading shots at point
            // blank range.
            if (!currentSafe)
            {
                state.HasCover = false;
                state.CoverCell = IntVec3.Invalid;
                IntVec3 escape = FindEscapeDestination(pawn, retreatThreat, requiredSafeDistance);
                if (escape.IsValid && escape != pawn.Position) IssueMove(pawn, escape, state, tick);
                return;
            }

            if (IsAttackJobForTarget(pawn.CurJob, target) || IsCurrentKitingMove(pawn, state)) return;

            // When a previous shot returned the pawn to cover, step out to a nearby firing
            // cell before the next shot. This gives the intended peek-shoot-hide cadence.
            if (state.HasCover && pawn.Position == state.CoverCell && weaponReady)
            {
                IntVec3 peek = FindPeekCell(pawn, target, verb, requiredSafeDistance, state.CoverCell);
                if (peek.IsValid && peek != pawn.Position)
                {
                    IssueMove(pawn, peek, state, tick);
                    return;
                }
            }

            // Pick a firing cover once, before the first shot. Open-ground shooters do not
            // receive a retreat job after every shot; they only move when the danger distance
            // check above requires it. Cover users use the explicit peek-shoot-hide cycle.
            if (!state.HasCover && weaponReady)
            {
                IntVec3 cover = FindCoverCell(pawn, target, requiredSafeDistance, state);
                if (cover.IsValid && cover != pawn.Position)
                {
                    state.CoverCell = cover;
                    state.HasCover = true;
                    IssueMove(pawn, cover, state, tick);
                    return;
                }
            }

            // A ready weapon fires once. If cover exists, the queued move is the "peek,
            // shoot, hide" cycle; without cover the pawn stays put until it is unsafe.
            if (ownCanHit && weaponReady && verb.CanHitTarget(targetInfo))
            {
                if (tick - state.LastCommandTick >= ReissueDelayTicks)
                {
                    IntVec3 retreat = FindRetreatDestination(pawn, target, verb, requiredSafeDistance, state);
                    IssueSingleShot(pawn, targetInfo, retreat, state, tick);
                }
                return;
            }

            // While the weapon is cooling down, hold position. Move only when a firing angle
            // is unavailable; this avoids the old move-after-every-shot behavior.
            if (!weaponReady) return;
            IntVec3 destination = FindDestination(pawn, target, verb, requiredSafeDistance, state);
            if (destination.IsValid && destination != pawn.Position) IssueMove(pawn, destination, state, tick);
        }

        private static bool WeaponReady(Pawn pawn, Verb verb)
        {
            if (pawn == null || verb == null || verb.verbProps == null || verb.Bursting || verb.WarmingUp) return false;
            if (!verb.IsStillUsableBy(pawn)) return false;
            if (TicksToNextBurstShotField != null)
            {
                object value = TicksToNextBurstShotField.GetValue(verb);
                if (value is int && (int)value > 0) return false;
            }
            return true;
        }

        private static Job MakeSingleAttackJob(LocalTargetInfo target)
        {
            Job attack = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
            attack.maxNumStaticAttacks = 1;
            attack.endIfCantShootTargetFromCurPos = true;
            return attack;
        }

        private static IntVec3 FindRetreatDestination(Pawn pawn, Pawn target, Verb verb, float minDistance, KitingState state)
        {
            if (state != null && state.HasCover && state.CoverCell.IsValid && state.CoverCell != pawn.Position)
            {
                if (IsRetreatCellSafe(pawn, target, minDistance, state.CoverCell,
                    pawn.Map == null ? Enumerable.Empty<Pawn>() : pawn.Map.mapPawns.AllPawnsSpawned.Where(IsHostileTarget)))
                    return state.CoverCell;
            }
            return IntVec3.Invalid;
        }

        private static IntVec3 FindCoverCell(Pawn pawn, Pawn target, float minDistance, KitingState state)
        {
            if (pawn == null || target == null || pawn.Map == null) return IntVec3.Invalid;
            IEnumerable<IntVec3> cells = state != null && state.HasAnchor
                ? GenRadial.RadialCellsAround(state.AnchorCell, 6f, true)
                : GenRadial.RadialCellsAround(pawn.Position, 8f, true);
            return cells
                .Where(cell => cell.InBounds(pawn.Map) && cell != pawn.Position && cell.Walkable(pawn.Map))
                .Where(cell => IsAllowedKitingCell(pawn, cell))
                .Where(cell => cell.DistanceTo(target.Position) >= minDistance)
                .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .Where(cell => CoverScore(target, cell, pawn.Map) >= 0.45f)
                .Where(cell => !IsProjectileCellThreat(cell, pawn.Map, null))
                .OrderByDescending(cell => CoverScore(target, cell, pawn.Map))
                .ThenBy(cell => cell.DistanceToSquared(pawn.Position))
                .DefaultIfEmpty(IntVec3.Invalid).First();
        }

        private static IntVec3 FindPeekCell(Pawn pawn, Pawn target, Verb verb, float minDistance, IntVec3 coverCell)
        {
            if (pawn == null || target == null || verb == null || pawn.Map == null || !coverCell.IsValid) return IntVec3.Invalid;
            LocalTargetInfo targetInfo = new LocalTargetInfo(target);
            return GenRadial.RadialCellsAround(coverCell, 4f, true)
                .Where(cell => cell.InBounds(pawn.Map) && cell != coverCell && cell.Walkable(pawn.Map))
                .Where(cell => IsAllowedKitingCell(pawn, cell))
                .Where(cell => cell.DistanceTo(target.Position) >= minDistance)
                .Where(cell => cell.GetThingList(pawn.Map).All(thing => !(thing is Pawn) || thing == pawn))
                .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .Where(cell => verb.CanHitTargetFrom(cell, targetInfo))
                .Where(cell => !IsProjectileCellThreat(cell, pawn.Map, null))
                .OrderBy(cell => cell.DistanceToSquared(coverCell))
                .ThenByDescending(cell => cell.DistanceTo(target.Position))
                .DefaultIfEmpty(IntVec3.Invalid).First();
        }

        private static IntVec3 FindEscapeDestination(Pawn pawn, Pawn target, float requiredDistance)
        {
            if (pawn == null || pawn.Map == null) return IntVec3.Invalid;
            float radius = Math.Min(32f, Math.Max(8f, requiredDistance + 4f));
            List<Pawn> enemies = pawn.Map.mapPawns.AllPawnsSpawned.Where(IsHostileTarget).ToList();
            return GenRadial.RadialCellsAround(pawn.Position, radius, true)
                .Where(cell => cell != pawn.Position && cell.InBounds(pawn.Map) && cell.Walkable(pawn.Map))
                .Where(cell => IsAllowedKitingCell(pawn, cell))
                .Where(cell => IsRetreatCellSafe(pawn, target, requiredDistance, cell, enemies))
                .OrderByDescending(cell => RetreatScore(pawn, target, cell, enemies))
                .Take(240)
                .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .DefaultIfEmpty(IntVec3.Invalid).First();
        }

        private static bool IsRetreatCellSafe(Pawn pawn, Pawn target, float requiredDistance, IntVec3 cell, IEnumerable<Pawn> enemies)
        {
            if (pawn == null || pawn.Map == null || !cell.IsValid) return false;
            if (!IsAllowedKitingCell(pawn, cell)) return false;
            if (target != null && cell.DistanceTo(target.Position) + 0.01f < requiredDistance) return false;

            foreach (Pawn enemy in enemies ?? Enumerable.Empty<Pawn>())
            {
                if (enemy == null || !IsHostileTarget(enemy)) continue;
                if (IsDirectMoveToward(pawn.Position, cell, enemy.Position)) return false;
            }
            return true;
        }

        private static bool IsDirectMoveToward(IntVec3 from, IntVec3 to, IntVec3 enemy)
        {
            IntVec3 move = to - from;
            IntVec3 direction = enemy - from;
            long moveLengthSquared = (long)move.x * move.x + (long)move.z * move.z;
            long directionLengthSquared = (long)direction.x * direction.x + (long)direction.z * direction.z;
            if (moveLengthSquared <= 0 || directionLengthSquared <= 0) return false;
            long dot = (long)move.x * direction.x + (long)move.z * direction.z;
            if (dot <= 0) return false;
            long cross = (long)move.x * direction.z - (long)move.z * direction.x;
            // Reject only a move that is exactly collinear with the enemy direction.
            // Sideways, diagonal and orbiting movement are allowed.
            return cross == 0;
        }

        private static float RetreatScore(Pawn pawn, Pawn target, IntVec3 cell, IEnumerable<Pawn> enemies)
        {
            if (pawn == null || pawn.Map == null) return 0f;
            float currentDistance = target == null ? 0f : pawn.Position.DistanceTo(target.Position);
            float candidateDistance = target == null ? 0f : cell.DistanceTo(target.Position);
            float distanceStability = -Math.Abs(candidateDistance - currentDistance) * 6f;
            float cover = target == null ? 0f : CoverScore(target, cell, pawn.Map);
            float nearbyCover = GenRadial.RadialCellsAround(cell, 1.5f, true)
                .Where(adjacent => adjacent.InBounds(pawn.Map))
                .SelectMany(adjacent => adjacent.GetThingList(pawn.Map))
                .Count(IsHardCoverThing);
            float orbit = target == null ? 0f : -OrbitTurnDistance(pawn.Position, cell, target.Position) * 2f;
            float crowding = enemies == null ? 0f : enemies
                .Where(enemy => enemy != null && IsHostileTarget(enemy))
                .Select(enemy => cell.DistanceTo(enemy.Position))
                .DefaultIfEmpty(0f)
                .Min() * 0.75f;
            return distanceStability + cover * 24f + nearbyCover * 10f + orbit + crowding -
                pawn.Position.DistanceToSquared(cell) * 0.03f;
        }

        private static float KiteScore(Pawn pawn, Pawn target, IntVec3 cell)
        {
            if (pawn == null || target == null || pawn.Map == null) return 0f;
            TerrainDef terrain = cell.GetTerrain(pawn.Map);
            float slowTerrain = terrain == null ? 0f : terrain.pathCost;
            float cover = CoverScore(target, cell, pawn.Map);
            float nearbyCover = GenRadial.RadialCellsAround(cell, 1.5f, true)
                .Where(adjacent => adjacent.InBounds(pawn.Map))
                .SelectMany(adjacent => adjacent.GetThingList(pawn.Map))
                .Any(IsHardCoverThing) ? 1f : 0f;
            return cell.DistanceTo(target.Position) * 10f + cover * 8f + slowTerrain * 0.15f + nearbyCover * 6f -
                pawn.Position.DistanceToSquared(cell) * 0.01f;
        }

        private static void IssueSingleShot(Pawn pawn, LocalTargetInfo target, IntVec3 retreat, KitingState state, int tick)
        {
            if (pawn == null || pawn.jobs == null) return;
            if (!pawn.jobs.TryTakeOrderedJob(MakeSingleAttackJob(target))) return;
            if (retreat.IsValid && retreat != pawn.Position)
            {
                pawn.jobs.jobQueue.EnqueueLast(JobMaker.MakeJob(JobDefOf.Goto, new LocalTargetInfo(retreat)));
                state.Destination = retreat;
                state.HasDestination = true;
                state.StrategicDestination = retreat;
                state.HasStrategicDestination = true;
                state.ProjectileDodgeMove = false;
                state.CoverCell = retreat;
                state.HasCover = true;
            }
            else
            {
                state.HasDestination = false;
                state.HasStrategicDestination = false;
                state.ProjectileDodgeMove = false;
                state.HasCover = false;
                state.CoverCell = IntVec3.Invalid;
            }
            state.LastCommandTick = tick;
            state.NextOrbitTick = tick + OrbitDelayTicks;
        }

        private static Pawn FindTarget(Pawn pawn, int targetId, bool requireAttackRange)
        {
            if (pawn == null || pawn.Map == null) return null;
            List<Pawn> hostile = pawn.Map.mapPawns.AllPawnsSpawned.Where(IsHostileTarget).ToList();
            if (hostile.Count == 0) return null;

            // Keep the target while an attack job is actively firing. Once that job ends,
            // always select the nearest living hostile; no elite/threat ranking is used.
            Pawn locked = hostile.FirstOrDefault(candidate => candidate.thingIDNumber == targetId);
            if (locked != null && IsFiringAtTarget(pawn, locked)) return locked;
            if (requireAttackRange)
            {
                hostile = hostile.Where(candidate =>
                {
                    Verb verb = pawn.TryGetAttackVerb(candidate, false);
                    return verb != null && verb.verbProps != null &&
                        candidate.Position.DistanceTo(pawn.Position) <= Math.Max(1.5f, verb.EffectiveRange);
                }).ToList();
                if (hostile.Count == 0) return null;
            }
            return hostile
                .OrderBy(candidate => candidate.Position.DistanceToSquared(pawn.Position))
                .FirstOrDefault();
        }

        private static bool IsFiringAtTarget(Pawn pawn, Pawn target)
        {
            if (pawn == null || target == null || pawn.CurJob == null) return false;
            if ((pawn.CurJob.def != JobDefOf.AttackStatic && pawn.CurJob.def != JobDefOf.AttackMelee) ||
                pawn.CurJob.targetA.Thing != target) return false;
            return true;
        }

        private static Pawn FindNearestRangedThreat(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return null;
            return pawn.Map.mapPawns.AllPawnsSpawned.Where(IsHostileTarget)
                .Where(enemy =>
                {
                    Verb verb = enemy.TryGetAttackVerb(pawn, false);
                    return verb != null && verb.verbProps != null && !verb.verbProps.IsMeleeAttack;
                })
                .OrderBy(enemy => enemy.Position.DistanceToSquared(pawn.Position))
                .FirstOrDefault();
        }

        private static Pawn FindNearestMeleeThreat(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null) return null;
            return pawn.Map.mapPawns.AllPawnsSpawned.Where(IsHostileTarget)
                .Where(enemy =>
                {
                    Verb verb = enemy.TryGetAttackVerb(pawn, false);
                    return verb != null && verb.verbProps != null && verb.verbProps.IsMeleeAttack;
                })
                .OrderBy(enemy => enemy.Position.DistanceToSquared(pawn.Position))
                .FirstOrDefault();
        }

        private static bool IsMeleeAttackJobForTarget(Job job, Pawn target)
        {
            return job != null && job.def == JobDefOf.AttackMelee && target != null && job.targetA.Thing == target;
        }

        private static bool IsHostileTarget(Pawn pawn)
        {
            return pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed && pawn.HostileTo(Faction.OfPlayer);
        }

        private static bool TryFindProjectileThreat(Pawn pawn, out Projectile threat)
        {
            threat = null;
            if (pawn == null || pawn.Map == null || pawn.Map.listerThings == null) return false;
            int bestTicks = Int32.MaxValue;
            float bestDistance = Single.MaxValue;
            foreach (Thing thing in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile))
            {
                Projectile projectile = thing as Projectile;
                if (projectile == null || !projectile.Spawned || !IsHostileProjectile(projectile)) continue;
                IntVec3 destination = ProjectileDestination(projectile);
                float explosionRadius = projectile.def != null && projectile.def.projectile != null
                    ? projectile.def.projectile.explosionRadius : 0f;
                bool direct = (projectile.intendedTarget.HasThing && projectile.intendedTarget.Thing == pawn) ||
                    (projectile.usedTarget.HasThing && projectile.usedTarget.Thing == pawn);
                bool destinationThreat = destination.IsValid && destination.DistanceTo(pawn.Position) <= explosionRadius + ProjectileExtraRadius;
                bool passingThreat = projectile.Position.DistanceTo(pawn.Position) <= 4f && destinationThreat;
                if (!direct && !destinationThreat && !passingThreat) continue;

                int ticks = ProjectileTicks(projectile);
                if (direct && ticks > ProjectileUrgentTicks && projectile.Position.DistanceTo(pawn.Position) > 8f) continue;
                float distance = projectile.ExactPosition.ToIntVec3().DistanceTo(pawn.Position);
                if (ticks < bestTicks || (ticks == bestTicks && distance < bestDistance))
                {
                    threat = projectile;
                    bestTicks = ticks;
                    bestDistance = distance;
                }
            }
            return threat != null;
        }

        private static bool IsHostileProjectile(Projectile projectile)
        {
            if (projectile == null) return false;
            Thing launcher = projectile.Launcher;
            if (launcher == null || launcher.Faction == null) return true;
            return launcher.Faction.HostileTo(Faction.OfPlayer);
        }

        private static int ProjectileTicks(Projectile projectile)
        {
            if (ProjectileTicksToImpactField != null)
            {
                object value = ProjectileTicksToImpactField.GetValue(projectile);
                if (value is int) return Math.Max(0, (int)value);
            }
            if (ProjectileStartingTicksProperty != null)
            {
                object value = ProjectileStartingTicksProperty.GetValue(projectile, null);
                if (value is float) return Math.Max(0, Mathf.RoundToInt((float)value));
            }
            return Int32.MaxValue;
        }

        private static IntVec3 ProjectileDestination(Projectile projectile)
        {
            if (projectile == null) return IntVec3.Invalid;
            try
            {
                if (ProjectileDestinationCellProperty != null)
                {
                    object value = ProjectileDestinationCellProperty.GetValue(projectile, null);
                    if (value is IntVec3) return (IntVec3)value;
                }
            }
            catch
            {
            }
            if (projectile.intendedTarget.Cell.IsValid) return projectile.intendedTarget.Cell;
            if (projectile.usedTarget.Cell.IsValid) return projectile.usedTarget.Cell;
            return projectile.Position;
        }

        private static IntVec3 FindProjectileEscape(Pawn pawn, Projectile projectile)
        {
            if (pawn == null || pawn.Map == null) return IntVec3.Invalid;
            IntVec3 dangerCell = ProjectileDestination(projectile);
            float radius = projectile != null && projectile.def != null && projectile.def.projectile != null
                ? projectile.def.projectile.explosionRadius + ProjectileExtraRadius : ProjectileExtraRadius;
            IEnumerable<IntVec3> candidates = GenRadial.RadialCellsAround(pawn.Position, 8f, true)
                .Where(cell => cell.InBounds(pawn.Map) && cell != pawn.Position && cell.Walkable(pawn.Map))
                .Where(cell => cell.GetThingList(pawn.Map).All(thing => !(thing is Pawn) || thing == pawn))
                .Where(cell => cell.DistanceTo(dangerCell) > radius)
                .Where(cell => IsRetreatCellSafe(pawn, null, 0f, cell,
                    pawn.Map.mapPawns.AllPawnsSpawned.Where(IsHostileTarget)))
                .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .Where(cell => !IsProjectileCellThreat(cell, pawn.Map, projectile));
            return candidates
                .OrderByDescending(cell => cell.DistanceTo(dangerCell))
                .ThenBy(cell => cell.DistanceToSquared(pawn.Position))
                .DefaultIfEmpty(IntVec3.Invalid).First();
        }

        private static IntVec3 FindProjectileEscapeFromProjectiles(Pawn pawn, Projectile primary, KitingState state)
        {
            if (pawn == null || pawn.Map == null || primary == null) return IntVec3.Invalid;
            List<Projectile> threats = FindProjectileThreats(pawn);
            if (!threats.Contains(primary)) threats.Add(primary);
            IEnumerable<IntVec3> candidates = GenRadial.RadialCellsAround(pawn.Position, 12f, true)
                .Where(cell => cell.InBounds(pawn.Map) && cell != pawn.Position && cell.Walkable(pawn.Map))
                .Where(cell => cell.GetThingList(pawn.Map).All(thing => !(thing is Pawn) || thing == pawn))
                .Where(cell => threats.All(projectile => cell.DistanceTo(ProjectileDestination(projectile)) > ProjectileDangerRadius(projectile)))
                .Where(cell => IsRetreatCellSafe(pawn, null, 0f, cell,
                    pawn.Map.mapPawns.AllPawnsSpawned.Where(IsHostileTarget)))
                .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .Where(cell => !IsProjectileCellThreat(cell, pawn.Map, null));
            return candidates
                .OrderByDescending(cell => ProjectileCellSafetyScore(cell, pawn.Position, threats, state))
                .ThenBy(cell => cell.DistanceToSquared(pawn.Position))
                .DefaultIfEmpty(IntVec3.Invalid)
                .First();
        }

        private static IntVec3 FindProjectileMicroEscape(Pawn pawn, Projectile primary, KitingState state, int tick)
        {
            if (pawn == null || pawn.Map == null || primary == null || state == null) return IntVec3.Invalid;
            if (tick - state.LastProjectileDodgeTick < ProjectileMicroDodgeCooldownTicks) return IntVec3.Invalid;

            EnsureProjectileStrategicDestination(pawn, state, tick);
            List<Projectile> threats = FindProjectileThreats(pawn);
            if (!threats.Contains(primary)) threats.Add(primary);
            IntVec3 current = pawn.Position;
            IEnumerable<IntVec3> candidates = GenRadial.RadialCellsAround(current, ProjectileMicroDodgeRadius, true)
                .Where(cell => cell.IsValid && cell.InBounds(pawn.Map) && cell != current && cell.Walkable(pawn.Map))
                .Where(cell => cell.DistanceTo(current) <= ProjectileMicroDodgeRadius)
                .Where(cell => cell.GetThingList(pawn.Map).All(thing => !(thing is Pawn) || thing == pawn))
                .Where(cell => threats.All(projectile => cell.DistanceTo(ProjectileDestination(projectile)) > ProjectileDangerRadius(projectile)))
                .Where(cell => IsRetreatCellSafe(pawn, null, 0f, cell,
                    pawn.Map.mapPawns.AllPawnsSpawned.Where(IsHostileTarget)))
                .Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                .Where(cell => !IsProjectileCellThreat(cell, pawn.Map, primary));

            IntVec3 result = candidates
                .OrderByDescending(cell => ProjectileCellSafetyScore(cell, current, threats, state))
                .ThenBy(cell => cell.DistanceToSquared(current))
                .DefaultIfEmpty(IntVec3.Invalid)
                .First();
            if (result.IsValid)
            {
                state.LastProjectileDodgeTick = tick;
            }
            return result;
        }

        private static void EnsureProjectileStrategicDestination(Pawn pawn, KitingState state, int tick)
        {
            if (pawn == null || state == null || pawn.Map == null || state.HasStrategicDestination) return;
            if (state.HasDestination && !state.ProjectileDodgeMove && state.Destination.IsValid)
            {
                state.StrategicDestination = state.Destination;
                state.HasStrategicDestination = true;
                return;
            }

            Pawn nearest = pawn.Map.mapPawns.AllPawnsSpawned
                .Where(IsHostileTarget)
                .OrderBy(enemy => enemy.Position.DistanceToSquared(pawn.Position))
                .FirstOrDefault();
            if (nearest == null) return;
            float requiredDistance = HasRangedWeapon(pawn)
                ? Math.Max(1.5f, EnemyAttackRange(nearest, pawn) + RangedSafetyMargin)
                : Math.Max(1.5f, EnemyAttackRange(nearest, pawn) + RangedSafetyMargin);
            IntVec3 destination = FindEscapeDestination(pawn, nearest, requiredDistance);
            if (!destination.IsValid || destination == pawn.Position) return;
            state.StrategicDestination = destination;
            state.HasStrategicDestination = true;
        }

        private static List<Projectile> FindProjectileThreats(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null || pawn.Map.listerThings == null) return new List<Projectile>();
            return pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile)
                .OfType<Projectile>()
                .Where(projectile => projectile != null && projectile.Spawned && IsHostileProjectile(projectile))
                .Where(projectile => IsProjectileDirectlyTargeting(projectile, pawn) || IsProjectileNearPawnPath(projectile, pawn) ||
                    (ProjectileDestination(projectile).IsValid &&
                        ProjectileDestination(projectile).DistanceTo(pawn.Position) <= ProjectileDangerRadius(projectile)))
                .OrderBy(projectile => ProjectileTicks(projectile))
                .ToList();
        }

        private static bool IsProjectileDirectlyTargeting(Projectile projectile, Pawn pawn)
        {
            return projectile != null && pawn != null &&
                ((projectile.intendedTarget.HasThing && projectile.intendedTarget.Thing == pawn) ||
                 (projectile.usedTarget.HasThing && projectile.usedTarget.Thing == pawn));
        }

        private static float ProjectileDangerRadius(Projectile projectile)
        {
            if (projectile == null || projectile.def == null || projectile.def.projectile == null) return 0.45f;
            // A normal bullet resolves on its landing cell; applying an
            // explosion-sized radius to it would reject almost every adjacent
            // dodge cell. Explosive rounds retain their real blast radius.
            return projectile.def.projectile.explosionRadius > 0f
                ? projectile.def.projectile.explosionRadius + ProjectileExtraRadius
                : 0.45f;
        }

        private static bool IsProjectileNearPawnPath(Projectile projectile, Pawn pawn)
        {
            if (projectile == null || pawn == null) return false;
            bool direct = (projectile.intendedTarget.HasThing && projectile.intendedTarget.Thing == pawn) ||
                (projectile.usedTarget.HasThing && projectile.usedTarget.Thing == pawn);
            if (direct) return true;
            Vector3 start = projectile.ExactPosition;
            Vector3 end = ProjectileDestination(projectile).ToVector3Shifted();
            float distance = DistanceToSegment(pawn.Position.ToVector3Shifted(), start, end);
            return distance <= 1.5f;
        }

        private static float ProjectileCellSafetyScore(IntVec3 cell, IntVec3 current, IEnumerable<Projectile> threats, KitingState state)
        {
            if (!cell.IsValid || threats == null) return Single.MinValue;
            Vector3 point = cell.ToVector3Shifted();
            float score = 0f;
            foreach (Projectile projectile in threats)
            {
                if (projectile == null || !projectile.Spawned) continue;
                Vector3 end = ProjectileDestination(projectile).ToVector3Shifted();
                float distance = DistanceToSegment(point, projectile.ExactPosition, end);
                int ticks = ProjectileTicks(projectile);
                float weight = ticks == Int32.MaxValue ? 0.1f : 1f / (1f + Math.Max(0, ticks));
                score += distance * weight * 10f;
            }
            if (state != null && state.HasStrategicDestination && state.StrategicDestination.IsValid)
            {
                float currentRouteDistance = current.DistanceToSquared(state.StrategicDestination);
                float candidateRouteDistance = cell.DistanceToSquared(state.StrategicDestination);
                score += (currentRouteDistance - candidateRouteDistance) * 0.75f;
            }
            score -= cell.DistanceToSquared(current) * 0.25f;
            return score;
        }

        private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = (end - start).Yto0();
            Vector3 relative = (point - start).Yto0();
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.0001f) return relative.magnitude;
            float t = Mathf.Clamp01(Vector3.Dot(relative, segment) / lengthSquared);
            return (relative - segment * t).magnitude;
        }

        private static bool IsProjectileCellThreat(IntVec3 cell, Map map, Projectile ignored)
        {
            if (map == null || !cell.IsValid) return false;
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile))
            {
                Projectile projectile = thing as Projectile;
                if (projectile == null || projectile == ignored || !projectile.Spawned || !IsHostileProjectile(projectile)) continue;
                IntVec3 destination = ProjectileDestination(projectile);
                float radius = projectile.def != null && projectile.def.projectile != null
                    ? projectile.def.projectile.explosionRadius + ProjectileExtraRadius : ProjectileExtraRadius;
                if (destination.IsValid && destination.DistanceTo(cell) <= radius) return true;
            }
            return false;
        }

        private static float EnemyAttackRange(Pawn enemy, Pawn pawn)
        {
            Verb verb = enemy == null || pawn == null ? null : enemy.TryGetAttackVerb(pawn, false);
            return verb == null ? 1.5f : Math.Max(1.5f, verb.EffectiveRange);
        }

        private static Thing FindDefenseAnchor(Map map, Pawn target)
        {
            if (map == null || target == null) return null;
            return map.listerThings.AllThings
                .Where(IsTacticalAnchor)
                .Where(thing => thing.Faction == null || thing.Faction == Faction.OfPlayer)
                .Where(thing => DefenseRange(thing) <= 0f || thing.Position.DistanceTo(target.Position) <= DefenseRange(thing))
                .OrderByDescending(AnchorPriority)
                .ThenBy(thing => thing.Position.DistanceToSquared(target.Position))
                .FirstOrDefault(thing => thing.Position.DistanceToSquared(target.Position) <= 45f * 45f);
        }

        private static bool IsTacticalAnchor(Thing thing)
        {
            return IsDefenseThing(thing) || IsHardCoverThing(thing);
        }

        private static int AnchorPriority(Thing thing)
        {
            if (thing == null || thing.def == null) return 0;
            if (thing.def.building != null && thing.def.building.IsTurret) return 3;
            if (thing.def.defName == "Sandbags" || thing.def.defName.IndexOf("Trap", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 1;
        }

        private static bool IsDefenseThing(Thing thing)
        {
            if (thing == null || !thing.Spawned || thing.def == null) return false;
            if (thing.def.defName == "Sandbags" || thing.def.defName.IndexOf("Trap", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return thing.def.building != null && thing.def.building.IsTurret;
        }

        private static bool IsHardCoverThing(Thing thing)
        {
            return thing != null && thing.Spawned && thing.def != null && thing.def.building != null &&
                thing.def.passability == Traversability.Impassable && thing.def.fillPercent >= 0.75f;
        }

        private static float DefenseRange(Thing thing)
        {
            if (thing == null || thing.def == null || thing.def.building == null || !thing.def.building.IsTurret ||
                thing.def.building.turretGunDef == null) return 0f;
            return thing.def.building.turretGunDef.Verbs
                .Where(verb => verb != null && !verb.IsMeleeAttack)
                .Select(verb => verb.range)
                .DefaultIfEmpty(0f)
                .Max();
        }

        private static IntVec3 FindDestination(Pawn pawn, Pawn target, Verb verb, float minimumDistance, KitingState state)
        {
            float maxRange = Math.Min(Math.Max(verb.EffectiveRange, 2f), 40f);
            float minSafeRange = minimumDistance;
            if (minSafeRange > maxRange - 0.5f) return IntVec3.Invalid;

            IEnumerable<IntVec3> cells;
            if (state.HasAnchor)
            {
                cells = GenRadial.RadialCellsAround(state.AnchorCell, 7f, true);
            }
            else
            {
                cells = GenRadial.RadialCellsAround(target.Position, maxRange, true);
            }

            // Limit path checks to a compact outer ring. Calling CanReach for every cell in a
            // 40-cell radius is needlessly expensive on large maps.
            List<IntVec3> candidates = cells
                .Where(cell => IsBasicCandidate(pawn, target, cell, minSafeRange, maxRange))
                .OrderByDescending(cell => cell.DistanceTo(target.Position))
                .Take(240)
                .Where(cell => IsUsableCell(pawn, target, verb, cell))
                .ToList();
            if (candidates.Count == 0 && state.HasAnchor)
            {
                // If the defense is behind a wall or otherwise unreachable, fall back to an ordinary kite.
                state.HasAnchor = false;
                state.HasDestination = false;
                return FindDestination(pawn, target, verb, minimumDistance, state);
            }
            if (candidates.Count == 0) return IntVec3.Invalid;

            if (state.HasAnchor)
            {
                if (state.AnchorRange > 0f)
                {
                    List<IntVec3> turretRangeCells = candidates
                        .Where(cell => cell.DistanceTo(target.Position) <= state.AnchorRange)
                        .ToList();
                    if (turretRangeCells.Count == 0)
                    {
                        state.HasAnchor = false;
                        state.HasDestination = false;
                        return FindDestination(pawn, target, verb, minimumDistance, state);
                    }
                    candidates = turretRangeCells;
                }
                List<IntVec3> orbit = candidates
                    .OrderBy(cell => Math.Atan2(cell.z - state.AnchorCell.z, cell.x - state.AnchorCell.x))
                    .ToList();
                int index = state.OrbitIndex < 0 ? 0 : state.OrbitIndex % orbit.Count;
                state.OrbitIndex = (index + 1) % orbit.Count;
                return orbit[index];
            }

            return candidates
                .OrderByDescending(cell => KiteScore(pawn, target, cell))
                .First();
        }

        private static bool IsBasicCandidate(Pawn pawn, Pawn target, IntVec3 cell, float minSafeRange, float maxRange)
        {
            if (pawn == null || target == null || !cell.InBounds(pawn.Map) || !cell.Walkable(pawn.Map)) return false;
            if (!IsAllowedKitingCell(pawn, cell)) return false;
            if (cell.GetThingList(pawn.Map).Any(thing => thing is Pawn && thing != pawn)) return false;
            if (cell.GetThingList(pawn.Map).Any(IsDefenseThing)) return false;
            float distance = cell.DistanceTo(target.Position);
            if (distance < minSafeRange || distance > maxRange) return false;
            return true;
        }

        private static bool IsUsableCell(Pawn pawn, Pawn target, Verb verb, IntVec3 cell)
        {
            if (pawn == null || target == null || verb == null) return false;
            LocalTargetInfo targetInfo = new LocalTargetInfo(target);
            return pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly) && verb.CanHitTargetFrom(cell, targetInfo);
        }

        private static float CoverScore(Pawn target, IntVec3 cell, Map map)
        {
            if (target == null || map == null) return 0f;
            return CoverUtility.CalculateCoverGiverSet(new LocalTargetInfo(target), cell, map).Sum(info => info.BlockChance);
        }

        private static bool IsKitingJob(Job job, Pawn target)
        {
            return IsAttackJobForTarget(job, target) || (job != null && job.def == JobDefOf.Goto);
        }

        private static bool IsCurrentKitingMove(Pawn pawn, KitingState state)
        {
            if (pawn == null || state == null || !state.HasDestination || pawn.CurJob == null || pawn.CurJob.def != JobDefOf.Goto) return false;
            return pawn.CurJob.targetA.Cell == state.Destination;
        }

        private static bool IsAttackJobForTarget(Job job, Pawn target)
        {
            return job != null && job.def == JobDefOf.AttackStatic && target != null && job.targetA.Thing == target;
        }
    }
}
