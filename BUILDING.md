# git clone

get or update source from git

 `git clone git://opensimulator.org/git/opensim`
	


# Building on Windows

## Requirements
  To building under Windows, the following is required:

  * [dotnet 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

optionally also

  * [Visual Studio .NET](https://visualstudio.microsoft.com/vs/features/net-development/), version 2022 or later
  

### Building
 To create the project files, run   

  `runprebuild.bat`

run 
  `compile.bat`

Or load the generated MutSea.sln into Visual Studio and build the solution.

Configure, see below

Now just run `MutSea.exe` from the `bin` folder, and set up the region.

# Building on Linux / Mac

## Requirements

 * [dotnet 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
 * libgdiplus 
 
 if you have mono 6.x complete, you already have libgdiplus, otherwise you need to install it
 using a package manager for your operating system, like apt, brew, macports, etc
 for example on debian:
 
 `apt-get update && apt-get install -y apt-utils libgdiplus libc6-dev`

### Building
  To create the project files, run:

  `./runprebuild.sh`

  then run

 `dotnet build --configuration Release MutSea.sln`
  

Configure. See below

run `./MutSea.sh` from the `bin` folder, and set up the region



# Configure #
## Standalone mode ##
Copy `MutSea.ini.example` to `MutSea.ini` in the `bin/` directory, and verify the `[Const]` section, correcting for your case.

On `[Architecture]` section uncomment only the line with Standalone.ini if you do now want HG, or the line with StandaloneHypergrid.ini if you do

copy the `StandaloneCommon.ini.example` to `StandaloneCommon.ini` in the `bin/config-include` directory.

The StandaloneCommon.ini file describes the database and backend services that MutSea will use, and is set to use sqlite by default, which requires no setup.


## Grid mode ##
Each grid may have its own requirements, so FOLLOW your Grid instructions!
in general:
Copy `MutSea.ini.example` to `MutSea.ini` in the `bin/` directory, and verify the `[Const]` section, correcting for your case
 
On `[Architecture]` section uncomment only the line with Grid.ini if you do now want HG, or the line with GridHypergrid.ini if you do

and copy the `GridCommon.ini.example` file to `GridCommon.ini` inside the `bin/config-include` directory and edit as necessary



# References

* http://opensimulator.org/wiki/Build_Instructions
* http://opensimulator.org/wiki/Configuration