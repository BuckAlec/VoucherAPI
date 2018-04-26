using FortressCodesApi.Handlers;
using Newtonsoft.Json.Serialization;
using System.Linq;
using System.Net.Http.Formatting;
using System.Web.Http;

namespace FortressCodesApi
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Web API configuration and services

            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "CodesValidate",
                routeTemplate: "api/Codes/Validate/{requestCode}",
                defaults: new { controller = "Codes", action = "Validate", requestCode = RouteParameter.Optional }
            );

            config.Routes.MapHttpRoute(
                name: "CodesCoverValidate",
                routeTemplate: "api/Codes/CoverValidation/{requestCode}",
                defaults: new { controller = "Codes", action = "CoverValidation", requestCode = RouteParameter.Optional }
            );
            config.Routes.MapHttpRoute(
                name: "CodesActivate",
                routeTemplate: "api/Codes/Activate/{requestCode}",
                defaults: new { controller = "Codes", action = "Activate", requestCode = RouteParameter.Optional }
            );

            config.Routes.MapHttpRoute(
                name: "CodesDeviceLevel",
                routeTemplate: "api/Codes/DeviceLevel/{requestCode}",
                defaults: new { controller = "Codes", action = "DeviceLevel", requestCode = RouteParameter.Optional }
            );

            config.Routes.MapHttpRoute(
                name: "DeviceValue",
                routeTemplate: "api/Codes/DeviceValue/{requestCode}",
                defaults: new { controller = "Codes", action = "DeviceValue", requestCode = RouteParameter.Optional }
            );

            config.Routes.MapHttpRoute(
                name: "CodesDeviceLevelMem",
                routeTemplate: "api/Codes/DeviceLevelMem/{requestCode}",
                defaults: new { controller = "Codes", action = "DeviceLevelMem", requestCode = RouteParameter.Optional }
            );
            

            config.Routes.MapHttpRoute(
                name: "ChangeCoverage",
                routeTemplate: "api/Codes/ChangeCoverage/{requestCode}",
                defaults: new { controller = "Codes", action = "ChangeCoverage", requestCode = RouteParameter.Optional }
            );

            /*config.Routes.MapHttpRoute(
                name: "PaymentGatewayCoverage",
                routeTemplate: "api/Codes/PaymentGatewayCoverage/{requestCode}",
                defaults: new { controller = "Codes", action = "PaymentGatewayCoverage", requestCode = RouteParameter.Optional }
            );

            config.Routes.MapHttpRoute(
                name: "ProcessCharge",
                routeTemplate: "api/Codes/ProcessCharge/{requestCode}",
                defaults: new { controller = "Codes", action = "ProcessCharge", requestCode = RouteParameter.Optional }
            );*/

            config.Routes.MapHttpRoute(
                name: "ProcessChargeResponse",
                routeTemplate: "api/Codes/ProcessChargeResponse/{requestCode}",
                defaults: new { controller = "Codes", action = "ProcessChargeResponse", requestCode = RouteParameter.Optional }
            );
            config.Routes.MapHttpRoute(
               name: "StripeRefundCover",
               routeTemplate: "api/Codes/StripeRefundCover/{requestCode}",
               defaults: new { controller = "Codes", action = "StripeRefundCover", requestCode = RouteParameter.Optional }
           );
            config.Routes.MapHttpRoute(
               name: "SubscriptionFreeDays",
               routeTemplate: "api/Codes/SubscriptionFreeDays/{requestCode}",
               defaults: new { controller = "Codes", action = "SubscriptionFreeDays", requestCode = RouteParameter.Optional }
           );
            config.Routes.MapHttpRoute(
               name: "PricingModelbyVoucherDeviceLevel",
               routeTemplate: "api/Codes/PricingModelbyVoucherDeviceLevel/{requestCode}",
               defaults: new { controller = "Codes", action = "PricingModelbyVoucherDeviceLevel", requestCode = RouteParameter.Optional }
           );
            
            /*config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );*/


            var json = config.Formatters.JsonFormatter;

            json.SerializerSettings.PreserveReferencesHandling =
                Newtonsoft.Json.PreserveReferencesHandling.None;
            config.Formatters.Remove(config.Formatters.XmlFormatter);

            var jsonStyleFormatter = config.Formatters.OfType<JsonMediaTypeFormatter>().FirstOrDefault();
            jsonStyleFormatter.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();

            config.MessageHandlers.Add(new LoggingMessageHandler());
        }
    }
}
