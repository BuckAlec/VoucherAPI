using FortressCodesDomain.DbModels;
using FortressDomain.Models.RequestObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace FortressCodesApi.Repsoitory
{
    public interface IRepository
    {
        Task<Voucher> GetCodeAsync(string code);
        Task<int> AddTransactionAsync(Transaction entity);
        Task<List<TransactionType>> GetAllTransactionTypesAsync();
        Task<TransactionType> GetTransactionTypeIdAsync(string transactionType);
        Task<int> UpdateVoucherAsync(Voucher entity);
        Task<int> GetCodeAttemptsInTimeLimitAsync(int code, int timeLimit, int validatedTransactionTypeId);
        Task<int> GetCodeUsageCountAsync(int codeId, int activatedTransactionTypeId);
        Task<DeviceLevel> GetDeviceLevelByDeviceDetailsAsync(String deviceMake, String deviceModel, string deviceCapacity, String userDeviceCountryIso, PricingModel pricingModel);
        Task<PricingModel> GetPricingModelByVoucherCodeAsync(String voucherCode);
        Task<PricingModel> GetPricingModelByDeviceIdAsync(Int32 deviceId);
        Task<PricingModel> GetPricingModelByDevicePartnerFamilyAsync(string deviceLevel, Int32 tierId, Int32 familyId);
        bool IsPostAuthorized(string secret, int UserID);

        Task<Tuple<Boolean, Int32, Int32>> GetDeviceLevelIDAsync(DeviceLevelRequest deviceLevelRequest);
        Task<Tuple<Boolean, String>> GetDeviceLevelAsync(DeviceLevelRequest deviceLevelRequest);
        Task<Tuple<Boolean, String>> GetDeviceLevelAsyncMem(DeviceLevelRequestMem deviceLevelRequest);
        Task<DeviceLevel> GetDeviceLevelByIDAsync(Int32 DeviceLevelID);

        //Task<PricingModel> GetPricingModelByFamilyDeviceLevelAsync(Int32? familyID, String deviceLevel);
        Task<Boolean> AddAsync<T>(T entity) where T : class;
        Task<Boolean> DeleteAsync<T>(T entity) where T : class;
        Task<Boolean> UpdateAsync<T>(T entity) where T : class;
        IQueryable<T> GetAll<T>() where T : class;
        T GetSingleOrDefault<T>(Expression<Func<T, Boolean>> predicate) where T : class;
        IQueryable<T> FindBy<T>(Expression<Func<T, Boolean>> predicate) where T : class;
        Task<PricingModel> GetPricingModelByVoucherAndDeviceLevel(Voucher voucher, string deviceLevel);
        Task<IEnumerable<PricingModel>> GetPricingModelsByFamily(Int32 familyId);
        Task<PricingModel> GetPricingModelsById(int id);
        Task<Device> GetDeviceByFormattedDeviceNameAsync(String formattedDeviceName);
        Task<Device> GetDeviceByDeviceFieldsAsync(String deviceMake, String deviceModel, String deviceCapacity);
        Task<Device> GetDeviceByIDAsync(Int32 DeviceID);
    }
}
