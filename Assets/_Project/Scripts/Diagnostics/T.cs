namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>Table names and headers. CsvTable takes the column count from the header.</summary>
    public static class T
    {
        public const string DimRunName = "dim_run";
        public const string DimRunHeader =
            "run_id,started_utc,schema_version,unity_version,app_version,random_seed,campaign_length_weeks," +
            "start_date,ticks_per_day,ticks_per_period,seconds_per_week,default_initial_supply," +
            "default_initial_demand,supply_reversion_rate,demand_reversion_rate,initial_listing_count," +
            "new_listings_per_week,anonymous_listing_lifetime_weeks,market_listing_fee_rate," +
            "local_listing_weight,domestic_listing_weight,international_listing_weight," +
            "contracts_generated_per_week,max_active_contracts,initial_contract_count,contract_min_quantity," +
            "contract_max_quantity,contract_min_duration_weeks,contract_max_duration_weeks," +
            "shortest_feasible_duration,paired_requirement_chance,min_types_for_pair," +
            "supply_share_for_contract,max_generation_attempts,demand_per_requested_piece," +
            "max_contract_demand_per_type,contract_min_reward_multiplier,contract_max_reward_multiplier," +
            "max_urgency_bonus_multiplier,exclusive_contract_chance,exclusive_penalty_fraction," +
            "standard_contract_weight,locked_class_preview_weight,reputation_class_names,npc_count," +
            "npc_default_starting_cash,collector_demand_enabled,antique_definition_count";

        public const string DimTraderName = "dim_trader";
        public const string DimTraderHeader =
            "run_id,trader_id,trader_name,kind,profile_id,profile_name,starting_cash,access_level," +
            "actions_per_period,weekly_budget_fraction,min_bargain_score," +
            "min_condition_to_buy,condition_premium,profit_target_multiplier,patient_profit_multiplier," +
            "min_holding_weeks_before_sell,patient_sell_after_weeks,relist_after_weeks,listing_markup," +
            "listing_price_cut_per_week,min_listing_multiplier," +
            "stop_loss_multiplier,risk_tolerance,never_sells,lists_on_market,takes_contracts," +
            "contract_slot_share,contract_panic_weeks,restores,buys_to_restore,restores_for_contracts," +
            "max_concurrent_restorations,max_restoration_weeks,max_restoration_share_of_cash," +
            "restoration_hurdle,rumour_plant_chance,lists_into_own_rumour,exit_pump_confidence," +
            "exit_target_ratio,exit_dump_lead_weeks,seasonal_foresight,seasonal_horizon_weeks," +
            "auction_budget_share,auction_value_multiplier,auction_fever_chance,auction_fever_raise," +
            "auction_hesitation,auction_news_premium,leak_trust,rumor_trust,min_reaction_delay_weeks," +
            "max_reaction_delay_weeks,preferred_types,preferred_countries,preferred_centuries," +
            "trend_sensitivity,trend_window_weeks,max_trend_boost," +
            "outlook_weight,speculation_budget_share,bargain_budget_share,restoration_budget_share," +
            "full_conviction_at,stance,panic_sell_after_weeks,exit_lead_weeks," +
            "starting_warehouse_level,weekly_endowment";

        public const string DimAntiqueDefinitionName = "dim_antique_definition";
        public const string DimAntiqueDefinitionHeader =
            "run_id,definition_id,display_name,antique_type,century,country,base_price,edition_size," +
            "initial_supply,initial_demand";

        public const string DimAntiqueName = "dim_antique";
        public const string DimAntiqueHeader =
            "run_id,antique_id,definition_id,first_seen_seq,first_seen_week,first_seen_tick,origin," +
            "initial_condition,price_factor,edition_number,initial_shipping_zone,initial_current_price," +
            "initial_market_listed_week";

        public const string DimContractName = "dim_contract";
        public const string DimContractHeader =
            "run_id,contract_id,created_seq,created_week,contract_type,class_name,duration_weeks," +
            "deadline_week,quantity,reward_per_unit,total_reward,premium,penalty,required_reputation," +
            "attributes_used,req_antique_type,req_country,req_century,min_condition,min_value," +
            "matching_definition_count,matching_definition_ids,expected_supply_at_creation," +
            "supportable_quantity_at_creation,qualifying_listings_at_creation,cheapest_qualifying_at_creation," +
            "created_by";

        public const string DimRestorationOptionName = "dim_restoration_option";
        public const string DimRestorationOptionHeader =
            "run_id,option_id,display_name,target_condition,max_condition,duration_weeks,cost_rate," +
            "minimum_fee,success_chance,partial_chance,failure_chance,failure_loss_min,failure_loss_max," +
            "required_reputation";

        public const string FactLedgerName = "fact_ledger";
        public const string FactLedgerHeader =
            "run_id,seq,week,tick,day_index,game_date,trader_id,category,amount,is_income,description," +
            "cash_after";

        public const string FactOwnershipName = "fact_ownership";
        public const string FactOwnershipHeader =
            "run_id,seq,week,tick,day_index,game_date,ticks_elapsed,trader_id,antique_id,definition_id,event," +
            "path,counterparty_trader_id,price,asking_price,cost_basis,transport_cost,restoration_cost," +
            "condition,current_price,status,shipping_zone,purpose,plan_contract_id,plan_restoration_option_id," +
            "acquired_week,held_weeks,realized_pnl,source";

        public const string FactMarketTransactionName = "fact_market_transaction";
        public const string FactMarketTransactionHeader =
            "run_id,seq,week,tick,day_index,game_date,kind,antique_id,definition_id,buyer_trader_id," +
            "seller_trader_id,price,asking_price,proceeds,listing_fee,market_value,condition,shipping_zone," +
            "listed_on_week,listing_age_weeks,listings_of_type_before,listings_of_type_after," +
            "disposal_reason,gross_price,spread_paid";

        public const string FactTypeStateName = "fact_type_state";
        public const string FactTypeStateHeader =
            "run_id,seq,week,tick,day_index,game_date,ticks_elapsed,trigger,definition_id,supply,demand," +
            "temp_supply_mod,temp_demand_mod,seasonal_demand_mod,contract_demand_mod,effective_demand," +
            "baseline_supply,baseline_demand,supply_demand_multiplier,reference_price,last_auction_price," +
            "last_auction_week,listing_count,listing_price_min,listing_price_avg,listing_price_max,held_count," +
            "delta_supply,delta_effective_demand,delta_reference_price,dependency_demand_mod," +
            "rumour_demand_mod,multiplier_clamped,long_term_demand_mod,demand_anchor,trend_reference_price," +
            "price_trend_4w";

        public const string FactPressureName = "fact_pressure";
        public const string FactPressureHeader =
            "run_id,seq,week,tick,day_index,game_date,definition_id,demand_delta,supply_delta,attributed_seq," +
            "attributed_cause,coverage_note";

        public const string FactContractStateName = "fact_contract_state";
        public const string FactContractStateHeader =
            "run_id,seq,week,tick,day_index,game_date,contract_id,trader_id,event,source,contract_status," +
            "quantity_required,reserved_matching,ready_count,missing_count,weeks_to_deadline,reward_paid," +
            "penalty_paid,trader_cash,other_committed_count,is_terminal";

        public const string FactContractProgressName = "fact_contract_progress";
        public const string FactContractProgressHeader =
            "run_id,seq,week,game_date,contract_id,trader_id,quantity_required,reserved_matching," +
            "restoring_for,in_transit_for,ready_count,missing_count,weeks_to_deadline," +
            "latest_useful_arrival_week,urgency,qualifying_listings_on_market,reachable_qualifying_listings," +
            "cheapest_qualifying_price,cheapest_qualifying_total_cost,is_profitable_to_buy,expected_unit_cost," +
            "reward_per_unit,trader_cash,contract_reserve,plan_reserve,week_budget,can_afford_cheapest," +
            "expected_supply_remaining";

        public const string FactReservationName = "fact_reservation";
        public const string FactReservationHeader =
            "run_id,seq,week,tick,day_index,game_date,ticks_elapsed,trader_id,antique_id,definition_id," +
            "contract_id,event,satisfies_requirement,release_reason,condition,current_price," +
            "min_condition_required,min_value_required,condition_margin,value_margin";

        public const string FactPlanName = "fact_plan";
        public const string FactPlanHeader =
            "run_id,seq,week,tick,day_index,game_date,ticks_elapsed,trader_id,antique_id,definition_id,event," +
            "purpose,previous_purpose,contract_id,previous_contract_id,restoration_option_id,assumed_fee," +
            "start_restore_by_week,exit_asking_price,dump_after_week,cost_basis,acquired_week";

        public const string FactNpcActionName = "fact_npc_action";
        public const string FactNpcActionHeader =
            "run_id,seq,week,tick,day_index,game_date,trader_id,slot_index,intent,intent_rank,acted," +
            "result_antique_id,result_contract_id,cash_before,cash_after,week_budget_before,week_budget_after," +
            "contract_reserve,plan_reserve,contract_slots_used,holdings_count,open_restorations," +
            "committed_contracts,beliefs_held,activity_scale,target_id,conviction,budget_ceiling,rationale";

        public const string FactRestorationName = "fact_restoration";
        public const string FactRestorationHeader =
            "run_id,seq,week,tick,day_index,game_date,job_id,event,trader_id,antique_id,definition_id," +
            "option_id,option_name,start_week,end_week,duration_weeks,cost,start_condition,target_condition," +
            "partial_condition,worst_condition,result_condition,outcome,value_before,value_after,value_change," +
            "net_gain,plan_purpose,plan_contract_id,cost_basis_after,trader_reputation";

        public const string FactShipmentName = "fact_shipment";
        public const string FactShipmentHeader =
            "run_id,seq,week,tick,day_index,game_date,shipment_id,event,trader_id,antique_id,definition_id," +
            "transport_option,shipping_zone,cost,dispatch_week,arrival_week,seasonal_shift_weeks," +
            "weeks_in_transit,condition_before,condition_lost,condition_after,reserved_for_contract_id," +
            "still_satisfies_contract,arrived_in_time";

        public const string FactRumourName = "fact_rumour";
        public const string FactRumourHeader =
            "run_id,seq,week,tick,day_index,game_date,news_id,event,planter_trader_id,planter_name,title," +
            "scope,scope_keys,bullish,plant_week,spread_week,claim_week,spend,cost_scale,credibility,traced," +
            "skepticism_after,planter_cash_after,exit_holdings_in_scope,exit_holdings_value";

        public const string FactAuctionName = "fact_auction";
        public const string FactAuctionHeader =
            "run_id,seq,week,tick,day_index,game_date,auction_id,auction_number,event,auction_week,start_tick," +
            "announced_on_week,lot_number,antique_id,definition_id,estimate_low,estimate_high,start_price," +
            "has_provenance,lot_status,winner_trader_id,hammer_price,market_value_at_sale,bid_count," +
            "buyers_premium,player_joined";

        public const string FactAuctionBidName = "fact_auction_bid";
        public const string FactAuctionBidHeader =
            "run_id,seq,week,tick,day_index,game_date,auction_id,lot_number,antique_id,bidder_trader_id," +
            "bidder_name,bidder_kind,event,amount,bidder_limit,bidder_cap,market_value,bid_over_market";

        public const string FactNewsName = "fact_news";
        public const string FactNewsHeader =
            "run_id,seq,week,tick,day_index,game_date,news_id,event,news_type,official_name,credibility," +
            "week_published,event_trigger_week,guaranteed_delivery,planted_by_trader_id,debunks_news_id," +
            "debunked,scope_count,scope_targets,affects_price_up,player_delivery_week,market_skepticism";

        public const string FactWorldEventName = "fact_world_event";
        public const string FactWorldEventHeader =
            "run_id,seq,week,tick,day_index,game_date,active_event_id,event_definition_id,event_name," +
            "event_type,lifecycle,trigger_week,end_week,duration_weeks,effect_kinds,scope_targets," +
            "perm_demand_change,temp_demand_change,perm_supply_change,temp_supply_change," +
            "affected_definition_ids";

        public const string FactTraderWeekName = "fact_trader_week";
        public const string FactTraderWeekHeader =
            "run_id,seq,week,game_date,season,trader_id,cash,holdings_count,holdings_value," +
            "holdings_cost_basis,unrealized_pnl,wealth,market_share,reputation,reputation_tier," +
            "contracts_accepted_total,contracts_fulfilled_total,contracts_failed_total,committed_contracts," +
            "listings_on_market,listed_value,open_restorations,in_transit_count,reserved_count,warehouse_used," +
            "warehouse_capacity,is_over_capacity,income_this_week,expenses_this_week,trade_volume_this_week," +
            "week_budget,contract_reserve,plan_reserve,action_slots_planned,activity_scale";

        public const string FactIntegrityName = "fact_integrity";
        public const string FactIntegrityHeader =
            "run_id,seq,week,tick,check_name,scope_kind,scope_id,passed,expected,actual,difference,detail";

        public const string FactRecorderErrorName = "fact_recorder_error";
        public const string FactRecorderErrorHeader =
            "run_id,seq,week,tick,target_table,hook,exception_type,message,occurrence_count,rows_lost";

        public const string FactBrowseFunnelName = "fact_browse_funnel";
        public const string FactBrowseFunnelHeader =
            "run_id,seq,week,tick,day_index,game_date,trader_id,listings_scanned,rejected_owns_it," +
            "rejected_not_interested,rejected_not_quoted,rejected_condition_floor," +
            "rejected_over_week_budget,rejected_cash_floor,rejected_below_bargain_bar," +
            "rejected_no_viable_restoration,candidates_as_is,candidates_restore,best_as_is_antique_id," +
            "best_as_is_weekly_return,best_as_is_total_cost,best_restore_antique_id," +
            "best_restore_weekly_return,best_restore_total_cost,bargain_threshold,restoration_bar,hold_weeks," +
            "week_budget,cash,cash_floor_headroom,has_active_commitments,chosen_path,chosen_antique_id," +
            "mirror_agrees_with_actual";

        public const string FactContractEligibilityWeekName = "fact_contract_eligibility_week";
        public const string FactContractEligibilityWeekHeader =
            "run_id,seq,week,game_date,contract_id,trader_id,takes_contracts,is_committed,meets_reputation," +
            "trader_reputation,required_reputation,can_be_claimed,cash_limit,offers_needed,offers_found," +
            "planned_purchase_cost,affordable,is_still_reachable,expected_profit,min_profit_required," +
            "is_attractive,blocked_at,qualifying_listings_on_market,reachable_qualifying_listings," +
            "cheapest_qualifying_total_cost,reward_per_unit,competitor_committed_count,weeks_to_deadline," +
            "rejected_on_price";

        public const string FactNewsReceptionName = "fact_news_reception";
        public const string FactNewsReceptionHeader =
            "run_id,seq,week,tick,day_index,game_date,news_id,trader_id,delivery_week,outcome,news_type," +
            "access_level,credibility,base_trust,market_skepticism,effective_trust,risk_tolerance," +
            "action_chance,trust_roll,expert_detection_chance,reaction_delay_weeks,acts_from_week," +
            "event_trigger_week,acted_on_week,scope_targets,holdings_in_scope,holdings_in_scope_value," +
            "estimated_move,estimate_spread,outlook_before,outlook_after";

        public const string FactEconomyWeekName = "fact_economy_week";
        public const string FactEconomyWeekHeader =
            "run_id,seq,week,game_date,season,total_trader_cash,total_holdings_value,total_wealth,wealth_gini," +
            "wealth_top_share,cash_created,cash_destroyed,net_cash_flow,cash_from_contracts," +
            "cash_from_collectors,cash_from_market_sales,cash_from_auction_sales,cash_to_anonymous_sellers," +
            "cash_to_transport,cash_to_commission,cash_to_warehouse_and_upkeep,cash_to_restoration," +
            "cash_to_rumours,cash_to_penalties,cash_to_upgrades,internal_transfer_volume,price_index," +
            "price_index_change,types_clamped_high,types_clamped_low,types_at_min_supply,mean_supply," +
            "mean_effective_demand,listings_total,listings_added,listings_sold,listings_expired," +
            "listing_expiry_rate,median_listing_price,total_listed_value,market_turnover,market_sales_count," +
            "contracts_on_board,contracts_created,contracts_fulfilled,contracts_expired," +
            "contracts_never_picked_up,active_rumours,market_skepticism,active_world_events,open_restorations," +
            "shipments_in_transit,season_npc_activity,season_collector_chance,season_new_listings," +
            "season_demand_by_type";

        public const string FactListingWeekName = "fact_listing_week";
        public const string FactListingWeekHeader =
            "run_id,seq,week,game_date,antique_id,definition_id,owner_trader_id,is_owner_listing,asking_price," +
            "market_value,sale_price,ask_over_market,condition,price_factor,shipping_zone,listed_on_week," +
            "age_weeks,weeks_until_expiry,collector_chance,qualifying_for_contract_count," +
            "qualifying_contract_ids,interested_trader_count,affordable_trader_count,reference_price," +
            "price_rank_in_type";

        public const string FactReputationChangeName = "fact_reputation_change";
        public const string FactReputationChangeHeader =
            "run_id,seq,week,tick,day_index,game_date,trader_id,kind,delta,reason,value_before,value_after," +
            "tier_index_before,tier_index_after,tier_name_after,crossed_tier,progress_to_next_tier," +
            "unlocked_contract_classes,unlocked_restoration_options,source_event_seq";

        public const string FactDependencyInfluenceName = "fact_dependency_influence";
        public const string FactDependencyInfluenceHeader =
            "run_id,seq,week,game_date,event,facet_kind,facet_value,facet_name,origin_facet_kind," +
            "origin_facet_value,origin_facet_name,dependency_kind,amount,previous_amount,decay_per_week,depth," +
            "created_on_week,last_refreshed_on_week,affected_definition_count,total_applied_demand," +
            "trigger_seq";

        public const string FactSessionName = "fact_session";
        public const string FactSessionHeader =
            "run_id,seq,week,tick,game_date,event,reason,session_index,timestamp_utc,campaign_length," +
            "campaign_finished,rows_written,real_seconds";

        // One row per agenda item the weekly review built, with the reasoning behind it and
        // whether a slot carried it out.
        public const string FactAgendaItemName = "fact_agenda_item";
        public const string FactAgendaItemHeader =
            "run_id,seq,week,game_date,trader_id,rank,intent,intent_rank,target_id,target_kind," +
            "conviction,budget_ceiling,valid_until_week,rationale,executed,executed_tick," +
            "skipped_reason,disposal_reason";

        public const string FactOutlookWeekName = "fact_outlook_week";
        public const string FactOutlookWeekHeader =
            "run_id,seq,week,game_date,trader_id,definition_id,antique_type,expected,confidence," +
            "conviction,supporting,opposing,peak_week,fade_week,beliefs_held";

        public const string FactPositionName = "fact_position";
        public const string FactPositionHeader =
            "run_id,seq,week,game_date,trader_id,antique_id,definition_id,purpose,cost_basis," +
            "acquired_week,entry_outlook,target_price,stop_price,expected_peak_week,expected_fade_week," +
            "based_on_news_ids,source_kinds,distress_since,current_value,unrealised_gain,is_listed," +
            "asking_price";

        public const string FactBeliefOutcomeName = "fact_belief_outcome";
        public const string FactBeliefOutcomeHeader =
            "run_id,seq,week,game_date,trader_id,news_id,official_name,source,direction," +
            "estimated_move,estimate_spread,realised_move,was_confirmed,start_week,end_week," +
            "heard_week,observations_before,trust_at_outcome";

        public const int TableCount = 38;
    }
}
