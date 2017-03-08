@echo off

cd tests\CodeCoverage

nuget restore packages.config -PackagesDirectory .

cd ..\SixLabors.Shapes.Tests

dotnet restore SixLabors.Shapes.Tests.csproj
dotnet build SixLabors.Shapes.Tests.csproj /p:codecov=true

cd ..
cd ..

rem The -threshold options prevents this taking ages...
tests\CodeCoverage\OpenCover.4.6.519\tools\OpenCover.Console.exe -target:"dotnet.exe" -targetargs:"test tests\SixLabors.Shapes.Tests\SixLabors.Shapes.Tests.csproj --no-build -c Release" -threshold:10 -register:user -filter:"+[SixLabors.Shapes*]*" -excludebyattribute:*.ExcludeFromCodeCoverage* -hideskipped:All -returntargetcode -output:.\SixLabors.Shapes.Coverage.xml

if %errorlevel% neq 0 exit /b %errorlevel%

SET PATH=C:\\Python34;C:\\Python34\\Scripts;%PATH%
pip install codecov
codecov -f "SixLabors.Shapes.Coverage.xml"