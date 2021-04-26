using BySeller.Models;
using eBay.Service.Call;
using eBay.Service.Core.Sdk;
using eBay.Service.Core.Soap;
using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Web.Http;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.Xsl;
using ZCRMSDK.CRM.Library.Api.Response;
using ZCRMSDK.CRM.Library.CRUD;
using ZCRMSDK.CRM.Library.Setup.RestClient;
using ZCRMSDK.OAuth.Client;
using ZCRMSDK.OAuth.Contract;

namespace BySeller.Controllers
{
    public class MainController : ApiController
    {
        // Zoho grant token
        private string grantToken = "XXXXXXXXX";
        // eBay api context
        private static ApiContext apiContext = null;
        // Zoho config
        private static Dictionary<string, string> config = new Dictionary<string, string>()
        {
            {"client_id","XXXXXXX"},
            {"client_secret","XXXXXXX"},
            {"access_type","offline"},
            {"persistence_handler_class","ZCRMSDK.OAuth.ClientApp.ZohoOAuthInMemoryPersistence, ZCRMSDK"},
            {"apiBaseUrl","https://www.zohoapis.com"},
            {"iamURL","https://accounts.zoho.com"},
            {"fileUploadUrl","https://content.zohoapis.com"},
            {"apiVersion","v2"},
            {"currentUserEmail","XXXXXXX@XXXXXXX.com"}
        };

        [HttpPost]
        public IHttpActionResult CheckToken()
        {
            try
            {
                ZCRMRestClient.Initialize(config);

                ZohoOAuthClient client = ZohoOAuthClient.GetInstance();
                if (!IsTokenGenerated("XXXXXXX@XXXXXXX.com"))
                {
                    Trace.TraceError("Token Not Generated...Generating Access Token");
                    ZohoOAuthTokens tokens = client.GenerateAccessToken(grantToken);
                    Trace.TraceError(tokens.AccessToken);
                }
                else
                {
                    Trace.TraceError("Token Generated");
                }
                return Ok();
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
                Trace.TraceError(ex.InnerException.Message);
                return Ok();
            }
        }
        [HttpPost]
        public IHttpActionResult GetEbayNotification(HttpRequestMessage request)
        {
            try
            {
                // Parse the SOAP request to get the data payload
                var strXml = GetSoapXmlBody(request);
                System.Diagnostics.Trace.TraceError(strXml);

                string xsl = "";
                string output = "";
                var leads = new List<ZohoLead>();

                // Bid Received
                if (strXml.StartsWith("<GetItemResponse"))
                {
                    xsl = "<?xml version=\"1.0\" encoding=\"utf-8\"?>";
                    xsl += "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" xmlns:eBay=\"urn:ebay:apis:eBLBaseComponents\">";
                    xsl += "<xsl:output method=\"xml\" indent=\"yes\"/>";
                    xsl += "<xsl:template match=\"/\">";
                    xsl += "<GetItemResponseType>";
                    xsl += "<xsl:copy-of select=\"//eBay:GetItemResponse/*\"/>";
                    xsl += "</GetItemResponseType>";
                    xsl += "</xsl:template>";
                    xsl += "</xsl:stylesheet>";

                    output = TransformXML(strXml, xsl);

                    // Deserialize the data payload
                    var serializer = new XmlSerializer(typeof(GetItemResponseType));
                    var item = (GetItemResponseType)serializer.Deserialize(new StringReader(output));

                    leads = GetBidderLead(leads, item);
                }
                // Best Offer
                else if(strXml.StartsWith("<GetBestOffers"))
                {
                    xsl = "<?xml version=\"1.0\" encoding=\"utf-8\"?>";
                    xsl += "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" xmlns:eBay=\"urn:ebay:apis:eBLBaseComponents\">";
                    xsl += "<xsl:output method=\"xml\" indent=\"yes\"/>";
                    xsl += "<xsl:template match=\"/\">";
                    xsl += "<GetBestOffersResponseType>";
                    xsl += "<xsl:copy-of select=\"//eBay:GetBestOffersResponse/*\"/>";
                    xsl += "</GetBestOffersResponseType>";
                    xsl += "</xsl:template>";
                    xsl += "</xsl:stylesheet>";

                    output = TransformXML(strXml, xsl);

                    // Deserialize the data payload
                    var serializer = new XmlSerializer(typeof(GetBestOffersResponseType));
                    var item = (GetBestOffersResponseType)serializer.Deserialize(new StringReader(output));

                    foreach (BestOfferType offer in item.BestOfferArray)
                    {
                        leads = GetBestOfferLead(leads, item.Item, offer);
                    }
                }
                // Ask Seller Question
                else if(strXml.StartsWith("<GetMemberMessages"))
                {
                    xsl = "<?xml version=\"1.0\" encoding=\"utf-8\"?>";
                    xsl += "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" xmlns:eBay=\"urn:ebay:apis:eBLBaseComponents\">";
                    xsl += "<xsl:output method=\"xml\" indent=\"yes\"/>";
                    xsl += "<xsl:template match=\"/\">";
                    xsl += "<GetMemberMessagesResponseType>";
                    xsl += "<xsl:copy-of select=\"//eBay:GetMemberMessagesResponse/*\"/>";
                    xsl += "</GetMemberMessagesResponseType>";
                    xsl += "</xsl:template>";
                    xsl += "</xsl:stylesheet>";

                    output = TransformXML(strXml, xsl);

                    // Deserialize the data payload
                    var serializer = new XmlSerializer(typeof(GetMemberMessagesResponseType));
                    var item = (GetMemberMessagesResponseType)serializer.Deserialize(new StringReader(output));

                    foreach (MemberMessageExchangeType message in item.MemberMessage)
                    {
                        leads = GetMemberMessageLead(leads, message.Item, message);
                    }
                }

                if (leads.Count > 0)
                {
                    leads = GetLeadInformation(leads);
                    InsertZohoLeads(leads);
                }

                return Ok();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(ex.Message);
                return Ok();
            }
        }

