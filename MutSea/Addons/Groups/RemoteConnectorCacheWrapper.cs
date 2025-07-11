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
using System.Reflection;
using System.Threading;

using MutSea.Framework;
//using MutSea.Region.Framework.Interfaces;
using MutSea.Services.Interfaces;

using OpenMetaverse;

namespace MutSea.Groups
{
    public delegate ExtendedGroupRecord GroupRecordDelegate();
    public delegate GroupMembershipData GroupMembershipDelegate();
    public delegate List<GroupMembershipData> GroupMembershipListDelegate();
    public delegate List<ExtendedGroupMembersData> GroupMembersListDelegate();
    public delegate List<GroupRolesData> GroupRolesListDelegate();
    public delegate List<ExtendedGroupRoleMembersData> RoleMembersListDelegate();
    public delegate GroupNoticeInfo NoticeDelegate();
    public delegate List<ExtendedGroupNoticeData> NoticeListDelegate();
    public delegate void VoidDelegate();
    public delegate bool BooleanDelegate();

    public class RemoteConnectorCacheWrapper
    {
        private const int GROUPS_CACHE_TIMEOUT = 1 * 60; // 1 minutes

        private ForeignImporter m_ForeignImporter;
        private HashSet<string> m_ActiveRequests = new HashSet<string>();

        // This all important cache caches objects of different types:
        // group-<GroupID> or group-<Name>          => ExtendedGroupRecord
        // active-<AgentID>                         => GroupMembershipData
        // membership-<AgentID>-<GroupID>           => GroupMembershipData
        // memberships-<AgentID>                    => List<GroupMembershipData>
        // members-<RequestingAgentID>-<GroupID>    => List<ExtendedGroupMembersData>
        // role-<RoleID>                            => GroupRolesData
        // roles-<GroupID>                          => List<GroupRolesData> ; all roles in the group
        // roles-<GroupID>-<AgentID>                => List<GroupRolesData> ; roles that the agent has
        // rolemembers-<RequestingAgentID>-<GroupID> => List<ExtendedGroupRoleMembersData>
        // notice-<noticeID>                        => GroupNoticeInfo
        // notices-<GroupID>                        => List<ExtendedGroupNoticeData>
        private ExpiringCacheOS<string, object> m_Cache = new ExpiringCacheOS<string, object>(30000);

        public RemoteConnectorCacheWrapper(IUserManagement uman)
        {
            m_ForeignImporter = new ForeignImporter(uman);
        }

        public UUID CreateGroup(UUID RequestingAgentID, GroupRecordDelegate d)
        {
            //m_log.DebugFormat("[Groups.RemoteConnector]: Creating group {0}", name);
            //reason = string.Empty;

            //ExtendedGroupRecord group = m_GroupsService.CreateGroup(RequestingAgentID.ToString(), name, charter, showInList, insigniaID,
            //    membershipFee, openEnrollment, allowPublish, maturePublish, founderID, out reason);
            ExtendedGroupRecord group = d();

            if (group == null)
                return UUID.Zero;

            if (!group.GroupID.IsZero())
            {
                m_Cache.AddOrUpdate("group-" + group.GroupID.ToString(), group, GROUPS_CACHE_TIMEOUT);
                m_Cache.Remove("memberships-" + RequestingAgentID.ToString());
            }
            return group.GroupID;
        }

        public bool UpdateGroup(UUID groupID, GroupRecordDelegate d)
        {
            //reason = string.Empty;
            //ExtendedGroupRecord group = m_GroupsService.UpdateGroup(RequestingAgentID, groupID, charter, showInList, insigniaID, membershipFee, openEnrollment, allowPublish, maturePublish);
            ExtendedGroupRecord group = d();

            if (!group.GroupID.IsZero())
                m_Cache.AddOrUpdate("group-" + group.GroupID.ToString(), group, GROUPS_CACHE_TIMEOUT);

            return true;
        }

