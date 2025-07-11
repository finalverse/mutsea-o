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
using System.IO;
using System.Reflection;
using System.Xml;

using MutSea.Framework;
using MutSea.Services.Base;
using MutSea.Services.Interfaces;

using log4net;
using Nini.Config;
using OpenMetaverse;
using PermissionMask = MutSea.Framework.PermissionMask;

namespace MutSea.Services.InventoryService
{
    /// <summary>
    /// Basically a hack to give us a Inventory library while we don't have a inventory server
    /// once the server is fully implemented then should read the data from that
    /// </summary>
    public class LibraryService : ServiceBase, ILibraryService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly UUID libOwner = Constants.m_MrMutSeaID;
        private const string m_LibraryRootFolderIDstr = "00000112-000f-0000-0000-000100bba000";
        private static readonly UUID m_LibraryRootFolderID = new UUID(m_LibraryRootFolderIDstr);

        static private InventoryFolderImpl m_LibraryRootFolder;

        public InventoryFolderImpl LibraryRootFolder
        {
            get { return m_LibraryRootFolder; }
        }


        /// <summary>
        /// Holds the root library folder and all its descendents.  This is really only used during inventory
        /// setup so that we don't have to repeatedly search the tree of library folders.
        /// </summary>
        static protected Dictionary<UUID, InventoryFolderImpl> libraryFolders
            = new Dictionary<UUID, InventoryFolderImpl>(32);

        static protected Dictionary<UUID, InventoryItemBase> m_items = new Dictionary<UUID, InventoryItemBase>(256);
        static LibraryService m_root;
        static object m_rootLock = new object();
        static readonly uint m_BasePermissions = (uint)PermissionMask.AllAndExport;
        static readonly uint m_EveryOnePermissions = (uint)PermissionMask.AllAndExportNoMod;
        static readonly uint m_CurrentPermissions = (uint)PermissionMask.AllAndExport;
        static readonly uint m_NextPermissions = (uint)PermissionMask.AllAndExport;
        static readonly uint m_GroupPermissions = 0;

        public LibraryService(IConfigSource config):base(config)
        {
            lock(m_rootLock)
            {
                if(m_root != null)
                    return;
                m_root = this;
            }

            string pLibrariesLocation = Path.Combine("inventory", "Libraries.xml");
            string pLibName = "MutSea Library";

            IConfig libConfig = config.Configs["LibraryService"];
            if (libConfig != null)
            {
                pLibrariesLocation = libConfig.GetString("DefaultLibrary", pLibrariesLocation);
                pLibName = libConfig.GetString("LibraryName", pLibName);
            }

            m_log.Debug("[LIBRARY]: Starting library service...");

            m_LibraryRootFolder = new InventoryFolderImpl();
            m_LibraryRootFolder.Owner = libOwner;
            m_LibraryRootFolder.ID = m_LibraryRootFolderID;
            m_LibraryRootFolder.Name = pLibName;
            m_LibraryRootFolder.ParentID = UUID.Zero;
            m_LibraryRootFolder.Type = 8;
            m_LibraryRootFolder.Version = 1;
            libraryFolders.Add(m_LibraryRootFolder.ID, m_LibraryRootFolder);

            LoadLibraries(pLibrariesLocation);
        }

        public InventoryItemBase CreateItem(UUID inventoryID, UUID assetID, string name, string description,
                                            int assetType, int invType, UUID parentFolderID)
        {
            InventoryItemBase item = new InventoryItemBase();
            item.Owner = libOwner;
            item.CreatorId = libOwner.ToString();
            item.ID = inventoryID;
            item.AssetID = assetID;
            item.Description = description;
            item.Name = name;
            item.AssetType = assetType;
            item.InvType = invType;
            item.Folder = parentFolderID;
            item.BasePermissions = m_BasePermissions;
            item.EveryOnePermissions = m_EveryOnePermissions;
            item.CurrentPermissions = m_CurrentPermissions;
            item.NextPermissions = m_NextPermissions;
            item.GroupPermissions = m_GroupPermissions;
            return item;
        }

        /// <summary>
        /// Use the asset set information at path to load assets
        /// </summary>
        /// <param name="path"></param>
        /// <param name="assets"></param>
        protected void LoadLibraries(string librariesControlPath)
        {
            m_log.InfoFormat("[LIBRARY INVENTORY]: Loading library control file {0}", librariesControlPath);
            LoadFromFile(librariesControlPath, "Libraries control", ReadLibraryFromConfig);
        }

        /// <summary>
        /// Read a library set from config
        /// </summary>
        /// <param name="config"></param>
        protected void ReadLibraryFromConfig(IConfig config, string path)
        {
            string basePath = Path.GetDirectoryName(path);
            if (config.Contains("RootVersion"))
            {
                m_LibraryRootFolder.Version = (ushort)config.GetInt("RootVersion", m_LibraryRootFolder.Version);
                return;
            }

            string foldersPath = Path.Combine(basePath, config.GetString("foldersFile", String.Empty));
            LoadFromFile(foldersPath, "Library folders", ReadFolderFromConfig);

            string itemsPath = Path.Combine( basePath, config.GetString("itemsFile", String.Empty));
            LoadFromFile(itemsPath, "Library items", ReadItemFromConfig);
        }

