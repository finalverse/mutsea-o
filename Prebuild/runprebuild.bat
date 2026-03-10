@echo OFF
set DOTNET_ROLL_FORWARD=Major
dotnet bootstrap/prebuild.dll /target vs2022 /targetframework net6_0 /excludedir = "obj | bin" /file prebuild.xml
set DOTNET_ROLL_FORWARD=
