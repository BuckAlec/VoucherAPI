using FortressCodesApi.Helpers;
using FortressCodesApi.Models;
using FortressCodesApi.Repsoitory;
using FortressDomain.Models.RequestObjects;
using FortressDomain.Models.ResponseObjects;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Newtonsoft.Json;

namespace FortressCodesApi.Controllers
{
    //[RequireHttps]
    //[AccessActionFilter]
    public class CodesController : ApiController
    {
        private readonly IRepository _repo;

        public CodesController(IRepository repo)
        {
            _repo = repo;
        }


        /// <summary>
        /// Gets the response message asynchronously depending on the method name passed in
        /// </summary>
        /// <param name="requestMetaData">The request meta data.</param>
        /// <param name="methodName">Name of the method.</param>
        /// <returns></returns>
        private async Task<HttpResponseMessage> GetResponseMessageAsync(RequestMetaData requestMetaData, Enumerations.MethodName methodName)
        {
            try
            {
                // Check if code exists
                var code = await _repo.GetCodeAsync(requestMetaData.Code);

                // Validate code if the code exists
                if (code == null) return Request.CreateResponse(HttpStatusCode.BadRequest,
                                                                ApiHelper.GetErrorResponseObject(Enumerations.TransactionType.InvalidVoucherCode));
                CodeResponse codeRespose = null;
                //need to use the domain context/repo for changing cover on basis

                FortressDomain.Repository.Respository domainRepo = new FortressDomain.Repository.Respository(new FortressDomain.Db.FortressContext());

                switch (methodName)
                {
                    case Enumerations.MethodName.Activate:
                        //Log the beginning of the request
                        await Logger.LogTransaction(requestMetaData.Code, (code == null) ? (int?)null : code.Id, requestMetaData,
                                                    Enumerations.GetEnumDescription(Enumerations.TransactionType.ActivationRequest), "",
                                                    _repo, requestMetaData.TransactionGuid);

                        codeRespose = await ApiHelper.ActivateCodeAsync(code, requestMetaData, _repo, domainRepo);

                        break;


                    case Enumerations.MethodName.Validate:
                        //Log the beginning of the request
                        await Logger.LogTransaction(requestMetaData.Code, (code == null) ? (int?)null : code.Id, requestMetaData,
                                                    Enumerations.GetEnumDescription(Enumerations.TransactionType.ValidationRequest), "",
                                                    _repo, requestMetaData.TransactionGuid);

                        codeRespose = await ApiHelper.ValidateCodeAsync(code, requestMetaData, _repo, domainRepo);
                        break;


                    case Enumerations.MethodName.ChangeCoverage:
                        //Log the beginning of the request
                        await Logger.LogTransaction(requestMetaData.Code, (code == null) ? (int?)null : code.Id, requestMetaData,
                                                    Enumerations.GetEnumDescription(Enumerations.TransactionType.ChangeCoverage), "",
                                                    _repo, requestMetaData.TransactionGuid);

                        Enumerations.TransactionType transactionType = ApiHelper.GetTranactionTypeByMethodName(methodName);

                        codeRespose =
                            await
                                ApiHelper.ValidateChangeCoverProRataAsync(code, requestMetaData, _repo, domainRepo,
                                    transactionType);
                        break;

                    //new code added for process charge response CHRIS
                    case Enumerations.MethodName.ProcessResponseCharge:

                        await Logger.LogTransaction(requestMetaData.Code, (code == null) ? (int?)null : code.Id, requestMetaData,
                                                   Enumerations.GetEnumDescription(Enumerations.TransactionType.ProcessChargeResponse), "",
                                                   _repo, requestMetaData.TransactionGuid);

                        transactionType = ApiHelper.GetTranactionTypeByMethodName(methodName);
                        codeRespose =
                                await
                                    ApiHelper.ValidateChangeCoverProRataAsync(code, requestMetaData, _repo, domainRepo,
                                        transactionType);
                        break;
                    case Enumerations.MethodName.StripeRefundCover:

                        await Logger.LogTransaction(requestMetaData.Code, (code == null) ? (int?)null : code.Id, requestMetaData,
                                                   Enumerations.GetEnumDescription(Enumerations.TransactionType.ProcessChargeResponse), "",
                                                   _repo, requestMetaData.TransactionGuid);

                        transactionType = ApiHelper.GetTranactionTypeByMethodName(methodName);
                        codeRespose =
                                await
                                    ApiHelper.ValidateChangeCoverProRataAsync(code, requestMetaData, _repo, domainRepo,
                                        transactionType);
                        break;
                }

                //Log the result of the request
                await Logger.LogTransaction(requestMetaData.Code, code.Id, requestMetaData, codeRespose.CodeResponseStatus, "",
                                            _repo, requestMetaData.TransactionGuid);

                //Return the response to the client
                return Request.CreateResponse(HttpStatusCode.OK, codeRespose);
            }
            catch (Exception ex)
            {
                Logger.LogTransaction(requestMetaData.Code, null, requestMetaData,
                                      Enumerations.GetEnumDescription(Enumerations.TransactionType.Error),
                                      ex.InnerException.ToString(), _repo, requestMetaData.TransactionGuid);
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex.Message);
            }
        }

        

