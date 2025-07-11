﻿using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mono.Addins;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("MutSea.Region.OptionalModules")]
[assembly: AssemblyDescription("Optional modules for MutSea")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("MutSea.Region.OptionalModules.Properties")]
[assembly: AssemblyCopyright("Copyright ©  2012")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("84a3082d-3011-4c13-835c-c7d93f97ac79")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
[assembly: AssemblyVersion(MutSea.VersionInfo.AssemblyVersionNumber)]


[assembly: Addin("MutSea.Region.OptionalModules", MutSea.VersionInfo.VersionNumber)]
[assembly: AddinDependency("MutSea.Region.Framework", MutSea.VersionInfo.VersionNumber)]
