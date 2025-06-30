Welcome to MutSea Virtual World (MutSea for short)!

# Overview

MutSea is a BSD Licensed Open Source project to develop a functioning
virtual worlds server platform capable of supporting multiple clients
and servers in a heterogeneous grid structure. MutSea is written in
C#, and can run under Mono or the Microsoft .NET runtimes.

This is considered an alpha release.  Some stuff works, a lot doesn't.
If it breaks, you get to keep *both* pieces.

# Compiling MutSea

Please see BUILDING.md

# Running MutSea on Windows

You will need dotnet 8.0 runtime (https://dotnet.microsoft.com/en-us/download/dotnet/8.0)


To run MutSea from a command prompt

 * cd to the bin/ directory where you unpacked MutSea
 * review and change configuration files (.ini) for your needs. see the "Configuring MutSea" section
 * run MutSea.exe


# Running MutSea on Linux/Mac

You will need

 * [dotnet 8.0 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
 * libgdiplus 
 
 if you have mono 6.x complete, you already have libgdiplus, otherwise you need to install it
 using a package manager for your operating system, like apt, brew, macports, etc
 for example on debian:
 
 `apt-get update && apt-get install -y apt-utils libgdiplus libc6-dev`
 
To run MutSea, from the unpacked distribution type:

 * cd bin
 * review and change configuration files (.ini) for your needs. see the "Configuring MutSea" section
 * run ./MutSea.sh


# Configuring MutSea

When MutSea starts for the first time, you will be prompted with a
series of questions that look something like:

	[09-17 03:54:40] DEFAULT REGION CONFIG: Simulator Name [MutSea Test]:

For all the options except simulator name, you can safely hit enter to accept
the default if you want to connect using a client on the same machine or over
your local network.

You will then be asked "Do you wish to join an existing estate?".  If you're
starting MutSea for the first time then answer no (which is the default) and
provide an estate name.

Shortly afterwards, you will then be asked to enter an estate owner first name,
last name, password and e-mail (which can be left blank).  Do not forget these
details, since initially only this account will be able to manage your region
in-world.  You can also use these details to perform your first login.

Once you are presented with a prompt that looks like:

	Region (My region name) #

You have successfully started MutSea.

If you want to create another user account to login rather than the estate
account, then type "create user" on the MutSea console and follow the prompts.

Helpful resources:
 * http://opensimulator.org/wiki/Configuration
 * http://opensimulator.org/wiki/Configuring_Regions

# Connecting to your MutSea

By default your sim will be available for login on port 9000.  You can login by
adding -loginuri http://127.0.0.1:9000 to the command that starts Second Life
(e.g. in the Target: box of the client icon properties on Windows).  You can
also login using the network IP address of the machine running MutSea (e.g.
http://192.168.1.2:9000)

To login, use the avatar details that you gave for your estate ownership or the
one you set up using the "create user" command.


# More Information on OpenSim

More extensive information on building, running, and configuring
OpenSim, as well as how to report bugs, and participate in the OpenSim
project can always be found at http://opensimulator.org.

Thanks for trying MutSea, we hope it is a pleasant experience.