        [HttpPost]
        [ActionName("SubscriptionFreeDays")]
        public async Task<HttpResponseMessage> SubscriptionFreeDays([FromBody] SubscriptionFreeDaysRequest subscriptionFreeDaysRequest)
        {
            try
            {

                var freeDays = await ApiHelper.ValidateFreeDaysOnSubScription(subscriptionFreeDaysRequest.DeviceID, subscriptionFreeDaysRequest.VoucherCode, subscriptionFreeDaysRequest.PricingModelID);
                SubscriptionFreeDaysResponse resp = new SubscriptionFreeDaysResponse();
                resp.FreeDays = freeDays;
                return Request.CreateResponse(HttpStatusCode.OK, resp);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex.Message);
            }
        }
        [HttpPost]
        [ActionName("PricingModelbyVoucherDeviceLevel")]
        public async Task<HttpResponseMessage> PricingModelByVoucherDeviceLevel([FromBody] PricingModelVoucherLevelRequest subscriptionFreeDaysRequest)
        {
            try
            {

                var pm = await ApiHelper.GetPricingModelNewDevice(subscriptionFreeDaysRequest.DeviceID, subscriptionFreeDaysRequest.VoucherCode);
                PricingModelVoucherLevelResponse resp = new PricingModelVoucherLevelResponse();
                resp.AnnualPrice = pm.AnnualPrice;
                resp.MonthlyPrice = pm.MonthlyPrice;
                resp.DailyPrice = pm.DailyPrice;
                resp.ExcessPrice = pm.ExcessPrice;
                resp.ReplacementPrice = pm.ReplacementPrice;
                //resp.TestingVoucher = pm.TestingVoucher;
                return Request.CreateResponse(HttpStatusCode.OK, resp);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex.Message);
            }
        }

        [HttpPost]
        [ActionName("DeviceLevel")]
        public async Task<HttpResponseMessage> DeviceLevel([FromBody]  DeviceLevelRequest deviceLevelRequest)
        {
            try
            {
                ////18-08-2017 no voucher code does not exist handled in voucher validation so it was falling over during the device level request which needs the pricing model data to do this
                //var code = await _repo.GetCodeAsync(deviceLevelRequest.VoucherCode);

                //// Validate code if the code exists
                //if (code == null) return Request.CreateResponse(HttpStatusCode.BadRequest,
                //                                                ApiHelper.GetErrorResponseObject(Enumerations.TransactionType.InvalidVoucherCode));
                var deviceLevel = await _repo.GetDeviceLevelAsync(deviceLevelRequest);

                if (String.IsNullOrEmpty(deviceLevel.Item2))
                {
                    return Request.CreateResponse(HttpStatusCode.BadRequest, ApiHelper.GetErrorResponseObject(Enumerations.TransactionType.NoDeviceLevel));
                }

                return Request.CreateResponse(HttpStatusCode.OK, deviceLevel);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex.Message);
            }
        }

        [HttpPost]
        [ActionName("DeviceLevelMem")]
        public async Task<HttpResponseMessage> DeviceLevelRequestMem([FromBody]  DeviceLevelRequestMem deviceLevelRequest)
        {
            try
            {

                var deviceLevel = await _repo.GetDeviceLevelAsyncMem(deviceLevelRequest);

                if (String.IsNullOrEmpty(deviceLevel.Item2))
                {
                    return Request.CreateResponse(HttpStatusCode.BadRequest, ApiHelper.GetErrorResponseObject(Enumerations.TransactionType.NoDeviceLevel));
                }

                return Request.CreateResponse(HttpStatusCode.OK, deviceLevel);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex.Message);
            }
        }
        [HttpPost]
        [ActionName("DeviceValue")]
        public async Task<HttpResponseMessage> DeviceValue([FromBody]  DeviceLevelValueRequest deviceValueRequest)
        {
            decimal? deviceValue = null;
            bool foundVal = false;
            DeviceLevelValueResponse deviceLevelValueResponse = new DeviceLevelValueResponse();
            try
            {




                DeviceLevelRequest deviceLevelRequest = new DeviceLevelRequest();
                deviceLevelRequest.DeviceCapacityRaw = deviceValueRequest.DeviceCapacityRaw;
                deviceLevelRequest.DeviceMake = deviceValueRequest.DeviceMake;
                deviceLevelRequest.DeviceModel = deviceValueRequest.DeviceModel;
                deviceLevelRequest.DeviceModelRaw = deviceValueRequest.DeviceModelRaw;
                deviceLevelRequest.UserCountryIso = deviceValueRequest.UserCountryIso;
                deviceLevelRequest.VoucherCode = deviceValueRequest.VoucherCode;

                var deviceLevelIDS = await _repo.GetDeviceLevelIDAsync(deviceLevelRequest);

                if (deviceLevelIDS.Item2 != 0)
                {
                    var deviceLevel = await _repo.GetDeviceLevelByIDAsync(deviceLevelIDS.Item2);
                    decimal? isoValue;
                    switch (deviceValueRequest.UserCountryIso.ToLower())
                    {
                        case "gb":
                            isoValue = deviceLevel.DeviceValueGBP;
                            break;
                        case "us":
                            isoValue = deviceLevel.DeviceValueUSD;
                            break;
                        default:
                            isoValue = deviceLevel.DeviceValueEUR;
                            break;
                    }

                    if (deviceLevel != null && isoValue.HasValue)
                    {
                        deviceValue = isoValue.Value;
                        foundVal = true;
                    }
                }
                if (deviceLevelIDS.Item3 != 0 && !foundVal)
                {
                    var device = await _repo.GetDeviceByIDAsync(deviceLevelIDS.Item3);
                    decimal? isoDeviceValue;
                    switch (deviceValueRequest.UserCountryIso.ToLower())
                    {
                        case "gb":
                            isoDeviceValue = device.DeviceValueGBP;
                            break;
                        case "us":
                            isoDeviceValue = device.DeviceValueUSD;
                            break;
                        default:
                            isoDeviceValue = device.DeviceValueEUR;
                            break;
                    }
                    if (device != null && isoDeviceValue.HasValue)
                    {
                        deviceValue = isoDeviceValue.Value;
                        foundVal = true;
                    }
                }
                deviceLevelValueResponse.DeviceValue = deviceValue;
                return Request.CreateResponse(HttpStatusCode.OK, deviceLevelValueResponse);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex.Message);
            }
        }


        [HttpPost]
        [ActionName("Validate")]
        public async Task<HttpResponseMessage> Validate([FromBody] RequestMetaData requestMetaData)
        {
            return await GetResponseMessageAsync(requestMetaData, Enumerations.MethodName.Validate);
        }

        [HttpPost]
        [ActionName("CoverValidation")]
        public async Task<HttpResponseMessage> CoverValidation([FromBody] RequestMetaDataMem requestMetaData)
        {
            FortressDomain.Repository.Respository domainRepo = new FortressDomain.Repository.Respository(new FortressDomain.Db.FortressContext());
            var code = await _repo.GetCodeAsync(requestMetaData.Code);
            CoverValidationCodeResponse codeRespose = null;
            codeRespose = await ApiHelper.ValidateChangeCoverProRataAsyncMem(code, requestMetaData, _repo, domainRepo, Enumerations.TransactionType.Validated, requestMetaData.DeviceCover);



            return Request.CreateResponse(HttpStatusCode.OK, codeRespose);

        }
        [HttpPost]
        [ActionName("Activate")]
        public async Task<HttpResponseMessage> Activate([FromBody] RequestMetaData requestMetaData)
        {
            return await GetResponseMessageAsync(requestMetaData, Enumerations.MethodName.Activate);
        }

        [HttpPost]
        [ActionName("ChangeCoverage")]
        public async Task<HttpResponseMessage> ChangeCoverage([FromBody] RequestMetaData requestMetaData)
        {
            return await GetResponseMessageAsync(requestMetaData, Enumerations.MethodName.ChangeCoverage);
        }
        [HttpPost]
        [ActionName("StripeRefundCover")]
        public async Task<HttpResponseMessage> RefundCover([FromBody] StripeRefundRequest refundRequest)
        {
            Guid requestGuid = Guid.NewGuid();
            const FortressDomain.Helpers.Enumerations.Method method = FortressDomain.Helpers.Enumerations.Method.AdminPayRefund;
            try
            {

                if (refundRequest == null)
                {
                    return Request.CreateResponse(HttpStatusCode.BadRequest, ApiHelper.GetErrorResponseObject(Enumerations.TransactionType.Error));
                }
                try
                {
                    bool hasher = _repo.IsPostAuthorized(refundRequest.Hash, refundRequest.UserId);

                    if (!hasher)
                    {
                        //return await ReturnError(FortressDomain.Helpers.Enumerations.ErrorValue.PostHashInvalid, _repo, null,
                        //                                         refundRequest.DeviceId, "", refundRequest.AppVersion,
                        //                                         method, requestGuid);
                        return Request.CreateErrorResponse(HttpStatusCode.BadRequest, FortressDomain.Helpers.Enumerations.ErrorValue.PostHashInvalid.ToString());
                    }
                }
                catch
                {
                    //return await ReturnError(FortressDomain.Helpers.Enumerations.ErrorValue.PostHashInvalid, _repo, null,
                    //                                         refundRequest.DeviceId, "", refundRequest.AppVersion,
                    //                                         method, requestGuid);
                    return Request.CreateErrorResponse(HttpStatusCode.BadRequest, FortressDomain.Helpers.Enumerations.ErrorValue.PostHashInvalid.ToString());
                }
                // //Log initial request
                //FortressDomain.Helpers.Logger.LogTransaction(JsonConvert.SerializeObject(refundRequest), _repo,
                //                       refundRequest.UserId, refundRequest.DeviceId, null, refundRequest.AppVersion, true,
                //                       method, FortressDomain.Helpers.Enumerations.TransactionType.Request, requestGuid);


                StripeRefundResponse stripeRefundResponse = await ApiHelper.ValidateBillingRefundVal(refundRequest);
                try
                {
                    HttpResponseMessage respMsg = Request.CreateResponse(HttpStatusCode.OK, stripeRefundResponse);
                    return respMsg;
                }
                catch (Exception ex)
                {
                    bool error = true;
                    return Request.CreateResponse(HttpStatusCode.BadRequest, ApiHelper.GetErrorResponseObject(Enumerations.TransactionType.Error));
                }
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ex.Message);
            }
        }
        [HttpPost]
        [ActionName("PaymentGatewayCoverage")]
        public async Task<HttpResponseMessage> PaymentGatewayCoverage([FromBody] RequestMetaData requestMetaData)
        {
            return await GetResponseMessageAsync(requestMetaData, Enumerations.MethodName.PaymentGatewayCoverage);
        }

        [HttpPost]
        [ActionName("ProcessCharge")]
        public async Task<HttpResponseMessage> ProcessCharge([FromBody] RequestMetaData requestMetaData)
        {
            return await GetResponseMessageAsync(requestMetaData, Enumerations.MethodName.ProcessCharge);
        }
        [HttpPost]
        [ActionName("ProcessChargeResponse")]
        public async Task<HttpResponseMessage> ProcessChargeResponse([FromBody] RequestMetaData requestMetaData)
        {
            return await GetResponseMessageAsync(requestMetaData, Enumerations.MethodName.ProcessResponseCharge);
        }

    }

}