        public ExtendedGroupRecord GetGroupRecord(string RequestingAgentID, UUID GroupID, string GroupName, GroupRecordDelegate d)
        {
            //if (GroupID.IsZero() && string.IsNullOrEmpty(GroupName))
            //    return null;

            object group = null;
            bool firstCall = false;
            string cacheKey = "group-";
            if (GroupID.IsZero())
                cacheKey += GroupName;
            else
                cacheKey += GroupID.ToString();

            //m_log.DebugFormat("[XXX]: GetGroupRecord {0}", cacheKey);

            while (true)
            {
                lock (m_Cache)
                {
                    if (m_Cache.TryGetValue(cacheKey, out group))
                    {
                        //m_log.DebugFormat("[XXX]: GetGroupRecord {0} cached!", cacheKey);
                        return (ExtendedGroupRecord)group;
                    }

                    // not cached
                    if (!m_ActiveRequests.Contains(cacheKey))
                    {
                        m_ActiveRequests.Add(cacheKey);
                        firstCall = true;
                    }
                }

                if (firstCall)
                {
                    try
                    {
                        //group = m_GroupsService.GetGroupRecord(RequestingAgentID, GroupID, GroupName);
                        group = d();

                        lock (m_Cache)
                        {
                            m_Cache.AddOrUpdate(cacheKey, group, GROUPS_CACHE_TIMEOUT);
                            return (ExtendedGroupRecord)group;
                        }
                    }
                    finally
                    {
                        m_ActiveRequests.Remove(cacheKey);
                    }
                }
                else
                    Thread.Sleep(50);
            }
        }

        public bool AddAgentToGroup(string RequestingAgentID, string AgentID, UUID GroupID, GroupMembershipDelegate d)
        {
            GroupMembershipData membership = d();
            if (membership == null)
                return false;

            lock (m_Cache)
            {
                // first, remove everything! add a user is a heavy-duty op
                m_Cache.Clear();

                m_Cache.AddOrUpdate("active-" + AgentID.ToString(), membership, GROUPS_CACHE_TIMEOUT);
                m_Cache.AddOrUpdate("membership-" + AgentID.ToString() + "-" + GroupID.ToString(), membership, GROUPS_CACHE_TIMEOUT);
            }
            return true;
        }

        public void RemoveAgentFromGroup(string RequestingAgentID, string AgentID, UUID GroupID, VoidDelegate d)
        {
            d();

            string AgentIDToString = AgentID.ToString();
            string cacheKey = "active-" + AgentIDToString;
            m_Cache.Remove(cacheKey);

            cacheKey = "memberships-" + AgentIDToString;
            m_Cache.Remove(cacheKey);

            string GroupIDToString = GroupID.ToString();
            cacheKey = "membership-" + AgentIDToString + "-" + GroupIDToString;
            m_Cache.Remove(cacheKey);

            cacheKey = "members-" + RequestingAgentID.ToString() + "-" + GroupIDToString;
            m_Cache.Remove(cacheKey);

            cacheKey = "roles-" + "-" + GroupIDToString + "-" + AgentIDToString;
            m_Cache.Remove(cacheKey);
        }

        public void SetAgentActiveGroup(string AgentID, GroupMembershipDelegate d)
        {
            GroupMembershipData activeGroup = d();
            string cacheKey = "active-" + AgentID.ToString();
            m_Cache.AddOrUpdate(cacheKey, activeGroup, GROUPS_CACHE_TIMEOUT);
        }

        public ExtendedGroupMembershipData GetAgentActiveMembership(string AgentID, GroupMembershipDelegate d)
        {
            object membership = null;
            bool firstCall = false;
            string cacheKey = "active-" + AgentID.ToString();

            //m_log.DebugFormat("[XXX]: GetAgentActiveMembership {0}", cacheKey);

            while (true)
            {
                lock (m_Cache)
                {
                    if (m_Cache.TryGetValue(cacheKey, out membership))
                    {
                        //m_log.DebugFormat("[XXX]: GetAgentActiveMembership {0} cached!", cacheKey);
                        return (ExtendedGroupMembershipData)membership;
                    }

                    // not cached
                    if (!m_ActiveRequests.Contains(cacheKey))
                    {
                       m_ActiveRequests.Add(cacheKey);
                        firstCall = true;
                    }
                }

                if (firstCall)
                {
                    try
                    {
                        membership = d();
                        m_Cache.AddOrUpdate(cacheKey, membership, GROUPS_CACHE_TIMEOUT);
                        return (ExtendedGroupMembershipData)membership;
                    }
                    finally
                    {
                        m_ActiveRequests.Remove(cacheKey);
                    }
                }
                else
                    Thread.Sleep(50);
            }

        }

