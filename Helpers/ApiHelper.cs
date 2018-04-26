using FortressCodesApi.Models;
using FortressCodesApi.Repsoitory;
using FortressCodesDomain.DbModels;
using FortressDomain.Models.Db;
using FortressDomain.Models.RequestObjects;
using FortressDomain.Models.ResponseObjects;
using Stripe;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Device = FortressDomain.Models.Db.Device;


namespace FortressCodesApi.Helpers
{
    public static class ApiHelper
    {
        private static int _maxValidationAttempts = 10;
        private static int _maxValidationAttemptsMinutes = 60;
        private static List<TransactionType> _transactionTypes = new List<TransactionType>();

        public static async Task<StripeRefundResponse> ValidateBillingRefund(StripeRefundRequest request)
        {
            var dbCodesContext = new FortressDomain.Db.FortressContext();
            var dbContext = new FortressCodesDomain.DbModels.FortressCodeContext();
            FortressDomain.Repository.Respository domainRepo = new FortressDomain.Repository.Respository(new FortressDomain.Db.FortressContext());
            Decimal AmountRefunded = request.RefundValue.Value;
            tbl_Payment tbl_payment = dbCodesContext.tbl_Payment.SingleOrDefault(e => e.Id == request.PaymentID);

            StripeRefundResponse codeResponse = new StripeRefundResponse();
            codeResponse.DeviceFound = false;

            //Check if the current date is within 365 days of the initial coverage activation
            if (tbl_payment.DateRequested >= tbl_payment.DateRequested.AddYears(1))
            {
                codeResponse.RefundResponseMessage = "Coverage is outside of the year cannot revoke coverage";
                codeResponse.Success = false;
                return codeResponse;
            }

            var vBillingVoucher = dbContext.Vouchers.SingleOrDefault(v => v.Id == tbl_payment.BillingVoucherID);
            if (vBillingVoucher == null)
            {
                codeResponse.RefundResponseMessage = "The voucher attached to this payment could not be found";
                codeResponse.Success = false;
                return codeResponse;
            }

            var vouchmetaData = vBillingVoucher.VoucherMetadatas.SingleOrDefault();
            if (vouchmetaData == null)
                return codeResponse;


            var devicePricingModel = dbContext.PricingModels.SingleOrDefault(pm => pm.Id == vouchmetaData.PricingModelID);

            if (devicePricingModel == null)
            {
                codeResponse.RefundResponseMessage = "The pricing model attached to this payments pricing model could not be found";
                codeResponse.Success = false;
                return codeResponse;
            }


            //Validation
            //Device has active cover to refund
            //Check if there was a voucher code used during the billing process as this would also need to be refunded
            //Check if the current date is within 365 days of the initial coverage activation
            //If there is no coverage against the payments device look at any other devices the user has check active cover against these
            //
            Int32 iDeviceID = tbl_payment.DeviceId;

            var device = await domainRepo.GetDeviceByIdAsync(iDeviceID);
            if (device == null)
                return codeResponse;



            DeviceCover activeDeviceCover = null;
            //Check if user still owns the device - only check the device cover of this device if the user still has ownership
            if (device.User.Id == tbl_payment.UserId)
                activeDeviceCover = await domainRepo.GetActiveDeviceCoverByDeviceIdAsync(iDeviceID);

            if (activeDeviceCover != null)
                codeResponse.DeviceFound = true;
            //Create list to hold any device cover that the paymnents user has
            List<DeviceCover> dc = new List<DeviceCover>();
            //If there is no coverage against the payments device look at any other devices the user has check active cover against these
            if (activeDeviceCover == null)
            {
                var devices = await domainRepo.GetDevicesByUserIdAsync(tbl_payment.UserId);
                if (devices.Any())
                {
                    foreach (var dev in devices)
                    {
                        var activeUserDeviceCover = await domainRepo.GetActiveDeviceCoverByDeviceIdAsync(iDeviceID);
                        if (activeUserDeviceCover != null)
                            dc.Add(activeUserDeviceCover);
                    }

                }
                //If the user has any devices in cover order by the date cover began and take the oldest
                if (dc.Any())
                {
                    activeDeviceCover = dc.OrderBy(a => a.ActivatedDate).Take(1).SingleOrDefault();
                }
            }
            //Check if any cover if found against the payments user/device or any other device the user currently has ownership of.
            if (activeDeviceCover == null)
            {
                codeResponse.RefundResponseMessage = "Could not locate device cover on this payments device/user or any other device this user has owernship of";
                codeResponse.Success = false;
                return codeResponse;
            }


            var currentCoverVoucher = dbContext.Vouchers.SingleOrDefault(v => v.vouchercode == activeDeviceCover.Voucher);
            if (currentCoverVoucher == null)
            {
                codeResponse.RefundResponseMessage = "The voucher attached to the devices current cover could not be found";
                codeResponse.Success = false;
                return codeResponse;
            }

            var currentCoverVoucherMetaData = currentCoverVoucher.VoucherMetadatas.SingleOrDefault();
            if (currentCoverVoucherMetaData == null)
            {
                codeResponse.RefundResponseMessage = "The meta data information attached to the devices current cover voucher could not be found";
                codeResponse.Success = false;
                return codeResponse;
            }


            var currentCoverVoucherPricingModel = dbContext.PricingModels.SingleOrDefault(pm => pm.Id == currentCoverVoucherMetaData.PricingModelID);

            if (currentCoverVoucherPricingModel == null)
                return codeResponse;

            TimeSpan tsDaysRemaining = activeDeviceCover.EndDate.Subtract(DateTime.UtcNow);

            Double totalDaysRemaining = Math.Ceiling(tsDaysRemaining.TotalDays);
            if (totalDaysRemaining < 0)
            {
                totalDaysRemaining = 0;
            }


            //Work out the percentage rate of the original pricing models daily rate compared to the daily rate associated with the current device cover
            int multiplier = (int)(devicePricingModel.DailyPrice.Value / currentCoverVoucherPricingModel.DailyPrice.Value);


            //The multiplier of what was refunded compared to the original amount
            decimal refundMultiplier = (AmountRefunded / tbl_payment.TransactionValue);
            int proRataDays = (int)((vBillingVoucher.membershiplength.HasValue ? vBillingVoucher.membershiplength.Value : 365) * refundMultiplier);


            proRataDays = proRataDays * multiplier;
            ////Get the number of days pro rata'd against the current device cover.


            if (tbl_payment.VoucherID != tbl_payment.BillingVoucherID)
            {
                //now check if a voucher with coverage was applied during the billing process
                var vBillingWithVoucher = dbContext.Vouchers.SingleOrDefault(v => v.Id == tbl_payment.VoucherID);
                if (vBillingWithVoucher == null)
                    return null;

                var vouchmetaDataWith = vBillingWithVoucher.VoucherMetadatas.SingleOrDefault();
                if (vouchmetaDataWith == null)
                    return codeResponse;


                var deviceWithPricingModel = dbContext.PricingModels.SingleOrDefault(pm => pm.Id == vouchmetaDataWith.PricingModelID);


                int multiplierWith = (int)(deviceWithPricingModel.DailyPrice.Value / currentCoverVoucherPricingModel.DailyPrice.Value);


                //The multiplier of what was refunded compared to the original amount
                decimal refundMultiplierWith = (AmountRefunded / tbl_payment.TransactionValue);
                int proRataDaysWith = (int)((vBillingWithVoucher.membershiplength.HasValue ? vBillingWithVoucher.membershiplength.Value : 365) * refundMultiplierWith);

                proRataDays = proRataDays + (proRataDaysWith * multiplier);
            }

            DateTime newCoverEndDate = DateTime.UtcNow;
            DateTime holdingEndDate = (activeDeviceCover.ActivatedDate.AddDays(proRataDays));
            holdingEndDate = activeDeviceCover.EndDate.AddDays(proRataDays * -1);
            codeResponse.OldDeviceCoverEndDate = activeDeviceCover.EndDate;

            //Always void the current Cover and set its end date today. If the new end date is greater than today then we also need to create a new device cover entry
            activeDeviceCover.Void = true;
            activeDeviceCover.EndDate = DateTime.UtcNow;


            if (holdingEndDate > DateTime.UtcNow)
            {
                DeviceCover dcNew = new DeviceCover();
                dcNew.DeviceId = activeDeviceCover.DeviceId;
                dcNew.AddedDate = DateTime.UtcNow;
                dcNew.ModifiedDate = DateTime.UtcNow;
                dcNew.ActivatedDate = DateTime.UtcNow;
                dcNew.EndDate = holdingEndDate;
                dcNew.MembershipLength = proRataDays.ToString();
                dcNew.MembershipTier = activeDeviceCover.MembershipTier;
                dcNew.Voucher = activeDeviceCover.Voucher;

                domainRepo.CreateDeviceCoverAsync(dcNew);
            }

            domainRepo.UpdateDeviceCoverAsync(activeDeviceCover);



            codeResponse.DeviceID = activeDeviceCover.DeviceId;
            codeResponse.NewDeviceCoverEndDate = (holdingEndDate > DateTime.UtcNow ? holdingEndDate : DateTime.UtcNow);


            codeResponse.RefundResponseMessage = "Coverage was successfully modified";
            codeResponse.Success = true;

            return codeResponse;
        }


        public static async Task<StripeRefundResponse> ValidateBillingRefundVal(StripeRefundRequest request)
        {
            var dbCodesContext = new FortressDomain.Db.FortressContext();
            var dbContext = new FortressCodesDomain.DbModels.FortressCodeContext();
            FortressDomain.Repository.Respository domainRepo = new FortressDomain.Repository.Respository(new FortressDomain.Db.FortressContext());
            //Decimal AmountRefunded = request.RefundValue;
            tbl_Payment tbl_payment = dbCodesContext.tbl_Payment.SingleOrDefault(e => e.Id == request.PaymentID);

            StripeRefundResponse codeResponse = new StripeRefundResponse();
            codeResponse.DeviceFound = false;

            var vBillingVoucher = dbContext.Vouchers.SingleOrDefault(v => v.Id == tbl_payment.BillingVoucherID);
            if (vBillingVoucher == null)
            {
                codeResponse.RefundResponseMessage = "The voucher attached to this payment could not be found";
                codeResponse.Success = false;
                return codeResponse;
            }

            var vouchmetaData = vBillingVoucher.VoucherMetadatas.SingleOrDefault();
            if (vouchmetaData == null)
                return codeResponse;


            var devicePricingModel = dbContext.PricingModels.SingleOrDefault(pm => pm.Id == vouchmetaData.PricingModelID);

            if (devicePricingModel == null)
            {
                codeResponse.RefundResponseMessage = "The pricing model attached to this payments pricing model could not be found";
                codeResponse.Success = false;
                return codeResponse;
            }


            //Validation
            //Device has active cover to refund
            //Check if there was a voucher code used during the billing process as this would also need to be refunded
            //Check if the current date is within 365 days of the initial coverage activation
            //If there is no coverage against the payments device look at any other devices the user has check active cover against these
            //
            Int32 iDeviceID = tbl_payment.DeviceId;

            var device = await domainRepo.GetDeviceByIdAsync(iDeviceID);
            if (device == null)
                return codeResponse;



            DeviceCover activeDeviceCover = null;
            //Check if user still owns the device - only check the device cover of this device if the user still has ownership
            if (device.User.Id == tbl_payment.UserId)
                activeDeviceCover = await domainRepo.GetActiveDeviceCoverByDeviceIdAsync(iDeviceID);

            if (activeDeviceCover != null)
                codeResponse.DeviceFound = true;
            //Create list to hold any device cover that the paymnents user has
            List<DeviceCover> dc = new List<DeviceCover>();
            //If there is no coverage against the payments device look at any other devices the user has check active cover against these
            if (activeDeviceCover == null)
            {
                var devices = await domainRepo.GetDevicesByUserIdAsync(tbl_payment.UserId);
                if (devices.Any())
                {
                    foreach (var dev in devices)
                    {
                        var activeUserDeviceCover = await domainRepo.GetActiveDeviceCoverByDeviceIdAsync(iDeviceID);
                        if (activeUserDeviceCover != null)
                            dc.Add(activeUserDeviceCover);
                    }

                }
                //If the user has any devices in cover order by the date cover began and take the oldest
                if (dc.Any())
                {
                    activeDeviceCover = dc.OrderBy(a => a.ActivatedDate).Take(1).SingleOrDefault();
                }
            }
            //Check if any cover if found against the payments user/device or any other device the user currently has ownership of.
            if (activeDeviceCover == null)
            {
                codeResponse.RefundResponseMessage = "Could not locate device cover on this payments device/user or any other device this user has owernship of";
                codeResponse.Success = false;
                return codeResponse;
            }

            TimeSpan span = activeDeviceCover.EndDate - DateTime.Now;
            if (span.Days > 0)
            {
                decimal coverageValueRem = (devicePricingModel.DailyPrice.Value * span.Days);
                codeResponse.RemainingCoverageValue = coverageValueRem - decimal.Parse("25.00");
                if (coverageValueRem < decimal.Parse("25.00"))
                    codeResponse.validCoverageValue = false;
                else
                    codeResponse.validCoverageValue = true;

            }
            var currentCoverVoucher = dbContext.Vouchers.SingleOrDefault(v => v.vouchercode == activeDeviceCover.Voucher);
            if (currentCoverVoucher == null)
            {
                codeResponse.RefundResponseMessage = "The voucher attached to the devices current cover could not be found";
                codeResponse.Success = false;
                return codeResponse;
            }

            var currentCoverVoucherMetaData = currentCoverVoucher.VoucherMetadatas.SingleOrDefault();
            if (currentCoverVoucherMetaData == null)
            {
                codeResponse.RefundResponseMessage = "The meta data information attached to the devices current cover voucher could not be found";
                codeResponse.Success = false;
                return codeResponse;
            }


            var currentCoverVoucherPricingModel = dbContext.PricingModels.SingleOrDefault(pm => pm.Id == currentCoverVoucherMetaData.PricingModelID);

            if (currentCoverVoucherPricingModel == null)
                return codeResponse;

            TimeSpan tsDaysRemaining = activeDeviceCover.EndDate.Subtract(DateTime.UtcNow);

            Double totalDaysRemaining = Math.Ceiling(tsDaysRemaining.TotalDays);
            if (totalDaysRemaining < 0)
            {
                totalDaysRemaining = 0;
            }

            DateTime newCoverEndDate = DateTime.UtcNow;

            //Always void the current Cover and set its end date today. If the new end date is greater than today then we also need to create a new device cover entry
            activeDeviceCover.Void = true;
            activeDeviceCover.EndDate = DateTime.UtcNow;
            if (request.ModifyCoverage)
            {
                DeviceCover dcNew = new DeviceCover();
                dcNew.DeviceId = activeDeviceCover.DeviceId;
                dcNew.AddedDate = DateTime.UtcNow;
                dcNew.ModifiedDate = DateTime.UtcNow;
                dcNew.ActivatedDate = DateTime.UtcNow;
                dcNew.EndDate = DateTime.UtcNow;
                dcNew.MembershipLength = "0";
                dcNew.MembershipTier = activeDeviceCover.MembershipTier;
                dcNew.Voucher = activeDeviceCover.Voucher;
                domainRepo.CreateDeviceCoverAsync(dcNew);
                domainRepo.UpdateDeviceCoverAsync(activeDeviceCover);
                codeResponse.RefundResponseMessage = "Coverage was successfully modified";
            }
            else
            {
                codeResponse.RefundResponseMessage = "Coverage was successfully validated";
            }
            codeResponse.DeviceID = activeDeviceCover.DeviceId;



            codeResponse.Success = true;

            return codeResponse;
        }



        public static async Task<CodeResponse> ValidateCodeAsync(Voucher code, RequestMetaData requestMetaData, IRepository repo, FortressDomain.Repository.IRepository domainRepo)
        {
            try
            {
                _maxValidationAttempts = Convert.ToInt32(ConfigurationManager.AppSettings["MaxValidationAttempts"]);
                _maxValidationAttemptsMinutes =
                    Convert.ToInt32(ConfigurationManager.AppSettings["MaxValidationAttemptsMinutes"]);

                // Get all transaction types to save multiple calls to db
                _transactionTypes = await repo.GetAllTransactionTypesAsync();

                // Default result to 'error'
                var result = GetErrorResponseObject();



                //subscription validation logic
                var subscriptions = await domainRepo.GetActiveSubscriptionPlanByDeviceIdAsync(requestMetaData.DeviceDetails.DeviceID);
                if (subscriptions != null)
                {
                    result = GetErrorResponseObject();
                    result.CodeResponseStatus =
                                    Enumerations.GetEnumDescription(Enumerations.TransactionType.ActiveSubscription);
                    return result;
                }


                TransactionType transactionType;
                var userID = 0;
                var deviceCheck = await domainRepo.GetDeviceByIdAsync(requestMetaData.DeviceDetails.DeviceID);
                if (deviceCheck != null)
                {
                    userID = deviceCheck.User.Id;
                }
                // Check if the code is available to be claimed
                if (code.TransactionType == null)
                {
                    // Null in database means voucher is available      
                    if (IsCodeExpired(code))
                    {
                        //If code has expired, return time limit response
                        result = await UpdateCodeAndGetTimeLimitCodeResponseAsync(repo, code, result);
                    }
                    else
                    {

                        //check if device is already in grace period, if it is, return error
                        var device = await domainRepo.GetDeviceByIdAsync(requestMetaData.DeviceDetails.DeviceID);
                        if (device != null)
                        {
                            if (device.MissingDevice.HasValue && device.MissingDevice.Value)
                            {
                                //var devicecovers = await domainRepo.GetDeviceCoversByDeviceIDMethodAsync(device.Id, "VoucherActivation");
                                //if (devicecovers.Any())
                                //{
                                //    result = GetErrorResponseObject();
                                //    result.CodeResponseStatus =
                                //                    Enumerations.GetEnumDescription(Enumerations.TransactionType.GracePeriod);
                                //    return result;
                                //}
                                var transactions = await domainRepo.GetTransactionsByDeviceIDMethodAsync(device.Id, "VoucherActivation");
                                if (transactions.Any())
                                {
                                    result = GetErrorResponseObject();
                                    result.CodeResponseStatus =
                                                    Enumerations.GetEnumDescription(Enumerations.TransactionType.GracePeriod);
                                    return result;
                                }

                            }
                        }

                        // If code has been used the maximum number of times: return max-use response
                        transactionType = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.Activated));

                        if (HasReachedMaxCount(code.MaxCount,
                            await repo.GetCodeUsageCountAsync(code.Id, transactionType.Id)))
                        {
                            return await UpdateCodeAndGetMaxReachedCodeResponseAsync(repo, code, result);
                        }

                        // Check if code needs extra validation - will return 
                        if (requestMetaData.DeviceDetails.Model != null ||
                            requestMetaData.UserDetails.CountryIso != null)
                        {

                            //result = await CheckExtraValidationAsync(repo, requestMetaData, code);
                            string codeResponseStatus = await CheckExtraValidationAsync(repo, requestMetaData, code);

                            if (codeResponseStatus != "")
                            {
                                result.CodeResponseStatus = codeResponseStatus;
                                return result;
                            }
                            /*if (result.CodeResponseStatus !=
                                Enumerations.GetEnumDescription(Enumerations.TransactionType.Validated))
                                return result;*/
                            return await GetResponseObject(repo, domainRepo, code, Enumerations.TransactionType.Validated, requestMetaData);
                        }
                        return await UpdateCodeAndGetValidatedResponseAsync(repo, domainRepo, code, requestMetaData);
                    }
                }
                else
                {

                    switch (code.TransactionType.Name.ToLower())
                    {
                        case null:
                        case "":
                        case "missing device":
                            // Check if code needs extra validation
                            if (requestMetaData.DeviceDetails.Model != null || requestMetaData.UserDetails.CountryIso != null)
                            {
                                //result = await CheckExtraValidationAsync(repo, requestMetaData, code);
                                string codeResponseStatus = await CheckExtraValidationAsync(repo, requestMetaData, code);

                                if (codeResponseStatus == "")
                                {
                                    return await GetResponseObject(repo, domainRepo, code, Enumerations.TransactionType.Validated, requestMetaData);
                                }
                                else
                                {
                                    result.CodeResponseStatus = codeResponseStatus;
                                    return result;
                                }
                            }
                            else
                            {
                                // Code is available to be claimed - Check if code has expired
                                result = IsCodeExpired(code)
                                    ? await UpdateCodeAndGetTimeLimitCodeResponseAsync(repo, code, result)
                                    : await UpdateCodeAndGetValidatedResponseAsync(repo, domainRepo, code, requestMetaData);
                            }
                            break;
                        case "validated":
                            /** 
                         * Code has been previously 'Validated' - 'Lock' if locking criteria has been met
                         * 
                         * Locking criteria: Lock code if @MaxValidationAttempts 'Validated' 
                         * responses have been returned within @MaxValidationAttemptsMinutes minutes 
                         * (3 failed activations)
                         */

                            //Check if max use count has been reached
                            transactionType = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.Activated));

