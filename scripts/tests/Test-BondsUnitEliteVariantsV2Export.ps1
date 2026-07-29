[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$AnimationAuditPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$generator = Join-Path $repositoryRoot 'scripts\Export-BondsUnitEliteVariantsV2.ps1'
$sourceDirectory = Join-Path $repositoryRoot 'Assets\GameData\Units\EliteVariants\Json'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'Artifacts\UnitDataImport20260729\GeneratorTestOutput'
}
if ([string]::IsNullOrWhiteSpace($AnimationAuditPath)) {
    $AnimationAuditPath = Join-Path $repositoryRoot 'Artifacts\UnitDataImport20260729\bonds-unit-animation-audit-v1.json'
}

if (-not (Test-Path -LiteralPath $generator -PathType Leaf)) {
    throw "Generator is missing: $generator"
}
if (-not (Test-Path -LiteralPath $AnimationAuditPath -PathType Leaf)) {
    throw "Animation audit is missing: $AnimationAuditPath"
}

& $generator -OutputDirectory $OutputDirectory -AnimationAuditPath $AnimationAuditPath
if (-not $?) {
    throw 'First v2 export failed.'
}
$firstHashes = @{}
foreach ($file in Get-ChildItem -LiteralPath $OutputDirectory -File -Filter '*.json') {
    $firstHashes[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}

& $generator -OutputDirectory $OutputDirectory -AnimationAuditPath $AnimationAuditPath
if (-not $?) {
    throw 'Second v2 export failed.'
}
$secondFiles = @(Get-ChildItem -LiteralPath $OutputDirectory -File -Filter '*.json')
if ($secondFiles.Count -ne 97 -or $firstHashes.Count -ne 97) {
    throw "Expected 97 generated documents, first=$($firstHashes.Count) second=$($secondFiles.Count)"
}
foreach ($file in $secondFiles) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if (-not $firstHashes.ContainsKey($file.Name) -or $firstHashes[$file.Name] -ne $hash) {
        throw "Generator output is not deterministic: $($file.Name)"
    }
}

$audit = [System.IO.File]::ReadAllText($AnimationAuditPath, $strictUtf8) | ConvertFrom-Json
$auditByUnitKey = @{}
foreach ($variant in @($audit.variants)) {
    $auditByUnitKey[$variant.unitKey] = $variant
}

$documents = @(
    $secondFiles |
        Sort-Object Name |
        ForEach-Object {
            [System.IO.File]::ReadAllText($_.FullName, $strictUtf8) | ConvertFrom-Json
        }
)
$variantCount = ($documents | ForEach-Object { @($_.variants).Count } | Measure-Object -Sum).Sum
if ($documents.Count -ne 97 -or $variantCount -ne 180) {
    throw "Generated scope mismatch: documents=$($documents.Count) variants=$variantCount"
}

$generatedTypeIds = @($documents | ForEach-Object { [int]$_.typeId } | Sort-Object)
if (@($generatedTypeIds | Sort-Object -Unique).Count -ne 97) {
    throw 'Generated TypeIds must be unique.'
}
foreach ($excluded in @(1000, 1021, 5503, 5504)) {
    if ($generatedTypeIds -contains $excluded) {
        throw "Generated scope contains an excluded or preserved TypeId=$excluded"
    }
}

$preservedBondsDocuments = @(
    '5503_arcslma.json',
    '5504_arcslmi.json' |
        ForEach-Object {
            [System.IO.File]::ReadAllText((Join-Path $sourceDirectory $_), $strictUtf8) |
                ConvertFrom-Json
        }
)
$bondsTypeIds = @(
    $generatedTypeIds +
    @($preservedBondsDocuments | ForEach-Object { [int]$_.typeId }) |
        Sort-Object -Unique
)
if ($bondsTypeIds.Count -ne 99) {
    throw "Generated plus preserved BONDS scope must contain 99 TypeIds, actual=$($bondsTypeIds.Count)"
}

$noAttack = @(1008, 1017, 1026, 1042, 1146, 1333, 1355, 10002)
$unblockable = @(1008, 1017, 1026, 1042, 1146, 1333, 1355)
$action2 = @(1008, 1017, 1026, 1042, 1355)
$action3 = @(1146)
$action4 = @(10002)
$forbiddenText = @(
    '"hitAnimation"',
    '"animationBehavior"',
    '"resourceFolderName"',
    '"unitSkeletonType"',
    '"Default"'
)

