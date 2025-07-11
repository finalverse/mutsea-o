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
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using Nini.Config;
using log4net;
using MutSea.Framework;
using MutSea.Framework.Console;
using MutSea.Data;
using MutSea.Server.Base;
using MutSea.Services.Interfaces;
using GridRegion = MutSea.Services.Interfaces.GridRegion;
using OpenMetaverse;

namespace MutSea.Services.GridService
{
    public class GridService : GridServiceBase, IGridService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private string LogHeader = "[GRID SERVICE]";

        private bool m_DeleteOnUnregister = true;
        private static GridService m_RootInstance = null;
        protected IConfigSource m_config;
        protected static HypergridLinker m_HypergridLinker;

        protected IAuthenticationService m_AuthenticationService = null;
        protected bool m_AllowDuplicateNames = false;
        protected bool m_AllowHypergridMapSearch = false;

        private static Dictionary<string,object> m_ExtraFeatures = new Dictionary<string, object>();

        public GridService(IConfigSource config)
            : base(config)
        {
            m_log.DebugFormat("[GRID SERVICE]: Starting...");

            m_config = config;
            IConfig gridConfig = config.Configs["GridService"];

            bool suppressConsoleCommands = false;

            if (gridConfig != null)
            {
                m_DeleteOnUnregister = gridConfig.GetBoolean("DeleteOnUnregister", true);

                string authService = gridConfig.GetString("AuthenticationService", String.Empty);

                if (authService != String.Empty)
                {
                    Object[] args = new Object[] { config };
                    m_AuthenticationService = ServerUtils.LoadPlugin<IAuthenticationService>(authService, args);
                }
                m_AllowDuplicateNames = gridConfig.GetBoolean("AllowDuplicateNames", m_AllowDuplicateNames);
                m_AllowHypergridMapSearch = gridConfig.GetBoolean("AllowHypergridMapSearch", m_AllowHypergridMapSearch);

                // This service is also used locally by a simulator running in grid mode.  This switches prevents
                // inappropriate console commands from being registered
                suppressConsoleCommands = gridConfig.GetBoolean("SuppressConsoleCommands", suppressConsoleCommands);
            }

            if (m_RootInstance == null)
            {
                m_RootInstance = this;

                if (!suppressConsoleCommands && MainConsole.Instance != null)
                {
                    MainConsole.Instance.Commands.AddCommand("Regions", true,
                            "deregister region id",
                            "deregister region id <region-id>+",
                            "Deregister a region manually.",
                            String.Empty,
                            HandleDeregisterRegion);

                    MainConsole.Instance.Commands.AddCommand("Regions", true,
                            "show regions",
                            "show regions",
                            "Show details on all regions",
                            String.Empty,
                            HandleShowRegions);

                    MainConsole.Instance.Commands.AddCommand("Regions", true,
                            "show region name",
                            "show region name <Region name>",
                            "Show details on a region",
                            String.Empty,
                            HandleShowRegion);

                    MainConsole.Instance.Commands.AddCommand("Regions", true,
                            "show region at",
                            "show region at <x-coord> <y-coord>",
                            "Show details on a region at the given co-ordinate.",
                            "For example, show region at 1000 1000",
                            HandleShowRegionAt);

                    MainConsole.Instance.Commands.AddCommand("General", true,
                            "show grid size",
                            "show grid size",
                            "Show the current grid size (excluding hyperlink references)",
                            String.Empty,
                            HandleShowGridSize);

                    MainConsole.Instance.Commands.AddCommand("Regions", true,
                             "set region flags",
                             "set region flags <Region name> <flags>",
                             "Set database flags for region",
                             String.Empty,
                             HandleSetFlags);
                }

                if (!suppressConsoleCommands)
                    SetExtraServiceURLs(config);

                m_HypergridLinker = new HypergridLinker(m_config, this, m_Database);
            }
        }

