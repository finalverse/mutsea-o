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
using System.Net;
using System.Reflection;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using Mono.Addins;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using MutSea.Framework;
using MutSea.Framework.Servers;
using MutSea.Framework.Servers.HttpServer;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;
using MutSea.Services.Interfaces;

namespace MutSea.Region.OptionalModules.World.MoneyModule
{
    /// <summary>
    /// This is only the functionality required to make the functionality associated with money work
    /// (such as land transfers).  There is no money code here!  Use FORGE as an example for money code.
    /// Demo Economy/Money Module.  This is a purposely crippled module!
    ///  // To land transfer you need to add:
    /// -helperuri http://serveraddress:port/
    /// to the command line parameters you use to start up your client
    /// This commonly looks like -helperuri http://127.0.0.1:9000/
    ///
    /// </summary>

    [Extension(Path = "/MutSea/RegionModules", NodeName = "RegionModule", Id = "SampleMoneyModule")]
    public class SampleMoneyModule : IMoneyModule, ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Where Stipends come from and Fees go to.
        /// </summary>
        // private UUID EconomyBaseAccount = UUID.Zero;

        private Dictionary<string, XmlRpcMethod> m_rpcHandlers;
        private string m_localEconomyURL;

        private float EnergyEfficiency = 1f;
        // private ObjectPaid handerOnObjectPaid;
        private bool m_enabled = true;
        private bool m_sellEnabled = true;

        private IConfigSource m_gConfig;

        /// <summary>
        /// Region UUIDS indexed by AgentID
        /// </summary>

        /// <summary>
        /// Scenes by Region Handle
        /// </summary>
        private Dictionary<ulong, Scene> m_scenes = new Dictionary<ulong, Scene>();

        // private int m_stipend = 1000;

        private int ObjectCount = 0;
        private int PriceEnergyUnit = 0;
        private int PriceGroupCreate = -1;
        private int PriceObjectClaim = 0;
        private float PriceObjectRent = 0f;
        private float PriceObjectScaleFactor = 10f;
        private int PriceParcelClaim = 0;
        private float PriceParcelClaimFactor = 1f;
        private int PriceParcelRent = 0;
        private int PricePublicObjectDecay = 0;
        private int PricePublicObjectDelete = 0;
        private int PriceRentLight = 0;
        private int PriceUpload = 0;
        private int TeleportMinPrice = 0;

        private float TeleportPriceExponent = 2f;


        #region IMoneyModule Members

#pragma warning disable 0067
        public event ObjectPaid OnObjectPaid;
#pragma warning restore 0067

        public int UploadCharge
        {
            get { return 0; }
        }

        public int GroupCreationCharge
        {
            get { return 0; }
        }

        /// <summary>
        /// Called on startup so the module can be configured.
        /// </summary>
        /// <param name="config">Configuration source.</param>
        public void Initialise(IConfigSource config)
        {
            m_gConfig = config;
            ReadConfigAndPopulate();
        }

        public void AddRegion(Scene scene)
        {
            if (m_enabled)
            {
                scene.RegisterModuleInterface<IMoneyModule>(this);
                IHttpServer httpServer = MainServer.Instance;

                lock (m_scenes)
                {
                    if (m_scenes.Count == 0)
                    {
                        m_localEconomyURL = scene.RegionInfo.ServerURI;
                        m_rpcHandlers = new Dictionary<string, XmlRpcMethod>();
                        m_rpcHandlers.Add("getCurrencyQuote", quote_func);
                        m_rpcHandlers.Add("buyCurrency", buy_func);
                        m_rpcHandlers.Add("preflightBuyLandPrep", preflightBuyLandPrep_func);
                        m_rpcHandlers.Add("buyLandPrep", landBuy_func);

                        // add php
                        MainServer.Instance.AddSimpleStreamHandler(new SimpleStreamHandler("/currency.php", processPHP));
                        MainServer.Instance.AddSimpleStreamHandler(new SimpleStreamHandler("/landtool.php", processPHP));
                    }

                    if (m_scenes.ContainsKey(scene.RegionInfo.RegionHandle))
                    {
                        m_scenes[scene.RegionInfo.RegionHandle] = scene;
                    }
                    else
                    {
                        m_scenes.Add(scene.RegionInfo.RegionHandle, scene);
                    }
                }

                scene.EventManager.OnNewClient += OnNewClient;
                scene.EventManager.OnMoneyTransfer += MoneyTransferAction;
                scene.EventManager.OnClientClosed += ClientClosed;
                scene.EventManager.OnAvatarEnteringNewParcel += AvatarEnteringParcel;
                scene.EventManager.OnMakeChildAgent += MakeChildAgent;
                scene.EventManager.OnValidateLandBuy += ValidateLandBuy;
                scene.EventManager.OnLandBuy += processLandBuy;
            }
        }

