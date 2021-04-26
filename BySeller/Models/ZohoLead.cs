using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace BySeller.Models
{
    public class ZohoLead
    {
        public string UserId { get; set; }
        public string FullName { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
        public string StockNumber { get; set; }
        public string PhoneNumber { get; set; }
        public string VehicleMake { get; set; }
        public string VehicleModel { get; set; }
        public string VehicleYear { get; set; }
        public string EbayItemId { get; set; }
        public string VehicleMileage { get; set; }
        public string ItemDescription { get; set; }
        public double? HighBidAmount { get; set; }
        public DateTime? TimeBid { get; set; }
        public List<Offer> Offers { get; set; }
        public string BidTransactionId { get; set; }
        public string MemberMessage { get; set; }
        public int? PageViews { get; set; }
        public int? Watchers { get; set; }
    }

    public class Offer
    {
        public double OfferAmount { get; set; }
        public DateTime OfferExpirationDate { get; set; }
    }
}