        private void SetExtraServiceURLs(IConfigSource config)
        {
            IConfig loginConfig = config.Configs["LoginService"];
            IConfig gridConfig = config.Configs["GridService"];

            if (loginConfig == null || gridConfig == null)
                return;

            string configVal;

            configVal = loginConfig.GetString("SearchURL", string.Empty);
            if (!string.IsNullOrEmpty(configVal))
                m_ExtraFeatures["search-server-url"] = configVal;

            configVal = loginConfig.GetString("MapTileURL", string.Empty);
            if (!string.IsNullOrEmpty(configVal))
            {
                // This URL must end with '/', the viewer doesn't check
                configVal = configVal.Trim();
                if (!configVal.EndsWith("/"))
                    configVal = configVal + "/";
                m_ExtraFeatures["map-server-url"] = configVal;
            }

            configVal = loginConfig.GetString("DestinationGuide", string.Empty);
            if (!string.IsNullOrEmpty(configVal))
                m_ExtraFeatures["destination-guide-url"] = configVal;

            configVal = Util.GetConfigVarFromSections<string>(
                    config, "GatekeeperURI", new string[] { "Startup", "Hypergrid" }, string.Empty);
            if (!string.IsNullOrEmpty(configVal))
                m_ExtraFeatures["GridURL"] = configVal;

            configVal = Util.GetConfigVarFromSections<string>(
                config, "GridName", new string[] { "Const", "Hypergrid" }, string.Empty);
            if (string.IsNullOrEmpty(configVal))
                configVal = Util.GetConfigVarFromSections<string>(
                    config, "gridname", new string[] { "GridInfo", "GridInfoService" }, string.Empty);
            if (!string.IsNullOrEmpty(configVal))
                m_ExtraFeatures["GridName"] = configVal;

            configVal = Util.GetConfigVarFromSections<string>(
                config, "GridNick", new string[] { "Const", "Hypergrid" }, string.Empty);
            if (string.IsNullOrEmpty(configVal))
                configVal = Util.GetConfigVarFromSections<string>(
                    config, "gridnick", new string[] { "GridInfo", "GridInfoService" }, string.Empty);
            if (!string.IsNullOrEmpty(configVal))
                m_ExtraFeatures["GridNick"] = configVal;

            configVal = Util.GetConfigVarFromSections<string>(
                config, "GridStatus", new string[] { "GridInfo", "GridInfoService" }, string.Empty);
            if (!string.IsNullOrEmpty(configVal))
                m_ExtraFeatures["GridStatus"] = configVal;

            configVal = Util.GetConfigVarFromSections<string>(
                config, "GridStatusRSS", new string[] { "GridInfo", "GridInfoService" }, string.Empty);
            if (!string.IsNullOrEmpty(configVal))
                m_ExtraFeatures["GridStatusRSS"] = configVal;

            m_ExtraFeatures["ExportSupported"] = gridConfig.GetString("ExportSupported", "true");

            string[] sections = new string[] { "Const, Startup", "Hypergrid", "GatekeeperService" };
            string gatekeeperURIAlias = Util.GetConfigVarFromSections<string>(config, "GatekeeperURIAlias", sections, string.Empty);

            if (!string.IsNullOrWhiteSpace(gatekeeperURIAlias))
            {
                string[] alias = gatekeeperURIAlias.Split(',');
                if(alias.Length > 0)
                {
                    StringBuilder sb = osStringBuilderCache.Acquire();
                    int last = alias.Length -1;
                    for (int i = 0; i < alias.Length; ++i)
                    {
                        OSHHTPHost tmp = new OSHHTPHost(alias[i], false);
                        if (tmp.IsValidHost)
                        {
                            sb.Append(tmp.URI);
                            if(i < last)
                                sb.Append(',');
                        }
                    }
                    if(sb.Length > 0)
                        m_ExtraFeatures["GridURLAlias"] = osStringBuilderCache.GetStringAndRelease(sb);
                    else
                        osStringBuilderCache.Release(sb);
                }
            }
        }

        #region IGridService

