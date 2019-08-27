set PATH=%PATH%;C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\MSBuild\ReadyRoll\OctoPack\tools
call "C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\Common7\Tools\VsDevCmd.bat" -arch=amd64 -host_arch=amd64 -winsdk=10.0.16299.0 -app_platform=Desktop

cd /D "%~dp0"
cd ../../

nuget.exe restore ".\TelemetryLib\TelemetryLib.csproj"
msbuild /p:Configuration=Release /p:Platform=x64 /property:AppInsightsKey="c065641b-ec84-43fe-a8e7-c2bcbb697995" ".\TelemetryLib\TelemetryLib.csproj"

nuget.exe restore ".\FabricObserver\FabricObserver.csproj"
msbuild /p:Configuration=Release /p:Platform=x64 ".\FabricObserver\FabricObserver.csproj"

nuget.exe restore ".\FabricObserverTests\FabricObserverTests.csproj"
msbuild /p:Configuration=Release /p:Platform=AnyCPU ".\FabricObserverTests\FabricObserverTests.csproj"

dotnet restore ".\FabricObserverWeb\FabricObserverWeb.csproj"
msbuild /p:Configuration=Release /p:Platform=AnyCPU ".\FabricObserverWeb\FabricObserverWeb.csproj"