        /// <summary>
        /// Read a library inventory folder from a loaded configuration
        /// </summary>
        /// <param name="source"></param>
        private void ReadFolderFromConfig(IConfig config, string path)
        {
            InventoryFolderImpl folderInfo = new InventoryFolderImpl();

            folderInfo.ID = new UUID(config.GetString("folderID", m_LibraryRootFolderIDstr));
            folderInfo.Name = config.GetString("name", "unknown");
            folderInfo.ParentID = new UUID(config.GetString("parentFolderID", m_LibraryRootFolderIDstr));
            folderInfo.Type = (short)config.GetInt("type", 8);
            folderInfo.Version = (ushort)config.GetInt("version", 1);
            folderInfo.Owner = libOwner;

            if (libraryFolders.TryGetValue(folderInfo.ParentID, out InventoryFolderImpl parentFolder))
            {
                libraryFolders.Add(folderInfo.ID, folderInfo);
                parentFolder.AddChildFolder(folderInfo);
                //m_log.InfoFormat("[LIBRARY INVENTORY]: Adding folder {0} ({1})", folderInfo.name, folderInfo.folderID);
            }
            else
            {
                m_log.WarnFormat(
                    "[LIBRARY INVENTORY]: Couldn't add folder {0} ({1}) since parent folder with ID {2} does not exist!",
                    folderInfo.Name, folderInfo.ID, folderInfo.ParentID);
            }
        }

        /// <summary>
        /// Read a library inventory item metadata from a loaded configuration
        /// </summary>
        /// <param name="source"></param>
        private void ReadItemFromConfig(IConfig config, string path)
        {
            InventoryItemBase item = new InventoryItemBase();
            item.Owner = libOwner;
            item.CreatorId = libOwner.ToString();
            UUID itID = new UUID(config.GetString("inventoryID", m_LibraryRootFolderIDstr));
            item.ID = itID; 
            item.AssetID = new UUID(config.GetString("assetID", item.ID.ToString()));
            item.Folder = new UUID(config.GetString("folderID", m_LibraryRootFolderIDstr));
            item.Name = config.GetString("name", String.Empty);
            item.Description = config.GetString("description", item.Name);
            item.InvType = config.GetInt("inventoryType", 0);
            item.AssetType = config.GetInt("assetType", item.InvType);
            item.CurrentPermissions = (uint)config.GetLong("currentPermissions", m_CurrentPermissions);
            item.NextPermissions = (uint)config.GetLong("nextPermissions", m_NextPermissions);
            item.EveryOnePermissions = (uint)config.GetLong("everyonePermissions", m_EveryOnePermissions);
            item.BasePermissions = (uint)config.GetLong("basePermissions", m_BasePermissions);
            item.GroupPermissions = (uint)config.GetLong("basePermissions", m_GroupPermissions);
            item.Flags = (uint)config.GetInt("flags", 0);

            if (libraryFolders.TryGetValue(item.Folder, out InventoryFolderImpl parentFolder))
            {
                if(!parentFolder.Items.ContainsKey(itID))
                {
                    parentFolder.Items.Add(itID, item);
                    m_items[itID] = item;
                }
                else
                {
                    m_log.WarnFormat("[LIBRARY INVENTORY] Item {1} [{0}] not added, duplicate item", item.ID, item.Name);
                }
            }
            else
            {
                m_log.WarnFormat(
                    "[LIBRARY INVENTORY]: Couldn't add item {0} ({1}) since parent folder with ID {2} does not exist!",
                    item.Name, item.ID, item.Folder);
            }
        }

        private delegate void ConfigAction(IConfig config, string path);

        /// <summary>
        /// Load the given configuration at a path and perform an action on each Config contained within it
        /// </summary>
        /// <param name="path"></param>
        /// <param name="fileDescription"></param>
        /// <param name="action"></param>
        private static void LoadFromFile(string path, string fileDescription, ConfigAction action)
        {
            if (File.Exists(path))
            {
                try
                {
                    XmlConfigSource source = new XmlConfigSource(path);

                    for (int i = 0; i < source.Configs.Count; i++)
                    {
                        action(source.Configs[i], path);
                    }
                }
                catch (XmlException e)
                {
                    m_log.ErrorFormat("[LIBRARY INVENTORY]: Error loading {0} : {1}", path, e);
                }
            }
            else
            {
                m_log.ErrorFormat("[LIBRARY INVENTORY]: {0} file {1} does not exist!", fileDescription, path);
            }
        }

        /// <summary>
        /// Looks like a simple getter, but is written like this for some consistency with the other Request
        /// methods in the superclass
        /// </summary>
        /// <returns></returns>
        public Dictionary<UUID, InventoryFolderImpl> GetAllFolders()
        {
            Dictionary<UUID, InventoryFolderImpl> fs = new Dictionary<UUID, InventoryFolderImpl>();
            fs.Add(m_LibraryRootFolderID, m_LibraryRootFolder);
            List<InventoryFolderImpl> fis = TraverseFolder(m_LibraryRootFolder);
            foreach (InventoryFolderImpl f in fis)
            {
                fs.Add(f.ID, f);
            }
            //return libraryFolders;
            return fs;
        }

        private List<InventoryFolderImpl> TraverseFolder(InventoryFolderImpl node)
        {
            List<InventoryFolderImpl> folders = node.RequestListOfFolderImpls();
            List<InventoryFolderImpl> subs = new List<InventoryFolderImpl>();
            foreach (InventoryFolderImpl f in folders)
                subs.AddRange(TraverseFolder(f));

            folders.AddRange(subs);
            return folders;
        }

        public InventoryItemBase GetItem(UUID itemID)
        {
            if(m_items.TryGetValue(itemID, out InventoryItemBase it))
                return it;
            return null;
        }

        public InventoryItemBase[] GetMultipleItems(UUID[] ids)
        {
            List<InventoryItemBase> items = new(ids.Length);
            foreach (UUID id in ids.AsSpan())
            {
                if (m_items.TryGetValue(id, out InventoryItemBase it))
                    items.Add(it);
            }

            if(items.Count == 0)
                return null;
            return items.ToArray();
        }
    }
}