        public ExtendedGroupMembershipData GetAgentGroupMembership(string AgentID, UUID GroupID, GroupMembershipDelegate d)
        {
            object membership = null;
            bool firstCall = false;
            string cacheKey = "membership-" + AgentID.ToString() + "-" + GroupID.ToString();

            //m_log.DebugFormat("[XXX]: GetAgentGroupMembership {0}", cacheKey);

            while (true)
            {
                lock (m_Cache)
                {
                    if (m_Cache.TryGetValue(cacheKey, out membership))
                    {
                        //m_log.DebugFormat("[XXX]: GetAgentGroupMembership {0}", cacheKey);
                        return (ExtendedGroupMembershipData)membership;
                    }

                    // not cached
                    if (!m_ActiveRequests.Contains(cacheKey))
                    {
                       m_ActiveRequests.Add(cacheKey);
                        firstCall = true;
                    }
                }

                if (firstCall)
                {
                    try
                    {
                        membership = d();
                        m_Cache.AddOrUpdate(cacheKey, membership, GROUPS_CACHE_TIMEOUT);
                        return (ExtendedGroupMembershipData)membership;
                    }
                    finally
                    {
                        m_ActiveRequests.Remove(cacheKey);
                    }
                }
                else
                    Thread.Sleep(50);
            }
        }

        public List<GroupMembershipData> GetAgentGroupMemberships(string AgentID, GroupMembershipListDelegate d)
        {
            object memberships = null;
            bool firstCall = false;
            string cacheKey = "memberships-" + AgentID.ToString();

            //m_log.DebugFormat("[XXX]: GetAgentGroupMemberships {0}", cacheKey);

            while (true)
            {
                lock (m_Cache)
                {
                    if (m_Cache.TryGetValue(cacheKey, out memberships))
                    {
                        //m_log.DebugFormat("[XXX]: GetAgentGroupMemberships {0} cached!", cacheKey);
                        return (List<GroupMembershipData>)memberships;
                    }

                    // not cached
                    if (!m_ActiveRequests.Contains(cacheKey))
                    {
                       m_ActiveRequests.Add(cacheKey);
                        firstCall = true;
                    }
                }

                if (firstCall)
                {
                    try
                    {
                        memberships = d();
                        m_Cache.AddOrUpdate(cacheKey, memberships, GROUPS_CACHE_TIMEOUT);
                        return (List<GroupMembershipData>)memberships;
                    }
                    finally
                    {
                        m_ActiveRequests.Remove(cacheKey);
                    }
                }
                else
                    Thread.Sleep(50);
            }
        }

        public List<GroupMembersData> GetGroupMembers(string RequestingAgentID, UUID GroupID, GroupMembersListDelegate d)
        {
            object members = null;
            bool firstCall = false;
            // we need to key in also on the requester, because different ppl have different view privileges
            string cacheKey = "members-" + RequestingAgentID.ToString() + "-" + GroupID.ToString();

            //m_log.DebugFormat("[XXX]: GetGroupMembers {0}", cacheKey);

            while (true)
            {
                lock (m_Cache)
                {
                    if (m_Cache.TryGetValue(cacheKey, out members))
                    {
                        List<ExtendedGroupMembersData> xx = (List<ExtendedGroupMembersData>)members;
                        return xx.ConvertAll<GroupMembersData>(new Converter<ExtendedGroupMembersData, GroupMembersData>(m_ForeignImporter.ConvertGroupMembersData));
                    }

                    // not cached
                    if (!m_ActiveRequests.Contains(cacheKey))
                    {
                       m_ActiveRequests.Add(cacheKey);
                        firstCall = true;
                    }
                }

                if (firstCall)
                {
                    try
                    {
                        List<ExtendedGroupMembersData> _members = d();

                        if (_members != null && _members.Count > 0)
                            members = _members.ConvertAll<GroupMembersData>(new Converter<ExtendedGroupMembersData, GroupMembersData>(m_ForeignImporter.ConvertGroupMembersData));
                        else
                            members = new List<GroupMembersData>();

                        m_Cache.AddOrUpdate(cacheKey, _members, GROUPS_CACHE_TIMEOUT);
                        return (List<GroupMembersData>)members;
                    }
                    finally
                    {
                        m_ActiveRequests.Remove(cacheKey);
                    }
                }
                else
                    Thread.Sleep(50);
            }
        }

