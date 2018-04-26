using FortressCodesApi.Repsoitory;
using FortressCodesDomain.DbModels;
using FortressDomain.Models.RequestObjects;
using System;
using System.Threading.Tasks;

namespace FortressCodesApi
{
    public class Logger
    {
        private static IRepository _repo;

        public static async Task LogTransaction(string suppliedCode, int? codeId, RequestMetaData requestMetaData,
                                                string transactionType, string exceptionMessage, IRepository repo, Guid requestGuid)
        {
            _repo = repo;
            requestMetaData.Code = suppliedCode;
            await _repo.AddTransactionAsync(exceptionMessage.Length == 0
                ? await CreateRequestTransaction(requestMetaData, transactionType, codeId, requestGuid)
                : await CreateRequestTransaction(requestMetaData, transactionType, codeId, exceptionMessage, requestGuid));
        }

        private static async Task<Transaction> CreateRequestTransaction(RequestMetaData requestMetaData, string transactionType,
                                                                        int? codeId, Guid requestGuid)
        {
            return await BindRequestMetaDataToTransaction(requestMetaData, transactionType, codeId, requestGuid);
        }
        private static async Task<Transaction> CreateRequestTransaction(RequestMetaData requestMetaData, string transactionType,
                                                                        int? codeId, string exceptionMessage, Guid requestGuid)
        {
            return await BindRequestMetaDataToTransaction(requestMetaData, transactionType, codeId, exceptionMessage, requestGuid);
        }

        private static async Task<Transaction> BindRequestMetaDataToTransaction(RequestMetaData requestMetaData,
                                                                                string transactionType, int? codeId, Guid requestGuid)
        {
            try
            {
                var tempTransaction = new Transaction
                {
                    CodeId = codeId,
                    RequestCode = requestMetaData.Code,
                    Date = DateTime.Now,
                    DeviceIMEI = requestMetaData.DeviceDetails.ImeiNumber,
                    DeviceOS = requestMetaData.DeviceDetails.OperatingSystem,
                    DevicePhoneNumber = requestMetaData.DeviceDetails.PhoneNumber,
                    DeviceSerialNumber = requestMetaData.DeviceDetails.SerialNumber,
                    DeviceVendorId = requestMetaData.DeviceDetails.VendorId,
                    FortressVersion = requestMetaData.AppDetails.Version,
                    UserAccountNumber = requestMetaData.UserDetails.AccountNumber,
                    UserEmail = requestMetaData.UserDetails.Email,
                    TransactionGuid = requestGuid,
                    TransactionTypeId =
                        await GetTransactionTypeId(transactionType)
                };
                return tempTransaction;
            }
            catch (Exception ex)
            {
                throw new Exception("Error binding RequestMetaData to Transaction", ex);
            }
        }

        private static async Task<Transaction> BindRequestMetaDataToTransaction(RequestMetaData requestMetaData, string transactionType,
                                                                                int? codeId, string exceptionMessage, Guid requestGuid)
        {
            try
            {
                var tempTransaction = new Transaction
                {
                    CodeId = codeId,
                    RequestCode = requestMetaData.Code,
                    Date = DateTime.Now,
                    DeviceIMEI = requestMetaData.DeviceDetails.ImeiNumber,
                    DeviceOS = requestMetaData.DeviceDetails.OperatingSystem,
                    DevicePhoneNumber = requestMetaData.DeviceDetails.PhoneNumber,
                    DeviceSerialNumber = requestMetaData.DeviceDetails.SerialNumber,
                    DeviceVendorId = requestMetaData.DeviceDetails.VendorId,
                    FortressVersion = requestMetaData.AppDetails.Version,
                    UserAccountNumber = requestMetaData.UserDetails.AccountNumber,
                    UserEmail = requestMetaData.UserDetails.Email,
                    TransactionGuid = requestGuid,
                    TransactionTypeId =
                        await GetTransactionTypeId(transactionType),
                    ExceptionMessage = exceptionMessage
                };
                return tempTransaction;
            }
            catch (Exception ex)
            {
                throw new Exception("Error binding RequestMetaData to Transaction", ex);
            }
        }

        private static async Task<int> GetTransactionTypeId(string transactionType)
        {
            try
            {
                TransactionType tempTransaction = await _repo.GetTransactionTypeIdAsync(transactionType);
                return tempTransaction.Id;
            }
            catch (Exception ex)
            {
                throw new Exception("Invalid transaction type supplied", ex);
            }
        }
    }
}