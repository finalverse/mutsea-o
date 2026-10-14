// Copyright 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
// Execute actual compiled native services with explicit fault-injected stores.
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using MutSea.Data;
using MutSea.Framework;
using MutSea.Services.Interfaces;
using MutSea.Services.UserAccountService;
using OpenMetaverse;

int passed=0;
void Assert(bool ok){if(!ok)throw new Exception("Assertion failed.");}
void Test(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
Test("activate only after six persisted wearable/link/asset bindings",()=>{
 var f=new Fixture();var account=f.Create();Assert(account!=null&&account.UserLevel==0&&f.Items.Count==12);
 Assert(f.Stored.First().Data["UserLevel"]=="-1000"&&f.Stored.Last().Data["UserLevel"]=="0"&&f.Stored.Last().Data["ServiceURLs"].Contains("MutSeaProvisioningState=active"));
});
foreach(string fault in new[]{"auth","inventory","wearable","link","appearance","asset","owner","binding","cof","root","activation","exception"})Test("inactive retained state on "+fault+" failure",()=>{
 var f=new Fixture{Fault=fault};Assert(f.Create()==null);Assert(f.Stored.Last().Data["UserLevel"]=="-1001"&&f.Stored.Last().Data["ServiceURLs"].Contains("MutSeaProvisioningState=failed"));
 Assert(f.Stored.All(s=>s.Data["UserLevel"]!="0"));
});
Test("legacy externally provisioned configuration stays compatible",()=>{var f=new Fixture{Fault="auth",Required=false};Assert(f.Create()!=null&&f.Stored.Count==1&&f.Stored[0].Data["UserLevel"]=="0");});
Test("chosen wardrobe completion runs while pending and before activation",()=>{
 var f=new Fixture();bool called=false;
 var result=f.Create(a=>{called=true;Assert(a.UserLevel==-1000&&f.Stored.All(s=>s.Data["UserLevel"]!="0"));});
 Assert(called&&result!=null&&result.UserLevel==0);
});
Test("chosen wardrobe failure retains inactive account without an active write",()=>{
 var f=new Fixture();Assert(f.Create(_=>throw new IOException("Synthetic wardrobe failure"))==null);
 Assert(f.Stored.Last().Data["UserLevel"]=="-1001"&&f.Stored.All(s=>s.Data["UserLevel"]!="0"));
});
Test("legacy external appearance cannot bypass extended provisioning",()=>{
 var f=new Fixture{Required=false};bool refused=false;
 try{f.Create(_=>{});}catch(InvalidOperationException){refused=true;}
 Assert(refused&&f.Stored.Count==0);
});
foreach(var type in new[]{typeof(MutSea.Data.SQLite.SQLiteGenericTableHandler<ColumnRow>),typeof(MutSea.Data.SQLite.SQLiteAuthenticationData),typeof(MutSea.Data.PGSQL.PGSQLGenericTableHandler<ColumnRow>),typeof(MutSea.Data.PGSQL.PGSQLAuthenticationData)})Test("parallel first queries never return partial columns: "+type.Name,()=>{
 var handler=RuntimeHelpers.GetUninitializedObject(type);
 if(type.IsGenericType)Fixture.Set(handler,"m_Fields",new Dictionary<string,FieldInfo>());
 Fixture.Set(handler,"m_ColumnNamesGate",new object());
 var method=type.GetMethod("CheckColumnNames",BindingFlags.Instance|BindingFlags.NonPublic);
 using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();using var secondStarted=new ManualResetEventSlim();using var secondDone=new ManualResetEventSlim();
 Exception failure=null;
 var first=new Thread(()=>{try{method.Invoke(handler,[new SchemaReader(()=>{entered.Set();if(!release.Wait(5000))throw new Exception("Schema test timed out.");})]);}catch(Exception e){failure=e;}});
 var second=new Thread(()=>{secondStarted.Set();try{method.Invoke(handler,[new SchemaReader(null)]);}catch(Exception e){failure=e;}finally{secondDone.Set();}});
 first.Start();Assert(entered.Wait(5000));second.Start();Assert(secondStarted.Wait(5000));bool premature=secondDone.Wait(200);release.Set();Assert(first.Join(5000)&&second.Join(5000));
 Assert(!premature&&failure==null);var columns=(List<string>)type.GetField("m_ColumnNames",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(handler);Assert(columns.Contains("Created")&&columns.Count==3);
});
Console.WriteLine($"{passed} native provisioning/concurrency tests passed");
return 0;

public class Proxy:DispatchProxy
{
 public Func<MethodInfo,object[],object> Handler;
 protected override object Invoke(MethodInfo method,object[] args)=>Handler(method,args);
 public static T For<T>(Func<MethodInfo,object[],object> fn) where T:class{T value=Create<T,Proxy>();((Proxy)(object)value).Handler=fn;return value;}
}
sealed class Fixture
{
 public string Fault="";public bool Required=true;
 public List<UserAccountData> Stored=new();public Dictionary<UUID,InventoryItemBase> Items=new();
 readonly Dictionary<FolderType,InventoryFolderBase> folders=new();AvatarAppearance appearance;
 public static void Set(object target,string name,object value){for(var t=target.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(f!=null){f.SetValue(target,value);return;}}throw new Exception("Missing field "+name);}
 public UserAccount Create(Action<UserAccount> complete=null)
 {
  var service=(UserAccountService)RuntimeHelpers.GetUninitializedObject(typeof(UserAccountService));
  Set(service,"m_CreateDefaultAvatarEntries",Required);
  Set(service,"m_Database",Proxy.For<IUserAccountData>((m,a)=>{
   if(m.Name=="Get")return Array.Empty<UserAccountData>();
   if(m.Name=="Store"){
    var s=(UserAccountData)a[0];if(Fault=="activation"&&s.Data["UserLevel"]=="0")return false;
    Stored.Add(new UserAccountData{PrincipalID=s.PrincipalID,FirstName=s.FirstName,LastName=s.LastName,ScopeID=s.ScopeID,Data=new(s.Data)});return true;
   }throw new Exception("Unexpected account operation "+m.Name);
  }));
  Set(service,"m_AuthenticationService",Proxy.For<IAuthenticationService>((m,a)=>m.Name=="SetPassword"?Fault!="auth":throw new Exception("Unexpected auth call")));
  Set(service,"m_InventoryService",Proxy.For<IInventoryService>((m,a)=>{
   var owner=a[0] is UUID id?id:((InventoryItemBase)a[0]).Owner;
   switch(m.Name){
    case "CreateUserInventory":if(Fault=="exception")throw new IOException("Injected store failure");return Fault!="inventory";
    case "GetRootFolder":return Fault=="root"?null:new InventoryFolderBase(UUID.Random(),"Root",owner,(short)FolderType.Root,UUID.Zero,1);
    case "GetFolderForType":var type=(FolderType)a[1];if(!folders.TryGetValue(type,out var folder))folders[type]=folder=new InventoryFolderBase(UUID.Random(),type.ToString(),owner,(short)type,UUID.Zero,1);return folder;
    case "AddItem":var item=(InventoryItemBase)a[0];if(Fault=="wearable"&&item.AssetType!=(int)AssetType.Link||Fault=="link"&&item.AssetType==(int)AssetType.Link)return false;Items[item.ID]=item;return true;
    case "GetFolderContent":return new InventoryCollection{Items=Fault=="cof"?new():Items.Values.Where(i=>i.Folder==(UUID)a[1]).ToList(),Folders=new()};
    case "GetItem":if(!Items.TryGetValue((UUID)a[1],out var worn))return null;if(Fault=="owner")worn.Owner=UUID.Random();if(Fault=="binding")worn.AssetID=UUID.Random();return worn;
    default:throw new Exception("Unexpected inventory operation "+m.Name);
   }
  }));
  Set(service,"m_AvatarService",Proxy.For<IAvatarService>((m,a)=>{
   if(m.Name=="SetAppearance"){if(Fault=="appearance")return false;appearance=(AvatarAppearance)a[1];return true;}
   if(m.Name=="GetAppearance")return appearance;throw new Exception("Unexpected appearance operation");
  }));
  Set(service,"m_AssetService",Proxy.For<IAssetService>((m,a)=>{
   if(Fault=="asset")return null;var id=new UUID((string)a[0]);var type=id==AvatarWearable.DEFAULT_SHIRT_ASSET||id==AvatarWearable.DEFAULT_PANTS_ASSET?AssetType.Clothing:AssetType.Bodypart;
   return new AssetBase(id,"Fixture",(sbyte)type,UUID.ZeroString){Data=[1]};
  }));
  return complete==null?service.CreateUser(UUID.Zero,UUID.Random(),"Fixture","Resident","synthetic-test-only",""):
   service.CreateUserWithProvisioning(UUID.Zero,UUID.Random(),"Fixture","Resident","synthetic-test-only","",complete);
 }
}
public class ColumnRow { public Dictionary<string,string> Data; }
sealed class SchemaReader:IDataReader
{
 readonly Action before;public SchemaReader(Action before){this.before=before;}
 public DataTable GetSchemaTable(){before?.Invoke();var t=new DataTable();t.Columns.Add("ColumnName",typeof(string));foreach(var name in new[]{"Created","UserLevel","ServiceURLs"})t.Rows.Add(name);return t;}
 public object this[int i]=>throw new NotSupportedException();public object this[string n]=>throw new NotSupportedException();public int Depth=>0;public bool IsClosed=>false;public int RecordsAffected=>0;public int FieldCount=>0;public void Close(){}public void Dispose(){}public bool NextResult()=>false;public bool Read()=>false;
 public bool GetBoolean(int i)=>throw new NotSupportedException();public byte GetByte(int i)=>throw new NotSupportedException();public long GetBytes(int i,long o,byte[] b,int bo,int l)=>throw new NotSupportedException();public char GetChar(int i)=>throw new NotSupportedException();public long GetChars(int i,long o,char[] b,int bo,int l)=>throw new NotSupportedException();public IDataReader GetData(int i)=>throw new NotSupportedException();public string GetDataTypeName(int i)=>throw new NotSupportedException();public DateTime GetDateTime(int i)=>throw new NotSupportedException();public decimal GetDecimal(int i)=>throw new NotSupportedException();public double GetDouble(int i)=>throw new NotSupportedException();public Type GetFieldType(int i)=>throw new NotSupportedException();public float GetFloat(int i)=>throw new NotSupportedException();public Guid GetGuid(int i)=>throw new NotSupportedException();public short GetInt16(int i)=>throw new NotSupportedException();public int GetInt32(int i)=>throw new NotSupportedException();public long GetInt64(int i)=>throw new NotSupportedException();public string GetName(int i)=>throw new NotSupportedException();public int GetOrdinal(string s)=>throw new NotSupportedException();public string GetString(int i)=>throw new NotSupportedException();public object GetValue(int i)=>throw new NotSupportedException();public int GetValues(object[] v)=>throw new NotSupportedException();public bool IsDBNull(int i)=>throw new NotSupportedException();
}
