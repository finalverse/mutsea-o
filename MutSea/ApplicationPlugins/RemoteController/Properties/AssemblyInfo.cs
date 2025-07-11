﻿using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mono.Addins;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("MutSea.ApplicationPlugins.RemoteController")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("http://mutsea.com")]
[assembly: AssemblyProduct("MutSea")]
[assembly: AssemblyCopyright("Copyright MutSea developers © 2025")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("efec6e69-fc4a-4e21-86e6-4a261c12d4db")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
[assembly: AssemblyVersion(MutSea.VersionInfo.AssemblyVersionNumber)]

[assembly: Addin("MutSea.ApplicationPlugins.RemoteController", MutSea.VersionInfo.VersionNumber)]
[assembly: AddinDependency("MutSea", MutSea.VersionInfo.VersionNumber)]