        public void RemoveRegion(Scene scene)
        {
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_enabled)
                return;
            if(scene.SceneGridInfo!= null && !string.IsNullOrEmpty(scene.SceneGridInfo.EconomyURL))
                return;
            ISimulatorFeaturesModule fm = scene.RequestModuleInterface<ISimulatorFeaturesModule>();
            if (fm != null && !string.IsNullOrWhiteSpace(m_localEconomyURL))
            {
                if(fm.TryGetMutSeaExtraFeature("currency-base-uri", out OSD tmp))
                    return;
                fm.AddMutSeaExtraFeature("currency-base-uri", Util.AppendEndSlash(m_localEconomyURL));
            }
        }

        public void processPHP(IOSHttpRequest request, IOSHttpResponse response)
        {
            MainServer.Instance.HandleXmlRpcRequests((OSHttpRequest)request, (OSHttpResponse)response, m_rpcHandlers);
        }

        // Please do not refactor these to be just one method
        // Existing implementations need the distinction
        //
        public void ApplyCharge(UUID agentID, int amount, MoneyTransactionType type, string extraData)
        {
        }

        public void ApplyCharge(UUID agentID, int amount, MoneyTransactionType type)
        {
        }

        public void ApplyUploadCharge(UUID agentID, int amount, string text)
        {
        }

        public bool ObjectGiveMoney(UUID objectID, UUID fromID, UUID toID, int amount, UUID txn, out string result)
        {
            result = String.Empty;
            string description = String.Format("Object {0} pays {1}", resolveObjectName(objectID), resolveAgentName(toID));

            bool give_result = doMoneyTransfer(fromID, toID, amount, 2, description);


            BalanceUpdate(fromID, toID, give_result, description);

            return give_result;
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public Type ReplaceableInterface
        {
            get { return typeof(IMoneyModule); }
        }

        public string Name
        {
            get { return "BetaGridLikeMoneyModule"; }
        }

        #endregion

        /// <summary>
        /// Parse Configuration
        /// </summary>
        private void ReadConfigAndPopulate()
        {
            // we are enabled by default

            IConfig startupConfig = m_gConfig.Configs["Startup"];

            if(startupConfig == null) // should not happen
                return;

            IConfig economyConfig = m_gConfig.Configs["Economy"];

            // economymodule may be at startup or Economy (legacy)
            string mmodule = startupConfig.GetString("economymodule","");
            if(string.IsNullOrEmpty(mmodule))
            {
                if(economyConfig != null)
                {
                    mmodule = economyConfig.GetString("economymodule", "");
                    if (String.IsNullOrEmpty(mmodule))
                        mmodule = economyConfig.GetString("EconomyModule", "");
                }
            }

            if (!string.IsNullOrEmpty(mmodule) && mmodule != Name)
            {
                // some other money module selected
                m_enabled = false;
                return;
            }

            if(economyConfig == null)
                return;

            PriceEnergyUnit = economyConfig.GetInt("PriceEnergyUnit", 0);
            PriceObjectClaim = economyConfig.GetInt("PriceObjectClaim", 0);
            PricePublicObjectDecay = economyConfig.GetInt("PricePublicObjectDecay", 4);
            PricePublicObjectDelete = economyConfig.GetInt("PricePublicObjectDelete", 0);
            PriceParcelClaim = economyConfig.GetInt("PriceParcelClaim", 0);
            PriceParcelClaimFactor = economyConfig.GetFloat("PriceParcelClaimFactor", 1f);
            PriceUpload = economyConfig.GetInt("PriceUpload", 0);
            PriceRentLight = economyConfig.GetInt("PriceRentLight", 0);
            TeleportMinPrice = economyConfig.GetInt("TeleportMinPrice", 0);
            TeleportPriceExponent = economyConfig.GetFloat("TeleportPriceExponent", 2f);
            EnergyEfficiency = economyConfig.GetFloat("EnergyEfficiency", 1);
            PriceObjectRent = economyConfig.GetFloat("PriceObjectRent", 0);
            PriceObjectScaleFactor = economyConfig.GetFloat("PriceObjectScaleFactor", 10);
            PriceParcelRent = economyConfig.GetInt("PriceParcelRent", 0);
            PriceGroupCreate = economyConfig.GetInt("PriceGroupCreate", -1);
            m_sellEnabled = economyConfig.GetBoolean("SellEnabled", true);
        }

        private void GetClientFunds(IClientAPI client)
        {
            CheckExistAndRefreshFunds(client.AgentId);
        }

        /// <summary>
        /// New Client Event Handler
        /// </summary>
        /// <param name="client"></param>
        private void OnNewClient(IClientAPI client)
        {
            GetClientFunds(client);

            // Subscribe to Money messages
            client.OnEconomyDataRequest += EconomyDataRequestHandler;
            client.OnMoneyBalanceRequest += SendMoneyBalance;
            client.OnRequestPayPrice += requestPayPrice;
            client.OnObjectBuy += ObjectBuy;
            client.OnLogout += ClientLoggedOut;
        }

        /// <summary>
        /// Transfer money
        /// </summary>
        /// <param name="Sender"></param>
        /// <param name="Receiver"></param>
        /// <param name="amount"></param>
        /// <returns></returns>
        private bool doMoneyTransfer(UUID Sender, UUID Receiver, int amount, int transactiontype, string description)
        {
            return true;
        }


        /// <summary>
        /// Sends the the stored money balance to the client
        /// </summary>
        /// <param name="client"></param>
        /// <param name="agentID"></param>
        /// <param name="SessionID"></param>
        /// <param name="TransactionID"></param>
        public void SendMoneyBalance(IClientAPI client, UUID agentID, UUID SessionID, UUID TransactionID)
        {
            if (client.AgentId == agentID && client.SessionId == SessionID)
            {
                int returnfunds = 0;

                try
                {
                    returnfunds = GetFundsForAgentID(agentID);
                }
                catch (Exception e)
                {
                    client.SendAlertMessage(e.Message + " ");
                }

                client.SendMoneyBalance(TransactionID, true, Array.Empty<byte>(), returnfunds, 0, UUID.Zero, false, UUID.Zero, false, 0, String.Empty);
            }
            else
            {
                client.SendAlertMessage("Unable to send your money balance to you!");
            }
        }

        private SceneObjectPart findPrim(UUID objectID)
        {
            lock (m_scenes)
            {
                foreach (Scene s in m_scenes.Values)
                {
                    SceneObjectPart part = s.GetSceneObjectPart(objectID);
                    if (part != null)
                    {
                        return part;
                    }
                }
            }
            return null;
        }

        private string resolveObjectName(UUID objectID)
        {
            SceneObjectPart part = findPrim(objectID);
            if (part != null)
            {
                return part.Name;
            }
            return String.Empty;
        }

        private string resolveAgentName(UUID agentID)
        {
            // try avatar username surname
            Scene scene = GetRandomScene();
            UserAccount account = scene.UserAccountService.GetUserAccount(scene.RegionInfo.ScopeID, agentID);
            if (account != null)
            {
                string avatarname = account.FirstName + " " + account.LastName;
                return avatarname;
            }
            else
            {
                m_log.ErrorFormat(
                    "[MONEY]: Could not resolve user {0}",
                    agentID);
            }

            return String.Empty;
        }

        private void BalanceUpdate(UUID senderID, UUID receiverID, bool transactionresult, string description)
        {
            IClientAPI sender = LocateClientObject(senderID);
            IClientAPI receiver = LocateClientObject(receiverID);

            if (senderID != receiverID)
            {
                if (sender != null)
                {
                    sender.SendMoneyBalance(UUID.Random(), transactionresult, Utils.StringToBytes(description), GetFundsForAgentID(senderID), 0, UUID.Zero, false, UUID.Zero, false, 0, String.Empty);
                }

                if (receiver != null)
                {
                    receiver.SendMoneyBalance(UUID.Random(), transactionresult, Utils.StringToBytes(description), GetFundsForAgentID(receiverID), 0, UUID.Zero, false, UUID.Zero, false, 0, String.Empty);
                }
            }
        }

        /// <summary>
        /// XMLRPC handler to send alert message and sound to client
        /// </summary>
        public XmlRpcResponse UserAlert(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            XmlRpcResponse ret = new XmlRpcResponse();
            Hashtable retparam = new Hashtable();
            Hashtable requestData = (Hashtable) request.Params[0];

            UUID agentId;
            UUID soundId;
            UUID regionId;

            UUID.TryParse((string) requestData["agentId"], out agentId);
            UUID.TryParse((string) requestData["soundId"], out soundId);
            UUID.TryParse((string) requestData["regionId"], out regionId);
            string text = (string) requestData["text"];
            string secret = (string) requestData["secret"];

            Scene userScene = GetSceneByUUID(regionId);
            if (userScene != null)
            {
                if (userScene.RegionInfo.regionSecret == secret)
                {

                    IClientAPI client = LocateClientObject(agentId);
                       if (client != null)
                       {

                           if (!soundId.IsZero())
                               client.SendPlayAttachedSound(soundId, UUID.Zero, UUID.Zero, 1.0f, 0);

                           client.SendBlueBoxMessage(UUID.Zero, "", text);

                           retparam.Add("success", true);
                       }
                    else
                    {
                        retparam.Add("success", false);
                    }
                }
                else
                {
                    retparam.Add("success", false);
                }
            }

            ret.Value = retparam;
            return ret;
        }

        # region Standalone box enablers only

        public XmlRpcResponse quote_func(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            // UUID agentId = UUID.Zero;
            int amount = 0;
            try
            {
                Hashtable requestData = (Hashtable)request.Params[0];
                amount = (int)requestData["currencyBuy"];
            }
            catch{ }

            Hashtable currencyResponse = new Hashtable();
            currencyResponse.Add("estimatedCost", 0);
            //currencyResponse.Add("estimatedLocalCost", " 0 Euros");

            currencyResponse.Add("currencyBuy", amount);

            Hashtable quoteResponse = new Hashtable();
            quoteResponse.Add("success", true);
            quoteResponse.Add("currency", currencyResponse);
            quoteResponse.Add("confirm", "asdfad9fj39ma9fj");

            //quoteResponse.Add("success", false);
            //quoteResponse.Add("errorMessage", "There is currency");
            //quoteResponse.Add("errorURI", "http://opensimulator.org");
            XmlRpcResponse returnval = new XmlRpcResponse();
            returnval.Value = quoteResponse;
            return returnval;
        }

        public XmlRpcResponse buy_func(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            // Hashtable requestData = (Hashtable) request.Params[0];
            // UUID agentId = UUID.Zero;
            // int amount = 0;

            XmlRpcResponse returnval = new XmlRpcResponse();
            Hashtable returnresp = new Hashtable();
            returnresp.Add("success", true);
            returnval.Value = returnresp;
            return returnval;
        }

        public XmlRpcResponse preflightBuyLandPrep_func(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            XmlRpcResponse ret = new XmlRpcResponse();
            Hashtable retparam = new Hashtable();
            Hashtable membershiplevels = new Hashtable();
            ArrayList levels = new ArrayList();
            Hashtable level = new Hashtable();
            level.Add("id", "00000000-0000-0000-0000-000000000000");
            level.Add("description", "some level");
            levels.Add(level);
            //membershiplevels.Add("levels",levels);

            Hashtable landuse = new Hashtable();
            landuse.Add("upgrade", false);
            landuse.Add("action", "http://invaliddomaininvalid.com/");

            Hashtable currency = new Hashtable();
            currency.Add("estimatedCost", 0);

            Hashtable membership = new Hashtable();
            membershiplevels.Add("upgrade", false);
            membershiplevels.Add("action", "http://invaliddomaininvalid.com/");
            membershiplevels.Add("levels", membershiplevels);

            retparam.Add("success", true);
            retparam.Add("currency", currency);
            retparam.Add("membership", membership);
            retparam.Add("landuse", landuse);
            retparam.Add("confirm", "asdfajsdkfjasdkfjalsdfjasdf");

            ret.Value = retparam;

            return ret;
        }

        public XmlRpcResponse landBuy_func(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            XmlRpcResponse ret = new XmlRpcResponse();
            Hashtable retparam = new Hashtable();
            // Hashtable requestData = (Hashtable) request.Params[0];

            // UUID agentId = UUID.Zero;
            // int amount = 0;

            retparam.Add("success", true);
            ret.Value = retparam;

            return ret;
        }

        #endregion

        #region local Fund Management

        /// <summary>
        /// Ensures that the agent accounting data is set up in this instance.
        /// </summary>
        /// <param name="agentID"></param>
        private void CheckExistAndRefreshFunds(UUID agentID)
        {

        }

        /// <summary>
        /// Gets the amount of Funds for an agent
        /// </summary>
        /// <param name="AgentID"></param>
        /// <returns></returns>
        private int GetFundsForAgentID(UUID AgentID)
        {
            int returnfunds = 0;

            return returnfunds;
        }

        // private void SetLocalFundsForAgentID(UUID AgentID, int amount)
        // {

        // }

        #endregion

        #region Utility Helpers

        /// <summary>
        /// Locates a IClientAPI for the client specified
        /// </summary>
        /// <param name="AgentID"></param>
        /// <returns></returns>
        private IClientAPI LocateClientObject(UUID AgentID)
        {
            ScenePresence tPresence;
            lock (m_scenes)
            {
                foreach (Scene _scene in m_scenes.Values)
                {
                    tPresence = _scene.GetScenePresence(AgentID);
                    if (tPresence != null && !tPresence.IsDeleted && !tPresence.IsChildAgent)
                        return tPresence.ControllingClient;
                }
            }
            return null;
        }

        private Scene LocateSceneClientIn(UUID AgentId)
        {
            lock (m_scenes)
            {
                foreach (Scene _scene in m_scenes.Values)
                {
                    ScenePresence tPresence = _scene.GetScenePresence(AgentId);
                    if (tPresence != null && !tPresence.IsDeleted && !tPresence.IsChildAgent)
                        return _scene;
                }
            }
            return null;
        }

        /// <summary>
        /// Utility function Gets a Random scene in the instance.  For when which scene exactly you're doing something with doesn't matter
        /// </summary>
        /// <returns></returns>
        public Scene GetRandomScene()
        {
            lock (m_scenes)
            {
                foreach (Scene rs in m_scenes.Values)
                    return rs;
            }
            return null;
        }

        /// <summary>
        /// Utility function to get a Scene by RegionID in a module
        /// </summary>
        /// <param name="RegionID"></param>
        /// <returns></returns>
        public Scene GetSceneByUUID(UUID RegionID)
        {
            lock (m_scenes)
            {
                foreach (Scene rs in m_scenes.Values)
                {
                    if (rs.RegionInfo.originRegionID == RegionID)
                    {
                        return rs;
                    }
                }
            }
            return null;
        }

        #endregion

        #region event Handlers

        public void requestPayPrice(IClientAPI client, UUID objectID)
        {
            Scene scene = LocateSceneClientIn(client.AgentId);
            if (scene == null)
                return;

            SceneObjectPart task = scene.GetSceneObjectPart(objectID);
            if (task == null)
                return;
            SceneObjectGroup group = task.ParentGroup;
            SceneObjectPart root = group.RootPart;

            client.SendPayPrice(objectID, root.PayPrice);
        }

        /// <summary>
        /// When the client closes the connection we remove their accounting
        /// info from memory to free up resources.
        /// </summary>
        /// <param name="AgentID">UUID of agent</param>
        /// <param name="scene">Scene the agent was connected to.</param>
        /// <see cref="MutSea.Region.Framework.Scenes.EventManager.ClientClosed"/>
        public void ClientClosed(UUID AgentID, Scene scene)
        {

        }

        /// <summary>
        /// Event called Economy Data Request handler.
        /// </summary>
        /// <param name="agentId"></param>
        public void EconomyDataRequestHandler(IClientAPI user)
        {
            Scene s = (Scene)user.Scene;

            user.SendEconomyData(EnergyEfficiency, s.RegionInfo.ObjectCapacity, ObjectCount, PriceEnergyUnit, PriceGroupCreate,
                                 PriceObjectClaim, PriceObjectRent, PriceObjectScaleFactor, PriceParcelClaim, PriceParcelClaimFactor,
                                 PriceParcelRent, PricePublicObjectDecay, PricePublicObjectDelete, PriceRentLight, PriceUpload,
                                 TeleportMinPrice, TeleportPriceExponent);
        }

        private void ValidateLandBuy(Object osender, EventManager.LandBuyArgs e)
        {


            lock (e)
            {
                e.economyValidated = true;
            }


        }

        private void processLandBuy(Object osender, EventManager.LandBuyArgs e)
        {

        }

        /// <summary>
        /// THis method gets called when someone pays someone else as a gift.
        /// </summary>
        /// <param name="osender"></param>
        /// <param name="e"></param>
        private void MoneyTransferAction(Object osender, EventManager.MoneyTransferArgs e)
        {

        }

        /// <summary>
        /// Event Handler for when a root agent becomes a child agent
        /// </summary>
        /// <param name="avatar"></param>
        private void MakeChildAgent(ScenePresence avatar)
        {

        }

        /// <summary>
        /// Event Handler for when the client logs out.
        /// </summary>
        /// <param name="AgentId"></param>
        private void ClientLoggedOut(IClientAPI client)
        {

        }

        /// <summary>
        /// Call this when the client disconnects.
        /// </summary>
        /// <param name="client"></param>
        public void ClientClosed(IClientAPI client)
        {
            ClientClosed(client.AgentId, null);
        }

        /// <summary>
        /// Event Handler for when an Avatar enters one of the parcels in the simulator.
        /// </summary>
        /// <param name="avatar"></param>
        /// <param name="localLandID"></param>
        /// <param name="regionID"></param>
        private void AvatarEnteringParcel(ScenePresence avatar, int localLandID, UUID regionID)
        {

            //m_log.Info("[FRIEND]: " + avatar.Name + " status:" + (!avatar.IsChildAgent).ToString());
        }

        public int GetBalance(UUID agentID)
        {
            return 0;
        }

        // Please do not refactor these to be just one method
        // Existing implementations need the distinction
        //
        public bool UploadCovered(UUID agentID, int amount)
        {
            return true;
        }
        public bool AmountCovered(UUID agentID, int amount)
        {
            return true;
        }

        #endregion

        public void ObjectBuy(IClientAPI remoteClient, UUID agentID,
                UUID sessionID, UUID groupID, UUID categoryID,
                uint localID, byte saleType, int salePrice)
        {
            if (!m_sellEnabled)
            {
                remoteClient.SendBlueBoxMessage(UUID.Zero, "", "Buying is not implemented in this version");
                return;
            }

            if (salePrice != 0)
            {
                remoteClient.SendBlueBoxMessage(UUID.Zero, "", "Buying anything for a price other than zero is not implemented");
                return;
            }

            Scene s = LocateSceneClientIn(remoteClient.AgentId);

            // Implmenting base sale data checking here so the default OpenSimulator implementation isn't useless
            // combined with other implementations.  We're actually validating that the client is sending the data
            // that it should.   In theory, the client should already know what to send here because it'll see it when it
            // gets the object data.   If the data sent by the client doesn't match the object, the viewer probably has an
            // old idea of what the object properties are.   Viewer developer Hazim informed us that the base module
            // didn't check the client sent data against the object do any.   Since the base modules are the
            // 'crowning glory' examples of good practice..

            // Validate that the object exists in the scene the user is in
            SceneObjectPart part = s.GetSceneObjectPart(localID);
            if(!part.IsRoot) // silent ignore non root parts
                return;

            if (part == null || part.ParentGroup == null || part.ParentGroup.IsDeleted)
            {
                remoteClient.SendAgentAlertMessage("Unable to buy now. The object was not found.", false);
                return;
            }

            if (part.ObjectSaleType == (byte)SaleType.Not)
            {
                string e = string.Format("Object {0} is not for sale", part.Name);
                remoteClient.SendAgentAlertMessage(e, false);
                return;
            }

            // Validate that the client sent the price that the object is being sold for
            if (part.SalePrice != salePrice)
            {
                string e = string.Format("Object {0} price does not match selected price", part.Name);
                remoteClient.SendAgentAlertMessage(e, false);
                return;
            }

            // Validate that the client sent the proper sale type the object has set
            if (part.ObjectSaleType != saleType)
            {
                string e = string.Format("Object {0} sell type does not match selected type", part.Name);
                remoteClient.SendAgentAlertMessage(e, false);
                return;
            }

            IBuySellModule module = s.RequestModuleInterface<IBuySellModule>();
            if (module != null)
                module.BuyObject(remoteClient, categoryID, localID, saleType, salePrice);
        }

        public void MoveMoney(UUID fromUser, UUID toUser, int amount, string text)
        {
        }

        public bool MoveMoney(UUID fromUser, UUID toUser, int amount, MoneyTransactionType type, string text)
        {
            return true;
        }
    }

    public enum TransactionType : int
    {
        SystemGenerated = 0,
        RegionMoneyRequest = 1,
        Gift = 2,
        Purchase = 3
    }
}