        public string RegisterRegion(UUID scopeID, GridRegion regionInfos)
        {
            IConfig gridConfig = m_config.Configs["GridService"];

            if (regionInfos.RegionID.IsZero())
                return "Invalid RegionID - cannot be zero UUID";

            if (regionInfos.RegionLocY <= Constants.MaximumRegionSize)
                return "Region location reserved for HG links coord Y must be higher than " + (Constants.MaximumRegionSize/256).ToString();

            String reason = "Region overlaps another region";

            List<RegionData> rdatas = m_Database.Get(
                        regionInfos.RegionLocX,
                        regionInfos.RegionLocY,
                        regionInfos.RegionLocX + regionInfos.RegionSizeX - 1,
                        regionInfos.RegionLocY + regionInfos.RegionSizeY - 1 ,
                        scopeID);

            RegionData region = null;
            if(rdatas.Count > 1)
            {
                m_log.WarnFormat("{0} Register region overlaps with {1} regions", LogHeader, scopeID, rdatas.Count);
                return reason;
            }
            else if(rdatas.Count == 1)
                region = rdatas[0];

            if ((region != null) && (region.RegionID != regionInfos.RegionID))
            {
                // If not same ID and same coordinates, this new region has conflicts and can't be registered.
                m_log.WarnFormat("{0} Register region conflict in scope {1}. {2}", LogHeader, scopeID, reason);
                return reason;
            }

            if (region != null)
            {
                // There is a preexisting record
                //
                // Get it's flags
                //
                MutSea.Framework.RegionFlags rflags = (MutSea.Framework.RegionFlags)Convert.ToInt32(region.Data["flags"]);

                // Is this a reservation?
                //
                if ((rflags & MutSea.Framework.RegionFlags.Reservation) != 0)
                {
                    // Regions reserved for the null key cannot be taken.
                    if ((string)region.Data["PrincipalID"] == UUID.ZeroString)
                        return "Region location is reserved";

                    // Treat it as an auth request
                    //
                    // NOTE: Fudging the flags value here, so these flags
                    //       should not be used elsewhere. Don't optimize
                    //       this with the later retrieval of the same flags!
                    rflags |= MutSea.Framework.RegionFlags.Authenticate;
                }

                if ((rflags & MutSea.Framework.RegionFlags.Authenticate) != 0)
                {
                    // Can we authenticate at all?
                    //
                    if (m_AuthenticationService == null)
                        return "No authentication possible";

                    if (!m_AuthenticationService.Verify(new UUID(region.Data["PrincipalID"].ToString()), regionInfos.Token, 30))
                        return "Bad authentication";
                }
            }

            // If we get here, the destination is clear. Now for the real check.

            if (!m_AllowDuplicateNames)
            {
                List<RegionData> dupe = m_Database.Get(Util.EscapeForLike(regionInfos.RegionName), scopeID);
                if (dupe != null && dupe.Count > 0)
                {
                    foreach (RegionData d in dupe)
                    {
                        if (d.RegionID != regionInfos.RegionID)
                        {
                            m_log.WarnFormat("[GRID SERVICE]: Region tried to register using a duplicate name. New region: {0} ({1}), existing region: {2} ({3}).",
                                regionInfos.RegionName, regionInfos.RegionID, d.RegionName, d.RegionID);
                            return "Duplicate region name";
                        }
                    }
                }
            }

            // If there is an old record for us, delete it if it is elsewhere.
            region = m_Database.Get(regionInfos.RegionID, scopeID);
            if ((region != null) && (region.RegionID == regionInfos.RegionID) &&
                ((region.posX != regionInfos.RegionLocX) || (region.posY != regionInfos.RegionLocY)))
            {
                if ((Convert.ToInt32(region.Data["flags"]) & (int)MutSea.Framework.RegionFlags.NoMove) != 0)
                    return "Can't move this region";

                if ((Convert.ToInt32(region.Data["flags"]) & (int)MutSea.Framework.RegionFlags.LockedOut) != 0)
                    return "Region locked out";

                // Region reregistering in other coordinates. Delete the old entry
                m_log.DebugFormat("[GRID SERVICE]: Region {0} ({1}) was previously registered at {2}-{3}. Deleting old entry.",
                    regionInfos.RegionName, regionInfos.RegionID, regionInfos.RegionCoordX, regionInfos.RegionCoordY);

                try
                {
                    m_Database.Delete(regionInfos.RegionID);
                }
                catch (Exception e)
                {
                    m_log.DebugFormat("[GRID SERVICE]: Database exception: {0}", e);
                }
            }

            // Everything is ok, let's register
            RegionData rdata = RegionInfo2RegionData(regionInfos);
            rdata.ScopeID = scopeID;

            int regionFlags = 0;
            if (region != null)
            {
                regionFlags = Convert.ToInt32(region.Data["flags"]);
                regionFlags &= ~(int)MutSea.Framework.RegionFlags.Reservation;
            }

            if ((gridConfig != null) && !string.IsNullOrEmpty(rdata.RegionName))
            {
                string regionName = rdata.RegionName.Trim().Replace(' ', '_');
                regionFlags = ParseFlags(regionFlags, gridConfig.GetString("DefaultRegionFlags", string.Empty));
                string byregionname = gridConfig.GetString("Region_" + regionName, string.Empty);
                if(!string.IsNullOrEmpty(byregionname))
                    regionFlags = ParseFlags(regionFlags, byregionname);
                else
                    regionFlags = ParseFlags(regionFlags, gridConfig.GetString("Region_" + rdata.RegionID.ToString(), string.Empty));
            }

            regionFlags |= (int)MutSea.Framework.RegionFlags.RegionOnline;
            rdata.Data["flags"] = regionFlags.ToString();

            try
            {
                rdata.Data["last_seen"] = Util.UnixTimeSinceEpoch();
                m_Database.Store(rdata);
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[GRID SERVICE]: Database exception: {0}", e);
            }

            m_log.DebugFormat
                ("[GRID SERVICE]: Region {0} ({1}, {2}x{3}) registered at {4},{5} with flags {6}",
                regionInfos.RegionName, regionInfos.RegionID, regionInfos.RegionSizeX, regionInfos.RegionSizeY,
                regionInfos.RegionCoordX, regionInfos.RegionCoordY,
                (MutSea.Framework.RegionFlags)regionFlags);

            return string.Empty;
        }

        // String describing name and region location of passed region
        private String RegionString(RegionData reg)
        {
            return String.Format("{0}/{1} at <{2},{3}>",
                reg.RegionName, reg.RegionID, reg.coordX, reg.coordY);
        }

        // String describing name and region location of passed region
        private String RegionString(GridRegion reg)
        {
            return String.Format("{0}/{1} at <{2},{3}>",
                reg.RegionName, reg.RegionID, reg.RegionCoordX, reg.RegionCoordY);
        }