                            if (HasReachedMaxCount(code.MaxCount,
                                await repo.GetCodeUsageCountAsync(code.Id, transactionType.Id)))
                            {
                                return await UpdateCodeAndGetMaxReachedCodeResponseAsync(repo, code, result);
                            }



                            //check to see if the voucher has been validated against this device
                            //19/02/2018 Move transaction check to device cover table.
                            //var validatedTrans = await domainRepo.GetTransactionsByDeviceIDMethodVoucherAsync(requestMetaData.DeviceDetails.DeviceID, "VoucherValidation", code.vouchercode);
                            var validatedDeviceCover = await domainRepo.GetDeviceCoversByDeviceIDMethodVoucherAsync(requestMetaData.DeviceDetails.DeviceID, userID, "VoucherValidation", code.vouchercode);
                            if (validatedDeviceCover.Any())
                            {
                                result = GetErrorResponseObject();
                                result.CodeResponseStatus =
                                                Enumerations.GetEnumDescription(Enumerations.TransactionType.InUse);
                                return result;
                            }

                            // Get the Validated status code to check the transaction log
                            transactionType = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.Validated));

                            if (transactionType == null) return result;

                            // Check if code needs extra validation
                            if (requestMetaData.DeviceDetails.Model != null || requestMetaData.UserDetails.CountryIso != null)
                            {
                                string codeResponseStatus = await CheckExtraValidationAsync(repo, requestMetaData, code);
                                if (codeResponseStatus != "")
                                {
                                    result.CodeResponseStatus = codeResponseStatus;
                                    return result;
                                }
                            }

                            // If code is voucher code, check transaction log for 'Validation' attempts made and 'Lock' if necessary
                            // TODO: check code type -> if(code.codeType = Enumerations.CodeType.VoucherCode)
                            if (
                                await
                                    repo.GetCodeAttemptsInTimeLimitAsync(code.Id, _maxValidationAttemptsMinutes,
                                        transactionType.Id) >= _maxValidationAttempts)
                            {
                                // Get the 'Locked' status code
                                transactionType = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.Locked));

                                if (transactionType == null) return result;
                                code.TransactionTypeId = transactionType.Id;

                                // Update the code with the 'Locked' status - return the error status if there was an issue updating voucher
                                if (await repo.UpdateVoucherAsync(code) != 1) return result;

                                // Code successfully updated to 'Locked' status - return 'Locked' response result
                                result.CodeResponseStatus =
                                    Enumerations.GetEnumDescription(Enumerations.TransactionType.Locked);
                            }
                            else
                            {
                                // A valid 'Validation' attempt made - return 'Validated' result
                                result = await GetResponseObject(repo, domainRepo, code, Enumerations.TransactionType.Validated, requestMetaData);
                            }


                            break;
                        case "maximum use":
                            // Code has been used maximum amount of times - return 'maximum use' response
                            result.CodeResponseStatus =
                                Enumerations.GetEnumDescription(Enumerations.TransactionType.MaximumUse);
                            break;
                        case "time limit":
                            // Code has reached its 'time limit' - return 'time limit' response
                            result.CodeResponseStatus =
                                Enumerations.GetEnumDescription(Enumerations.TransactionType.TimeLimit);
                            break;
                        case "activated":
                            // Code has been previously 'activated' 
                            transactionType = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.Activated));

                            if (HasReachedMaxCount(code.MaxCount,
                                await repo.GetCodeUsageCountAsync(code.Id, transactionType.Id)))
                            {
                                return await UpdateCodeAndGetMaxReachedCodeResponseAsync(repo, code, result);
                            }

                            //check to see if the voucher has been validated against this device
                            var activatedDeviceCover = await domainRepo.GetDeviceCoversByDeviceIDMethodVoucherAsync(requestMetaData.DeviceDetails.DeviceID, userID, "VoucherActivation", code.vouchercode);
                            if (activatedDeviceCover.Any())
                            {
                                result = GetErrorResponseObject();
                                result.CodeResponseStatus =
                                                Enumerations.GetEnumDescription(Enumerations.TransactionType.InUse);
                                return result;
                            }

                            // Get the activated transaction type
                            transactionType =
                                transactionType = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.Activated));

                            // Check if code has been used max amount of times if it is a multi-use code
                            if (code.multiuse)
                            {
                                // Check if code needs extra validation
                                if (requestMetaData.DeviceDetails.Model != null || requestMetaData.UserDetails.CountryIso != null)
                                {
                                    //result = await CheckExtraValidationAsync(repo, requestMetaData, code);
                                    string codeResponseStatus = await CheckExtraValidationAsync(repo, requestMetaData, code);
                                    if (codeResponseStatus != "")
                                    {
                                        result.CodeResponseStatus = codeResponseStatus;
                                        return result;
                                    }
                                }
                                // If code has MaxCount value - code can have limited amount of usages
                                if (code.MaxCount.HasValue)
                                {
                                    if (Convert.ToInt32(code.MaxCount) >= 0)
                                    {
                                        // Check if code has been used maximum amount of times
                                        if (HasReachedMaxCount(code.MaxCount,
                                            await repo.GetCodeUsageCountAsync(code.Id, transactionType.Id)))
                                        {
                                            //Code can be used more than once - but has reached max-use
                                            result = await UpdateCodeAndGetMaxReachedCodeResponseAsync(repo, code, result);
                                        }
                                        else
                                        {
                                            result = await GetResponseObject(repo, domainRepo, code, Enumerations.TransactionType.Validated, requestMetaData);
                                        }
                                    }
                                    else
                                    {
                                        result.CodeResponseStatus =
                                            Enumerations.GetEnumDescription(Enumerations.TransactionType.InUse);
                                    }
                                }
                                else
                                {
                                    result = await GetResponseObject(repo, domainRepo, code, Enumerations.TransactionType.Validated, requestMetaData);
                                }

                            }
                            else
                            {
                                result.CodeResponseStatus =
                                    Enumerations.GetEnumDescription(Enumerations.TransactionType.InUse);
                            }

                            break;
                        case "locked":
                            // Code has been 'locked' - return 'locked' response
                            result.CodeResponseStatus =
                                Enumerations.GetEnumDescription(Enumerations.TransactionType.Locked);
                            break;
                        case "cancellation approved":
                            result.CodeResponseStatus =
                                Enumerations.GetEnumDescription(Enumerations.TransactionType.CancellationApproved);
                            break;
                        case "cancellation declined":
                            result.CodeResponseStatus =
                                Enumerations.GetEnumDescription(Enumerations.TransactionType.CancellationDeclined);
                            break;
                        case "cancelled":
                            result.CodeResponseStatus =
                                Enumerations.GetEnumDescription(Enumerations.TransactionType.Cancelled);
                            break;
                        default:
                            // No valid response found - return 'error'
                            result.CodeResponseStatus =
                                Enumerations.GetEnumDescription(Enumerations.TransactionType.Error);
                            throw new Exception("Error processing code");
                            break;
                    }
                }
                return result;

            }
            catch (Exception e)
            {
                return new CodeResponse();
            }
        }
        private static async Task<string> CheckExtraSubscriptionValidationAsync(IRepository repo, RequestMetaData requestMetaData, Voucher code)
        {
            var result = GetErrorResponseObject();
            var voucherMetaData = code.VoucherMetadatas.FirstOrDefault();
            if (voucherMetaData != null)
            {
                /// If the voucher is a gateway voucher
                if (voucherMetaData.VoucherTypeID == (Int32)Enumerations.VoucherType.Subscription)
                {
                    /// Attempt to validate the gateway voucher
                    if (!await CheckGatewayVoucherValidationAsync(repo, requestMetaData, code))
                    {
                        //result.CodeResponseStatus = Enumerations.GetEnumDescription(Enumerations.TransactionType.MissingFamilyLevel);
                        return Enumerations.GetEnumDescription(Enumerations.TransactionType.MissingFamilyLevel);
                    }
                }
            }

            /// Check if device and voucher countries match
            if (!await UserDeviceIsAllowedForVoucherCountryAsync(repo, code,
                requestMetaData.UserDetails.CountryIso))
            {
                //result.CodeResponseStatus = Enumerations.GetEnumDescription(Enumerations.TransactionType.TerritoryMismatch);
                return Enumerations.GetEnumDescription(Enumerations.TransactionType.TerritoryMismatch);
            }

            //return await GetResponseObject(code, Enumerations.TransactionType.Validated, requestMetaData);
            return "";
        }

        private static async Task<string> CheckExtraValidationAsync(IRepository repo, RequestMetaData requestMetaData, Voucher code)
        {
            var result = GetErrorResponseObject();
            var voucherMetaData = code.VoucherMetadatas.FirstOrDefault();

            if (voucherMetaData != null)
            {
                /// If the voucher is a gateway voucher
                if (voucherMetaData.VoucherTypeID == (Int32)Enumerations.VoucherType.Gateway)
                {
                    /// Attempt to validate the gateway voucher
                    if (!await CheckGatewayVoucherValidationAsync(repo, requestMetaData, code))
                    {
                        //result.CodeResponseStatus = Enumerations.GetEnumDescription(Enumerations.TransactionType.MissingFamilyLevel);
                        return Enumerations.GetEnumDescription(Enumerations.TransactionType.MissingFamilyLevel);
                    }
                }
            }

            /// Check if device and voucher countries match
            if (!await UserDeviceIsAllowedForVoucherCountryAsync(repo, code,
                requestMetaData.UserDetails.CountryIso))
            {
                //result.CodeResponseStatus = Enumerations.GetEnumDescription(Enumerations.TransactionType.TerritoryMismatch);
                return Enumerations.GetEnumDescription(Enumerations.TransactionType.TerritoryMismatch);
            }

            //return await GetResponseObject(code, Enumerations.TransactionType.Validated, requestMetaData);
            return "";
        }


        /// <summary>
        /// Checks the the Voucher to determine if it is a gateway Voucher.  
        /// If it is, then the Voucher Level is set to the same Level in that particular Family as the Device. 
        /// If Voucher is B and Device is A, Voucher becomes A (in that Family), conversely if Voucher is A and Device is B, Voucher becomes B (in that Family)
        /// </summary>
        /// <param name="repo">The repository.</param>
        /// <param name="requestMetaData">The request meta data.</param>
        /// <param name="voucher">The voucher object.</param>
        private static async Task<Boolean> CheckGatewayVoucherValidationAsync(IRepository repo, RequestMetaData requestMetaData, Voucher voucher)
        {
            Boolean ret = false;

            //There should only be one voucher metadata entry per voucher
            var voucherMetaData = voucher.VoucherMetadatas.FirstOrDefault();

            if (voucherMetaData != null)
            {
                // if the voucher is a gateway
                if (voucherMetaData.VoucherTypeID == (Int32)Enumerations.VoucherType.Gateway)
                {

                    //Get pricing model by family and level
                    var pricingModel = repo.GetSingleOrDefault<PricingModel>(pm => pm.FamilyId == voucherMetaData.PricingModel.FamilyId &&
                                                                                   pm.Level.Name == requestMetaData.DeviceDetails.DeviceLevel &&
                                                                                   pm.Tier.Id == voucherMetaData.PricingModel.TeirId);
                    if (pricingModel != null)
                    {
                        // Log the Old Level
                        await Logger.LogTransaction(requestMetaData.Code, (voucher == null) ? (Int32?)null : voucher.Id,
                                                    requestMetaData,
                                                    Enumerations.GetEnumDescription(Enumerations.TransactionType.GatewayVoucherAlteration),
                                                    "OLD LEVEL: " + voucherMetaData.PricingModel.Level.Name, repo, requestMetaData.TransactionGuid);

                        // update the Vouchers pricing model id to be that of the device in the same family
                        voucherMetaData.PricingModelID = pricingModel.Id;
                        await repo.UpdateAsync<VoucherMetadata>(voucherMetaData);

                        // Log the New Level
                        await Logger.LogTransaction(requestMetaData.Code, (voucher == null) ? (Int32?)null : voucher.Id,
                                                    requestMetaData,
                                                    Enumerations.GetEnumDescription(Enumerations.TransactionType.GatewayVoucherAlteration),
                                                    "NEW LEVEL: " + pricingModel.Level.Name, repo, requestMetaData.TransactionGuid);

                        // log the change for reporting
                        await Logger.LogTransaction(requestMetaData.Code, (voucher == null) ? (Int32?)null : voucher.Id,
                                                    requestMetaData,
                                                    Enumerations.GetEnumDescription(Enumerations.TransactionType.GatewayVoucherAlteration),
                                                    String.Empty, repo, requestMetaData.TransactionGuid);
                        ret = true;
                    }
                }
            }
            return ret;
        }


        private static async Task<bool> UserDeviceIsAllowedForVoucherCountryAsync(IRepository repo, Voucher code, string deviceCountryIso)
        {
            bool result = false;
            Voucher voucher = await repo.GetCodeAsync(code.vouchercode);

            var voucherMetaData = voucher.VoucherMetadatas.FirstOrDefault();
            if (voucherMetaData != null)
            {
                if (voucherMetaData.PricingModel.Country.ISO == deviceCountryIso)
                {
                    result = true;
                }
            }

            return result;
        }

        private static async Task<bool> UserDeviceIsAllowedForVoucherCategoryAsync(IRepository repo, Voucher code, string deviceLevel)
        {
            bool result = false;
            Voucher voucher = await repo.GetCodeAsync(code.vouchercode);

            var voucherMetaData = voucher.VoucherMetadatas.FirstOrDefault();
            if (voucherMetaData != null)
            {
                result = (voucherMetaData.PricingModel.Level.Name == deviceLevel);

            }

            return result;
        }

        private static bool HasReachedMaxCount(int? maxCount, int getCodeUsageCount)
        {
            return (maxCount != null) && (maxCount <= getCodeUsageCount);
        }

        private static bool IsCodeExpired(Voucher code)
        {
            return code.expirydate.Date < DateTime.UtcNow.Date;
        }


        public static async Task<CodeResponse> ActivateCodeAsync(Voucher code, RequestMetaData requestMetaData, IRepository repo, FortressDomain.Repository.IRepository domainRepo)
        {
            //check if device is already in grace period, if it is, return error
            var device = await domainRepo.GetDeviceByIdAsync(requestMetaData.DeviceDetails.DeviceID);
            if (device != null)
            {
                if (device.MissingDevice.HasValue && device.MissingDevice.Value)
                {
                    //var deviceCovers = await domainRepo.GetDeviceCoversByDeviceIDMethodAsync(device.Id, "VoucherActivation");
                    //if (deviceCovers.Any())
                    //{
                    //    var result = GetErrorResponseObject();
                    //    result.CodeResponseStatus =
                    //                    Enumerations.GetEnumDescription(Enumerations.TransactionType.GracePeriod);
                    //    return result;
                    //}

                    var transactions = await domainRepo.GetTransactionsByDeviceIDMethodAsync(device.Id, "VoucherActivation");
                    if (transactions.Any())
                    {
                        var result = GetErrorResponseObject();
                        result.CodeResponseStatus =
                                        Enumerations.GetEnumDescription(Enumerations.TransactionType.GracePeriod);
                        return result;
                    }
                }
            }

            // Get the 'Validated' transaction type
            if (_transactionTypes == null || _transactionTypes.Count == 0)
            {
                _transactionTypes = await repo.GetAllTransactionTypesAsync();
            }

            if (code.multiuse)
            {
                //var trans = await domainRepo.GetTransactionsByDeviceIDMethodVoucherAsync(requestMetaData.DeviceDetails.DeviceID, "VoucherActivation", code.vouchercode);
                var validatedDeviceCover = await domainRepo.GetDeviceCoversByDeviceIDMethodVoucherAsync(requestMetaData.DeviceDetails.DeviceID, device.User.Id, "VoucherActivation", code.vouchercode);
                if (validatedDeviceCover.Any())
                {
                    var result = GetErrorResponseObject();
                    result.CodeResponseStatus =
                                    Enumerations.GetEnumDescription(Enumerations.TransactionType.InUse);
                    return result;
                }
            }

            return await UpdateCodeAndGetActivatedResponseAsync(repo, domainRepo, code, requestMetaData);

            // Check that the code has been 'Validated' or previously 'Activated' before being 'Activated'
            // AS A RESULT OF PROJECT GROWING SINCE ONLY HAVING VALIDATION AND ACTIVATION, REMOVING CHECK FOR PREVIOUS VALIDATION
            // WOULD HAVE TO CHECK TRANSACTION LOGS JSON VALUE FOR VALIDATION / ACTIVATION DETAILS
            /*if (code.TransactionTypeId == transactionTypeValidated.Id || code.TransactionTypeId == transactionTypeActivated.Id)
            {
                // Code has been 'validated' by a user. 
                // TODO: Check that the same user is trying to activate the code in a 'handshake' call and update to 'activated'
                // Assume for now that the user is the same as before and update to 'Activated'

                // Get the 'Activated' transaction type and update the code's status
                return await UpdateCodeAndGetActivatedResponseAsync(repo, code, requestMetaData);
            }
            else
            {
                throw new Exception("Code has not been validated before activation");
            }*/
        }


        /// <summary>
        /// Changes the cover on a pro rata basis depending on the current rate and the new rate to be applied.
        /// </summary>
        /// <remarks>
        /// If user is on a basic tariff for 30 days and enters a 30 day premium pin during the basic cover, the pro rata
        /// cost per day of current cover is calculated.  The pro-rata cost per day of new cover is calculated and divide that
        /// into the value of the remaining coverage and add that number of days to the new cover.  calc will always round
        /// down if part days remain 
        /// </remarks>
        /// <example>
        /// $0.76 is pro-rata of current coverage, $0.33 is pro-rata of new cover, $0.76/$0.33 = 2.33 days, 
        /// so round down and add 2 days to the new cover end date
        /// </example>
        /// <param name="voucher">The voucher.</param>
        /// <param name="requestMetaData">The request metadata.</param>
        /// <param name="repo">The repo.</param>
        /// <param name="domainRepo">The domain repo.</param>
        /// <returns></returns>
        /// <exception cref="System.Exception">Not full implemented yet</exception>
        public static async Task<CodeResponse> ChangeCoverProRataAsync(Voucher voucher, RequestMetaData requestMetaData,
                                                                               IRepository repo,
                                                                               FortressDomain.Repository.IRepository domainRepo)
        {

            if (_transactionTypes == null || _transactionTypes.Count == 0)
            {
                _transactionTypes = await repo.GetAllTransactionTypesAsync();
            }
            var transactionTypeValidated = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.Validated));
            var transactionTypeActivated = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.Activated));

            if (voucher.TransactionTypeId == transactionTypeValidated.Id || voucher.TransactionTypeId == transactionTypeActivated.Id)
            {
                Int32 iDeviceID = requestMetaData.DeviceDetails.DeviceID;

                var devicePricingModel = await repo.GetPricingModelByDeviceIdAsync(iDeviceID);
                var activeDeviceCover = await domainRepo.GetActiveDeviceCoverByDeviceIdAsync(iDeviceID);

                // Moving between 2 active devices
                if (devicePricingModel != null && activeDeviceCover != null)
                {
                    //get the number of days remaining on the cover
                    TimeSpan tsDaysRemaining = activeDeviceCover.EndDate.Subtract(DateTime.UtcNow);

                    //if still has cover
                    if (tsDaysRemaining.TotalDays > 0)
                    {
                        Decimal mCurrentDailyRate = Convert.ToDecimal(devicePricingModel.DailyPrice.Value);

                        //round down any partial days remaining and multiply by daily rate to get remaining value
                        Decimal mCurrentRemainingValue = (mCurrentDailyRate * Convert.ToDecimal(Math.Round(tsDaysRemaining.TotalDays)));

                        //find the first metadata so we can get the associated pricing model
                        var newCodeMetadata = voucher.VoucherMetadatas.FirstOrDefault();

                        if (newCodeMetadata == null) return GetErrorResponseObject();

                        Decimal mNewDailyRate = Convert.ToDecimal(newCodeMetadata.PricingModel.DailyPrice.Value);

                        if (mCurrentRemainingValue != Decimal.Zero && mNewDailyRate != Decimal.Zero)
                        {
                            Decimal mAdditionalDays = mCurrentRemainingValue / mNewDailyRate;
                            Int32 iAdditionalDays = Convert.ToInt32(Math.Round(mAdditionalDays));

                            // Write off current device cover
                            activeDeviceCover.EndDate = DateTime.UtcNow;
                            activeDeviceCover.Void = true;
                            activeDeviceCover.ModifiedDate = DateTime.UtcNow;
                            await domainRepo.UpdateDeviceCoverAsync(activeDeviceCover);

                            // Insert new device cover
                            DeviceCover newDeviceCover = new DeviceCover();
                            newDeviceCover.ActivatedDate = DateTime.UtcNow;
                            newDeviceCover.AddedDate = DateTime.UtcNow;
                            newDeviceCover.DeviceId = iDeviceID;

                            //set the end date to be today plus the length of the membership plus any additional pro-rata days
                            newDeviceCover.EndDate = DateTime.UtcNow.AddDays(Convert.ToDouble(voucher.membershiplength + iAdditionalDays));

                            newDeviceCover.MembershipLength = (voucher.membershiplength + iAdditionalDays).ToString();
                            newDeviceCover.MembershipTier = newCodeMetadata.PricingModel.Tier.Name;

                            //Don't set modified date as it is new, not modified
                            newDeviceCover.Voucher = voucher.vouchercode;

                            await domainRepo.CreateDeviceCoverAsync(newDeviceCover);

                            return await GetResponseObject(repo, domainRepo, voucher, Enumerations.TransactionType.ChangeCoverage, requestMetaData);

                        }
                        else
                        {
                            //TODO: do we need actual error responses to be sent rather than exceptions
                            throw new Exception("Remaining value and daily rate cannot be null");
                        }
                    }
                    else
                    {
                        throw new Exception("Cannot pro-rate coverage if cover has expired");
                    }
                }
                else
                {
                    //throw new Exception("New device does not have cover");

                    // Get the previous device's cover
                    var user = await domainRepo.GetUserByEmailAsync(requestMetaData.UserDetails.Email);

                    var devices = await domainRepo.GetDevicesByUserIdAsync(user.Id);
                    var orderedDevices = devices.Where(d => d.Id != requestMetaData.DeviceDetails.DeviceID).OrderByDescending(d => d.Id);

                    // Get the old device
                    Device oldDevice =
                        (from device in orderedDevices where domainRepo.GetDeviceByIdAsync(device.Id) != null select device)
                        .OrderByDescending(d => d.Id).FirstOrDefault();

                    if (oldDevice == null)
                    {
                        throw new Exception("Old device does not exist");
                    }

                    // Get last active device cover for devices
                    DeviceCover oldActiveDeviceCover =
                        (from device in orderedDevices where domainRepo.GetActiveDeviceCoverByDeviceIdAsync(device.Id) != null select activeDeviceCover)
                        .OrderByDescending(dc => dc.EndDate).FirstOrDefault();

                    if (oldActiveDeviceCover == null)
                    {
                        throw new Exception("Old device does not have active cover");
                    }

                    // Get the old device's pricing model
                    PricingModel oldDevicePricingModel =
                        (from device in orderedDevices where repo.GetPricingModelByDeviceIdAsync(device.Id) != null select devicePricingModel).FirstOrDefault();

                    if (oldDevicePricingModel == null)
                    {
                        throw new Exception("Old device does not have a pricing model");
                    }

                    // Now we need to get the pricing model the new device should be on so that we can pro-rata any remaining cover.
                    // Find the first metadata so we can get the associated pricing model
                    var newCodeMetadata = voucher.VoucherMetadatas.FirstOrDefault();
                    var newDevice = await domainRepo.GetDeviceByIdAsync(iDeviceID);

                    if (newDevice == null)
                    {
                        throw new Exception("New device does not exist");
                    }

                    // Pass users new device details to the voucher API to get the tier the device is on
                    DeviceLevelRequest deviceLevelRequest = new DeviceLevelRequest()
                    {
                        DeviceCapacityRaw = newDevice.DeviceCapacityRaw,
                        DeviceModel = newDevice.DeviceModel,
                        DeviceModelRaw = newDevice.DeviceModelRaw,
                        DeviceMake = newDevice.DeviceMake,
                        UserCountryIso = user.CountryIso,
                        VoucherCode = requestMetaData.Code,
                    };

                    // Get the new device level
                    String newDeviceLevel = null;
                    var deviceLevelResult = await repo.GetDeviceLevelAsync(deviceLevelRequest);
                    newDeviceLevel = deviceLevelResult.Item2;

                    Boolean bIsMissingDevice = deviceLevelResult.Item1;


                    if (newDeviceLevel == null)
                    {
                        throw new Exception("Device does not have a level");
                    }

                    // Find the pricing model for the voucher that matches the new device's level
                    PricingModel pricingModelOfVoucherForNewDevice = await repo.GetPricingModelByVoucherAndDeviceLevel(voucher, newDeviceLevel);

                    if (pricingModelOfVoucherForNewDevice == null)
                    {
                        throw new Exception("New device does not have a pricing model for the voucher supplied");
                    }

                    //get the number of days remaining on the cover on the old device
                    TimeSpan tsDaysRemaining = oldActiveDeviceCover.EndDate.Subtract(DateTime.UtcNow);

                    // If old device still has cover
                    if (tsDaysRemaining.TotalDays > 0)
                    {
                        Decimal mCurrentDailyRate = (Decimal)oldDevicePricingModel.DailyPrice.Value;

                        //round down any partial days remaining and multiply by daily rate to get remaining value
                        Decimal mCurrentRemainingValue = (mCurrentDailyRate * Convert.ToDecimal(Math.Round(tsDaysRemaining.TotalDays)));

                        if (newCodeMetadata == null) return GetErrorResponseObject();

                        Decimal mNewDailyRate = (Decimal)newCodeMetadata.PricingModel.DailyPrice.Value;

                        // Pro-rata this cover to the new device
                        Decimal mAdditionalDays = mCurrentRemainingValue / mNewDailyRate;
                        Int32 iAdditionalDays = Convert.ToInt32(Math.Round(mAdditionalDays));

                        // Insert new device cover
                        DeviceCover newDeviceCover = new DeviceCover();
                        newDeviceCover.ActivatedDate = DateTime.UtcNow;
                        newDeviceCover.AddedDate = DateTime.UtcNow;
                        newDeviceCover.DeviceId = iDeviceID;

                        //set the end date to be today plus the length of the membership plus any additional pro-rata days
                        newDeviceCover.EndDate = DateTime.UtcNow.AddDays(Convert.ToDouble(voucher.membershiplength + iAdditionalDays));

                        newDeviceCover.MembershipLength = (voucher.membershiplength + iAdditionalDays).ToString();
                        newDeviceCover.MembershipTier = pricingModelOfVoucherForNewDevice.Tier.Name;

                        //Don't set modified date as it is new, not modified
                        newDeviceCover.Voucher = voucher.vouchercode;

                        await domainRepo.CreateDeviceCoverAsync(newDeviceCover);

                        return await GetResponseObject(repo, domainRepo, voucher, Enumerations.TransactionType.ChangeCoverage, requestMetaData);

                    }
                    else
                    {
                        throw new Exception("Cannot pro-rate coverage if cover has expired");
                    }
                }
            }
            else
            {
                throw new Exception("Code has not been validated before activation");
            }
        }


        #region Methods used to process responses
        private static async Task<CodeResponse> UpdateCodeAndGetValidatedResponseAsync(IRepository repo, FortressDomain.Repository.IRepository domainRepo, Voucher code, RequestMetaData requestMetaData)
        {
            //get the 'Validated' transaction type
            var transactionType = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.Validated));
            if (transactionType == null) return GetErrorResponseObject();

            //Update the codes transaction type to 'Validated'
            code.TransactionTypeId = transactionType.Id;
            if (await repo.UpdateVoucherAsync(code) != 1) return GetErrorResponseObject();

            // Update the result object to 'Validated'
            return await GetResponseObject(repo, domainRepo, code, Enumerations.TransactionType.Validated, requestMetaData);
        }



        private static async Task<CodeResponse> UpdateCodeAndGetActivatedResponseAsync(IRepository repo, FortressDomain.Repository.IRepository domainRepo, Voucher code, RequestMetaData requestMetaData)
        {
            var transactionType = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.Activated));

            if (transactionType == null) return GetErrorResponseObject();
            code.TransactionTypeId = transactionType.Id;

            // Update the code with the new transaction type
            if (code.TransactionTypeId != transactionType.Id)
            {
                if (await repo.UpdateVoucherAsync(code) != 1) return GetErrorResponseObject();
            }

            return await GetResponseObject(repo, domainRepo, code, Enumerations.TransactionType.Activated, requestMetaData);
        }

        private static async Task<CodeResponse> UpdateCodeAndGetTimeLimitCodeResponseAsync(IRepository repo, Voucher code, CodeResponse result)
        {
            // Code has expired - get the 'Time Limit' code
            var transactionType = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.TimeLimit));
            if (transactionType == null) return result;

            // Update the code with the new transaction type
            code.TransactionTypeId = transactionType.Id;
            if (await repo.UpdateVoucherAsync(code) != 1) return result;

            // Update the result object to 'Time limit'
            result.CodeResponseStatus =
                Enumerations.GetEnumDescription(Enumerations.TransactionType.TimeLimit);

            return result;
        }

        private static async Task<CodeResponse> UpdateCodeAndGetMaxReachedCodeResponseAsync(IRepository repo, Voucher code, CodeResponse result)
        {
            // Code has reached Max count - get 'Max Count' code
            var transactionType = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.MaximumUse));
            if (transactionType == null) return result;

            // Update the code with the new transaction type
            code.TransactionTypeId = transactionType.Id;
            if (await repo.UpdateVoucherAsync(code) != 1) return result;

            // Update the result object to 'Max Use'
            result.CodeResponseStatus =
                Enumerations.GetEnumDescription(Enumerations.TransactionType.MaximumUse);

            return result;
        }
        #endregion


        #region Quick generation of code response objects
        private static CodeResponse GetErrorResponseObject()
        {
            return GetErrorResponseObject(Enumerations.TransactionType.Error);
        }

        public static CodeResponse GetErrorResponseObject(Enumerations.TransactionType type)
        {
            return new CodeResponse()
            {
                CodeResponseStatus = Enumerations.GetEnumDescription(type),
                MembershipLength = "NA",
                MembershipTier = "NA"
            };
        }

        /// <summary>
        /// Changes the cover on a pro rata basis depending on the current rate and the new rate to be applied.
        /// </summary>
        /// <remarks>
        /// If user is on a basic tariff for 30 days and enters a 30 day premium pin during the basic cover, the pro rata
        /// cost per day of current cover is calculated.  The pro-rata cost per day of new cover is calculated and divide that
        /// into the value of the remaining coverage and add that number of days to the new cover.  calc will always round
        /// down if part days remain 
        /// </remarks>
        /// <example>
        /// $0.76 is pro-rata of current coverage, $0.33 is pro-rata of new cover, $0.76/$0.33 = 2.33 days, 
        /// so round down and add 2 days to the new cover end date
        /// </example>
        /// <param name="voucher">The voucher.</param>
        /// <param name="requestMetaData">The request metadata.</param>
        /// <param name="repo">The repo.</param>
        /// <param name="domainRepo">The domain repo.</param>
        /// <returns></returns>
        /// <exception cref="System.Exception">Not full implemented yet</exception>

        public static async Task<CodeResponse> ValidateChangeCoverProRataAsync(Voucher voucher, RequestMetaData requestMetaData,
                                                                               IRepository repo,
                                                                               FortressDomain.Repository.IRepository domainRepo,
                                                                               Enumerations.TransactionType transactionType)
        {
            if (_transactionTypes == null || _transactionTypes.Count == 0)
            {
                _transactionTypes = await repo.GetAllTransactionTypesAsync();
            }
            //var transactionTypeValidated = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.Validated));
            //var transactionTypeActivated = _transactionTypes.FirstOrDefault(t => t.Name == Enumerations.GetEnumDescription(Enumerations.TransactionType.Activated));

            Int32 iDeviceID = requestMetaData.DeviceDetails.DeviceID;
            /*********************************************************************************************************/
            //MISSING DEVICE
            var userDevice = await domainRepo.GetDeviceByIdAsync(iDeviceID);
            if (userDevice.MissingDevice.HasValue && userDevice.MissingDevice.Value)
            {
                DeviceLevelRequest deviceLevelRequest = new DeviceLevelRequest()
                {
                    DeviceCapacityRaw = userDevice.DeviceCapacityRaw,
                    DeviceModel = userDevice.DeviceModel,
                    DeviceModelRaw = userDevice.DeviceModelRaw,
                    DeviceMake = userDevice.DeviceMake,
                    UserCountryIso = (userDevice.User != null) ? userDevice.User.CountryIso : String.Empty,
                    VoucherCode = requestMetaData.Code,
                };
                var deviceLevelResult = await repo.GetDeviceLevelAsync(deviceLevelRequest);

                PricingModel voucherPricingModel = await repo.GetPricingModelByVoucherCodeAsync(voucher.vouchercode);

                //20/04/2017 - pricing update change
                //get payment details from model passed in by the request (if there is one).
                if (requestMetaData.PaymentDetails != null && requestMetaData.PaymentDetails.PricingModelId != 0)
                {
                    voucherPricingModel = await repo.GetPricingModelsById(requestMetaData.PaymentDetails.PricingModelId);
                }


                if (userDevice.DeviceCover.Any())
                {
                    //the largest id is the last, as they may not be in correct order
                    Int32 iMaxDeviceCover = userDevice.DeviceCover.Max(dc => dc.Id);
                    //void all previous covers
                    foreach (var cover in userDevice.DeviceCover)
                    {
                        cover.Void = true;
                        //Only update the end date of the very last cover
                        if (cover.Id == iMaxDeviceCover)
                        {
                            cover.EndDate = DateTime.UtcNow;
                        }
                        cover.ModifiedDate = DateTime.UtcNow;

                        //Need to call this synchronously - seems a combination of this and the CreateDeviceCoverAsync would
                        //create 2 new device covers
                        domainRepo.UpdateDeviceCoverAsync(cover);

                    }
                }

                DeviceCover missingCover = new DeviceCover();
                missingCover.ActivatedDate = DateTime.UtcNow;
                missingCover.AddedDate = DateTime.UtcNow;
                missingCover.DeviceId = userDevice.Id;

                Int32 maxTempCoverDays = 7;
                if (voucher.membershiplength.HasValue)
                {
                    //If the voucher is for longer than 7 days, cap at max of 7 days cover
                    if (voucher.membershiplength >= maxTempCoverDays)
                    {
                        missingCover.EndDate = DateTime.UtcNow.AddDays(maxTempCoverDays);
                        missingCover.MembershipLength = maxTempCoverDays.ToString();
                    }
                    else
                    {
                        missingCover.EndDate = DateTime.UtcNow.AddDays(voucher.membershiplength.Value);
                        missingCover.MembershipLength = voucher.membershiplength.Value.ToString();
                    }
                }
                if (voucherPricingModel.Tier != null)
                {
                    missingCover.MembershipTier = voucherPricingModel.Tier.Name;
                }
                missingCover.ModifiedDate = DateTime.UtcNow;
                missingCover.Voucher = voucher.vouchercode;

                //Need to call this synchronously - when marked with the await keyword 
                //2 device covers would be created (with created dated 10th of seconds apart),
                //this would breaks subsequent calls as there should be only one active cover
                domainRepo.CreateDeviceCoverAsync(missingCover);

                return new CodeResponse()
                {
                    CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                    //return the updated length, not the original voucher length
                    MembershipLength = missingCover.MembershipLength,
                    MembershipTier = missingCover.MembershipTier,
                    CodeResponseMessage = Enumerations.GetEnumDescription(Enumerations.CodeResponseMessage.MissingDevice)
                };
            }
            /*********************************************************************************************************/


            else
            {
                //device is not missing



                var activeDeviceCover = await domainRepo.GetActiveDeviceCoverByDeviceIdAsync(iDeviceID);
                PricingModel devicePreviousPricingModelByDeviceLevel = null;
                //IEnumerable<FortressDomain.Models.Db.DeviceCover> validatedDeviceCovers = null;

                //you cannot rely  on the existing Voucher being for the correct category of device
                //get the device pricing model for the partner of the voucher
                var metaDataVoucher = voucher.VoucherMetadatas.FirstOrDefault();


                PricingModel newVoucherPricingModelByDeviceLevel = await repo.GetPricingModelByDevicePartnerFamilyAsync(requestMetaData.DeviceDetails.DeviceLevel,
                                                                                                               metaDataVoucher.PricingModel.TeirId.Value,
                                                                                                               metaDataVoucher.PricingModel.FamilyId.Value);


                //20/04/2017 - pricing update change
                //get payment details from model passed in by the request (if there is one).
                if (requestMetaData.PaymentDetails != null && requestMetaData.PaymentDetails.PricingModelId != 0)
                {
                    newVoucherPricingModelByDeviceLevel = await repo.GetPricingModelsById(requestMetaData.PaymentDetails.PricingModelId);
                }


                if (activeDeviceCover != null)
                {
                    //get pricing model associated with the voucher (irrespective of level)
                    //this will allow us to get pricing model for the currently active voucher
                    devicePreviousPricingModelByDeviceLevel = await repo.GetPricingModelByVoucherCodeAsync(activeDeviceCover.Voucher);

                    //Get the pricing model of the device level, tier and family for pricing model as may have been pro-rated previously
                    devicePreviousPricingModelByDeviceLevel = await repo.GetPricingModelByDevicePartnerFamilyAsync(requestMetaData.DeviceDetails.DeviceLevel,
                                                                                              devicePreviousPricingModelByDeviceLevel.TeirId.Value,
                                                                                              devicePreviousPricingModelByDeviceLevel.FamilyId.Value);

                    //validatedDeviceCovers = await domainRepo.GetDeviceCoversByDeviceIDMethodVoucherAsync(requestMetaData.DeviceDetails.DeviceID, userDevice.User.Id, "VoucherActivation", activeDeviceCover.Voucher);
                    //await domainRepo.GetTransactionsByDeviceIDMethodVoucherAsync(requestMetaData.DeviceDetails.DeviceID, "VoucherActivation", activeDeviceCover.Voucher);

                }




                // Moving between 2 active devices
                //15/02/2018 transaction check removed. Redundant checking for a voucher activation on the existing cover. In all instances where a device cover entry is created we willl create the transaction. Severaly impacts on performance.

                if (devicePreviousPricingModelByDeviceLevel != null && activeDeviceCover != null)
                //if (devicePreviousPricingModelByDeviceLevel != null && activeDeviceCover != null && validatedDeviceCovers.Any())
                {
                    //get the number of days remaining on the cover
                    //TimeSpan tsDaysRemaining = activeDeviceCover.EndDate.Subtract(DateTime.UtcNow);                    
                    //Double totalDaysRemaining = Math.Ceiling(tsDaysRemaining.TotalDays);

                    DateTime dt = DateTime.UtcNow;
                    TimeSpan timeOfDay = dt.TimeOfDay;

                    //Catch all if cover ends today
                    if (activeDeviceCover.EndDate.Date == DateTime.UtcNow.Date)
                    {
                        dt = dt.AddDays(-1);
                    }
                    DateTime fullStartDateTime = activeDeviceCover.EndDate.Date.Add(timeOfDay);

                    TimeSpan tsDaysRemaining = fullStartDateTime.Subtract(dt);
                    Double totalDaysRemaining = Math.Ceiling(tsDaysRemaining.TotalDays);



                    if (totalDaysRemaining < 0)
                    {
                        totalDaysRemaining = 0;
                    }

                    //Get the in the bank value on the device
                    //This is the value of the days remaining in cover multiplied by the daily rate of the pricing model that voucher belongs to.
                    //This will be a monetary value.
                    Decimal mInTheBankValue = Convert.ToDecimal(devicePreviousPricingModelByDeviceLevel.DailyPrice.Value) *
                                              Convert.ToDecimal(totalDaysRemaining);

                    FortressCodeContext db = new FortressCodeContext();

                    Decimal dailyPriceVal = Convert.ToDecimal(metaDataVoucher.PricingModel.DailyPrice.Value);
                    Decimal memLength = Convert.ToDecimal(voucher.membershiplength.Value);
                    string deviceLevel = requestMetaData.DeviceDetails.DeviceLevel;
                    Level lev = db.Levels.Where(le => le.Name == deviceLevel).SingleOrDefault();
                    //Check if the voucher is gateway to ensure pro rata calculations are calculated against the voucher familys correct
                    if (metaDataVoucher.VoucherTypeID == (Int32)Enumerations.VoucherType.Gateway)
                    {
                        // Check if the vouchers pricing model passed in is the correct level for the device
                        // If not grab the correct pricing model from the same family
                        if (metaDataVoucher.PricingModel.Level.Name != deviceLevel)
                        {
                            try
                            {
                                var pmNewLevel = db.PricingModels.Where(pm => pm.FamilyId == metaDataVoucher.PricingModel.FamilyId && pm.LevelId == lev.Id && pm.Tier.TierLevel == metaDataVoucher.PricingModel.Tier.TierLevel).SingleOrDefault();
                                dailyPriceVal = Convert.ToDecimal(pmNewLevel.DailyPrice.Value);
                            }
                            catch
                            {
                            }

                        }
                    }
                    //Letter representation of device level
                    var dbContext = new FortressCodesDomain.DbModels.FortressCodeContext();



                    Decimal mComingInValue = dailyPriceVal *
                                         memLength;

                    Decimal mTotalValue = mInTheBankValue + mComingInValue;

                    Decimal? dDaysToApply = 0;

                    //divide by zero error handling
                    if (newVoucherPricingModelByDeviceLevel.DailyPrice.Value != 0)
                    {
                        dDaysToApply = Math.Ceiling(mTotalValue / Convert.ToDecimal(newVoucherPricingModelByDeviceLevel.DailyPrice.Value));
                    }
                    else
                    {
                        //MMC - If a zero value voucher is used all existing coverage is removed
                        //if (mInTheBankValue <= 0)
                        //{
                        dDaysToApply = metaDataVoucher.Voucher.membershiplength;
                        //}

                    }
                    if (devicePreviousPricingModelByDeviceLevel.DailyPrice.Value != 0 && newVoucherPricingModelByDeviceLevel.DailyPrice.Value == 0)
                    {
                        return new CodeResponse()
                        {
                            CodeResponseStatus = Enumerations.GetEnumDescription(Enumerations.TransactionType.VoucherZeroCoverPaid),
                            //return the updated length, not the original voucher length
                            MembershipLength = "NA",
                            MembershipTier = "NA",
                            CodeResponseMessage = Enumerations.GetEnumDescription(Enumerations.TransactionType.VoucherZeroCoverPaid)
                        };
                    }
                    // Commented out by MMC - 1 December 2016 - Ref:MMC1DEC
                    // New code implemented to calculate pro rata
                    //
                    ////Decimal mCurrentDailyRate = Convert.ToDecimal(devicePreviousPricingModelByDeviceLevel.DailyPrice.Value);
                    //Decimal mCurrentDailyRate = Convert.ToDecimal(newVoucherPricingModelByDeviceLevel.DailyPrice.Value);

                    ////round down any partial days remaining and multiply by daily rate to get remaining value
                    //Decimal mCurrentRemainingValue = (mCurrentDailyRate * Convert.ToDecimal(Math.Ceiling(totalDaysRemaining)));

                    ////Decimal mNewDailyRate = Convert.ToDecimal(newVoucherPricingModelByDeviceLevel.DailyPrice.Value);

                    //Decimal mNewDailyRate = Convert.ToDecimal(metaDataVoucher.PricingModel.DailyPrice.Value);

                    ////Now Pro-Rata this new voucher coming in
                    ////Decimal? mDays = (newVoucherPricingModelByDeviceLevel.DailyPrice.Value * voucher.membershiplength.Value) /
                    ////                            newVoucherPricingModelByDeviceLevel.DailyPrice.Value;

                    //Decimal? mDays = (metaDataVoucher.PricingModel.DailyPrice.Value * voucher.membershiplength.Value) /
                    //                               newVoucherPricingModelByDeviceLevel.DailyPrice.Value;

                    //Int32 iNewCodeDays = Convert.ToInt32(Math.Ceiling(mDays.Value));

                    //if (mCurrentRemainingValue != Decimal.Zero && mNewDailyRate != Decimal.Zero)
                    if (mTotalValue != Decimal.Zero && voucher.membershiplength.Value != Decimal.Zero)
                    {
                        Int32 iAdditionalDays = 0;
                        if (newVoucherPricingModelByDeviceLevel.TeirId != devicePreviousPricingModelByDeviceLevel.TeirId)
                        {
                            bool upgrade = true;
                            //Check if the tierlevel of the existing cover is less than the new cover, if so we send an upgrade email. If not we send a downgrade email
                            if (newVoucherPricingModelByDeviceLevel.Tier != null && devicePreviousPricingModelByDeviceLevel.Tier != null)
                            {
                                if (newVoucherPricingModelByDeviceLevel.Tier.TierLevel != null && devicePreviousPricingModelByDeviceLevel.Tier.TierLevel != null)
                                {
                                    if (newVoucherPricingModelByDeviceLevel.Tier.TierLevel < devicePreviousPricingModelByDeviceLevel.Tier.TierLevel)
                                        upgrade = false;
                                }
                            }
                            //if(newVoucherPricingModelByDeviceLevel.Tier.TierLevel != null && devicePreviousPricingModelByDeviceLevel.Tier.TierLevel)



                            //Decimal mAdditionalDays = mCurrentRemainingValue / mNewDailyRate;
                            Decimal mAdditionalDays = Convert.ToDecimal(dDaysToApply) - Convert.ToDecimal(totalDaysRemaining);

                            iAdditionalDays = Convert.ToInt32(Math.Ceiling(mAdditionalDays));

                            //Only write to email queue on activation
                            //commented out 05/04/2018
                            //if (transactionType == Enumerations.TransactionType.Activated)
                            //{
                            //    //get the upgrade type based on the new tier name
                            //    FortressDomain.Helpers.Enumerations.EmailType emailType = FortressDomain.Helpers.Enumerations.EmailType.None;
                            //    switch (newVoucherPricingModelByDeviceLevel.Tier.Name.ToLower())
                            //    {
                            //        case "basic":
                            //            emailType = FortressDomain.Helpers.Enumerations.EmailType.UpgradeBasic;
                            //            break;
                            //        case "premium":
                            //            emailType = FortressDomain.Helpers.Enumerations.EmailType.UpgradePremium;
                            //            break;
                            //        case "ultimate":
                            //            emailType = FortressDomain.Helpers.Enumerations.EmailType.UpgradeUltimate;
                            //            break;
                            //        default:
                            //            break;
                            //    }
                            //    if (!upgrade)
                            //        switch (newVoucherPricingModelByDeviceLevel.Tier.Name.ToLower())
                            //        {
                            //            case "basic":
                            //                emailType = FortressDomain.Helpers.Enumerations.EmailType.DowngradeBasic;
                            //                break;
                            //            case "premium":
                            //                emailType = FortressDomain.Helpers.Enumerations.EmailType.DowngradePremium;
                            //                break;
                            //            case "ultimate":
                            //                emailType = FortressDomain.Helpers.Enumerations.EmailType.DowngradeUltimate;
                            //                break;
                            //            default:
                            //                break;
                            //        }
                            //    //create upgrade email queue item
                            //    await FortressDomain.Helpers.ApiHelper.CreateEmailQueueItemAsync(DateTime.UtcNow,
                            //                                                                    FortressDomain.Helpers.Enumerations.Method.VoucherActivation,
                            //                                                                    emailType,
                            //                                                                    userDevice.User.Id,
                            //                                                                    activeDeviceCover.Id,
                            //                                                                    domainRepo);
                            //}

                        }
                        else
                        {
                            //User is topping up coverage

                            //Only write to email queue on activation
                            if (transactionType == Enumerations.TransactionType.Activated)
                            {
                                //create top up confirmation email queue item
                                //await FortressDomain.Helpers.ApiHelper.CreateEmailQueueItemAsync(DateTime.Now,
                                //                                                                 FortressDomain.Helpers.Enumerations.Method.VoucherActivation,
                                //                                                                 FortressDomain.Helpers.Enumerations.EmailType.TopUpConfirmation,
                                //                                                                 userDevice.User.Id,
                                //                                                                 activeDeviceCover.Id,
                                //                                                                 domainRepo);
                            }
                            iAdditionalDays = Convert.ToInt32(Math.Ceiling(totalDaysRemaining));
                        }

                        //double dblTotalDays = (iNewCodeDays + iAdditionalDays);
                        double dblTotalDays = Convert.ToDouble(dDaysToApply.Value);

                        //Added by MMC to stop double entry of cover when only validating
                        if (transactionType == Enumerations.TransactionType.Activated)
                        {
                            if (userDevice.DeviceCover.Any())
                            {
                                //the largest id is the last, as they may not be in correct order
                                Int32 iMaxDeviceCover = userDevice.DeviceCover.Max(dc => dc.Id);
                                //void all previous covers
                                foreach (var cover in userDevice.DeviceCover)
                                {
                                    cover.Void = true;
                                    //Only update the end date of the very last cover
                                    if (cover.Id == iMaxDeviceCover)
                                    {
                                        cover.EndDate = DateTime.UtcNow;
                                    }
                                    cover.ModifiedDate = DateTime.UtcNow;
                                    await domainRepo.UpdateDeviceCoverAsync(cover);
                                }
                            }

                            // Insert new device cover
                            DeviceCover newDeviceCover = new DeviceCover();
                            newDeviceCover.ActivatedDate = DateTime.UtcNow;
                            newDeviceCover.AddedDate = DateTime.UtcNow;
                            newDeviceCover.DeviceId = iDeviceID;

                            //set the end date to be today plus the length of the membership plus any additional pro-rata days
                            newDeviceCover.EndDate = DateTime.UtcNow.AddDays(dblTotalDays);

                            newDeviceCover.MembershipLength = dblTotalDays.ToString();
                            newDeviceCover.MembershipTier = newVoucherPricingModelByDeviceLevel.Tier.Name;

                            //Don't set modified date as it is new, not modified
                            newDeviceCover.Voucher = voucher.vouchercode;
                        }

                        // Get code response message based on voucher result
                        Enumerations.CodeResponseMessage codeResponseMessageResult =
                            GetCodeResponseMessageResult(Convert.ToInt32(totalDaysRemaining),
                                                         Convert.ToInt32(voucher.membershiplength),
                                                         dblTotalDays, transactionType);

                        //Amended by MMC so the return is still valid
                        return new CodeResponse()
                        {
                            CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                            //return the updated length, not the original voucher length
                            MembershipLength = ((dblTotalDays).ToString()),
                            MembershipTier = newVoucherPricingModelByDeviceLevel.Tier.Name,
                            CodeResponseMessage = Enumerations.GetEnumDescription(codeResponseMessageResult)
                        };
                    }
                    else
                    {
                        // Assumptiopns
                        //New voucher tier is the same as existing coverage tier
                        //NEw voucher daily price = 0 and existing voucher daily price = 0

                        //if true then apply number of days on voucher + number of days remaining

                        if (transactionType == Enumerations.TransactionType.Activated)
                        {
                            if (userDevice.DeviceCover.Any())
                            {
                                //the largest id is the last, as they may not be in correct order
                                Int32 iMaxDeviceCover = userDevice.DeviceCover.Max(dc => dc.Id);
                                //void all previous covers
                                foreach (var cover in userDevice.DeviceCover)
                                {
                                    cover.Void = true;
                                    //Only update the end date of the very last cover
                                    if (cover.Id == iMaxDeviceCover)
                                    {
                                        cover.EndDate = DateTime.UtcNow;
                                    }
                                    cover.ModifiedDate = DateTime.UtcNow;
                                    await domainRepo.UpdateDeviceCoverAsync(cover);
                                }
                            }
                        }
                        if ((newVoucherPricingModelByDeviceLevel.Tier.Name == activeDeviceCover.MembershipTier) && (newVoucherPricingModelByDeviceLevel.DailyPrice.Value == 0 && devicePreviousPricingModelByDeviceLevel.DailyPrice.Value == 0))
                        {

                            Enumerations.CodeResponseMessage codeResponseMessageResult =
                        GetCodeResponseMessageResult((Int32)Math.Ceiling(totalDaysRemaining),
                                                     Convert.ToInt32(voucher.membershiplength),
                                                     (Int32)Math.Ceiling(totalDaysRemaining) + Convert.ToInt32(voucher.membershiplength), transactionType);

                            return new CodeResponse()
                            {
                                CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                                //return the updated length, not the original voucher length
                                MembershipLength = ((Int32)Math.Ceiling(totalDaysRemaining) + Convert.ToInt32(voucher.membershiplength)).ToString(),
                                MembershipTier = newVoucherPricingModelByDeviceLevel.Tier.Name,
                                CodeResponseMessage = Enumerations.GetEnumDescription(codeResponseMessageResult)
                            };
                        }
                        // Assumptiopns
                        //New voucher tier is not the same as existing coverage tier
                        //NEw voucher daily price = 0 and existing voucher daily price = 0
                        else if ((newVoucherPricingModelByDeviceLevel.Tier.Name != activeDeviceCover.MembershipTier) && (newVoucherPricingModelByDeviceLevel.DailyPrice.Value == 0 && devicePreviousPricingModelByDeviceLevel.DailyPrice.Value == 0))
                        {
                            Enumerations.CodeResponseMessage codeResponseMessageResult =
                                GetCodeResponseMessageResult((Int32)Math.Ceiling(totalDaysRemaining),
                                                             Convert.ToInt32(voucher.membershiplength),
                                                             Convert.ToInt32(voucher.membershiplength), transactionType);

                            return new CodeResponse()
                            {
                                CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                                //return the updated length, not the original voucher length
                                MembershipLength = voucher.membershiplength.ToString(),
                                MembershipTier = newVoucherPricingModelByDeviceLevel.Tier.Name,
                                CodeResponseMessage = Enumerations.GetEnumDescription(codeResponseMessageResult)
                            };
                        }

                        //TODO: do we need actual error responses to be sent rather than exceptions
                        throw new Exception("Remaining value and daily rate cannot be null");
                    }
                }
                else
                {
                    //Fortress Freedom section


                    // Get the previous device's cover
                    var user = await domainRepo.GetUserByEmailAsync(requestMetaData.UserDetails.Email);
                    var devices = await domainRepo.GetDevicesByUserIdAsync(user.Id);

                    // We now need to determine which device is in coverage.
                    // for each device, find which one is in coverage
                    Int32 iActiveDeviceID = 0;
                    foreach (var itemDevice in devices)
                    {
                        var itemDeviceCovers = itemDevice.DeviceCover.Where(p => p.EndDate >= DateTime.UtcNow && (!p.Void.HasValue || (p.Void.HasValue && p.Void.Value == false))).ToList();
                        if (itemDeviceCovers.Any())
                        {
                            iActiveDeviceID = itemDevice.Id;
                        }
                    }

                    Device previousDevice = await domainRepo.GetDeviceByIdAsync(iActiveDeviceID);

                    //Scenario where you have an old device cover
                    if (previousDevice != null)
                    {
                        // Get last active device cover for devices
                        DeviceCover oldActiveDeviceCover = await domainRepo.GetActiveDeviceCoverByDeviceIdAsync(previousDevice.Id);

                        if (oldActiveDeviceCover == null)
                        {
                            throw new Exception("Old device does not have active cover");
                        }

                        // Pass users new device details to the voucher API to get the tier the device is on
                        DeviceLevelRequest previousDeviceLevelRequest = new DeviceLevelRequest()
                        {
                            DeviceCapacityRaw = previousDevice.DeviceCapacityRaw,
                            DeviceModel = previousDevice.DeviceModel,
                            DeviceModelRaw = previousDevice.DeviceModelRaw,
                            DeviceMake = previousDevice.DeviceMake,
                            UserCountryIso = user.CountryIso,
                            VoucherCode = requestMetaData.Code,
                        };

                        // Get the new device level
                        String oldDeviceLevel = null;
                        var previousDeviceLevelResult = await repo.GetDeviceLevelAsync(previousDeviceLevelRequest);
                        oldDeviceLevel = previousDeviceLevelResult.Item2;

                        if (oldDeviceLevel == null)
                        {
                            throw new Exception("Device does not have a level");
                        }

                        var oldDeviceVoucherMetadatas = voucher.VoucherMetadatas.FirstOrDefault();

                        if (oldDeviceVoucherMetadatas == null)
                        {
                            throw new Exception("Voucher does not have meta data");
                        }

                        PricingModel oldDevicePricingModel = oldDeviceVoucherMetadatas.PricingModel;

                        //Get the pricing model of the device level, tier and family for pricing model as may have been pro-rated previously
                        oldDevicePricingModel = await repo.GetPricingModelByDevicePartnerFamilyAsync(oldDeviceLevel,
                                                                                                    oldDevicePricingModel.TeirId.Value,
                                                                                                    oldDevicePricingModel.FamilyId.Value);

                        Device newDevice = await domainRepo.GetDeviceByIdAsync(iDeviceID);

                        if (newDevice == null)
                        {
                            throw new Exception("New device does not exist");
                        }

                        // Pass users new device details to the voucher API to get the tier the device is on
                        DeviceLevelRequest deviceLevelRequest = new DeviceLevelRequest()
                        {
                            DeviceCapacityRaw = newDevice.DeviceCapacityRaw,
                            DeviceModel = newDevice.DeviceModel,
                            DeviceModelRaw = newDevice.DeviceModelRaw,
                            DeviceMake = newDevice.DeviceMake,
                            UserCountryIso = user.CountryIso,
                            VoucherCode = requestMetaData.Code,
                        };

                        // Get the new device level
                        String newDeviceLevel = null;
                        var deviceLevelResult = await repo.GetDeviceLevelAsync(deviceLevelRequest);
                        newDeviceLevel = deviceLevelResult.Item2;

                        if (newDeviceLevel == null)
                        {
                            throw new Exception("Device does not have a level");
                        }

                        // Get pricing model the new device should be on based on the old device cover's voucher
                        PricingModel newDevicePricingModel = await repo.GetPricingModelByDevicePartnerFamilyAsync(
                                                                            newDeviceLevel,
                                                                            oldDevicePricingModel.TeirId.Value,
                                                                            oldDevicePricingModel.FamilyId.Value);


                        //20/04/2017 - pricing update change
                        //get payment details from model passed in by the request (if there is one).
                        if (requestMetaData.PaymentDetails != null && requestMetaData.PaymentDetails.PricingModelId != 0)
                        {
                            newDevicePricingModel = await repo.GetPricingModelsById(requestMetaData.PaymentDetails.PricingModelId);
                        }

                        if (newDevicePricingModel == null)
                        {
                            throw new Exception("New device does not have a pricing model for the voucher supplied");
                        }

                        //get the number of days remaining on the cover on the old device
                        TimeSpan tsDaysRemaining = oldActiveDeviceCover.EndDate.Subtract(DateTime.UtcNow);

                        // If old device does not have any cover
                        if (tsDaysRemaining.TotalDays <= 0)
                        {
                            throw new Exception("Cannot coverage if cover has expired");
                        }

                        // Use the oldDevicePricingModel and newDevicePricingModel to pro rata the current cover
                        Decimal mOldDailyRate = Convert.ToDecimal(oldDevicePricingModel.DailyPrice.Value);
                        Decimal mNewDailyRate = Convert.ToDecimal(newDevicePricingModel.DailyPrice.Value);

                        // Calculate new days based on existing daily rate
                        Decimal dblNewDays = 0;
                        //handle division by 0 error
                        if (mNewDailyRate > 0.00m)
                        {
                            dblNewDays = mOldDailyRate * Convert.ToDecimal(tsDaysRemaining.TotalDays) / mNewDailyRate;
                        }
                        else
                        {
                            //MMC - If a zero value voucher is used all existing coverage is removed
                            //if (tsDaysRemaining.TotalDays <= 0)
                            //{
                            dblNewDays = Convert.ToDecimal(metaDataVoucher.Voucher.membershiplength);
                            //}
                        }

                        Double dblNewTotalDays = Math.Ceiling(Convert.ToDouble(dblNewDays));


                        if (previousDevice.DeviceCover.Any())
                        {
                            //the largest id is the last, as they may not be in correct order
                            Int32 iMaxDeviceCover = previousDevice.DeviceCover.Max(dc => dc.Id);
                            //void all previous covers
                            foreach (var cover in previousDevice.DeviceCover)
                            {
                                cover.Void = true;
                                //Only update the end date of the very last cover
                                if (cover.Id == iMaxDeviceCover)
                                {
                                    cover.EndDate = DateTime.UtcNow;
                                }
                                cover.ModifiedDate = DateTime.UtcNow;
                                await domainRepo.UpdateDeviceCoverAsync(cover);
                            }
                        }

                        if (userDevice.DeviceCover.Any())
                        {
                            //the largest id is the last, as they may not be in correct order
                            Int32 iMaxDeviceCover = userDevice.DeviceCover.Max(dc => dc.Id);
                            //void all previous covers
                            foreach (var cover in userDevice.DeviceCover)
                            {
                                cover.Void = true;
                                //Only update the end date of the very last cover
                                if (cover.Id == iMaxDeviceCover)
                                {
                                    cover.EndDate = DateTime.UtcNow;
                                }
                                cover.ModifiedDate = DateTime.UtcNow;
                                await domainRepo.UpdateDeviceCoverAsync(cover);
                            }
                        }

                        // Insert new device cover
                        DeviceCover newDeviceCover = new DeviceCover();
                        newDeviceCover.ActivatedDate = DateTime.UtcNow;
                        newDeviceCover.AddedDate = DateTime.UtcNow;
                        newDeviceCover.DeviceId = iDeviceID;

                        //set the end date to be today plus the new days
                        newDeviceCover.EndDate = DateTime.UtcNow.AddDays(dblNewTotalDays);

                        newDeviceCover.MembershipLength = dblNewTotalDays.ToString();
                        newDeviceCover.MembershipTier = newDevicePricingModel.Tier.Name;

                        //Don't set modified date as it is new, not modified
                        newDeviceCover.Voucher = voucher.vouchercode;

                        //actual device cover
                        if (oldActiveDeviceCover != null)
                            newDeviceCover.DeviceValue = oldActiveDeviceCover.DeviceValue;
                        //actia; device cover
                        // Code Added - 6 December 2016 - Mark McCann
                        //
                        // Need to add a transaction item to the newly transfered deviceCover so that the next voucher does not trigger FF again
                        //
                        // Begin
                        //

                        //await Logger.LogTransaction(voucher.vouchercode, (voucher.vouchercode == null) ? (int?)null : voucher.Id, requestMetaData,
                        //                            Enumerations.GetEnumDescription(Enumerations.TransactionType.ActivationRequest), "",
                        //                            repo, requestMetaData.TransactionGuid);



                        //get the previous activation for this voucher and add a row for this device
                        //IEnumerable<FortressDomain.Models.Db.Transaction> updatedTrans = null;
                        //updatedTrans = await domainRepo.GetTransactionsByDeviceIDMethodVoucherAsync(
                        //                                    previousDevice.Id, "VoucherActivation", requestMetaData.Code);

                        //FortressDomain.Helpers.Logger.LogTransaction()
                        //if (updatedTrans.Any())
                        //{
                        FortressDomain.Models.Db.Transaction t1 = new FortressDomain.Models.Db.Transaction();

                        t1.Date = DateTime.UtcNow;
                        t1.MethodName = "VoucherActivation";
                        t1.UserId = user.Id;
                        t1.DeviceId = newDevice.Id;
                        t1.Success = true;
                        t1.Type = "Response";
                        t1.Guid = Guid.NewGuid();



                        domainRepo.AddTransaction(t1);
                        //}

                        //String jsonData, IRepository repo, int? userId, int? deviceId,
                        //String email, String appVersion, Boolean success, Enumerations.Method methodName, 
                        //Enumerations.TransactionType type, Guid requestGuid)
                        // End
                        //

                        await domainRepo.CreateDeviceCoverAsync(newDeviceCover);

                        Enumerations.CodeResponseMessage codeResponseMessageResult =
                               GetCodeResponseMessageResult(0, Convert.ToInt32(voucher.membershiplength),
                                                            dblNewTotalDays, transactionType);

                        return new CodeResponse()
                        {
                            CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                            //return the updated length, not the original voucher length
                            MembershipLength = newDeviceCover.MembershipLength,
                            MembershipTier = newDevicePricingModel.Tier.Name,
                            CodeResponseMessage = Enumerations.GetEnumDescription(codeResponseMessageResult)
                        };
                    }
                    else
                    {
                        // No old device scenario
                        // No existing Cover
                        // Get the new voucher pricing model
                        var metaData = voucher.VoucherMetadatas.FirstOrDefault();
                        PricingModel targetPricingModel = null;
                        if (metaData != null)
                        {
                            targetPricingModel = metaData.PricingModel;
                        }

                        //20/04/2017 - pricing update change
                        //get payment details from model passed in by the request (if there is one).
                        if (requestMetaData.PaymentDetails != null && requestMetaData.PaymentDetails.PricingModelId != 0)
                        {
                            targetPricingModel = await repo.GetPricingModelsById(requestMetaData.PaymentDetails.PricingModelId);
                        }


                        PricingModel pricingModelForThisDevice = await repo.GetPricingModelByDevicePartnerFamilyAsync(
                                                                                requestMetaData.DeviceDetails.DeviceLevel,
                                                                                metaData.PricingModel.TeirId.Value,
                                                                                metaData.PricingModel.FamilyId.Value);

                        PricingModel originalPricingModel = pricingModelForThisDevice;

                        if (originalPricingModel != null && targetPricingModel != null)
                        {
                            if (!originalPricingModel.DailyPrice.HasValue || !targetPricingModel.DailyPrice.HasValue)
                            {
                                throw new Exception("Daily price(s) must have a value");
                            }


                            //Calculate the incoming value and divide by the day rate that should be charged

                            //This will be a monetary value.

                            Decimal mComingInValue = Convert.ToDecimal(metaDataVoucher.PricingModel.DailyPrice.Value) *
                                                     Convert.ToDecimal(voucher.membershiplength.Value);


                            //now divide the monetary value of the incoming voucher by the daily rate of the correct pricing model
                            Decimal? mDays = 0;
                            //handle division by zero
                            if (pricingModelForThisDevice.DailyPrice.Value > 0.00m)
                            {
                                mDays = Math.Ceiling(mComingInValue / Convert.ToDecimal(pricingModelForThisDevice.DailyPrice.Value));
                            }
                            else
                            {
                                mDays = metaDataVoucher.Voucher.membershiplength;
                            }


                            ////calculate new value of cover from new daily price and voucher length
                            //var totalVoucherCoverValue = Convert.ToDecimal(originalPricingModel.DailyPrice) * voucher.membershiplength;

                            //Decimal? mDays = (pricingModelForThisDevice.DailyPrice.Value * voucher.membershiplength.Value) /
                            //                        pricingModelForThisDevice.DailyPrice.Value;


                            Int32 iDays = Convert.ToInt32(Math.Ceiling(mDays.Value));

                            if (transactionType == Enumerations.TransactionType.Activated)
                            {

                                if (userDevice.DeviceCover.Any())
                                {
                                    //the largest id is the last, as they may not be in correct order
                                    Int32 iMaxDeviceCover = userDevice.DeviceCover.Max(dc => dc.Id);
                                    //void all previous covers
                                    foreach (var cover in userDevice.DeviceCover)
                                    {
                                        cover.Void = true;
                                        //Only update the end date of the very last cover
                                        if (cover.Id == iMaxDeviceCover)
                                        {
                                            cover.EndDate = DateTime.UtcNow;
                                        }
                                        cover.ModifiedDate = DateTime.UtcNow;
                                        await domainRepo.UpdateDeviceCoverAsync(cover);
                                    }
                                }

                                //Create new cover for
                                DeviceCover newDeviceCover = new DeviceCover()
                                {
                                    ActivatedDate = DateTime.UtcNow,
                                    AddedDate = DateTime.UtcNow,
                                    DeviceId = iDeviceID,
                                    EndDate = DateTime.UtcNow.AddDays(Convert.ToDouble(iDays)),
                                    MembershipLength = iDays.ToString(),
                                    MembershipTier = targetPricingModel.Tier.Name,
                                    Voucher = voucher.vouchercode
                                };
                            }

                            // Get code response message based on voucher result
                            Enumerations.CodeResponseMessage codeResponseMessageResult =
                                GetCodeResponseMessageResult(0, Convert.ToInt32(voucher.membershiplength),
                                                             iDays, transactionType);

                            return new CodeResponse()
                            {
                                CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                                //return the updated length, not the original voucher length
                                MembershipLength = iDays.ToString(),
                                MembershipTier = targetPricingModel.Tier.Name,
                                //CodeResponseMessage = Enumerations.GetEnumDescription(Enumerations.CodeResponseMessage.Success),
                                CodeResponseMessage = Enumerations.GetEnumDescription(codeResponseMessageResult),
                            };

                        }
                        else
                        {
                            throw new Exception("Both pricing models cannot be null");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks to see if we should return a Pro Rata CodeResponseMessage or Success CodeResponseMessage
        /// </summary>
        /// <param name="totalDaysRemaining"></param>
        /// <param name="iAdditionalDays"></param>
        /// <param name="dblTotalDays"></param>
        /// <returns></returns>
        /// 
        public static async Task<int> ValidateFreeDaysOnSubScription(int deviceID, string voucherCode, int? pricingModelId)
        {
            int freeDays = 0;
            try
            {

                FortressDomain.Repository.Respository domainRepo = new FortressDomain.Repository.Respository(new FortressDomain.Db.FortressContext());
                FortressCodeContext fcd = new FortressCodeContext();
                FortressDomain.Db.FortressContext fc = new FortressDomain.Db.FortressContext();
                Respository codesRepo = new Respository(new FortressCodeContext());
                Device device = fc.Devices.Where(dv => dv.Id == deviceID).SingleOrDefault();
                User usr = fc.Users.Where(us => us.Id == device.User.Id).SingleOrDefault();

                var activeDeviceCover = FortressDomain.Helpers.ApiHelper.GetDeviceActiveCover(device);
                if (activeDeviceCover == null)
                    return 0;

                //you cannot rely  on the existing Voucher being for the correct category of device
                //get the device pricing model for the partner of the voucher


                Voucher voucher = fcd.Vouchers.Where(v => v.vouchercode == voucherCode).SingleOrDefault();

                PricingModel voucherSubscriptionPricingModel = null;
                if (voucherCode != "")
                {
                    VoucherMetadata metaDataVoucher = voucher.VoucherMetadatas.FirstOrDefault();
                    voucherSubscriptionPricingModel = metaDataVoucher.PricingModel;
                }
                if (pricingModelId.HasValue && pricingModelId != 0)
                {
                    var tempVoucherSubscriptionPricingModel = await codesRepo.GetPricingModelsById(pricingModelId.Value);
                    if (tempVoucherSubscriptionPricingModel != null)
                        voucherSubscriptionPricingModel = tempVoucherSubscriptionPricingModel;
                }





                System.Net.Http.HttpResponseMessage message = null;



                if (voucherCode == "" && pricingModelId.HasValue)
                {
                    DeviceLevelRequestMem deviceLevelRequestMem = new DeviceLevelRequestMem()
                    {
                        DeviceCapacityRaw = device.DeviceCapacityRaw,
                        DeviceModel = device.DeviceModel,
                        DeviceModelRaw = device.DeviceModelRaw,
                        DeviceMake = device.DeviceMake,
                        UserCountryIso = usr.CountryIso,
                        PricingModelID = pricingModelId.Value
                    };
                    message = await FortressDomain.Helpers.ApiHelper.CallVoucherAPIDeviceLevelMem(deviceLevelRequestMem);
                }
                else
                {
                    DeviceLevelRequest deviceLevelRequest = new DeviceLevelRequest()
                    {
                        DeviceCapacityRaw = device.DeviceCapacityRaw,
                        DeviceModel = device.DeviceModel,
                        DeviceModelRaw = device.DeviceModelRaw,
                        DeviceMake = device.DeviceMake,
                        UserCountryIso = usr.CountryIso,
                        VoucherCode = voucher.vouchercode
                    };
                    await FortressDomain.Helpers.ApiHelper.CallVoucherAPIDeviceLevel(deviceLevelRequest);
                }

                var deviceLevel = "0";
                if (message != null)
                {
                    if (message.IsSuccessStatusCode)
                    {
                        String responseString = message.Content.ReadAsStringAsync().Result;
                        Tuple<Boolean, String> responseObject = JsonConvert.DeserializeObject<Tuple<Boolean, String>>(responseString);
                        if (!String.IsNullOrEmpty(responseObject.Item2))
                        {
                            deviceLevel = responseObject.Item2;
                        }
                    }
                }
                else
                {
                    return 0;
                }

                PricingModel newVoucherSubcriptionPricingModelByDeviceLevel = await codesRepo.GetPricingModelByDevicePartnerFamilyAsync(deviceLevel,
                                                                                                                   voucherSubscriptionPricingModel.TeirId.Value,
                                                                                                                   voucherSubscriptionPricingModel.FamilyId.Value);

                //var activeDeviceCover = await domainRepo.GetActiveDeviceCoverByDeviceIdAsync(iDeviceID);
                PricingModel devicePreviousPricingModelByDeviceLevel = null;



                PricingModel newVoucherPricingModelByDeviceLevel = await codesRepo.GetPricingModelByDevicePartnerFamilyAsync(deviceLevel,
                                                                                                               voucherSubscriptionPricingModel.TeirId.Value,
                                                                                                               voucherSubscriptionPricingModel.FamilyId.Value);


                if (activeDeviceCover != null)
                {
                    //get pricing model associated with the voucher (irrespective of level)
                    //this will allow us to get pricing model for the currently active voucher

                    devicePreviousPricingModelByDeviceLevel = await codesRepo.GetPricingModelByVoucherCodeAsync(activeDeviceCover.Voucher);

                    //Get the pricing model of the device level, tier and family for pricing model as may have been pro-rated previously
                    devicePreviousPricingModelByDeviceLevel = await codesRepo.GetPricingModelByDevicePartnerFamilyAsync(deviceLevel,
                                                                                              devicePreviousPricingModelByDeviceLevel.TeirId.Value,
                                                                                              devicePreviousPricingModelByDeviceLevel.FamilyId.Value);
                }

                // Moving between 2 active devices
                //if (devicePreviousPricingModelByDeviceLevel != null && activeDeviceCover != null && validatedTrans.Any())
                if (devicePreviousPricingModelByDeviceLevel != null && activeDeviceCover != null)
                {
                    //get the number of days remaining on the cover
                    DateTime dt = DateTime.UtcNow;
                    TimeSpan timeOfDay = dt.TimeOfDay;
                    if (activeDeviceCover.EndDate.Date == DateTime.UtcNow.Date)
                    {
                        dt = dt.AddDays(-1);
                    }

                    DateTime fullStartDateTime = activeDeviceCover.EndDate.Date.Add(timeOfDay);

                    TimeSpan tsDaysRemaining = fullStartDateTime.Subtract(dt);
                    Double totalDaysRemaining = Math.Ceiling(tsDaysRemaining.TotalDays);
                    if (totalDaysRemaining < 0)
                    {
                        totalDaysRemaining = 0;
                    }

                    //Get the in the bank value on the device
                    //This is the value of the days remaining in cover multiplied by the daily rate of the pricing model that voucher belongs to.
                    //This will be a monetary value.
                    Decimal mInTheBankValue = Convert.ToDecimal(devicePreviousPricingModelByDeviceLevel.DailyPrice.Value) *
                                              Convert.ToDecimal(totalDaysRemaining);

                    FortressCodeContext db = new FortressCodeContext();

                    Decimal dailyPriceVal = Convert.ToDecimal(voucherSubscriptionPricingModel.DailyPrice.Value);
                    Level lev = db.Levels.Where(le => le.Name == deviceLevel).SingleOrDefault();
                    //Check if the voucher is gateway to ensure pro rata calculations are calculated against the voucher familys correct
                    if (voucherCode != "")
                    {
                        VoucherMetadata vmd = voucher.VoucherMetadatas.FirstOrDefault();
                        if (vmd.VoucherTypeID == (Int32)Enumerations.VoucherType.Gateway)
                        {
                            // Check if the vouchers pricing model passed in is the correct level for the device
                            // If not grab the correct pricing model from the same family
                            if (vmd.PricingModel.Level.Name != deviceLevel)
                            {
                                try
                                {
                                    var pmNewLevel = db.PricingModels.Where(pm => pm.FamilyId == vmd.PricingModel.FamilyId && pm.LevelId == lev.Id && pm.Tier.TierLevel == vmd.PricingModel.Tier.TierLevel).SingleOrDefault();
                                    dailyPriceVal = Convert.ToDecimal(pmNewLevel.DailyPrice.Value);
                                }
                                catch
                                {
                                }

                            }
                        }
                    }
                    //Letter representation of device level
                    var dbContext = new FortressCodesDomain.DbModels.FortressCodeContext();



                    Decimal mTotalValue = mInTheBankValue;
                    Decimal? dDaysToApply = 0;

                    //divide by zero error handling
                    if (newVoucherPricingModelByDeviceLevel.DailyPrice.Value != 0)
                    {
                        dDaysToApply = Math.Ceiling(mTotalValue / Convert.ToDecimal(newVoucherPricingModelByDeviceLevel.DailyPrice.Value));
                    }
                    else
                    {
                        //MMC - If a zero value voucher is used all existing coverage is removed
                        //if (mInTheBankValue <= 0)
                        //{
                        dDaysToApply = (decimal)totalDaysRemaining;
                        //}

                    }


                    if (mTotalValue != Decimal.Zero)
                    {
                        Int32 iAdditionalDays = 0;
                        if (newVoucherPricingModelByDeviceLevel.TeirId != devicePreviousPricingModelByDeviceLevel.TeirId)
                        {
                            bool upgrade = true;
                            //Check if the tierlevel of the existing cover is less than the new cover, if so we send an upgrade email. If not we send a downgrade email
                            if (newVoucherPricingModelByDeviceLevel.Tier != null && devicePreviousPricingModelByDeviceLevel.Tier != null)
                            {
                                if (newVoucherPricingModelByDeviceLevel.Tier.TierLevel != null && devicePreviousPricingModelByDeviceLevel.Tier.TierLevel != null)
                                {
                                    if (newVoucherPricingModelByDeviceLevel.Tier.TierLevel < devicePreviousPricingModelByDeviceLevel.Tier.TierLevel)
                                        upgrade = false;
                                }
                            }
                            //if(newVoucherPricingModelByDeviceLevel.Tier.TierLevel != null && devicePreviousPricingModelByDeviceLevel.Tier.TierLevel)



                            //Decimal mAdditionalDays = mCurrentRemainingValue / mNewDailyRate;
                            Decimal mAdditionalDays = Convert.ToDecimal(dDaysToApply) - Convert.ToDecimal(totalDaysRemaining);

                            iAdditionalDays = Convert.ToInt32(Math.Ceiling(mAdditionalDays));



                        }
                        else
                        {
                            iAdditionalDays = Convert.ToInt32(Math.Ceiling(totalDaysRemaining));
                        }

                        //double dblTotalDays = (iNewCodeDays + iAdditionalDays);
                        double dblTotalDays = Convert.ToDouble(dDaysToApply.Value);

                        return int.Parse(dDaysToApply.ToString());
                    }
                    else
                    {
                        //TODO: do we need actual error responses to be sent rather than exceptions
                        throw new Exception("Remaining value and daily rate cannot be null");

                        return 0;
                    }
                    //end pro rate wizardry
                }
            }
            catch
            {
                return 0;
            }
            return freeDays;
        }

        public static async Task<PricingModel> GetPricingModelNewDevice(int deviceID, string voucherCode)
        {
            try
            {

                FortressDomain.Repository.Respository domainRepo = new FortressDomain.Repository.Respository(new FortressDomain.Db.FortressContext());
                FortressCodeContext fcd = new FortressCodeContext();
                FortressDomain.Db.FortressContext fc = new FortressDomain.Db.FortressContext();
                Respository codesRepo = new Respository(new FortressCodeContext());
                Device device = fc.Devices.Where(dv => dv.Id == deviceID).SingleOrDefault();
                User usr = fc.Users.Where(us => us.Id == device.User.Id).SingleOrDefault();

                var activeDeviceCover = FortressDomain.Helpers.ApiHelper.GetDeviceActiveCover(device);

                //you cannot rely  on the existing Voucher being for the correct category of device
                //get the device pricing model for the partner of the voucher


                var voucher = fcd.Vouchers.Where(v => v.vouchercode == voucherCode).SingleOrDefault();
                var metaDataVoucher = voucher.VoucherMetadatas.FirstOrDefault();
                var voucherSubscriptionPricingModel = metaDataVoucher.PricingModel;
                DeviceLevelRequest deviceLevelRequest = new DeviceLevelRequest()
                {
                    DeviceCapacityRaw = device.DeviceCapacityRaw,
                    DeviceModel = device.DeviceModel,
                    DeviceModelRaw = device.DeviceModelRaw,
                    DeviceMake = device.DeviceMake,
                    UserCountryIso = usr.CountryIso,
                    VoucherCode = voucher.vouchercode
                };

                var message = await FortressDomain.Helpers.ApiHelper.CallVoucherAPIDeviceLevel(deviceLevelRequest);
                var deviceLevel = "0";
                if (message.IsSuccessStatusCode)
                {
                    if (message != null)
                    {
                        String responseString = message.Content.ReadAsStringAsync().Result;
                        Tuple<Boolean, String> responseObject = JsonConvert.DeserializeObject<Tuple<Boolean, String>>(responseString);
                        if (!String.IsNullOrEmpty(responseObject.Item2))
                        {
                            deviceLevel = responseObject.Item2;
                        }
                    }
                }
                else
                {
                    return null;
                }

                PricingModel newVoucherSubcriptionPricingModelByDeviceLevel = await codesRepo.GetPricingModelByDevicePartnerFamilyAsync(deviceLevel,
                                                                                                                 voucherSubscriptionPricingModel.TeirId.Value,
                                                                                                                   voucherSubscriptionPricingModel.FamilyId.Value);
                return newVoucherSubcriptionPricingModelByDeviceLevel;
            }
            catch (Exception ex)
            { }
            return null;
        }
        public static async Task<CoverValidationCodeResponse> ValidateChangeCoverProRataAsyncMem(Voucher voucher, RequestMetaDataMem requestMetaData,
                                                                               IRepository repo,
                                                                               FortressDomain.Repository.IRepository domainRepo,
                                                                               Enumerations.TransactionType transactionType, DeviceCover userDeviceCover)
        {
            string currSymbol = "";
            switch (requestMetaData.UserDetails.CountryIso.ToLower())
            {
                case "gb":
                    currSymbol = "£";
                    break;
                case "us":
                    currSymbol = "$";
                    break;
            }


            if (_transactionTypes == null || _transactionTypes.Count == 0)
            {
                _transactionTypes = await repo.GetAllTransactionTypesAsync();
            }
            Int32 iDeviceID = requestMetaData.DeviceDetails.DeviceID;
            /*********************************************************************************************************/
            //MISSING DEVICE
            var userDevice = await domainRepo.GetDeviceByIdAsync(iDeviceID);

            //SUBSCRIPTION CODE
            //2017-8-17 addition voucher null check silly me
            if (voucher != null)
            {
                if (voucher.VoucherMetadatas.SingleOrDefault().VoucherTypeID == (Int32)Enumerations.VoucherType.Subscription)
                {
                    Enumerations.CodeResponseMessage codeSubscriptionResponseMessageResult = new Enumerations.CodeResponseMessage();
                    int daysInMonth = DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month);

                    VoucherMetadata voucherMetaData = voucher.VoucherMetadatas.SingleOrDefault();
                    var voucherSubscriptionPricingModel = voucherMetaData.PricingModel;

                    if (requestMetaData.PaymentDetails != null)
                    {
                        if (requestMetaData.PaymentDetails.PricingModelId != 0)
                        {
                            var tempVoucherSubscriptionPricingModel = await repo.GetPricingModelsById(requestMetaData.PaymentDetails.PricingModelId);
                            if (tempVoucherSubscriptionPricingModel != null)
                                voucherSubscriptionPricingModel = tempVoucherSubscriptionPricingModel;
                        }
                    }

                    PricingModel newVoucherSubcriptionPricingModelByDeviceLevel = await repo.GetPricingModelByDevicePartnerFamilyAsync(requestMetaData.DeviceDetails.DeviceLevel,
                                                                                                                       voucherSubscriptionPricingModel.TeirId.Value,
                                                                                                                       voucherSubscriptionPricingModel.FamilyId.Value);

                    //pro rata wizardry
                    int daysRemaining = 0;
                    bool proRata = false;

                    var activeDeviceCover = userDeviceCover;
                    if (activeDeviceCover != null)
                    {
                        daysRemaining = await ValidateFreeDaysOnSubScription(iDeviceID, voucher.vouchercode, requestMetaData.PaymentDetails.PricingModelId);
                    }



                    string marketingMessage = "";
                    int freeDays = daysRemaining;
                    if (voucherMetaData.FreeDays.HasValue)
                    {
                        freeDays = freeDays + voucherMetaData.FreeDays.Value;
                    }
                    string billingString = "";
                    decimal dailyPrice = Convert.ToDecimal(newVoucherSubcriptionPricingModelByDeviceLevel.DailyPrice.Value);
                    decimal monthlyPrice = Convert.ToDecimal(newVoucherSubcriptionPricingModelByDeviceLevel.MonthlyPrice.Value);
                    decimal annualPrice = Convert.ToDecimal(newVoucherSubcriptionPricingModelByDeviceLevel.AnnualPrice.Value);

                    bool useResource = false;
                    Resource res = await domainRepo.GetResourceByResourceKeyAndLocale(string.Format("SUBSCRIPTIONVOUCHER.MARKETING.{0}.{1}", (voucherMetaData.PaymentSource.HasValue ? voucherMetaData.PaymentSource.Value : (Int32)FortressDomain.Helpers.Enumerations.SubscriptionPaymentSource.PaidFor) == (Int32)FortressDomain.Helpers.Enumerations.SubscriptionPaymentSource.PaidFor ? "PAIDFOR" : "SUBSIDISED", freeDays > 0 ? (requestMetaData.BillingOption == 0 ? "FREEMONTHLY" : "FREEANNUALLY") : (requestMetaData.BillingOption == 0 ? "MONTHLY" : "ANNUALLY")), userDevice.SourceLanguage);
                    if (res != null)
                    {
                        useResource = true;
                        res.ResourceValue = res.ResourceValue.Replace("##price##", currSymbol + (requestMetaData.BillingOption == 0 ? monthlyPrice : annualPrice));
                        res.ResourceValue = res.ResourceValue.Replace("##freedays##", freeDays.ToString());
                        res.ResourceValue = res.ResourceValue.Replace("##tier##", newVoucherSubcriptionPricingModelByDeviceLevel.Tier.Name);
                    }

                    if (requestMetaData.BillingOption.Value == 0)
                        billingString = string.Format("be billed monthly at {0}", currSymbol + monthlyPrice);
                    else if (requestMetaData.BillingOption.Value == 1)
                        billingString = string.Format("be billed annualy at {0}", currSymbol + annualPrice);



                    //Amended by MMC so the return is still valid
                    if (freeDays > 0)
                        marketingMessage = string.Format("You will receive {0} days of free cover and then {1}", freeDays, billingString);
                    else
                    {
                        marketingMessage = "You will " + billingString;
                    }

                    if (useResource)
                        marketingMessage = res.ResourceValue;

                    return new CoverValidationCodeResponse()
                    {
                        CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                        //return the updated length, not the original voucher length
                        MarketingMessage = marketingMessage,
                        MembershipLength = (requestMetaData.BillingOption == 0 ? daysInMonth.ToString() : "365"),
                        MembershipTier = voucherSubscriptionPricingModel.Tier.Name,
                        CodeResponseMessage = Enumerations.GetEnumDescription(codeSubscriptionResponseMessageResult)
                    };

                    //return new CoverValidationCodeResponse()
                    //{
                    //    CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                    //    //return the updated length, not the original voucher length
                    //    MarketingMessage = marketingMessage,
                    //    MembershipLength = null,
                    //    MembershipTier = null,
                    //    CodeResponseMessage = null
                    //};
                    //END OF SUBSCRIPTION CODE
                }
            }
            bool usePayment = false;
            if (requestMetaData.PaymentDetails != null && voucher == null)
            {
                if (requestMetaData.PaymentDetails.PricingModelId != 0)
                {
                    //code to create voucher and meta data in memory to avoid having to create  a dummy voucher everytime payment information is sent for validation
                    Voucher BvbActiVoucher = new Voucher();
                    VoucherMetadata vmd = new FortressCodesDomain.DbModels.VoucherMetadata();
                    bool useOneTimeAnnual = false;
                    if (useOneTimeAnnual)
                    {
                        BvbActiVoucher.multiuse = false;
                        BvbActiVoucher.expirydate = DateTime.Now.AddDays(365);
                        BvbActiVoucher.createddate = DateTime.Now;
                        BvbActiVoucher.createduser = "System";
                        BvbActiVoucher.vouchercode = "Payment Validation request device:" + userDevice.User.Id;
                        BvbActiVoucher.membershiplength = 365;
                        BvbActiVoucher.revoked = false;
                        BvbActiVoucher.incentivesource = "";
                        BvbActiVoucher.incentivemembershiptierid = 0;
                        BvbActiVoucher.incentivemembershiplength = 0;
                        BvbActiVoucher.deviceid = userDevice.Id.ToString();
                        BvbActiVoucher.partnernote = "System created voucher";
                        BvbActiVoucher.batchnumber = 0;
                    }
                    else
                    {
                        //subscription start
                        Enumerations.CodeResponseMessage codeSubscriptionResponseMessageResult = new Enumerations.CodeResponseMessage();
                        int daysInMonth = DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month);

                        PricingModel voucherSubscriptionPricingModel = null;


                        var tempVoucherSubscriptionPricingModel = await repo.GetPricingModelsById(requestMetaData.PaymentDetails.PricingModelId);
                        if (tempVoucherSubscriptionPricingModel != null)
                            voucherSubscriptionPricingModel = tempVoucherSubscriptionPricingModel;


                        PricingModel newVoucherSubcriptionPricingModelByDeviceLevel = await repo.GetPricingModelByDevicePartnerFamilyAsync(requestMetaData.DeviceDetails.DeviceLevel,
                                                                                                                           voucherSubscriptionPricingModel.TeirId.Value,
                                                                                                                           voucherSubscriptionPricingModel.FamilyId.Value);

                        //pro rata wizardry
                        int daysRemaining = 0;
                        bool proRata = false;

                        var activeDeviceCover = userDeviceCover;
                        if (activeDeviceCover != null)
                        {
                            daysRemaining = await ValidateFreeDaysOnSubScription(iDeviceID, (voucher == null ? "" : voucher.vouchercode), requestMetaData.PaymentDetails.PricingModelId);
                        }



                        string marketingMessage = "";
                        int freeDays = daysRemaining;

                        string billingString = "";
                        decimal dailyPrice = Convert.ToDecimal(newVoucherSubcriptionPricingModelByDeviceLevel.DailyPrice.Value);
                        decimal monthlyPrice = Convert.ToDecimal(newVoucherSubcriptionPricingModelByDeviceLevel.MonthlyPrice.Value);
                        decimal annualPrice = Convert.ToDecimal(newVoucherSubcriptionPricingModelByDeviceLevel.AnnualPrice.Value);


                        //code here 
                        bool useResource = false;
                        Resource res = await domainRepo.GetResourceByResourceKeyAndLocale(string.Format("SUBSCRIPTIONVOUCHER.MARKETING.PAIDFOR.{0}", freeDays > 0 ? (requestMetaData.BillingOption == 0 ? "FREEMONTHLY" : "FREEANNUALLY") : (requestMetaData.BillingOption == 0 ? "MONTHLY" : "ANNUALLY")), userDevice.SourceLanguage);
                        if (res != null)
                        {
                            useResource = true;
                            res.ResourceValue = res.ResourceValue.Replace("##price##", currSymbol + (requestMetaData.BillingOption == 0 ? monthlyPrice : annualPrice));
                            res.ResourceValue = res.ResourceValue.Replace("##freedays##", freeDays.ToString());
                            res.ResourceValue = res.ResourceValue.Replace("##tier##", newVoucherSubcriptionPricingModelByDeviceLevel.Tier.Name);
                        }
                        if (requestMetaData.BillingOption.Value == 0)
                            billingString = string.Format("be billed monthly at {0}", currSymbol + monthlyPrice);
                        else if (requestMetaData.BillingOption.Value == 1)
                            billingString = string.Format("be billed annualy at {0}", currSymbol + annualPrice);



                        if (codeSubscriptionResponseMessageResult == null)
                        {
                            codeSubscriptionResponseMessageResult = Enumerations.CodeResponseMessage.Success;
                        }
                        //Amended by MMC so the return is still valid
                        if (freeDays > 0)
                            marketingMessage = string.Format("You will receive {0} days of free cover and then {1}", freeDays, billingString);
                        else
                        {
                            marketingMessage = "You will " + billingString;
                        }
                        if (useResource)
                            marketingMessage = res.ResourceValue;

                        return new CoverValidationCodeResponse()
                        {
                            CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                            //return the updated length, not the original voucher length
                            MarketingMessage = marketingMessage,
                            MembershipLength = freeDays != 0 ? freeDays.ToString() : (requestMetaData.BillingOption == 0 ? daysInMonth.ToString() : "365"),
                            MembershipTier = voucherSubscriptionPricingModel.Tier.Name,
                            CodeResponseMessage = Enumerations.GetEnumDescription(codeSubscriptionResponseMessageResult)
                        };
                        //END OF SUBSCRIPTION CODE
                        // subscription end
                    }

                    vmd.VoucherTypeID = (Int32)Enumerations.VoucherType.Voucher;
                    vmd.PricingModelID = requestMetaData.PaymentDetails.PricingModelId;

                    vmd.PricingModel = await repo.GetPricingModelsById(requestMetaData.PaymentDetails.PricingModelId);
                    List<FortressCodesDomain.DbModels.VoucherMetadata> vmds = new List<FortressCodesDomain.DbModels.VoucherMetadata>();
                    vmds.Add(vmd);
                    BvbActiVoucher.VoucherMetadatas = vmds;

                    voucher = BvbActiVoucher;
                    usePayment = true;
                }
            }


            PricingModel voucherPricingModel = null;
            if (usePayment)
            {
                voucherPricingModel = await repo.GetPricingModelsById(voucher.VoucherMetadatas.First().PricingModelID.Value);
            }
            else
                voucherPricingModel = await repo.GetPricingModelByVoucherCodeAsync(voucher.vouchercode);





            if (userDevice.MissingDevice.HasValue && userDevice.MissingDevice.Value)
            {
                DeviceLevelRequest deviceLevelRequest = new DeviceLevelRequest()
                {
                    DeviceCapacityRaw = userDevice.DeviceCapacityRaw,
                    DeviceModel = userDevice.DeviceModel,
                    DeviceModelRaw = userDevice.DeviceModelRaw,
                    DeviceMake = userDevice.DeviceMake,
                    UserCountryIso = (userDevice.User != null) ? userDevice.User.CountryIso : String.Empty,
                    VoucherCode = requestMetaData.Code,
                };
                var deviceLevelResult = await repo.GetDeviceLevelAsync(deviceLevelRequest);




                DeviceCover missingCover = new DeviceCover();
                missingCover.ActivatedDate = DateTime.UtcNow;
                missingCover.AddedDate = DateTime.UtcNow;
                missingCover.DeviceId = userDevice.Id;

                Int32 maxTempCoverDays = 7;
                if (voucher.membershiplength.HasValue)
                {
                    //If the voucher is for longer than 7 days, cap at max of 7 days cover
                    if (voucher.membershiplength >= maxTempCoverDays)
                    {
                        missingCover.EndDate = DateTime.UtcNow.AddDays(maxTempCoverDays);
                        missingCover.MembershipLength = maxTempCoverDays.ToString();
                    }
                    else
                    {
                        missingCover.EndDate = DateTime.UtcNow.AddDays(voucher.membershiplength.Value);
                        missingCover.MembershipLength = voucher.membershiplength.Value.ToString();
                    }
                }
                if (voucherPricingModel.Tier != null)
                {
                    missingCover.MembershipTier = voucherPricingModel.Tier.Name;
                }
                missingCover.ModifiedDate = DateTime.UtcNow;
                missingCover.Voucher = voucher.vouchercode;

                //Need to call this synchronously - when marked with the await keyword 
                //2 device covers would be created (with created dated 10th of seconds apart),
                //this would breaks subsequent calls as there should be only one active cover

                //CODE REMOVED AS REDENDANT
                //domainRepo.CreateDeviceCoverAsync(missingCover);

                return new CoverValidationCodeResponse()
                {
                    CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                    //return the updated length, not the original voucher length
                    MembershipLength = missingCover.MembershipLength,
                    MembershipTier = missingCover.MembershipTier,
                    CodeResponseMessage = Enumerations.GetEnumDescription(Enumerations.CodeResponseMessage.MissingDevice)
                };
            }
            /*********************************************************************************************************/


            else
            {
                //device is not missing



                var activeDeviceCover = userDeviceCover;
                //var activeDeviceCover = await domainRepo.GetActiveDeviceCoverByDeviceIdAsync(iDeviceID);
                PricingModel devicePreviousPricingModelByDeviceLevel = null;

                //you cannot rely  on the existing Voucher being for the correct category of device
                //get the device pricing model for the partner of the voucher
                var metaDataVoucher = voucher.VoucherMetadatas.FirstOrDefault();

                PricingModel newVoucherPricingModelByDeviceLevel = await repo.GetPricingModelByDevicePartnerFamilyAsync(requestMetaData.DeviceDetails.DeviceLevel,
                                                                                                               voucherPricingModel.TeirId.Value,
                                                                                                               voucherPricingModel.FamilyId.Value);


                if (activeDeviceCover != null)
                {
                    //get pricing model associated with the voucher (irrespective of level)
                    //this will allow us to get pricing model for the currently active voucher

                    devicePreviousPricingModelByDeviceLevel = await repo.GetPricingModelByVoucherCodeAsync(activeDeviceCover.Voucher);

                    //Get the pricing model of the device level, tier and family for pricing model as may have been pro-rated previously
                    devicePreviousPricingModelByDeviceLevel = await repo.GetPricingModelByDevicePartnerFamilyAsync(requestMetaData.DeviceDetails.DeviceLevel,
                                                                                              devicePreviousPricingModelByDeviceLevel.TeirId.Value,
                                                                                              devicePreviousPricingModelByDeviceLevel.FamilyId.Value);

                }

                // Moving between 2 active devices
                //if (devicePreviousPricingModelByDeviceLevel != null && activeDeviceCover != null && validatedTrans.Any())
                if (devicePreviousPricingModelByDeviceLevel != null && activeDeviceCover != null)
                {
                    //get the number of days remaining on the cover
                    DateTime dt = DateTime.UtcNow;
                    TimeSpan timeOfDay = dt.TimeOfDay;
                    if (activeDeviceCover.EndDate.Date == DateTime.UtcNow.Date)
                    {
                        dt = dt.AddDays(-1);
                    }

                    DateTime fullStartDateTime = activeDeviceCover.EndDate.Date.Add(timeOfDay);

                    TimeSpan tsDaysRemaining = fullStartDateTime.Subtract(dt);
                    Double totalDaysRemaining = Math.Ceiling(tsDaysRemaining.TotalDays);
                    if (totalDaysRemaining < 0)
                    {
                        totalDaysRemaining = 0;
                    }

                    //Get the in the bank value on the device
                    //This is the value of the days remaining in cover multiplied by the daily rate of the pricing model that voucher belongs to.
                    //This will be a monetary value.
                    Decimal mInTheBankValue = Convert.ToDecimal(devicePreviousPricingModelByDeviceLevel.DailyPrice.Value) *
                                              Convert.ToDecimal(totalDaysRemaining);

                    FortressCodeContext db = new FortressCodeContext();

                    Decimal dailyPriceVal = Convert.ToDecimal(metaDataVoucher.PricingModel.DailyPrice.Value);
                    Decimal memLength = Convert.ToDecimal(voucher.membershiplength.Value);
                    string deviceLevel = requestMetaData.DeviceDetails.DeviceLevel;
                    Level lev = db.Levels.Where(le => le.Name == deviceLevel).SingleOrDefault();
                    //Check if the voucher is gateway to ensure pro rata calculations are calculated against the voucher familys correct
                    if (metaDataVoucher.VoucherTypeID == (Int32)Enumerations.VoucherType.Gateway)
                    {
                        // Check if the vouchers pricing model passed in is the correct level for the device
                        // If not grab the correct pricing model from the same family
                        if (metaDataVoucher.PricingModel.Level.Name != deviceLevel)
                        {
                            try
                            {
                                var pmNewLevel = db.PricingModels.Where(pm => pm.FamilyId == metaDataVoucher.PricingModel.FamilyId && pm.LevelId == lev.Id && pm.Tier.TierLevel == metaDataVoucher.PricingModel.Tier.TierLevel).SingleOrDefault();
                                dailyPriceVal = Convert.ToDecimal(pmNewLevel.DailyPrice.Value);
                            }
                            catch
                            {
                            }

                        }
                    }
                    //Letter representation of device level
                    var dbContext = new FortressCodesDomain.DbModels.FortressCodeContext();



                    Decimal mComingInValue = dailyPriceVal *
                                         memLength;

                    Decimal mTotalValue = mInTheBankValue + mComingInValue;

                    Decimal? dDaysToApply = 0;

                    //divide by zero error handling
                    if (newVoucherPricingModelByDeviceLevel.DailyPrice.Value != 0)
                    {
                        dDaysToApply = Math.Ceiling(mTotalValue / Convert.ToDecimal(newVoucherPricingModelByDeviceLevel.DailyPrice.Value));
                    }
                    else
                    {
                        //MMC - If a zero value voucher is used all existing coverage is removed
                        //if (mInTheBankValue <= 0)
                        //{
                        dDaysToApply = metaDataVoucher.Voucher.membershiplength;
                        //}

                    }
                    if (devicePreviousPricingModelByDeviceLevel.DailyPrice.Value != 0 && newVoucherPricingModelByDeviceLevel.DailyPrice.Value == 0)
                    {
                        return new CoverValidationCodeResponse()
                        {
                            CodeResponseStatus = Enumerations.GetEnumDescription(Enumerations.TransactionType.VoucherZeroCoverPaid),
                            //return the updated length, not the original voucher length
                            MembershipLength = "NA",
                            MembershipTier = "NA",
                            CodeResponseMessage = Enumerations.GetEnumDescription(Enumerations.TransactionType.VoucherZeroCoverPaid)
                        };
                    }



                    if (mTotalValue != Decimal.Zero && voucher.membershiplength.Value != Decimal.Zero)
                    {
                        Int32 iAdditionalDays = 0;
                        if (newVoucherPricingModelByDeviceLevel.TeirId != devicePreviousPricingModelByDeviceLevel.TeirId)
                        {
                            bool upgrade = true;
                            //Check if the tierlevel of the existing cover is less than the new cover, if so we send an upgrade email. If not we send a downgrade email
                            if (newVoucherPricingModelByDeviceLevel.Tier != null && devicePreviousPricingModelByDeviceLevel.Tier != null)
                            {
                                if (newVoucherPricingModelByDeviceLevel.Tier.TierLevel != null && devicePreviousPricingModelByDeviceLevel.Tier.TierLevel != null)
                                {
                                    if (newVoucherPricingModelByDeviceLevel.Tier.TierLevel < devicePreviousPricingModelByDeviceLevel.Tier.TierLevel)
                                        upgrade = false;
                                }
                            }
                            //if(newVoucherPricingModelByDeviceLevel.Tier.TierLevel != null && devicePreviousPricingModelByDeviceLevel.Tier.TierLevel)



                            //Decimal mAdditionalDays = mCurrentRemainingValue / mNewDailyRate;
                            Decimal mAdditionalDays = Convert.ToDecimal(dDaysToApply) - Convert.ToDecimal(totalDaysRemaining);

                            iAdditionalDays = Convert.ToInt32(Math.Ceiling(mAdditionalDays));



                        }
                        else
                        {
                            iAdditionalDays = Convert.ToInt32(Math.Ceiling(totalDaysRemaining));
                        }

                        //double dblTotalDays = (iNewCodeDays + iAdditionalDays);
                        double dblTotalDays = Convert.ToDouble(dDaysToApply.Value);

                        // Get code response message based on voucher result
                        Enumerations.CodeResponseMessage codeResponseMessageResult =
                            GetCodeResponseMessageResult(Convert.ToInt32(totalDaysRemaining),
                                                         Convert.ToInt32(voucher.membershiplength),
                                                         dblTotalDays, transactionType);

                        //Amended by MMC so the return is still valid
                        return new CoverValidationCodeResponse()
                        {
                            CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                            //return the updated length, not the original voucher length
                            MembershipLength = ((dblTotalDays).ToString()),
                            MembershipTier = newVoucherPricingModelByDeviceLevel.Tier.Name,
                            CodeResponseMessage = Enumerations.GetEnumDescription(codeResponseMessageResult)
                        };
                    }
                    else
                    {
                        // code in here 



                        if ((newVoucherPricingModelByDeviceLevel.Tier.Name == activeDeviceCover.MembershipTier) && (newVoucherPricingModelByDeviceLevel.DailyPrice.Value == 0 && devicePreviousPricingModelByDeviceLevel.DailyPrice.Value == 0))
                        {

                            Enumerations.CodeResponseMessage codeResponseMessageResult =
                        GetCodeResponseMessageResult((Int32)Math.Ceiling(totalDaysRemaining),
                                                     Convert.ToInt32(voucher.membershiplength),
                                                     (Int32)Math.Ceiling(totalDaysRemaining) + Convert.ToInt32(voucher.membershiplength), transactionType);

                            return new CoverValidationCodeResponse()
                            {
                                CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                                //return the updated length, not the original voucher length
                                MembershipLength = ((Int32)Math.Ceiling(totalDaysRemaining) + Convert.ToInt32(voucher.membershiplength)).ToString(),
                                MembershipTier = newVoucherPricingModelByDeviceLevel.Tier.Name,
                                CodeResponseMessage = Enumerations.GetEnumDescription(codeResponseMessageResult)
                            };
                        }
                        else if ((newVoucherPricingModelByDeviceLevel.Tier.Name != activeDeviceCover.MembershipTier) && (newVoucherPricingModelByDeviceLevel.DailyPrice.Value == 0 && devicePreviousPricingModelByDeviceLevel.DailyPrice.Value == 0))
                        {
                            Enumerations.CodeResponseMessage codeResponseMessageResult =
                                GetCodeResponseMessageResult((Int32)Math.Ceiling(totalDaysRemaining),
                                                             Convert.ToInt32(voucher.membershiplength),
                                                             Convert.ToInt32(voucher.membershiplength), transactionType);

                            return new CoverValidationCodeResponse()
                            {
                                CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                                //return the updated length, not the original voucher length
                                MembershipLength = voucher.membershiplength.ToString(),
                                MembershipTier = newVoucherPricingModelByDeviceLevel.Tier.Name,
                                CodeResponseMessage = Enumerations.GetEnumDescription(codeResponseMessageResult)
                            };
                        }

                        //TODO: do we need actual error responses to be sent rather than exceptions
                        throw new Exception("Remaining value and daily rate cannot be null");

                    }
                }
                else
                {
                    //Fortress Freedom section
                    // Get the previous device's cover
                    var user = await domainRepo.GetUserByEmailAsync(requestMetaData.UserDetails.Email);
                    var devices = await domainRepo.GetDevicesByUserIdAsync(user.Id);

                    Int32 iActiveDeviceID = 0;
                    foreach (var itemDevice in devices)
                    {
                        var itemDeviceCovers = itemDevice.DeviceCover.Where(p => p.EndDate >= DateTime.UtcNow && (!p.Void.HasValue || (p.Void.HasValue && p.Void.Value == false))).ToList();
                        if (itemDeviceCovers.Any())
                        {
                            if (itemDevice.Id != userDevice.Id)
                                iActiveDeviceID = itemDevice.Id;
                        }
                    }

                    Device previousDevice = await domainRepo.GetDeviceByIdAsync(iActiveDeviceID);

                    //Scenario where you have an old device cover
                    if (previousDevice != null)
                    {
                        // Get last active device cover for devices
                        DeviceCover oldActiveDeviceCover = await domainRepo.GetActiveDeviceCoverByDeviceIdAsync(previousDevice.Id);

                        if (oldActiveDeviceCover == null)
                        {
                            throw new Exception("Old device does not have active cover");
                        }

                        // Pass users new device details to the voucher API to get the tier the device is on
                        DeviceLevelRequest previousDeviceLevelRequest = new DeviceLevelRequest()
                        {
                            DeviceCapacityRaw = previousDevice.DeviceCapacityRaw,
                            DeviceModel = previousDevice.DeviceModel,
                            DeviceModelRaw = previousDevice.DeviceModelRaw,
                            DeviceMake = previousDevice.DeviceMake,
                            UserCountryIso = user.CountryIso,
                            VoucherCode = requestMetaData.Code,
                        };

                        // Get the new device level
                        String oldDeviceLevel = null;
                        var previousDeviceLevelResult = await repo.GetDeviceLevelAsync(previousDeviceLevelRequest);
                        oldDeviceLevel = previousDeviceLevelResult.Item2;

                        if (oldDeviceLevel == null)
                        {
                            throw new Exception("Device does not have a level");
                        }

                        var oldDeviceVoucherMetadatas = voucher.VoucherMetadatas.FirstOrDefault();

                        if (oldDeviceVoucherMetadatas == null)
                        {
                            throw new Exception("Voucher does not have meta data");
                        }

                        PricingModel oldDevicePricingModel = oldDeviceVoucherMetadatas.PricingModel;

                        //Get the pricing model of the device level, tier and family for pricing model as may have been pro-rated previously
                        oldDevicePricingModel = await repo.GetPricingModelByDevicePartnerFamilyAsync(oldDeviceLevel,
                                                                                                    oldDevicePricingModel.TeirId.Value,
                                                                                                    oldDevicePricingModel.FamilyId.Value);

                        Device newDevice = await domainRepo.GetDeviceByIdAsync(iDeviceID);

                        if (newDevice == null)
                        {
                            throw new Exception("New device does not exist");
                        }

                        // Pass users new device details to the voucher API to get the tier the device is on
                        DeviceLevelRequest deviceLevelRequest = new DeviceLevelRequest()
                        {
                            DeviceCapacityRaw = newDevice.DeviceCapacityRaw,
                            DeviceModel = newDevice.DeviceModel,
                            DeviceModelRaw = newDevice.DeviceModelRaw,
                            DeviceMake = newDevice.DeviceMake,
                            UserCountryIso = user.CountryIso,
                            VoucherCode = requestMetaData.Code,
                        };

                        // Get the new device level
                        String newDeviceLevel = null;
                        var deviceLevelResult = await repo.GetDeviceLevelAsync(deviceLevelRequest);
                        newDeviceLevel = deviceLevelResult.Item2;

                        if (newDeviceLevel == null)
                        {
                            throw new Exception("Device does not have a level");
                        }

                        // Get pricing model the new device should be on based on the old device cover's voucher
                        PricingModel newDevicePricingModel = await repo.GetPricingModelByDevicePartnerFamilyAsync(
                                                                            newDeviceLevel,
                                                                            oldDevicePricingModel.TeirId.Value,
                                                                            oldDevicePricingModel.FamilyId.Value);


                        //20/04/2017 - pricing update change
                        //get payment details from model passed in by the request (if there is one).
                        if (requestMetaData.PaymentDetails != null && requestMetaData.PaymentDetails.PricingModelId != 0)
                        {
                            newDevicePricingModel = await repo.GetPricingModelsById(requestMetaData.PaymentDetails.PricingModelId);
                        }

                        if (newDevicePricingModel == null)
                        {
                            throw new Exception("New device does not have a pricing model for the voucher supplied");
                        }

                        //get the number of days remaining on the cover on the old device
                        TimeSpan tsDaysRemaining = oldActiveDeviceCover.EndDate.Subtract(DateTime.UtcNow);

                        // If old device does not have any cover
                        if (tsDaysRemaining.TotalDays <= 0)
                        {
                            throw new Exception("Cannot coverage if cover has expired");
                        }

                        // Use the oldDevicePricingModel and newDevicePricingModel to pro rata the current cover
                        Decimal mOldDailyRate = Convert.ToDecimal(oldDevicePricingModel.DailyPrice.Value);
                        Decimal mNewDailyRate = Convert.ToDecimal(newDevicePricingModel.DailyPrice.Value);

                        // Calculate new days based on existing daily rate
                        Decimal dblNewDays = 0;
                        //handle division by 0 error
                        if (mNewDailyRate > 0.00m)
                        {
                            dblNewDays = mOldDailyRate * Convert.ToDecimal(tsDaysRemaining.TotalDays) / mNewDailyRate;
                        }
                        else
                        {
                            //MMC - If a zero value voucher is used all existing coverage is removed
                            //if (tsDaysRemaining.TotalDays <= 0)
                            //{
                            dblNewDays = Convert.ToDecimal(metaDataVoucher.Voucher.membershiplength);
                            //}
                        }

                        Double dblNewTotalDays = Math.Ceiling(Convert.ToDouble(dblNewDays));


                        if (previousDevice.DeviceCover.Any())
                        {
                            //the largest id is the last, as they may not be in correct order
                            Int32 iMaxDeviceCover = previousDevice.DeviceCover.Max(dc => dc.Id);
                            //void all previous covers
                            foreach (var cover in previousDevice.DeviceCover)
                            {
                                cover.Void = true;
                                //Only update the end date of the very last cover
                                if (cover.Id == iMaxDeviceCover)
                                {
                                    cover.EndDate = DateTime.UtcNow;
                                }
                                cover.ModifiedDate = DateTime.UtcNow;
                                //await domainRepo.UpdateDeviceCoverAsync(cover);
                            }
                        }

                        if (userDevice.DeviceCover.Any())
                        {
                            //the largest id is the last, as they may not be in correct order
                            Int32 iMaxDeviceCover = userDevice.DeviceCover.Max(dc => dc.Id);
                            //void all previous covers
                            foreach (var cover in userDevice.DeviceCover)
                            {
                                cover.Void = true;
                                //Only update the end date of the very last cover
                                if (cover.Id == iMaxDeviceCover)
                                {
                                    cover.EndDate = DateTime.UtcNow;
                                }
                                cover.ModifiedDate = DateTime.UtcNow;
                                //await domainRepo.UpdateDeviceCoverAsync(cover);
                            }
                        }

                        // Insert new device cover
                        DeviceCover newDeviceCover = new DeviceCover();
                        newDeviceCover.ActivatedDate = DateTime.UtcNow;
                        newDeviceCover.AddedDate = DateTime.UtcNow;
                        newDeviceCover.DeviceId = iDeviceID;

                        //set the end date to be today plus the new days
                        newDeviceCover.EndDate = DateTime.UtcNow.AddDays(dblNewTotalDays);

                        newDeviceCover.MembershipLength = dblNewTotalDays.ToString();
                        newDeviceCover.MembershipTier = newDevicePricingModel.Tier.Name;

                        //Don't set modified date as it is new, not modified
                        newDeviceCover.Voucher = voucher.vouchercode;


                        // Code Added - 6 December 2016 - Mark McCann
                        //
                        // Need to add a transaction item to the newly transfered deviceCover so that the next voucher does not trigger FF again
                        //
                        // Begin
                        //

                        //await Logger.LogTransaction(voucher.vouchercode, (voucher.vouchercode == null) ? (int?)null : voucher.Id, requestMetaData,
                        //                            Enumerations.GetEnumDescription(Enumerations.TransactionType.ActivationRequest), "",
                        //                            repo, requestMetaData.TransactionGuid);



                        //get the previous activation for this voucher and add a row for this device
                        //IEnumerable<FortressDomain.Models.Db.Transaction> updatedTrans = null;

                        //FortressDomain.Models.Db.Transaction t1 = new FortressDomain.Models.Db.Transaction();

                        //t1.Date = DateTime.UtcNow;
                        //t1.MethodName = "VoucherActivation";
                        //t1.UserId = user.Id;
                        //t1.DeviceId = newDevice.Id;
                        //t1.Success = true;
                        //t1.Type = "Response";
                        //t1.Guid = Guid.NewGuid();

                        //domainRepo.AddTransaction(t1);


                        //String jsonData, IRepository repo, int? userId, int? deviceId,
                        //String email, String appVersion, Boolean success, Enumerations.Method methodName, 
                        //Enumerations.TransactionType type, Guid requestGuid)
                        // End
                        //

                        //await domainRepo.CreateDeviceCoverAsync(newDeviceCover);

                        Enumerations.CodeResponseMessage codeResponseMessageResult =
                               GetCodeResponseMessageResult(0, Convert.ToInt32(voucher.membershiplength),
                                                            dblNewTotalDays, transactionType);

                        return new CoverValidationCodeResponse()
                        {
                            CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                            //return the updated length, not the original voucher length
                            MembershipLength = newDeviceCover.MembershipLength,
                            MembershipTier = newDevicePricingModel.Tier.Name,
                            CodeResponseMessage = Enumerations.GetEnumDescription(codeResponseMessageResult)
                        };
                    }
                    else
                    {
                        // No old device scenario
                        // No existing Cover
                        // Get the new voucher pricing model
                        var metaData = voucher.VoucherMetadatas.FirstOrDefault();
                        PricingModel targetPricingModel = null;
                        if (metaData != null)
                        {
                            targetPricingModel = metaData.PricingModel;
                        }

                        //20/04/2017 - pricing update change
                        //get payment details from model passed in by the request (if there is one).
                        if (requestMetaData.PaymentDetails != null && requestMetaData.PaymentDetails.PricingModelId != 0)
                        {
                            targetPricingModel = await repo.GetPricingModelsById(requestMetaData.PaymentDetails.PricingModelId);
                        }


                        PricingModel pricingModelForThisDevice = await repo.GetPricingModelByDevicePartnerFamilyAsync(
                                                                                requestMetaData.DeviceDetails.DeviceLevel,
                                                                                metaData.PricingModel.TeirId.Value,
                                                                                metaData.PricingModel.FamilyId.Value);

                        PricingModel originalPricingModel = pricingModelForThisDevice;

                        if (originalPricingModel != null && targetPricingModel != null)
                        {
                            if (!originalPricingModel.DailyPrice.HasValue || !targetPricingModel.DailyPrice.HasValue)
                            {
                                throw new Exception("Daily price(s) must have a value");
                            }


                            //Calculate the incoming value and divide by the day rate that should be charged

                            //This will be a monetary value.

                            Decimal mComingInValue = Convert.ToDecimal(metaDataVoucher.PricingModel.DailyPrice.Value) *
                                                     Convert.ToDecimal(voucher.membershiplength.Value);


                            //now divide the monetary value of the incoming voucher by the daily rate of the correct pricing model
                            Decimal? mDays = 0;
                            //handle division by zero
                            if (pricingModelForThisDevice.DailyPrice.Value > 0.00m)
                            {
                                mDays = Math.Ceiling(mComingInValue / Convert.ToDecimal(pricingModelForThisDevice.DailyPrice.Value));
                            }
                            else
                            {
                                mDays = metaDataVoucher.Voucher.membershiplength;
                            }


                            ////calculate new value of cover from new daily price and voucher length
                            //var totalVoucherCoverValue = Convert.ToDecimal(originalPricingModel.DailyPrice) * voucher.membershiplength;

                            //Decimal? mDays = (pricingModelForThisDevice.DailyPrice.Value * voucher.membershiplength.Value) /
                            //                        pricingModelForThisDevice.DailyPrice.Value;


                            Int32 iDays = Convert.ToInt32(Math.Ceiling(mDays.Value));

                            // Get code response message based on voucher result
                            Enumerations.CodeResponseMessage codeResponseMessageResult =
                                GetCodeResponseMessageResult(0, Convert.ToInt32(voucher.membershiplength),
                                                             iDays, transactionType);

                            return new CoverValidationCodeResponse()
                            {
                                CodeResponseStatus = Enumerations.GetEnumDescription(transactionType),
                                //return the updated length, not the original voucher length
                                MembershipLength = iDays.ToString(),
                                MembershipTier = targetPricingModel.Tier.Name,
                                //CodeResponseMessage = Enumerations.GetEnumDescription(Enumerations.CodeResponseMessage.Success),
                                CodeResponseMessage = Enumerations.GetEnumDescription(codeResponseMessageResult),
                            };

                        }
                        else
                        {
                            throw new Exception("Both pricing models cannot be null");
                        }
                    }
                }
            }
        }
        private static Enumerations.CodeResponseMessage GetCodeResponseMessageResult(int totalDaysRemaining,
                                                             int voucherLength, double totalDays,
                                                             Enumerations.TransactionType requestingCodeResponseMessage)
        {
            // Calculate the addition of the days remaining with the additional days to add from the voucher
            double normalAdditionResult = Math.Ceiling(Convert.ToDouble(totalDaysRemaining + voucherLength));

            // If the normal result of adding a voucher to days matches the total days from pro rata,
            // then return success message, else return prorata message
            return (normalAdditionResult == totalDays) ? Enumerations.CodeResponseMessage.Success :
                                                         Enumerations.CodeResponseMessage.ProRata;
        }



        private static async Task<CodeResponse> GetResponseObject(IRepository repo,
                                                                  FortressDomain.Repository.IRepository domainRepo,
                                                                  Voucher code,
                                                                  Enumerations.TransactionType transactionType,
                                                                  RequestMetaData requestMetaData)
        {
            //Check that devices level matches the vouchers level

            var tier = (code.Tier != null) ? code.Tier.Name : "";
            if (String.IsNullOrEmpty(tier))
            {
                var voucherMetadata = code.VoucherMetadatas.FirstOrDefault();
                if (voucherMetadata != null)
                {
                    var pricingModel = voucherMetadata.PricingModel;
                    if (pricingModel != null)
                    {
                        if (pricingModel.Tier != null)
                        {
                            tier = pricingModel.Tier.Name;
                        }
                    }
                }
            }

            return await ApiHelper.ValidateChangeCoverProRataAsync(code, requestMetaData, repo, domainRepo, transactionType);
        }

        #endregion

        #region Stripe Charges
        /// <summary>
        /// Method to create a charge with the 3rd party Stripe reference
        /// https://github.com/jaymedavis/stripe.net - (3rd party dll)
        /// https://stripe.com/docs/api (API reference)
        /// </summary>
        /// <param name="requestMetaData">The request meta data.</param>
        /// <param name="repo">The codes repo.</param>
        /// <param name="domainRepo">The domain repo.</param>
        /// <returns>String charge id on success, empty string on failure</returns>
        public static async Task<String> StripeChargeAsync(RequestMetaData requestMetaData,
                                                           FortressCodesApi.Repsoitory.IRepository repo,
                                                           FortressDomain.Repository.IRepository domainRepo)
        {
            //the id of the returned stripe charge (initially empty)
            var chargeId = String.Empty;

            //get the API key from the web.config
            //secret key is used by the API, and the publishable key by the device app to generate credit card tokens
            var apiKey = System.Configuration.ConfigurationManager.AppSettings.Get("Stripe_SecretKey");

            if (!String.IsNullOrEmpty(apiKey))
            {
                //set the configuration
                Stripe.StripeConfiguration.SetApiKey(apiKey);

                if (requestMetaData.PaymentDetails != null)
                {
                    var pricingModel = await repo.GetPricingModelsById(requestMetaData.PaymentDetails.PricingModelId);
                    var paymentTransaction = await domainRepo.GetPaymentTransactionByIdAsync(requestMetaData.PaymentDetails.PaymentId);

                    if (pricingModel != null && paymentTransaction != null)
                    {
                        //call method that interacts with the stripe API
                        tbl_Payment payment = await domainRepo.GetPaymentByIdAsync(paymentTransaction.PaymentId);

                        //Payment failures will be handled through web-hooks to listen for event notifications
                        //https://stripe.com/docs/declines

                        //there should be an already created payment to be able to get the amount from
                        if (payment != null)
                        {
                            //set up customers and charges
                            var customers = new StripeCustomerService();
                            var charges = new StripeChargeService();

                            //set the charge metadata
                            Dictionary<String, String> chargeMetaData = new Dictionary<String, String>();

                            //add in our generated transaction id to the charge request, so that it is available in the web-hooks
                            chargeMetaData.Add("PaymentId", requestMetaData.PaymentDetails.PaymentId.ToString());

                            //create customer and set the previously generated token from the mobile device
                            var customer = customers.Create(new StripeCustomerCreateOptions
                            {
                                Email = requestMetaData.UserDetails.Email,
                                SourceToken = requestMetaData.PaymentDetails.Token

                            });

                            //request options to be supplied to the create charge method call
                            var requestOptions = new StripeRequestOptions();

                            //create idempotent key from concatenation of the device id and the transaction id
                            //https://stripe.com/docs/api#idempotent_requests
                            requestOptions.IdempotencyKey = String.Format("{0}{1}", requestMetaData.DeviceDetails.DeviceID,
                                                                                    requestMetaData.PaymentDetails.PaymentId);

                            //need to convert to smallest unit of currency for Stripe so multiply by 100
                            //https://support.stripe.com/questions/which-zero-decimal-currencies-does-stripe-support

                            Int32 chargeAmount;

                            if (IsStripeZeroDecimalCurrency(pricingModel.Country.Currency.Code))
                            {
                                //if is a zero decimal currency use the actual value as that is the smallest unit
                                chargeAmount = (Int32)payment.TransactionValue;
                            }
                            else
                            {
                                //otherwise multiply by 100 to get to the smallest unit
                                chargeAmount = (Int32)(payment.TransactionValue * 100);
                            }

                            var chargeRequestOptions = (new StripeChargeCreateOptions
                            {
                                Amount = chargeAmount,
                                Currency = pricingModel.Country.Currency.Code,
                                Description = "Fortress Billing Charge",
                                CustomerId = customer.Id,
                                ApplicationFee = 0,
                                Capture = true,
                                Metadata = chargeMetaData
                            });

                            //charge the customer
                            var charge = await charges.CreateAsync(chargeRequestOptions, requestOptions);
                            chargeId = charge.Id;
                        }
                        else
                        {
                            throw new Exception("There is no payment object to charge");
                        }
                    }
                    else
                    {
                        throw new Exception("Incomplete Stripe request data");
                    }
                }
                else
                {
                    throw new Exception("No payment object specified");
                }
            }
            else
            {
                throw new Exception("No Stripe API Key found");
            }
            return chargeId;
        }

        /// <summary>
        /// This is the list of zero code currencies stripes supports, if this ever changes we will need to amend
        /// the dictionary values
        /// </summary>
        private static Boolean IsStripeZeroDecimalCurrency(String currencyCode)
        {
            //https://support.stripe.com/questions/which-zero-decimal-currencies-does-stripe-support
            Dictionary<String, String> stripeZeroDecimalCurrencies = new Dictionary<String, String>();
            stripeZeroDecimalCurrencies.Add("BIF", "Burundian Franc");
            stripeZeroDecimalCurrencies.Add("CLP", "Chilean Peso");
            stripeZeroDecimalCurrencies.Add("DJF", "Djiboutian Franc");
            stripeZeroDecimalCurrencies.Add("GNF", "Guinean Franc");
            stripeZeroDecimalCurrencies.Add("JPY", "Japanese Yen");
            stripeZeroDecimalCurrencies.Add("KMF", "Comorian Franc");
            stripeZeroDecimalCurrencies.Add("KRW", "South Korean Won");
            stripeZeroDecimalCurrencies.Add("MGA", "Malagasy Ariary");
            stripeZeroDecimalCurrencies.Add("PYG", "Paraguayan Guaraní");
            stripeZeroDecimalCurrencies.Add("RWF", "Rwandan Franc");
            stripeZeroDecimalCurrencies.Add("VND", "Vietnamese Đồng");
            stripeZeroDecimalCurrencies.Add("VUV", "Vanuatu Vatu");
            stripeZeroDecimalCurrencies.Add("XAF", "Central African Cfa Franc");
            stripeZeroDecimalCurrencies.Add("XOF", "West African Cfa Franc");
            stripeZeroDecimalCurrencies.Add("XPF", "Cfp Franc");

            return stripeZeroDecimalCurrencies.ContainsKey(currencyCode);
        }
        #endregion


        public static Enumerations.TransactionType GetTranactionTypeByMethodName(Enumerations.MethodName methodName)
        {
            switch (Enumerations.GetEnumDescription(methodName))
            {
                case "Activate":
                    return Enumerations.TransactionType.Activated;

                case "Validate":
                    return Enumerations.TransactionType.Validated;

                case "ChangeCoverage":
                    return Enumerations.TransactionType.ChangeCoverage;

                case "PaymentGatewayCoverage":
                    return Enumerations.TransactionType.PaymentGatewayCoverage;

                case "ProcessCharge":
                    return Enumerations.TransactionType.ProcessCharge;
                case "ProcessChargeError":
                    return Enumerations.TransactionType.ProcessChargeError;
                case "ProcessResponseCharge":
                    return Enumerations.TransactionType.ProcessChargeResponse;

                default:
                    return Enumerations.TransactionType.Error;

            }
        }
    }
}