        public bool AddGroupRole(UUID groupID, UUID roleID, string description, string name, ulong powers, string title, BooleanDelegate d)
        {
            if (d())
            {
                GroupRolesData role = new GroupRolesData();
                role.Description = description;
                role.Members = 0;
                role.Name = name;
                role.Powers = powers;
                role.RoleID = roleID;
                role.Title = title;

                m_Cache.AddOrUpdate("role-" + roleID.ToString(), role, GROUPS_CACHE_TIMEOUT);

                    // also remove this list
                m_Cache.Remove("roles-" + groupID.ToString());
                return true;
            }

            return false;
        }

        public bool UpdateGroupRole(UUID groupID, UUID roleID, string name, string description, string title, ulong powers, BooleanDelegate d)
        {
            if (d())
            {
                object role;
                lock (m_Cache)
                    if (m_Cache.TryGetValue("role-" + roleID.ToString(), out role))
                    {
                        GroupRolesData r = (GroupRolesData)role;
                        r.Description = description;
                        r.Name = name;
                        r.Powers = powers;
                        r.Title = title;

                        m_Cache.AddOrUpdate("role-" + roleID.ToString(), r, GROUPS_CACHE_TIMEOUT);
                    }
                return true;
            }
            else
            {
                m_Cache.Remove("role-" + roleID.ToString());
                // also remove these lists, because they will have an outdated role
                m_Cache.Remove("roles-" + groupID.ToString());

                return false;
            }
        }

        public void RemoveGroupRole(string RequestingAgentID, UUID groupID, UUID roleID, VoidDelegate d)
        {
            d();

            lock (m_Cache)
            {
                m_Cache.Remove("role-" + roleID.ToString());
                // also remove the list, because it will have an removed role
                m_Cache.Remove("roles-" + groupID.ToString());
                m_Cache.Remove("roles-" + groupID.ToString() + "-" + RequestingAgentID.ToString());
                m_Cache.Remove("rolemembers-" + RequestingAgentID.ToString() + "-" + groupID.ToString());
            }
        }

        public List<GroupRolesData> GetGroupRoles(string RequestingAgentID, UUID GroupID, GroupRolesListDelegate d)
        {
            object roles = null;
            bool firstCall = false;
            string cacheKey = "roles-" + GroupID.ToString();

            while (true)
            {
                lock (m_Cache)
                {
                    if (m_Cache.TryGetValue(cacheKey, out roles))
                        return (List<GroupRolesData>)roles;

                    // not cached
                    if (!m_ActiveRequests.Contains(cacheKey))
                    {
                       m_ActiveRequests.Add(cacheKey);
                        firstCall = true;
                    }
                }

                if (firstCall)
                {
                    try
                    {
                        roles = d();
                        if (roles != null)
                        {
                            m_Cache.AddOrUpdate(cacheKey, roles, GROUPS_CACHE_TIMEOUT);
                            return (List<GroupRolesData>)roles;
                        }
                    }
                    finally
                    {
                        m_ActiveRequests.Remove(cacheKey);
                    }
                }
                else
                    Thread.Sleep(50);
            }
        }

        public List<GroupRoleMembersData> GetGroupRoleMembers(string RequestingAgentID, UUID GroupID, RoleMembersListDelegate d)
        {
            object rmembers = null;
            bool firstCall = false;
            // we need to key in also on the requester, because different ppl have different view privileges
            string cacheKey = "rolemembers-" + RequestingAgentID.ToString() + "-" + GroupID.ToString();

            //m_log.DebugFormat("[XXX]: GetGroupRoleMembers {0}", cacheKey);
            while (true)
            {
                lock (m_Cache)
                {
                    if (m_Cache.TryGetValue(cacheKey, out rmembers))
                    {
                        List<ExtendedGroupRoleMembersData> xx = (List<ExtendedGroupRoleMembersData>)rmembers;
                        return xx.ConvertAll<GroupRoleMembersData>(m_ForeignImporter.ConvertGroupRoleMembersData);
                    }

                    // not cached
                    if (!m_ActiveRequests.Contains(cacheKey))
                    {
                       m_ActiveRequests.Add(cacheKey);
                        firstCall = true;
                    }
                }

                if (firstCall)
                {
                    try
                    {
                        List<ExtendedGroupRoleMembersData> _rmembers = d();

                        if (_rmembers != null && _rmembers.Count > 0)
                            rmembers = _rmembers.ConvertAll<GroupRoleMembersData>(new Converter<ExtendedGroupRoleMembersData, GroupRoleMembersData>(m_ForeignImporter.ConvertGroupRoleMembersData));
                        else
                            rmembers = new List<GroupRoleMembersData>();

                        // For some strange reason, when I cache the list of GroupRoleMembersData,
                        // it gets emptied out. The TryGet gets an empty list...
                        //m_Cache.AddOrUpdate(cacheKey, rmembers, GROUPS_CACHE_TIMEOUT);
                        // Caching the list of ExtendedGroupRoleMembersData doesn't show that issue
                        // I don't get it.
                        m_Cache.AddOrUpdate(cacheKey, _rmembers, GROUPS_CACHE_TIMEOUT);
                        return (List<GroupRoleMembersData>)rmembers;
                    }
                    finally
                    {
                        m_ActiveRequests.Remove(cacheKey);
                    }
                }
                else
                    Thread.Sleep(50);
            }
        }

