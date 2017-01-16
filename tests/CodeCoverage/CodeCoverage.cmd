@echo off

cd tests\CodeCoverage

nuget restore packages.config -PackagesDirectory .

cd ..\Shaper2D.Tests

dotnet restore

cd ..
cd ..

rem The -threshold options prevents this taking ages...
tests\CodeCoverage\OpenCover.4.6.519\tools\OpenCover.Console.exe -target:"C:\Program Files\dotnet\dotnet.exe" -targetargs:"test tests\Shaper2D.Tests -c Release -f net451" -threshold:10 -register:user -filter:"+[Shaper2D*]*" -excludebyattribute:*.ExcludeFromCodeCoverage* -hideskipped:All -returntargetcode -output:.\Shaper2D.Coverage.xml

if %errorlevel% neq 0 exit /b %errorlevel%

SET PATH=C:\\Python34;C:\\Python34\\Scripts;%PATH%
pip install codecov
codecov -f "Shaper2D.Coverage.xml"