// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
using Finalverse.World;

int passed=0;
void Check(bool value){if(!value)throw new Exception("Admission assertion failed.");passed++;}
void Refuse(Action action){try{action();}catch(Exception e)when(e is InvalidDataException or JsonException){passed++;return;}throw new Exception("Admission should be refused.");}
var admission=Guid.NewGuid().ToString();var account=Guid.NewGuid().ToString();
var n=new NewcomerAccount("Pilot","Builder","synthetic-private-only","/config/genesis/auren",new string('a',64),admission);
byte[] FileFor(params NewcomerAccount[] accounts)=>JsonSerializer.SerializeToUtf8Bytes(new NewcomerFile(accounts));
Check(NewcomerFile.Parse(FileFor(n)).accounts[0].admission_id==admission);
Check(NewcomerFile.Parse(FileFor(n with{admission_id=null})).accounts.Length==1);
Refuse(()=>NewcomerFile.Parse(FileFor(n with{admission_id=Guid.Empty.ToString()})));
Refuse(()=>NewcomerFile.Parse(FileFor(n with{admission_id=admission.ToUpperInvariant()})));
Refuse(()=>NewcomerFile.Parse(FileFor(n,n with{last_name="Explorer"})));
Refuse(()=>NewcomerFile.Parse(FileFor(n with{genesis=null,genesis_sha256=null})));
string selection=PilotAdmission.Selection(n,"region",[128,123]);
Check(selection==PilotAdmission.Selection(n with{password="different-private-password"},"region",[128,123]));
Check(selection!=PilotAdmission.Selection(n with{genesis_sha256=new string('b',64)},"region",[128,123]));
Check(selection!=PilotAdmission.Selection(n,"other-region",[128,123]));
Check(selection!=PilotAdmission.Selection(n,"region",[129,123]));
string Record(string kind,string id,string principal,string hash)=>JsonSerializer.Serialize(new{kind,admission_id=id,account_id=principal,selection_sha256=hash});
Check(PilotAdmission.Confirmed([Record("account_activated",admission,account,selection)],admission,account,selection));
Check(!PilotAdmission.Confirmed([Record("account_created",admission,account,selection)],admission,account,selection));
Check(!PilotAdmission.Confirmed([Record("account_activated",Guid.NewGuid().ToString(),account,selection)],admission,account,selection));
Check(!PilotAdmission.Confirmed([Record("account_activated",admission,Guid.NewGuid().ToString(),selection)],admission,account,selection));
Check(!PilotAdmission.Confirmed([Record("account_activated",admission,account,new string('b',64))],admission,account,selection));
Check(!PilotAdmission.Confirmed(["{\"kind\":\"account_activated\",\"account_id\":\"legacy\"}"],admission,account,selection));
Refuse(()=>PilotAdmission.Confirmed([Record("account_activated",admission,account,selection),"{"],admission,account,selection));
Console.WriteLine($"{passed} admission validation/replay checks passed.");
