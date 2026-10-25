// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Finalverse.World;

// RFC 8785 JSON Canonicalization Scheme. A binding version is SHA-256 over these bytes, so
// any JCS implementation (JavaScript's JSON.stringify of sorted objects is one) reproduces it.
public static class CanonicalJson
{
    public static byte[] Bytes(JsonElement value){var b=new StringBuilder();Write(b,value,0);return Encoding.UTF8.GetBytes(b.ToString());}
    public static byte[] Bytes<T>(T value)=>Bytes(JsonSerializer.SerializeToElement(value));
    public static string Sha256<T>(T value)=>ContentCatalog.Hash(Bytes(value));

    static void Write(StringBuilder b,JsonElement e,int depth)
    {
        ContentCatalog.Require(depth<=32,"Canonical JSON is too deep.");
        switch(e.ValueKind)
        {
            case JsonValueKind.Object:
                var members=e.EnumerateObject().ToList();
                members.Sort((x,y)=>string.CompareOrdinal(x.Name,y.Name));
                for(int i=1;i<members.Count;i++)ContentCatalog.Require(members[i].Name!=members[i-1].Name,"Canonical JSON rejects duplicate names.");
                b.Append('{');
                for(int i=0;i<members.Count;i++){if(i>0)b.Append(',');String(b,members[i].Name);b.Append(':');Write(b,members[i].Value,depth+1);}
                b.Append('}');break;
            case JsonValueKind.Array:
                b.Append('[');int n=0;
                foreach(var item in e.EnumerateArray()){if(n++>0)b.Append(',');Write(b,item,depth+1);}
                b.Append(']');break;
            case JsonValueKind.String:String(b,e.GetString()!);break;
            case JsonValueKind.Number:b.Append(Number(e.GetDouble()));break;
            case JsonValueKind.True:b.Append("true");break;
            case JsonValueKind.False:b.Append("false");break;
            case JsonValueKind.Null:b.Append("null");break;
            default:throw new InvalidDataException("Unsupported canonical JSON value.");
        }
    }

    static void String(StringBuilder b,string s)
    {
        b.Append('"');
        foreach(char c in s)
        {
            switch(c)
            {
                case '"':b.Append("\\\"");break; case '\\':b.Append("\\\\");break;
                case '\b':b.Append("\\b");break; case '\f':b.Append("\\f");break; case '\n':b.Append("\\n");break;
                case '\r':b.Append("\\r");break; case '\t':b.Append("\\t");break;
                default: if(c<0x20)b.Append("\\u").Append(((int)c).ToString("x4"));else b.Append(c);break;
            }
        }
        b.Append('"');
    }

    // ECMAScript Number::toString over the shortest round-trip digits.
    public static string Number(double d)
    {
        ContentCatalog.Require(double.IsFinite(d),"Canonical JSON numbers must be finite.");
        if(d==0)return "0";
        string r=Math.Abs(d).ToString("R",CultureInfo.InvariantCulture);
        int e=r.IndexOfAny(['E','e']),exponent=e<0?0:int.Parse(r[(e+1)..],CultureInfo.InvariantCulture);
        string mantissa=e<0?r:r[..e];int dot=mantissa.IndexOf('.');
        string digits=dot<0?mantissa:mantissa[..dot]+mantissa[(dot+1)..];int point=(dot<0?mantissa.Length:dot)+exponent;
        int lead=0;while(lead<digits.Length-1&&digits[lead]=='0')lead++;
        digits=digits[lead..];point-=lead;digits=digits.TrimEnd('0');
        if(digits.Length==0)return "0";
        int k=digits.Length,nn=point;var o=new StringBuilder(d<0?"-":"");
        if(k<=nn&&nn<=21)o.Append(digits).Append('0',nn-k);
        else if(0<nn&&nn<=21)o.Append(digits[..nn]).Append('.').Append(digits[nn..]);
        else if(-6<nn&&nn<=0)o.Append("0.").Append('0',-nn).Append(digits);
        else
        {
            o.Append(digits[0]);if(k>1)o.Append('.').Append(digits[1..]);
            o.Append('e').Append(nn-1>=0?"+":"-").Append(Math.Abs(nn-1).ToString(CultureInfo.InvariantCulture));
        }
        return o.ToString();
    }
}
