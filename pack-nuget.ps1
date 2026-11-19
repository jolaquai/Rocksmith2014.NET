param([string]$Output = 'D:\C#\NuGets')

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Output | Out-Null
dotnet pack "$PSScriptRoot\src\Rocksmith2014.All\Rocksmith2014.All.csproj" -c Release -o $Output
if ($LASTEXITCODE) { throw 'pack failed' }
