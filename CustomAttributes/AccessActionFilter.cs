using System;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Web.Http.Controllers;
using FortressCodesApi.Helpers;
using AuthorizeAttribute = System.Web.Http.AuthorizeAttribute;

namespace FortressCodesApi.CustomAttributes
{
    public class AccessActionFilter : AuthorizeAttribute
    {
        public override void OnAuthorization(HttpActionContext actionContext)
        {
            if (actionContext.Request.Headers.Authorization != null)
            {
                if (!IsAuthorized(actionContext.Request.Headers.Authorization.ToString()))
                {
                    actionContext.Response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }
            }
            else
            {
                actionContext.Response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            
        }

        private bool IsAuthorized(string secret)
        {
            if (secret == null) return false;

            string token = Encryption.GetToken();
            string decodedSecret = HttpUtility.UrlDecode(secret);
            string decryptedSecret = "";

            try
            {
                string desEncryptionKey = ConfigurationManager.AppSettings["DES_EncryptionKey"];
                string desIv = ConfigurationManager.AppSettings["DES_IV"];

                var tripleDes = new TripleDESImplementation(desEncryptionKey, desIv);
                decryptedSecret = tripleDes.Decrypt(decodedSecret);
            }
            catch (Exception e)
            {
                return false;
            }

            return (decryptedSecret == token);
        }
    }
}