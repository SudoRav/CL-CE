using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Inventory {

    using OptimizeApparel = JobGiver_OptimizeApparel;

    public class ThinkNode_LoadoutRealisation : ThinkNode_ConditionalColonist {

        private Dictionary<Pawn, int> nextUpdateTick = new Dictionary<Pawn, int>();

        private static JobDef EquipApparel => JobDefOf.Wear;
        private static JobDef EquipItem => JobDefOf.TakeInventory;
        private static JobDef HoldItem => JobDefOf.Equip;
        private static JobDef UnloadItem => InvJobDefOf.CL_UnloadInventory;
        
        public ThinkNode_LoadoutRealisation() { }

        public override bool Satisfied(Pawn pawn) {
            if (!pawn.IsValidLoadoutHolder()) return true;
            // nothing to do on a caravan
            if (pawn.Map == null) return true;

            return false;
        }

        public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams) {
            // For some reason, the `ThinkNode_SubtreesByTag` does not check to see if the ThinkNode is satisfied before querying it, which would negate the need for this check.
            if (!pawn.IsValidLoadoutHolder()) {
                return ThinkResult.NoJob;
            }

            // This is a relatively high priority node on the thinknode, so we do some sanity safeguards to check that the pawn
            // isn't shirking 'more important' responsibilities, like eating when they are starving, or walking around while they are dying!
            if (HealthUtility.TicksUntilDeathDueToBloodLoss(pawn) < 45000) {
                return ThinkResult.NoJob;
            }

            if (pawn.needs.food is not null && pawn.needs.food.CurLevelPercentage < pawn.needs.food.PercentageThreshUrgentlyHungry) {
                return ThinkResult.NoJob;
            }

            var comp = pawn.TryGetComp<LoadoutComponent>();
            
            if (comp.Loadout.NeedsUpdate || PawnNeedsUpdate(pawn)) {
                var job = SatisfyLoadoutClothingJob(pawn, comp.Loadout);
                if (job != null) {
                    return new ThinkResult(job, this);
                }

                // if we have no job, it means there are no items on the map which match those required
                // by the pawns loadout, or their loadout is fully satisfied, either way we do not need
                // to re-check the pawns loadout status for a while (10-15k) ticks.
                SetPawnLastUpdated(pawn);
                comp.Loadout.Updated();
            }

            return ThinkResult.NoJob;
        }

        private bool PawnNeedsUpdate(Pawn pawn) {
            if (!nextUpdateTick.TryGetValue(pawn, out var nextTick)) {
                nextTick = 0;
                nextUpdateTick.Add(pawn, nextTick);
            }

            return GenTicks.TicksAbs >= nextTick;
        }

        // pre-condition that in `nextUpdateTick` there should be an entry for `pawn`
        private void SetPawnLastUpdated(Pawn pawn) {
            nextUpdateTick[pawn] = GenTicks.TicksAbs + Rand.Range(10_000, 15_000);
        }

        private Job RemoveThingsJob(Pawn pawn, Loadout loadout) {
            return null;
        }

        // a heavily modified version of JobGiver_OptimizeApparel:TryGiveJob
        private Job SatisfyLoadoutClothingJob(Pawn pawn, Loadout loadout) {
            var wornApparel = pawn.apparel.WornApparel;
            var desiredApparel = loadout.HypotheticalWornApparel(loadout.CurrentState, pawn.def.race.body).ToList();

            if (desiredApparel.Count == 0) {
                return null;
            }

            OptimizeApparel.neededWarmth = PawnApparelGenerator.CalculateNeededWarmth(pawn, pawn.Map.Tile, GenLocalDate.Twelfth(pawn));
            OptimizeApparel.wornApparelScores.Clear();
            foreach (var apparel in wornApparel) {
                OptimizeApparel.wornApparelScores.Add(OptimizeApparel.ApparelScoreRaw(pawn, apparel));
            }

            foreach (var desiredItem in desiredApparel) {
                if (wornApparel.Any(desiredItem.Allows)) {
                    continue;
                }

                Apparel bestApparel = null;
                var bestApparelScore = float.MinValue;
                var bestDistance = float.MaxValue;

                foreach (var apparel in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel).OfType<Apparel>().Where(desiredItem.Allows)) {
                    if (!ValidApparelFor(apparel, pawn)) {
                        continue;
                    }

                    var apparelScore = OptimizeApparel.ApparelScoreGain(pawn, apparel, OptimizeApparel.wornApparelScores);
                    if (apparelScore < 0.05f) continue;

                    var distance = pawn.Position.DistanceToSquared(apparel.Position);
                    if (apparelScore > bestApparelScore || (Mathf.Approximately(apparelScore, bestApparelScore) && distance < bestDistance)) {
                        bestApparel = apparel;
                        bestApparelScore = apparelScore;
                        bestDistance = distance;
                    }
                }

                if (bestApparel != null) {
                    return JobMaker.MakeJob(EquipApparel, bestApparel);
                }
            }

            return null;
        }

        private bool ValidApparelFor(Apparel apparel, Pawn pawn) {
            if (!pawn.outfits.CurrentApparelPolicy.filter.Allows(apparel)) return false;
            if (apparel.def.apparel.gender != Gender.None && apparel.def.apparel.gender != pawn.gender) return false;
            if (!RimWorld.ApparelUtility.HasPartsToWear(pawn, apparel.def)) return false;

            return Utility.ShouldAttemptToEquip(pawn, apparel);
        }

        private Job SatisfyLoadoutItemsJob(Pawn pawn, Loadout loadout) {
            return null;
        }

        private Job FindItem(Pawn pawn, Item item, int count) {
            return null;
        }

        // Mostly for better bio-coded weapon integration.
        private IOrderedEnumerable<Thing> DecideItemPriority(Pawn pawn, List<Thing> things)
        {
            return things.OrderBy(t => t.InteractionCell.DistanceToSquared(pawn.InteractionCell));
        }

        private Job RemoveItem(List<Thing> pawnGear, Item item, int count) {
            return null;
        }

    }

}
