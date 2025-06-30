using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mono.Addins;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("MutSea")]
[assembly: AssemblyDescription("The executable for regions simulator")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("http://mutsea.com")]
[assembly: AssemblyProduct("MutSea")]
[assembly: AssemblyCopyright("MutSea developers")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("f6700ed5-1e6f-44d8-8397-e5eac42b3856")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
[assembly: AssemblyVersion(MutSea.VersionInfo.AssemblyVersionNumber)]

[assembly: AddinRoot("MutSea", MutSea.VersionInfo.VersionNumber)]
[assembly: ImportAddinAssembly("MutSea.Framework.dll")]