foreach ($file in $secondFiles) {
    $json = [System.IO.File]::ReadAllText($file.FullName, $strictUtf8)
    $document = $json | ConvertFrom-Json
    $typeId = [int]$document.typeId
    if ($document.schemaVersion -ne 'unit-elite-variants-v2') {
        throw "Schema mismatch: $($file.Name)"
    }
    foreach ($forbidden in $forbiddenText) {
        if ($json.Contains($forbidden)) {
            throw "Forbidden field or animation entered source: file=$($file.Name) token=$forbidden"
        }
    }
    if ([int]$document.common.deploymentCost -ne 2 `
        -or [double]$document.common.attackRadiusMetres -ne 0 `
        -or [double]$document.common.blockRadiusMetres -ne 0 `
        -or [int]$document.common.tauntLevel -ne 0) {
        throw "Common constants mismatch: TypeId=$typeId"
    }

    $expectedAction = if ($typeId -in $action2) {
        2
    } elseif ($typeId -in $action3) {
        3
    } elseif ($typeId -in $action4) {
        4
    } else {
        1
    }
    if ([int]$document.common.actionMethod -ne $expectedAction) {
        throw "Action method mismatch: TypeId=$typeId"
    }
    $expectedCanBlock = $typeId -notin $unblockable
    if ([bool]$document.common.canBlock -ne $expectedCanBlock `
        -or [int]$document.common.blockCapacity -ne $(if ($expectedCanBlock) { 1 } else { 0 })) {
        throw "Blocking rule mismatch: TypeId=$typeId"
    }

    $isAttacker = $typeId -notin $noAttack
    if ([int]$document.common.attackMethod -ne $(if ($isAttacker) { 1 } else { 0 })) {
        throw "Attack method mismatch: TypeId=$typeId"
    }
    if (($isAttacker -and $document.common.damageType -notin @('Physical', 'Magic')) `
        -or (-not $isAttacker -and $document.common.damageType -ne 'None')) {
        throw "Damage type mismatch: TypeId=$typeId value=$($document.common.damageType)"
    }

    $variants = @($document.variants)
    if (@($variants | Where-Object { [int]$_.minEliteLevel -eq 0 }).Count -ne 1) {
        throw "Elite zero must be unique: TypeId=$typeId"
    }
    $base = @($variants | Where-Object { [int]$_.minEliteLevel -eq 0 })[0]
    if ($file.BaseName -ne $base.sourceVariant) {
        throw "File name does not match base sourceVariant: $($file.Name)"
    }

    foreach ($variant in $variants) {
        $unitKey = [string]$variant.sourceVariant
        if (-not $auditByUnitKey.ContainsKey($unitKey)) {
            throw "Animation audit is missing generated unitKey=$unitKey"
        }
        if ([int]$variant.statsLevel -ne 0 `
            -or [string]::IsNullOrWhiteSpace([string]$variant.displayNameZhHans) `
            -or $variant.skillDescriptionZhHans -ne '' `
            -or @($variant.innateAbilityIds).Count -ne 0) {
            throw "Variant completeness mismatch: unitKey=$unitKey"
        }
        if ([int]$variant.stats.combat.maxHitPoints -le 0 `
            -or [int]$variant.stats.combat.defense -lt 0 `
            -or [int]$variant.stats.combat.magicResistance -lt 0 `
            -or [int]$variant.stats.combat.magicResistance -gt 100 `
            -or [int]$variant.stats.shared.lifeDeduct -lt 0) {
            throw "Variant stats are invalid: unitKey=$unitKey"
        }
        if ($document.common.actionMethod -eq 4) {
            if ([double]$variant.stats.shared.moveSpeedMetresPerSecond -ne 0) {
                throw "Stationary unit has nonzero move speed: unitKey=$unitKey"
            }
        } elseif ([double]$variant.stats.shared.moveSpeedMetresPerSecond -le 0) {
            throw "Moving unit has invalid move speed: unitKey=$unitKey"
        }

        $resourceKey = $unitKey.Substring($unitKey.IndexOf('_') + 1)
        if ($variant.model.resourceKey -ne $resourceKey `
            -or $variant.model.skeletonDataResourceName -ne "enemy_$($unitKey)_SkeletonData" `
            -or $variant.model.profilePictureResourceName -ne "UIImage_$unitKey") {
            throw "Model resource identity mismatch: unitKey=$unitKey"
        }

        $bindings = @($variant.model.animations)
        $keys = @($bindings | ForEach-Object { [string]$_.key })
        foreach ($required in @('idle', 'move', 'death')) {
            if ($keys -notcontains $required) {
                throw "Required base animation missing: unitKey=$unitKey key=$required"
            }
        }
        if ($isAttacker) {
            if ($keys -notcontains 'attack' `
                -or [int]$variant.stats.combat.attack -lt 0 `
                -or [double]$variant.stats.shared.attackIntervalSeconds -le 0) {
                throw "Attacker contract mismatch: unitKey=$unitKey"
            }
        } else {
            if (@($keys | Where-Object { $_ -eq 'attack' -or $_.StartsWith('attack.') }).Count -ne 0 `
                -or [int]$variant.stats.combat.attack -ne 0 `
                -or [double]$variant.stats.shared.attackIntervalSeconds -ne 0) {
                throw "Non-attacker contract mismatch: unitKey=$unitKey"
            }
        }

        $auditAnimations = @{}
        foreach ($animation in @($auditByUnitKey[$unitKey].animations)) {
            $auditAnimations[$animation.name] = [double]$animation.durationSeconds
        }
        foreach ($binding in $bindings) {
            if (-not $auditAnimations.ContainsKey($binding.name)) {
                throw "Configured animation is absent from Spine audit: unitKey=$unitKey name=$($binding.name)"
            }
            $durationProperty = $binding.PSObject.Properties['durationSeconds']
            $isBase = $binding.key -in @('idle', 'move', 'death')
            if ($isBase -and $null -ne $durationProperty) {
                throw "Base animation must not store a duration: unitKey=$unitKey key=$($binding.key)"
            }
            if (-not $isBase) {
                if ($null -eq $durationProperty `
                    -or [double]$durationProperty.Value -le 0 `
                    -or [Math]::Abs([double]$durationProperty.Value - $auditAnimations[$binding.name]) -gt 0.000001) {
                    throw "Animation duration mismatch: unitKey=$unitKey key=$($binding.key)"
                }
            }
        }
    }
}

