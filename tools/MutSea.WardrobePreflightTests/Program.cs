// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using Finalverse.World;
using System.Security.Cryptography;

if(args.Length!=3)throw new ArgumentException("Expected approved attachment, animated citizen and retired provisional directories.");
string Pin(string directory)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,"compiled.json")))).ToLowerInvariant();
int passed=0;
void Reject(Action action){bool refused=false;try{action();}catch(InvalidDataException){refused=true;}if(!refused)throw new Exception("Expected preflight refusal");passed++;}
var result=AvatarCatalog.LoadWardrobe(args[0],Pin(args[0]),out var alpha);
if(result.package.representation!="attachment"||alpha is not {Length:>0})throw new Exception("Pinned worn wardrobe not loaded");passed++;
Reject(()=>AvatarCatalog.LoadWardrobe(args[0],new string('0',64),out _));
Reject(()=>AvatarCatalog.LoadWardrobe(args[1],Pin(args[1]),out _));
Reject(()=>AvatarCatalog.LoadWardrobe(args[2],Pin(args[2]),out _));
Console.WriteLine($"{passed} pinned wardrobe preflight tests passed; no identity, inventory or appearance mutation.");