        public void AddAgentToGroupRole(string RequestingAgentID, string AgentID, UUID GroupID, UUID RoleID, BooleanDelegate d)
        {
            if (d())
            {
                lock (m_Cache)
                {
                    // update the cached role
                    string cacheKey = "role-" + RoleID.ToString();
                    object obj;
                    if (m_Cache.TryGetValue(cacheKey, out obj))
                    {
                        GroupRolesData r = (GroupRolesData)obj;
                        r.Members++;
                    }

                    // add this agent to the list of role members
                    cacheKey = "rolemembers-" + RequestingAgentID.ToString() + "-" + GroupID.ToString();
                    if (m_Cache.TryGetValue(cacheKey, out obj))
                    {
                        try
                        {
                            // This may throw an exception, in which case the agentID is not a UUID but a full ID
                            // In that case, let's just remove the whoe things from the cache
                            UUID id = new UUID(AgentID);
                            List<ExtendedGroupRoleMembersData> xx = (List<ExtendedGroupRoleMembersData>)obj;
                            List<GroupRoleMembersData> rmlist = xx.ConvertAll<GroupRoleMembersData>(m_ForeignImporter.ConvertGroupRoleMembersData);
                            GroupRoleMembersData rm = new GroupRoleMembersData();
                            rm.MemberID = id;
                            rm.RoleID = RoleID;
                            rmlist.Add(rm);
                        }
                        catch
                        {
                            m_Cache.Remove(cacheKey);
                        }
                    }

                    // Remove the cached info about this agent's roles
                    // because we don't have enough local info about the new role
                    cacheKey = "roles-" + GroupID.ToString() + "-" + AgentID.ToString();
                    if (m_Cache.Contains(cacheKey))
                        m_Cache.Remove(cacheKey);

                }
            }
        }

        public void RemoveAgentFromGroupRole(string RequestingAgentID, string AgentID, UUID GroupID, UUID RoleID, BooleanDelegate d)
        {
            if (d())
            {
                lock (m_Cache)
                {
                    // update the cached role
                    string cacheKey = "role-" + RoleID.ToString();
                    object obj;
                    if (m_Cache.TryGetValue(cacheKey, out obj))
                    {
                        GroupRolesData r = (GroupRolesData)obj;
                        r.Members--;
                    }

                    cacheKey = "roles-" + GroupID.ToString() + "-" + AgentID.ToString();
                     m_Cache.Remove(cacheKey);

                    cacheKey = "rolemembers-" + RequestingAgentID.ToString() + "-" + GroupID.ToString();
                    m_Cache.Remove(cacheKey);
                }
            }
        }

        public List<GroupRolesData> GetAgentGroupRoles(string RequestingAgentID, string AgentID, UUID GroupID, GroupRolesListDelegate d)
        {
            object roles = null;
            bool firstCall = false;
            string cacheKey = "roles-" + GroupID.ToString() + "-" + AgentID.ToString();

            //m_log.DebugFormat("[XXX]: GetAgentGroupRoles {0}", cacheKey);

            while (true)
            {
                lock (m_Cache)
                {
                    if (m_Cache.TryGetValue(cacheKey, out roles))
                    {
                        //m_log.DebugFormat("[XXX]: GetAgentGroupRoles {0} cached!", cacheKey);
                        return (List<GroupRolesData>)roles;
                    }

                    // not cached
                    if (!m_ActiveRequests.Contains(cacheKey))
                    {
                       m_ActiveRequests.Add(cacheKey);
                        firstCall = true;
                    }
                }

                if (firstCall)
                {
                    try
                    {
                        roles = d();
                        m_Cache.AddOrUpdate(cacheKey, roles, GROUPS_CACHE_TIMEOUT);
                        return (List<GroupRolesData>)roles;
                    }
                    finally
                    {
                        m_ActiveRequests.Remove(cacheKey);
                    }
                }
                else
                    Thread.Sleep(50);
            }
        }

