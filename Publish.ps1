$ErrorActionPreference = "Stop"

$version = (Select-Xml -Path .\Directory.Build.props -XPath /Project/PropertyGroup/Version).Node.InnerText
$namePrefix = "NekoTrace-${version}-"

if (Test-Path publish) {
    Remove-Item publish/* -Recurse
} else {
    New-Item publish -ItemType Directory | Out-Null
}

# One artifact per profile, each holding the server and the NekoTrace.Cli client beside it. The CLI is
# published by NekoTrace.Web's own PublishNekoTraceCli target rather than by a second loop here, so it lands
# in any publish of the server — including a plain `dotnet publish` — instead of only in a release built by
# this script.
#
# Output goes to artifacts/ at the root rather than under NekoTrace.Web/bin, because it is not the web
# project's output any more: it is the product, and two projects contribute to it.
foreach ($publishName in @("Portable", "Linux64SelfContained", "Win64", "Win64SelfContained")) {
    dotnet publish ./NekoTrace.Web/NekoTrace.Web.csproj -p:PublishProfile=$publishName

    Compress-Archive -Path "./artifacts/${publishName}/*" -DestinationPath "./publish/${namePrefix}${publishName}.zip"
}
