/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using OpenMetaverse;

namespace MutSea.Framework.Capabilities
{
    /// <summary>
    /// Borrowed from (a older version of) libsl for now, as their new llsd code doesn't work we our decoding code.
    /// </summary>
    public static class LLSD
    {
        /// <summary>
        ///
        /// </summary>
        public class LLSDParseException : Exception
        {
            public LLSDParseException(string message) : base(message)
            {
            }
        }

        /// <summary>
        ///
        /// </summary>
        public class LLSDSerializeException : Exception
        {
            public LLSDSerializeException(string message) : base(message)
            {
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        public static object LLSDDeserialize(byte[] b)
        {
            using (MemoryStream ms = new MemoryStream(b, false))
            {
                return LLSDDeserialize(ms);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="st"></param>
        /// <returns></returns>
        public static object LLSDDeserialize(Stream st)
        {
            using (XmlTextReader reader = new XmlTextReader(st))
            {
                reader.DtdProcessing = DtdProcessing.Ignore;

                reader.Read();
                SkipWS(reader);

                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "llsd")
                    throw new LLSDParseException("Expected <llsd>");

                reader.Read();
                object ret = LLSDParseOne(reader);
                SkipWS(reader);

                if (reader.NodeType != XmlNodeType.EndElement || reader.LocalName != "llsd")
                    throw new LLSDParseException("Expected </llsd>");

                return ret;
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static byte[] LLSDSerialize(object obj)
        {
            using(StringWriter sw = new StringWriter())
            using(XmlTextWriter writer = new XmlTextWriter(sw))
            {
                writer.Formatting = Formatting.None;

                writer.WriteStartElement(string.Empty, "llsd", string.Empty);
                LLSDWriteOne(writer, obj);
                writer.WriteEndElement();
                writer.Flush();
                return Util.UTF8.GetBytes(sw.ToString());
            }           
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="writer"></param>
        /// <param name="obj"></param>
        public static void LLSDWriteOne(XmlTextWriter writer, object obj)
        {
            if (obj == null)
            {
                writer.WriteStartElement(string.Empty, "undef", string.Empty);
                writer.WriteEndElement();
                return;
            }

            if (obj is string s)
            {
                writer.WriteStartElement(string.Empty, "string", string.Empty);
                writer.WriteString(s);
                writer.WriteEndElement();
            }
            else if (obj is int)
            {
                writer.WriteStartElement(string.Empty, "integer", string.Empty);
                writer.WriteString(obj.ToString());
                writer.WriteEndElement();
            }
            else if (obj is double || obj is float)
            {
                writer.WriteStartElement(string.Empty, "real", string.Empty);
                writer.WriteString(obj.ToString());
                writer.WriteEndElement();
            }
            else if (obj is bool b)
            {
                writer.WriteStartElement(string.Empty, "boolean", string.Empty);
                writer.WriteString(b ? "1" : "0");
                writer.WriteEndElement();
            }
            else if (obj is ulong)
            {
                throw new Exception("ulong in LLSD is currently not implemented, fix me!");
            }
            else if (obj is UUID u)
            {
                writer.WriteStartElement(string.Empty, "uuid", string.Empty);
                writer.WriteString(u.ToString());
                writer.WriteEndElement();
            }
            else if (obj is Hashtable h)
            {
                writer.WriteStartElement(string.Empty, "map", string.Empty);
                foreach (DictionaryEntry de in h)
                {
                    writer.WriteStartElement(string.Empty, "key", string.Empty);
                    writer.WriteString((string)de.Key);
                    writer.WriteEndElement();
                    LLSDWriteOne(writer, de.Value);
                }
                writer.WriteEndElement();
            }
            else if (obj is Dictionary<string,object> dict)
            {
                writer.WriteStartElement(string.Empty, "map", string.Empty);
                foreach (KeyValuePair<string,object> kvp in dict)
                {
                    writer.WriteStartElement(string.Empty, "key", string.Empty);
                    writer.WriteString(kvp.Key);
                    writer.WriteEndElement();
                    LLSDWriteOne(writer, kvp.Value);
                }
                writer.WriteEndElement();
            }
            else if (obj is ArrayList a)
            {
                writer.WriteStartElement(string.Empty, "array", string.Empty);
                foreach (object item in a)
                {
                    LLSDWriteOne(writer, item);
                }
                writer.WriteEndElement();
            }
            else if (obj is List<object> lsto)
            {
                writer.WriteStartElement(string.Empty, "array", string.Empty);
                foreach (object item in lsto)
                {
                    LLSDWriteOne(writer, item);
                }
                writer.WriteEndElement();
            }
            else if (obj is byte[] bytes)
            {
                 writer.WriteStartElement(string.Empty, "binary", string.Empty);

                writer.WriteStartAttribute(String.Empty, "encoding", String.Empty);
                writer.WriteString("base64");
                writer.WriteEndAttribute();

                writer.WriteString(Convert.ToBase64String(bytes));
                writer.WriteEndElement();
            }
            else
            {
                throw new LLSDSerializeException("Unknown type " + obj.GetType().Name);
            }
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        public static object LLSDParseOne(XmlTextReader reader)
        {
            SkipWS(reader);
            if (reader.NodeType != XmlNodeType.Element)
                throw new LLSDParseException("Expected an element");

            string dtype = reader.LocalName;
            object ret = null;

            switch (dtype)
            {
                case "undef":
                    {
                        if (reader.IsEmptyElement)
                        {
                            reader.Read();
                            return null;
                        }

                        reader.Read();
                        SkipWS(reader);
                        ret = null;
                        break;
                    }
                case "boolean":
                    {
                        if (reader.IsEmptyElement)
                        {
                            reader.Read();
                            return false;
                        }

                        reader.Read();
                        string s = reader.ReadString().Trim();

                        if (s == String.Empty || s == "false" || s == "0")
                            ret = false;
                        else if (s == "true" || s == "1")
                            ret = true;
                        else
                            throw new LLSDParseException("Bad boolean value " + s);

                        break;
                    }
                case "integer":
                    {
                        if (reader.IsEmptyElement)
                        {
                            reader.Read();
                            return 0;
                        }

                        reader.Read();
                        ret = Convert.ToInt32(reader.ReadString().Trim());
                        break;
                    }
                case "real":
                    {
                        if (reader.IsEmptyElement)
                        {
                            reader.Read();
                            return 0.0f;
                        }

                        reader.Read();
                        ret = Convert.ToDouble(reader.ReadString().Trim());
                        break;
                    }
                case "uuid":
                    {
                        if (reader.IsEmptyElement)
                        {
                            reader.Read();
                            return UUID.Zero;
                        }

                        reader.Read();
                        ret = new UUID(reader.ReadString().Trim());
                        break;
                    }
                case "string":
                    {
                        if (reader.IsEmptyElement)
                        {
                            reader.Read();
                            return String.Empty;
                        }

                        reader.Read();
                        ret = reader.ReadString();
                        break;
                    }
                case "binary":
                    {
                        if (reader.IsEmptyElement)
                        {
                            reader.Read();
                            return new byte[0];
                        }

                        if (reader.GetAttribute("encoding") != null &&
                            reader.GetAttribute("encoding") != "base64")
                        {
                            throw new LLSDParseException("Unknown encoding: " + reader.GetAttribute("encoding"));
                        }

                        reader.Read();
                        FromBase64Transform b64 = new FromBase64Transform(FromBase64TransformMode.IgnoreWhiteSpaces);
                        byte[] inp = Util.UTF8.GetBytes(reader.ReadString());
                        ret = b64.TransformFinalBlock(inp, 0, inp.Length);
                        break;
                    }
                case "date":
                    {
                        reader.Read();
                        throw new Exception("LLSD TODO: date");
                    }
                case "map":
                    {
                        return LLSDParseMap(reader);
                    }
                case "array":
                    {
                        return LLSDParseArray(reader);
                    }
                default:
                    throw new LLSDParseException("Unknown element <" + dtype + ">");
            }

            if (reader.NodeType != XmlNodeType.EndElement || reader.LocalName != dtype)
            {
                throw new LLSDParseException("Expected </" + dtype + ">");
            }

            reader.Read();
            return ret;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        public static Hashtable LLSDParseMap(XmlTextReader reader)
        {
            Hashtable ret = new Hashtable();

            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "map")
                throw new LLSDParseException("Expected <map>");

            if (reader.IsEmptyElement)
            {
                reader.Read();
                return ret;
            }

            reader.Read();

            while (true)
            {
                SkipWS(reader);
                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "map")
                {
                    reader.Read();
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "key")
                    throw new LLSDParseException("Expected <key>");

                string key = reader.ReadString();

                if (reader.NodeType != XmlNodeType.EndElement || reader.LocalName != "key")
                    throw new LLSDParseException("Expected </key>");

                reader.Read();
                object val = LLSDParseOne(reader);
                ret[key] = val;
            }

            return ret; // TODO
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        public static ArrayList LLSDParseArray(XmlTextReader reader)
        {
            ArrayList ret = new ArrayList();

            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "array")
                throw new LLSDParseException("Expected <array>");

            if (reader.IsEmptyElement)
            {
                reader.Read();
                return ret;
            }

            reader.Read();

            while (true)
            {
                SkipWS(reader);

                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "array")
                {
                    reader.Read();
                    break;
                }

                ret.Insert(ret.Count, LLSDParseOne(reader));
            }

            return ret; // TODO
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="count"></param>
        /// <returns></returns>
        private static string GetSpaces(int count)
        {
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < count; i++) b.Append(' ');
            return b.ToString();
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="indent"></param>
        /// <returns></returns>
        /* 
        public static String LLSDDump(object obj, int indent)
        {
            if (obj == null)
            {
                return GetSpaces(indent) + "- undef\n";
            }
            else if (obj is string)
            {
                return GetSpaces(indent) + "- string \"" + (string) obj + "\"\n";
            }
            else if (obj is int)
            {
                return GetSpaces(indent) + "- integer " + obj.ToString() + "\n";
            }
            else if (obj is double)
            {
                return GetSpaces(indent) + "- float " + obj.ToString() + "\n";
            }
            else if (obj is UUID)
            {
                return GetSpaces(indent) + "- uuid " + ((UUID) obj).ToString() + Environment.NewLine;
            }
            else if (obj is Hashtable)
            {
                StringBuilder ret = new StringBuilder();
                ret.Append(GetSpaces(indent) + "- map" + Environment.NewLine);
                Hashtable map = (Hashtable) obj;

                foreach (string key in map.Keys)
                {
                    ret.Append(GetSpaces(indent + 2) + "- key \"" + key + "\"" + Environment.NewLine);
                    ret.Append(LLSDDump(map[key], indent + 3));
                }

                return ret.ToString();
            }
            else if (obj is ArrayList)
            {
                StringBuilder ret = new StringBuilder();
                ret.Append(GetSpaces(indent) + "- array\n");
                ArrayList list = (ArrayList) obj;

                foreach (object item in list)
                {
                    ret.Append(LLSDDump(item, indent + 2));
                }

                return ret.ToString();
            }
            else if (obj is byte[])
            {
                return GetSpaces(indent) + "- binary\n" + Utils.BytesToHexString((byte[]) obj, GetSpaces(indent)) +
                       Environment.NewLine;
            }
            else
            {
                return GetSpaces(indent) + "- unknown type " + obj.GetType().Name + Environment.NewLine;
            }
        }

        public static object ParseTerseLLSD(string llsd)
        {
            int notused;
            return ParseTerseLLSD(llsd, out notused);
        }

        public static object ParseTerseLLSD(string llsd, out int endPos)
        {
            if (llsd.Length == 0)
            {
                endPos = 0;
                return null;
            }

            // Identify what type of object this is
            switch (llsd[0])
            {
                case '!':
                    throw new LLSDParseException("Undefined value type encountered");
                case '1':
                    endPos = 1;
                    return true;
                case '0':
                    endPos = 1;
                    return false;
                case 'i':
                    {
                        if (llsd.Length < 2) throw new LLSDParseException("Integer value type with no value");
                        int value;
                        endPos = FindEnd(llsd, 1);

                        if (Int32.TryParse(llsd.Substring(1, endPos - 1), out value))
                            return value;
                        else
                            throw new LLSDParseException("Failed to parse integer value type");
                    }
                case 'r':
                    {
                        if (llsd.Length < 2) throw new LLSDParseException("Real value type with no value");
                        double value;
                        endPos = FindEnd(llsd, 1);

                        if (Double.TryParse(llsd.Substring(1, endPos - 1), NumberStyles.Float,
                                            Culture.NumberFormatInfo, out value))
                            return value;
                        else
                            throw new LLSDParseException("Failed to parse double value type");
                    }
                case 'u':
                    {
                        if (llsd.Length < 17) throw new LLSDParseException("UUID value type with no value");
                        UUID value;
                        endPos = FindEnd(llsd, 1);

                        if (UUID.TryParse(llsd.Substring(1, endPos - 1), out value))
                            return value;
                        else
                            throw new LLSDParseException("Failed to parse UUID value type");
                    }
                case 'b':
                    //byte[] value = new byte[llsd.Length - 1];
                    // This isn't the actual binary LLSD format, just the terse format sent
                    // at login so I don't even know if there is a binary type
                    throw new LLSDParseException("Binary value type is unimplemented");
                case 's':
                case 'l':
                    if (llsd.Length < 2) throw new LLSDParseException("String value type with no value");
                    endPos = FindEnd(llsd, 1);
                    return llsd.Substring(1, endPos - 1);
                case 'd':
                    // Never seen one before, don't know what the format is
                    throw new LLSDParseException("Date value type is unimplemented");
                case '[':
                    {
                        if (llsd.IndexOf(']') == -1) throw new LLSDParseException("Invalid array");

                        int pos = 0;
                        ArrayList array = new ArrayList();

                        while (llsd[pos] != ']')
                        {
                            ++pos;

                            // Advance past comma if need be
                            if (llsd[pos] == ',') ++pos;

                            // Allow a single whitespace character
                            if (pos < llsd.Length && llsd[pos] == ' ') ++pos;

                            int end;
                            array.Add(ParseTerseLLSD(llsd.Substring(pos), out end));
                            pos += end;
                        }

                        endPos = pos + 1;
                        return array;
                    }
                case '{':
                    {
                        if (llsd.IndexOf('}') == -1) throw new LLSDParseException("Invalid map");

                        int pos = 0;
                        Hashtable hashtable = new Hashtable();

                        while (llsd[pos] != '}')
                        {
                            ++pos;

                            // Advance past comma if need be
                            if (llsd[pos] == ',') ++pos;

                            // Allow a single whitespace character
                            if (pos < llsd.Length && llsd[pos] == ' ') ++pos;

                            if (llsd[pos] != '\'') throw new LLSDParseException("Expected a map key");
                            int endquote = llsd.IndexOf('\'', pos + 1);
                            if (endquote == -1 || (endquote + 1) >= llsd.Length || llsd[endquote + 1] != ':')
                                throw new LLSDParseException("Invalid map format");
                            string key = llsd.Substring(pos, endquote - pos);
                            key = key.Replace("'", String.Empty);
                            pos += (endquote - pos) + 2;

                            int end;
                            hashtable.Add(key, ParseTerseLLSD(llsd.Substring(pos), out end));
                            pos += end;
                        }

                        endPos = pos + 1;
                        return hashtable;
                    }
                default:
                    throw new Exception("Unknown value type");
            }
        }
*/
        private static int FindEnd(string llsd, int start)
        {
            int end = llsd.IndexOfAny(new char[] {',', ']', '}'});
            if (end == -1) end = llsd.Length - 1;
            return end;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="reader"></param>
        private static void SkipWS(XmlTextReader reader)
        {
            while (
                reader.NodeType == XmlNodeType.Comment ||
                reader.NodeType == XmlNodeType.Whitespace ||
                reader.NodeType == XmlNodeType.SignificantWhitespace ||
                reader.NodeType == XmlNodeType.XmlDeclaration)
            {
                reader.Read();
            }
        }
    }
}
