[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'generated')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Write-Utf8File([string] $Path, [string] $Contents) {
    [IO.File]::WriteAllText($Path, $Contents + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$bundleRoot = Join-Path $outputRoot 'bundle'
$demandRoot = Join-Path $bundleRoot 'demand\operational-demand-hh'
$weatherRoot = Join-Path $bundleRoot 'weather\station-01'
$dataRoot = Join-Path $outputRoot 'data'
$scenarioRoot = Join-Path $outputRoot 'scenarios'
$sweepRoot = Join-Path $outputRoot 'sweeps'
foreach ($directory in @($demandRoot, $weatherRoot, $dataRoot, $scenarioRoot, $sweepRoot)) {
    [IO.Directory]::CreateDirectory($directory) | Out-Null
}

$regionId = 'Island-X'
$weatherDirectory = 'station-01'
$regionBytes = [Text.Encoding]::UTF8.GetBytes($regionId.ToUpperInvariant())
$regionFileName = 'region-' + [BitConverter]::ToString($regionBytes).Replace('-', '')
$periodStart = [DateTimeOffset]::new(2025, 1, 1, 0, 0, 0, [TimeSpan]::FromHours(10))
$periodEnd = $periodStart.AddYears(1)

$manifest = [ordered]@{
    '$schema' = 'https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema/manifest-v2.json'
    schemaVersion = 2
    bundleId = 'bundle'
    name = 'Synthetic custom-grid demonstration'
    period = [ordered]@{
        start = $periodStart.ToString('yyyy-MM-ddTHH:mm:sszzz')
        end = $periodEnd.ToString('yyyy-MM-ddTHH:mm:sszzz')
    }
    regions = @([ordered]@{ regionId = $regionId; weatherDirectory = $weatherDirectory })
}
Write-Utf8File (Join-Path $bundleRoot 'manifest.json') ($manifest | ConvertTo-Json -Depth 20)

$zipPath = Join-Path $demandRoot 'synthetic-demand.zip'
if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
$archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    $entry = $archive.CreateEntry('operational-demand.csv')
    $entryStream = $entry.Open()
    try {
        $writer = [IO.StreamWriter]::new($entryStream, [Text.UTF8Encoding]::new($false))
        try {
            $writer.WriteLine('C,NEMP.WORLD,ACTUAL_OPERATIONAL_DEMAND_HH,SYNTHETIC,PUBLIC')
            $writer.WriteLine('I,OPERATIONAL_DEMAND,ACTUAL,3,INTERVAL_DATETIME,REGIONID,OPERATIONAL_DEMAND')
            for ($interval = 0; $interval -lt 17520; $interval++) {
                $intervalStart = $periodStart.AddMinutes(30 * $interval)
                $hour = $intervalStart.Hour
                $demandMw = if ($hour -ge 8 -and $hour -lt 20) { 60 } else { 30 }
                $intervalEnd = $intervalStart.AddMinutes(30)
                $timestamp = $intervalEnd.ToString('yyyy/MM/dd HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)
                $writer.WriteLine("D,OPERATIONAL_DEMAND,ACTUAL,3,$timestamp,$regionId,$demandMw")
            }
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $entryStream.Dispose()
    }
}
finally {
    $archive.Dispose()
}

$epwPath = Join-Path $weatherRoot 'synthetic-typical-year.epw'
$epw = [IO.StreamWriter]::new($epwPath, $false, [Text.UTF8Encoding]::new($false))
try {
    $epw.WriteLine('LOCATION,Synthetic Island,NA,XX,Synthetic,000001,-20.0,150.0,10,0')
    $epw.WriteLine('DESIGN CONDITIONS,0')
    $epw.WriteLine('TYPICAL/EXTREME PERIODS,0')
    $epw.WriteLine('GROUND TEMPERATURES,0')
    $epw.WriteLine('HOLIDAYS/DAYLIGHT SAVING,No')
    $epw.WriteLine('COMMENTS 1,Generated synthetic weather; not a measured site')
    $epw.WriteLine('COMMENTS 2,NEMSweep onboarding example')
    $epw.WriteLine('DATA PERIODS,1,1,Data,Sunday,1/1,12/31')
    for ($hourIndex = 0; $hourIndex -lt 8760; $hourIndex++) {
        $date = [DateTime]::new(2001, 1, 1).AddHours($hourIndex)
        [string[]] $fields = [string[]]::new(22)
        for ($column = 0; $column -lt $fields.Length; $column++) { $fields[$column] = '0' }
        $fields[0] = '2001'
        $fields[1] = $date.Month.ToString([Globalization.CultureInfo]::InvariantCulture)
        $fields[2] = $date.Day.ToString([Globalization.CultureInfo]::InvariantCulture)
        $fields[3] = ($date.Hour + 1).ToString([Globalization.CultureInfo]::InvariantCulture)
        $fields[5] = 'A0'
        $fields[6] = '25'
        $daylight = $date.Hour -ge 6 -and $date.Hour -lt 18
        $fields[13] = if ($daylight) { '500' } else { '0' }
        $fields[14] = if ($daylight) { '600' } else { '0' }
        $fields[15] = if ($daylight) { '100' } else { '0' }
        $fields[21] = '5'
        $epw.WriteLine([string]::Join(',', $fields))
    }
}
finally {
    $epw.Dispose()
}

$gasFleet = {
    param([double] $CapacityMw)
    [ordered]@{
        technology = 'Gas'
        nameplateCapacityMw = $CapacityMw
        costParameters = [ordered]@{
            capitalCostAudPerMw = 1000000
            fixedOperatingCostAudPerMwYear = 12000
            variableOperatingCostAudPerMwh = 10
            fuelPriceAudPerGj = 10
        }
        technologyProfile = [ordered]@{
            heatRateGjPerMwh = 7
            technicalLifeYears = 30
            emissionsIntensityTonnesPerMwh = 0.4
        }
    }
}

$batteryFleet = {
    [ordered]@{
        technology = 'Battery'
        initialEnergyCapacityMwh = 0
        initialPowerCapacityMw = 0
        costParameters = [ordered]@{
            powerCapitalCostAudPerMw = 200000
            energyCapitalCostAudPerMwh = 150000
            fixedOperatingCostAudPerMwYear = 10000
        }
        technologyProfile = [ordered]@{
            technicalLifeYears = 15
            roundTripEfficiency = 0.9
        }
    }
}

function New-Scenario([string] $id, [string] $name, [double] $gasCapacityMw, [string] $executionMode) {
    $scenario = [ordered]@{
        '$schema' = 'https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema/scenario-v7.json'
        schemaVersion = 7
        id = $id
        name = $name
        executionMode = $executionMode
        costBasis = [ordered]@{ year = 2025; realDiscountRate = 0.05 }
        reliability = [ordered]@{ targetUsePercentage = 0.01; standardName = 'Synthetic demonstration target' }
        regions = @([ordered]@{
            regionId = $regionId
            demandFile = "demand-$regionFileName.json"
            weatherFile = "weather-$regionFileName.json"
            generatingFleets = @((& $gasFleet $gasCapacityMw))
            storageFleets = @()
        })
    }
    if ($executionMode -eq 'sizeBattery') {
        $scenario['regions'][0]['storageFleets'] = @((& $batteryFleet))
        $scenario['storageSizing'] = [ordered]@{ maximumPowerMw = 30; maximumEnergyMwh = 500; maximumPasses = 64 }
    }
    return $scenario
}

$fixedScenario = New-Scenario 'island-fixed' 'Synthetic island: declared fleet' 80 'fixedFleet'
$sizedScenario = New-Scenario 'island-sized' 'Synthetic island: battery sizing' 50 'sizeBattery'
$sweepBaseline = New-Scenario 'island-mode-baseline' 'Synthetic island: mode sweep baseline' 50 'fixedFleet'
$sweepBaseline['regions'][0]['storageFleets'] = @((& $batteryFleet))
Write-Utf8File (Join-Path $scenarioRoot 'fixed-scenario.json') ($fixedScenario | ConvertTo-Json -Depth 30)
Write-Utf8File (Join-Path $scenarioRoot 'sized-scenario.json') ($sizedScenario | ConvertTo-Json -Depth 30)
Write-Utf8File (Join-Path $scenarioRoot 'mode-sweep-baseline.json') ($sweepBaseline | ConvertTo-Json -Depth 30)

$sweep = [ordered]@{
    '$schema' = 'https://raw.githubusercontent.com/HasinthaAttanayake/NEMSweep/main/schema/sweep-v2.json'
    schemaVersion = 2
    sweepId = 'island-storage-mode'
    name = 'Synthetic island fixed fleet and Battery sizing'
    axis = [ordered]@{ label = 'Execution mode'; unit = 'mode index' }
    baselineConfigPath = (Join-Path $scenarioRoot 'mode-sweep-baseline.json')
    points = @(
        [ordered]@{ pointId = 'fixed-fleet'; axisValue = 0; label = 'Declared fleet only'; overrides = [ordered]@{ executionMode = 'fixedFleet' } },
        [ordered]@{ pointId = 'size-battery'; axisValue = 1; label = 'Search bounded Battery capacity'; overrides = [ordered]@{ executionMode = 'sizeBattery'; storageSizing = [ordered]@{ maximumPowerMw = 30; maximumEnergyMwh = 500; maximumPasses = 64 } } }
    )
}
Write-Utf8File (Join-Path $sweepRoot 'mode-sweep.json') ($sweep | ConvertTo-Json -Depth 30)

Write-Host "Generated synthetic annual bundle: $bundleRoot"
Write-Host "Generated scenarios and sweep under: $outputRoot"
Write-Host 'No measured demand, weather, or generation-history data is included.'