﻿/*
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
using System.Collections.Generic;

using OpenMetaverse;

//namespace MutSea.Services.Interfaces
namespace MutSea.Framework
{
    /// <summary>
    /// This maintains the relationship between a UUID and a user name.
    /// </summary>
    public interface IUserManagement
    {
        UserData GetUserData(UUID id);
        string GetUserName(UUID uuid);
        bool GetUserName(UUID uuid, out string FirstName, out string LastName);
        string GetUserHomeURL(UUID uuid);
        string GetUserHomeURL(UUID uuid, out bool failedWeb);
        string GetUserUUI(UUID uuid);
        bool GetUserUUI(UUID userID, out string uui);
        string GetUserServerURL(UUID uuid, string serverType);
        string GetUserServerURL(UUID uuid, string serverType, out bool failedWeb);
        Dictionary<UUID, string> GetUsersNames(string[] ids, UUID scopeID);
        Dictionary<UUID, string> GetKnownUserNames(string[] ids, UUID scopeID);
        List<UserData> GetKnownUsers(string[] ids, UUID scopeID);
        void UserWebFailed(UUID id);

        /// <summary>
        /// Get user ID by the given name.
        /// </summary>
        /// <param name="name"></param>
        /// <returns>UUID.Zero if no user with that name is found or if the name is "Unknown User"</returns>
        UUID GetUserIdByName(string name);

        /// <summary>
        /// Get user ID by the given name.
        /// </summary>
        /// <param name="firstName"></param>
        /// <param name="lastName"></param>
        /// <returns>UUID.Zero if no user with that name is found or if the name is "Unknown User"</returns>
        UUID GetUserIdByName(string firstName, string lastName);


        void AddSystemUser(UUID uuid, string first, string last);
        void AddNPCUser(UUID uuid, string first, string last);
        /// <summary>
        /// Add a creator user.
        /// </summary>
        /// <remarks>
        /// If an account is found for the UUID, then the names in this will be used rather than any information
        /// extracted from creatorData.
        /// </remarks>
        /// <param name="uuid"></param>
        /// <param name="creatorData">The creator data for this user.</param>
        void AddCreatorUser(UUID uuid, string creatorData);

        /// <summary>
        /// Add a user.
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="uuid"></param>
        /// <param name="firstName"></param>
        /// <param name="homeURL"></param>
        void AddUser(UUID uuid, string firstName, string lastName, string homeURL);
        bool RemoveUser(UUID uuid);
        bool IsLocalGridUser(UUID uuid);
    }
}
