$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$stage = Join-Path $PSScriptRoot 'artifacts/mutationen'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
$project = Join-Path $PSScriptRoot 'Versorgungsabsturz.csproj'
$phase = [IO.File]::ReadAllText((Join-Path $repo 'Tools/ParkingLotVersorgungsphasen.cs'))
$diagnose = [IO.File]::ReadAllText((Join-Path $repo 'Tools/ParkingLotVersorgungsdiagnoseSystem.cs'))
$faelle = @(
    @{ Name='alte-phase'; Property='Phasenquelle'; Text=$phase.Replace('SystemUpdatePhase.Modification1','SystemUpdatePhase.Modification2') },
    @{ Name='nur-100-bilder'; Property='Diagnosequelle'; Text=$diagnose.Replace('_bilder >= 120','_bilder >= 100') },
    @{ Name='flussende-ungeprueft'; Property='Diagnosequelle'; Text=$diagnose.Replace('if (!Existiert(a) || !Existiert(b) || !Typ(a) || !Typ(b)) tot++;','if (false) tot++;') }
)
foreach ($fall in $faelle) {
    $datei = Join-Path $stage ($fall.Name + '.cs')
    [IO.File]::WriteAllText($datei, $fall.Text)
    dotnet run -c Release --project $project "-p:$($fall.Property)=$datei" *> (Join-Path $stage ($fall.Name + '.log'))
    $code = $LASTEXITCODE
    $befund = Get-Content (Join-Path $stage ($fall.Name + '.log')) -Raw
    if ($code -eq 0 -or $befund -notmatch 'FEHLER:') { throw "Mutation $($fall.Name) nicht durch Testbefund erkannt: Exit=$code" }
    "Mutation $($fall.Name) erkannt: Exit=$code"
}
dotnet run -c Release --project $project
if ($LASTEXITCODE -ne 0) { throw 'Original nach Mutation nicht gruen.' }

# Die Original-Testdateien bleiben unangetastet. Nur die Produktionsquelle
# wird in einem getrennten Projekt gegen die alte Zielwahl ausgetauscht.
$geoProjekt = Join-Path $repo 'Tests/GeometryParity/CSharp/GeometryParity.csproj'
[xml]$xml = [IO.File]::ReadAllText($geoProjekt)
foreach ($item in $xml.Project.ItemGroup.Compile) {
    $item.SetAttribute('Include', (Join-Path (Split-Path $geoProjekt) $item.GetAttribute('Include')))
    if ($item.GetAttribute('Include').EndsWith('\Geometry\*.cs')) {
        $item.SetAttribute('Exclude', $item.GetAttribute('Include').Replace('*.cs','VersorgungstrassenPlan.cs'))
    }
}
$planQuelle = Join-Path $repo 'Geometry/VersorgungstrassenPlan.cs'
$planMutation = Join-Path $stage 'Plan-Kantenmitte.cs'
[IO.File]::WriteAllText($planMutation, [IO.File]::ReadAllText($planQuelle).Replace('if (e.NurKnotenziele)', 'if (false)'))
$group = $xml.CreateElement('ItemGroup')
$compile = $xml.CreateElement('Compile')
$compile.SetAttribute('Include', $planMutation)
$group.AppendChild($compile) | Out-Null
$xml.Project.AppendChild($group) | Out-Null
$geoMutation = Join-Path $stage 'GeometryMutation.csproj'
$xml.Save($geoMutation)
dotnet run -c Release --project $geoMutation -- --versorgung *> (Join-Path $stage 'kantenmitte.log')
$code = $LASTEXITCODE
$befund = Get-Content (Join-Path $stage 'kantenmitte.log') -Raw
if ($code -eq 0 -or $befund -notmatch 'FEHLER Versorgung: Diagnose: vorhandener Endknoten') {
    throw "Mutation Kantenmitte nicht durch Testbefund erkannt: Exit=$code"
}
"Mutation Kantenmitte erkannt: Exit=$code"