        public bool DeregisterRegion(UUID regionID)
        {
            RegionData region = m_Database.Get(regionID, UUID.Zero);
            if (region == null)
                return false;

            m_log.DebugFormat(
                "[GRID SERVICE]: Deregistering region {0} ({1}) at {2}-{3}",
                region.RegionName, region.RegionID, region.coordX, region.coordY);

            int flags = Convert.ToInt32(region.Data["flags"]);

            if ((!m_DeleteOnUnregister) || ((flags & (int)MutSea.Framework.RegionFlags.Persistent) != 0))
            {
                flags &= ~(int)MutSea.Framework.RegionFlags.RegionOnline;
                region.Data["flags"] = flags.ToString();
                region.Data["last_seen"] = Util.UnixTimeSinceEpoch();
                try
                {
                    m_Database.Store(region);
                }
                catch (Exception e)
                {
                    m_log.DebugFormat("[GRID SERVICE]: Database exception: {0}", e);
                }

                return true;
            }

            return m_Database.Delete(regionID);
        }

        public List<GridRegion> GetNeighbours(UUID scopeID, UUID regionID)
        {
            List<GridRegion> rinfos = new List<GridRegion>();
            RegionData region = m_Database.Get(regionID, scopeID);

            if (region != null)
            {
                List<RegionData> rdatas = m_Database.Get(
                    region.posX - 1, region.posY - 1,
                    region.posX + region.sizeX + 1, region.posY + region.sizeY + 1, scopeID);

                foreach (RegionData rdata in rdatas)
                {
                    if (rdata.RegionID != regionID)
                    {
                        int flags = Convert.ToInt32(rdata.Data["flags"]);
                        if ((flags & (int)Framework.RegionFlags.Hyperlink) == 0) // no hyperlinks as neighbours
                            rinfos.Add(RegionData2RegionInfo(rdata));
                    }
                }

                // string rNames = "";
                // foreach (GridRegion gr in rinfos)
                //     rNames += gr.RegionName + ",";
                // m_log.DebugFormat("{0} region {1} has {2} neighbours ({3})",
                //             LogHeader, region.RegionName, rinfos.Count, rNames);
            }
            else
            {
                m_log.WarnFormat(
                    "[GRID SERVICE]: GetNeighbours() called for scope {0}, region {1} but no such region found",
                    scopeID, regionID);
            }

            return rinfos;
        }

        public GridRegion GetRegionByUUID(UUID scopeID, UUID regionID)
        {
            RegionData rdata = m_Database.Get(regionID, scopeID);
            if (rdata != null)
                return RegionData2RegionInfo(rdata);

            return null;
        }

        public GridRegion GetRegionByHandle(UUID scopeID, ulong regionhandle)
        {
            int x = (int)(regionhandle >> 32);
            int y = (int)(regionhandle & 0xfffffffful);
            return GetRegionByPosition(scopeID, x, y);
        }

        // Get a region given its base coordinates.
        // NOTE: this is NOT 'get a region by some point in the region'. The coordinate MUST
        //     be the base coordinate of the region.
        // The snapping is technically unnecessary but is harmless because regions are always
        //     multiples of the legacy region size (256).

        public GridRegion GetRegionByPosition(UUID scopeID, int x, int y)
        {
            uint regionX = Util.WorldToRegionLoc((uint)x);
            uint regionY = Util.WorldToRegionLoc((uint)y);
            int snapX = (int)Util.RegionToWorldLoc(regionX);
            int snapY = (int)Util.RegionToWorldLoc(regionY);

            RegionData rdata = m_Database.Get(snapX, snapY, scopeID);
            if (rdata != null)
            {
                m_log.DebugFormat("{0} GetRegionByPosition. Found region {1} in database. Pos=<{2},{3}>",
                                 LogHeader, rdata.RegionName, regionX, regionY);
                return RegionData2RegionInfo(rdata);
            }
            else
            {
//                m_log.DebugFormat("{0} GetRegionByPosition. Did not find region in database. Pos=<{1},{2}>",
//                                 LogHeader, regionX, regionY);
                return null;
            }
        }

        public GridRegion GetRegionByName(UUID scopeID, string name)
        {
            var nameURI = new RegionURI(name);
            if (!nameURI.IsValid)
                return null;
            return GetRegionByURI(scopeID, nameURI);
        }

        public GridRegion GetRegionByURI(UUID scopeID, RegionURI uri)
        {
            if (!uri.IsValid)
                return null;

            bool localGrid = true;
            if (uri.HasHost)
            {
                if (!uri.ResolveDNS())
                    return null;
                localGrid = m_HypergridLinker.IsLocalGrid(uri.HostUrl);
                uri.IsLocalGrid = localGrid;
            }

            if (localGrid)
            {
                if (uri.HasRegionName)
                {
                    RegionData rdata = m_Database.GetSpecific(uri.RegionName, scopeID);
                    if (rdata != null)
                        return RegionData2RegionInfo(rdata);
                }
                else
                {
                    List<GridRegion> defregs = GetDefaultRegions(scopeID);
                    if (defregs != null)
                        return defregs[0];
                }
                return null;
            }

            if (!m_AllowHypergridMapSearch)
                return null;

            string mapname = uri.RegionHostPortSpaceName;
            List<RegionData> rdatas = m_Database.Get("%" + Util.EscapeForLike(mapname), scopeID);
            if (rdatas != null && rdatas.Count > 0)
            {
                foreach (RegionData rdata in rdatas)
                {
                    int indx = rdata.RegionName.IndexOf("://");
                    if (indx < 0)
                        continue;
                    string rname = rdata.RegionName.Substring(indx + 3);
                    if (mapname.Equals(rname, StringComparison.InvariantCultureIgnoreCase))
                        return RegionData2RegionInfo(rdata);
                }
            }

            GridRegion r = m_HypergridLinker.LinkRegion(scopeID, uri);
            return r;
        }

