using System;

namespace FortressCodesApi.Models
{
    public class RequestMetaData
    {
        public String AppId { get; set; }
        public AppDetails AppDetails { get; set; }
        public DeviceDetails DeviceDetails { get; set; }
        public UserDetails UserDetails { get; set; }
        public string Code { get; set; }
        public string Secret { get; set; }
    }

    public class AppDetails
    {
        public string Version { get; set; }
    }
    public class DeviceDetails
    {
        public string ImeiNumber { get; set; }
        public string SerialNumber { get; set; }
        public string VendorId { get; set; }
        public string PhoneNumber { get; set; }
        public string OperatingSystem { get; set; }
        public string Version { get; set; }
        public string Model { get; set; }
        public string DeviceLevel { get; set; }
        public string DeviceCapacity { get; set; }
        public Int32 PartnerID { get; set; }
    }

    public class UserDetails
    {
        public string Email { get; set; }
        public string AccountNumber { get; set; }
        public string CountryIso { get; set; }
    }
}