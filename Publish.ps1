$ErrorActionPreference = "Stop"

$version = (Select-Xml -Path .\NekoTrace.Web\NekoTrace.Web.csproj -XPath /Project/PropertyGroup/Version).Node.InnerText
$namePrefix = "NekoTrace-${version}-"

if (Test-Path Publish) {
    Remove-Item Publish/* -Recurse
} else {
    New-Item Publish -ItemType Directory | Out-Null
}

foreach ($publishName in @("Portable", "Linux64SelfContained", "Win64", "Win64SelfContained")) {
    dotnet publish ./NekoTrace.Web/NekoTrace.Web.csproj -p:PublishProfile=$publishName

    Compress-Archive -Path "./NekoTrace.Web/bin/Release/net9.0/publish/${publishName}/*" -DestinationPath "./Publish/${namePrefix}${publishName}.zip"
}