        public void SetAgentActiveGroupRole(string AgentID, UUID GroupID, VoidDelegate d)
        {
            d();

            lock (m_Cache)
            {
                // Invalidate cached info, because it has ActiveRoleID and Powers
                string cacheKey = "membership-" + AgentID.ToString() + "-" + GroupID.ToString();
                m_Cache.Remove(cacheKey);

                cacheKey = "memberships-" + AgentID.ToString();
                m_Cache.Remove(cacheKey);
            }
        }

        public void UpdateMembership(string AgentID, UUID GroupID, bool AcceptNotices, bool ListInProfile, VoidDelegate d)
        {
            d();

            lock (m_Cache)
            {
                string cacheKey = "membership-" + AgentID.ToString() + "-" + GroupID.ToString();
                if (m_Cache.Contains(cacheKey))
                    m_Cache.Remove(cacheKey);

                cacheKey = "memberships-" + AgentID.ToString();
                m_Cache.Remove(cacheKey);

                cacheKey = "active-" + AgentID.ToString();
                object m = null;
                if (m_Cache.TryGetValue(cacheKey, out m))
                {
                    GroupMembershipData membership = (GroupMembershipData)m;
                    membership.ListInProfile = ListInProfile;
                    membership.AcceptNotices = AcceptNotices;
                }
            }
        }

        public bool AddGroupNotice(UUID groupID, UUID noticeID, GroupNoticeInfo notice, BooleanDelegate d)
        {
            if (d())
            {
                m_Cache.AddOrUpdate("notice-" + noticeID.ToString(), notice, GROUPS_CACHE_TIMEOUT);
                string cacheKey = "notices-" + groupID.ToString();
                m_Cache.Remove(cacheKey);

                return true;
            }

            return false;
        }

        public GroupNoticeInfo GetGroupNotice(UUID noticeID, NoticeDelegate d)
        {
            object notice = null;
            bool firstCall = false;
            string cacheKey = "notice-" + noticeID.ToString();

            //m_log.DebugFormat("[XXX]: GetAgentGroupRoles {0}", cacheKey);

            while (true)
            {
                lock (m_Cache)
                {
                    if (m_Cache.TryGetValue(cacheKey, out notice))
                    {
                        return (GroupNoticeInfo)notice;
                    }

                    // not cached
                    if (!m_ActiveRequests.Contains(cacheKey))
                    {
                       m_ActiveRequests.Add(cacheKey);
                        firstCall = true;
                    }
                }

                if (firstCall)
                {
                    try
                    {
                        GroupNoticeInfo _notice = d();

                        m_Cache.AddOrUpdate(cacheKey, _notice, GROUPS_CACHE_TIMEOUT);
                        return _notice;
                    }
                    finally
                    {
                        m_ActiveRequests.Remove(cacheKey);
                    }
                }
                else
                    Thread.Sleep(50);
            }
        }

        public List<ExtendedGroupNoticeData> GetGroupNotices(UUID GroupID, NoticeListDelegate d)
        {
            object notices = null;
            bool firstCall = false;
            string cacheKey = "notices-" + GroupID.ToString();

            //m_log.DebugFormat("[XXX]: GetGroupNotices {0}", cacheKey);

            while (true)
            {
                lock (m_Cache)
                {
                    if (m_Cache.TryGetValue(cacheKey, out notices))
                    {
                        //m_log.DebugFormat("[XXX]: GetGroupNotices {0} cached!", cacheKey);
                        return (List<ExtendedGroupNoticeData>)notices;
                    }

                    // not cached
                    if (!m_ActiveRequests.Contains(cacheKey))
                    {
                       m_ActiveRequests.Add(cacheKey);
                        firstCall = true;
                    }
                }

                if (firstCall)
                {
                    try
                    {
                        notices = d();

                        m_Cache.AddOrUpdate(cacheKey, notices, GROUPS_CACHE_TIMEOUT);
                        return (List<ExtendedGroupNoticeData>)notices;
                    }
                    finally
                    {
                        m_ActiveRequests.Remove(cacheKey);
                    }
                }
                else
                    Thread.Sleep(50);
            }
        }
    }
}