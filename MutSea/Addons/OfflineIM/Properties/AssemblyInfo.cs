﻿using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mono.Addins;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("MutSea.Addons.OfflineIM")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("http://mutsea.com")]
[assembly: AssemblyProduct("MutSea.Addons.OfflineIM")]
[assembly: AssemblyCopyright("Copyright (c) OpenSimulator.org Developers")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("a16a9905-4393-4872-9fca-4c81bedbd9f2")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
[assembly: AssemblyVersion(MutSea.VersionInfo.AssemblyVersionNumber)]

[assembly: Addin("MutSea.OfflineIM", MutSea.VersionInfo.VersionNumber)]
[assembly: AddinDependency("MutSea.Region.Framework", MutSea.VersionInfo.VersionNumber)]
