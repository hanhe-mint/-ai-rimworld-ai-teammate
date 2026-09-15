using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AICoopCompanion
{
    [HarmonyPatch(typeof(Pawn_DraftController), "set_Drafted")]
    internal static class AICoopDraftKitingPatch
    {
        public static void Postfix(Pawn_DraftController __instance, bool __0)
        {
            Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            AICoopKitingManager.NotifyDraftChanged(pawn, __0);
        }
    }

    [HarmonyPatch(typeof(Root), "Update")]
    internal static class AICoopRootUpdatePatch
    {
        public static void Postfix()
        {
            AICoopKitingManager.PollDraftStates();
            AICoopAgentRuntime.Update();
            AICoopAgentBridge.PumpMainThread();
        }
    }

    internal static class AICoopOwnershipMenu
    {
        public static bool CanChange(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && pawn.IsColonist && pawn.Faction == Faction.OfPlayer && pawn.RaceProps.Humanlike;
        }

        public static FloatMenuOption CreateOption(Pawn pawn)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            bool changeToAI = component != null && !component.IsAI(pawn);
            string label = changeToAI ? "更改为 AI 操控" : "更改为玩家操控";
            return new FloatMenuOption(label, delegate
            {
                AICoopGameComponent current = AICoopGameComponent.Current;
                if (current == null || !CanChange(pawn)) return;
                AICoopOwner owner = current.IsAI(pawn) ? AICoopOwner.Player : AICoopOwner.AI;
                current.AssignNewRecruit(pawn, owner);
                if (owner == AICoopOwner.AI && Find.Selector != null) Find.Selector.Deselect(pawn);
                current.AddLog("[系统] " + pawn.LabelShort + " 已更改为" + (owner == AICoopOwner.AI ? " AI 操控。" : "玩家操控。"));
            }, null, AICoopAssets.AIIcon);
        }

        public static void Show(Pawn pawn)
        {
            if (!CanChange(pawn) || AICoopGameComponent.Current == null) return;
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> { CreateOption(pawn) }));
        }

        public static bool TryGetMapPawn(out Pawn pawn)
        {
            pawn = null;
            if (Find.CurrentMap == null || !WorldRendererUtility.DrawingMap) return false;
            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(Find.CurrentMap)) return false;
            List<Thing> things = GenUI.ThingsUnderMouse(UI.MouseMapPosition(), 0.8f, TargetingParameters.ForPawns());
            foreach (Thing thing in things)
            {
                Pawn candidate = thing as Pawn;
                if (CanChange(candidate))
                {
                    pawn = candidate;
                    return true;
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ColonistBar), "CheckRecacheEntries")]
    public static class ColonistBarOrderPatch
    {
        public static void Postfix(List<ColonistBar.Entry> ___cachedEntries)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null || ___cachedEntries == null || ___cachedEntries.Count < 2) return;

            List<ColonistBar.Entry> ordered = new List<ColonistBar.Entry>(___cachedEntries.Count);
            int index = 0;
            while (index < ___cachedEntries.Count)
            {
                int originalGroup = ___cachedEntries[index].group;
                int end = index;
                while (end < ___cachedEntries.Count && ___cachedEntries[end].group == originalGroup) end++;

                for (int i = index; i < end; i++)
                {
                    ColonistBar.Entry entry = ___cachedEntries[i];
                    if (entry.pawn == null || !component.IsAI(entry.pawn)) ordered.Add(entry);
                }
                for (int i = index; i < end; i++)
                {
                    ColonistBar.Entry entry = ___cachedEntries[i];
                    if (entry.pawn != null && component.IsAI(entry.pawn)) ordered.Add(entry);
                }
                index = end;
            }

            ___cachedEntries.Clear();
            ___cachedEntries.AddRange(ordered);
        }
    }

    [HarmonyPatch(typeof(FloatMenuMakerMap), "GetOptions")]
    public static class MapOwnershipMenuOptionPatch
    {
        public static void Postfix(ref FloatMenuContext context, ref List<FloatMenuOption> __result)
        {
            if (AICoopGameComponent.Current == null || context == null || __result == null || context.ClickedPawns == null) return;
            foreach (Pawn pawn in context.ClickedPawns)
            {
                if (!AICoopOwnershipMenu.CanChange(pawn)) continue;
                __result.Add(AICoopOwnershipMenu.CreateOption(pawn));
                return;
            }
        }
    }

    [HarmonyPatch(typeof(Selector), "SelectorOnGUI")]
    public static class MapDirectOwnershipMenuPatch
    {
        public static void Prefix(Selector __instance)
        {
            if (Event.current.type != EventType.MouseDown || Event.current.button != 1 || __instance.SelectedPawns.Count != 0) return;
            Pawn pawn;
            if (!AICoopOwnershipMenu.TryGetMapPawn(out pawn)) return;
            Event.current.Use();
            AICoopOwnershipMenu.Show(pawn);
        }
    }

    [HarmonyPatch(typeof(Selector), "Select")]
    public static class SelectorSelectPatch
    {
        public static bool Prefix(object obj)
        {
            if (AICoopMod.Settings == null || AICoopMod.Settings.allowSharedControl) return true;
            if (AICoopGameComponent.Current == null) return true;
            Pawn pawn = obj as Pawn;
            if (pawn != null && AICoopGameComponent.Current.IsAI(pawn))
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ColonistBarColonistDrawer), "DrawColonist")]
    public static class ColonistBarDrawPatch
    {
        public static void Postfix(UnityEngine.Rect rect, Pawn colonist)
        {
            if (colonist == null || AICoopGameComponent.Current == null || !AICoopGameComponent.Current.IsAI(colonist)) return;
            Texture2D icon = AICoopAssets.AIIcon;
            if (icon == null) return;
            UnityEngine.Rect iconRect = new UnityEngine.Rect(rect.xMax - 22f, rect.yMin + 2f, 20f, 20f);
            UnityEngine.GUI.DrawTexture(iconRect, icon, UnityEngine.ScaleMode.ScaleToFit, true);
        }
    }

    [HarmonyPatch(typeof(ColonistBar), "ColonistBarOnGUI")]
    public static class ColonistBarOwnershipMenuPatch
    {
        public static void Prefix(ColonistBar __instance)
        {
            if (Event.current.type != EventType.MouseDown || Event.current.button != 1) return;
            ColonistBar.Entry entry;
            if (!__instance.TryGetEntryAt(UI.MousePositionOnUIInverted, out entry) || !AICoopOwnershipMenu.CanChange(entry.pawn)) return;
            Event.current.Use();
            AICoopOwnershipMenu.Show(entry.pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_GuestTracker), "Notify_PawnRecruited")]
    public static class RecruitPatch
    {
        private static readonly System.Collections.Generic.Dictionary<int, AICoopOwner> PendingOwners = new System.Collections.Generic.Dictionary<int, AICoopOwner>();

        [HarmonyPatch(typeof(Pawn_GuestTracker), "SetRecruitmentData")]
        public static class RecruitmentDataPatch
        {
            public static void Postfix(Pawn_GuestTracker __instance, Pawn recruiter)
            {
                Pawn guest = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
                if (guest == null) return;
                AICoopGameComponent component = AICoopGameComponent.Current;
                PendingOwners[guest.thingIDNumber] = recruiter != null && component != null && component.IsAI(recruiter) ? AICoopOwner.AI : AICoopOwner.Player;
            }
        }

        public static void Postfix(Pawn_GuestTracker __instance)
        {
            Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn == null) return;
            AICoopOwner owner;
            if (!PendingOwners.TryGetValue(pawn.thingIDNumber, out owner)) owner = AICoopOwner.Player;
            PendingOwners.Remove(pawn.thingIDNumber);
            AICoopGameComponent.Current.AssignNewRecruit(pawn, owner);
            AICoopGameComponent.Current.AddLog(pawn.LabelShort + " 已招募，归属 " + (owner == AICoopOwner.AI ? "AI" : "玩家") + "。");
        }
    }

    [HarmonyPatch(typeof(Pawn_GuestTracker), "SetGuestStatus")]
    public static class PrisonerStatusPatch
    {
        // Use positional injection because parameter names differ between RimWorld 1.6 builds.
        public static void Postfix(Pawn_GuestTracker __instance, GuestStatus __1)
        {
            if (__1 != GuestStatus.Prisoner) return;
            Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (pawn != null && component != null) component.CheckBedCapacityForPawn(pawn, "俘虏新囚犯");
        }
    }

    [HarmonyPatch(typeof(Pawn_GuestTracker), "CapturedBy")]
    public static class PrisonerCapturedByPatch
    {
        public static void Postfix(Pawn_GuestTracker __instance, Pawn byPawn)
        {
            Pawn prisoner = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (prisoner != null && byPawn != null && component != null) component.RecordPrisonerCapture(prisoner, byPawn);
        }
    }

    internal static class AICoopFailedConstructionGuard
    {
        public static bool ShouldBlockResources(Pawn pawn, Thing thing)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            return component != null && component.IsAI(pawn) && component.IsConstructionWaitingForPlayer(thing);
        }

        public static bool ShouldBlockConstruction(Pawn pawn, Thing thing)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            return component != null && component.IsAI(pawn) && !component.CanAIConstruct(pawn, thing);
        }
    }

    [HarmonyPatch(typeof(Frame), "FailConstruction")]
    public static class ConstructionFailurePatch
    {
        public static void Prefix(Frame __instance, Pawn worker)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component != null) component.RecordConstructionFailure(__instance, worker);
        }
    }

    [HarmonyPatch(typeof(Frame), "CompleteConstruction")]
    public static class ConstructionSuccessPatch
    {
        public static void Prefix(Frame __instance, ref System.Tuple<Map, IntVec3, BuildableDef> __state)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component != null)
            {
                if (__instance != null && __instance.Map != null && __instance.def != null && __instance.def.entityDefToBuild != null &&
                    component.ConsumeAIPlannedConstruction(__instance.Map, __instance.Position, __instance.def.entityDefToBuild))
                    __state = new System.Tuple<Map, IntVec3, BuildableDef>(__instance.Map, __instance.Position, __instance.def.entityDefToBuild);
                component.RecordConstructionSuccess(__instance);
            }
        }

        public static void Postfix(System.Tuple<Map, IntVec3, BuildableDef> __state)
        {
            if (__state != null)
            {
                AICoopActionExecutor.ConfigureAutomaticProductionBill(__state.Item1, __state.Item2, __state.Item3 as ThingDef);
                AICoopPresetManager.ApplyPendingState(__state.Item1, __state.Item2);
            }
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component != null) component.TryRecordDefensePresetCompletion();
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), "HasJobOnThing")]
    public static class FailedBlueprintHasJobPatch
    {
        public static bool Prefix(Pawn pawn, Thing t, ref bool __result)
        {
            if (!AICoopFailedConstructionGuard.ShouldBlockResources(pawn, t)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToBlueprints), "JobOnThing")]
    public static class FailedBlueprintJobPatch
    {
        public static bool Prefix(Pawn pawn, Thing t, ref Job __result)
        {
            if (!AICoopFailedConstructionGuard.ShouldBlockResources(pawn, t)) return true;
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToFrames), "HasJobOnThing")]
    public static class FailedFrameHasJobPatch
    {
        public static bool Prefix(Pawn pawn, Thing t, ref bool __result)
        {
            if (!AICoopFailedConstructionGuard.ShouldBlockResources(pawn, t)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResourcesToFrames), "JobOnThing")]
    public static class FailedFrameResourceJobPatch
    {
        public static bool Prefix(Pawn pawn, Thing t, ref Job __result)
        {
            if (!AICoopFailedConstructionGuard.ShouldBlockResources(pawn, t)) return true;
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructFinishFrames), "JobOnThing")]
    public static class FailedFrameFinishJobPatch
    {
        public static bool Prefix(Pawn pawn, Thing t, ref Job __result)
        {
            if (!AICoopFailedConstructionGuard.ShouldBlockConstruction(pawn, t)) return true;
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_ConstructDeliverResources), "IsNewValidNearbyNeeder")]
    public static class FailedConstructionNearbyResourcePatch
    {
        public static void Postfix(Thing t, Pawn pawn, ref bool __result)
        {
            if (__result && AICoopFailedConstructionGuard.ShouldBlockResources(pawn, t)) __result = false;
        }
    }

    [StaticConstructorOnStartup]
    public static class AICoopAssets
    {
        static AICoopAssets()
        {
            _icon = ContentFinder<Texture2D>.Get("AIIcon", false);
        }

        public static Texture2D AIIcon
        {
            get
            {
                return _icon;
            }
        }
        private static Texture2D _icon;
    }
}
