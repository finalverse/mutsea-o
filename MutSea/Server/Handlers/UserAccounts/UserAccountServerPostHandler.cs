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

using Nini.Config;
using log4net;
using System;
using System.Reflection;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using System.Collections.Generic;
using MutSea.Server.Base;
using MutSea.Services.Interfaces;
using MutSea.Services.UserAccountService;
using MutSea.Framework;
using MutSea.Framework.Servers.HttpServer;
using MutSea.Framework.ServiceAuth;
using OpenMetaverse;

namespace MutSea.Server.Handlers.UserAccounts
{
    public class UserAccountServerPostHandler : BaseStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IUserAccountService m_UserAccountService;
        private bool m_AllowCreateUser = false;
        private bool m_AllowSetAccount = false;

        public UserAccountServerPostHandler(IUserAccountService service)
            : this(service, null, null) {}

        public UserAccountServerPostHandler(IUserAccountService service, IConfig config, IServiceAuth auth) :
                base("POST", "/accounts", auth)
        {
            m_UserAccountService = service;

            if (config != null)
            {
                m_AllowCreateUser = config.GetBoolean("AllowCreateUser", m_AllowCreateUser);
                m_AllowSetAccount = config.GetBoolean("AllowSetAccount", m_AllowSetAccount);
            }
        }

        protected override byte[] ProcessRequest(string path, Stream requestData,
                IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            string body;
            using(StreamReader sr = new StreamReader(requestData))
                body = sr.ReadToEnd();
            body = body.Trim();

            // We need to check the authorization header
            //httpRequest.Headers["authorization"] ...

            //m_log.DebugFormat("[XXX]: query String: {0}", body);
            string method = string.Empty;
            try
            {
                Dictionary<string, object> request = ServerUtils.ParseQueryString(body);

                if (!request.ContainsKey("METHOD"))
                    return FailureResult();

                method = request["METHOD"].ToString();

                switch (method)
                {
                    case "createuser":
                        if (m_AllowCreateUser)
                            return CreateUser(request);
                        else
                            return FailureResult();
                    case "getaccount":
                        return GetAccount(request);
                    case "getaccounts":
                        return GetAccounts(request);
                    case "getmultiaccounts":
                        return GetMultiAccounts(request);
                    case "setaccount":
                        if (m_AllowSetAccount)
                            return StoreAccount(request);
                        else
                            return FailureResult();
                }

                m_log.DebugFormat("[USER SERVICE HANDLER]: unknown method request: {0}", method);
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[USER SERVICE HANDLER]: Exception in method {0}: {1}", method, e);
            }

            return FailureResult();
        }

        byte[] GetAccount(Dictionary<string, object> request)
        {
            UserAccount account = null;
            UUID scopeID = UUID.Zero;
            Dictionary<string, object> result = new Dictionary<string, object>();

            object otmp;
            if (request.TryGetValue("ScopeID", out otmp) && !UUID.TryParse(otmp.ToString(), out scopeID))
            {
                result["result"] = "null";
                return ResultToBytes(result);
            }

            if (request.TryGetValue("UserID", out otmp) && otmp != null)
            {
                if (UUID.TryParse(otmp.ToString(), out UUID userID))
                    account = m_UserAccountService.GetUserAccount(scopeID, userID);
            }
            else if (request.TryGetValue("PrincipalID", out otmp) && otmp != null)
            {
                if (UUID.TryParse(otmp.ToString(), out UUID userID))
                    account = m_UserAccountService.GetUserAccount(scopeID, userID);
            }
            else if (request.TryGetValue("Email", out otmp) && otmp != null)
            {
                account = m_UserAccountService.GetUserAccount(scopeID, otmp.ToString());
            }
            else if (request.TryGetValue("FirstName", out object ofn) && ofn != null &&
                request.TryGetValue("LastName", out object oln) && oln != null)
            {
                account = m_UserAccountService.GetUserAccount(scopeID, ofn.ToString(), oln.ToString());
            }

            if (account == null)
            {
                result["result"] = "null";
            }
            else
            {
                result["result"] = account.ToKeyValuePairs();
            }

            return ResultToBytes(result);
        }