        private string TransformXML(string strXml, string xsl)
        {
            try
            {
                XslCompiledTransform xslTransform = new XslCompiledTransform();

                using (var stringReader = new StringReader(xsl))
                {
                    using (var xslt = XmlReader.Create(stringReader))
                    {
                        xslTransform.Load(xslt);
                    }
                }

                string output = String.Empty;
                using (StringReader sri = new StringReader(strXml))
                {
                    using (XmlReader xri = XmlReader.Create(sri))
                    {
                        using (StringWriter sw = new StringWriter())
                        using (XmlWriter xwo = XmlWriter.Create(sw, xslTransform.OutputSettings))
                        {
                            xslTransform.Transform(xri, xwo);
                            output = sw.ToString();
                        }
                    }
                }

                return output;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public IHttpActionResult SetNotificationPreferences()
        {
            try
            {
                apiContext = GetApiContext();

                SetNotificationPreferencesCall call = new SetNotificationPreferencesCall(apiContext);
                ApplicationDeliveryPreferencesType delivery = new ApplicationDeliveryPreferencesType();
                delivery.AlertEnable = EnableCodeType.Enable;
                delivery.AlertEmail = "mailto://snharris@gmail.com";
                delivery.ApplicationEnable = EnableCodeType.Enable;
                delivery.ApplicationURL = "XXXXXXX/api/main/GetEbayNotification";
                delivery.AlertEnableSpecified = true;
                delivery.ApplicationEnableSpecified = true;
                delivery.DeviceType = DeviceTypeCodeType.Platform;
                delivery.DeviceTypeSpecified = true;

                NotificationEnableType notification =
                    new NotificationEnableType
                    {
                        EventEnable = EnableCodeType.Enable,
                        EventEnableSpecified = true,
                        EventType = NotificationEventTypeCodeType.BidReceived,
                        EventTypeSpecified = true
                    };

                NotificationEnableType notification2 =
                    new NotificationEnableType
                    {
                        EventEnable = EnableCodeType.Enable,
                        EventEnableSpecified = true,
                        EventType = NotificationEventTypeCodeType.BestOffer,
                        EventTypeSpecified = true
                    };

                NotificationEnableType notification3 =
                    new NotificationEnableType
                    {
                        EventEnable = EnableCodeType.Enable,
                        EventEnableSpecified = true,
                        EventType = NotificationEventTypeCodeType.AskSellerQuestion,
                        EventTypeSpecified = true
                    };

                call.SetNotificationPreferences(delivery, new NotificationEnableTypeCollection(new[] { notification, notification2, notification3 }));

                return Ok();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public IHttpActionResult GetNotificationPreferences()
        {
            try
            {
                apiContext = GetApiContext();

                GetNotificationPreferencesCall call = new GetNotificationPreferencesCall(apiContext);
                call.GetNotificationPreferences(NotificationRoleCodeType.Application);

                return Ok();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public IHttpActionResult GetNotificationUsage()
        {
            try
            {
                apiContext = GetApiContext();

                GetNotificationsUsageCall call = new GetNotificationsUsageCall(apiContext);

                call.GetNotificationsUsage(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), "110526051874");

                return Ok();
            }
            catch (Exception ex)
            {
                throw ex;
            }

        }

        public IHttpActionResult GetSellerEvents()
        {
            try
            {
                apiContext = GetApiContext();

                GetSellerEventsCall call = new GetSellerEventsCall(apiContext);

                call.GetSellerEvents(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

                return Ok();
            }
            catch (Exception ex)
            {
                throw ex;
            }

        }

        public IHttpActionResult GetSellerItems()
        {
            
            //[Step 1] Initialize eBay ApiContext object
            apiContext = GetApiContext();

            GetSellerListCall call = new GetSellerListCall(apiContext);
            
            call.DetailLevelList.Add(DetailLevelCodeType.ReturnAll);
            call.Pagination = new PaginationType() { EntriesPerPage = 200 };
            call.EndTimeFrom = DateTime.UtcNow.AddDays(-1);
            call.EndTimeTo = DateTime.UtcNow.AddDays(30);

            call.IncludeWatchCount = true;

            ItemTypeCollection items = call.GetSellerList();
            foreach(ItemType item in items)
            {
                try
                {
                    List<ZohoLead> leads = new List<ZohoLead>();

                    GetMemberMessagesCall messages = new GetMemberMessagesCall(apiContext);
                    messages.GetMemberMessages(item.ItemID, MessageTypeCodeType.AskSellerQuestion, MessageStatusTypeCodeType.Unanswered);

                    foreach (MemberMessageExchangeType message in messages.MemberMessageList)
                    {
                        leads = GetMemberMessageLead(leads, item, message);
                    }

                    if (item.BestOfferEnabled)
                    {
                        GetBestOffersCall boCall = new GetBestOffersCall(apiContext);
                        boCall.GetBestOffers(item.ItemID, null, BestOfferStatusCodeType.All, new PaginationType() { EntriesPerPage = 200 });
                        foreach (BestOfferType offer in boCall.BestOfferList)
                        {
                            leads = GetBestOfferLead(leads, item, offer);
                        }
                    }

                    GetAllBiddersCall bidderCall = new GetAllBiddersCall(apiContext);
                    var bidders = bidderCall.GetAllBidders(item.ItemID, GetAllBiddersModeCodeType.ViewAll);
                    foreach (OfferType bidder in bidders)
                    {
                        leads = GetBidderLead(leads, item, bidder);
                    }

                    if (leads.Count > 0)
                    {
                        leads = GetLeadInformation(leads);
                        InsertZohoLeads(leads);
                    }
                }
                catch(Exception ex)
                {
                    Trace.TraceError(ex.Message);
                }
            }

            return Ok();
            
        }

        private List<ZohoLead> GetLeadInformation(List<ZohoLead> leads)
        {
            try
            {
                foreach (var lead in leads)
                {
                    try
                    {
                        var doc = new HtmlDocument();
                        doc.LoadHtml(lead.ItemDescription);
                    
                        foreach (HtmlNode li in doc.DocumentNode.SelectNodes("//li"))
                        {
                            var splitLi = li.InnerText.Split(':');
                            if (splitLi.Count() == 2)
                            {
                                switch (splitLi[0].Trim())
                                {
                                    case "Year":
                                        lead.VehicleYear = splitLi[1].Trim();
                                        break;
                                    case "Make":
                                        lead.VehicleMake = splitLi[1].Trim();
                                        break;
                                    case "Model":
                                        lead.VehicleModel = splitLi[1].Trim();
                                        break;
                                    case "Stock Number":
                                        lead.StockNumber = splitLi[1].Trim();
                                        break;
                                }
                            }
                        }

                        if (String.IsNullOrEmpty(lead.StockNumber))
                        {
                            foreach (HtmlNode span in doc.DocumentNode.SelectNodes("//span"))
                            {
                                if (span.InnerText == "Stock:")
                                    lead.StockNumber = span.NextSibling.InnerText;
                            }
                        }
                    }
                    catch
                    {

                    }

                    try
                    {
                        apiContext = GetApiContext();
                        GetUserContactDetailsCall user = new GetUserContactDetailsCall(apiContext);
                        var details = user.GetUserContactDetails(lead.EbayItemId, lead.UserId, "XXXXXXX");
                        lead.City = user.ContactAddress.CityName;
                        lead.State = user.ContactAddress.StateOrProvince;
                        lead.Zip = user.ContactAddress.PostalCode;
                        lead.PhoneNumber = user.ContactAddress.Phone;
                        lead.FullName = user.ContactAddress.Name;

                        var splitName = lead.FullName.Split(' ');
                        lead.FirstName = splitName[0];
                        lead.LastName = lead.FullName.Replace(splitName[0], "");
                    }
                    catch(Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                        if (ex.Message.StartsWith("Item is not active for item id"))
                        {
                            Trace.TraceError(ex.Message);
                            throw ex;
                        }

                        lead.FullName = "eBayUser " + lead.UserId;
                        lead.FirstName = "eBayUser";
                        lead.LastName = lead.UserId;
                    }
                    
                }

                return leads;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
                throw ex;
            }
        }

        private void InsertZohoLeads(List<ZohoLead> leads)
        {
            try
            {
                ZCRMRestClient.Initialize(config);

                ZohoOAuthClient client = ZohoOAuthClient.GetInstance();
                if (!IsTokenGenerated("XXXXXXX@XXXXXXX.com"))
                {
                    ZohoOAuthTokens tokens = client.GenerateAccessToken(grantToken);
                }

                List<ZCRMRecord> listRecord = new List<ZCRMRecord>();

                foreach (var lead in leads)
                {
                    ZCRMRecord record = ZCRMRecord.GetInstance("Leads", null); //To get ZCRMRecord instance

                    if (lead.Offers != null)
                    {
                        record = InsertZohoLeadOffer(lead);
                        listRecord.Add(record);
                    }

                    if (lead.HighBidAmount != null)
                    {
                        record = InsertZohoLeadBid(lead);
                        listRecord.Add(record);
                    }

                    if (lead.MemberMessage != null)
                    {
                        record = InsertZohoLeadMessage(lead);
                        listRecord.Add(record);
                    }
                }

                ZCRMModule moduleIns = ZCRMModule.GetInstance("Leads");
                BulkAPIResponse<ZCRMRecord> responseIns = moduleIns.CreateRecords(listRecord); //To call the create record method

                if (responseIns.HttpStatusCode != ZCRMSDK.CRM.Library.Api.APIConstants.ResponseCode.OK)
                {
                    Console.WriteLine("Error");
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
                throw ex;
            }
        }

        private ZCRMRecord InsertZohoLeadMessage(ZohoLead lead)
        {
            try
            {
                ZCRMRecord record = ZCRMRecord.GetInstance("Leads", null); //To get ZCRMRecord instance

                //lead.StockNumber = "2168";
                if (!String.IsNullOrEmpty(lead.StockNumber))
                {
                    record.SetFieldValue("Stock_Number", lead.StockNumber);

                    ZCRMModule module = ZCRMModule.GetInstance("Contacts");
                    BulkAPIResponse<ZCRMRecord> response = module.SearchByCriteria("(Cust_ID:equals:" + lead.StockNumber + ")");
                    List<ZCRMRecord> contacts = response.BulkData;

                    if (contacts != null)
                    {
                        if (contacts.Count > 0)
                        {
                            JObject customerId = new JObject()
                        {
                            { "id", contacts.First().EntityId }
                        };
                            record.SetFieldValue("Stock_Nbr", customerId);
                        }
                    }
                }

                if (lead.PageViews != null)
                    record.SetFieldValue("Page_Views", lead.PageViews);

                if (!String.IsNullOrEmpty(lead.StockNumber))
                    record.SetFieldValue("Registered_Watchers", lead.Watchers);

                if (!String.IsNullOrEmpty(lead.MemberMessage))
                    record.SetFieldValue("Description", lead.MemberMessage);

                if (!String.IsNullOrEmpty(lead.VehicleYear))
                    record.SetFieldValue("Year_of_Unit", lead.VehicleYear);

                if (!String.IsNullOrEmpty(lead.PhoneNumber))
                    record.SetFieldValue("Phone_Number", lead.PhoneNumber);

                if (!String.IsNullOrEmpty(lead.VehicleMake))
                {
                    record.SetFieldValue("Year_Make_Model", lead.VehicleMake + " " + lead.VehicleModel);
                    record.SetFieldValue("Year_Make_Model1", lead.VehicleMake + " " + lead.VehicleModel);
                }

                if (!String.IsNullOrEmpty(lead.EbayItemId))
                    record.SetFieldValue("Item_Number", lead.EbayItemId);

                if (!String.IsNullOrEmpty(lead.City))
                    record.SetFieldValue("Bidders_City", lead.City);

                if (!String.IsNullOrEmpty(lead.State))
                    record.SetFieldValue("Bidders_State", lead.State);

                record.SetFieldValue("Last_Name", lead.LastName);
                record.SetFieldValue("First_Name", lead.FirstName);
                record.SetFieldValue("Name1", lead.FullName);
                record.SetFieldValue("Name2", lead.FullName);
                record.SetFieldValue("Email", lead.Email);
                record.SetFieldValue("Email_provided", lead.Email);
                record.SetFieldValue("Ebay_User_ID", lead.UserId);

                JObject layout = new JObject()
                {
                    { "id", 4212012000003357573 }
                };

                record.SetFieldValue("Layout", layout);

                return record;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
                throw ex;
            }
        }

        private ZCRMRecord InsertZohoLeadBid(ZohoLead lead)
        {
            try
            {
                ZCRMRecord record = ZCRMRecord.GetInstance("Leads", null); //To get ZCRMRecord instance

                JArray jsonBid = new JArray();
                if (lead.HighBidAmount != null)
                {
                    record.SetFieldValue("Bid_Amount", lead.HighBidAmount);
                    record.SetFieldValue("Bid_Date", new DateTime(lead.TimeBid.Value.Year, lead.TimeBid.Value.Month, lead.TimeBid.Value.Day, lead.TimeBid.Value.Hour, lead.TimeBid.Value.Minute, lead.TimeBid.Value.Second));
                }

                //lead.StockNumber = "2168";
                if (!String.IsNullOrEmpty(lead.StockNumber))
                {
                    record.SetFieldValue("Stock_Number", lead.StockNumber);

                    ZCRMModule module = ZCRMModule.GetInstance("Contacts");
                    BulkAPIResponse<ZCRMRecord> response = module.SearchByCriteria("(Cust_ID:equals:" + lead.StockNumber + ")");
                    List<ZCRMRecord> contacts = response.BulkData;

                    if (contacts != null)
                    {
                        if (contacts.Count > 0)
                        {
                            JObject customerId = new JObject()
                        {
                            { "id", contacts.First().EntityId }
                        };
                            record.SetFieldValue("Stock_Nbr", customerId);
                        }
                    }
                }

                if (lead.PageViews != null)
                    record.SetFieldValue("Page_Views", lead.PageViews);

                if (!String.IsNullOrEmpty(lead.StockNumber))
                    record.SetFieldValue("Registered_Watchers", lead.Watchers);

                if (!String.IsNullOrEmpty(lead.VehicleYear))
                    record.SetFieldValue("Year_of_Unit", lead.VehicleYear);

                if (!String.IsNullOrEmpty(lead.PhoneNumber))
                    record.SetFieldValue("Phone_Number", lead.PhoneNumber);

                if (!String.IsNullOrEmpty(lead.VehicleMake))
                {
                    record.SetFieldValue("Year_Make_Model", lead.VehicleMake + " " + lead.VehicleModel);
                    record.SetFieldValue("Year_Make_Model1", lead.VehicleMake + " " + lead.VehicleModel);
                }

                if (!String.IsNullOrEmpty(lead.EbayItemId))
                    record.SetFieldValue("Item_Number", lead.EbayItemId);

                if (!String.IsNullOrEmpty(lead.City))
                    record.SetFieldValue("Bidders_City", lead.City);

                if (!String.IsNullOrEmpty(lead.State))
                    record.SetFieldValue("Bidders_State", lead.State);

                record.SetFieldValue("Last_Name", lead.LastName);
                record.SetFieldValue("First_Name", lead.FirstName);
                record.SetFieldValue("Name1", lead.FullName);
                record.SetFieldValue("Name2", lead.FullName);
                record.SetFieldValue("Email", lead.Email);
                record.SetFieldValue("Email_provided", lead.Email);
                record.SetFieldValue("Email_1", lead.Email);
                record.SetFieldValue("Ebay_User_ID", lead.UserId);

                JObject layout = new JObject()
                {
                    { "id", 4212012000003357020 }
                };

                record.SetFieldValue("Layout", layout);

                return record;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
                throw ex;
            }
        }

        private ZCRMRecord InsertZohoLeadOffer(ZohoLead lead)
        {
            try
            {
                ZCRMRecord record = ZCRMRecord.GetInstance("Leads", null); //To get ZCRMRecord instance

                if (lead.Offers.Count > 0)
                {
                    Offer leadOffer = lead.Offers.First();
                    record.SetFieldValue("Offer_Amount1", leadOffer.OfferAmount);
                    record.SetFieldValue("Offer_Expiration", new DateTime(leadOffer.OfferExpirationDate.Year, leadOffer.OfferExpirationDate.Month, leadOffer.OfferExpirationDate.Day, leadOffer.OfferExpirationDate.Hour, leadOffer.OfferExpirationDate.Minute, leadOffer.OfferExpirationDate.Second));
                }

                //lead.StockNumber = "2168";
                if (!String.IsNullOrEmpty(lead.StockNumber))
                {
                    record.SetFieldValue("Stock_Number", lead.StockNumber);

                    ZCRMModule module = ZCRMModule.GetInstance("Contacts");
                    BulkAPIResponse<ZCRMRecord> response = module.SearchByCriteria("(Cust_ID:equals:" + lead.StockNumber + ")");
                    List<ZCRMRecord> contacts = response.BulkData;

                    if (contacts != null)
                    {
                        if (contacts.Count > 0)
                        {
                            JObject customerId = new JObject()
                        {
                            { "id", contacts.First().EntityId }
                        };
                            record.SetFieldValue("Stock_Nbr", customerId);
                        }
                    }
                }

                if (lead.PageViews != null)
                    record.SetFieldValue("Page_Views", lead.PageViews);

                if (lead.Watchers != null)
                    record.SetFieldValue("Current_Watchers", lead.Watchers.ToString());

                if (!String.IsNullOrEmpty(lead.VehicleYear))
                    record.SetFieldValue("Year_of_Unit", lead.VehicleYear);

                if (!String.IsNullOrEmpty(lead.PhoneNumber))
                {
                    record.SetFieldValue("Phone_Number", lead.PhoneNumber);
                }
                if (!String.IsNullOrEmpty(lead.VehicleMake))
                {
                    record.SetFieldValue("Year_Make_Model", lead.VehicleMake + " " + lead.VehicleModel);
                    record.SetFieldValue("Year_Make_Model1", lead.VehicleMake + " " + lead.VehicleModel);
                }

                record.SetFieldValue("Item_Number", lead.EbayItemId);

                if (!String.IsNullOrEmpty(lead.City))
                {
                    record.SetFieldValue("Bidders_City", lead.City);
                }
                if (!String.IsNullOrEmpty(lead.State))
                {
                    record.SetFieldValue("Bidders_State", lead.State);
                }
                record.SetFieldValue("Last_Name", lead.LastName);
                record.SetFieldValue("Name1", lead.FullName);
                record.SetFieldValue("Email_provided", lead.Email);

                record.SetFieldValue("Ebay_User_ID", lead.UserId);

                JObject layout = new JObject()
                {
                    { "id", 4212012000003357243 }
                };
                //string layout = "{\"name\", \"Ebay Offer\",\"id\", \4212012000003357243\"}";

                record.SetFieldValue("Layout", layout);

                return record;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
                throw ex;
            }
        }

        private bool IsTokenGenerated(string currentUserEmail)
        {
            try
            {
                ZohoOAuthTokens tokens = ZohoOAuth.GetPersistenceHandlerInstance().GetOAuthTokens(currentUserEmail);
                return tokens != null ? true : false;
            }
            catch (Exception ex)
            {
                Trace.TraceError("Is Token Generated Error: " + ex.Message);
                return false;
            }
        }
        /// <summary>
        /// Populate eBay SDK ApiContext object with data from application configuration file
        /// </summary>
        /// <returns>ApiContext</returns>
        static ApiContext GetApiContext()
        {
            //apiContext is a singleton,
            //to avoid duplicate configuration reading
            if (apiContext != null)
            {
                return apiContext;
            }
            else
            {
                apiContext = new ApiContext();

                //set Api Server Url
                //apiContext.SoapApiServerUrl = "https://api.sandbox.ebay.com/wsapi"; // "https://api.ebay.com/wsapi";
                apiContext.SoapApiServerUrl = "https://api.ebay.com/wsapi";
                //set Api Token to access eBay Api Server
                ApiCredential apiCredential = new ApiCredential();
                apiCredential.eBayToken = "XXXXXXX";
                apiContext.ApiCredential = apiCredential;
                //set eBay Site target to US
                apiContext.Site = SiteCodeType.US;

                return apiContext;
            }
        }

        private string GetSoapXmlBody(HttpRequestMessage request)
        {
            var xmlDocument = new XmlDocument();
            xmlDocument.Load(request.Content.ReadAsStreamAsync().Result);

            var xmlData = xmlDocument.DocumentElement;
            var xmlBodyElement = xmlData.GetElementsByTagName("soapenv:Body");

            var xmlBodyNode = xmlBodyElement.Item(0);
            if (xmlBodyNode == null) throw new Exception("Function GetSoapXmlBody: Can't find SOAP-ENV:Body node");

            var xmlPayload = xmlBodyNode.FirstChild;
            if (xmlPayload == null) throw new Exception("Function GetSoapXmlBody: Can't find XML payload");

            return xmlPayload.OuterXml;
        }

        private List<ZohoLead> GetMemberMessageLead(List<ZohoLead> leads, ItemType item, MemberMessageExchangeType message)
        {
            try
            {
                var checkLead = leads.Where(i => i.UserId == message.Question.SenderID).FirstOrDefault();
                if (checkLead == null)
                {
                    var lead = new ZohoLead()
                    {
                        //ItemDescription = item.Description,
                        UserId = message.Question.SenderID,
                        //PageViews = (int)message.Item.HitCount,
                        //Watchers = (int)message.Item.WatchCount,
                        EbayItemId = item.ItemID,
                        Email = message.Question.SenderEmail,
                        MemberMessage = $"{message.CreationDate} - {message.Question.Body}\n"
                    };

                    apiContext = GetApiContext();
                    GetItemCall call = new GetItemCall(apiContext);
                    call.ItemID = item.ItemID;
                    call.IncludeWatchCount = true;
                    call.DetailLevelList.Add(DetailLevelCodeType.ReturnAll);
                    ItemType ebayItem = call.GetItem(item.ItemID);

                    lead.ItemDescription = ebayItem.Description;
                    lead.PageViews = (int)ebayItem.HitCount;
                    lead.Watchers = (int)ebayItem.WatchCount;

                    leads.Add(lead);
                }
                else
                {
                    checkLead.MemberMessage += $"{message.CreationDate} - {message.Question.Body}\n";
                }

                return leads;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
                throw ex;
            }
        }

        private List<ZohoLead> GetBestOfferLead(List<ZohoLead> leads, ItemType item, BestOfferType offer)
        {
            try
            {
                var checkLead = leads.Where(i => i.UserId == offer.Buyer.UserID).FirstOrDefault();
                if (checkLead == null)
                {
                    var lead = new ZohoLead()
                    {
                        ItemDescription = item.Description,
                        UserId = offer.Buyer.UserID,
                        EbayItemId = item.ItemID,
                        Offers = new List<Offer>()
                                    {
                                        new Offer()
                                        {
                                            OfferAmount = offer.Price.Value,
                                            OfferExpirationDate = offer.ExpirationTime
                                        }
                                    },
                        Email = offer.Buyer.Email,
                        FirstName = offer.Buyer.UserFirstName,
                        LastName = offer.Buyer.UserLastName,
                        //PageViews = (int)item.HitCount,
                        //Watchers = (int)item.WatchCount,
                    };

                    apiContext = GetApiContext();
                    GetItemCall call = new GetItemCall(apiContext);
                    call.ItemID = item.ItemID;
                    call.IncludeWatchCount = true;
                    call.DetailLevelList.Add(DetailLevelCodeType.ReturnAll);
                    ItemType ebayItem = call.GetItem(item.ItemID);

                    lead.ItemDescription = ebayItem.Description;
                    lead.PageViews = (int)ebayItem.HitCount;
                    lead.Watchers = (int)ebayItem.WatchCount;

                    leads.Add(lead);
                }
                else
                {
                    checkLead.Offers.Add(
                        new Offer()
                        {
                            OfferAmount = offer.Price.Value,
                            OfferExpirationDate = offer.ExpirationTime
                        }
                    );
                }

                return leads;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
                throw ex;
            }
        }

        private List<ZohoLead> GetBidderLead(List<ZohoLead> leads, ItemType item, OfferType bidder)
        {
            try
            {
                var checkLead = leads.Where(i => i.UserId == bidder.User.UserID).FirstOrDefault();
                if (checkLead == null)
                {
                    var lead = new ZohoLead()
                    {
                        ItemDescription = item.Description,
                        UserId = bidder.User.UserID,
                        EbayItemId = item.ItemID,
                        HighBidAmount = bidder.HighestBid.Value,
                        TimeBid = bidder.TimeBid,
                        Email = bidder.User.Email,
                        FirstName = bidder.User.UserFirstName,
                        LastName = bidder.User.UserLastName,
                        //PageViews = (int)item.HitCount,
                        //Watchers = (int)item.WatchCount,
                    };

                    apiContext = GetApiContext();
                    GetItemCall call = new GetItemCall(apiContext);
                    call.ItemID = item.ItemID;
                    call.IncludeWatchCount = true;
                    call.DetailLevelList.Add(DetailLevelCodeType.ReturnAll);
                    ItemType ebayItem = call.GetItem(item.ItemID);

                    lead.ItemDescription = ebayItem.Description;
                    lead.PageViews = (int)ebayItem.HitCount;
                    lead.Watchers = (int)ebayItem.WatchCount;

                    leads.Add(lead);
                }
                else
                {
                    checkLead.HighBidAmount = bidder.HighestBid.Value;
                    checkLead.TimeBid = bidder.TimeBid;
                }

                return leads;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
                throw ex;
            }
        }

        private List<ZohoLead> GetBidderLead(List<ZohoLead> leads, GetItemResponseType item)
        {
            try
            {
                var lead = new ZohoLead()
                {
                    ItemDescription = item.Item.Description,
                    //PageViews = (int)item.Item.HitCount,
                    //Watchers = (int)item.Item.WatchCount,
                    UserId = item.Item.SellingStatus.HighBidder.UserID,
                    EbayItemId = item.Item.ItemID,
                    HighBidAmount = item.Item.SellingStatus.CurrentPrice.Value,
                    TimeBid = item.Timestamp,
                    Email = item.Item.SellingStatus.HighBidder.Email,
                    FirstName = item.Item.SellingStatus.HighBidder.UserFirstName,
                    LastName = item.Item.SellingStatus.HighBidder.UserLastName
                };

                apiContext = GetApiContext();
                GetItemCall call = new GetItemCall(apiContext);
                call.ItemID = item.Item.ItemID;
                call.IncludeWatchCount = true;
                call.DetailLevelList.Add(DetailLevelCodeType.ReturnAll);
                ItemType ebayItem = call.GetItem(item.Item.ItemID);

                lead.ItemDescription = ebayItem.Description;
                lead.PageViews = (int)ebayItem.HitCount;
                lead.Watchers = (int)ebayItem.WatchCount;

                leads.Add(lead);

                return leads;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message);
                throw ex;
            }
        }
    }
}
