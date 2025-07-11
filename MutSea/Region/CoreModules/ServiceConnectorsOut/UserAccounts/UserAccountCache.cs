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
using System.Reflection;
using System.Collections.Generic;
using MutSea.Framework;
using MutSea.Services.Interfaces;
using OpenMetaverse;
using log4net;

namespace MutSea.Region.CoreModules.ServiceConnectorsOut.UserAccounts
{
    public class UserAccountCache : IUserAccountCacheModule
    {
        private const int CACHE_ALIEN_EXPIRATION_SECONDS = 172800; // 48 hours
        private const int CACHE_EXPIRATION_SECONDS = 3600; // 1 hour!
        private const int CACHE_NULL_EXPIRATION_SECONDS = 600; // 10minutes

        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        //5min expire checks
        private ExpiringCacheOS<UUID, UserAccount> m_UUIDCache = new ExpiringCacheOS<UUID, UserAccount>(300000);
        private ExpiringCacheOS<string, UserAccount> m_NameCache = new ExpiringCacheOS<string, UserAccount>(300000);
        private readonly object accessLock = new object();

        public UserAccountCache()
        {
        }

        ~UserAccountCache()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private bool disposed;
        private void Dispose(bool disposing)
        {
            if (!disposed)
            {
                disposed = true;
                m_UUIDCache.Dispose();
                m_NameCache.Dispose();
                m_UUIDCache = null;
                m_NameCache = null;
            }
        }

        public void Cache(UUID userID, UserAccount account)
        {
            // Cache even null accounts
            lock(accessLock)
            {
                if (account == null)
                    m_UUIDCache.AddOrUpdate(userID, null, CACHE_NULL_EXPIRATION_SECONDS);
                else if(account.LocalToGrid)
                {
                    m_UUIDCache.AddOrUpdate(userID, account, CACHE_EXPIRATION_SECONDS);
                    m_NameCache.AddOrUpdate(account.Name.ToLowerInvariant(), account, CACHE_EXPIRATION_SECONDS);
                }
                else
                {
                    m_UUIDCache.AddOrUpdate(userID, account, CACHE_ALIEN_EXPIRATION_SECONDS);
                    m_NameCache.AddOrUpdate(account.Name.ToLowerInvariant(), account, CACHE_ALIEN_EXPIRATION_SECONDS);
                }
            //m_log.DebugFormat("[USER CACHE]: cached user {0}", userID);
            }
        }

        public UserAccount Get(UUID userID, out bool inCache)
        {
            lock(accessLock)
            {
                if (m_UUIDCache.TryGetValue(userID, out UserAccount account))
                {
                    //m_log.DebugFormat("[USER CACHE]: Account {0} {1} found in cache", account.FirstName, account.LastName);
                    inCache = true;
                    return account;
                }
            }
            inCache = false;
            return null;
        }

        public UserAccount Get(string name, out bool inCache)
        {
            lock(accessLock)
            {
                if (m_NameCache.TryGetValue(name.ToLowerInvariant(), out UserAccount account))
                {
                    inCache = true;
                    return account;
                }
            }
            inCache = false;
            return null;
        }

        public void Invalidate(UUID userID)
        {
            Remove(userID); //??
        }

        public void Remove(UUID id)
        {
            lock(accessLock)
            {
                if (m_UUIDCache.TryGetValue(id, out UserAccount account))
                {
                    m_UUIDCache.Remove(id);
                    if (account != null)
                        m_NameCache.Remove(account.Name.ToLowerInvariant());
                }
            }
        }

        public void Remove(string name)
        {
            lock(accessLock)
            {
                if (m_NameCache.TryGetValue(name.ToLowerInvariant(), out UserAccount account))
                {
                    m_NameCache.Remove(name);
                    if (account != null)
                        m_UUIDCache.Remove(account.PrincipalID);
                }
            }
        }
    }
}