$crownslayer = @($documents | Where-Object { [int]$_.typeId -eq 1502 })[0]
$crownslayerKeys = @($crownslayer.variants[0].model.animations | ForEach-Object { $_.key })
if ($crownslayerKeys -notcontains 'blink.disappear' -or $crownslayerKeys -notcontains 'blink.appear') {
    throw 'Crownslayer must retain both factual blink animation durations.'
}

$prisoner = @($documents | Where-Object { [int]$_.typeId -eq 1116 })[0]
$prisonerBindings = @{}
foreach ($binding in @($prisoner.variants[0].model.animations)) {
    $prisonerBindings[$binding.key] = $binding.name
}
if ($prisonerBindings['attack'] -ne 'Attack3' `
    -or $prisonerBindings['attack.2'] -ne 'Attack2' `
    -or $prisonerBindings['attack.released'] -ne 'Attack') {
    throw 'Prisoner state animation bindings do not follow the confirmed 3 -> 2 -> released order.'
}

$wdgyht = @($documents | Where-Object { [int]$_.typeId -eq 1322 })[0]
$wdgyhtBase = @($wdgyht.variants | Where-Object { [int]$_.minEliteLevel -eq 0 })[0]
$wdgyhtElite2 = @($wdgyht.variants | Where-Object { [int]$_.minEliteLevel -eq 2 })[0]
$externalRoot = 'G:\ARKnoNIGHTS_tools\spine-fetcher-output-variants-20260725\staging'
$externalDefault = [System.IO.File]::ReadAllText(
    (Join-Path $externalRoot '1322_wdgyht_2\unit-levels.json'),
    $strictUtf8) | ConvertFrom-Json
$externalElite2 = [System.IO.File]::ReadAllText(
    (Join-Path $externalRoot '1322_wdgyht\unit-levels.json'),
    $strictUtf8) | ConvertFrom-Json
if ($wdgyhtBase.sourceVariant -ne '1322_wdgyht' `
    -or [int]$wdgyhtBase.stats.combat.maxHitPoints -ne [int]$externalDefault.levels[0].maxHitPoints `
    -or $wdgyhtElite2.sourceVariant -ne '1322_wdgyht_2' `
    -or [int]$wdgyhtElite2.stats.combat.maxHitPoints -ne [int]$externalElite2.levels[0].maxHitPoints) {
    throw '1322 canonical resources do not retain the confirmed reversed external-source mapping.'
}

Write-Output "BONDS_UNIT_ELITE_VARIANTS_V2_EXPORT_VALID documents=$($documents.Count) variants=$variantCount typeIds=$($bondsTypeIds.Count)"