        public GridRegion GetLocalRegionByName(UUID scopeID, string name)
        {
            var nameURI = new RegionURI(name);
            return GetLocalRegionByURI(scopeID, nameURI);
        }

        public GridRegion GetLocalRegionByURI(UUID scopeID, RegionURI uri)
        {
            if (!uri.IsValid)
                return null;

            if (uri.HasHost)
            {
                if (!uri.ResolveDNS())
                    return null;
                if(!m_HypergridLinker.IsLocalGrid(uri.HostUrl))
                    return null;
            }

            if (uri.HasRegionName)
            {
                RegionData rdata = m_Database.GetSpecific(uri.RegionName, scopeID);
                if (rdata != null)
                    return RegionData2RegionInfo(rdata);
            }
            else
            {
                List<GridRegion> defregs = GetDefaultRegions(scopeID);
                if (defregs != null)
                    return defregs[0];
            }
            return null;
        }

        public List<GridRegion> GetRegionsByName(UUID scopeID, string name, int maxNumber)
        {
            // m_log.DebugFormat("[GRID SERVICE]: GetRegionsByName {0}", name);

            var nameURI = new RegionURI(name);
            if (!nameURI.IsValid)
                return new List<GridRegion>();

            return GetRegionsByURI(scopeID, nameURI, maxNumber);
        }

        public List<GridRegion> GetRegionsByURI(UUID scopeID, RegionURI nameURI, int maxNumber)
        {
            // m_log.DebugFormat("[GRID SERVICE]: GetRegionsByName {0}", name);
            if (!nameURI.IsValid)
                return new List<GridRegion>();

            bool localGrid;
            if (nameURI.HasHost)
            {
                if (!nameURI.ResolveDNS())
                    return new List<GridRegion>();
                localGrid = m_HypergridLinker.IsLocalGrid(nameURI.HostUrl);
                nameURI.IsLocalGrid = localGrid;
                if (!nameURI.IsValid)
                    return new List<GridRegion>();
            }
            else
                localGrid = true;

            int count = 0;

            string mapname = nameURI.RegionHostPortSpaceName;
            List<RegionData> rdatas = m_Database.Get("%" + Util.EscapeForLike(mapname) + "%", scopeID);
            List<GridRegion> rinfos = new List<GridRegion>();

            if(localGrid)
            {
                if (!nameURI.HasRegionName)
                {
                    List<GridRegion> dinfos = GetDefaultRegions(scopeID);
                    if (dinfos != null && dinfos.Count > 0)
                        rinfos.Add(dinfos[0]);
                }
                else
                {
                    string name = nameURI.RegionName;
                    if (rdatas != null && (rdatas.Count > 0))
                    {
                        //m_log.DebugFormat("[GRID SERVICE]: Found {0} regions", rdatas.Count);
                        foreach (RegionData rdata in rdatas)
                        {
                            if (name.Equals(rdata.RegionName, StringComparison.InvariantCultureIgnoreCase))
                            {
                                rinfos.Insert(0, RegionData2RegionInfo(rdata));
                                if (count == maxNumber)
                                    rinfos.RemoveAt(count - 1);
                            }
                            else if (count++ < maxNumber)
                                rinfos.Add(RegionData2RegionInfo(rdata));
                        }
                    }
                }
                return rinfos;
            }

            if (!m_AllowHypergridMapSearch)
                return rinfos;

            if (rdatas != null && (rdatas.Count > 0))
            {
                bool haveMatch = false;
                // m_log.DebugFormat("[GRID SERVICE]: Found {0} regions", rdatas.Count);
                foreach (RegionData rdata in rdatas)
                {
                    int indx = rdata.RegionName.IndexOf("://");
                    if(indx < 0)
                        continue;
                    string rname = rdata.RegionName.Substring(indx + 3);
                    if (mapname.Equals(rname, StringComparison.InvariantCultureIgnoreCase))
                    {
                        haveMatch = true;
                        rinfos.Insert(0, RegionData2RegionInfo(rdata));
                        if (count == maxNumber)
                            rinfos.RemoveAt(count - 1);
                    }
                    else if (count++ < maxNumber)
                        rinfos.Add(RegionData2RegionInfo(rdata));
                }
                if (haveMatch)
                    return rinfos;
            }

            GridRegion r = m_HypergridLinker.LinkRegion(scopeID, nameURI);
            if (r != null)
            {
                if (count == maxNumber)
                    rinfos.RemoveAt(count - 1);
                rinfos.Add(r);
            }

            return rinfos;
        }

