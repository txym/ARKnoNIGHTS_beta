@{
    DamageTypeOverrides = @{
        '1238'  = 'Physical'
        '1243'  = 'Physical'
        '10039' = 'Physical'
    }

    UnitScenarios = @{
        '1008' = @{
            TypeId = 1008
            ModelKind = 'PathPressure'
            Parameters = @{ WindowSeconds = 20 }
            Evidence = @('BONDS: no ordinary attack, moves directly toward the opposing home, unblockable. Formula: normalized LifeDeduct * normalized move speed * 20-second survival fraction.')
            UnquantifiedRisk = @('Route length and contact timing are not confirmed.')
        }
        '1017' = @{
            TypeId = 1017
            ModelKind = 'SupportAura20Seconds'
            Parameters = @{ WindowSeconds = 20; AuraDefenseBonus = 300; AuraTargetsLow = 1; AuraTargetsMain = 3; AuraTargetsHigh = 5 }
            Evidence = @('BONDS: other allies within radius 2.5 gain DEF +300. Design 5.2: aura sensitivity uses 1/3/5 effective targets; main uses 3.')
            UnquantifiedRisk = @('Actual route, recipients, uptime, stacking and silence timing are not confirmed.')
        }
        '1021' = @{
            TypeId = 1021
            ModelKind = 'DeathBurst20Seconds'
            Parameters = @{ WindowSeconds = 20; BurstAtSeconds = 10; BurstAttackMultiplier = 4.0; BurstTargetsLow = 1; BurstTargetsMain = 2; BurstTargetsHigh = 3 }
            Evidence = @('BONDS: death burst is 400% ATK physical splash, radius 1.25, delay 1 second. Scenario convention: death at the 10-second midpoint; AoE sensitivity 1/2/3.')
            UnquantifiedRisk = @('Actual death time, air-target mix and affected target count are not confirmed.')
        }
        '1025' = @{
            TypeId = 1025
            ModelKind = 'ThresholdOutput20Seconds'
            Parameters = @{ WindowSeconds = 20; OutputSegments = @(@{ DurationSeconds = 10; AttackMultiplier = 1.0 }, @{ DurationSeconds = 10; AttackMultiplier = 2.0 }) }
            Evidence = @('BONDS E0: ATK +100% at HP <=50%. Scenario convention: threshold is crossed at the 10-second midpoint.')
            UnquantifiedRisk = @('Actual threshold-crossing time is encounter dependent.')
        }
        '1026' = @{
            TypeId = 1026
            ModelKind = 'PathPressure'
            Parameters = @{ WindowSeconds = 20 }
            Evidence = @('BONDS: no ordinary attack, moves directly toward the opposing home, unblockable. Formula: normalized LifeDeduct * normalized move speed * 20-second survival fraction.')
            UnquantifiedRisk = @('Route length and contact timing are not confirmed.')
        }
        '1042' = @{
            TypeId = 1042
            ModelKind = 'SupportAura20Seconds'
            Parameters = @{ WindowSeconds = 20; AuraAttackSpeedBonus = -50; AuraTargetsLow = 1; AuraTargetsMain = 3; AuraTargetsHigh = 5 }
            Evidence = @('BONDS: enemies within radius 2.5 have ASPD -50. Baseline attack rate is 200 after the confirmed interval halving; prevented output per target is 1-(200-50)/200. Aura sensitivity is 1/3/5.')
            UnquantifiedRisk = @('Actual route, affected enemies, uptime, stacking and silence timing are not confirmed.')
        }
        '1043' = @{
            TypeId = 1043
            ModelKind = 'SelfRegen20Seconds'
            Parameters = @{ WindowSeconds = 20; RegenPerSecond = 160 }
            Evidence = @('BONDS: natural HP recovery 160/s. Formula adds 160*20 effective HP over the fixed window.')
            UnquantifiedRisk = @('Overheal and actual incoming-damage timing are not modeled.')
        }
        '1044' = @{
            TypeId = 1044
            ModelKind = 'SelfRegen20Seconds'
            Parameters = @{ WindowSeconds = 20; RegenPerSecond = 400 }
            Evidence = @('BONDS: natural HP recovery 400/s. Formula adds 400*20 effective HP over the fixed window.')
            UnquantifiedRisk = @('Overheal and actual incoming-damage timing are not modeled.')
        }
        '1061' = @{
            TypeId = 1061
            ModelKind = 'SelfRegen20Seconds'
            Parameters = @{ WindowSeconds = 20; RegenPerSecond = 400 }
            Evidence = @('BONDS: natural HP recovery 400/s. Formula adds 400*20 effective HP over the fixed window.')
            UnquantifiedRisk = @('Overheal and actual incoming-damage timing are not modeled.')
        }
        '1062' = @{
            TypeId = 1062
            ModelKind = 'SelfDamage20Seconds'
            Parameters = @{ WindowSeconds = 20; SelfDamagePerSecond = 330 }
            Evidence = @('BONDS: 330 source-less true self-damage per second. Output uptime and effective HP use min(20, HP/330).')
            UnquantifiedRisk = @('External healing and encounter duration beyond 20 seconds are not modeled.')
        }
        '1089' = @{
            TypeId = 1089
            ModelKind = 'SelfDamageDeathBurst20Seconds'
            Parameters = @{ WindowSeconds = 20; SelfDamagePerSecond = 800; BurstAttackMultiplier = 2.0; BurstTargetsLow = 1; BurstTargetsMain = 2; BurstTargetsHigh = 3; BurstDamageType = 'Magic' }
            Evidence = @('BONDS: 800 source-less true self-damage/s and 200% ATK magic death splash, radius 1.5, delay 1.3 seconds. Death time is min(20, HP/800); AoE sensitivity is 1/2/3.')
            UnquantifiedRisk = @('External damage/healing can change death time and whether the burst occurs in-window.')
        }
        '1116' = @{
            TypeId = 1116
            ModelKind = 'OpeningHitsThenSteady20Seconds'
            Parameters = @{ WindowSeconds = 20; OpeningAttackCount = 3; OpeningAttackSpeedBonus = -50; SteadyAttackMultiplier = 1.5 }
            Evidence = @('BONDS: initial ASPD -50; before attack 4, release and gain ATK +50%. Formula gives the first three attacks the opening state and the remainder the released state.')
            UnquantifiedRisk = @()
        }
        '1118' = @{
            TypeId = 1118
            ModelKind = 'OpeningHitsThenSteady20Seconds'
            Parameters = @{ WindowSeconds = 20; OpeningAttackCount = 3; OpeningAttackSpeedBonus = -50; SteadyAttackMultiplier = 1.5; SteadyDefenseIgnoreFraction = 0.60 }
            Evidence = @('BONDS: first three attacks at ASPD -50; released attacks gain ATK +50% and ignore 60% of target DEF.')
            UnquantifiedRisk = @()
        }
        '1119' = @{
            TypeId = 1119
            ModelKind = 'OpeningHitsThenSteady20Seconds'
            Parameters = @{ WindowSeconds = 20; OpeningAttackCount = 3; OpeningAttackSpeedBonus = -50; SteadyAttackMultiplier = 1.5; DefenseActiveStartSeconds = 10; MagicResistanceBonus = 40; RegenPerSecond = 300 }
            Evidence = @('BONDS: first three attacks at ASPD -50; released attacks gain ATK +50%, MR +40 and regen 300/s. Output release follows attack count; defense effects use the fixed 10-second midpoint convention.')
            UnquantifiedRisk = @('Actual release time changes defense-effect uptime.')
        }
        '1121' = @{
            TypeId = 1121
            ModelKind = 'OpeningHitsThenSteady20Seconds'
            Parameters = @{ WindowSeconds = 20; OpeningAttackCount = 3; OpeningAttackSpeedBonus = -50; SteadyAttackMultiplier = 1.5; DefenseBonus = 300; DefenseActiveStartSeconds = 0; DefenseActiveEndSeconds = 10 }
            Evidence = @('BONDS: initial ASPD -50 and DEF +300; before attack 4, release and gain ATK +50%. Output follows attack count; opening DEF uses the fixed first 10 seconds.')
            UnquantifiedRisk = @('Actual release time and the first-release ally effect are not fully determined.')
        }
        '1131' = @{
            TypeId = 1131
            ModelKind = 'DeathSummon20Seconds'
            Parameters = @{ WindowSeconds = 20; SummonAtSeconds = 10; SummonTypeId = 1137; SummonCount = 2 }
            Evidence = @('BONDS E0: 0.2 seconds after death, summon two 1137. Scenario convention: parent death at 10 seconds; contribution uses child PanelPower * remaining lifetime / 20.')
            UnquantifiedRisk = @('Actual death time and spawn placement are encounter dependent.')
        }
        '1132' = @{
            TypeId = 1132
            ModelKind = 'DeathSummon20Seconds'
            Parameters = @{ WindowSeconds = 20; SummonAtSeconds = 10; SummonTypeId = 1137; SummonCount = 3 }
            Evidence = @('BONDS E0: 0.2 seconds after death, summon three 1137. Scenario convention: parent death at 10 seconds; contribution uses child PanelPower * remaining lifetime / 20.')
            UnquantifiedRisk = @('Actual death time and spawn placement are encounter dependent.')
        }
        '1146' = @{
            TypeId = 1146
            ModelKind = 'SupportAura20Seconds'
            Parameters = @{ WindowSeconds = 20; AuraDefenseBonus = 200; AuraRegenPerSecond = 400; AuraTargetsLow = 1; AuraTargetsMain = 3; AuraTargetsHigh = 5 }
            Evidence = @('BONDS: after reaching the support point, other allies in attack range gain DEF +200 and regen 400/s; auto-death is at 30 seconds, outside this window. Aura sensitivity is 1/3/5.')
            UnquantifiedRisk = @('Travel time, attack-range geometry, recipients, stacking and silence timing are not confirmed.')
        }
        '1165' = @{
            TypeId = 1165
            ModelKind = 'SelfResistance20Seconds'
            Parameters = @{ WindowSeconds = 20; MagicResistanceBonus = 70 }
            Evidence = @('BONDS: self MR +70 for the full scenario.')
            UnquantifiedRisk = @()
        }
        '1166' = @{
            TypeId = 1166
            ModelKind = 'SelfResistance20Seconds'
            Parameters = @{ WindowSeconds = 20; MagicResistanceBonus = 70 }
            Evidence = @('BONDS: self MR +70 for the full scenario.')
            UnquantifiedRisk = @()
        }
        '1169' = @{
            TypeId = 1169
            ModelKind = 'SelfResistanceAndAura20Seconds'
            Parameters = @{ WindowSeconds = 20; MagicResistanceBonus = 70; AuraDefenseBonus = 200; AuraTargetsLow = 1; AuraTargetsMain = 3; AuraTargetsHigh = 5 }
            Evidence = @('BONDS: self MR +70; each other nearby Deep Pool Phalanx entity gains DEF +200, stackable. Aura sensitivity is 1/3/5.')
            UnquantifiedRisk = @('Actual same-family entity count and formation uptime are encounter dependent.')
        }
        '1170' = @{
            TypeId = 1170
            ModelKind = 'SelfResistance20Seconds'
            Parameters = @{ WindowSeconds = 20; MagicResistanceBonus = 70 }
            Evidence = @('BONDS: self MR +70 for the full scenario.')
            UnquantifiedRisk = @()
        }
        '1230' = @{
            TypeId = 1230
            ModelKind = 'SelfResistance20Seconds'
            Parameters = @{ WindowSeconds = 20; MagicResistanceBonus = 60 }
            Evidence = @('BONDS: self MR +60 for the full scenario.')
            UnquantifiedRisk = @()
        }
        '1231' = @{
            TypeId = 1231
            ModelKind = 'Evasion20Seconds'
            Parameters = @{ WindowSeconds = 20; PhysicalEvasionProbability = 0.80; MagicEvasionProbability = 0.80 }
            Evidence = @('BONDS: 80% physical and magic evasion. Expected incoming damage multipliers are 0.20.')
            UnquantifiedRisk = @('Variance and true-damage interactions are not modeled.')
        }
        '1232' = @{
            TypeId = 1232
            ModelKind = 'ThresholdDefense20Seconds'
            Parameters = @{ WindowSeconds = 20; DefenseMultiplier = 4.0; DefenseActiveStartSeconds = 10 }
            Evidence = @('BONDS: below half HP, DEF increases 300%, modeled as base DEF *4. Scenario convention: threshold at 10 seconds.')
            UnquantifiedRisk = @('Actual threshold time and block-count value are encounter dependent.')
        }
        '1238' = @{
            TypeId = 1238
            ModelKind = 'OrdinaryBaseline'
            Parameters = @{ WindowSeconds = 20 }
            Evidence = @('Approved design section 4: score only the confirmed ordinary melee physical panel.')
            UnquantifiedRisk = @()
        }
        '1243' = @{
            TypeId = 1243
            ModelKind = 'OrdinaryBaseline'
            Parameters = @{ WindowSeconds = 20 }
            Evidence = @('Approved design section 4: score only the confirmed ordinary melee physical panel.')
            UnquantifiedRisk = @()
        }
        '1262' = @{
            TypeId = 1262
            ModelKind = 'UnblockedDamageReduction20Seconds'
            Parameters = @{ WindowSeconds = 20; PhysicalDamageTakenMultiplier = 0.50; MagicDamageTakenMultiplier = 0.50 }
            Evidence = @('BONDS: while unblocked, physical and magic damage taken is reduced 50%. Main scenario is explicitly unblocked for 20 seconds.')
            UnquantifiedRisk = @('Actual blocked uptime is encounter dependent.')
        }
        '1264' = @{
            TypeId = 1264
            ModelKind = 'TimedAttackSpeed20Seconds'
            Parameters = @{ WindowSeconds = 20; OutputSegments = @(@{ DurationSeconds = 15; AttackSpeedBonus = 100 }, @{ DurationSeconds = 5 }) }
            Evidence = @('BONDS: after first damage, ASPD +100 for 15 seconds. Scenario convention: first damage occurs at time 0; remaining five seconds are baseline.')
            UnquantifiedRisk = @('Actual first-damage time and movement-speed path effect are encounter dependent.')
        }
        '1274' = @{
            TypeId = 1274
            ModelKind = 'ThresholdAttackSpeed20Seconds'
            Parameters = @{ WindowSeconds = 20; OutputSegments = @(@{ DurationSeconds = 10 }, @{ DurationSeconds = 10; AttackSpeedBonus = 100 }) }
            Evidence = @('BONDS: first time below 50% HP, ASPD +100. Scenario convention: threshold at 10 seconds.')
            UnquantifiedRisk = @('Actual threshold time and movement-speed path effect are encounter dependent.')
        }
        '1314' = @{
            TypeId = 1314
            ModelKind = 'FirstStrike20Seconds'
            Parameters = @{ WindowSeconds = 20; FirstAttackMultiplier = 2.0 }
            Evidence = @('BONDS: first attack deals 200% ATK physical damage. Formula replaces exactly one baseline hit in the 20-second uninterrupted-attack sequence.')
            UnquantifiedRisk = @('Target availability can change the number of attacks in-window.')
        }
        '1333' = @{
            TypeId = 1333
            ModelKind = 'PathPressure'
            Parameters = @{ WindowSeconds = 20 }
            Evidence = @('BONDS: no ordinary attack and unblockable. Formula: normalized LifeDeduct * normalized move speed * 20-second survival fraction.')
            UnquantifiedRisk = @('Route length and whether movement is interrupted by non-blocking interactions are not confirmed.')
        }
        '1355' = @{
            TypeId = 1355
            ModelKind = 'SupportAura20Seconds'
            Parameters = @{ WindowSeconds = 20; AuraMagicResistanceBonus = 30; AuraTargetsLow = 1; AuraTargetsMain = 3; AuraTargetsHigh = 5 }
            Evidence = @('BONDS: other allies within radius 2.5 gain MR +30. Design 5.2: aura sensitivity uses 1/3/5 effective targets; main uses 3.')
            UnquantifiedRisk = @('Actual route, recipients, uptime, stacking and silence timing are not confirmed.')
        }
        '1371' = @{
            TypeId = 1371
            ModelKind = 'PeriodicAttack20Seconds'
            Parameters = @{ WindowSeconds = 20; AttackCycleMultipliers = @(1.0, 1.0, 1.3) }
            Evidence = @('BONDS: after two attacks, the next deals 130% ATK physical damage. Formula repeats the three-hit cycle.')
            UnquantifiedRisk = @()
        }
        '1372' = @{
            TypeId = 1372
            ModelKind = 'PeriodicAoe20Seconds'
            Parameters = @{ WindowSeconds = 20; AttackCycleMultipliers = @(1.0, 1.0, 1.0); AttackCycleTargetCountsLow = @(1, 1, 1); AttackCycleTargetCountsMain = @(1, 1, 2); AttackCycleTargetCountsHigh = @(1, 1, 3) }
            Evidence = @('BONDS: after two attacks, the next deals 100% ATK physical damage in a cross. AoE sensitivity gives that third hit 1/2/3 targets.')
            UnquantifiedRisk = @('Actual cross occupancy and air-target exclusions are encounter dependent.')
        }
        '1375' = @{
            TypeId = 1375
            ModelKind = 'RampAttack20Seconds'
            Parameters = @{ WindowSeconds = 20; RampCheckIntervalSeconds = 0.25; RampAttackFlatPerStack = 30; RampMaxStacks = 30 }
            Evidence = @('BONDS: while unblocked, every 0.25 seconds adds final ATK +30, max 30 stacks, cleared after each normal attack. Formula applies floor(attack interval/0.25), capped at 30, to every uninterrupted attack.')
            UnquantifiedRisk = @('Actual blocked uptime and target availability are encounter dependent.')
        }
        '1437' = @{
            TypeId = 1437
            ModelKind = 'DeathRandomSummon20Seconds'
            Parameters = @{ WindowSeconds = 20; SummonAtSeconds = 10; RandomSummons = @(@{ TypeId = 1434; Probability = 0.40 }, @{ TypeId = 1433; Probability = 0.60 }) }
            Evidence = @('BONDS: on death summon 1434 with 40% probability or 1433 with 60%, and grant move speed +200%. Scenario convention: death at 10 seconds; expected child PanelPower is probability weighted.')
            UnquantifiedRisk = @('Actual death time and the summoned unit movement-speed path value are encounter dependent.')
        }
        '2031' = @{
            TypeId = 2031
            ModelKind = 'AttackCycleSummon20Seconds'
            Parameters = @{ WindowSeconds = 20; SummonTypeId = 2033; AttacksPerSummon = 3 }
            Evidence = @('BONDS: after two attacks, the next damaging attack summons one 2033. Spawn times use each third uninterrupted attack; contribution uses child PanelPower * remaining lifetime / 20.')
            UnquantifiedRisk = @('Summons from every ten incoming hits and their shared-count condition are not counted because incoming hit timing is not confirmed.')
        }
        '5503' = @{
            TypeId = 5503
            ModelKind = 'ScheduledSummon20Seconds'
            Parameters = @{ WindowSeconds = 20; SummonTypeId = 5504; SummonCountPerCast = 3; SkillPointsPerSecond = 2; InitialSkillPoints = 5; SkillPointCost = 15; SpawnTimesSeconds = @(5.0, 12.5, 20.0) }
            Evidence = @('BONDS: initial SP 5, cost 15, summon three 5504. SPEC: automatic skills recover 2 SP/s. Casts occur at 5, 12.5 and the 20-second endpoint; endpoint children have zero remaining-window contribution.')
            UnquantifiedRisk = @('Actual battle termination and spawn survival can reduce contribution.')
        }
        '7002' = @{
            TypeId = 7002
            ModelKind = 'Counter20Seconds'
            Parameters = @{ WindowSeconds = 20; CounterMagicDamagePerHit = 200; IncomingHitsPerSecond = 1 }
            Evidence = @('BONDS: each received hit deals 200 source-less magic damage to its source. Scenario convention: one incoming hit per second for 20 seconds; formal magic damage is applied to the defender sample.')
            UnquantifiedRisk = @('Actual incoming hit rate and attacker targetability are encounter dependent.')
        }
        '10004' = @{
            TypeId = 10004
            ModelKind = 'ThresholdHeal20Seconds'
            Parameters = @{ WindowSeconds = 20; HealFractionOfMaxHp = 0.50; HealAtSeconds = 10 }
            Evidence = @('BONDS: first time below 50% HP, heal to 100%. Scenario convention: trigger at 10 seconds after exactly half HP has been lost, adding 50% max HP.')
            UnquantifiedRisk = @('Actual trigger time, overkill and healing prevention are not confirmed.')
        }
        '10006' = @{
            TypeId = 10006
            ModelKind = 'ThresholdSummon20Seconds'
            Parameters = @{ WindowSeconds = 20; SummonAtSeconds = 10; SummonTypeId = 10002; SummonCount = 4 }
            Evidence = @('BONDS: first time below 50% HP, summon one 10002 in each of four adjacent non-home tiles. Scenario convention: trigger at 10 seconds. Child has no ordinary attack or movement, so no fabricated DPS/path contribution is added.')
            UnquantifiedRisk = @('Child distraction value, tile availability and parent movement-speed path value are not quantified.')
        }
        '10031' = @{
            TypeId = 10031
            ModelKind = 'OrdinaryBaseline'
            Parameters = @{ WindowSeconds = 20 }
            Evidence = @('Approved design section 4: score only the confirmed ordinary physical panel.')
            UnquantifiedRisk = @()
        }
        '10038' = @{
            TypeId = 10038
            ModelKind = 'DamageReduction20Seconds'
            Parameters = @{ WindowSeconds = 20; PhysicalDamageTakenMultiplier = 0.10; MagicDamageTakenMultiplier = 0.10 }
            Evidence = @('BONDS: physical and magic damage taken -90%, so both expected incoming multipliers are 0.10.')
            UnquantifiedRisk = @('Collision entry count, collision target count and block-count value are not quantified.')
        }
        '10039' = @{
            TypeId = 10039
            ModelKind = 'DamageReduction20Seconds'
            Parameters = @{ WindowSeconds = 20; PhysicalDamageTakenMultiplier = 0.10; MagicDamageTakenMultiplier = 0.10 }
            Evidence = @('Approved design section 4: quantify only physical and magic damage taken -90%, giving multipliers 0.10 and 0.10.')
            UnquantifiedRisk = @('charge time unknown', 'range damage multiplier/radius unknown', 'block change unknown')
        }
        '10077' = @{
            TypeId = 10077
            ModelKind = 'ScheduledSummon20Seconds'
            Parameters = @{ WindowSeconds = 20; SummonTypeId = 10073; SummonCountPerCast = 1; SkillPointsPerSecond = 2; InitialSkillPoints = 3; SkillPointCost = 5; SpawnTimesSeconds = @(1.0, 3.5, 6.0, 8.5, 11.0, 13.5, 16.0, 18.5) }
            Evidence = @('Approved design section 4 and SPEC: 2 SP/s, initial 3 SP, cost 5. Cast times are (5-3)/2=1 then every 5/2=2.5 seconds through 18.5; contribution is 10073 PanelPower * remaining lifetime / 20.')
            UnquantifiedRisk = @('Actual battle termination and summoned-entity deaths can reduce contribution.')
        }
        '10078' = @{
            TypeId = 10078
            ModelKind = 'Aoe20Seconds'
            Parameters = @{ WindowSeconds = 20; OutputTargetCountLow = 1; OutputTargetCountMain = 2; OutputTargetCountHigh = 3 }
            Evidence = @('BONDS: every attack deals 100% ATK physical splash in radius 1.5. AoE sensitivity is 1/2/3 targets; main uses 2.')
            UnquantifiedRisk = @('Actual target occupancy is encounter dependent.')
        }
        '10126' = @{
            TypeId = 10126
            ModelKind = 'DamageReductionCounter20Seconds'
            Parameters = @{ WindowSeconds = 20; PhysicalDamageTakenMultiplier = 0.50; MagicDamageTakenMultiplier = 0.50; CounterMagicDamagePerHit = 200; IncomingHitsPerSecond = 1 }
            Evidence = @('BONDS: physical/magic damage taken -50%; each enemy hit returns 200 source-less magic damage. Counter convention is one incoming hit per second.')
            UnquantifiedRisk = @('Actual incoming hit rate and attacker targetability are encounter dependent.')
        }
        '10127' = @{
            TypeId = 10127
            ModelKind = 'AttackSpeedDamageReduction20Seconds'
            Parameters = @{ WindowSeconds = 20; OutputSegments = @(@{ DurationSeconds = 20; AttackSpeedBonus = 100 }); PhysicalDamageTakenMultiplier = 0.50; MagicDamageTakenMultiplier = 0.50 }
            Evidence = @('BONDS: ASPD +100 and physical/magic damage taken -50% for the full window. Relative attack rate is (200+100)/200.')
            UnquantifiedRisk = @()
        }
    }

    ExplicitRiskOnly = @{
        '10001' = @('Unblockable and move speed +150% last 1.5 seconds after the first below-50% trigger; route length and trigger time are not confirmed, so numeric contribution is zero.')
        '1017'  = @('Actual route, recipients, uptime, stacking and silence timing are not confirmed.')
        '1021'  = @('Actual death time, air-target mix and affected target count are not confirmed.')
        '1042'  = @('Actual route, affected enemies, uptime, stacking and silence timing are not confirmed.')
        '1089'  = @('External damage/healing can change death time and whether the burst occurs in-window.')
        '1119'  = @('Actual release time changes defense-effect uptime.')
        '1121'  = @('Actual release time and the first-release ally effect are not fully determined.')
        '1131'  = @('Actual death time and spawn placement are encounter dependent.')
        '1132'  = @('Actual death time and spawn placement are encounter dependent.')
        '1146'  = @('Travel time, attack-range geometry, recipients, stacking and silence timing are not confirmed.')
        '1169'  = @('Actual same-family entity count and formation uptime are encounter dependent.')
        '1232'  = @('Actual threshold time and block-count value are encounter dependent.')
        '1240'  = @('BONDS confirms block count +1, but the approved 20-second model has no confirmed conversion from block slots to continuous power; numeric contribution is zero.')
        '1262'  = @('Actual blocked uptime is encounter dependent.')
        '1264'  = @('Actual first-damage time and movement-speed path effect are encounter dependent.')
        '1274'  = @('Actual threshold time and movement-speed path effect are encounter dependent.')
        '1322'  = @('BONDS confirms a 1.5-tile displacement after every third attack and one second unblockable, but route geometry and resulting target pressure are not confirmed; numeric contribution is zero.')
        '1355'  = @('Actual route, recipients, uptime, stacking and silence timing are not confirmed.')
        '1372'  = @('Actual cross occupancy and air-target exclusions are encounter dependent.')
        '1375'  = @('Actual blocked uptime and target availability are encounter dependent.')
        '1437'  = @('Actual death time and the summoned unit movement-speed path value are encounter dependent.')
        '2031'  = @('Summons from every ten incoming hits and their shared-count condition are not counted because incoming hit timing is not confirmed.')
        '5503'  = @('Actual battle termination and spawn survival can reduce contribution.')
        '7002'  = @('Actual incoming hit rate and attacker targetability are encounter dependent.')
        '10006' = @('Child distraction value, tile availability and parent movement-speed path value are not quantified.')
        '10038' = @('Collision entry count, collision target count and block-count value are not quantified.')
        '10039' = @('charge time unknown', 'range damage multiplier/radius unknown', 'block change unknown')
        '10077' = @('Actual battle termination and summoned-entity deaths can reduce contribution.')
        '10078' = @('Actual target occupancy is encounter dependent.')
        '10126' = @('Actual incoming hit rate and attacker targetability are encounter dependent.')
    }
}
