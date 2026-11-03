$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if (!(Test-Path 'numbers.txt')) {
    dotnet run -c Release -- generate --file numbers.txt
    if ($LASTEXITCODE -ne 0) { throw 'Generation failed' }
}
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$program = 'bin/Release/net8.0/ConcurrencyLab1.dll'
foreach ($task in @('1a','1c')) {
    foreach ($threads in @(1,2,4,8,16,32,64)) {
        dotnet $program $task --file numbers.txt --threads $threads
        if ($LASTEXITCODE -ne 0) { throw "$task / $threads failed" }
    }
}
foreach ($mode in @('unsafe','unsafe','unsafe','lock','local')) {
    dotnet $program 1b --file numbers.txt --mode $mode
    if ($LASTEXITCODE -ne 0) { throw "1b / $mode failed" }
}
Write-Host 'Local measurements finished. See results/results.csv.'