        public List<GridRegion> GetRegionRange(UUID scopeID, int xmin, int xmax, int ymin, int ymax)
        {
            int xminSnap = (int)(xmin / Constants.RegionSize) * (int)Constants.RegionSize;
            int xmaxSnap = (int)(xmax / Constants.RegionSize) * (int)Constants.RegionSize;
            int yminSnap = (int)(ymin / Constants.RegionSize) * (int)Constants.RegionSize;
            int ymaxSnap = (int)(ymax / Constants.RegionSize) * (int)Constants.RegionSize;

            List<RegionData> rdatas = m_Database.Get(xminSnap, yminSnap, xmaxSnap, ymaxSnap, scopeID);
            List<GridRegion> rinfos = new List<GridRegion>();
            foreach (RegionData rdata in rdatas)
                rinfos.Add(RegionData2RegionInfo(rdata));

            return rinfos;
        }

        #endregion

        #region Data structure conversions

        public RegionData RegionInfo2RegionData(GridRegion rinfo)
        {
            RegionData rdata = new RegionData();
            rdata.posX = (int)rinfo.RegionLocX;
            rdata.posY = (int)rinfo.RegionLocY;
            rdata.sizeX = rinfo.RegionSizeX;
            rdata.sizeY = rinfo.RegionSizeY;
            rdata.RegionID = rinfo.RegionID;
            rdata.RegionName = rinfo.RegionName;
            rdata.Data = rinfo.ToKeyValuePairs();
            rdata.Data["regionHandle"] = Utils.UIntsToLong((uint)rdata.posX, (uint)rdata.posY);
            rdata.Data["owner_uuid"] = rinfo.EstateOwner.ToString();
            return rdata;
        }

        public GridRegion RegionData2RegionInfo(RegionData rdata)
        {
            GridRegion rinfo = new GridRegion(rdata.Data);
            rinfo.RegionLocX = rdata.posX;
            rinfo.RegionLocY = rdata.posY;
            rinfo.RegionSizeX = rdata.sizeX;
            rinfo.RegionSizeY = rdata.sizeY;
            rinfo.RegionID = rdata.RegionID;
            rinfo.RegionName = rdata.RegionName;
            rinfo.ScopeID = rdata.ScopeID;

            return rinfo;
        }

        #endregion

        public List<GridRegion> GetDefaultRegions(UUID scopeID)
        {
            List<GridRegion> ret = new List<GridRegion>();

            List<RegionData> regions = m_Database.GetDefaultRegions(scopeID);

            foreach (RegionData r in regions)
            {
                if ((Convert.ToInt32(r.Data["flags"]) & (int)MutSea.Framework.RegionFlags.RegionOnline) != 0)
                    ret.Add(RegionData2RegionInfo(r));
            }

            m_log.DebugFormat("[GRID SERVICE]: GetDefaultRegions returning {0} regions", ret.Count);
            return ret;
        }

        public List<GridRegion> GetDefaultHypergridRegions(UUID scopeID)
        {
            List<GridRegion> ret = new List<GridRegion>();

            List<RegionData> regions = m_Database.GetDefaultHypergridRegions(scopeID);

            foreach (RegionData r in regions)
            {
                if ((Convert.ToInt32(r.Data["flags"]) & (int)MutSea.Framework.RegionFlags.RegionOnline) != 0)
                    ret.Add(RegionData2RegionInfo(r));
            }

            int hgDefaultRegionsFoundOnline = regions.Count;

            // For now, hypergrid default regions will always be given precedence but we will also return simple default
            // regions in case no specific hypergrid regions are specified.
            ret.AddRange(GetDefaultRegions(scopeID));

            int normalDefaultRegionsFoundOnline = ret.Count - hgDefaultRegionsFoundOnline;

            m_log.DebugFormat(
                "[GRID SERVICE]: GetDefaultHypergridRegions returning {0} hypergrid default and {1} normal default regions",
                hgDefaultRegionsFoundOnline, normalDefaultRegionsFoundOnline);

            return ret;
        }

        public List<GridRegion> GetFallbackRegions(UUID scopeID, int x, int y)
        {
            List<GridRegion> ret = new List<GridRegion>();

            List<RegionData> regions = m_Database.GetFallbackRegions(scopeID);
            if (regions.Count > 0)
            {
                if (regions.Count > 1)
                {
                    regions.Sort(new RegionDataDistanceCompare(x, y));
                }

                foreach (RegionData r in regions)
                {
                    int rflags = Convert.ToInt32(r.Data["flags"]);
                    if ((rflags & (int)MutSea.Framework.RegionFlags.Hyperlink) != 0)
                        continue;
                    if ((rflags & (int)MutSea.Framework.RegionFlags.RegionOnline) != 0)
                        ret.Add(RegionData2RegionInfo(r));
                }
            }

            m_log.DebugFormat("[GRID SERVICE]: Fallback returned {0} regions", ret.Count);
            return ret;
        }

