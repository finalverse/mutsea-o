#!/usr/bin/env python3
"""Build selected native services against an already built matching MutSea bin.

Copyright 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
Uses the repository's prebuild.xml references/resources; generates only in a new
external output directory. This is an incremental development build, not a full
simulator/platform build or a distribution artifact.
"""
import argparse
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
PROJECTS = ("MutSea.Data.SQLite", "MutSea.Data.PGSQL", "MutSea.Services.UserAccountService", "MutSea.Services.LLLoginService")

def build(assemblies, output):
    assemblies = assemblies.resolve(strict=True)
    if output.exists():
        raise ValueError("Preserve prior component builds; choose a new output directory")
    tree = ET.parse(ROOT / "prebuild.xml")
    for element in tree.iter():
        element.tag = element.tag.rsplit("}", 1)[-1]
    for name in PROJECTS:
        project = next(p for p in tree.iter("Project") if p.get("name") == name)
        source = ROOT / project.get("path")
        directory = output / name
        directory.mkdir(parents=True)
        root = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
        props = ET.SubElement(root, "PropertyGroup")
        for key, value in {"TargetFramework": "net8.0", "AssemblyName": name,
                           "RootNamespace": name, "GenerateAssemblyInfo": "false",
                           "EnableDefaultCompileItems": "false", "AllowUnsafeBlocks": "true",
                           "NoWarn": "0618;SYSLIB0050;SYSLIB0051"}.items():
            ET.SubElement(props, key).text = value
        items = ET.SubElement(root, "ItemGroup")
        for path in sorted(source.rglob("*.cs")):
            if not any(p in ("Tests", "obj", "bin") for p in path.relative_to(source).parts):
                ET.SubElement(items, "Compile", Include=str(path))
        for ref in project.findall("Reference"):
            module = ref.get("name")
            path = assemblies / (module + ".dll")
            if not path.is_file():
                raise ValueError("Missing pinned assembly: " + module)
            element = ET.SubElement(items, "Reference", Include=module)
            ET.SubElement(element, "HintPath").text = str(path)
        resources = source / "Resources"
        if resources.exists():
            for path in sorted(resources.rglob("*")):
                if path.suffix in (".sql", ".migrations") or path.name.endswith(".addin.xml"):
                    element = ET.SubElement(items, "EmbeddedResource", Include=str(path))
                    ET.SubElement(element, "LogicalName").text = name + ".Resources." + str(path.relative_to(resources)).replace("/", ".")
        ET.indent(root)
        file = directory / "Build.csproj"
        ET.ElementTree(root).write(file, encoding="utf-8", xml_declaration=True)
        subprocess.run(["dotnet", "build", str(file), "-c", "Release", "-o", str(directory / "bin")], check=True)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mutsea-bin", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    build(args.mutsea_bin, args.output.resolve())
