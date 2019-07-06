param([string]$dir)

$c = Invoke-WebRequest -UseBasicParsing -Uri https://dotnetcli.blob.core.windows.net/dotnet/Sdk/master/latest.version
$d = $c.Content.Substring(41).Trim()
Invoke-WebRequest -UseBasicParsing -Uri "https://dotnetcli.azureedge.net/dotnet/Sdk/$d/dotnet-sdk-$d-win-x64.zip" -OutFile $dir\"dotnet-sdk-$d-win-x64.zip"
Expand-Archive $dir\"dotnet-sdk-$d-win-x64.zip" -DestinationPath $dir