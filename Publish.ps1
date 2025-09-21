$ErrorActionPreference = "Stop"

$version = (Select-Xml -Path .\NekoTrace.Web\NekoTrace.Web.csproj -XPath /Project/PropertyGroup/Version).Node.InnerText
$namePrefix = "NekoTrace-${version}-"

if (Test-Path publish) {
    Remove-Item publish/* -Recurse
} else {
    New-Item publish -ItemType Directory | Out-Null
}

foreach ($publishName in @("Portable", "Linux64SelfContained", "Win64", "Win64SelfContained")) {
    dotnet publish ./NekoTrace.Web/NekoTrace.Web.csproj -p:PublishProfile=$publishName

    Compress-Archive -Path "./NekoTrace.Web/bin/Release/net9.0/publish/${publishName}/*" -DestinationPath "./publish/${namePrefix}${publishName}.zip"
}