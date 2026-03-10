#!/bin/sh

case "$1" in

 'clean')
    DOTNET_ROLL_FORWARD=Major dotnet bin/prebuild.dll /file prebuild.xml /clean

  ;;


  'autoclean')

    echo y|DOTNET_ROLL_FORWARD=Major dotnet bin/prebuild.dll /file prebuild.xml /clean

  ;;



  *)

    cp bin/System.Drawing.Common.dll.linux bin/System.Drawing.Common.dll
    DOTNET_ROLL_FORWARD=Major dotnet bin/prebuild.dll /target vs2022 /targetframework net8_0 /excludedir = "obj | bin" /file prebuild.xml
    echo "dotnet build -c Release MutSea.sln" > compile.sh
    chmod +x compile.sh

  ;;

esac