        byte[] GetAccounts(Dictionary<string, object> request)
        {
            if (!request.TryGetValue("query", out object oquery) || oquery == null)
                return FailureResult();

            UUID scopeID = UUID.Zero;
            if (request.TryGetValue("ScopeID", out object oscope) && !UUID.TryParse(oscope.ToString(), out scopeID))
                return FailureResult();

            List<UserAccount> accounts = null;
            string query = oquery.ToString().Trim();
            if(!string.IsNullOrEmpty(query))
                accounts = m_UserAccountService.GetUserAccounts(scopeID, query);

            Dictionary<string, object> result = new Dictionary<string, object>();
            if ((accounts == null) || accounts.Count == 0)
            {
                result["result"] = "null";
            }
            else
            {
                int i = 0;
                foreach (UserAccount acc in accounts)
                {
                    Dictionary<string, object> rinfoDict = acc.ToKeyValuePairs();
                    result["account" + i] = rinfoDict;
                    i++;
                }
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[GRID HANDLER]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] GetMultiAccounts(Dictionary<string, object> request)
        {
            UUID scopeID = UUID.Zero;
            if (request.TryGetValue("ScopeID", out object oscope) && !UUID.TryParse(oscope.ToString(), out scopeID))
                return FailureResult();

            if (!request.TryGetValue("IDS", out object oids))
            {
                m_log.DebugFormat("[USER SERVICE HANDLER]: GetMultiAccounts called without required uuids argument");
                return FailureResult();
            }

            List<string> lids = oids as List<string>;
            if (lids == null)
            {
                m_log.DebugFormat("[USER SERVICE HANDLER]: GetMultiAccounts input argument was of unexpected type {0} or null", oids.GetType().ToString());
                return FailureResult();
            }

            List<string> userIDs = new List<string>(lids.Count);
            foreach (string s in lids)
            {
                if(UUID.TryParse(s, out UUID tmpid))
                    userIDs.Add(s);
            }

            List<UserAccount> accounts = null;
            if (userIDs.Count > 0)
                accounts = m_UserAccountService.GetUserAccounts(scopeID, userIDs);

            Dictionary<string, object> result = new Dictionary<string, object>();
            if ((accounts == null) || accounts.Count == 0)
            {
                result["result"] = "null";
            }
            else
            {
                int i = 0;
                foreach (UserAccount acc in accounts)
                {
                    if(acc == null)
                        continue;
                    Dictionary<string, object> rinfoDict = acc.ToKeyValuePairs();
                    result["account" + i] = rinfoDict;
                    i++;
                }
            }

            string xmlString = ServerUtils.BuildXmlResponse(result);

            //m_log.DebugFormat("[GRID HANDLER]: resp string: {0}", xmlString);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }

        byte[] StoreAccount(Dictionary<string, object> request)
        {
            object otmp;
            UUID principalID = UUID.Zero;
            if (request.TryGetValue("PrincipalID", out otmp) && !UUID.TryParse(otmp.ToString(), out principalID) )
                return FailureResult();

            if(principalID.IsZero())
                return FailureResult();

            UUID scopeID = UUID.Zero;
            if (request.TryGetValue("ScopeID", out otmp) && !UUID.TryParse(otmp.ToString(), out scopeID))
                return FailureResult();

            UserAccount existingAccount = m_UserAccountService.GetUserAccount(scopeID, principalID);
            if (existingAccount == null)
                return FailureResult();

            if (request.TryGetValue("FirstName", out otmp))
                existingAccount.FirstName = otmp.ToString();

            if (request.TryGetValue("LastName", out otmp))
                existingAccount.LastName = otmp.ToString();

            if (request.TryGetValue("Email", out otmp))
                existingAccount.Email = otmp.ToString();

            int created = 0;
            if (request.TryGetValue("Created", out otmp) && int.TryParse(otmp.ToString(), out created))
                existingAccount.Created = created;

            int userLevel = 0;
            if (request.TryGetValue("UserLevel", out otmp) && int.TryParse(otmp.ToString(), out userLevel))
                existingAccount.UserLevel = userLevel;

            int userFlags = 0;
            if (request.TryGetValue("UserFlags", out otmp) && int.TryParse(otmp.ToString(), out userFlags))
                existingAccount.UserFlags = userFlags;

            if (request.TryGetValue("UserTitle", out otmp))
                existingAccount.UserTitle = otmp.ToString();

            if (!m_UserAccountService.StoreUserAccount(existingAccount))
            {
                m_log.ErrorFormat(
                    "[USER ACCOUNT SERVER POST HANDLER]: Account store failed for account {0} {1} {2}",
                    existingAccount.FirstName, existingAccount.LastName, existingAccount.PrincipalID);

                return FailureResult();
            }

            Dictionary<string, object> result = new Dictionary<string, object>();
            result["result"] = existingAccount.ToKeyValuePairs();
            return ResultToBytes(result);
        }

        byte[] CreateUser(Dictionary<string, object> request)
        {
            if (!(m_UserAccountService is UserAccountService))
                return FailureResult();

            object otmp;
            if (!request.TryGetValue("FirstName", out otmp) || otmp == null)
                return FailureResult();
            string firstName = otmp.ToString();

            if(!request.TryGetValue("LastName", out otmp) || otmp == null)
                return FailureResult();
            string lastName = otmp.ToString();

            if(!request.TryGetValue("Password", out otmp) || otmp == null)
                return FailureResult();
            string password = otmp.ToString();

            UUID scopeID = UUID.Zero;
            if (request.TryGetValue("ScopeID", out otmp) && !UUID.TryParse(otmp.ToString(), out scopeID))
                return FailureResult();

            UUID principalID = UUID.Random();
            if (request.TryGetValue("PrincipalID", out otmp) && !UUID.TryParse(otmp.ToString(), out principalID))
                return FailureResult();

            string email = "";
            if (request.TryGetValue("Email", out otmp))
                email = otmp.ToString();

            string model = "";
            if (request.TryGetValue("Model", out otmp))
                model = otmp.ToString();

            UserAccount createdUserAccount = ((UserAccountService)m_UserAccountService).CreateUser(
                        scopeID, principalID, firstName, lastName, password, email, model);

            if (createdUserAccount == null)
                return FailureResult();

            Dictionary<string, object> result = new Dictionary<string, object>();
            result["result"] = createdUserAccount.ToKeyValuePairs();
            return ResultToBytes(result);
        }

        /*
        private byte[] SuccessResult()
        {
            XmlDocument doc = new XmlDocument();

            XmlNode xmlnode = doc.CreateNode(XmlNodeType.XmlDeclaration, "", "");
            doc.AppendChild(xmlnode);

            XmlElement rootElement = doc.CreateElement("", "ServerResponse", "");
            doc.AppendChild(rootElement);

            XmlElement result = doc.CreateElement("", "result", "");
            result.AppendChild(doc.CreateTextNode("Success"));

            rootElement.AppendChild(result);

            return Util.DocToBytes(doc);
        }
        */

        private static byte[] ResultFailureBytes = osUTF8.GetASCIIBytes("<?xml version =\"1.0\"?><ServerResponse><result>Failure</result></ServerResponse>");

        private byte[] FailureResult()
        {
            /*
            XmlDocument doc = new XmlDocument();

            XmlNode xmlnode = doc.CreateNode(XmlNodeType.XmlDeclaration, "", "");

            doc.AppendChild(xmlnode);

            XmlElement rootElement = doc.CreateElement("", "ServerResponse", "");

            doc.AppendChild(rootElement);

            XmlElement result = doc.CreateElement("", "result", "");
            result.AppendChild(doc.CreateTextNode("Failure"));

            rootElement.AppendChild(result);

            return Util.DocToBytes(doc);
            */
            return ResultFailureBytes;
        }

        private byte[] ResultToBytes(Dictionary<string, object> result)
        {
            string xmlString = ServerUtils.BuildXmlResponse(result);
            return Util.UTF8NoBomEncoding.GetBytes(xmlString);
        }
    }
}