        public List<GridRegion> GetOnlineRegions(UUID scopeID, int x, int y, int maxCount)
        {
            List<GridRegion> ret = new List<GridRegion>();

            List<RegionData> regions = m_Database.GetOnlineRegions(scopeID);
            if (regions.Count > 0)
            {
                if (regions.Count > 1)
                {
                    regions.Sort(new RegionDataDistanceCompare(x, y));
                }

                foreach (RegionData r in regions)
                {
                    int rflags = Convert.ToInt32(r.Data["flags"]);
                    if ((rflags & (int)MutSea.Framework.RegionFlags.Hyperlink) != 0)
                        continue;
                    ret.Add(RegionData2RegionInfo(r));
                    if(ret.Count >= maxCount)
                        break;
                }
            }

            m_log.DebugFormat("[GRID SERVICE]: online returned {0} regions", ret.Count);
            return ret;
        }

        public List<GridRegion> GetHyperlinks(UUID scopeID)
        {
            List<GridRegion> ret = new List<GridRegion>();

            List<RegionData> regions = m_Database.GetHyperlinks(scopeID);

            foreach (RegionData r in regions)
            {
                if ((Convert.ToInt32(r.Data["flags"]) & (int)MutSea.Framework.RegionFlags.RegionOnline) != 0)
                    ret.Add(RegionData2RegionInfo(r));
            }

            m_log.DebugFormat("[GRID SERVICE]: Hyperlinks returned {0} regions", ret.Count);
            return ret;
        }

        public int GetRegionFlags(UUID scopeID, UUID regionID)
        {
            RegionData region = m_Database.Get(regionID, scopeID);

            if (region != null)
            {
                int flags = Convert.ToInt32(region.Data["flags"]);
                //m_log.DebugFormat("[GRID SERVICE]: Request for flags of {0}: {1}", regionID, flags);
                return flags;
            }
            else
                return -1;
        }

        private void HandleDeregisterRegion(string module, string[] cmd)
        {
            if (cmd.Length < 4)
            {
                MainConsole.Instance.Output("Usage: degregister region id <region-id>+");
                return;
            }

            for (int i = 3; i < cmd.Length; i++)
            {
                string rawRegionUuid = cmd[i];
                UUID regionUuid;

                if (!UUID.TryParse(rawRegionUuid, out regionUuid))
                {
                    MainConsole.Instance.Output("{0} is not a valid region uuid", rawRegionUuid);
                    return;
                }

                GridRegion region = GetRegionByUUID(UUID.Zero, regionUuid);

                if (region == null)
                {
                    MainConsole.Instance.Output("No region with UUID {0}", regionUuid);
                    return;
                }

                if (DeregisterRegion(regionUuid))
                {
                    MainConsole.Instance.Output("Deregistered {0} {1}", region.RegionName, regionUuid);
                }
                else
                {
                    // I don't think this can ever occur if we know that the region exists.
                    MainConsole.Instance.Output("Error deregistering {0} {1}", region.RegionName, regionUuid);
                }
            }
        }

        private void HandleShowRegions(string module, string[] cmd)
        {
            if (cmd.Length != 2)
            {
                MainConsole.Instance.Output("Syntax: show regions");
                return;
            }

            List<RegionData> regions = m_Database.Get(0, 0, int.MaxValue, int.MaxValue, UUID.Zero);

            OutputRegionsToConsoleSummary(regions);
        }

        private void HandleShowGridSize(string module, string[] cmd)
        {
            List<RegionData> regions = m_Database.Get(0, 0, int.MaxValue, int.MaxValue, UUID.Zero);

            double size = 0;

            foreach (RegionData region in regions)
            {
                int flags = Convert.ToInt32(region.Data["flags"]);

                if ((flags & (int)Framework.RegionFlags.Hyperlink) == 0)
                    size += region.sizeX * region.sizeY;
            }

            MainConsole.Instance.Output("This is a very rough approximation.");
            MainConsole.Instance.Output("Although it will not count regions that are actually links to others over the Hypergrid, ");
            MainConsole.Instance.Output("it will count regions that are inactive but were not deregistered from the grid service");
            MainConsole.Instance.Output("(e.g. simulator crashed rather than shutting down cleanly).\n");

            MainConsole.Instance.Output("Grid size: {0} km squared.", size / 1000000);
        }

        private void HandleShowRegion(string module, string[] cmd)
        {
            if (cmd.Length != 4)
            {
                MainConsole.Instance.Output("Syntax: show region name <region name>");
                return;
            }

            string regionName = cmd[3];

            List<RegionData> regions = m_Database.Get(Util.EscapeForLike(regionName), UUID.Zero);
            if (regions == null || regions.Count < 1)
            {
                MainConsole.Instance.Output("No region with name {0} found", regionName);
                return;
            }

            OutputRegionsToConsole(regions);
        }

