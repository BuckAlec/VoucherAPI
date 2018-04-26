using System;
using System.Configuration;
using System.Text;

namespace FortressCodesApi.Helpers
{
    public static class Encryption
    {
        public static string GetToken()
        {
            Int32 unixTimestamp = (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalDays;
            string secretKey = ConfigurationManager.AppSettings["SecretKey"];

            StringBuilder sb = new StringBuilder();
            sb.Append(unixTimestamp.ToString());
            sb.Append(":");
            sb.Append(secretKey);

            return sb.ToString();
        }
    }
}