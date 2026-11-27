using AntiqueTradingSimulator.Auctions;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using AntiqueTradingSimulator.Restoration;
using AntiqueTradingSimulator.Rumours;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static AntiqueTradingSimulator.Events.EventEffect;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Contract work: offers, urgency, reachability and fulfilment.
    /// One part of NPCTrader, which is a partial class.
    /// </summary>
    public partial class NPCTrader
    {
        // ---------------------------------------------------------------- contract tick actions

        private bool WorkOnContracts(int week, NpcBehaviorProfile profile, bool urgentOnly)
        {
            if (_contractManager == null) return false;

            CollectActiveContracts(week, profile);
            if (_contractScratch.Count == 0) return false;

            foreach (var contract in _contractScratch)
            {
                int missing = MissingFor(contract);
                if (missing <= 0) continue;

                var urgency = UrgencyOf(contract, week, profile);
                if (urgentOnly && urgency == ContractUrgency.Normal) continue;
                if (!urgentOnly && urgency != ContractUrgency.Normal) continue;

                var offer = BestOfferFor(contract, week, profile, urgency);

                // Restoring stock already in the warehouse costs only the fee plus the resale value given up.
                if (PlanStockRestoration(contract, week, profile, offer))
                {
                    if (!urgentOnly) _contractSlotsUsed++;
                    return true;
                }

                if (offer.Listing == null) continue;

                float cost = offer.Cost;
                if (!Core.Money.Covers(Inventory.Cash, cost)) continue;

                if (!BuyListing(offer.Listing.Id, offer.Option)) continue;

                RecordPlan(offer.Listing.Id, offer.Listing.PurchasePrice, week, PlanPurpose.Contract, contract.ContractId);

                if (offer.NeedsRestoration)
                    AttachRestorationLeg(offer.Listing.Id, offer.RestorationOptionId, offer.RestorationFee,
                        LatestUsefulArrivalWeek(contract) - offer.RestorationWeeks);
                else
                    Inventory.ReserveForContract(offer.Listing.Id, contract.ContractId);

                _contractReserve = CalculateContractReserve();
                if (!urgentOnly) _contractSlotsUsed++;

                Debug.Log($"{TraderName} bought {offer.Listing.Name} for contract {contract.ContractId} ({urgency}, {offer.Option} transport, ready week {offer.ArrivalWeek}).");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Pledges the cheapest held antique a workshop could bring up to a contract's requirement in
        /// time, when that beats buying one. TierExecutePlans starts the work.
        /// </summary>
        private bool PlanStockRestoration(Contract contract, int week, NpcBehaviorProfile profile, Offer offer)
        {
            if (_restorationManager == null || !profile.Restores || !profile.RestoresForContracts) return false;

            float buyCost = offer.Listing != null ? offer.Cost : float.MaxValue;

            Antique chosen = null;
            RestorationQuote chosenQuote = null;
            float chosenCost = buyCost;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding == null || !holding.IsAvailable) continue;
                if (holding.IsReservedForContract || holding.IsListedForSale) continue;

                var existing = PlanFor(holding.Id);
                if (existing != null && existing.HasRestorationLeg) continue;
                if (!contract.Requirement.MatchesAttributes(holding.Type, holding.Country, holding.Century)) continue;
                if (contract.Requirement.IsSatisfiedBy(holding)) continue;

                var quote = BestContractQuote(contract, holding, week, profile, owned: true);
                if (quote == null) continue;

                float cost = quote.Cost + holding.CurrentPrice;
                if (cost >= chosenCost) continue;

                chosen = holding;
                chosenQuote = quote;
                chosenCost = cost;
            }

            if (chosen == null) return false;

            var plan = PlanForOrCreate(chosen, week);
            plan.Purpose = PlanPurpose.Contract;
            plan.ContractId = contract.ContractId;
            AttachRestorationLeg(chosen.Id, chosenQuote.Option.Id, chosenQuote.Cost,
                LatestUsefulArrivalWeek(contract) - chosenQuote.DurationWeeks);

            Debug.Log($"{TraderName} will restore {chosen.Name} at {chosenQuote.Option.DisplayName} " +
                      $"for contract {contract.ContractId} ({Core.Money.Format(chosenQuote.Cost)}).");
            return true;
        }

        private void CollectActiveContracts(int week, NpcBehaviorProfile profile)
        {
            _contractScratch.Clear();
            if (_contractManager == null) return;

            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;
                _contractScratch.Add(contract);
            }

            // Tightest deadline first.
            _contractScratch.Sort((a, b) => a.DeadlineWeek.CompareTo(b.DeadlineWeek));
        }

        private bool TierTakeContract(int week, NpcBehaviorProfile profile)
        {
            if (!profile.TakesContracts || _contractManager == null) return false;
            return ConsiderNewContract(week, profile);
        }

        // ---------------------------------------------------------------- contracts

        /// <summary>
        /// Latest day an item may arrive and still be handed in.
        /// </summary>
        private static int LatestUsefulArrivalWeek(Contract contract) => contract.DeadlineWeek - 1;

        /// <summary>Items still needed: quantity minus what is reserved and still qualifies.</summary>
        private int MissingFor(Contract contract) =>
            Mathf.Max(0, contract.Requirement.Quantity - ReservedCountFor(contract) - RestoringForCount(contract));

        /// <summary>Reserved for this contract and still matching In transit included.</summary>
        private int ReservedCountFor(Contract contract)
        {
            int count = 0;
            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.ReservedForContractId != contract.ContractId) continue;
                if (!contract.Requirement.IsSatisfiedBy(holding)) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Held antiques promised to this contract through a restoration plan. They cannot be reserved
        /// until they qualify, so they are counted here to stop the NPC buying a replacement as well.
        /// </summary>
        private int RestoringForCount(Contract contract)
        {
            int count = 0;
            foreach (var pair in _plans)
            {
                var plan = pair.Value;
                if (plan.Purpose != PlanPurpose.Contract || plan.ContractId != contract.ContractId) continue;
                if (!plan.HasRestorationLeg) continue;

                var holding = Inventory.GetHolding(pair.Key);
                if (holding == null || holding.IsReservedForContract) continue;
                count++;
            }
            return count;
        }

        /// <summary>Reserved, arrived and still matching. Only items that can be currently handed in.</summary>
        private int ReadyCountFor(Contract contract)
        {
            int count = 0;
            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.ReservedForContractId != contract.ContractId) continue;
                if (!holding.IsAvailable) continue;
                if (!contract.Requirement.IsSatisfiedBy(holding)) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Slack is the deadline minus today minus the worst transit still ahead of this contract —
        /// both the shipments already flying and the trips the missing items have yet to make.
        /// </summary>
        private ContractUrgency UrgencyOf(Contract contract, int week, NpcBehaviorProfile profile)
        {
            if (MissingFor(contract) <= 0) return ContractUrgency.Normal;

            int slack = LatestUsefulArrivalWeek(contract) - week - WorstTransitAhead(contract);

            if (slack <= 0) return ContractUrgency.Critical;
            if (slack <= profile.ContractPanicWeeks) return ContractUrgency.Pressing;
            return ContractUrgency.Normal;
        }

        private int WorstTransitAhead(Contract contract)
        {
            int worst = 1;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.ReservedForContractId != contract.ContractId) continue;
                if (!holding.IsInTransit) continue;
                worst = Mathf.Max(worst, holding.ArrivalWeek - CurrentWeek);
            }

            foreach (var pair in _plans)
            {
                var plan = pair.Value;
                if (plan.Purpose != PlanPurpose.Contract || plan.ContractId != contract.ContractId) continue;
                if (!plan.HasRestorationLeg) continue;

                var holding = Inventory.GetHolding(pair.Key);
                if (holding == null) continue;

                int ready = holding.IsInRestoration
                    ? holding.RestorationEndWeek - CurrentWeek
                    : plan.StartRestoreByWeek - CurrentWeek;
                worst = Mathf.Max(worst, ready);
            }

            if (MissingFor(contract) > 0 && _transportManager != null && _transportManager.Settings != null)
                worst = Mathf.Max(worst, _transportManager.Settings.GetBaseDurationWeeks(ShippingZone.Domestic, NpcTransportOption));

            return Mathf.Max(1, worst);
        }

        /// <summary>
        /// Cheapest matching listing that can still land in time, re-quoted now. The transport option
        /// escalates with urgency and with how little condition margin the item has to lose.
        /// </summary>
        private Offer BestOfferFor(Contract contract, int week, NpcBehaviorProfile profile, ContractUrgency urgency)
        {
            var market = _economyManager.Market;
            if (market == null) return default;

            int latestArrival = LatestUsefulArrivalWeek(contract);
            var listings = market.Listings;

            Offer best = default;
            float bestCost = float.MaxValue;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (Inventory.Owns(listing.Id)) continue;
                if (!IsAcceptableContractPrice(listing, profile)) continue;

                var option = TransportOptionFor(listing, contract, urgency);
                var quote = QuoteFor(listing, option);
                int arrival = quote != null ? quote.ArrivalWeek : week;
                int duration = quote != null ? quote.DurationWeeks : 0;
                float shipped = listing.SalePrice + (quote != null ? quote.Cost : 0f);

                if (contract.Requirement.IsSatisfiedBy(listing))
                {
                    if (arrival > latestArrival) continue;
                    if (shipped > contract.RewardPerUnit || shipped >= bestCost) continue;

                    best = new Offer(listing, option, shipped, arrival, duration);
                    bestCost = shipped;
                    continue;
                }

                if (!profile.Restores || !profile.RestoresForContracts || !profile.BuysToRestore) continue;

                var restoration = BestContractQuote(contract, listing, arrival, profile, owned: false);
                if (restoration == null) continue;

                int ready = arrival + restoration.DurationWeeks;
                if (ready > latestArrival) continue;

                float total = shipped + restoration.Cost;
                if (total > contract.RewardPerUnit || total >= bestCost) continue;

                best = new Offer(listing, option, total, ready, duration + restoration.DurationWeeks,
                    restoration.Option.Id, restoration.Cost, restoration.DurationWeeks);
                bestCost = total;
            }

            return best;
        }

        private TransportOption TransportOptionFor(Antique listing, Contract contract, ContractUrgency urgency)
        {
            if (contract == null) return NpcTransportOption;
            if (urgency != ContractUrgency.Normal) return TransportOption.Express;

            var requirement = contract.Requirement;
            if (!requirement.HasMinCondition && !requirement.HasMinValue) return NpcTransportOption;

            float maxLoss = _transportManager != null && _transportManager.Settings != null
                ? _transportManager.Settings.MaxConditionLoss
                : 0f;

            // Damage lowers condition, and the price falls with it (price is linear in condition), so
            // an item close to either floor may arrive no longer qualifying.
            bool conditionAtRisk = requirement.HasMinCondition && listing.Condition - requirement.MinCondition < maxLoss;
            bool valueAtRisk = requirement.HasMinValue && listing.Condition > 0f &&
                               listing.CurrentPrice * (listing.Condition - maxLoss) / listing.Condition < requirement.MinAcceptedValue;

            return conditionAtRisk || valueAtRisk ? TransportOption.Express : NpcTransportOption;
        }

        private TransportQuote QuoteFor(Antique listing, TransportOption option) =>
            _transportManager != null ? _transportManager.Quote(listing, option, buyer: Inventory) : null;

        /// <summary>
        /// Picks the carriage for a discretionary purchase, through the shared chooser so the
        /// telemetry mirrors cost the same purchase the same way.
        /// </summary>
        private TransportChoice ChooseTransportFor(Antique listing, float worth, int holdWeeks) =>
            TraderValuation.BestTransport(listing, worth, holdWeeks, _transportManager, Inventory,
                MarginalUpkeepPerItem(), NpcTransportOption);


        private bool IsStillReachable(Contract contract, int missing, int week, NpcBehaviorProfile profile)
        {
            int perPeriod = Mathf.Max(1, ContractSlotsAllowed(profile));
            int latestArrival = LatestUsefulArrivalWeek(contract);

            BuildOfferPlan(contract, week, profile, missing);
            if (_offerScratch.Count < missing) return false;

            for (int k = 0; k < missing; k++)
            {
                int buyWeek = week + k / perPeriod;
                if (buyWeek + _offerScratch[k].DurationWeeks > latestArrival) return false;
            }

            return true;
        }

        private void BuildOfferPlan(Contract contract, int week, NpcBehaviorProfile profile, int missing)
        {
            _offerScratch.Clear();

            var market = _economyManager.Market;
            if (market == null || missing <= 0) return;

            int latestArrival = LatestUsefulArrivalWeek(contract);
            var listings = market.Listings;
            var urgency = ContractUrgency.Normal;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (Inventory.Owns(listing.Id)) continue;
                if (!contract.Requirement.IsSatisfiedBy(listing)) continue;
                if (!IsAcceptableContractPrice(listing, profile)) continue;

                var option = TransportOptionFor(listing, contract, urgency);
                var quote = QuoteFor(listing, option);
                int arrival = quote != null ? quote.ArrivalWeek : week;
                if (arrival > latestArrival) continue;

                float cost = listing.SalePrice + (quote != null ? quote.Cost : 0f);
                if (cost > contract.RewardPerUnit) continue;

                _offerScratch.Add(new Offer(listing, option, cost, arrival, quote != null ? quote.DurationWeeks : 0));
            }

            _offerScratch.Sort((a, b) => a.Cost.CompareTo(b.Cost));
            if (_offerScratch.Count > missing) _offerScratch.RemoveRange(missing, _offerScratch.Count - missing);

            _offerScratch.Sort((a, b) => b.DurationWeeks.CompareTo(a.DurationWeeks));
        }

        /// <summary>
        /// Takes the most profitable contract the NPC can actually finish.
        /// </summary>
        private bool ConsiderNewContract(int week, NpcBehaviorProfile profile)
        {
            int reputation = Record.Reputation.Reputation;

            var candidates = _contractManager.OpenContracts
                .Where(c => c != null && !Inventory.IsCommittedToContract(c.ContractId))
                .Concat(_contractManager.ExclusiveContracts.Where(c => c != null && c.CanBeClaimed))
                .Where(c => c.RequiredReputation <= reputation && c.Status == ContractStatus.Active)
                .ToList();

            if (candidates.Count == 0) return false;

            float cashLimit = Inventory.Cash * ContractCashShareFor(profile) - _contractReserve;
            if (cashLimit <= 0f) return false;

            Contract chosen = null;
            float bestProfit = 0f;

            foreach (var contract in candidates)
            {
                int missing = contract.Requirement.Quantity - ReservedCountFor(contract);
                if (missing <= 0) continue;

                BuildOfferPlan(contract, week, profile, missing);
                if (_offerScratch.Count < missing) continue;

                float purchaseCost = 0f;
                for (int k = 0; k < _offerScratch.Count; k++) purchaseCost += _offerScratch[k].Cost;
                if (purchaseCost > cashLimit) continue;

                if (!IsStillReachable(contract, missing, week, profile)) continue;

                float minProfit = contract.Type == ContractType.Exclusive
                    ? contract.TotalReward * ExclusiveMinProfitShare
                    : 0f;

                float profit = contract.TotalReward - purchaseCost;
                if (profit <= minProfit) continue;

                if (chosen == null || profit > bestProfit)
                {
                    chosen = contract;
                    bestProfit = profit;
                }
            }

            if (chosen == null) return false;

            if (chosen.Type == ContractType.Exclusive && !_contractManager.ClaimContract(chosen.ContractId, Id))
                return false;

            Inventory.CommitToContract(chosen.ContractId);
            Record.ContractAccepted();

            // Exclusive contracts are recorded by ClaimContract above; open ones are never claimed.
            if (chosen.Type != ContractType.Exclusive)
                _contractManager.RecordTakenOn(chosen, Id);

            _contractReserve = CalculateContractReserve();
            _weekBudget = Mathf.Min(_weekBudget, Mathf.Max(0f, Inventory.Cash - _contractReserve));

            Debug.Log($"{TraderName} picked up {chosen.Type} contract {chosen.ContractId} — needs {chosen.Requirement.Quantity}, due week {chosen.DeadlineWeek}, expected profit {bestProfit:F0}.");
            return true;
        }

        // ---------------------------------------------------------------- deliveries 

        private void HandleShipmentDelivered(Shipment shipment, Antique antique, float conditionLost)
        {
            if (shipment == null || antique == null) return;
            if (!ReferenceEquals(shipment.Recipient, Inventory)) return;
            if (conditionLost <= 0f) return;

            string contractId = antique.ReservedForContractId;
            if (string.IsNullOrEmpty(contractId) || _contractManager == null) return;

            var contract = _contractManager.GetById(contractId);
            if (contract == null || contract.Requirement.IsSatisfiedBy(antique)) return;

            Inventory.ReleaseReservation(antique.Id);

            if (PlanRescueRestoration(contract, antique))
            {
                Debug.Log($"{TraderName}: {antique.Name} arrived damaged (-{conditionLost:F2}) and goes to a workshop for contract {contractId}.");
                return;
            }


            Debug.Log($"{TraderName}: {antique.Name} arrived damaged (-{conditionLost:F2}) and no longer meets contract {contractId} — reservation released, looking for a replacement.");
        }
        private bool PlanRescueRestoration(Contract contract, Antique antique)
        {
            var profile = Profile;
            if (profile == null) return false;

            var quote = BestContractQuote(contract, antique, CurrentWeek, profile, owned: true);
            if (quote == null) return false;

            var plan = PlanForOrCreate(antique, CurrentWeek);
            plan.Purpose = PlanPurpose.Contract;
            plan.ContractId = contract.ContractId;
            AttachRestorationLeg(antique.Id, quote.Option.Id, quote.Cost,
                LatestUsefulArrivalWeek(contract) - quote.DurationWeeks);
            return true;
        }
    }
}