        private void HandleShowRegionAt(string module, string[] cmd)
        {
            if (cmd.Length != 5)
            {
                MainConsole.Instance.Output("Syntax: show region at <x-coord> <y-coord>");
                return;
            }

            uint x, y;
            if (!uint.TryParse(cmd[3], out x))
            {
                MainConsole.Instance.Output("x-coord must be an integer");
                return;
            }

            if (!uint.TryParse(cmd[4], out y))
            {
                MainConsole.Instance.Output("y-coord must be an integer");
                return;
            }


            RegionData region = m_Database.Get((int)Util.RegionToWorldLoc(x), (int)Util.RegionToWorldLoc(y), UUID.Zero);

            if (region == null)
            {
                MainConsole.Instance.Output("No region found at {0},{1}", x, y);
                return;
            }

            OutputRegionToConsole(region);
        }

        private void OutputRegionToConsole(RegionData r)
        {
            MutSea.Framework.RegionFlags flags = (MutSea.Framework.RegionFlags)Convert.ToInt32(r.Data["flags"]);

            ConsoleDisplayList dispList = new ConsoleDisplayList();
            dispList.AddRow("Region Name", r.RegionName);
            dispList.AddRow("Region ID", r.RegionID);
            dispList.AddRow("Location", string.Format("{0},{1}", r.coordX, r.coordY));
            dispList.AddRow("Size", string.Format("{0}x{1}", r.sizeX, r.sizeY));
            dispList.AddRow("URI", r.Data["serverURI"]);
            dispList.AddRow("Owner ID", r.Data["owner_uuid"]);
            dispList.AddRow("Flags", flags);

            MainConsole.Instance.Output(dispList.ToString());
        }

        private void OutputRegionsToConsole(List<RegionData> regions)
        {
            foreach (RegionData r in regions)
                OutputRegionToConsole(r);
        }

        private void OutputRegionsToConsoleSummary(List<RegionData> regions)
        {
            ConsoleDisplayTable dispTable = new ConsoleDisplayTable();
            dispTable.AddColumn("Name", ConsoleDisplayUtil.RegionNameSize);
            dispTable.AddColumn("ID", ConsoleDisplayUtil.UuidSize);
            dispTable.AddColumn("Position", ConsoleDisplayUtil.CoordTupleSize);
            dispTable.AddColumn("Size", 11);
            dispTable.AddColumn("Flags", 60);

            foreach (RegionData r in regions)
            {
                MutSea.Framework.RegionFlags flags = (MutSea.Framework.RegionFlags)Convert.ToInt32(r.Data["flags"]);
                dispTable.AddRow(
                    r.RegionName,
                    r.RegionID.ToString(),
                    string.Format("{0},{1}", r.coordX, r.coordY),
                    string.Format("{0}x{1}", r.sizeX, r.sizeY),
                    flags.ToString());
            }

            MainConsole.Instance.Output(dispTable.ToString());
        }

        private int ParseFlags(int prev, string flags)
        {
            MutSea.Framework.RegionFlags f = (MutSea.Framework.RegionFlags)prev;

            string[] parts = flags.Split(new char[] {',', ' '}, StringSplitOptions.RemoveEmptyEntries);

            foreach (string p in parts)
            {
                int val;

                try
                {
                    if (p.StartsWith("+"))
                    {
                        val = (int)Enum.Parse(typeof(MutSea.Framework.RegionFlags), p.Substring(1));
                        f |= (MutSea.Framework.RegionFlags)val;
                    }
                    else if (p.StartsWith("-"))
                    {
                        val = (int)Enum.Parse(typeof(MutSea.Framework.RegionFlags), p.Substring(1));
                        f &= ~(MutSea.Framework.RegionFlags)val;
                    }
                    else
                    {
                        val = (int)Enum.Parse(typeof(MutSea.Framework.RegionFlags), p);
                        f |= (MutSea.Framework.RegionFlags)val;
                    }
                }
                catch (Exception)
                {
                    MainConsole.Instance.Output("Error in flag specification: " + p);
                }
            }

            return (int)f;
        }

        private void HandleSetFlags(string module, string[] cmd)
        {
            if (cmd.Length < 5)
            {
                MainConsole.Instance.Output("Syntax: set region flags <region name> <flags>");
                return;
            }

            List<RegionData> regions = m_Database.Get(Util.EscapeForLike(cmd[3]), UUID.Zero);
            if (regions == null || regions.Count < 1)
            {
                MainConsole.Instance.Output("Region not found");
                return;
            }

            foreach (RegionData r in regions)
            {
                int flags = Convert.ToInt32(r.Data["flags"]);
                flags = ParseFlags(flags, cmd[4]);
                r.Data["flags"] = flags.ToString();
                MutSea.Framework.RegionFlags f = (MutSea.Framework.RegionFlags)flags;

                MainConsole.Instance.Output(String.Format("Set region {0} to {1}", r.RegionName, f));
                m_Database.Store(r);
            }
        }

        /// <summary>
        /// Gets the grid extra service URls we wish for the region to send in MutSeaExtras to dynamically refresh
        /// parameters in the viewer used to access services like map, search and destination guides.
        /// <para>see "SimulatorFeaturesModule" </para>
        /// </summary>
        /// <returns>
        /// The grid extra service URls.
        /// </returns>
        public Dictionary<string,object> GetExtraFeatures()
        {
            return m_ExtraFeatures;
        }
    }
}
