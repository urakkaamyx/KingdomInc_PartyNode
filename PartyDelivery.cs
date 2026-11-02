using System;
using System.Collections.Generic;
using System.Linq;
using Assets.Source.Flow;
using Assets.Source.Flow.Elements;
using Assets.Source.Items;
using HarmonyLib;

namespace KingdomPartyNode
{
    internal static class PartyDelivery
    {
        internal static bool Plan(FlowOutput output, IReadOnlyList<InventoryItem> members, out FlowInput[] slots, out string reason)
        {
            slots = null; reason = null;
            if (members.Count == 0) return false;
            var pipe = output.ConnectedPipe;
            var input = pipe?.ConnectedInput;
            if (input == null || pipe.ConnectedOutput != output || input.ConnectedPipe != pipe || input.Parent == output.Parent)
            { reason = "Connect output"; return false; }

            // These nodes move their input into an internal roster/store one item
            // per tick. Queue a whole admitted party without enlarging that store.
            int? capacity = null;
            if (input.ParentIndex == 0)
            {
                if (input.Parent is Stockpile stockpile) capacity = stockpile.Capacity - stockpile.Contents.Count;
                else if (input.Parent is Inn inn) capacity = inn.Contents.Count(s => s == null);
                else if (input.Parent is TrainingGrounds training) capacity = training.Capacity - training.Contents.Count(s => s != null);
                else if (input.Parent is QuestBoard quest) capacity = QuestBoard.SlotCount - quest.Contents.Count;
            }
            if (capacity.HasValue)
            {
                if (capacity.Value - input.BufferedItems.Count < members.Count)
                { reason = "Need room · " + members.Count; return false; }
                if (members.Any(member => !input.CanAcceptItem(member)))
                { reason = "Target rejects"; return false; }
                slots = Enumerable.Repeat(input, members.Count).ToArray(); return true;
            }

            // A dungeon/another Party has individual adventurer slots. One party
            // connection fills compatible free slots; other wired inputs are kept
            // for their own source. Ordinary routing nodes use the connected port.
            var inputs = new List<FlowInput> { input };
            if (input.Parent is Dungeon || input.Parent is Party)
                inputs.AddRange(input.Parent.Inputs.Where(i => i != input && i.ConnectedPipe == null));
            var free = inputs.Select(i => Math.Max(0, i.BufferSize - i.BufferedItems.Count)).ToArray();
            if (free.Sum() < members.Count)
            { reason = "Need room · " + members.Count; return false; }
            var plan = new FlowInput[members.Count];
            if (!Assign(0, members, inputs, free, plan))
            { reason = "Target rejects"; return false; }
            slots = plan; return true;
        }

        static bool Assign(int member, IReadOnlyList<InventoryItem> members, List<FlowInput> inputs, int[] free, FlowInput[] plan)
        {
            if (member == members.Count) return true;
            for (int i = 0; i < inputs.Count; i++)
            {
                if (free[i] == 0 || !inputs[i].CanAcceptItem(members[member])) continue;
                free[i]--; plan[member] = inputs[i];
                if (Assign(member + 1, members, inputs, free, plan)) return true;
                free[i]++;
            }
            return false;
        }

        [HarmonyPatch(typeof(FlowPipe), nameof(FlowPipe.Update))]
        static class TransferWholeParty
        {
            static bool Prefix(FlowPipe __instance)
            {
                var output = __instance.ConnectedOutput;
                if (!(output?.Parent is Party)) return true;
                __instance.ItemRejected = false;
                if (!output.HasBuffer || output.ConnectedPipe != __instance) return false;
                var members = output.BufferedItems.ToArray();
                if (!Plan(output, members, out var slots, out var reason))
                {
                    __instance.ItemRejected = reason == "Target rejects";
                    return false;
                }
                // No simulation tick can interleave these writes. Either every
                // original member enters a reserved input queue, or none leave.
                for (int i = 0; i < members.Length; i++) slots[i].BufferedItems.Add(members[i]);
                output.BufferedItems.Clear();
                foreach (var member in members) __instance.World?.ItemTransferred(member);
                return false;
            }
        }
    }
}
